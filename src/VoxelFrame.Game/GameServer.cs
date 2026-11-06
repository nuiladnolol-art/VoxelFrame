using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;

namespace VoxelFrame.Game;

public sealed class ConnectedClient {
    public int Id { get; }
    public TcpClient Socket { get; }
    public string Name { get; set; } = "Player";
    public Vector3 Position { get; set; }
    public Vector3 TargetPosition { get; set; }
    public float Yaw { get; set; }
    public float TargetYaw { get; set; }
    public float Pitch { get; set; }
    public float TargetPitch { get; set; }
    public bool IsMoving { get; set; }
    public bool IsSneaking { get; set; }
    public bool IsFlying { get; set; }
    public bool IsBlocking { get; set; }
    public float Health { get; set; } = 20f;
    public int SelectedItemId { get; set; }
    public double LastExplosionTime { get; set; }
    public int OffhandItemId => PlayerData.OffhandEntry?.Item.Definition.Id ?? 0;
    public int HelmetId => PlayerData.Armor[0]?.Item.Definition.Id ?? 0;
    public int ChestplateId => PlayerData.Armor[1]?.Item.Definition.Id ?? 0;
    public int LeggingsId => PlayerData.Armor[2]?.Item.Definition.Id ?? 0;
    public int BootsId => PlayerData.Armor[3]?.Item.Definition.Id ?? 0;
    public float ArmSwingTimer { get; set; }
    public float HurtTimer { get; set; }
    public string SkinName { get; set; } = "cyan";
    public Dimension Dimension { get; set; } = Dimension.Overworld;
    public Player PlayerData { get; set; } = new();
    public DateTime LastMovementTime { get; set; } = DateTime.UtcNow;

    // Асинхронная очередь отправки: главный цикл и хендлеры не блокируются на медленных сокетах.
    private readonly ConcurrentQueue<byte[]> _sendQueue = new();
    // Count у ConcurrentQueue — O(n); держим счётчик сами.
    private long _queuedPackets;
    private readonly SemaphoreSlim _sendSignal = new(0, int.MaxValue);
    private readonly CancellationTokenSource _sendCts = new();
    private readonly Task _writerTask;
    private volatile bool _connected = true;
    private const int MaxQueuedPackets = 512;

    public ConnectedClient(int id, TcpClient socket) {
        Id = id;
        Socket = socket;
        _writerTask = Task.Run(SendWriterLoop);
    }

    public void Send(byte[] data) {
        if (!_connected || data == null || data.Length < 5) return;
        try {
            // Клиент сильно отстал. Раньше здесь без разбора удалялись самые старые пакеты —
            // потеря WorldChunksSync/BlockChange оставляла у клиента дыры в мире.
            if (Interlocked.Read(ref _queuedPackets) >= MaxQueuedPackets) {
                if (IsReplaceableSnapshot(data[4])) return; // придёт более свежий снапшот
                // Критичный пакет выбросить нельзя: рвём соединение, клиент переподключится
                // и получит мир заново (лучше, чем молча битый мир).
                _connected = false;
                try { Socket.Close(); } catch { }
                return;
            }
            _sendQueue.Enqueue(data);
            Interlocked.Increment(ref _queuedPackets);
            _sendSignal.Release();
        } catch (ObjectDisposedException) {
            _connected = false;
        }
    }

    /// <summary>Пакеты-снапшоты: их безопасно проредить, актуальное состояние придёт следующим кадром.</summary>
    private static bool IsReplaceableSnapshot(byte packetType) =>
        packetType == (byte)PacketType.MobSync ||
        packetType == (byte)PacketType.PickupSync ||
        packetType == (byte)PacketType.ProjectileSync ||
        packetType == (byte)PacketType.PlayerMovement ||
        packetType == (byte)PacketType.TimeWeatherSync;

    private async Task SendWriterLoop() {
        var token = _sendCts.Token;
        while (!token.IsCancellationRequested) {
            try {
                await _sendSignal.WaitAsync(token);
            } catch (OperationCanceledException) {
                break;
            }
            // Разбудили один раз — забираем сигналы за все уже отправленные пакеты,
            // иначе счётчик семафора копился и writer крутил пустые итерации.
            while (_sendQueue.TryDequeue(out var data)) {
                Interlocked.Decrement(ref _queuedPackets);
                _sendSignal.Wait(0);
                try {
                    if (!_connected || !Socket.Connected) return;
                    var stream = Socket.GetStream();
                    await stream.WriteAsync(data, token);
                } catch (OperationCanceledException) {
                    return;
                } catch (Exception ex) {
                    Console.WriteLine($"[GameServer Client {Id}] Ошибка отправки пакета: {ex.Message}");
                    _connected = false;
                    return;
                }
            }
            // Остаток «лишних» сигналов (если пакеты уже разобраны) обнуляем.
            while (_sendSignal.Wait(0)) { }
        }
    }

    public void Close() {
        _connected = false;
        try { _sendCts.Cancel(); } catch { }
        try { Socket.Close(); } catch { }
        try { _writerTask.Wait(50); } catch { }
    }

    public void Update(float dt) {
        Position = Vector3.Lerp(Position, TargetPosition, MathF.Min(1f, dt * 18f));
        Yaw = MathF.BitIncrement(Yaw) != MathF.BitIncrement(TargetYaw) ? Yaw + (TargetYaw - Yaw) * MathF.Min(1f, dt * 18f) : TargetYaw;
        Pitch = Pitch + (TargetPitch - Pitch) * MathF.Min(1f, dt * 18f);

        if (ArmSwingTimer > 0f) ArmSwingTimer = MathF.Max(0f, ArmSwingTimer - dt * 3.5f);
        if (HurtTimer > 0f) HurtTimer = MathF.Max(0f, HurtTimer - dt * 2.5f);
    }
}

public sealed class GameServer : IDisposable {
    public static GameServer? Active { get; private set; }

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _nextClientId = 10;
    private readonly ConcurrentDictionary<int, ConnectedClient> _clients = new();

    /// <summary>Предел одновременных подключений.</summary>
    private const int MaxClients = 16;
    private int _activeClientConnections;
    private readonly List<Animal> _nearAnimalsBuffer = new();
    private readonly List<HostileMob> _nearHostilesBuffer = new();
    private readonly List<ItemPickup> _nearPickupsBuffer = new();
    private readonly List<ArrowProjectile> _nearArrowsBuffer = new();
    private readonly GameSession _session;
    private float _timeSyncTimer = 0f;
    private float _entitySyncTimer = 0f;

    public int Port { get; }
    public bool IsRunning => _listener != null;
    public int ClientCount => _clients.Count;
    public IReadOnlyCollection<ConnectedClient> Clients => _clients.Values.ToArray();

    public GameServer(GameSession session, int port = NetworkProtocol.DefaultPort) {
        _session = session;
        Port = port;
    }

    public static GameServer Start(GameSession session, int port = NetworkProtocol.DefaultPort) {
        Active?.Dispose();
        var srv = new GameServer(session, port);
        srv.StartListening();
        Active = srv;
        GameWorld.OnNetworkBlockChangeBroadcast = (pos, typeId, mask, isBreak, dim) => {
            Active?.BroadcastBlockChange(pos.X, pos.Y, pos.Z, typeId, mask, isBreak, (byte)dim);
        };
        GameWorld.OnFurnaceChangedBroadcast = (pos, furnace, dim) => {
            Active?.BroadcastFurnaceSync(pos, furnace, (byte)dim);
        };
        GameWorld.NetworkHookOwner = srv;
        return srv;
    }

    public static void Stop() {
        var owner = Active;
        // Снимаем хуки, только если их поставил сервер: клиент в том же процессе
        // иначе оставался без трансляции изменений блоков.
        if (owner == null || ReferenceEquals(GameWorld.NetworkHookOwner, owner)) {
            GameWorld.OnNetworkBlockChangeBroadcast = null;
            GameWorld.OnFurnaceChangedBroadcast = null;
            GameWorld.NetworkHookOwner = null;
        }
        Active?.Dispose();
        Active = null;
    }

    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

    public void EnqueueMainThreadAction(Action action) {
        _mainThreadActions.Enqueue(action);
    }

    public void ProcessMainThreadActions() {
        while (_mainThreadActions.TryDequeue(out var action)) {
            try {
                action();
            } catch (Exception ex) {
                Console.WriteLine($"[GameServer Action Error] {ex.Message}");
            }
        }
    }

