// -----------------------------------------------------------------------------
// HandlerRegistrationCompletenessTests.cs
//
// L2 CONTROL-PLANE keyed-DI registration completeness gate (task 103, Phase
// C'' Wave G-1 -- closes DS-2 §3.2 gap C1.2).
//
// PURPOSE:
//   Makes the NoHandler dead-letter dispatch outcome (DS-2 §2.8 table --
//   "no keyed registration for HandlerId") UNREACHABLE by build-time
//   assertion. Task 102's ProvisioningHandlerDispatcher resolves handlers
//   via scopedProvider.GetKeyedService<IProvisioningHandler>(envelope.HandlerId);
//   if a future handler addition/rename forgets its keyed line, this test
//   fails BEFORE the gap reaches a live dispatch.
//
// STRATEGY -- the REAL DI container, not a duplicate:
//   Uses WebApplicationFactory<Worker.Program> so the exact production
//   composition (Sprk.Provisioning.ControlPlane.Worker/Program.cs -- 14
//   inline handler registrations + 4 via per-handler *Module.cs extension
//   methods + H0 + all 19 keyed registrations via HandlersModule.cs /
//   HandlerDispatchRegistrationModule.cs) is exercised, matching the
//   RunsEndpointsTests.cs / L2WebApplicationFactory pattern already
//   established for the .Api host. Duplicating Program.cs's registration
//   calls in a hand-rolled ServiceCollection would test a fictional
//   container that silently drifts from production wiring -- exactly what
//   this gate exists to prevent.
//
// WHY NO NETWORK CALLS / NO HANGING BACKGROUND WORK:
//   Program.cs registers two IHostedService implementations
//   (StateReconcilerService via AddReconcilerModule, CrashRecoveryStartupService
//   directly) that scan Cosmos on host start. WebApplicationFactory's
//   TestServer DOES start the host (and therefore every IHostedService) the
//   moment `.Services` is first accessed. This test's WorkerTestFactory
//   strips every IHostedService registration in ConfigureServices BEFORE the
//   host builds, so no background work ever runs -- only constructor-graph
//   resolution is exercised (GetKeyedService<IProvisioningHandler> never
//   calls HandleAsync). Cosmos + Service Bus module fail-fast validators are
//   satisfied with syntactically-valid-but-unreachable endpoints (identical
//   values to L2WebApplicationFactory in Api/RunsEndpointsTests.cs) because
//   their *clients* are still constructed (just never invoked) as
//   collaborators inside several handlers' dependency graphs.
//
// ADR-038 alignment:
//   - KEEP category: unit test (tests/unit/ equivalent — in-process TestServer,
//     no external resource actually reached; no Mock<HttpMessageHandler>).
//   - Not a DI-registration-only ctor-null-check test — this asserts the
//     KEYED resolution surface's shape (which id maps to which concrete
//     type), a behavior no other test in the suite covers.
// -----------------------------------------------------------------------------

// extern alias required — see Sprk.Provisioning.ControlPlane.Tests.csproj
// comment on the aliased Worker ProjectReference: both .Api and .Worker's
// top-level-statement Program class land in the GLOBAL namespace, so an
// unaliased Worker reference would make every WebApplicationFactory<Program>
// usage project-wide ambiguous (CS0433), including Api/RunsEndpointsTests.cs.
extern alias WorkerHost;

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Registry;
using Xunit;
using WorkerProgram = WorkerHost::Program;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

/// <summary>
/// Build-time completeness gate over the keyed <see cref="IProvisioningHandler"/>
/// resolution surface: every <see cref="HandlerIds.Dispatchable"/> id MUST
/// resolve to a handler whose <see cref="IProvisioningHandler.HandlerId"/>
/// equals the key, and the three in-process H14 sub-steps (H14a/b/c) MUST
/// NOT be keyed-registered.
/// </summary>
public sealed class HandlerRegistrationCompletenessTests : IClassFixture<WorkerTestFactory>
{
    private readonly WorkerTestFactory _factory;

    public HandlerRegistrationCompletenessTests(WorkerTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Dispatchable_ContainsExactlyNineteenIds()
    {
        HandlerIds.Dispatchable.Should().HaveCount(19);
    }

    [Theory]
    [MemberData(nameof(DispatchableIds))]
    public void DispatchableId_ResolvesKeyedHandler_WithMatchingHandlerIdProperty(string handlerId)
    {
        using var scope = _factory.Services.CreateScope();

        var handler = scope.ServiceProvider.GetKeyedService<IProvisioningHandler>(handlerId);

        handler.Should().NotBeNull(
            $"'{handlerId}' is in HandlerIds.Dispatchable and MUST have a keyed " +
            "AddKeyedScoped<IProvisioningHandler> registration (task 102's dispatcher " +
            "would otherwise dead-letter every dispatch of this handler as NoHandler)");
        handler!.HandlerId.Should().Be(
            handlerId,
            "the keyed resolution and the handler's own HandlerId property must agree " +
            "(a mismatch means the wrong concrete type was factory-forwarded to this key)");
    }

    [Theory]
    [InlineData(HandlerIds.H14a)]
    [InlineData(HandlerIds.H14b)]
    [InlineData(HandlerIds.H14c)]
    public void H14SubStepId_IsNotKeyedRegistered(string subStepHandlerId)
    {
        using var scope = _factory.Services.CreateScope();

        var handler = scope.ServiceProvider.GetKeyedService<IProvisioningHandler>(subStepHandlerId);

        handler.Should().BeNull(
            $"'{subStepHandlerId}' is an H14 in-process sub-step (DS-2 §3.2) -- it is " +
            "orchestrated directly by H14IntegrationWiringHandler and MUST NEVER be " +
            "independently dispatchable via keyed DI");
    }

    public static TheoryData<string> DispatchableIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in HandlerIds.Dispatchable)
        {
            data.Add(id);
        }

