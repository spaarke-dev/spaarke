// -----------------------------------------------------------------------------
// FileKvSecretManifest.cs
//
// Task 126 (Wave G-2 Batch G-2C) — the "task-084 canonical manifest DI-swap
// (C2.2)" half of this task. Replaces the interim <see cref="StaticKvSecretManifest"/>
// (7 hardcoded entries) with a reader over the REAL Phase H canonical
// secret-catalog manifest (task 084, scripts/canonical-secret-catalog/
// manifest.yaml — 26 entries as of 2026-08-19).
//
// SCOPE CORRECTION vs the dispatch brief's "1-line DI swap" sizing:
//   Neither task 084 nor its follow-on task 086 shipped a .NET-consumable
//   reader for manifest.yaml — task 084's own deviations note
//   (notes/task-084-deviations.md "Downstream consumers") explicitly says
//   "the file-backed reader lands (post-084 follow-on, out of scope for
//   this task)". DS-4's "1 line" estimate assumed that reader already
//   existed; it does not. This file IS that follow-on reader — genuinely
//   new work, not a registration-only change. See
//   notes/task-126-deviations.md "manifest DI-swap scope correction".
//
// WHY EMBED THE YAML (not read from disk at runtime):
//   Parity with task 124's IndexSchemas/*.json embedding (SearchIndexClientProvisioner)
//   — the L2 App Service publish output must be self-contained (no repo-
//   checkout dependency at runtime). manifest.yaml lives OUTSIDE this
//   project's folder (scripts/canonical-secret-catalog/ at repo root) —
//   embedded via a relative <EmbeddedResource> Include path + explicit
//   <Link> in the .csproj so the SINGLE canonical file (task 084's
//   single-source-of-truth contract) is the actual build input, never a
//   duplicated copy that could drift.
//
// PARSER CHOICE (YamlDotNet, new PackageReference):
//   manifest.yaml is genuinely YAML (task 084's own generator uses
//   PowerShell's `powershell-yaml` module for the SAME file). YamlDotNet
//   18.1.0 is the de-facto standard .NET YAML library (537M+ NuGet
//   downloads, MIT license) — reading the SAME canonical file directly
//   (rather than adding a 5th generated JSON artifact to task 084's
//   generator, which would be new scope on a DIFFERENT task's owned
//   surface) is the minimal, non-duplicating choice per CLAUDE.md §11.
//
// FIELDS CONSUMED (only what <see cref="KvSecretEntry"/> needs today):
//   canonical_name, never_delete, value_source. The manifest carries much
//   richer fields (purpose, consumers, rotation_cadence, aliases,
//   app_settings, tags) that no current H4 collaborator consumes — reading
//   only what's needed keeps this parser small and matches
//   IKvSecretManifest's existing 3-field KvSecretEntry contract (Operation
//   is always Upsert for every manifest.yaml entry — Delete ops are a
//   manual, pre-checked alias-collapse operation per task 085's pattern,
//   never emitted by this generator/reader).
//
// BINDING INVARIANT (ported from Invoke-CatalogGenerator.ps1's own guard —
// manifest.yaml's own "Binding invariants" comment block):
//   Dataverse-ClientSecret + BFF-API-ClientSecret MUST both be present with
//   never_delete: true. This reader refuses to SERVE entries (returns
//   Failure) if that invariant is violated — the C# read-time mirror of the
//   PowerShell generator's write-time refusal.
//
// A38a SECRET-FREE SERVED-ENTRY FILTER (task 205a, 2026-08-25 — auth-v4
// §9.1 OMIT-is-the-signal + §10.1 Δ1/Δ2; peer escalation record in
// notes/auth-v4-integration-draft-punch-rows.md "A38 execution record";
// §6.5 record notes/decisions/adr-028-a4-integration-conflict-resolution.md;
// remediation plan §5 item 3):
//   On secret-free environments (KvSecretsPopulationOptions.
//   RequireSecretFreeIdentity=true, mirroring the BFF's
//   Graph__Credentials__RequireSecretFreeIdentity), the three A38a
//   credential slots (SecretFreeIdentityOmitTargets: BFF-API-ClientSecret,
//   ServiceBus-ConnectionString, AiSearch--AdminKey) are FILTERED from the
//   SERVED entry list — DOWNSTREAM of the BINDING never-delete invariant
//   check above, which runs against the RAW yaml document and is unaffected.
//   The manifest.yaml rows themselves are NEVER deleted (the invariant
//   REQUIRES BFF-API-ClientSecret's row to stay present with
//   never_delete=true; omit is a served-entry filter, not a catalog edit).
//   OMIT — never a sentinel value — is the §9.1-ruled signal: a sentinel in
//   the slot produces an opaque AADSTS7000215 at runtime, indistinguishable
//   from real misconfiguration. The positive migration marker lives OUTSIDE
//   the credential slots (KV resource tag + sprk_dataverseenvironment state
//   field — see ISecretFreeMarkerApplier).
//   Q3 Path A rollback (SecretFreeIdentityRollback=true) re-includes ONLY
//   the three A38a targets. Dataverse-ClientSecret is NEVER filtered (Q3
//   Path A rollback copy, unconditional until 2026-11-23 sunset).
// -----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Reads the Phase H canonical secret-catalog manifest (task 084,
/// scripts/canonical-secret-catalog/manifest.yaml) from an embedded resource.
/// Production replacement for the interim <see cref="StaticKvSecretManifest"/>.
/// </summary>
public sealed class FileKvSecretManifest : IKvSecretManifest
{
    /// <summary>
    /// Embedded resource logical name — set by the &lt;Link&gt; in
    /// Sprk.Provisioning.ControlPlane.Core.csproj's &lt;EmbeddedResource&gt; item
    /// (the manifest.yaml SOURCE file lives outside this project, at
    /// scripts/canonical-secret-catalog/manifest.yaml, per task 084's
    /// single-source-of-truth contract).
    /// </summary>
    internal const string EmbeddedResourceName =
        "Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation.CanonicalManifest.manifest.yaml";

