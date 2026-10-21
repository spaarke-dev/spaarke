using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// IdentityModel's logger is process-wide (<c>LogHelper.Logger</c>) and Identity Web binds it, once, to the logger of the
/// first host built in the process. In a test run that host is a fixture that is disposed when its class finishes, and
/// every later IdentityModel log line then writes into its disposed logger factory: on Windows the EventLog provider
/// throws <see cref="ObjectDisposedException"/>, the logger wraps it in an <see cref="AggregateException"/>, and the token
/// handler returns THAT as the validation failure. An expired token then reads as "invalid", in whichever test happens to
/// run after the first host is gone (task 188 r3 slice B: <c>ExternalWorkforceLinkEndpointsTests</c>' "expired" case).
/// <see cref="IdentityModelLoggerIsolation"/> pins the logger for the whole process before any host is built.
/// </summary>
public sealed class IdentityModelLoggerIsolationTests
{
    [Fact]
    public async Task AnExpiredToken_StillReadsAsExpired_AfterAHostIsDisposed()
    {
        // A real BFF host, built and disposed (the first host of an isolated run is the one Identity Web would bind).
        var host = new ExternalWorkforceLinkEndpointsTests.LinkFixture();
        _ = host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        await host.DisposeAsync();

        var key = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "t188b-logger-isolation" };
        var expires = DateTime.UtcNow.AddMinutes(-10);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://issuer.example/",
            Audience = "api://audience.example",
            IssuedAt = expires.AddMinutes(-70),
            NotBefore = expires.AddMinutes(-70),
            Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://issuer.example/",
            ValidAudience = "api://audience.example",
            IssuerSigningKey = key,
            ValidateLifetime = true,
        });

        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<SecurityTokenExpiredException>(
            "IdentityModel's process-wide logger must not be a disposed host's logger: {0}", result.Exception?.ToString());
    }
}
