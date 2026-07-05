using System.Text.Json;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="DataverseDeleteRecordHandler"/> (spaarke-ai-architecture-redesign-r1
/// task 009, FR-P0-07 write half).
/// </summary>
/// <remarks>
/// The Dataverse boundary is mocked at <see cref="IDataverseUserClient"/> (module boundary —
/// NOT an HttpMessageHandler mock, per ADR-038). Tests exercise the handler under a simulated
/// test USER's security context and assert the user-scoped outcome flows through unchanged.
/// The <c>hasUserApproved</c> checks assert GA-frozen argument semantics only — the platform
/// confirmation gate (FR-P2-02, task 031) is deliberately NOT implemented in the handler.
/// </remarks>
public sealed class DataverseDeleteRecordHandlerTests : TypedToolHandlerTestFixture
{
    private readonly Mock<IDataverseUserClient> _dataverse = new();

    private DataverseDeleteRecordHandler CreateHandler() =>
        new(_dataverse.Object, CreateLogger<DataverseDeleteRecordHandler>());

    private static AnalysisTool BuildDeleteTool() =>
        BuildAnalysisTool(handlerClass: nameof(DataverseDeleteRecordHandler), name: "SYS-Dataverse Delete Record");

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
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "accounts" }""")));

    // ═════════════════════════════════════════════════════════════════════════════
    // Contract + argument validation (GA: delete_record(tablename, hasUserApproved, recordId))
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        CreateHandler().HandlerId.Should().Be(nameof(DataverseDeleteRecordHandler));
    }

    [Fact]
    public void Validate_InPlaybookContext_Rejects()
    {
        var result = CreateHandler().Validate(BuildToolExecutionContext(), BuildDeleteTool());
        result.IsValid.Should().BeFalse(because: "dataverse.delete_record is an agent-loop (chat) tool");
    }

    [Theory]
    [InlineData("{}", "*tablename*")]
    [InlineData("""{"tablename":"account","recordId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"}""", "*hasUserApproved*")]
    [InlineData("""{"tablename":"account","hasUserApproved":"yes","recordId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"}""", "*hasUserApproved*")]
    [InlineData("""{"tablename":"account","hasUserApproved":true}""", "*recordId*")]
    [InlineData("""{"tablename":"account","hasUserApproved":true,"recordId":"not-a-guid"}""", "*recordId*")]
    public void ValidateChat_InvalidArgs_Fails(string argsJson, string expectedErrorPattern)
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);
        var result = CreateHandler().ValidateChat(ctx, BuildDeleteTool());
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainMatch(expectedErrorPattern);
    }

    [Fact]
    public async Task ExecuteChatAsync_UserApprovalFalse_RefusesWithoutTouchingDataverse()
    {
        // GA-frozen argument semantics ("proceed solely on explicit user consent") — NOT the
        // platform confirmation gate, which keys on sprk_sideeffectclass upstream (task 031).
        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$"""
            {"tablename":"account","hasUserApproved":false,"recordId":"{{Guid.NewGuid():D}}"}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDeleteTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("approval");
        _dataverse.VerifyNoOtherCalls();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Successful delete — round-shape (no citation: the record no longer exists)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ValidDelete_DeletesViaResolvedEntitySet()
    {
        var recordId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        SetupAccountMetadata();

        string? deletedPath = null;
        _dataverse
            .Setup(d => d.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((path, _) => deletedPath = path)
            .ReturnsAsync(DataverseUserResponse.Ok(204, body: null));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$"""
            {"tablename":"account","hasUserApproved":true,"recordId":"{{recordId:D}}"}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDeleteTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.Data!.Value;
        data.GetProperty("tool").GetString().Should().Be("dataverse.delete_record");
        data.GetProperty("tablename").GetString().Should().Be("account");
        data.GetProperty("recordId").GetString().Should().Be(recordId.ToString("D"));
        data.GetProperty("deleted").GetBoolean().Should().BeTrue();

        deletedPath.Should().Be($"accounts({recordId:D})",
            because: "the DELETE targets the metadata-resolved entity set, never a guessed collection name");

        // Deleted records are not citable — the tables/… path is no longer replayable.
        (result.Metadata is null || !result.Metadata.ContainsKey(ToolResultMetadataKeys.Citations))
            .Should().BeTrue(because: "delete results must not emit citations to a record that no longer exists");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // User-context outcomes (spec MUST)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_UserLacksDeletePrivilege_SurfacesAccessDeniedNotEscalation()
    {
        SetupAccountMetadata();
        _dataverse
            .Setup(d => d.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied,
                "The user does not have delete access to this record."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$"""
            {"tablename":"account","hasUserApproved":true,"recordId":"{{Guid.NewGuid():D}}"}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDeleteTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            because: "a delete the user lacks privileges for fails with the USER's own access error (spec MUST rule)");
    }

    [Fact]
    public async Task ExecuteChatAsync_TableInvisibleToUser_FailsNotFoundBeforeAnyDelete()
    {
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound,
                "The entity with a name = 'secrettable' was not found in the MetadataCache."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$"""
            {"tablename":"secrettable","hasUserApproved":true,"recordId":"{{Guid.NewGuid():D}}"}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDeleteTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound);
        _dataverse.Verify(
            d => d.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            failMessage: "a table invisible to the user must 404 at metadata resolution BEFORE any delete is attempted");
    }

    [Fact]
    public async Task ExecuteChatAsync_MissingUserContext_FailsClosed()
    {
        _dataverse
            .Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired,
                "No bearer token on the current request; dataverse.* tools execute only under an authenticated user's session."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$"""
            {"tablename":"account","hasUserApproved":true,"recordId":"{{Guid.NewGuid():D}}"}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDeleteTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
    }
}
