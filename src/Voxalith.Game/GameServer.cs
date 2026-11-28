using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Voxalith.Core;
using Voxalith.Core.Inventory;

namespace Voxalith.Game;

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
    public DateTime LastAttackTime { get; set; } = DateTime.MinValue;
    public DateTime LastChatTime { get; set; } = DateTime.MinValue;
    public DateTime LastProjectileTime { get; set; } = DateTime.MinValue;

    // Асинхронная очередь отправки: главный цикл и хендлеры не блокируются на медленных сокетах.
    private readonly Channel<byte[]> _sendChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private long _queuedPackets;
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
            if (Interlocked.Read(ref _queuedPackets) >= MaxQueuedPackets) {
                if (NetworkProtocol.IsReplaceableSnapshot(data[4])) return;
                _connected = false;
                try { Socket.Close(); } catch { }
                return;
            }
            if (_sendChannel.Writer.TryWrite(data)) {
                Interlocked.Increment(ref _queuedPackets);
            }
        } catch (ObjectDisposedException) {
            _connected = false;
        }
    }

    private async Task SendWriterLoop() {
        var token = _sendCts.Token;
        try {
            while (await _sendChannel.Reader.WaitToReadAsync(token)) {
                while (_sendChannel.Reader.TryRead(out var data)) {
                    Interlocked.Decrement(ref _queuedPackets);
                    if (!_connected || !Socket.Connected) return;
                    var stream = Socket.GetStream();
                    await stream.WriteAsync(data, token);
                }
            }
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            Console.WriteLine($"[GameServer Client {Id}] Ошибка отправки пакета: {ex.Message}");
            _connected = false;
        }
    }

    public void Close() {
        _connected = false;
        _sendChannel.Writer.TryComplete();
        try { _sendCts.Cancel(); } catch { }
        try { Socket.Close(); } catch { }
        try { _writerTask.Wait(50); } catch { }
        try { _sendCts.Dispose(); } catch { }
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

    private sealed class ServerWorldNetworkSink(GameServer server) : IWorldNetworkSink {
        public bool IsRemoteClient => false;
        public void OnBlockChanged(Vec3i pos, ushort typeId, byte mask, bool isBreak, Dimension dimension) =>
            server.BroadcastBlockChange(pos.X, pos.Y, pos.Z, typeId, mask, isBreak, (byte)dimension);
    }

    public static GameServer Start(GameSession session, int port = NetworkProtocol.DefaultPort) {
        Active?.Dispose();
        var srv = new GameServer(session, port);
        srv.StartListening();
        Active = srv;
        session.BindNetworkSink(new ServerWorldNetworkSink(srv));
        return srv;
    }

    public static void Stop() {
        var owner = Active;
        if (owner != null) {
            owner._session.BindNetworkSink(null);
            owner.Dispose();
        }
        Active = null;
    }

    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private const int MaxMainThreadActions = 8192;

    public void EnqueueMainThreadAction(Action action) {
        if (_mainThreadActions.Count >= MaxMainThreadActions) return;
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
                    _ = Task.Run(() => HandleClient(socket, token), CancellationToken.None);
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
        ushort claimed = (ushort)client.SelectedItemId;
        if (claimed == 0) return 0;
        var inv = client.PlayerData.Inventory;
        for (int i = 0; i < inv.Capacity; i++) {
            if (inv.Slots[i] is { } e && e.Quantity > 0 && e.Item.Definition.Id == claimed) return claimed;
        }
        return 0;
    }

    private static bool ValidateSurvivalInventoryTransition(Player serverPlayer, Dictionary<int, ItemEntry> newInv, ItemEntry? newOffhand, ItemEntry?[] newArmor) {
        var avail = new Dictionary<ushort, int>();
        var maxDur = new Dictionary<ushort, int>();

        void AddItem(in ItemEntry e) {
            if (e.Quantity <= 0) return;
            ushort id = e.Item.Definition.Id;
            avail[id] = avail.GetValueOrDefault(id) + e.Quantity;
            if (e.Item.Durability > maxDur.GetValueOrDefault(id)) {
                maxDur[id] = e.Item.Durability;
            }
        }

        for (int i = 0; i < serverPlayer.Inventory.Capacity; i++) {
            if (serverPlayer.Inventory.Slots[i] is { } e) AddItem(in e);
        }
        if (serverPlayer.OffhandEntry is { } off) AddItem(in off);
        for (int a = 0; a < 4; a++) {
            if (serverPlayer.Armor[a] is { } arm) AddItem(in arm);
        }

        bool TakeItem(in ItemEntry e) {
            if (e.Quantity <= 0) return true;
            ushort id = e.Item.Definition.Id;
            int have = avail.GetValueOrDefault(id, 0);
            if (e.Quantity > have) return false;
            if (GameData.GetMaxItemDurability(id) > 0 && e.Item.Durability > maxDur.GetValueOrDefault(id, 0)) {
                return false;
            }
            avail[id] = have - e.Quantity;
            return true;
        }

        foreach (var kv in newInv) {
            if (!TakeItem(kv.Value)) return false;
        }
        if (newOffhand is { } nOff && !TakeItem(in nOff)) return false;
        for (int a = 0; a < 4; a++) {
            if (newArmor[a] is { } nArm && !TakeItem(in nArm)) return false;
        }
        return true;
    }

    private static void AdjustPlayerInventoryDelta(Container inv, ushort itemId, int delta, int durabilityHint = 0) {
        if (delta == 0 || !GameData.Items.TryGetValue(itemId, out var def)) return;
        if (delta > 0) {
            int rem = delta;
            while (rem > 0) {
                int chunk = Math.Min(rem, def.MaxStack);
                var inst = GameData.NewItem(def);
                int maxDur = GameData.GetMaxItemDurability(def.Id);
                if (maxDur > 0 && durabilityHint > 0) inst.Durability = Math.Clamp(durabilityHint, 1, maxDur);
                inv.TryInsert(inst, chunk);
                rem -= chunk;
            }
        } else {
            inv.TryRemove(def, -delta);
        }
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
                var frameNullable = NetworkProtocol.ReadPacketFrame(stream, allowLargeWorldSync: false);
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
                        string rawName = reader.ReadString();
                        string ver = reader.ReadString();
                        string rawSkin = "cyan";
                        try { rawSkin = reader.ReadString(); } catch { }

                        string name = NetworkProtocol.SanitizePlayerName(rawName, "Player", 20);
                        string skin = NetworkProtocol.SanitizePlayerName(rawSkin, "cyan", 24);
                        
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
                                initSyncTcs.TrySetException(ex);
                            }
                        });

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
                        _ = reader.ReadString();
                        return; // Сохранение выполнится безопасно в finally через EnqueueMainThreadAction
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
                            client.PlayerData.SelectedSlot = Math.Clamp(slot, 0, 8);
                            if (_session.GameMode != GameMode.Creative &&
                                !ValidateSurvivalInventoryTransition(client.PlayerData, newInvSlots, newOffhand, newArmor)) {
                                // Отклоняем попытку подделать инвентарь в Survival и возвращаем серверное состояние.
                                client.Send(NetworkProtocol.WritePlayerInventoryUpdate(client.PlayerData, (byte)client.Dimension));
                                return;
                            }

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
                            !float.IsFinite(yaw) || !float.IsFinite(pitch) || dim > (byte)Dimension.Void) {
                            break;
                        }
                        var packetTime = DateTime.UtcNow;

                        EnqueueMainThreadAction(() => {
                            if ((Dimension)dim != client.Dimension) {
                                if (IsNearPortal(client, (Dimension)dim)) {
                                    client.Dimension = (Dimension)dim;
                                    client.PlayerData.Dimension = (Dimension)dim;
                                } else {
                                    dim = (byte)client.Dimension;
                                }
                            }

                            bool wantsFly = (flags & 4) != 0 && _session.GameMode == GameMode.Creative;
                            float elapsed = Math.Clamp((float)(packetTime - client.LastMovementTime).TotalSeconds, 0.01f, 1.0f);
                            client.LastMovementTime = packetTime;

                            if (client.Position == Vector3.Zero) {
                                client.Position = newPos;
                                client.TargetPosition = newPos;
                            } else {
                                float maxAllowedSpeed = wantsFly ? 35f : 18f;
                                float maxAllowedDist = maxAllowedSpeed * elapsed + 2.0f;
                                float dist = Vector3.Distance(client.Position, newPos);
                                if (dist > maxAllowedDist) {
                                    // Аномальный прыжок/телепорт — корректируем на сервере
                                    newPos = client.Position;
                                    client.Send(NetworkProtocol.WritePlayerMovement(clientId, client.Position, client.Yaw, client.Pitch, false, false, wantsFly, false, client.Health, (byte)client.Dimension));
                                } else {
                                    client.Position = newPos;
                                    client.TargetPosition = newPos;
                                }
                            }
                            client.PlayerData.Position = newPos;
                            client.PlayerData.Yaw = yaw;
                            client.PlayerData.Pitch = pitch;
                            client.TargetYaw = yaw;
                            client.TargetPitch = pitch;
                            client.IsMoving = (flags & 1) != 0;
                            client.IsSneaking = (flags & 2) != 0;
                            client.IsFlying = wantsFly;
                            client.IsBlocking = (flags & 8) != 0;

                            // Здоровье не может произвольно подниматься вверх (защита от PvP godmode):
                            // разрешаем снижение и лишь естественную регенерацию вне боевого окна.
                            if (float.IsFinite(hp)) {
                                float clampedHp = Math.Clamp(hp, 0f, client.PlayerData.MaxHealth);
                                if (client.Health <= 0f) {
                                    clampedHp = 0f; // Восстановление только через Respawn
                                } else if (clampedHp > client.Health) {
                                    float maxRegen = client.HurtTimer > 0f ? 0f : 0.75f * elapsed + 0.1f;
                                    clampedHp = Math.Min(clampedHp, client.Health + maxRegen);
                                }
                                client.Health = clampedHp;
                                client.PlayerData.Health = clampedHp;
                            }

                            var moveP = NetworkProtocol.WritePlayerMovement(clientId, newPos, yaw, pitch, client.IsMoving, client.IsSneaking, client.IsFlying, client.IsBlocking, client.Health, (byte)client.Dimension);
                            Broadcast(moveP, exceptClientId: clientId);
                        });
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

                        EnqueueMainThreadAction(() => {
                            client.SelectedItemId = itemId;
                            if (action == PlayerActionType.SwingArm) {
                                client.ArmSwingTimer = 1.0f;
                            } else if (action == PlayerActionType.Hurt) {
                                client.HurtTimer = 1.0f;
                            } else if (action == PlayerActionType.Respawn) {
                                client.Health = client.PlayerData.MaxHealth;
                                client.PlayerData.Health = client.PlayerData.MaxHealth;
                                client.HurtTimer = 0f;
                            } else if (action == PlayerActionType.DropItem && itemId > 0 && GameData.Items.TryGetValue((ushort)itemId, out var dropDef)) {
                                var inv = client.PlayerData.Inventory;
                                int qty = 0;
                                int haveInv = inv.CountOf(dropDef);
                                if (haveInv > 0) {
                                    qty = Math.Clamp(quantity, 1, haveInv);
                                    if (!inv.TryRemove(dropDef, qty)) qty = 0;
                                } else if (client.PlayerData.OffhandEntry is { } off && off.Item.Definition.Id == dropDef.Id && off.Quantity > 0) {
                                    qty = Math.Clamp(quantity, 1, off.Quantity);
                                    client.PlayerData.OffhandEntry = off.Quantity > qty ? off with { Quantity = off.Quantity - qty } : null;
                                } else {
                                    for (int a = 0; a < 4; a++) {
                                        if (client.PlayerData.Armor[a] is { } arm && arm.Item.Definition.Id == dropDef.Id && arm.Quantity > 0) {
                                            qty = Math.Clamp(quantity, 1, arm.Quantity);
                                            client.PlayerData.Armor[a] = arm.Quantity > qty ? arm with { Quantity = arm.Quantity - qty } : null;
                                            break;
                                        }
                                    }
                                }
                                if (qty <= 0) return;

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
                                client.Send(NetworkProtocol.WritePlayerInventoryUpdate(client.PlayerData, (byte)client.Dimension));
                            }
                            var actP = NetworkProtocol.WritePlayerAction(clientId, action, itemId, quantity, durability);
                            Broadcast(actP, exceptClientId: clientId);
                        });
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
                        var now = DateTime.UtcNow;

                        EnqueueMainThreadAction(() => {
                            if ((now - client.LastAttackTime).TotalSeconds < 0.2) return;
                            client.LastAttackTime = now;
                            var targetWorld = _session.GetWorld(client.Dimension);
                            ushort toolId = ResolveClientWeaponId(client);
                            float actualDmg = GameData.GetWeaponDamage(toolId);
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
                        int shooterId = clientId;
                        reader.ReadInt32();
                        float px = reader.ReadSingle(), py = reader.ReadSingle(), pz = reader.ReadSingle();
                        float vx = reader.ReadSingle(), vy = reader.ReadSingle(), vz = reader.ReadSingle();
                        byte pTypeFlags = reader.ReadByte();
                        byte pDim = reader.ReadByte();
                        if (!float.IsFinite(px) || !float.IsFinite(py) || !float.IsFinite(pz) ||
                            !float.IsFinite(vx) || !float.IsFinite(vy) || !float.IsFinite(vz) || pDim > (byte)Dimension.Void) {
                            break;
                        }
                        var now = DateTime.UtcNow;
                        EnqueueMainThreadAction(() => {
                            if ((Dimension)pDim != client.Dimension) return;
                            if ((now - client.LastProjectileTime).TotalSeconds < 0.15) return;
                            if (Vector3.Distance(client.Position, new Vector3(px, py, pz)) > 8f) return;
                            if ((pTypeFlags & 1) != 0) return; // Клиент не может спавнить плевки босса
                            var velVec = new Vector3(vx, vy, vz);
                            if (velVec.LengthSquared() > 60f * 60f) return;

                            bool isPearl = (pTypeFlags & 2) != 0;
                            bool isEye = (pTypeFlags & 4) != 0;
                            if (_session.GameMode != GameMode.Creative) {
                                var inv = client.PlayerData.Inventory;
                                if (isPearl) {
                                    if (!inv.TryRemove(GameData.EnderPearlItem, 1)) return;
                                } else if (isEye) {
                                    if (!inv.TryRemove(GameData.EyeOfEnderItem, 1)) return;
                                } else {
                                    if (inv.CountOf(GameData.BowItem) <= 0) return;
                                    if (client.PlayerData.OffhandEntry is { } off && off.Item.Definition.Id == GameData.ArrowItem.Id && off.Quantity > 0) {
                                        client.PlayerData.OffhandEntry = off.Quantity > 1 ? off with { Quantity = off.Quantity - 1 } : null;
                                    } else if (!inv.TryRemove(GameData.ArrowItem, 1)) {
                                        return;
                                    }
                                }
                            }
                            client.LastProjectileTime = now;
                            var targetWorld = _session.GetWorld((Dimension)pDim);
                            if (targetWorld == null) return;
                            var arr = new ArrowProjectile(new Vector3(px, py, pz), velVec, null) {
                                IsEnderPearl = isPearl,
                                IsEyeOfEnder = isEye,
                                FromPlayer = true,
                                Damage = 7f
                            };
                            targetWorld.Arrows.Add(arr);
                            var spawnProjP = NetworkProtocol.WriteSpawnProjectile(shooterId, new Vector3(px, py, pz), velVec, (byte)(pTypeFlags & 6), pDim);
                            Broadcast(spawnProjP, exceptClientId: clientId);
                        });
                        break;
                    }
                    case PacketType.Explosion: {
                        // Клиент не имеет права инициировать произвольные взрывы; взрывы создаются только сервером.
                        _ = reader.ReadSingle(); _ = reader.ReadSingle(); _ = reader.ReadSingle();
                        _ = reader.ReadSingle(); _ = reader.ReadSingle(); _ = reader.ReadByte();
                        break;
                    }
                    case PacketType.BedSleep: {
                        int clId = reader.ReadInt32();
                        bool isSleeping = reader.ReadBoolean();
                        int bedX = reader.ReadInt32(), bedY = reader.ReadInt32(), bedZ = reader.ReadInt32();
                        EnqueueMainThreadAction(() => {
                            if (client.Dimension != Dimension.Overworld) return;
                            var bedPos = new Vec3i(bedX, bedY, bedZ);
                            var world = _session.GetWorld(Dimension.Overworld);
                            if (world == null) return;
                            ushort bedType = world.GetVoxel(bedPos).TypeId;
                            if (bedType != GameData.BBed.Id && bedType != GameData.BBedHead.Id) return;
                            if (Vector3.Distance(client.Position, new Vector3(bedX + 0.5f, bedY + 0.5f, bedZ + 0.5f)) > 6.5f) return;
                            float tod = _session.DayNight.TimeOfDay;
                            bool isNightOrStorm = tod >= 0.75f || tod <= 0.25f || _session.Weather == WeatherType.Thunder;
                            if (!isNightOrStorm) return;

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
                            if (cDim > (byte)Dimension.Void || (Dimension)cDim != client.Dimension) return;
                            var targetWorld = _session.GetWorld((Dimension)cDim);
                            var cPos = new Vec3i(cx, cy, cz);
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
                            if (fDim > (byte)Dimension.Void || (Dimension)fDim != client.Dimension) return;
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
                            if (fDim > (byte)Dimension.Void || (Dimension)fDim != client.Dimension) return;
                            var targetWorld = _session.GetWorld((Dimension)fDim);
                            if (targetWorld == null) return;
                            var fPos = new Vec3i(fx, fy, fz);
                            if (!CanReachContainer(targetWorld, client, fPos, GameData.BFurnace.Id)) return;
                            var f = targetWorld.GetOrCreateFurnace(fPos);

                            var oldCounts = new Dictionary<ushort, int>();
                            void CountSlot(ItemEntry? s) {
                                if (s is { } se && se.Quantity > 0) oldCounts[se.Item.Definition.Id] = oldCounts.GetValueOrDefault(se.Item.Definition.Id) + se.Quantity;
                            }
                            CountSlot(f.Input);
                            CountSlot(f.Fuel);
                            CountSlot(f.Output);

                            f.FuelTimer = float.IsFinite(fuelTimer) ? Math.Clamp(fuelTimer, 0f, 600f) : 0f;
                            f.MaxFuelTimer = float.IsFinite(maxFuelTimer) ? Math.Clamp(maxFuelTimer, 0f, 600f) : 0f;
                            f.SmeltTimer = float.IsFinite(smeltTimer) ? Math.Clamp(smeltTimer, 0f, 600f) : 0f;

                            if (outItem.HasValue) {
                                if (f.Output.HasValue && f.Output.Value.Item.Definition.Id == outItem.Value.Item.Definition.Id) {
                                    int allowedQty = Math.Min(f.Output.Value.Quantity, outItem.Value.Quantity);
                                    f.Output = allowedQty > 0 ? f.Output.Value with { Quantity = allowedQty } : null;
                                }
                            } else {
                                f.Output = null;
                            }

                            var playerInv = client.PlayerData.Inventory;
                            if (inItem.HasValue) {
                                ushort inId = inItem.Value.Item.Definition.Id;
                                int prevIn = (f.Input.HasValue && f.Input.Value.Item.Definition.Id == inId) ? f.Input.Value.Quantity : 0;
                                int availIn = prevIn + playerInv.CountOf(inItem.Value.Item.Definition);
                                int allowedIn = Math.Clamp(inItem.Value.Quantity, 0, Math.Min(inItem.Value.Item.Definition.MaxStack, availIn));
                                f.Input = allowedIn > 0 ? inItem.Value with { Quantity = allowedIn } : null;
                            } else {
                                f.Input = null;
                            }

                            if (fItem.HasValue) {
                                ushort fId = fItem.Value.Item.Definition.Id;
                                int prevF = (f.Fuel.HasValue && f.Fuel.Value.Item.Definition.Id == fId) ? f.Fuel.Value.Quantity : 0;
                                int availF = prevF + playerInv.CountOf(fItem.Value.Item.Definition);
                                int allowedF = Math.Clamp(fItem.Value.Quantity, 0, Math.Min(fItem.Value.Item.Definition.MaxStack, availF));
                                f.Fuel = allowedF > 0 ? fItem.Value with { Quantity = allowedF } : null;
                            } else {
                                f.Fuel = null;
                            }

                            var newCounts = new Dictionary<ushort, int>();
                            void CountNewSlot(ItemEntry? s) {
                                if (s is { } se && se.Quantity > 0) newCounts[se.Item.Definition.Id] = newCounts.GetValueOrDefault(se.Item.Definition.Id) + se.Quantity;
                            }
                            CountNewSlot(f.Input);
                            CountNewSlot(f.Fuel);
                            CountNewSlot(f.Output);

                            var allIds = new HashSet<ushort>(oldCounts.Keys);
                            foreach (var k in newCounts.Keys) allIds.Add(k);
                            foreach (ushort id in allIds) {
                                int delta = oldCounts.GetValueOrDefault(id, 0) - newCounts.GetValueOrDefault(id, 0);
                                AdjustPlayerInventoryDelta(playerInv, id, delta);
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
                        int attackerId = clientId;
                        reader.ReadInt32();
                        int targetId = reader.ReadInt32();
                        float ignoredClientDmg = reader.ReadSingle();
                        var now = DateTime.UtcNow;

                        EnqueueMainThreadAction(() => {
                            if ((now - client.LastAttackTime).TotalSeconds < 0.2) return;
                            client.LastAttackTime = now;
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
                            } else if (_clients.TryGetValue(targetId, out var targetClient)) {
                                if (targetClient.Dimension == client.Dimension && Vector3.Distance(client.Position, targetClient.Position) <= 6.0f) {
                                    targetClient.Health = Math.Max(0f, targetClient.Health - validDmg);
                                    targetClient.PlayerData.Health = targetClient.Health;
                                    targetClient.HurtTimer = 1.0f;
                                    var hitPacket = NetworkProtocol.WritePlayerHit(attackerId, targetId, validDmg);
                                    Broadcast(hitPacket, exceptClientId: clientId);
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
                            if (dim > (byte)Dimension.Void || (Dimension)dim != client.Dimension) return;
                            var targetWorld = _session.GetWorld((Dimension)dim);
                            if (targetWorld == null) return;
                            var cell = new Vec3i(bx, by, bz);
                            var blockCenter = new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f);

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
                                bool hasItem = false;
                                ushort requiredItemId;
                                if (_session.GameMode == GameMode.Creative) {
                                    hasItem = true;
                                    requiredItemId = typeId;
                                } else {
                                    bool isPairedDoorUpper = typeId == GameData.BDoorUpper.Id &&
                                        targetWorld.GetVoxel(cell).TypeId == 0 &&
                                        targetWorld.GetVoxel(cell + new Vec3i(0, -1, 0)).TypeId == GameData.BDoorLower.Id;
                                    bool isPairedBedHead = typeId == GameData.BBedHead.Id &&
                                        targetWorld.GetVoxel(cell).TypeId == 0 &&
                                        (targetWorld.GetVoxel(cell + new Vec3i(1, 0, 0)).TypeId == GameData.BBed.Id ||
                                         targetWorld.GetVoxel(cell + new Vec3i(-1, 0, 0)).TypeId == GameData.BBed.Id ||
                                         targetWorld.GetVoxel(cell + new Vec3i(0, 0, 1)).TypeId == GameData.BBed.Id ||
                                         targetWorld.GetVoxel(cell + new Vec3i(0, 0, -1)).TypeId == GameData.BBed.Id);

                                    if (isPairedDoorUpper || isPairedBedHead) {
                                        hasItem = true;
                                    } else {
                                        if (typeId == GameData.BDoorLower.Id) requiredItemId = GameData.DoorItem.Id;
                                        else if (typeId == GameData.BBed.Id) requiredItemId = GameData.BedItem.Id;
                                        else if (!GameData.TryGetItemByBlock(typeId, out requiredItemId)) requiredItemId = typeId;

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
                                    }
                                }

                                if (!hasItem) {
                                    var currentV = targetWorld.GetVoxel(cell);
                                    client.Send(NetworkProtocol.WriteBlockReject(bx, by, bz, currentV.TypeId, currentV.SubGridLayerMask, dim));
                                    client.Send(NetworkProtocol.WritePlayerInventoryUpdate(client.PlayerData, dim));
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
                        int rawCount = reader.ReadInt32();
                        if (dim > (byte)Dimension.Void || rawCount < 0 || rawCount > GameWorld.ChestCapacity) break;
                        var items = new List<(int idx, ushort id, int qty, int dur)>(rawCount);
                        for (int i = 0; i < rawCount; i++) {
                            items.Add((reader.ReadInt32(), reader.ReadUInt16(), reader.ReadInt32(), reader.ReadInt32()));
                        }

                        EnqueueMainThreadAction(() => {
                            if ((Dimension)dim != client.Dimension) return;
                            var targetWorld = _session.GetWorld((Dimension)dim);
                            if (targetWorld == null) return;
                            var cPos = new Vec3i(cx, cy, cz);
                            if (!CanReachContainer(targetWorld, client, cPos, GameData.BChest.Id)) return;
                            var chest = targetWorld.GetOrCreateChest(cPos);

                            var playerInv = client.PlayerData.Inventory;
                            var oldChestCounts = new Dictionary<ushort, int>();
                            var available = new Dictionary<ushort, int>();
                            var durHint = new Dictionary<ushort, int>();

                            for (int i = 0; i < chest.Capacity; i++) {
                                if (chest.Slots[i] is { } ce && ce.Quantity > 0) {
                                    ushort id = ce.Item.Definition.Id;
                                    oldChestCounts[id] = oldChestCounts.GetValueOrDefault(id) + ce.Quantity;
                                    available[id] = available.GetValueOrDefault(id) + ce.Quantity;
                                    if (ce.Item.Durability > durHint.GetValueOrDefault(id)) durHint[id] = ce.Item.Durability;
                                }
                            }
                            for (int i = 0; i < playerInv.Capacity; i++) {
                                if (playerInv.Slots[i] is { } pe && pe.Quantity > 0) {
                                    ushort id = pe.Item.Definition.Id;
                                    available[id] = available.GetValueOrDefault(id) + pe.Quantity;
                                    if (pe.Item.Durability > durHint.GetValueOrDefault(id)) durHint[id] = pe.Item.Durability;
                                }
                            }

                            var newChestCounts = new Dictionary<ushort, int>();
                            for (int i = 0; i < chest.Capacity; i++) chest.RemoveAt(i);
                            foreach (var item in items) {
                                if (GameData.Items.TryGetValue(item.id, out var def)) {
                                    if (item.idx < 0 || item.idx >= chest.Capacity) continue;
                                    int maxAvail = available.GetValueOrDefault(item.id, 0);
                                    int qty = Math.Clamp(item.qty, 0, Math.Min(def.MaxStack, maxAvail));
                                    if (qty <= 0) continue;
                                    available[item.id] -= qty;
                                    newChestCounts[item.id] = newChestCounts.GetValueOrDefault(item.id) + qty;

                                    var inst = GameData.NewItem(def);
                                    int maxDur = GameData.GetMaxItemDurability(def.Id);
                                    if (maxDur > 0) inst.Durability = Math.Clamp(item.dur, 1, maxDur);
                                    chest.InsertAt(item.idx, new ItemEntry(inst, qty));
                                }
                            }

                            // Переносим дельту сундука в серверный инвентарь игрока
                            var allIds = new HashSet<ushort>(oldChestCounts.Keys);
                            foreach (var k in newChestCounts.Keys) allIds.Add(k);
                            foreach (ushort id in allIds) {
                                int delta = oldChestCounts.GetValueOrDefault(id, 0) - newChestCounts.GetValueOrDefault(id, 0);
                                AdjustPlayerInventoryDelta(playerInv, id, delta, durHint.GetValueOrDefault(id));
                            }

                            var chestPacket = NetworkProtocol.WriteChestSync(cx, cy, cz, chest, dim);
                            Broadcast(chestPacket, exceptClientId: clientId);
                        });
                        break;
                    }
                    case PacketType.ChatMessage: {
                        reader.ReadString();
                        string msg = reader.ReadString();
                        _ = reader.ReadByte(); _ = reader.ReadByte(); _ = reader.ReadByte();
                        var now = DateTime.UtcNow;
                        if (string.IsNullOrWhiteSpace(msg)) break;
                        msg = msg.Replace('\r', ' ').Replace('\n', ' ').Trim();
                        if (msg.Length > 256) msg = msg.Substring(0, 256);
                        string sender = client.Name;
                        EnqueueMainThreadAction(() => {
                            if ((now - client.LastChatTime).TotalSeconds < 0.4) return;
                            client.LastChatTime = now;
                            _session.AddChatMessage($"<{sender}> {msg}", Raylib_cs.Color.White);
                            var chatP = NetworkProtocol.WriteChatMessage(sender, msg, 255, 255, 255);
                            Broadcast(chatP, exceptClientId: clientId);
                        });
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
        if (_clients.TryGetValue(targetId, out var targetClient)) {
            targetClient.Health = Math.Max(0f, targetClient.Health - Math.Max(0f, damage));
            targetClient.PlayerData.Health = targetClient.Health;
            targetClient.HurtTimer = 1.0f;
        }
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
