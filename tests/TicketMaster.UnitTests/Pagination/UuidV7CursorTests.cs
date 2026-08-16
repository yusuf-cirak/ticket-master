#nullable enable
using FluentAssertions;
using BuildingBlocks.Application.Pagination;
using Xunit;

namespace TicketMaster.UnitTests.Pagination;

public class UuidV7CursorTests
{
    private static readonly DateTimeOffset Timestamp = new(2030, 6, 15, 12, 30, 45, 123, TimeSpan.Zero);
    private static readonly long UnixMs = Timestamp.ToUnixTimeMilliseconds();

    [Fact]
    public void MinAtMillisecond_has_the_uuidv7_version_and_variant_bits_set()
    {
        var guid = UuidV7Cursor.MinAtMillisecond(UnixMs);
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);

        (bytes[6] >> 4).Should().Be(0x7, "the version nibble must be 0111");
        (bytes[8] >> 6).Should().Be(0b10, "the variant bits must be 10");
    }

    [Fact]
    public void MaxAtMillisecond_has_the_uuidv7_version_and_variant_bits_set()
    {
        var guid = UuidV7Cursor.MaxAtMillisecond(UnixMs);
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);

        (bytes[6] >> 4).Should().Be(0x7);
        (bytes[8] >> 6).Should().Be(0b10);
    }

    [Fact]
    public void ExtractMillisecond_round_trips_MinAtMillisecond_and_MaxAtMillisecond()
    {
        UuidV7Cursor.ExtractMillisecond(UuidV7Cursor.MinAtMillisecond(UnixMs)).Should().Be(UnixMs);
        UuidV7Cursor.ExtractMillisecond(UuidV7Cursor.MaxAtMillisecond(UnixMs)).Should().Be(UnixMs);
    }

    [Fact]
    public void ExtractMillisecond_matches_a_real_Guid_CreateVersion7_value()
    {
        // The strongest correctness check: our hand-rolled byte layout must agree with .NET's own UUIDv7 minting.
        var real = Guid.CreateVersion7(Timestamp);

        UuidV7Cursor.ExtractMillisecond(real).Should().Be(UnixMs);
    }

    [Fact]
    public void MinAtMillisecond_is_less_than_any_real_row_at_the_same_millisecond()
    {
        var min = UuidV7Cursor.MinAtMillisecond(UnixMs);
        var real = Guid.CreateVersion7(Timestamp);

        min.CompareTo(real).Should().BeLessThan(0);
    }

    [Fact]
    public void MaxAtMillisecond_is_greater_than_any_real_row_at_the_same_millisecond()
    {
        var max = UuidV7Cursor.MaxAtMillisecond(UnixMs);
        var real = Guid.CreateVersion7(Timestamp);

        max.CompareTo(real).Should().BeGreaterThan(0);
    }

    [Fact]
    public void MaxAtMillisecond_of_an_earlier_ms_is_still_less_than_MinAtMillisecond_of_a_later_ms()
    {
        // Temporal ordering must dominate the random tail — the whole premise of "no extra index needed".
        var earlier = UuidV7Cursor.MaxAtMillisecond(UnixMs);
        var later = UuidV7Cursor.MinAtMillisecond(UnixMs + 1);

        earlier.CompareTo(later).Should().BeLessThan(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EncodeCursor_and_TryDecodeCursor_round_trip_the_millisecond_and_direction(bool forward)
    {
        var cursor = UuidV7Cursor.EncodeCursor(UnixMs, forward);

        UuidV7Cursor.TryDecodeCursor(cursor, out var decodedMs, out var decodedForward).Should().BeTrue();
        decodedMs.Should().Be(UnixMs);
        decodedForward.Should().Be(forward);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-cursor!!")]
    [InlineData("dG9v")] // valid base64url but the wrong payload length
    public void TryDecodeCursor_rejects_malformed_input(string? raw)
    {
        UuidV7Cursor.TryDecodeCursor(raw, out _, out _).Should().BeFalse();
    }
}
