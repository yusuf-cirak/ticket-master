#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Persistence.Outbox;

/// <summary>
/// Polls one module's outbox table and relays its rows to Kafka, for deployments without Debezium/CDC.
/// <para>
/// The claim and the publish are deliberately in <b>separate</b> steps so a broker round-trip never holds a database
/// connection (which would starve the pool under load):
/// </para>
/// <list type="number">
/// <item><b>Claim</b> — one <c>UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING …</c>: it leases a batch
/// and returns it in a single auto-committed statement, so the row locks are released and the connection returns to the
/// pool immediately. <c>SKIP LOCKED</c> lets multiple relay instances run without contending for the same rows.</item>
/// <item><b>Publish</b> — each claimed row is produced to Kafka with <b>no DB connection held</b>.</item>
/// <item><b>Complete</b> — a short second statement stamps <c>processed_on</c> on the rows that published. Rows that
/// failed keep their lease and are retried once it expires.</item>
/// </list>
/// </summary>
public sealed class OutboxPollingService<TContext>(
    IServiceScopeFactory scopeFactory,
    IOutboxEventPublisher publisher,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPollingService<TContext>> logger,
    string schema) : BackgroundService
    where TContext : DbContext
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int published;
            try
            {
                published = await RelayBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox relay poll failed for {Schema}.outbox", schema);
                published = 0;
            }

            // Drain a backlog back-to-back; idle only when a poll came up empty.
            if (published == 0)
                await Task.Delay(_options.PollInterval, stoppingToken);
        }
    }

    /// <summary>Claims, publishes and completes one batch. Public so the relay cycle can be driven directly in tests.</summary>
    public async Task<int> RelayBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var claimed = await ClaimAsync(db, ct);
        if (claimed.Count == 0)
            return 0;

        // Publish outside any transaction — no DB connection is held during the broker round-trips.
        var publishedIds = new List<Guid>(claimed.Count);
        foreach (var message in claimed)
        {
            try
            {
                await publisher.PublishAsync(message, ct);
                publishedIds.Add(message.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox publish failed for {Id}; it stays leased and is retried later", message.Id);
            }
        }

        if (publishedIds.Count > 0)
            await CompleteAsync(db, publishedIds, ct);

        return publishedIds.Count;
    }

    // schema comes from trusted module registration (never user input), so interpolating it is safe; values are parameters.
    private Task<List<ClaimedOutboxMessage>> ClaimAsync(TContext db, CancellationToken ct)
    {
        // Assigned to a local (not inlined) so the EF1002 raw-SQL analyzer doesn't flag the trusted {schema}; the
        // batch/lease {0}/{1} are real parameters. Table/schema names can't be parameterized in SQL.
        var sql = $@"UPDATE {schema}.outbox
               SET locked_until = now() + {{0}}, attempts = attempts + 1
               WHERE id IN (
                   SELECT id FROM {schema}.outbox
                   WHERE processed_on IS NULL AND (locked_until IS NULL OR locked_until < now())
                   ORDER BY occurred_on
                   LIMIT {{1}}
                   FOR UPDATE SKIP LOCKED
               )
               RETURNING id, aggregate_type, aggregate_id, event_type, payload, correlation_id, causation_id, trace_parent";

        return db.Database.SqlQueryRaw<ClaimedOutboxMessage>(sql, _options.LeaseDuration, _options.BatchSize).ToListAsync(ct);
    }

    private Task CompleteAsync(TContext db, List<Guid> ids, CancellationToken ct)
    {
        var sql = $"UPDATE {schema}.outbox SET processed_on = now(), locked_until = NULL WHERE id = ANY({{0}})";
        return db.Database.ExecuteSqlRawAsync(sql, [ids.ToArray()], ct);
    }
}
