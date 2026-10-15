// ISS-018 (#1452, owner decision D-77) — LIVE proof of Dataverse's own FetchXML list semantics. READ-ONLY.
//
// Opt-in only (skip-via-return, the repo's convention — see Events/EventRoutesLiveTests): a plain `dotnet test` touches
// no network. Enable with
//   SPAARKE_LIVE_PLAYBOOKS_DATAVERSE_URL=https://<env>.crm.dynamics.com   (e.g. spaarkedev1)
// and an `az login` session for an identity that may read sprk_matter, sprk_event, sprk_document, sprk_communication and
// sprk_workassignment in that environment.
//
// For every repo notification playbook query, this test renders the query through the PRODUCTION Layer 1 renderer
// (PlaybookOrchestrationService.RenderConfigJsonStructurally) and the executor's variable resolution, with 0, 1 and
// many REAL matter ids, and executes it against Dataverse (Web API `?fetchXml=`, the same server-side FetchXML parser
// the BFF's SDK FetchExpression path reaches). It then asks Dataverse to convert the query to a QueryExpression and
// counts the values Dataverse itself read for the membership condition. The negative control runs the pre-fix comma
// form with the same real ids and expects Dataverse's "ConditionOperator.In is empty" fault.
// Nothing is written; no record is created.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.IntegrationTests.Playbooks;

public sealed class NotificationPlaybookFetchXmlLiveTests
{
    private const string UrlVar = "SPAARKE_LIVE_PLAYBOOKS_DATAVERSE_URL";
    private static readonly ITemplateEngine Engine = new TemplateEngine(NullLogger<TemplateEngine>.Instance);

