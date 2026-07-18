using System.Security.Claims;
using MassTransit;
using Scalar.AspNetCore;
using BuildingBlocks.Security;
using BuildingBlocks.Modularity;
using BuildingBlocks.Configuration.Secrets;
using BuildingBlocks.Caching.FusionCache;
using BuildingBlocks.Caching.Redis;
using BuildingBlocks.Locking;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Web;
using BuildingBlocks.Messaging.Kafka;
using TicketMaster.Modules.Platform;
using TicketMaster.Modules.Platform.Contracts;
using TicketMaster.Modules.Iam;
using TicketMaster.Modules.Events;
using BuildingBlocks.Security.Authorization;

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

var bootstrap = builder.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
var platformTopic = builder.Configuration["Kafka:PlatformTopic"] ?? "tm.platform.events";

builder.Services.AddMassTransit(x =>
{
    // Inert, but required: MassTransit 8's Kafka rider must attach to a bus; nothing flows through it
    // (integration events arrive via the outbox → Debezium → Kafka and are consumed by the rider below).
    x.UsingInMemory();
    x.AddRider(rider =>
    {
        rider.AddConsumer<PingConsumer>();
        rider.UsingKafka((context, k) =>
        {
            k.Host(bootstrap);
            k.TopicEndpoint<PingIntegrationEvent>(platformTopic, "platform", e =>
            {
                e.ApplyDefaults(context); // offset, raw-JSON, context-propagation filter, retry
                e.ConfigureConsumer<PingConsumer>(context);
            });
        });
    });
});

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
