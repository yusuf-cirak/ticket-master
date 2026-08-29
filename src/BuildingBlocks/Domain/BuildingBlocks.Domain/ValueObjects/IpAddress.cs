using System.Net;
using Vogen;

namespace BuildingBlocks.Domain.ValueObjects;

/// <summary>
/// An IPv4 or IPv6 address, validated on creation and stored as its canonical string form. Vogen generates value
/// equality, the System.Text.Json converter, and IParsable. (No EF converter: this lives in the persistence-agnostic
/// domain assembly, which does not reference EF Core — configure a conversion at the point of use if persisted.)
/// </summary>
[ValueObject<string>(conversions: Conversions.SystemTextJson)]
public readonly partial struct IpAddress
{
    private static string NormalizeInput(string input) =>
        IPAddress.TryParse(input?.Trim(), out var parsed) ? parsed.ToString() : input?.Trim() ?? string.Empty;

    private static Validation Validate(string value) =>
        IPAddress.TryParse(value, out _) ? Validation.Ok : Validation.Invalid("Not a valid IP address.");

    public static bool operator <(IpAddress left, IpAddress right) => left.CompareTo(right) < 0;
    public static bool operator <=(IpAddress left, IpAddress right) => left.CompareTo(right) <= 0;
    public static bool operator >(IpAddress left, IpAddress right) => left.CompareTo(right) > 0;
    public static bool operator >=(IpAddress left, IpAddress right) => left.CompareTo(right) >= 0;
}
