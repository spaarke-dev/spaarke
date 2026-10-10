using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Sprk.Bff.Api.Api;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api;

/// <summary>
/// Behaviour of <c>GET /api/config/client</c> (<see cref="ConfigEndpoints.GetClientConfig"/>) for the
/// browser telemetry connection string added by #1537 (spaarkeai-word-add-in-r1 task 127).
///
/// The handler is called directly with explicit configuration: CI sets
/// <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> process-wide (ci-tier1-blocking.yml), so a
/// WebApplicationFactory host cannot exercise the "not configured" case deterministically.
/// Anonymous access and the "anonymous" rate-limit policy on the route are asserted by the
/// RouteAuthorizationGuard ledger (tests/Spaarke.ArchTests), which this change leaves unchanged.
/// </summary>
public class ConfigEndpointsClientConfigTests
{
    private const string ValidConnectionString =
        "InstrumentationKey=00000000-0000-4000-8000-000000000127;" +
        "IngestionEndpoint=https://westus2-2.in.applicationinsights.azure.com/;" +
        "LiveEndpoint=https://westus2.livediagnostics.monitor.azure.com/;" +
        "ApplicationId=11111111-2222-3333-4444-555555555555";

    private static IConfiguration Config(string? appInsightsConnectionString)
    {
        var values = new Dictionary<string, string?>
        {
            ["AzureAd:ClientId"] = "1e40baad-0000-0000-0000-000000000001",
            ["AzureAd:TenantId"] = "a221a95e-6abc-4434-aecc-e48338a1b2f2",
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
        };
        if (appInsightsConnectionString is not null)
        {
            values["APPLICATIONINSIGHTS_CONNECTION_STRING"] = appInsightsConnectionString;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ConfigEndpoints.ClientConfigResponse Invoke(IConfiguration configuration)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("bff.example.com");

        var result = ConfigEndpoints.GetClientConfig(configuration, httpContext);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status200OK);
        return result.Should().BeAssignableTo<IValueHttpResult>()
            .Which.Value.Should().BeOfType<ConfigEndpoints.ClientConfigResponse>().Subject;
    }

    [Fact]
    public void Configured_ReturnsTheConnectionString()
    {
        var response = Invoke(Config(ValidConnectionString));

        response.AppInsightsConnectionString.Should().Be(ValidConnectionString);
    }

    [Fact]
    public void NotConfigured_ReturnsNull_AndTheRestOfTheResponseIsUnchanged()
    {
        var response = Invoke(Config(null));

        response.AppInsightsConnectionString.Should().BeNull();
        response.BffBaseUrl.Should().Be("https://bff.example.com");
        response.MsalClientId.Should().Be("1e40baad-0000-0000-0000-000000000001");
        response.MsalAuthority.Should().Be("https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2");
        response.MsalScopes.Should().Equal("api://1e40baad-0000-0000-0000-000000000001/user_impersonation");
        response.TenantId.Should().Be("a221a95e-6abc-4434-aecc-e48338a1b2f2");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    // An App Service Key Vault reference that failed to resolve stays as the literal reference.
    [InlineData("@Microsoft.KeyVault(VaultName=spaarke-kv;SecretName=AppInsights-ConnectionString)")]
    // Anything carrying a key outside the documented connection-string keys.
    [InlineData("InstrumentationKey=00000000-0000-4000-8000-000000000127;SharedAccessKey=abc")]
    // No (or a non-GUID) instrumentation key.
    [InlineData("IngestionEndpoint=https://westus2-2.in.applicationinsights.azure.com/")]
    [InlineData("InstrumentationKey=not-a-guid")]
    [InlineData("InstrumentationKey")]
    public void NotAPlainConnectionString_ReturnsNull(string value)
    {
        Invoke(Config(value)).AppInsightsConnectionString.Should().BeNull();
    }

    [Fact]
    public void WireShape_IsAdditive_CamelCaseField()
    {
        var response = Invoke(Config(ValidConnectionString));

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("appInsightsConnectionString").GetString().Should().Be(ValidConnectionString);
        foreach (var existing in new[] { "bffBaseUrl", "msalClientId", "msalAuthority", "msalScopes", "tenantId" })
        {
            doc.RootElement.TryGetProperty(existing, out _).Should().BeTrue($"'{existing}' is part of the existing contract");
        }
    }
}
