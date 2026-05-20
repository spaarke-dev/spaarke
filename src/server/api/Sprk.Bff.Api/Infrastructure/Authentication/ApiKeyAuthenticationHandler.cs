using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Infrastructure.Authentication;

/// <summary>
/// Generic <see cref="AuthenticationHandler{TOptions}"/> that validates an API key carried in an
/// HTTP header against a configuration-bound expected value (task AUTHV2-045).
/// </summary>
/// <remarks>
/// <para>
/// This handler is intended for service-to-service / CLI / webhook scenarios where OAuth/OBO is
/// not available. The constant-time comparison (<see cref="CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan{byte}, System.ReadOnlySpan{byte})"/>)
/// protects against timing-attack discovery of the secret.
/// </para>
/// <para>
/// Multiple named instances of this handler can be registered (one per consumer) via
/// <c>AddScheme&lt;ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler&gt;(name, configure)</c>,
/// each binding to a different configuration key. Endpoints opt-in via
/// <c>.RequireAuthorization("PolicyName")</c> where the policy specifies the scheme.
/// </para>
/// <para>
/// Failures intentionally return <see cref="AuthenticateResult.NoResult"/> (when the header is
/// absent) rather than a hard failure, so endpoints that allow either OAuth bearer or API key can
/// be composed via authorization policies. Endpoints that require API key authentication should
/// use <c>RequireAuthenticatedUser</c> in the policy to force a 401 on missing credentials.
/// </para>
/// </remarks>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IConfiguration _configuration;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headerName = Options.HeaderName;
        if (!Request.Headers.TryGetValue(headerName, out var providedValues))
        {
            // Header absent → leave room for other schemes (e.g., JwtBearer) to authenticate.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedKey = providedValues.ToString();
        if (string.IsNullOrEmpty(providedKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (string.IsNullOrEmpty(Options.ConfigKey))
        {
            Logger.LogError(
                "ApiKey scheme {Scheme} has no ConfigKey configured. Rejecting request.",
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Fail("API key scheme not configured."));
        }

        var expectedKey = _configuration[Options.ConfigKey];
        if (string.IsNullOrEmpty(expectedKey))
        {
            Logger.LogError(
                "ApiKey scheme {Scheme} expects configuration value at {ConfigKey} but none was found.",
                Scheme.Name,
                Options.ConfigKey);
            return Task.FromResult(AuthenticateResult.Fail("API key not configured on server."));
        }

        if (!FixedTimeEqualsString(providedKey, expectedKey))
        {
            Logger.LogWarning(
                "ApiKey scheme {Scheme} rejected invalid key from {RemoteIp}.",
                Scheme.Name,
                Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identityName = Options.IdentityName ?? Scheme.Name;
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, identityName),
            new Claim("auth_scheme", Scheme.Name),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        Logger.LogInformation(
            "ApiKey scheme {Scheme} authenticated request as {Identity}.",
            Scheme.Name,
            identityName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Constant-time string compare to thwart timing-attack key recovery.
    /// Compares the UTF-8 byte representations; differing lengths still take the same time as
    /// the longer string by padding the comparison.
    /// </summary>
    private static bool FixedTimeEqualsString(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);

        // Pad to the same length so the comparison is still constant-time across length mismatch.
        var maxLength = Math.Max(aBytes.Length, bBytes.Length);
        var aPadded = new byte[maxLength];
        var bPadded = new byte[maxLength];
        Buffer.BlockCopy(aBytes, 0, aPadded, 0, aBytes.Length);
        Buffer.BlockCopy(bBytes, 0, bPadded, 0, bBytes.Length);

        var equal = CryptographicOperations.FixedTimeEquals(aPadded, bPadded);
        // Mix in the length check (still constant time — both branches do equivalent work).
        return equal & (aBytes.Length == bBytes.Length);
    }
}
