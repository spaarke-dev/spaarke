// -----------------------------------------------------------------------------
// AiContentSet.cs
//
// L56 / data-setup plan D3 / demo-environment plan W2: the AI content a stamp
// must receive with FIXED primary keys — tools, skills, knowledge, actions (with
// their JPS input/output schemas), playbooks, nodes, their N:N links, playbook
// canvases and routing rows (sprk_playbookconsumer, ADR-039's single routing
// surface).
//
// SOURCE OF TRUTH: scripts/seed-data/ai-content/*.json — exported from the
// authoring environment by Export-AiContent.ps1, reviewed in a PR, embedded in
// this assembly. Provisioning never reads dev.
//
// WHY FIXED KEYS: clients hard-code playbook ids (Document Profile, Summarize
// File), routing-row ids are Binding identities (chips, ledger keys — ADR-040),
// and the stable-id alternate key sprk_playbookid mirrors the playbook's own id.
//
// DEPENDENCY ORDER (Phases): types → tools → skills → knowledge → actions →
// playbooks → playbook N:N → nodes → node N:N → canvas → routing rows. Validate()
// refuses a set whose lookups, links or canvases point at a row the set does not
// carry, or at a table that is written later — before anything is written.
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <summary>How the seeder decides that a source row is already in the environment.</summary>
internal enum AiContentMatch
{
    /// <summary>
    /// Product content: the row's fixed primary key. A row found only by a match key (code, name, routing key) under another id
    /// is a COLLISION: not created, reported, and used for references so dependent rows still link.
    /// </summary>
    FixedId = 0,

    /// <summary>Type lookups: a row with the same name counts as present (expected, not reported); absent rows are created with the source id.</summary>
    NaturalKey = 1,
}

/// <summary>A lookup column carried by the set: written as <c>{NavigationProperty}@odata.bind</c> to the target table's row.</summary>
internal sealed record AiContentLookup(string Column, string NavigationProperty, string Target);

/// <summary>One source row: its primary key, scalar values (multi-line text already joined) and lookup target ids (null = empty).</summary>
internal sealed record AiContentRow(
    Guid Id,
    IReadOnlyDictionary<string, JsonElement> Values,
    IReadOnlyDictionary<string, Guid?> Lookups,
    string DisplayName);

/// <summary>One table of the set.</summary>
internal sealed record AiContentTable(
    string Table,
    string EntitySet,
    string PrimaryKey,
    AiContentMatch Match,
    IReadOnlyList<IReadOnlyList<string>> MatchKeys,
    IReadOnlyList<string> Columns,
    IReadOnlyList<AiContentLookup> Lookups,
    IReadOnlyList<AiContentRow> Rows);

/// <summary>One N:N relationship's links, associated from the source side.</summary>
internal sealed record AiContentAssociation(
    string Relationship,
    string Source,
    string NavigationProperty,
    string Target,
    IReadOnlyList<(Guid Source, Guid Target)> Pairs);

/// <summary>Playbook canvas layouts, written after the nodes (fill-if-empty).</summary>
internal sealed record AiContentCanvas(string Table, string Column, IReadOnlyList<(Guid Id, string Value)> Rows);

/// <summary>A step of the seed, in order.</summary>
internal abstract record AiContentPhase
{
    /// <summary>Create the rows of one table.</summary>
    internal sealed record Rows(string Table) : AiContentPhase;

    /// <summary>Link every N:N relationship whose source is this table.</summary>
    internal sealed record Links(string SourceTable) : AiContentPhase;

    /// <summary>Write the playbook canvases.</summary>
    internal sealed record Canvas : AiContentPhase;
}

/// <summary>The embedded AI content set, parsed and validated.</summary>
internal sealed class AiContentSet
{
    /// <summary>Embedded-resource prefix; the csproj links each file with <c>LogicalName = prefix + path with '/' as '.'</c>.</summary>
    internal const string ResourcePrefix = "Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain.AiContent.";

    internal const string PlaybookTable = "sprk_analysisplaybook";
    internal const string NodeTable = "sprk_playbooknode";
    internal const string CanvasFile = "sprk_analysisplaybook.canvas.json";

