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
    string? NaturalKey = null);

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

    private static readonly Lazy<IReadOnlyList<ReferenceTable>> Loaded = new(LoadEmbedded);

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
        var naturalKey = NaturalKeyColumns.TryGetValue(table, out var configured) ? configured : "sprk_name";
        return new ReferenceTable(
            table,
            root.GetProperty("entitySet").GetString()!,
            primaryKey,
            columns,
            rows,
            columns.Contains(naturalKey, StringComparer.Ordinal) ? naturalKey : null);
    }

    private static IReadOnlyList<ReferenceTable> LoadEmbedded()
    {
        var assembly = typeof(ReferenceDataSet).Assembly;
        var tables = new List<ReferenceTable>(TableNames.Count);
        foreach (var name in TableNames)
        {
            var resource = $"{ResourcePrefix}{name}.json";
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException(
                    $"Embedded reference-data resource '{resource}' is missing from the L2 assembly — restore its " +
                    "<EmbeddedResource> item in Sprk.Provisioning.ControlPlane.Core.csproj.");
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            tables.Add(Parse(text));
        }

        return tables;
    }
}
