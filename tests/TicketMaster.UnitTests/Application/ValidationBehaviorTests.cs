using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using MediatR;
using TicketMaster.BuildingBlocks.Application.Behaviors;
using TicketMaster.BuildingBlocks.Domain.Errors;
using Xunit;
using YC.Monad;

namespace TicketMaster.UnitTests.Application;

public class ValidationBehaviorTests
{
    private sealed record Command(string Name);

    private static RequestHandlerDelegate<Result<string>> Next(Result<string>? value = null)
        => () => Task.FromResult(value ?? Result.Success("ok"));

    [Fact]
    public async Task Passes_through_when_no_validators()
    {
        var behavior = new ValidationBehavior<Command, Result<string>>([]);

        var result = await behavior.Handle(new Command("x"), Next(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }

    [Fact]
    public async Task Invokes_handler_when_valid()
    {
        var validator = new InlineValidator<Command>();
        validator.RuleFor(c => c.Name).NotEmpty();
        var behavior = new ValidationBehavior<Command, Result<string>>([validator]);

        var result = await behavior.Handle(new Command("ok"), Next(Result.Success("handled")), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("handled");
    }

    [Fact]
    public async Task Returns_ValidationError_failure_when_invalid()
    {
        var validator = new InlineValidator<Command>();
        validator.RuleFor(c => c.Name).NotEmpty();
        var behavior = new ValidationBehavior<Command, Result<string>>([validator]);

        var result = await behavior.Handle(new Command(""), Next(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        ((ValidationError)result.Error).Errors.Should().ContainKey(nameof(Command.Name));
    }
}
