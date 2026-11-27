using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Data-setup plan D4: every starter-taxonomy pack file (<c>scripts/seed-data/reference/packs/{pack}/{table}.json</c>) must be creatable
/// in a customer environment, judged statically against the SpaarkeMaster solution source.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> H12b POSTs these rows to every new stamp. A column the entity does not define, a required column left empty, or a
/// text longer than its column makes Dataverse refuse the create, and H12b then fails on every stamp that takes the default pack. A
/// duplicate code or name inside a pack would make the second row look "already present" and silently never be created. All of these
/// are decidable from the files and <c>Entity.xml</c>, so a bad edit fails in CI instead of in a customer run.</para>
/// <para><b>Enforced by:</b> this test (CI and the Stop hook). <b>Controls:</b> an unknown column, an empty required column, an
/// over-long text and a duplicate key are each reported (must fire); a valid file is not (must not fire).</para>
/// </remarks>
public sealed class ReferencePackContentTests
{
    private const string PacksDirectory = "scripts/seed-data/reference/packs";
    private const string EntitiesDirectory = "src/dataverse/solutions/SpaarkeMaster/Entities";

    private static string Abs(string repoRelative) => Path.Combine(SourceScan.RepoRoot, repoRelative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>One column of an entity as <c>Entity.xml</c> defines it.</summary>
    internal sealed record Column(string Name, bool Required, int? MaxLength);

    /// <summary>The attributes of an <c>Entity.xml</c>, keyed by lower-cased logical name.</summary>
    internal static Dictionary<string, Column> Columns(string entityXml)
    {
        var columns = new Dictionary<string, Column>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in XDocument.Parse(entityXml).Descendants("attribute"))
        {
            var name = attribute.Element("LogicalName")?.Value ?? attribute.Element("Name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var level = attribute.Element("RequiredLevel")?.Value;
            var required = string.Equals(level, "required", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(level, "systemrequired", StringComparison.OrdinalIgnoreCase);
            int? max = int.TryParse(attribute.Element("MaxLength")?.Value, out var length) ? length : null;
            columns[name.Trim()] = new Column(name.Trim(), required, max);
        }

        return columns;
    }

    /// <summary>Problems a pack file has against its entity; empty when it can be created as written.</summary>
    internal static IReadOnlyList<string> Problems(string packJson, string entityXml)
    {
        using var doc = JsonDocument.Parse(packJson);
        var root = doc.RootElement;
        var table = root.GetProperty("table").GetString()!;
        var primaryKey = root.GetProperty("primaryKey").GetString()!;
        var columnNames = root.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToList();
        // Lookup columns (data-setup plan D5) are bound at create like any column: they must exist on the entity, and a required one
        // (billing code -> code set) must be set on every row.
        var lookupNames = root.TryGetProperty("lookups", out var lookupsElement)
            ? lookupsElement.EnumerateArray().Select(l => l.GetProperty("column").GetString()!).ToList()
            : [];
        var defined = Columns(entityXml);
        var problems = new List<string>();

        foreach (var column in columnNames.Concat(lookupNames).Append(primaryKey).Where(c => !defined.ContainsKey(c)))
        {
            problems.Add($"{table}: column '{column}' is not an attribute of the entity.");
        }

        // A system-required or required column that the pack does not carry at all (statecode is system-required but defaulted).
        foreach (var column in defined.Values.Where(c => c.Required && c.Name is not ("statecode" or "ownerid" or "owningbusinessunit")
                                                         && !columnNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase)
                                                         && !lookupNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase)
                                                         && !string.Equals(c.Name, primaryKey, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add($"{table}: required column '{column.Name}' is not in the pack's columns.");
        }

        var natural = root.TryGetProperty("naturalKey", out var nk) ? nk.GetString() : null;
        var nameColumn = root.TryGetProperty("nameColumn", out var nc) ? nc.GetString() : null;
        // "naturalKeyScope": a code or name is unique within the row's value of this lookup (a billing code within its code set), not table-wide.
        var scope = root.TryGetProperty("naturalKeyScope", out var ns) ? ns.GetString() : null;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowNumber = 0;
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            rowNumber++;
            var id = row.GetProperty(primaryKey).GetString();
            if (!Guid.TryParse(id, out var parsed) || parsed == Guid.Empty || !ids.Add(parsed.ToString()))
            {
                problems.Add($"{table} row {rowNumber}: primary key '{id}' is not a unique non-empty GUID.");
            }

            foreach (var column in columnNames.Where(defined.ContainsKey))
            {
                if (!row.TryGetProperty(column, out var value))
                {
                    problems.Add($"{table} row {rowNumber}: has no value for column '{column}' (write null for none; the loader reads every column of every row).");
                    continue;
                }

                var meta = defined[column];
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (meta.Required && (value.ValueKind == JsonValueKind.Null || (text is not null && string.IsNullOrWhiteSpace(text))))
                {
                    problems.Add($"{table} row {rowNumber}: required column '{column}' is empty.");
                }

                if (text is not null && meta.MaxLength is { } max && text.Length > max)
                {
                    problems.Add($"{table} row {rowNumber}: '{column}' is {text.Length} characters; the column holds {max}.");
                }

                if (text is not null && text != text.Trim())
                {
                    problems.Add($"{table} row {rowNumber}: '{column}' has leading or trailing whitespace (the seeder compares trimmed).");
                }
            }

            foreach (var lookup in lookupNames)
            {
                var target = row.TryGetProperty(lookup, out var lookupValue) && lookupValue.ValueKind == JsonValueKind.String ? lookupValue.GetString() : null;
                if (target is null && defined.TryGetValue(lookup, out var lookupMeta) && lookupMeta.Required)
                {
                    problems.Add($"{table} row {rowNumber}: required column '{lookup}' is empty.");
                }
                else if (target is not null && (!Guid.TryParse(target, out var targetId) || targetId == Guid.Empty))
                {
                    problems.Add($"{table} row {rowNumber}: lookup '{lookup}' value '{target}' is not a non-empty GUID.");
                }
            }

            var scopeValue = scope is not null && row.TryGetProperty(scope, out var scopeElement) ? scopeElement.GetString() : null;
            var scopePrefix = scope is null ? string.Empty : $"{scopeValue}|";
            if (natural is not null && row.TryGetProperty(natural, out var codeValue) && codeValue.GetString()?.Trim() is { Length: > 0 } code && !codes.Add(scopePrefix + code))
            {
                problems.Add($"{table} row {rowNumber}: code '{code}' repeats an earlier row (the second would be read as already present).");
            }

            if (nameColumn is not null && row.TryGetProperty(nameColumn, out var nameValue) && nameValue.GetString()?.Trim() is { Length: > 0 } name && !names.Add(scopePrefix + name))
            {
                problems.Add($"{table} row {rowNumber}: name '{name}' repeats an earlier row (the second would be read as already present).");
            }
        }

        if (rowNumber == 0)
        {
            problems.Add($"{table}: the pack file has no rows.");
        }

        return problems;
    }

    /// <summary>
    /// Wording that belongs to the authoring project, not the customer: pack rows are copied into every stamp and never updated, so a
    /// wave/task reference, a column name or an authoring-process note would be permanent customer-visible text.
    /// </summary>
    private static readonly Regex DevInternalMarkers = new(
        @"\bWave\b|\bD[0-9]\b|\bLayer ?[0-9]\b|sprk_|\bNULL\b|\bseeds?\b|canonical|\btask \d+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The text values (other than the id) of a pack file that carry dev-internal wording.</summary>
    internal static IReadOnlyList<string> DevInternalWording(string packJson)
    {
        using var doc = JsonDocument.Parse(packJson);
        var root = doc.RootElement;
        var table = root.GetProperty("table").GetString()!;
        var primaryKey = root.GetProperty("primaryKey").GetString()!;
        var found = new List<string>();
        var rowNumber = 0;
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            rowNumber++;
            foreach (var property in row.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String && p.Name != primaryKey))
            {
                var hit = DevInternalMarkers.Match(property.Value.GetString()!);
                if (hit.Success)
                {
                    found.Add($"{table} row {rowNumber}: '{property.Name}' contains dev-internal wording '{hit.Value}'.");
                }
            }
        }

        return found;
    }

