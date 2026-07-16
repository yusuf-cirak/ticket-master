using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using YC.BuildingBlocks.RateLimiting;
using Xunit;

namespace TicketMaster.UnitTests.RateLimiting;

public class PrincipalPartitionerTests
{
    private static PrincipalPartitioner Sut() =>
        new(Options.Create(new RateLimitingOptions
        {
            KeyPrefix = "ticket.master",
            Tiers = new(StringComparer.OrdinalIgnoreCase) { ["anon"] = 0.25, ["authenticated"] = 1, ["internal"] = 5 },
            ApiKeys = ["super-secret"]
        }));

    private static DefaultHttpContext Authenticated(string scheme, params Claim[] claims)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: scheme));
        return ctx;
    }

    [Fact]
    public void Authenticated_user_resolves_to_sub_and_authenticated_tier()
    {
        var (id, tier) = Sut().ResolvePrincipal(Authenticated("oidc", new Claim("sub", "user-1")));
        id.Should().Be("user:user-1");
        tier.Should().Be(PrincipalTier.Authenticated);
    }

    [Fact]
    public void Internal_tier_requires_the_trusted_internal_scheme_and_namespaced_claim()
    {
        var (_, tier) = Sut().ResolvePrincipal(
            Authenticated("Internal", new Claim("sub", "svc-1"), new Claim(RateLimitingConstants.DefaultTierClaim, RateLimitingConstants.InternalTierValue)));
        tier.Should().Be(PrincipalTier.Internal);
    }

    [Fact]
    public void Forged_tier_claim_from_an_untrusted_scheme_does_not_elevate()
    {
        // Same claim, but the identity was authenticated by an external scheme → stays Authenticated.
        var (_, tier) = Sut().ResolvePrincipal(
            Authenticated("oidc", new Claim("sub", "attacker"), new Claim(RateLimitingConstants.DefaultTierClaim, RateLimitingConstants.InternalTierValue)));
        tier.Should().Be(PrincipalTier.Authenticated);
    }

    [Fact]
    public void Registered_api_key_resolves_to_hashed_key_and_authenticated_tier()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Api-Key"] = "super-secret"; // on the allowlist (see Sut)
        var (id, tier) = Sut().ResolvePrincipal(ctx);
        id.Should().StartWith("apiKey:").And.NotContain("super-secret");
        tier.Should().Be(PrincipalTier.Authenticated);
    }

    [Fact]
    public void Unregistered_api_key_does_not_elevate_and_falls_through_to_ip()
    {
        // Security: an arbitrary (unallowlisted) key must not mint a fresh authenticated bucket — it falls through
        // to the per-IP anonymous bucket, so rotating bogus keys can't evade the rate limit.
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Api-Key"] = "attacker-supplied";
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        var (id, tier) = Sut().ResolvePrincipal(ctx);
        id.Should().StartWith("anon:ip:");
        tier.Should().Be(PrincipalTier.Anonymous);
    }

    [Fact]
    public void Anonymous_falls_back_to_hashed_ip_and_user_agent()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        ctx.Request.Headers.UserAgent = "curl/8";
        var (id, tier) = Sut().ResolvePrincipal(ctx);
        id.Should().StartWith("anon:").And.NotContain("203.0.113.7");
        tier.Should().Be(PrincipalTier.Anonymous);
    }

    [Fact]
    public void Partition_key_has_prefix_policy_principal_and_route_template()
    {
        // Route template (not raw path), so /api/items/1 and /api/items/2 collapse to one bucket.
        Sut().PartitionKey("burst", "user:user-1", "/api/items/{id}")
            .Should().Be("ticket.master:ratelimit:burst:user:user-1|/api/items/{id}");
    }

    [Theory]
    [InlineData(PrincipalTier.Anonymous, 0.25)]
    [InlineData(PrincipalTier.Authenticated, 1.0)]
    [InlineData(PrincipalTier.Internal, 5.0)]
    public void Tier_multiplier_comes_from_options(PrincipalTier tier, double expected) =>
        Sut().TierMultiplier(tier).Should().Be(expected);
}
