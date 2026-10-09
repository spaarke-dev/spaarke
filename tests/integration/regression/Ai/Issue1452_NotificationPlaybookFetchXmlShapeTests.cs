// Regression: ISS-018 (#1452). Every notification playbook in spaarkedev1 failed for every user for 89+ days, silently:
//   (1) `<condition operator="in" value="{{joinIds myMatters.ids}}"/>` — Dataverse ignores the value attribute of a list
//       operator, so the list is EMPTY and the query fails ("The value passed for ConditionOperator.In is empty");
//   (2) Layer 1 renders `"left": "{{q.output.count}}"` to a JSON number and ConditionExpression.Left was a string;
//   (3) Layer 1 rendered CreateNotification's `{{item.*}}` templates (empty) before the executor's per-item loop;
//   (4) `{{item.m_sprk_mattername}}` never resolved — an aliased column is keyed "m.sprk_mattername".
// These tests run the repo definitions (and, since D-100, an inline legacy-shape fixture) through the production render
// path and executors, and each pins the old form failing. No test before this executed a real list condition; the one Dataverse simulator split the commas itself.
// D-100 (task 131): the seven notification playbook definitions and the CreateNotification node executor were removed.
// The engine-level checks below (Condition numeric left, old comma form) run on LegacyNotificationShape, a small inline
// definition copied from the retired Tasks Due Soon playbook. They guard ConditionNodeExecutor, the Layer 1 render and
// FetchXmlShapeValidator. The FR-6 notification payload is covered by CustomDataSchemaConformanceTests (NotificationActionCore).

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression.Ai;

[Trait("status", "regression-iss018")]
public class Issue1452_NotificationPlaybookFetchXmlShapeTests
{
    private static readonly ITemplateEngine Engine = new TemplateEngine(NullLogger<TemplateEngine>.Instance);
    private static readonly Guid UserId = Guid.Parse("1d02f31c-1872-f011-b4cb-7c1e52671ad0");
    private const string ImpossibleMatch = "00000000-0000-0000-0000-000000000000";
    private const string HostileName = "O'Brien & Sons <LLP> \"x\"/><condition attribute=\"ownerid\" operator=\"not-null\"/>";

    private static readonly string[] SearchRoots = ["projects", "src", "scripts", "infra", "infrastructure"];
    private static readonly string[] SkippedDirectories = ["bin", "obj", "node_modules", ".git", "dist", "out"];