    /// <summary>
    /// Row A38a — the three credential slots omitted from SERVED entries on
    /// secret-free environments (auth-v4 §10.1 Δ1/Δ2; §9.1 OMIT-is-the-signal).
    /// Shared with H4/H4-shared, which union these into the task-126 FR-39
    /// <see cref="KvSecretWriteRequest.OmitCanonicalNames"/> seam as
    /// defense-in-depth (covers an emergency <see cref="StaticKvSecretManifest"/>
    /// DI-revert, which does not filter). MUST NOT contain
    /// <c>Dataverse-ClientSecret</c> (Q3 Path A rollback copy — §6.5 record
    /// 2026-08-25, sunset 2026-11-23).
    /// </summary>
    public static readonly IReadOnlySet<string> SecretFreeIdentityOmitTargets = new HashSet<string>(StringComparer.Ordinal)
    {
        "BFF-API-ClientSecret",
        "ServiceBus-ConnectionString",
        "AiSearch--AdminKey",
    };

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ILogger<FileKvSecretManifest> _logger;
    private readonly KvSecretsPopulationOptions _options;
    private readonly Lazy<KvSecretManifestReadResult> _cached;

    /// <summary>
    /// Constructs the reader. Parsing happens once, lazily, on first
    /// <see cref="ReadAsync"/> call (Singleton lifetime — see
    /// IKvSecretManifest.cs's thread-safety contract). Options are read at
    /// parse time: <see cref="KvSecretsPopulationOptions.RequireSecretFreeIdentity"/>
    /// drives the A38a served-entry filter; changing it requires a redeploy
    /// (same reload semantics as the manifest itself).
    /// </summary>
    public FileKvSecretManifest(
        ILogger<FileKvSecretManifest> logger,
        IOptions<KvSecretsPopulationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        _options = options.Value;
        _cached = new Lazy<KvSecretManifestReadResult>(LoadFromEmbeddedResource, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc/>
    public Task<KvSecretManifestReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_cached.Value);
    }

    private KvSecretManifestReadResult LoadFromEmbeddedResource()
    {
        string yaml;
        try
        {
            using var stream = typeof(FileKvSecretManifest).Assembly.GetManifestResourceStream(EmbeddedResourceName);
            if (stream is null)
            {
                var diagnostic =
                    $"Embedded resource '{EmbeddedResourceName}' not found in " +
                    $"{typeof(FileKvSecretManifest).Assembly.GetName().Name}. Verify the .csproj " +
                    "<EmbeddedResource> item for scripts/canonical-secret-catalog/manifest.yaml is intact.";
                _logger.LogError("H4 FileKvSecretManifest: {Diagnostic}", diagnostic);
                return new KvSecretManifestReadResult.Failure(diagnostic);
            }
            using var reader = new StreamReader(stream);
            yaml = reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            var diagnostic = $"Failed to read embedded manifest resource: {ex.GetType().Name}: {ex.Message}";
            _logger.LogError(ex, "H4 FileKvSecretManifest: {Diagnostic}", diagnostic);
            return new KvSecretManifestReadResult.Failure(diagnostic);
        }

        return ParseYaml(yaml);
    }

