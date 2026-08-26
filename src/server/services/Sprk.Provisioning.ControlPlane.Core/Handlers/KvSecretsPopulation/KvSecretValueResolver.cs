// -----------------------------------------------------------------------------
// KvSecretValueResolver.cs
//
// Task 126 (Wave G-2 Batch G-2C, H4 real-values correctness gate) — REPLACES
// the deterministic non-functional placeholder value (canonical name +
// "-interim-" + "placeholder-" + customerId, concatenated) that both the
// retired AzCliKvSecretsWriter and task 125's SecretClientKvWriter wrote
// for EVERY manifest entry regardless of its declared <see cref="KvSecretValueSource"/>.
// DS-4 §3 flagged this as the single highest-priority correctness gap in the
// Wave G build: "a successful H4 leaves every downstream secret consumer
// broken" because the written VALUES were never real.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1: this production impl (real SDK/
//   crypto resolution) + per-unit-test fakes (SecretClientKvWriterTests.cs's
//   FakeValueResolver — canned outcomes, no HTTP) + this file's own tests
//   exercise the production impl against a fake HttpClientTransport (ADR-038
//   path #1, ground-truthed SecretClient.GetSecretAsync shape reused from
//   SecretClientKvWriterTests.cs's header comment — same 4.11.0 package).
//
// VALUE-SOURCE MAPPING (the manifest's real `value_source` vocabulary vs this
// task's dispatch-brief "generate/copy/reference" 3-bucket shorthand):
//   The actual <see cref="KvSecretValueSource"/> enum (unchanged since task
//   047) has FOUR members, not three. scripts/canonical-secret-catalog/
//   manifest.yaml (task 084) confirms all four are live: from-existing-kv,
//   from-bicep-output, from-run-parameter, generated. This resolver maps:
//     - Generated            -> GENERATE: RandomNumberGenerator, 256-bit,
//                               lower-hex encoded (Convert.ToHexStringLower).
//                               No manifest-declared length/charset field
//                               exists yet (task 084's schema doesn't carry
//                               one) — 256-bit hex is the uniform default,
//                               matches this project's other generated
//                               secrets (webhook signing keys) in shape.
//     - FromExistingKvSecret  -> COPY: resolves a KeyVaultSecretRef from
//     - FromRunParameters     -> RunParameters.Secrets[canonicalName] (keyed
//                               by CANONICAL NAME — a new, minimal, additive
//                               convention this task establishes on TOP of
//                               the existing RunParameters.Secrets contract;
//                               see task-126-deviations.md), reads the
//                               REAL cleartext from the referenced source
//                               vault via SecretClient.GetSecretAsync, and
//                               returns it for the writer to copy into the
//                               target vault. BOTH enum members resolve
//                               identically — App Service's
//                               keyVaultReferenceIdentity resolves KV
//                               references exactly ONE hop, so ANY secret
//                               consumed via a customer-vault KV-ref app
//                               setting (e.g. TenantId, Dataverse-ServiceUrl,
//                               BingSearch-ApiKey) needs its REAL cleartext
//                               landed in the target vault — a nested
//                               "write the pointer, not the value" behavior
//                               would silently break every such consumer,
//                               which is exactly the class of defect this
//                               task exists to eliminate. See
//                               task-126-deviations.md "generate/copy/
//                               reference vocabulary mismatch" for the full
//                               reasoning (the dispatch brief's 3-bucket
//                               mental model doesn't map 1:1 onto the real
//                               4-member enum; this is the evidence-grounded
//                               resolution, not an invented shortcut).
//     - FromBicepOutput       -> NO REACHABLE SOURCE from H4's writer today.
//                               task 084's own kv-secrets.generated.bicep
//                               module (not yet wired into customer.bicep —
//                               that's a still-open follow-on beyond task
//                               086's actual landed scope) is the INTENDED
//                               writer for these entries, sourcing values
//                               from ARM deployment outputs at Bicep-deploy
//                               time (H2a). H4's InterStepState is a locked,
//                               enumerated POCO (design.md §6.2) that does
//                               NOT carry per-secret Bicep-output slots for
//                               the ~10 FromBicepOutput entries. Rather than
//                               invent a fabricated value (the exact defect
//                               DS-4 flagged, one level up), this resolver
//                               ALWAYS returns Failed for FromBicepOutput —
//                               the WRITER (SecretClientKvWriter) only calls
//                               this branch when the secret does NOT already
//                               exist on the target vault (if it exists —
//                               written by an earlier Bicep deploy or a
//                               prior run — the writer skips before ever
//                               invoking the resolver). This is a genuine,
//                               tracked plumbing gap — see
//                               task-126-deviations.md "FromBicepOutput gap"
//                               for the escalation-lite record + candidate
//                               follow-on task.
//
// SECRET-VALUE NO-LOG GUARD (root CLAUDE.md §9 Security Rules): this file
// contains ZERO Log* calls. Cleartext values flow ONLY as method return
// values / local variables, never through ILogger. Diagnostics on Failed
// resolutions carry secret NAMES + vault NAMES only, never values.
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Resolves the real cleartext value for a single manifest entry per its
/// declared <see cref="KvSecretValueSource"/>. Cleartext values pass through
/// this seam ONLY as return values — never logged (ADR-028 MUST rule).
/// </summary>
public interface IKvSecretValueResolver
{
    /// <summary>
    /// Resolves the value H4's writer should persist for <paramref name="entry"/>.
    /// Domain outcomes (resolved / cannot-resolve) never throw; only genuine
    /// infra faults (network, auth) throw so the writer can distinguish
    /// "this entry cannot be resolved" (per-entry Failed) from "the resolver
    /// itself broke" (writer-level Resumable).
    /// </summary>
    Task<KvSecretValueResolution> ResolveAsync(
        KvSecretEntry entry,
        KvSecretWriteRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated outcome of a single entry's value resolution. Mirrors the
/// <see cref="KvSecretManifestReadResult"/> / <see cref="KvSecretsWriteOutcome"/>
/// sealed-hierarchy pattern used elsewhere in this handler family.
/// </summary>
public abstract record KvSecretValueResolution
{
    private KvSecretValueResolution() { }

    /// <summary>Real cleartext value resolved — never logged by the caller.</summary>
    public sealed record Resolved(string Value) : KvSecretValueResolution;

    /// <summary>
    /// No real value could be resolved. <see cref="Diagnostic"/> MUST NOT
    /// contain any secret value — canonical names + vault names only.
    /// </summary>
    public sealed record Failed(string Diagnostic) : KvSecretValueResolution;
}

/// <summary>
/// Production <see cref="IKvSecretValueResolver"/>. Generated values use
/// <see cref="RandomNumberGenerator"/> (cryptographically secure — root
/// CLAUDE.md §9; never <see cref="Random"/>). Copy-sourced values (
/// <see cref="KvSecretValueSource.FromExistingKvSecret"/> +
/// <see cref="KvSecretValueSource.FromRunParameters"/>) are read via
/// <see cref="SecretClient.GetSecretAsync(string, string?, CancellationToken)"/>
/// against the vault named in the entry's <see cref="KeyVaultSecretRef"/> —
/// same ground-truthed SDK shape SecretClientKvWriter already uses (task 125
/// header comment).
/// </summary>
public sealed class KvSecretValueResolver : IKvSecretValueResolver
{
    /// <summary>Generated-value entropy: 256 bits, lower-hex encoded (64 chars).</summary>
    private const int GeneratedValueByteLength = 32;

    private readonly TokenCredential _credential;
    private readonly SecretClientOptions? _clientOptions;

    /// <summary>Constructs the production resolver bound to the shared UAMI-pinned credential.</summary>
    public KvSecretValueResolver(TokenCredential credential)
        : this(credential, clientOptions: null)
    {
    }

    /// <summary>Test seam constructor — injects a fake-transport <see cref="SecretClientOptions"/>.</summary>
    internal KvSecretValueResolver(TokenCredential credential, SecretClientOptions? clientOptions)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _credential = credential;
        _clientOptions = clientOptions;
    }

    /// <inheritdoc/>
    public Task<KvSecretValueResolution> ResolveAsync(
        KvSecretEntry entry,
        KvSecretWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(request);

        return entry.ValueSource switch
        {
            KvSecretValueSource.Generated => Task.FromResult(ResolveGenerated()),
            KvSecretValueSource.FromExistingKvSecret => ResolveFromReferenceAsync(entry, request, cancellationToken),
            KvSecretValueSource.FromRunParameters => ResolveFromReferenceAsync(entry, request, cancellationToken),
            KvSecretValueSource.FromBicepOutput => Task.FromResult<KvSecretValueResolution>(
                new KvSecretValueResolution.Failed(
                    $"No H4-reachable source for value_source=FromBicepOutput on '{entry.CanonicalName}'. " +
                    "This value is expected to already exist on the target vault (written by the " +
                    "customer.bicep ARM deployment at H2a time, or a prior run) — the writer only reaches " +
                    "this resolver branch when the secret does NOT already exist. H4 has no plumbing today " +
                    "to read ARM deployment outputs directly (InterStepState is a locked enumerated POCO " +
                    "without a slot for this entry). See notes/task-126-deviations.md 'FromBicepOutput gap'.")),
            KvSecretValueSource.FromSharedService => Task.FromResult<KvSecretValueResolution>(
                new KvSecretValueResolution.Failed(
                    $"value_source=FromSharedService on '{entry.CanonicalName}' is owned by " +
                    "H4SharedKvSecretsPopulationHandler (task 200) — NOT the per-tenant H4 flow. " +
                    "The per-tenant H4 handler filters these entries out before invoking the writer; " +
                    "if this resolver call is reached, the per-tenant filter has regressed. See " +
                    "H4SharedKvSecretsPopulationHandler.cs for the source-extraction pipeline.")),
            _ => Task.FromResult<KvSecretValueResolution>(
                new KvSecretValueResolution.Failed(
                    $"Unrecognized KvSecretValueSource '{entry.ValueSource}' for '{entry.CanonicalName}'.")),
        };
    }

    /// <summary>
    /// GENERATE branch: cryptographically secure random value. Root CLAUDE.md
    /// §9 — MUST use System.Security.Cryptography, never System.Random.
    /// </summary>
    private static KvSecretValueResolution ResolveGenerated()
        => new KvSecretValueResolution.Resolved(
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(GeneratedValueByteLength)));

