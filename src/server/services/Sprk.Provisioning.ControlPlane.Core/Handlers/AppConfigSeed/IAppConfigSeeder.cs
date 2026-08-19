// -----------------------------------------------------------------------------
// IAppConfigSeeder.cs
//
// Abstraction over one app-config seed unit invoked by
// H12bAppConfigSeedHandler (task 071, wave Cp — DAG-parallel with H12a).
//
// PURPOSE:
//   Each app-config scope (DataGrid, field-mapping, workspace-layout,
//   chart-def) is one IAppConfigSeeder implementation. The handler
//   iterates the registered seeders in a fixed order + rolls up outcomes
//   into a single HandlerResult. Splitting per-scope isolates PS
//   shell-out logic behind a stable seam so:
//     - Unit tests swap in-memory fakes without touching pwsh.
//     - Future consolidation (moving each scope's records into
//       task-069's declarative manifest — see task 004 §4b migration
//       deltas N1/N2/N3/N5) is a per-seeder swap, not a handler
//       rewrite.
//
// SCOPE INVENTORY (task 004 §4b decision matrix):
//   scope=data-grid            → PowerShellAppConfigSeeder wrapping
//                                scripts/seed-reconciliation-gridconfig.ps1
//                                (task 004 §4b row 10 — interim: existing
//                                script; N1/N2 deltas defer generalization)
//   scope=workspace-layout     → PowerShellAppConfigSeeder wrapping
//                                scripts/Deploy-SystemWorkspaceLayouts.ps1
//                                (task 004 §4b row 12 — verbatim reuse;
//                                schema-additive + data-idempotent)
//   scope=field-mapping        → DeferredAppConfigSeeder (task 004 §4b row 11
//                                — NO repo source; H12b MUST author mirror
//                                files by exporting live spaarkedev1 first,
//                                which is a Wave-C5 follow-on. Interim: Deferred.)
//   scope=chart-definition     → DeferredAppConfigSeeder (task 004 §4b row 13
//                                — per-family scripts exist but consolidated
//                                mirror does not; N5 delta pending. Interim: Deferred.)
//
// FUTURE (Wave C5+):
//   When task 069's manifest gains `scope: app-config` artifact entries for
//   these four scopes, the handler MAY migrate to invoking
//   scripts/seed-data/Invoke-SeedManifest.ps1 with a scope filter (mirrors
//   task 070's H12a shape). Today the manifest carries only ai-seed scope —
//   the in-code IAppConfigSeeder registry IS the app-config seed plan per
//   task 004 §4b interim design.
//
// DESIGN REFS:
//   - .claude/adr/ADR-004-job-contract.md — idempotent + at-least-once safe
//   - .claude/adr/ADR-010-di-minimalism.md — seam earns keep: ≥2 impls
//     (PowerShellAppConfigSeeder for shell-out scopes +
//      DeferredAppConfigSeeder for interim scopes; more impls arrive as
//      mirror-authoring completes per task 004 §4b N-deltas)
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-16
//     (H12b app-config seed acceptance)
//   - projects/customer-provisioning-orchestration-r1/notes/ai-seed-drift-resolution-2026-08.md
//     §4b (app-config source-of-truth decision matrix) + §5b (N-deltas)
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// Contract for one app-config scope seed unit (DataGrid, field-mapping,
/// workspace-layout, chart-def). One instance per scope; handler iterates
/// the registered set in a fixed order.
/// </summary>
public interface IAppConfigSeeder
{
    /// <summary>
    /// Stable identifier for the scope this seeder owns (e.g.
    /// <c>data-grid</c>, <c>workspace-layout</c>). Matches the entry in
    /// <see cref="AppConfigSeedScopes"/> so grep across the codebase finds
    /// every reader/writer of the same scope name.
    /// </summary>
    string ScopeName { get; }

    /// <summary>
    /// Executes the seed unit against the target Dataverse environment.
    /// Implementations MUST return a structured
    /// <see cref="AppConfigSeederResult"/> — domain failures (script exit
    /// non-zero, expected records missing) yield <see cref="AppConfigSeederStatus.Failed"/>;
    /// scopes without a shipping repo source yield <see cref="AppConfigSeederStatus.Deferred"/>;
    /// only unexpected infrastructure faults (pwsh missing, cancellation)
    /// SHOULD throw.
    /// </summary>
    /// <param name="input">Per-invocation seed context (customer id, tenant id, target Dataverse URL).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AppConfigSeederResult> SeedAsync(
        AppConfigSeedInput input,
        CancellationToken cancellationToken);
}

/// <summary>
/// Non-secret invocation context passed to every <see cref="IAppConfigSeeder"/>.
/// Deliberately narrow — the seeders talk to Dataverse via existing PS scripts
/// which do their own <c>az account get-access-token</c> under the operator's
/// (or L2 App Service UAMI's) auth chain (ADR-028 MI-outbound rule).
/// </summary>
/// <param name="CustomerId">Cosmos partition key (§4D I3) — copied through to seeder script diagnostics.</param>
/// <param name="TenantId">Entra tenant id (§4D I1 — never a default fallback; handler enforces non-empty).</param>
/// <param name="TargetDataverseUrl">Target customer Dataverse env URL (e.g. <c>https://acme.crm.dynamics.com</c>).</param>
public sealed record AppConfigSeedInput(
    string CustomerId,
    string TenantId,
    string TargetDataverseUrl);

