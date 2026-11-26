// CONTRACT — spaarke_list_tasks and spaarke_get_task over /mcp/legal-operations (spaarkeai-mcp-bff-api-r2 task 025, FR-07 read half, NFR-02,
// NFR-05, NFR-07, ADR-015, ADR-028 A10). KEEP path: endpoint-contract.
//
// WHAT RUNS: the PRODUCTION AddMcpModule / MapMcpEndpoints on a real Kestrel listener behind a real JwtBearer scheme, serving the production
// tools next to the REAL events REST routes (MapEventEndpoints), over the REAL EventsCapability and DataverseWebApiService on task 006's
// row-secured Dataverse double (TaskToolsHost). "The tool's ids equal GET /api/v1/events" is compared over the wire with the same token.
// The pure parts (the size bound, the argument mapping, the slim projection) run on the tools' own methods; the fault paths run on a
// hand-written IEventsCapability, because Dataverse's double cannot fault.
// NOT HERE: A10 token refusals (tests/integration/auth/Mcp, route-wide); another caller's private event (tests/integration/auth/Mcp/Tasks/
// TaskReadToolsMustNotLeakTests.cs); the capability's own proofs (tests/integration/auth/Events/EventCallerScopeTests.cs).

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Sprk.Bff.Api.Api.Events.Dtos;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Tasks;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Events;
using Sprk.Bff.Api.Tests.Auth.Events;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Tasks;

public sealed class TaskReadToolsContractTests
{
    private const int MaxBytes = 64 * 1024;

    // ---- tools/list ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_ShowsBothToolsReadOnlyAndModelVisible_WithAUseForNotForDescription_AndAClosedSchema()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var list = await host.RpcAsync("A", "tools/list", new { });

