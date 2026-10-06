// -----------------------------------------------------------------------------
// ApiHostShadowWorkerGuardTests.cs
//
// L2 CONTROL-PLANE .Api-vs-.Worker split invariant guard (task 204d, punch
// list row B11 -- DS-3 staging-slot shadow-worker defect follow-on).
//
// PURPOSE:
//   Makes the DS-3 §1.3 staging-slot shadow-worker defect UNREACHABLE by
//   build-time assertion. The defect required two conditions holding
//   simultaneously on the same host: (a) a staging slot, and (b) background
//   services (reconciler + crash-recovery + dispatcher) that touch production
//   Cosmos/Service Bus. Wave G-1 tasks 100/101/102 eliminated the second
//   condition on any staging-slot-bearing host by splitting the pre-Wave-G-1
//   single Sprk.Provisioning.ControlPlane project into three:
//     - Sprk.Provisioning.ControlPlane.Api    -- REST intake, has staging slot,
//                                                NO background handler execution.
//     - Sprk.Provisioning.ControlPlane.Worker -- background execution
//                                                (dispatcher + reconciler +
//                                                crash-recovery + 21-handler
//                                                fleet), SLOTLESS by Bicep
//                                                design (controlplane-worker-
//                                                app-service.bicep declares
//                                                Microsoft.Web/sites with NO
//                                                child Microsoft.Web/sites/slots
//                                                resource).
//     - Sprk.Provisioning.ControlPlane.Core   -- shared types + module DI
//                                                extension methods.
//   This test class asserts that shape does not regress: no future PR can
//   accidentally re-introduce background-execution registrations into the
//   .Api composition root, because doing so would fail these tests.
//
// STRATEGY -- the REAL DI container, not a duplicate:
//   Uses WebApplicationFactory<Program> (bare .Api Program, parity with the
//   L2WebApplicationFactory pattern in Api/RunsEndpointsTests.cs) so the exact
//   production composition (Sprk.Provisioning.ControlPlane.Api/Program.cs) is
//   exercised. Duplicating Program.cs's registration calls in a hand-rolled
//   ServiceCollection would test a fictional container that silently drifts
//   from production wiring -- the exact defect class task 103's sibling
//   HandlerRegistrationCompletenessTests already established the anti-pattern
//   guard against.
//
//   Deliberately does NOT strip IHostedService registrations (contrast with
//   sibling WorkerTestFactory in HandlerRegistrationCompletenessTests.cs) --
//   the whole point IS to observe them: if any IHostedService IS registered
//   on the .Api graph, that IS the regression this test exists to catch.
//   Cosmos + Service Bus module fail-fast validators are satisfied with
//   syntactically-valid-but-unreachable endpoints (identical values to
//   L2WebApplicationFactory), and the Testing environment tag short-circuits
//   AzureMonitorGuard so no telemetry-exporter background loop starts either.
//
// WHY NOT AN ARCHUNIT-STYLE STATIC ASSEMBLY SCAN:
//   The regression class this guards against is "some future contributor
//   calls services.AddHostedService<T>() or services.AddKeyedScoped
//   <IProvisioningHandler, T>() from Program.cs (or a module Program.cs
//   registers)". That is a RUNTIME DI-shape property, not a static type-graph
//   property (the .Core assembly LEGITIMATELY contains BackgroundService
//   subclasses -- StateReconcilerService, ProvisioningHandlerDispatcher lives
//   in .Worker, etc. -- because the .Worker composition root registers them;
//   a static "reject any BackgroundService in transitively-referenced
//   assemblies" test would be a false positive). Observing the ACTUAL built
//   DI graph is the only reliable check.
//
// ADR-038 alignment:
//   - KEEP category: unit test (tests/unit/ equivalent -- in-process
//     TestServer, no external resource actually reached; no
//     Mock<HttpMessageHandler>).
//   - Not a DI-registration-only ctor-null-check test -- this asserts a
//     COMPOSITION-BOUNDARY invariant (which IHostedService/handler
//     registrations belong to which host), a behavior no other test covers.
//   - Category: forcing-function guard (per §14A upgrade model / this
//     project's CLAUDE.md TEST-MODIFYING override -- Full rigor).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sprk.Provisioning.ControlPlane.Handlers;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