    /// <summary>
    /// Inline stand-in for the retired Tasks Due Soon definition (D-100): Start, Lookup My Matters, Query, Check Results
    /// (Condition), Create Notification, with the node shapes the retired playbooks used.
    /// </summary>
    private const string LegacyNotificationShape = """
{
  "nodes": [
    {
      "name": "Start",
      "executorType": 33,
      "outputVariable": "start",
      "dependsOn": [],
      "configJson": {
        "__actionType": 33,
        "scope": "user-matters",
        "resolveUserId": true
      }
    },
    {
      "name": "Lookup My Matters",
      "executorType": 52,
      "outputVariable": "myMatters",
      "dependsOn": [
        "Start"
      ],
      "configJson": {
        "__actionType": 52,
        "entityType": "sprk_matter",
        "targeting": "people",
        "includeRelated": false
      }
    },
    {
      "name": "Query Tasks Due Soon",
      "executorType": 51,
      "outputVariable": "dueSoonQuery",
      "dependsOn": [
        "Lookup My Matters"
      ],
      "configJson": {
        "__actionType": 51,
        "queryMode": true,
        "entityLogicalName": "sprk_event",
        "fetchXml": "<fetch top=\"50\"><entity name=\"sprk_event\"><attribute name=\"sprk_eventname\"/><attribute name=\"sprk_duedate\"/><attribute name=\"sprk_finalduedate\"/><attribute name=\"sprk_eventid\"/><attribute name=\"sprk_eventtype_ref\"/><attribute name=\"statuscode\"/><attribute name=\"modifiedon\"/><attribute name=\"sprk_regardingrecordname\"/><attribute name=\"sprk_regardingrecordurl\"/><attribute name=\"sprk_regardingmatter\"/><attribute name=\"ownerid\"/><link-entity name=\"sprk_matter\" from=\"sprk_matterid\" to=\"sprk_regardingmatter\" link-type=\"outer\" alias=\"m\"><attribute name=\"sprk_mattername\"/><attribute name=\"ownerid\"/></link-entity><filter type=\"and\"><condition attribute=\"sprk_eventtype_ref\" operator=\"eq\" value=\"124f5fc9-98ff-f011-8406-7c1e525abd8b\"/><condition attribute=\"statuscode\" operator=\"eq\" value=\"659490001\"/><filter type=\"and\"><condition attribute=\"sprk_duedate\" operator=\"ge\" value=\"{{todayUtc}}\"/><condition attribute=\"sprk_duedate\" operator=\"le\" value=\"{{dueSoonWindowUtc}}\"/></filter><filter type=\"or\"><condition attribute=\"sprk_regardingmatter\" operator=\"in\">{{fetchInGuids myMatters.ids}}</condition><condition entityname=\"m\" attribute=\"ownerid\" operator=\"eq-userid\"/><condition attribute=\"ownerid\" operator=\"eq-userid\"/></filter></filter><order attribute=\"sprk_duedate\" descending=\"false\"/></entity></fetch>"
      }
    },
    {
      "name": "Check Results",
      "executorType": 30,
      "outputVariable": "hasDueSoon",
      "dependsOn": [
        "Query Tasks Due Soon"
      ],
      "configJson": {
        "__actionType": 30,
        "condition": {
          "operator": "gt",
          "left": "{{dueSoonQuery.output.count}}",
          "right": 0
        },
        "trueBranch": "Create Notification"
      }
    },
    {
      "name": "Create Notification",
      "executorType": 50,
      "outputVariable": "notification",
      "dependsOn": [
        "Check Results"
      ],
      "configJson": {
        "__actionType": 50,
        "title": "{{dueSoonQuery.output.count}} task(s) due in the next {{dueWithinDays}} day(s)",
        "body": "{{#each dueSoonQuery.output.items}}{{sprk_eventname}} is due on {{sprk_duedate}}.\n{{/each}}",
        "category": "tasks-due-soon",
        "priority": 200000000,
        "actionUrl": "/main.aspx?pagetype=entitylist&etn=sprk_event&viewtype=1039",
        "recipientId": "{{run.userId}}",
        "regardingType": "sprk_event",
        "iterateItems": true,
        "itemNotification": {
          "title": "Due soon: {{item.sprk_eventname}}",
          "body": "{{item.sprk_eventname}} is due on {{item.sprk_duedate}} ({{item.sprk_regardingrecordname}})",
          "category": "tasks-due-soon",
          "priority": 200000000,
          "actionUrl": "/main.aspx?pagetype=entityrecord&etn=sprk_event&id={{item.sprk_eventid}}",
          "recipientId": "{{run.userId}}",
          "regardingId": "{{item.sprk_eventid}}",
          "regardingType": "sprk_event",
          "dueDate": "{{item.sprk_duedate}}",
          "regardingName": "{{item.sprk_eventname}}",
          "sourceEntityType": "sprk_event",
          "sourceId": "{{item.sprk_eventid}}",
          "sourceModifiedOn": "{{item.modifiedon}}",
          "sourceOwningUser": "{{item.ownerid}}",
          "viaMatterId": "{{item.sprk_regardingmatter}}",
          "viaMatterName": "{{lookup item 'm.sprk_mattername'}}",
          "viaMatterMembershipsVariable": "myMatters"
        }
      }
    }
  ]
}
""";

    // ── Discovery: every repo playbook definition that carries a FetchXML query ─────────────

