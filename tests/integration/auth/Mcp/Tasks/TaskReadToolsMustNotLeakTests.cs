// AUTH — must-not-leak proofs for spaarke_list_tasks and spaarke_get_task (spaarkeai-mcp-bff-api-r2 task 025, FR-07, FR-15, ADR-028 A10).
// KEEP path: auth.
//
// The claim: through the MCP tools, a caller never sees another user's private event. Two callers, A and B, and an event private to A
// (task 006's world): B's list never contains it, on any page or in the count, and B's get on it is the uniform not-found, the same answer
// an id that does not exist gets.
//
// WHAT MAKES THIS A PROOF AND NOT A RESTATEMENT. The tools add no filter; the boundary is what the BFF sends to Dataverse. Here that is
// the REAL EventsCapability over the REAL DataverseWebApiService on task 006's hand-written Dataverse double, which applies row security
// the way Dataverse does: a request impersonating a user (MSCRMCallerID) sees only that user's rows and counts only them, and an app-only
// request sees every row. A tool whose list went out app-only, or as the wrong user, returns A's event here and fails. Everything runs over
// the real MCP route with real bearer tokens (TaskToolsHost). Controls: A sees A's own event through the same list and get (must-not-fire),
// so the double is not simply hiding it.
// ESCALATION (POML): if one of these failed THROUGH THE CAPABILITY (the same result from EventCallerScopeTests' capability proofs), it is a
// defect in task 006's capability, to be fixed there with uac-r2 review, not worked around in the tool.

using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Tests.Api.Mcp.Tasks;
using Sprk.Bff.Api.Tests.Auth.Events;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Mcp.Tasks;

public sealed class TaskReadToolsMustNotLeakTests
{
    private static readonly Guid Absent = Guid.Parse("e0060000-0000-4000-8000-0000000000ff");

    private static bool IsError(JsonElement result) => result.TryGetProperty("isError", out var e) && e.GetBoolean();

    private static IEnumerable<Guid> Ids(JsonElement result) =>
        result.GetProperty("structuredContent").GetProperty("tasks").EnumerateArray().Select(t => t.GetProperty("id").GetGuid());

    // ---- list ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_CallerB_NeverSeesAPrivateEventOfCallerA_OnAnyPageOrInTheCount()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var seen = new List<Guid>();
        int? totalCount = null;
        var raw = new System.Text.StringBuilder();
        for (var page = 1; ; page++)
        {
            var result = await host.CallAsync("B", "spaarke_list_tasks", new { pageSize = 1, pageNumber = page });

            IsError(result).Should().BeFalse(result.GetRawText());
            raw.AppendLine(result.GetRawText());
            var body = result.GetProperty("structuredContent");
            totalCount ??= body.GetProperty("totalCount").GetInt32();
            body.GetProperty("totalCount").GetInt32().Should().Be(totalCount, "every page reports the same trimmed count");
            seen.AddRange(Ids(result));
            if (!body.GetProperty("hasNextPage").GetBoolean())
            {
                break;
            }
        }

