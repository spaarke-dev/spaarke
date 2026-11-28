// AUTH — spaarke_create_task and spaarke_complete_task (spaarkeai-mcp-bff-api-r2 task 036, FR-07, FR-15, NFR-07, ADR-028 A10, #3181).
// KEEP path: auth.
//
// The claim: through the MCP write tools a caller changes only what the REST routes would let them change, answered the REST way, and
// a refusal writes nothing and tells nothing.
//   - complete: caller B on A's private task is the uniform not-found, the same answer an id that does not exist gets, and the task is
//     never read; B with Read but no Write on a shared task is a 403 with the route's reason; neither writes.
//   - create: no Create privilege is a 403 (the route's reason); Create but no rights on the regarding record is a 403; AppendTo
//     WITHOUT Read on it is a 403 too (#3181: the name and number are copied, so the caller must be able to read them).
//   - no signed-in caller: UserContextRequired, and nothing at all is asked of Dataverse (no app-only fallback).
//
// WHAT MAKES THIS A PROOF AND NOT A RESTATEMENT. The tools add no check; the decision is the REAL EventsCapability's, over task 006's
// row-secured FakeDataverse (a request impersonating a user sees only that user's rows; an app-only one sees all) and its row-security
// probe. Everything a write would use is a strict double with no setup, so a refusal that reached a write would fault and the tool
// would answer a 500 here, not the refusal asserted. The tool's refusal is also compared with the capability's own answer for the same
// call, so "the tool's reasonCode is the route's" is checked against the capability that owns the decision.

using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Tasks;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Events;
using Sprk.Bff.Api.Tests.Auth.Events;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;
using static Sprk.Bff.Api.Tests.Auth.Events.EventCallerScopeTests;

namespace Sprk.Bff.Api.Tests.Auth.Mcp.Tasks;

public sealed class TaskWriteToolsAuthTests
{
    private const string ConsoleUrl = "https://spaarke-test.crm.dynamics.com";
    private static readonly Guid Absent = Guid.Parse("e0360000-0000-4000-8000-0000000000ff");
    private static readonly Guid Matter = Guid.Parse("a7360000-0000-4000-8000-000000000001");

