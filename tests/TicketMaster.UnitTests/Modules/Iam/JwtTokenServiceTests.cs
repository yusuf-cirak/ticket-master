using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using BuildingBlocks.Domain.ValueObjects;
using BuildingBlocks.Security;
using TicketMaster.Modules.Iam.Domain;
using TicketMaster.Modules.Iam.Infrastructure;
using Xunit;

namespace TicketMaster.UnitTests.Modules.Iam;

/// <summary>
/// Proves access tokens are stamped with the issuer's current key id, so a validator holding multiple
/// (current + previous) signing keys can find the right one — the foundation for rotating the signing
/// key without invalidating tokens already in flight.
/// </summary>
public class JwtTokenServiceTests
{
    private static JwtTokenService Sut(string currentKeyId) => new(
        Options.Create(new JwtOptions
        {
            Issuer = "ticketmaster",
            Audience = "ticketmaster-api",
            SigningKey = "dev-only-not-a-secret-change-me-min-32-bytes-0000",
            CurrentKeyId = currentKeyId
        }),
        Options.Create(new TokenIssuanceOptions()),
        TimeProvider.System);

    private static User AUser() => User.Register(Email.Create("user@example.com").Value, "Ada Lovelace").Value;

    [Fact]
    public void Access_token_header_carries_the_current_key_id()
    {
        var token = Sut("key-2").CreateAccessToken(AUser(), []);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Token);

        jwt.Kid.Should().Be("key-2");
    }
}