        var tools = list.GetProperty("tools").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!);
        tools.Keys.Should().BeEquivalentTo("spaarke_list_tasks", "spaarke_get_task");
        foreach (var (name, tool) in tools)
        {
            var annotations = tool.GetProperty("annotations");
            annotations.GetProperty("readOnlyHint").GetBoolean().Should().BeTrue(name);
            annotations.GetProperty("destructiveHint").GetBoolean().Should().BeFalse(name);
            annotations.GetProperty("idempotentHint").GetBoolean().Should().BeTrue(name);
            tool.TryGetProperty("_meta", out _).Should().BeFalse($"{name} has no widget and no app-only visibility: the model calls it");
            tool.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
            var description = tool.GetProperty("description").GetString()!;
            description.Length.Should().BeLessThanOrEqualTo(1024, name);
            description.Should().Contain("Use for:").And.Contain("Not for:").And.Contain("Spaarke", because: "the description says these are Spaarke tasks, not calendar meetings");
            tool.GetProperty("inputSchema").GetProperty("additionalProperties").GetBoolean().Should().BeFalse(name);
        }

        tools["spaarke_list_tasks"].GetProperty("inputSchema").GetProperty("properties").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("status", "priority", "dueDateFrom", "dueDateTo", "regardingRecordType", "regardingRecordId", "pageNumber", "pageSize");
        tools["spaarke_get_task"].GetProperty("inputSchema").GetProperty("required").EnumerateArray().Select(r => r.GetString())
            .Should().Equal("taskId");
    }

    // ---- list: the ids are the REST route's ids ----------------------------------------------------------------------------

    [Theory]
    [InlineData("B", null, "")]
    [InlineData("B", "{\"pageSize\":3}", "?pageSize=3")]
    [InlineData("B", "{\"pageSize\":3,\"pageNumber\":2}", "?pageSize=3&pageNumber=2")]
    [InlineData("A", "{\"status\":\"open\",\"priority\":100000001,\"dueDateFrom\":\"2026-11-01\",\"dueDateTo\":\"2026-12-31\"}", "?status=open&priority=100000001&dueDateFrom=2026-11-01&dueDateTo=2026-12-31")]
    public async Task List_IdsAndCountAreTheRestRoutesIds_ForTheSameCallerAndFilters(string who, string? argumentsJson, string restQuery)
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());
        var rest = await host.RestJsonAsync(who, "/api/v1/events" + restQuery);

        var result = await host.CallAsync(who, "spaarke_list_tasks", argumentsJson is null ? new { } : JsonSerializer.Deserialize<JsonElement>(argumentsJson));

        IsError(result).Should().BeFalse(result.GetRawText());
        var body = result.GetProperty("structuredContent");
        body.GetProperty("tasks").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())
            .Should().Equal(rest["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()), "the tool's rows are the route's rows, in the route's order");
        body.GetProperty("totalCount").GetInt32().Should().Be(rest["totalCount"]!.GetValue<int>());
        body.GetProperty("pageNumber").GetInt32().Should().Be(rest["pageNumber"]!.GetValue<int>());
        body.GetProperty("hasNextPage").GetBoolean().Should().Be(rest["hasNextPage"]!.GetValue<bool>());
        body.GetProperty("count").GetInt32().Should().Be(rest["items"]!.AsArray().Count);
        body.GetProperty("truncated").GetBoolean().Should().BeFalse();
        // Both asked Dataverse AS the caller (the capability's list), once each.
        host.Dataverse.ListQueries.Should().HaveCount(2).And.OnlyContain(q => q.ImpersonatedUser != null);
    }

    [Fact]
    public async Task List_DefaultPage_IsTheRoutesDefault_Of50()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var result = await host.CallAsync("B", "spaarke_list_tasks", new { });

        result.GetProperty("structuredContent").GetProperty("pageSize").GetInt32().Should().Be(50);
        host.Dataverse.ListQueries.Should().ContainSingle().Which.Top.Should().Be(50);
    }

    // ---- get: the same event as REST, slimmed ---------------------------------------------------------------------------------

    [Fact]
    public async Task Get_IsTheRestRoutesEvent_Slimmed_WithAnOpenLink()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());
        var id = EventCallerScopeTests.SharedWithB;
        var rest = await host.RestJsonAsync("B", $"/api/v1/events/{id}");

        var result = await host.CallAsync("B", "spaarke_get_task", new { taskId = id.ToString() });

        IsError(result).Should().BeFalse(result.GetRawText());
        var body = result.GetProperty("structuredContent");
        body.GetProperty("id").GetGuid().Should().Be(rest["id"]!.GetValue<Guid>());
        body.GetProperty("subject").GetString().Should().Be(rest["subject"]!.GetValue<string>());
        body.GetProperty("dueDate").GetString().Should().Be(rest["dueDate"]!.GetValue<string>());
        body.GetProperty("status").GetString().Should().Be(rest["status"]!.GetValue<string>());
        body.GetProperty("link").GetString().Should().Be($"{TaskToolsHost.EnvironmentUrl}/main.aspx?pagetype=entityrecord&etn=sprk_event&id={id:D}");
        body.EnumerateObject().Select(p => p.Name).Should().OnlyContain(
            n => new[] { "id", "subject", "dueDate", "status", "priority", "regardingRecordName", "regardingRecordType", "regardingRecordId", "link" }.Contains(n),
            "ADR-015: no description, owner, event type or audit field is model-visible");
        Text(result).Should().Contain("Shared filing").And.Contain("Open in Spaarke: " + TaskToolsHost.EnvironmentUrl);
    }

    [Fact]
    public async Task Get_AnAbsentEvent_IsTheUniformNotFound_AsRestAnswers404()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());
        var absent = Guid.Parse("e0250000-0000-4000-8000-0000000000ff");
        using var rest = await host.GetAsync("B", $"/api/v1/events/{absent}");

        var result = await host.CallAsync("B", "spaarke_get_task", new { taskId = absent.ToString() });

        rest.StatusCode.Should().Be(HttpStatusCode.NotFound);
        IsError(result).Should().BeTrue();
        var body = result.GetProperty("structuredContent");
        body.GetProperty("reasonCode").GetString().Should().Be("sdap.access.deny.record_unavailable");
        body.GetProperty("status").GetInt32().Should().Be(404);
        body.GetProperty("message").GetString().Should().Be(TaskToolSupport.NotFoundMessage);
    }

    // ---- no caller (ADR-028 A10) ---------------------------------------------------------------------------------------------

    public static TheoryData<string> NoSignedInCaller => new() { "null context", "unauthenticated identity" };

    [Theory]
    [MemberData(nameof(NoSignedInCaller))]
    public async Task WithoutASignedInCaller_BothTools_AreUserContextRequired_AndTheCapabilityIsNeverCalled(string shape)
    {
        var capability = new StubEventsCapability();
        HttpContext? context = shape == "null context" ? null : new DefaultHttpContext();

        var list = await ListTasksTool.RunAsync(null, context, capability, NullLogger.Instance, CancellationToken.None);
        var get = await GetTaskTool.RunAsync(
            new Dictionary<string, JsonElement> { ["taskId"] = Json(Guid.NewGuid().ToString()) }, context, capability, TaskToolsHost.EnvironmentUrl, NullLogger.Instance, CancellationToken.None);

        foreach (var result in new[] { list, get })
        {
            result.IsError.Should().BeTrue();
            var body = result.StructuredContent!.Value;
            body.GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
            body.GetProperty("status").GetInt32().Should().Be(401);
        }

        capability.Calls.Should().Be(0, "no caller, no read: nothing reaches the capability");
    }

    // ---- invalid arguments -----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("{\"category\":\"all\"}")]
    [InlineData("{\"status\":\"overdue\"}")]
    [InlineData("{\"status\":7}")]
    [InlineData("{\"priority\":\"high\"}")]
    [InlineData("{\"priority\":1.5}")]
    [InlineData("{\"dueDateFrom\":\"01/12/2026\"}")]
    [InlineData("{\"dueDateTo\":20261201}")]
    [InlineData("{\"regardingRecordType\":\"matter\"}")]
    [InlineData("{\"regardingRecordId\":42}")]
    [InlineData("{\"pageNumber\":0}")]
    [InlineData("{\"pageSize\":0}")]
    [InlineData("{\"pageSize\":101}")]
    [InlineData("{\"pageSize\":\"ten\"}")]
    public async Task List_AnInvalidArgument_IsAnIsErrorThatNamesTheFix_AndNothingIsRead(string arguments)
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var result = await host.CallAsync("B", "spaarke_list_tasks", JsonSerializer.Deserialize<JsonElement>(arguments));

        IsError(result).Should().BeTrue();
        var body = result.GetProperty("structuredContent");
        body.GetProperty("reasonCode").GetString().Should().Be(TaskToolSupport.InvalidArgumentReasonCode);
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("message").GetString().Should().Contain("spaarke_list_tasks takes only these optional arguments");
        host.Dataverse.Requests.Should().Be(0, "an invalid call asks Dataverse nothing");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"taskId\":\"\"}")]
    [InlineData("{\"taskId\":42}")]
    [InlineData("{\"taskId\":\"e0250000-0000-4000-8000-0000000000ff\",\"extra\":1}")]
    public async Task Get_AMissingOrInventedArgument_IsAnIsErrorThatNamesTheFix_AndNothingIsRead(string arguments)
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var result = await host.CallAsync("B", "spaarke_get_task", JsonSerializer.Deserialize<JsonElement>(arguments));

        IsError(result).Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(TaskToolSupport.InvalidArgumentReasonCode);
        result.GetProperty("structuredContent").GetProperty("message").GetString().Should().Contain("spaarke_get_task takes exactly these arguments");
        host.Dataverse.Requests.Should().Be(0);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Get_ATaskIdThatIsNotAGuid_IsInvalidId_AsTheRoutesDo(string taskId)
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var result = await host.CallAsync("B", "spaarke_get_task", new { taskId });

        IsError(result).Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be("invalid_id");
        result.GetProperty("structuredContent").GetProperty("status").GetInt32().Should().Be(400);
        host.Dataverse.Requests.Should().Be(0);
    }

    [Theory]
    [InlineData("{\"regardingRecordId\":\"not-a-guid\"}", "Regarding record id must be a GUID.")]
    [InlineData("{\"regardingRecordType\":9}", "Regarding record type must be between 0 and 7.")]
    [InlineData("{\"priority\":3}", "Priority must be a sprk_priority value")]
    public async Task List_AValueTheCapabilityRefuses_IsAnIsErrorWithRestsText_AndNothingIsRead(string arguments, string restText)
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var result = await host.CallAsync("B", "spaarke_list_tasks", JsonSerializer.Deserialize<JsonElement>(arguments));

        IsError(result).Should().BeTrue();
        var body = result.GetProperty("structuredContent");
        body.GetProperty("reasonCode").GetString().Should().Be(TaskToolSupport.InvalidArgumentReasonCode);
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("message").GetString().Should().StartWith(restText);
        host.Dataverse.Requests.Should().Be(0);
    }

    [Fact]
    public async Task List_ACallerWithNoDataverseUser_IsTheRestRoutes403_AndNothingIsListed()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());
        using var rest = await host.GetAsync("U", "/api/v1/events");
        var restBody = JsonNode.Parse(await rest.Content.ReadAsStringAsync())!;

        var result = await host.CallAsync("U", "spaarke_list_tasks", new { });

        rest.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        IsError(result).Should().BeTrue();
        var body = result.GetProperty("structuredContent");
        body.GetProperty("reasonCode").GetString().Should().Be(restBody["reasonCode"]!.GetValue<string>()).And.Be("sdap.access.deny.caller_unresolved");
        body.GetProperty("status").GetInt32().Should().Be(403);
        host.Dataverse.ListQueries.Should().BeEmpty("no caller-scoped answer can be given, and no app-only list is served instead");
    }

    // ---- the arguments, as the capability's query ----------------------------------------------------------------------------------

    [Fact]
    public void TryReadQuery_MapsEveryArgumentOntoTheCapabilitysQuery_AndDefaultsToTheRoutesPage()
    {
        var arguments = new Dictionary<string, JsonElement>
        {
            ["status"] = Json("Open"),
            ["priority"] = Json(100000002),
            ["dueDateFrom"] = Json("2026-11-01"),
            ["dueDateTo"] = Json("2026-12-31"),
            ["regardingRecordType"] = Json(1),
            ["regardingRecordId"] = Json("e0250000-0000-4000-8000-0000000000aa"),
            ["pageNumber"] = Json(3),
            ["pageSize"] = Json(20),
        };

        ListTasksTool.TryReadQuery(arguments, null, out var query, out var error).Should().BeTrue();
        error.Should().BeNull();
        query.Should().BeEquivalentTo(new EventListQuery
        {
            Status = "open",
            Priority = 100000002,
            DueDateFrom = new DateTime(2026, 11, 1),
            DueDateTo = new DateTime(2026, 12, 31),
            RegardingRecordType = 1,
            RegardingRecordId = "e0250000-0000-4000-8000-0000000000aa",
            PageNumber = 3,
            PageSize = 20,
        });

        ListTasksTool.TryReadQuery(null, null, out var defaults, out _).Should().BeTrue();
        defaults.Should().BeEquivalentTo(new EventListQuery { PageNumber = 1, PageSize = 50 });
        ListTasksTool.TryReadQuery(new Dictionary<string, JsonElement> { ["status"] = JsonDocument.Parse("null").RootElement.Clone() }, null, out var nulls, out _)
            .Should().BeTrue("a JSON null is the same as leaving the argument out");
        nulls.Status.Should().BeNull();
    }

    // ---- faults: fixed text, never the exception, never a transport error ------------------------------------------------------------

    [Fact]
    public async Task List_AFaultFromTheCapability_IsAnIsErrorWithFixedText_NotTheExceptionsMessage()
    {
        var capability = new StubEventsCapability { Throw = new HttpRequestException("dataverse down: secret-internal-detail") };
        var http = SignedIn();

        var thrown = await ListTasksTool.RunAsync(null, http, capability, NullLogger.Instance, CancellationToken.None);
        capability.Throw = null;
        capability.ListResult = EventCapabilityResult<EventListResponse>.Of(EventCapabilityStatus.Failed);
        var failed = await ListTasksTool.RunAsync(null, http, capability, NullLogger.Instance, CancellationToken.None);

        foreach (var result in new[] { thrown, failed })
        {
            result.IsError.Should().BeTrue();
            var body = result.StructuredContent!.Value;
            body.GetProperty("reasonCode").GetString().Should().Be(TaskToolSupport.ListFailedReasonCode);
            body.GetProperty("status").GetInt32().Should().Be(500);
            body.GetRawText().Should().NotContain("secret-internal-detail");
        }
    }

    [Fact]
    public async Task Get_AFaultFromTheCapability_IsAnIsErrorWithFixedText_NotTheExceptionsMessage()
    {
        var capability = new StubEventsCapability { Throw = new HttpRequestException("dataverse down: secret-internal-detail") };
        var arguments = new Dictionary<string, JsonElement> { ["taskId"] = Json(Guid.NewGuid().ToString()) };

        var result = await GetTaskTool.RunAsync(arguments, SignedIn(), capability, TaskToolsHost.EnvironmentUrl, NullLogger.Instance, CancellationToken.None);

        result.IsError.Should().BeTrue();
        var body = result.StructuredContent!.Value;
        body.GetProperty("reasonCode").GetString().Should().Be(TaskToolSupport.GetFailedReasonCode);
        body.GetProperty("status").GetInt32().Should().Be(500);
        body.GetRawText().Should().NotContain("secret-internal-detail");
    }

    [Fact]
    public async Task ACallerWhoHasGoneAway_IsNotAFault_TheCancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        var capability = new StubEventsCapability { OnCall = cts.Cancel, Throw = new OperationCanceledException(cts.Token) };
        var arguments = new Dictionary<string, JsonElement> { ["taskId"] = Json(Guid.NewGuid().ToString()) };

        var list = () => ListTasksTool.RunAsync(null, SignedIn(), capability, NullLogger.Instance, cts.Token);
        var get = () => GetTaskTool.RunAsync(arguments, SignedIn(), capability, TaskToolsHost.EnvironmentUrl, NullLogger.Instance, cts.Token);

        await list.Should().ThrowAsync<OperationCanceledException>();
        await get.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- model-visible text (ADR-015) and size (NFR-02) ------------------------------------------------------------------------------

    [Fact]
    public void Rows_CarryOnlyIdSubjectDueDateStatusPriorityAndRegardingName_AndNoDescription()
    {
        var task = Event("Review the lease", "Smith v. Jones", due: new DateOnly(2026, 12, 1)) with { Description = "PRIVILEGED: counsel's strategy for the hearing" };

        var result = ListTasksTool.ResultOf(Page(task));

        var row = result.StructuredContent!.Value.GetProperty("tasks")[0];
        row.EnumerateObject().Select(p => p.Name).Should().Equal("id", "subject", "dueDate", "status", "priority", "regardingRecordName");
        row.GetProperty("dueDate").GetString().Should().Be("2026-12-01");
        row.GetProperty("priority").GetString().Should().Be("Normal");
        var raw = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        raw.Should().NotContain("PRIVILEGED").And.NotContain("counsel's strategy");
        ((TextContentBlock)result.Content[0]).Text.Should().Contain("Review the lease (due 2026-12-01, Open, Normal; id ");
    }

    [Fact]
    public void Get_ASubjectAndRegardingNameAreClipped_AndTheDescriptionIsNeverReturned()
    {
        var task = Event(new string('s', 5000), new string('r', 5000), due: null) with { Description = "PRIVILEGED description" };

        var result = GetTaskTool.ResultOf(task, "https://spaarke-test.crm.dynamics.com");

        var body = result.StructuredContent!.Value;
        body.GetProperty("subject").GetString()!.Length.Should().BeLessThanOrEqualTo(TaskToolSupport.GetSubjectBytes);
        body.GetProperty("regardingRecordName").GetString()!.Length.Should().BeLessThanOrEqualTo(TaskToolSupport.GetRegardingNameBytes);
        body.TryGetProperty("dueDate", out _).Should().BeFalse("no due date is omitted, not null");
        JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions).Should().NotContain("PRIVILEGED");
    }

    [Theory]
    [InlineData("https://spaarke-test.crm.dynamics.com", "https://spaarke-test.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_event&id=")]
    [InlineData("https://spaarke-test.crm.dynamics.com/", "https://spaarke-test.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_event&id=")]
    [InlineData("http://insecure.example.com", null)]
    [InlineData("https://user:pw@spaarke-test.crm.dynamics.com", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Get_TheLink_IsBuiltOnlyOnAnHttpsEnvironmentUrlWithNoUserInfo(string? environmentUrl, string? expectedPrefix)
    {
        var task = Event("Review the lease", null, due: null);

        var result = GetTaskTool.ResultOf(task, Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions.DecisionsTextSummary.ConsoleBaseUrl(environmentUrl));

        var body = result.StructuredContent!.Value;
        if (expectedPrefix is null)
        {
            body.TryGetProperty("link", out _).Should().BeFalse();
        }
        else
        {
            body.GetProperty("link").GetString().Should().Be(expectedPrefix + task.Id.ToString("D"));
        }
    }

    [Fact]
    public void TwoHundredRealisticTasks_AreAllReturned_Under64KiB()
    {
        var tasks = Enumerable.Range(1, 200)
            .Select(i => Event($"Review lease amendment draft {i}", "Smith v. Jones LLC", new DateOnly(2026, 12, 1)))
            .ToArray();

        var result = ListTasksTool.ResultOf(Page(tasks));

        var body = result.StructuredContent!.Value;
        body.GetProperty("count").GetInt32().Should().Be(200);
        body.GetProperty("truncated").GetBoolean().Should().BeFalse();
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions)).Should().BeLessThan(MaxBytes, "NFR-02: 200 tasks");
    }

    [Fact]
    public void TwoHundredEscapeHeavyTasks_AreASuccessUnder64KiB_AndTheResultSaysTheListWasCut()
    {
        string Heavy(int characters) => string.Concat(Enumerable.Repeat("\"é<", characters / 3));
        var tasks = Enumerable.Range(1, 200).Select(i => Event(Heavy(850), Heavy(850), new DateOnly(2026, 12, 1))).ToArray();

        var result = ListTasksTool.ResultOf(Page(tasks));

        result.IsError.Should().NotBe(true);
        var body = result.StructuredContent!.Value;
        body.GetProperty("truncated").GetBoolean().Should().BeTrue();
        body.GetProperty("count").GetInt32().Should().BeInRange(1, 199);
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions)).Should().BeLessThan(MaxBytes);
        ((TextContentBlock)result.Content[0]).Text.Should().Contain("narrow it with status");
    }

    [Fact]
    public async Task AtThePageMaximum_OverTheWire_TheResultIsUnder64KiB()
    {
        var world = new EventCallerScopeTests.FakeDataverse();
        for (var i = 0; i < 150; i++)
        {
            world.Add(Guid.Parse($"e0250000-0000-4000-8000-{i:D12}"), $"Review lease amendment draft {i}", readers: [EventCallerScopeTests.UserB]);
        }

        await using var host = await TaskToolsHost.StartAsync(world);

        var result = await host.CallAsync("B", "spaarke_list_tasks", new { pageSize = 100 });

        result.GetProperty("structuredContent").GetProperty("count").GetInt32().Should().Be(100);
        Encoding.UTF8.GetByteCount(result.GetRawText()).Should().BeLessThan(MaxBytes);
    }

    // ---- logs (NFR-05) ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EachCall_WritesOneTelemetryLine_WithToolOutcomeRowsAndCorrelation_AndNoRecordTextOrToken()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var list = await host.CallAsync("B", "spaarke_list_tasks", new { });
        var get = await host.CallAsync("B", "spaarke_get_task", new { taskId = EventCallerScopeTests.PrivateToA.ToString() });

        IsError(list).Should().BeFalse();
        IsError(get).Should().BeTrue();
        var lines = host.Logs.Lines;
        lines.Should().ContainSingle(l => l.Contains("MCP tool spaarke_list_tasks completed: outcome ok, 4 rows,"));
        lines.Should().ContainSingle(l => l.Contains("MCP tool spaarke_get_task completed: outcome sdap.access.deny.record_unavailable, 0 rows,"));
        lines.Where(l => l.Contains("MCP tool")).Should().OnlyContain(l => l.Contains("correlation "));
        var all = string.Join("\n", lines);
        all.Should().NotContain("B's work").And.NotContain("Shared filing").And.NotContain("A's private hearing", "no subject is ever logged");
        all.Should().NotContain(TaskToolsHost.TokenOf("B"), "no token is ever logged");
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------

    private static bool IsError(JsonElement result) => result.TryGetProperty("isError", out var e) && e.GetBoolean();

    private static string Text(JsonElement result) => result.GetProperty("content")[0].GetProperty("text").GetString()!;

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private static HttpContext SignedIn() => new DefaultHttpContext
    {
        User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("oid", EventCallerScopeTests.OidB.ToString("D"))], "test")),
    };

    private static EventDto Event(string subject, string? regarding, DateOnly? due) => new()
    {
        Id = Guid.NewGuid(),
        Subject = subject,
        Status = "Open",
        StatusCode = 659490001,
        Priority = 100000001,
        PriorityName = "Normal",
        DueDate = due,
        RegardingRecordName = regarding,
    };

    private static EventListResponse Page(params EventDto[] tasks) =>
        new() { Items = tasks, TotalCount = tasks.Length, PageNumber = 1, PageSize = Math.Max(tasks.Length, 1) };

    /// <summary>Hand-written (no mock): answers what it is told, records that it was asked.</summary>
    private sealed class StubEventsCapability : IEventsCapability
    {
        public int Calls { get; private set; }

        public Exception? Throw { get; set; }

        public Action? OnCall { get; set; }

        public EventCapabilityResult<EventListResponse> ListResult { get; set; } = EventCapabilityResult<EventListResponse>.Ok(new EventListResponse());

        public Task<EventCapabilityResult<EventListResponse>> ListAsync(HttpContext? httpContext, EventListQuery query, CancellationToken ct) =>
            Answer(ListResult);

        public Task<EventCapabilityResult<EventDto>> GetAsync(HttpContext? httpContext, Guid eventId, CancellationToken ct) =>
            Answer(EventCapabilityResult<EventDto>.Of(EventCapabilityStatus.Failed));

        public Task<EventCapabilityResult<CreateEventResponse>> CreateAsync(HttpContext? httpContext, CreateEventRequest? request, CancellationToken ct) =>
            throw new InvalidOperationException("a read tool never creates");

        public Task<EventCapabilityResult<EventActionResponse>> CompleteAsync(HttpContext? httpContext, Guid eventId, CancellationToken ct) =>
            throw new InvalidOperationException("a read tool never completes");

        private Task<T> Answer<T>(T result)
        {
            Calls++;
            OnCall?.Invoke();
            return Throw is { } ex ? Task.FromException<T>(ex) : Task.FromResult(result);
        }
    }
}
