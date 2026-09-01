using System;
using System.Buffers;
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

public readonly struct PacketFrame : IDisposable {
    public readonly PacketType Type;
    public readonly byte[] Buffer;
    public readonly int Length;

    public PacketFrame(PacketType type, byte[] buffer, int length) {
        Type = type;
        Buffer = buffer;
        Length = length;
    }

    public void Dispose() {
        if (Buffer != null) {
            ArrayPool<byte>.Shared.Return(Buffer);
        }
    }
}

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
    PlayerHit = 12,
    PlayerDataSync = 13,
    PlayerInventoryUpdate = 14,
    Teleport = 15,
    ChestSync = 16,
    GameRuleSync = 17,
    WorldChunksSync = 18,
    MobSync = 19,
    PickupSync = 20,
    ProjectileSync = 21,
    PickupCollect = 22,
    AttackEntity = 23,
    SpawnProjectile = 24,
    Explosion = 25,
    BedSleep = 26,
    RequestChest = 27,
    FurnaceSync = 28,
    BossSync = 29,
    BlockReject = 30,
    RequestFurnace = 31,
    InventoryAction = 32
}

public enum InventoryActionType : byte {
    Move = 1,
    Drop = 2,
    Craft = 3
}

public enum PlayerActionType : byte {
    SwingArm = 1,
    Hurt = 2,
    Die = 3,
    Respawn = 4,
    ItemChange = 5,
    ShootArrow = 6,
    DropItem = 7
}

public static class NetworkProtocol {
    public const int DefaultPort = 25565;
    public const int DiscoveryPort = 44455;
    public const string DiscoveryMagic = "VF_LAN_DISCOVERY_V1";
    public const int MaxStandardPacketSize = 128 * 1024; // 128 KB standard max
    public const int MaxPacketSize = 16 * 1024 * 1024;   // 16 MB max for world chunks