        return data;
    }

    // -------------------------------------------------------------------------
    // Task 122 forcing-function — the exact category of regression this test
    // exists to catch: if a future PR flips the composition root back to
    // NullDataverseEnvironmentRegistryClient (or introduces a second,
    // divergent registration), H0.5's re-consent semantics (spec.md FR-02)
    // silently go inert again with a still-green build (the Null-Object
    // satisfies the interface; nothing else fails). Asserting the CONCRETE
    // resolved type (not just "non-null") is load-bearing — reuses task 103's
    // WorkerTestFactory pattern (the REAL Worker composition root, not a
    // duplicated hand-rolled container).
    // -------------------------------------------------------------------------
    [Fact]
    public void IDataverseEnvironmentRegistryClient_ResolvesRealClient_NotNullObject()
    {
        using var scope = _factory.Services.CreateScope();

        var registryClient = scope.ServiceProvider.GetRequiredService<IDataverseEnvironmentRegistryClient>();

        registryClient.Should().BeOfType<DataverseEnvironmentRegistryClient>(
            "task 122 DI-swaps H0.5's (and H13's read-path) registry-client dependency onto the real " +
            "Path X client (task 112); NullDataverseEnvironmentRegistryClient must never be the active " +
            "composition-root registration again — a regression here silently re-inerts FR-02 re-consent " +
            "semantics while every other test still passes (the Null-Object satisfies the interface).");
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> over the REAL
/// <see cref="Sprk.Provisioning.ControlPlane.Worker"/> composition root.
/// Satisfies Cosmos + Service Bus fail-fast validators with syntactically
/// valid (but unreachable) endpoints -- same values as Api's
/// L2WebApplicationFactory (Api/RunsEndpointsTests.cs) -- and strips every
/// <see cref="IHostedService"/> so accessing <see cref="WebApplicationFactory{TEntryPoint}.Services"/>
/// never starts the reconciler or crash-recovery background work (neither
/// is exercised by this test; both would otherwise attempt to scan Cosmos
/// the instant the test host starts).
/// </summary>
public sealed class WorkerTestFactory : WebApplicationFactory<WorkerProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Cosmos:AccountEndpoint", "https://l2-test.documents.azure.com:443/");
        builder.UseSetting("ServiceBus:FullyQualifiedNamespace", "l2-test.servicebus.windows.net");

        // Task 122 — DataverseEnvironmentRegistryModule.AddDataverseEnvironmentRegistry
        // now the active IDataverseEnvironmentRegistryClient registration (replacing
        // NullDataverseEnvironmentRegistryClient); its Options.Validate() fails fast
        // at boot on a missing AdminEnvironmentUrl (NFR-05), so the test host needs a
        // syntactically-valid-but-unreachable value — same convention as the Cosmos /
        // Service Bus settings above (the client itself is never invoked here, only
        // constructor-graph resolution is exercised).
        builder.UseSetting("DataverseEnvironmentRegistry:AdminEnvironmentUrl", "https://l2-test.crm.dynamics.com/");

        // Task 123 — ArmDeploymentRunner / ArmWhatIfDriftDetector's
        // BicepInfraDeployOptions.Validate() fails fast at boot on a missing
        // ProvisioningArtifactsContainerUri (NFR-05), same convention as the
        // Cosmos / ServiceBus / DataverseEnvironmentRegistry settings above —
        // syntactically-valid-but-unreachable value; the blob client itself
        // is never invoked here, only constructor-graph resolution.
        builder.UseSetting("BicepInfraDeployOptions:ProvisioningArtifactsContainerUri", "https://l2-test.blob.core.windows.net/provisioning-artifacts");

        // Task 132 — ArtifactManifestVerifier / BlobArtifactDownloader's
        // BffDeployOptions.Validate() fails fast at boot on a missing
        // ProvisioningArtifactsContainerUri (same convention as
        // BicepInfraDeployOptions above — same underlying `provisioning-artifacts`
        // container, different options binding per this project's
        // self-contained-per-handler convention); syntactically-valid-but-
        // unreachable value, the blob client itself is never invoked here.
        builder.UseSetting("BffDeployOptions:ProvisioningArtifactsContainerUri", "https://l2-test.blob.core.windows.net/provisioning-artifacts");

        // Task 142 — EnvVarValuesOptions.Validate() (H7) fails fast at boot on
        // a missing ClientSecret (NFR-05), same convention as the other
        // AddOptions<T>().ValidateOnStart() registrations above — syntactically
        // valid placeholder value; H7's writer is never invoked here, only
        // constructor-graph resolution.
        builder.UseSetting("EnvVarValues:ClientSecret", "l2-test-envvarvalues-client-secret-placeholder");

        // Testing environment — TelemetryModule's AzureMonitorGuard skips
        // exporter wiring silently on non-Development/Production envs
        // (parity with Api/RunsEndpointsTests.cs L2WebApplicationFactory).
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove every IHostedService registration (StateReconcilerService,
            // CrashRecoveryStartupService) so the test host never starts
            // background work that would scan Cosmos. This test only proves
            // DI-graph SHAPE (keyed resolution), never handler execution.
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
