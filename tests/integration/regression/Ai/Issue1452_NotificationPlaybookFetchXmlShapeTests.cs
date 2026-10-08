// Regression: ISS-018 (#1452). Every notification playbook in spaarkedev1 failed for every user for 89+ days, silently:
//   (1) `<condition operator="in" value="{{joinIds myMatters.ids}}"/>` — Dataverse ignores the value attribute of a list
//       operator, so the list is EMPTY and the query fails ("The value passed for ConditionOperator.In is empty");
//   (2) Layer 1 renders `"left": "{{q.output.count}}"` to a JSON number and ConditionExpression.Left was a string;
//   (3) Layer 1 rendered CreateNotification's `{{item.*}}` templates (empty) before the executor's per-item loop;
//   (4) `{{item.m_sprk_mattername}}` never resolved — an aliased column is keyed "m.sprk_mattername".
// These tests run the REPO definitions through the production render path and executors, and each pins the old form
// failing. No test before this executed a real list condition; the one Dataverse simulator split the commas itself.
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

    public static TheoryData<string> NotificationPlaybooks()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(
                     Path.Combine(RepoRoot(), "projects", "spaarke-daily-update-service", "notes", "playbooks"), "notification-*.json"))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [Fact]
    public void Discovery_FindsAllSevenNotificationPlaybooks_AndTheInsightsPlaybook()
    {
        var found = FindPlaybooksWithFetchXml().Select(Path.GetFileName).ToList();
        found.Count(f => f!.StartsWith("notification-", StringComparison.Ordinal)).Should().Be(7);
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
    public void EveryRepoPlaybook_RenderedQuery_IsWellShaped_ForZeroOneAndManyMemberships(string relativePath)
    {
        var definition = LoadDefinition(relativePath);
        foreach (var ids in new[] { Array.Empty<string>(), [Guid.NewGuid().ToString()], Enumerable.Range(0, 17).Select(_ => Guid.NewGuid().ToString()).ToArray() })
        {
            foreach (var node in QueryNodesOf(definition))
            {
                var rendered = RenderedFetchXml(node, ids);
                FetchXmlShapeValidator.Validate(rendered).Should().BeEmpty($"{relativePath} '{node["name"]}' with {ids.Length} id(s)");

                foreach (var list in XDocument.Parse(rendered).Descendants("condition")
                             .Where(c => (string?)c.Attribute("operator") == "in" && c.Attribute("attribute")?.Value is "sprk_regardingmatter" or "sprk_matter"))
                {
                    var expected = ids.Length == 0 ? [ImpossibleMatch] : ids.OrderBy(i => i, StringComparer.Ordinal).ToArray();
                    list.Elements("value").Select(v => v.Value).Should().Equal(expected,
                        "one <value> child per membership id; an empty list selects nothing instead of failing the run");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(NotificationPlaybooks))]
    public void TheOldCommaForm_FailsTheSameChecks(string file)
    {
        // Put today's master shape back into the current definition: both the authored and the rendered check must fail.
        var text = File.ReadAllText(NotificationPath(file)).Replace(
            "operator=\\\"in\\\">{{fetchInGuids myMatters.ids}}</condition>",
            "operator=\\\"in\\\" value=\\\"{{joinIds myMatters.ids}}\\\"/>",
            StringComparison.Ordinal);
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

    [Theory]
    [MemberData(nameof(NotificationPlaybooks))]
    public async Task TheConditionNode_EvaluatesALayer1RenderedNumericLeft(string file)
    {
        var definition = LoadDefinition(NotificationRelative(file));
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

    // ── (3)+(4) CreateNotification: item templates survive Layer 1 and render per item ───────

    [Theory]
    [MemberData(nameof(NotificationPlaybooks))]
    public async Task CreateNotification_RendersEachItemsTitleRegardingAndMatterName_AfterLayer1(string file)
    {
        var definition = LoadDefinition(NotificationRelative(file));
        var create = NodesOf(definition).Single(n => (int?)n["executorType"] == 50);
        var query = QueryNodesOf(definition).Single();
        var queryVar = (string)query["outputVariable"]!;
        var entityName = XDocument.Parse((string)query["configJson"]!["fetchXml"]!).Descendants("entity").First().Attribute("name")!.Value;
        var recordId = Guid.NewGuid();
        var matterId = Guid.NewGuid();

        // One query row as QueryDataverseNodeExecutor emits it: every queried attribute, the aliased matter name keyed
        // "m.sprk_mattername".
        var item = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [entityName + "id"] = recordId.ToString() };
        foreach (var attribute in XDocument.Parse((string)query["configJson"]!["fetchXml"]!).Descendants("entity").First().Elements("attribute"))
        {
            item[attribute.Attribute("name")!.Value] = "v-" + attribute.Attribute("name")!.Value;
        }

        item[entityName + "id"] = recordId.ToString();
        item[entityName == "sprk_document" ? "sprk_matter" : "sprk_regardingmatter"] = matterId.ToString();
        item["ownerid"] = UserId.ToString();
        item["m.sprk_mattername"] = "Acme v. Beta";
        var outputs = new Dictionary<string, NodeOutput>
        {
            ["myMatters"] = NodeOutput.Ok(Guid.NewGuid(), "myMatters", new { ids = new[] { matterId.ToString() }, count = 1, byRole = new Dictionary<string, string[]>() }),
            [queryVar] = NodeOutput.Ok(Guid.NewGuid(), queryVar, new { count = 1, items = new[] { item } }),
        };

        var layer1 = PlaybookOrchestrationService.RenderConfigJsonStructurally(
            create["configJson"]!.ToJsonString(), Layer1Context(outputs), Engine, ExecutorType.CreateNotification);
        var renderedItemConfig = JsonNode.Parse(layer1)!["itemNotification"]!;
        ((string)renderedItemConfig["regardingId"]!).Should().Contain("{{item.", "Layer 1 leaves executor-scoped templates for the per-item loop");

        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EntityCollection());
        var created = new List<Entity>();
        entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created.Add(e)).ReturnsAsync(Guid.NewGuid());
        var executor = new CreateNotificationNodeExecutor(Engine, entities.Object, NullLogger<CreateNotificationNodeExecutor>.Instance);

        var result = await executor.ExecuteAsync(
            NodeContext("Create Notification", layer1, ExecutorType.CreateNotification, outputs), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var notification = created.Should().ContainSingle().Subject;
        notification.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(UserId);
        notification.GetAttributeValue<string>("title").Should().Contain("v-", "the per-item title rendered from the item");
        notification.GetAttributeValue<string>("sprk_regardingid").Should().Be(recordId.ToString());
        notification.GetAttributeValue<string>("data").Should().Contain("Acme v. Beta", "viaMatter.name reads the aliased column");
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

    private static string NotificationRelative(string file) => "projects/spaarke-daily-update-service/notes/playbooks/" + file;

    private static string NotificationPath(string file) => Path.Combine(RepoRoot(), NotificationRelative(file));

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
