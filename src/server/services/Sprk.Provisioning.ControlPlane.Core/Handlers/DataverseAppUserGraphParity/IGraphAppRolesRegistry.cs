// -----------------------------------------------------------------------------
// IGraphAppRolesRegistry.cs
//
// L2-LOCAL COMPILED MIRROR of Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles
// (r3 task 062). L2 is a PEER service to Sprk.Bff.Api and MUST NOT reference
// the BFF assembly (ADR-010 DI minimalism + project MUST rule — no
// Sprk.Bff.Api project/assembly reference from L2). Two independent copies of
// the catalog (15 roles as of task 144; 14 as of task 005) are the
// INTENTIONAL cost of that isolation — the same rationale IProvisioningHandler's
// file header documents for the handler contract shape itself.
//
// DRIFT GUARD: task 067 ("Nightly Graph app-role parity ArchTest", depends on
// this task 053) is the mechanism that keeps this mirror in sync with the BFF
// source of truth. Until task 067 ships, a manual reconciliation is required
// whenever GraphAppRoles.cs changes (add a role, populate/replace a GUID).
//
// SPEC / DESIGN references:
//   - src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs
//     (canonical source; 14 AppRoleId GUIDs populated 2026-08-17 per r1
//     task 005; a 15th, User.Invite.All, added 2026-08-20 by task 144 for
//     H11's B2BGuest preset).
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
/// not consume (H10 always operates on the FULL catalog regardless of
/// per-module conditionality — spec.md FR-33 says "ALL 15" (as of task 144),
/// not a filtered subset).
/// </summary>
public sealed record GraphAppRoleEntry(string Value, string? AppRoleId);

/// <summary>
/// Reads the L2-local compiled mirror of the Graph app-role catalog (15
/// roles as of task 144).
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

    /// <summary>The full expected role catalog (15 as of task 144).</summary>
    IReadOnlyList<GraphAppRoleEntry> GetAll();
}
