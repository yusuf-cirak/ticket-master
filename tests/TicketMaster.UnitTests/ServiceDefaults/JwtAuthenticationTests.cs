using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TicketMaster.ServiceDefaults;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class JwtAuthenticationTests
{
    private static JwtBearerOptions ResolveBearerOptions()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["Authentication:Mode"] = "Symmetric",
            ["Authentication:Issuer"] = "ticketmaster",
            ["Authentication:Audience"] = "ticketmaster-api",
            ["Authentication:SigningKey"] = "dev-only-not-a-secret-change-me-min-32-bytes-0000"
        });
        builder.AddJwtAuthentication();
        var app = builder.Build();

        return app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void Keeps_sub_unmapped_and_hardens_validation()
    {
        var o = ResolveBearerOptions();

        o.MapInboundClaims.Should().BeFalse();
        o.TokenValidationParameters.NameClaimType.Should().Be("sub");
        o.TokenValidationParameters.ClockSkew.Should().Be(TimeSpan.FromSeconds(30));
        o.TokenValidationParameters.ValidateIssuer.Should().BeTrue();
        o.TokenValidationParameters.ValidateAudience.Should().BeTrue();
        o.TokenValidationParameters.ValidateLifetime.Should().BeTrue();
        o.TokenValidationParameters.RequireExpirationTime.Should().BeTrue();
        o.TokenValidationParameters.RequireSignedTokens.Should().BeTrue();
    }

    [Fact]
    public void Wires_symmetric_signing_key_from_config()
    {
        var o = ResolveBearerOptions();

        o.TokenValidationParameters.IssuerSigningKey.Should().BeOfType<SymmetricSecurityKey>();
        o.TokenValidationParameters.ValidIssuer.Should().Be("ticketmaster");
        o.TokenValidationParameters.ValidAudience.Should().Be("ticketmaster-api");
    }
}
