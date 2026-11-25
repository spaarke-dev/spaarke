// -----------------------------------------------------------------------------
// ReferenceDataSet.cs
//
// The Tier 1 product-configuration reference data that H12b seeds with FIXED
// primary keys (ISS-039): sprk_eventtype_ref, sprk_agreementtype,
// sprk_triagecategory.
//
// SOURCE OF TRUTH: scripts/seed-data/reference/<table>.json — exported from the
// authoring environment by Export-ReferenceData.ps1, reviewed in a PR, and
// embedded in this assembly (csproj <EmbeddedResource>, parity with the seed
// manifest). Provisioning never reads dev; it seeds from what git holds.
//
// WHY FIXED KEYS: product code hard-codes row ids of these tables (the "Task"
// event type in DailyBriefingCollector / PortfolioService / TaskActionCore, the
// eleven event-type ids of the Events page and the Event side pane). A stamp that
// created its own rows would get different ids and every one of those lookups
// would match nothing.
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// One reference table: its Web API entity set, primary-key column, seeded columns and rows. <c>NaturalKey</c> is the
/// column that identifies a row for a human (for event types and agreement types it is an alternate key Dataverse
/// enforces); the seeder uses it to spot a row that already exists under a DIFFERENT id.
/// </summary>
internal sealed record ReferenceTable(
    string Table,
    string EntitySet,
    string PrimaryKey,
    IReadOnlyList<string> Columns,
    IReadOnlyList<ReferenceRow> Rows,
    string? NaturalKey = null,
    ReferenceMatch Match = ReferenceMatch.FixedId,
    string? NameColumn = null);

/// <summary>How the seeder decides that a source row is already in the environment.</summary>
internal enum ReferenceMatch
{
    /// <summary>
    /// Tier 1 product configuration (the default): the row's fixed primary key. Product code references the id, so a row under
    /// another id does not satisfy it; drift, inactive rows and natural-key collisions are reported.
    /// </summary>
    FixedId = 0,

    /// <summary>
    /// Tier 2 starter taxonomy (data-setup plan D4): the customer owns the rows after seeding, so a row counts as present when it
    /// matches by id, by code (<see cref="ReferenceTable.NaturalKey"/>) or by name (<see cref="ReferenceTable.NameColumn"/>) —
    /// trimmed, case-insensitive; active or not. Present rows are never written; absent rows are created with the pack's id.
    /// </summary>
    NaturalKey = 1,
}

/// <summary>One source row: its primary key and its column values (null values are kept as JSON null).</summary>
internal sealed record ReferenceRow(Guid Id, IReadOnlyDictionary<string, JsonElement> Values)
{
    /// <summary>The row's display name for diagnostics (<c>sprk_name</c> when the table has one).</summary>
    public string DisplayName =>
        Values.TryGetValue("sprk_name", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()!
            : Id.ToString();
}

/// <summary>The embedded reference-data files, parsed once.</summary>
internal static class ReferenceDataSet
{
    /// <summary>Embedded-resource prefix; the csproj links each file with <c>LogicalName = prefix + file name</c>.</summary>
    internal const string ResourcePrefix = "Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.ReferenceData.";

    /// <summary>The tables, in seed order. Adding a table is: export file + csproj item + a name here.</summary>
    internal static readonly IReadOnlyList<string> TableNames =
    [
        "sprk_eventtype_ref",
        "sprk_agreementtype",
        "sprk_triagecategory",
    ];

    /// <summary>
    /// The natural-key column per table. <c>sprk_eventtypecode</c> and <c>sprk_key</c> are Dataverse alternate keys
    /// (SpaarkeMaster Entity.xml); <c>sprk_triagecategory</c> has none, so its display name stands in. A table not listed
    /// here falls back to <c>sprk_name</c> when it has that column.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> NaturalKeyColumns =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sprk_eventtype_ref"] = "sprk_eventtypecode",
            ["sprk_agreementtype"] = "sprk_key",
            ["sprk_triagecategory"] = "sprk_name",
        };

    /// <summary>
    /// Embedded-resource prefix of the starter taxonomy packs: <c>…ReferenceData.packs.{pack}.{table}.json</c>
    /// (<c>scripts/seed-data/reference/packs/{pack}/{table}.json</c>).
    /// </summary>
    internal const string PackResourcePrefix = ResourcePrefix + "packs.";

