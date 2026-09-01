using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

public sealed class RemotePlayer {
    public int Id { get; }
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
    public int OffhandItemId { get; set; }
    public int HelmetId { get; set; }
    public int ChestplateId { get; set; }
    public int LeggingsId { get; set; }
    public int BootsId { get; set; }
    public float ArmSwingTimer { get; set; }
    public float HurtTimer { get; set; }
    public string SkinName { get; set; } = "cyan";
    public Dimension Dimension { get; set; } = Dimension.Overworld;

    public RemotePlayer(int id, string name, Vector3 pos, float yaw, float pitch, Dimension dimension = Dimension.Overworld) {
        Id = id;
        Name = name;
        Position = pos;
        TargetPosition = pos;
        Yaw = yaw;
        TargetYaw = yaw;
        Pitch = pitch;
        TargetPitch = pitch;
        Dimension = dimension;
    }

    public void Update(float dt) {
        // Плавная интерполяция позиции и углов поворота
        Position = Vector3.Lerp(Position, TargetPosition, MathF.Min(1f, dt * 18f));
        Yaw = MathF.BitIncrement(Yaw) != MathF.BitIncrement(TargetYaw) ? Yaw + (TargetYaw - Yaw) * MathF.Min(1f, dt * 18f) : TargetYaw;
        Pitch = Pitch + (TargetPitch - Pitch) * MathF.Min(1f, dt * 18f);

        if (ArmSwingTimer > 0f) {
            ArmSwingTimer = MathF.Max(0f, ArmSwingTimer - dt * 3.5f);
        }
        if (HurtTimer > 0f) {
            HurtTimer = MathF.Max(0f, HurtTimer - dt * 2.5f);
        }
    }
}

public sealed class GameClient : IDisposable {
    public static GameClient? Active { get; private set; }

    private TcpClient? _socket;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private GameSession? _session;

    public int LocalClientId { get; private set; } = -1;
    public bool IsConnected => _socket is { Connected: true };
    public string Host { get; }
    public int Port { get; }
    public string PlayerName { get; }

    public int ReceivedSeed { get; private set; } = 1337;
    public float ReceivedTimeOfDay { get; private set; } = 0.25f;
    public bool ReceivedCheats { get; private set; } = false;
    public int ReceivedGamemode { get; private set; } = 0;
    public bool ReceivedKeepInventory { get; private set; } = false;
    public bool HasReceivedWelcome { get; private set; } = false;
    public bool HasReceivedPlayerData { get; private set; } = false;
    public Player? InitialPlayerData { get; private set; }
    public bool WasDisconnected { get; set; } = false;

    private readonly ConcurrentDictionary<int, RemotePlayer> _remotePlayers = new();
    public IReadOnlyCollection<RemotePlayer> RemotePlayers => _remotePlayers.Values.ToArray();

    private readonly object _sendLock = new();
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

    public void EnqueueMainThreadAction(Action action) {
        _mainThreadActions.Enqueue(action);
    }

    public void ProcessMainThreadActions() {
        while (_mainThreadActions.TryDequeue(out var action)) {
            try {
                action();
            } catch { }
        }
    }

    public GameClient(string host, int port, string playerName) {
        Host = host;
        Port = port;
        PlayerName = playerName;
    }

    public static async Task<GameClient> ConnectAsync(string host, int port, string playerName) {
        Active?.Dispose();
        var client = new GameClient(host, port, playerName);
        await client.StartAsync();
        Active = client;
        return client;
    }

    public void BindSession(GameSession session) {
        _session = session;
        GameWorld.OnNetworkBlockChangeBroadcast = (pos, typeId, mask, isBreak, dim) => {
            SendBlockChange(pos.X, pos.Y, pos.Z, typeId, mask, isBreak, (byte)dim);
        };
        if (InitialPlayerData != null) {
            ApplyPlayerDataToSession(session, InitialPlayerData);
        }
    }

    public static void Disconnect(Player? player = null) {
        Active?.DisconnectAndNotify(player);
        Active = null;
    }

    public void DisconnectAndNotify(Player? player = null) {
        try {
            if (player != null) {
                SendInventoryUpdate(player);
            }
            var disc = NetworkProtocol.WriteDisconnect("User disconnected");
            SendPacket(disc);
            Thread.Sleep(100);
        } catch { }
        Dispose();
    }

    private async Task StartAsync() {
        _socket = new TcpClient { NoDelay = true };
        await _socket.ConnectAsync(Host, Port);
        _stream = _socket.GetStream();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Отправка приветственного Handshake с передачей скина
        var handshake = NetworkProtocol.WriteHandshake(PlayerName, "1.0.0", _session?.Player.SkinName ?? "cyan");
        await _stream.WriteAsync(handshake, token);

        _ = Task.Run(() => ReceiveLoop(token), token);
    }

