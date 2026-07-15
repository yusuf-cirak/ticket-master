using System.Collections.Frozen;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace YC.BuildingBlocks.Web;

/// <summary>
/// Redacts sensitive log attributes by <b>name</b> — the OpenTelemetry-native equivalent of Serilog's
/// destructuring/sensitive-property masking, with no Serilog dependency. It walks the record's attributes once and
/// rewrites the <i>value</i> to <c>***</c> whenever the key is on the sensitive list. It never inspects or
/// pattern-matches the value content (no regex, no string surgery), so it is cheap and predictable. Keys match
/// case-insensitively; the list is kept in snake_case to match the app's own log fields (add variants if a
/// framework emits a sensitive value under a different spelling).
/// </summary>
internal sealed class SensitiveDataLogRecordProcessor : BaseProcessor<LogRecord>
{
    private const string Mask = "***";

    // Sensitive attribute names (snake_case). Add project-specific keys here as the schema grows.
    private static readonly FrozenSet<string> SensitiveKeys = new[]
    {
        "password", "pwd", "secret", "client_secret",
        "token", "access_token", "refresh_token", "id_token", "authorization",
        "api_key", "apikey", "x_api_key", "cookie", "set_cookie",
        "credit_card", "card_number", "card_no", "pan", "cvv", "cvc",
        "iban", "ssn", "tc_kimlik", "tckn", "national_id",
        "email", "phone", "phone_number", "msisdn"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static bool IsSensitive(string key) => SensitiveKeys.Contains(key);

    public override void OnEnd(LogRecord record)
    {
        if (record.Attributes is not { Count: > 0 } attributes)
            return;

        List<KeyValuePair<string, object>> rewritten = null;
        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i].Value is null || !IsSensitive(attributes[i].Key))
                continue;

            rewritten ??= new List<KeyValuePair<string, object>>(attributes);
            rewritten[i] = new KeyValuePair<string, object>(attributes[i].Key, Mask);
        }

        if (rewritten is not null)
            record.Attributes = rewritten;
    }
}
