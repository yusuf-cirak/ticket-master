using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using TicketMaster.Modules.Platform;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// Starts Postgres (wal_level=logical) + Kafka + Debezium Connect on a shared Docker network,
/// boots the API via WebApplicationFactory, applies EF migrations, and registers the Debezium
/// Outbox Event Router connector so the spine is live for the test(s).
/// </summary>
public sealed class SpineFixture : IAsyncLifetime
{
    // ── containers ──────────────────────────────────────────────────────────
    private INetwork _network = null!;
    private PostgreSqlContainer _postgres = null!;
    private KafkaContainer _kafka = null!;
    private IContainer _connect = null!;
    private RedisContainer _redis = null!;

    // ── public surface for tests ─────────────────────────────────────────────
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    // ── connection strings visible to tests ───────────────────────────────────
    public string PostgresConnectionString { get; private set; } = null!;
    public string RedisConnectionString { get; private set; } = null!;

    // ── JWT (symmetric HS256): the test app validates against these; mirrored into Authentication:* below ──
    public const string JwtIssuer = "ticketmaster-test";
    public const string JwtAudience = "ticketmaster-api";
    private const string JwtSigningKey = "integration-test-signing-key-min-32-bytes-aaaaaaaa";

    /// <summary>Mints a short-lived HS256 token the booted app accepts, optionally carrying scopes.</summary>
    public string MintToken(params string[] scopes)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = JwtIssuer,
            Audience = JwtAudience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = "test-user-1", ["jti"] = Guid.NewGuid().ToString("N") }
        };
        if (scopes.Length > 0)
            descriptor.Claims["scope"] = string.Join(' ', scopes);

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    // ── constants ────────────────────────────────────────────────────────────
    private const string PgAlias = "postgres";
    private const string KafkaAlias = "kafka";

    // Debezium Connect REST port
    private const int ConnectRestPort = 8083;

    // In-network Kafka listener that Debezium Connect (and any future in-network service) uses.
    // The KafkaBuilder.WithListener("kafka:29092") call adds this listener AND registers "kafka"
    // as a network alias so Connect can resolve it by name.
    private const string KafkaInNetworkListener = $"{KafkaAlias}:29092";

    // ─────────────────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        // 1. Shared bridge network
        _network = new NetworkBuilder()
            .Build();
        await _network.CreateAsync();

        // 2. Postgres with wal_level=logical (required by Debezium pgoutput plugin)
        _postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("ticketmaster")
            .WithUsername("tm")
            .WithPassword("tm")
            .WithCommand("-c", "wal_level=logical")
            .WithNetwork(_network)
            .WithNetworkAliases(PgAlias)
            .Build();

        // 3. Kafka (KRaft, no ZooKeeper) with an extra in-network listener for Connect.
        //    WithListener("kafka:29092") adds:
        //      - an advertised listener on kafka:29092 reachable inside the Docker network
        //      - "kafka" as a network alias automatically (per Testcontainers.Kafka docs)
        _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.7.1")
            .WithKRaft()
            .WithListener(KafkaInNetworkListener)
            .WithNetwork(_network)
            .WithNetworkAliases(KafkaAlias)
            .Build();

        // Single-node Redis for the rate limiter's distributed counter (counter correctness needs no Sentinel).
        _redis = new RedisBuilder("redis:7-alpine").Build();

        // Start Postgres + Kafka + Redis in parallel
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync(), _redis.StartAsync());

        // 4. Debezium Kafka Connect
        //    - Reaches Kafka at kafka:29092 (in-network)
        //    - Expose REST API on 8083
        //    UntilHttpRequestIsSucceeded signature: (Func<HttpWaitStrategy, HttpWaitStrategy>, Action<IWaitStrategy>)
        //    Pinned for reproducibility; docker-compose runs debezium/connect:latest per project
        //    directive. The test loads the prod connector JSON verbatim (see RegisterOutboxConnectorAsync),
        //    so config drift is caught here even though the engine version differs from prod.
        _connect = new ContainerBuilder("debezium/connect:2.5")
            .WithNetwork(_network)
            .WithNetworkAliases("connect")
            .WithEnvironment("GROUP_ID", "1")
            .WithEnvironment("BOOTSTRAP_SERVERS", KafkaInNetworkListener)
            .WithEnvironment("CONFIG_STORAGE_TOPIC", "connect.configs")
            .WithEnvironment("OFFSET_STORAGE_TOPIC", "connect.offsets")
            .WithEnvironment("STATUS_STORAGE_TOPIC", "connect.status")
            .WithPortBinding(ConnectRestPort, assignRandomHostPort: true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(
                        r => r.ForPath("/connector-plugins")
                              .ForPort((ushort)ConnectRestPort)
                              .ForStatusCode(System.Net.HttpStatusCode.OK),
                        ws => ws.WithTimeout(TimeSpan.FromMinutes(3))))
            .Build();

        await _connect.StartAsync();

        // 5. Boot the API via WebApplicationFactory, pointing at our containers.
        //    Must happen BEFORE migrations so we can resolve PlatformDbContext.
        PostgresConnectionString = _postgres.GetConnectionString();
        RedisConnectionString = $"{_redis.GetConnectionString()},abortConnect=false";
        var kafkaBootstrap = _kafka.GetBootstrapAddress();

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseSetting("ConnectionStrings:Postgres", PostgresConnectionString);
                host.UseSetting("Kafka:BootstrapServers", kafkaBootstrap);
                // Keep topic name consistent with Debezium routing
                host.UseSetting("Kafka:PlatformTopic", "tm.platform.events");
                // Standalone Redis for the rate limiter (no Sentinel in tests).
                host.UseSetting("Redis:Mode", "Standalone");
                host.UseSetting("Redis:ConnectionString", RedisConnectionString);
                // Keep the rate limiter Redis-authoritative in tests so a FlushDatabase fully resets it. The two-tier
                // local pre-limiter (prod default) keeps per-node window state that FlushDatabase can't reach, which
                // would leak across the assembly's shared host; it is covered by unit tests instead.
                host.UseSetting("RateLimiting:LocalPreLimit", "false");
                // No SigNoz in the test/CI environment — don't spin up OTLP exporters against a dead
                // localhost:4317 (avoids connection-refused noise and a flush delay on shutdown).
                host.UseSetting("OTEL_SDK_DISABLED", "true");
                // JWT auth: symmetric mode with a known test key so the test can mint tokens the app accepts.
                host.UseSetting("Authentication:Mode", "Symmetric");
                host.UseSetting("Authentication:Issuer", JwtIssuer);
                host.UseSetting("Authentication:Audience", JwtAudience);
                host.UseSetting("Authentication:SigningKey", JwtSigningKey);
                host.UseSetting("Authentication:RequireHttpsMetadata", "false");
            });

        // Creating the client starts the host's hosted services, which now includes each module's
        // auto-migrator (PlatformModule.AddDbContextMigration) — so platform.outbox/inbox exist before
        // the Debezium connector is registered, with no explicit migrate call here.
        Client = Factory.CreateClient();

        // 7. Register the Debezium Outbox Event Router connector.
        //    The connector config uses "database.hostname=postgres" (in-network alias).
        //    No sleep-to-win-the-race here: the rider is configured AutoOffsetReset=Earliest, so the
        //    consumer group picks up events even if Debezium starts capturing before it has subscribed.
        await RegisterOutboxConnectorAsync();
    }

    /// <summary>
    /// Flushes the shared Redis so a rate-limit-sensitive test starts from a clean window. The whole assembly shares
    /// one Redis (single container), so tests that assert exact rate-limit counts must reset it first; tests run
    /// sequentially within the collection, so this is safe.
    /// </summary>
    public async Task ResetRedisAsync()
    {
        await using var mux = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(
            $"{RedisConnectionString},allowAdmin=true");
        foreach (var endpoint in mux.GetEndPoints())
            await mux.GetServer(endpoint).FlushDatabaseAsync();

        // The burst-ban mirror now lives in the host's local IMemoryCache (not Redis), so flushing Redis alone would
        // leave a prior test's ban visible. Clear it too, keeping rate-limit-sensitive tests isolated.
        if (Factory?.Services.GetService<IMemoryCache>() is MemoryCache memoryCache)
            memoryCache.Clear();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (Factory != null) await Factory.DisposeAsync();
        if (_connect != null) await _connect.DisposeAsync();
        if (_kafka != null) await _kafka.DisposeAsync();
        if (_redis != null) await _redis.DisposeAsync();
        if (_postgres != null) await _postgres.DisposeAsync();
        if (_network != null) await _network.DeleteAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task RegisterOutboxConnectorAsync()
    {
        var connectPort = _connect.GetMappedPublicPort(ConnectRestPort);
        var connectUrl = $"http://localhost:{connectPort}";

        // Register the SAME connector config the prod deploy uses, overriding only the in-network
        // Postgres hostname. The prod JSON is linked into this test's output (see .csproj), so the
        // test is the guard against drift in infra/debezium/outbox-connector.json — no hand-kept copy.
        var connectorJsonPath = Path.Combine(AppContext.BaseDirectory, "outbox-connector.json");
        var connectorNode = JsonNode.Parse(await File.ReadAllTextAsync(connectorJsonPath))!;
        connectorNode["config"]!["database.hostname"] = PgAlias;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var postResponse = await http.PostAsync(
            $"{connectUrl}/connectors",
            new StringContent(connectorNode.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));

        // 201 Created = new connector; 409 Conflict = already exists (idempotent)
        if (postResponse.StatusCode != System.Net.HttpStatusCode.Conflict)
            postResponse.EnsureSuccessStatusCode();

        // Wait until the connector is in RUNNING state (up to 90 s).
        // The status endpoint may 404 briefly right after registration while Connect
        // finishes writing its metadata — treat 404 as "not ready yet" and keep polling.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var statusResp = await http.GetAsync(
                $"{connectUrl}/connectors/tm-outbox-connector/status");

            if (statusResp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await Task.Delay(2_000);
                continue;
            }

            statusResp.EnsureSuccessStatusCode();

            var body = await statusResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var state = doc.RootElement
                .GetProperty("connector")
                .GetProperty("state")
                .GetString();

            if (string.Equals(state, "RUNNING", StringComparison.Ordinal)) return;
            if (string.Equals(state, "FAILED", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Debezium connector entered FAILED state. Body: {body}");
            }

            await Task.Delay(2_000);
        }

        throw new InvalidOperationException(
            "Debezium connector did not reach RUNNING state within the timeout.");
    }
}
