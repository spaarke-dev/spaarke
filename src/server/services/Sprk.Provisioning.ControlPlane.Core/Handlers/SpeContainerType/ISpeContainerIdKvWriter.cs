// -----------------------------------------------------------------------------
// ISpeContainerIdKvWriter.cs
//
// L2 abstraction over persisting H8's SPE container-type id to the customer's
// Key Vault under the §7.9 canonical secret name `SPE-ContainerTypeId` — the
// SAME slot H4's KvSecretsPopulation manifest pre-creates with a placeholder
// value (see StaticKvSecretManifest.cs: "H4 pre-creates the slot; H8
// populates the actual container ID value"). H8 UPDATES that slot with the
// real value.
//
// COMPONENT JUSTIFICATION (CLAUDE.md §11 three-question template):
//   1. Existing — IKvSecretsWriter (H4's KvSecretsPopulation seam) already
//      writes named secrets to a target vault via `az keyvault secret set`.
//   2. Extension — NOT reused directly. AzCliKvSecretsWriter's production
//      value-resolution path (ResolveValueForEntry) is an INTERIM Phase-H
//      placeholder that always returns a deterministic non-secret placeholder
//      string ("{name}-interim-placeholder-{customerId}") regardless of
//      caller intent — it has no code path for a caller-supplied real value
//      (see AzCliKvSecretsWriter.cs header "SCOPE NOTE"). Extending that
//      manifest-driven abstraction (KvSecretValueSource + KvSecretWriteRequest)
//      to carry an explicit value is a cross-cutting change to H4's already-
//      shipped, tested production code that Phase H (task 084, canonical
//      secret-catalog manifest generator) is explicitly slated to replace
//      wholesale — extending it now duplicates work Phase H will redo, and
//      risks regressing a live, merged handler outside this task's scope.
//   3. Cost of doing nothing — without a real-value writer, H8 cannot ever
//      persist the actual container-type id to the customer's KV vault; the
//      FR-11 / T6 / §4D-I4 KV-persistence acceptance criteria would be
//      structurally unmet.
//   This seam is intentionally narrow: ONE named secret, ONE explicit value,
//   parity with AzCliKvSecretsWriter's az-CLI shell-out + rotation-safe
//   posture but without the manifest/placeholder machinery H8 does not need.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <summary>
/// Persists H8's SPE container-type id to the customer Key Vault under the
/// canonical <see cref="SpeContainerTypeOptions.ContainerTypeIdSecretName"/>
/// name. Production impl shells out to <c>az keyvault secret set/show</c>.
/// </summary>
public interface ISpeContainerIdKvWriter
{
    /// <summary>
    /// Writes (or rotation-safely skips) the container-type id secret.
    /// Domain outcomes never throw; genuine infra faults (az CLI missing, auth
    /// chain failure) MAY throw so the handler can classify "the writer itself
    /// broke" separately from a per-secret domain outcome.
    /// </summary>
    Task<SpeContainerIdKvWriteResult> WriteAsync(
        SpeContainerIdKvWriteRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Inputs to the single-secret KV write.</summary>
/// <param name="CustomerId">Customer partition key — audit logs only.</param>
/// <param name="TargetKeyVaultName">Canonical customer KV name (§7.9 R3) — tenant-scoped, never a fallback default (§4D I4).</param>
/// <param name="SubscriptionId">Subscription id for `az` CLI <c>--subscription</c> scoping.</param>
/// <param name="ContainerTypeId">The real SPE container-type id value to persist.</param>
/// <param name="UpgradeMode">
/// True when the customer's <c>provisionedOn</c> run parameter is non-null.
/// design.md line 1158 marks this secret "Never rotate; container = data" —
/// when true AND the secret already holds a value, the writer SKIPS overwrite
/// (protects a live customer's container reference from being clobbered by a
/// re-provision run). When false (fresh provisioning), the writer overwrites
/// unconditionally — the only prior value can be H4's non-secret-shaped
/// placeholder.
/// </param>
public sealed record SpeContainerIdKvWriteRequest(
    string CustomerId,
    string TargetKeyVaultName,
    string SubscriptionId,
    string ContainerTypeId,
    bool UpgradeMode);

/// <summary>Discriminated result of <see cref="ISpeContainerIdKvWriter.WriteAsync"/>.</summary>
public abstract record SpeContainerIdKvWriteResult
{
    private SpeContainerIdKvWriteResult() { }

    /// <summary>Secret written (created or overwritten).</summary>
    public sealed record Wrote() : SpeContainerIdKvWriteResult;

    /// <summary>Upgrade mode + secret already present — skipped to protect live customer data ("never rotate; container = data").</summary>
    public sealed record SkippedAlreadyPresent(string ExistingValuePreview) : SpeContainerIdKvWriteResult;

    /// <summary>Write failed. Never carries the value being written.</summary>
    public sealed record Failure(string Diagnostic) : SpeContainerIdKvWriteResult;
}
