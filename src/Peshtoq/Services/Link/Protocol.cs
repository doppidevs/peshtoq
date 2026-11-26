using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Peshtoq.Services.Link;

/// <summary>
/// Wire packet types. Stage 0 uses only Hello and Heartbeat; the rest are reserved
/// so the enum (and thus the protocol version) stays stable as later stages land.
/// </summary>
public enum PacketType : byte
{
    Hello = 1,
    Heartbeat = 2,

    ClipText = 10,
    ClipBlob = 11,

    // Input relay (stage 2).
    MouseMove = 20,
    MouseButton = 21,
    Wheel = 22,
    Key = 23,
    ControlAcquire = 30,
    ControlRelease = 31,
}

/// <summary>
/// Length-prefixed binary framing over a stream: [uint16 bodyLen][byte type][payload].
/// Big-endian (network order). bodyLen counts the type byte plus the payload.
/// </summary>
public static class Protocol
{
    public const byte Version = 1;
    public const int MaxFrame = 64 * 1024; // a KVM frame is tiny; cap to reject garbage/oversized input

    public static async Task WriteFrameAsync(Stream s, PacketType type, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        int bodyLen = 1 + payload.Length;
        if (bodyLen > MaxFrame) throw new InvalidOperationException("frame too large");

        var header = new byte[3];
        BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)bodyLen);
        header[2] = (byte)type;

        await s.WriteAsync(header, ct).ConfigureAwait(false);
        if (!payload.IsEmpty) await s.WriteAsync(payload, ct).ConfigureAwait(false);
        await s.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads one frame, or returns null at end of stream.</summary>
    public static async Task<(PacketType type, byte[] payload)?> ReadFrameAsync(Stream s, CancellationToken ct = default)
    {
        var len = new byte[2];
        if (!await ReadExactAsync(s, len, ct).ConfigureAwait(false)) return null;
        int bodyLen = BinaryPrimitives.ReadUInt16BigEndian(len);
        if (bodyLen < 1 || bodyLen > MaxFrame) throw new InvalidDataException("bad frame length");

        var body = new byte[bodyLen];
        if (!await ReadExactAsync(s, body, ct).ConfigureAwait(false)) return null;
        return ((PacketType)body[0], body[1..]);
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int read = 0;
        while (read < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0) return false; // peer closed
            read += n;
        }
        return true;
    }

    public static byte[] HelloPayload(string hostname)
    {
        var name = Encoding.UTF8.GetBytes(hostname);
        var buf = new byte[1 + name.Length];
        buf[0] = Version;
        name.CopyTo(buf, 1);
        return buf;
    }

    public static (byte version, string hostname) ParseHello(byte[] payload)
    {
        if (payload.Length < 1) return (0, "");
        return (payload[0], Encoding.UTF8.GetString(payload, 1, payload.Length - 1));
    }

    // ----- Input packet encode/decode (stage 2) -----

    public static byte[] MouseMove(short dx, short dy)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt16BigEndian(b, dx);
        BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(2), dy);
        return b;
    }

    public static (int dx, int dy) ReadMouseMove(byte[] p) =>
        p.Length < 4 ? (0, 0) : (BinaryPrimitives.ReadInt16BigEndian(p), BinaryPrimitives.ReadInt16BigEndian(p.AsSpan(2)));

    public static byte[] MouseButton(byte button, bool down) => new[] { button, (byte)(down ? 1 : 0) };
    public static (int button, bool down) ReadMouseButton(byte[] p) => p.Length < 2 ? (-1, false) : (p[0], p[1] != 0);

    public static byte[] Wheel(short delta, bool horizontal)
    {
        var b = new byte[3];
        BinaryPrimitives.WriteInt16BigEndian(b, delta);
        b[2] = (byte)(horizontal ? 1 : 0);
        return b;
    }

    public static (int delta, bool horizontal) ReadWheel(byte[] p) =>
        p.Length < 3 ? (0, false) : (BinaryPrimitives.ReadInt16BigEndian(p), p[2] != 0);

    public static byte[] Key(ushort vk, ushort scan, bool down, bool extended)
    {
        var b = new byte[5];
        BinaryPrimitives.WriteUInt16BigEndian(b, vk);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(2), scan);
        b[4] = (byte)((down ? 1 : 0) | (extended ? 2 : 0));
        return b;
    }

    public static (ushort vk, ushort scan, bool down, bool extended) ReadKey(byte[] p) =>
        p.Length < 5 ? ((ushort)0, (ushort)0, false, false)
        : (BinaryPrimitives.ReadUInt16BigEndian(p), BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(2)), (p[4] & 1) != 0, (p[4] & 2) != 0);
}
