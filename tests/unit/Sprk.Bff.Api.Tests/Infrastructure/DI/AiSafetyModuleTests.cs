// customer-provisioning-orchestration-r1 — Task 246 (plan G26, 2026-10-06)
// AiSafetyModule's Content Safety endpoint rule: required outside Development/Testing (no default — the old
// fallback named Spaarke's dev account), optional in Development/Testing (scans then fail open), and the
// configured endpoint is what the named "ContentSafety" client calls.

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Ai.Safety;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

public class AiSafetyModuleTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Sprk.Bff.Api.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(string? endpoint) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [AiSafetyModule.EndpointConfigKey] = endpoint })
        .Build();

    private static FakeHostEnvironment Env(string name) => new() { EnvironmentName = name };

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", "")]
    [InlineData("Staging", "   ")]
    public void EndpointUnset_OutsideDevelopmentAndTesting_ThrowsNamingTheSetting(string environment, string? endpoint)
    {
        var act = () => AiSafetyModule.ResolveContentSafetyBaseAddress(Config(endpoint), Env(environment));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AiSafety__ContentSafety__Endpoint*required for AiSafetyModule*except Development and Testing*")
            .Which.Message.Should().NotContain("spaarke-contentsafety-dev");
    }

    [Fact]
    public void EndpointUnset_InProduction_AddAiSafetyModuleFailsStartup()
    {
        var act = () => new ServiceCollection().AddAiSafetyModule(Config(null), Env(Environments.Production));

        act.Should().Throw<InvalidOperationException>().WithMessage("*AiSafety:ContentSafety:Endpoint*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("testing")]
    public void EndpointUnset_InDevelopmentOrTesting_HasNoBaseAddress(string environment)
    {
        AiSafetyModule.ResolveContentSafetyBaseAddress(Config(null), Env(environment)).Should().BeNull(
            "local and test hosts boot without Content Safety; scans fail open through the services' own catch");
    }

    [Fact]
    public async Task EndpointUnset_InTesting_PromptShieldScanFailsOpen_InsteadOfThrowing()
    {
        // The Development/Testing carve-out relies on PromptShieldService's own catch: with no base address the
        // relative request throws inside the service, which must report a fail-open verdict, not throw to the caller.
        var configuration = Config(null);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<AiTelemetry>();
        services.AddAiSafetyModule(configuration, Env("Testing"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var shield = scope.ServiceProvider.GetRequiredService<IPromptShieldService>();

        var result = await shield.ScanAsync(new PromptShieldRequest("What is the capital of France?"));

        result.FailedOpen.Should().BeTrue("an unconfigured perimeter must fail open, never block or crash the turn");
    }

    [Theory]
    [InlineData("https://sprk-acme-prod-contentsafety.cognitiveservices.azure.com")]
    [InlineData("https://sprk-acme-prod-contentsafety.cognitiveservices.azure.com/")]
    [InlineData(" https://sprk-acme-prod-contentsafety.cognitiveservices.azure.com/ ")]
    public void EndpointSet_IsTheNamedClientsBaseAddress(string endpoint)
    {
        var configuration = Config(endpoint);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAiSafetyModule(configuration, Env(Environments.Production));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(PromptShieldService.HttpClientName);

        client.BaseAddress.Should().Be(new Uri("https://sprk-acme-prod-contentsafety.cognitiveservices.azure.com/"));
    }

    [Theory]
    [InlineData("http://sprk-acme-prod-contentsafety.cognitiveservices.azure.com/")]
    [InlineData("sprk-acme-prod-contentsafety")]
    public void EndpointNotAbsoluteHttps_Throws_InAnyEnvironment(string endpoint)
    {
        var act = () => AiSafetyModule.ResolveContentSafetyBaseAddress(Config(endpoint), Env(Environments.Development));

        act.Should().Throw<InvalidOperationException>().WithMessage("*absolute https URI*");
    }
}
