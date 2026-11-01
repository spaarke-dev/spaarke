using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 017 (phase 2d-3): the chat <c>dataverse.create_record</c> tool's owned create and
/// the budget inquiry create through the ONE server create (<see cref="RecordCreationService"/>, as the caller), so a
/// chat-created child carries its I-1 stamp and I-16 IN the create payload and gets its secure parent's sharees; each caller
/// keeps its existing refusal contract (each refusal KIND to the caller's existing code or outcome; the Office precedent);
/// a refusal, a fault or a service that throws creates nothing, with no fallback create (NFR-01).
/// </summary>
/// <remarks>
/// A partial of <see cref="SecureChildOwnershipAiToolTests"/> on purpose, as <c>RouteDecisionCreateParityTests</c> and
/// <c>RouteCreatePreCreateStampTests</c> are: the chat tool and the executor run over the SAME harness (the caller's scripted
/// Dataverse, the REAL resolver over its directory, the share world, the app-only create it records).
/// </remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    // =====================================================================================
    // Chat dataverse.create_record on the ONE server create
    // =====================================================================================

    /// <summary>The chat tool over a stamp world this test controls (the harness's own is empty).</summary>
    private DataverseCreateRecordHandler ChatHandlerOver(
        StampWorld stamps, IFieldMappingDataverseService? appOnly = null, IRecordOwnershipResolver? resolver = null)
    {
        var ownership = resolver ?? _world.Resolver();
        // The REAL resolver over the stamp world with the share world's column probe (it has sprk_accesspermission, as the
        // host's does), so step 10's I-16 is observable in the payload (RecordCreationServicePreWriteRuleSeamTests does the same).
        var restamper = new CoreAncestorRestamper(stamps.Service,
            new CoreAncestorResolver(stamps.Service, Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.ColumnProbe(), NullLogger<CoreAncestorResolver>.Instance),
            NullLogger<CoreAncestorRestamper>.Instance);
        return new(_user, CreateLogger<DataverseCreateRecordHandler>(), new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            ownership, IdentityNormalizationFixtures.WithContact(CallerContact).Object,
            RouteRecordCreation.Over(ownership, appOnly ?? _appOnly.Object, restamper, Shares(),
                SecureRootFilingGateFixtures.NothingSecure()));
    }

    private static Task<ToolResult> Chat(
        DataverseCreateRecordHandler handler, string table, params (string Column, JsonElement Value)[] item) =>
        handler.ExecuteChatAsync(
            CreateContext(table, item), BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

    /// <summary>The event the new event is filed under, itself under the ordinary matter; I-16 Restricted on the matter.</summary>
    private StampWorld TheEventUnderTheRestrictedOrdinaryMatter()
    {
        TheEventIsFiledUnderTheOrdinaryMatter();
        return new StampWorld().Row("sprk_matter", OrdinaryMatter)
            .Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)])
            .Set("sprk_matter", OrdinaryMatter, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Restricted));
    }

    [Fact]
    public async Task ChatCreate_AnEventUnderAnEvent_CarriesItsI1StampAndI16_InTheCreatePayload()
    {
        var stamps = TheEventUnderTheRestrictedOrdinaryMatter();

        var result = await Chat(ChatHandlerOver(stamps), "sprk_event", Lookup("sprk_regardingevent", "sprk_event", Event));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var (table, id, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_event");
        fields.Should().ContainKey("sprk_RegardingMatter@odata.bind")
            .WhoseValue.Should().Be($"/sprk_matters({OrdinaryMatter:D})", "step 10 derives the I-1 stamp into the one create");
        fields.Should().ContainKey(InheritedAccessPermission.Column)
            .WhoseValue.Should().Be(InheritedAccessPermission.Restricted, "I-16 is set in the payload before the create");
        CreatedRecordId(result).Should().Be(id);
        _user.Posts.Should().BeEmpty("never created as the caller");
    }

    /// <summary>
    /// What the owned create (the deleted <c>OwnedChildWrite.CreateAsync</c>) wrote for an event filed under
    /// <see cref="Event"/> (item <c>{"sprk_regardingevent": Event}</c>), every column, recorded from a run of it before task 017
    /// (2d-5) deleted it: the event's business-unit team as owner, the caller as creator person, the regarding event — and NO
    /// I-1 stamp (<c>sprk_RegardingMatter</c>) and no I-16: the owned create wrote neither at create.
    /// </summary>
    private static IReadOnlyDictionary<string, string> OwnedCreateEventUnderAnEvent => new Dictionary<string, string>
    {
        ["ownerid@odata.bind"] = $"/teams({Directory.ChildTeam:D})",
        ["sprk_CreatedByPerson@odata.bind"] = $"/systemusers({Caller:D})",
        ["sprk_RegardingEvent@odata.bind"] = $"/sprk_events({Event:D})",
    };

    [Fact]
    public async Task ChatCreate_AStampedEvent_IsOwnedAsTodaysCoreOwnsIt_TheStampChangesNoOwner()
    {
        // Correction item 4: the old chat create wrote no I-1 stamp into its item; step 10 adds it to the create BODY only, so
        // the owner question (step 7, over the item) is unchanged. Task 017 (2d-5): the old core is deleted; its owner and
        // creator are pinned (OwnedCreateEventUnderAnEvent), and it wrote NO stamp — the chat create through the service does.
        var stamps = TheEventUnderTheRestrictedOrdinaryMatter();

        var viaChat = await Chat(ChatHandlerOver(stamps), "sprk_event", Lookup("sprk_regardingevent", "sprk_event", Event));

        viaChat.Success.Should().BeTrue(viaChat.ErrorMessage);
        var (_, _, chats) = _appCreates.Should().ContainSingle().Subject;
        OwnedCreateEventUnderAnEvent.Should().NotContainKey("sprk_RegardingMatter@odata.bind",
            "the old core wrote no stamp at create");
        Owner(chats).Should().Be(Directory.ChildTeam, "record-first: the filed-under event's business-unit team");
        chats["ownerid@odata.bind"].Should().Be(OwnedCreateEventUnderAnEvent["ownerid@odata.bind"],
            "the same owner as the old core gave the same row");
        chats["sprk_CreatedByPerson@odata.bind"].Should().Be(OwnedCreateEventUnderAnEvent["sprk_CreatedByPerson@odata.bind"]);
        Bind(chats, "sprk_RegardingEvent@odata.bind").Should().Be(OwnedCreateEventUnderAnEvent["sprk_RegardingEvent@odata.bind"],
            "the caller's own bind (a mapped body value)");
        chats.Should().ContainKey("sprk_RegardingMatter@odata.bind")
            .WhoseValue.Should().Be($"/sprk_matters({OrdinaryMatter:D})", "the service writes the stamp the old core did not");
    }

    [Fact]
    public async Task ChatCreate_AToDoUnderASecureMatter_GetsItsSecureParentsShareesMirror_AtCreate()
    {
        var result = await CreateRecord("sprk_todo", Lookup("sprk_regardingmatter", "sprk_matter", SecureMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var (_, id, fields) = _appCreates.Should().ContainSingle().Subject;
        Owner(fields).Should().Be(Directory.SecureNamedTeam);
        _shareTable.MaskOf("sprk_todo", id, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask,
            "the secure matter's sharee sees the chat-created to-do at once (the service's sharee mirror)");
    }

    [Fact]
    public async Task ChatCreate_AnOwnedPathTableTheServiceDoesNotCreate_IsRefused_ValidationFailed_WithNoCreateCall()
    {
        // Owner decision 2026-10-30 (option b): only task, contact and account were registered for chat; every other
        // unregistered table is refused (record_creation.table_unsupported) — never created by a second core.
        var result = await CreateRecord("phonecall", ("subject", JsonSerializer.SerializeToElement("Call back")));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("not created here");
        _appCreates.Should().BeEmpty("no create call");
        _user.Posts.Should().BeEmpty("and no run-as-user fallback");
    }

    [Fact]
    public async Task ChatCreate_ACallerWithoutAppendToOnTheMatter_GetsTheSameAccessDeniedAsBefore_AndNothingIsCreated()
    {
        _user.NoAppendTo.Add(OrdinaryMatter);

        var result = await CreateRecord("sprk_todo", Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied, "the code the old core's AppendTo denial gave");
        result.ErrorMessage.Should().Be("A record this to-do is filed under was not found. The to-do was not saved.",
            "the shared map's uniform not-found: missing and not-appendable answer alike");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task ChatCreate_WhenTheServiceRefusesAFault_IsInternalError_AndNothingIsCreated()
    {
        TheEventIsFiledUnderTheOrdinaryMatter();
        var stamps = new StampWorld().Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)])
            .FailRead("sprk_event", Event);

        var result = await Chat(ChatHandlerOver(stamps), "sprk_event", Lookup("sprk_regardingevent", "sprk_event", Event));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError, "a fault: the code the old core's throw gave");
        result.ErrorMessage.Should().Be(
            "The event could not be saved right now, so nothing was created. Try again in a few minutes.");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty("no fallback create");
    }

    [Fact]
    public async Task ChatCreate_WhenTheServiceThrows_IsInternalError_AndNothingFallsBackToAnotherCreate()
    {
        var attempts = 0;
        var throwing = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        throwing
            .Setup(a => a.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback(() => attempts++)
            .ThrowsAsync(new HttpRequestException("the app-only create faulted"));

        var result = await Chat(ChatHandlerOver(new StampWorld(), throwing.Object), "sprk_todo",
            Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.ErrorMessage.Should().NotContain("faulted", "never the exception's text");
        attempts.Should().Be(1, "the one create, never retried (NFR-03)");
        _user.Posts.Should().BeEmpty("no run-as-user fallback");
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public void ChatCreate_AnIsolatedRootWhoseSecuringDidNotFinish_KeepsTodaysErrorAndItsReasonCode()
    {
        // Escalation A, decided: the existing tool error and its secured.ReasonCode stay (the service reports provisioning's
        // answer on RecordCreationOutcome.IsolatedCompletion; it is never a success with a warning here).
        var handler = CreateRecordHandler();
        var tool = BuildAnalysisTool(nameof(DataverseCreateRecordHandler));
        var item = new DataverseWriteItemMapper.MappedItem("{}", []);
        var id = Guid.NewGuid();
        SecureRootInheritResult Completion(SecureRootInheritOutcome outcome, string code) =>
            new("sprk_workassignment", id, outcome, code, null, [], null);

        var stranded = handler.OwnedCreateResult(tool, "sprk_workassignment", item, new RecordCreationOutcome
        {
            CreatedId = id,
            Isolated = true,
            IsolatedCompletion = Completion(SecureRootInheritOutcome.Refused, "sdap.provision.creator_no_access") with { RowStranded = true },
            Warnings = ["the service's warning"],
        }, DateTimeOffset.UtcNow);
        var unfinished = handler.OwnedCreateResult(tool, "sprk_workassignment", item, new RecordCreationOutcome
        {
            CreatedId = id,
            Isolated = true,
            IsolatedCompletion = Completion(SecureRootInheritOutcome.Failed, "sdap.secure_inherit.container_failed"),
        }, DateTimeOffset.UtcNow);
        var removed = handler.OwnedCreateResult(tool, "sprk_workassignment", item, new RecordCreationOutcome
        {
            Refusal = new RecordCreationRefusal(RecordCreationRefusalKind.Undecided, "sdap.provision.creator_share_failed", "removed"),
            IsolatedCompletion = Completion(SecureRootInheritOutcome.Failed, "sdap.provision.creator_share_failed") with { RowRemoved = true },
        }, DateTimeOffset.UtcNow);
        var complete = handler.OwnedCreateResult(tool, "sprk_workassignment", item, new RecordCreationOutcome
        {
            CreatedId = id,
            Isolated = true,
            IsolatedCompletion = Completion(SecureRootInheritOutcome.Secured, "unused"),
        }, DateTimeOffset.UtcNow);

        stranded.Success.Should().BeFalse();
        stranded.ErrorCode.Should().Be("sdap.provision.creator_no_access");
        stranded.ErrorMessage.Should().Contain("could not be removed again").And.Contain("will not be shared to you automatically");
        unfinished.Success.Should().BeFalse();
        unfinished.ErrorCode.Should().Be("sdap.secure_inherit.container_failed");
        unfinished.ErrorMessage.Should().Contain("securing it could not be finished yet");
        removed.Success.Should().BeFalse();
        removed.ErrorCode.Should().Be("sdap.provision.creator_share_failed");
        removed.ErrorMessage.Should().StartWith("The record was NOT created:").And.Contain("removed again");
        complete.Success.Should().BeTrue("a finished completion is a plain success (must-not-fire)");
    }

    [Fact]
    public void ChatCreate_ColumnsSet_IsLogicalNames_WithACopiedDefaultTranslatedFromItsBindKey()
    {
        var item = new DataverseWriteItemMapper.MappedItem("{}", ["sprk_name", "sprk_assignedtointernal"])
        {
            Lookups = [new("sprk_assignedtointernal", "contact", "contacts", Guid.NewGuid())],
        };

        var columns = DataverseCreateRecordHandler.WrittenColumnsOf(item,
        [
            "sprk_name", "sprk_AssignedToInternal@odata.bind", "sprk_AssignedTo@odata.bind", "ownerid@odata.bind",
            "sprk_CreatedByPerson@odata.bind", "sprk_RegardingMatter@odata.bind",
        ]);

        columns.Should().Equal("sprk_name", "sprk_assignedtointernal", "sprk_assignedto");
    }

    private static readonly List<(RecordCreationRefusal Refusal, string Expected)> ChatRefusals = new()
    {
        (Refusal(RecordCreationRefusalKind.PrincipalUnresolved, "record_creation.principal") with
            { ClientFailure = DataverseUserResponse.Fail(401, DataverseUserClientErrorCodes.UserContextRequired, "x") },
            DataverseUserClientErrorCodes.UserContextRequired),
        (Refusal(RecordCreationRefusalKind.PrincipalUnresolved, "record_creation.principal"), DataverseUserClientErrorCodes.AccessDenied),
        (Refusal(RecordCreationRefusalKind.Upstream, RecordCreationRefusal.UpstreamCode) with
            { ClientFailure = DataverseUserResponse.Fail(429, DataverseUserClientErrorCodes.RateLimited, "x") },
            DataverseUserClientErrorCodes.RateLimited),
        (Refusal(RecordCreationRefusalKind.NotFoundOrDenied, RecordCreationRefusal.NotFoundCode), DataverseUserClientErrorCodes.AccessDenied),
        (Refusal(RecordCreationRefusalKind.BlockedColumn, RecordCreationRefusal.BlockedColumnCode), DataverseUserClientErrorCodes.AccessDenied),
        (Refusal(RecordCreationRefusalKind.Forbidden, RecordCreationRefusal.DeniedCode), DataverseUserClientErrorCodes.AccessDenied),
        (Refusal(RecordCreationRefusalKind.Forbidden, "sdap.provision.creator_no_access"), "sdap.provision.creator_no_access"),
        (Refusal(RecordCreationRefusalKind.Invalid, RecordCreationRefusal.InvalidPayloadCode), ToolErrorCodes.ValidationFailed),
        (Refusal(RecordCreationRefusalKind.Invalid, RecordCreationRefusal.TableUnsupportedCode), ToolErrorCodes.ValidationFailed),
        (Refusal(RecordCreationRefusalKind.Invalid, RecordCreationRefusal.StampUnderivableCode), ToolErrorCodes.ValidationFailed),
        (Refusal(RecordCreationRefusalKind.Invalid, "invoice_family_two_core_records") with
            { PreWrite = PreWriteRefusal.Refused("invoice_family_two_core_records", "it names two core records") },
            "invoice_family_two_core_records"),
        (Refusal(RecordCreationRefusalKind.OwnerUnresolved, RecordOwnerRefusal.NoOwnerSource) with
            { OwnerDecision = RecordOwnerResolution.Owned(Directory.SecureNamedTeam) with { IsSecureOwner = true } },
            ToolErrorCodes.ValidationFailed),
        (Refusal(RecordCreationRefusalKind.OwnerUnresolved, RecordOwnerRefusal.NoDefaultOwnerTeam) with
            { OwnerDecision = RecordOwnerResolution.Refused(RecordOwnerRefusal.NoDefaultOwnerTeam, "no team") },
            RecordOwnerRefusal.NoDefaultOwnerTeam),
        (Refusal(RecordCreationRefusalKind.Conflict, RecordCreationRefusal.NumberUnavailableCode), RecordCreationRefusal.NumberUnavailableCode),
        (Refusal(RecordCreationRefusalKind.Undecided, RecordCreationRefusal.NoDecisionCode), ToolErrorCodes.InternalError),
        (Refusal(RecordCreationRefusalKind.Undecided, "sdap.provision.creator_share_failed"), "sdap.provision.creator_share_failed"),
        (Refusal(RecordCreationRefusalKind.Unavailable, RecordCreationRefusal.StampUnavailableCode), ToolErrorCodes.InternalError),
        (Refusal(RecordCreationRefusalKind.Unavailable, RecordCreationRefusal.OwnerUnavailableCode), ToolErrorCodes.InternalError),
    };

    private static RecordCreationRefusal Refusal(RecordCreationRefusalKind kind, string code) => new(kind, code, "the service's words");

    [Fact]
    public void ChatCreate_EachRefusalKind_MapsToTheToolsExistingErrorCode()
    {
        foreach (var (refusal, expected) in ChatRefusals)
        {
            DataverseCreateRecordHandler.RefusalErrorCode(refusal).Should().Be(expected, $"{refusal.Kind}/{refusal.Code}");
            DataverseCreateRecordHandler.RefusalText(refusal, "sprk_todo", "corr").Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void ChatCreate_EveryRefusalKind_HasACode()
    {
        foreach (var kind in Enum.GetValues<RecordCreationRefusalKind>())
            DataverseCreateRecordHandler.RefusalErrorCode(Refusal(kind, "some.code")).Should().NotBeNullOrWhiteSpace(kind.ToString());
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(400)]
    [InlineData(500)]
    public void ChatCreate_AnUpstreamFailureThatNamesNoCode_IsPassedThroughAsBefore_NotMadeIntoInternalError(int status)
    {
        // Task 017 review F-6 (NFR-07): MapClientError gave the result the failure's own ErrorCode, null included.
        var failure = new DataverseUserResponse { IsSuccess = false, StatusCode = status, ErrorCode = null, ErrorMessage = "Test: words" };
        var refusal = new RecordCreationRefusal(RecordCreationRefusalKind.Upstream, RecordCreationRefusal.UpstreamCode, "x")
        {
            ClientFailure = failure,
        };

        DataverseCreateRecordHandler.RefusalErrorCode(refusal).Should().BeNull($"as MapClientError gave it, for a {status}");
        DataverseCreateRecordHandler.RefusalText(refusal, "sprk_todo", "corr").Should().NotContain("Test: words");
    }

    [Fact]
    public void ChatCreate_AnUpstreamFailureThatNamesACode_KeepsItsOwnCode_MustNotFire()
    {
        var failure = DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "Test: words");
        var refusal = new RecordCreationRefusal(RecordCreationRefusalKind.Upstream, RecordCreationRefusal.UpstreamCode, "x")
        {
            ClientFailure = failure,
        };

        DataverseCreateRecordHandler.RefusalErrorCode(refusal).Should().Be(DataverseUserClientErrorCodes.AccessDenied);
    }

    [Fact]
    public void ChatCreate_TheToolDescription_SaysAllThreeNumbersAreServerSet_AndOffersNoneAsAWritableColumn_OwnerDecision2c()
    {
        // Task 017 review F-8: the autonumber columns the service refuses as BlockedColumn are the matter's, the project's and
        // the budget's — the description names all three as the server's and offers none as a column to send.
        var description = CreateRecordHandler().Metadata.Description;

        description.Should().Contain("set by the server, so NEVER send any of them");
        foreach (var number in RecordTableRules.All.Select(r => r.AutonumberColumn).OfType<string>())
        {
            description.Should().Contain(number, "the platform's number is named as the server's");
            description.Should().NotContain($"{number} (", "never offered as a writable column with a type, as sprk_mattername is");
        }

        RecordTableRules.All.Select(r => r.AutonumberColumn).OfType<string>()
            .Should().BeEquivalentTo("sprk_matternumber", "sprk_projectnumber", "sprk_budgetnumber");
        description.Should().Contain("sprk_mattername (text, REQUIRED); sprk_matterdescription");
    }

    // =====================================================================================
    // Budget inquiry (BudgetInquiryExecutor.ExecuteAsync) on the ONE server create
    // =====================================================================================

    private static BudgetInquiryRequest InquiryRequest(Guid matter) =>
        new(matter, ["firm@example.com"], "Budget question", "Please explain.", Confirmed: true, PtyFirm);

    /// <summary>The executor over this harness; the service is in the request's services unless <paramref name="withService"/> is false.</summary>
    private (BudgetInquiryExecutor Executor, RecordingInquirySender Email) InquiryExecutor(
        IRecordOwnershipResolver? resolver = null, bool withService = true, IServiceScopeFactory? scopes = null)
    {
        InquiryCallerMayCreate();
        // The matter's catalog row, read as the caller (the executor's own filter query).
        _user.GetOverride = path => path.StartsWith("sprk_recordtype_refs?", StringComparison.Ordinal)
            ? DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(
                new { value = new[] { new { sprk_recordtype_refid = DcRecordTypeMatter } } }))
            : null;

        var ownership = resolver ?? _world.Resolver();
        var services = new ServiceCollection();
        if (withService)
        {
            services.AddSingleton(RouteRecordCreation.Over(
                ownership, _appOnly.Object, Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure(), scopes));
        }

        var http = HttpContextOfCaller();
        http.RequestServices = services.BuildServiceProvider();
        var email = new RecordingInquirySender();
        return (new BudgetInquiryExecutor(
            _user, ownership, email, new HttpContextAccessor { HttpContext = http }, NullLogger<BudgetInquiryExecutor>.Instance), email);
    }

    /// <summary>The Graph boundary, recorded: the inquiry email is never sent here.</summary>
    private sealed class RecordingInquirySender : InquiryEmailSender
    {
        public RecordingInquirySender()
            : base(null!, new HttpContextAccessor())
        {
        }

        public List<Guid> Sent { get; } = new();

        public override Task<Guid?> SendAsync(BudgetInquiryRequest request, Guid serviceRequestId, CancellationToken ct)
        {
            Sent.Add(serviceRequestId);
            return Task.FromResult<Guid?>(Guid.NewGuid());
        }
    }

    [Fact]
    public async Task BudgetInquiry_ThroughTheService_CreatesTheServiceRequest_WithItsTeamOwnerAndI1Stamp_ThenSendsTheEmail()
    {
        var (executor, email) = InquiryExecutor();

        var result = await executor.ExecuteAsync(InquiryRequest(OrdinaryMatter), CancellationToken.None);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Sent, result.Error);
        var (table, id, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be(BudgetInquiryExecutor.Table);
        Owner(fields).Should().Be(Directory.ChildTeam, "record-first: the regarding matter's business-unit team");
        Bind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})",
            "its I-1 stamp, in the create payload");
        result.ServiceRequestId.Should().Be(id);
        email.Sent.Should().Equal(id);
        _user.Posts.Should().BeEmpty("the row is never created as the caller");
    }

    /// <summary>
    /// Task 017 r3 (UAC-r2 F1): the grantor the budget inquiry's create carries, pinned to master's. On master its owned create
    /// (<c>OwnedChildWrite.CreateAsync</c>, a service request is a CHILD table) never ran the "Assigned *" step at all, so no
    /// grantor was ever passed and nobody could be invited; the service runs that step for a ROOT only, so the request needs no
    /// override and keeps the default. The materializer is never asked for, and no invitation goes out.
    /// </summary>
    [Fact]
    public async Task BudgetInquiry_ThroughTheService_NeverRunsTheAssignedAccessStep_SoNoGrantorIsPassedAndNobodyIsInvited()
    {
        var (scopes, harness) = Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.AssignedAccessGrantorTests.CountingScopes();
        var (executor, _) = InquiryExecutor(scopes: scopes);

        var result = await executor.ExecuteAsync(InquiryRequest(OrdinaryMatter), CancellationToken.None);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Sent, result.Error);
        _appCreates.Should().ContainSingle(c => c.Table == BudgetInquiryExecutor.Table);
        scopes.Opened.Should().Be(0, "a child table's create never reaches the Assigned-access materializer (master: not called either)");
        harness.Emails.InviteUrls.Should().BeEmpty();
        harness.Ciam.Attempts.Should().BeEmpty();
    }

    [Fact]
    public async Task BudgetInquiry_WhenTheServiceCannotBeReached_CreatesNothing_SendsNothing_AndIsCreateFailed()
    {
        // NFR-01: no fallback create — a service that cannot be resolved (it throws) leaves nothing behind.
        var (executor, email) = InquiryExecutor(withService: false);

        var result = await executor.ExecuteAsync(InquiryRequest(OrdinaryMatter), CancellationToken.None);

        result.Outcome.Should().Be(BudgetInquiryOutcome.CreateFailed);
        result.ServiceRequestId.Should().BeNull();
        _appCreates.Should().BeEmpty("there is no fallback create");
        _user.Posts.Should().BeEmpty();
        email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task BudgetInquiry_ACallerWithoutAppendToOnTheMatter_IsRefused_AsBefore_InTheSharedMapsWords_AndNothingIsCreated()
    {
        _user.NoAppendTo.Add(OrdinaryMatter);
        var (executor, email) = InquiryExecutor();

        var result = await executor.ExecuteAsync(InquiryRequest(OrdinaryMatter), CancellationToken.None);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Refused, "the outcome the old core's AppendTo denial gave");
        result.Error.Should().Be(
            "A record this service request is filed under was not found. The service request was not saved.",
            "the shared map's uniform not-found (NFR-01): missing and not-appendable answer alike; sprk_servicerequest has its own noun in NounOf (2d-5)");
        _appCreates.Should().BeEmpty();
        email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task BudgetInquiry_WhenTheOwnerDecisionFaults_IsCreateFailed_AsBefore_InTheSharedMapsWords_AndNothingIsCreated()
    {
        var (executor, email) = InquiryExecutor(_world.Resolver(fault: new TimeoutException("throttled")));

        var result = await executor.ExecuteAsync(InquiryRequest(OrdinaryMatter), CancellationToken.None);

        result.Outcome.Should().Be(BudgetInquiryOutcome.CreateFailed, "a fault: the outcome the old core's throw gave");
        result.Error.Should().Be(
            "The service request could not be saved right now, so nothing was created. Try again in a few minutes.",
            "the shared map's 503 words, with the service request's own noun (2d-5)");
        _appCreates.Should().BeEmpty();
        email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task BudgetInquiry_UnderASecureMatter_IsRefused_AndNothingIsCreatedOrSent()
    {
        var (executor, email) = InquiryExecutor();

        var result = await executor.ExecuteAsync(InquiryRequest(SecureMatter), CancellationToken.None);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Refused);
        _appCreates.Should().BeEmpty("a service request is no reparentable child");
        email.Sent.Should().BeEmpty();
    }

    [Fact]
    public void BudgetInquiry_EveryRefusalKind_MapsToItsExistingOutcome_WithTheSharedMapsText()
    {
        foreach (var kind in Enum.GetValues<RecordCreationRefusalKind>())
            EachRefusalKindMapsToItsExistingOutcome(kind);
    }

    private static void EachRefusalKindMapsToItsExistingOutcome(RecordCreationRefusalKind kind)
    {
        var refusal = new RecordCreationRefusal(kind, "record_creation.some_code", "the service's words")
        {
            ClientFailure = kind == RecordCreationRefusalKind.Upstream
                ? DataverseUserResponse.Fail(429, DataverseUserClientErrorCodes.RateLimited, "Dataverse's own words")
                : null,
        };

        var result = BudgetInquiryExecutor.RefusalResult(refusal, "corr-1");

        result.Outcome.Should().Be(kind == RecordCreationRefusalKind.Unavailable
            ? BudgetInquiryOutcome.CreateFailed
            : BudgetInquiryOutcome.Refused, kind.ToString());
        result.Error.Should().Be(RecordCreationProblemMap.ToProblemDetails(refusal, BudgetInquiryExecutor.Table, "corr-1").Detail);
        result.Error.Should().NotContain("Dataverse's own words");
        result.ServiceRequestId.Should().BeNull("a refusal names no row");
    }

    [Theory]
    [InlineData(RecordCreationRefusal.StampUnderivableCode, nameof(RecordCreationRefusalKind.Invalid), nameof(BudgetInquiryOutcome.Refused))]
    [InlineData(RecordCreationRefusal.StampUnavailableCode, nameof(RecordCreationRefusalKind.Unavailable), nameof(BudgetInquiryOutcome.CreateFailed))]
    public void BudgetInquiry_TheStepTenStampRefusals_MapByTheirKind(string code, string kind, string expected)
    {
        BudgetInquiryExecutor.RefusalResult(
                new RecordCreationRefusal(Enum.Parse<RecordCreationRefusalKind>(kind), code, "the I-1 stamp could not be derived"), "corr-2")
            .Outcome.Should().Be(Enum.Parse<BudgetInquiryOutcome>(expected));
    }
}
