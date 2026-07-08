using System.Text.Json;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
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

    private DataverseUpdateRecordHandler CreateHandler() =>
        new(_dataverse.Object, CreateLogger<DataverseUpdateRecordHandler>());

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
