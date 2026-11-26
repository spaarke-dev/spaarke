// AUTH — spaarke_run_playbook refuses what POST /api/ai/playbooks/{id}/execute refuses, with the route's reasonCode, and starts nothing;
// spaarke_get_playbook_run returns a run only to the caller who started it (spaarkeai-mcp-bff-api-r2 task 030, spec FR-05, NFR-06).
// KEEP path: auth.
//
// WHAT RUNS: the tools' RunAsync over the REAL DetachedPlaybookRunner and task 007's REAL authorization (PlaybookRunWorld). That 007's
// reasonCodes are the route's own is proven route-for-route by PlaybookRunAuthorizationSharedMethodTests; here each refusal must reach
// the tool result unchanged, with nothing fetched and nothing started.

using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Playbooks;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Api.Mcp.Playbooks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Auth.Mcp.Playbooks;

public sealed class PlaybookRunToolsAuthTests
{
    private const string Matters = "sprk_matters";

    private static string ReasonOf(CallToolResult result) => result.StructuredContent!.Value.GetProperty("reasonCode").GetString()!;

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content.Single()).Text;

    private static void NothingFetchedOrStarted(PlaybookRunWorld world)
    {
        world.VerifyNothingStarted();
        world.Graph.Verify(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never());
        world.Cache.Writes.Should().NotContain(w => w.Contains(RunPlaybookTool.RetryResource + ":", StringComparison.Ordinal),
            "a refused call keeps no retry entry, so it can be sent again");
    }

    // ---- 007 refusals -----------------------------------------------------------------------------------------------------------

    public enum Refusal
    {
        PlaybookTheCallerMayNotUse,
        UnreadableDocument,
        UnreadableRecordParameter,
        RunUserUnresolved,
    }

    [Theory(DisplayName = "030 run auth: each 007 refusal is isError with the execute route's reasonCode and status; no token is fetched and nothing starts")]
    [InlineData(Refusal.PlaybookTheCallerMayNotUse)]
    [InlineData(Refusal.UnreadableDocument)]
    [InlineData(Refusal.UnreadableRecordParameter)]
    [InlineData(Refusal.RunUserUnresolved)]
    public async Task Run_007Refusal_IsTheRoutesReasonCode_AndNothingStarts(Refusal refusal)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var playbookId = Guid.NewGuid();
        world.Host.PublicPlaybook(playbookId);
        world.Host.NodesOf(playbookId, ExecutorType.AiAnalysis);
        var documentId = world.Host.ReadableDocument();
        object? parameters = null;
        PlaybookRunAuthorization expected;
        int status;
        switch (refusal)
        {
            case Refusal.PlaybookTheCallerMayNotUse:
                playbookId = Guid.NewGuid();
                world.Host.PrivatePlaybook(playbookId);
                expected = PlaybookRunAuthorization.PlaybookUnavailable;
                status = StatusCodes.Status404NotFound;
                break;
            case Refusal.UnreadableDocument:
                documentId = Guid.NewGuid();
                expected = PlaybookRunAuthorization.RecordAccessDenied;
                status = StatusCodes.Status403Forbidden;
                break;
            case Refusal.UnreadableRecordParameter:
                var matterId = Guid.NewGuid();
                world.Host.Access.Grant(Matters, matterId, AccessRights.AppendTo);
                parameters = new { matterId = matterId.ToString() };
                expected = PlaybookRunAuthorization.RecordAccessDenied;
                status = StatusCodes.Status403Forbidden;
                break;
            default:
                world.Host.Probe.SystemUserId = null;
                expected = PlaybookRunAuthorization.RunUserUnresolved;
                status = StatusCodes.Status403Forbidden;
                break;
        }

        var result = await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = new[] { documentId }, parameters, idempotencyKey = "k" });

        result.IsError.Should().BeTrue();
        ReasonOf(result).Should().Be(expected.ReasonCode);
        result.StructuredContent!.Value.GetProperty("status").GetInt32().Should().Be(status);
        NothingFetchedOrStarted(world);
    }

    [Theory(DisplayName = "030 run auth: a parameter 007 rejects is isError with the route's 400 errorCode naming the caller's key; nothing starts")]
    [InlineData("userId", "6f0c1a52-0000-4000-8000-000000000999")]
    [InlineData("matterId", "not-a-guid")]
    public async Task Run_RejectedParameter_IsParameterRejected(string key, string value)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();

        var result = await world.RunToolAsync(world.CallerContext(), new
        {
            playbookId,
            documentIds = new[] { documentId },
            parameters = new Dictionary<string, string> { [key] = value },
            idempotencyKey = "k",
        });

        result.IsError.Should().BeTrue();
        ReasonOf(result).Should().Be(PlaybookAuthorizationFilter.ParameterRejectedErrorCode);
        result.StructuredContent!.Value.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
        Text(result).Should().Contain($"'{key}'");
        world.Host.Access.Calls.Should().BeEmpty("a rejected parameter precedes every rights question");
        NothingFetchedOrStarted(world);
    }

    [Fact(DisplayName = "030 run auth: a refused parameter key and reason reach the text cut to 64 and 200 characters, on one line")]
    public void ParameterRejected_KeyAndReason_AreCut()
    {
        var refusal = PlaybookRunAuthorization.ParameterRejected(new string('k', 100) + "\nX", "reason " + new string('r', 400) + "\nY");

        var error = RunPlaybookTool.ErrorOf(refusal);

        error.ReasonCode.Should().Be(PlaybookAuthorizationFilter.ParameterRejectedErrorCode);
        error.Message.Should().Contain($"'{new string('k', 64)}'").And.NotContain(new string('k', 65));
        error.Message.Should().NotContain(new string('r', 194)).And.NotContainAny("\r", "\n");
    }

    public enum NoCaller
    {
        NoContext,
        Unauthenticated,
        NoOid,
    }

    [Theory(DisplayName = "030 run auth: no signed-in caller is UserContextRequired before anything is read; nothing starts")]
    [InlineData(NoCaller.NoContext)]
    [InlineData(NoCaller.Unauthenticated)]
    [InlineData(NoCaller.NoOid)]
    public async Task Run_NoCaller_IsUserContextRequired(NoCaller @case)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        HttpContext? http = @case switch
        {
            NoCaller.NoContext => null,
            NoCaller.Unauthenticated => world.CallerContext(principal: new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", PlaybookRunWorld.CallerOid) }))),
            _ => world.CallerContext(principal: new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "Test"))),
        };

        var run = await RunPlaybookTool.RunAsync(PlaybookRunWorld.Args(new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "k" }),
            http, world.Cache, world.Runs, world.Time, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);
        var get = await GetPlaybookRunTool.RunAsync(PlaybookRunWorld.Args(new { runId = Guid.NewGuid() }),
            http, world.Runs, world.Time, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);

        ReasonOf(run).Should().Be(DocumentToolSupport.UserContextRequired.ReasonCode);
        ReasonOf(get).Should().Be(DocumentToolSupport.UserContextRequired.ReasonCode);
        world.Host.Access.Calls.Should().BeEmpty();
        NothingFetchedOrStarted(world);
    }

    // ---- input caps ---------------------------------------------------------------------------------------------------------------

    public static TheoryData<string, string> BadArguments()
    {
        var playbook = Guid.NewGuid().ToString();
        var document = Guid.NewGuid().ToString();
        var data = new TheoryData<string, string>
        {
            { "21 documents", JsonSerializer.Serialize(new { playbookId = playbook, documentIds = Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()), idempotencyKey = "k" }) },
            { "no documents", JsonSerializer.Serialize(new { playbookId = playbook, documentIds = Array.Empty<Guid>(), idempotencyKey = "k" }) },
            { "21 parameters", JsonSerializer.Serialize(new { playbookId = playbook, documentIds = new[] { document }, idempotencyKey = "k", parameters = Enumerable.Range(0, 21).ToDictionary(i => $"p{i}", i => "v") }) },
            { "65-char parameter name", JsonSerializer.Serialize(new { playbookId = playbook, documentIds = new[] { document }, idempotencyKey = "k", parameters = new Dictionary<string, string> { [new string('n', 65)] = "v" } }) },
            { "2,001-char parameter value", JsonSerializer.Serialize(new { playbookId = playbook, documentIds = new[] { document }, idempotencyKey = "k", parameters = new Dictionary<string, string> { ["tone"] = new string('v', 2_001) } }) },
            { "names differing in case", JsonSerializer.Serialize(new { playbookId = playbook, documentIds = new[] { document }, idempotencyKey = "k", parameters = new Dictionary<string, string> { ["tone"] = "a", ["Tone"] = "b" } }) },
            { "non-text parameter", JsonSerializer.Serialize(new { playbookId = playbook, documentIds = new[] { document }, idempotencyKey = "k", parameters = new { tone = 3 } }) },
            { "no idempotencyKey", JsonSerializer.Serialize(new { playbookId = playbook, documentIds = new[] { document } }) },
            { "201-char idempotencyKey", JsonSerializer.Serialize(new { playbookId = playbook, documentIds = new[] { document }, idempotencyKey = new string('k', 201) }) },
            { "unknown argument", JsonSerializer.Serialize(new { playbookId = playbook, documentIds = new[] { document }, idempotencyKey = "k", userContext = "x" }) },
            { "playbookId not a GUID", JsonSerializer.Serialize(new { playbookId = "nope", documentIds = new[] { document }, idempotencyKey = "k" }) },
            { "document id not a GUID", JsonSerializer.Serialize(new { playbookId = playbook, documentIds = new[] { "nope" }, idempotencyKey = "k" }) },
        };
        return data;
    }

    [Theory(DisplayName = "030 run caps: arguments outside the closed set or caps are refused with the fix; nothing is read or started")]
    [MemberData(nameof(BadArguments))]
    public async Task Run_ArgumentsOutsideTheCaps_AreRefused(string @case, string argumentsJson)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var arguments = JsonDocument.Parse(argumentsJson).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        var result = await RunPlaybookTool.RunAsync(arguments, world.CallerContext(), world.Cache, world.Runs, world.Time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);

        result.IsError.Should().BeTrue(@case);
        ReasonOf(result).Should().BeOneOf(DocumentToolSupport.InvalidArgumentReasonCode, DocumentToolSupport.InvalidIdReasonCode);
        world.Host.Access.Calls.Should().BeEmpty(@case);
        NothingFetchedOrStarted(world);
    }

    // ---- status: one not-found -------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "030 get auth: another user's live run and a random id are the same not-found, byte for byte")]
    public async Task Get_AnotherUsersLiveRun_IsTheSameNotFoundAsARandomId()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var started = await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "a" });
        var runId = Guid.Parse(started.StructuredContent!.Value.GetProperty("runId").GetString()!);

        var otherUser = world.CallerContext(PlaybookRunWorld.OtherOid);
        var theirs = await world.GetToolAsync(otherUser, runId);
        var random = await world.GetToolAsync(otherUser, Guid.NewGuid());

        theirs.IsError.Should().BeTrue();
        ReasonOf(theirs).Should().Be(DocumentToolSupport.NotFound.ReasonCode);
        Serialize(theirs).Should().Be(Serialize(random));
        (await world.GetToolAsync(world.CallerContext(), runId)).IsError.Should().NotBe(true, "the starter reads it");
    }

    [Fact(DisplayName = "030 get auth: another user's durable run record and a random id are the same not-found, byte for byte")]
    public async Task Get_AnotherUsersStoredRun_IsTheSameNotFoundAsARandomId()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var runId = Guid.NewGuid();
        world.StoredRuns[runId] = new PlaybookRunStatus
        {
            RunId = runId,
            PlaybookId = Guid.NewGuid(),
            State = PlaybookRunState.Completed,
            StartedAt = world.Time.GetUtcNow(),
            StartedByOid = PlaybookRunWorld.CallerOid,
        };

        var otherUser = world.CallerContext(PlaybookRunWorld.OtherOid);
        var theirs = await world.GetToolAsync(otherUser, runId);
        var random = await world.GetToolAsync(otherUser, Guid.NewGuid());

        ReasonOf(theirs).Should().Be(DocumentToolSupport.NotFound.ReasonCode);
        Serialize(theirs).Should().Be(Serialize(random));
    }

    [Fact(DisplayName = "030 get auth: a status read that faults is 'could not read' (503), never the not-found")]
    public async Task Get_ReadFaults_IsReadFailed()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        world.Engine.Setup(e => e.GetRunStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down"));

        var result = await world.GetToolAsync(world.CallerContext(), Guid.NewGuid());

        ReasonOf(result).Should().Be(GetPlaybookRunTool.GetFailedReasonCode);
    }

    private static string Serialize(CallToolResult result) => JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
}
