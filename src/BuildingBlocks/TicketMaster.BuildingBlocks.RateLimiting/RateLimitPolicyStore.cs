using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace TicketMaster.BuildingBlocks.RateLimiting;

/// <summary>
/// Resolves the effective limit for a (policy, principal): the configured base × tier multiplier, unless a
/// per-principal override exists. Overrides are read through FusionCache — L1 (local memory) first, L2 (Redis)
/// on miss — so this hot-path lookup almost never touches Redis. Admin writes go through
/// <see cref="SetOverrideAsync"/> (→ L2 + backplane-invalidate L1 on the other nodes).
/// </summary>
public sealed class RateLimitPolicyStore(
    IFusionCache cache,
    IOptions<RateLimitingOptions> options,
    PrincipalPartitioner partitioner)
{
    public ResolvedPolicy Resolve(string policyName, HttpContext context, string principalId, PrincipalTier tier)
    {
        if (!options.Value.Policies.TryGetValue(policyName, out var def))
            throw new InvalidOperationException($"Rate limit policy '{policyName}' is not configured.");

        var overrideLimit = cache.GetOrDefault<int?>(OverrideKey(policyName, principalId));
        var permit = overrideLimit ?? (int)Math.Ceiling(def.PermitLimit * partitioner.TierMultiplier(tier));

        return new ResolvedPolicy(Math.Max(1, permit), def.Window, def.SegmentsPerWindow, def.QueueLimit);
    }

    /// <summary>Sets a per-principal limit override (write-through to Redis + backplane invalidation).</summary>
    public ValueTask SetOverrideAsync(string policyName, string principalId, int permitLimit, CancellationToken ct = default) =>
        cache.SetAsync(OverrideKey(policyName, principalId), permitLimit, token: ct);

    private static string OverrideKey(string policyName, string principalId) => $"ratelimit:override:{policyName}:{principalId}";
}

public readonly record struct ResolvedPolicy(int PermitLimit, TimeSpan Window, int SegmentsPerWindow, int QueueLimit);
