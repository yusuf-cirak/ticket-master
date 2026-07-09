using System.Collections.Frozen;
using System.Text.RegularExpressions;
using YC.Monad;

namespace TicketMaster.BuildingBlocks.Domain.ValueObjects;

/// <summary>
/// A validated email address. Construct only through <see cref="Create"/>, which returns a
/// <see cref="Result{Email}"/> — a failure carries a typed <see cref="Error"/> (code + message) instead of throwing.
/// The stored value is trimmed and lower-cased. Validation is format (RFC-ish, ReDoS-safe source-generated regex over
/// an allowed character set — which also rejects control chars, spaces and CR/LF/injection payloads), length
/// (≤ <see cref="MaxLength"/>), a small reserved-TLD blocklist, and a disposable-domain blocklist. There is no
/// "known TLD" allowlist by design — new TLDs appear constantly and an allowlist rejects valid addresses.
/// </summary>
public sealed partial record Email
{
    /// <summary>RFC 5321 maximum total length.</summary>
    public const int MaxLength = 254;

    public string Value { get; }

    private Email(string value) => Value = value;

    public override string ToString() => Value;

    public static Result<Email> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Error.Create("email.empty", "Email address is required.", 400);

        var value = input.Trim().ToLowerInvariant();

        if (value.Length > MaxLength)
            return Error.Create("email.too_long", $"Email address is too long (maximum {MaxLength} characters).", 400);

        if (!EmailRegex().IsMatch(value))
            return Error.Create("email.invalid_format", "Email address format is invalid.", 400);

        var domain = value[(value.IndexOf('@') + 1)..];
        var tld = domain[(domain.LastIndexOf('.') + 1)..];

        if (ReservedTlds.Contains(tld))
            return Error.Create("email.reserved_tld", "Email domain has an invalid or reserved TLD.", 400);

        if (DisposableDomains.Contains(domain))
            return Error.Create("email.disposable", "Disposable email addresses are not allowed.", 400);

        return new Email(value);
    }

    // Local part and each domain label start/end alphanumeric; TLD is 2+ letters. Only [a-z0-9._%+-@] survive,
    // so control chars, whitespace, CR/LF and injection payloads fail here. NonBacktracking = no ReDoS.
    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9._%+-]*[a-z0-9])?@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,}$",
        RegexOptions.NonBacktracking)]
    private static partial Regex EmailRegex();

    // RFC 2606 / Windows-reserved names that are syntactically valid but never real public mail domains.
    private static readonly FrozenSet<string> ReservedTlds = new[]
    {
        "con", "prn", "aux", "nul", "lan", "local", "localhost", "test", "invalid", "example"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> DisposableDomains = new[]
    {
        "10minutemail.com", "guerrillamail.com", "guerrillamailblock.com", "mailinator.com", "tempmail.org",
        "temp-mail.org", "yopmail.com", "throwaway.email", "maildrop.cc", "sharklasers.com", "pokemail.net",
        "spam4.me", "mohmal.com", "emailondeck.com", "fakeinbox.com", "trashmail.com", "dispostable.com",
        "tempail.com", "getnada.com", "armyspy.com"
    }.ToFrozenSet(StringComparer.Ordinal);
}
