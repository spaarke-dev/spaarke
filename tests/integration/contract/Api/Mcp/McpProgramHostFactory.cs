using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Sprk.Bff.Api.Tests.Api.Mcp;

/// <summary>
/// TESTS ONLY (spaarkeai-mcp-bff-api-r1 task 025). The REAL BFF host (<c>Program</c>, through <see cref="CustomWebAppFactory"/>) with MCP
/// switched on and a real JwtBearer scheme that trusts <see cref="SigningKey"/>, so a request carries an ADR-028 A10 token the route's own
/// filter and per-tool policies judge unchanged. <see cref="CustomWebAppFactory"/>'s <c>FakeAuthHandler</c> would admit anything, so a
/// tools/list through it would prove nothing about the A10 route. Used by <c>McpHostContractTests</c> (the exact tool list through the
/// production assembly scan) and, subclassed, by <c>tests/integration/Mcp.Conformance.Host</c> (the conformance run). Nothing here, or
/// anywhere under src/, adds a test scheme, a switch or a bypass to the BFF: the scheme is registered in the test's own service
/// configuration.
/// </summary>
public class McpProgramHostFactory : CustomWebAppFactory
{
    // The ids McpToolAuthorization.A10DenyReason checks a token against: the Spaarke tenant and the BFF app.
    public const string TenantId = "11111111-2222-4333-8444-555555555555";
    public const string BffClientId = "66666666-7777-4888-9999-aaaaaaaaaaaa";
    public const string Issuer = "https://login.microsoftonline.com/" + TenantId + "/v2.0";
    public const string SchemeName = "McpProgramHostBearer";

    public static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("mcp-program-host-signing-key-0123456789-0123456789"));

    /// <summary>A workforce-style A10 token for the caller whose <c>oid</c> is <paramref name="oid"/>.</summary>
    public static string MintToken(Guid oid) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = BffClientId,
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        Claims = new Dictionary<string, object>
        {
            ["scp"] = "user_impersonation",
            ["oid"] = oid.ToString(),
            ["tid"] = TenantId,
        },
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder); // the shared BFF test host: config, Graph fake, stub credential, no hosted services

        // Mcp:Enabled is read while Program.cs registers services, so UseSetting (not a later configuration source).
        builder.UseSetting("Mcp:Enabled", "true");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = TenantId,
            ["AzureAd:ClientId"] = BffClientId,
        }));

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddJwtBearer(SchemeName, options => options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = BffClientId,
                IssuerSigningKey = SigningKey,
            });
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = SchemeName;
                options.DefaultChallengeScheme = SchemeName;
                options.DefaultScheme = SchemeName;
            });
            ConfigureSeededServices(services);
        });
    }

    /// <summary>Replace what a subclass seeds at the module boundary (the decision capability, the lookup search). Default: nothing.</summary>
    protected virtual void ConfigureSeededServices(IServiceCollection services)
    {
    }
}