    /// <summary>
    /// The starter taxonomy packs and their tables (data-setup plan D4). Every key is an
    /// <c>IntakeParameterCatalog.AllowedReferenceDataPacks</c> value other than <c>none</c>, and the reverse
    /// (<c>ReferenceDataPackTests</c>). Adding a pack or a table is: files + csproj items + a name here.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PackTableNames =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["legal-standard"] =
            [
                "sprk_mattertype_ref",
                "sprk_mattersubtype_ref",
                "sprk_practicearea_ref",
                "sprk_projecttype_ref",
                "sprk_documenttype_ref",
                "sprk_contacttype_ref",
                "sprk_organizationtype_ref",
            ],
        };

    private static readonly Lazy<IReadOnlyList<ReferenceTable>> Loaded = new(LoadEmbedded);

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<ReferenceTable>>> LoadedPacks = new(LoadPacks);

    /// <summary>
    /// The tables of the named starter taxonomy pack; empty for <c>none</c>. An unknown name throws
    /// <see cref="InvalidOperationException"/> (the handler and POST /api/runs refuse it first).
    /// </summary>
    internal static IReadOnlyList<ReferenceTable> Pack(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.Equals(name, IntakeParameterCatalog.NoReferenceDataPack, StringComparison.Ordinal))
        {
            return [];
        }

        return LoadedPacks.Value.TryGetValue(name, out var tables)
            ? tables
            : throw new InvalidOperationException(
                $"Unknown reference-data pack '{name}'; packs are {string.Join(" | ", PackTableNames.Keys.Order(StringComparer.Ordinal))}.");
    }

    /// <summary>The parsed tables (throws <see cref="InvalidOperationException"/> if an embedded file is missing — a build defect).</summary>
    internal static IReadOnlyList<ReferenceTable> Tables => Loaded.Value;

    /// <summary>Parses one exported file. Exposed for tests.</summary>
    internal static ReferenceTable Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var primaryKey = root.GetProperty("primaryKey").GetString()!;
        var columns = root.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToList();

        var rows = new List<ReferenceRow>();
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var id = Guid.Parse(row.GetProperty(primaryKey).GetString()!, CultureInfo.InvariantCulture);
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var column in columns)
            {
                values[column] = row.GetProperty(column).Clone();
            }

            rows.Add(new ReferenceRow(id, values));
        }

        var table = root.GetProperty("table").GetString()!;
        var entitySet = root.GetProperty("entitySet").GetString()!;

        // Starter taxonomy files (data-setup plan D4) say so: "match": "naturalKey" with an optional "naturalKey" (the code
        // column; absent when the table has none) and a required "nameColumn". Tier 1 files carry none of these.
        if (root.TryGetProperty("match", out var matchElement))
        {
            var match = matchElement.GetString();
            if (!string.Equals(match, "naturalKey", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Reference-data file for '{table}' has unknown \"match\" value '{match}'; only \"naturalKey\" is supported.");
            }

            var code = root.TryGetProperty("naturalKey", out var codeElement) ? codeElement.GetString() : null;
            var nameColumn = root.TryGetProperty("nameColumn", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(nameColumn) || !columns.Contains(nameColumn, StringComparer.Ordinal)
                || (code is not null && !columns.Contains(code, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Reference-data file for '{table}' must name a \"nameColumn\" (and a \"naturalKey\" when it has a code) that is one of its columns.");
            }

            return new ReferenceTable(table, entitySet, primaryKey, columns, rows, code, ReferenceMatch.NaturalKey, nameColumn);
        }

        var naturalKey = NaturalKeyColumns.TryGetValue(table, out var configured) ? configured : "sprk_name";
        return new ReferenceTable(
            table,
            entitySet,
            primaryKey,
            columns,
            rows,
            columns.Contains(naturalKey, StringComparer.Ordinal) ? naturalKey : null);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ReferenceTable>> LoadPacks()
    {
        var packs = new Dictionary<string, IReadOnlyList<ReferenceTable>>(PackTableNames.Count, StringComparer.Ordinal);
        foreach (var (pack, names) in PackTableNames)
        {
            var tables = new List<ReferenceTable>(names.Count);
            foreach (var name in names)
            {
                tables.Add(RequireNaturalKeyTable(pack, Parse(ReadEmbedded($"{PackResourcePrefix}{pack}.{name}.json"))));
            }

            packs[pack] = tables;
        }

        return packs;
    }

    /// <summary>
    /// A starter-pack table must be Tier 2 (<see cref="ReferenceMatch.NaturalKey"/>) with a name column. A pack file that omits
    /// <c>"match"</c> would otherwise load as Tier 1 (fixed-id), with name matching silently off, and H12b would create duplicates of
    /// rows a customer already renamed or re-keyed. Throws <see cref="InvalidOperationException"/> (a build defect, not a runtime input).
    /// </summary>
    internal static ReferenceTable RequireNaturalKeyTable(string pack, ReferenceTable table)
    {
        if (table.Match != ReferenceMatch.NaturalKey || string.IsNullOrWhiteSpace(table.NameColumn))
        {
            throw new InvalidOperationException(
                $"Starter pack '{pack}' table '{table.Table}' must declare \"match\": \"naturalKey\" and a \"nameColumn\" " +
                "(a pack file without them would seed as fixed-id Tier 1 data with name matching off).");
        }

        return table;
    }

    private static string ReadEmbedded(string resource)
    {
        using var stream = typeof(ReferenceDataSet).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Embedded reference-data resource '{resource}' is missing from the L2 assembly — restore its " +
                "<EmbeddedResource> item in Sprk.Provisioning.ControlPlane.Core.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IReadOnlyList<ReferenceTable> LoadEmbedded()
    {
        var tables = new List<ReferenceTable>(TableNames.Count);
        foreach (var name in TableNames)
        {
            tables.Add(Parse(ReadEmbedded($"{ResourcePrefix}{name}.json")));
        }

        return tables;
    }
}