    /// <summary>The N:N relationships the set carries (one file each under <c>associations/</c>).</summary>
    internal static readonly IReadOnlyList<string> RelationshipNames =
    [
        "sprk_analysisplaybook_action",
        "sprk_playbook_skill",
        "sprk_playbook_knowledge",
        "sprk_playbook_tool",
        "sprk_playbooknode_skill",
        "sprk_playbooknode_knowledge",
        "sprk_playbooknode_tool",
    ];

    /// <summary>
    /// The seed order (W2 / data-setup plan D3): every table before anything that points at it, links after both ends, canvases after
    /// the nodes they draw, routing rows last.
    /// </summary>
    internal static readonly IReadOnlyList<AiContentPhase> Phases =
    [
        new AiContentPhase.Rows("sprk_aitooltype"),
        new AiContentPhase.Rows("sprk_aiskilltype"),
        new AiContentPhase.Rows("sprk_aiknowledgetype"),
        new AiContentPhase.Rows("sprk_aioutputtype"),
        new AiContentPhase.Rows("sprk_analysistool"),
        new AiContentPhase.Rows("sprk_analysisskill"),
        new AiContentPhase.Rows("sprk_analysisknowledge"),
        new AiContentPhase.Rows("sprk_analysisaction"),
        new AiContentPhase.Rows(PlaybookTable),
        new AiContentPhase.Links(PlaybookTable),
        new AiContentPhase.Rows(NodeTable),
        new AiContentPhase.Links(NodeTable),
        new AiContentPhase.Canvas(),
        new AiContentPhase.Rows("sprk_playbookconsumer"),
    ];

    private static readonly Lazy<AiContentSet> Loaded = new(LoadEmbedded);

    internal AiContentSet(
        IReadOnlyList<AiContentPhase> phases,
        IReadOnlyDictionary<string, AiContentTable> tables,
        IReadOnlyList<AiContentAssociation> associations,
        AiContentCanvas? canvas,
        string contentHash)
    {
        Order = phases;
        Tables = tables;
        Associations = associations;
        Canvas = canvas;
        ContentHash = contentHash;
    }

    /// <summary>The order this set is written in.</summary>
    internal IReadOnlyList<AiContentPhase> Order { get; }

    /// <summary>Tables by logical name.</summary>
    internal IReadOnlyDictionary<string, AiContentTable> Tables { get; }

    /// <summary>The N:N links.</summary>
    internal IReadOnlyList<AiContentAssociation> Associations { get; }

    /// <summary>The playbook canvases (null when the set carries none).</summary>
    internal AiContentCanvas? Canvas { get; }

    /// <summary>SHA-256 (lower hex) over every embedded content file; part of H12a's idempotency key so a re-export re-seeds.</summary>
    internal string ContentHash { get; }

    /// <summary>The embedded set (throws <see cref="InvalidOperationException"/> when a file is missing or malformed — a build defect).</summary>
    internal static AiContentSet Embedded => Loaded.Value;

    /// <summary>
    /// Builds a set from file texts keyed by path relative to the content folder (<c>sprk_analysistool.json</c>,
    /// <c>associations/sprk_playbook_tool.json</c>, <c>sprk_analysisplaybook.canvas.json</c>). Exposed for tests.
    /// </summary>
    internal static AiContentSet FromFiles(IReadOnlyDictionary<string, string> files, IReadOnlyList<AiContentPhase>? phases = null)
    {
        phases ??= Phases;
        var tables = new Dictionary<string, AiContentTable>(StringComparer.Ordinal);
        foreach (var phase in phases.OfType<AiContentPhase.Rows>())
        {
            tables[phase.Table] = ParseTable(Require(files, $"{phase.Table}.json"));
        }

        var associations = files
            .Where(f => f.Key.StartsWith("associations/", StringComparison.Ordinal))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => ParseAssociation(f.Value))
            .ToList();
        var canvas = files.TryGetValue(CanvasFile, out var canvasText) ? ParseCanvas(canvasText) : null;

        using var sha = SHA256.Create();
        var hashInput = new StringBuilder();
        foreach (var (path, text) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            hashInput.Append(path).Append('\n').Append(text.Replace("\r\n", "\n", StringComparison.Ordinal)).Append('\u0000');
        }

