using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using FluentAssertions;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// End-to-end JWT pipeline against the booted app: the scope-protected <c>/api/platform/whoami</c> probe rejects
/// anonymous (401) and insufficiently-scoped (403) callers and admits a correctly-scoped token (200). Confirms
/// the secure-by-default fallback policy, scope enforcement, and problem+json challenge.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AuthenticationTest(SpineFixture fixture) 
{
    private const string Probe = "/api/whoami";

    [Fact]
    public async Task Returns_401_problem_json_without_a_token()
    {
        var response = await fixture.Client.GetAsync(Probe);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Returns_403_when_required_scope_is_missing()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Probe);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.MintToken());

        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Returns_200_with_identity_when_scope_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Probe);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.MintToken("platform:read"));

        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("test-user-1");
    }
}