    /// <summary>Reported as SKIPPED (not passed) when <see cref="UrlVar"/> is not set, so a run that never touched
    /// Dataverse cannot read as live evidence.</summary>
    private sealed class LivePlaybooksFactAttribute : FactAttribute
    {
        public LivePlaybooksFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UrlVar)))
                Skip = $"Live leg: set {UrlVar} to run it against a real environment (read-only).";
        }
    }

    private readonly ITestOutputHelper _out;

    public NotificationPlaybookFetchXmlLiveTests(ITestOutputHelper output) => _out = output;

    [LivePlaybooksFact]
    public async Task LiveMode_EveryRepoNotificationQuery_RunsWithZeroOneAndManyIds_AndTheCommaFormFails()
    {
        var url = Environment.GetEnvironmentVariable(UrlVar);
        if (string.IsNullOrWhiteSpace(url))
            return; // not opted in

        var credential = new AzureCliCredential();
        var token = await credential.GetTokenAsync(new TokenRequestContext([$"{url.TrimEnd('/')}/.default"]), default);
        using var dv = new HttpClient { BaseAddress = new Uri($"{url.TrimEnd('/')}/api/data/v9.2/") };
        dv.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        dv.DefaultRequestHeaders.Add("OData-Version", "4.0");

        var userId = (await dv.GetFromJsonAsync<JsonElement>("WhoAmI()")).GetProperty("UserId").GetGuid();
        var matters = (await dv.GetFromJsonAsync<JsonElement>("sprk_matters?$select=sprk_matterid&$top=15"))
            .GetProperty("value").EnumerateArray().Select(m => m.GetProperty("sprk_matterid").GetString()!).ToArray();
        matters.Length.Should().BeGreaterThan(1, "the environment needs at least two matters for the many-ids leg");

        var dir = Path.Combine(RepoRoot(), "projects", "spaarke-daily-update-service", "notes", "playbooks");
        var files = Directory.GetFiles(dir, "notification-*.json");
        files.Should().HaveCount(7);

        foreach (var file in files)
        {
            var definition = JsonNode.Parse(await File.ReadAllTextAsync(file))!.AsObject();
            var query = definition["nodes"]!.AsArray().Select(n => n!.AsObject()).Single(n => n["configJson"]?["fetchXml"] is not null);
            var entity = (string)query["configJson"]!["entityLogicalName"]!;
            var entitySet = (await dv.GetFromJsonAsync<JsonElement>($"EntityDefinitions(LogicalName='{entity}')?$select=EntitySetName"))
                .GetProperty("EntitySetName").GetString()!;

            foreach (var ids in new[] { Array.Empty<string>(), matters.Take(1).ToArray(), matters })
            {
                var fetchXml = Render(query, ids, userId);
                FetchXmlShapeValidator.Validate(fetchXml).Should().BeEmpty();

                using var response = await dv.GetAsync($"{entitySet}?fetchXml={Uri.EscapeDataString(fetchXml)}");
                var body = await response.Content.ReadAsStringAsync();
                response.StatusCode.Should().Be(HttpStatusCode.OK, $"{Path.GetFileName(file)} with {ids.Length} id(s): {body}");

                var valuesRead = await MembershipValuesDataverseReadsAsync(dv, fetchXml);
                valuesRead.Should().Be(Math.Max(ids.Length, 1),
                    "Dataverse reads one value per id (the impossible match when the list is empty)");
                _out.WriteLine($"{Path.GetFileName(file)}: {ids.Length} id(s) -> 200, Dataverse read {valuesRead} value(s), {JsonDocument.Parse(body).RootElement.GetProperty("value").GetArrayLength()} row(s)");
            }

            // Negative control: the pre-fix comma form with the same REAL ids is an empty list to Dataverse.
            var comma = XDocument.Parse(Render(query, matters, userId));
            foreach (var list in comma.Descendants("condition").Where(c => (string?)c.Attribute("operator") == "in"
                         && c.Attribute("attribute")?.Value is "sprk_regardingmatter" or "sprk_matter"))
            {
                list.SetAttributeValue("value", string.Join(",", matters));
                list.RemoveNodes();
            }

            using var old = await dv.GetAsync($"{entitySet}?fetchXml={Uri.EscapeDataString(comma.ToString(SaveOptions.DisableFormatting))}");
            var oldBody = await old.Content.ReadAsStringAsync();
            old.StatusCode.Should().Be(HttpStatusCode.BadRequest, Path.GetFileName(file));
            oldBody.Should().Contain("ConditionOperator.In is empty");
            _out.WriteLine($"{Path.GetFileName(file)}: comma form with {matters.Length} real ids -> 400 'ConditionOperator.In is empty'");
        }
    }

    private static string Render(JsonObject query, IReadOnlyList<string> ids, Guid userId)
    {
        var outputs = new Dictionary<string, NodeOutput>
        {
            ["myMatters"] = NodeOutput.Ok(Guid.NewGuid(), "myMatters", new { ids, count = ids.Count }),
        };
        var actionId = Guid.NewGuid();
        var context = PlaybookTemplateContextBuilder.Build(new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto { Id = Guid.NewGuid(), PlaybookId = Guid.NewGuid(), ActionId = actionId, Name = "layer1", ExecutionOrder = 1, OutputVariable = "x", IsActive = true },
            Action = new AnalysisAction { Id = actionId, Name = "layer1" },
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "live",
            UserId = userId,
            PreviousOutputs = outputs,
            Parameters = new Dictionary<string, string>
            {
                ["todayUtc"] = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
                ["dueSoonWindowUtc"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3).ToString("yyyy-MM-dd"),
                ["timeWindowHours"] = "24",
                ["dueWithinDays"] = "3",
            },
        });
        var rendered = PlaybookOrchestrationService.RenderConfigJsonStructurally(
            query["configJson"]!.ToJsonString(), context, Engine, ExecutorType.QueryDataverse);
        var fetchXml = (string)JsonNode.Parse(rendered)!["fetchXml"]!;
        fetchXml = QueryDataverseNodeExecutor.ResolveFetchXmlVariables(
            fetchXml, null, userId.ToString(), DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow);
        // The executor's eq-userid rewrite (private there): the Web API would resolve eq-userid to the CALLER instead.
        return fetchXml.Replace("operator=\"eq-userid\"", $"operator=\"eq\" value=\"{userId}\"", StringComparison.Ordinal)
            .Replace("operator=\"ne-userid\"", $"operator=\"ne\" value=\"{userId}\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// Asks Dataverse to convert the FetchXML to a QueryExpression and returns how many values it read for the
    /// membership <c>in</c> condition — Dataverse's own parse, not ours.
    /// </summary>
    private static async Task<int> MembershipValuesDataverseReadsAsync(HttpClient dv, string fetchXml)
    {
        using var response = await dv.GetAsync($"FetchXmlToQueryExpression(FetchXml=@p)?@p='{Uri.EscapeDataString(fetchXml.Replace("'", "''", StringComparison.Ordinal))}'");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var counts = new List<int>();
        Walk(JsonNode.Parse(body)!["Query"]);
        counts.Should().ContainSingle("exactly one membership `in` condition per query");
        return counts[0];

        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject o:
                    if ((string?)o["AttributeName"] is "sprk_regardingmatter" or "sprk_matter" && (string?)o["Operator"] == "In" && o["Values"] is JsonArray values)
                        counts.Add(values.Count);
                    foreach (var (_, child) in o) Walk(child);
                    break;
                case JsonArray a:
                    foreach (var child in a) Walk(child);
                    break;
            }
        }
    }

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
