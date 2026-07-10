using Xunit;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// Binds every integration test class to a single shared <see cref="SpineFixture"/>, so the whole assembly starts
/// Postgres/Kafka/Connect/Redis (and boots the host) exactly once — not once per test class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<SpineFixture>
{
    public const string Name = "integration";
}