    [Fact(DisplayName = "D4: no starter-taxonomy pack text carries dev-internal wording (it is copied into every customer stamp)")]
    public void NoPackFile_CarriesDevInternalWording()
    {
        var problems = Directory.EnumerateFiles(Abs(PacksDirectory), "*.json", SearchOption.AllDirectories)
            .SelectMany(file => DevInternalWording(File.ReadAllText(file)).Select(p => $"{Path.GetRelativePath(Abs(PacksDirectory), file)}: {p}"))
            .ToList();

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact(DisplayName = "D4 control (must fire): wave, layer, column-name, NULL, seeds and canonical wording in a pack text is reported")]
    public void Control_DevInternalWording_Fires()
    {
        foreach (var text in new[] { "Wave D3 priority 1.", "Layer 2 required", "when sprk_layer2actioncode is set", "gate = NULL", "Wave D1 seeds this", "the canonical example", "see task 12" })
        {
            Assert.NotEmpty(DevInternalWording(WidgetPack("{ \"sprk_widgetid\": \"11111111-0000-4000-8000-000000000001\", \"sprk_code\": \"AA\", \"sprk_name\": \"" + text + "\" }")));
        }
    }

    [Fact(DisplayName = "D4 control (must not fire): a customer-facing description is not reported")]
    public void Control_CustomerFacingWording_DoesNotFire()
        => Assert.Empty(DevInternalWording(WidgetPack("{ \"sprk_widgetid\": \"11111111-0000-4000-8000-000000000001\", \"sprk_code\": \"AA\", \"sprk_name\": \"Non-disclosure agreement: effective date, parties, term and scope.\" }")));