    public void Update(float dt) {
        ProcessMainThreadActions();

        foreach (var c in _clients.Values) {
            c.Update(dt);
        }

        // Автоматический сбор пикапов и тик пикапов во всех активных измерениях
        Span<Dimension> allDims = stackalloc Dimension[] { Dimension.Overworld, Dimension.Nether, Dimension.End, Dimension.Void };
        foreach (var dim in allDims) {
            var world = _session.GetWorld(dim);
            if (world == null) continue;
            if (world != _session.World) {
                world.TickPickups(dt, null);
            }
            for (int i = world.Pickups.Count - 1; i >= 0; i--) {
                var p = world.Pickups[i];
                if (p.Quantity <= 0) continue;
                foreach (var cl in _clients.Values) {
                    if (cl.Dimension != world.Dimension) continue;
                    float d = Vector3.Distance(cl.Position, p.Position);
                    if (p.PickupDelay <= 0f && d < 1.5f) {
                        // Предмет исчезает только если он реально влез в инвентарь:
                        // иначе при полном инвентаре дроп пропадал бесследно.
                        if (!cl.PlayerData.Inventory.TryInsert(p.Item, p.Quantity)) continue;
                        var collectPacket = NetworkProtocol.WritePickupCollect(p.Id, cl.Id);
                        Broadcast(collectPacket);
                        p.Quantity = 0;
                        break;
                    }
                }
            }
        }

        _timeSyncTimer += dt;
        if (_timeSyncTimer >= 3.0f && ClientCount > 0) {
            _timeSyncTimer = 0f;
            var timePacket = NetworkProtocol.WriteTimeWeatherSync(_session.DayNight.TimeOfDay, (int)_session.Weather);
            Broadcast(timePacket);
        }

        _entitySyncTimer += dt;
        if (_entitySyncTimer >= 0.15f && ClientCount > 0) {
            _entitySyncTimer = 0f;
            foreach (var client in _clients.Values) {
                var cWorld = _session.GetWorld(client.Dimension);
                if (cWorld == null) continue;

                _nearAnimalsBuffer.Clear();
                for (int i = 0; i < cWorld.Animals.Count; i++) {
                    var a = cWorld.Animals[i];
                    if (Vector3.DistanceSquared(a.Position, client.Position) < 128f * 128f) _nearAnimalsBuffer.Add(a);
                }

                _nearHostilesBuffer.Clear();
                for (int i = 0; i < cWorld.HostileMobs.Count; i++) {
                    var h = cWorld.HostileMobs[i];
                    if (Vector3.DistanceSquared(h.Position, client.Position) < 128f * 128f) _nearHostilesBuffer.Add(h);
                }

                var mobPacket = NetworkProtocol.WriteMobSync((byte)cWorld.Dimension, _nearAnimalsBuffer, _nearHostilesBuffer);
                client.Send(mobPacket);

                _nearPickupsBuffer.Clear();
                for (int i = 0; i < cWorld.Pickups.Count; i++) {
                    var p = cWorld.Pickups[i];
                    if (Vector3.DistanceSquared(p.Position, client.Position) < 128f * 128f) _nearPickupsBuffer.Add(p);
                }
                if (_nearPickupsBuffer.Count > 0) {
                    var pickupPacket = NetworkProtocol.WritePickupSync((byte)cWorld.Dimension, _nearPickupsBuffer);
                    client.Send(pickupPacket);
                }

                _nearArrowsBuffer.Clear();
                for (int i = 0; i < cWorld.Arrows.Count; i++) {
                    var arr = cWorld.Arrows[i];
                    if (Vector3.DistanceSquared(arr.Position, client.Position) < 128f * 128f) _nearArrowsBuffer.Add(arr);
                }
                if (_nearArrowsBuffer.Count > 0) {
                    var projPacket = NetworkProtocol.WriteProjectileSync((byte)cWorld.Dimension, _nearArrowsBuffer);
                    client.Send(projPacket);
                }

                // Боссы синхронизируются для тех, кто в соответствующем измерении
                if (cWorld.Dimension == Dimension.End) {
                    if (cWorld.EndBoss is { } eb) {
                        var bossP = NetworkProtocol.WriteBossSync(0, eb.Alive, eb.Awake, eb.Health, EndSlime.MaxHealth, eb.Phase, eb.Position, eb.Velocity, eb.HurtTime, eb.SlamWarningTimer, eb.IsResting, (byte)Dimension.End);
                        client.Send(bossP);
                    }
                } else if (cWorld.Dimension == Dimension.Void) {
                    if (cWorld.TrueVoidBoss is { } tb) {
                        var tbP = NetworkProtocol.WriteBossSync(1, tb.Alive, tb.Awake, tb.Health, TrueEndSlime.MaxHealth, tb.Phase, tb.Position, tb.Velocity, tb.HurtTime, tb.SingularityWarningTimer, tb.State == TrueBossState.Resting, (byte)Dimension.Void);
                        client.Send(tbP);
                    }
                }
            }
        }
    }

    private void StartListening() {
        try {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
        } catch (Exception ex) {
            _listener = null;
            _session.AddChatMessage($"Ошибка запуска сервера: {ex.Message}", Raylib_cs.Color.Red);
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () => {
            while (!token.IsCancellationRequested && _listener != null) {
                try {
                    var socket = await _listener.AcceptTcpClientAsync(token);
                    // Ограничение числа клиентов: без него любой может забить сервер сокетами.
                    if (Interlocked.Increment(ref _activeClientConnections) > MaxClients) {
                        Interlocked.Decrement(ref _activeClientConnections);
                        try { socket.Close(); } catch { }
                        continue;
                    }
                    _ = Task.Run(() => HandleClient(socket, token), token);
                } catch (OperationCanceledException) {
                    break;
                } catch (ObjectDisposedException) {
                    break;
                } catch (Exception ex) {
                    // Разовый сбой accept не должен навсегда выключать приём подключений.
                    Console.WriteLine($"[GameServer] Ошибка accept: {ex.Message}");
                    try { await Task.Delay(200, token); } catch { break; }
                }
            }
        }, token);

        _session.AddChatMessage($"Локальный сервер открыт на порту {Port}!", Raylib_cs.Color.Green);
    }

    /// <summary>
    /// Серверное определение оружия клиента из его собственного инвентаря (не из пакета).
    /// Предотвращает подделку itemId для расчёта урона в AttackEntity/PlayerHit.
    /// </summary>
    private static ushort ResolveClientWeaponId(ConnectedClient client) {
        // Используем itemId из последнего действия клиента, но только если такой предмет
        // реально есть в его инвентаре. Это закрывает подделку (напр. алмазный меч из ничего),
        // не ломая нормальную синхронизацию выбранного предмета.
        ushort claimed = (ushort)client.SelectedItemId;
        if (claimed == 0) return 0;
        var inv = client.PlayerData.Inventory;
        for (int i = 0; i < inv.Capacity; i++) {
            if (inv.Slots[i] is { } e && e.Quantity > 0 && e.Item.Definition.Id == claimed) return claimed;
        }
        return 0;
    }

