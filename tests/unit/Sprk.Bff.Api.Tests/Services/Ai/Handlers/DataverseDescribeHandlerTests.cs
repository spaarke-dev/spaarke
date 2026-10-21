using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Identity;
using Xunit;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="DataverseDescribeHandler"/> (spaarke-ai-architecture-redesign-r1
/// task 008, FR-P0-07 read half).
/// </summary>
/// <remarks>
/// <para>
/// The Dataverse boundary is mocked at <see cref="IDataverseUserClient"/> (module boundary —
/// NOT an HttpMessageHandler mock, per docs/standards/TEST-ARCHITECTURE.md mock rules /
/// ADR-038). Tests exercise the handler under a simulated test USER's security context: the
/// mocked boundary returns exactly what Dataverse would return for that user (403 for records
/// the user cannot read, 404 for records invisible to the user) and the tests assert the
/// handler surfaces the user-scoped outcome instead of escalating.
/// </para>
/// </remarks>
public sealed class DataverseDescribeHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Guid CallerSystemUserId = Guid.Parse("0a0a0a0a-0000-0000-0000-00000000c413");

    private readonly Mock<IDataverseUserClient> _dataverse = new();
    private readonly HttpContextAccessor _httpContextAccessor = new()
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) }
    };
    private readonly Mock<ICallerSystemUserResolver> _callerResolver = new();
    private readonly Mock<ISystemUserIdentityResolver> _identityResolver = new();

    public DataverseDescribeHandlerTests()
    {
        // Default caller: a resolved INTERNAL systemuser (sprk_isexternal not true).
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
        _identityResolver
            .Setup(r => r.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private DataverseDescribeHandler CreateHandler() =>
        new(_dataverse.Object, CreateLogger<DataverseDescribeHandler>(), _httpContextAccessor,
            _callerResolver.Object, _identityResolver.Object);

    private void AsExternalCaller() =>
        _identityResolver
            .Setup(r => r.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

    private static AnalysisTool BuildDescribeTool() =>
        BuildAnalysisTool(handlerClass: nameof(DataverseDescribeHandler), name: "SYS-Dataverse Describe");

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests (HandlerContractTestTemplate, retargeted)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var services = BuildToolFrameworkServiceCollection();

        var registeredImplementations = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .ToList();

        registeredImplementations.Should().Contain(
            typeof(DataverseDescribeHandler),
            because: "the handler type must be auto-discovered by the assembly scan (ADR-010: no manual DI lines per handler)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        CreateHandler().HandlerId.Should().Be(
            nameof(DataverseDescribeHandler),
            because: "HandlerId == nameof(handler class) routes sprk_handlerclass at runtime");
    }

    [Fact]
    public void Metadata_IsValid()
    {
        var metadata = CreateHandler().Metadata;
        metadata.Should().NotBeNull();
        metadata.Name.Should().NotBeNullOrWhiteSpace();
        metadata.Description.Should().NotBeNullOrWhiteSpace();
        metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void SupportedToolTypes_IsNonEmpty()
    {
        CreateHandler().SupportedToolTypes.Should().NotBeNullOrEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Playbook-context rejection (chat-only tool)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_InPlaybookContext_Rejects()
    {
        var result = CreateHandler().Validate(BuildToolExecutionContext(), BuildDescribeTool());
        result.IsValid.Should().BeFalse(because: "dataverse.describe is an agent-loop (chat) tool");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Argument validation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_MissingPath_Fails()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var result = CreateHandler().ValidateChat(ctx, BuildDescribeTool());
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainMatch("*'path'*");
    }

    [Fact]
    public void ValidateChat_MissingTenantId_Fails()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"path":"tables/"}""") with { TenantId = "" };
        var result = CreateHandler().ValidateChat(ctx, BuildDescribeTool());
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteChatAsync_GaOnlyPathSegment_ReturnsStructuredValidationError()
    {
        // skills/ + scopes/ exist on the GA MCP transport only — the native handler must
        // refuse with a clear message, never guess.
        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"path":"skills/"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("MCP transport");
        _dataverse.VerifyNoOtherCalls();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // tables/ — table list under the user's metadata visibility
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_TableListPath_ReturnsTablesWithGaStylePaths()
    {
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions?")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""
                {
                  "value": [
                    { "LogicalName": "account", "EntitySetName": "accounts", "IsPrivate": false,
                      "DisplayName": { "UserLocalizedLabel": { "Label": "Account" } } },
                    { "LogicalName": "sprk_matter", "EntitySetName": "sprk_matters", "IsPrivate": false,
                      "DisplayName": { "UserLocalizedLabel": { "Label": "Matter" } } },
                    { "LogicalName": "privatething", "EntitySetName": "privatethings", "IsPrivate": true }
                  ]
                }
                """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"path":"tables/"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.Data!.Value;
        data.GetProperty("kind").GetString().Should().Be("table_list");
        data.GetProperty("count").GetInt32().Should().Be(2, because: "IsPrivate tables are excluded");
        var paths = data.GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("path").GetString()).ToList();
        paths.Should().Contain("tables/account");
        paths.Should().Contain("tables/sprk_matter");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // tables/{t}/records/{guid} — record read under the USER's row-level security
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_RecordPath_ReturnsFieldsAndCitation()
    {
        var recordId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='account')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "accounts", "PrimaryIdAttribute": "accountid" }""")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"accounts({recordId:D})")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                {
                  "@odata.etag": "W/\"123\"",
                  "accountid": "{{recordId:D}}",
                  "name": "Contoso Legal Fixture",
                  "revenue": 125000.5
                }
                """)));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $$"""{"path":"tables/account/records/{{recordId:D}}"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.Data!.Value;
        data.GetProperty("kind").GetString().Should().Be("record");
        data.GetProperty("entity").GetString().Should().Be("account");
        data.GetProperty("fields").GetProperty("name").GetString().Should().Be("Contoso Legal Fixture");
        data.TryGetProperty("fields", out var fields).Should().BeTrue();
        fields.EnumerateObject().Select(p => p.Name).Should().NotContain(n => n.StartsWith("@odata"),
            because: "OData annotations are stripped from the LLM-facing payload");

        // ADR-039 grounding: adapter-level citation metadata present with the GA-style path.
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Citations);
        var citations = (IEnumerable<ToolResultCitation>)result.Metadata![ToolResultMetadataKeys.Citations]!;
        citations.Should().ContainSingle(c => c.ChunkId == $"tables/account/records/{recordId:D}" && c.SourceName == "account");
    }

    [Fact]
    public async Task ExecuteChatAsync_RecordUserCannotRead_SurfacesAccessDeniedNotEscalation()
    {
        // Test-user security context: Dataverse (via the user's OBO token) refuses the read.
        // The handler MUST surface the denial — never retry app-only (there is no app-only path).
        var recordId = Guid.NewGuid();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='account')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "accounts", "PrimaryIdAttribute": "accountid" }""")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("accounts(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied,
                "The user does not have read access to this record."));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $$"""{"path":"tables/account/records/{{recordId:D}}"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            because: "row-level security outcomes flow through unchanged — the user's context is the only context");
    }

    [Fact]
    public async Task ExecuteChatAsync_MissingUserContext_FailsClosed()
    {
        // Simulates the boundary refusing because no user bearer token exists on the request —
        // the MUST-rule fail-closed path (never app-only).
        _dataverse
            .Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired,
                "No bearer token on the current request; dataverse.* tools execute only under an authenticated user's session."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"path":"tables/"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 / NFR-07 telemetry — record content never appears in logs
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_NeverLogsRecordContent_Adr015()
    {
        const string sensitiveValue = "Contoso Confidential Settlement Amount 9,750,000";
        var recordId = Guid.NewGuid();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='account')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "accounts", "PrimaryIdAttribute": "accountid" }""")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("accounts(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                { "accountid": "{{recordId:D}}", "name": "{{sensitiveValue}}" }
                """)));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $$"""{"path":"tables/account/records/{{recordId:D}}"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        AssertTelemetryRespectsAdr015(sensitiveValue);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ISS-010 (external-access r3 task 078) — external callers and communication records
    // ═════════════════════════════════════════════════════════════════════════════

    private static readonly Guid CommunicationId = Guid.Parse("c0000000-0000-0000-0000-0000000000d1");
    private static readonly Guid ThreadId = Guid.Parse("70000000-0000-0000-0000-0000000000d1");

    private List<string> SetupCommunicationRecord(string isInternalOnlyJson, string threadValueJson)
    {
        var requests = new List<string>();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_communication')")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => requests.Add(p))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "sprk_communications", "PrimaryIdAttribute": "sprk_communicationid" }""")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications(")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => requests.Add(p))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                {
                  "sprk_communicationid": "{{CommunicationId:D}}",
                  "sprk_subject": "Strategy",
                  "sprk_body": "Body text",
                  "sprk_isinternalonly": {{isInternalOnlyJson}},
                  "_sprk_communicationthread_value": {{threadValueJson}}
                }
                """)));
        return requests;
    }

    private void SetupThreadPrivacy(int privacyState) =>
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communicationthreads?")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                { "value": [ { "sprk_communicationthreadid": "{{ThreadId:D}}", "sprk_privacystate": {{privacyState}} } ] }
                """)));

    private Task<ToolResult> DescribeCommunicationAsync() =>
        CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: $$"""{"path":"tables/sprk_communication/records/{{CommunicationId:D}}"}"""),
            BuildDescribeTool(), CancellationToken.None);

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerShareableRecordOnOpenThread_ReturnsRecord()
    {
        AsExternalCaller();
        var requests = SetupCommunicationRecord("false", $"\"{ThreadId:D}\"");
        SetupThreadPrivacy(100000000);

        var result = await DescribeCommunicationAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("fields").GetProperty("sprk_body").GetString().Should().Be("Body text");
        requests.Should().Contain($"sprk_communications({CommunicationId:D})");
        _dataverse.Verify(d => d.GetAsync(
            $"sprk_communicationthreads?$select=sprk_communicationthreadid,sprk_privacystate&$filter=sprk_communicationthreadid eq {ThreadId:D}&$top=1",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("true", true, 100000000)]   // internal-only
    [InlineData("null", true, 100000000)]   // flag unset
    [InlineData("false", true, 100000001)]  // Private thread
    [InlineData("false", false, 100000000)] // no thread
    public async Task ExecuteChatAsync_ExternalCallerRecordHiddenByRule_AnswersNotFound(
        string isInternalOnlyJson, bool hasThread, int privacyState)
    {
        AsExternalCaller();
        SetupCommunicationRecord(isInternalOnlyJson, hasThread ? $"\"{ThreadId:D}\"" : "null");
        SetupThreadPrivacy(privacyState);

        var result = await DescribeCommunicationAsync();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound,
            because: "a hidden record answers with the not-found code a guarded missing record gets");
        result.ErrorMessage.Should().Be(ExternalCallerCommunicationGuard.RecordNotVisibleMessage,
            because: "and with the same message (ExecuteChatAsync_ExternalCallerMissingRecord_AnswersExactlyLikeHiddenRecord pins the pair)");
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerMissingRecord_AnswersExactlyLikeHiddenRecord()
    {
        // Review K4: the answer must not tell an external caller that a hidden record exists.
        AsExternalCaller();
        SetupCommunicationRecord("true", $"\"{ThreadId:D}\"");
        SetupThreadPrivacy(100000000);
        var hidden = await DescribeCommunicationAsync();

        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound,
                $"Entity 'sprk_communication' With Id = {CommunicationId:D} Does Not Exist"));
        var missing = await DescribeCommunicationAsync();

        hidden.Success.Should().BeFalse();
        missing.Success.Should().BeFalse();
        missing.ErrorCode.Should().Be(hidden.ErrorCode);
        missing.ErrorMessage.Should().Be(hidden.ErrorMessage,
            because: "a guarded not-found and a hidden record carry one message, not Dataverse's own 404 text");
        missing.Data.Should().BeNull();
    }

    [Theory]
    [InlineData("sprk_emailartifact")]
    [InlineData("sprk_attachmentartifact")]
    [InlineData("sprk_emailreviewlog")]
    [InlineData("sprk_affinity")]
    public async Task ExecuteChatAsync_ExternalCallerContentTableOutsidePrefix_RefusedWithNoDataverseCall(string table)
    {
        AsExternalCaller();

        var result = await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: $$"""{"path":"tables/{{table}}/records/{{Guid.NewGuid():D}}"}"""),
            BuildDescribeTool(), CancellationToken.None);

        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        result.Data.Should().BeNull();
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalityReadThrows_ErrorWithNoData()
    {
        // Review F2: a failing externality read is an error with no data, never "internal".
        _identityResolver
            .Setup(r => r.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));
        SetupCommunicationRecord("true", "null");

        var result = await DescribeCommunicationAsync();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_CallerLookupFailed_ErrorWithNoData()
    {
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved(ExternalCallerCommunicationGuard.LookupFailedReason));
        SetupCommunicationRecord("true", "null");

        var result = await DescribeCommunicationAsync();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── sprk_document records carrying a related communication (review round 1, F1) ──

    private static readonly Guid DocumentId = Guid.Parse("d0000000-0000-0000-0000-0000000000d1");
    private static readonly Guid RelatedCommunicationId = Guid.Parse("c1000000-0000-0000-0000-0000000000d1");

    private List<string> SetupDocumentRecord(string relatedCommunicationJson)
    {
        var requests = new List<string>();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_document')")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => requests.Add(p))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "sprk_documents", "PrimaryIdAttribute": "sprk_documentid" }""")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_documents(")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => requests.Add(p))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                {
                  "sprk_documentid": "{{DocumentId:D}}",
                  "sprk_documentname": "RE: Strategy.eml",
                  "sprk_filesummary": "Summary text",
                  "_sprk_relatedcommunication_value": {{relatedCommunicationJson}}
                }
                """)));
        return requests;
    }

    private List<string> SetupRelatedCommunication(string isInternalOnlyJson, string threadValueJson)
    {
        var requests = new List<string>();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications?")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => requests.Add(p))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(isInternalOnlyJson == "absent"
                ? """{ "value": [] }"""
                : $$"""
                  { "value": [ { "sprk_communicationid": "{{RelatedCommunicationId:D}}", "sprk_isinternalonly": {{isInternalOnlyJson}}, "_sprk_communicationthread_value": {{threadValueJson}} } ] }
                  """)));
        return requests;
    }

    private Task<ToolResult> DescribeDocumentAsync() =>
        CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: $$"""{"path":"tables/sprk_document/records/{{DocumentId:D}}"}"""),
            BuildDescribeTool(), CancellationToken.None);

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocumentWithNoRelatedCommunication_ReturnsRecordWithNoExtraRead()
    {
        AsExternalCaller();
        var requests = SetupDocumentRecord("null");

        var result = await DescribeDocumentAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        requests.Should().HaveCount(2);
        _dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communication")), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocumentWithShareableRelatedCommunication_ReturnsRecord()
    {
        AsExternalCaller();
        SetupDocumentRecord($"\"{RelatedCommunicationId:D}\"");
        var communicationReads = SetupRelatedCommunication("false", $"\"{ThreadId:D}\"");
        SetupThreadPrivacy(100000000);

        var result = await DescribeDocumentAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("fields").GetProperty("sprk_filesummary").GetString().Should().Be("Summary text");
        communicationReads.Should().ContainSingle().Which.Should().Be(
            "sprk_communications?$select=sprk_communicationid,sprk_isinternalonly,_sprk_communicationthread_value" +
            $"&$filter=sprk_communicationid eq {RelatedCommunicationId:D}&$top=1");
    }

    [Theory]
    [InlineData("true", true, 100000000)]    // related communication internal-only
    [InlineData("null", true, 100000000)]    // flag unset
    [InlineData("false", true, 100000001)]   // Private thread
    [InlineData("false", false, 100000000)]  // no thread
    [InlineData("absent", false, 100000000)] // related communication unreadable or missing
    public async Task ExecuteChatAsync_ExternalCallerDocumentHiddenByRelatedCommunication_AnswersLikeNotFound(
        string isInternalOnlyJson, bool hasThread, int privacyState)
    {
        AsExternalCaller();
        SetupDocumentRecord($"\"{RelatedCommunicationId:D}\"");
        SetupRelatedCommunication(isInternalOnlyJson, hasThread ? $"\"{ThreadId:D}\"" : "null");
        SetupThreadPrivacy(privacyState);

        var result = await DescribeDocumentAsync();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound);
        result.ErrorMessage.Should().Be(ExternalCallerCommunicationGuard.RecordNotVisibleMessage);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocumentRelatedReadFails_ErrorWithNoData()
    {
        AsExternalCaller();
        SetupDocumentRecord($"\"{RelatedCommunicationId:D}\"");
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications?")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "Server error."));

        var result = await DescribeDocumentAsync();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
    }

    // ── Task 188 (D7 T7, #2126): THE external document predicate in describe ──

    private static readonly Guid ParentEmlId = Guid.Parse("e1880000-0000-0000-0000-0000000000d1");

    private void SetupNarrowedDocumentRecord(string permissionJson, string parentJson)
    {
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_document')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "sprk_documents", "PrimaryIdAttribute": "sprk_documentid" }""")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_documents(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                {
                  "sprk_documentid": "{{DocumentId:D}}",
                  "sprk_documentname": "attachment.pdf",
                  "sprk_accesspermission": {{permissionJson}},
                  "_sprk_relatedcommunication_value": null,
                  "_sprk_parentdocument_value": {{parentJson}}
                }
                """)));
    }

    [Theory]
    [InlineData("100000002", "null", null, null)]                          // own Restricted
    [InlineData("null", "parent", "100000002", null)]                      // parent .eml Restricted, no link anywhere
    [InlineData("null", "parent", "null", "internal")]                     // parent .eml of an internal-only email
    public async Task ExecuteChatAsync_ExternalCallerDocument_Task188_HiddenByThePredicate_AnswersLikeNotFound(
        string permissionJson, string parent, string? parentPermissionJson, string? parentCommunication)
    {
        AsExternalCaller();
        SetupNarrowedDocumentRecord(permissionJson, parent == "parent" ? $"\"{ParentEmlId:D}\"" : "null");
        DocumentNarrowingReads.AnswerByKey(_dataverse, "sprk_documents", "sprk_documentid", $$"""
            { "sprk_documentid": "{{ParentEmlId:D}}", "sprk_accesspermission": {{parentPermissionJson ?? "null"}},
              "_sprk_relatedcommunication_value": {{(parentCommunication is null ? "null" : $"\"{RelatedCommunicationId:D}\"")}},
              "_sprk_parentdocument_value": null }
            """);
        SetupRelatedCommunication("true", $"\"{ThreadId:D}\"");
        SetupThreadPrivacy(100000000);

        var result = await DescribeDocumentAsync();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound);
        result.ErrorMessage.Should().Be(ExternalCallerCommunicationGuard.RecordNotVisibleMessage);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocument_Task188_StandardParentWithNoLink_ReturnsRecord()
    {
        AsExternalCaller();
        SetupNarrowedDocumentRecord("null", $"\"{ParentEmlId:D}\"");
        DocumentNarrowingReads.AnswerByKey(_dataverse, "sprk_documents", "sprk_documentid", $$"""
            { "sprk_documentid": "{{ParentEmlId:D}}", "sprk_accesspermission": 100000000,
              "_sprk_relatedcommunication_value": null, "_sprk_parentdocument_value": null }
            """);

        var result = await DescribeDocumentAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("fields").GetProperty("sprk_documentname").GetString().Should().Be("attachment.pdf");
    }

    [Fact]
    public async Task ExecuteChatAsync_InternalCallerDocumentWithInternalOnlyRelatedCommunication_UnchangedAndNoExtraRead()
    {
        var requests = SetupDocumentRecord($"\"{RelatedCommunicationId:D}\"");

        var result = await DescribeDocumentAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        requests.Should().Equal(
            "EntityDefinitions(LogicalName='sprk_document')?$select=EntitySetName,PrimaryIdAttribute",
            $"sprk_documents({DocumentId:D})");
        _dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communication")), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerThreadPrivacyReadFails_ReturnsErrorWithNoData()
    {
        AsExternalCaller();
        SetupCommunicationRecord("false", $"\"{ThreadId:D}\"");
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communicationthreads?")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "Server error."));

        var result = await DescribeCommunicationAsync();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
    }

    [Theory]
    [InlineData("sprk_communicationthread")]
    [InlineData("sprk_communicationattachment")]
    public async Task ExecuteChatAsync_ExternalCallerOtherCommunicationFamilyRecord_RefusedWithNoDataverseCall(string table)
    {
        AsExternalCaller();

        var result = await CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: $$"""{"path":"tables/{{table}}/records/{{Guid.NewGuid():D}}"}"""),
            BuildDescribeTool(), CancellationToken.None);

        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_UnresolvedCallerOnCommunicationRecord_TreatedAsExternal()
    {
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-matching-systemuser"));
        SetupCommunicationRecord("true", $"\"{ThreadId:D}\"");

        var result = await DescribeCommunicationAsync();

        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound, because: "an unresolvable caller is external (fail closed)");
    }

    [Fact]
    public async Task ExecuteChatAsync_InternalCallerInternalOnlyRecord_UnchangedAndNoThreadRead()
    {
        var requests = SetupCommunicationRecord("true", "null");

        var result = await DescribeCommunicationAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        requests.Should().Equal(
            "EntityDefinitions(LogicalName='sprk_communication')?$select=EntitySetName,PrimaryIdAttribute",
            $"sprk_communications({CommunicationId:D})");
        _dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communicationthreads?")), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // shared plumbing
    // ═════════════════════════════════════════════════════════════════════════════

    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }
}
