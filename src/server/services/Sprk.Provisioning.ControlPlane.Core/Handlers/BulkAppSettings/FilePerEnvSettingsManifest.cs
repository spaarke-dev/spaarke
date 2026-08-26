// -----------------------------------------------------------------------------
// FilePerEnvSettingsManifest.cs
//
// Task 201 — production impl of IPerEnvSettingsManifest. Reads the SAME
// embedded manifest.yaml resource FileKvSecretManifest embeds (single source
// of truth per task 084 contract), but exposes only the `per_env_settings:`
// top-level list. Absent list → Success with 0 entries (v1 backwards-compat).
//
// PARSER (YamlDotNet), naming convention UnderscoredNamingConvention:
//   per_env_source     -> PerEnvSource (string, then TryParse to enum)
//   literal_value      -> LiteralValue
//   iOptionsModule     -> IOptionsModule
//   required           -> Required
//   notes              -> ignored (documentation-only)
//
// Determinism: entries returned in alphabetical-by-Key order (parity with
// generator sort at emit time).
// -----------------------------------------------------------------------------

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <summary>
/// Reads the <c>per_env_settings:</c> top-level list from the embedded
/// canonical secret-catalog manifest. Consumed by
/// <see cref="H4bBulkAppSettingsHandler"/>.
/// </summary>
public sealed class FilePerEnvSettingsManifest : IPerEnvSettingsManifest
{
    /// <summary>
    /// Embedded resource name — SAME logical name FileKvSecretManifest uses
    /// so both readers share the SINGLE source of truth (task 084 contract).
    /// </summary>
    internal const string EmbeddedResourceName =
        "Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation.CanonicalManifest.manifest.yaml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ILogger<FilePerEnvSettingsManifest> _logger;
    private readonly Lazy<PerEnvSettingsManifestReadResult> _cached;

    /// <summary>
    /// Constructs the reader. Parsing happens once, lazily, on first
    /// <see cref="ReadAsync"/> call (Singleton lifetime).
    /// </summary>
    public FilePerEnvSettingsManifest(ILogger<FilePerEnvSettingsManifest> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _cached = new Lazy<PerEnvSettingsManifestReadResult>(
            LoadFromEmbeddedResource, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc/>
    public Task<PerEnvSettingsManifestReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_cached.Value);
    }

    private PerEnvSettingsManifestReadResult LoadFromEmbeddedResource()
    {
        string yaml;
        try
        {
            using var stream = typeof(FilePerEnvSettingsManifest).Assembly
                .GetManifestResourceStream(EmbeddedResourceName);
            if (stream is null)
            {
                var diagnostic =
                    $"Embedded resource '{EmbeddedResourceName}' not found in " +
                    $"{typeof(FilePerEnvSettingsManifest).Assembly.GetName().Name}. Verify the " +
                    "Sprk.Provisioning.ControlPlane.Core.csproj <EmbeddedResource> item for " +
                    "scripts/canonical-secret-catalog/manifest.yaml is intact.";
                _logger.LogError("H4b FilePerEnvSettingsManifest: {Diagnostic}", diagnostic);
                return new PerEnvSettingsManifestReadResult.Failure(diagnostic);
            }
            using var reader = new StreamReader(stream);
            yaml = reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            var diagnostic = $"Failed to read embedded manifest resource: {ex.GetType().Name}: {ex.Message}";
            _logger.LogError(ex, "H4b FilePerEnvSettingsManifest: {Diagnostic}", diagnostic);
            return new PerEnvSettingsManifestReadResult.Failure(diagnostic);
        }

        ManifestYamlDocument document;
        try
        {
            document = Deserializer.Deserialize<ManifestYamlDocument>(yaml)
                ?? throw new InvalidOperationException("Deserialized manifest document was null.");
        }
        catch (Exception ex)
        {
            var diagnostic = $"Failed to parse manifest.yaml: {ex.GetType().Name}: {ex.Message}";
            _logger.LogError(ex, "H4b FilePerEnvSettingsManifest: {Diagnostic}", diagnostic);
            return new PerEnvSettingsManifestReadResult.Failure(diagnostic);
        }

        // Absent per_env_settings:  Success with 0 entries (v1 backwards-compat).
        if (document.PerEnvSettings is null || document.PerEnvSettings.Count == 0)
        {
            _logger.LogInformation(
                "H4b FilePerEnvSettingsManifest: manifest.yaml carries no per_env_settings; H4b run will be a no-op.");
            return new PerEnvSettingsManifestReadResult.Success(Array.Empty<PerEnvSettingEntry>());
        }

        var entries = new List<PerEnvSettingEntry>(document.PerEnvSettings.Count);
        foreach (var raw in document.PerEnvSettings)
        {
            if (string.IsNullOrWhiteSpace(raw.Key))
            {
                return new PerEnvSettingsManifestReadResult.Failure(
                    "manifest.yaml contains a per_env_settings entry with an empty key.");
            }

            if (!TryParsePerEnvSource(raw.PerEnvSource, out var kind, out var parameterKey))
            {
                return new PerEnvSettingsManifestReadResult.Failure(
                    $"manifest.yaml per_env_settings entry '{raw.Key}' has unrecognized per_env_source " +
                    $"'{raw.PerEnvSource}' (expected 'literal' OR 'from-{{handler}}-{{output|parameter}}:{{key}}').");
            }

            if (kind == PerEnvSettingSource.Literal && raw.LiteralValue is null)
            {
                return new PerEnvSettingsManifestReadResult.Failure(
                    $"manifest.yaml per_env_settings entry '{raw.Key}' has per_env_source=literal but " +
                    "missing literal_value.");
            }

            if (string.IsNullOrWhiteSpace(raw.IOptionsModule))
            {
                return new PerEnvSettingsManifestReadResult.Failure(
                    $"manifest.yaml per_env_settings entry '{raw.Key}' has empty iOptionsModule (documentation field).");
            }

            entries.Add(new PerEnvSettingEntry(
                Key: raw.Key,
                PerEnvSource: kind,
                LiteralValue: kind == PerEnvSettingSource.Literal ? raw.LiteralValue : null,
                ParameterKey: kind == PerEnvSettingSource.Literal ? null : parameterKey,
                Required: raw.Required,
                IOptionsModuleName: raw.IOptionsModule));
        }

        // Alphabetical sort by Key (parity with generator emit order).
        entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

        _logger.LogInformation(
            "H4b FilePerEnvSettingsManifest: loaded {Count} per_env_settings entries",
            entries.Count);

        return new PerEnvSettingsManifestReadResult.Success(entries);
    }