    /// <summary>
    /// Test seam (A38a): parses a caller-supplied yaml document through the
    /// EXACT production pipeline (BINDING invariant check + entry emission +
    /// A38a served-entry filter + sort) so the invariant's refusal branches
    /// and the filter's downstream-of-invariant ordering are unit-testable
    /// without mutating the embedded canonical manifest.
    /// </summary>
    internal KvSecretManifestReadResult ParseYamlForTest(string yaml) => ParseYaml(yaml);

    private KvSecretManifestReadResult ParseYaml(string yaml)
    {
        ManifestYamlDocument document;
        try
        {
            document = Deserializer.Deserialize<ManifestYamlDocument>(yaml)
                ?? throw new InvalidOperationException("Deserialized manifest document was null.");
        }
        catch (Exception ex)
        {
            var diagnostic = $"Failed to parse manifest.yaml: {ex.GetType().Name}: {ex.Message}";
            _logger.LogError(ex, "H4 FileKvSecretManifest: {Diagnostic}", diagnostic);
            return new KvSecretManifestReadResult.Failure(diagnostic);
        }

        if (document.Secrets is null || document.Secrets.Count == 0)
        {
            const string diagnostic = "manifest.yaml parsed but contains zero entries under 'secrets:'.";
            _logger.LogError("H4 FileKvSecretManifest: {Diagnostic}", diagnostic);
            return new KvSecretManifestReadResult.Failure(diagnostic);
        }

        // BINDING invariant (ported from Invoke-CatalogGenerator.ps1's own
        // startup guard) — refuse to serve entries if either never-delete
        // secret is missing or has never_delete=false.
        foreach (var required in new[] { "Dataverse-ClientSecret", "BFF-API-ClientSecret" })
        {
            var match = document.Secrets.FirstOrDefault(s =>
                string.Equals(s.CanonicalName, required, StringComparison.Ordinal));
            if (match is null || !match.NeverDelete)
            {
                var diagnostic =
                    $"BINDING never-delete invariant violated: manifest.yaml entry '{required}' is " +
                    (match is null ? "MISSING" : "present but never_delete=false") +
                    ". spec.md MUST rule + r3 handoff — refusing to serve ANY entries.";
                _logger.LogError("H4 FileKvSecretManifest: {Diagnostic}", diagnostic);
                return new KvSecretManifestReadResult.Failure(diagnostic);
            }
        }

        var entries = new List<KvSecretEntry>(document.Secrets.Count);
        foreach (var secret in document.Secrets)
        {
            if (string.IsNullOrWhiteSpace(secret.CanonicalName))
            {
                var diagnostic = "manifest.yaml contains a 'secrets:' entry with an empty canonical_name.";
                _logger.LogError("H4 FileKvSecretManifest: {Diagnostic}", diagnostic);
                return new KvSecretManifestReadResult.Failure(diagnostic);
            }

            if (!TryMapValueSource(secret.ValueSource, out var valueSource))
            {
                var diagnostic =
                    $"manifest.yaml entry '{secret.CanonicalName}' has unrecognized value_source " +
                    $"'{secret.ValueSource}' (expected one of: from-existing-kv, from-bicep-output, " +
                    "from-run-parameter, generated, from-shared-service).";
                _logger.LogError("H4 FileKvSecretManifest: {Diagnostic}", diagnostic);
                return new KvSecretManifestReadResult.Failure(diagnostic);
            }

            // Task 200: enforce parser-side that from-shared-service entries
            // carry a non-empty service_ref (mirrors the PowerShell generator's
            // conditional-required check in Test-ManifestShape). Without a
            // service_ref the downstream H4-shared handler has no source to
            // extract from, so the entry is malformed — fail loud rather than
            // silently swallow.
            if (valueSource == KvSecretValueSource.FromSharedService
                && string.IsNullOrWhiteSpace(secret.ServiceRef))
            {
                var diagnostic =
                    $"manifest.yaml entry '{secret.CanonicalName}' has value_source=from-shared-service " +
                    "but missing/empty service_ref. service_ref is CONDITIONALLY required for this " +
                    "value_source (format '<type>:<az-resource-name>', e.g. 'search:sprksharedprod-search').";
                _logger.LogError("H4 FileKvSecretManifest: {Diagnostic}", diagnostic);
                return new KvSecretManifestReadResult.Failure(diagnostic);
            }

            // Operation is always Upsert — manifest.yaml never declares a
            // per-entry operation field; Delete ops are a manual, pre-checked
            // alias-collapse action (task 085 pattern), never generator-emitted.
            entries.Add(new KvSecretEntry(
                secret.CanonicalName,
                KvSecretOperation.Upsert,
                valueSource,
                ServiceRef: valueSource == KvSecretValueSource.FromSharedService ? secret.ServiceRef : null));
        }

        // A38a served-entry filter (task 205a — auth-v4 §9.1 OMIT-is-the-signal).
        // Runs DOWNSTREAM of the BINDING never-delete invariant above (which
        // validated the RAW yaml document — BFF-API-ClientSecret's yaml row
        // stays present + never_delete=true; only the SERVED list shrinks).
        // Q3 Path A rollback (SecretFreeIdentityRollback) re-includes the
        // three targets; Dataverse-ClientSecret is never in the target set.
        if (_options.RequireSecretFreeIdentity && !_options.SecretFreeIdentityRollback)
        {
            var beforeCount = entries.Count;
            var removedNames = entries
                .Where(e => SecretFreeIdentityOmitTargets.Contains(e.CanonicalName))
                .Select(e => e.CanonicalName)
                .ToList();
            entries.RemoveAll(e => SecretFreeIdentityOmitTargets.Contains(e.CanonicalName));
            _logger.LogInformation(
                "H4 FileKvSecretManifest: A38a secret-free served-entry filter active " +
                "(RequireSecretFreeIdentity=true) — serving {After} of {Before} entries; omitted: {Omitted}. " +
                "manifest.yaml rows unchanged; §9.1 OMIT-is-the-signal (never sentinel).",
                entries.Count, beforeCount,
                removedNames.Count > 0 ? string.Join(", ", removedNames) : "(none present in manifest)");
        }

        // Determinism contract parity with the PowerShell generator: sort
        // alphabetically by canonical_name (ordinal, matching manifest.yaml's
        // own documented determinism contract).
        entries.Sort((a, b) => string.CompareOrdinal(a.CanonicalName, b.CanonicalName));

        _logger.LogInformation(
            "H4 FileKvSecretManifest: loaded {Count} canonical entries from manifest.yaml (task 084 Phase H manifest)",
            entries.Count);
        return new KvSecretManifestReadResult.Success(entries);
    }