/// <summary>
/// Build-time invariant guard over the <see cref="Program"/> (Sprk.Provisioning.
/// ControlPlane.Api) composition root: the .Api host MUST NOT register any
/// <see cref="IHostedService"/> and MUST NOT keyed-register any
/// <see cref="IProvisioningHandler"/>. Together these two assertions close
/// the DS-3 §1.3 staging-slot shadow-worker defect at build time: even if a
/// future Bicep change accidentally re-exposed a staging slot on the .Worker,
/// AND if a future config change accidentally allowed two hosts to share the
/// same App Service Plan settings, the .Api site's process itself could still
/// not shadow-drain the fleet queue or shadow-scan Cosmos -- because it has
/// no dispatcher / reconciler / crash-recovery in its DI graph.
/// </summary>
public sealed class ApiHostShadowWorkerGuardTests : IClassFixture<ApiHostShadowWorkerGuardTests.ApiHostTestFactory>
{
    private readonly ApiHostTestFactory _factory;

    public ApiHostShadowWorkerGuardTests(ApiHostTestFactory factory)
    {
        _factory = factory;
    }

    // -------------------------------------------------------------------------
    // Invariant 1: the .Api composition root registers ZERO IHostedService.
    //
    // Regressions this catches:
    //   - Someone re-adds services.AddHostedService<StateReconcilerService>()
    //     to .Api/Program.cs (or wires AddReconcilerModule into it -- that
    //     extension internally calls AddHostedService per ReconcilerModule.cs).
    //   - Someone adds services.AddHostedService<CrashRecoveryStartupService>()
    //     to .Api/Program.cs (currently only .Worker/Program.cs line 963 does).
    //   - Someone adds services.AddHostedService<ProvisioningHandlerDispatcher>()
    //     to .Api/Program.cs (currently only .Worker/Program.cs line 1013 does).
    //   - Someone imports AddDispatchModule into .Api (which internally
    //     registers the dispatcher's collaborator seams -- the AddHostedService
    //     line is separate in Worker/Program.cs but any future refactor that
    //     folds it inside would leak into .Api if .Api ever adopted the module).
    //   - Any other future BackgroundService (say a new pollers-batch host) is
    //     accidentally added to .Api rather than .Worker.
    // -------------------------------------------------------------------------

    [Fact]
    public void ApiComposition_HasZeroHostedServices()
    {
        // Resolving IEnumerable<IHostedService> from the built ServiceProvider
        // materializes every registered IHostedService entry. Any registration
        // (from AddHostedService<T>, AddSingleton<IHostedService, T>, etc.)
        // shows up here regardless of how it was added.
        var hostedServices = _factory.Services.GetServices<IHostedService>().ToList();

        // NOTE ON THE EXPECTED SHAPE: Microsoft.AspNetCore.Mvc.Testing's
        // WebApplicationFactory<T> may register its own framework-level
        // IHostedService instances (e.g. GenericWebHostService) to wire up the
        // TestServer. We assert on the PROJECT-OWNED types only: anything
        // whose type lives under the Sprk.Provisioning.ControlPlane.* namespace
        // tree (both .Core-defined and .Worker-defined) is the defect surface;
        // framework hosted services are permitted noise and irrelevant to
        // shadow-worker risk.
        var projectOwnedHostedServices = hostedServices
            .Where(hs => hs.GetType().FullName?.StartsWith(
                "Sprk.Provisioning.ControlPlane",
                StringComparison.Ordinal) == true)
            .Select(hs => hs.GetType().FullName)
            .ToList();

        projectOwnedHostedServices.Should().BeEmpty(
            "the .Api host is the REST intake surface (task 100 split, DS-3 §3 " +
            "Option 2). Every project-owned IHostedService MUST live in the .Worker " +
            "composition root (Sprk.Provisioning.ControlPlane.Worker/Program.cs). " +
            "Registering one here re-opens the DS-3 §1.3 staging-slot shadow-worker " +
            "defect: the .Api staging slot would then compete for the same Service " +
            "Bus session locks and issue duplicate Cosmos writes against the " +
            "production runs container. If you need to add background work, add it " +
            "to Sprk.Provisioning.ControlPlane.Worker/Program.cs instead. " +
            $"Detected: [{string.Join(", ", projectOwnedHostedServices)}]");
    }

