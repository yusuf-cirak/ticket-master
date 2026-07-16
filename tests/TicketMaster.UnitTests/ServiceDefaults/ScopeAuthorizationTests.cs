using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Xunit;
using BuildingBlocks.Security.Authorization;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class ScopeAuthorizationTests
{
    private static async Task<bool> Evaluate(string[] required, params Claim[] claims)
    {
        var requirement = new ScopeRequirement(required);
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"));
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);

        await new ScopeAuthorizationHandler().HandleAsync(context);

        return context.HasSucceeded;
    }

    [Fact]
    public async Task Succeeds_when_space_delimited_scope_claim_contains_required() =>
        (await Evaluate(["orders:write"], new Claim("scope", "orders:read orders:write"))).Should().BeTrue();

    [Fact]
    public async Task Succeeds_when_scp_claim_contains_required() =>
        (await Evaluate(["orders:write"], new Claim("scp", "orders:write"))).Should().BeTrue();

    [Fact]
    public async Task Fails_when_required_scope_absent() =>
        (await Evaluate(["orders:write"], new Claim("scope", "orders:read"))).Should().BeFalse();

    [Fact]
    public async Task Fails_when_no_scope_claims_present() =>
        (await Evaluate(["orders:write"])).Should().BeFalse();
}
