// -----------------------------------------------------------------------------
// StaticKvSecretManifest.cs
//
// INTERIM Null-placeholder <see cref="IKvSecretManifest"/> implementation
// pending Phase H task 084 (canonical secret-catalog manifest generator).
//
// PATTERN JUSTIFICATION:
//   Follows the established Null-placeholder pattern documented in the
//   project's CLAUDE.md § "Sub-Agent Write Boundary" and the sibling
//   implementations:
//     - NullSubscriptionReadinessProbe (task 043) → future ARM impl
//     - NullDataverseEnvironmentRegistryClient (task 042) → future
//       Dataverse-backed impl
//     - NullAdminConsentVerifier (task 046) → future Microsoft.Graph SDK
//       v6 impl
//     - StubAiSearchTenantFilterTemplateProvisioner (task 045) → future
//       real REST-API-backed impl
//   The seam earns its keep on day one (H4 handler + tests DO exercise the
//   interface); the interim body will be swapped for the real Phase H reader
//   without touching H4 handler code, tests, or the DI registration site
//   (parity with the sibling swaps queued for wave C5+).
//
// SCOPE (interim behavior):
//   Returns the known-required §7.9 canonical entries hard-coded per the
//   design.md §7.9 rename map + the spec.md § MUST rules pertaining to H4:
//     - `Dataverse-ClientSecret` (BINDING never-delete; §7.9 grandfathered casing)
//     - `BFF-API-ClientSecret`   (BINDING never-delete; §7.9 grandfathered casing)
//     - `AiSearch--AdminKey`     (§7.9 canonical over 3 legacy aliases)
//     - `Dataverse-ServiceUrl`   (§7.9 rename from SPRK-DEV-DATAVERSE-URL)
//     - `OpenAi-ApiKey`          (E-2 fallback per ADR-028; kept structurally so H4 covers upgrade
//                                  paths where MI OpenAI auth is unavailable — the actual value's
//                                  provenance is E-2 remediation-only)
//     - `SPE-ContainerTypeId`    (per FR-11 H8 output — H4 pre-creates the slot; H8 populates value)
//     - `Communication-Webhook-SigningKey` (§7.9 kebab canonical; H14 consumer)
//   All entries: Operation=Upsert, ValueSource=FromBicepOutput (interim
//   simplification — the real Phase H manifest will vary source per entry).
//
// PHASE H REPLACEMENT:
//   When task 084 lands `scripts/canonical-secret-catalog/manifest.yaml` +
//   the generator, replace the DI registration in Program.cs:
//     services.AddSingleton<IKvSecretManifest, StaticKvSecretManifest>();
//     // becomes:
//     services.AddSingleton<IKvSecretManifest, FileKvSecretManifest>();
//   Handler + tests are untouched. Interim + real impl coexist for one
//   release cycle so the interim can be trivially reverted-to if Phase H
//   generator hits a bug.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Interim Null-placeholder <see cref="IKvSecretManifest"/> returning the
/// known-required §7.9 canonical entries hard-coded per design.md §7.9 +
/// spec.md MUST rules. Swap for the real Phase H generator-driven impl
/// (task 084) via DI registration change only.
/// </summary>
public sealed class StaticKvSecretManifest : IKvSecretManifest
{
    private static readonly IReadOnlyList<KvSecretEntry> KnownEntries = new List<KvSecretEntry>
    {
        // BINDING never-delete pair (spec.md MUST rule + r3 handoff) —
        // Dataverse-ClientSecret is the OBO + shared-lib Dataverse camp; task
        // 011 #3b will migrate it to MI eventually, but until then any
        // rename/delete breaks the entire fleet. Grandfathered PascalCase per
        // §7.9 R2 (never gain a second casing).
        new KvSecretEntry("Dataverse-ClientSecret", KvSecretOperation.Upsert, KvSecretValueSource.FromExistingKvSecret),
        new KvSecretEntry("BFF-API-ClientSecret",  KvSecretOperation.Upsert, KvSecretValueSource.FromExistingKvSecret),

        // §7.9 rename map: 3 aliases in 3 casings collapse to this canonical
        // form. Interim impl still upserts; Phase H manifest may add explicit
        // aliasCollapse ops after LIVE App-Service + KV + Dataverse pre-check.
        new KvSecretEntry("AiSearch--AdminKey",    KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),

        // §7.9 rename from SPRK-DEV-DATAVERSE-URL (env-token-in-name violation).
        new KvSecretEntry("Dataverse-ServiceUrl",  KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),

        // E-2 fallback per ADR-028: AzureOpenAI__ApiKey KV ref used when MI
        // auth on kind=AIServices returns persistent 401. H4 pre-creates the
        // slot so the App Service KV reference resolves; value provenance is
        // E-2 remediation. Non-fatal if absent post-manifest-refresh.
        new KvSecretEntry("AzureOpenAI-ApiKey",    KvSecretOperation.Upsert, KvSecretValueSource.Generated),

        // FR-11 (H8) — SPE container-type ID: H4 pre-creates the slot; H8
        // populates the actual container ID value after 24h SPE replication
        // completes. Storing as a KV secret (not a plain env-var) so the
        // shared-lib SpeAdmin path can resolve via KV ref.
        new KvSecretEntry("SPE-ContainerTypeId",   KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),

        // FR-19 (H14) — Communication module webhook signing key; kebab-case
        // canonical per §7.9 R2 (versus the drift form `compose-webhook-signingkey`
        // which is a Phase H alias-collapse target).
        new KvSecretEntry("Communication-Webhook-SigningKey", KvSecretOperation.Upsert, KvSecretValueSource.Generated),
    };

    private readonly ILogger<StaticKvSecretManifest> _logger;

    /// <summary>Constructs the interim static manifest.</summary>
    public StaticKvSecretManifest(ILogger<StaticKvSecretManifest> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<KvSecretManifestReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation(
            "H4 interim StaticKvSecretManifest returning {Count} known-required §7.9 entries " +
            "(Phase H canonical secret-catalog manifest task 084 NOT YET SHIPPED — swap DI when it lands)",
            KnownEntries.Count);
        return Task.FromResult<KvSecretManifestReadResult>(
            new KvSecretManifestReadResult.Success(KnownEntries));
    }

    /// <summary>
    /// Exposed for tests + operator visibility: the canonical entry set the
    /// interim manifest returns. NOT part of the public API contract — the
    /// real Phase H manifest generates entries at runtime.
    /// </summary>
    internal static IReadOnlyList<KvSecretEntry> KnownRequiredEntries => KnownEntries;
}
