using System.Security.Claims;
using Scalar.AspNetCore;
using BuildingBlocks.Modularity;
using BuildingBlocks.Configuration.Secrets;
using BuildingBlocks.Caching.FusionCache;
using BuildingBlocks.Caching.Redis;
using BuildingBlocks.Locking;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Web;
using BuildingBlocks.Messaging.Kafka;
using TicketMaster.Modules.Platform;
using TicketMaster.Modules.Iam;
using TicketMaster.Modules.Events;
using BuildingBlocks.Persistence.Outbox;
using BuildingBlocks.Security.Authorization;
using TicketMaster.AppHost;

var builder = WebApplication.CreateBuilder(args);

// Decrypt `secret:`-prefixed config values (AES-GCM, key derived from CONFIG_SECRET_KEY + app salt); reload-aware.
builder.Configuration.AddSecretConfiguration();

// UTC + structured logging + OpenTelemetry + health checks + correlation id.
builder.AddServiceDefaults();

// Shared sentinel-aware Redis connection + FusionCache (L1/L2) + distributed rate limiting.
builder.AddRedisInfrastructure();
builder.AddRedisLocking();
builder.AddAppCache();
builder.AddRateLimiting();

// Named opt-in; each module's services + endpoints + MediatR/validators are auto-discovered (IModule).
builder.AddModules(m => m
    .Add<PlatformModule>()
    .Add<IamModule>()
    .Add<EventsModule>());
builder.Services.AddOpenApi();

// Source-gen JSON metadata first, reflection fallback second — precompiled metadata for the hot boundary DTOs
// without losing the shared runtime JSON contract (AddJsonDefaults) or breaking on unlisted types.
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

var bootstrap = builder.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

// Outbox relay: Debezium (default, CDC) or Polling (the app claims + produces to Kafka — no Debezium/CDC needed). Each
// module self-registers polling for its own DbContext (AddOutboxPollingIfEnabled); the host only wires the shared publisher.
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
if (builder.Configuration.GetValue<OutboxRelay>("Outbox:Relay") == OutboxRelay.Polling)
    builder.Services.AddKafkaOutboxPublisher(bootstrap);

// Kafka rider wired generically from the modules that consume Kafka — the host names no consumer or topic. A new
// Kafka-consuming module implements IKafkaConsumerModule and is picked up here with no host change.
var kafkaModules = builder.Services.GetModuleRegistry().Modules.OfType<IKafkaConsumerModule>().ToList();
builder.Services.AddModuleMessaging(bootstrap, kafkaModules);

var app = builder.Build();

app.UseServiceDefaults(); // correlation id (+ trace baggage) + request logging
app.UseRateLimiting();    // distributed rate limiter, after correlation so 429s carry correlation_id

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapDefaultEndpoints(); // /health/liveness, /health/readiness, /health

// Business endpoints opt into the burst policy; health/docs stay unthrottled.
var api = app.MapGroup("/api").RequireApiRateLimiting(); // burst (per-caller) + adaptive (system-wide) on all /api endpoints
api.MapModuleEndpoints();

// Diagnostic identity probe — scope-protected; exercises JWT auth + user_id/client_ip log enrichment.
api.MapGet("/whoami", (ClaimsPrincipal user) =>
{
    var scopes = user.FindAll("scope").Concat(user.FindAll("scp"))
        .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Distinct()
        .ToArray();
    
    return Results.Ok(new { sub = user.FindFirst("sub")?.Value, scopes });
}).RequireScope("platform:read");

app.Run();

public partial class Program; // for WebApplicationFactory in integration tests
