using System.Text.Json;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="DataverseUpdateRecordHandler"/> (spaarke-ai-architecture-redesign-r1
/// task 009, FR-P0-07 write half).
/// </summary>
/// <remarks>
/// The Dataverse boundary is mocked at <see cref="IDataverseUserClient"/> (module boundary —
/// NOT an HttpMessageHandler mock, per ADR-038). Tests exercise the handler under a simulated
/// test USER's security context and assert the user-scoped outcome flows through unchanged.
/// </remarks>
public sealed class DataverseUpdateRecordHandlerTests : TypedToolHandlerTestFixture
{
    private readonly Mock<IDataverseUserClient> _dataverse = new();

    /// <summary>
    /// The rows the after-write re-stamp reads and PATCHes app-only (task 156, owner round 8 item 1) — an in-memory
    /// Dataverse; the restamper and the helper are the real code. Empty unless a test seeds it.
    /// </summary>
    private readonly StampWorld _world = new();

    private DataverseUpdateRecordHandler CreateHandler() =>
        new(_dataverse.Object, _world.AfterWriteRestamp, CreateLogger<DataverseUpdateRecordHandler>(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure());

    private static AnalysisTool BuildUpdateTool() =>
        BuildAnalysisTool(handlerClass: nameof(DataverseUpdateRecordHandler), name: "SYS-Dataverse Update Record");

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private void SetupAccountMetadata() =>
        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='account')?$select=EntitySetName")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                """{ "EntitySetName": "accounts", "PrimaryIdAttribute": "accountid" }""")));

    // ═════════════════════════════════════════════════════════════════════════════
    // Contract + argument validation (GA: update_record(tablename, recordId, item))
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        CreateHandler().HandlerId.Should().Be(nameof(DataverseUpdateRecordHandler));
    }

    [Fact]
    public void Validate_InPlaybookContext_Rejects()
    {
        var result = CreateHandler().Validate(BuildToolExecutionContext(), BuildUpdateTool());
        result.IsValid.Should().BeFalse(because: "dataverse.update_record is an agent-loop (chat) tool");
    }

    [Theory]
    [InlineData("{}", "*tablename*")]
    [InlineData("""{"tablename":"account","item":{"name":"x"}}""", "*recordId*")]
    [InlineData("""{"tablename":"account","recordId":"not-a-guid","item":{"name":"x"}}""", "*recordId*")]
    [InlineData("""{"tablename":"account","recordId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"}""", "*item*")]
    public void ValidateChat_InvalidArgs_Fails(string argsJson, string expectedErrorPattern)
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);
        var result = CreateHandler().ValidateChat(ctx, BuildUpdateTool());
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainMatch(expectedErrorPattern);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Successful update — round-shape + updated-record citation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ValidUpdate_PatchesResolvedEntitySetAndReturnsCitation()
    {
        var recordId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        SetupAccountMetadata();

        string? patchedPath = null;
        string? patchedBody = null;
        _dataverse
            .Setup(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, body, _) => { patchedPath = path; patchedBody = body; })
            .ReturnsAsync(DataverseUserResponse.Ok(204, body: null));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$$"""
            {"tablename":"account","recordId":"{{{recordId:D}}}","item":{"name":"Contoso Renamed","numberofemployees":50}}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.Data!.Value;
        data.GetProperty("tool").GetString().Should().Be("dataverse.update_record");
        data.GetProperty("tablename").GetString().Should().Be("account");
        data.GetProperty("recordId").GetString().Should().Be(recordId.ToString("D"));
        data.GetProperty("path").GetString().Should().Be($"tables/account/records/{recordId:D}");
        data.GetProperty("columnCount").GetInt32().Should().Be(2);
        data.GetProperty("columnsUpdated").EnumerateArray().Select(c => c.GetString())
            .Should().BeEquivalentTo("name", "numberofemployees");

        patchedPath.Should().Be($"accounts({recordId:D})",
            because: "the PATCH targets the metadata-resolved entity set, never a guessed collection name");
        using var bodyDoc = JsonDocument.Parse(patchedBody!);
        bodyDoc.RootElement.GetProperty("name").GetString().Should().Be("Contoso Renamed");
        bodyDoc.RootElement.GetProperty("numberofemployees").GetInt32().Should().Be(50);

        // ADR-039 grounding: adapter-level citation for the UPDATED record.
        result.Metadata.Should().NotBeNull();
        var citations = (IEnumerable<ToolResultCitation>)result.Metadata![ToolResultMetadataKeys.Citations]!;
        citations.Should().ContainSingle(c =>
            c.ChunkId == $"tables/account/records/{recordId:D}" && c.SourceName == "account" && c.SourceType == "dataverse");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // unified-access-control-r2 task 156 — owner decisions round 8 item 1 (CLAUDE.md §6.5 path B): a write that moves a
    // core-ancestor copy re-stamps it INLINE, in the same call, through the one narrow app-only helper
    // (CoreAncestorAfterWriteRestamp). The caller's own update stays user-OBO and runs FIRST; nothing is queued.
    // ═════════════════════════════════════════════════════════════════════════════

    private static readonly Guid MatterA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid MatterB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid Communication = Guid.Parse("15600000-0000-0000-0000-000000000c41");
    private static readonly Guid Invoice = Guid.Parse("15600000-0000-0000-0000-000000000c42");
    private static readonly Guid Event = Guid.Parse("15600000-0000-0000-0000-000000000e41");
    private static readonly Guid TodoUnderEvent = Guid.Parse("15600000-0000-0000-0000-000000000141");

    /// <summary>
    /// An event filed under a communication (matter A — its pair names the communication, so its copy says A) that ALSO
    /// names an invoice of matter B, and a to-do filed under the event copying A. Moving the event's pair to the invoice
    /// moves the event's copy to B, and the to-do's with it (the <c>EventRefileRestampRouteTests</c> shape).
    /// </summary>
    private void SeedEventWithTwoSources() => _world
        .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterA)])
        .Row("sprk_invoice", Invoice, [("sprk_matter", "sprk_matter", MatterB)])
        .Row("sprk_event", Event,
        [
            ("sprk_regardingcommunication", "sprk_communication", Communication),
            ("sprk_regardinginvoice", "sprk_invoice", Invoice),
            ("sprk_regardingmatter", "sprk_matter", MatterA),
        ], pairId: Communication.ToString())
        .Row("sprk_todo", TodoUnderEvent,
            [("sprk_regardingevent", "sprk_event", Event), ("sprk_regardingmatter", "sprk_matter", MatterA)],
            pairId: Event.ToString());

    private void SetupEventMetadata() =>
        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_event')?$select=EntitySetName")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                """{ "EntitySetName": "sprk_events", "PrimaryIdAttribute": "sprk_eventid" }""")));

    private static string MoveThePairToTheInvoice() => $$$"""
        {"tablename":"sprk_event","recordId":"{{{Event:D}}}","item":{"sprk_regardingrecordid":"{{{Invoice:D}}}"}}
        """;

    [Fact(DisplayName = "Task 156 (owner round 8 item 1): the AI update tool moving an event's pair re-stamps the event AND the to-do under it IN THE SAME CALL, after the caller's own update")]
    public async Task ExecuteChatAsync_UpdateThatMovesAnEventsPair_RestampsTheEventAndItsChildren_InTheSameCall()
    {
        SeedEventWithTwoSources();
        SetupEventMetadata();
        int? restampPatchesWhenTheUserWrote = null;
        _dataverse
            .Setup(d => d.PatchAsync($"sprk_events({Event:D})", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, _, _) =>
            {
                // The caller's own PATCH (user-OBO), applied to the rows as Dataverse would apply it.
                restampPatchesWhenTheUserWrote = _world.Patches.Count;
                _world.SetPair("sprk_event", Event, Invoice.ToString());
            })
            .ReturnsAsync(DataverseUserResponse.Ok(204, body: null));

        var result = await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: MoveThePairToTheInvoice()), BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        restampPatchesWhenTheUserWrote.Should().Be(0, "the caller's own update runs first, and through the user's client");
        _world.Lookup("sprk_event", Event, "sprk_regardingmatter").Should().Be(MatterB,
            "the event's copy now comes from the invoice its pair names");
        _world.Lookup("sprk_todo", TodoUnderEvent, "sprk_regardingmatter").Should().Be(MatterB,
            "the to-do filed under the event is re-stamped in the same call — not queued for a job seconds later (AC1)");
        _world.PatchesTo("sprk_todo", TodoUnderEvent).Should().ContainSingle()
            .Which.Fields.Keys.Should().BeEquivalentTo(["sprk_regardingmatter"], "the re-stamp writes ONLY stamp columns");
        _dataverse.Verify(
            d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            failMessage: "the user's client carries exactly the caller's own update — the re-stamp never runs through it");
    }

    private static readonly Guid TodoUnderCommunication = Guid.Parse("15600000-0000-0000-0000-000000000142");

    [Fact(DisplayName = "Task 156 (owner round 8 item 1): the AI update tool re-filing a communication filed under nothing (its own sprk_regardingmatter, A to B) carries B to the to-do under it IN THE SAME CALL, and leaves the communication's own root as the caller wrote it")]
    public async Task ExecuteChatAsync_UpdateOfACommunicationsOwnRoot_CascadesTheNewRootToItsChildren_InTheSameCall()
    {
        // The AI tool's most common re-file: a communication filed under NOTHING (no pair, no source lookup), so its
        // sprk_regardingmatter is its own direct root. Writing it moves no copy ON the communication; it moves the copies
        // of everything filed under it. Only the after-write cascade (AfterWriteAsync) reaches those children — re-stamping
        // the communication itself as a child would find nothing to change and stop there.
        _world
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_todo", TodoUnderCommunication,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString());
        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_communication')?$select=EntitySetName")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                """{ "EntitySetName": "sprk_communications", "PrimaryIdAttribute": "sprk_communicationid" }""")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.Contains("ManyToOneRelationships")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""
                {
                  "LogicalName": "sprk_communication",
                  "ManyToOneRelationships": [
                    { "ReferencingAttribute": "sprk_regardingmatter", "ReferencingEntityNavigationPropertyName": "sprk_RegardingMatter", "ReferencedEntity": "sprk_matter" }
                  ]
                }
                """)));
        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_matter')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "sprk_matters" }""")));
        // Batch 4 integration (task 146 r2): filing a communication under a matter is a RE-FILE — asked as the caller
        // (WhoAmI, AppendTo on the matter, the row visible to them), owned through the resolver, which applies this PATCH.
        _dataverse
            .Setup(d => d.GetAsync("WhoAmI()", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""{ "UserId": "{{Guid.NewGuid():D}}" }""")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.Contains("RetrievePrincipalAccess")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "AccessRights": "ReadAccess, WriteAccess, AppendToAccess" }""")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"sprk_communications({Communication:D})?$select=")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""{ "sprk_communicationid": "{{Communication:D}}" }""")));
        int? restampPatchesWhenTheUserWrote = null;
        _dataverse
            .Setup(d => d.PatchAsync($"sprk_communications({Communication:D})", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, _, _) =>
            {
                // The caller's own PATCH (user-OBO), applied to the rows as Dataverse would apply it.
                restampPatchesWhenTheUserWrote = _world.Patches.Count;
                _world.OutOfBand("sprk_communication", Communication, "sprk_regardingmatter", "sprk_matter", MatterB);
            })
            .ReturnsAsync(DataverseUserResponse.Ok(204, body: null));

        var result = await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: $$$$"""
                {"tablename":"sprk_communication","recordId":"{{{{Communication:D}}}}","item":{"sprk_regardingmatter":{"relatedTable":"sprk_matter","recordId":"{{{{MatterB:D}}}}"}}}
                """),
            BuildUpdateTool(),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        restampPatchesWhenTheUserWrote.Should().Be(0, "the caller's own update runs first, and through the user's client");
        _world.Lookup("sprk_todo", TodoUnderCommunication, "sprk_regardingmatter").Should().Be(MatterB,
            "the to-do filed under the communication copies its root, and the re-file carries the new root to it in the same "
            + "call (AC1) — not queued, not left for the reconciliation job");
        _world.PatchesTo("sprk_todo", TodoUnderCommunication).Should().ContainSingle()
            .Which.Fields.Keys.Should().BeEquivalentTo(["sprk_regardingmatter"], "the re-stamp writes ONLY stamp columns");
        _world.Lookup("sprk_communication", Communication, "sprk_regardingmatter").Should().Be(MatterB,
            "a communication filed under nothing owns its root: the caller's value stands");
        _world.PatchesTo("sprk_communication", Communication).Should().BeEmpty(
            "the app-only helper never rewrites the caller's own direct choice");
        _dataverse.Verify(
            d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            failMessage: "the user's client carries exactly the caller's own update — the re-stamp never runs through it");
    }

    [Fact(DisplayName = "Task 156 (owner round 8 item 1): a child the inline re-stamp cannot write leaves the caller's update standing and the tool result a success; the reconciliation job repairs the child")]
    public async Task ExecuteChatAsync_RestampOfAChildFails_TheUsersUpdateStands()
    {
        SeedEventWithTwoSources();
        _world.FailWrite("sprk_todo", TodoUnderEvent);
        SetupEventMetadata();
        _dataverse
            .Setup(d => d.PatchAsync($"sprk_events({Event:D})", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, _, _) => _world.SetPair("sprk_event", Event, Invoice.ToString()))
            .ReturnsAsync(DataverseUserResponse.Ok(204, body: null));

        var result = await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: MoveThePairToTheInvoice()), BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeTrue("the caller's update was written; a child the re-stamp could not write is the job's");
        _world.Lookup("sprk_event", Event, "sprk_regardingmatter").Should().Be(MatterB);
        _world.Lookup("sprk_todo", TodoUnderEvent, "sprk_regardingmatter").Should().Be(MatterA,
            "the failed child keeps its old copy until the reconciliation job (the storage resolver refuses it as stale meanwhile)");
    }

    [Fact(DisplayName = "Task 156 (owner round 8 item 1): when the caller's own update is refused, nothing is re-stamped and nothing is read app-only")]
    public async Task ExecuteChatAsync_UsersUpdateRefused_RestampsNothing()
    {
        SeedEventWithTwoSources();
        Mock.Get(_world.Service).Invocations.Clear();
        SetupEventMetadata();
        _dataverse
            .Setup(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied,
                "The user does not have write access to this record."));

        var result = await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: MoveThePairToTheInvoice()), BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        Mock.Get(_world.Service).Invocations.Should().BeEmpty("the re-stamp runs only after the caller's own update succeeded");
        _world.Lookup("sprk_todo", TodoUnderEvent, "sprk_regardingmatter").Should().Be(MatterA);
    }

    [Fact(DisplayName = "Task 156 (owner round 8 item 1): an update that cannot move a copy reads and writes nothing app-only")]
    public async Task ExecuteChatAsync_WriteThatCannotMoveACopy_ReadsAndWritesNothing()
    {
        SetupAccountMetadata();
        _dataverse
            .Setup(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(204, body: null));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$$"""
            {"tablename":"account","recordId":"{{{Guid.NewGuid():D}}}","item":{"name":"x"}}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        Mock.Get(_world.Service).Invocations.Should().BeEmpty("a write that cannot move a stamp costs nothing");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // User-context outcomes (spec MUST) + update-only semantics
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_UserLacksWritePrivilege_SurfacesAccessDeniedNotEscalation()
    {
        SetupAccountMetadata();
        _dataverse
            .Setup(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied,
                "The user does not have write access to this record."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$$"""
            {"tablename":"account","recordId":"{{{Guid.NewGuid():D}}}","item":{"name":"x"}}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            because: "row-level write security outcomes flow through unchanged — the user's context is the only context");
    }

    [Fact]
    public async Task ExecuteChatAsync_RecordMissingOrInvisible_SurfacesNotFound_NeverUpsertCreates()
    {
        // If-Match: * makes PATCH update-only: Dataverse answers 404 for a record that does not
        // exist or is invisible to the user; the handler surfaces exactly that.
        SetupAccountMetadata();
        _dataverse
            .Setup(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound,
                "account with Id = … does not exist."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$$"""
            {"tablename":"account","recordId":"{{{Guid.NewGuid():D}}}","item":{"name":"x"}}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound);
        _dataverse.Verify(
            d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            failMessage: "update_record must never fall back to creating the record");
        _dataverse.Verify(
            d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_TableInvisibleToUser_FailsNotFoundBeforeAnyWrite()
    {
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound,
                "The entity with a name = 'secrettable' was not found in the MetadataCache."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$$"""
            {"tablename":"secrettable","recordId":"{{{Guid.NewGuid():D}}}","item":{"name":"x"}}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound);
        _dataverse.Verify(
            d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            failMessage: "a table invisible to the user must 404 at metadata resolution BEFORE any write is attempted");
    }

    [Fact]
    public async Task ExecuteChatAsync_MissingUserContext_FailsClosed()
    {
        _dataverse
            .Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired,
                "No bearer token on the current request; dataverse.* tools execute only under an authenticated user's session."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$$"""
            {"tablename":"account","recordId":"{{{Guid.NewGuid():D}}}","item":{"name":"x"}}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 / NFR-07 telemetry — column VALUES never appear in logs
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_NeverLogsColumnValues_Adr015()
    {
        const string sensitiveValue = "Privileged legal strategy memo contents for opposing counsel review";
        SetupAccountMetadata();
        _dataverse
            .Setup(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(204, body: null));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$$"""
            {"tablename":"account","recordId":"{{{Guid.NewGuid():D}}}","item":{"description":"{{{sensitiveValue}}}"}}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        AssertTelemetryRespectsAdr015(sensitiveValue);
    }
}