    private void ReceiveLoop(CancellationToken token) {
        try {
            while (!token.IsCancellationRequested && _socket is { Connected: true }) {
                var frameNullable = NetworkProtocol.ReadPacketFrame(_stream!);
                if (!frameNullable.HasValue) break;
                
                using var frame = frameNullable.Value;
                using var pMs = new MemoryStream(frame.Buffer, 1, frame.Length - 1, writable: false);
                using var reader = new BinaryReader(pMs, Encoding.UTF8);

                switch (frame.Type) {
                    case PacketType.Welcome: {
                        LocalClientId = reader.ReadInt32();
                        ReceivedSeed = reader.ReadInt32();
                        ReceivedTimeOfDay = reader.ReadSingle();
                        ReceivedCheats = reader.ReadBoolean();
                        ReceivedGamemode = reader.ReadInt32();
                        ReceivedKeepInventory = reader.ReadBoolean();
                        if (_session != null) {
                            _session.KeepInventory = ReceivedKeepInventory;
                        }
                        HasReceivedWelcome = true;
                        break;
                    }
                    case PacketType.PlayerJoin: {
                        int id = reader.ReadInt32();
                        string name = reader.ReadString();
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        float yaw = reader.ReadSingle();
                        float pitch = reader.ReadSingle();
                        byte dim = reader.ReadByte();
                        string skin = "cyan";
                        try { skin = reader.ReadString(); } catch { }

                        if (id != LocalClientId) {
                            var rp = new RemotePlayer(id, name, new Vector3(x, y, z), yaw, pitch, (Dimension)dim);
                            rp.SkinName = skin;
                            _remotePlayers[id] = rp;
                            EnqueueMainThreadAction(() => {
                                _session?.AddChatMessage($"Игрок {name} вошёл в мир.", Raylib_cs.Color.Yellow);
                                _session?.AddMessage($"Игрок {name} вошёл в мир");
                            });
                        }
                        break;
                    }
                    case PacketType.PlayerLeave: {
                        int id = reader.ReadInt32();
                        if (_remotePlayers.TryRemove(id, out var rp)) {
                            EnqueueMainThreadAction(() => {
                                _session?.AddChatMessage($"Игрок {rp.Name} вышел из игры.", Raylib_cs.Color.Yellow);
                                _session?.AddMessage($"Игрок {rp.Name} вышел");
                            });
                        }
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

                        if (id != LocalClientId && _remotePlayers.TryGetValue(id, out var rp)) {
                            var newPos = new Vector3(x, y, z);
                            if (Vector3.DistanceSquared(rp.Position, newPos) > 400f || rp.Position == Vector3.Zero) {
                                rp.Position = newPos;
                            }
                            rp.TargetPosition = newPos;
                            rp.TargetYaw = yaw;
                            rp.TargetPitch = pitch;
                            rp.IsMoving = (flags & 1) != 0;
                            rp.IsSneaking = (flags & 2) != 0;
                            rp.IsFlying = (flags & 4) != 0;
                            rp.IsBlocking = (flags & 8) != 0;
                            rp.Health = hp;
                            rp.Dimension = (Dimension)dim;
                        }
                        break;
                    }
                    case PacketType.PlayerAction: {
                        int id = reader.ReadInt32();
                        var action = (PlayerActionType)reader.ReadByte();
                        int itemId = reader.ReadInt32();

                        if (id != LocalClientId && _remotePlayers.TryGetValue(id, out var rp)) {
                            rp.SelectedItemId = itemId;
                            if (action == PlayerActionType.SwingArm) {
                                rp.ArmSwingTimer = 1.0f;
                            } else if (action == PlayerActionType.Hurt) {
                                rp.HurtTimer = 1.0f;
                            }
                        }
                        break;
                    }
                    case PacketType.PlayerHit: {
                        int attackerId = reader.ReadInt32();
                        int targetId = reader.ReadInt32();
                        float dmg = reader.ReadSingle();

                        if (targetId == LocalClientId) {
                            EnqueueMainThreadAction(() => {
                                if (_session != null) {
                                    Vector3? attackerPos = null;
                                    if (_remotePlayers.TryGetValue(attackerId, out var att)) attackerPos = att.Position;
                                    _session.Player.ApplyDamage(dmg, _session, attackerPos, cause: "pvp");
                                    _session.Player.HurtTimer = 1.0f;
                                    SendAction(PlayerActionType.Hurt, 0);
                                }
                            });
                        } else if (_remotePlayers.TryGetValue(targetId, out var targetRp)) {
                            targetRp.HurtTimer = 1.0f;
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
                            if (_session != null) {
                                GameWorld.SuppressNetworkSync = true;
                                try {
                                    var targetWorld = _session.GetWorld((Dimension)dim);
                                    var cell = new Vec3i(bx, by, bz);
                                    var cellPos = new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f);
                                    if (isBreak) {
                                        targetWorld.RemoveBlock(cell);
                                        if (_session.World.Dimension == (Dimension)dim) SoundSystem.PlayDigAt(cellPos, typeId);
                                    } else {
                                        targetWorld.PlacePlacedBlock(cell, GameData.GetBlock(typeId), mask);
                                        if (_session.World.Dimension == (Dimension)dim) SoundSystem.PlayPlaceAt(cellPos);
                                    }
                                } finally {
                                    GameWorld.SuppressNetworkSync = false;
                                }
                            }
                        });
                        break;
                    }
                    case PacketType.ChatMessage: {
                        string sender = reader.ReadString();
                        string msg = reader.ReadString();
                        byte r = reader.ReadByte();
                        byte g = reader.ReadByte();
                        byte b = reader.ReadByte();
                        EnqueueMainThreadAction(() => {
                            _session?.AddChatMessage($"<{sender}> {msg}", new Raylib_cs.Color(r, g, b, (byte)255));
                        });
                        break;
                    }
                    case PacketType.PlayerDataSync: {
                        float px = reader.ReadSingle();
                        float py = reader.ReadSingle();
                        float pz = reader.ReadSingle();
                        float yaw = reader.ReadSingle();
                        float pitch = reader.ReadSingle();
                        float hp = reader.ReadSingle();
                        float hunger = reader.ReadSingle();
                        float sat = reader.ReadSingle();
                        int slot = reader.ReadInt32();
                        byte pDim = reader.ReadByte();

                        var pData = new Player {
                            Position = new Vector3(px, py, pz),
                            Yaw = yaw,
                            Pitch = pitch,
                            Health = hp,
                            Hunger = hunger,
                            Saturation = sat,
                            SelectedSlot = slot,
                            Dimension = (Dimension)pDim
                        };

                        int invCount = reader.ReadInt32();
                        for (int i = 0; i < invCount; i++) {
                            int idx = reader.ReadInt32();
                            ushort itId = reader.ReadUInt16();
                            int itQty = reader.ReadInt32();
                            int itDur = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(itId, out var itDef)) {
                                var itemInst = GameData.NewItem(itDef);
                                itemInst.Durability = itDur;
                                pData.Inventory.InsertAt(idx, new ItemEntry(itemInst, itQty));
                            }
                        }

                        if (reader.ReadBoolean()) {
                            ushort offId = reader.ReadUInt16();
                            int offQty = reader.ReadInt32();
                            int offDur = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(offId, out var offDef)) {
                                var offInst = GameData.NewItem(offDef);
                                offInst.Durability = offDur;
                                pData.OffhandEntry = new ItemEntry(offInst, offQty);
                            }
                        }

                        for (int a = 0; a < 4; a++) {
                            if (reader.ReadBoolean()) {
                                ushort armId = reader.ReadUInt16();
                                int armQty = reader.ReadInt32();
                                int armDur = reader.ReadInt32();
                                if (GameData.Items.TryGetValue(armId, out var armDef)) {
                                    var armInst = GameData.NewItem(armDef);
                                    armInst.Durability = armDur;
                                    pData.Armor[a] = new ItemEntry(armInst, armQty);
                                }
                            }
                        }

                        InitialPlayerData = pData;
                        HasReceivedPlayerData = true;

                        EnqueueMainThreadAction(() => {
                            if (_session != null) {
                                ApplyPlayerDataToSession(_session, pData);
                            }
                        });
                        break;
                    }
                    case PacketType.WorldChunksSync: {
                        byte dim = reader.ReadByte();
                        int compLen = reader.ReadInt32();
                        byte[] compBytes = reader.ReadBytes(compLen);
                        using var memGz = new MemoryStream(compBytes);
                        using var gz = new GZipStream(memGz, CompressionMode.Decompress);
                        using var brGz = new BinaryReader(gz, Encoding.UTF8);
                        int chunkCount = brGz.ReadInt32();

                        var loadedChunks = new List<(Vec3i Coord, ushort[] Types, Dictionary<int, byte>? Masks)>();
                        for (int c = 0; c < chunkCount; c++) {
                            var cc = new Vec3i(brGz.ReadInt32(), brGz.ReadInt32(), brGz.ReadInt32());
                            var types = new ushort[Chunk.VoxelCount];
                            byte[] chunkBytes = brGz.ReadBytes(Chunk.VoxelCount * sizeof(ushort));
                            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(chunkBytes).CopyTo(types);
                            int maskCount = brGz.ReadInt32();
                            Dictionary<int, byte>? masks = null;
                            if (maskCount > 0) {
                                masks = new Dictionary<int, byte>();
                                for (int m = 0; m < maskCount; m++) {
                                    int idx = brGz.ReadInt32();
                                    byte mask = brGz.ReadByte();
                                    masks[idx] = mask;
                                }
                            }
                            loadedChunks.Add((cc, types, masks));
                        }

                        EnqueueMainThreadAction(() => {
                            if (_session != null) {
                                var targetWorld = _session.GetWorld((Dimension)dim);
                                foreach (var (cc, types, masks) in loadedChunks) {
                                    targetWorld.LoadChunk(cc, types, masks);
                                }
                            }
                        });
                        break;
                    }
                    case PacketType.MobSync: {
                        byte dim = reader.ReadByte();
                        int animCount = reader.ReadInt32();
                        var syncdAnimals = new List<(uint id, AnimalType type, Vector3 pos, Vector3 vel, float hp, float hurt, bool baby, bool love)>();
                        for (int i = 0; i < animCount; i++) {
                            uint id = reader.ReadUInt32();
                            var aType = (AnimalType)reader.ReadByte();
                            var pos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            var vel = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            float hp = reader.ReadSingle();
                            float hurt = reader.ReadSingle();
                            bool isBaby = reader.ReadBoolean();
                            bool inLove = reader.ReadBoolean();
                            syncdAnimals.Add((id, aType, pos, vel, hp, hurt, isBaby, inLove));
                        }

                        int hostCount = reader.ReadInt32();
                        var syncdHostiles = new List<(uint id, HostileType type, Vector3 pos, Vector3 vel, float hp, float hurt, float fuse)>();
                        for (int i = 0; i < hostCount; i++) {
                            uint id = reader.ReadUInt32();
                            var hType = (HostileType)reader.ReadByte();
                            var pos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            var vel = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            float hp = reader.ReadSingle();
                            float hurt = reader.ReadSingle();
                            float fuse = reader.ReadSingle();
                            syncdHostiles.Add((id, hType, pos, vel, hp, hurt, fuse));
                        }

                        EnqueueMainThreadAction(() => {
                            if (_session != null && _session.World.Dimension == (Dimension)dim) {
                                var world = _session.World;

                                // Update Animals smoothly without clearing
                                var aliveAnimIds = new HashSet<uint>();
                                foreach (var aData in syncdAnimals) {
                                    aliveAnimIds.Add(aData.id);
                                    var existing = world.Animals.Find(a => a.Id == aData.id);
                                    if (existing != null) {
                                        existing.Position = Vector3.Lerp(existing.Position, aData.pos, 0.4f);
                                        existing.Velocity = aData.vel;
                                        existing.Health = aData.hp;
                                        existing.HurtTime = aData.hurt;
                                        existing.IsBaby = aData.baby;
                                        existing.LoveTimer = aData.love ? 20f : 0f;
                                    } else {
                                        var newAnim = new Animal(aData.type, aData.pos) {
                                            Id = aData.id,
                                            Velocity = aData.vel,
                                            Health = aData.hp,
                                            HurtTime = aData.hurt,
                                            IsBaby = aData.baby,
                                            LoveTimer = aData.love ? 20f : 0f
                                        };
                                        world.Animals.Add(newAnim);
                                    }
                                }
                                world.Animals.RemoveAll(a => !aliveAnimIds.Contains(a.Id));

                                // Update Hostiles smoothly without clearing
                                var aliveHostIds = new HashSet<uint>();
                                foreach (var hData in syncdHostiles) {
                                    aliveHostIds.Add(hData.id);
                                    var existing = world.HostileMobs.Find(m => m.Id == hData.id);
                                    if (existing != null) {
                                        existing.Position = Vector3.Lerp(existing.Position, hData.pos, 0.4f);
                                        existing.Velocity = hData.vel;
                                        existing.Health = hData.hp;
                                        existing.HurtTime = hData.hurt;
                                        existing.FuseTimer = hData.fuse;
                                    } else {
                                        var newMob = new HostileMob(hData.type, hData.pos) {
                                            Id = hData.id,
                                            Velocity = hData.vel,
                                            Health = hData.hp,
                                            HurtTime = hData.hurt,
                                            FuseTimer = hData.fuse
                                        };
                                        world.HostileMobs.Add(newMob);
                                    }
                                }
                                world.HostileMobs.RemoveAll(m => !aliveHostIds.Contains(m.Id));
                            }
                        });
                        break;
                    }
                    case PacketType.PickupSync: {
                        byte dim = reader.ReadByte();
                        int pCount = reader.ReadInt32();
                        var syncdPickups = new List<(uint id, ushort itId, int qty, int dur, Vector3 pos, Vector3 vel, float bob)>();
                        for (int i = 0; i < pCount; i++) {
                            uint id = reader.ReadUInt32();
                            ushort itId = reader.ReadUInt16();
                            int qty = reader.ReadInt32();
                            int dur = reader.ReadInt32();
                            var pos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            var vel = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            float bob = reader.ReadSingle();
                            syncdPickups.Add((id, itId, qty, dur, pos, vel, bob));
                        }

                        EnqueueMainThreadAction(() => {
                            if (_session != null && _session.World.Dimension == (Dimension)dim) {
                                var world = _session.World;
                                var aliveIds = new HashSet<uint>();
                                foreach (var pData in syncdPickups) {
                                    aliveIds.Add(pData.id);
                                    var existing = world.Pickups.Find(p => p.Id == pData.id);
                                    if (existing != null) {
                                        existing.Position = Vector3.Lerp(existing.Position, pData.pos, 0.5f);
                                        existing.Velocity = pData.vel;
                                        existing.Quantity = pData.qty;
                                    } else if (GameData.Items.TryGetValue(pData.itId, out var def)) {
                                        var inst = GameData.NewItem(def);
                                        if (pData.dur > 0) inst.Durability = pData.dur;
                                        var pk = new ItemPickup(inst, pData.qty, pData.pos) {
                                            Id = pData.id,
                                            Velocity = pData.vel,
                                            BobPhase = pData.bob
                                        };
                                        world.Pickups.Add(pk);
                                    }
                                }
                                world.Pickups.RemoveAll(p => !aliveIds.Contains(p.Id));
                            }
                        });
                        break;
                    }
                    case PacketType.PickupCollect: {
                        uint pickupId = reader.ReadUInt32();
                        int collectorId = reader.ReadInt32();
                        EnqueueMainThreadAction(() => {
                            if (_session != null) {
                                var p = _session.World.Pickups.Find(x => x.Id == pickupId);
                                if (p != null) {
                                    _session.World.Pickups.Remove(p);
                                    if (collectorId != LocalClientId) {
                                        SoundSystem.PlayPop();
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
                        byte flags = reader.ReadByte();
                        byte dim = reader.ReadByte();
                        EnqueueMainThreadAction(() => {
                            if (_session != null && _session.World.Dimension == (Dimension)dim) {
                                var arr = new ArrowProjectile(new Vector3(px, py, pz), new Vector3(vx, vy, vz), null) {
                                    IsSlimeSpit = (flags & 1) != 0,
                                    IsEnderPearl = (flags & 2) != 0,
                                    IsEyeOfEnder = (flags & 4) != 0,
                                    FromPlayer = true,
                                    Damage = 7f
                                };
                                _session.World.Arrows.Add(arr);
                            }
                        });
                        break;
                    }
                    case PacketType.Explosion: {
                        float ex = reader.ReadSingle(), ey = reader.ReadSingle(), ez = reader.ReadSingle();
                        float radius = reader.ReadSingle();
                        float maxDmg = reader.ReadSingle();
                        byte dim = reader.ReadByte();
                        EnqueueMainThreadAction(() => {
                            if (_session != null && _session.World.Dimension == (Dimension)dim) {
                                GameWorld.CreateExplosion(new Vector3(ex, ey, ez), radius, maxDmg, _session);
                            }
                        });
                        break;
                    }
                    case PacketType.ProjectileSync: {
                        byte dim = reader.ReadByte();
                        int arrCount = reader.ReadInt32();
                        var syncdArrows = new List<ArrowProjectile>();
                        for (int i = 0; i < arrCount; i++) {
                            var pos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            var vel = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            byte flags = reader.ReadByte();
                            var arr = new ArrowProjectile(pos, vel) {
                                IsSlimeSpit = (flags & 1) != 0,
                                IsEnderPearl = (flags & 2) != 0,
                                IsEyeOfEnder = (flags & 4) != 0
                            };
                            syncdArrows.Add(arr);
                        }

                        EnqueueMainThreadAction(() => {
                            if (_session != null && _session.World.Dimension == (Dimension)dim) {
                                _session.World.Arrows.Clear();
                                _session.World.Arrows.AddRange(syncdArrows);
                            }
                        });
                        break;
                    }
                    case PacketType.Teleport: {
                        float tpx = reader.ReadSingle();
                        float tpy = reader.ReadSingle();
                        float tpz = reader.ReadSingle();
                        EnqueueMainThreadAction(() => {
                            if (_session != null) {
                                _session.Player.Position = new Vector3(tpx, tpy, tpz);
                                _session.Player.Velocity = Vector3.Zero;
                                _session.AddChatMessage($"Вы были телепортированы на X:{tpx:F1} Y:{tpy:F1} Z:{tpz:F1}", Raylib_cs.Color.Green);
                            }
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
                        int invCount = reader.ReadInt32();
                        
                        var newInvSlots = new Dictionary<int, ItemEntry>();
                        for (int i = 0; i < invCount; i++) {
                            int idx = reader.ReadInt32();
                            ushort itId = reader.ReadUInt16();
                            int itQty = reader.ReadInt32();
                            int itDur = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(itId, out var itDef)) {
                                var itemInst = GameData.NewItem(itDef);
                                itemInst.Durability = itDur;
                                newInvSlots[idx] = new ItemEntry(itemInst, itQty);
                            }
                        }

                        bool hasOffhand = reader.ReadBoolean();
                        ItemEntry? newOffhand = null;
                        if (hasOffhand) {
                            ushort offId = reader.ReadUInt16();
                            int offQty = reader.ReadInt32();
                            int offDur = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(offId, out var offDef)) {
                                var offInst = GameData.NewItem(offDef);
                                offInst.Durability = offDur;
                                newOffhand = new ItemEntry(offInst, offQty);
                            }
                        }

                        var newArmor = new ItemEntry?[4];
                        for (int a = 0; a < 4; a++) {
                            if (reader.ReadBoolean()) {
                                ushort armId = reader.ReadUInt16();
                                int armQty = reader.ReadInt32();
                                int armDur = reader.ReadInt32();
                                if (GameData.Items.TryGetValue(armId, out var armDef)) {
                                    var armInst = GameData.NewItem(armDef);
                                    armInst.Durability = armDur;
                                    newArmor[a] = new ItemEntry(armInst, armQty);
                                }
                            }
                        }

                        EnqueueMainThreadAction(() => {
                            if (_session != null) {
                                // Sync basic stats
                                _session.Player.Health = hp;
                                _session.Player.Hunger = hunger;
                                _session.Player.Saturation = sat;
                                _session.Player.SelectedSlot = slot;
                                
                                // Sync inventory
                                for (int i = 0; i < _session.Player.Inventory.Capacity; i++) {
                                    _session.Player.Inventory.RemoveAt(i);
                                }
                                foreach (var kvp in newInvSlots) {
                                    _session.Player.Inventory.InsertAt(kvp.Key, kvp.Value);
                                }
                                
                                _session.Player.OffhandEntry = newOffhand;
                                for (int a = 0; a < 4; a++) {
                                    _session.Player.Armor[a] = newArmor[a];
                                }
                            }
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
                            if (_session != null) {
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
                            }
                        });
                        break;
                    }
                    case PacketType.TimeWeatherSync: {
                        float tod = reader.ReadSingle();
                        int weather = reader.ReadInt32();
                        EnqueueMainThreadAction(() => {
                            if (_session != null) {
                                _session.DayNight.TimeOfDay = tod;
                                _session.Weather = (WeatherType)weather;
                            }
                        });
                        break;
                    }
                    case PacketType.GameRuleSync: {
                        string rule = reader.ReadString();
                        bool val = reader.ReadBoolean();
                        if (rule.Equals("keepInventory", StringComparison.OrdinalIgnoreCase)) {
                            ReceivedKeepInventory = val;
                            EnqueueMainThreadAction(() => {
                                if (_session != null) {
                                    _session.KeepInventory = val;
                                    _session.AddChatMessage($"Игровое правило keepInventory изменено сервером на {val}", Raylib_cs.Color.Yellow);
                                }
                            });
                        }
                        break;
                    }
                    case PacketType.FurnaceSync: {
                        int fx = reader.ReadInt32();
                        int fy = reader.ReadInt32();
                        int fz = reader.ReadInt32();
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
                            if (_session != null) {
                                var targetWorld = _session.GetWorld((Dimension)fDim);
                                var f = targetWorld.GetOrCreateFurnace(new Vec3i(fx, fy, fz));
                                f.FuelTimer = fuelTimer;
                                f.MaxFuelTimer = maxFuelTimer;
                                f.SmeltTimer = smeltTimer;
                                f.Input = inItem;
                                f.Fuel = fItem;
                                f.Output = outItem;
                            }
                        });
                        break;
                    }
                    case PacketType.BossSync: {
                        byte bossType = reader.ReadByte();
                        bool alive = reader.ReadBoolean();
                        bool awake = reader.ReadBoolean();
                        float hp = reader.ReadSingle();
                        float maxHp = reader.ReadSingle();
                        int phase = reader.ReadInt32();
                        float px = reader.ReadSingle(), py = reader.ReadSingle(), pz = reader.ReadSingle();
                        float vx = reader.ReadSingle(), vy = reader.ReadSingle(), vz = reader.ReadSingle();
                        float hurt = reader.ReadSingle();
                        float slam = reader.ReadSingle();
                        bool resting = reader.ReadBoolean();
                        byte bDim = reader.ReadByte();

                        EnqueueMainThreadAction(() => {
                            if (_session != null && _session.World.Dimension == (Dimension)bDim) {
                                var world = _session.World;
                                if (bossType == 0) {
                                    if (world.EndBoss == null && alive) {
                                        float islandTop = world.Generator.EndSurfaceHeight(0, 0);
                                        world.EndBoss = new EndSlime(new Vector3(px, py, pz), new Vector3(0.5f, islandTop, 0.5f), world.Seed);
                                    }
                                    if (world.EndBoss != null) {
                                        world.EndBoss.Alive = alive;
                                        world.EndBoss.Awake = awake;
                                        world.EndBoss.Health = hp;
                                        world.EndBoss.Position = Vector3.Lerp(world.EndBoss.Position, new Vector3(px, py, pz), 0.5f);
                                        world.EndBoss.Velocity = new Vector3(vx, vy, vz);
                                        world.EndBoss.HurtTime = hurt;
                                        world.EndBoss.SlamWarningTimer = slam;
                                        world.EndBoss.IsResting = resting;
                                    }
                                } else if (bossType == 1) {
                                    if (world.TrueVoidBoss == null && alive) {
                                        world.TrueVoidBoss = new TrueEndSlime(new Vector3(px, py, pz), new Vector3(0.5f, 11f, 0.5f), world.Seed);
                                    }
                                    if (world.TrueVoidBoss != null) {
                                        world.TrueVoidBoss.Alive = alive;
                                        world.TrueVoidBoss.Awake = awake;
                                        world.TrueVoidBoss.Health = hp;
                                        world.TrueVoidBoss.Position = Vector3.Lerp(world.TrueVoidBoss.Position, new Vector3(px, py, pz), 0.5f);
                                        world.TrueVoidBoss.Velocity = new Vector3(vx, vy, vz);
                                        world.TrueVoidBoss.HurtTime = hurt;
                                        world.TrueVoidBoss.SingularityWarningTimer = slam;
                                        if (resting) world.TrueVoidBoss.State = TrueBossState.Resting;
                                    }
                                }
                            }
                        });
                        break;
                    }
                    case PacketType.BlockReject: {
                        int rx = reader.ReadInt32();
                        int ry = reader.ReadInt32();
                        int rz = reader.ReadInt32();
                        ushort actualType = reader.ReadUInt16();
                        byte actualMask = reader.ReadByte();
                        byte rDim = reader.ReadByte();

                        EnqueueMainThreadAction(() => {
                            if (_session != null) {
                                var targetWorld = _session.GetWorld((Dimension)rDim);
                                var cell = new Vec3i(rx, ry, rz);
                                GameWorld.SuppressNetworkSync = true;
                                try {
                                    if (actualType == 0) {
                                        targetWorld.RemoveBlock(cell);
                                    } else {
                                        targetWorld.PlacePlacedBlock(cell, GameData.GetBlock(actualType), actualMask);
                                    }
                                } finally {
                                    GameWorld.SuppressNetworkSync = false;
                                }
                            }
                        });
                        break;
                    }
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"[GameClient] Ошибка в цикле сокета: {ex.Message}");
        } finally {
            WasDisconnected = true;
            EnqueueMainThreadAction(() => {
                _session?.AddChatMessage("Соединение с сервером разорвано.", Raylib_cs.Color.Red);
                _session?.AddMessage("Соединение с сервером разорвано");
            });
        }
    }

