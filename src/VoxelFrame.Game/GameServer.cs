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
    public float Health { get; set; } = 20f;
    public int SelectedItemId { get; set; }
    public float ArmSwingTimer { get; set; }
    public float HurtTimer { get; set; }
    public string SkinName { get; set; } = "steve";
    public Player PlayerData { get; set; } = new();

    public ConnectedClient(int id, TcpClient socket) {
        Id = id;
        Socket = socket;
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
        return srv;
    }

    public static void Stop() {
        Active?.Dispose();
        Active = null;
    }

    public void Update(float dt) {
        foreach (var c in _clients.Values) {
            c.Update(dt);
        }

        _timeSyncTimer += dt;
        if (_timeSyncTimer >= 3.0f && ClientCount > 0) {
            _timeSyncTimer = 0f;
            var timePacket = NetworkProtocol.WriteTimeWeatherSync(_session.DayNight.TimeOfDay, (int)_session.Weather);
            Broadcast(timePacket);
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
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        try {
            // Welcome handshake
            var welcome = NetworkProtocol.WriteWelcome(
                clientId,
                _session.World.Generator.Seed,
                _session.DayNight.TimeOfDay,
                _session.CheatsEnabled,
                (int)_session.GameMode
            );
            stream.Write(welcome, 0, welcome.Length);

            // Inform the new client about the host player
            var hostJoin = NetworkProtocol.WritePlayerJoin(
                1,
                _session.Player.Name,
                _session.Player.Position,
                _session.Player.Yaw,
                _session.Player.Pitch
            );
            stream.Write(hostJoin, 0, hostJoin.Length);

            // Inform about other connected clients
            foreach (var other in _clients.Values) {
                if (other.Id != clientId) {
                    var otherJoin = NetworkProtocol.WritePlayerJoin(other.Id, other.Name, other.Position, other.Yaw, other.Pitch);
                    stream.Write(otherJoin, 0, otherJoin.Length);
                }
            }

            while (!token.IsCancellationRequested && socket.Connected) {
                byte packetTypeId = reader.ReadByte();
                var pType = (PacketType)packetTypeId;

                switch (pType) {
                    case PacketType.Handshake: {
                        string name = reader.ReadString();
                        string ver = reader.ReadString();
                        client.Name = name;
                        client.PlayerData.Name = name;

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

                                var pDataSync = NetworkProtocol.WritePlayerDataSync(client.PlayerData);
                                stream.Write(pDataSync, 0, pDataSync.Length);
                            }
                        }

                        _session.AddChatMessage($"Игрок {name} присоединился к игре!", Raylib_cs.Color.Yellow);
                        _session.AddMessage($"Игрок {name} вошел в мир");
                        // Broadcast new player to all clients
                        var joinPacket = NetworkProtocol.WritePlayerJoin(clientId, name, client.Position, client.Yaw, client.Pitch);
                        Broadcast(joinPacket, exceptClientId: clientId);
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

                        client.PlayerData.Position = new Vector3(px, py, pz);
                        client.PlayerData.Yaw = yaw;
                        client.PlayerData.Pitch = pitch;
                        client.PlayerData.Health = hp;
                        client.PlayerData.Hunger = hunger;
                        client.PlayerData.Saturation = sat;
                        client.PlayerData.SelectedSlot = slot;

                        // Инвентарь
                        for (int i = 0; i < client.PlayerData.Inventory.Capacity; i++) client.PlayerData.Inventory.RemoveAt(i);
                        int invCount = reader.ReadInt32();
                        for (int i = 0; i < invCount; i++) {
                            int idx = reader.ReadInt32();
                            ushort itId = reader.ReadUInt16();
                            int itQty = reader.ReadInt32();
                            int itDur = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(itId, out var itDef)) {
                                var itemInst = GameData.NewItem(itDef);
                                itemInst.Durability = itDur;
                                client.PlayerData.Inventory.InsertAt(idx, new ItemEntry(itemInst, itQty));
                            }
                        }

                        // Оффхэнд
                        if (reader.ReadBoolean()) {
                            ushort offId = reader.ReadUInt16();
                            int offQty = reader.ReadInt32();
                            int offDur = reader.ReadInt32();
                            if (GameData.Items.TryGetValue(offId, out var offDef)) {
                                var offInst = GameData.NewItem(offDef);
                                offInst.Durability = offDur;
                                client.PlayerData.OffhandEntry = new ItemEntry(offInst, offQty);
                            }
                        } else {
                            client.PlayerData.OffhandEntry = null;
                        }

                        // Броня
                        for (int a = 0; a < 4; a++) {
                            if (reader.ReadBoolean()) {
                                ushort armId = reader.ReadUInt16();
                                int armQty = reader.ReadInt32();
                                int armDur = reader.ReadInt32();
                                if (GameData.Items.TryGetValue(armId, out var armDef)) {
                                    var armInst = GameData.NewItem(armDef);
                                    armInst.Durability = armDur;
                                    client.PlayerData.Armor[a] = new ItemEntry(armInst, armQty);
                                }
                            } else {
                                client.PlayerData.Armor[a] = null;
                            }
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

                        var newPos = new Vector3(x, y, z);
                        if (Vector3.DistanceSquared(client.Position, newPos) > 400f || client.Position == Vector3.Zero) {
                            client.Position = newPos;
                        }
                        client.TargetPosition = newPos;
                        client.TargetYaw = yaw;
                        client.TargetPitch = pitch;
                        client.IsMoving = (flags & 1) != 0;
                        client.IsSneaking = (flags & 2) != 0;
                        client.IsFlying = (flags & 4) != 0;
                        client.Health = hp;

                        // Broadcast movement to all other clients
                        var moveP = NetworkProtocol.WritePlayerMovement(id, client.Position, yaw, pitch, client.IsMoving, client.IsSneaking, client.IsFlying, hp);
                        Broadcast(moveP, exceptClientId: clientId);
                        break;
                    }
                    case PacketType.PlayerAction: {
                        int id = reader.ReadInt32();
                        var action = (PlayerActionType)reader.ReadByte();
                        int itemId = reader.ReadInt32();
                        client.SelectedItemId = itemId;
                        if (action == PlayerActionType.SwingArm) {
                            client.ArmSwingTimer = 1.0f;
                        } else if (action == PlayerActionType.Hurt) {
                            client.HurtTimer = 1.0f;
                        }
                        var actP = NetworkProtocol.WritePlayerAction(id, action, itemId);
                        Broadcast(actP, exceptClientId: clientId);
                        break;
                    }
                    case PacketType.PlayerHit: {
                        int attackerId = reader.ReadInt32();
                        int targetId = reader.ReadInt32();
                        float dmg = reader.ReadSingle();

                        if (targetId == 1) {
                            // Hit the host player!
                            var attackerPos = client.Position;
                            _session.Player.ApplyDamage(dmg, _session, attackerPos, cause: "pvp");
                            _session.Player.HurtTimer = 1.0f;
                            BroadcastHostAction(PlayerActionType.Hurt, 0);
                        } else {
                            // Forward to target client
                            var hitPacket = NetworkProtocol.WritePlayerHit(attackerId, targetId, dmg);
                            Broadcast(hitPacket, exceptClientId: clientId);
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

                        var cell = new Vec3i(bx, by, bz);
                        if (isBreak) {
                            _session.World.RemoveBlock(cell);
                        } else {
                            _session.World.PlacePlacedBlock(cell, GameData.GetBlock(typeId), mask);
                        }

                        // Broadcast to other clients
                        var blockP = NetworkProtocol.WriteBlockChange(bx, by, bz, typeId, mask, isBreak);
                        Broadcast(blockP, exceptClientId: clientId);
                        break;
                    }
                    case PacketType.ChatMessage: {
                        string sender = reader.ReadString();
                        string msg = reader.ReadString();
                        byte r = reader.ReadByte();
                        byte g = reader.ReadByte();
                        byte b = reader.ReadByte();
                        _session.AddChatMessage($"<{sender}> {msg}", new Raylib_cs.Color(r, g, b, (byte)255));
                        var chatP = NetworkProtocol.WriteChatMessage(sender, msg, r, g, b);
                        Broadcast(chatP, exceptClientId: clientId);
                        break;
                    }
                }
            }
        } catch {
            // Disconnected
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
            try {
                var stream = client.Socket.GetStream();
                stream.Write(tpPacket, 0, tpPacket.Length);
            } catch { }
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
            try {
                var stream = client.Socket.GetStream();
                stream.Write(data, 0, data.Length);
            } catch {
                // Ignore transient write errors
            }
        }
    }

    public void BroadcastHostMovement(Vector3 pos, float yaw, float pitch, bool isMoving, bool isSneaking, bool isFlying, float health) {
        if (!IsRunning || ClientCount == 0) return;
        var p = NetworkProtocol.WritePlayerMovement(1, pos, yaw, pitch, isMoving, isSneaking, isFlying, health);
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
        var p = NetworkProtocol.WriteBlockChange(x, y, z, blockTypeId, mask, isBreak);
        Broadcast(p);
    }

    public void BroadcastHostChat(string name, string message) {
        if (!IsRunning || ClientCount == 0) return;
        var p = NetworkProtocol.WriteChatMessage(name, message);
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