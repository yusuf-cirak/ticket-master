using System.Collections.Frozen;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;

namespace BuildingBlocks.Observability;

/// <summary>
/// Logs the request query string with sensitive parameter <i>values</i> redacted, instead of the framework's
/// wholesale <see cref="HttpLoggingFields.RequestQuery"/> (which would leak OAuth codes/tokens, signed-URL
/// signatures, API keys, …). Parameter names are kept (useful for debugging: paging, filters); only the values of
/// known-sensitive names become <c>***</c>. Matching is case-insensitive.
/// </summary>
public sealed class SensitiveQueryLogInterceptor : IHttpLoggingInterceptor
{
    private const string Mask = "***";

    private static readonly FrozenSet<string> SensitiveParameters = new[]
    {
        "access_token", "id_token", "refresh_token", "token", "code", "client_secret", "secret",
        "api_key", "apikey", "key", "signature", "sig", "password", "pwd", "email", "phone"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logContext)
    {
        var request = logContext.HttpContext.Request;
        if (request.QueryString.HasValue)
            logContext.AddParameter("query", Redact(request.Query));

        return ValueTask.CompletedTask;
    }

    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext) => ValueTask.CompletedTask;

    internal static string Redact(IQueryCollection query)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in query)
        {
            if (sb.Length > 0)
                sb.Append('&');
            sb.Append(key).Append('=').Append(SensitiveParameters.Contains(key) ? Mask : value.ToString());
        }

        return sb.ToString();
    }
}
