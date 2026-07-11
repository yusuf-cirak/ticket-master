using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// End-to-end IAM auth flow against the booted app: register → login → protected /me → refresh (with rotation
/// single-use) → logout (access-token blacklist). Exercises the custom JWT issuer, refresh-token persistence/rotation
/// and revocation all through the real HTTP pipeline.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class IamAuthTest(SpineFixture fixture)
{
    private const string Password = "Str0ng!Passw0rd"; // 12+ chars, has a non-alphanumeric

    private static string UniqueEmail() => $"iamtest{Guid.NewGuid():N}@gmail.com";

    private sealed record Tokens(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);

    [Fact]
    public async Task Full_auth_flow_register_login_me_refresh_rotation_logout()
    {
        var client = fixture.Client;
        var email = UniqueEmail();

        // 1. Register → 201
        var register = await client.PostAsJsonAsync("/api/iam/register",
            new { email, displayName = "IAM Test User", password = Password });
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        // 2. Login → 200 with tokens
        var login = await client.PostAsJsonAsync("/api/iam/login", new { email, password = Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await login.Content.ReadFromJsonAsync<Tokens>();
        tokens.Should().NotBeNull();
        tokens!.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.TokenType.Should().Be("Bearer");
        tokens.ExpiresIn.Should().BeGreaterThan(0);

        // 3a. /me without a token → 401
        (await client.GetAsync("/api/iam/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 3b. /me with the access token → 200
        (await Me(tokens.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        // 4a. Refresh → 200 with a new pair
        var refresh = await client.PostAsJsonAsync("/api/iam/refresh", new { refreshToken = tokens.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await refresh.Content.ReadFromJsonAsync<Tokens>();
        rotated.Should().NotBeNull();
        rotated!.RefreshToken.Should().NotBe(tokens.RefreshToken); // rotated

        // 4b. Reusing the OLD refresh token → 401 (single-use, revoked on rotation)
        var reuse = await client.PostAsJsonAsync("/api/iam/refresh", new { refreshToken = tokens.RefreshToken });
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 5. Logout → 204, then the logged-out access token is rejected (blacklist)
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/iam/logout")
        {
            Content = JsonContent.Create(new { refreshToken = rotated.RefreshToken })
        };
        logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        (await client.SendAsync(logoutReq)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Me(rotated.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private Task<HttpResponseMessage> Me(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/iam/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return fixture.Client.SendAsync(request);
    }
}
