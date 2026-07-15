using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using YC.BuildingBlocks.Domain.Security;
using Xunit;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// Proves live revocation: a token whose signature and lifetime are still valid is nonetheless rejected once its
/// jti is on the blacklist (logout / forced sign-out) — the JWT bearer OnTokenValidated hook consults ITokenBlacklist.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class TokenRevocationTest(SpineFixture fixture)
{
    [Fact]
    public async Task Revoked_token_is_rejected_though_signature_and_lifetime_are_valid()
    {
        var token = fixture.MintToken("platform:read");
        var jti = new JsonWebTokenHandler().ReadJsonWebToken(token).Id;

        var blacklist = fixture.Factory.Services.GetRequiredService<ITokenBlacklist>();
        await blacklist.RevokeAsync(jti, DateTimeOffset.UtcNow.AddMinutes(10));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
