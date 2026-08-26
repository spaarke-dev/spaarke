// -----------------------------------------------------------------------------
// CredentialKind.cs
//
// L2 CONTROL-PLANE Worker credential kinds for the FR-39 ordered credential
// seam (punch row A44.5 — the H7/task-142 half of A30's sentinel contract;
// customer-provisioning-orchestration-r1 task 205i, 2026-08-25).
//
// MIRROR PROVENANCE (constraint "mirror master's pattern — do NOT invent a
// parallel pattern"): member names + semantics mirror the BFF's
// `Sprk.Bff.Api.Configuration.CredentialKind` (auth-v4 task 021, ADR-028
// Amendment A4, brought onto this branch via A35) verbatim for the two kinds
// the L2 Worker supports. Enum member NAMES are the config contract — they
// bind case-insensitively from `{Section}:Credentials:Order:N` app settings,
// exactly like `Graph__Credentials__Order__0=ManagedIdentityFederated` does
// on the BFF (§10.2 live contract).
//
// DELIBERATE NARROWING — no KeyVaultCertificate member: A4's certificate
// fallback is "dropped, not deferred" on the provisioning estate (§9.2
// contingency in notes/decisions/adr-028-a4-integration-conflict-resolution.md);
// the L2 cert-provisioning estate is unbuilt. Listing an unimplementable kind
// here would let an operator configure a credential that can never be
// acquired — a silent-fail invite of exactly the class this project exists to
// eliminate. An unknown name in the configured order (including
// "KeyVaultCertificate") fails FAST at Worker boot via
// WorkerCredentialSelectionOptions.ResolveEffectiveOrder.
//
// §11 three-question justification (root CLAUDE.md — required for new files):
//   Existing  — the BFF's CredentialKind exists but is BFF-assembly-internal
//               (Sprk.Bff.Api.Configuration); the shared Spaarke.Dataverse lib
//               exposes only the MSAL-based IConfidentialClientProvider seam.
//   Extension — ControlPlane.Core deliberately does NOT reference
//               Spaarke.Dataverse (it would drag Microsoft.PowerPlatform
//               .Dataverse.Client + MSAL into the Worker publish; the L2
//               collaborators are raw-HttpClient + Azure.Identity by design —
//               see IEnvVarValuesWriter.cs "DESIGN CHOICE"). Mirroring the
//               enum (2 members, name-identical) is the minimal extension that
//               keeps ONE naming convention across both hosts.
//   Cost-of-doing-nothing — without the seam, H7/H6 hard-require
//               `BFF-API-ClientSecret` and every secret-free env (§6.5
//               resolution prong 1) boot-loops the Worker or invites a §9.1
//               sentinel (opaque AADSTS7000215). See task 205i POML notes.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.Credentials;

/// <summary>
/// The confidential credentials the L2 Worker may present as the shared BFF
/// app registration's identity when authenticating to Dataverse environments
/// (H7 env-var writes, H6 solution import), in the priority order configured
/// via <c>{Section}:Credentials:Order</c> (FR-39 / ADR-028 Amendment A4 —
/// mirror of the BFF's <c>Graph:Credentials:Order</c> contract).
/// </summary>
public enum CredentialKind
{
    /// <summary>
    /// A client assertion minted by the Worker App Service's user-assigned
    /// managed identity and trusted by the BFF app registration through a
    /// federated identity credential (created by H3 /
    /// <c>Register-EntraAppRegistrations.ps1 -FicOnly</c>). The canonical A4
    /// credential: secret-free, nothing to rotate, nothing to leak. The
    /// ONLY entry on secret-free environments (§10.2 live contract).
    /// </summary>
    ManagedIdentityFederated,

    /// <summary>
    /// The transitional client secret (<c>BFF-API-ClientSecret</c>).
    /// Selectable ONLY for prong-3 unmigrated environments (§6.5 resolution
    /// record) — never for new secret-free provisioning. This is also the
    /// implicit LEGACY default when no <c>Credentials:Order</c> is configured,
    /// preserving pre-A44.5 (task 142 / task 204a) behavior byte-for-byte.
    /// </summary>
    ClientSecret,
}
