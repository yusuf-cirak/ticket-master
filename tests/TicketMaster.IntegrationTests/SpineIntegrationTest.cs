using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;

namespace TicketMaster.IntegrationTests;

public sealed class SpineIntegrationTest(SpineFixture fixture) : IClassFixture<SpineFixture>
{
    /// <summary>
    /// Full end-to-end test:
    ///   POST /api/platform/ping
    ///     → outbox row written (EF SaveChanges)
    ///     → Debezium CDC picks up the row from platform.outbox
    ///     → Outbox Event Router routes it to Kafka topic tm.platform.events
    ///     → MassTransit Kafka rider delivers to PingConsumer
    ///     → PingConsumer deduplicates via platform.inbox and writes a row
    ///   Assert: platform.inbox has a row for consumer = 'platform.ping'
    /// </summary>
    [Fact]
    public async Task PostPlatformPing_ShouldWriteInboxRow_ViaOutboxDebeziumKafkaSpine()
    {
        // Arrange — client is pre-created in SpineFixture; MassTransit rider is live

        // Act
        var response = await fixture.Client.PostAsJsonAsync(
            "/api/platform/ping",
            new { note = "spine-e2e-test" });

        response.IsSuccessStatusCode.Should().BeTrue(
            $"POST /api/platform/ping returned {(int)response.StatusCode}");

        // Assert — poll platform.inbox for up to 90 s (Debezium + Kafka latency)
        var found = await PollInboxAsync(
            fixture.PostgresConnectionString,
            consumer: "platform.ping",
            timeoutSeconds: 90);

        found.Should().BeTrue(
            "platform.inbox should contain a row for consumer='platform.ping' " +
            "after the outbox→Debezium→Kafka→MassTransit spine completes.");
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<bool> PollInboxAsync(
        string connectionString,
        string consumer,
        int timeoutSeconds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM platform.inbox WHERE consumer = @consumer",
                conn);
            cmd.Parameters.AddWithValue("consumer", consumer);

            var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            if (count > 0) return true;

            await Task.Delay(2_000);
        }

        return false;
    }
}
