using FluentAssertions;
using TicketMaster.BuildingBlocks.Domain;
using Xunit;

namespace TicketMaster.UnitTests.BuildingBlocks;

public sealed record SampleId(Guid Value) : StronglyTypedId(Value);

public sealed record SampleRaised(string What) : DomainEvent;

public sealed class Sample : AggregateRoot<SampleId>
{
    public Sample(SampleId id) : base(id) { }
    public void Do() => Raise(new SampleRaised("done"));
}

public class AggregateRootTests
{
    [Fact]
    public void Raise_buffers_domain_event()
    {
        var sample = new Sample(new SampleId(Guid.NewGuid()));
        sample.Do();
        sample.DomainEvents.Should().ContainSingle(e => e is SampleRaised);
    }

    [Fact]
    public void ClearDomainEvents_empties_buffer()
    {
        var sample = new Sample(new SampleId(Guid.NewGuid()));
        sample.Do();
        sample.ClearDomainEvents();
        sample.DomainEvents.Should().BeEmpty();
    }
}
