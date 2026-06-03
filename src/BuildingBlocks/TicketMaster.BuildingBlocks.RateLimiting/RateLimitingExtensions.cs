using System.Diagnostics;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RedisRateLimiting;
using StackExchange.Redis;

namespace TicketMaster.BuildingBlocks.RateLimiting;

/// <summary>
/// Distributed rate limiting. Counters live in Redis (atomic Lua via RedisRateLimiting) — the single source of
/// truth, so the limit holds across N instances. Per-principal policy/limit lookups go through FusionCache
/// (L1→L2). When the Redis master is unreachable the limiter degrades <b>fail-open</b> to a per-node in-memory
/// limiter (still bounded, never a hard outage) and emits the <c>ratelimit.fallback</c> metric. Rejections are
/// RFC-7807 ProblemDetails with a <c>traceId</c> and a <c>Retry-After</c> header.
/// </summary>
public static class RateLimitingExtensions
{
    private const string ConcurrencyPolicy = "concurrency";
    private const string RetryAfterItemKey = "__ratelimit_retry_after";

    public static WebApplicationBuilder AddRateLimiting(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(RateLimitingOptions.SectionName);
        var serviceName = builder.Configuration["ServiceName"] ?? builder.Environment.ApplicationName;

        builder.Services.AddOptions<RateLimitingOptions>()
            .Bind(section)
            .PostConfigure(o => o.KeyPrefix ??= serviceName);

        builder.Services.AddSingleton<PrincipalPartitioner>();
        builder.Services.AddSingleton<RateLimitPolicyStore>();

        var options = section.Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        builder.Services.AddRateLimiter(limiter =>
        {
            foreach (var policyName in options.Policies.Keys)
            {
                var name = policyName;
                limiter.AddPolicy(name, httpContext => BuildPartition(name, httpContext));
            }

            // Coarse per-IP safety net for un-attributed traffic (independent of the named policies).
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var mux = httpContext.RequestServices.GetRequiredService<IConnectionMultiplexer>();
                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var key = $"{serviceName}:ratelimit:global:ip:{ip}";
                return mux.IsConnected
                    ? RedisRateLimitPartition.GetFixedWindowRateLimiter(key, _ => new RedisFixedWindowRateLimiterOptions
                    {
                        ConnectionMultiplexerFactory = () => mux,
                        PermitLimit = 600,
                        Window = TimeSpan.FromMinutes(1)
                    })
                    : LocalFallback(key, new ResolvedPolicy(600, TimeSpan.FromMinutes(1), 1, 0), isConcurrency: false);
            });

            limiter.OnRejected = OnRejected;
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return builder;
    }

    public static WebApplication UseRateLimiting(this WebApplication app)
    {
        app.UseRateLimiter();
        return app;
    }

    private static RateLimitPartition<string> BuildPartition(string policyName, HttpContext httpContext)
    {
        var services = httpContext.RequestServices;
        var mux = services.GetRequiredService<IConnectionMultiplexer>();
        var partitioner = services.GetRequiredService<PrincipalPartitioner>();
        var store = services.GetRequiredService<RateLimitPolicyStore>();

        var (principalId, tier) = partitioner.ResolvePrincipal(httpContext);
        var policy = store.Resolve(policyName, httpContext, principalId, tier);
        // Key on the matched route TEMPLATE, not the raw URL path: /api/items/1 and /api/items/2 must share a
        // bucket, otherwise path iteration multiplies a principal's effective limit.
        var key = partitioner.PartitionKey(policyName, principalId, PrincipalPartitioner.RouteScope(httpContext));
        var isConcurrency = string.Equals(policyName, ConcurrencyPolicy, StringComparison.OrdinalIgnoreCase);

        // Retry-After hint for OnRejected: a window-based policy frees a permit after at most one window.
        if (!isConcurrency)
            httpContext.Items[RetryAfterItemKey] = policy.Window;

        if (!mux.IsConnected)
        {
            RateLimitingMeter.Fallback.Add(1);
            return LocalFallback(key, policy, isConcurrency);
        }

        if (isConcurrency)
            return RedisRateLimitPartition.GetConcurrencyRateLimiter(key, _ => new RedisConcurrencyRateLimiterOptions
            {
                ConnectionMultiplexerFactory = () => mux,
                PermitLimit = policy.PermitLimit
            });

        if (policy.SegmentsPerWindow > 1)
            return RedisRateLimitPartition.GetSlidingWindowRateLimiter(key, _ => new RedisSlidingWindowRateLimiterOptions
            {
                ConnectionMultiplexerFactory = () => mux,
                PermitLimit = policy.PermitLimit,
                Window = policy.Window
            });

        return RedisRateLimitPartition.GetFixedWindowRateLimiter(key, _ => new RedisFixedWindowRateLimiterOptions
        {
            ConnectionMultiplexerFactory = () => mux,
            PermitLimit = policy.PermitLimit,
            Window = policy.Window
        });
    }

    // Per-node in-memory limiter used when Redis is unreachable (degraded but still bounded).
    private static RateLimitPartition<string> LocalFallback(string key, ResolvedPolicy policy, bool isConcurrency)
    {
        if (isConcurrency)
            return RateLimitPartition.GetConcurrencyLimiter(key, _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = policy.PermitLimit,
                QueueLimit = 0
            });

        if (policy.SegmentsPerWindow > 1)
            return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = policy.PermitLimit,
                Window = policy.Window,
                SegmentsPerWindow = policy.SegmentsPerWindow,
                QueueLimit = 0
            });

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = policy.PermitLimit,
            Window = policy.Window,
            QueueLimit = 0
        });
    }

    private static async ValueTask OnRejected(OnRejectedContext context, CancellationToken cancellationToken)
    {
        RateLimitingMeter.Rejected.Add(1);

        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        // Prefer the limiter's own metadata; fall back to the policy window stashed by BuildPartition; else 1s.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var meta)
            ? meta
            : context.HttpContext.Items.TryGetValue(RetryAfterItemKey, out var stashed) && stashed is TimeSpan window
                ? window
                : TimeSpan.FromSeconds(1);
        response.Headers.RetryAfter =
            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Type = "https://datatracker.ietf.org/doc/html/rfc6585#section-4"
        };
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString();

        await response.WriteAsJsonAsync(problem, cancellationToken);
    }
}