    private sealed class TaskWorld
    {
        public TaskWorld()
        {
            Dataverse = EventCallerScopeTests.World();
            Probe = new RowSecurityProbe(Dataverse);
            Entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string logicalName, CancellationToken _) => logicalName + "s");
            Capability = new EventsCapability(
                new OfflineDataverseService(Dataverse),
                new Mock<ICommunicationDataverseService>(MockBehavior.Strict).Object,
                CallerResolver.Object,
                NoLinkedContacts().Object,
                Entities.Object,
                recordCreation: null!, // never reached: a refusal that got this far would fault
                UserClient.Object,
                Ownership.Object,
                Probe,
                NullLogger<EventsCapability>.Instance);
        }

        public FakeDataverse Dataverse { get; }

        public RowSecurityProbe Probe { get; }

        public Mock<ICallerSystemUserResolver> CallerResolver { get; } = EventCallerScopeTests.CallerResolver();

        public Mock<Spaarke.Dataverse.IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);

        public Mock<IDataverseUserClient> UserClient { get; } = new(MockBehavior.Strict);

        public Mock<IRecordOwnershipResolver> Ownership { get; } = new(MockBehavior.Strict);

        public EventsCapability Capability { get; }

        public HttpContext Request(string who, ClaimsPrincipal? principal = null)
        {
            var context = new DefaultHttpContext { User = principal ?? Principal(who) };
            context.Request.Headers.Authorization = "Bearer token-" + who;
            return context;
        }

        public void AssertNothingWritten()
        {
            UserClient.Invocations.Should().BeEmpty("no write as the caller");
            Ownership.Invocations.Should().BeEmpty("no owner decided for a write");
            Dataverse.ListQueries.Should().BeEmpty();
        }
    }

    private static IDictionary<string, JsonElement> Args(object arguments) =>
        JsonSerializer.SerializeToElement(arguments).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    private static Task<ModelContextProtocol.Protocol.CallToolResult> Complete(TaskWorld world, HttpContext? request, Guid id) =>
        CompleteTaskTool.RunAsync(
            Args(new { taskId = id, idempotencyKey = "k-" + id.ToString("N") }), request, new InMemoryTenantCache(), world.Capability,
            ConsoleUrl, NullLogger.Instance, CancellationToken.None);

    private static Task<ModelContextProtocol.Protocol.CallToolResult> Create(TaskWorld world, HttpContext? request, object? regarding = null) =>
        CreateTaskTool.RunAsync(
            Args(regarding ?? new { subject = "Review the draft", idempotencyKey = "k-create" }), request, new InMemoryTenantCache(),
            world.Capability, ConsoleUrl, NullLogger.Instance, CancellationToken.None);

    private static JsonElement Body(ModelContextProtocol.Protocol.CallToolResult result) => result.StructuredContent!.Value;

    private static string ReasonOf(ModelContextProtocol.Protocol.CallToolResult result) => Body(result).GetProperty("reasonCode").GetString()!;

    private static string TextOf(ModelContextProtocol.Protocol.CallToolResult result) =>
        string.Join("\n", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(t => t.Text)) + "\n" + Body(result).GetRawText();

    // ---- complete ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Complete_CallerBOnAPrivateTaskOfCallerA_IsTheUniformNotFound_TheSameAsAnAbsentId_AndNothingIsReadOrWritten()
    {
        var world = new TaskWorld();

        var forPrivate = await Complete(world, world.Request("B"), PrivateToA);
        var forAbsent = await Complete(world, world.Request("B"), Absent);

        forPrivate.IsError.Should().BeTrue();
        ReasonOf(forPrivate).Should().Be(TaskToolSupport.NotFound.ReasonCode);
        Body(forPrivate).GetProperty("status").GetInt32().Should().Be(404);
        Body(forPrivate).GetProperty("message").GetString().Should().Be(Body(forAbsent).GetProperty("message").GetString());
        ReasonOf(forAbsent).Should().Be(ReasonOf(forPrivate), "no existence oracle: a task B may not see answers as an absent one does");
        TextOf(forPrivate).Should().NotContain(PrivateToA.ToString("D")).And.NotContain("A's private hearing");
        world.Dataverse.ReadsOf(PrivateToA).Should().Be(0, "the task is never read for a caller who may not read it");
        world.AssertNothingWritten();
    }

    [Fact]
    public async Task Complete_WithReadButNotWrite_IsForbiddenWithTheCapabilitysReason_AndWritesNothing()
    {
        var world = new TaskWorld();
        world.Probe.Grants[(UserB, EventEndpoints.EventEntitySet, SharedWithB)] = AccessRights.Read;
        var viaCapability = await world.Capability.CompleteAsync(world.Request("B"), SharedWithB, CancellationToken.None);

        var result = await Complete(world, world.Request("B"), SharedWithB);

        viaCapability.Status.Should().Be(EventCapabilityStatus.Forbidden, "the control: the capability itself refuses a Read-only caller");
        result.IsError.Should().BeTrue();
        Body(result).GetProperty("status").GetInt32().Should().Be(403);
        ReasonOf(result).Should().Be(viaCapability.ReasonCode, "the tool's reason is the route's");
        world.Dataverse.ReadsOf(SharedWithB).Should().Be(0, "the task is not read before Write is established");
        world.AssertNothingWritten();
    }

    // ---- create -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_WithoutTheCreatePrivilege_IsForbiddenWithTheCapabilitysReason_AndWritesNothing()
    {
        var world = new TaskWorld(); // B holds no privilege
        var viaCapability = await world.Capability.CreateAsync(world.Request("B"), new Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest("Review the draft"), CancellationToken.None);

        var result = await Create(world, world.Request("B"));

        viaCapability.Status.Should().Be(EventCapabilityStatus.InsufficientPrivilege);
        result.IsError.Should().BeTrue();
        Body(result).GetProperty("status").GetInt32().Should().Be(403);
        ReasonOf(result).Should().Be(viaCapability.ReasonCode, "the tool's reason is the route's");
        world.AssertNothingWritten();
    }

    [Theory]
    [InlineData(AccessRights.None)]
    [InlineData(AccessRights.AppendTo)] // #3181: AppendTo does not imply Read, and the create copies the record's name and number
    [InlineData(AccessRights.Read)]
    public async Task Create_UnderARecordTheCallerMayNotBothReadAndAttachTo_IsForbidden_AndNamesNothing(AccessRights rights)
    {
        var world = new TaskWorld();
        world.Probe.HeldPrivileges.Add(EventEndpoints.CreateEventPrivilege);
        world.Probe.Grants[(UserB, "sprk_matters", Matter)] = rights;

        var result = await Create(world, world.Request("B"), new
        {
            subject = "Review the draft",
            regardingRecordType = 1,
            regardingRecordId = Matter,
            idempotencyKey = "k-secret-key",
        });

        result.IsError.Should().BeTrue();
        Body(result).GetProperty("status").GetInt32().Should().Be(403);
        ReasonOf(result).Should().Be("sdap.access.deny.insufficient_rights");
        TextOf(result).Should().NotContain(Matter.ToString("D"), "ADR-015: the refusal names no record")
            .And.NotContain("k-secret-key").And.NotContain("Read").And.NotContain("AppendTo");
        world.Probe.Calls.Should().Be(2, "the privilege, then the rights on the regarding record, both as the caller");
        world.AssertNothingWritten();
    }

    // ---- no signed-in caller ----------------------------------------------------------------------------------------------------------

    public static TheoryData<string> NoSignedInCaller => new() { "null context", "no principal claims", "unauthenticated identity" };

    [Theory]
    [MemberData(nameof(NoSignedInCaller))]
    public async Task BothTools_WithoutASignedInCaller_AreUserContextRequired_AndAskDataverseNothing(string shape)
    {
        var world = new TaskWorld();
        world.Probe.HeldPrivileges.Add(EventEndpoints.CreateEventPrivilege);
        HttpContext? context = shape switch
        {
            "null context" => null,
            "no principal claims" => new DefaultHttpContext { User = new ClaimsPrincipal() },
            // Claims, and the bearer token, but no authentication type: not signed in.
            _ => world.Request("A", new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", OidA.ToString("D"))]))),
        };

        var create = await Create(world, context);
        var complete = await Complete(world, context, PrivateToA);

        foreach (var result in new[] { create, complete })
        {
            result.IsError.Should().BeTrue();
            ReasonOf(result).Should().Be(TaskToolSupport.UserContextRequired.ReasonCode);
            Body(result).GetProperty("status").GetInt32().Should().Be(401);
        }

        world.Dataverse.Requests.Should().Be(0, "no Dataverse call without a signed-in caller, and no app-only fallback");
        world.Probe.Calls.Should().Be(0, "not even an as-caller access question");
        world.CallerResolver.Invocations.Should().BeEmpty();
        world.UserClient.Invocations.Should().BeEmpty();
    }
}
