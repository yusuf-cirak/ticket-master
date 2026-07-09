using FluentAssertions;
using TicketMaster.Modules.Iam.Domain;
using Xunit;

namespace TicketMaster.UnitTests.Modules.Iam;

public class RoleTests
{
    [Fact]
    public void Create_with_permissions_succeeds()
    {
        var result = Role.Create("admin", ["iam:users:read", "iam:users:write"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("admin");
        result.Value.Permissions.Should().BeEquivalentTo("iam:users:read", "iam:users:write");
    }

    [Fact]
    public void Create_without_name_fails()
    {
        Role.Create("  ").Error.Code.Should().Be("role.name_required");
    }

    [Fact]
    public void Grant_and_revoke_permission()
    {
        var role = Role.Create("editor").Value;

        role.Grant("iam:users:read");
        role.Grant("iam:users:read"); // idempotent
        role.Permissions.Should().ContainSingle();

        role.Revoke("iam:users:read");
        role.Permissions.Should().BeEmpty();
    }
}
