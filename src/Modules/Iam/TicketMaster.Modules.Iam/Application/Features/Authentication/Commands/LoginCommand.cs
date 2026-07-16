using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using TicketMaster.Modules.Iam.Application.Abstractions;
using TicketMaster.Modules.Iam.Application.Features.Authentication;
using TicketMaster.Modules.Iam.Domain;
using BuildingBlocks.Web;
using YC.Monad;

namespace TicketMaster.Modules.Iam.Application.Features.Authentication.Commands;

/// <summary>Password login. Wrong password drives Identity lockout; every failure returns the same generic 401.</summary>
public static class LoginCommand
{
    public sealed record Command(string Email, string Password) : IRequest<Result<AuthTokensResponse>>;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty();
            RuleFor(x => x.Password).NotEmpty();
        }
    }

    public sealed class Handler(
        UserManager<User> userManager,
        IAuthTokenIssuer issuer,
        IHttpContextAccessor httpContextAccessor) : IRequestHandler<Command, Result<AuthTokensResponse>>
    {
        public async Task<Result<AuthTokensResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null || await userManager.IsLockedOutAsync(user))
                return AuthErrors.InvalidCredentials;

            if (!await userManager.CheckPasswordAsync(user, request.Password))
            {
                await userManager.AccessFailedAsync(user); // drives lockout after N failures
                return AuthErrors.InvalidCredentials;
            }

            await userManager.ResetAccessFailedCountAsync(user);

            var ip = httpContextAccessor.HttpContext?.GetClientIpAddress() ?? string.Empty;
            var tokens = await issuer.IssueAsync(user, ip, cancellationToken);
            return Result.Success(tokens);
        }
    }
}
