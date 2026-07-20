using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketMaster.Modules.Iam.Domain;
using TicketMaster.Modules.Iam.Infrastructure;
using Xunit;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// Proves optimistic concurrency via Postgres's xmin system column (HasSystemConcurrency on Role): two contexts load
/// the same row, both mutate; the first commit wins (xmin advances) and the second is rejected with
/// DbUpdateConcurrencyException — a lost update is caught, not silently overwritten.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class XminConcurrencyTest(SpineFixture fixture)
{
    [Fact]
    public async Task Concurrent_updates_of_the_same_role_throw_a_concurrency_exception()
    {
        var role = Role.Create($"concurrency-{Guid.NewGuid():N}").Value;

        using (var seed = fixture.Factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<IamDbContext>();
            db.Add(role);
            await db.SaveChangesAsync();
        }

        using var scopeA = fixture.Factory.Services.CreateScope();
        using var scopeB = fixture.Factory.Services.CreateScope();
        var ctxA = scopeA.ServiceProvider.GetRequiredService<IamDbContext>();
        var ctxB = scopeB.ServiceProvider.GetRequiredService<IamDbContext>();

        // AsTracking: the context defaults to no-tracking, but this exercises a tracked load-modify-save update.
        var roleA = await ctxA.Set<Role>().AsTracking().SingleAsync(r => r.Id == role.Id);
        var roleB = await ctxB.Set<Role>().AsTracking().SingleAsync(r => r.Id == role.Id);

        roleA.Grant("catalog:read");
        await ctxA.SaveChangesAsync(); // wins — Postgres advances xmin

        roleB.Grant("catalog:write");
        var stale = async () => await ctxB.SaveChangesAsync();
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