    public static TheoryData<string> PlaybooksWithFetchXml()
    {
        var data = new TheoryData<string>();
        foreach (var path in FindPlaybooksWithFetchXml())
        {
            data.Add(Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/'));
        }

        return data;
    }

    [Fact]
    public void Discovery_FindsTheInsightsPlaybook_AndNoRetiredNotificationPlaybook()
    {
        var found = FindPlaybooksWithFetchXml().Select(Path.GetFileName).ToList();
        found.Should().NotContain(f => f!.StartsWith("notification-", StringComparison.Ordinal), "D-100 retired the seven notification playbooks; their definitions must not return");
        found.Should().Contain("matter-health-single.playbook.json");
    }

    // ── (1) FetchXML list shape — authored and rendered ─────────────────────────────────────

    [Theory]
    [MemberData(nameof(PlaybooksWithFetchXml))]
    public void EveryRepoPlaybook_AuthoredFetchXml_PassesTheShapeCheck(string relativePath)
    {
        foreach (var (node, fetchXml) in QueriesOf(LoadDefinition(relativePath)))
        {
            FetchXmlShapeValidator.Validate(fetchXml, authoredTemplate: true)
                .Should().BeEmpty($"{relativePath} node '{node}' is deployed by lint C, which runs this check");
        }
    }

    [Theory]
    [MemberData(nameof(PlaybooksWithFetchXml))]
    public void EveryRepoPlaybook_RenderedQuery_IsWellShaped_ForZeroOneAndManyMemberships(string relativePath) =>
        CheckRenderedListShape(LoadDefinition(relativePath), relativePath);

    [Fact]
    public void TheLegacyShape_RendersOneValueChildPerMembershipId_ForZeroOneAndManyMemberships()
    {
        // The only repo playbook with FetchXML left (matter-health-single) has no `in` list condition, so the theory above can
        // pass without checking one. This runs the full production render chain (Layer 1 + fetchInGuids + variable
        // resolution) with real ids on the legacy-shape definition and requires that it really checked list conditions.
        var checked_ = CheckRenderedListShape(JsonNode.Parse(LegacyNotificationShape)!.AsObject(), "LegacyNotificationShape");

        checked_.Should().Be(3, "the legacy query has one matter `in` list and is rendered for 0, 1 and 17 ids");
    }

    [Fact]
    public void ListShapeChecks_AcrossAllRepoPlaybooksAndTheLegacyShape_AreNeverVacuous()
    {
        var total = FindPlaybooksWithFetchXml().Sum(path => CheckRenderedListShape(LoadDefinition(Path.GetRelativePath(RepoRoot(), path)), path));
        total += CheckRenderedListShape(JsonNode.Parse(LegacyNotificationShape)!.AsObject(), "LegacyNotificationShape");

        total.Should().BeGreaterThan(0, "a list-shape check that inspected zero `in` conditions proves nothing");
    }

    /// <summary>Renders every query node for 0, 1 and 17 membership ids; returns how many matter `in` lists it checked.</summary>
    private static int CheckRenderedListShape(JsonObject definition, string label)
    {
        var listsChecked = 0;
        foreach (var ids in new[] { Array.Empty<string>(), [Guid.NewGuid().ToString()], Enumerable.Range(0, 17).Select(_ => Guid.NewGuid().ToString()).ToArray() })
        {
            foreach (var node in QueryNodesOf(definition))
            {
                var rendered = RenderedFetchXml(node, ids);
                FetchXmlShapeValidator.Validate(rendered).Should().BeEmpty($"{label} '{node["name"]}' with {ids.Length} id(s)");

                foreach (var list in XDocument.Parse(rendered).Descendants("condition")
                             .Where(c => (string?)c.Attribute("operator") == "in" && c.Attribute("attribute")?.Value is "sprk_regardingmatter" or "sprk_matter"))
                {
                    var expected = ids.Length == 0 ? [ImpossibleMatch] : ids.OrderBy(i => i, StringComparer.Ordinal).ToArray();
                    list.Elements("value").Select(v => v.Value).Should().Equal(expected,
                        "one <value> child per membership id; an empty list selects nothing instead of failing the run");
                    listsChecked++;
                }
            }
        }

        return listsChecked;
    }

    [Fact]
    public void TheOldCommaForm_FailsTheSameChecks()
    {
        // Put the old comma shape back into the legacy-shape definition: both the authored and the rendered check must fail.
        var text = LegacyNotificationShape.Replace(
            "operator=\\\"in\\\">{{fetchInGuids myMatters.ids}}</condition>",
            "operator=\\\"in\\\" value=\\\"{{joinIds myMatters.ids}}\\\"/>",
            StringComparison.Ordinal);
        text.Should().NotBe(LegacyNotificationShape, "the replacement must actually apply");
        var definition = JsonNode.Parse(text)!.AsObject();

        foreach (var (_, fetchXml) in QueriesOf(definition))
        {
            FetchXmlShapeValidator.Validate(fetchXml, authoredTemplate: true).Should().Contain(p => p.Contains("joinIds"));
        }

        foreach (var node in QueryNodesOf(definition))
        {
            FetchXmlShapeValidator.Validate(RenderedFetchXml(node, [Guid.NewGuid().ToString(), Guid.NewGuid().ToString()]))
                .Should().Contain(p => p.Contains("value attribute"), "a real list in the value attribute is still an empty list");
        }
    }

    [Fact]
    public async Task TheExecutor_RefusesTheOldForm_WithoutCallingDataverse_AndRunsTheNewForm()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("usersettings", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("usersettings") { ["timezonecode"] = 85 });
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("timezonedefinition") { ["standardname"] = "GMT Standard Time" } }));
        var fetches = new List<string>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((f, _) => fetches.Add(f.Query))
            .ReturnsAsync(new EntityCollection());
        var executor = new QueryDataverseNodeExecutor(Engine, entities.Object, NullLogger<QueryDataverseNodeExecutor>.Instance);
        var a = Guid.NewGuid().ToString();
        var b = Guid.NewGuid().ToString();

        var old = await executor.ExecuteAsync(QueryContext(
            $"<fetch><entity name=\"sprk_event\"><filter><condition attribute=\"sprk_regardingmatter\" operator=\"in\" value=\"{a},{b}\"/></filter></entity></fetch>"),
            CancellationToken.None);

        old.Success.Should().BeFalse();
        old.ErrorCode.Should().Be(NodeErrorCodes.InvalidConfiguration);
        old.ErrorMessage.Should().Contain("Query Matter Activity").And.Contain("sprk_regardingmatter").And.Contain("operator=\"in\"");
        fetches.Should().BeEmpty("the malformed query never reaches Dataverse");

        var good = await executor.ExecuteAsync(QueryContext(
            $"<fetch><entity name=\"sprk_event\"><filter><condition attribute=\"sprk_regardingmatter\" operator=\"in\"><value>{a}</value><value>{b}</value></condition></filter></entity></fetch>"),
            CancellationToken.None);

        good.Success.Should().BeTrue();
        fetches.Should().ContainSingle();
    }

    // ── (2) Condition node: Layer 1 renders `left` to a JSON number ─────────────────────────

    [Fact]
    public async Task TheConditionNode_EvaluatesALayer1RenderedNumericLeft()
    {
        var definition = JsonNode.Parse(LegacyNotificationShape)!.AsObject();
        var condition = NodesOf(definition).Single(n => (int?)n["executorType"] == 30);
        var query = QueryNodesOf(definition).Single();
        var executor = new ConditionNodeExecutor(Engine, NullLogger<ConditionNodeExecutor>.Instance);

        foreach (var (count, expected) in new[] { (0, false), (12, true) })
        {
            var outputs = new Dictionary<string, NodeOutput>
            {
                [(string)query["outputVariable"]!] = NodeOutput.Ok(Guid.NewGuid(), (string)query["outputVariable"]!, new { count, items = Array.Empty<object>() }),
            };
            var rendered = PlaybookOrchestrationService.RenderConfigJsonStructurally(
                condition["configJson"]!.ToJsonString(), Layer1Context(outputs), Engine, ExecutorType.Condition);
            JsonNode.Parse(rendered)!["condition"]!["left"]!.GetValueKind().Should().Be(JsonValueKind.Number, "this is what failed every run");

            var result = await executor.ExecuteAsync(NodeContext("Check", rendered, ExecutorType.Condition, outputs), CancellationToken.None);

            result.Success.Should().BeTrue(result.ErrorMessage);
            result.GetData<ConditionResult>()!.Result.Should().Be(expected);
        }
    }

    [Fact]
    public void Layer1_RendersRunUserId_OnTheOrchestratorsRunContext()
    {
        // {{run.userId}} rendered null in Layer 1 (the run bag had no userId); CreateNotification's recipientId survived
        // only through the executor's own fallback. ApplyConfigJsonTemplates builds from the PlaybookRunContext overload.
        var run = new PlaybookRunContext(Guid.NewGuid(), Guid.NewGuid(), [], "tenant", CancellationToken.None) { UserId = UserId };
        Engine.Render("{{run.userId}}", PlaybookTemplateContextBuilder.Build(run)).Should().Be(UserId.ToString());
        Engine.Render("{{run.userId}}", Layer1Context(new Dictionary<string, NodeOutput>())).Should().Be(UserId.ToString());
    }

    [Theory]
    [InlineData("Overdue: {{item.sprk_eventname}}", ExecutorType.CreateNotification, true)]
    [InlineData("{{lookup item 'm.sprk_mattername'}}", ExecutorType.CreateNotification, true)]
    [InlineData("{{#if item.x}}y{{/if}}", ExecutorType.CreateNotification, true)]
    [InlineData("{{q.item}} and {{items}}", ExecutorType.CreateNotification, false)]
    [InlineData("{{lookup q 'item'}}", ExecutorType.CreateNotification, false)]
    [InlineData("{{#each xs as |item|}}{{item.name}}{{/each}}", ExecutorType.CreateNotification, false)]
    [InlineData("Overdue: {{item.sprk_eventname}}", ExecutorType.UpdateRecord, false)]
    [InlineData("Overdue: {{item.sprk_eventname}}", null, false)]
    public void ExecutorScopedItem_IsLeftForTheExecutor_OnlyWhereThatExecutorBindsIt(string raw, ExecutorType? type, bool leftVerbatim)
    {
        PlaybookOrchestrationService.ReferencesUnboundExecutorScope(raw, new Dictionary<string, object?>(), type).Should().Be(leftVerbatim);
        PlaybookOrchestrationService.ReferencesUnboundExecutorScope(raw, new Dictionary<string, object?> { ["item"] = new { } }, type)
            .Should().BeFalse("a fan-out overlay that binds `item` renders it in Layer 1");
    }

    [Fact]
    public void ANestedConfigString_StillRendersItsOtherLeaves_WhenOneLeafUsesItem()
    {
        // A Designer wrapper stores the real config as a JSON string; only the leaf that uses `item` is left verbatim.
        var outputs = new Dictionary<string, NodeOutput> { ["q"] = NodeOutput.Ok(Guid.NewGuid(), "q", new { count = 3 }) };
        var wrapper = new JsonObject
        {
            ["configJson"] = new JsonObject { ["title"] = "{{q.output.count}} items", ["itemNotification"] = new JsonObject { ["title"] = "Item {{item.name}}" } }.ToJsonString(),
        }.ToJsonString();

        var rendered = PlaybookOrchestrationService.RenderConfigJsonStructurally(wrapper, Layer1Context(outputs), Engine, ExecutorType.CreateNotification);

        var inner = JsonNode.Parse((string)JsonNode.Parse(rendered)!["configJson"]!)!;
        ((string)inner["title"]!).Should().Be("3 items");
        ((string)inner["itemNotification"]!["title"]!).Should().Be("Item {{item.name}}");
    }

    [Theory]
    [InlineData("eq", "null")]
    [InlineData("ne", "null")]
    [InlineData("gt", "null")]
    [InlineData("eq", "\"\"")]
    [InlineData("lt", "\"  \"")]
    public async Task TheConditionNode_FailsANullOrBlankLeft_ForEveryOperatorButExists(string op, string left)
    {
        // A missing upstream value must not silently pick a branch: only `exists` may see null/blank (-> false).
        var executor = new ConditionNodeExecutor(Engine, NullLogger<ConditionNodeExecutor>.Instance);
        var config = $"{{\"condition\":{{\"operator\":\"{op}\",\"left\":{left},\"right\":0}},\"trueBranch\":\"Next\",\"falseBranch\":\"Other\"}}";

        var result = await executor.ExecuteAsync(NodeContext("Check", config, ExecutorType.Condition, new Dictionary<string, NodeOutput>()), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("left");
    }

    [Theory]
    [InlineData("item", true)]
    [InlineData(" item ", true)]
    [InlineData("items", false)]
    [InlineData("Item", false)]
    [InlineData("myMatters", false)]
    public void TheOutputVariableItem_IsReserved(string name, bool reserved) =>
        PlaybookOrchestrationService.IsReservedOutputVariable(name).Should().Be(reserved);

    [Fact]
    public async Task TheConditionNode_TreatsALayer1RenderedNullAsAValue_ExistsIsFalse()
    {
        // A missing upstream value renders to JSON null in Layer 1; that must evaluate (exists -> false), not fail the
        // node as "left operand is required" (found in passing by the ISS-018 review).
        var executor = new ConditionNodeExecutor(Engine, NullLogger<ConditionNodeExecutor>.Instance);
        var config = "{\"condition\":{\"operator\":\"exists\",\"left\":null},\"trueBranch\":\"Next\"}";

        var result = await executor.ExecuteAsync(NodeContext("Check", config, ExecutorType.Condition, new Dictionary<string, NodeOutput>()), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.GetData<ConditionResult>()!.Result.Should().BeFalse();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static string RenderedFetchXml(JsonObject node, IReadOnlyList<string> ids)
    {
        var outputs = new Dictionary<string, NodeOutput>
        {
            ["myMatters"] = NodeOutput.Ok(Guid.NewGuid(), "myMatters", new { ids, count = ids.Count }),
        };
        var rendered = PlaybookOrchestrationService.RenderConfigJsonStructurally(
            node["configJson"]!.ToJsonString(), Layer1Context(outputs), Engine, ExecutorType.QueryDataverse);
        var fetchXml = (string)JsonNode.Parse(rendered)!["fetchXml"]!;
        return QueryDataverseNodeExecutor.ResolveFetchXmlVariables(
            fetchXml, null, UserId.ToString(), new DateOnly(2026, 10, 8), new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
    }

    /// <summary>The context Layer 1 renders with: node outputs plus the parameters the scheduler passes each user.</summary>
    private static Dictionary<string, object?> Layer1Context(IReadOnlyDictionary<string, NodeOutput> outputs) =>
        PlaybookTemplateContextBuilder.Build(NodeContext("layer1", "{}", ExecutorType.Start, outputs));

    private static NodeExecutionContext NodeContext(
        string name, string configJson, ExecutorType type, IReadOnlyDictionary<string, NodeOutput> outputs)
    {
        var actionId = Guid.NewGuid();
        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = Guid.NewGuid(), PlaybookId = Guid.NewGuid(), ActionId = actionId, Name = name, ExecutionOrder = 1,
                OutputVariable = "out", ConfigJson = configJson, IsActive = true,
            },
            Action = new AnalysisAction { Id = actionId, Name = name },
            ExecutorType = type,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant",
            UserId = UserId,
            PreviousOutputs = outputs,
            Parameters = new Dictionary<string, string>
            {
                ["userId"] = UserId.ToString(), ["userName"] = HostileName, ["todayUtc"] = "2026-10-08",
                ["dueSoonWindowUtc"] = "2026-10-11", ["timeWindowHours"] = "24", ["dueWithinDays"] = "3",
                ["matterId"] = Guid.NewGuid().ToString(),
            },
        };
    }

    private static NodeExecutionContext QueryContext(string fetchXml) =>
        NodeContext("Query Matter Activity", JsonSerializer.Serialize(new { entityLogicalName = "sprk_event", fetchXml }),
            ExecutorType.QueryDataverse, new Dictionary<string, NodeOutput>());

    private static IEnumerable<JsonObject> NodesOf(JsonObject definition) =>
        definition["nodes"]!.AsArray().Select(n => n!.AsObject());

    private static IEnumerable<JsonObject> QueryNodesOf(JsonObject definition) =>
        NodesOf(definition).Where(n => ConfigOf(n)?["fetchXml"] is JsonValue).Select(n =>
        {
            var copy = n.DeepClone().AsObject();
            copy["configJson"] = ConfigOf(n)!.DeepClone();
            return copy;
        });

    private static JsonObject? ConfigOf(JsonObject node) => (node["configJson"] ?? node["config"]) as JsonObject;

    private static IEnumerable<(string Node, string FetchXml)> QueriesOf(JsonObject definition) =>
        NodesOf(definition).SelectMany(n => FetchXmlStrings(ConfigOf(n)).Select(x => ((string?)n["name"] ?? "(unnamed)", x)));

    private static IEnumerable<string> FetchXmlStrings(JsonNode? value) => value switch
    {
        JsonObject o => o.SelectMany(p => p.Key == "fetchXml" && p.Value is JsonValue v && v.TryGetValue<string>(out var s)
            ? [s]
            : FetchXmlStrings(p.Value)),
        JsonArray a => a.SelectMany(FetchXmlStrings),
        _ => [],
    };

    private static List<string> FindPlaybooksWithFetchXml()
    {
        var found = new List<string>();
        foreach (var root in SearchRoots.Select(r => Path.Combine(RepoRoot(), r)).Where(Directory.Exists))
        {
            var pending = new Stack<string>([root]);
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                foreach (var sub in Directory.GetDirectories(dir).Where(d => !SkippedDirectories.Contains(Path.GetFileName(d))))
                {
                    pending.Push(sub);
                }

                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    if (!File.ReadAllText(file).Contains("\"fetchXml\"", StringComparison.Ordinal)) continue;
                    try
                    {
                        if (JsonNode.Parse(File.ReadAllText(file)) is JsonObject o && o["nodes"] is JsonArray && QueriesOf(o).Any())
                        {
                            found.Add(file);
                        }
                    }
                    catch (JsonException)
                    {
                        // Not a JSON document we can read (e.g. JSON-with-comments config); not a playbook definition.
                    }
                }
            }
        }

        return found;
    }

    private static JsonObject LoadDefinition(string relativePath) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), relativePath)))!.AsObject();

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Repository root (.git) not found above " + AppContext.BaseDirectory);
    }
}
