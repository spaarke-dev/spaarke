// -----------------------------------------------------------------------------
// IKvSecretReader.cs
//
// Read-only counterpart to KvSecretsPopulation/IKvSecretsWriter — H14b + H14c
// both need to READ BACK the `Communication-Webhook-SigningKey` HMAC secret
// H4 (task 047) provisioned per §7.9 canonical naming, so they can pass it as
// the Graph subscription `clientState` / Dataverse serviceendpoint signing
// material. IKvSecretsWriter is write-only (WriteAsync a manifest) — no read
// capability exists on that seam.
//
// COMPONENT JUSTIFICATION (CLAUDE.md §11):
//   1. Existing — IKvSecretsWriter (H4) only writes; no interface in the
//      codebase currently reads a single secret VALUE back out of KV for
//      handler consumption (AzCliKvSecretsWriter's SecretExistsAsync only
//      probes existence via `--query id`, never `--query value`).
//   2. Extension — Extending IKvSecretsWriter with a read method would widen
//      H4's writer contract (secrets-population write path) with an
//      unrelated read concern H4 itself never needs; H14 is the ONLY current
//      consumer of a read-back. A narrow, purpose-built seam is cleaner.
//   3. Cost of doing nothing — H14b/H14c cannot obtain the HMAC signing key
//      without a reader seam; Graph webhook subscriptions + Dataverse
//      service-endpoint webhooks would either ship with NO signing key
//      (defeating the whole point of an HMAC-verified webhook) or duplicate
//      ad-hoc `az keyvault secret show` shell-out logic inside two separate
//      sub-handlers.
//
// SEAM JUSTIFICATION (ADR-010): ≥2 implementations exist from day 1 —
// production AzCliKvSecretReader (shells to `az keyvault secret show`) +
// per-unit-test fakes.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Reads a single secret VALUE back out of a customer's Key Vault. Narrow,
/// read-only counterpart to <c>KvSecretsPopulation.IKvSecretsWriter</c>.
/// </summary>
public interface IKvSecretReader
{
    /// <summary>
    /// Reads the current value of <paramref name="secretName"/> from
    /// <paramref name="vaultName"/>. Domain outcomes (NotFound / Failure)
    /// never throw for the "secret doesn't exist yet" case — only genuine
    /// infra faults (network, auth) throw.
    /// </summary>
    Task<KvSecretReadResult> ReadSecretAsync(
        string vaultName,
        string subscriptionId,
        string secretName,
        CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated result of <see cref="IKvSecretReader.ReadSecretAsync"/>.
/// Exhaustive: <see cref="Success"/> | <see cref="NotFound"/> |
/// <see cref="Failure"/>.
/// </summary>
public abstract record KvSecretReadResult
{
    private KvSecretReadResult() { }

    /// <summary>The secret exists; <paramref name="Value"/> carries its cleartext value for the duration of the caller's in-memory use only (never logged, never persisted to Cosmos).</summary>
    public sealed record Success(string Value) : KvSecretReadResult;

    /// <summary>No secret with that name exists on the target vault (e.g. H4 has not yet run for this customer).</summary>
    public sealed record NotFound() : KvSecretReadResult;

    /// <summary>Infrastructure-level failure (auth, network, throttle) reading the secret.</summary>
    public sealed record Failure(string Diagnostic) : KvSecretReadResult;
}
