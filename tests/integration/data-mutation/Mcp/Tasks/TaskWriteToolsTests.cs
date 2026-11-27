// DATA-MUTATION — spaarke_create_task and spaarke_complete_task (spaarkeai-mcp-bff-api-r2 task 036, FR-07 write half, NFR-06, ADR-028 A10).
// KEEP path: data-mutation.
//
// WHAT RUNS. The tools' RunAsync over the REAL EventsCapability, and for a create the REAL RecordCreationService, on the SAME seams the
// REST route is tested on (EventsAuthHost: the real route filters, the host's Dataverse doubles, the one app-only create captured).
// A tool whose create differed from POST /api/v1/events fails the first test here; a tool that wrote twice for a retried key fails the
// retry tests. Mocks sit only at module boundaries (Dataverse, the caller-rights probe).
//
// CONTROLS. A different key is a second task and a different caller's "same" key is not a retry (must-not-fire of the retry key); a
// refused create is not remembered (the same key succeeds once the caller may create); an already-completed task is success with no second write,
// and a cancelled one keeps its refusal (the success is not a blanket swallow of the capability's 400).

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Api.Events.Dtos;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Tasks;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Events;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;
using EventsAuthHost = Sprk.Bff.Api.Tests.Api.Events.EventEndpointsAuthorizationContractTests.EventsAuthHost;

namespace Sprk.Bff.Api.Tests.DataMutation.Mcp.Tasks;

public sealed class TaskWriteToolsTests
{
    private const string MattersSet = "sprk_matters";
    private const string CreateEventPrivilege = "prvCreatesprk_Event";
    private const string ConsoleUrl = "https://spaarke-test.crm.dynamics.com";
    private const string OidOne = "6f0c1a52-0000-4000-8000-000000000361";
    private const string OidTwo = "6f0c1a52-0000-4000-8000-000000000362";

    // ---- the world --------------------------------------------------------------------------------------------------------------------

    /// <summary>The caller may create events and holds Read + AppendTo on a matter that has a name and a number.</summary>
    private static Guid CallerWithAMatter(EventsAuthHost host)
    {
        var matter = Guid.NewGuid();
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant(MattersSet, matter, AccessRights.Read | AccessRights.AppendTo);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync(MattersSet);
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(host.InCallerCatalog("sprk_matter", new Entity("sprk_recordtype_ref", Guid.NewGuid())
            {
                [Spaarke.Dataverse.RegardingRecordType.RecordNumberFieldColumn] = "sprk_matternumber",
            }));
        host.Entities.Setup(e => e.RetrieveAsync("sprk_matter", matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matter) { ["sprk_mattername"] = "Acme Holdings", ["sprk_matternumber"] = "MAT-361" });
        return matter;
    }