    // -------------------------------------------------------------------------
    // Invariant 2: the .Api composition root keyed-registers ZERO
    // IProvisioningHandler. Defense-in-depth against Invariant 1 -- even if a
    // future IHostedService dispatcher were somehow injected into .Api, it
    // would still resolve zero handlers.
    //
    // Regressions this catches:
    //   - Someone imports AddProvisioningHandlers(...) into .Api/Program.cs
    //     (currently only .Worker/Program.cs line 105 does).
    //   - Someone adds AddH12bAppConfigSeedHandler / AddH12cRuntimeReferences
    //     Handler / AddH13E2EAcceptanceGateHandler / AddH14IntegrationWiring
    //     Handler / AddDispatchModule to .Api/Program.cs (each internally
    //     performs keyed IProvisioningHandler registrations).
    //   - Any future H* handler module is accidentally wired into .Api rather
    //     than .Worker.
    // -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(DispatchableIds))]
    public void ApiComposition_HasNoKeyedProvisioningHandler(string handlerId)
    {
        using var scope = _factory.Services.CreateScope();

        var handler = scope.ServiceProvider.GetKeyedService<IProvisioningHandler>(handlerId);

        handler.Should().BeNull(
            $"'{handlerId}' MUST be keyed-registered ONLY in the .Worker composition " +
            $"root (Sprk.Provisioning.ControlPlane.Worker/Program.cs, per task 102's " +
            $"ProvisioningHandlerDispatcher). Registering it here would allow the .Api " +
            $"host to dispatch handlers directly, bypassing session-serialization + " +
            $"§4C rollback authority + the whole DS-2 §1.5 divergence rationale.");
    }

    public static IEnumerable<object[]> DispatchableIds() =>
        HandlerIds.Dispatchable.Select(id => new object[] { id });

    // -------------------------------------------------------------------------
    // Test-host factory. Bare .Api Program (unaliased -- .Worker's Program is
    // aliased "WorkerHost" in the csproj so `Program` here binds to .Api's
    // Sprk.Provisioning.ControlPlane.Api/Program.cs unambiguously; parity with
    // L2WebApplicationFactory in Api/RunsEndpointsTests.cs).
    //
    // DELIBERATELY does NOT strip IHostedService registrations (contrast with
    // sibling WorkerTestFactory in HandlerRegistrationCompletenessTests.cs) --
    // observing them is the whole test.
    // -------------------------------------------------------------------------
    public sealed class ApiHostTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Satisfy AddCosmosModule + AddServiceBusModule fail-fast validators
            // with syntactically-valid-but-unreachable endpoints -- identical to
            // L2WebApplicationFactory (Api/RunsEndpointsTests.cs). No client is
            // ever invoked; only the DI graph is inspected.
            builder.UseSetting("Cosmos:AccountEndpoint", "https://l2-test.documents.azure.com:443/");
            builder.UseSetting("ServiceBus:FullyQualifiedNamespace", "l2-test.servicebus.windows.net");

            // REG-02 (Wave 2 pre-dispatch remediation, 2026-08-27) — Path X
            // migration flipped CustomerRunGuard:Enabled default to true; the
            // guard's PostConfigure now fails-fast at boot without a URL. Test
            // hosts opt out via the ADR-032 kill-switch (Enabled=false); the
            // DI graph is inspected without touching a Dataverse env.
            builder.UseSetting("CustomerRunGuard:Enabled", "false");

            // REG-07 (Wave 2 pre-dispatch remediation, 2026-08-27) — the Api
            // Program.cs now registers DataverseEnvironmentRegistryClient
            // (Path X); its options.Validate() requires AdminEnvironmentUrl.
            // Provide a stub URL so boot succeeds — no HTTP is invoked in
            // these DI-inspection tests.
            builder.UseSetting("DataverseEnvironmentRegistry:AdminEnvironmentUrl", "https://l2-test.crm.dynamics.com");

            // Testing environment -- TelemetryModule's AzureMonitorGuard skips
            // exporter wiring silently on non-Development/Production envs.
            builder.UseEnvironment("Testing");
        }
    }
}
