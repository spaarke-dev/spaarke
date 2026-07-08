using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="EmailDraftToolHandler"/> (spaarke-ai-architecture-redesign-r1
/// task 041, FR-P3-02 — the email.draft write shape of the draft-correspondence capability).
/// </summary>
/// <remarks>
/// <para>
/// The Dataverse boundary is mocked at <see cref="IDataverseUserClient"/> (module boundary —
/// NOT an HttpMessageHandler mock, per docs/standards/TEST-ARCHITECTURE.md / ADR-038).
/// </para>
/// <para>
/// The load-bearing group is <b>DRAFT-ONLY invariant</b>: the posted record MUST carry
/// <c>statuscode = 1 (Draft)</c> pinned server-side, the handler must never issue anything but
/// the metadata GET + the single record POST (no send-shaped call exists on
/// <see cref="IDataverseUserClient"/> and none is added indirectly), and no argument the model
/// supplies can alter the pinned status/direction/type discriminators.
/// </para>
/// </remarks>
public sealed class EmailDraftToolHandlerTests : TypedToolHandlerTestFixture
{
    private readonly Mock<IDataverseUserClient> _dataverse = new();

    private EmailDraftToolHandler CreateHandler() =>
        new(_dataverse.Object, CreateLogger<EmailDraftToolHandler>());

    private static AnalysisTool BuildDraftTool() =>
        BuildAnalysisTool(handlerClass: nameof(EmailDraftToolHandler), name: "SYS-Email Draft");

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private const string ValidArgs =
        """{"subject":"Acme MSA — indemnity findings","body":"Dear Ms. Smith,\n\nPlease find our findings below.\n\nKind regards,\n[Your name]","to":["jane.smith@smithlegal.example"],"source_refs":["f7dc4a00-6b79-f111-ab0e-7ced8ddc4cc6@t2"]}""";

    private void SetupCommunicationMetadata()
    {
        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_communication')?$select=EntitySetName")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                """{ "EntitySetName": "sprk_communications", "PrimaryIdAttribute": "sprk_communicationid" }""")));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests (HandlerContractTestTemplate, retargeted)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);

        services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .Should().Contain(
                typeof(EmailDraftToolHandler),
                because: "the handler type must be auto-discovered by the assembly scan (ADR-010) — the FR-P0-04 " +
                         "bijection health check requires a registered handler for the seeded email.draft row");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        CreateHandler().HandlerId.Should().Be(
            nameof(EmailDraftToolHandler),
            because: "HandlerId == nameof(handler class) routes sprk_handlerclass at runtime");
    }

    [Fact]
    public void Validate_InPlaybookContext_Rejects()
    {
        var result = CreateHandler().Validate(BuildToolExecutionContext(), BuildDraftTool());
        result.IsValid.Should().BeFalse(because: "email.draft is an agent-loop (chat) tool; the frozen engine keeps SendEmailNodeExecutor");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Argument validation (closed vocabulary)
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("{}", "*subject*")]
    [InlineData("""{"subject":"s","to":["a@b.example"]}""", "*body*")]
    [InlineData("""{"subject":"s","body":"b"}""", "*'to'*")]
    [InlineData("""{"subject":"s","body":"b","to":[]}""", "*at least one*")]
    [InlineData("""{"subject":"s","body":"b","to":["not-an-address"]}""", "*not an email address*")]
    [InlineData("""{"subject":"s","body":"b","to":["a@b.example"],"body_format":"markdown"}""", "*body_format*")]
    [InlineData("""{"subject":"s","body":"b","to":["a@b.example"],"regarding":{"table":"systemuser","recordId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"}}""", "*regarding.table*")]
    [InlineData("""{"subject":"s","body":"b","to":["a@b.example"],"regarding":{"table":"sprk_matter","recordId":"not-a-guid"}}""", "*recordId*")]
    public void ValidateChat_InvalidArgs_Fails(string argsJson, string expectedErrorPattern)
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);
        var result = CreateHandler().ValidateChat(ctx, BuildDraftTool());
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainMatch(expectedErrorPattern);
    }

    [Fact]
    public void ValidateChat_ValidArgs_Succeeds()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: ValidArgs);
        CreateHandler().ValidateChat(ctx, BuildDraftTool()).IsValid.Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // DRAFT-ONLY invariant (FR-P3-02 — the load-bearing group)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ValidDraft_PostsDraftStatusPinnedServerSide()
    {
        var createdId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        SetupCommunicationMetadata();

        string? postedPath = null;
        string? postedBody = null;
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, CancellationToken>((path, body, _, _) => { postedPath = path; postedBody = body; })
            .ReturnsAsync(DataverseUserResponse.Ok(201, ParseJson(
                $$"""{ "sprk_communicationid": "{{createdId}}" }""")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: ValidArgs);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDraftTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        postedPath.Should().Be("/api/data/v9.2/sprk_communications",
            "the DRAFT record is created in the Communication service's own table");

        using var posted = JsonDocument.Parse(postedBody!);
        var root = posted.RootElement;
        root.GetProperty("statuscode").GetInt32().Should().Be(1,
            "DRAFT-ONLY invariant: statuscode is server-pinned to CommunicationStatus.Draft — never model-supplied");
        root.GetProperty("statecode").GetInt32().Should().Be(0, "draft records are Active");
        root.GetProperty("sprk_direction").GetInt32().Should().Be(100000001, "outgoing correspondence");
        root.GetProperty("sprk_communicationtype").GetInt32().Should().Be(100000000, "email type");
        root.GetProperty("sprk_bodyformat").GetInt32().Should().Be(100000000, "plain text is the default body format");
        root.GetProperty("sprk_to").GetString().Should().Be("jane.smith@smithlegal.example");
        root.GetProperty("sprk_subject").GetString().Should().Be("Acme MSA — indemnity findings");
        root.GetProperty("sprk_name").GetString().Should().StartWith("Email: ");
        root.TryGetProperty("sprk_sentat", out _).Should().BeFalse("a DRAFT was never sent — no sent timestamp");

        // The result relays the DRAFT posture to the model (grounded turn instruction).
        result.Summary.Should().Contain("DRAFT").And.Contain("NO email was sent");

        // G-P3 UAT round-4 R4-6 pin (2026-07-07): the model-facing Summary carries
        // instruction text ("tell the user…") which LEAKED verbatim into the operator's
        // transcript on the gate-resume path. The handler now ALSO emits the user-facing
        // sentence — the transcript renders THIS, never the Summary.
        result.Metadata.Should().ContainKey(ToolResultMetadataKeys.UserSummary);
        var userSummary = (string)result.Metadata![ToolResultMetadataKeys.UserSummary]!;
        userSummary.Should().Be("Draft email created (1 recipient(s)) — ready for your review in Communications. No email was sent.");
        userSummary.Should().NotContain("tell the user",
            because: "instruction-to-model text must never reach the transcript");

        // R4-3: primary-record reference — the gate-resume seam composes the MDA link.
        result.Metadata.Should().ContainKey(ToolResultMetadataKeys.CreatedRecord);
        var record = (ToolCreatedRecord)result.Metadata![ToolResultMetadataKeys.CreatedRecord]!;
        record.EntityLogicalName.Should().Be("sprk_communication");
        record.RecordId.Should().Be(createdId);
    }

    /// <summary>
    /// G-P3 UAT round-4 R4-4 guidance pin (2026-07-07): "send this as an email" after a
    /// combined summary produced a generic draft REFERENCING AN ATTACHMENT that cannot
    /// exist (the capability cannot attach files). The tool description must carry the
    /// no-attachments rule + inline-the-content steering.
    /// </summary>
    [Fact]
    public void Metadata_Description_CarriesNoAttachmentsRule_AndInlineContentSteering()
    {
        var description = CreateHandler().Metadata.Description;

        description.Should().Contain("CANNOT carry attachments");
        description.Should().Contain("please find attached",
            because: "the ban names the exact phrase the round-4 draft invented");
        description.Should().Contain("INLINE",
            because: "conversation-produced content (summaries, findings) goes in the body");
    }

    [Fact]
    public async Task ExecuteChatAsync_ArgsAttemptingStatusOverride_CannotAlterPinnedDraftStatus()
    {
        // A steered model (NFR-03 hostile-document scenario) tries to smuggle status/direction
        // vocabulary through the args. The closed parser ignores unknown members entirely —
        // the posted record still carries the pinned Draft status.
        var createdId = Guid.NewGuid();
        SetupCommunicationMetadata();

        string? postedBody = null;
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, CancellationToken>((_, body, _, _) => postedBody = body)
            .ReturnsAsync(DataverseUserResponse.Ok(201, ParseJson(
                $$"""{ "sprk_communicationid": "{{createdId}}" }""")));

        var hostileArgs =
            """{"subject":"s","body":"b","to":["a@b.example"],"statuscode":659490002,"status":"send","sendImmediately":true,"sprk_direction":100000000}""";
        var ctx = BuildChatInvocationContext(toolArgumentsJson: hostileArgs);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDraftTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        using var posted = JsonDocument.Parse(postedBody!);
        posted.RootElement.GetProperty("statuscode").GetInt32().Should().Be(1,
            "no argument vocabulary exists that can alter the pinned Draft status");
        posted.RootElement.GetProperty("sprk_direction").GetInt32().Should().Be(100000001,
            "direction is a pinned constant, not model input");
        posted.RootElement.TryGetProperty("sendImmediately", out _).Should().BeFalse(
            "unknown members never reach the record");
    }

    [Fact]
    public async Task ExecuteChatAsync_ValidDraft_MakesNoDataverseCallBeyondMetadataAndSingleCreate()
    {
        // DRAFT-ONLY, mechanically: the handler's complete Dataverse surface for a valid draft
        // is ONE metadata GET + ONE record POST. Nothing send-shaped exists or is invoked.
        SetupCommunicationMetadata();
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(201, ParseJson(
                $$"""{ "sprk_communicationid": "{{Guid.NewGuid()}}" }""")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: ValidArgs);
        await CreateHandler().ExecuteChatAsync(ctx, BuildDraftTool(), CancellationToken.None);

        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _dataverse.Verify(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once);
        _dataverse.Verify(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _dataverse.Verify(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _dataverse.Verify(d => d.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _dataverse.VerifyNoOtherCalls();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Ledger grounding + regarding association (FR-P3-02 acceptance legs)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_WithRegardingMatter_MapsCommunicationServiceAssociationContract()
    {
        var matterId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var createdId = Guid.NewGuid();
        SetupCommunicationMetadata();

        // The write mapper resolves the regarding lookup's navigation property from metadata.
        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.Contains("ManyToOneRelationships")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                """
                { "LogicalName": "sprk_communication", "ManyToOneRelationships": [
                    { "ReferencingAttribute": "sprk_regardingmatter", "ReferencingEntityNavigationPropertyName": "sprk_RegardingMatter", "ReferencedEntity": "sprk_matter" }
                ] }
                """)));
        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_matter')?$select=EntitySetName")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "sprk_matters" }""")));

        string? postedBody = null;
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, CancellationToken>((_, body, _, _) => postedBody = body)
            .ReturnsAsync(DataverseUserResponse.Ok(201, ParseJson(
                $$"""{ "sprk_communicationid": "{{createdId}}" }""")));

        var args =
            $$"""{"subject":"s","body":"b","to":["a@b.example"],"regarding":{"table":"sprk_matter","recordId":"{{matterId}}","name":"Acme MSA Matter"},"source_refs":["f7dc4a00-6b79-f111-ab0e-7ced8ddc4cc6@t2","tables/sprk_matter/records/{{matterId}}"]}""";
        var ctx = BuildChatInvocationContext(toolArgumentsJson: args);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDraftTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        using var posted = JsonDocument.Parse(postedBody!);
        var root = posted.RootElement;
        root.GetProperty("sprk_RegardingMatter@odata.bind").GetString()
            .Should().Be($"/sprk_matters({matterId:D})",
                "the regarding lookup binds through the metadata-resolved navigation property " +
                "(Communication-service association contract)");
        root.GetProperty("sprk_regardingrecordid").GetString().Should().Be(matterId.ToString("D"));
        root.GetProperty("sprk_regardingrecordname").GetString().Should().Be("Acme MSA Matter");
        root.GetProperty("sprk_associationcount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteChatAsync_ValidDraft_EchoesLedgerSourceRefsAndRecordCitation()
    {
        var createdId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        SetupCommunicationMetadata();
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(201, ParseJson(
                $$"""{ "sprk_communicationid": "{{createdId}}" }""")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: ValidArgs);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDraftTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var dataJson = JsonSerializer.Serialize(result.Data);
        using var data = JsonDocument.Parse(dataJson);
        data.RootElement.GetProperty("status").GetString().Should().Be("draft");
        data.RootElement.GetProperty("communicationId").GetString().Should().Be(createdId.ToString("D"));
        data.RootElement.GetProperty("path").GetString()
            .Should().Be($"tables/sprk_communication/records/{createdId:D}");
        data.RootElement.GetProperty("sourceRefs")[0].GetString()
            .Should().Be("f7dc4a00-6b79-f111-ab0e-7ced8ddc4cc6@t2",
                "the ledger keys the draft was composed from are echoed for the ToolChain (ADR-039 grounding)");
        result.Metadata.Should().ContainKey(ToolResultMetadataKeys.Citations);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // User-OBO error surfacing (spec MUST — user's own access error, never escalated)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_UserLacksCreatePrivilege_SurfacesUsersOwnAccessError()
    {
        SetupCommunicationMetadata();
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied,
                "The user does not have create permission for sprk_communication."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: ValidArgs);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDraftTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            "a privilege-denied create surfaces the USER's own access error — no app-only escalation exists");
    }

    [Fact]
    public async Task ExecuteChatAsync_TableInvisibleToUser_FailsAtMetadataBeforeAnyWrite()
    {
        _dataverse
            .Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound,
                "Table not found or not visible to the calling user."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: ValidArgs);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDraftTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound);
        _dataverse.Verify(
            d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never,
            failMessage: "no write is attempted when the table is invisible to the user");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 / NFR-07 telemetry discipline
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Telemetry_CarriesCountsOnly_NeverSubjectBodyOrAddresses()
    {
        SetupCommunicationMetadata();
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(201, ParseJson(
                $$"""{ "sprk_communicationid": "{{Guid.NewGuid()}}" }""")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: ValidArgs);
        await CreateHandler().ExecuteChatAsync(ctx, BuildDraftTool(), CancellationToken.None);

        AssertTelemetryRespectsAdr015(
            "Acme MSA — indemnity findings",
            "Dear Ms. Smith",
            "jane.smith@smithlegal.example");
    }
}