    private static (EventsCapability Capability, Func<string, HttpContext> Request) CapabilityOver(EventsAuthHost host)
    {
        var capability = new EventsCapability(
            host.Events.Object, host.RecordTypes.Object, host.Caller.Object, host.Identity.Object, host.Entities.Object,
            host.RecordCreation, host.CallerClient, host.Ownership, host.Probe, NullLogger<EventsCapability>.Instance);

        var services = new ServiceCollection().AddSingleton<CallerRecordAccessProbe>(host.Probe).BuildServiceProvider();
        HttpContext Request(string oid)
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", oid), new Claim("tid", "tenant-361")], "TaskWriteTools")),
                RequestServices = services,
            };
            context.Request.Headers.Authorization = "Bearer caller-token";
            return context;
        }

        return (capability, Request);
    }

    /// <summary>Arguments that cannot be a task are refused before the capability is asked anything: no privilege and no rights question.</summary>
    private static void NoAccessQuestion(EventsAuthHost host)
    {
        host.Probe.RightsCalls.Should().BeEmpty("no rights question is asked about arguments that cannot be a task");
        host.Probe.PrivilegeCalls.Should().BeEmpty("no privilege question either");
    }

    private static IDictionary<string, JsonElement> Args(object arguments) =>
        JsonSerializer.SerializeToElement(arguments).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    private static Task<ModelContextProtocol.Protocol.CallToolResult> Create(
        EventsCapability capability, HttpContext request, ITenantCache cache, object arguments) =>
        CreateTaskTool.RunAsync(Args(arguments), request, cache, capability, ConsoleUrl, NullLogger.Instance, CancellationToken.None);

    private static Task<ModelContextProtocol.Protocol.CallToolResult> Complete(
        EventsCapability capability, HttpContext request, ITenantCache cache, object arguments) =>
        CompleteTaskTool.RunAsync(Args(arguments), request, cache, capability, ConsoleUrl, NullLogger.Instance, CancellationToken.None);

    private static JsonElement Body(ModelContextProtocol.Protocol.CallToolResult result) => result.StructuredContent!.Value;

    private static string ReasonOf(ModelContextProtocol.Protocol.CallToolResult result)
    {
        result.IsError.Should().BeTrue();
        return Body(result).GetProperty("reasonCode").GetString()!;
    }

    // ---- create -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_WritesTheSameRowAsPostEvents_AndAnswersItsIdAndALink()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var matter = CallerWithAMatter(host);
        var creates = host.CaptureCreates();
        var (capability, request) = CapabilityOver(host);

        var rest = await host.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/events")
        {
            Content = JsonContent.Create(new
            {
                subject = "File the response",
                description = "Draft first",
                dueDate = "2026-12-04",
                priority = EventPriority.All[0].Value,
                regardingRecordType = 1,
                regardingRecordId = matter,
            }),
        });
        rest.StatusCode.Should().Be(HttpStatusCode.Created, await rest.Content.ReadAsStringAsync());

        var tool = await Create(capability, request(OidOne), new InMemoryTenantCache(), new
        {
            subject = "File the response",
            description = "Draft first",
            dueDate = "2026-12-04",
            priority = EventPriority.All[0].Value,
            regardingRecordType = 1,
            regardingRecordId = matter,
            idempotencyKey = "k-1",
        });

        tool.IsError.Should().NotBe(true, tool.StructuredContent?.ToString());
        creates.Should().HaveCount(2, "one row from the route, one from the tool");
        var (viaRoute, viaTool) = (creates[0], creates[1]);
        viaTool.Table.Should().Be(viaRoute.Table);
        viaTool.Payload.Keys.Should().BeEquivalentTo(viaRoute.Payload.Keys, "the same columns are written");
        foreach (var key in viaRoute.Payload.Keys.Where(k => !k.Contains("eventid", StringComparison.OrdinalIgnoreCase)))
        {
            viaTool.Payload[key].Should().Be(viaRoute.Payload[key], $"column {key} is the same as the route's");
        }

        Body(tool).GetProperty("id").GetString().Should().Be(viaTool.Id.ToString("D"));
        Body(tool).GetProperty("replayed").GetBoolean().Should().BeFalse();
        Body(tool).GetProperty("link").GetString().Should().StartWith(ConsoleUrl).And.Contain(viaTool.Id.ToString("D"));
        tool.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text
            .Should().NotContain("Draft first", "the description is not echoed to the model");
    }

    [Fact]
    public async Task Create_RetriedWithTheSameKey_WritesOnce_AndAnswersTheFirstTasksId()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        var creates = host.CaptureCreates();
        var (capability, request) = CapabilityOver(host);
        var cache = new InMemoryTenantCache();
        var arguments = new { subject = "Call the client", idempotencyKey = "k-retry" };

        var first = await Create(capability, request(OidOne), cache, arguments);
        var retry = await Create(capability, request(OidOne), cache, arguments);

        first.IsError.Should().NotBe(true, first.StructuredContent?.ToString());
        retry.IsError.Should().NotBe(true);
        creates.Should().ContainSingle("a retried key writes nothing the second time");
        Body(retry).GetProperty("id").GetString().Should().Be(Body(first).GetProperty("id").GetString());
        Body(first).GetProperty("replayed").GetBoolean().Should().BeFalse();
        Body(retry).GetProperty("replayed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Create_WithADifferentKey_IsASecondTask()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        var creates = host.CaptureCreates();
        var (capability, request) = CapabilityOver(host);
        var cache = new InMemoryTenantCache();

        var first = await Create(capability, request(OidOne), cache, new { subject = "Call the client", idempotencyKey = "k-a" });
        var second = await Create(capability, request(OidOne), cache, new { subject = "Call the client", idempotencyKey = "k-b" });

        creates.Should().HaveCount(2);
        Body(second).GetProperty("id").GetString().Should().NotBe(Body(first).GetProperty("id").GetString());
    }

    [Fact]
    public async Task Create_TheSameKeyFromAnotherCaller_IsNotTheFirstCallersRetry()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        var creates = host.CaptureCreates();
        var (capability, request) = CapabilityOver(host);
        var cache = new InMemoryTenantCache();
        var arguments = new { subject = "Call the client", idempotencyKey = "shared-key" };

        var one = await Create(capability, request(OidOne), cache, arguments);
        var two = await Create(capability, request(OidTwo), cache, arguments);

        one.IsError.Should().NotBe(true);
        two.IsError.Should().NotBe(true);
        creates.Should().HaveCount(2, "a key is per caller: caller two's key is not caller one's");
        Body(two).GetProperty("replayed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Create_TheSameKeyWithDifferentInputs_IsRefusedAsKeyReused_AndWritesNothingNew()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        var creates = host.CaptureCreates();
        var (capability, request) = CapabilityOver(host);
        var cache = new InMemoryTenantCache();

        await Create(capability, request(OidOne), cache, new { subject = "Call the client", idempotencyKey = "k-reuse" });
        var reused = await Create(capability, request(OidOne), cache, new { subject = "Something else entirely", idempotencyKey = "k-reuse" });

        ReasonOf(reused).Should().Be(McpIdempotency.KeyReusedReasonCode);
        creates.Should().ContainSingle();
    }

    [Fact]
    public async Task Create_ARefusedCreate_IsNotRemembered_SoTheSameKeyCanBeRetried()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var creates = host.CaptureCreates();
        var (capability, request) = CapabilityOver(host);
        var cache = new InMemoryTenantCache();
        var arguments = new { subject = "Call the client", idempotencyKey = "k-after-refusal" };

        var refused = await Create(capability, request(OidOne), cache, arguments); // no Create privilege in the world yet
        creates.Should().BeEmpty();
        host.Probe.Hold(CreateEventPrivilege);
        var retried = await Create(capability, request(OidOne), cache, arguments);

        refused.IsError.Should().BeTrue();
        retried.IsError.Should().NotBe(true, retried.StructuredContent?.ToString());
        creates.Should().ContainSingle("the refusal left no entry behind");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"subject\":\"x\"}")]
    [InlineData("{\"idempotencyKey\":\"k\"}")]
    [InlineData("{\"subject\":\"x\",\"idempotencyKey\":\"k\",\"owner\":\"someone\"}")]
    [InlineData("{\"subject\":\"x\",\"idempotencyKey\":\"k\",\"dueDate\":\"04/12/2026\"}")]
    [InlineData("{\"subject\":\"x\",\"idempotencyKey\":\"k\",\"priority\":\"high\"}")]
    [InlineData("{\"subject\":\"x\",\"idempotencyKey\":\"k\",\"regardingRecordType\":1.5}")]
    [InlineData("{\"subject\":\"x\",\"idempotencyKey\":\"k\",\"regardingRecordId\":\"not-a-guid\"}")]
    public async Task Create_WithWrongArguments_IsRefusedBeforeAnyAccessQuestion_AndWritesNothing(string json)
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        var creates = host.CaptureCreates();
        var (capability, request) = CapabilityOver(host);

        var result = await CreateTaskTool.RunAsync(
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json), request(OidOne), new InMemoryTenantCache(),
            capability, ConsoleUrl, NullLogger.Instance, CancellationToken.None);

        result.IsError.Should().BeTrue();
        creates.Should().BeEmpty();
        NoAccessQuestion(host);
    }

    [Fact]
    public async Task Create_ARegardingTypeWithoutAnId_IsTheCapabilitysBadRequest_AndWritesNothing()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        var creates = host.CaptureCreates();
        var (capability, request) = CapabilityOver(host);

        var result = await Create(capability, request(OidOne), new InMemoryTenantCache(), new { subject = "x", regardingRecordType = 1, idempotencyKey = "k" });

        ReasonOf(result).Should().Be(TaskToolSupport.InvalidArgumentReasonCode);
        Body(result).GetProperty("status").GetInt32().Should().Be(400);
        creates.Should().BeEmpty();
    }

    // ---- complete ---------------------------------------------------------------------------------------------------------------------

    /// <summary>An event the caller may write, whose live status is <paramref name="initial"/> and follows what the capability writes.</summary>
    private static (Guid Id, Func<int> Status) WritableEvent(EventsAuthHost host, int initial)
    {
        var id = Guid.NewGuid();
        var status = initial;
        host.Probe.Grant(EventEndpoints.EventEntitySet, id, AccessRights.Read | AccessRights.Write);
        host.Events.Setup(e => e.GetEventAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Spaarke.Dataverse.EventEntity { Id = id, Name = "Filing deadline", StatusCode = status });
        host.Events.Setup(e => e.UpdateEventStatusAsync(id, EventStatusCode.Completed, It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .Callback(() => status = EventStatusCode.Completed)
            .Returns(Task.CompletedTask);
        host.Events.Setup(e => e.CreateEventLogAsync(id, EventLogAction.Completed, It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        return (id, () => status);
    }

    private static void VerifyWrites(EventsAuthHost host, Guid id, Times times) =>
        host.Events.Verify(e => e.UpdateEventStatusAsync(id, EventStatusCode.Completed, It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task Complete_AnOpenTask_IsCompletedOnce_AndAnswersItsStatusAndALink()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var (id, status) = WritableEvent(host, EventStatusCode.Open);
        var (capability, request) = CapabilityOver(host);

        var result = await Complete(capability, request(OidOne), new InMemoryTenantCache(), new { taskId = id, idempotencyKey = "c-1" });

        result.IsError.Should().NotBe(true, result.StructuredContent?.ToString());
        status().Should().Be(EventStatusCode.Completed);
        VerifyWrites(host, id, Times.Once());
        Body(result).GetProperty("id").GetString().Should().Be(id.ToString("D"));
        Body(result).GetProperty("alreadyCompleted").GetBoolean().Should().BeFalse();
        Body(result).GetProperty("link").GetString().Should().StartWith(ConsoleUrl);
    }

    [Fact]
    public async Task Complete_RetriedWithTheSameKey_ReplaysTheFirstAnswer_WithoutCallingTheCapabilityAgain()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var (id, _) = WritableEvent(host, EventStatusCode.Open);
        var (capability, request) = CapabilityOver(host);
        var cache = new InMemoryTenantCache();
        var arguments = new { taskId = id, idempotencyKey = "c-retry" };

        await Complete(capability, request(OidOne), cache, arguments);
        var retry = await Complete(capability, request(OidOne), cache, arguments);

        retry.IsError.Should().NotBe(true);
        Body(retry).GetProperty("replayed").GetBoolean().Should().BeTrue();
        VerifyWrites(host, id, Times.Once());
        host.Events.Verify(e => e.GetEventAsync(id, It.IsAny<CancellationToken>()), Times.Once, "the retry never reached the capability");
    }

    [Fact]
    public async Task Complete_AnAlreadyCompletedTask_WithANewKey_IsSuccess_AndWritesNothing()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var (id, _) = WritableEvent(host, EventStatusCode.Completed);
        var (capability, request) = CapabilityOver(host);

        var result = await Complete(capability, request(OidOne), new InMemoryTenantCache(), new { taskId = id, idempotencyKey = "c-again" });

        result.IsError.Should().NotBe(true, result.StructuredContent?.ToString());
        Body(result).GetProperty("alreadyCompleted").GetBoolean().Should().BeTrue();
        VerifyWrites(host, id, Times.Never());
    }

    [Fact]
    public async Task Complete_ACancelledTask_KeepsTheRefusal_AndWritesNothing()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var (id, _) = WritableEvent(host, EventStatusCode.Cancelled);
        var (capability, request) = CapabilityOver(host);

        var result = await Complete(capability, request(OidOne), new InMemoryTenantCache(), new { taskId = id, idempotencyKey = "c-cancelled" });

        ReasonOf(result).Should().Be(CompleteTaskTool.InvalidTransitionReasonCode);
        Body(result).GetProperty("status").GetInt32().Should().Be(400);
        VerifyWrites(host, id, Times.Never());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"taskId\":\"not-a-guid\",\"idempotencyKey\":\"k\"}")]
    [InlineData("{\"taskId\":\"00000000-0000-0000-0000-000000000000\",\"idempotencyKey\":\"k\"}")]
    [InlineData("{\"taskId\":\"e0360000-0000-4000-8000-000000000001\"}")]
    [InlineData("{\"taskId\":\"e0360000-0000-4000-8000-000000000001\",\"idempotencyKey\":\"k\",\"status\":\"cancelled\"}")]
    public async Task Complete_WithWrongArguments_IsRefusedBeforeAnyAccessQuestion_AndWritesNothing(string json)
    {
        await using var host = await EventsAuthHost.StartAsync();
        var (capability, request) = CapabilityOver(host);

        var result = await CompleteTaskTool.RunAsync(
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json), request(OidOne), new InMemoryTenantCache(),
            capability, ConsoleUrl, NullLogger.Instance, CancellationToken.None);

        result.IsError.Should().BeTrue();
        NoAccessQuestion(host);
        host.Events.Invocations.Should().BeEmpty();
    }
}
