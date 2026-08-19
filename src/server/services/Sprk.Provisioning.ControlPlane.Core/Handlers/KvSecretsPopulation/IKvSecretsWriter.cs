// -----------------------------------------------------------------------------
// IKvSecretsWriter.cs
//
// Writer abstraction over the customer's Key Vault — populates + deletes secrets
// per an ordered list of manifest entries. Owns the ADR-028 "cleartext never
// traverses handler code" boundary — the writer implementation is the ONLY
// place cleartext bytes exist in memory, and only for the duration of the
// `az keyvault secret set` shell-out (or `SecretClient.SetSecretAsync` SDK call).
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="AzCliKvSecretsWriter"/> — shells out to
//       `az keyvault secret set/show/delete` under the operator's `az` auth
//       chain (DefaultAzureCredential inside az CLI via `az login`). Parity
//       with the other H-series shell-out probes (T1, drift detector, etc.).
//     - Test: per-unit-test stubs that return canned outcomes without any
//       KV round-trip.
//   Interface earns its keep — no NIH.
//
// DESIGN CHOICE (az CLI shell-out vs Azure.Security.KeyVault.Secrets SDK):
//   Same trade-off as H2a IArmKeyVaultRefProbe: the `az` CLI already carries
//   the operator's auth chain + retry semantics + throttle back-off + a
//   consistent audit signal that matches what operators see when running the
//   PS scripts by hand. Adding Azure.Security.KeyVault.Secrets as a project
//   PackageReference (it's currently a transitive of Microsoft.Identity.Web.
//   Certificate) would trade one shell-out for one SDK; ROI is negative for
//   H4's ~7-entry manifest per customer per invocation.
//
// UPGRADE-MODE ROTATION SEMANTICS (spec.md FR-34):
//   The writer's Upsert branch is rotation-safe BY DEFAULT — a request with
//   <see cref="KvSecretWriteRequest.RotateExisting"/> = false + an already-
//   present secret returns <see cref="KvSecretWriteAction.SkippedRotationSafe"/>
//   WITHOUT overwriting. Only <see cref="KvSecretWriteRequest.RotateExisting"/>
//   = true (H4-rotate variant, spec.md FR-34 H4 row) causes an overwrite.
//   The default is INTENTIONAL: a re-provision that accidentally overwrites
//   a live `Dataverse-ClientSecret` breaks OBO fleet-wide (§7.9 pre-check).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Populates + deletes KV secrets per an ordered list of manifest entries.
/// Owns the ADR-028 "cleartext never traverses handler code" boundary.
/// </summary>
public interface IKvSecretsWriter
{
    /// <summary>
    /// Executes the manifest against the target Key Vault. Returns a per-entry
    /// outcome list preserving input order so the handler can classify partial
    /// failures per §4C. Domain outcomes (write / skip / delete / per-entry
    /// failure) never throw; only genuine infra faults (network, auth) throw
    /// so the handler can distinguish "one entry failed" (partial-fail branch)
    /// from "the writer itself broke" (Resumable branch).
    /// </summary>
    /// <param name="request">The manifest + target vault + rotation mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<KvSecretsWriteOutcome> WriteAsync(
        KvSecretWriteRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// One writer invocation input. Assembled by
/// <see cref="H4KvSecretsPopulationHandler"/> from the run parameters +
/// manifest.
/// </summary>
/// <param name="CustomerId">Customer partition key — carried for audit logs only.</param>
/// <param name="TargetKeyVaultName">Canonical KV name (e.g. <c>sprk-acme-prod-kv</c>) per §7.9 R3.</param>
/// <param name="SubscriptionId">Subscription id — needed for `az` CLI --subscription scoping.</param>
/// <param name="Entries">Manifest entries in canonical order.</param>
/// <param name="UpgradeMode">True when the customer's <c>sprk_provisionedon</c> is non-null (spec.md FR-34).</param>
/// <param name="RotateExisting">When true (H4-rotate variant), overwrite existing secret values on Upsert. Default false = rotation-safe (§7.9 pre-check).</param>
/// <param name="SecretParameters">
/// Task 126 addition. Keyed by manifest <see cref="KvSecretEntry.CanonicalName"/> — a
/// new, minimal, additive convention on top of the existing
/// <see cref="Models.RunParameters.Secrets"/> contract (see
/// notes/task-126-deviations.md). Consumed by
/// <see cref="IKvSecretValueResolver"/> for
/// <see cref="KvSecretValueSource.FromExistingKvSecret"/> +
/// <see cref="KvSecretValueSource.FromRunParameters"/> entries. Defaults to an
/// empty dictionary so existing call sites/tests need no change.
/// </param>
/// <param name="OmitCanonicalNames">
/// Task 126 addition (spec.md FR-39 auth-v4 FIC pluggability). Canonical
/// names the writer MUST omit entirely — NO KV read/write call at all —
/// because a coordinated run parameter indicates the corresponding OBO
/// credential has already migrated to FIC. Manifest-driven / run-parameter-
/// driven, NEVER a hardcoded canonical-name check in writer code (parity
/// with task 125's FR-39 "no special-casing" commitment — this set is DATA
/// supplied per-run, not a code branch on identity). Defaults to an empty
/// set so existing call sites/tests need no change.
/// </param>
public sealed record KvSecretWriteRequest(
    string CustomerId,
    string TargetKeyVaultName,
    string SubscriptionId,
    IReadOnlyList<KvSecretEntry> Entries,
    bool UpgradeMode,
    bool RotateExisting,
    IReadOnlyDictionary<string, Models.KeyVaultSecretRef>? SecretParameters = null,
    IReadOnlySet<string>? OmitCanonicalNames = null)
{
    /// <summary>Effective secret-parameter map — never null (empty when the caller passes none).</summary>
    public IReadOnlyDictionary<string, Models.KeyVaultSecretRef> SecretParameters { get; init; } =
        SecretParameters ?? new Dictionary<string, Models.KeyVaultSecretRef>(StringComparer.Ordinal);

    /// <summary>Effective FIC-omit set — never null (empty when the caller passes none).</summary>
    public IReadOnlySet<string> OmitCanonicalNames { get; init; } =
        OmitCanonicalNames ?? new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// Outcome of a single manifest entry's write attempt. Fields capture enough
/// signal for the handler's operator-facing diagnostic without leaking the
/// secret value.
/// </summary>
/// <param name="CanonicalName">The manifest entry's canonical name (never a value).</param>
/// <param name="Action">What actually happened (Wrote / Skipped / Deleted / Failed).</param>
/// <param name="ErrorMessage">Failure detail if <see cref="Action"/> == Failed; null otherwise. MUST NOT contain any secret value.</param>
public sealed record KvSecretWriteResult(
    string CanonicalName,
    KvSecretWriteAction Action,
    string? ErrorMessage);

/// <summary>What the writer did for a single manifest entry.</summary>
public enum KvSecretWriteAction
{
    /// <summary>New secret created OR an existing secret rotated (H4-rotate variant only).</summary>
    Wrote = 1,

    /// <summary>Secret already exists on target vault; upgrade mode + rotate=false; NO overwrite (§7.9 pre-check + FR-34 rotation-safe default).</summary>
    SkippedRotationSafe = 2,

    /// <summary>Existing secret deleted per manifest Delete op (only reachable after BINDING pre-check permits — never for Dataverse-ClientSecret / BFF-API-ClientSecret).</summary>
    Deleted = 3,

    /// <summary>Per-entry write / delete failed. See <see cref="KvSecretWriteResult.ErrorMessage"/> for detail.</summary>
    Failed = 4,

    /// <summary>
    /// Task 126 addition (spec.md FR-39). Entry intentionally NOT written —
    /// <see cref="KvSecretWriteRequest.OmitCanonicalNames"/> lists this entry's
    /// canonical name because a coordinated run parameter indicates the
    /// corresponding OBO credential has already migrated to FIC. NO KV
    /// read/write call was made for this entry.
    /// </summary>
    Omitted = 5,
}

/// <summary>
/// Discriminated result of the whole writer invocation. Success carries per-
/// entry results; Failure carries a whole-invocation diagnostic (writer itself
/// failed BEFORE any entry attempted — Resumable). Partial-failure state is
/// encoded in the Success payload (any entry with Action=Failed) — the handler
/// classifies partial as QuarantineRequired.
/// </summary>
public abstract record KvSecretsWriteOutcome
{
    private KvSecretsWriteOutcome() { }

    /// <summary>Writer invocation completed — per-entry results may include failures.</summary>
    public sealed record Success(IReadOnlyList<KvSecretWriteResult> Results) : KvSecretsWriteOutcome;

    /// <summary>Writer invocation failed BEFORE any entry was attempted (e.g. az CLI missing, no auth chain). Resumable.</summary>
    public sealed record Failure(string Diagnostic) : KvSecretsWriteOutcome;
}
