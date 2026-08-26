// -----------------------------------------------------------------------------
// YamlSeedManifestEngine.cs
//
// Task 150 (Wave G-5 Batch G-5A) — YamlDotNet port of task-069's
// scripts/seed-data/manifest.yaml PARSE + TOPOLOGICAL-SORT steps (the
// PowerShell orchestrator's Read-ManifestYaml + Get-TopologicalOrder
// functions, Invoke-SeedManifest.ps1:168-316). Replaces the retired second
// PowerShell YAML-parsing module dependency (DS-1b matrix-correction
// finding — see H12aAiSeedChainHandler.cs file header) with YamlDotNet
// 18.1.0 (already a project dependency — task 126's FileKvSecretManifest.cs
// is this engine's direct idiom precedent: embedded-resource read +
// DeserializerBuilder + IgnoreUnmatchedProperties()).
//
// SCOPE:
//   This engine owns manifest STRUCTURE (parse + dependency-order
//   resolution) only. It does NOT perform retired-artifact enforcement
//   (that stays FileSeedManifestReader's line-oriented defense-in-depth
//   scan, run by the HANDLER before the runner is ever invoked — task 150
//   does not touch that file, per the POML's declared file-modify scope)
//   and does NOT perform any Dataverse write (that is
//   <see cref="DataverseWebApiSeedWriter"/>'s job, which calls this engine
//   to learn WHAT to seed and in WHAT ORDER).
//
// NAMING CONVENTION:
//   manifest.yaml keys are camelCase (schemaVersion, authoritativeSource,
//   dependsOn, deployerOwnedBy, ...) — CamelCaseNamingConvention, NOT the
//   UnderscoredNamingConvention FileKvSecretManifest.cs uses for its
//   snake_case canonical-secret-catalog manifest. Same DeserializerBuilder
//   shape otherwise (IgnoreUnmatchedProperties() — this engine only maps
//   the fields <see cref="DataverseWebApiSeedWriter"/> consumes; manifest.yaml
//   carries richer governance/traceability fields — driftMatrixRef, tags,
//   notes, orchestration, traceability — that no C# consumer needs today,
//   parity with FileKvSecretManifest.cs's own "only what's needed" note).
//
// YAML-SHAPE VERIFICATION (escalation-trigger check per this task's POML):
//   manifest.yaml was read in full before authoring this engine. It uses
//   only standard YAML: block mappings/sequences, folded block scalars
//   (`>-` for notes/description fields), comments, and explicit nulls
//   (authoritativeSource: null, deployer: null) — no anchors, aliases,
//   custom tags, or extensions specific to the retired PowerShell YAML
//   module. YamlDotNet's default deserializer handles every construct in
//   the file; the POML's escalation trigger does NOT fire.
// -----------------------------------------------------------------------------

using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <summary>
/// Parses <c>scripts/seed-data/manifest.yaml</c> (embedded resource) into a
/// strongly-typed <see cref="SeedManifestDocument"/> and computes the
/// dependency-topological seed order — the YamlDotNet + Kahn's-algorithm
/// port of Invoke-SeedManifest.ps1's Read-ManifestYaml + Get-TopologicalOrder.
/// </summary>
public static class YamlSeedManifestEngine
{
    /// <summary>
    /// Embedded resource logical name — set by the &lt;Link&gt; in
    /// Sprk.Provisioning.ControlPlane.Core.csproj's &lt;EmbeddedResource&gt;
    /// item (manifest.yaml's canonical location is scripts/seed-data/ at the
    /// repo root, per task-069's own file header single-source-of-truth
    /// contract — not duplicated into this project's folder).
    /// </summary>
    internal const string ManifestResourceName =
        "Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain.SeedContent.manifest.yaml";

    private const int SupportedSchemaVersion = 1;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Reads + parses manifest.yaml from this assembly's embedded resources.
    /// Throws <see cref="InvalidOperationException"/> with a diagnostic
    /// message on any read/parse/schema failure — parity with
    /// Read-ManifestYaml's fail-fast `throw` behavior (NFR-05 — deployment
    /// errors surface loudly, never silently).
    /// </summary>
    public static SeedManifestDocument ParseFromEmbeddedResource()
        => Parse(ReadEmbeddedResourceText(typeof(YamlSeedManifestEngine).Assembly, ManifestResourceName));

    /// <summary>
    /// Parses raw YAML text into a <see cref="SeedManifestDocument"/>.
    /// Public + pure (no I/O) so unit tests can exercise parse/validation
    /// edge cases without an embedded-resource round-trip.
    /// </summary>
    public static SeedManifestDocument Parse(string yamlText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlText);

        SeedManifestYaml? raw;
        try
        {
            raw = Deserializer.Deserialize<SeedManifestYaml>(yamlText);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Failed to parse seed manifest YAML: {ex.GetType().Name}: {ex.Message}", ex);
        }

        if (raw is null)
        {
            throw new InvalidOperationException("Deserialized seed manifest document was null.");
        }

        // Parity with Read-ManifestYaml's minimal schema validation.
        if (raw.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported manifest schemaVersion: {raw.SchemaVersion} (this engine understands version {SupportedSchemaVersion}).");
        }
        if (raw.Artifacts is null || raw.Artifacts.Count == 0)
        {
            throw new InvalidOperationException("Manifest missing required field: artifacts (must be a non-empty list).");
        }

        var artifacts = new List<SeedArtifact>(raw.Artifacts.Count);
        foreach (var a in raw.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(a.Id))
            {
                throw new InvalidOperationException("Manifest contains an artifact with a missing/empty 'id' field.");
            }

