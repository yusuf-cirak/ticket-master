using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Application.Abstractions.Locking;
using Xunit;

namespace TicketMaster.IntegrationTests;

/// <summary>
/// Proves the lease-renewal gap is closed: a held <see cref="IDistributedLock"/> auto-extends its TTL in the
/// background, so a long-running critical section outliving the original expiration is not evicted mid-flight.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RedisDistributedLockTest(SpineFixture fixture)
{
    [Fact]
    public async Task Held_lock_survives_past_its_original_expiration_via_background_renewal()
    {
        var @lock = fixture.Factory.Services.GetRequiredService<IDistributedLock>();
        var key = $"lock-renewal-test:{Guid.NewGuid():N}";

        var handle = await @lock.AcquireAsync(key, TimeSpan.FromMilliseconds(600));
        handle.Should().NotBeNull();

        // Outlive the original TTL several times over — only background renewal keeps this held.
        await Task.Delay(TimeSpan.FromSeconds(2));

        var stillHeld = await @lock.AcquireAsync(key, TimeSpan.FromMilliseconds(600));
        stillHeld.Should().BeNull("the original holder's renewal loop must have kept extending the key's TTL");

        await handle!.DisposeAsync();

        var afterRelease = await @lock.AcquireAsync(key, TimeSpan.FromMilliseconds(600));
        afterRelease.Should().NotBeNull("disposing the handle must stop renewal and release the key immediately");
        await afterRelease!.DisposeAsync();
    }
}