    /// <summary>
    /// Parses a manifest <c>per_env_source</c> string. Format:
    /// <c>literal</c> OR <c>from-{handler}-{output|parameter}:{key}</c>.
    /// Returns the enum kind and (for non-literals) the parameter key H4b
    /// looks up in <c>envelope.Parameters.NonSecret</c>.
    /// </summary>
    internal static bool TryParsePerEnvSource(string? raw, out PerEnvSettingSource kind, out string? parameterKey)
    {
        kind = default;
        parameterKey = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        if (raw == "literal")
        {
            kind = PerEnvSettingSource.Literal;
            return true;
        }

        if (!raw.StartsWith("from-", StringComparison.Ordinal)) return false;
        var colon = raw.IndexOf(':');
        if (colon < 0 || colon == raw.Length - 1) return false;

        // Prefix is 'from-{handler}-{output|parameter}' before the colon.
        var prefix = raw[..colon];
        var suffix = raw[(colon + 1)..];
        if (string.IsNullOrWhiteSpace(suffix)) return false;

        if (prefix.EndsWith("-output", StringComparison.Ordinal))
        {
            kind = PerEnvSettingSource.FromHandlerOutput;
            parameterKey = suffix;
            return true;
        }
        if (prefix.EndsWith("-parameter", StringComparison.Ordinal))
        {
            kind = PerEnvSettingSource.FromHandlerParameter;
            parameterKey = suffix;
            return true;
        }
        return false;
    }

    private sealed class ManifestYamlDocument
    {
        public List<PerEnvYamlEntry>? PerEnvSettings { get; set; }
    }

    private sealed class PerEnvYamlEntry
    {
        public string? Key { get; set; }
        public string? PerEnvSource { get; set; }
        public string? LiteralValue { get; set; }

        // BUG FIX (task 205c / punch row A39, discovered 2026-08-26): the
        // manifest spells this key "iOptionsModule" (camelCase, matching the
        // file header's documented schema) rather than the underscored
        // "i_options_module" UnderscoredNamingConvention would derive from
        // the property name by default. Without an explicit alias, EVERY
        // per_env_settings entry failed IOptionsModule binding silently, so
        // ReadAsync() against the REAL embedded manifest.yaml has returned
        // Failure ("has empty iOptionsModule") for every entry since task
        // 201 shipped this reader -- undetected because no prior test
        // exercised the real embedded resource through this reader (only
        // hand-rolled test fixtures, which set IOptionsModuleName directly).
        //
        // ApplyNamingConventions = false is REQUIRED alongside Alias: YamlDotNet
        // 18.1.0 runs the configured INamingConvention over the alias string
        // too (verified empirically -- Alias alone still produced
        // "i_options_module" and mismatched), not just the bare property
        // name. Without this flag the alias is silently neutered back to the
        // same mismatched underscored form the bug started with.
        //
        // Verified via FilePerEnvSettingsManifestTests (added alongside this
        // fix) exercising the real embedded manifest end-to-end.
        [YamlMember(Alias = "iOptionsModule", ApplyNamingConventions = false)]
        public string? IOptionsModule { get; set; }

        public bool Required { get; set; } = true;
        public string? Notes { get; set; }
    }
}
