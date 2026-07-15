using System.Linq;
using FluentAssertions;
using YC.BuildingBlocks.Domain.ValueObjects;
using TicketMaster.Modules.Iam.Domain;
using TicketMaster.Modules.Iam.Domain.Events;
using Xunit;

namespace TicketMaster.UnitTests.Modules.Iam;

public class UserTests
{
    private static Email AnEmail() => Email.Create("user@example.com").Value;

    [Fact]
    public void Register_valid_user_succeeds_and_raises_event()
    {
        var result = User.Register(AnEmail(), "Ada Lovelace");

        result.IsSuccess.Should().BeTrue();
        var user = result.Value;
        user.Email.Should().Be("user@example.com");
        user.DisplayName.Should().Be("Ada Lovelace");
        user.Status.Should().Be(UserStatus.Active);
        user.DomainEvents.OfType<UserRegisteredDomainEvent>().Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_without_display_name_fails(string displayName)
    {
        var result = User.Register(AnEmail(), displayName);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("user.display_name_required");
    }

    [Fact]
    public void AssignRole_adds_role_once()
    {
        var user = User.Register(AnEmail(), "Ada").Value;
        var roleId = RoleId.From(1);

        user.AssignRole(roleId);
        user.AssignRole(roleId);

        user.Roles.Should().ContainSingle().Which.Should().Be(roleId);
    }

    [Fact]
    public void Lock_disabled_user_fails()
    {
        var user = User.Register(AnEmail(), "Ada").Value;
        user.Disable();

        var result = user.Lock();

        result.IsFailure.Should().BeTrue();
        result.Error.Status.Should().Be(409);
    }

    [Fact]
    public void UserId_New_is_not_empty_and_rejects_empty()
    {
        UserId.New().Value.Should().NotBe(System.Guid.Empty);

        var act = () => UserId.From(System.Guid.Empty);
        act.Should().Throw<System.Exception>().Which.Message.Should().Contain("empty");
    }
}