    private static string EntityXmlPath(string table)
    {
        var directory = Directory.EnumerateDirectories(Abs(EntitiesDirectory))
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), table, StringComparison.OrdinalIgnoreCase));
        Assert.True(directory is not null, $"No entity folder for '{table}' under {EntitiesDirectory}: the pack names a table SpaarkeMaster does not ship.");
        return Path.Combine(directory, "Entity.xml");
    }

    [Fact(DisplayName = "D4: every starter-taxonomy pack file can be created as written: columns exist, required columns filled, lengths fit, keys unique")]
    public void EveryPackFile_CanBeCreatedAsWritten()
    {
        var files = Directory.EnumerateFiles(Abs(PacksDirectory), "*.json", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count >= 7, $"Only {files.Count} pack files found under {PacksDirectory}; the check would be vacuous.");

        var problems = new List<string>();
        foreach (var file in files)
        {
            var json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            problems.AddRange(Problems(json, File.ReadAllText(EntityXmlPath(doc.RootElement.GetProperty("table").GetString()!)))
                .Select(p => $"{Path.GetRelativePath(Abs(PacksDirectory), file)}: {p}"));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private const string WidgetXml = """
        <Entity><EntityInfo><entity><attributes>
          <attribute PhysicalName="sprk_WidgetId"><Name>sprk_widgetid</Name><LogicalName>sprk_widgetid</LogicalName><RequiredLevel>systemrequired</RequiredLevel></attribute>
          <attribute PhysicalName="sprk_Code"><Name>sprk_code</Name><LogicalName>sprk_code</LogicalName><RequiredLevel>none</RequiredLevel><MaxLength>5</MaxLength></attribute>
          <attribute PhysicalName="sprk_Name"><Name>sprk_name</Name><LogicalName>sprk_name</LogicalName><RequiredLevel>required</RequiredLevel><MaxLength>10</MaxLength></attribute>
        </attributes></entity></EntityInfo></Entity>
        """;

    private static string WidgetPack(string rows, string columns = "\"sprk_code\", \"sprk_name\"")
        => "{ \"table\": \"sprk_widget\", \"entitySet\": \"sprk_widgets\", \"primaryKey\": \"sprk_widgetid\", \"match\": \"naturalKey\"," +
           " \"naturalKey\": \"sprk_code\", \"nameColumn\": \"sprk_name\", \"columns\": [" + columns + "], \"rows\": [" + rows + "] }";

    private const string GoodRow = "{ \"sprk_widgetid\": \"11111111-0000-4000-8000-000000000001\", \"sprk_code\": \"AA\", \"sprk_name\": \"Alpha\" }";

    [Fact(DisplayName = "D4 control (must not fire): a valid pack file has no problems")]
    public void Control_AValidFile_DoesNotFire()
        => Assert.Empty(Problems(WidgetPack(GoodRow), WidgetXml));

    [Fact(DisplayName = "D4 control (must fire): an unknown column, an empty required column and an over-long text are each reported")]
    public void Control_UnknownColumn_EmptyRequired_AndTooLong_Fire()
    {
        var unknown = Problems(WidgetPack(GoodRow.Replace("}", ", \"sprk_extra\": 1 }", StringComparison.Ordinal), "\"sprk_code\", \"sprk_name\", \"sprk_extra\""), WidgetXml);
        Assert.Contains(unknown, p => p.Contains("'sprk_extra' is not an attribute", StringComparison.Ordinal));

        var empty = Problems(WidgetPack(GoodRow.Replace("\"Alpha\"", "null", StringComparison.Ordinal)), WidgetXml);
        Assert.Contains(empty, p => p.Contains("required column 'sprk_name' is empty", StringComparison.Ordinal));

        var tooLong = Problems(WidgetPack(GoodRow.Replace("\"AA\"", "\"ABCDEFG\"", StringComparison.Ordinal)), WidgetXml);
        Assert.Contains(tooLong, p => p.Contains("7 characters; the column holds 5", StringComparison.Ordinal));
    }

    /// <summary>
    /// Problems with the lookups a pack file declares: each must match a many-to-one relationship in the SpaarkeMaster source (referencing
    /// table, column, target table and the navigation property the Web API binds through). A wrong navigation property is a 400 on every
    /// stamp that takes the pack, and cannot be seen from the pack file alone.
    /// </summary>
    internal static IReadOnlyList<string> LookupProblems(string packJson, IEnumerable<string> relationshipXmls)
    {
        using var doc = JsonDocument.Parse(packJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("lookups", out var lookups))
        {
            return [];
        }

        var table = root.GetProperty("table").GetString()!;
        var relationships = relationshipXmls
            .SelectMany(xml => XDocument.Parse(xml).Descendants("EntityRelationship"))
            .Select(r => (
                Referencing: r.Element("ReferencingEntityName")?.Value,
                Referenced: r.Element("ReferencedEntityName")?.Value,
                Column: r.Element("ReferencingAttributeName")?.Value,
                Navigation: r.Descendants("EntityRelationshipRole")
                    .Where(role => role.Element("RelationshipRoleType")?.Value == "1")
                    .Select(role => role.Element("NavigationPropertyName")?.Value).FirstOrDefault()))
            .ToList();

        var problems = new List<string>();
        foreach (var lookup in lookups.EnumerateArray())
        {
            var column = lookup.GetProperty("column").GetString()!;
            var navigation = lookup.GetProperty("navigationProperty").GetString()!;
            var target = lookup.GetProperty("targetTable").GetString()!;
            bool Same(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            if (!relationships.Any(r => Same(r.Referencing, table) && Same(r.Column, column) && Same(r.Referenced, target) && string.Equals(r.Navigation, navigation, StringComparison.Ordinal)))
            {
                problems.Add($"{table}: lookup '{column}' -> '{target}' via navigation property '{navigation}' matches no relationship in the SpaarkeMaster source.");
            }
        }

        return problems;
    }

    [Fact(DisplayName = "D5: every lookup a pack file declares matches a relationship and navigation property of the SpaarkeMaster solution")]
    public void EveryPackLookup_MatchesARelationshipOfTheSolution()
    {
        var relationships = Directory.EnumerateFiles(Abs("src/dataverse/solutions/SpaarkeMaster/Other/Relationships"), "*.xml").Select(File.ReadAllText).ToList();
        var files = Directory.EnumerateFiles(Abs(PacksDirectory), "*.json", SearchOption.AllDirectories).ToList();
        var declared = files.Count(f => File.ReadAllText(f).Contains("\"lookups\"", StringComparison.Ordinal));
        Assert.True(declared >= 3, $"Only {declared} pack files declare lookups (billing code, matter type, practice area); the check would be vacuous.");

        var problems = files.SelectMany(f => LookupProblems(File.ReadAllText(f), relationships).Select(p => $"{Path.GetFileName(f)}: {p}")).ToList();

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private const string RelationshipsXml = """
        <EntityRelationships>
          <EntityRelationship Name="sprk_set_widget_setid">
            <ReferencingEntityName>sprk_Widget</ReferencingEntityName><ReferencedEntityName>sprk_set</ReferencedEntityName>
            <ReferencingAttributeName>sprk_SetId</ReferencingAttributeName>
            <EntityRelationshipRoles>
              <EntityRelationshipRole><NavigationPropertyName>sprk_SetId</NavigationPropertyName><RelationshipRoleType>1</RelationshipRoleType></EntityRelationshipRole>
              <EntityRelationshipRole><NavigationPropertyName>sprk_set_widget_setid</NavigationPropertyName><RelationshipRoleType>0</RelationshipRoleType></EntityRelationshipRole>
            </EntityRelationshipRoles>
          </EntityRelationship>
        </EntityRelationships>
        """;

    [Fact(DisplayName = "D5 control (must not fire): a lookup that matches a relationship and its navigation property is not reported")]
    public void Control_AMatchingLookup_DoesNotFire()
        => Assert.Empty(LookupProblems(ScopedPack(ScopedRow("11111111-0000-4000-8000-000000000001", "L110", SetA)), [RelationshipsXml]));

    [Fact(DisplayName = "D5 control (must fire): a wrong navigation property, a wrong target table and a missing relationship are each reported")]
    public void Control_AWrongNavigationTargetOrMissingRelationship_Fire()
    {
        var pack = ScopedPack(ScopedRow("11111111-0000-4000-8000-000000000001", "L110", SetA));

        Assert.NotEmpty(LookupProblems(pack.Replace("\"navigationProperty\": \"sprk_SetId\"", "\"navigationProperty\": \"sprk_setid\"", StringComparison.Ordinal), [RelationshipsXml]));
        Assert.NotEmpty(LookupProblems(pack.Replace("\"targetTable\": \"sprk_set\"", "\"targetTable\": \"sprk_other\"", StringComparison.Ordinal), [RelationshipsXml]));
        Assert.NotEmpty(LookupProblems(pack, ["<EntityRelationships />"]));
    }

    private const string ScopedXml = """
        <Entity><EntityInfo><entity><attributes>
          <attribute PhysicalName="sprk_WidgetId"><Name>sprk_widgetid</Name><LogicalName>sprk_widgetid</LogicalName><RequiredLevel>systemrequired</RequiredLevel></attribute>
          <attribute PhysicalName="sprk_Code"><Name>sprk_code</Name><LogicalName>sprk_code</LogicalName><RequiredLevel>systemrequired</RequiredLevel><MaxLength>5</MaxLength></attribute>
          <attribute PhysicalName="sprk_SetId"><Name>sprk_setid</Name><LogicalName>sprk_setid</LogicalName><RequiredLevel>systemrequired</RequiredLevel></attribute>
        </attributes></entity></EntityInfo></Entity>
        """;

    private static string ScopedPack(string rows, string lookupColumn = "sprk_setid")
        => "{ \"table\": \"sprk_widget\", \"entitySet\": \"sprk_widgets\", \"primaryKey\": \"sprk_widgetid\", \"match\": \"naturalKey\"," +
           " \"naturalKey\": \"sprk_code\", \"nameColumn\": \"sprk_code\", \"naturalKeyScope\": \"" + lookupColumn + "\"," +
           " \"lookups\": [ { \"column\": \"" + lookupColumn + "\", \"navigationProperty\": \"sprk_SetId\", \"targetTable\": \"sprk_set\", \"targetEntitySet\": \"sprk_sets\" } ]," +
           " \"columns\": [\"sprk_code\"], \"rows\": [" + rows + "] }";

    private static string ScopedRow(string id, string code, string? set)
        => "{ \"sprk_widgetid\": \"" + id + "\", \"sprk_code\": \"" + code + "\", \"sprk_setid\": " + (set is null ? "null" : "\"" + set + "\"") + " }";

    private const string SetA = "aaaaaaaa-0000-4000-8000-00000000000a";
    private const string SetB = "bbbbbbbb-0000-4000-8000-00000000000b";

    [Fact(DisplayName = "D5 control (must not fire): the same code in two different code sets is not a duplicate")]
    public void Control_ASameCodeInTwoScopes_DoesNotFire()
        => Assert.Empty(Problems(ScopedPack(
            ScopedRow("11111111-0000-4000-8000-000000000001", "L110", SetA) + "," + ScopedRow("11111111-0000-4000-8000-000000000002", "L110", SetB)), ScopedXml));

    [Fact(DisplayName = "D5 control (must fire): a code repeated inside one code set, an empty required lookup and an unknown lookup column are reported")]
    public void Control_ASameCodeInOneScope_AnEmptyRequiredLookup_AndAnUnknownLookup_Fire()
    {
        var repeated = Problems(ScopedPack(
            ScopedRow("11111111-0000-4000-8000-000000000001", "L110", SetA) + "," + ScopedRow("11111111-0000-4000-8000-000000000002", "l110", SetA)), ScopedXml);
        Assert.Contains(repeated, p => p.Contains("code 'l110' repeats", StringComparison.Ordinal));

        var unset = Problems(ScopedPack(ScopedRow("11111111-0000-4000-8000-000000000001", "L110", null)), ScopedXml);
        Assert.Contains(unset, p => p.Contains("required column 'sprk_setid' is empty", StringComparison.Ordinal));

        var unknown = Problems(ScopedPack(ScopedRow("11111111-0000-4000-8000-000000000001", "L110", SetA).Replace("sprk_setid", "sprk_nope", StringComparison.Ordinal), "sprk_nope"), ScopedXml);
        Assert.Contains(unknown, p => p.Contains("'sprk_nope' is not an attribute", StringComparison.Ordinal));

        var notAGuid = Problems(ScopedPack(ScopedRow("11111111-0000-4000-8000-000000000001", "L110", "not-a-guid")), ScopedXml);
        Assert.Contains(notAGuid, p => p.Contains("is not a non-empty GUID", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "D4 control (must fire): a repeated code or name, a repeated id and a column the pack omits are reported")]
    public void Control_DuplicateKeys_AndAMissingRequiredColumn_Fire()
    {
        var second = "{ \"sprk_widgetid\": \"11111111-0000-4000-8000-000000000002\", \"sprk_code\": \"aa\", \"sprk_name\": \"ALPHA\" }";
        var duplicates = Problems(WidgetPack(GoodRow + "," + second), WidgetXml);
        Assert.Contains(duplicates, p => p.Contains("code 'aa' repeats", StringComparison.Ordinal));
        Assert.Contains(duplicates, p => p.Contains("name 'ALPHA' repeats", StringComparison.Ordinal));

        var sameId = Problems(WidgetPack(GoodRow + "," + GoodRow.Replace("AA", "BB", StringComparison.Ordinal).Replace("Alpha", "Beta", StringComparison.Ordinal)), WidgetXml);
        Assert.Contains(sameId, p => p.Contains("not a unique non-empty GUID", StringComparison.Ordinal));

        var omitted = Problems(WidgetPack("{ \"sprk_widgetid\": \"11111111-0000-4000-8000-000000000001\", \"sprk_code\": \"AA\" }", "\"sprk_code\""), WidgetXml)
            .Where(p => p.Contains("required column 'sprk_name' is not in the pack's columns", StringComparison.Ordinal));
        Assert.NotEmpty(omitted);
    }
}
