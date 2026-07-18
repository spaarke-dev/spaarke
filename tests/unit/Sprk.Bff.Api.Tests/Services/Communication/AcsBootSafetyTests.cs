using Azure.Communication.Chat;
using Azure.Communication.Identity;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Acs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Regression guard for the 2026-07-17 production-down incident: the merged BFF crash-looped at startup
/// (SIGABRT / exit 134) because the ACS client factories throw when <c>Communication:Acs:Endpoint</c> is
/// unconfigured, and <see cref="AcsThreadService"/> / <see cref="AcsIdentityService"/> are reachable from the
/// startup hosted-service graph (MembershipReconciler → MembershipReconcileSweepService). The fix (ADR-032
/// boot-safety) makes those services inject <c>Lazy&lt;client&gt;</c> so CONSTRUCTION never builds the client —
/// the endpoint check fires only on first actual ACS call.
///
/// <para>These tests pin the deferred-failure contract as BEHAVIOR (not a DI-registration smoke test): with no
/// ACS endpoint configured, the services still construct (the whole BFF still boots); an actual ACS operation
/// then fails with the clear config error.</para>
/// </summary>
public class AcsBootSafetyTests
{
    private static ServiceProvider BuildProviderWithNoAcsEndpoint()
    {
        // Empty configuration — Communication:Acs:Endpoint is intentionally absent (the crash scenario).
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<TokenCredential>());
        services.AddSingleton(Mock.Of<IGenericEntityService>());

        services.AddAcsIdentityPlane(configuration);
        services.AddAcsThreadPlane();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AcsServices_WithNoEndpointConfigured_ConstructWithoutThrowing()
    {
        using var provider = BuildProviderWithNoAcsEndpoint();

        // Constructing the services must NOT build the ACS clients (the throwing factories stay dormant),
        // so the startup hosted-service graph — and therefore the whole BFF — boots with no ACS config.
        var identity = () => provider.GetRequiredService<IAcsIdentityService>();
        var thread = () => provider.GetRequiredService<IAcsThreadService>();

        identity.Should().NotThrow("a BFF with no ACS config must still boot (ADR-032 boot-safety)");
        thread.Should().NotThrow("a BFF with no ACS config must still boot (ADR-032 boot-safety)");
    }

    [Fact]
    public async Task AcsThreadOperation_WithNoEndpointConfigured_ThrowsClearConfigError()
    {
        using var provider = BuildProviderWithNoAcsEndpoint();
        var threadService = provider.GetRequiredService<IAcsThreadService>();

        // The failure is DEFERRED to first use: building the ChatClient (via Lazy.Value) throws the clear,
        // actionable config error rather than an opaque startup SIGABRT.
        var act = async () => await threadService.CreateThreadAsync("topic", new[] { "8:acs:user" });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Communication:Acs:Endpoint must be configured");
    }

    [Fact]
    public async Task AcsIdentityOperation_WithNoEndpointConfigured_ThrowsClearConfigError()
    {
        using var provider = BuildProviderWithNoAcsEndpoint();
        var identityService = provider.GetRequiredService<IAcsIdentityService>();

        var act = async () => await identityService.MintChatTokenAsync("8:acs:user");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Communication:Acs:Endpoint must be configured");
    }
}
