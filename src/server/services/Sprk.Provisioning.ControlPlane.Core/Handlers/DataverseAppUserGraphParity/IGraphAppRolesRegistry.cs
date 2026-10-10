// -----------------------------------------------------------------------------
// IGraphAppRolesRegistry.cs
//
// L2-LOCAL COMPILED MIRROR of Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles
// (r3 task 062). L2 is a PEER service to Sprk.Bff.Api and MUST NOT reference
// the BFF assembly (ADR-010 DI minimalism + project MUST rule — no
// Sprk.Bff.Api project/assembly reference from L2). Two independent copies of
// the catalog (4 roles since task 261; 15 before) are the
// INTENTIONAL cost of that isolation — the same rationale IProvisioningHandler's
// file header documents for the handler contract shape itself.
//
// DRIFT GUARD: tests/Spaarke.ArchTests/TenantIsolation/StampGraphAppRoleEvidenceTests.cs
// (task 261, runs in CI) fails when this mirror, GraphAppRoles.cs and the task
// 261 evidence note disagree; task 067's nightly test also compares the two.
//
// SPEC / DESIGN references:
//   - src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs
//     (canonical source; the stamp identity's evidence-backed set since task
//     261 — projects/customer-provisioning-orchestration-r1/notes/t261-stamp-graph-least-privilege.md).
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
/// per-module conditionality — spec.md FR-33 says "ALL", not a filtered subset).
/// </summary>
public sealed record GraphAppRoleEntry(string Value, string? AppRoleId);

/// <summary>
/// Reads the L2-local compiled mirror of the Graph app-role catalog (the stamp
/// identity's set — 4 roles since task 261).
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

    /// <summary>
    /// The full role catalog — byte-for-byte the BFF's <c>GraphAppRoles.All</c> (the task 261 ArchTest and the
    /// nightly drift test compare the two). It lists what the stamp identity NEEDS; how each role is granted is
    /// split below.
    /// </summary>
    IReadOnlyList<GraphAppRoleEntry> GetAll();

    /// <summary>
    /// Mailbox roles granted through Exchange "RBAC for Applications", scoped to the customer's
    /// mail-enabled security group (H14a, task 251 / owner D26) — NOT through Entra. Exchange role
    /// assignments add to Entra app permissions, so an Entra grant of any of these would give the
    /// stamp every mailbox in the tenant and make the scope meaningless; H10 never grants them and
    /// H13 T3 fails if one is present. This is a classifier: <c>MailboxSettings.Read</c> left the catalog with
    /// task 261 (no BFF code reads mailbox settings) but stays here, so that if it is ever needed again it is
    /// granted through Exchange, never Entra.
    /// </summary>
    static readonly IReadOnlySet<string> ExchangeScopedValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "Mail.Read", "Mail.ReadWrite", "Mail.Send", "MailboxSettings.Read",
    };

    /// <summary>The Exchange application role that carries a Graph mailbox permission (e.g. <c>Application Mail.Send</c>).</summary>
    static string ToExchangeApplicationRole(string graphValue) => "Application " + graphValue;

    /// <summary>
    /// The roles H10 grants on the stamp UAMI through Entra — what H13 T3 expects EXACTLY: H10 removes every other
    /// Microsoft Graph app role on the stamp identity (task 261) and T3 fails on any other.
    /// </summary>
    IReadOnlyList<GraphAppRoleEntry> GetEntraGranted() => GetAll().Where(r => !ExchangeScopedValues.Contains(r.Value)).ToArray();

    /// <summary>The roles H14a grants through Exchange, scoped to the customer's group.</summary>
    IReadOnlyList<GraphAppRoleEntry> GetExchangeScoped() => GetAll().Where(r => ExchangeScopedValues.Contains(r.Value)).ToArray();
}
