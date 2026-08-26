#nullable enable
using System;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Application.Abstractions.Tenancy;
using BuildingBlocks.Domain.Tenancy;
using BuildingBlocks.Persistence.EfCore.Interceptors;
using BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.Tenancy;

public class TenancyTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ---- resolvers ----

    [Fact]
    public void ClaimResolver_reads_the_tenant_claim()
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TenancyConstants.TenantClaim, TenantA.ToString())], "test"))
        };

        new ClaimTenantResolver().TryResolve(ctx, out var tenantId).Should().BeTrue();
        tenantId.Should().Be(TenantA);
    }

    [Fact]
    public void ClaimResolver_fails_without_the_claim() =>
        new ClaimTenantResolver().TryResolve(new DefaultHttpContext(), out _).Should().BeFalse();

    [Fact]
    public void HeaderResolver_reads_the_tenant_header()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[TenancyConstants.TenantHeader] = TenantB.ToString();

        new HeaderTenantResolver().TryResolve(ctx, out var tenantId).Should().BeTrue();
        tenantId.Should().Be(TenantB);
    }

    [Fact]
    public void HeaderResolver_fails_on_a_non_guid_header()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[TenancyConstants.TenantHeader] = "not-a-guid";

        new HeaderTenantResolver().TryResolve(ctx, out _).Should().BeFalse();
    }

    // ---- ambient context ----

    [Fact]
    public void AmbientContext_scope_overrides_then_restores()
    {
        var context = new AmbientTenantContext();
        context.SetResolved(TenantA);
        context.TenantId.Should().Be(Option<Guid>.Some(TenantA));

        using (context.BeginScope(TenantB))
            context.TenantId.Should().Be(Option<Guid>.Some(TenantB), "the scope overrides the resolved tenant");

        context.TenantId.Should().Be(Option<Guid>.Some(TenantA), "disposing restores the previous tenant");
    }

    // ---- write interceptor ----

    [Fact]
    public void Interceptor_stamps_the_current_tenant_on_insert()
    {
        using var db = NewDb(TenantA);
        var widget = new Widget();
        db.Add(widget);
        db.SaveChanges();

        widget.TenantId.Should().Be(TenantA);
    }

    [Fact]
    public void Interceptor_blocks_a_cross_tenant_write()
    {
        using var db = NewDb(TenantB);
        var widget = new Widget();
        ((ITenantOwned)widget).SetTenantId(TenantA); // belongs to A, but the ambient tenant is B
        db.Add(widget);

        db.Invoking(d => d.SaveChanges()).Should().Throw<InvalidOperationException>()
            .WithMessage("*Cross-tenant write blocked*");
    }

    [Fact]
    public void Interceptor_refuses_to_write_without_a_tenant()
    {
        using var db = NewDb(tenant: null);
        db.Add(new Widget());

        db.Invoking(d => d.SaveChanges()).Should().Throw<InvalidOperationException>()
            .WithMessage("*without an ambient tenant*");
    }

    private static TestDbContext NewDb(Guid? tenant) =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new TenantSaveChangesInterceptor(new StubTenantContext(tenant)))
            .Options);

    private sealed class Widget : ITenantOwned
    {
        public int Id { get; set; }
        public Guid TenantId { get; private set; }
        public void SetTenantId(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();
    }

    private sealed class StubTenantContext(Guid? tenant) : ITenantContext
    {
        public Option<Guid> TenantId => tenant is { } id ? Option<Guid>.Some(id) : Option<Guid>.None();
        public bool HasTenant => tenant.HasValue;
        public IDisposable BeginScope(Guid tenantId) => throw new NotSupportedException();
    }
}