    public void UpdateRemotePlayers(float dt) {
        ProcessMainThreadActions();
        foreach (var rp in _remotePlayers.Values) {
            rp.Update(dt);
        }
    }

    public void SendPacket(byte[] data) {
        if (!IsConnected || _stream == null) return;
        lock (_sendLock) {
            try {
                if (_socket is { Connected: true }) {
                    _stream.Write(data, 0, data.Length);
                    _stream.Flush();
                }
            } catch { }
        }
    }

    public void SendMovement(Vector3 pos, float yaw, float pitch, bool isMoving, bool isSneaking, bool isFlying, bool isBlocking, float health, byte dimension = 0) {
        var data = NetworkProtocol.WritePlayerMovement(LocalClientId, pos, yaw, pitch, isMoving, isSneaking, isFlying, isBlocking, health, dimension);
        SendPacket(data);
    }

    public void SendAction(PlayerActionType action, int itemId, int quantity = 1, int durability = 0) {
        var data = NetworkProtocol.WritePlayerAction(LocalClientId, action, itemId, quantity, durability);
        SendPacket(data);
    }

    public void SendDropItem(int itemId, int quantity, int durability) {
        var data = NetworkProtocol.WritePlayerDropItem(LocalClientId, itemId, quantity, durability);
        SendPacket(data);
    }

