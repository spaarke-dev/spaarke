// -----------------------------------------------------------------------------
// SecretFreeMarkerConsistencyDetector.cs
//
// Row A38a (task 205a, 2026-08-25) — Model 2 fleet-consistency detector for
// the positive secret-free migration marker (remediation plan §5.3 gap).
//
// PROBLEM: under Model 2 the secret-free contract fans out to N per-customer
// vaults (one H4 run per customer per vault — the per-customer DISPATCH
// iteration is where the marker is applied once-per-vault; there is no
// N-vault loop inside a single handler run by design). A missed tag on ONE
// vault is itself a silent-skip failure: rotation/seeding scripts (A38c)
// read the tag as their pre-check gate, so an untagged secret-free vault
// silently re-admits secret writes while the fleet reports migrated.
//
// DETECTION RULE: across the N vaults of one environment's fleet, the
// marker tag must be UNIFORM — all tagged (migrated fleet) or none tagged
// (pre-migration fleet). MIXED state is the failure. The detector is a
// DETECTOR, not a fatal gate: it logs Warning + returns a Mixed failure
// record for the run report; it never throws (per row A38a step-8 contract).
//
// CONSUMERS: pure evaluation component consumed by the T8-probe /
// H13-aggregation fleet-observation family (task 186 acceptance surface),
// which is where an N-vault observation set actually exists at runtime.
// Wiring the fleet enumeration into that probe family is tracked as a
// follow-up row in notes/task-202-punch-list.md (filed by task 205a).
//
// §11 JUSTIFICATION (new component, row A38a):
//   Existing — no component evaluates cross-vault tag uniformity; H4/H4-shared
//   each see exactly one vault per run. Extension — extracted as its own
//   class (rather than a private helper inside H4-shared) per POML step 8
//   "if a full detector class is warranted for testability" — the N-vault
//   evaluation is unreachable inside any single handler run, so testability
//   REQUIRES the standalone shape. Cost-of-doing-nothing — the §5.3 missed-
//   tag case stays undetectable until a rotation script silently re-seeds a
//   secret the environment migrated away from ("secret beneath MI-FIC
//   absorbing a broken FIC with green health").
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>One vault's observed marker state (input to the detector).</summary>
/// <param name="VaultId">Vault identifier (name or ARM resource id) for operator-facing reporting.</param>
/// <param name="HasSecretFreeTag">Whether the vault carries <c>spaarke-secret-free-identity=true</c>.</param>
public sealed record VaultMarkerObservation(string VaultId, bool HasSecretFreeTag);

/// <summary>Discriminated result of one fleet-consistency evaluation.</summary>
public abstract record SecretFreeMarkerConsistencyResult
{
    private SecretFreeMarkerConsistencyResult() { }

    /// <summary>All N vaults agree (all tagged, or none tagged). Healthy.</summary>
    public sealed record Uniform(bool AllTagged, int VaultCount) : SecretFreeMarkerConsistencyResult;

    /// <summary>
    /// MIXED state — the §5.3 failure record: at least one vault is tagged
    /// while at least one is not. Carries both lists for the run report.
    /// </summary>
    public sealed record Mixed(
        IReadOnlyList<string> TaggedVaultIds,
        IReadOnlyList<string> UntaggedVaultIds) : SecretFreeMarkerConsistencyResult;
}

/// <summary>
/// Evaluates N per-customer vault marker observations for uniformity.
/// Mixed state logs Warning + returns the failure record; never throws.
/// </summary>
public sealed class SecretFreeMarkerConsistencyDetector
{
    private readonly ILogger<SecretFreeMarkerConsistencyDetector> _logger;

    /// <summary>Constructs the detector.</summary>
    public SecretFreeMarkerConsistencyDetector(ILogger<SecretFreeMarkerConsistencyDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Evaluates the observation set. Empty input is vacuously
    /// <see cref="SecretFreeMarkerConsistencyResult.Uniform"/> (no fleet, no
    /// inconsistency).
    /// </summary>
    public SecretFreeMarkerConsistencyResult Evaluate(IReadOnlyList<VaultMarkerObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var tagged = observations.Where(o => o.HasSecretFreeTag).Select(o => o.VaultId).ToList();
        var untagged = observations.Where(o => !o.HasSecretFreeTag).Select(o => o.VaultId).ToList();

        if (tagged.Count > 0 && untagged.Count > 0)
        {
            _logger.LogWarning(
                "A38a fleet-consistency detector: MIXED secret-free marker state across {Total} vaults — " +
                "{TaggedCount} tagged, {UntaggedCount} MISSING the {Tag} tag. Untagged vaults: {Untagged}. " +
                "A missed tag re-admits secret writes on a migrated vault (remediation plan §5.3); " +
                "re-run H4 for the untagged vaults or investigate a partial rollback.",
                observations.Count, tagged.Count, untagged.Count,
                SecretFreeMarker.VaultTagName, string.Join(", ", untagged));
            return new SecretFreeMarkerConsistencyResult.Mixed(tagged, untagged);
        }

        return new SecretFreeMarkerConsistencyResult.Uniform(
            AllTagged: tagged.Count > 0 && untagged.Count == 0,
            VaultCount: observations.Count);
    }
}
