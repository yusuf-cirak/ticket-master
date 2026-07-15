using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using YC.BuildingBlocks.RateLimiting;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace TicketMaster.UnitTests.RateLimiting;

public class RateLimitPolicyStoreTests
{
    private static (RateLimitPolicyStore Store, IFusionCache Cache) Build()
    {
        var options = Options.Create(new RateLimitingOptions
        {
            KeyPrefix = "ticket.master",
            Policies = new(StringComparer.OrdinalIgnoreCase)
            {
                ["burst"] = new PolicyDefinition { PermitLimit = 20, Window = TimeSpan.FromSeconds(5), SegmentsPerWindow = 5 }
            },
            Tiers = new(StringComparer.OrdinalIgnoreCase) { ["anon"] = 0.25, ["authenticated"] = 1, ["internal"] = 5 }
        });
        var cache = new FusionCache(new FusionCacheOptions());
        var store = new RateLimitPolicyStore(cache, options, new PrincipalPartitioner(options));
        return (store, cache);
    }

    [Theory]
    [InlineData(PrincipalTier.Anonymous, 5)]      // ceil(20 * 0.25)
    [InlineData(PrincipalTier.Authenticated, 20)] // 20 * 1
    [InlineData(PrincipalTier.Internal, 100)]     // 20 * 5
    public void Resolve_applies_tier_multiplier_to_base_permit(PrincipalTier tier, int expected)
    {
        var (store, _) = Build();
        var policy = store.Resolve("burst", new DefaultHttpContext(), "user:1", tier);
        policy.PermitLimit.Should().Be(expected);
        policy.Window.Should().Be(TimeSpan.FromSeconds(5));
        policy.SegmentsPerWindow.Should().Be(5);
    }

    [Fact]
    public async Task Per_principal_override_takes_precedence_over_tier_math()
    {
        var (store, _) = Build();
        await store.SetOverrideAsync("burst", "user:vip", 9999);

        var policy = store.Resolve("burst", new DefaultHttpContext(), "user:vip", PrincipalTier.Anonymous);

        policy.PermitLimit.Should().Be(9999);
    }

    [Fact]
    public void Unknown_policy_throws()
    {
        var (store, _) = Build();
        var act = () => store.Resolve("does-not-exist", new DefaultHttpContext(), "user:1", PrincipalTier.Authenticated);
        act.Should().Throw<InvalidOperationException>();
    }
}
