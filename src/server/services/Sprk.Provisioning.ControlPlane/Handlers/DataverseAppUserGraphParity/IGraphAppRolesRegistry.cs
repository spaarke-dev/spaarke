// -----------------------------------------------------------------------------
// IGraphAppRolesRegistry.cs
//
// L2-LOCAL COMPILED MIRROR of Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles
// (r3 task 062). L2 is a PEER service to Sprk.Bff.Api and MUST NOT reference
// the BFF assembly (ADR-010 DI minimalism + project MUST rule — no
// Sprk.Bff.Api project/assembly reference from L2). Two independent copies of
// the 14-role catalog are the INTENTIONAL cost of that isolation — the same
// rationale IProvisioningHandler's file header documents for the handler
// contract shape itself.
//
// DRIFT GUARD: task 067 ("Nightly Graph app-role parity ArchTest", depends on
// this task 053) is the mechanism that keeps this mirror in sync with the BFF
// source of truth. Until task 067 ships, a manual reconciliation is required
// whenever GraphAppRoles.cs changes (add a role, populate/replace a GUID).
//
// SPEC / DESIGN references:
//   - src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs
//     (canonical source; ALL 14 AppRoleId GUIDs populated 2026-08-17 per r1
//     task 005).
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-13 + FR-33.
//   - projects/customer-provisioning-orchestration-r1/design.md §9.2 +
//     §7.2 row 9 ("Graph app-role grants ... Nightly parity ArchTest").
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>
/// One Microsoft Graph application (app-only) role entry — the L2-local
/// mirror shape of <c>Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.GraphAppRole</c>.
/// Only the fields H10 actually needs (Value for diagnostics, AppRoleId for
/// the escalation-gate check + grant/verify calls) are mirrored — DisplayName,
/// OwningModule, WhyRequired, ModuleConditional are BFF-side concerns H10 does
/// not consume (H10 always operates on the FULL 14-role catalog regardless of
/// per-module conditionality — spec.md FR-33 says "ALL 14", not a filtered
/// subset).
/// </summary>
public sealed record GraphAppRoleEntry(string Value, string? AppRoleId);

/// <summary>
/// Reads the L2-local compiled mirror of the 14-role Graph app-role catalog.
/// Abstracted behind an interface (rather than a bare static class reference)
/// so unit tests can substitute a fixture catalog — e.g. one entry with a
/// null AppRoleId to exercise the H10 escalation gate — without depending on
/// the real mirror's current (fully-populated) state.
/// </summary>
public interface IGraphAppRolesRegistry
{
    /// <summary>
    /// Well-known Microsoft Graph resource service principal appId (constant
    /// across every tenant). Mirrors
    /// <c>Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.GraphResourceAppId</c>.
    /// </summary>
    string GraphResourceAppId { get; }

    /// <summary>The full expected 14-role catalog.</summary>
    IReadOnlyList<GraphAppRoleEntry> GetAll();
}
