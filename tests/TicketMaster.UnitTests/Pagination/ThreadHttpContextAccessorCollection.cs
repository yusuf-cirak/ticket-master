using Xunit;

namespace TicketMaster.UnitTests.Pagination;

/// <summary>
/// Binds every test class that mutates <c>ThreadHttpContextAccessor</c> (a process-wide static) into one xUnit
/// collection, so they run sequentially against each other. xUnit parallelizes across collections by default — two
/// tests in different collections both calling <c>Configure(...)</c> around the same time would race, and one could
/// read the other's stubbed <c>HttpContext</c> (this was an actual, reproducible flake, not a theoretical one).
/// </summary>
[CollectionDefinition(Name)]
public sealed class ThreadHttpContextAccessorCollection
{
    public const string Name = "thread-http-context-accessor";
}