    private static bool TryMapValueSource(string? raw, out KvSecretValueSource valueSource)
    {
        switch (raw)
        {
            case "from-existing-kv":
                valueSource = KvSecretValueSource.FromExistingKvSecret;
                return true;
            case "from-bicep-output":
                valueSource = KvSecretValueSource.FromBicepOutput;
                return true;
            case "from-run-parameter":
                valueSource = KvSecretValueSource.FromRunParameters;
                return true;
            case "generated":
                valueSource = KvSecretValueSource.Generated;
                return true;
            case "from-shared-service":
                // Task 200: shared-tier secrets extracted by H4-shared from
                // source Azure services (Search / CognitiveServices / Service
                // Bus / Storage / Redis).
                valueSource = KvSecretValueSource.FromSharedService;
                return true;
            default:
                valueSource = default;
                return false;
        }
    }

    /// <summary>Minimal YAML deserialization shape — only the fields this reader consumes.</summary>
    private sealed class ManifestYamlDocument
    {
        public List<ManifestYamlSecret>? Secrets { get; set; }
    }

    /// <summary>One 'secrets:' list entry. Property names map via UnderscoredNamingConvention (canonical_name -> CanonicalName, etc.).</summary>
    private sealed class ManifestYamlSecret
    {
        public string? CanonicalName { get; set; }
        public bool NeverDelete { get; set; }
        public string? ValueSource { get; set; }

        /// <summary>
        /// Task 200 addition. CONDITIONALLY required (populated) only when
        /// <see cref="ValueSource"/> == <c>from-shared-service</c>. Format
        /// <c>&lt;type&gt;:&lt;az-resource-name&gt;</c>. Absent for every other
        /// value_source — the deserializer leaves this null, which the loop
        /// above enforces explicitly.
        /// </summary>
        public string? ServiceRef { get; set; }
    }
}
