// -----------------------------------------------------------------------------
// AppConfigSeedModule.cs
//
// L2 CONTROL-PLANE DI composition for the H12b app-config seed handler
// (task 071 — wave Cp DAG-parallel with H12a).
//
// SCOPE:
//   - Bind AppConfigSeed:{PwshExecutable, ScriptsDirectory, ManifestPath,
//     SeederTimeout, DataGridScriptFileName, WorkspaceLayoutScriptFileName}
//     options.
//   - Register the four IAppConfigSeeder instances (two shell-out via
//     PowerShellAppConfigSeeder + two Deferred via DeferredAppConfigSeeder,
//     per task 004 §4b decision matrix).
//   - Register H12bAppConfigSeedHandler as Scoped (parity with H0/H0.5/H1/H2a
//     handler scoping).
//
// UNCONDITIONAL REGISTRATION (ADR-032):
//   Every registration below is UNCONDITIONAL — no feature-gate branch. If
//   a future wave feature-gates a SPECIFIC seeder scope, apply the
//   Null-Object kill-switch pattern (P1/P2/P3) to THAT scope's seeder, never
//   to the module itself. The DeferredAppConfigSeeder is NOT a feature-gate
//   — it is the intentional interim behavior for scopes without a repo
//   source (per task 004 §4b decision matrix).
//
// SPEC / ADR references:
//   - projects/customer-provisioning-orchestration-r1/spec.md §5.2 + D3/D8/D12:
//     Provisioning handlers register in L2 control-plane service, NOT the BFF.
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-16 (H12b
//     acceptance).
//   - ADR-010: Feature-module extension method keeps Program.cs additions to
//     a single new line (NFR-07 god-class ratchet). One
//     AddH12bAppConfigSeedHandler() call replaces four per-seeder + one
//     per-options + one per-handler registrations.
//   - ADR-032: Unconditional registration; feature-gates apply per-service
//     via Null-Object, never at the module boundary.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   H12b lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; it consumes NO
//   AI-internal types (ADR-013 forcing-function rule — no IActionResolver,
//   IActionRunner, IOpenAiClient, IPlaybookService injection). It uses
//   IProvisioningRunRepository (task 037) + IHandlerEnqueuer (task 038) +
//   the local IAppConfigSeeder seam; no BFF-facade dependencies.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// DI registration for the H12b app-config seed handler + its four
/// per-scope <see cref="IAppConfigSeeder"/> dependencies. Composed behind a
/// single <see cref="AddH12bAppConfigSeedHandler"/> extension method to
/// minimize Program.cs edit surface (one new line) + avoid merge-conflict
/// pressure with sibling H12a task 070 (both handlers register in the same
/// Program.cs during wave Cp parallel execution).
/// </summary>
public static class AppConfigSeedModule
{
    /// <summary>Configuration section for app-config seed options.</summary>
    public const string ConfigSection = "AppConfigSeed";

    /// <summary>
    /// Registers <see cref="H12bAppConfigSeedHandler"/> + its four
    /// <see cref="IAppConfigSeeder"/> dependencies with the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration (reads the <c>AppConfigSeed</c> section).</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddH12bAppConfigSeedHandler(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind AppConfigSeed options. Defaults inside AppConfigSeedOptions
        // cover the operator running from a workstation with pwsh on PATH
        // + scripts/ under AppContext.BaseDirectory. Production deployments
        // override via App Service settings.
        services.Configure<AppConfigSeedOptions>(configuration.GetSection(ConfigSection));

        // Four seeder registrations — kept explicit (rather than reflection
        // / attribute-scanning) so the compile-time diff is visible: a new
        // scope is a new line here + a new AppConfigSeedScopes const.
        //
        // Order below matches the canonical dependency-free sequence documented
        // in task 004 §4b. Registration order is preserved through
        // IEnumerable<IAppConfigSeeder> resolution; the handler's aggregation
        // logic does not depend on order but the operator-visible outcome
        // ordering follows this list.

        // (1) DataGrid — shell-out to scripts/seed-reconciliation-gridconfig.ps1
        services.AddScoped<IAppConfigSeeder>(sp => new PowerShellAppConfigSeeder(
            scopeName: AppConfigSeedScopes.DataGrid,
            scriptFileName: sp.GetRequiredService<IOptions<AppConfigSeedOptions>>().Value.DataGridScriptFileName,
            options: sp.GetRequiredService<IOptions<AppConfigSeedOptions>>(),
            logger: sp.GetRequiredService<ILogger<PowerShellAppConfigSeeder>>()));

        // (2) System workspace layouts — shell-out to scripts/Deploy-SystemWorkspaceLayouts.ps1
        services.AddScoped<IAppConfigSeeder>(sp => new PowerShellAppConfigSeeder(
            scopeName: AppConfigSeedScopes.WorkspaceLayout,
            scriptFileName: sp.GetRequiredService<IOptions<AppConfigSeedOptions>>().Value.WorkspaceLayoutScriptFileName,
            options: sp.GetRequiredService<IOptions<AppConfigSeedOptions>>(),
            logger: sp.GetRequiredService<ILogger<PowerShellAppConfigSeeder>>()));

        // (3) Field-mapping — Deferred (no repo source yet; task 004 §4b row 11 + §5b N3)
        services.AddScoped<IAppConfigSeeder>(sp => new DeferredAppConfigSeeder(
            scopeName: AppConfigSeedScopes.FieldMapping,
            driftMatrixReference: "§4b row 11",
            followOnDeltaId: "N3",
            logger: sp.GetRequiredService<ILogger<DeferredAppConfigSeeder>>()));

        // (4) Chart-def — Deferred (no consolidated mirror yet; task 004 §4b row 13 + §5b N5)
        services.AddScoped<IAppConfigSeeder>(sp => new DeferredAppConfigSeeder(
            scopeName: AppConfigSeedScopes.ChartDefinition,
            driftMatrixReference: "§4b row 13",
            followOnDeltaId: "N5",
            logger: sp.GetRequiredService<ILogger<DeferredAppConfigSeeder>>()));

        // Handler — Scoped per IProvisioningHandler contract + parity with
        // IHandlerEnqueuer's Scoped registration + parity with H0/H0.5/H1/H2a
        // registrations in Program.cs.
        services.AddScoped<H12bAppConfigSeedHandler>();

        return services;
    }
}
