// -----------------------------------------------------------------------------
// WorkerSecretFreeBootTests.cs
//
// A44.5 (task 205i, 2026-08-25) — L2 Worker secret-free BOOT contract over the
// REAL Worker composition root (WebApplicationFactory<Worker.Program>, same
// harness discipline as HandlerRegistrationCompletenessTests: hosted services
// stripped, syntactically-valid-but-unreachable endpoints, Testing env).
//
// WHAT "BOOT" MEANS HERE (honest scope, per the POML's
// webapplicationfactory-unavailable degradation note — this is the STRONGER
// available form, not the degraded one): the production DI container is
// composed and the AddOptions<T>().Validate(...) chains are executed by
// resolving IOptions<T>.Value (the exact validators ValidateOnStart forces at
// real boot — ValidationHostedService is stripped with the other hosted
// services, so the validators are triggered explicitly instead). What this
// deliberately does NOT prove: a live MI-FIC token exchange — L2 cannot
// exchange-verify from a test host (remediation plan §5 item 2 / SF-4:
// "L2 cannot exchange-verify — real proof lands post-App-Service" in the
// H13/T4 E2E, task 186).
//
// COVERAGE:
//   B1  Secret-free config (EnvVarValues__ClientSecret ABSENT + FR-39 chain
//       settings exactly as the Bicep module emits under
//       requireSecretFreeIdentity=true) → EnvVarValuesOptions +
//       SolutionImportOptions BOTH resolve + validate; H7/H6 handler graphs
//       (incl. WorkerDataverseCredentialFactory) resolve from keyed DI.
//       No boot-loop, no sentinel anywhere in the fixture.
//   B2  Legacy config with the secret ABSENT and NO chain configured →
//       options resolution FAIL-FASTS (task-142 boot guard preserved for
//       prong-3 unmigrated envs — the relaxation is chain-scoped, not
//       unconditional).
// -----------------------------------------------------------------------------

extern alias WorkerHost;

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
using Xunit;
using WorkerProgram = WorkerHost::Program;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

