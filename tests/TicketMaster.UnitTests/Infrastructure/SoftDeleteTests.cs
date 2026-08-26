using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Domain;
using BuildingBlocks.Application.Abstractions.Security;
using BuildingBlocks.Persistence.EfCore.Interceptors;
using Xunit;

namespace TicketMaster.UnitTests.Infrastructure;

public class SoftDeleteTests
{
    [Fact]
    public async Task Delete_soft_deletes_stamps_and_filters_from_queries()
    {
        await using var db = NewContext("actor-1");
        var thing = new Thing();
        db.Things.Add(thing);
        await db.SaveChangesAsync();

        db.Things.Remove(thing);
        await db.SaveChangesAsync();

        // The row is not gone — a normal query hides it, IgnoreQueryFilters reveals it soft-deleted + stamped.
        (await db.Things.CountAsync()).Should().Be(0);

        var raw = await db.Things.IgnoreQueryFilters().SingleAsync();
        raw.IsDeleted.Should().BeTrue();
        raw.DeletedBy.Should().Be("actor-1");
        raw.DeletedAtUtc.Should().NotBeNull();
    }

    private static SoftDeleteContext NewContext(string userId)
    {
        var interceptor = new SoftDeleteSaveChangesInterceptor(new StubUser(userId), TimeProvider.System);
        return new SoftDeleteContext(new DbContextOptionsBuilder<SoftDeleteContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options);
    }

    private sealed class Thing : ISoftDeletable
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedAtUtc { get; set; }
        public string DeletedBy { get; set; } = string.Empty;
    }

    private sealed class SoftDeleteContext(DbContextOptions<SoftDeleteContext> options) : DbContext(options)
    {
        public DbSet<Thing> Things => Set<Thing>();
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplySoftDeleteFilters();
    }

    private sealed class StubUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId.Length > 0;
        public Option<string> UserId => userId.Length > 0 ? Option<string>.Some(userId) : Option<string>.None();
        public Option<string> TokenId => Option<string>.None();
        public Option<string> UserName => UserId;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
        public bool HasPermission(string permission) => false;
    }
}
