using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// Proves Gmail-alias account de-duplication: the canonical email normalizer folds dots and +tags, so two
/// addresses that are the same Gmail account cannot both register — the second hits the NormalizedEmail unique index.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class EmailUniquenessTest(SpineFixture fixture)
{
    [Fact]
    public async Task Gmail_dot_and_plus_aliases_resolve_to_one_account()
    {
        const string password = "Str0ng!Passw0rd";

        var first = await fixture.Client.PostAsJsonAsync("/api/iam/register",
            new { email = "john.doe+news@gmail.com", displayName = "John", password });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Canonically the same account (dots + tag stripped) → rejected on the unique email.
        var second = await fixture.Client.PostAsJsonAsync("/api/iam/register",
            new { email = "johndoe@gmail.com", displayName = "John Again", password });
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