    public void SendPickupCollect(uint networkId) {
        var data = NetworkProtocol.WritePickupCollect(networkId, LocalClientId);
        SendPacket(data);
    }

    public void SendAttackEntity(uint entityId, float damage, bool isHostile) {
        var data = NetworkProtocol.WriteAttackEntity(entityId, damage, isHostile);
        SendPacket(data);
    }

    public void SendSpawnProjectile(Vector3 pos, Vector3 vel, byte projType, byte dimension = 0) {
        var data = NetworkProtocol.WriteSpawnProjectile(LocalClientId, pos, vel, projType, dimension);
        SendPacket(data);
    }

    public void SendExplosion(Vector3 pos, float radius, float maxDamage, byte dimension = 0) {
        var data = NetworkProtocol.WriteExplosion(pos, radius, maxDamage, dimension);
        SendPacket(data);
    }

    public void SendBedSleep(bool isSleeping, Vec3i bedPos) {
        var data = NetworkProtocol.WriteBedSleep(LocalClientId, isSleeping, bedPos.X, bedPos.Y, bedPos.Z);
        SendPacket(data);
    }

    public void SendRequestChest(Vec3i pos, byte dimension = 0) {
        var data = NetworkProtocol.WriteRequestChest(pos.X, pos.Y, pos.Z, dimension);
        SendPacket(data);
    }

