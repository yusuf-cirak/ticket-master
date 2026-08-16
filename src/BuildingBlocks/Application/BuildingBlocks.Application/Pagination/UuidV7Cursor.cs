#nullable enable
using Microsoft.AspNetCore.WebUtilities;

namespace BuildingBlocks.Application.Pagination;

/// <summary>
/// UUIDv7 (RFC 9562) millisecond-boundary construction, plus the opaque cursor wire format this pagination building
/// block uses for RFC 9865: a 7-byte payload — 1 direction byte + a 6-byte big-endian Unix-millisecond timestamp —
/// base64url-encoded via ASP.NET Core's own <see cref="WebEncoders"/> (no hand-rolled base64url). The direction byte
/// lets a single decode tell the caller whether to query <c>&gt;</c> (forward) or <c>&lt;</c> (backward), even
/// though the client only ever echoes back an opaque string (RFC 9865: cursors are opaque to the client) — it never
/// needs to know which direction it is carrying.
/// <para>
/// Every id in this repo is minted via <c>Guid.CreateVersion7()</c>, which embeds a sortable 48-bit millisecond
/// timestamp in its top bits. A range scan on the existing primary-key index (<c>id &gt; boundary</c>) is therefore
/// already time-ordered — no extra index, no COUNT query.
/// </para>
/// </summary>
public static class UuidV7Cursor
{
    private const byte ForwardMarker = (byte)'n'; // "next" — id > boundary
    private const byte BackwardMarker = (byte)'p'; // "previous" — id < boundary

    public static string EncodeCursor(long unixMs, bool forward)
    {
        var payload = new byte[7];
        payload[0] = forward ? ForwardMarker : BackwardMarker;
        WriteUInt48BigEndian(payload.AsSpan(1), unixMs);
        return WebEncoders.Base64UrlEncode(payload);
    }

    public static bool TryDecodeCursor(string? cursor, out long unixMs, out bool forward)
    {
        unixMs = 0;
        forward = true;

        if (cursor is null)
            return false;

        byte[] payload;
        try
        {
            payload = WebEncoders.Base64UrlDecode(cursor);
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload.Length != 7)
            return false;

        switch (payload[0])
        {
            case ForwardMarker:
                forward = true;
                break;
            case BackwardMarker:
                forward = false;
                break;
            default:
                return false;
        }

        unixMs = ReadUInt48BigEndian(payload.AsSpan(1));
        return true;
    }

    /// <summary>The smallest possible UUIDv7 minted at <paramref name="unixMs"/> (all random bits zero) — a real row
    /// sharing that exact millisecond is always <c>&gt;</c> this. Used as the "previous"/backward query boundary
    /// (<c>id &lt; boundary</c>): it always excludes the boundary's own source row (see <see cref="CursorRequest"/>).</summary>
    public static Guid MinAtMillisecond(long unixMs) => Build(unixMs, randA: 0, randBTop6: 0, randBTail: 0);

    /// <summary>The largest possible UUIDv7 minted at <paramref name="unixMs"/> (all random bits one) — a real row
    /// sharing that exact millisecond is always <c>&lt;</c> this. Used as the "next"/forward query boundary
    /// (<c>id &gt; boundary</c>): it always excludes the boundary's own source row (see <see cref="CursorRequest"/>).</summary>
    public static Guid MaxAtMillisecond(long unixMs) =>
        Build(unixMs, randA: 0xFFF, randBTop6: 0b0011_1111, randBTail: 0xFFFF_FFFF_FFFF_FF);

    /// <summary>Extracts the embedded Unix-millisecond timestamp from a UUIDv7 value.</summary>
    public static long ExtractMillisecond(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        return ReadUInt48BigEndian(bytes);
    }

    // RFC 9562 UUIDv7 layout (16 bytes, big-endian): ts_ms(48) | ver(4)=0111 | rand_a(12) | var(2)=10 | rand_b(62).
    private static Guid Build(long unixMs, int randA, int randBTop6, ulong randBTail)
    {
        Span<byte> bytes = stackalloc byte[16];
        WriteUInt48BigEndian(bytes, unixMs);
        bytes[6] = (byte)(0x70 | ((randA >> 8) & 0x0F)); // version nibble 0111 + top 4 bits of rand_a
        bytes[7] = (byte)randA;                          // low 8 bits of rand_a
        bytes[8] = (byte)(0x80 | (randBTop6 & 0b0011_1111)); // variant bits 10 + top 6 bits of rand_b
        for (var i = 0; i < 7; i++)                          // remaining 56 bits of rand_b
            bytes[9 + i] = (byte)(randBTail >> (8 * (6 - i)));

        return new Guid(bytes, bigEndian: true);
    }

    private static void WriteUInt48BigEndian(Span<byte> destination, long value)
    {
        destination[0] = (byte)(value >> 40);
        destination[1] = (byte)(value >> 32);
        destination[2] = (byte)(value >> 24);
        destination[3] = (byte)(value >> 16);
        destination[4] = (byte)(value >> 8);
        destination[5] = (byte)value;
    }

    private static long ReadUInt48BigEndian(ReadOnlySpan<byte> source) =>
        ((long)source[0] << 40) | ((long)source[1] << 32) | ((long)source[2] << 24) |
        ((long)source[3] << 16) | ((long)source[4] << 8) | source[5];
}
