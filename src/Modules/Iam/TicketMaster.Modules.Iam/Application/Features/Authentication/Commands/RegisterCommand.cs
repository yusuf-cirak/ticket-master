using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Identity;
using BuildingBlocks.Domain.Errors;
using TicketMaster.Modules.Iam.Domain;
using YC.Monad;
using DomainEmail = BuildingBlocks.Domain.ValueObjects.Email;

namespace TicketMaster.Modules.Iam.Application.Features.Authentication.Commands;

/// <summary>Registers a new user account. Domain factory failures and Identity errors flow back as Result failures.</summary>
public static class RegisterCommand
{
    public sealed record Command(string Email, string DisplayName, string Password) : IRequest<Result<Response>>;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty();
            RuleFor(x => x.DisplayName).NotEmpty();
            RuleFor(x => x.Password).NotEmpty().MinimumLength(12);
        }
    }

    public sealed class Handler(UserManager<User> userManager) : IRequestHandler<Command, Result<Response>>
    {
        public async ValueTask<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            var email = DomainEmail.Create(request.Email);
            if (!email.IsSuccess)
                return email.Error;

            var userResult = User.Register(email.Value, request.DisplayName);
            if (!userResult.IsSuccess)
                return userResult.Error;

            var user = userResult.Value;
            var create = await userManager.CreateAsync(user, request.Password);
            if (!create.Succeeded)
            {
                // Preserve the old field→messages 400 shape (e.g. duplicate email → the unique-email code).
                var errors = create.Errors
                    .GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
                return new ValidationError(errors);
            }

            return Result.Success(new Response(user.Id.Value));
        }
    }

    public sealed record Response(Guid UserId);
}
