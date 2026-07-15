using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using YC.BuildingBlocks.Domain;
using YC.BuildingBlocks.Domain.Auditing;
using YC.BuildingBlocks.Domain.Security;
using YC.BuildingBlocks.Persistence.EfCore;
using YC.BuildingBlocks.Persistence.EfCore.Interceptors;
using Xunit;

namespace TicketMaster.UnitTests.Infrastructure;

public class InterceptorTests
{
    [Fact]
    public async Task Dispatches_pre_and_post_events_and_clears_them()
    {
        var publisher = new CapturingPublisher();
        await using var db = NewContext(domainEvents: new DomainEventDispatchInterceptor(publisher));

        var item = new TestAggregate(Guid.CreateVersion7(), "a");
        item.RaisePre();
        item.RaisePost();
        db.Items.Add(item);

        await db.SaveChangesAsync();

        publisher.Published.OfType<PreEvent>().Should().HaveCount(1);
        publisher.Published.OfType<PostEvent>().Should().HaveCount(1);
        item.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Stamps_auditable_on_add_and_modify()
    {
        var user = new StubCurrentUser("user-123");
        await using var db = NewContext(auditing: new AuditingSaveChangesInterceptor(user, TimeProvider.System));

        var item = new TestAggregate(Guid.CreateVersion7(), "a");
        db.Items.Add(item);
        await db.SaveChangesAsync();

        item.CreatedBy.Should().Be("user-123");
        item.CreatedAtUtc.Should().NotBe(default);
        item.ModifiedBy.Should().BeNull();

        item.Name = "b";
        await db.SaveChangesAsync();

        item.ModifiedBy.Should().Be("user-123");
        item.ModifiedAtUtc.Should().NotBeNull();
    }

    private static TestDbContext NewContext(
        DomainEventDispatchInterceptor domainEvents = null,
        AuditingSaveChangesInterceptor auditing = null)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());
        if (domainEvents is not null) options.AddInterceptors(domainEvents);
        if (auditing is not null) options.AddInterceptors(auditing);
        return new TestDbContext(options.Options);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestAggregate> Items => Set<TestAggregate>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<TestAggregate>().Ignore(x => x.DomainEvents);
    }

    private sealed class TestAggregate : AggregateRoot<Guid>, IAuditable
    {
        public TestAggregate(Guid id, string name) : base(id) => Name = name;
        private TestAggregate() { } // EF

        public string Name { get; set; } = "";
        public DateTimeOffset CreatedAtUtc { get; set; }
        public string CreatedBy { get; set; }
        public DateTimeOffset? ModifiedAtUtc { get; set; }
        public string ModifiedBy { get; set; }

        public void RaisePre() => Raise(new PreEvent());
        public void RaisePost() => Raise(new PostEvent());
    }

    private sealed record PreEvent : DomainEvent, IPreDomainEvent;
    private sealed record PostEvent : DomainEvent, IPostDomainEvent;

    private sealed class CapturingPublisher : IPublisher
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Published.Add(notification!);
            return Task.CompletedTask;
        }
    }

    private sealed class StubCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId is not null;
        public string UserId => userId ?? string.Empty;
        public string TokenId => string.Empty;
        public string UserName => userId ?? string.Empty;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
        public bool HasPermission(string permission) => false;
    }
}