    public void SendHit(int targetId, float damage) {
        var data = NetworkProtocol.WritePlayerHit(LocalClientId, targetId, damage);
        SendPacket(data);
    }

    public void SendBlockChange(int x, int y, int z, ushort blockTypeId, byte mask, bool isBreak, byte dimension = 0) {
        var data = NetworkProtocol.WriteBlockChange(x, y, z, blockTypeId, mask, isBreak, dimension);
        SendPacket(data);
    }

    public void SendChatMessage(string message) {
        var data = NetworkProtocol.WriteChatMessage(PlayerName, message);
        SendPacket(data);
    }

    public void SendInventoryUpdate(Player player, byte dimension = 0) {
        var data = NetworkProtocol.WritePlayerInventoryUpdate(player, dimension);
        SendPacket(data);
    }

    public void SendInventoryAction(InventoryActionType action, int fromSlot, int toSlot, int amount) {
        var data = NetworkProtocol.WriteInventoryAction(action, fromSlot, toSlot, amount);
        SendPacket(data);
    }

    public void SendChestSync(Vec3i pos, VoxelFrame.Core.Inventory.Container container, byte dimension = 0) {
        var data = NetworkProtocol.WriteChestSync(pos.X, pos.Y, pos.Z, container, dimension);
        SendPacket(data);
    }