    private void HandleClient(TcpClient socket, CancellationToken token) {
        socket.NoDelay = true;
        socket.ReceiveTimeout = 30000;
        int clientId = Interlocked.Increment(ref _nextClientId);
        var client = new ConnectedClient(clientId, socket);
        _clients[clientId] = client;

        NetworkStream stream = socket.GetStream();
        bool handshaked = false;

        try {
            // Welcome handshake
            var welcome = NetworkProtocol.WriteWelcome(
                clientId,
                _session.World.Generator.Seed,
                _session.DayNight.TimeOfDay,
                _session.CheatsEnabled,
                (int)_session.GameMode,
                _session.KeepInventory
            );
            client.Send(welcome);

            // Inform the new client about the host player
            var hostJoin = NetworkProtocol.WritePlayerJoin(
                1,
                _session.Player.Name,
                _session.Player.Position,
                _session.Player.Yaw,
                _session.Player.Pitch,
                (byte)_session.World.Dimension,
                _session.Player.SkinName
            );
            client.Send(hostJoin);

            // Inform about other connected clients
            foreach (var other in _clients.Values) {
                if (other.Id != clientId) {
                    var otherJoin = NetworkProtocol.WritePlayerJoin(other.Id, other.Name, other.Position, other.Yaw, other.Pitch, (byte)other.Dimension, other.SkinName);
                    client.Send(otherJoin);
                }
            }

            while (!token.IsCancellationRequested && socket.Connected) {
                var frameNullable = NetworkProtocol.ReadPacketFrame(stream);
                if (!frameNullable.HasValue) break;

                using var frame = frameNullable.Value;
                if (!handshaked) {
                    if (frame.Type != PacketType.Handshake) {
                        Console.WriteLine($"[GameServer Client {clientId}] Неавторизованный пакет до Handshake: {frame.Type}");
                        break;
                    }
                    handshaked = true;
                }

                using var pMs = new MemoryStream(frame.Buffer, 1, frame.Length - 1, writable: false);
                using var reader = new BinaryReader(pMs, Encoding.UTF8);

                switch (frame.Type) {
                    case PacketType.KeepAlive: {
                        break;
                    }
                    case PacketType.Handshake: {
                        string name = reader.ReadString();
                        string ver = reader.ReadString();
                        string skin = "cyan";
                        try { skin = reader.ReadString(); } catch { }
                        
                        // Anti-spoofing check
                        int suffix = 1;
                        string originalName = name;
                        bool nameTaken = false;
                        do {
                            nameTaken = false;
                            if (name.Equals(_session.Player.Name, StringComparison.OrdinalIgnoreCase)) {
                                nameTaken = true;
                            } else {
                                foreach (var other in _clients.Values) {
                                    if (other.Id != clientId && other.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                                        nameTaken = true;
                                        break;
                                    }
                                }
                            }
                            if (nameTaken) {
                                name = $"{originalName}_{suffix++}";
                            }
                        } while (nameTaken);

                        client.Name = name;
                        client.SkinName = skin;
                        client.PlayerData.Name = name;
                        client.PlayerData.SkinName = skin;

                        // Если у игрока есть сохраненный файл PlayerData на сервере — загружаем и синхронизируем с клиентом
                        if (SaveSystem.HasPlayerData(name)) {
                            if (SaveSystem.LoadPlayerData(name, client.PlayerData)) {
                                client.Position = client.PlayerData.Position;
                                client.TargetPosition = client.PlayerData.Position;
                                client.Yaw = client.PlayerData.Yaw;
                                client.TargetYaw = client.PlayerData.Yaw;
                                client.Pitch = client.PlayerData.Pitch;
                                client.TargetPitch = client.PlayerData.Pitch;
                                client.Health = client.PlayerData.Health;
                                client.Dimension = client.PlayerData.Dimension;

                                var pDataSync = NetworkProtocol.WritePlayerDataSync(client.PlayerData, (byte)client.Dimension);
                                client.Send(pDataSync);
                            }
                        }

                        // Синхронизируем текущее состояние мира хоста с клиентом через главный поток (предотвращает гонки потоков и зависания)
                        var initSyncTcs = new TaskCompletionSource<(List<byte[]> ChunkSync, byte[]? MobSync, byte[]? PickupSync)>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        EnqueueMainThreadAction(() => {
                            try {
                                var dim = (byte)_session.World.Dimension;
                                var modified = _session.World.GetModifiedChunksSnapshot();
                                var cSync = NetworkProtocol.WriteWorldChunksSyncBatches(dim, modified);

                                byte[]? mSync = null;
                                if (_session.World.Animals.Count > 0 || _session.World.HostileMobs.Count > 0) {
                                    mSync = NetworkProtocol.WriteMobSync(dim, _session.World.Animals, _session.World.HostileMobs);
                                }

                                byte[]? pSync = null;
                                if (_session.World.Pickups.Count > 0) {
                                    pSync = NetworkProtocol.WritePickupSync(dim, _session.World.Pickups);
                                }

                                _session.AddChatMessage($"Игрок {name} присоединился к игре!", Raylib_cs.Color.Yellow);
                                _session.AddMessage($"Игрок {name} вошел в мир");

                                initSyncTcs.TrySetResult((cSync, mSync, pSync));
                            } catch (Exception ex) {
                                // Без этого сокет-поток навсегда зависал на GetResult(),
                                // потому что ProcessMainThreadActions глотает исключения.
                                initSyncTcs.TrySetException(ex);
                            }
                        });

                        // Таймаут обязателен: если главный поток занят/остановлен, сокет-поток
                        // не должен висеть вечно на ожидании результата.
                        if (!initSyncTcs.Task.Wait(TimeSpan.FromSeconds(10))) {
                            throw new TimeoutException("Главный поток не ответил на инициализацию мира");
                        }
                        var (chunkSync, mobSync, pickupSync) = initSyncTcs.Task.Result;
                        foreach (var chunkPacket in chunkSync) {
                            client.Send(chunkPacket);
                        }
                        if (mobSync != null) client.Send(mobSync);
                        if (pickupSync != null) client.Send(pickupSync);

                        // Broadcast new player to all clients with skin
                        var joinPacket = NetworkProtocol.WritePlayerJoin(clientId, name, client.Position, client.Yaw, client.Pitch, (byte)client.Dimension, skin);
                        Broadcast(joinPacket, exceptClientId: clientId);
                        break;
                    }
                    case PacketType.Disconnect: {
                        string reason = reader.ReadString();
                        SaveSystem.SavePlayerData(client.Name, client.PlayerData);
                        return;
                    }
                    case PacketType.InventoryAction: {
                        var action = (InventoryActionType)reader.ReadByte();
                        int fromSlot = reader.ReadInt32();
                        int toSlot = reader.ReadInt32();
                        int amount = reader.ReadInt32();

                        EnqueueMainThreadAction(() => {
                            var inv = client.PlayerData.Inventory;
                            if (action == InventoryActionType.Move) {
                                // Basic swap/move logic
                                if (fromSlot >= 0 && fromSlot < inv.Capacity && toSlot >= 0 && toSlot < inv.Capacity) {
                                    var src = inv.Slots[fromSlot];
                                    var dst = inv.Slots[toSlot];
                                    if (src.HasValue) {
                                        if (!dst.HasValue) {
                                            inv.Slots[fromSlot] = null;
                                            inv.Slots[toSlot] = src;
                                        } else if (src.Value.Item.Definition.Id == dst.Value.Item.Definition.Id) {
                                            int maxStack = src.Value.Item.Definition.MaxStack;
                                            int space = maxStack - dst.Value.Quantity;
                                            int toMove = Math.Min(amount, Math.Min(src.Value.Quantity, space));
                                            if (toMove > 0) {
                                                inv.Slots[toSlot] = new ItemEntry(dst.Value.Item, dst.Value.Quantity + toMove);
                                                if (src.Value.Quantity == toMove) inv.Slots[fromSlot] = null;
                                                else inv.Slots[fromSlot] = new ItemEntry(src.Value.Item, src.Value.Quantity - toMove);
                                            }
                                        } else {
                                            // Swap
                                            inv.Slots[fromSlot] = dst;
                                            inv.Slots[toSlot] = src;
                                        }
                                    }
                                }
                            } else if (action == InventoryActionType.Drop) {
                                if (fromSlot >= 0 && fromSlot < inv.Capacity) {
                                    var src = inv.Slots[fromSlot];
                                    if (src.HasValue) {
                                        int toDrop = Math.Min(amount, src.Value.Quantity);
                                        if (toDrop > 0) {
                                            var dropItem = new ItemEntry(src.Value.Item, toDrop);
                                            if (src.Value.Quantity == toDrop) inv.Slots[fromSlot] = null;
                                            else inv.Slots[fromSlot] = new ItemEntry(src.Value.Item, src.Value.Quantity - toDrop);
                                            
                                            // Spawn pickup
                                            var targetWorld = _session.GetWorld(client.Dimension);
                                            var dropPos = client.Position + new Vector3(0, 1.5f, 0);
                                            var p = new ItemPickup(dropItem.Item, dropItem.Quantity, dropPos) {
                                                Velocity = client.TargetPosition - client.Position + new Vector3(0, 0.2f, 0)
                                            };
                                            targetWorld.Pickups.Add(p);
                                        }
                                    }
                                }
                            } else if (action == InventoryActionType.Craft) {
                                int recipeIdx = fromSlot;
                                int craftTimes = Math.Clamp(amount, 1, 64 * 36); // Сколько раз скрафтили
                                if (recipeIdx >= 0 && recipeIdx < GameData.CraftRecipes.Count) {
                                    var recipe = GameData.CraftRecipes[recipeIdx];
                                    
                                    // Проверка наличия ингредиентов и защита от переполнения
                                    bool canCraft = true;
                                    if (recipe.Count > int.MaxValue / craftTimes) canCraft = false;
                                    foreach (var (reqItem, reqCount) in recipe.Ingredients) {
                                        if (reqCount > int.MaxValue / craftTimes) {
                                            canCraft = false; break;
                                        }
                                        int have = inv.Entries.Where(e => e.Item.Definition == reqItem).Sum(e => e.Quantity);
                                        if (have < reqCount * craftTimes) {
                                            canCraft = false; break;
                                        }
                                    }
                                    
                                    if (canCraft) {
                                        // Списываем ингредиенты (упрощенно)
                                        foreach (var (reqItem, reqCount) in recipe.Ingredients) {
                                            int needed = reqCount * craftTimes;
                                            for (int i = 0; i < inv.Capacity && needed > 0; i++) {
                                                var e = inv.Slots[i];
                                                if (e.HasValue && e.Value.Item.Definition == reqItem) {
                                                    int take = Math.Min(needed, e.Value.Quantity);
                                                    needed -= take;
                                                    if (e.Value.Quantity == take) inv.Slots[i] = null;
                                                    else inv.Slots[i] = new ItemEntry(e.Value.Item, e.Value.Quantity - take);
                                                }
                                            }
                                        }
                                        
                                        // Выдаем результат
                                        int totalToGive = recipe.Count * craftTimes;
                                        while (totalToGive > 0) {
                                            int toAdd = Math.Min(totalToGive, recipe.Output.MaxStack);
                                            var item = GameData.NewItem(recipe.Output);
                                            if (!inv.TryInsert(item, toAdd)) {
                                                // Дропаем, если нет места
                                                var targetWorld = _session.GetWorld(client.Dimension);
                                                var dropPos = client.Position + new Vector3(0, 1.5f, 0);
                                                var p = new ItemPickup(item, toAdd, dropPos) {
                                                    Velocity = client.TargetPosition - client.Position + new Vector3(0, 0.2f, 0)
                                                };
                                                targetWorld.Pickups.Add(p);
                                            }
                                            totalToGive -= toAdd;
                                        }
                                    }
                                }
                            }
                            // После любого действия принудительно синхронизируем инвентарь клиента с сервером
                            client.Send(NetworkProtocol.WritePlayerInventoryUpdate(client.PlayerData, (byte)client.Dimension));
                        });
                        break;
                    }
                    case PacketType.PlayerInventoryUpdate: {
                        float px = reader.ReadSingle();
                        float py = reader.ReadSingle();
                        float pz = reader.ReadSingle();
                        float yaw = reader.ReadSingle();
                        float pitch = reader.ReadSingle();
                        float hp = reader.ReadSingle();
                        float hunger = reader.ReadSingle();
                        float sat = reader.ReadSingle();
                        int slot = reader.ReadInt32();
                        byte invDim = reader.ReadByte();

                        // Измерение, координаты и статы — только в разумных пределах:
                        // NaN/бесконечность из клиента ломали физику и рендер.
                        if (!float.IsFinite(px) || !float.IsFinite(py) || !float.IsFinite(pz) ||
                            !float.IsFinite(yaw) || !float.IsFinite(pitch)) {
                            break; // мусорный пакет — игнорируем целиком
                        }
                        if (invDim > (byte)Dimension.Void) invDim = (byte)client.Dimension;
                        if ((Dimension)invDim != client.Dimension && !IsNearPortal(client, (Dimension)invDim)) {
                            invDim = (byte)client.Dimension;
                        }

                        int invCount = Math.Clamp(reader.ReadInt32(), 0, client.PlayerData.Inventory.Capacity);
                        var newInvSlots = new Dictionary<int, ItemEntry>();
                        for (int i = 0; i < invCount; i++) {
                            int idx = reader.ReadInt32();
                            ushort itId = reader.ReadUInt16();
                            int itQty = reader.ReadInt32();
                            int itDur = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(itId, out var itDef) && idx >= 0 && idx < client.PlayerData.Inventory.Capacity) {
                                var itemInst = GameData.NewItem(itDef);
                                int maxDur = GameData.GetMaxItemDurability(itDef.Id);
                                if (maxDur > 0) itemInst.Durability = Math.Clamp(itDur, 1, maxDur);
                                int qty = Math.Clamp(itQty, 1, itDef.MaxStack);
                                newInvSlots[idx] = new ItemEntry(itemInst, qty);
                            }
                        }

                        ItemEntry? newOffhand = null;
                        if (reader.ReadBoolean()) {
                            ushort offId = reader.ReadUInt16();
                            int offQty = reader.ReadInt32();
                            int offDur = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(offId, out var offDef)) {
                                var itemInst = GameData.NewItem(offDef);
                                int maxDur = GameData.GetMaxItemDurability(offDef.Id);
                                if (maxDur > 0) itemInst.Durability = Math.Clamp(offDur, 1, maxDur);
                                int qty = Math.Clamp(offQty, 1, offDef.MaxStack);
                                newOffhand = new ItemEntry(itemInst, qty);
                            }
                        }

                        var newArmor = new ItemEntry?[4];
                        for (int a = 0; a < 4; a++) {
                            if (reader.ReadBoolean()) {
                                ushort armId = reader.ReadUInt16();
                                int armQty = reader.ReadInt32();
                                int armDur = reader.ReadInt32();
                                if (GameData.Items.TryGetValue(armId, out var armDef)) {
                                    var itemInst = GameData.NewItem(armDef);
                                    int maxDur = GameData.GetMaxItemDurability(armDef.Id);
                                    if (maxDur > 0) itemInst.Durability = Math.Clamp(armDur, 1, maxDur);
                                    int qty = Math.Clamp(armQty, 1, armDef.MaxStack);
                                    newArmor[a] = new ItemEntry(itemInst, qty);
                                }
                            }
                        }

                        EnqueueMainThreadAction(() => {
                            client.Dimension = (Dimension)invDim;
                            client.PlayerData.Dimension = (Dimension)invDim;

                            var pPos = new Vector3(px, py, pz);
                            client.PlayerData.Position = pPos;
                            if (client.Position == Vector3.Zero) {
                                client.Position = pPos;
                                client.TargetPosition = pPos;
                            }
                            client.PlayerData.Yaw = yaw;
                            client.PlayerData.Pitch = pitch;
                            if (float.IsFinite(hp)) client.PlayerData.Health = Math.Clamp(hp, 0f, 20f);
                            if (float.IsFinite(hunger)) client.PlayerData.Hunger = Math.Clamp(hunger, 0f, Player.MaxHunger);
                            if (float.IsFinite(sat)) client.PlayerData.Saturation = Math.Clamp(sat, 0f, 20f);
                            client.PlayerData.SelectedSlot = Math.Clamp(slot, 0, 8);

                            // Обновляем инвентарь на главном потоке (потокобезопасно для SavePlayerData и ResolveClientWeaponId)
                            client.PlayerData.Inventory.Clear();
                            foreach (var (idx, entry) in newInvSlots) {
                                client.PlayerData.Inventory.Slots[idx] = entry;
                            }
                            client.PlayerData.OffhandEntry = newOffhand;
                            for (int a = 0; a < 4; a++) {
                                client.PlayerData.Armor[a] = newArmor[a];
                            }
                        });
                        break;
                    }
                    case PacketType.PlayerMovement: {
                        int id = clientId; // Аутентифицированный clientId
                        reader.ReadInt32(); // Пропускаем client-supplied id
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        float yaw = reader.ReadSingle();
                        float pitch = reader.ReadSingle();
                        byte flags = reader.ReadByte();
                        float hp = reader.ReadSingle();
                        byte dim = reader.ReadByte();
                        var newPos = new Vector3(x, y, z);
                        // Отбрасываем некорректные пакеты: NaN/Inf-позиции и неизвестные измерения
                        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) ||
                            !float.IsFinite(yaw) || !float.IsFinite(pitch) || dim > 3) {
                            break;
                        }

                        if ((Dimension)dim != client.Dimension) {
                            if (IsNearPortal(client, (Dimension)dim)) {
                                client.Dimension = (Dimension)dim;
                                client.PlayerData.Dimension = (Dimension)dim;
                            } else {
                                dim = (byte)client.Dimension;
                            }
                        }

                        // Серверная валидация перемещения: ограничение скорости по времени
                        var now = DateTime.UtcNow;
                        float elapsed = (float)(now - client.LastMovementTime).TotalSeconds;
                        client.LastMovementTime = now;
                        if (elapsed <= 0f) elapsed = 0.001f;
                        if (elapsed > 1.0f) elapsed = 1.0f;

                        if (client.Position == Vector3.Zero) {
                            client.Position = newPos;
                            client.TargetPosition = newPos;
                        } else {
                            float maxAllowedSpeed = ((flags & 4) != 0 ? 35f : 18f); // 35 м/с при полёте, 18 м/с при спринте
                            float maxAllowedDist = maxAllowedSpeed * elapsed + 3.0f;
                            float dist = Vector3.Distance(client.Position, newPos);
                            if (dist > maxAllowedDist && dist > 8.0f) {
                                // Аномальный прыжок/телепорт — корректируем на сервере
                                newPos = client.Position;
                                client.Send(NetworkProtocol.WritePlayerMovement(clientId, client.Position, client.Yaw, client.Pitch, false, false, false, false, client.Health, (byte)client.Dimension));
                            } else {
                                client.Position = newPos;
                                client.TargetPosition = newPos;
                            }
                        }
                        client.TargetYaw = yaw;
                        client.TargetPitch = pitch;
                        client.IsMoving = (flags & 1) != 0;
                        client.IsSneaking = (flags & 2) != 0;
                        client.IsFlying = (flags & 4) != 0;
                        client.IsBlocking = (flags & 8) != 0;
                        client.Health = float.IsFinite(hp) ? Math.Clamp(hp, 0f, 20f) : client.Health;

                        // Broadcast movement with actual target newPos and authenticated clientId
                        var moveP = NetworkProtocol.WritePlayerMovement(clientId, newPos, yaw, pitch, client.IsMoving, client.IsSneaking, client.IsFlying, client.IsBlocking, client.Health, (byte)client.Dimension);
                        Broadcast(moveP, exceptClientId: clientId);
                        break;
                    }
                    case PacketType.PlayerAction: {
                        int id = reader.ReadInt32();
                        var action = (PlayerActionType)reader.ReadByte();
                        int itemId = reader.ReadInt32();
                        int quantity = 1;
                        int durability = 0;
                        try {
                            if (reader.BaseStream.Position < reader.BaseStream.Length) quantity = reader.ReadInt32();
                            if (reader.BaseStream.Position < reader.BaseStream.Length) durability = reader.ReadInt32();
                        } catch { }

                        client.SelectedItemId = itemId;
                        if (action == PlayerActionType.SwingArm) {
                            client.ArmSwingTimer = 1.0f;
                        } else if (action == PlayerActionType.Hurt) {
                            client.HurtTimer = 1.0f;
                        } else if (action == PlayerActionType.DropItem && itemId > 0 && GameData.Items.TryGetValue((ushort)itemId, out var dropDef)) {
                            EnqueueMainThreadAction(() => {
                                // Дроп берём из серверного инвентаря: раньше клиент мог
                                // spawn'ить предметы «из воздуха» в любом количестве.
                                var inv = client.PlayerData.Inventory;
                                int have = inv.CountOf(dropDef);
                                if (have <= 0) return;
                                int qty = Math.Clamp(quantity, 1, have);
                                if (!inv.TryRemove(dropDef, qty)) return;

                                var dropPos = client.Position + new Vector3(0f, 0.6f, 0f);
                                var fwd = new Vector3(-MathF.Sin(client.Yaw), 0f, MathF.Cos(client.Yaw));
                                var itemInst = GameData.NewItem(dropDef);
                                int maxDur = GameData.GetMaxItemDurability(dropDef.Id);
                                if (maxDur > 0) itemInst.Durability = Math.Clamp(durability, 1, maxDur);
                                var pickup = new ItemPickup(itemInst, qty, dropPos) {
                                    PickupDelay = 1.2f,
                                    Velocity = fwd * 4.5f + new Vector3(0f, 2.0f, 0f)
                                };
                                _session.GetWorld(client.Dimension).Pickups.Add(pickup);
                                // Синхронизируем урезанный инвентарь клиенту.
                                client.Send(NetworkProtocol.WritePlayerInventoryUpdate(client.PlayerData, (byte)client.Dimension));
                            });
                        } else if (action == PlayerActionType.ShootArrow) {
                            EnqueueMainThreadAction(() => {
                                var shootPos = client.Position + new Vector3(0f, 0.8f, 0f);
                                var fwd = new Vector3(-MathF.Sin(client.Yaw) * MathF.Cos(client.Pitch), -MathF.Sin(client.Pitch), MathF.Cos(client.Yaw) * MathF.Cos(client.Pitch));
                                var arr = new ArrowProjectile(shootPos, fwd * 28f, null) { FromPlayer = true, Damage = 7f };
                                _session.GetWorld(client.Dimension).Arrows.Add(arr);
                            });
                        }
                        var actP = NetworkProtocol.WritePlayerAction(clientId, action, itemId, quantity, durability);
                        Broadcast(actP, exceptClientId: clientId);
                        break;
                    }
                    case PacketType.PickupCollect: {
                        uint pickupId = reader.ReadUInt32();
                        int collectorId = reader.ReadInt32();
                        EnqueueMainThreadAction(() => {
                            // Запрещаем уничтожение чужих дропов: сервер собирает пикапы сам в Update().
                            if (collectorId != client.Id) return;
                            var targetWorld = _session.GetWorld(client.Dimension);
                            var p = targetWorld.Pickups.Find(x => x.Id == pickupId);
                            // Собирать можно только то, что реально рядом: иначе клиент
                            // «подбирал» любой дроп на карте по id.
                            if (p == null || Vector3.Distance(client.Position, p.Position) > 3.0f) return;
                            if (!client.PlayerData.Inventory.TryInsert(p.Item, p.Quantity)) return;
                            targetWorld.Pickups.Remove(p);
                            var collectP = NetworkProtocol.WritePickupCollect(pickupId, collectorId);
                            Broadcast(collectP);
                        });
                        break;
                    }
                    case PacketType.AttackEntity: {
                        uint entityId = reader.ReadUInt32();
                        float ignoredClientDmg = reader.ReadSingle();
                        bool isHostile = reader.ReadBoolean();

                        EnqueueMainThreadAction(() => {
                            var targetWorld = _session.GetWorld(client.Dimension);
                            // Серверный расчёт допустимого урона и проверка дистанции
                            ushort toolId = ResolveClientWeaponId(client);
                            float actualDmg = GameData.GetWeaponDamage(toolId);
                            
                            // Критический урон (упрощенная серверная эвристика: если игрок падает)
                            // Для надежности берем базовый урон, клиент может отправить крит, но мы проверяем жестко
                            float validDmg = Math.Clamp(actualDmg, 1f, actualDmg * 1.5f + 1f);

                            if (isHostile) {
                                var mob = targetWorld.HostileMobs.Find(m => m.Id == entityId);
                                if (mob != null && mob.Alive) {
                                    if (Vector3.Distance(client.Position, mob.Position) <= 6.0f) {
                                        mob.Health -= validDmg;
                                        mob.HurtTime = 0.5f;
                                        targetWorld.SpawnCrit(mob.Position, 8);
                                        SoundSystem.PlayHit();
                                        if (mob.Health <= 0f) {
                                            mob.Die(targetWorld, _session);
                                        }
                                    }
                                } else if (entityId == BossEntityIds.EndSlime) {
                                    // Боссы не лежат в списке мобов — у них свои зарезервированные id.
                                    var boss = targetWorld.EndBoss;
                                    if (boss is { Alive: true } && Vector3.Distance(client.Position, boss.Position) <= 8.0f) {
                                        boss.TakeDamage(validDmg, targetWorld, _session);
                                    }
                                } else if (entityId == BossEntityIds.TrueEndSlime) {
                                    var boss = targetWorld.TrueVoidBoss;
                                    if (boss is { Alive: true } && Vector3.Distance(client.Position, boss.Position) <= 8.0f) {
                                        boss.TakeDamage(validDmg, targetWorld, _session);
                                    }
                                }
                            } else {
                                var anim = targetWorld.Animals.Find(a => a.Id == entityId);
                                if (anim != null && anim.Alive) {
                                    if (Vector3.Distance(client.Position, anim.Position) <= 6.0f) {
                                        anim.Health -= validDmg;
                                        anim.HurtTime = 0.5f;
                                        targetWorld.SpawnCrit(anim.Position, 8);
                                        SoundSystem.PlayHit();
                                        if (anim.Health <= 0f) {
                                            anim.Die(targetWorld, _session);
                                        }
                                    }
                                }
                            }
                        });
                        break;
                    }
                    case PacketType.SpawnProjectile: {
                        int shooterId = clientId; // Аутентифицированный id стрелка
                        reader.ReadInt32(); // Пропускаем client-supplied shooterId
                        float px = reader.ReadSingle(), py = reader.ReadSingle(), pz = reader.ReadSingle();
                        float vx = reader.ReadSingle(), vy = reader.ReadSingle(), vz = reader.ReadSingle();
                        byte pTypeFlags = reader.ReadByte();
                        byte pDim = reader.ReadByte();
                        // Защита от NaN / бесконечности
                        if (!float.IsFinite(px) || !float.IsFinite(py) || !float.IsFinite(pz) ||
                            !float.IsFinite(vx) || !float.IsFinite(vy) || !float.IsFinite(vz)) {
                            break;
                        }
                        EnqueueMainThreadAction(() => {
                            // Валидация: снаряд должен создаваться рядом с игроком и в его измерении.
                            if ((Dimension)pDim != client.Dimension) return;
                            if (Vector3.Distance(client.Position, new Vector3(px, py, pz)) > 8f) return;
                            var targetWorld = _session.GetWorld((Dimension)pDim);
                            if (targetWorld == null) return;
                            var arr = new ArrowProjectile(new Vector3(px, py, pz), new Vector3(vx, vy, vz), null) {
                                IsSlimeSpit = (pTypeFlags & 1) != 0,
                                IsEnderPearl = (pTypeFlags & 2) != 0,
                                IsEyeOfEnder = (pTypeFlags & 4) != 0,
                                FromPlayer = true,
                                Damage = 7f
                            };
                            targetWorld.Arrows.Add(arr);
                            var spawnProjP = NetworkProtocol.WriteSpawnProjectile(shooterId, new Vector3(px, py, pz), new Vector3(vx, vy, vz), pTypeFlags, pDim);
                            Broadcast(spawnProjP, exceptClientId: clientId);
                        });
                        break;
                    }
                    case PacketType.Explosion: {
                        float ex = reader.ReadSingle(), ey = reader.ReadSingle(), ez = reader.ReadSingle();
                        float radius = Math.Clamp(reader.ReadSingle(), 0.5f, 5.0f);
                        float maxDmg = Math.Clamp(reader.ReadSingle(), 1f, 35f);
                        byte eDim = reader.ReadByte();

                        EnqueueMainThreadAction(() => {
                            // Валидация взрыва: только в своём измерении, рядом с игроком и с кулдауном.
                            if ((Dimension)eDim != client.Dimension) return;
                            double now = _session.TotalPlaySeconds;
                            if (now - client.LastExplosionTime < 1.0) return;
                            if (Vector3.Distance(client.Position, new Vector3(ex, ey, ez)) > 8.0f) return;
                            client.LastExplosionTime = now;
                            var targetWorld = _session.GetWorld((Dimension)eDim);
                            GameWorld.CreateExplosion(targetWorld, new Vector3(ex, ey, ez), radius, maxDmg, _session);
                            var expP = NetworkProtocol.WriteExplosion(new Vector3(ex, ey, ez), radius, maxDmg, eDim);
                            Broadcast(expP, exceptClientId: clientId);
                        });
                        break;
                    }
                    case PacketType.BedSleep: {
                        int clId = reader.ReadInt32();
                        bool isSleeping = reader.ReadBoolean();
                        int bedX = reader.ReadInt32(), bedY = reader.ReadInt32(), bedZ = reader.ReadInt32();
                        EnqueueMainThreadAction(() => {
                            // Спать можно только в обычном мире; иначе это способ манипуляции временем.
                            if (client.Dimension != Dimension.Overworld) return;
                            _session.DayNight.TimeOfDay = 0.25f;
                            _session.Weather = WeatherType.Clear;
                            var timePacket = NetworkProtocol.WriteTimeWeatherSync(_session.DayNight.TimeOfDay, (int)_session.Weather);
                            Broadcast(timePacket);
                            _session.AddChatMessage($"Ночь пропущена игроком {client.Name}!", Raylib_cs.Color.Green);
                        });
                        break;
                    }
                    case PacketType.RequestChest: {
                        int cx = reader.ReadInt32(), cy = reader.ReadInt32(), cz = reader.ReadInt32();
                        byte cDim = reader.ReadByte();
                        EnqueueMainThreadAction(() => {
                            if ((Dimension)cDim != client.Dimension) return;
                            var targetWorld = _session.GetWorld((Dimension)cDim);
                            var cPos = new Vec3i(cx, cy, cz);
                            // Сундук должен реально существовать и быть рядом: иначе
                            // клиент открывал контейнеры в любой точке карты.
                            if (!CanReachContainer(targetWorld, client, cPos, GameData.BChest.Id)) return;
                            var chest = targetWorld.GetOrCreateChest(cPos);
                            var chestPacket = NetworkProtocol.WriteChestSync(cx, cy, cz, chest, cDim);
                            client.Send(chestPacket);
                        });
                        break;
                    }
                    case PacketType.RequestFurnace: {
                        int fx = reader.ReadInt32(), fy = reader.ReadInt32(), fz = reader.ReadInt32();
                        byte fDim = reader.ReadByte();
                        EnqueueMainThreadAction(() => {
                            if ((Dimension)fDim != client.Dimension) return;
                            var targetWorld = _session.GetWorld((Dimension)fDim);
                            var fPos = new Vec3i(fx, fy, fz);
                            if (!CanReachContainer(targetWorld, client, fPos, GameData.BFurnace.Id)) return;
                            var f = targetWorld.GetOrCreateFurnace(fPos);
                            var inItem = f.Input.HasValue ? (f.Input.Value.Item.Definition.Id, f.Input.Value.Quantity) : ((ushort, int)?)null;
                            var fuelItem = f.Fuel.HasValue ? (f.Fuel.Value.Item.Definition.Id, f.Fuel.Value.Quantity) : ((ushort, int)?)null;
                            var outItem = f.Output.HasValue ? (f.Output.Value.Item.Definition.Id, f.Output.Value.Quantity) : ((ushort, int)?)null;
                            var fPacket = NetworkProtocol.WriteFurnaceSync(fx, fy, fz, f.FuelTimer, f.MaxFuelTimer, f.SmeltTimer, inItem, fuelItem, outItem, fDim);
                            client.Send(fPacket);
                        });
                        break;
                    }
                    case PacketType.FurnaceSync: {
                        int fx = reader.ReadInt32(), fy = reader.ReadInt32(), fz = reader.ReadInt32();
                        byte fDim = reader.ReadByte();
                        float fuelTimer = reader.ReadSingle();
                        float maxFuelTimer = reader.ReadSingle();
                        float smeltTimer = reader.ReadSingle();

                        ItemEntry? inItem = null;
                        if (reader.ReadBoolean()) {
                            ushort id = reader.ReadUInt16();
                            int qty = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(id, out var def) && qty > 0) inItem = new ItemEntry(GameData.NewItem(def), Math.Clamp(qty, 1, def.MaxStack));
                        }
                        ItemEntry? fItem = null;
                        if (reader.ReadBoolean()) {
                            ushort id = reader.ReadUInt16();
                            int qty = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(id, out var def) && qty > 0) fItem = new ItemEntry(GameData.NewItem(def), Math.Clamp(qty, 1, def.MaxStack));
                        }
                        ItemEntry? outItem = null;
                        if (reader.ReadBoolean()) {
                            ushort id = reader.ReadUInt16();
                            int qty = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(id, out var def) && qty > 0) outItem = new ItemEntry(GameData.NewItem(def), Math.Clamp(qty, 1, def.MaxStack));
                        }

                        EnqueueMainThreadAction(() => {
                            if ((Dimension)fDim != client.Dimension) return;
                            var targetWorld = _session.GetWorld((Dimension)fDim);
                            if (targetWorld == null) return;
                            var fPos = new Vec3i(fx, fy, fz);
                            if (!CanReachContainer(targetWorld, client, fPos, GameData.BFurnace.Id)) return;
                            var f = targetWorld.GetOrCreateFurnace(fPos);
                            f.FuelTimer = float.IsFinite(fuelTimer) ? Math.Clamp(fuelTimer, 0f, 600f) : 0f;
                            f.MaxFuelTimer = float.IsFinite(maxFuelTimer) ? Math.Clamp(maxFuelTimer, 0f, 600f) : 0f;
                            f.SmeltTimer = float.IsFinite(smeltTimer) ? Math.Clamp(smeltTimer, 0f, 600f) : 0f;

                            // Валидация печи: Output слот нельзя увеличивать клиенту (он производится только плавкой)!
                            if (outItem.HasValue) {
                                if (f.Output.HasValue && f.Output.Value.Item.Definition.Id == outItem.Value.Item.Definition.Id) {
                                    int allowedQty = Math.Min(f.Output.Value.Quantity, outItem.Value.Quantity);
                                    if (allowedQty > 0) f.Output = f.Output.Value with { Quantity = allowedQty };
                                    else f.Output = null;
                                }
                            } else {
                                f.Output = null;
                            }

                            // Валидация Input и Fuel слотов (нельзя положить больше, чем есть у игрока + было в печи)
                            var playerInv = client.PlayerData.Inventory;
                            if (inItem.HasValue) {
                                ushort inId = inItem.Value.Item.Definition.Id;
                                int prevIn = (f.Input.HasValue && f.Input.Value.Item.Definition.Id == inId) ? f.Input.Value.Quantity : 0;
                                int availIn = prevIn + playerInv.Entries.Where(e => e.Item.Definition.Id == inId).Sum(e => e.Quantity);
                                int allowedIn = Math.Clamp(inItem.Value.Quantity, 0, Math.Min(inItem.Value.Item.Definition.MaxStack, availIn));
                                if (allowedIn > 0) f.Input = inItem.Value with { Quantity = allowedIn };
                                else f.Input = null;
                            } else {
                                f.Input = null;
                            }

                            if (fItem.HasValue) {
                                ushort fId = fItem.Value.Item.Definition.Id;
                                int prevF = (f.Fuel.HasValue && f.Fuel.Value.Item.Definition.Id == fId) ? f.Fuel.Value.Quantity : 0;
                                int availF = prevF + playerInv.Entries.Where(e => e.Item.Definition.Id == fId).Sum(e => e.Quantity);
                                int allowedF = Math.Clamp(fItem.Value.Quantity, 0, Math.Min(fItem.Value.Item.Definition.MaxStack, availF));
                                if (allowedF > 0) f.Fuel = fItem.Value with { Quantity = allowedF };
                                else f.Fuel = null;
                            } else {
                                f.Fuel = null;
                            }

                            var inT = f.Input.HasValue ? (f.Input.Value.Item.Definition.Id, f.Input.Value.Quantity) : ((ushort, int)?)null;
                            var fuelT = f.Fuel.HasValue ? (f.Fuel.Value.Item.Definition.Id, f.Fuel.Value.Quantity) : ((ushort, int)?)null;
                            var outT = f.Output.HasValue ? (f.Output.Value.Item.Definition.Id, f.Output.Value.Quantity) : ((ushort, int)?)null;
                            var fPacket = NetworkProtocol.WriteFurnaceSync(fx, fy, fz, f.FuelTimer, f.MaxFuelTimer, f.SmeltTimer, inT, fuelT, outT, fDim);
                            Broadcast(fPacket);
                        });
                        break;
                    }
                    case PacketType.PlayerHit: {
                        int attackerId = clientId; // Аутентифицированный id нападающего
                        reader.ReadInt32(); // Пропускаем client-supplied attackerId
                        int targetId = reader.ReadInt32();
                        float ignoredClientDmg = reader.ReadSingle();

                        EnqueueMainThreadAction(() => {
                            ushort toolId = ResolveClientWeaponId(client);
                            float actualDmg = GameData.GetWeaponDamage(toolId);
                            float validDmg = Math.Clamp(actualDmg, 1f, actualDmg * 1.5f + 1f);

                            if (targetId == 1) {
                                var attackerPos = client.Position;
                                if (Vector3.Distance(attackerPos, _session.Player.Position) <= 6.0f) {
                                    _session.Player.ApplyDamage(validDmg, _session, attackerPos, cause: client.Name);
                                    _session.Player.HurtTimer = 1.0f;
                                    BroadcastHostAction(PlayerActionType.Hurt, 0);
                                }
                            } else {
                                if (_clients.TryGetValue(targetId, out var targetClient)) {
                                    if (Vector3.Distance(client.Position, targetClient.Position) <= 6.0f) {
                                        var hitPacket = NetworkProtocol.WritePlayerHit(attackerId, targetId, validDmg);
                                        Broadcast(hitPacket, exceptClientId: clientId);
                                    }
                                }
                            }
                        });
                        break;
                    }
                    case PacketType.BlockChange: {
                        int bx = reader.ReadInt32();
                        int by = reader.ReadInt32();
                        int bz = reader.ReadInt32();
                        ushort typeId = reader.ReadUInt16();
                        byte mask = reader.ReadByte();
                        bool isBreak = reader.ReadBoolean();
                        byte dim = reader.ReadByte();

                        EnqueueMainThreadAction(() => {
                            var targetWorld = _session.GetWorld((Dimension)dim);
                            var cell = new Vec3i(bx, by, bz);
                            var blockCenter = new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f);

                            // Проверка дистанции взаимодействия (до 7.5 блоков)
                            if (Vector3.Distance(client.Position, blockCenter) > 7.5f) {
                                var currentV = targetWorld.GetVoxel(cell);
                                client.Send(NetworkProtocol.WriteBlockReject(bx, by, bz, currentV.TypeId, currentV.SubGridLayerMask, dim));
                                return;
                            }

                            if (isBreak) {
                                var currentV = targetWorld.GetVoxel(cell);
                                if (currentV.TypeId == GameData.BBedrock.Id && _session.GameMode != GameMode.Creative) {
                                    client.Send(NetworkProtocol.WriteBlockReject(bx, by, bz, currentV.TypeId, currentV.SubGridLayerMask, dim));
                                    return;
                                }

                                if (_session.GameMode != GameMode.Creative && GameData.TryGetBlock(currentV.TypeId, out var blk) && blk != null) {
                                    if (blk.DropItemId != 0 && GameData.Items.TryGetValue(blk.DropItemId, out var drop)) {
                                        targetWorld.SpawnPickup(drop.Id, blk.DropItemCount, cell);
                                    }
                                }

                                GameWorld.SuppressNetworkSync = true;
                                try {
                                    targetWorld.RemoveBlock(cell);
                                } finally {
                                    GameWorld.SuppressNetworkSync = false;
                                }
                            } else {
                                // Server-side item consumption
                                bool hasItem = false;
                                ushort requiredItemId;
                                if (_session.GameMode == GameMode.Creative) {
                                    hasItem = true;
                                    requiredItemId = typeId;
                                } else {
                                    bool hasReq = GameData.TryGetItemByBlock(typeId, out requiredItemId);
                                    if (!hasReq) requiredItemId = typeId;

                                    var inv = client.PlayerData.Inventory;
                                    for (int i = 0; i < inv.Capacity; i++) {
                                        var entry = inv.Slots[i];
                                        if (entry.HasValue && entry.Value.Item.Definition.Id == requiredItemId) {
                                            hasItem = true;
                                            if (entry.Value.Quantity > 1) {
                                                inv.Slots[i] = new ItemEntry(entry.Value.Item, entry.Value.Quantity - 1);
                                            } else {
                                                inv.Slots[i] = null;
                                            }
                                            break;
                                        }
                                    }
                                    
                                    // Для особых составных блоков (например, кровать), проверяем сам предмет кровати
                                    if (!hasItem && (typeId == GameData.BBed.Id || typeId == GameData.BBedHead.Id || typeId == GameData.BDoorLower.Id || typeId == GameData.BDoorUpper.Id)) {
                                        hasItem = true; // Упрощение для составных блоков, которые устанавливаются несколькими пакетами
                                    }
                                }

                                if (!hasItem) {
                                    // У игрока нет предмета - отклоняем
                                    var currentV = targetWorld.GetVoxel(cell);
                                    client.Send(NetworkProtocol.WriteBlockReject(bx, by, bz, currentV.TypeId, currentV.SubGridLayerMask, dim));
                                    client.Send(NetworkProtocol.WritePlayerInventoryUpdate(client.PlayerData, dim)); // Синхронизируем обратно
                                    return;
                                }

                                GameWorld.SuppressNetworkSync = true;
                                try {
                                    if (GameData.TryGetBlock(typeId, out var block)) {
                                        targetWorld.PlacePlacedBlock(cell, block, mask);
                                    }
                                } finally {
                                    GameWorld.SuppressNetworkSync = false;
                                }
                                client.Send(NetworkProtocol.WritePlayerInventoryUpdate(client.PlayerData, dim));
                            }

                            // Broadcast to other clients
                            var blockP = NetworkProtocol.WriteBlockChange(bx, by, bz, typeId, mask, isBreak, dim);
                            Broadcast(blockP, exceptClientId: clientId);
                        });
                        break;
                    }
                    case PacketType.ChestSync: {
                        int cx = reader.ReadInt32();
                        int cy = reader.ReadInt32();
                        int cz = reader.ReadInt32();
                        byte dim = reader.ReadByte();
                        int cCount = Math.Clamp(reader.ReadInt32(), 0, GameWorld.ChestCapacity);
                        var items = new List<(int idx, ushort id, int qty, int dur)>();
                        for (int i = 0; i < cCount; i++) {
                            items.Add((reader.ReadInt32(), reader.ReadUInt16(), reader.ReadInt32(), reader.ReadInt32()));
                        }

                        EnqueueMainThreadAction(() => {
                            if ((Dimension)dim != client.Dimension) return;
                            var targetWorld = _session.GetWorld((Dimension)dim);
                            if (targetWorld == null) return;
                            var cPos = new Vec3i(cx, cy, cz);
                            if (!CanReachContainer(targetWorld, client, cPos, GameData.BChest.Id)) return;
                            var chest = targetWorld.GetOrCreateChest(cPos);

                            // Валидация сохранения предметов: предметы в сундуке могут браться только
                            // из существующего содержимого сундука или инвентаря игрока.
                            var playerInv = client.PlayerData.Inventory;
                            var available = new Dictionary<ushort, int>();
                            for (int i = 0; i < chest.Capacity; i++) {
                                if (chest.Slots[i] is { } ce && ce.Quantity > 0) {
                                    ushort id = ce.Item.Definition.Id;
                                    available[id] = available.GetValueOrDefault(id) + ce.Quantity;
                                }
                            }
                            for (int i = 0; i < playerInv.Capacity; i++) {
                                if (playerInv.Slots[i] is { } pe && pe.Quantity > 0) {
                                    ushort id = pe.Item.Definition.Id;
                                    available[id] = available.GetValueOrDefault(id) + pe.Quantity;
                                }
                            }

                            for (int i = 0; i < chest.Capacity; i++) chest.RemoveAt(i);
                            foreach (var item in items) {
                                if (GameData.Items.TryGetValue(item.id, out var def)) {
                                    if (item.idx < 0 || item.idx >= chest.Capacity) continue;
                                    int maxAvail = available.GetValueOrDefault(item.id, 0);
                                    int qty = Math.Clamp(item.qty, 0, Math.Min(def.MaxStack, maxAvail));
                                    if (qty <= 0) continue;
                                    available[item.id] -= qty;

                                    var inst = GameData.NewItem(def);
                                    int maxDur = GameData.GetMaxItemDurability(def.Id);
                                    if (maxDur > 0) inst.Durability = Math.Clamp(item.dur, 1, maxDur);
                                    chest.InsertAt(item.idx, new ItemEntry(inst, qty));
                                }
                            }
                            var chestPacket = NetworkProtocol.WriteChestSync(cx, cy, cz, chest, dim);
                            Broadcast(chestPacket, exceptClientId: clientId);
                        });
                        break;
                    }
                    case PacketType.ChatMessage: {
                        reader.ReadString(); // Пропускаем client-supplied sender для защиты от подмены имени
                        string msg = reader.ReadString();
                        byte r = reader.ReadByte();
                        byte g = reader.ReadByte();
                        byte b = reader.ReadByte();
                        string sender = client.Name;
                        EnqueueMainThreadAction(() => {
                            _session.AddChatMessage($"<{sender}> {msg}", new Raylib_cs.Color(r, g, b, (byte)255));
                        });
                        var chatP = NetworkProtocol.WriteChatMessage(sender, msg, r, g, b);
                        Broadcast(chatP, exceptClientId: clientId);
                        break;
                    }
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"[GameServer Client {client.Name}] Исключение в цикле сокета: {ex.Message}");
        } finally {
            _clients.TryRemove(clientId, out _);
            client.Close();
            Interlocked.Decrement(ref _activeClientConnections);
            // Списки чата/сообщений итерирует главный поток в HUD — мутировать их
            // из сокет-потока нельзя (InvalidOperationException в отрисовке).
            // Сохранение тоже уходит в главный поток: инвентарь меняется именно там.
            if (handshaked) {
                EnqueueMainThreadAction(() => {
                    SaveSystem.SavePlayerData(client.Name, client.PlayerData);
                    _session.AddChatMessage($"Игрок {client.Name} покинул игру.", Raylib_cs.Color.Yellow);
                    _session.AddMessage($"Игрок {client.Name} вышел");
                });
                var leaveP = NetworkProtocol.WritePlayerLeave(clientId);
                Broadcast(leaveP);
            }
        }
    }

    /// <summary>
    /// Можно ли клиенту трогать контейнер в этой точке: блок нужного типа реально
    /// существует в мире, клиент в том же измерении и не дальше 6 блоков.
    /// </summary>
    private static bool CanReachContainer(GameWorld world, ConnectedClient client, Vec3i pos, ushort expectedBlockId) {
        if (client.Dimension != world.Dimension) return false;
        if (world.GetVoxel(pos).TypeId != expectedBlockId) return false;
        var center = new Vector3(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
        return Vector3.Distance(client.Position, center) <= 6.0f;
    }

    private bool IsNearPortal(ConnectedClient client, Dimension targetDim) {
        var world = _session.GetWorld(client.Dimension);
        if (world == null) return false;
        ushort requiredPortal = targetDim switch {
            Dimension.Nether => GameData.BNetherPortal.Id,
            Dimension.End => GameData.BEndPortal.Id,
            Dimension.Overworld => (client.Dimension == Dimension.Nether ? GameData.BNetherPortal.Id : GameData.BEndPortal.Id),
            _ => 0
        };
        if (requiredPortal == 0) return true;
        int cx = (int)MathF.Floor(client.Position.X);
        int cy = (int)MathF.Floor(client.Position.Y);
        int cz = (int)MathF.Floor(client.Position.Z);
        for (int dx = -6; dx <= 6; dx++) {
            for (int dy = -6; dy <= 6; dy++) {
                for (int dz = -6; dz <= 6; dz++) {
                    if (world.GetVoxel(new Vec3i(cx + dx, cy + dy, cz + dz)).TypeId == requiredPortal) {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public void TeleportClient(int clientId, Vector3 pos) {
        if (_clients.TryGetValue(clientId, out var client)) {
            client.Position = pos;
            client.TargetPosition = pos;
            client.PlayerData.Position = pos;
            var tpPacket = NetworkProtocol.WriteTeleport(pos);
            client.Send(tpPacket);
        }
    }

    public bool TeleportClientByName(string name, Vector3 pos) {
        foreach (var client in _clients.Values) {
            if (client.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                TeleportClient(client.Id, pos);
                return true;
            }
        }
        return false;
    }

    public void SaveAllPlayers() {
        foreach (var client in _clients.Values) {
            SaveSystem.SavePlayerData(client.Name, client.PlayerData);
        }
    }

    public void Broadcast(byte[] data, int exceptClientId = -1) {
        foreach (var client in _clients.Values) {
            if (client.Id == exceptClientId) continue;
            client.Send(data);
        }
    }

    public void BroadcastHostMovement(Vector3 pos, float yaw, float pitch, bool isMoving, bool isSneaking, bool isFlying, bool isBlocking, float health) {
        if (!IsRunning || ClientCount == 0) return;
        var p = NetworkProtocol.WritePlayerMovement(1, pos, yaw, pitch, isMoving, isSneaking, isFlying, isBlocking, health, (byte)_session.World.Dimension);
        Broadcast(p);
    }

    public void BroadcastHostAction(PlayerActionType action, int itemId) {
        if (!IsRunning || ClientCount == 0) return;
        var p = NetworkProtocol.WritePlayerAction(1, action, itemId);
        Broadcast(p);
    }

    public void BroadcastHostHit(int targetId, float damage) {
        if (!IsRunning || ClientCount == 0) return;
        var p = NetworkProtocol.WritePlayerHit(1, targetId, damage);
        Broadcast(p);
    }

    public void BroadcastHostBlockChange(int x, int y, int z, ushort blockTypeId, byte mask, bool isBreak) {
        if (!IsRunning || ClientCount == 0) return;
        var p = NetworkProtocol.WriteBlockChange(x, y, z, blockTypeId, mask, isBreak, (byte)_session.World.Dimension);
        Broadcast(p);
    }

    public void BroadcastBlockChange(int x, int y, int z, ushort blockTypeId, byte mask, bool isBreak, byte dimension) {
        if (!IsRunning || ClientCount == 0) return;
        var p = NetworkProtocol.WriteBlockChange(x, y, z, blockTypeId, mask, isBreak, dimension);
        Broadcast(p);
    }

    public void BroadcastHostChat(string name, string message) {
        if (!IsRunning || ClientCount == 0) return;
        var p = NetworkProtocol.WriteChatMessage(name, message);
        Broadcast(p);
    }

    public void BroadcastChestSync(Vec3i pos, Container container) {
        if (!IsRunning || ClientCount == 0) return;
        var p = NetworkProtocol.WriteChestSync(pos.X, pos.Y, pos.Z, container, (byte)_session.World.Dimension);
        Broadcast(p);
    }

    public void BroadcastFurnaceSync(Vec3i pos, FurnaceData furnace, byte dimension) {
        if (!IsRunning || ClientCount == 0) return;
        var inItem = furnace.Input.HasValue ? (furnace.Input.Value.Item.Definition.Id, furnace.Input.Value.Quantity) : ((ushort, int)?)null;
        var fuelItem = furnace.Fuel.HasValue ? (furnace.Fuel.Value.Item.Definition.Id, furnace.Fuel.Value.Quantity) : ((ushort, int)?)null;
        var outItem = furnace.Output.HasValue ? (furnace.Output.Value.Item.Definition.Id, furnace.Output.Value.Quantity) : ((ushort, int)?)null;
        var p = NetworkProtocol.WriteFurnaceSync(pos.X, pos.Y, pos.Z, furnace.FuelTimer, furnace.MaxFuelTimer, furnace.SmeltTimer, inItem, fuelItem, outItem, dimension);
        Broadcast(p);
    }

    public void BroadcastGameRuleSync(string rule, bool value) {
        if (!IsRunning || ClientCount == 0) return;
        var p = NetworkProtocol.WriteGameRuleSync(rule, value);
        Broadcast(p);
    }

    public void Dispose() {
        SaveAllPlayers();
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        foreach (var c in _clients.Values) {
            c.Close();
        }
        _clients.Clear();
        _cts?.Dispose();
        _cts = null;
    }
}
