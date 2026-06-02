using System.Collections.Concurrent;
using System.Text;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Converts log attribute key names (PascalCase / camelCase, acronym-aware) to snake_case, the
/// OpenTelemetry semantic-convention casing. Conversions are cached because the same template
/// property names recur on every log call (hot path).
/// </summary>
internal static class SnakeCase
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Convert(string name) =>
        string.IsNullOrEmpty(name) ? name : Cache.GetOrAdd(name, Transform);

    private static string Transform(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var prev = i > 0 ? name[i - 1] : '\0';
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                // Boundary before an uppercase letter when the previous char is lower/digit
                // (camel boundary) or it ends an acronym that flows into a new word (HTTPStatus).
                var boundary = i > 0 &&
                    (char.IsLower(prev) || char.IsDigit(prev) ||
                     (char.IsUpper(prev) && char.IsLower(next)));
                if (boundary && sb.Length > 0 && sb[^1] != '_') sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