            SeedDeployer? deployer = a.Deployer is null
                ? null
                : new SeedDeployer(a.Deployer.Script, a.Deployer.IdempotencyMode, a.Deployer.IdempotencyKey);

            artifacts.Add(new SeedArtifact(
                Id: a.Id,
                Type: a.Type ?? string.Empty,
                AuthoritativeSource: a.AuthoritativeSource,
                Deployer: deployer,
                DeployerOwnedBy: a.DeployerOwnedBy,
                DependsOn: a.DependsOn ?? new List<string>()));
        }

        var retired = (raw.RetiredArtifacts ?? new List<SeedRetiredArtifactYaml>())
            .Select(r => new SeedRetiredArtifact(r.Id, r.Path, r.Name))
            .ToList();

        return new SeedManifestDocument(
            SchemaVersion: raw.SchemaVersion,
            ManifestName: raw.ManifestName ?? string.Empty,
            Artifacts: artifacts,
            RetiredArtifacts: retired);
    }

    /// <summary>
    /// Kahn's-algorithm topological sort over <paramref name="artifacts"/>'
    /// <c>dependsOn</c> edges — exact port of Get-TopologicalOrder
    /// (Invoke-SeedManifest.ps1:257-316). Ties (multiple zero-indegree nodes
    /// ready at once) resolve in manifest declaration order for deterministic,
    /// stable output — same FIFO-queue behavior as the PS implementation.
    /// Throws <see cref="InvalidOperationException"/> with a diagnostic on an
    /// unknown dependency OR a cycle (parity with the POML acceptance
    /// criterion the PS script itself enforces: "missing dependency causes a
    /// clear diagnostic, not silent success").
    /// </summary>
    public static IReadOnlyList<string> ComputeTopologicalOrder(IReadOnlyList<SeedArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);

        var ids = artifacts.Select(a => a.Id).ToList();
        var idSet = new HashSet<string>(ids, StringComparer.Ordinal);

        foreach (var artifact in artifacts)
        {
            foreach (var dep in artifact.DependsOn)
            {
                if (!idSet.Contains(dep))
                {
                    throw new InvalidOperationException(
                        $"Artifact '{artifact.Id}' declares dependency on unknown artifact '{dep}' — cannot resolve " +
                        $"seed order. Known artifacts: {string.Join(", ", ids)}");
                }
            }
        }

        var indegree = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var adjacency = ids.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var artifact in artifacts)
        {
            foreach (var dep in artifact.DependsOn)
            {
                // dep -> artifact.Id (artifact depends on dep, so dep is processed first).
                adjacency[dep].Add(artifact.Id);
                indegree[artifact.Id]++;
            }
        }

        var ordered = new List<string>(ids.Count);
        var ready = new Queue<string>();
        foreach (var id in ids)
        {
            if (indegree[id] == 0)
            {
                ready.Enqueue(id);
            }
        }

        while (ready.Count > 0)
        {
            var current = ready.Dequeue();
            ordered.Add(current);
            foreach (var next in adjacency[current])
            {
                indegree[next]--;
                if (indegree[next] == 0)
                {
                    ready.Enqueue(next);
                }
            }
        }

        if (ordered.Count != ids.Count)
        {
            var stuck = ids.Where(id => !ordered.Contains(id)).ToList();
            throw new InvalidOperationException(
                $"Cyclic dependency detected in manifest — artifacts stuck in cycle: {string.Join(", ", stuck)}");
        }

        return ordered;
    }

    internal static string ReadEmbeddedResourceText(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found in {assembly.GetName().Name}. Verify the .csproj " +
                "<EmbeddedResource> item for scripts/seed-data/ content is intact.");
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ---- YamlDotNet deserialization POCOs (mutable, camelCase-mapped) ----

    private sealed class SeedManifestYaml
    {
        public int SchemaVersion { get; set; }
        public string? ManifestName { get; set; }
        public List<SeedArtifactYaml>? Artifacts { get; set; }
        public List<SeedRetiredArtifactYaml>? RetiredArtifacts { get; set; }
    }

    private sealed class SeedArtifactYaml
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? AuthoritativeSource { get; set; }
        public SeedDeployerYaml? Deployer { get; set; }
        public string? DeployerOwnedBy { get; set; }
        public List<string>? DependsOn { get; set; }
    }

    private sealed class SeedDeployerYaml
    {
        public string? Script { get; set; }
        public string? IdempotencyMode { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    private sealed class SeedRetiredArtifactYaml
    {
        public string? Id { get; set; }
        public string? Path { get; set; }
        public string? Name { get; set; }
    }
}

/// <summary>Parsed seed manifest — the normalized shape <see cref="DataverseWebApiSeedWriter"/> consumes.</summary>
public sealed record SeedManifestDocument(
    int SchemaVersion,
    string ManifestName,
    IReadOnlyList<SeedArtifact> Artifacts,
    IReadOnlyList<SeedRetiredArtifact> RetiredArtifacts);

/// <summary>One <c>artifacts:</c> entry.</summary>
public sealed record SeedArtifact(
    string Id,
    string Type,
    string? AuthoritativeSource,
    SeedDeployer? Deployer,
    string? DeployerOwnedBy,
    IReadOnlyList<string> DependsOn);

/// <summary>An artifact's <c>deployer:</c> block (null for deployerOwnedBy / placeholder artifacts).</summary>
public sealed record SeedDeployer(
    string? Script,
    string? IdempotencyMode,
    string? IdempotencyKey);

/// <summary>One <c>retiredArtifacts:</c> entry (governance-only — ADR-039 exclusion list).</summary>
public sealed record SeedRetiredArtifact(string? Id, string? Path, string? Name);
