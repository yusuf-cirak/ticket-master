using FluentAssertions;
using NetArchTest.Rules;
using YC.BuildingBlocks.Domain;
using Xunit;

namespace TicketMaster.ArchitectureTests;

public class DependencyRuleTests
{
    private static readonly System.Reflection.Assembly DomainAsm = typeof(AggregateRoot<>).Assembly;
    private static readonly System.Reflection.Assembly PlatformAsm = typeof(TicketMaster.Modules.Platform.PlatformDbContext).Assembly;

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
                "YC.BuildingBlocks.Application",
                "YC.BuildingBlocks.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Domain depends on a higher layer: {string.Join(", ", result.FailingTypeNames ?? [])}");
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