    public void SendRequestFurnace(Vec3i pos, byte dimension = 0) {
        var data = NetworkProtocol.WriteRequestFurnace(pos.X, pos.Y, pos.Z, dimension);
        SendPacket(data);
    }

    public void SendFurnaceSync(Vec3i pos, FurnaceData furnace, byte dimension = 0) {
        var inItem = furnace.Input.HasValue ? (furnace.Input.Value.Item.Definition.Id, furnace.Input.Value.Quantity) : ((ushort, int)?)null;
        var fuelItem = furnace.Fuel.HasValue ? (furnace.Fuel.Value.Item.Definition.Id, furnace.Fuel.Value.Quantity) : ((ushort, int)?)null;
        var outItem = furnace.Output.HasValue ? (furnace.Output.Value.Item.Definition.Id, furnace.Output.Value.Quantity) : ((ushort, int)?)null;
        var data = NetworkProtocol.WriteFurnaceSync(pos.X, pos.Y, pos.Z, furnace.FuelTimer, furnace.MaxFuelTimer, furnace.SmeltTimer, inItem, fuelItem, outItem, dimension);
        SendPacket(data);
    }

    public static void ApplyPlayerDataToSession(GameSession session, Player data) {
        if (data.Dimension != session.World.Dimension) {
            session.SwitchDimension(data.Dimension);
        }
        session.Player.Position = data.Position;
        session.Player.Yaw = data.Yaw;
        session.Player.Pitch = data.Pitch;
        session.Player.Health = data.Health;
        session.Player.Hunger = data.Hunger;
        session.Player.Saturation = data.Saturation;
        session.Player.SelectedSlot = data.SelectedSlot;
        session.Player.Dimension = data.Dimension;

        for (int i = 0; i < session.Player.Inventory.Capacity; i++) session.Player.Inventory.RemoveAt(i);
        for (int i = 0; i < data.Inventory.Capacity; i++) {
            if (data.Inventory.Slots[i] is { } e) {
                session.Player.Inventory.InsertAt(i, e);
            }
        }
        session.Player.OffhandEntry = data.OffhandEntry;
        for (int a = 0; a < 4; a++) {
            session.Player.Armor[a] = data.Armor[a];
        }
        try {
            session.World.EnsureLoadedAroundSync(session.Player.Position, 1);
        } catch { }
    }

    public void Dispose() {
        if (GameWorld.OnNetworkBlockChangeBroadcast != null) {
            GameWorld.OnNetworkBlockChangeBroadcast = null;
        }
        if (_session != null) {
            SendInventoryUpdate(_session.Player);
        }
        _cts?.Cancel();
        try { _socket?.Close(); } catch { }
        _socket = null;
        _remotePlayers.Clear();
        _cts?.Dispose();
        _cts = null;
    }
}
