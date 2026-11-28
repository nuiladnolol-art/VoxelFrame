using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Voxalith.Core.Net;

/// <summary>
/// Чистый сетевой фрейминг [int32 Length][byte Type][Payload...] и валидация имён
/// без привязки к Raylib или игровой логике.
/// </summary>
public static class ProtocolFraming {
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase) {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string SanitizePlayerName(string? rawName, string fallback = "Player", int maxLen = 24) {
        if (string.IsNullOrWhiteSpace(rawName)) return fallback;
        var sb = new StringBuilder(Math.Min(rawName.Length, maxLen));
        foreach (char c in rawName.Trim()) {
            if (sb.Length >= maxLen) break;
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') {
                sb.Append(c);
            }
        }
        string clean = sb.ToString();
        if (string.IsNullOrWhiteSpace(clean) || WindowsReservedNames.Contains(clean)) {
            return fallback;
        }
        return clean;
    }

    public static byte[] BuildPacket(byte packetType, Action<BinaryWriter> writeBody) {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write(0);
        w.Write(packetType);
        writeBody(w);
        w.Flush();

        int payloadLength = (int)ms.Length - 4;
        ms.Position = 0;
        w.Write(payloadLength);
        w.Flush();
        return ms.ToArray();
    }

    public static bool TryReadFrame(
        Stream stream,
        int maxStandardPacketSize,
        int maxLargePacketSize,
        byte largePacketType,
        bool allowLargePacket,
        out byte packetType,
        out byte[] buffer,
        out int length) {
        packetType = 0;
        buffer = Array.Empty<byte>();
        length = 0;

        Span<byte> lenBuf = stackalloc byte[4];
        int read = 0;
        while (read < 4) {
            int r = stream.Read(lenBuf.Slice(read, 4 - read));
            if (r <= 0) return false;
            read += r;
        }

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        int maxAllowed = allowLargePacket ? maxLargePacketSize : maxStandardPacketSize;
        if (payloadLength <= 0 || payloadLength > maxAllowed) return false;

        int firstByte = stream.ReadByte();
        if (firstByte < 0) return false;
        byte typeByte = (byte)firstByte;

        if ((!allowLargePacket || typeByte != largePacketType) && payloadLength > maxStandardPacketSize) {
            return false;
        }

        byte[] payload = ArrayPool<byte>.Shared.Rent(payloadLength);
        payload[0] = typeByte;
        read = 1;
        while (read < payloadLength) {
            int r = stream.Read(payload, read, payloadLength - read);
            if (r <= 0) {
                ArrayPool<byte>.Shared.Return(payload);
                return false;
            }
            read += r;
        }

        packetType = typeByte;
        buffer = payload;
        length = payloadLength;
        return true;
    }

    public static async Task<(bool Ok, byte PacketType, byte[] Buffer, int Length)> TryReadFrameAsync(
        Stream stream,
        int maxStandardPacketSize,
        int maxLargePacketSize,
        byte largePacketType,
        bool allowLargePacket,
        CancellationToken token = default) {
        byte[] lenBuf = ArrayPool<byte>.Shared.Rent(4);
        try {
            int read = 0;
            while (read < 4) {
                int r = await stream.ReadAsync(lenBuf.AsMemory(read, 4 - read), token);
                if (r <= 0) return (false, 0, Array.Empty<byte>(), 0);
                read += r;
            }

            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
            int maxAllowed = allowLargePacket ? maxLargePacketSize : maxStandardPacketSize;
            if (payloadLength <= 0 || payloadLength > maxAllowed) return (false, 0, Array.Empty<byte>(), 0);

            byte[] payload = ArrayPool<byte>.Shared.Rent(payloadLength);
            int r1 = await stream.ReadAsync(payload.AsMemory(0, 1), token);
            if (r1 <= 0) {
                ArrayPool<byte>.Shared.Return(payload);
                return (false, 0, Array.Empty<byte>(), 0);
            }
            byte typeByte = payload[0];

            if ((!allowLargePacket || typeByte != largePacketType) && payloadLength > maxStandardPacketSize) {
                ArrayPool<byte>.Shared.Return(payload);
                return (false, 0, Array.Empty<byte>(), 0);
            }

            read = 1;
            while (read < payloadLength) {
                int r = await stream.ReadAsync(payload.AsMemory(read, payloadLength - read), token);
                if (r <= 0) {
                    ArrayPool<byte>.Shared.Return(payload);
                    return (false, 0, Array.Empty<byte>(), 0);
                }
                read += r;
            }

            return (true, typeByte, payload, payloadLength);
        } finally {
            ArrayPool<byte>.Shared.Return(lenBuf);
        }
    }
}
