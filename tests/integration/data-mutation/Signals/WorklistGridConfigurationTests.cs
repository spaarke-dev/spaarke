using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Guard for the worklist <c>sprk_gridconfiguration</c> definition (<c>config/worklist.gridconfiguration.json</c>,
/// spaarke-ontology-platform-r1 task 050; applied by <c>scripts/Apply-WorklistGridConfiguration.ps1</c>). The definition
/// is data that task 038's Signal read route executes server-side, so a wrong column name or option value would not fail
/// the build: it would fail at runtime as an empty or erroring worklist.
/// </summary>
/// <remarks>
/// <para><b>Real-schema pairing (ADR-038, project CLAUDE.md section 6).</b> This class asserts column names, an entity
/// name and option-set values, so each is checked against the schema the repo ships:
/// <c>src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Signal|sprk_Policy/Entity.xml</c> (every selected, filtered and
/// ordered column exists; every status and lane value is a declared option). The same queries are run against live
/// Dataverse, with live metadata, by <c>Seam/Signals/WorklistGridConfigurationLiveTests</c> (opt-in, same convention as
/// <c>SignalPredicateTests</c>), and by <c>Apply-WorklistGridConfiguration.ps1 -Verify</c>.</para>
/// <para><b>Controls.</b> <see cref="Problems"/> is exercised on seeded defects (must-fire: a rank column in the order, an
/// unknown column, a closed-earlier window that disagrees with its query, a filter on the core record, a core type named in
/// the definition, an action in the row) and on the shipped definition (must-not-fire).</para>
/// </remarks>
public class WorklistGridConfigurationTests
{
    private const int Open = 100000000;
    private const int Acknowledged = 100000001;
    private const int Resolved = 100000002;
    private const int Decide = 100000000;
    private const int Do = 100000001;
    private const int ConfigJsonMaxLength = 20000; // sprk_gridconfiguration.sprk_configjson MaxLength (live, 2026-10-13)

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The core record types today (CoreAncestorResolver.CoreRecordEntities): none may be named in the definition (D-36).</summary>
    private static readonly string[] CoreTypes = { "sprk_matter", "sprk_project", "sprk_workassignment", "sprk_servicerequest" };

    private static readonly string[] PerTypeColumns =
    {
        "sprk_matter", "sprk_project", "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment",
        "sprk_regardingservicerequest", "sprk_regardingevent", "sprk_regardingtodo", "sprk_regardinginvoice",
        "sprk_regardingdocument", "sprk_regardingcommunication",
    };

    [Fact]
    public void TheShippedDefinition_HasNoProblems()
    {
        Problems(LoadDefinitionText(), Schema()).Should().BeEmpty();
    }

    [Fact]
    public void TheDefinition_HoldsOneQueryPerLaneAndTheThreeSecondarySets_AndNoActions()
    {
        var def = JsonNode.Parse(LoadDefinitionText())!.AsObject();

        def["lanes"]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo("decide", "do");
        def["secondary"]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo("closedToday", "suppressed", "closedEarlier");
        def["lanes"]!["decide"]!["laneValue"]!.GetValue<int>().Should().Be(Decide);
        def["lanes"]!["do"]!["laneValue"]!.GetValue<int>().Should().Be(Do);
        def.ContainsKey("actions").Should().BeFalse("actions come from the policy version's decision plan (FR-49)");
        def.ContainsKey("secondaryActions").Should().BeFalse();
    }

    [Fact]
    public void BothLaneQueries_SelectOpenAndAcknowledged_ExcludeSuppressed_AndFilterOnNeitherCoreRecordNorUser()
    {
        foreach (var (name, doc) in Queries(LoadDefinitionText()).Where(q => q.Key.StartsWith("lane:")))
        {
            var statuses = Conditions(doc).Single(c => c.Attribute == "sprk_signalstatus");
            statuses.Operator.Should().Be("in", name);
            statuses.Values.Select(int.Parse).Should().BeEquivalentTo(new[] { Open, Acknowledged }, name);

            var suppression = Conditions(doc).Where(c => c.Attribute == "sprk_suppresseduntil").Select(c => c.Operator).ToArray();
            suppression.Should().BeEquivalentTo(new[] { "null", "olderthan-x-minutes" }, $"{name}: not suppressed = no stamp or a lapsed one");

            // D-35 and D-34: rows with no core record, and project-grouped rows, are in the Do-lane query; so no filter on the pair.
            Conditions(doc).Select(c => c.Attribute).Should().NotContain(new[] { "sprk_corerecordid", "sprk_corerecordtype" }, name);
            // Membership is by rule evaluation, never a user filter (and the owner is read by the route, not filtered here).
            Conditions(doc).Select(c => c.Operator).Should().NotContain(o => o.Contains("userid") || o.Contains("userteams"), name);
        }
    }

    [Fact]
    public void EveryQuery_GroupsByCoreRecord_AndOrdersByNeitherRankNorSeverityNorHighPriority()
    {
        foreach (var (name, doc) in Queries(LoadDefinitionText()))
        {
            var order = doc.Root!.Element("entity")!.Elements("order").Select(o => (string)o.Attribute("attribute")!).ToArray();
            order.Take(2).Should().Equal("sprk_corerecordtype", "sprk_corerecordid");
            order.Should().NotContain(a => a.Contains("rankscore") || a.Contains("severity") || a.Contains("highpriority"), name);
            order.Last().Should().Be("sprk_signalid", $"{name}: a stable tiebreak so paging is deterministic");
        }

        foreach (var (name, doc) in Queries(LoadDefinitionText()).Where(q => q.Key.StartsWith("lane:")))
        {
            doc.Root!.Element("entity")!.Elements("order").Select(o => (string)o.Attribute("attribute")!)
                .Should().Contain("sprk_firstdetected", name);
        }
    }

    [Fact]
    public void EveryQuery_SelectsTheDisplayColumns_AndTheCoreRecordPair_AndTheSignalsPolicyLookup_WithNoPolicyLink()
    {
        foreach (var (name, doc) in Queries(LoadDefinitionText()))
        {
            var entity = doc.Root!.Element("entity")!;
            entity.Elements("attribute").Select(a => (string)a.Attribute("name")!).Should().Contain(new[]
            {
                "sprk_signalid", "sprk_name", "sprk_lane", "sprk_signalstatus", "sprk_firstdetected", "sprk_duedate",
                "sprk_regardingrecordname", "sprk_corerecordtype", "sprk_corerecordid", "sprk_policy", "sprk_policyversion", "sprk_evidencerefs", "ownerid",
                "sprk_sentence", // task 154: the row's sentence, as stored
            }, name);

            // D-185 (task 163, #2285): the route reads the policy's short name, work type and severity as the BFF, by the id the Signal's
            // sprk_policy lookup (selected above) carries. A policy link here would make Dataverse refuse the whole query for a caller
            // without Read on sprk_policy.
            entity.Elements("link-entity").Should().BeEmpty($"{name}: no query links sprk_policy (D-185)");
            entity.Elements("attribute").Select(a => (string)a.Attribute("name")!).Should().NotContain(PerTypeColumns);
        }
    }

    [Fact]
    public void SecondarySets_AreEachOnlyTheirSet()
    {
        var def = JsonNode.Parse(LoadDefinitionText())!.AsObject();
        var queries = Queries(LoadDefinitionText());

        var today = Conditions(queries["secondary:closedToday"]);
        today.Single(c => c.Attribute == "sprk_signalstatus").Should().Be(
            new Condition("sprk_signalstatus", "eq", Resolved.ToString(), Array.Empty<string>()));
        today.Single(c => c.Attribute == "sprk_resolvedon").Should().Be(
            new Condition("sprk_resolvedon", "last-x-hours", def["secondary"]!["closedToday"]!["widenHours"]!.GetValue<int>().ToString(), Array.Empty<string>()));

        var suppressed = Conditions(queries["secondary:suppressed"]);
        suppressed.Single(c => c.Attribute == "sprk_signalstatus").Values.Select(int.Parse)
            .Should().BeEquivalentTo(new[] { Open, Acknowledged }, "a dismissed Signal also carries the stamp but is a closed item, not a suppressed one");
        suppressed.Single(c => c.Attribute == "sprk_suppresseduntil").Operator.Should().Be("next-x-years");
        queries["secondary:suppressed"].Root!.Element("entity")!.Elements("attribute").Select(a => (string)a.Attribute("name")!)
            .Should().Contain("sprk_suppresseduntil", "the until-date is shown");

        var earlier = Conditions(queries["secondary:closedEarlier"]);
        earlier.Single(c => c.Attribute == "sprk_signalstatus").Should().Be(
            new Condition("sprk_signalstatus", "eq", Resolved.ToString(), Array.Empty<string>()));
        var windowDays = def["secondary"]!["closedEarlier"]!["windowDays"]!.GetValue<int>();
        earlier.Single(c => c.Attribute == "sprk_resolvedon").Should().Be(
            new Condition("sprk_resolvedon", "last-x-hours", (windowDays * 24).ToString(), Array.Empty<string>()));
    }

    // ── Real-schema agreement against the repo solution source ───────────────────────────────────────────────────

    [Fact]
    public void EveryColumnAndOptionValue_ExistsInTheSolutionSource()
    {
        var schema = Schema();
        // Premise pins: if the solution source stops declaring these, the check above would pass vacuously.
        schema.Columns["sprk_signal"].Should().Contain(new[] { "sprk_signalstatus", "sprk_suppresseduntil", "sprk_resolvedon", "sprk_corerecordtype", "sprk_corerecordid", "sprk_duedate" });
        schema.Columns["sprk_policy"].Should().Contain(new[] { "sprk_shortname", "sprk_worktype", "sprk_severity", "sprk_policyid" });
        schema.Options["sprk_signalstatus"].Should().BeEquivalentTo(new[] { Open, Acknowledged, Resolved });
        schema.Options["sprk_lane"].Should().BeEquivalentTo(new[] { Decide, Do });

        Problems(LoadDefinitionText(), schema).Should().NotContain(p => p.StartsWith("schema:"));
    }

    // ── Controls: the checker must fire on each seeded defect ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("<order attribute='sprk_firstdetected' descending='false' />", "<order attribute='sprk_rankscore' descending='true' /><order attribute='sprk_firstdetected' descending='false' />", "order names")]
    [InlineData("<attribute name='sprk_evidencerefs' />", "<attribute name='sprk_noSuchColumn' />", "schema: sprk_signal has no column")]
    [InlineData("<attribute name='sprk_policy' />", "<attribute name='sprk_policy' /><link-entity name='sprk_policy' from='sprk_policyid' to='sprk_policy' link-type='outer' alias='policy'><attribute name='sprk_shortname' /></link-entity>", "D-185")]
    [InlineData("<value>100000001</value>", "<value>100000009</value>", "schema: sprk_signalstatus has no option")]
    [InlineData("\"windowDays\": 30", "\"windowDays\": 7", "closedEarlier")]
    [InlineData("<condition attribute='statecode' operator='eq' value='0' />", "<condition attribute='statecode' operator='eq' value='0' /><condition attribute='sprk_corerecordid' operator='not-null' />", "filters on the core record")]
    [InlineData("<condition attribute='statecode' operator='eq' value='0' />", "<condition attribute='ownerid' operator='eq-userid' />", "user filter")]
    [InlineData("\"kind\": \"worklist\",", "\"kind\": \"worklist\", \"actions\": [],", "no actions")]
    [InlineData("<attribute name='sprk_firstdetected' />", "<attribute name='sprk_regardingmatter' />", "per-type")]
    [InlineData("Ontology worklist membership", "Ontology worklist for sprk_matter membership", "names a core type")]
    public void Problems_FiresOnEachSeededDefect(string find, string replace, string expectedProblem)
    {
        var mutated = LoadDefinitionText().Replace(find, replace);
        mutated.Should().NotBe(LoadDefinitionText(), "the seeded defect must change the definition");

        Problems(mutated, Schema()).Should().Contain(p => p.Contains(expectedProblem));
    }

    [Fact]
    public void Problems_FiresWhenAQueryIsMissing_AndWhenTheJsonIsTooLongForTheColumn()
    {
        var def = JsonNode.Parse(LoadDefinitionText())!.AsObject();
        def["secondary"]!.AsObject().Remove("suppressed");
        Problems(def.ToJsonString(), Schema()).Should().Contain(p => p.Contains("suppressed"));

        var padded = JsonNode.Parse(LoadDefinitionText())!.AsObject();
        padded["description"] = new string('x', ConfigJsonMaxLength);
        Problems(padded.ToJsonString(Compact), Schema()).Should().Contain(p => p.Contains("sprk_configjson"));
    }

    // ── The checker ──────────────────────────────────────────────────────────────────────────────────────────────

    internal sealed record Condition(string Attribute, string Operator, string? Value, string[] Values)
    {
        public bool Equals(Condition? other) =>
            other is not null && Attribute == other.Attribute && Operator == other.Operator && Value == other.Value && Values.SequenceEqual(other.Values);

        public override int GetHashCode() => HashCode.Combine(Attribute, Operator, Value);
    }

    internal sealed record SolutionSchema(
        IReadOnlyDictionary<string, ISet<string>> Columns, IReadOnlyDictionary<string, ISet<int>> Options);

    internal static IReadOnlyDictionary<string, XDocument> Queries(string definitionText)
    {
        var def = JsonNode.Parse(definitionText)!.AsObject();
        var found = new Dictionary<string, XDocument>();
        void Add(string key, JsonNode? node)
        {
            var text = (string?)node?["fetchXml"];
            if (text is not null) found[key] = XDocument.Parse(text);
        }

        Add("lane:decide", def["lanes"]?["decide"]);
        Add("lane:do", def["lanes"]?["do"]);
        Add("secondary:closedToday", def["secondary"]?["closedToday"]);
        Add("secondary:suppressed", def["secondary"]?["suppressed"]);
        Add("secondary:closedEarlier", def["secondary"]?["closedEarlier"]);
        return found;
    }

    /// <summary>Every condition on the main entity (the linked policy carries none).</summary>
    internal static IReadOnlyList<Condition> Conditions(XDocument doc) =>
        doc.Root!.Element("entity")!.Descendants("condition")
            .Select(c => new Condition(
                (string)c.Attribute("attribute")!, (string)c.Attribute("operator")!, (string?)c.Attribute("value"),
                c.Elements("value").Select(v => v.Value.Trim()).ToArray()))
            .ToList();

    /// <summary>Empty when the definition is sound, else one line per problem. Lines starting "schema:" are real-schema disagreements.</summary>
    internal static IReadOnlyList<string> Problems(string definitionText, SolutionSchema schema)
    {
        var problems = new List<string>();
        var def = JsonNode.Parse(definitionText)!.AsObject();
        var compact = def.ToJsonString(Compact);

        if (compact.Length > ConfigJsonMaxLength)
            problems.Add($"the definition is {compact.Length} characters; sprk_configjson holds {ConfigJsonMaxLength}");
        if (def.ContainsKey("actions") || def.ContainsKey("secondaryActions"))
            problems.Add("the row carries no actions (FR-49)");
        foreach (var core in CoreTypes)
        {
            // The configuration must not name a core type (D-36): nothing but the generic pair identifies the core record.
            if (definitionText.Contains(core, StringComparison.Ordinal))
                problems.Add($"the definition names a core type ({core})");
        }

        var queries = Queries(definitionText);
        foreach (var key in new[] { "lane:decide", "lane:do", "secondary:closedToday", "secondary:suppressed", "secondary:closedEarlier" })
        {
            if (!queries.ContainsKey(key)) problems.Add($"{key}: the query is missing");
        }

        foreach (var (name, doc) in queries)
        {
            var entity = doc.Root!.Element("entity")!;
            if ((string?)entity.Attribute("name") != "sprk_signal") problems.Add($"{name}: not on sprk_signal");

            foreach (var o in entity.Elements("order").Select(o => (string)o.Attribute("attribute")!))
            {
                if (o.Contains("rankscore") || o.Contains("severity") || o.Contains("highpriority"))
                    problems.Add($"{name}: order names {o}; rank is applied by the route (D-42)");
            }

            foreach (var a in entity.Elements("attribute").Select(a => (string)a.Attribute("name")!).Where(a => PerTypeColumns.Contains(a)))
                problems.Add($"{name}: selects the per-type column {a}; select sprk_corerecordtype/sprk_corerecordid (D-36)");

            foreach (var c in Conditions(doc))
            {
                if (c.Attribute is "sprk_corerecordid" or "sprk_corerecordtype")
                    problems.Add($"{name}: filters on the core record ({c.Attribute}); rows with no core record and every core type must be included");
                if (c.Operator.Contains("userid") || c.Operator.Contains("userteams"))
                    problems.Add($"{name}: user filter ({c.Operator} on {c.Attribute}); membership is by rule evaluation");
            }

            // D-185: a linked table is read AS THE CALLER, so the caller would need Read on it. The policy columns are the BFF's read.
            foreach (var link in entity.Elements("link-entity"))
            {
                problems.Add($"{name}: links {(string?)link.Attribute("name")}; the BFF reads the policy columns (D-185), a join needs the caller's Read");
            }

            // Real-schema agreement with the solution source.
            foreach (var a in entity.Elements("attribute").Select(a => (string)a.Attribute("name")!)
                         .Concat(entity.Elements("order").Select(o => (string)o.Attribute("attribute")!))
                         .Concat(Conditions(doc).Select(c => c.Attribute)).Distinct())
            {
                if (!schema.Columns["sprk_signal"].Contains(a)) problems.Add($"schema: sprk_signal has no column {a} ({name})");
            }

            foreach (var c in Conditions(doc).Where(c => c.Attribute is "sprk_signalstatus" or "sprk_lane"))
            {
                var declared = schema.Options[c.Attribute];
                foreach (var v in c.Values.Concat(c.Value is null ? Array.Empty<string>() : new[] { c.Value }))
                {
                    if (!declared.Contains(int.Parse(v))) problems.Add($"schema: {c.Attribute} has no option {v} ({name})");
                }
            }
        }

        // Lane queries: one lane value each, and the Open/Acknowledged set (the sets above are checked by their own tests).
        if (queries.TryGetValue("lane:decide", out var decide) && Conditions(decide).SingleOrDefault(c => c.Attribute == "sprk_lane")?.Value != Decide.ToString())
            problems.Add("lane:decide: must filter sprk_lane = Decide");
        if (queries.TryGetValue("lane:do", out var doLane) && Conditions(doLane).SingleOrDefault(c => c.Attribute == "sprk_lane")?.Value != Do.ToString())
            problems.Add("lane:do: must filter sprk_lane = Do");

        // The closed-earlier window the row states must be the window its query applies.
        if (queries.TryGetValue("secondary:closedEarlier", out var earlier) && def["secondary"]?["closedEarlier"]?["windowDays"] is { } windowDays)
        {
            var resolvedOn = Conditions(earlier).SingleOrDefault(c => c.Attribute == "sprk_resolvedon");
            if (resolvedOn?.Operator != "last-x-hours" || resolvedOn.Value != (windowDays.GetValue<int>() * 24).ToString())
                problems.Add($"closedEarlier: windowDays {windowDays} does not match its query ({resolvedOn?.Operator} {resolvedOn?.Value})");
        }

        if (queries.TryGetValue("secondary:closedToday", out var today) && def["secondary"]?["closedToday"]?["widenHours"] is { } widen)
        {
            var resolvedOn = Conditions(today).SingleOrDefault(c => c.Attribute == "sprk_resolvedon");
            if (resolvedOn?.Operator != "last-x-hours" || resolvedOn.Value != widen.GetValue<int>().ToString())
                problems.Add($"closedToday: widenHours {widen} does not match its query ({resolvedOn?.Operator} {resolvedOn?.Value})");
        }

        return problems;
    }

    // ── Inputs ───────────────────────────────────────────────────────────────────────────────────────────────────

    internal static string LoadDefinitionText() => File.ReadAllText(Path.Combine(RepoRoot(), "config", "worklist.gridconfiguration.json"));

    private static SolutionSchema Schema()
    {
        var entities = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Entities");
        var signal = XDocument.Load(Path.Combine(entities, "sprk_Signal", "Entity.xml"));
        var policy = XDocument.Load(Path.Combine(entities, "sprk_Policy", "Entity.xml"));

        return new SolutionSchema(
            new Dictionary<string, ISet<string>> { ["sprk_signal"] = ColumnsOf(signal), ["sprk_policy"] = ColumnsOf(policy) },
            new Dictionary<string, ISet<int>>
            {
                ["sprk_signalstatus"] = OptionsOf(signal, "sprk_signalstatus"),
                ["sprk_lane"] = OptionsOf(signal, "sprk_lane"),
            });
    }

    /// <summary>Logical names: the explicit LogicalName/Name where the file gives one, else the lower-cased PhysicalName (system columns).</summary>
    private static ISet<string> ColumnsOf(XDocument entityXml) =>
        entityXml.Descendants("attribute")
            .Where(a => a.Attribute("PhysicalName") is not null)
            .SelectMany(a => new[] { ((string?)a.Element("LogicalName"))?.Trim(), ((string)a.Attribute("PhysicalName")!).ToLowerInvariant() })
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

    private static ISet<int> OptionsOf(XDocument entityXml, string column) =>
        entityXml.Descendants("attribute")
            .Single(a => (string?)a.Element("LogicalName") == column)
            .Descendants("option")
            .Select(o => int.Parse((string)o.Attribute("value")!))
            .ToHashSet();

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