    /// <summary>
    /// Собирает пакет с 4-байтовым заголовком длины [int32 Length][byte PacketType][Payload...].
    /// </summary>
    public static byte[] BuildPacket(PacketType type, Action<BinaryWriter> writeBody) {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write(0); // резервируем 4 байта под длину
        w.Write((byte)type);
        writeBody(w);
        w.Flush();

        int payloadLength = (int)ms.Length - 4;
        ms.Position = 0;
        w.Write(payloadLength);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Синхронное чтение следующего фрейма из TCP-потока с защитой от фрагментации и DoS-аллокаций.
    /// Возвращает структуру PacketFrame, которую необходимо освободить через Dispose().
    /// </summary>
    public static PacketFrame? ReadPacketFrame(Stream stream) {
        byte[] lenBuf = ArrayPool<byte>.Shared.Rent(4);
        try {
            int read = 0;
            while (read < 4) {
                int r = stream.Read(lenBuf, read, 4 - read);
                if (r <= 0) return null;
                read += r;
            }
            int payloadLength = BitConverter.ToInt32(lenBuf, 0);
            if (payloadLength <= 0 || payloadLength > MaxPacketSize) return null;

            int firstByte = stream.ReadByte();
            if (firstByte < 0) return null;
            var type = (PacketType)firstByte;

            if (type != PacketType.WorldChunksSync && payloadLength > MaxStandardPacketSize) {
                return null;
            }

            byte[] payload = ArrayPool<byte>.Shared.Rent(payloadLength);
            payload[0] = (byte)firstByte;
            read = 1;
            while (read < payloadLength) {
                int r = stream.Read(payload, read, payloadLength - read);
                if (r <= 0) {
                    ArrayPool<byte>.Shared.Return(payload);
                    return null;
                }
                read += r;
            }
            return new PacketFrame(type, payload, payloadLength);
        } finally {
            ArrayPool<byte>.Shared.Return(lenBuf);
        }
    }

    /// <summary>
    /// Асинхронное чтение следующего фрейма из TCP-потока.
    /// Возвращает структуру PacketFrame, которую необходимо освободить через Dispose().
    /// </summary>
    public static async Task<PacketFrame?> ReadPacketFrameAsync(Stream stream, CancellationToken token = default) {
        byte[] lenBuf = ArrayPool<byte>.Shared.Rent(4);
        try {
            int read = 0;
            while (read < 4) {
                int r = await stream.ReadAsync(lenBuf.AsMemory(read, 4 - read), token);
                if (r <= 0) return null;
                read += r;
            }
            int payloadLength = BitConverter.ToInt32(lenBuf, 0);
            if (payloadLength <= 0 || payloadLength > MaxPacketSize) return null;

            byte[] oneByte = ArrayPool<byte>.Shared.Rent(1);
            int r1;
            try {
                r1 = await stream.ReadAsync(oneByte.AsMemory(0, 1), token);
            } finally {
                ArrayPool<byte>.Shared.Return(oneByte);
            }
            if (r1 <= 0) return null;
            var type = (PacketType)oneByte[0];

            if (type != PacketType.WorldChunksSync && payloadLength > MaxStandardPacketSize) {
                return null;
            }

            byte[] payload = ArrayPool<byte>.Shared.Rent(payloadLength);
            payload[0] = oneByte[0];
            read = 1;
            while (read < payloadLength) {
                int r = await stream.ReadAsync(payload.AsMemory(read, payloadLength - read), token);
                if (r <= 0) {
                    ArrayPool<byte>.Shared.Return(payload);
                    return null;
                }
                read += r;
            }
            return new PacketFrame(type, payload, payloadLength);
        } finally {
            ArrayPool<byte>.Shared.Return(lenBuf);
        }
    }

    public static byte[] WriteHandshake(string playerName, string version, string skinName = "cyan") =>
        BuildPacket(PacketType.Handshake, w => {
            w.Write(playerName);
            w.Write(version);
            w.Write(skinName);
        });

    public static byte[] WriteWelcome(int clientId, int seed, float timeOfDay, bool cheatsEnabled, int gamemode, bool keepInventory) =>
        BuildPacket(PacketType.Welcome, w => {
            w.Write(clientId);
            w.Write(seed);
            w.Write(timeOfDay);
            w.Write(cheatsEnabled);
            w.Write(gamemode);
            w.Write(keepInventory);
        });

    public static byte[] WriteGameRuleSync(string rule, bool value) =>
        BuildPacket(PacketType.GameRuleSync, w => {
            w.Write(rule);
            w.Write(value);
        });

    public static byte[] WritePlayerJoin(int clientId, string playerName, Vector3 pos, float yaw, float pitch, byte dimension = 0, string skinName = "cyan") =>
        BuildPacket(PacketType.PlayerJoin, w => {
            w.Write(clientId);
            w.Write(playerName);
            w.Write(pos.X); w.Write(pos.Y); w.Write(pos.Z);
            w.Write(yaw); w.Write(pitch);
            w.Write(dimension);
            w.Write(skinName);
        });

    public static byte[] WritePlayerLeave(int clientId) =>
        BuildPacket(PacketType.PlayerLeave, w => {
            w.Write(clientId);
        });

    public static byte[] WritePlayerMovement(int clientId, Vector3 pos, float yaw, float pitch, bool isMoving, bool isSneaking, bool isFlying, bool isBlocking, float health, byte dimension = 0) =>
        BuildPacket(PacketType.PlayerMovement, w => {
            w.Write(clientId);
            w.Write(pos.X); w.Write(pos.Y); w.Write(pos.Z);
            w.Write(yaw); w.Write(pitch);
            byte flags = 0;
            if (isMoving) flags |= 1;
            if (isSneaking) flags |= 2;
            if (isFlying) flags |= 4;
            if (isBlocking) flags |= 8;
            w.Write(flags);
            w.Write(health);
            w.Write(dimension);
        });

    public static byte[] WritePlayerAction(int clientId, PlayerActionType action, int itemId, int quantity = 1, int durability = 0) =>
        BuildPacket(PacketType.PlayerAction, w => {
            w.Write(clientId);
            w.Write((byte)action);
            w.Write(itemId);
            w.Write(quantity);
            w.Write(durability);
        });

    public static byte[] WritePlayerDropItem(int clientId, int itemId, int quantity, int durability) =>
        WritePlayerAction(clientId, PlayerActionType.DropItem, itemId, quantity, durability);

    public static byte[] WritePlayerHit(int attackerId, int targetId, float damage) =>
        BuildPacket(PacketType.PlayerHit, w => {
            w.Write(attackerId);
            w.Write(targetId);
            w.Write(damage);
        });

    public static byte[] WriteBlockChange(int x, int y, int z, ushort blockTypeId, byte subGridMask, bool isBreak, byte dimension = 0) =>
        BuildPacket(PacketType.BlockChange, w => {
            w.Write(x); w.Write(y); w.Write(z);
            w.Write(blockTypeId);
            w.Write(subGridMask);
            w.Write(isBreak);
            w.Write(dimension);
        });

    public static byte[] WriteChatMessage(string sender, string message, byte r = 255, byte g = 255, byte b = 255) =>
        BuildPacket(PacketType.ChatMessage, w => {
            w.Write(sender);
            w.Write(message);
            w.Write(r); w.Write(g); w.Write(b);
        });

    public static byte[] WriteTimeWeatherSync(float timeOfDay, int weatherState) =>
        BuildPacket(PacketType.TimeWeatherSync, w => {
            w.Write(timeOfDay);
            w.Write(weatherState);
        });

    public static byte[] WriteTeleport(Vector3 pos) =>
        BuildPacket(PacketType.Teleport, w => {
            w.Write(pos.X);
            w.Write(pos.Y);
            w.Write(pos.Z);
        });

    public static byte[] WritePlayerDataSync(Player player, byte dimension = 0) =>
        BuildPacket(PacketType.PlayerDataSync, w => {
            WritePlayerVitalData(w, player, dimension);
        });

    public static byte[] WritePlayerInventoryUpdate(Player player, byte dimension = 0) =>
        BuildPacket(PacketType.PlayerInventoryUpdate, w => {
            WritePlayerVitalData(w, player, dimension);
        });

    public static byte[] WriteInventoryAction(InventoryActionType action, int fromSlot, int toSlot, int amount) =>
        BuildPacket(PacketType.InventoryAction, w => {
            w.Write((byte)action);
            w.Write(fromSlot);
            w.Write(toSlot);
            w.Write(amount);
        });

    private static void WritePlayerVitalData(BinaryWriter w, Player player, byte dimension) {
        w.Write(player.Position.X);
        w.Write(player.Position.Y);
        w.Write(player.Position.Z);
        w.Write(player.Yaw);
        w.Write(player.Pitch);
        w.Write(player.Health);
        w.Write(player.Hunger);
        w.Write(player.Saturation);
        w.Write(player.SelectedSlot);
        w.Write(dimension);

        // Инвентарь
        int nonNullCount = 0;
        for (int i = 0; i < player.Inventory.Capacity; i++) {
            var slot = player.Inventory.Slots[i];
            if (slot != null && slot.Value.Quantity > 0) nonNullCount++;
        }
        w.Write(nonNullCount);
        for (int i = 0; i < player.Inventory.Capacity; i++) {
            var slot = player.Inventory.Slots[i];
            if (slot != null && slot.Value.Quantity > 0) {
                w.Write(i);
                w.Write(slot.Value.Item.Definition.Id);
                w.Write(slot.Value.Quantity);
                w.Write(slot.Value.Item.Durability);
            }
        }

        // Оффхэнд
        if (player.OffhandEntry != null && player.OffhandEntry.Value.Quantity > 0) {
            w.Write(true);
            w.Write(player.OffhandEntry.Value.Item.Definition.Id);
            w.Write(player.OffhandEntry.Value.Quantity);
            w.Write(player.OffhandEntry.Value.Item.Durability);
        } else {
            w.Write(false);
        }

        // Броня
        for (int a = 0; a < 4; a++) {
            if (player.Armor[a] is { } ae && ae.Quantity > 0) {
                w.Write(true);
                w.Write(ae.Item.Definition.Id);
                w.Write(ae.Quantity);
                w.Write(ae.Item.Durability);
            } else {
                w.Write(false);
            }
        }
    }

    public static byte[] WriteDisconnect(string reason = "Left game") =>
        BuildPacket(PacketType.Disconnect, w => {
            w.Write(reason);
        });

    public static byte[] WriteChestSync(int x, int y, int z, Container container, byte dimension = 0) =>
        BuildPacket(PacketType.ChestSync, w => {
            w.Write(x);
            w.Write(y);
            w.Write(z);
            w.Write(dimension);

            int nonNullCount = 0;
            for (int i = 0; i < container.Capacity; i++) {
                var slot = container.Slots[i];
                if (slot != null && slot.Value.Quantity > 0) nonNullCount++;
            }
            w.Write(nonNullCount);
            for (int i = 0; i < container.Capacity; i++) {
                var slot = container.Slots[i];
                if (slot != null && slot.Value.Quantity > 0) {
                    w.Write(i);
                    w.Write(slot.Value.Item.Definition.Id);
                    w.Write(slot.Value.Quantity);
                    w.Write(slot.Value.Item.Durability);
                }
            }
        });

    /// <summary>
    /// Синхронизация измененных / загруженных чанков мира хоста со сжатием GZip.
    /// </summary>
    public static byte[] WriteWorldChunksSync(byte dimension, IReadOnlyCollection<GameChunk> chunks) =>
        BuildPacket(PacketType.WorldChunksSync, w => {
            w.Write(dimension);

            using var memGz = new MemoryStream();
            using (var gz = new GZipStream(memGz, CompressionLevel.Fastest, leaveOpen: true))
            using (var bwGz = new BinaryWriter(gz, Encoding.UTF8)) {
                bwGz.Write(chunks.Count);
                var chunkByteBuf = new byte[Chunk.VoxelCount * sizeof(ushort)];
                var types = new ushort[Chunk.VoxelCount];

                foreach (var gc in chunks) {
                    bwGz.Write(gc.Coord.X);
                    bwGz.Write(gc.Coord.Y);
                    bwGz.Write(gc.Coord.Z);

                    var masks = new List<(int Index, byte Mask)>();
                    for (int i = 0; i < Chunk.VoxelCount; i++) {
                        var v = gc.Chunk.Get(i);
                        types[i] = v.TypeId;
                        if (v.TypeId != 0 && v.SubGridLayerMask != 0)
                            masks.Add((i, v.SubGridLayerMask));
                    }

                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(types.AsSpan()).CopyTo(chunkByteBuf);
                    bwGz.Write(chunkByteBuf);

                    bwGz.Write(masks.Count);
                    foreach (var (idx, mask) in masks) {
                        bwGz.Write(idx);
                        bwGz.Write(mask);
                    }
                }
            }

            byte[] compressed = memGz.ToArray();
            w.Write(compressed.Length);
            w.Write(compressed);
        });

    /// <summary>
    /// Синхронизация мобов (животные и монстры) в измерении.
    /// </summary>
    public static byte[] WriteMobSync(byte dimension, IReadOnlyList<Animal> animals, IReadOnlyList<HostileMob> hostiles) =>
        BuildPacket(PacketType.MobSync, w => {
            w.Write(dimension);
            
            // Животные
            int animCount = 0;
            for (int i = 0; i < animals.Count; i++) if (animals[i].Alive) animCount++;
            w.Write(animCount);
            for (int i = 0; i < animals.Count; i++) {
                var a = animals[i];
                if (!a.Alive) continue;
                w.Write(a.Id);
                w.Write((byte)a.Type);
                w.Write(a.Position.X); w.Write(a.Position.Y); w.Write(a.Position.Z);
                w.Write(a.Velocity.X); w.Write(a.Velocity.Y); w.Write(a.Velocity.Z);
                w.Write(a.Health);
                w.Write(a.HurtTime);
                w.Write(a.IsBaby);
                w.Write(a.LoveTimer > 0f);
            }

            // Монстры
            int hostCount = 0;
            for (int i = 0; i < hostiles.Count; i++) if (hostiles[i].Alive) hostCount++;
            w.Write(hostCount);
            for (int i = 0; i < hostiles.Count; i++) {
                var h = hostiles[i];
                if (!h.Alive) continue;
                w.Write(h.Id);
                w.Write((byte)h.Type);
                w.Write(h.Position.X); w.Write(h.Position.Y); w.Write(h.Position.Z);
                w.Write(h.Velocity.X); w.Write(h.Velocity.Y); w.Write(h.Velocity.Z);
                w.Write(h.Health);
                w.Write(h.HurtTime);
                w.Write(h.FuseTimer);
            }
        });

    /// <summary>
    /// Синхронизация выпавших предметов (ItemPickups).
    /// </summary>
    public static byte[] WritePickupSync(byte dimension, IReadOnlyList<ItemPickup> pickups) =>
        BuildPacket(PacketType.PickupSync, w => {
            w.Write(dimension);
            int count = 0;
            for (int i = 0; i < pickups.Count; i++) if (pickups[i].Quantity > 0) count++;
            w.Write(count);
            for (int i = 0; i < pickups.Count; i++) {
                var p = pickups[i];
                if (p.Quantity <= 0) continue;
                w.Write(p.Id);
                w.Write(p.Item.Definition.Id);
                w.Write(p.Quantity);
                w.Write(p.Item.Durability);
                w.Write(p.Position.X); w.Write(p.Position.Y); w.Write(p.Position.Z);
                w.Write(p.Velocity.X); w.Write(p.Velocity.Y); w.Write(p.Velocity.Z);
                w.Write(p.BobPhase);
            }
        });

    /// <summary>
    /// Синхронизация снарядов (стрелы, жемчуг).
    /// </summary>
    public static byte[] WriteProjectileSync(byte dimension, IReadOnlyList<ArrowProjectile> arrows) =>
        BuildPacket(PacketType.ProjectileSync, w => {
            w.Write(dimension);
            int count = 0;
            for (int i = 0; i < arrows.Count; i++) if (arrows[i].Alive) count++;
            w.Write(count);
            for (int i = 0; i < arrows.Count; i++) {
                var arr = arrows[i];
                if (!arr.Alive) continue;
                w.Write(arr.Position.X); w.Write(arr.Position.Y); w.Write(arr.Position.Z);
                w.Write(arr.Velocity.X); w.Write(arr.Velocity.Y); w.Write(arr.Velocity.Z);
                byte flags = 0;
                if (arr.IsSlimeSpit) flags |= 1;
                if (arr.IsEnderPearl) flags |= 2;
                if (arr.IsEyeOfEnder) flags |= 4;
                w.Write(flags);
            }
        });

    public static byte[] WritePickupCollect(uint networkId, int collectorClientId) =>
        BuildPacket(PacketType.PickupCollect, w => {
            w.Write(networkId);
            w.Write(collectorClientId);
        });

    public static byte[] WriteAttackEntity(uint entityId, float damage, bool isHostile) =>
        BuildPacket(PacketType.AttackEntity, w => {
            w.Write(entityId);
            w.Write(damage);
            w.Write(isHostile);
        });

    public static byte[] WriteSpawnProjectile(int shooterId, Vector3 pos, Vector3 vel, byte projType, byte dimension) =>
        BuildPacket(PacketType.SpawnProjectile, w => {
            w.Write(shooterId);
            w.Write(pos.X); w.Write(pos.Y); w.Write(pos.Z);
            w.Write(vel.X); w.Write(vel.Y); w.Write(vel.Z);
            w.Write(projType);
            w.Write(dimension);
        });

    public static byte[] WriteExplosion(Vector3 pos, float radius, float maxDamage, byte dimension) =>
        BuildPacket(PacketType.Explosion, w => {
            w.Write(pos.X); w.Write(pos.Y); w.Write(pos.Z);
            w.Write(radius);
            w.Write(maxDamage);
            w.Write(dimension);
        });

    public static byte[] WriteBedSleep(int clientId, bool isSleeping, int bedX, int bedY, int bedZ) =>
        BuildPacket(PacketType.BedSleep, w => {
            w.Write(clientId);
            w.Write(isSleeping);
            w.Write(bedX); w.Write(bedY); w.Write(bedZ);
        });

    public static byte[] WriteRequestChest(int x, int y, int z, byte dimension) =>
        BuildPacket(PacketType.RequestChest, w => {
            w.Write(x); w.Write(y); w.Write(z);
            w.Write(dimension);
        });

    public static byte[] WriteFurnaceSync(int x, int y, int z, float fuelTimer, float maxFuelTimer, float smeltTimer,
        (ushort Id, int Qty)? input, (ushort Id, int Qty)? fuel, (ushort Id, int Qty)? output, byte dimension) =>
        BuildPacket(PacketType.FurnaceSync, w => {
            w.Write(x); w.Write(y); w.Write(z);
            w.Write(dimension);
            w.Write(fuelTimer);
            w.Write(maxFuelTimer);
            w.Write(smeltTimer);

            w.Write(input.HasValue);
            if (input.HasValue) {
                w.Write(input.Value.Id);
                w.Write(input.Value.Qty);
            }

            w.Write(fuel.HasValue);
            if (fuel.HasValue) {
                w.Write(fuel.Value.Id);
                w.Write(fuel.Value.Qty);
            }

            w.Write(output.HasValue);
            if (output.HasValue) {
                w.Write(output.Value.Id);
                w.Write(output.Value.Qty);
            }
        });

    public static byte[] WriteRequestFurnace(int x, int y, int z, byte dimension) =>
        BuildPacket(PacketType.RequestFurnace, w => {
            w.Write(x); w.Write(y); w.Write(z);
            w.Write(dimension);
        });

    public static byte[] WriteBossSync(byte bossType, bool alive, bool awake, float hp, float maxHp, int phase,
        Vector3 pos, Vector3 vel, float hurtTime, float slamWarning, bool isResting, byte dimension) =>
        BuildPacket(PacketType.BossSync, w => {
            w.Write(bossType);
            w.Write(alive);
            w.Write(awake);
            w.Write(hp);
            w.Write(maxHp);
            w.Write(phase);
            w.Write(pos.X); w.Write(pos.Y); w.Write(pos.Z);
            w.Write(vel.X); w.Write(vel.Y); w.Write(vel.Z);
            w.Write(hurtTime);
            w.Write(slamWarning);
            w.Write(isResting);
            w.Write(dimension);
        });

    public static byte[] WriteBlockReject(int x, int y, int z, ushort actualTypeId, byte actualMask, byte dimension) =>
        BuildPacket(PacketType.BlockReject, w => {
            w.Write(x); w.Write(y); w.Write(z);
            w.Write(actualTypeId);
            w.Write(actualMask);
            w.Write(dimension);
        });
}
