// customer-provisioning-orchestration-r1 — Task 258 (204e rows F5–F8, 2026-10-09)
// The fail-fast paths behind the four keys a stamp BFF demanded at start but no stamp channel wrote
// (.claude/constraints/bff-extensions.md §F.5 item 3): each validator rejects the missing value with a
// message naming the key, per environment class, and the Onboarding rule applies only behind its gate.
// The stamp VALUES are proved on the L2 side (H4bBulkAppSettingsHandlerTests.T258_*).

using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Endpoints.Onboarding;
using Xunit;

namespace Sprk.Bff.Api.Tests.OptionsValidation;

[Trait("Category", "Configuration")]
public class StampStartupSettingsValidationTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Sprk.Bff.Api.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static FakeHostEnvironment Env(string name) => new() { EnvironmentName = name };

    // ── PublicConfig (204e-F6): BffUrl / MsalClientId / TenantId ─────────────────────────────────────

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void PublicConfig_Empty_OutsideDevelopmentAndTesting_FailsNamingEachKey(string environment)
    {
        var result = new PublicConfigOptionsValidator(Env(environment)).Validate(null, new PublicConfigOptions());

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("PublicConfig:BffUrl"))
            .And.Contain(f => f.Contains("PublicConfig:MsalClientId"))
            .And.Contain(f => f.Contains("PublicConfig:TenantId"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void PublicConfig_Empty_InDevelopmentOrTesting_Succeeds(string environment)
        => new PublicConfigOptionsValidator(Env(environment)).Validate(null, new PublicConfigOptions())
            .Succeeded.Should().BeTrue();

    [Fact]
    public void PublicConfig_TheStampValues_SucceedInProduction()
    {
        var options = new PublicConfigOptions
        {
            BffUrl = "https://sprk-acme-prod-api.azurewebsites.net",
            MsalClientId = "00000000-aaaa-bbbb-cccc-999999999999",
            TenantId = "00000000-1111-2222-3333-444444444444",
        };

        new PublicConfigOptionsValidator(Env(Environments.Production)).Validate(null, options).Succeeded.Should().BeTrue();
    }

    // ── Graph:Scopes (204e-F7) and ServiceBus:QueueName (204e-F8): DataAnnotations, every environment ──

    [Fact]
    public void GraphScopes_Empty_FailsNamingTheKey()
    {
        var failures = DataAnnotationFailures(new GraphOptions { TenantId = "t", ClientId = "c" });

        // An empty array passes [Required] and fails [MinLength(1)]; ValidateDataAnnotations reports it as
        // "GraphOptions members: 'Scopes' with the error: …", i.e. by member name.
        failures.Should().ContainSingle().Which.Should().StartWith("Scopes:");
    }

    [Fact]
    public void ServiceBusQueueName_Empty_FailsNamingTheKey()
    {
        var failures = DataAnnotationFailures(new ServiceBusOptions());

        failures.Should().ContainSingle().Which.Should().Be("QueueName: ServiceBus:QueueName is required");
    }

    [Fact]
    public void GraphScopesAndServiceBusQueueName_TheStampValues_Validate()
    {
        DataAnnotationFailures(new GraphOptions { TenantId = "t", ClientId = "c", Scopes = ["https://graph.microsoft.com/.default"] })
            .Should().BeEmpty();
        DataAnnotationFailures(new ServiceBusOptions { QueueName = "sdap-jobs" }).Should().BeEmpty();
    }

    private static List<string> DataAnnotationFailures(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results.Select(r => $"{string.Join(",", r.MemberNames)}: {r.ErrorMessage}").ToList();
    }

    // ── Onboarding (204e-F5): the HMAC key rule applies only behind Onboarding:Enabled ──────────────

    private static ServiceProvider BuildOnboarding(string environment, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddOnboardingModule(configuration, Env(environment));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Onboarding_NotEnabled_InProduction_RegistersNothingAndDemandsNoKey()
    {
        // A customer stamp: Onboarding:Enabled is written by no stamp channel.
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddOnboardingModule(configuration, Env(Environments.Production));

        services.Should().BeEmpty("no stamp can serve the Model 2 consent callback, so nothing is registered or validated");
        OnboardingModule.IsEnabled(configuration).Should().BeFalse("the route is mapped only when the same gate is on");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Onboarding_Enabled_WithoutSigningKey_OutsideDevelopmentAndTesting_FailsNamingTheKey(string environment)
    {
        using var provider = BuildOnboarding(environment, (OnboardingOptions.EnabledConfigKey, "true"));

        var act = () => provider.GetRequiredService<IOptions<OnboardingOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Onboarding:HmacSigningKey*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Onboarding_Enabled_WithoutSigningKey_InDevelopmentOrTesting_Starts(string environment)
    {
        using var provider = BuildOnboarding(environment, (OnboardingOptions.EnabledConfigKey, "true"));

        provider.GetRequiredService<IOptions<OnboardingOptions>>().Value.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Onboarding_Enabled_WithSigningKey_InProduction_Validates()
    {
        using var provider = BuildOnboarding(Environments.Production,
            (OnboardingOptions.EnabledConfigKey, "true"), ("Onboarding:HmacSigningKey", "a-key-from-key-vault"));

        provider.GetRequiredService<IOptions<OnboardingOptions>>().Value.HmacSigningKey.Should().Be("a-key-from-key-vault");
    }
}
