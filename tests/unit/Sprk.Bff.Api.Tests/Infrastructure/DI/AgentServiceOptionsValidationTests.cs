// code-quality-and-assurance-r3 — task 061 (uniform fail-fast configuration validation).
//
// ADR-038 note: these are NOT DI-wiring assertions. They prove the OBSERVABLE startup contract for a
// kill-switch-gated option (AgentServiceOptions, ADR-018): the "required-WHEN-enabled" (Enabled→Endpoint/
// AgentId) invariant is enforced by AgentServiceOptionsValidator + ValidateOnStart, so a MISCONFIGURED
// Enabled=true host fails AT STARTUP naming the offending key(s) — while a disabled host (Enabled=false,
// no Foundry config) still boots cleanly (no eager-.Value 2026-06-09 BingGrounding regression). The host
// is started for real so the ValidateOnStart hook actually runs; this is the honest fail-fast proof.

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.Foundry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

public class AgentServiceOptionsValidationTests
{
    // Builds a minimal host that registers ONLY the AgentService options under the same canonical
    // chain ConfigurationModule uses (Bind + ValidateDataAnnotations + ValidateOnStart) plus the
    // cross-property validator — so host.StartAsync() exercises the real startup-validation path.
    private static IHost BuildHost(Dictionary<string, string?> settings)
    {
        return new HostBuilder()
            .ConfigureAppConfiguration(cb => cb.AddInMemoryCollection(settings))
            .ConfigureServices((ctx, services) =>
            {
                services
                    .AddOptions<AgentServiceOptions>()
                    .Bind(ctx.Configuration.GetSection(AgentServiceOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                services.AddSingleton<IValidateOptions<AgentServiceOptions>, AgentServiceOptionsValidator>();
            })
            .Build();
    }

    [Fact]
    public async Task StartAsync_WhenEnabledTrueAndEndpointMissing_FailsAtStartupNamingTheKey()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["AgentService:Enabled"] = "true",
            ["AgentService:AgentId"] = "agent-123",
            // AgentService:Endpoint intentionally absent
        });

        var act = async () => await host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain("AgentService:Endpoint");
    }

    [Fact]
    public async Task StartAsync_WhenEnabledTrueAndMultipleKeysMissing_AggregatesAllFailures()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["AgentService:Enabled"] = "true",
            // Both Endpoint and AgentId absent → both must be reported, not just the first.
        });

        var act = async () => await host.StartAsync();

        var ex = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        ex.Failures.Should().Contain(f => f.Contains("AgentService:Endpoint"));
        ex.Failures.Should().Contain(f => f.Contains("AgentService:AgentId"));
    }

    [Fact]
    public async Task StartAsync_WhenEnabledTrueAndFullyConfigured_Boots()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["AgentService:Enabled"] = "true",
            ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
            ["AgentService:AgentId"] = "agent-123",
        });

        var act = async () => await host.StartAsync();

        await act.Should().NotThrowAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_WhenDisabledAndNoFoundryConfig_Boots()
    {
        // The gated boot path (ADR-018): kill switch off + Endpoint/AgentId absent MUST still start.
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["AgentService:Enabled"] = "false",
        });

        var act = async () => await host.StartAsync();

        await act.Should().NotThrowAsync();
        await host.StopAsync();
    }
}
