using System.Diagnostics;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RedisRateLimiting;
using StackExchange.Redis;

namespace TicketMaster.BuildingBlocks.RateLimiting;

/// <summary>
/// Rate limiting with a burst window + fixed soft-ban. Counters (and the ban) live in Redis in
/// <see cref="RateLimitingMode.Distributed"/> — the single source of truth across N instances — or per-node
/// in-memory in <see cref="RateLimitingMode.Local"/>. When the Redis master is unreachable the limiter degrades
/// <b>fail-open</b> to a per-node in-memory limiter (still bounded, never a hard outage) and emits the
/// <c>ratelimit.fallback</c> metric; the ban store fails open too. Exceeding the burst window arms a fixed ban that
/// outlives the sliding window, so a caller stays 429 for the full ban duration. Rejections are RFC-7807
/// ProblemDetails with a <c>traceId</c> and a <c>Retry-After</c> header.
/// </summary>
public static class RateLimitingExtensions
{
    private const string BurstPolicy = "burst";
    private const string RetryAfterItemKey = "__ratelimit_retry_after";

    public static WebApplicationBuilder AddRateLimiting(this WebApplicationBuilder builder, Action<RateLimitingOptions> configure = null)
    {
        var section = builder.Configuration.GetSection(RateLimitingOptions.SectionName);
        var serviceName = builder.Configuration["ServiceName"] ?? builder.Environment.ApplicationName;

        var optionsBuilder = builder.Services.AddOptions<RateLimitingOptions>()
            .Bind(section);
        if (configure is not null)
            optionsBuilder.Configure(configure); // code-based config wins over the appsettings section
        optionsBuilder
            .PostConfigure(o => o.KeyPrefix ??= serviceName)
            .Validate(o => o.BurstBan.Duration >= TimeSpan.FromSeconds(10),
                "RateLimiting:BurstBan:Duration must be at least 10 seconds.")
            .Validate(o => o.Adaptive.MinLimit >= 1
                    && o.Adaptive.MaxLimit >= o.Adaptive.MinLimit
                    && o.Adaptive.Step >= 1
                    && o.Adaptive.HealthyThreshold > 0
                    && o.Adaptive.HealthyThreshold <= o.Adaptive.OverloadThreshold
                    && o.Adaptive.OverloadThreshold <= 1
                    && o.Adaptive.DecreaseFactor is > 0 and < 1
                    && o.Adaptive.ScoreSmoothing is > 0 and <= 1,
                "RateLimiting:Adaptive limits/thresholds are invalid (need 1≤Min≤Max, Step≥1, 0<Healthy≤Overload≤1, 0<DecreaseFactor<1, 0<ScoreSmoothing≤1).")
            .ValidateOnStart();

        builder.Services.AddSingleton<PrincipalPartitioner>();
        builder.Services.AddSingleton<RateLimitPolicyStore>();
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Eagerly materialize options to branch DI (Mode: Local vs Distributed) at registration time; apply the same
        // code-based override so the two paths (this and the IOptions pipeline above) agree.
        var options = section.Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        configure?.Invoke(options);

        if (options.Mode == RateLimitingMode.Local)
            builder.Services.AddSingleton<IBurstBanStore, LocalBurstBanStore>();
        else
            builder.Services.AddSingleton<IBurstBanStore, RedisBurstBanStore>();

        // Layer 2 — system-wide adaptive admission control (AIMD). Enforcement is ALWAYS the in-process gate; the
        // Mode only picks the load SIGNAL: Local = this process, Distributed = fleet max via Redis (see AdaptiveOptions).
        builder.Services.AddSingleton<RequestMetricsCollector>();
        builder.Services.AddSingleton<LocalLoadSignal>();
        if (options.Mode == RateLimitingMode.Distributed)
            builder.Services.AddSingleton<ILoadSignal, DistributedLoadSignal>();
        else
            builder.Services.AddSingleton<ILoadSignal>(sp => sp.GetRequiredService<LocalLoadSignal>());
        builder.Services.AddSingleton<LoadSignalState>();
        builder.Services.AddSingleton(new AdmissionLimit(options.Adaptive.MaxLimit));
        builder.Services.AddSingleton<IAdmissionGate, InProcAdmissionGate>();
        builder.Services.AddHostedService<AdaptiveAdmissionController>();

        builder.Services.AddRateLimiter(limiter =>
        {
            foreach (var policyName in options.Policies.Keys)
            {
                var name = policyName;
                limiter.AddPolicy(name, httpContext => BuildPartition(name, httpContext));
            }

            limiter.OnRejected = OnRejected;
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return builder;
    }

    public static WebApplication UseRateLimiting(this WebApplication app)
    {
        var metrics = app.Services.GetRequiredService<RequestMetricsCollector>();
        var gate = app.Services.GetRequiredService<IAdmissionGate>();

        // Outermost: time every request and record latency + 5xx rate, feeding the adaptive load signal. Runs on the
        // whole pipeline so it captures true end-to-end latency (including any short-circuited 429/503).
        app.Use(async (context, next) =>
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                metrics.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, context.Response.StatusCode);
            }
        });

