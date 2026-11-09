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

    /// <summary>Task 188: what the BFF reads APP-ONLY when it decides an external caller's document.</summary>
    private readonly AppOnlyDocumentWorld _appOnly = new();

    /// <summary>Task 098: the assistant tools' (a') invoice-family seam (a test that reaches it sets one).</summary>
    private Sprk.Bff.Api.Infrastructure.ExternalAccess.AppOnlyInvoiceFamilyHiding _hideInvoiceFamily = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyHidingStubs.ChildTablesShown;

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
            _callerResolver.Object, _identityResolver.Object, _appOnly.Narrowing, _hideInvoiceFamily);

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

    // #2253 fix round 3 (K): `$` matches before a final "\n", and the segment is lower-cased but not trimmed, so
    // "sprk_document\n" passed the name check and then missed the external-caller guard, which compares names exactly.
    [Theory]
    [InlineData("tables/sprk_document\n/records/6f0c1a52-0000-4000-8000-000000002253")]
    [InlineData("tables/sprk_document\n/")]
    public async Task ExecuteChatAsync_ATableNameWithATrailingNewline_IsRefused_AndNothingIsAsked(string path)
    {
        AsExternalCaller();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { path }));

        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
    // ═════════════════════════════════════════════════════════════════════════════
    // ebilling-r1 task 098 surface (d): the (a') invoice predicate on an invoice-family record
    // ═════════════════════════════════════════════════════════════════════════════

    private static readonly Guid InvoiceRecordId = Guid.Parse("09800000-0000-4000-8000-0000000000d1");

    private void SetupInvoiceRecord(bool exists = true)
    {
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_invoice')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("{ \"EntitySetName\": \"sprk_invoices\", \"PrimaryIdAttribute\": \"sprk_invoiceid\" }")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_invoices(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists
                ? DataverseUserResponse.Ok(200, ParseJson("{ \"sprk_invoiceid\": \"" + InvoiceRecordId.ToString("D") + "\", \"sprk_name\": \"INV-1\" }"))
                : DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "Entity 'sprk_invoice' Does Not Exist"));
    }

    private Task<ToolResult> DescribeInvoiceAsync() =>
        CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: "{\"path\":\"tables/sprk_invoice/records/" + InvoiceRecordId.ToString("D") + "\"}"),
            BuildDescribeTool(), CancellationToken.None);

    [Fact]
    public async Task ExecuteChatAsync_Task098_ExternalCaller_AHiddenInvoice_AnswersExactlyLikeAMissingOne()
    {
        AsExternalCaller();
        SetupInvoiceRecord();
        var calls = new List<string>();
        _hideInvoiceFamily = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyHidingStubs.Hiding(calls, InvoiceRecordId);
        var hidden = await DescribeInvoiceAsync();

        SetupInvoiceRecord(exists: false);
        var missing = await DescribeInvoiceAsync();

        hidden.Success.Should().BeFalse();
        hidden.Data.Should().BeNull();
        hidden.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound);
        hidden.ErrorMessage.Should().Be(ExternalCallerCommunicationGuard.RecordNotVisibleMessage);
        missing.ErrorCode.Should().Be(hidden.ErrorCode);
        missing.ErrorMessage.Should().Be(hidden.ErrorMessage, "a hidden invoice and a missing one are indistinguishable");
        calls.Should().Equal("sprk_invoice:1");
    }

    [Fact]
    public async Task ExecuteChatAsync_Task098_ExternalCaller_AVisibleInvoice_ReturnsTheRecord()
    {
        AsExternalCaller();
        SetupInvoiceRecord();
        _hideInvoiceFamily = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyHidingStubs.Hiding(null, Guid.NewGuid());

        var result = await DescribeInvoiceAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("fields").GetProperty("sprk_name").GetString().Should().Be("INV-1");
    }

    [Fact]
    public async Task ExecuteChatAsync_Task098_ExternalCaller_ThePredicateReadFails_ErrorWithNoData()
    {
        AsExternalCaller();
        SetupInvoiceRecord();
        _hideInvoiceFamily = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyHidingStubs.Throwing;

        var result = await DescribeInvoiceAsync();

        result.Success.Should().BeFalse("a predicate read fault can never turn into 'show it'");
        result.Data.Should().BeNull();
    }

    // Review round 5 (F1): a submission with no invoice is decided by its own source, as on the routes (the REAL seam).

    private Task<ToolResult> DescribeSubmissionAsync(Guid submission)
    {
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_invoicesubmission')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                "{ \"EntitySetName\": \"sprk_invoicesubmissions\", \"PrimaryIdAttribute\": \"sprk_invoicesubmissionid\" }")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_invoicesubmissions(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                "{ \"sprk_invoicesubmissionid\": \"" + submission.ToString("D") + "\", \"sprk_name\": \"SUB-1\" }")));
        return CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson:
                "{\"path\":\"tables/sprk_invoicesubmission/records/" + submission.ToString("D") + "\"}"),
            BuildDescribeTool(), CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteChatAsync_Task098_R5_ExternalCaller_ANoInvoiceInternalSubmissionWhoseSourceIsWithheld_AnswersLikeAMissingOne()
    {
        AsExternalCaller();
        var world = new Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld();
        var withheld = world.PendingSubmission(
            world.Document(Guid.NewGuid(), permission: Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.RestrictedPermission), Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.InternalChannel);
        var spa = world.PendingSubmission(null, Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.SpaChannel);
        _hideInvoiceFamily = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyHidingStubs.Real(world);

        var result = await DescribeSubmissionAsync(withheld);

        result.Success.Should().BeFalse();
        result.Data.Should().BeNull();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound);
        result.ErrorMessage.Should().Be(ExternalCallerCommunicationGuard.RecordNotVisibleMessage);
    }

    [Fact]
    public async Task ExecuteChatAsync_Task098_R5_ExternalCaller_ANoInvoiceSpaSubmission_ReturnsTheRecord()
    {
        AsExternalCaller();
        var world = new Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld();
        var spa = world.PendingSubmission(null, Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.SpaChannel);
        _hideInvoiceFamily = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyHidingStubs.Real(world);

        var result = await DescribeSubmissionAsync(spa);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("fields").GetProperty("sprk_name").GetString().Should().Be("SUB-1");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ebilling-r1 task 130-0b: a partner sees only invoices of its own firms (the seam as AddExternalAccess registers it)
    // ═════════════════════════════════════════════════════════════════════════════

    private Task<ToolResult> DescribeInvoiceAsync(Guid invoice)
    {
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_invoice')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("{ \"EntitySetName\": \"sprk_invoices\", \"PrimaryIdAttribute\": \"sprk_invoiceid\" }")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_invoices(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("{ \"sprk_invoiceid\": \"" + invoice.ToString("D") + "\", \"sprk_name\": \"INV-1\" }")));
        return CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: "{\"path\":\"tables/sprk_invoice/records/" + invoice.ToString("D") + "\"}"),
            BuildDescribeTool(), CancellationToken.None);
    }

    private static void ShouldAnswerLikeAMissingRecord(ToolResult result)
    {
        result.Success.Should().BeFalse();
        result.Data.Should().BeNull();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound);
        result.ErrorMessage.Should().Be(ExternalCallerCommunicationGuard.RecordNotVisibleMessage);
    }

    [Fact(DisplayName = "130-0b describe must fire: another firm's invoice answers like a missing one; one firm read")]
    public async Task ExecuteChatAsync_Task130_0b_AnotherFirmsInvoice_AnswersLikeAMissingOne()
    {
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var other = firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld.OtherFirm);
        _hideInvoiceFamily = firms.Seam;

        ShouldAnswerLikeAMissingRecord(await DescribeInvoiceAsync(other));
        firms.FirmReads.Should().Be(1);
    }

    [Fact(DisplayName = "130-0b describe must not fire: the caller's own firm's invoice is returned")]
    public async Task ExecuteChatAsync_Task130_0b_TheCallersOwnFirmsInvoice_IsReturned()
    {
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var own = firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.Firm);
        _hideInvoiceFamily = firms.Seam;

        var result = await DescribeInvoiceAsync(own);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("fields").GetProperty("sprk_name").GetString().Should().Be("INV-1");
        firms.FirmReads.Should().Be(1);
    }

    [Theory(DisplayName = "130-0b describe must fire: for a caller with no usable firm (no linked contact, a contact-binding fault, an inactive contact) even its own firm's invoice answers like a missing one")]
    [MemberData(nameof(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld.CallersWithNoFirm), MemberType = typeof(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld))]
    public async Task ExecuteChatAsync_Task130_0b_ACallerWithNoUsableFirm_GetsNoInvoice(string caller)
    {
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        firms.CallerIs(caller);
        var own = firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.Firm);
        _hideInvoiceFamily = firms.Seam;

        ShouldAnswerLikeAMissingRecord(await DescribeInvoiceAsync(own));
        firms.World.Urls.Should().BeEmpty("no row is read for a caller with no usable firm");
        firms.World.InvoiceReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "130-0b describe: an internal systemuser gets another firm's invoice unchanged, and no identity or firm is read")]
    public async Task ExecuteChatAsync_Task130_0b_AnInternalCaller_IsUnchanged_AndReadsNoFirm()
    {
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var other = firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld.OtherFirm);
        _hideInvoiceFamily = firms.Seam;

        var result = await DescribeInvoiceAsync(other);

        result.Success.Should().BeTrue(result.ErrorMessage);
        firms.FirmReads.Should().Be(0);
        firms.MembershipReads.Should().Be(0);
        firms.World.Urls.Should().BeEmpty("an internal caller costs no app-only read");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ebilling-r1 task 130-0b step 2: a CHILD record is decided by the invoices it links to (InvoiceLinks)
    // ═════════════════════════════════════════════════════════════════════════════

    private static readonly Guid LinkedOpenThread = Guid.Parse("70000000-0000-4000-8000-0000000130b2");

    /// <summary>
    /// Describes the record <paramref name="id"/> of <paramref name="linked"/>'s table as the user reads it, with every other guard on
    /// that table passing: a document is visible to the document predicate (app-only), an analysis has no source document, a
    /// communication is shareable on an Open thread.
    /// </summary>
    private Task<ToolResult> DescribeLinkedAsync(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase linked, Guid id)
    {
        var communication = linked.Table == "sprk_communication";
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"EntityDefinitions(LogicalName='{linked.Table}')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                "{ \"EntitySetName\": \"" + linked.EntitySet + "\", \"PrimaryIdAttribute\": \"" + linked.IdColumn + "\" }")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"{linked.EntitySet}({id:D})")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                "{ \"" + linked.IdColumn + "\": \"" + id.ToString("D") + "\", \"sprk_name\": \"REC-1\""
                + (communication
                    ? ", \"sprk_isinternalonly\": false, \"_sprk_communicationthread_value\": \"" + LinkedOpenThread.ToString("D") + "\""
                    : string.Empty)
                + " }")));
        if (linked.Table == "sprk_document")
            _appOnly.Document(id);
        if (linked.Table == "sprk_analysis")
        {
            _dataverse
                .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_analysises?")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("{ \"value\": [ { \"sprk_analysisid\": \"" + id.ToString("D") + "\" } ] }")));
        }

        if (communication)
        {
            _dataverse
                .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communicationthreads?")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("{ \"value\": [ { \"sprk_communicationthreadid\": \""
                    + LinkedOpenThread.ToString("D") + "\", \"sprk_privacystate\": 100000000 } ] }")));
        }

        return CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: "{\"path\":\"tables/" + linked.Table + "/records/" + id.ToString("D") + "\"}"),
            BuildDescribeTool(), CancellationToken.None);
    }

    private static void ShouldReturnTheRecord(ToolResult result)
    {
        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("fields").GetProperty("sprk_name").GetString().Should().Be("REC-1");
    }

    [Theory(DisplayName = "130-0b step 2 describe must fire: a child record linked to another firm's invoice answers like a missing one")]
    [MemberData(nameof(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Keys), MemberType = typeof(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase))]
    public async Task ExecuteChatAsync_Task130_0b_S2_AChildRecordOfAnotherFirmsInvoice_AnswersLikeAMissingOne(string key)
    {
        var linked = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Of(key);
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var row = firms.LinkedRow(linked, firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld.OtherFirm));
        _hideInvoiceFamily = firms.Seam;

        ShouldAnswerLikeAMissingRecord(await DescribeLinkedAsync(linked, row));
        firms.FirmReads.Should().Be(1);
    }

    [Theory(DisplayName = "130-0b step 2 describe must not fire: a child record linked to the caller's own firm's invoice is returned")]
    [MemberData(nameof(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Keys), MemberType = typeof(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase))]
    public async Task ExecuteChatAsync_Task130_0b_S2_AChildRecordOfTheCallersOwnInvoice_IsReturned(string key)
    {
        var linked = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Of(key);
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var row = firms.LinkedRow(linked, firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.Firm));
        _hideInvoiceFamily = firms.Seam;

        ShouldReturnTheRecord(await DescribeLinkedAsync(linked, row));
        firms.FirmReads.Should().Be(1);
    }

    [Theory(DisplayName = "130-0b step 2 describe must not fire: a child record with no invoice link is returned, and no firm is read")]
    [MemberData(nameof(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Keys), MemberType = typeof(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase))]
    public async Task ExecuteChatAsync_Task130_0b_S2_AChildRecordWithNoInvoiceLink_IsReturned(string key)
    {
        var linked = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Of(key);
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var row = firms.UnlinkedRow(linked);
        _hideInvoiceFamily = firms.Seam;

        ShouldReturnTheRecord(await DescribeLinkedAsync(linked, row));
        firms.FirmReads.Should().Be(0, "the record links to no invoice, so no firm is needed");
    }

    [Theory(DisplayName = "130-0b step 2 describe fault: a failed link read or a failed decision hides an invoice-linked record")]
    [MemberData(nameof(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.KeysAndFaults), MemberType = typeof(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase))]
    public async Task ExecuteChatAsync_Task130_0b_S2_AFault_HidesTheInvoiceLinkedRecord(string key, string fault)
    {
        var linked = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Of(key);
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var own = firms.LinkedRow(linked, firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.Firm));
        firms.Fail(linked, fault);
        _hideInvoiceFamily = firms.Seam;

        ShouldAnswerLikeAMissingRecord(await DescribeLinkedAsync(linked, own));
    }

    [Theory(DisplayName = "130-0b step 2 describe must fire: a link that cannot be decided (not a GUID, or a record the app-only read does not return) hides the record")]
    [InlineData("not-a-guid")]
    [InlineData("not-returned")]
    public async Task ExecuteChatAsync_Task130_0b_S2_AnUnreadableLink_HidesTheRecord(string kind)
    {
        var linked = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Of("todo");
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var row = kind == "not-a-guid" ? firms.LinkedRow(linked, "not-a-guid") : Guid.NewGuid();
        _hideInvoiceFamily = firms.Seam;

        ShouldAnswerLikeAMissingRecord(await DescribeLinkedAsync(linked, row));
    }

    [Fact(DisplayName = "130-0b step 2 describe must fire: a document linked to one visible and one hidden invoice answers like a missing one")]
    public async Task ExecuteChatAsync_Task130_0b_S2_ADocumentWithOneVisibleAndOneHiddenInvoice_IsHidden()
    {
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var own = firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.Firm);
        var other = firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld.OtherFirm);
        var mixed = firms.World.LinkedChildRow("sprk_document", ("sprk_invoice", own), ("sprk_relatedinvoice", other));
        _hideInvoiceFamily = firms.Seam;

        ShouldAnswerLikeAMissingRecord(await DescribeLinkedAsync(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Of("document-invoice"), mixed));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ebilling-r1 task 130-0b step 3: a ROOT record is returned; every form of its invoice link is blanked when that invoice is not visible
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Every form of the work assignment's invoice link a describe record can carry (describe drops the <c>@OData.Community</c>
    /// annotations itself; the other annotations pass through).
    /// </summary>
    private static readonly string[] RootLinkForms =
    {
        "_sprk_regardinginvoice_value",
        "_sprk_regardinginvoice_value@Microsoft.Dynamics.CRM.lookuplogicalname",
        "_sprk_regardinginvoice_value@Microsoft.Dynamics.CRM.associatednavigationproperty",
        "sprk_regardingrecordid",
        "sprk_regardingrecordname",
        "sprk_regardingrecordurl",
    };

    /// <summary>
    /// Describes the work assignment <paramref name="root"/> as the user reads it, linked to <paramref name="invoice"/> in every form
    /// (when not null), with the regarding id/name/URL naming that invoice.
    /// </summary>
    private Task<ToolResult> DescribeRootAsync(Guid root, Guid? invoice)
    {
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_workassignment')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("{ \"EntitySetName\": \"sprk_workassignments\", \"PrimaryIdAttribute\": \"sprk_workassignmentid\" }")));
        var link = invoice is { } id
            ? "\"_sprk_regardinginvoice_value\": \"" + id.ToString("D") + "\", "
              + "\"_sprk_regardinginvoice_value@OData.Community.Display.V1.FormattedValue\": \"INV-0042\", "
              + "\"_sprk_regardinginvoice_value@Microsoft.Dynamics.CRM.lookuplogicalname\": \"sprk_invoice\", "
              + "\"_sprk_regardinginvoice_value@Microsoft.Dynamics.CRM.associatednavigationproperty\": \"sprk_RegardingInvoice\", "
              + "\"sprk_regardingrecordid\": \"" + id.ToString("D") + "\", \"sprk_regardingrecordname\": \"INV-0042\", "
              + "\"sprk_regardingrecordurl\": \"https://org.test/main.aspx?id=" + id.ToString("D") + "\", "
            : "\"_sprk_regardinginvoice_value\": null, ";
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"sprk_workassignments({root:D})")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                "{ \"sprk_workassignmentid\": \"" + root.ToString("D") + "\", " + link + "\"sprk_name\": \"REC-1\" }")));

        return CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: "{\"path\":\"tables/sprk_workassignment/records/" + root.ToString("D") + "\"}"),
            BuildDescribeTool(), CancellationToken.None);
    }

    private static void ShouldReturnTheRootWithTheLinkBlank(ToolResult result)
    {
        ShouldReturnTheRecord(result);
        var fields = result.Data!.Value.GetProperty("fields");
        foreach (var form in RootLinkForms)
        {
            fields.TryGetProperty(form, out var value).Should().BeTrue($"{form} is still returned (blank)");
            value.ValueKind.Should().Be(JsonValueKind.Null, $"{form} carries the invoice link");
        }

        result.Data!.Value.GetRawText().Should().NotContain("INV-0042");
    }

    [Fact(DisplayName = "130-0b step 3 describe must fire: a root linked to another firm's invoice is returned with every link form blank")]
    public async Task ExecuteChatAsync_Task130_0b_S3_ARootOfAnotherFirmsInvoice_IsReturnedWithEveryLinkFormBlank()
    {
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var invoice = firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld.OtherFirm);
        var root = firms.LinkedRow(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Root, invoice);
        _hideInvoiceFamily = firms.Seam;

        var result = await DescribeRootAsync(root, invoice);

        ShouldReturnTheRootWithTheLinkBlank(result);
        result.Data!.Value.GetProperty("fields").GetRawText().Should().NotContain(invoice.ToString("D"));
        firms.FirmReads.Should().Be(1);
    }

    [Fact(DisplayName = "130-0b step 3 describe must not fire: a root linked to the caller's own firm's invoice shows the link")]
    public async Task ExecuteChatAsync_Task130_0b_S3_ARootOfTheCallersOwnInvoice_ShowsTheLink()
    {
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var invoice = firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.Firm);
        var root = firms.LinkedRow(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Root, invoice);
        _hideInvoiceFamily = firms.Seam;

        var result = await DescribeRootAsync(root, invoice);

        ShouldReturnTheRecord(result);
        var fields = result.Data!.Value.GetProperty("fields");
        fields.GetProperty("_sprk_regardinginvoice_value").GetString().Should().Be(invoice.ToString("D"));
        fields.GetProperty("_sprk_regardinginvoice_value@Microsoft.Dynamics.CRM.lookuplogicalname").GetString().Should().Be("sprk_invoice");
        fields.GetProperty("sprk_regardingrecordname").GetString().Should().Be("INV-0042");
        firms.FirmReads.Should().Be(1);
    }

    [Theory(DisplayName = "130-0b step 3 describe fault: a failed link read or a failed decision returns the root with every link form blank")]
    [InlineData("link-read")]
    [InlineData("decision")]
    public async Task ExecuteChatAsync_Task130_0b_S3_AFault_ReturnsTheRootWithTheLinkBlank(string fault)
    {
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var invoice = firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.Firm);
        var root = firms.LinkedRow(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Root, invoice);
        firms.Fail(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Root, fault);
        _hideInvoiceFamily = firms.Seam;

        ShouldReturnTheRootWithTheLinkBlank(await DescribeRootAsync(root, invoice));
    }

    [Theory(DisplayName = "130-0b step 3 describe must fire: a root link that cannot be decided (not a GUID, or not returned by the app-only read) is blank")]
    [InlineData("not-a-guid")]
    [InlineData("not-returned")]
    public async Task ExecuteChatAsync_Task130_0b_S3_AnUnreadableLink_IsBlank(string kind)
    {
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var invoice = firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.Firm);
        var root = kind == "not-a-guid"
            ? firms.LinkedRow(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Root, "not-a-guid")
            : Guid.NewGuid();
        _hideInvoiceFamily = firms.Seam;

        ShouldReturnTheRootWithTheLinkBlank(await DescribeRootAsync(root, invoice));
    }

    [Fact(DisplayName = "130-0b step 3 describe must not fire: a root with no invoice link is unchanged, and no firm is read")]
    public async Task ExecuteChatAsync_Task130_0b_S3_ARootWithNoInvoiceLink_IsUnchanged()
    {
        AsExternalCaller();
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var root = firms.UnlinkedRow(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Root);
        _hideInvoiceFamily = firms.Seam;

        ShouldReturnTheRecord(await DescribeRootAsync(root, invoice: null));
        firms.FirmReads.Should().Be(0, "the root links to no invoice, so no firm is needed");
    }

    [Fact(DisplayName = "130-0b step 3 describe: an internal systemuser's root is unchanged, and no link or firm is read")]
    public async Task ExecuteChatAsync_Task130_0b_S3_AnInternalCaller_IsUnchanged_AndReadsNothing()
    {
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var invoice = firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld.OtherFirm);
        var root = firms.LinkedRow(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Root, invoice);
        _hideInvoiceFamily = firms.Seam;

        var result = await DescribeRootAsync(root, invoice);

        ShouldReturnTheRecord(result);
        result.Data!.Value.GetProperty("fields").GetProperty("_sprk_regardinginvoice_value").GetString().Should().Be(invoice.ToString("D"));
        firms.FirmReads.Should().Be(0);
        firms.MembershipReads.Should().Be(0);
        firms.World.Urls.Should().BeEmpty("an internal caller costs no app-only link read");
    }

    [Theory(DisplayName = "130-0b step 2 describe: an internal systemuser's child record is unchanged, and no link or firm is read")]
    [MemberData(nameof(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Keys), MemberType = typeof(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase))]
    public async Task ExecuteChatAsync_Task130_0b_S2_AnInternalCaller_IsUnchanged_AndReadsNoLinkOrFirm(string key)
    {
        var linked = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceLinkedCase.Of(key);
        await using var firms = new Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld(CallerSystemUserId);
        var row = firms.LinkedRow(linked, firms.InvoiceOfFirm(Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyFirmWorld.OtherFirm));
        _hideInvoiceFamily = firms.Seam;

        ShouldReturnTheRecord(await DescribeLinkedAsync(linked, row));
        firms.FirmReads.Should().Be(0);
        firms.MembershipReads.Should().Be(0);
        firms.World.Urls.Should().BeEmpty("an internal caller costs no app-only link read");
    }

    [Fact]
    public async Task ExecuteChatAsync_Task098_InternalCaller_AnInvoiceNeverReachesThePredicate()
    {
        SetupInvoiceRecord();
        // _hideInvoiceFamily stays NeverCalled: any call would fail the tool.

        var result = await DescribeInvoiceAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
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

    // ── sprk_document records for an external caller (review round 1 F1; task 188: THE predicate, decided APP-ONLY) ──
    //
    // The user's read (the mocked client) returns the record; THE external document predicate re-reads it BY ID in the
    // app-only world (_appOnly) and decides there (main-session decision 2026-10-20), never on the record the user read.

    private static readonly Guid DocumentId = Guid.Parse("d0000000-0000-0000-0000-0000000000d1");
    private static readonly Guid RelatedCommunicationId = Guid.Parse("c1000000-0000-0000-0000-0000000000d1");
    private static readonly Guid ParentEmlId = Guid.Parse("e1880000-0000-0000-0000-0000000000d1");

    /// <summary>The user's read of the record. A <c>null</c> link and parent: what a field-security mask reads as.</summary>
    private List<string> SetupDocumentRecord(string relatedCommunicationJson = "null", string parentJson = "null")
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
                  "_sprk_relatedcommunication_value": {{relatedCommunicationJson}},
                  "_sprk_parentdocument_value": {{parentJson}}
                }
                """)));
        return requests;
    }

    /// <summary>
    /// The app-only related communication: <paramref name="isInternalOnly"/> "true" / "false" / "null", or "absent" (the BFF
    /// cannot read it back); on <see cref="ThreadId"/> of <paramref name="privacyState"/> when <paramref name="hasThread"/>.
    /// </summary>
    private void SeedAppOnlyCommunication(string isInternalOnly, bool hasThread = true, int privacyState = 100000000)
    {
        _appOnly.Thread(ThreadId, privacyState);
        if (isInternalOnly == "absent")
            return;
        _appOnly.Communication(RelatedCommunicationId, isInternalOnly switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        }, hasThread ? ThreadId : null);
    }

    private Task<ToolResult> DescribeDocumentAsync() =>
        CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: $$"""{"path":"tables/sprk_document/records/{{DocumentId:D}}"}"""),
            BuildDescribeTool(), CancellationToken.None);

    private static void ShouldAnswerLikeNotFound(ToolResult result)
    {
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound);
        result.ErrorMessage.Should().Be(ExternalCallerCommunicationGuard.RecordNotVisibleMessage);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocumentWithNoRelatedCommunication_ReturnsRecord_NoCommunicationRead()
    {
        AsExternalCaller();
        var requests = SetupDocumentRecord();
        _appOnly.Document(DocumentId);

        var result = await DescribeDocumentAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        requests.Should().HaveCount(2, "the user's metadata and record reads, nothing more");
        _appOnly.Reads("sprk_documents").Should().ContainSingle().Which.Should().Contain($"sprk_documentid eq {DocumentId:D}");
        _appOnly.Reads("sprk_communications").Should().BeEmpty();
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocumentWithShareableRelatedCommunication_ReturnsRecord()
    {
        AsExternalCaller();
        SetupDocumentRecord($"\"{RelatedCommunicationId:D}\"");
        _appOnly.Document(DocumentId, communication: RelatedCommunicationId);
        SeedAppOnlyCommunication("false");

        var result = await DescribeDocumentAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("fields").GetProperty("sprk_filesummary").GetString().Should().Be("Summary text");
        _appOnly.Reads("sprk_communications").Should().ContainSingle().Which.Should().EndWith(
            "sprk_communications?$select=sprk_communicationid,sprk_isinternalonly,_sprk_communicationthread_value" +
            $"&$filter=sprk_communicationid eq {RelatedCommunicationId:D}&$top=1");
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
    }

    [Theory]
    [InlineData("true", true, 100000000)]    // related communication internal-only
    [InlineData("null", true, 100000000)]    // flag unset
    [InlineData("false", true, 100000001)]   // Private thread
    [InlineData("false", false, 100000000)]  // no thread
    [InlineData("absent", false, 100000000)] // related communication unreadable or missing
    public async Task ExecuteChatAsync_ExternalCallerDocumentHiddenByRelatedCommunication_AnswersLikeNotFound(
        string isInternalOnly, bool hasThread, int privacyState)
    {
        AsExternalCaller();
        SetupDocumentRecord($"\"{RelatedCommunicationId:D}\"");
        _appOnly.Document(DocumentId, communication: RelatedCommunicationId);
        SeedAppOnlyCommunication(isInternalOnly, hasThread, privacyState);

        ShouldAnswerLikeNotFound(await DescribeDocumentAsync());
    }

    [Theory]
    [InlineData("sprk_documents")]
    [InlineData("sprk_communications")]
    public async Task ExecuteChatAsync_ExternalCallerDocumentRelatedReadFails_ErrorWithNoData(string faultedSet)
    {
        AsExternalCaller();
        SetupDocumentRecord($"\"{RelatedCommunicationId:D}\"");
        _appOnly.Document(DocumentId, communication: RelatedCommunicationId);
        SeedAppOnlyCommunication("false");
        _appOnly.Faults[faultedSet] = System.Net.HttpStatusCode.InternalServerError;

        var result = await DescribeDocumentAsync();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
    }

    // ── Task 188 (D7 T7, #2126): THE external document predicate in describe ──

    [Theory]
    [InlineData(100000002, false, null, false)]          // own Restricted
    [InlineData(null, true, 100000002, false)]           // parent .eml Restricted, no link anywhere
    [InlineData(null, true, null, true)]                 // parent .eml of an internal-only email
    public async Task ExecuteChatAsync_ExternalCallerDocument_Task188_HiddenByThePredicate_AnswersLikeNotFound(
        int? permission, bool hasParent, int? parentPermission, bool parentLinkedToInternalOnly)
    {
        AsExternalCaller();
        SetupDocumentRecord();
        _appOnly.Document(DocumentId, permission: permission, parent: hasParent ? ParentEmlId : null);
        _appOnly.Document(ParentEmlId, permission: parentPermission,
            communication: parentLinkedToInternalOnly ? RelatedCommunicationId : null);
        SeedAppOnlyCommunication("true");

        ShouldAnswerLikeNotFound(await DescribeDocumentAsync());
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocument_Task188_OwnVisibleLinkUnderARestrictedParent_AnswersLikeNotFound()
    {
        AsExternalCaller();
        SetupDocumentRecord($"\"{RelatedCommunicationId:D}\"");
        _appOnly.Document(DocumentId, communication: RelatedCommunicationId, parent: ParentEmlId);
        _appOnly.Document(ParentEmlId, permission: 100000002);
        SeedAppOnlyCommunication("false");

        ShouldAnswerLikeNotFound(await DescribeDocumentAsync());
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocument_Task188_LinkMaskedForTheUser_AppOnlyReadsAnInternalOnlyCommunication_AnswersLikeNotFound()
    {
        AsExternalCaller();
        SetupDocumentRecord(relatedCommunicationJson: "null");
        _appOnly.Document(DocumentId, communication: RelatedCommunicationId);
        SeedAppOnlyCommunication("true");

        ShouldAnswerLikeNotFound(await DescribeDocumentAsync());
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocument_Task188_ParentMaskedForTheUser_AppOnlyReadsARestrictedParent_AnswersLikeNotFound()
    {
        AsExternalCaller();
        SetupDocumentRecord(parentJson: "null");
        _appOnly.Document(DocumentId, parent: ParentEmlId);
        _appOnly.Document(ParentEmlId, permission: 100000002);

        ShouldAnswerLikeNotFound(await DescribeDocumentAsync());
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocument_Task188_StandardParentWithNoLink_ReturnsRecord()
    {
        AsExternalCaller();
        SetupDocumentRecord(parentJson: $"\"{ParentEmlId:D}\"");
        _appOnly.Document(DocumentId, parent: ParentEmlId);
        _appOnly.Document(ParentEmlId, permission: 100000000);

        var result = await DescribeDocumentAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("fields").GetProperty("sprk_documentname").GetString().Should().Be("RE: Strategy.eml");
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
        _appOnly.Calls.Should().Be(0, "an internal caller is not narrowed");
    }

    // ── Task 188 round 3 slice C (F-C1, OD-C1): analysis and analysis-output records for an external caller ──
    //
    // An analysis follows its source document AND its output file; an output follows its analysis. The links are read as
    // the user (the mocked client); only the hide decision on the documents runs in the app-only world (_appOnly).

    private static readonly Guid AnalysisId = Guid.Parse("a1880000-0000-0000-0000-0000000000d1");
    private static readonly Guid AnalysisOutputId = Guid.Parse("0a180000-0000-0000-0000-0000000000d1");
    private static readonly Guid OutputFileId = Guid.Parse("d0f00000-0000-0000-0000-0000000000d1");

    /// <summary>
    /// The user's client for the two derived tables: metadata, the record reads, and the by-id link reads. An analysis's
    /// links are <paramref name="sourceJson"/> and <paramref name="outputFileJson"/> (raw JSON); <paramref name="linkReturned"/>
    /// false: the link read does not return the analysis. Records every request.
    /// </summary>
    private List<string> SetupDerivedRecords(
        string sourceJson, string outputFileJson = "null", bool linkReturned = true, string regardingJson = "null")
    {
        var requests = new List<string>();
        void Answer(Func<string, bool> match, string json) =>
            _dataverse
                .Setup(d => d.GetAsync(It.Is<string>(p => match(p)), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((p, _) => requests.Add(p))
                .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(json)));

        Answer(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_analysis')"),
            """{ "EntitySetName": "sprk_analysises", "PrimaryIdAttribute": "sprk_analysisid" }""");
        Answer(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_analysisoutput')"),
            """{ "EntitySetName": "sprk_analysisoutputs", "PrimaryIdAttribute": "sprk_analysisoutputid" }""");
        Answer(p => p.StartsWith("sprk_analysises("),
            $$"""{ "sprk_analysisid": "{{AnalysisId:D}}", "sprk_name": "Analysis text" }""");
        Answer(p => p.StartsWith("sprk_analysisoutputs("),
            $$"""{ "sprk_analysisoutputid": "{{AnalysisOutputId:D}}", "sprk_name": "Output text" }""");
        Answer(p => p.StartsWith("sprk_analysises?"), linkReturned
            ? $$"""{ "value": [ { "sprk_analysisid": "{{AnalysisId:D}}", "_sprk_documentid_value": {{sourceJson}}, "_sprk_outputfileid_value": {{outputFileJson}}, "_sprk_regardingdocument_value": {{regardingJson}} } ] }"""
            : """{ "value": [] }""");
        Answer(p => p.StartsWith("sprk_analysisoutputs?"),
            $$"""{ "value": [ { "sprk_analysisoutputid": "{{AnalysisOutputId:D}}", "_sprk_analysisid_value": "{{AnalysisId:D}}" } ] }""");
        return requests;
    }

    /// <summary>
    /// A document link by kind: "restricted" / "visible" (seeded app-only, own Restricted or not), "unseeded" (the BFF
    /// cannot read it back), "notguid" (present but not a GUID), "null" (not set). Returns the raw JSON of the link.
    /// </summary>
    private string SeedLink(Guid id, string kind)
    {
        switch (kind)
        {
            case "restricted":
                _appOnly.Document(id, permission: 100000002);
                return $"\"{id:D}\"";
            case "visible":
                _appOnly.Document(id);
                return $"\"{id:D}\"";
            case "unseeded":
                return $"\"{id:D}\"";
            case "notguid":
                return "\"not-a-guid\"";
            default:
                return "null";
        }
    }

    private Task<ToolResult> DescribeDerivedAsync(string table, Guid id) =>
        CreateHandler().ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: $$"""{"path":"tables/{{table}}/records/{{id:D}}"}"""),
            BuildDescribeTool(), CancellationToken.None);

    [Theory]
    [InlineData("restricted", "null", true)]      // source document own Restricted
    [InlineData("unseeded", "null", true)]        // source document undecidable
    [InlineData("notguid", "null", true)]         // source link not a GUID
    [InlineData("visible", "restricted", true)]   // OD-C1: output file own Restricted, source visible
    [InlineData("visible", "unseeded", true)]     // OD-C1: output file undecidable
    [InlineData("null", "notguid", true)]         // OD-C1: output file link not a GUID
    [InlineData("visible", "null", false)]        // the link read does not return the analysis
    public async Task ExecuteChatAsync_ExternalCallerAnalysis_Task188SliceC_HiddenOrUndecidableDocument_AnswersLikeNotFound(
        string source, string outputFile, bool linkReturned)
    {
        AsExternalCaller();
        SetupDerivedRecords(SeedLink(DocumentId, source), SeedLink(OutputFileId, outputFile), linkReturned);

        ShouldAnswerLikeNotFound(await DescribeDerivedAsync("sprk_analysis", AnalysisId));
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
        CapturedLogMessages.Should().Contain(m =>
            m.FormattedMessage.Contains(ExternalCallerCommunicationGuard.LogEvent) &&
            m.FormattedMessage.Contains("entity=sprk_analysis") && m.FormattedMessage.Contains("reason=record_hidden"));
        CapturedLogMessages.Should().NotContain(m => m.FormattedMessage.Contains("Analysis text"));
    }

    [Theory]
    [InlineData("restricted", "null")]  // the analysis's source document
    [InlineData("visible", "restricted")] // OD-C1: the analysis's output file
    public async Task ExecuteChatAsync_ExternalCallerAnalysisOutput_Task188SliceC_AnalysisDocumentHidden_AnswersLikeNotFound(
        string source, string outputFile)
    {
        AsExternalCaller();
        var requests = SetupDerivedRecords(SeedLink(DocumentId, source), SeedLink(OutputFileId, outputFile));

        ShouldAnswerLikeNotFound(await DescribeDerivedAsync("sprk_analysisoutput", AnalysisOutputId));
        requests.Should().Contain(p => p.StartsWith("sprk_analysisoutputs?"))
            .And.Contain(p => p.StartsWith("sprk_analysises?"), "an output follows its analysis's documents");
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerMissingAnalysis_AnswersExactlyLikeHiddenAnalysis()
    {
        AsExternalCaller();
        SetupDerivedRecords("null");
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_analysises(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "Not found."));

        ShouldAnswerLikeNotFound(await DescribeDerivedAsync("sprk_analysis", AnalysisId));
        _appOnly.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("visible", "null")]
    [InlineData("visible", "visible")]
    [InlineData("null", "null")]
    public async Task ExecuteChatAsync_ExternalCallerAnalysis_Task188SliceC_VisibleOrNoDocument_ReturnsRecord(
        string source, string outputFile)
    {
        AsExternalCaller();
        var requests = SetupDerivedRecords(SeedLink(DocumentId, source), SeedLink(OutputFileId, outputFile));

        var result = await DescribeDerivedAsync("sprk_analysis", AnalysisId);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("fields").GetProperty("sprk_name").GetString().Should().Be("Analysis text");
        Uri.UnescapeDataString(requests.Last()).Should().Be(
            "sprk_analysises?$select=sprk_analysisid,_sprk_documentid_value,_sprk_outputfileid_value,_sprk_regardingdocument_value" +
            $"&$filter=sprk_analysisid eq {AnalysisId:D}&$top=1");
    }

    private static readonly Guid RegardingDocumentId = Guid.Parse("d0e00000-0000-0000-0000-0000000000d1");

    [Theory(DisplayName = "Task 188 r4b: describe hides an analysis whose typed REGARDING document is hidden or undecidable (source visible)")]
    [InlineData("restricted")]
    [InlineData("unseeded")]
    [InlineData("notguid")]
    public async Task ExecuteChatAsync_ExternalCallerAnalysis_Task188R4b_RegardingDocumentHiddenOrUndecidable_AnswersLikeNotFound(string regarding)
    {
        AsExternalCaller();
        SetupDerivedRecords(SeedLink(DocumentId, "visible"), regardingJson: SeedLink(RegardingDocumentId, regarding));

        ShouldAnswerLikeNotFound(await DescribeDerivedAsync("sprk_analysis", AnalysisId));
        CapturedLogMessages.Should().NotContain(m => m.FormattedMessage.Contains("Analysis text"));
    }

    [Fact(DisplayName = "Task 188 r4b: must not fire — describe returns an analysis whose regarding document is visible")]
    public async Task ExecuteChatAsync_ExternalCallerAnalysis_Task188R4b_RegardingDocumentVisible_ReturnsRecord()
    {
        AsExternalCaller();
        SetupDerivedRecords(SeedLink(DocumentId, "visible"), regardingJson: SeedLink(RegardingDocumentId, "visible"));

        var result = await DescribeDerivedAsync("sprk_analysis", AnalysisId);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _appOnly.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerAnalysisOutput_Task188SliceC_VisibleAnalysis_ReturnsRecord()
    {
        AsExternalCaller();
        SetupDerivedRecords(SeedLink(DocumentId, "visible"), SeedLink(OutputFileId, "visible"));

        var result = await DescribeDerivedAsync("sprk_analysisoutput", AnalysisOutputId);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("fields").GetProperty("sprk_name").GetString().Should().Be("Output text");
    }

    [Fact]
    public async Task ExecuteChatAsync_InternalCallerAnalysis_Task188SliceC_RestrictedSource_UnchangedAndNoExtraRead()
    {
        var requests = SetupDerivedRecords(SeedLink(DocumentId, "restricted"), SeedLink(OutputFileId, "restricted"));

        var result = await DescribeDerivedAsync("sprk_analysis", AnalysisId);

        result.Success.Should().BeTrue(result.ErrorMessage);
        requests.Should().Equal(
            "EntityDefinitions(LogicalName='sprk_analysis')?$select=EntitySetName,PrimaryIdAttribute",
            $"sprk_analysises({AnalysisId:D})");
        _appOnly.Calls.Should().Be(0, "an internal caller is not narrowed");
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerAnalysis_Task188SliceC_LinkReadFails_ErrorWithNoData()
    {
        AsExternalCaller();
        SetupDerivedRecords(SeedLink(DocumentId, "visible"));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_analysises?")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "down"));

        var result = await DescribeDerivedAsync("sprk_analysis", AnalysisId);

        result.Success.Should().BeFalse("a link read fault never turns into 'show it'");
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerAnalysis_Task188SliceC_AppOnlyReadFails_ErrorWithNoData()
    {
        AsExternalCaller();
        SetupDerivedRecords(SeedLink(DocumentId, "visible"));
        _appOnly.Faults["sprk_documents"] = System.Net.HttpStatusCode.InternalServerError;

        var result = await DescribeDerivedAsync("sprk_analysis", AnalysisId);

        result.Success.Should().BeFalse("a narrowing fault never shows an analysis");
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
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