        seen.Should().NotContain(EventCallerScopeTests.PrivateToA);
        seen.Should().BeEquivalentTo(EventCallerScopeTests.OwnedByB.Append(EventCallerScopeTests.SharedWithB), "B sees exactly what B may read");
        totalCount.Should().Be(4, "the count is B's trimmed count, not the table's 5 rows");
        raw.ToString().Should().NotContain(EventCallerScopeTests.PrivateToA.ToString()).And.NotContain("A's private hearing", "neither the id nor the subject is anywhere in what B was sent");
        host.Dataverse.ListQueries.Should().OnlyContain(q => q.ImpersonatedUser == EventCallerScopeTests.UserB, "every page is queried AS B, never app-only");
    }

    [Fact]
    public async Task List_CallerA_SeesTheirOwnPrivateEvent_AndNotCallerBsEvents()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var result = await host.CallAsync("A", "spaarke_list_tasks", new { pageSize = 100 });

        Ids(result).Should().BeEquivalentTo(new[] { EventCallerScopeTests.PrivateToA, EventCallerScopeTests.SharedWithB });
        result.GetProperty("structuredContent").GetProperty("totalCount").GetInt32().Should().Be(2);
        host.Dataverse.ListQueries.Should().OnlyContain(q => q.ImpersonatedUser == EventCallerScopeTests.UserA);
    }

    [Fact]
    public async Task List_OneHostAndOneConnection_TheCallerIsTheTokensOwner_ForEveryCall()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var asB = await host.CallAsync("B", "spaarke_list_tasks", new { });
        var asA = await host.CallAsync("A", "spaarke_list_tasks", new { });
        var asB2 = await host.CallAsync("B", "spaarke_list_tasks", new { });

        Ids(asB).Should().NotContain(EventCallerScopeTests.PrivateToA);
        Ids(asA).Should().Contain(EventCallerScopeTests.PrivateToA);
        Ids(asB2).Should().NotContain(EventCallerScopeTests.PrivateToA, "a call after A's carries no memory of A");
        host.Dataverse.ListQueries.Select(q => q.ImpersonatedUser).Should().Equal(
            EventCallerScopeTests.UserB, EventCallerScopeTests.UserA, EventCallerScopeTests.UserB);
    }

    // ---- get -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_CallerBOnCallerAsPrivateEvent_IsTheUniformNotFound_IdenticalToAnAbsentId_AndTheEventIsNeverRead()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var forPrivate = await host.CallAsync("B", "spaarke_get_task", new { taskId = EventCallerScopeTests.PrivateToA.ToString() });
        var forAbsent = await host.CallAsync("B", "spaarke_get_task", new { taskId = Absent.ToString() });

        IsError(forPrivate).Should().BeTrue();
        IsError(forAbsent).Should().BeTrue();
        string Shape(JsonElement r)
        {
            var body = r.GetProperty("structuredContent");
            return $"{body.GetProperty("reasonCode").GetString()}|{body.GetProperty("message").GetString()}|{body.GetProperty("status").GetInt32()}|{r.GetProperty("content")[0].GetProperty("text").GetString()}";
        }

        Shape(forPrivate).Should().Be(Shape(forAbsent), "a private event and an absent one get one answer, in the text and in the structure");
        forPrivate.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be("sdap.access.deny.record_unavailable");
        forPrivate.GetProperty("structuredContent").GetProperty("status").GetInt32().Should().Be(404);
        forPrivate.GetRawText().Should().NotContain("A's private hearing").And.NotContain(EventCallerScopeTests.PrivateToA.ToString());
        host.Dataverse.ReadsOf(EventCallerScopeTests.PrivateToA).Should().Be(0, "the event is never read for a caller who may not read it");
    }

    [Fact]
    public async Task Get_CallerAOnTheirOwnPrivateEvent_IsReturned()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var result = await host.CallAsync("A", "spaarke_get_task", new { taskId = EventCallerScopeTests.PrivateToA.ToString() });

        IsError(result).Should().BeFalse(result.GetRawText());
        result.GetProperty("structuredContent").GetProperty("id").GetGuid().Should().Be(EventCallerScopeTests.PrivateToA);
        result.GetProperty("structuredContent").GetProperty("subject").GetString().Should().Be("A's private hearing");
    }

    [Fact]
    public async Task Get_CallerBOnAnEventSharedWithB_IsReturned()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var result = await host.CallAsync("B", "spaarke_get_task", new { taskId = EventCallerScopeTests.SharedWithB.ToString() });

        IsError(result).Should().BeFalse(result.GetRawText());
        result.GetProperty("structuredContent").GetProperty("id").GetGuid().Should().Be(EventCallerScopeTests.SharedWithB);
    }

    // ---- no caller identity that resolves to a user -------------------------------------------------------------------------------------

    [Fact]
    public async Task ASignedInCallerWithNoDataverseUser_ListsNothing_AndGetsNothing()
    {
        await using var host = await TaskToolsHost.StartAsync(EventCallerScopeTests.World());

        var list = await host.CallAsync("U", "spaarke_list_tasks", new { });
        var get = await host.CallAsync("U", "spaarke_get_task", new { taskId = EventCallerScopeTests.SharedWithB.ToString() });

        IsError(list).Should().BeTrue("the list fails closed rather than serving a security-untrimmed app-only list");
        IsError(get).Should().BeTrue("the caller holds no Read on any event");
        host.Dataverse.ListQueries.Should().BeEmpty();
        host.Dataverse.ReadsOf(EventCallerScopeTests.SharedWithB).Should().Be(0);
        get.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be("sdap.access.deny.record_unavailable");
    }
}
