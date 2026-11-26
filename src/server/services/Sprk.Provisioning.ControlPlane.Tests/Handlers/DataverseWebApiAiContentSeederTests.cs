// -----------------------------------------------------------------------------
// DataverseWebApiAiContentSeederTests.cs
//
// L56 / demo plan W2: H12a writes the exported AI content set with FIXED primary
// keys, create-if-missing, in dependency order (types → tools → skills →
// knowledge → actions → playbooks → playbook N:N → nodes → node N:N → canvas →
// routing rows), and REFUSES a set with a missing dependency before any call.
// ADR-038: a real HttpClient over a hand-rolled in-memory Dataverse (no
// Mock<HttpMessageHandler>), parity with the sibling seeder tests.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed partial class DataverseWebApiAiContentSeederTests
{
    private const string EnvUrl = "https://acme.crm.dynamics.com/";

    private const string ToolType = "10000000-0000-f011-8406-000000000001";
    private const string Tool = "20000000-0000-f011-8406-000000000002";
    private const string Skill = "30000000-0000-f011-8406-000000000003";
    private const string Knowledge = "40000000-0000-f011-8406-000000000004";
    private const string Action = "50000000-0000-f011-8406-000000000005";
    private const string Playbook = "60000000-0000-f011-8406-000000000006";
    private const string Node = "70000000-0000-f011-8406-000000000007";
    private const string Consumer = "80000000-0000-f011-8406-000000000008";
    private const string Other = "99999999-0000-f011-8406-000000000009";

    // ---- dependency order ------------------------------------------------------------------------------------------

    [Fact]
    public async Task W2_AFreshEnvironment_IsWrittenInDependencyOrder_WithTheSourceIds()
    {
        var dataverse = new InMemoryDataverse();

        var outcome = await Seed(dataverse, Files());

        outcome.Should().BeOfType<AiContentSeedOutcome.Success>();
        dataverse.Writes.Should().Equal(
            "POST sprk_aitooltypes",
            "POST sprk_analysistools",
            "POST sprk_analysisskills",
            "POST sprk_analysisknowledges",
            "POST sprk_analysisactions",
            "POST sprk_analysisplaybooks",
            "LINK sprk_analysisplaybook_action",
            "LINK sprk_playbook_skill",
            "POST sprk_playbooknodes",
            "LINK sprk_playbooknode_tool",
            "PATCH sprk_analysisplaybooks canvas",
            "POST sprk_playbookconsumers");

        dataverse.Row("sprk_analysisplaybooks", Playbook).Should().NotBeNull("the playbook keeps its source id");
        var node = dataverse.Body("sprk_playbooknodes", Node);
        node.GetProperty("sprk_playbooknodeid").GetString().Should().Be(Node);
        node.GetProperty("sprk_playbookid@odata.bind").GetString().Should().Be($"/sprk_analysisplaybooks({Playbook})");
        node.GetProperty("sprk_actionid@odata.bind").GetString().Should().Be($"/sprk_analysisactions({Action})");
        node.TryGetProperty("sprk_conditionjson", out _).Should().BeFalse("a null source value is not sent");
        dataverse.Body("sprk_analysistools", Tool).GetProperty("sprk_ToolTypeId@odata.bind").GetString()
            .Should().Be($"/sprk_aitooltypes({ToolType})");
        dataverse.Body("sprk_analysisactions", Action).GetProperty("sprk_systemprompt").GetString()
            .Should().Be("line one\nline two", "the export's line array is joined with LF");
        var consumer = dataverse.Body("sprk_playbookconsumers", Consumer);
        consumer.GetProperty("sprk_Playbook@odata.bind").GetString().Should().Be($"/sprk_analysisplaybooks({Playbook})");
        consumer.GetProperty("sprk_Action@odata.bind").GetString().Should().Be($"/sprk_analysisactions({Action})");
        dataverse.Canvas(Playbook).Should().Be("{\"nodes\":[]}");
    }

    // ---- idempotence -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task W2_ASecondRun_WritesNothing()
    {
        var dataverse = new InMemoryDataverse();
        await Seed(dataverse, Files());
        dataverse.Writes.Clear();

        var outcome = await Seed(dataverse, Files());

        var success = outcome.Should().BeOfType<AiContentSeedOutcome.Success>().Subject;
        dataverse.Writes.Should().BeEmpty("every row, link and canvas is already there");
        success.Summary.Should().Contain("0 row(s) created").And.Contain("0 link(s) created").And.Contain("0 canvas(es) written")
            .And.Contain("nothing to report");
    }

    [Fact]
    public async Task W2_ARunThatStoppedAfterThePlaybook_IsCompletedByTheNextRun()
    {
        var dataverse = new InMemoryDataverse { FailPostTo = "sprk_playbooknodes" };
        var first = await Seed(dataverse, Files());
        first.Should().BeOfType<AiContentSeedOutcome.Failed>().Which.Diagnostic.Should().Contain("sprk_playbooknodes");

        dataverse.FailPostTo = null;
        dataverse.Writes.Clear();
        var second = await Seed(dataverse, Files());

        second.Should().BeOfType<AiContentSeedOutcome.Success>();
        dataverse.Writes.Should().Equal(
            "POST sprk_playbooknodes", "LINK sprk_playbooknode_tool", "PATCH sprk_analysisplaybooks canvas", "POST sprk_playbookconsumers");
    }

    [Fact]
    public async Task W2_AnExistingRow_IsNeverUpdated_DifferencesAreReported()
    {
        var dataverse = new InMemoryDataverse();
        await Seed(dataverse, Files());
        dataverse.Row("sprk_analysisactions", Action)!["sprk_systemprompt"] = "tuned by the customer";
        dataverse.Row("sprk_analysisplaybooks", Playbook)!["sprk_canvaslayoutjson"] = "{\"maker\":true}";
        dataverse.Writes.Clear();

        var outcome = await Seed(dataverse, Files());

        var success = outcome.Should().BeOfType<AiContentSeedOutcome.Success>().Subject;
        dataverse.Writes.Should().BeEmpty();
        success.Summary.Should().Contain($"sprk_analysisaction/{Action} 'Summarize' differs in sprk_systemprompt");
        dataverse.Canvas(Playbook).Should().Be("{\"maker\":true}", "a canvas is only written when it is empty");
    }

    // ---- missing dependency ------------------------------------------------------------------------------------------

    [Fact]
    public async Task W2_ANodeWhosePlaybookIsNotInTheSet_IsRefused_BeforeAnyCall()
    {
        var files = Files(nodePlaybook: Other);
        var dataverse = new InMemoryDataverse();
        var credential = new CountingCredential();

        var outcome = await Seed(dataverse, files, credential);

        var refused = outcome.Should().BeOfType<AiContentSeedOutcome.Refused>().Subject;
        refused.Diagnostic.Should().Contain($"sprk_analysisplaybook/{Other}").And.Contain("nothing was written");
        dataverse.Requests.Should().Be(0);
        credential.Calls.Should().Be(0, "a refused set never signs in to the environment");
    }

    [Fact]
    public async Task W2_ALinkToARowNotInTheSet_IsRefused()
    {
        var files = Files();
        files["associations/sprk_playbook_skill.json"] = Link("sprk_playbook_skill", "sprk_analysisplaybook", "sprk_analysisskill", (Playbook, Other));
        var dataverse = new InMemoryDataverse();

        var outcome = await Seed(dataverse, files);

        outcome.Should().BeOfType<AiContentSeedOutcome.Refused>().Which.Diagnostic.Should().Contain(Other);
        dataverse.Requests.Should().Be(0);
    }

    [Fact]
    public async Task W2_AnOrderThatWritesNodesBeforePlaybooks_IsRefused()
    {
        var phases = AiContentSet.Phases.Where(p => p is not AiContentPhase.Rows { Table: "sprk_playbooknode" }).ToList();
        phases.Insert(phases.FindIndex(p => p is AiContentPhase.Rows { Table: "sprk_analysisplaybook" }), new AiContentPhase.Rows("sprk_playbooknode"));
        var set = AiContentSet.FromFiles(Files(), phases);

        set.Validate().Should().Contain(p => p.Contains("sprk_playbooknode.sprk_playbookid points at sprk_analysisplaybook, which is not written before"));
        AiContentSet.FromFiles(Files()).Validate().Should().BeEmpty("the production order is valid");
    }

    // ---- collisions ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task W2_APlaybookAlreadyThereUnderAnotherId_IsNotCreated_AndDependentRowsLinkToIt()
    {
        var dataverse = new InMemoryDataverse();
        dataverse.Seed("sprk_analysisplaybooks", "sprk_analysisplaybookid", Other, new() { ["sprk_name"] = "  DOCUMENT PROFILE " });
        dataverse.Seed("sprk_aitooltypes", "sprk_aitooltypeid", Other, new() { ["sprk_name"] = "Analysis" });

        var outcome = await Seed(dataverse, Files());

        var success = outcome.Should().BeOfType<AiContentSeedOutcome.Success>().Subject;
        dataverse.Row("sprk_analysisplaybooks", Playbook).Should().BeNull("the customer's row holds the name");
        dataverse.Body("sprk_playbooknodes", Node).GetProperty("sprk_playbookid@odata.bind").GetString()
            .Should().Be($"/sprk_analysisplaybooks({Other})");
        dataverse.Writes.Should().NotContain(w => w.StartsWith("LINK sprk_analysisplaybook_action", StringComparison.Ordinal)
                                                 || w.StartsWith("LINK sprk_playbook_skill", StringComparison.Ordinal),
            "a customer's playbook found by name gets no links from the seed");
        dataverse.Writes.Should().NotContain("PATCH sprk_analysisplaybooks canvas");
        success.Summary.Should().Contain("COLLISION").And.Contain(Other);
        dataverse.Body("sprk_analysistools", Tool).GetProperty("sprk_ToolTypeId@odata.bind").GetString()
            .Should().Be($"/sprk_aitooltypes({Other})", "a type row matches by name");
        success.Summary.Should().NotContain("sprk_aitooltype '", "a type matched by name is expected, not reported");
    }

    // ---- the embedded export ---------------------------------------------------------------------------------------------

    [Fact]
    public void W2_TheEmbeddedExport_IsComplete_AndHasNoMissingDependency()
    {
        var set = AiContentSet.Embedded;

        set.Validate().Should().BeEmpty();
        set.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");
        set.Associations.Select(a => a.Relationship).Should().BeEquivalentTo(AiContentSet.RelationshipNames);
        var playbooks = set.Tables["sprk_analysisplaybook"].Rows.Select(r => r.Id.ToString()).ToList();
        playbooks.Should().Contain(new[]
        {
            "18cf3cc8-02ec-f011-8406-7c1e520aa4df", // PB-002 Document Profile (useAiSummary.ts)
            "4a72f99c-a119-f111-8343-7ced8d1dc988", // PB-015 Summarize File (DocumentEmailWizard, SemanticSearch, DocumentRelationshipViewer)
        });
        set.Tables["sprk_analysisplaybook"].Rows.Should().OnlyContain(r =>
            r.Values["sprk_playbookid"].ValueKind == JsonValueKind.Null || r.Values["sprk_playbookid"].GetString() == r.Id.ToString(),
            "where set, the stable-id alternate key mirrors the primary key (a GUID lookup resolves by primary key either way)");
        set.Tables["sprk_playbookconsumer"].Rows.Should().Contain(r => r.Values["sprk_consumertype"].GetString() == "summarize-file");
        set.Tables["sprk_playbooknode"].Lookups.Select(l => l.Column).Should().NotContain("sprk_modeldeploymentid",
            "model deployments are H12c's rows with stamp-generated ids");
    }

    [Fact]
    public void W2_EveryExportedFile_IsEmbedded()
    {
        var folder = Path.Combine(RepoRoot(), "scripts", "seed-data", "ai-content");
        var onDisk = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).Length;
        var embedded = typeof(AiContentSet).Assembly.GetManifestResourceNames().Count(n => n.StartsWith(AiContentSet.ResourcePrefix, StringComparison.Ordinal));
        embedded.Should().Be(onDisk);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------

    private static async Task<AiContentSeedOutcome> Seed(InMemoryDataverse dataverse, Dictionary<string, string> files, CountingCredential? credential = null)
    {
        credential ??= new CountingCredential();
        var seeder = new DataverseWebApiAiContentSeeder(
            new HttpClient(dataverse),
            Options.Create(new AiSeedChainOptions()),
            NullLogger<DataverseWebApiAiContentSeeder>.Instance,
            _ => credential,
            () => AiContentSet.FromFiles(files));
        return await seeder.SeedAsync(new SeedManifestInvocationRequest("acme", "tenant", EnvUrl), CancellationToken.None);
    }

    /// <summary>A minimal set: one row in most tables, every lookup kind, links from both sources, one canvas.</summary>
    private static Dictionary<string, string> Files(string nodePlaybook = Playbook)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sprk_aitooltype.json"] = Table("sprk_aitooltype", "sprk_aitooltypes", "naturalKey", [["sprk_name"]], ["sprk_name"], [],
                (ToolType, new() { ["sprk_name"] = "analysis" })),
            ["sprk_aiskilltype.json"] = Table("sprk_aiskilltype", "sprk_aiskilltypes", "naturalKey", [["sprk_name"]], ["sprk_name"], []),
            ["sprk_aiknowledgetype.json"] = Table("sprk_aiknowledgetype", "sprk_aiknowledgetypes", "naturalKey", [["sprk_name"]], ["sprk_name"], []),
            ["sprk_aioutputtype.json"] = Table("sprk_aioutputtype", "sprk_aioutputtypes", "naturalKey", [["sprk_name"]], ["sprk_name"], []),
            ["sprk_analysistool.json"] = Table("sprk_analysistool", "sprk_analysistools", "fixedId", [["sprk_toolcode"], ["sprk_name"]],
                ["sprk_name", "sprk_toolcode"], [("sprk_tooltypeid", "sprk_ToolTypeId", "sprk_aitooltype")],
                (Tool, new() { ["sprk_name"] = "Extractor", ["sprk_toolcode"] = "EXT@v1", ["sprk_tooltypeid"] = ToolType })),
            ["sprk_analysisskill.json"] = Table("sprk_analysisskill", "sprk_analysisskills", "fixedId", [["sprk_name"]], ["sprk_name"], [],
                (Skill, new() { ["sprk_name"] = "Clause reading" })),
            ["sprk_analysisknowledge.json"] = Table("sprk_analysisknowledge", "sprk_analysisknowledges", "fixedId", [["sprk_name"]], ["sprk_name"], [],
                (Knowledge, new() { ["sprk_name"] = "Standards" })),
            ["sprk_analysisaction.json"] = Table("sprk_analysisaction", "sprk_analysisactions", "fixedId", [["sprk_actioncode"], ["sprk_name"]],
                ["sprk_actioncode", "sprk_name", "sprk_systemprompt", "sprk_temperature"], [],
                (Action, new() { ["sprk_actioncode"] = "SUM@v1", ["sprk_name"] = "Summarize", ["sprk_systemprompt"] = new[] { "line one", "line two" }, ["sprk_temperature"] = 0.2 })),
            ["sprk_analysisplaybook.json"] = Table("sprk_analysisplaybook", "sprk_analysisplaybooks", "fixedId", [["sprk_playbookid"], ["sprk_name"]],
                ["sprk_name", "sprk_playbookid"], [],
                (Playbook, new() { ["sprk_name"] = "Document Profile", ["sprk_playbookid"] = Playbook })),
            ["sprk_playbooknode.json"] = Table("sprk_playbooknode", "sprk_playbooknodes", "fixedId", [], ["sprk_conditionjson", "sprk_name"],
                [("sprk_actionid", "sprk_actionid", "sprk_analysisaction"), ("sprk_playbookid", "sprk_playbookid", "sprk_analysisplaybook")],
                (Node, new() { ["sprk_conditionjson"] = null, ["sprk_name"] = "Analyze", ["sprk_actionid"] = Action, ["sprk_playbookid"] = nodePlaybook })),
            ["sprk_playbookconsumer.json"] = Table("sprk_playbookconsumer", "sprk_playbookconsumers", "fixedId",
                [["sprk_consumertype", "sprk_consumercode", "sprk_environment"]],
                ["sprk_consumercode", "sprk_consumertype", "sprk_environment", "sprk_name"],
                [("sprk_action", "sprk_Action", "sprk_analysisaction"), ("sprk_playbook", "sprk_Playbook", "sprk_analysisplaybook")],
                (Consumer, new()
                {
                    ["sprk_consumercode"] = "default",
                    ["sprk_consumertype"] = "document-profile",
                    ["sprk_environment"] = "*",
                    ["sprk_name"] = "Document profile",
                    ["sprk_action"] = Action,
                    ["sprk_playbook"] = Playbook
                })),
            ["associations/sprk_analysisplaybook_action.json"] = Link("sprk_analysisplaybook_action", "sprk_analysisplaybook", "sprk_analysisaction", (Playbook, Action)),
            ["associations/sprk_playbook_skill.json"] = Link("sprk_playbook_skill", "sprk_analysisplaybook", "sprk_analysisskill", (Playbook, Skill)),
            ["associations/sprk_playbooknode_tool.json"] = Link("sprk_playbooknode_tool", "sprk_playbooknode", "sprk_analysistool", (Node, Tool)),
            [AiContentSet.CanvasFile] = JsonSerializer.Serialize(new
            {
                table = "sprk_analysisplaybook",
                column = "sprk_canvaslayoutjson",
                rows = new[] { new Dictionary<string, string> { ["sprk_analysisplaybookid"] = Playbook, ["sprk_canvaslayoutjson"] = "{\"nodes\":[]}" } },
            }),
        };
        return files;
    }

    private static string Table(
        string table, string entitySet, string match, string[][] matchKeys, string[] columns,
        (string Column, string Nav, string Target)[] lookups, params (string Id, Dictionary<string, object?> Values)[] rows)
    {
        var primaryKey = $"{table}id";
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["table"] = table,
            ["entitySet"] = entitySet,
            ["primaryKey"] = primaryKey,
            ["nameColumn"] = "sprk_name",
            ["match"] = match,
            ["matchKeys"] = matchKeys,
            ["columns"] = columns,
            ["lookups"] = lookups.Select(l => new { column = l.Column, navigationProperty = l.Nav, target = l.Target }).ToArray(),
            ["rows"] = rows.Select(r =>
            {
                var row = new Dictionary<string, object?> { [primaryKey] = r.Id };
                foreach (var (k, v) in r.Values)
                {
                    row[k] = v;
                }

                return row;
            }).ToArray(),
        });
    }

    private static string Link(string relationship, string source, string target, params (string Source, string Target)[] pairs)
        => JsonSerializer.Serialize(new
        {
            relationship,
            source,
            navigationProperty = relationship,
            target,
            pairs = pairs.Select(p => new { source = p.Source, target = p.Target }).ToArray(),
        });

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "scripts", "seed-data")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above the test output folder.");
    }

    private sealed class CountingCredential : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            return new AccessToken("token", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    /// <summary>
    /// An in-memory Dataverse Web API: collection GET (with $filter on the key), POST create, $ref associate, collection-valued
    /// navigation GET, PATCH. It records every write as "POST set", "LINK relationship" or "PATCH set canvas".
    /// </summary>
    private sealed partial class InMemoryDataverse : HttpMessageHandler
    {
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, object?>>> _tables = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonElement> _bodies = new(StringComparer.Ordinal);
        private readonly HashSet<string> _links = new(StringComparer.Ordinal);

        public List<string> Writes { get; } = [];
        public int Requests { get; private set; }
        public string? FailPostTo { get; set; }

        public void Seed(string set, string primaryKey, string id, Dictionary<string, object?> values)
        {
            values[primaryKey] = id;
            values["statecode"] = 0;
            Table(set)[id] = values;
        }

        public Dictionary<string, object?>? Row(string set, string id) => Table(set).GetValueOrDefault(id);

        public JsonElement Body(string set, string id) => _bodies[$"{set}/{id}"];

        public string? Canvas(string id) => Row("sprk_analysisplaybooks", id)?.GetValueOrDefault("sprk_canvaslayoutjson") as string;

        private Dictionary<string, Dictionary<string, object?>> Table(string set)
        {
            if (!_tables.TryGetValue(set, out var table))
            {
                _tables[set] = table = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
            }

            return table;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath)["/api/data/v9.2/".Length..];
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (request.Method == HttpMethod.Get && NavigationPath().Match(path) is { Success: true } nav)
            {
                var source = $"{nav.Groups["set"].Value}({nav.Groups["id"].Value})/{nav.Groups["nav"].Value}";
                var pk = Regex.Match(query, @"\$select=(\w+)").Groups[1].Value;
                var rows = _links.Where(l => l.StartsWith(source + "|", StringComparison.Ordinal))
                    .Select(l => new Dictionary<string, object?> { [pk] = l.Split('|')[1] });
                return Json(new { value = rows });
            }

            if (request.Method == HttpMethod.Get)
            {
                var filter = Regex.Match(query, @"\$filter=\w+ eq ([0-9a-f-]{36})");
                IEnumerable<Dictionary<string, object?>> rows = !filter.Success
                    ? Table(path).Values
                    : Table(path).TryGetValue(filter.Groups[1].Value, out var one) ? [one] : [];
                return Json(new { value = rows });
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/$ref", StringComparison.Ordinal))
            {
                var nav2 = NavigationPath().Match(path[..^"/$ref".Length]);
                var target = Regex.Match(JsonDocument.Parse(body!).RootElement.GetProperty("@odata.id").GetString()!, @"\(([0-9a-f-]{36})\)$").Groups[1].Value;
                _links.Add($"{nav2.Groups["set"].Value}({nav2.Groups["id"].Value})/{nav2.Groups["nav"].Value}|{target}");
                Writes.Add($"LINK {nav2.Groups["nav"].Value}");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post)
            {
                if (FailPostTo == path)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":\"busy\"}") };
                }

                var element = JsonDocument.Parse(body!).RootElement.Clone();
                var id = element.EnumerateObject().First(p => p.Name.EndsWith("id", StringComparison.Ordinal) && !p.Name.Contains('@', StringComparison.Ordinal)).Value.GetString()!;
                var row = new Dictionary<string, object?> { ["statecode"] = 0 };
                foreach (var property in element.EnumerateObject().Where(p => !p.Name.Contains('@', StringComparison.Ordinal)))
                {
                    row[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetDecimal(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => null,
                    };
                }

                Table(path)[id] = row;
                _bodies[$"{path}/{id}"] = element;
                Writes.Add($"POST {path}");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Patch)
            {
                var key = Regex.Match(path, @"^(?<set>\w+)\((?<id>[0-9a-f-]{36})\)$");
                var row = Row(key.Groups["set"].Value, key.Groups["id"].Value)!;
                foreach (var property in JsonDocument.Parse(body!).RootElement.EnumerateObject())
                {
                    row[property.Name] = property.Value.GetString();
                }

                Writes.Add($"PATCH {key.Groups["set"].Value} canvas");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        private static HttpResponseMessage Json(object value)
            => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

        [GeneratedRegex(@"^(?<set>\w+)\((?<id>[0-9a-f-]{36})\)/(?<nav>\w+)$")]
        private static partial Regex NavigationPath();
    }
}
