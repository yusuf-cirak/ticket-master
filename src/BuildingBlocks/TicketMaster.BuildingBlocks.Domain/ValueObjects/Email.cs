using System.Collections.Frozen;
using System.Text.RegularExpressions;
using YC.Monad;

namespace TicketMaster.BuildingBlocks.Domain.ValueObjects;

/// <summary>
/// A validated email address. Construct only through <see cref="Create"/>, which returns a
/// <see cref="Result{Email}"/> — a failure carries a typed <see cref="Error"/> (code + message) instead of throwing.
///
/// <para>Security posture (see the security notes on each step):</para>
/// <list type="bullet">
/// <item><b>ASCII-only.</b> Non-ASCII / IDN input is rejected outright. This makes the type immune to Unicode
/// homograph / punycode spoofing (e.g. <c>tàrget.com</c> vs <c>target.com</c>), a known source of 0-click account
/// takeovers where a confusable address collapses onto a victim's during a lookup.</item>
/// <item><b>Strict charset + structure.</b> The source-generated, non-backtracking (ReDoS-proof) regex allows only
/// <c>[a-z0-9._%+-]</c> in the local part and hostname characters in the domain, so control chars, whitespace,
/// CR/LF header-injection, quotes and XSS/SQL payloads never validate.</item>
/// <item><b>RFC 5321 length caps</b> (total 254, local 64, domain 253, label 63), checked cheaply and before any
/// allocation so a pathologically long input can't cause a DoS.</item>
/// <item><b>Canonical uniqueness.</b> <see cref="Canonical"/> is the value to use as the account-uniqueness key —
/// it neutralizes the Gmail dot/plus multi-account trick and gives ONE canonical form to use consistently across
/// registration, login and recovery (inconsistent canonicalization is the root of the takeover class above).
/// Store <see cref="Value"/> for sending; key/dedupe on <see cref="Canonical"/>.</item>
/// </list>
/// </summary>
public sealed partial record Email
{
    /// <summary>RFC 5321 maximums.</summary>
    public const int MaxLength = 254;
    public const int MaxLocalLength = 64;
    public const int MaxDomainLength = 253;
    public const int MaxLabelLength = 63;

    public string Value { get; }

    private Email(string value) => Value = value;

    public override string ToString() => Value;

    /// <summary>
    /// The account-uniqueness key. Lower-cased already; additionally neutralizes provider-specific aliasing for known
    /// providers (Gmail/Googlemail: dots in the local part are insignificant and a <c>+tag</c> suffix is a sub-address,
    /// so both are stripped and the domain folded to <c>gmail.com</c>). Other providers are left as the exact
    /// lower-cased address — we do not strip <c>+tags</c> generally, since not every provider treats them as aliases
    /// and wrongly merging two real accounts is worse than allowing a duplicate.
    /// </summary>
    public string Canonical
    {
        get
        {
            var at = Value.IndexOf('@');
            var local = Value[..at];
            var domain = Value[(at + 1)..];

            if (domain is "gmail.com" or "googlemail.com")
            {
                var plus = local.IndexOf('+');
                if (plus >= 0)
                    local = local[..plus];
                local = local.Replace(".", string.Empty);
                domain = "gmail.com";
            }

            return $"{local}@{domain}";
        }
    }

    public static Result<Email> Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Error.Create("email.empty", "Email address is required.", 400);

        // Length guard BEFORE Trim/ToLower so a huge input can't force an allocation (cheap DoS defense).
        if (input.Length > MaxLength + 2)
            return Error.Create("email.too_long", $"Email address is too long (maximum {MaxLength} characters).", 400);

        var value = input.Trim();

        if (value.Length > MaxLength)
            return Error.Create("email.too_long", $"Email address is too long (maximum {MaxLength} characters).", 400);

        // Reject non-ASCII up front: a clear, intentional anti-homograph policy (not a generic format failure).
        foreach (var c in value)
            if (c > 127)
                return Error.Create("email.non_ascii", "International (non-ASCII) email addresses are not accepted.", 400);

        value = value.ToLowerInvariant();

        if (!EmailRegex().IsMatch(value))
            return Error.Create("email.invalid_format", "Email address format is invalid.", 400);

        var at = value.IndexOf('@');
        var local = value[..at];
        var domain = value[(at + 1)..];

        if (local.Length > MaxLocalLength || domain.Length > MaxDomainLength)
            return Error.Create("email.invalid_format", "Email address format is invalid.", 400);

        foreach (var label in domain.Split('.'))
            if (label.Length > MaxLabelLength)
                return Error.Create("email.invalid_format", "Email address format is invalid.", 400);

        var tld = domain[(domain.LastIndexOf('.') + 1)..];
        if (ReservedTlds.Contains(tld))
            return Error.Create("email.reserved_tld", "Email domain has an invalid or reserved TLD.", 400);

        if (DisposableEmailDomains.Contains(domain))
            return Error.Create("email.disposable", "Disposable email addresses are not allowed.", 400);

        return new Email(value);
    }

    // Local part: alphanumeric runs separated by a single . _ % + or - (no leading/trailing/consecutive separator).
    // Domain: labels starting/ending alphanumeric (hyphen only interior), TLD 2+ letters. NonBacktracking = no ReDoS.
    [GeneratedRegex(@"^[a-z0-9]+(?:[._%+-][a-z0-9]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,}$",
        RegexOptions.NonBacktracking)]
    private static partial Regex EmailRegex();

    // RFC 2606 / Windows-reserved names: syntactically valid but never real public mail domains.
    private static readonly FrozenSet<string> ReservedTlds = new[]
    {
        "con", "prn", "aux", "nul", "lan", "local", "localhost", "test", "invalid", "example"
    }.ToFrozenSet(StringComparer.Ordinal);
}