        var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(hashInput.ToString()))).ToLowerInvariant();
        return new AiContentSet(phases, tables, associations, canvas, hash);
    }

    /// <summary>
    /// The missing dependencies of this set: a lookup, link or canvas that points at a row the set does not carry, or at a table
    /// written in a later phase (or never). Empty means the set can be written in order. Nothing is written when this is not empty.
    /// </summary>
    internal IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        var written = new HashSet<string>(StringComparer.Ordinal);
        var linkedSources = new HashSet<string>(StringComparer.Ordinal);
        var ids = Tables.ToDictionary(t => t.Key, t => t.Value.Rows.Select(r => r.Id).ToHashSet(), StringComparer.Ordinal);

        foreach (var phase in Order)
        {
            switch (phase)
            {
                case AiContentPhase.Rows rows:
                    var table = Tables[rows.Table];
                    foreach (var lookup in table.Lookups)
                    {
                        if (!written.Contains(lookup.Target))
                        {
                            problems.Add($"{table.Table}.{lookup.Column} points at {lookup.Target}, which is not written before {table.Table}.");
                            continue;
                        }

                        foreach (var row in table.Rows)
                        {
                            if (row.Lookups[lookup.Column] is { } target && !ids[lookup.Target].Contains(target))
                            {
                                problems.Add($"{table.Table}/{row.Id} '{row.DisplayName}' points at {lookup.Target}/{target} through " +
                                             $"{lookup.Column}, which the set does not carry.");
                            }
                        }
                    }

                    written.Add(rows.Table);
                    break;

                case AiContentPhase.Links links:
                    foreach (var association in Associations.Where(a => a.Source == links.SourceTable))
                    {
                        foreach (var end in new[] { association.Source, association.Target })
                        {
                            if (!written.Contains(end))
                            {
                                problems.Add($"{association.Relationship} links {end}, which is not written before the links.");
                            }
                        }

                        if (!ids.TryGetValue(association.Source, out var sourceIds) || !ids.TryGetValue(association.Target, out var targetIds))
                        {
                            continue;
                        }

                        foreach (var (source, target) in association.Pairs)
                        {
                            if (!sourceIds.Contains(source) || !targetIds.Contains(target))
                            {
                                problems.Add($"{association.Relationship} link {source} -> {target} names a row the set does not carry.");
                            }
                        }
                    }

                    linkedSources.Add(links.SourceTable);
                    break;

                case AiContentPhase.Canvas:
                    if (Canvas is null)
                    {
                        break;
                    }

                    if (!written.Contains(Canvas.Table) || !written.Contains(NodeTable) || !linkedSources.Contains(NodeTable))
                    {
                        problems.Add("Canvases are written before the playbooks, nodes and node links they draw.");
                        break;
                    }

                    foreach (var (id, _) in Canvas.Rows)
                    {
                        if (!ids[Canvas.Table].Contains(id))
                        {
                            problems.Add($"Canvas for {Canvas.Table}/{id} names a playbook the set does not carry.");
                        }
                    }

                    break;
            }
        }

        foreach (var association in Associations.Where(a => !linkedSources.Contains(a.Source)))
        {
            problems.Add($"{association.Relationship} has source {association.Source}, which no link phase writes.");
        }

        return problems;
    }

    // ---- parsing ---------------------------------------------------------------------------------------------------

    /// <summary>Parses one table file. Text arrays (multi-line values) are joined with <c>"\n"</c>.</summary>
    internal static AiContentTable ParseTable(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var table = root.GetProperty("table").GetString()!;
        var primaryKey = root.GetProperty("primaryKey").GetString()!;
        var nameColumn = root.TryGetProperty("nameColumn", out var nameElement) ? nameElement.GetString() : null;
        var match = root.GetProperty("match").GetString() switch
        {
            "fixedId" => AiContentMatch.FixedId,
            "naturalKey" => AiContentMatch.NaturalKey,
            var other => throw new InvalidOperationException($"AI content file for '{table}' has unknown \"match\" value '{other}'."),
        };
        var matchKeys = root.GetProperty("matchKeys").EnumerateArray()
            .Select(k => (IReadOnlyList<string>)k.EnumerateArray().Select(c => c.GetString()!).ToList())
            .ToList();
        var columns = root.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToList();
        var lookups = root.GetProperty("lookups").EnumerateArray()
            .Select(l => new AiContentLookup(
                l.GetProperty("column").GetString()!,
                l.GetProperty("navigationProperty").GetString()!,
                l.GetProperty("target").GetString()!))
            .ToList();

        foreach (var key in matchKeys.SelectMany(k => k))
        {
            if (!columns.Contains(key, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"AI content file for '{table}': match key column '{key}' is not one of its columns.");
            }
        }

        if (match == AiContentMatch.NaturalKey && matchKeys.Count == 0)
        {
            throw new InvalidOperationException($"AI content file for '{table}' matches by natural key but names no \"matchKeys\".");
        }

        var rows = new List<AiContentRow>();
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var id = Guid.Parse(row.GetProperty(primaryKey).GetString()!, CultureInfo.InvariantCulture);
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var column in columns)
            {
                values[column] = JoinLines(row.GetProperty(column));
            }

            var lookupValues = new Dictionary<string, Guid?>(StringComparer.Ordinal);
            foreach (var lookup in lookups)
            {
                var value = row.GetProperty(lookup.Column);
                lookupValues[lookup.Column] = value.ValueKind == JsonValueKind.Null
                    ? null
                    : Guid.Parse(value.GetString()!, CultureInfo.InvariantCulture);
            }

            var display = nameColumn is not null && values.TryGetValue(nameColumn, out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()!
                : id.ToString();
            rows.Add(new AiContentRow(id, values, lookupValues, display));
        }

        var duplicate = rows.GroupBy(r => r.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"AI content file for '{table}' lists id {duplicate.Key} more than once.");
        }

        return new AiContentTable(table, root.GetProperty("entitySet").GetString()!, primaryKey, match, matchKeys, columns, lookups, rows);
    }

    internal static AiContentAssociation ParseAssociation(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var pairs = root.GetProperty("pairs").EnumerateArray()
            .Select(p => (
                Guid.Parse(p.GetProperty("source").GetString()!, CultureInfo.InvariantCulture),
                Guid.Parse(p.GetProperty("target").GetString()!, CultureInfo.InvariantCulture)))
            .ToList();
        return new AiContentAssociation(
            root.GetProperty("relationship").GetString()!,
            root.GetProperty("source").GetString()!,
            root.GetProperty("navigationProperty").GetString()!,
            root.GetProperty("target").GetString()!,
            pairs);
    }

    internal static AiContentCanvas ParseCanvas(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var column = root.GetProperty("column").GetString()!;
        var table = root.GetProperty("table").GetString()!;
        var primaryKey = $"{table}id";
        var rows = root.GetProperty("rows").EnumerateArray()
            .Select(r => (
                Guid.Parse(r.GetProperty(primaryKey).GetString()!, CultureInfo.InvariantCulture),
                JoinLines(r.GetProperty(column)).GetString()!))
            .ToList();
        return new AiContentCanvas(table, column, rows);
    }

    /// <summary>An array of strings (the export's multi-line form) becomes one string joined with "\n"; anything else is cloned as is.</summary>
    private static JsonElement JoinLines(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return value.Clone();
        }

        var text = string.Join("\n", value.EnumerateArray().Select(line => line.GetString()));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(text));
        return doc.RootElement.Clone();
    }

    private static string Require(IReadOnlyDictionary<string, string> files, string path)
        => files.TryGetValue(path, out var text)
            ? text
            : throw new InvalidOperationException(
                $"AI content file '{path}' is missing — restore scripts/seed-data/ai-content/{path} and its <EmbeddedResource> item " +
                "in Sprk.Provisioning.ControlPlane.Core.csproj.");

    private static AiContentSet LoadEmbedded()
    {
        var assembly = typeof(AiContentSet).Assembly;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var relative = resource[ResourcePrefix.Length..];
            if (relative.StartsWith("associations.", StringComparison.Ordinal))
            {
                relative = "associations/" + relative["associations.".Length..];
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            files[relative] = reader.ReadToEnd();
        }

        foreach (var relationship in RelationshipNames)
        {
            Require(files, $"associations/{relationship}.json");
        }

        Require(files, CanvasFile);
        return FromFiles(files);
    }
}
