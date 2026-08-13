using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using BuildingBlocks.Application.Abstractions.Security;
using BuildingBlocks.Security.Authorization;

namespace BuildingBlocks.Security;

/// <summary>
/// JWT bearer authentication + secure-by-default authorization. One configuration drives both signing strategies
/// (symmetric secret for dev/test, OIDC authority for production). Hardened defaults: <c>sub</c> kept un-remapped
/// (so the rate-limiter and log enrichment can read it), tight clock skew, signed-and-expiring tokens required,
/// and 401/403 returned as RFC 9457 problem+json consistent with the global exception handler.
/// </summary>
public static class JwtAuthenticationExtensions
{
    public static WebApplicationBuilder AddJwtAuthentication(this WebApplicationBuilder builder, Action<JwtOptions> configure = null)
    {
        var optionsBuilder = builder.Services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName);
        if (configure is not null)
            optionsBuilder.Configure(configure); // code-based config wins over the appsettings section
        optionsBuilder
            .ValidateDataAnnotations()
            .Validate(o => o.Mode != JwtMode.Symmetric || !string.IsNullOrWhiteSpace(o.SigningKey),
                "Authentication:SigningKey is required when Mode=Symmetric.")
            .Validate(o => o.Mode != JwtMode.Authority || !string.IsNullOrWhiteSpace(o.Authority),
                "Authentication:Authority is required when Mode=Authority.")
            .ValidateOnStart();

        var opts = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        configure?.Invoke(opts); // keep the eager copy (drives JwtBearer wiring below) in sync with code config

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false; // keep "sub" as "sub" — PrincipalPartitioner + log enrichment read it raw
                o.RequireHttpsMetadata = opts.RequireHttpsMetadata;

                if (opts.Mode == JwtMode.Authority)
                    o.Authority = opts.Authority; // JWKS auto-discovered; signing keys filled from metadata

                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = opts.Issuer,
                    ValidateAudience = true,
                    ValidAudience = opts.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(opts.ClockSkewSeconds),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                    IssuerSigningKey = opts.Mode == JwtMode.Symmetric && !string.IsNullOrWhiteSpace(opts.SigningKey)
                        ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey))
                        : null
                };

                o.Events = new JwtBearerEvents
                {
                    OnTokenValidated = RejectRevokedToken,
                    OnChallenge = WriteChallengeProblem,
                    OnForbidden = WriteForbiddenProblem,
                    OnAuthenticationFailed = SuppressFailureDetail
                };
            });

        // Secure by default: every endpoint requires an authenticated user unless it opts out with AllowAnonymous.
        var authenticated = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(authenticated)
            .SetDefaultPolicy(authenticated);

        builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

        return builder;
    }

    // The default challenge writes a bare 401; replace it with problem+json (the ProblemDetails customizer stamps
    // traceId + correlation_id). Detail stays generic so token-validation specifics are not leaked to the caller.
    private static async Task WriteChallengeProblem(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.Headers.WWWAuthenticate = $"Bearer error=\"{context.Error ?? "invalid_token"}\"";
        await WriteProblem(context.HttpContext, StatusCodes.Status401Unauthorized,
            "Unauthorized", "Authentication is required to access this resource.");
    }

    private static Task WriteForbiddenProblem(ForbiddenContext context) =>
        WriteProblem(context.HttpContext, StatusCodes.Status403Forbidden,
            "Forbidden", "You do not have permission to access this resource.");

    // Don't surface token-validation specifics to the caller; the generic challenge response stands. Details remain
    // in the trace/logs for diagnostics.
    private static Task SuppressFailureDetail(AuthenticationFailedContext context) => Task.CompletedTask;

    // A token can be signed, unexpired, and still revoked (logout / forced sign-out). Reject it if its jti is on the
    // blacklist. Fails open if the blacklist store is unavailable (the store swallows its own backing-store errors).
    private static async Task RejectRevokedToken(TokenValidatedContext context)
    {
        var jti = context.Principal?.FindFirst("jti")?.Value;
        if (string.IsNullOrEmpty(jti))
            return;

        var blacklist = context.HttpContext.RequestServices.GetService<ITokenBlacklist>();
        if (blacklist is not null && await blacklist.IsRevokedAsync(jti, context.HttpContext.RequestAborted))
            context.Fail("token_revoked");
    }

    private static async Task WriteProblem(HttpContext http, int status, string title, string detail)
    {
        if (http.Response.HasStarted)
            return;

        http.Response.StatusCode = status;
        var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        });
    }
}
