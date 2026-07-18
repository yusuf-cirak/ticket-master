using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using BuildingBlocks.Domain;
using Xunit;

namespace TicketMaster.ArchitectureTests;

public class DependencyRuleTests
{
    private static readonly Assembly DomainAsm = typeof(AggregateRoot<>).Assembly;
    private static readonly Assembly ApplicationAsm = typeof(BuildingBlocks.Application.Pagination.PagedResult<>).Assembly;
    private static readonly Assembly PlatformAsm = typeof(TicketMaster.Modules.Platform.PlatformDbContext).Assembly;

    private static readonly Assembly[] InfrastructureAsms =
    [
        Assembly.Load("BuildingBlocks.Persistence"),
        Assembly.Load("BuildingBlocks.Messaging.Kafka"),
        Assembly.Load("BuildingBlocks.Caching"),
        Assembly.Load("BuildingBlocks.Locking"),
        Assembly.Load("BuildingBlocks.Configuration.Secrets"),
    ];

    private static readonly string[] InfrastructureNamespaces =
    [
        "BuildingBlocks.Persistence", "BuildingBlocks.Messaging.Kafka", "BuildingBlocks.Caching",
        "BuildingBlocks.Locking", "BuildingBlocks.Configuration.Secrets",
    ];

    private static readonly string[] PresentationNamespaces =
    [
        "BuildingBlocks.Web", "BuildingBlocks.Security", "BuildingBlocks.Observability",
        "BuildingBlocks.HealthChecks", "BuildingBlocks.RateLimiting", "BuildingBlocks.Modularity",
    ];

    [Fact]
    public void Domain_must_not_depend_on_ef_or_kafka()
    {
        var result = Types.InAssembly(DomainAsm)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Confluent.Kafka", "Npgsql", "MassTransit")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Domain has forbidden infra deps: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_must_not_depend_on_application_or_infrastructure()
    {
        // The shared kernel's Domain layer sits at the bottom of the dependency graph: it must not
        // reference the Application or Infrastructure building blocks (which depend on it, not vice versa).
        var result = Types.InAssembly(DomainAsm)
            .ShouldNot()
            .HaveDependencyOnAny(
                "BuildingBlocks.Application",
                "BuildingBlocks.Persistence",
                "BuildingBlocks.Messaging.Kafka")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Domain depends on a higher layer: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_must_not_depend_on_infrastructure_or_presentation()
    {
        // Application only knows Domain. It must not reach into any Infrastructure (persistence/messaging/caching/…)
        // or Presentation (web/security/observability/…) building block — those depend on it, not the reverse.
        var result = Types.InAssembly(ApplicationAsm)
            .ShouldNot()
            .HaveDependencyOnAny([.. InfrastructureNamespaces, .. PresentationNamespaces])
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Application depends on an outer layer: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Infrastructure_must_not_depend_on_presentation()
    {
        // Infrastructure blocks (talking to DB/Kafka/Redis/config) must not depend on the Presentation/host layer.
        // (Presentation -> Infrastructure is allowed, e.g. RateLimiting -> Caching.)
        var result = Types.InAssemblies(InfrastructureAsms)
            .ShouldNot()
            .HaveDependencyOnAny(PresentationNamespaces)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Infrastructure depends on Presentation: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Module_must_not_depend_on_another_modules_internals()
    {
        var result = Types.InAssembly(PlatformAsm)
            .That().ResideInNamespaceStartingWith("TicketMaster.Modules.Platform")
            .And().DoNotResideInNamespaceEndingWith(".Contracts")
            .ShouldNot()
            .HaveDependencyOnAny("TicketMaster.Modules.Event", "TicketMaster.Modules.Booking",
                "TicketMaster.Modules.Inventory", "TicketMaster.Modules.Ordering")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Module crossed an internal boundary: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