        // Runs BEFORE the rate limiter consumes a permit: a banned caller is short-circuited with 429 regardless of
        // whether the sliding window has since reset. Only endpoints carrying the burst policy metadata are checked
        // (health/docs stay untouched). Routing has already run in a WebApplication pipeline, so GetEndpoint() is set.
        app.Use(async (context, next) =>
        {
            var meta = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>();
            if (meta?.PolicyName != BurstPolicy)
            {
                await next(context);
                return;
            }

            var key = BurstBanScope(context);
            var store = context.RequestServices.GetRequiredService<IBurstBanStore>();
            var remaining = await store.GetRemainingBanAsync(key, context.RequestAborted);

            if (remaining is { } ban)
            {
                await Write429Async(context, ban, context.RequestAborted);
                return;
            }

            await next(context);
        });

        app.UseRateLimiter();

        // Runs AFTER the rate limiter (per-caller shedding first) but before the endpoint: the system-wide AIMD gate.
        // Same burst-policy endpoints as Layer 1 (health/docs skipped). Over the current admission limit ⇒ 503.
        app.Use(async (context, next) =>
        {
            var meta = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>();
            if (meta?.PolicyName != BurstPolicy)
            {
                await next(context);
                return;
            }

            if (!gate.TryEnter(out var lease))
            {
                await Write503Async(context, context.RequestAborted);
                return;
            }

            using (lease)
                await next(context);
        });

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

        // Retry-After hint for OnRejected: a window-based policy frees a permit after at most one window.
        httpContext.Items[RetryAfterItemKey] = policy.Window;

        if (!mux.IsConnected)
        {
            RateLimitingMeter.Fallback.Add(1);
            return LocalFallback(key, policy);
        }

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
    private static RateLimitPartition<string> LocalFallback(string key, ResolvedPolicy policy)
    {
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

    // The burst-ban scope: principal + route (the same bucket the burst limiter breached), WITHOUT the limiter's
    // key prefix — the ban store adds its own "{prefix}:ban:" prefix, so this stays a clean single-namespaced key.
    // Shared by OnRejected (arm) and the ban middleware (enforce).
    private static string BurstBanScope(HttpContext context)
    {
        var partitioner = context.RequestServices.GetRequiredService<PrincipalPartitioner>();
        var (principalId, _) = partitioner.ResolvePrincipal(context);
        return $"{principalId}|{PrincipalPartitioner.RouteScope(context)}";
    }

    private static async ValueTask OnRejected(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        // Prefer the limiter's own metadata; fall back to the policy window stashed by BuildPartition; else 1s.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var meta)
            ? meta
            : httpContext.Items.TryGetValue(RetryAfterItemKey, out var stashed) && stashed is TimeSpan window
                ? window
                : TimeSpan.FromSeconds(1);

        // Exceeding the burst window arms a fixed soft-ban that outlives the sliding window.
        var options = httpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimitingOptions>>().Value;
        var store = httpContext.RequestServices.GetRequiredService<IBurstBanStore>();
        var banDuration = options.BurstBan.Duration;
        await store.BanAsync(BurstBanScope(httpContext), banDuration, cancellationToken);
        RateLimitingMeter.Banned.Add(1);

        // The caller should not retry before the ban lifts.
        await Write429Async(httpContext, banDuration > retryAfter ? banDuration : retryAfter, cancellationToken);
    }

    // The shared RFC-7807 429 shape used by both the rate limiter's OnRejected and the ban middleware.
    private static async Task Write429Async(HttpContext httpContext, TimeSpan retryAfter, CancellationToken ct)
    {
        RateLimitingMeter.Rejected.Add(1);

        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.RetryAfter =
            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Type = "https://datatracker.ietf.org/doc/html/rfc6585#section-4"
        };
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString();

        await response.WriteAsJsonAsync(problem, ct);
    }

    // The RFC-7807 503 shape emitted by the adaptive admission gate when the system is shedding load.
    private static async Task Write503Async(HttpContext httpContext, CancellationToken ct)
    {
        RateLimitingMeter.AdaptiveRejected.Add(1);

        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        response.Headers.RetryAfter = "1";

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Service Unavailable",
            Detail = "System under load",
            Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.4"
        };
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString();

        await response.WriteAsJsonAsync(problem, ct);
    }
}
