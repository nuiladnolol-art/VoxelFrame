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

    private readonly object _sendLock = new();

    public ConnectedClient(int id, TcpClient socket) {
        Id = id;
        Socket = socket;
    }

    public void Send(byte[] data) {
        lock (_sendLock) {
            try {
                if (Socket.Connected) {
                    var stream = Socket.GetStream();
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                }
            } catch (Exception ex) {
                Console.WriteLine($"[GameServer Client {Id}] Ошибка отправки пакета: {ex.Message}");
            }
        }
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
        return srv;
    }

    public static void Stop() {
        if (GameWorld.OnNetworkBlockChangeBroadcast != null) {
            GameWorld.OnNetworkBlockChangeBroadcast = null;
        }
        if (GameWorld.OnFurnaceChangedBroadcast != null) {
            GameWorld.OnFurnaceChangedBroadcast = null;
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

        // Автоматический сбор пикапов для подключенных клиентов
        var world = _session.World;
        for (int i = world.Pickups.Count - 1; i >= 0; i--) {
            var p = world.Pickups[i];
            if (p.Quantity <= 0) continue;
            foreach (var cl in _clients.Values) {
                if (cl.Dimension != world.Dimension) continue;
                float d = Vector3.Distance(cl.Position, p.Position);
                if (p.PickupDelay <= 0f && d < 1.5f) {
                    var collectPacket = NetworkProtocol.WritePickupCollect(p.Id, cl.Id);
                    Broadcast(collectPacket);
                    p.Quantity = 0;
                    break;
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

                var nearAnimals = cWorld.Animals.FindAll(a => Vector3.DistanceSquared(a.Position, client.Position) < 128f * 128f);
                var nearHostiles = cWorld.HostileMobs.FindAll(h => Vector3.DistanceSquared(h.Position, client.Position) < 128f * 128f);
                if (nearAnimals.Count > 0 || nearHostiles.Count > 0) {
                    var mobPacket = NetworkProtocol.WriteMobSync((byte)cWorld.Dimension, nearAnimals, nearHostiles);
                    client.Send(mobPacket);
                }

                var nearPickups = cWorld.Pickups.FindAll(p => Vector3.DistanceSquared(p.Position, client.Position) < 128f * 128f);
                if (nearPickups.Count > 0) {
                    var pickupPacket = NetworkProtocol.WritePickupSync((byte)cWorld.Dimension, nearPickups);
                    client.Send(pickupPacket);
                }

                var nearArrows = cWorld.Arrows.FindAll(a => Vector3.DistanceSquared(a.Position, client.Position) < 128f * 128f);
                if (nearArrows.Count > 0) {
                    var projPacket = NetworkProtocol.WriteProjectileSync((byte)cWorld.Dimension, nearArrows);
                    client.Send(projPacket);
                }

                // Боссы синхронизируются только для тех, кто в Энде
                if (cWorld.Dimension == Dimension.End) {
                    if (cWorld.EndBoss is { } eb) {
                        var bossP = NetworkProtocol.WriteBossSync(0, eb.Alive, eb.Awake, eb.Health, EndSlime.MaxHealth, eb.Phase, eb.Position, eb.Velocity, eb.HurtTime, eb.SlamWarningTimer, eb.IsResting, (byte)Dimension.End);
                        client.Send(bossP);
                    }
                    if (cWorld.TrueVoidBoss is { } tb) {
                        var tbP = NetworkProtocol.WriteBossSync(1, tb.Alive, tb.Awake, tb.Health, TrueEndSlime.MaxHealth, tb.Phase, tb.Position, tb.Velocity, tb.HurtTime, tb.SingularityWarningTimer, tb.State == TrueBossState.Resting, (byte)Dimension.End);
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
            _session.AddChatMessage($"Ошибка запуска сервера: {ex.Message}", Raylib_cs.Color.Red);
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () => {
            while (!token.IsCancellationRequested && _listener != null) {
                try {
                    var socket = await _listener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleClient(socket, token), token);
                } catch {
                    break;
                }
            }
        }, token);

        _session.AddChatMessage($"Локальный сервер открыт на порту {Port}!", Raylib_cs.Color.Green);
    }

    private void HandleClient(TcpClient socket, CancellationToken token) {
        socket.NoDelay = true;
        int clientId = Interlocked.Increment(ref _nextClientId);
        var client = new ConnectedClient(clientId, socket);
        _clients[clientId] = client;

        NetworkStream stream = socket.GetStream();

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
                using var pMs = new MemoryStream(frame.Buffer, 1, frame.Length - 1, writable: false);
                using var reader = new BinaryReader(pMs, Encoding.UTF8);

                switch (frame.Type) {
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

                        // Синхронизируем текущее состояние чанков мира хоста с клиентом
                        var chunkSync = NetworkProtocol.WriteWorldChunksSync((byte)_session.World.Dimension, _session.World.Chunks);
                        client.Send(chunkSync);

                        // Первоначальная синхронизация мобов и предметов
                        if (_session.World.Animals.Count > 0 || _session.World.HostileMobs.Count > 0) {
                            var mobSync = NetworkProtocol.WriteMobSync((byte)_session.World.Dimension, _session.World.Animals, _session.World.HostileMobs);
                            client.Send(mobSync);
                        }
                        if (_session.World.Pickups.Count > 0) {
                            var pickupSync = NetworkProtocol.WritePickupSync((byte)_session.World.Dimension, _session.World.Pickups);
                            client.Send(pickupSync);
                        }

                        _session.AddChatMessage($"Игрок {name} присоединился к игре!", Raylib_cs.Color.Yellow);
                        _session.AddMessage($"Игрок {name} вошел в мир");
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
                                int craftTimes = amount; // Сколько раз скрафтили
                                if (recipeIdx >= 0 && recipeIdx < GameData.CraftRecipes.Count && craftTimes > 0) {
                                    var recipe = GameData.CraftRecipes[recipeIdx];
                                    
                                    // Проверка наличия ингредиентов
                                    bool canCraft = true;
                                    foreach (var (reqItem, reqCount) in recipe.Ingredients) {
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
                        // Пакет теперь используется только для синхронизации базовой статистики от клиента (оставлено для обратной совместимости)
                        // Инвентарь от клиента полностью игнорируется для защиты от читов.
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
                        client.Dimension = (Dimension)invDim;
                        client.PlayerData.Dimension = (Dimension)invDim;

                        client.PlayerData.Position = new Vector3(px, py, pz);
                        client.PlayerData.Yaw = yaw;
                        client.PlayerData.Pitch = pitch;
                        client.PlayerData.Health = hp;
                        client.PlayerData.Hunger = hunger;
                        client.PlayerData.Saturation = sat;
                        client.PlayerData.SelectedSlot = slot;

                        // Пропускаем остаток пакета (чтобы не сломать чтение следующих пакетов, если они есть, хотя NetworkProtocol обычно не объединяет пакеты так)
                        break;
                    }
                    case PacketType.PlayerMovement: {
                        int id = reader.ReadInt32();
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        float yaw = reader.ReadSingle();
                        float pitch = reader.ReadSingle();
                        byte flags = reader.ReadByte();
                        float hp = reader.ReadSingle();
                        byte dim = reader.ReadByte();
                        client.Dimension = (Dimension)dim;

                        var newPos = new Vector3(x, y, z);
                        // Серверная валидация перемещения: ограничение аномальных скачков позиции
                        if (client.Position == Vector3.Zero) {
                            client.Position = newPos;
                            client.TargetPosition = newPos;
                        } else {
                            float maxAllowedStep = ((flags & 4) != 0 ? 35f : 18f); // 35 м/с при полёте, 18 м/с при спринте
                            float dist = Vector3.Distance(client.Position, newPos);
                            if (dist > maxAllowedStep * 2.0f && dist > 10.0f) {
                                // Аномальный прыжок/телепорт — корректируем на сервере
                                newPos = client.Position;
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
                        client.Health = Math.Clamp(hp, 0f, 20f);

                        // Broadcast movement with actual target newPos and authenticated clientId
                        var moveP = NetworkProtocol.WritePlayerMovement(clientId, newPos, yaw, pitch, client.IsMoving, client.IsSneaking, client.IsFlying, client.IsBlocking, client.Health, dim);
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
                                var dropPos = client.Position + new Vector3(0f, 0.6f, 0f);
                                var fwd = new Vector3(-MathF.Sin(client.Yaw), 0f, MathF.Cos(client.Yaw));
                                var itemInst = GameData.NewItem(dropDef);
                                if (durability > 0) itemInst.Durability = durability;
                                var pickup = new ItemPickup(itemInst, Math.Max(1, quantity), dropPos) {
                                    PickupDelay = 1.2f,
                                    Velocity = fwd * 4.5f + new Vector3(0f, 2.0f, 0f)
                                };
                                _session.GetWorld(client.Dimension).Pickups.Add(pickup);
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
                            var targetWorld = _session.GetWorld(client.Dimension);
                            var p = targetWorld.Pickups.Find(x => x.Id == pickupId);
                            if (p != null) {
                                targetWorld.Pickups.Remove(p);
                            }
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
                            ushort toolId = (ushort)client.SelectedItemId;
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
                        int shooterId = reader.ReadInt32();
                        float px = reader.ReadSingle(), py = reader.ReadSingle(), pz = reader.ReadSingle();
                        float vx = reader.ReadSingle(), vy = reader.ReadSingle(), vz = reader.ReadSingle();
                        byte pTypeFlags = reader.ReadByte();
                        byte pDim = reader.ReadByte();
                        EnqueueMainThreadAction(() => {
                            var targetWorld = _session.GetWorld((Dimension)pDim);
                            var arr = new ArrowProjectile(new Vector3(px, py, pz), new Vector3(vx, vy, vz), null) {
                                IsSlimeSpit = (pTypeFlags & 1) != 0,
                                IsEnderPearl = (pTypeFlags & 2) != 0,
                                IsEyeOfEnder = (pTypeFlags & 4) != 0,
                                FromPlayer = true,
                                Damage = 7f
                            };
                            targetWorld.Arrows.Add(arr);
                        });
                        var spawnProjP = NetworkProtocol.WriteSpawnProjectile(shooterId, new Vector3(px, py, pz), new Vector3(vx, vy, vz), pTypeFlags, pDim);
                        Broadcast(spawnProjP, exceptClientId: clientId);
                        break;
                    }
                    case PacketType.Explosion: {
                        float ex = reader.ReadSingle(), ey = reader.ReadSingle(), ez = reader.ReadSingle();
                        float radius = Math.Clamp(reader.ReadSingle(), 0.5f, 5.0f);
                        float maxDmg = Math.Clamp(reader.ReadSingle(), 1f, 35f);
                        byte eDim = reader.ReadByte();

                        EnqueueMainThreadAction(() => {
                            if (Vector3.Distance(client.Position, new Vector3(ex, ey, ez)) <= 8.0f) {
                                var targetWorld = _session.GetWorld((Dimension)eDim);
                                GameWorld.CreateExplosion(new Vector3(ex, ey, ez), radius, maxDmg, _session);
                                var expP = NetworkProtocol.WriteExplosion(new Vector3(ex, ey, ez), radius, maxDmg, eDim);
                                Broadcast(expP, exceptClientId: clientId);
                            }
                        });
                        break;
                    }
                    case PacketType.BedSleep: {
                        int clId = reader.ReadInt32();
                        bool isSleeping = reader.ReadBoolean();
                        int bedX = reader.ReadInt32(), bedY = reader.ReadInt32(), bedZ = reader.ReadInt32();
                        EnqueueMainThreadAction(() => {
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
                            var targetWorld = _session.GetWorld((Dimension)cDim);
                            var chest = targetWorld.GetOrCreateChest(new Vec3i(cx, cy, cz));
                            var chestPacket = NetworkProtocol.WriteChestSync(cx, cy, cz, chest, cDim);
                            client.Send(chestPacket);
                        });
                        break;
                    }
                    case PacketType.RequestFurnace: {
                        int fx = reader.ReadInt32(), fy = reader.ReadInt32(), fz = reader.ReadInt32();
                        byte fDim = reader.ReadByte();
                        EnqueueMainThreadAction(() => {
                            var targetWorld = _session.GetWorld((Dimension)fDim);
                            var fPos = new Vec3i(fx, fy, fz);
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
                            if (GameData.Items.TryGetValue(id, out var def)) inItem = new ItemEntry(GameData.NewItem(def), qty);
                        }
                        ItemEntry? fItem = null;
                        if (reader.ReadBoolean()) {
                            ushort id = reader.ReadUInt16();
                            int qty = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(id, out var def)) fItem = new ItemEntry(GameData.NewItem(def), qty);
                        }
                        ItemEntry? outItem = null;
                        if (reader.ReadBoolean()) {
                            ushort id = reader.ReadUInt16();
                            int qty = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(id, out var def)) outItem = new ItemEntry(GameData.NewItem(def), qty);
                        }

                        EnqueueMainThreadAction(() => {
                            var targetWorld = _session.GetWorld((Dimension)fDim);
                            var fPos = new Vec3i(fx, fy, fz);
                            var f = targetWorld.GetOrCreateFurnace(fPos);
                            f.FuelTimer = fuelTimer;
                            f.MaxFuelTimer = maxFuelTimer;
                            f.SmeltTimer = smeltTimer;
                            f.Input = inItem;
                            f.Fuel = fItem;
                            f.Output = outItem;

                            var inT = inItem.HasValue ? (inItem.Value.Item.Definition.Id, inItem.Value.Quantity) : ((ushort, int)?)null;
                            var fuelT = fItem.HasValue ? (fItem.Value.Item.Definition.Id, fItem.Value.Quantity) : ((ushort, int)?)null;
                            var outT = outItem.HasValue ? (outItem.Value.Item.Definition.Id, outItem.Value.Quantity) : ((ushort, int)?)null;
                            var fPacket = NetworkProtocol.WriteFurnaceSync(fx, fy, fz, fuelTimer, maxFuelTimer, smeltTimer, inT, fuelT, outT, fDim);
                            Broadcast(fPacket, exceptClientId: clientId);
                        });
                        break;
                    }
                    case PacketType.PlayerHit: {
                        int attackerId = reader.ReadInt32();
                        int targetId = reader.ReadInt32();
                        float ignoredClientDmg = reader.ReadSingle();

                        ushort toolId = (ushort)client.SelectedItemId;
                        float actualDmg = GameData.GetWeaponDamage(toolId);
                        float validDmg = Math.Clamp(actualDmg, 1f, actualDmg * 1.5f + 1f);

                        if (targetId == 1) {
                            var attackerPos = client.Position;
                            EnqueueMainThreadAction(() => {
                                if (Vector3.Distance(attackerPos, _session.Player.Position) <= 6.0f) {
                                    _session.Player.ApplyDamage(validDmg, _session, attackerPos, cause: client.Name);
                                    _session.Player.HurtTimer = 1.0f;
                                    BroadcastHostAction(PlayerActionType.Hurt, 0);
                                }
                            });
                        } else {
                            if (_clients.TryGetValue(targetId, out var targetClient)) {
                                if (Vector3.Distance(client.Position, targetClient.Position) <= 6.0f) {
                                    var hitPacket = NetworkProtocol.WritePlayerHit(attackerId, targetId, validDmg);
                                    Broadcast(hitPacket, exceptClientId: clientId);
                                }
                            }
                        }
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
                                GameWorld.SuppressNetworkSync = true;
                                try {
                                    targetWorld.RemoveBlock(cell);
                                } finally {
                                    GameWorld.SuppressNetworkSync = false;
                                }
                            } else {
                                // Server-side item consumption
                                bool hasItem = false;
                                if (_session.GameMode == GameMode.Creative) {
                                    hasItem = true;
                                } else {
                                    var inv = client.PlayerData.Inventory;
                                    for (int i = 0; i < inv.Capacity; i++) {
                                        var entry = inv.Slots[i];
                                        if (entry.HasValue && entry.Value.Item.Definition.Id == typeId) {
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
                                    targetWorld.PlacePlacedBlock(cell, GameData.GetBlock(typeId), mask);
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
                        int cCount = reader.ReadInt32();
                        var items = new List<(int idx, ushort id, int qty, int dur)>();
                        for (int i = 0; i < cCount; i++) {
                            items.Add((reader.ReadInt32(), reader.ReadUInt16(), reader.ReadInt32(), reader.ReadInt32()));
                        }

                        EnqueueMainThreadAction(() => {
                            var targetWorld = _session.GetWorld((Dimension)dim);
                            var cPos = new Vec3i(cx, cy, cz);
                            var chest = targetWorld.GetOrCreateChest(cPos);
                            for (int i = 0; i < chest.Capacity; i++) chest.RemoveAt(i);
                            foreach (var item in items) {
                                if (GameData.Items.TryGetValue(item.id, out var def)) {
                                    var inst = GameData.NewItem(def);
                                    inst.Durability = item.dur;
                                    chest.InsertAt(item.idx, new ItemEntry(inst, item.qty));
                                }
                            }
                        });

                        // Broadcast to other clients
                        var chestPacket = NetworkProtocol.WriteChestSync(cx, cy, cz, _session.GetWorld((Dimension)dim).GetOrCreateChest(new Vec3i(cx, cy, cz)), dim);
                        Broadcast(chestPacket, exceptClientId: clientId);
                        break;
                    }
                    case PacketType.ChatMessage: {
                        string sender = reader.ReadString();
                        string msg = reader.ReadString();
                        byte r = reader.ReadByte();
                        byte g = reader.ReadByte();
                        byte b = reader.ReadByte();
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
            SaveSystem.SavePlayerData(client.Name, client.PlayerData);
            _session.AddChatMessage($"Игрок {client.Name} покинул игру.", Raylib_cs.Color.Yellow);
            _session.AddMessage($"Игрок {client.Name} вышел");
            var leaveP = NetworkProtocol.WritePlayerLeave(clientId);
            Broadcast(leaveP);
            try { socket.Close(); } catch { }
        }
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
            try { c.Socket.Close(); } catch { }
        }
        _clients.Clear();
        _cts?.Dispose();
        _cts = null;
    }
}