    /// <summary>
    /// COPY branch (FromExistingKvSecret + FromRunParameters — see file-header
    /// mapping note for why both resolve identically). Looks up a
    /// <see cref="KeyVaultSecretRef"/> keyed by the entry's CANONICAL NAME in
    /// <see cref="KvSecretWriteRequest.SecretParameters"/>, then reads the
    /// REAL cleartext from the referenced vault. Fails loudly (no fabricated
    /// value) when the reference is absent or the source read fails — the
    /// exact discipline DS-4's fix exists to enforce.
    /// </summary>
    private async Task<KvSecretValueResolution> ResolveFromReferenceAsync(
        KvSecretEntry entry,
        KvSecretWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.SecretParameters.TryGetValue(entry.CanonicalName, out var reference))
        {
            return new KvSecretValueResolution.Failed(
                $"No KeyVaultSecretRef supplied via RunParameters.Secrets['{entry.CanonicalName}'] " +
                $"(value_source={entry.ValueSource}). Upstream handler or operator intake MUST supply " +
                "this reference (vault + secret name pointing at the real source value) before H4 can " +
                "resolve a functional value for this entry — H4 will NOT fabricate a placeholder.");
        }

        SecretClient sourceClient;
        try
        {
            var sourceVaultUri = new Uri($"https://{reference.VaultName}.vault.azure.net/");
            sourceClient = _clientOptions is null
                ? new SecretClient(sourceVaultUri, _credential)
                : new SecretClient(sourceVaultUri, _credential, _clientOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new KvSecretValueResolution.Failed(
                $"Failed to construct SecretClient for source vault '{reference.VaultName}' " +
                $"(entry '{entry.CanonicalName}'): {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var response = await sourceClient
                .GetSecretAsync(reference.SecretName, reference.VersionId, cancellationToken)
                .ConfigureAwait(false);
            return new KvSecretValueResolution.Resolved(response.Value.Value);
        }
        catch (RequestFailedException ex)
        {
            return new KvSecretValueResolution.Failed(
                $"Failed to read source secret '{reference.SecretName}' (version={reference.VersionId ?? "current"}) " +
                $"from vault '{reference.VaultName}' for entry '{entry.CanonicalName}': " +
                $"HTTP {ex.Status} {ex.ErrorCode}: {ex.Message}");
        }
    }
}