/// <summary>
/// Well-known scope names used by <see cref="IAppConfigSeeder.ScopeName"/>.
/// Constants (not enum) so grep across the codebase finds every string
/// literal + so a future manifest-declared scope (from task 069's manifest
/// when app-config entries land) can extend the set without an enum change.
/// </summary>
public static class AppConfigSeedScopes
{
    /// <summary>
    /// DataGrid configurations (<c>sprk_gridconfiguration</c>). Wraps
    /// <c>scripts/seed-reconciliation-gridconfig.ps1</c>. See task 004
    /// §4b row 10 for the drift-resolution rationale.
    /// </summary>
    public const string DataGrid = "data-grid";

    /// <summary>
    /// System workspace layouts (<c>sprk_workspacelayout</c>, <c>sprk_issystem=true</c>).
    /// Wraps <c>scripts/Deploy-SystemWorkspaceLayouts.ps1</c>. See task 004
    /// §4b row 12 for the verbatim-reuse rationale.
    /// </summary>
    public const string WorkspaceLayout = "workspace-layout";

    /// <summary>
    /// Field-mapping profiles + rules (<c>sprk_fieldmappingprofile</c> +
    /// <c>sprk_fieldmappingrule</c>). Interim: <see cref="DeferredAppConfigSeeder"/>.
    /// Mirror-authoring path documented in task 004 §4b row 11 + §5b N3.
    /// </summary>
    public const string FieldMapping = "field-mapping";

    /// <summary>
    /// Chart definitions (<c>sprk_chartdefinition</c>). Interim:
    /// <see cref="DeferredAppConfigSeeder"/>. Consolidated-mirror authoring
    /// path documented in task 004 §4b row 13 + §5b N5.
    /// </summary>
    public const string ChartDefinition = "chart-definition";
}

/// <summary>
/// Outcome of a single <see cref="IAppConfigSeeder.SeedAsync"/> invocation.
/// Mirrors the shape of <see cref="SubscriptionReadiness.SubscriptionReadinessCheckResult"/>
/// so a future migration to a unified seeder contract is trivial.
/// </summary>
/// <param name="Status">
/// <see cref="AppConfigSeederStatus.Ok"/> when the seed succeeded (or was an
/// idempotent no-op inside the underlying script); <see cref="AppConfigSeederStatus.Deferred"/>
/// when the scope has no shipping repo source yet + is intentionally
/// skipped (surfaces to operator log without failing the run);
/// <see cref="AppConfigSeederStatus.Failed"/> on a domain failure the
/// operator must resolve.
/// </param>
/// <param name="Diagnostic">
/// Human-readable diagnostic. On <see cref="AppConfigSeederStatus.Failed"/>
/// MUST cite BOTH the observed state (script exit code, stderr tail) AND
/// the remediation path so the operator can resolve without further probing.
/// On <see cref="AppConfigSeederStatus.Deferred"/> MUST cite the drift
/// resolution note reference so the operator understands this is intentional.
/// </param>
/// <param name="Evidence">
/// Optional structured evidence (stdout snippet, per-row summary counts).
/// Written to <see cref="Models.GateEntry.Evidence"/> so the operator sees
/// raw diagnostic without pulling logs. May be <c>null</c>.
/// </param>
public sealed record AppConfigSeederResult(
    AppConfigSeederStatus Status,
    string Diagnostic,
    JsonElement? Evidence)
{
    /// <summary>Convenience factory for the happy-path outcome.</summary>
    public static AppConfigSeederResult Ok(string diagnostic, JsonElement? evidence = null)
        => new(AppConfigSeederStatus.Ok, diagnostic, evidence);

    /// <summary>Convenience factory for the interim-Deferred outcome (no repo source yet).</summary>
    public static AppConfigSeederResult Deferred(string diagnostic)
        => new(AppConfigSeederStatus.Deferred, diagnostic, null);

    /// <summary>Convenience factory for a domain failure (script exit non-zero, records missing).</summary>
    public static AppConfigSeederResult Failed(string diagnostic, JsonElement? evidence = null)
        => new(AppConfigSeederStatus.Failed, diagnostic, evidence);
}

/// <summary>
/// Outcome status of a single <see cref="IAppConfigSeeder.SeedAsync"/>
/// invocation.
/// </summary>
public enum AppConfigSeederStatus
{
    /// <summary>Seed succeeded (or was an idempotent no-op inside the underlying script).</summary>
    Ok = 1,

    /// <summary>
    /// Scope has no shipping repo source yet + is intentionally skipped.
    /// Handler rolls Deferred results into a Success outcome with a
    /// diagnostic listing the deferred scopes; does NOT fail the run.
    /// </summary>
    Deferred = 2,

    /// <summary>
    /// Domain failure the operator must resolve (script exit non-zero,
    /// expected records missing, connectivity fault). Handler classifies
    /// as <see cref="FailureClass.Resumable"/> — every wrapped script is
    /// upsert-safe (existence-check-then-insert or PATCH) so re-invocation
    /// after operator remediation is safe.
    /// </summary>
    Failed = 3,
}
