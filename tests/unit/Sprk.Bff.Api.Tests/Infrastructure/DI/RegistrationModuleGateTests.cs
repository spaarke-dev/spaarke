// Task 261 (G31): the demo self-service registration feature — the BFF's only app-only caller of Graph /users and
// /groups (directory WRITE) — is registered only where DemoProvisioning:AccountDomain is configured (Spaarke's platform
// BFF). On a customer stamp it is absent, so the stamp identity needs no directory role, and the stamp no longer fails
// at start constructing DemoExpirationService without its [Required] options.

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

public class RegistrationModuleGateTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    /// <summary>The complete set the feature needs (every [Required] key of DemoProvisioningOptions and its licenses).</summary>
    private static (string Key, string? Value)[] FullSet() => new (string, string?)[]
    {
        ("DemoProvisioning:AccountDomain", "demo.spaarke.com"),
        ("DemoProvisioning:DemoUsersGroupId", "00000000-0000-0000-0000-0000000000d1"),
        ("DemoProvisioning:Licenses:PowerAppsPlan2TrialSkuId", "00000000-0000-0000-0000-0000000000d2"),
        ("DemoProvisioning:Licenses:FabricFreeSkuId", "00000000-0000-0000-0000-0000000000d3"),
        ("DemoProvisioning:Licenses:PowerAutomateFreeSkuId", "00000000-0000-0000-0000-0000000000d4"),
        ("DemoProvisioning:AdminNotificationEmails:0", "admin@spaarke.com"),
    };

    [Fact]
    public void IsDemoProvisioningEnabled_TrueOnlyForTheCompleteSet()
    {
        RegistrationModule.IsDemoProvisioningEnabled(Config(FullSet())).Should().BeTrue();
        RegistrationModule.IsDemoProvisioningEnabled(Config()).Should().BeFalse();
    }

    [Theory]
    [InlineData("DemoProvisioning:AccountDomain")]
    [InlineData("DemoProvisioning:DemoUsersGroupId")]
    [InlineData("DemoProvisioning:Licenses:PowerAppsPlan2TrialSkuId")]
    [InlineData("DemoProvisioning:Licenses:FabricFreeSkuId")]
    [InlineData("DemoProvisioning:Licenses:PowerAutomateFreeSkuId")]
    [InlineData("DemoProvisioning:AdminNotificationEmails:0")]
    public void IsDemoProvisioningEnabled_AnyMissingOrBlankRequiredKey_DisablesTheFeature_NotTheHost(string missing)
    {
        // A PARTIAL set would make DemoExpirationService's options read throw at host start. It fails closed instead.
        var withoutKey = FullSet().Where(v => v.Key != missing).ToArray();
        var blank = FullSet().Select(v => v.Key == missing ? (v.Key, (string?)"  ") : v).ToArray();

        RegistrationModule.IsDemoProvisioningEnabled(Config(withoutKey)).Should().BeFalse();
        RegistrationModule.IsDemoProvisioningEnabled(Config(blank)).Should().BeFalse();

        var services = new ServiceCollection();
        services.AddRegistrationModule(Config(withoutKey));
        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(DemoExpirationService));
    }

    [Fact]
    public void OnAStamp_TheDirectoryWritingServices_AndTheExpiryJob_AreNotRegistered()
    {
        var services = new ServiceCollection();

        services.AddRegistrationModule(Config());

        services.Should().NotContain(d => d.ServiceType == typeof(GraphUserService),
            "GraphUserService creates, licenses and disables Entra users — a stamp identity must not need those roles");
        services.Should().NotContain(d => d.ServiceType == typeof(DemoProvisioningService));
        services.Should().NotContain(d => d.ServiceType == typeof(EmailDomainValidator));
        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(DemoExpirationService),
            "its constructor reads the [Required] DemoProvisioning options, which a stamp does not have");
    }

    [Fact]
    public void OnAStamp_TheServicesOtherModulesUse_StayRegistered()
    {
        var services = new ServiceCollection();

        services.AddRegistrationModule(Config());

        // External access, communication and identity-link reconciliation consume these.
        services.Should().Contain(d => d.ServiceType == typeof(RegistrationDataverseService));
        services.Should().Contain(d => d.ServiceType == typeof(RegistrationEmailService));
        services.Should().Contain(d => d.ServiceType == typeof(PasswordGenerator));
        services.Should().Contain(d => d.ServiceType == typeof(DataverseEnvironmentService));
    }

    [Fact]
    public void WithoutTheSettings_TheExpiryJobsOptionsCannotBeRead_WhichIsWhyAStampMustNotRegisterIt()
    {
        // Pins finding F1: DemoExpirationService (a hosted service) reads IOptions<DemoProvisioningOptions>.Value in its
        // constructor. With no DemoProvisioning:* settings — every stamp — that read throws, so the host failed to start.
        var services = new ServiceCollection();
        services.AddRegistrationModule(Config());
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Sprk.Bff.Api.Configuration.DemoProvisioningOptions>>().Value;

        read.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>();
    }

    [Fact]
    public void OnThePlatformBff_TheFeatureIsRegistered()
    {
        var services = new ServiceCollection();

        services.AddRegistrationModule(Config(FullSet()));

        services.Should().Contain(d => d.ServiceType == typeof(GraphUserService));
        services.Should().Contain(d => d.ServiceType == typeof(DemoProvisioningService));
        services.Should().Contain(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(DemoExpirationService));
    }
}
