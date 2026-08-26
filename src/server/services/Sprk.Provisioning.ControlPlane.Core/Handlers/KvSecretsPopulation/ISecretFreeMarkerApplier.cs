// -----------------------------------------------------------------------------
// ISecretFreeMarkerApplier.cs
//
// Row A38a (task 205a, 2026-08-25) — positive secret-free migration marker
// seam for H4/H4-shared. Auth-v4 §9.1 sentinel ruling: OMIT is the signal for
// the credential slots themselves (a sentinel value produces an opaque
// AADSTS7000215 at runtime); the POSITIVE record that an environment has
// migrated therefore lives OUTSIDE the credential slots, as BOTH:
//   (a) a KV resource tag  `spaarke-secret-free-identity=true`  — infra-scan
//       detectability (az/ARM tag queries; A38c operator-script pre-check
//       gates read this tag before re-seeding);
//   (b) a `sprk_dataverseenvironment` state field
//       `sprk_credentialmode=secret-free` — provisioning-run auditability on
//       the admin-tenant registry row.
// Both halves are IDEMPOTENT (tag: check-then-apply; registry: value-
// idempotent PATCH) — a re-run yields the same state.
//
// §11 JUSTIFICATION (CLAUDE.md — new component, row A38a):
//   Existing — no current component writes KV resource tags or the (new)
//   sprk_credentialmode registry field. IAppServiceIdentityPatcher /
//   ISlotIdentityRoleGranter hold ArmClients but own single unrelated
//   responsibilities (App Service PATCH / RBAC grant); overloading either
//   would mix reasons-to-change. IDataverseEnvironmentRegistryClient is
//   REUSED for the registry half (extended with UpdateCredentialModeAsync —
//   extension over duplication).
//   Extension — this seam is consumed by BOTH H4 (per-tenant vault; under
//   Model 2 the per-customer dispatch fan-out invokes H4 once per vault, so
//   the marker lands once per vault with no new iteration pass) and
//   H4-shared (shared vault), keeping marker semantics in ONE place.
//   Cost-of-doing-nothing — without the positive marker, a secret-free
//   environment is indistinguishable from a mis-seeded one; rotation /
//   seeding scripts (A38c) have no pre-check signal and silently re-seed —
//   the exact remediation-plan §5.3 fleet-consistency gap.
//
// SEAM JUSTIFICATION (ADR-010): ≥2 impls from day 1 — production
// ArmSecretFreeMarkerApplier (ArmClient tag ops + registry client) and
// per-unit-test stubs in H4/H4-shared handler tests.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Shared constants for the A38a positive secret-free migration marker
/// (auth-v4 §9.1 — marker lives OUTSIDE the credential slots).
/// </summary>
public static class SecretFreeMarker
{
    /// <summary>KV resource tag name applied to every vault participating in the secret-free contract.</summary>
    public const string VaultTagName = "spaarke-secret-free-identity";

    /// <summary>KV resource tag value.</summary>
    public const string VaultTagValue = "true";

    /// <summary><c>sprk_dataverseenvironment.sprk_credentialmode</c> value written for a migrated environment.</summary>
    public const string CredentialModeSecretFree = "secret-free";
}

/// <summary>
/// Applies the A38a positive secret-free migration marker: KV resource tag
/// (<see cref="SecretFreeMarker.VaultTagName"/>) + registry state field
/// (<c>sprk_credentialmode</c>). Idempotent — see file header.
/// </summary>
public interface ISecretFreeMarkerApplier
{
    /// <summary>
    /// Applies both marker halves for one vault + its environment registry
    /// row. Domain failures return
    /// <see cref="SecretFreeMarkerApplyOutcome.Failure"/> (never throw);
    /// only <see cref="OperationCanceledException"/> propagates.
    /// </summary>
    Task<SecretFreeMarkerApplyOutcome> ApplyAsync(
        SecretFreeMarkerApplyRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// One marker application input.
/// </summary>
/// <param name="SubscriptionId">Subscription hosting the target vault.</param>
/// <param name="ResourceGroupName">Resource group hosting the target vault.</param>
/// <param name="KeyVaultName">Target vault name (per-tenant <c>kv-{customerId}-{secretsVer}</c> / shared <c>sprk-{env}-kv</c>).</param>
/// <param name="TenantId">Customer Entra tenant id — the registry row lookup key (<c>sprk_tenantid</c>).</param>
/// <param name="CustomerIdForLog">Customer id — log correlation only.</param>
/// <param name="RunIdForLog">Run id — log correlation only.</param>
public sealed record SecretFreeMarkerApplyRequest(
    string SubscriptionId,
    string ResourceGroupName,
    string KeyVaultName,
    string TenantId,
    string CustomerIdForLog,
    string RunIdForLog);

/// <summary>
/// Discriminated outcome of one marker application.
/// </summary>
public abstract record SecretFreeMarkerApplyOutcome
{
    private SecretFreeMarkerApplyOutcome() { }

    /// <summary>
    /// Both marker halves are in the desired state.
    /// <paramref name="VaultTagWasAlreadyPresent"/> distinguishes the
    /// idempotent re-run (check-then-apply found the tag already set — no
    /// ARM write issued) from the first application.
    /// </summary>
    public sealed record Applied(bool VaultTagWasAlreadyPresent) : SecretFreeMarkerApplyOutcome;

    /// <summary>
    /// Either half failed (ARM tag read/write error, registry row missing,
    /// registry PATCH rejected). FAIL-LOUD by design — a silently unmarked
    /// secret-free vault is the remediation-plan §5.3 fleet-consistency gap.
    /// </summary>
    public sealed record Failure(string Diagnostic) : SecretFreeMarkerApplyOutcome;
}