public sealed class WorkerSecretFreeBootTests
    : IClassFixture<SecretFreeWorkerTestFactory>, IClassFixture<LegacyMissingSecretWorkerTestFactory>
{
    private readonly SecretFreeWorkerTestFactory _secretFreeFactory;
    private readonly LegacyMissingSecretWorkerTestFactory _legacyMissingSecretFactory;

    public WorkerSecretFreeBootTests(
        SecretFreeWorkerTestFactory secretFreeFactory,
        LegacyMissingSecretWorkerTestFactory legacyMissingSecretFactory)
    {
        _secretFreeFactory = secretFreeFactory;
        _legacyMissingSecretFactory = legacyMissingSecretFactory;
    }

    // ---------- B1 secret-free boot succeeds ----------

    [Fact]
    public void SecretFreeConfig_EnvVarValuesOptions_ResolveAndValidate_WithEmptySecretSlot()
    {
        var options = _secretFreeFactory.Services
            .GetRequiredService<IOptions<EnvVarValuesOptions>>().Value; // runs the Validate chain

        options.ClientSecret.Should().BeNullOrEmpty(
            "the secret-free fixture omits EnvVarValues__ClientSecret entirely — empty is the signal " +
            "(auth-v4 §9.1), never a sentinel");
        options.Credentials.ResolveEffectiveOrder(EnvVarValuesOptions.SectionName)
            .Should().Equal(CredentialKind.ManagedIdentityFederated);
    }

    [Fact]
    public void SecretFreeConfig_SolutionImportOptions_ResolveAndValidate_WithEmptySecretSlot()
    {
        var options = _secretFreeFactory.Services
            .GetRequiredService<IOptions<SolutionImportOptions>>().Value; // PostConfigure runs Validate

        options.ClientSecret.Should().BeNullOrEmpty();
        options.Credentials.ResolveEffectiveOrder(SolutionImportOptions.SectionName)
            .Should().Equal(CredentialKind.ManagedIdentityFederated);
    }

    [Theory]
    [InlineData(HandlerIds.H6)]
    [InlineData(HandlerIds.H7)]
    public void SecretFreeConfig_H6AndH7HandlerGraphs_ResolveFromKeyedDi(string handlerId)
    {
        using var scope = _secretFreeFactory.Services.CreateScope();

        var handler = scope.ServiceProvider.GetKeyedService<IProvisioningHandler>(handlerId);

        handler.Should().NotBeNull(
            $"'{handlerId}' must compose on a secret-free Worker — its collaborators (incl. " +
            "WorkerDataverseCredentialFactory) resolve without any BFF-API-ClientSecret material");
        handler!.HandlerId.Should().Be(handlerId);
    }

    // NOTE (ADR-038): no bare "factory is registered" DI assertion here — that
    // is the banned DI-registration test class. The keyed H6/H7 graph
    // resolutions above already construct WorkerDataverseCredentialFactory
    // transitively (H7 writer ctor + H6 importer factory lambda), which is
    // the behavioral form of the same guarantee.

    // ---------- B2 legacy boot guard preserved ----------

    [Fact]
    public void LegacyConfig_MissingSecret_NoChainConfigured_FailsFastAtOptionsResolution()
    {
        var act = () => _legacyMissingSecretFactory.Services
            .GetRequiredService<IOptions<EnvVarValuesOptions>>().Value;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EnvVarValues:ClientSecret*required*",
                "prong-3 unmigrated environments (no FR-39 chain configured) MUST keep the task-142 " +
                "boot fail-fast — the A44.5 relaxation is strictly chain-scoped");
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> over the REAL Worker
/// composition root with the EXACT app-setting shape
/// modules/controlplane-worker-app-service.bicep emits when
/// <c>requireSecretFreeIdentity=true</c>: NO <c>EnvVarValues__ClientSecret</c> /
/// <c>SolutionImportOptions__ClientSecret</c> / <c>CustomerRunGuard__ClientSecret</c>
/// KV-refs; FR-39 chain settings present instead. Everything else mirrors
/// <see cref="WorkerTestFactory"/> (HandlerRegistrationCompletenessTests).
/// </summary>
public sealed class SecretFreeWorkerTestFactory : WebApplicationFactory<WorkerProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ApplyCommonWorkerFixtureSettings(builder);

        // A44.5 secret-free contract — the four settings the Bicep module's
        // secretFreeCredentialAppSettings var emits (and the DELIBERATE
        // ABSENCE of every *__ClientSecret setting).
        builder.UseSetting("EnvVarValues:Credentials:Order:0", "ManagedIdentityFederated");
        builder.UseSetting("EnvVarValues:Credentials:RequireSecretFreeIdentity", "true");
        builder.UseSetting("SolutionImportOptions:Credentials:Order:0", "ManagedIdentityFederated");
        builder.UseSetting("SolutionImportOptions:Credentials:RequireSecretFreeIdentity", "true");
        builder.UseSetting("ManagedIdentity:ClientId", "11111111-aaaa-bbbb-cccc-222222222222");
    }

    /// <summary>
    /// The non-credential fixture settings shared with
    /// <see cref="WorkerTestFactory"/> (same syntactically-valid-but-
    /// unreachable values; same hosted-service strip; same Testing env).
    /// </summary>
    internal static void ApplyCommonWorkerFixtureSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("Cosmos:AccountEndpoint", "https://l2-test.documents.azure.com:443/");
        builder.UseSetting("ServiceBus:FullyQualifiedNamespace", "l2-test.servicebus.windows.net");
        builder.UseSetting("DataverseEnvironmentRegistry:AdminEnvironmentUrl", "https://l2-test.crm.dynamics.com/");
        builder.UseSetting("BicepInfraDeployOptions:ProvisioningArtifactsContainerUri", "https://l2-test.blob.core.windows.net/provisioning-artifacts");
        builder.UseSetting("BffDeployOptions:ProvisioningArtifactsContainerUri", "https://l2-test.blob.core.windows.net/provisioning-artifacts");
        builder.UseSetting("SolutionImportOptions:ProvisioningArtifactsContainerUri", "https://l2-test.blob.core.windows.net/provisioning-artifacts");
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            for (var i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == typeof(IHostedService))
                {
                    services.RemoveAt(i);
                }
            }
        });
    }
}

/// <summary>
/// Prong-3 (unmigrated) shape with a MISSING secret and NO FR-39 chain — the
/// misconfiguration task 142's boot guard exists to catch. Deliberately does
/// NOT set <c>EnvVarValues:ClientSecret</c>.
/// </summary>
public sealed class LegacyMissingSecretWorkerTestFactory : WebApplicationFactory<WorkerProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => SecretFreeWorkerTestFactory.ApplyCommonWorkerFixtureSettings(builder);
}
