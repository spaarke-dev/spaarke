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
    string? NameColumn = null)
{
    /// <summary>
    /// Lookup columns of a starter-pack table (data-setup plan D5): each holds the id of a row of an EARLIER pack table. They are
    /// bound when the row is created and never read back for drift, so they are not part of <see cref="Columns"/>.
    /// </summary>
    public IReadOnlyList<ReferenceLookup> Lookups { get; init; } = [];

    /// <summary>
    /// The lookup column (one of <see cref="Lookups"/>) that scopes the code and name match: a billing code is unique within its code
    /// set, not across them (<c>sprk_billingcode_codeset_code_key</c>), so a code counts as present only inside the same set. Null for
    /// a table whose codes and names are unique across the whole table.
    /// </summary>
    public string? NaturalKeyScope { get; init; }
}

/// <summary>A lookup column: its logical name, the Web API navigation property that binds it, and the pack table it points into.</summary>
internal sealed record ReferenceLookup(string Column, string NavigationProperty, string TargetTable, string TargetEntitySet);

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
    /// <summary>The row's lookup values (column → pack id of the row it points at); a lookup the row does not set is absent.</summary>
    public IReadOnlyDictionary<string, Guid> Lookups { get; init; } = new Dictionary<string, Guid>();

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
        // ISS-045: the 17 grid configurations product code hard-codes by dev row id (see Export-ReferenceData.ps1).
        "sprk_gridconfiguration",
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
            ["sprk_gridconfiguration"] = "sprk_name",
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
    /// <para>
    /// ORDER IS THE SEED ORDER, and a lookup may only point at an EARLIER table (checked at load, <see cref="RequireLookupTargets"/>):
    /// the UTBMS code sets (data-setup plan D5) come first, then their codes (each bound to its set), then the matter types and
    /// practice areas that name a default code set.
    /// </para>
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PackTableNames =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["legal-standard"] =
            [
                "sprk_billingcodeset",
                "sprk_billingcode",
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

        var table = root.GetProperty("table").GetString()!;
        var entitySet = root.GetProperty("entitySet").GetString()!;

        // Lookup columns (data-setup plan D5): starter-pack files only. A row names the pack id of the row it points at.
        var lookups = new List<ReferenceLookup>();
        if (root.TryGetProperty("lookups", out var lookupsElement))
        {
            foreach (var lookup in lookupsElement.EnumerateArray())
            {
                lookups.Add(new ReferenceLookup(
                    lookup.GetProperty("column").GetString()!,
                    lookup.GetProperty("navigationProperty").GetString()!,
                    lookup.GetProperty("targetTable").GetString()!,
                    lookup.GetProperty("targetEntitySet").GetString()!));
            }

            if (lookups.Any(l => columns.Contains(l.Column, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException($"Reference-data file for '{table}': a lookup column must not also be listed in \"columns\" (lookups are bound at create, never compared).");
            }

            if (!root.TryGetProperty("match", out _))
            {
                throw new InvalidOperationException($"Reference-data file for '{table}' declares \"lookups\" but no \"match\"; lookups are supported in starter-pack files only.");
            }
        }

        var rows = new List<ReferenceRow>();
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var id = Guid.Parse(row.GetProperty(primaryKey).GetString()!, CultureInfo.InvariantCulture);
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var column in columns)
            {
                values[column] = row.GetProperty(column).Clone();
            }

            var lookupIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var lookup in lookups)
            {
                if (row.TryGetProperty(lookup.Column, out var target) && target.ValueKind == JsonValueKind.String)
                {
                    lookupIds[lookup.Column] = Guid.Parse(target.GetString()!, CultureInfo.InvariantCulture);
                }
            }

            rows.Add(new ReferenceRow(id, values) { Lookups = lookupIds });
        }

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

            // "naturalKeyScope": the lookup that scopes the code/name match (a billing code is unique within its code set only).
            var scope = root.TryGetProperty("naturalKeyScope", out var scopeElement) ? scopeElement.GetString() : null;
            if (scope is not null && !lookups.Any(l => string.Equals(l.Column, scope, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Reference-data file for '{table}': \"naturalKeyScope\" '{scope}' must be one of its \"lookups\".");
            }

            return new ReferenceTable(table, entitySet, primaryKey, columns, rows, code, ReferenceMatch.NaturalKey, nameColumn)
            {
                Lookups = lookups,
                NaturalKeyScope = scope,
            };
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

            RequireLookupTargets(pack, tables);
            packs[pack] = tables;
        }

        return packs;
    }

    /// <summary>
    /// Every lookup of a pack table must point at a row of an EARLIER table of the same pack (so the target is seeded, and its real id
    /// known, before the row that names it), and a table that scopes its match by a lookup must set it on every row. Throws
    /// <see cref="InvalidOperationException"/> (a build defect, not a runtime input).
    /// </summary>
    internal static void RequireLookupTargets(string pack, IReadOnlyList<ReferenceTable> tables)
    {
        for (var i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            foreach (var lookup in table.Lookups)
            {
                var target = tables.Take(i).FirstOrDefault(t => string.Equals(t.Table, lookup.TargetTable, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException(
                        $"Starter pack '{pack}' table '{table.Table}': lookup '{lookup.Column}' points at '{lookup.TargetTable}', which is not an EARLIER table of the pack " +
                        "(a lookup target is seeded first, or its id is not yet known).");
                if (!string.Equals(target.EntitySet, lookup.TargetEntitySet, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Starter pack '{pack}' table '{table.Table}': lookup '{lookup.Column}' names entity set '{lookup.TargetEntitySet}' but '{target.Table}' is '{target.EntitySet}'.");
                }

                var targetIds = target.Rows.Select(r => r.Id).ToHashSet();
                foreach (var row in table.Rows)
                {
                    if (row.Lookups.TryGetValue(lookup.Column, out var id) && !targetIds.Contains(id))
                    {
                        throw new InvalidOperationException(
                            $"Starter pack '{pack}' table '{table.Table}' row '{row.DisplayName}': lookup '{lookup.Column}' {id} is not a row of '{target.Table}'.");
                    }
                }
            }

            if (table.NaturalKeyScope is { } scope)
            {
                var unscoped = table.Rows.FirstOrDefault(r => !r.Lookups.ContainsKey(scope));
                if (unscoped is not null)
                {
                    throw new InvalidOperationException(
                        $"Starter pack '{pack}' table '{table.Table}' row '{unscoped.Id}': the match scope '{scope}' must be set on every row.");
                }
            }
        }
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
