using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoxelFrame.Core;

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
    public float Health { get; set; } = 20f;
    public int SelectedItemId { get; set; }
    public float ArmSwingTimer { get; set; }
    public float HurtTimer { get; set; }

    public RemotePlayer(int id, string name, Vector3 pos, float yaw, float pitch) {
        Id = id;
        Name = name;
        Position = pos;
        TargetPosition = pos;
        Yaw = yaw;
        TargetYaw = yaw;
        Pitch = pitch;
        TargetPitch = pitch;
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
    public bool HasReceivedWelcome { get; private set; } = false;

    private readonly ConcurrentDictionary<int, RemotePlayer> _remotePlayers = new();
    public IReadOnlyCollection<RemotePlayer> RemotePlayers => _remotePlayers.Values.ToArray();

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
    }

    public static void Disconnect() {
        Active?.Dispose();
        Active = null;
    }

    private async Task StartAsync() {
        _socket = new TcpClient { NoDelay = true };
        await _socket.ConnectAsync(Host, Port);
        _stream = _socket.GetStream();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Отправка приветственного Handshake
        var handshake = NetworkProtocol.WriteHandshake(PlayerName, "1.0.0");
        await _stream.WriteAsync(handshake, token);

        _ = Task.Run(() => ReceiveLoop(token), token);
    }

    private void ReceiveLoop(CancellationToken token) {
        using var reader = new BinaryReader(_stream!, Encoding.UTF8, leaveOpen: true);

        try {
            while (!token.IsCancellationRequested && _socket is { Connected: true }) {
                byte packetTypeId = reader.ReadByte();
                var pType = (PacketType)packetTypeId;

                switch (pType) {
                    case PacketType.Welcome: {
                        LocalClientId = reader.ReadInt32();
                        ReceivedSeed = reader.ReadInt32();
                        ReceivedTimeOfDay = reader.ReadSingle();
                        ReceivedCheats = reader.ReadBoolean();
                        ReceivedGamemode = reader.ReadInt32();
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

                        if (id != LocalClientId) {
                            _remotePlayers[id] = new RemotePlayer(id, name, new Vector3(x, y, z), yaw, pitch);
                            _session?.AddChatMessage($"Игрок {name} вошёл в мир.", Raylib_cs.Color.Yellow);
                            _session?.AddMessage($"Игрок {name} вошёл в мир");
                        }
                        break;
                    }
                    case PacketType.PlayerLeave: {
                        int id = reader.ReadInt32();
                        if (_remotePlayers.TryRemove(id, out var rp)) {
                            _session?.AddChatMessage($"Игрок {rp.Name} вышел из игры.", Raylib_cs.Color.Yellow);
                            _session?.AddMessage($"Игрок {rp.Name} вышел");
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
                            rp.Health = hp;
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

                        if (targetId == LocalClientId && _session != null) {
                            Vector3? attackerPos = null;
                            if (_remotePlayers.TryGetValue(attackerId, out var att)) attackerPos = att.Position;
                            _session.Player.ApplyDamage(dmg, _session, attackerPos, cause: "pvp");
                            _session.Player.HurtTimer = 1.0f;
                            SendAction(PlayerActionType.Hurt, 0);
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

                        if (_session != null) {
                            var cell = new Vec3i(bx, by, bz);
                            if (isBreak) {
                                _session.World.RemoveBlock(cell);
                                SoundSystem.PlayDig(typeId);
                            } else {
                                _session.World.PlacePlacedBlock(cell, GameData.GetBlock(typeId), mask);
                                SoundSystem.PlayPlace();
                            }
                        }
                        break;
                    }
                    case PacketType.ChatMessage: {
                        string sender = reader.ReadString();
                        string msg = reader.ReadString();
                        byte r = reader.ReadByte();
                        byte g = reader.ReadByte();
                        byte b = reader.ReadByte();
                        _session?.AddChatMessage($"<{sender}> {msg}", new Raylib_cs.Color(r, g, b, (byte)255));
                        break;
                    }
                    case PacketType.TimeWeatherSync: {
                        float tod = reader.ReadSingle();
                        int weather = reader.ReadInt32();
                        if (_session != null) {
                            _session.DayNight.TimeOfDay = tod;
                            _session.Weather = (WeatherType)weather;
                        }
                        break;
                    }
                }
            }
        } catch {
            // Disconnected
        } finally {
            _session?.AddChatMessage("Соединение с сервером разорвано.", Raylib_cs.Color.Red);
            _session?.AddMessage("Соединение с сервером разорвано");
        }
    }

    public void UpdateRemotePlayers(float dt) {
        foreach (var rp in _remotePlayers.Values) {
            rp.Update(dt);
        }
    }

    public void SendMovement(Vector3 pos, float yaw, float pitch, bool isMoving, bool isSneaking, bool isFlying, float health) {
        if (!IsConnected || _stream == null) return;
        try {
            var data = NetworkProtocol.WritePlayerMovement(LocalClientId, pos, yaw, pitch, isMoving, isSneaking, isFlying, health);
            _stream.Write(data, 0, data.Length);
        } catch { }
    }

    public void SendAction(PlayerActionType action, int itemId) {
        if (!IsConnected || _stream == null) return;
        try {
            var data = NetworkProtocol.WritePlayerAction(LocalClientId, action, itemId);
            _stream.Write(data, 0, data.Length);
        } catch { }
    }

    public void SendHit(int targetId, float damage) {
        if (!IsConnected || _stream == null) return;
        try {
            var data = NetworkProtocol.WritePlayerHit(LocalClientId, targetId, damage);
            _stream.Write(data, 0, data.Length);
        } catch { }
    }

    public void SendBlockChange(int x, int y, int z, ushort blockTypeId, byte mask, bool isBreak) {
        if (!IsConnected || _stream == null) return;
        try {
            var data = NetworkProtocol.WriteBlockChange(x, y, z, blockTypeId, mask, isBreak);
            _stream.Write(data, 0, data.Length);
        } catch { }
    }

    public void SendChatMessage(string message) {
        if (!IsConnected || _stream == null) return;
        try {
            var data = NetworkProtocol.WriteChatMessage(PlayerName, message);
            _stream.Write(data, 0, data.Length);
        } catch { }
    }

    public void Dispose() {
        _cts?.Cancel();
        try { _socket?.Close(); } catch { }
        _socket = null;
        _remotePlayers.Clear();
        _cts?.Dispose();
        _cts = null;
    }
}