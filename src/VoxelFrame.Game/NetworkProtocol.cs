using System;
using System.IO;
using System.Numerics;
using System.Text;

namespace VoxelFrame.Game;

public enum PacketType : byte {
    Handshake = 1,
    Welcome = 2,
    PlayerJoin = 3,
    PlayerLeave = 4,
    PlayerMovement = 5,
    PlayerAction = 6,
    BlockChange = 7,
    ChatMessage = 8,
    TimeWeatherSync = 9,
    KeepAlive = 10,
    Disconnect = 11,
    PlayerHit = 12
}

public enum PlayerActionType : byte {
    SwingArm = 1,
    Hurt = 2,
    Die = 3,
    Respawn = 4,
    ItemChange = 5,
    ShootArrow = 6
}

public static class NetworkProtocol {
    public const int DefaultPort = 25565;
    public const int DiscoveryPort = 44455;
    public const string DiscoveryMagic = "VF_LAN_DISCOVERY_V1";

    public static byte[] WriteHandshake(string playerName, string version) {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write((byte)PacketType.Handshake);
        w.Write(playerName);
        w.Write(version);
        return ms.ToArray();
    }

    public static byte[] WriteWelcome(int clientId, int seed, float timeOfDay, bool cheatsEnabled, int gamemode) {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write((byte)PacketType.Welcome);
        w.Write(clientId);
        w.Write(seed);
        w.Write(timeOfDay);
        w.Write(cheatsEnabled);
        w.Write(gamemode);
        return ms.ToArray();
    }

    public static byte[] WritePlayerJoin(int clientId, string playerName, Vector3 pos, float yaw, float pitch) {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write((byte)PacketType.PlayerJoin);
        w.Write(clientId);
        w.Write(playerName);
        w.Write(pos.X); w.Write(pos.Y); w.Write(pos.Z);
        w.Write(yaw); w.Write(pitch);
        return ms.ToArray();
    }

    public static byte[] WritePlayerLeave(int clientId) {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write((byte)PacketType.PlayerLeave);
        w.Write(clientId);
        return ms.ToArray();
    }

    public static byte[] WritePlayerMovement(int clientId, Vector3 pos, float yaw, float pitch, bool isMoving, bool isSneaking, bool isFlying, float health) {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write((byte)PacketType.PlayerMovement);
        w.Write(clientId);
        w.Write(pos.X); w.Write(pos.Y); w.Write(pos.Z);
        w.Write(yaw); w.Write(pitch);
        byte flags = 0;
        if (isMoving) flags |= 1;
        if (isSneaking) flags |= 2;
        if (isFlying) flags |= 4;
        w.Write(flags);
        w.Write(health);
        return ms.ToArray();
    }

    public static byte[] WritePlayerAction(int clientId, PlayerActionType action, int itemId) {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write((byte)PacketType.PlayerAction);
        w.Write(clientId);
        w.Write((byte)action);
        w.Write(itemId);
        return ms.ToArray();
    }

    public static byte[] WritePlayerHit(int attackerId, int targetId, float damage) {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write((byte)PacketType.PlayerHit);
        w.Write(attackerId);
        w.Write(targetId);
        w.Write(damage);
        return ms.ToArray();
    }

    public static byte[] WriteBlockChange(int x, int y, int z, ushort blockTypeId, byte subGridMask, bool isBreak) {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write((byte)PacketType.BlockChange);
        w.Write(x); w.Write(y); w.Write(z);
        w.Write(blockTypeId);
        w.Write(subGridMask);
        w.Write(isBreak);
        return ms.ToArray();
    }

    public static byte[] WriteChatMessage(string sender, string message, byte r = 255, byte g = 255, byte b = 255) {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write((byte)PacketType.ChatMessage);
        w.Write(sender);
        w.Write(message);
        w.Write(r); w.Write(g); w.Write(b);
        return ms.ToArray();
    }

    public static byte[] WriteTimeWeatherSync(float timeOfDay, int weatherState) {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write((byte)PacketType.TimeWeatherSync);
        w.Write(timeOfDay);
        w.Write(weatherState);
        return ms.ToArray();
    }
}