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

    // Reserved for later stages — see docs/mouse-without-borders.md §3.
    ClipText = 10,
    ClipBlob = 11,
    MouseMove = 20,
    MouseButton = 21,
    Wheel = 22,
    Key = 23,
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
}
