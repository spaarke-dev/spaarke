using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// Field-mapping-server-write-path-r1 task 017 (phase 2d-4): a FILED <c>email.draft</c> is created through the ONE server
/// create (<see cref="RecordCreationService"/>, as the caller) with today's owner, sender and refusal codes. Option (a′),
/// coordinator 2026-10-30: the handler keeps its derived I-1 stamp in the create item, because the service decides the owner
/// and asks AppendTo over the item — so the stamped root (the matter above a regarding invoice) takes part in both, exactly
/// as it did in the owned create the handler used before (<c>OwnedChildWrite.CreateAsync</c>, deleted in 017 2d-5), and step 10 finds no stamp
/// missing. Each case below is the answer the owned create gave (probed against it before the re-point;
/// notes/task-017-notes.md, phase 2d-4). A refusal or a fault creates nothing, and nothing falls back to a create as the user.
/// </summary>
/// <remarks>A partial of <see cref="SecureChildOwnershipAiToolTests"/>: it drives the real handler over that harness's
/// caller, resolver world and app-only create seam, as the route's parity suites do.</remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid DraftInvoice = Guid.Parse("d4d40000-0000-4000-8000-000000000001");
    private static readonly Guid DraftMatter = Guid.Parse("d4d40000-0000-4000-8000-000000000002");

    /// <summary>The ONE server create over this harness's resolver, app-only seam and share world.</summary>
    private RecordCreationService DraftCreation(CoreAncestorRestamper? restamper = null) =>
        RouteRecordCreation.Over(
            _world.Resolver(), _appOnly.Object, restamper ?? Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure());

    /// <summary>
    /// A draft regarding <see cref="DraftInvoice"/>, which is filed under <see cref="DraftMatter"/>: the invoice team-owned by
    /// the child business unit's team (<paramref name="matter"/> decides the matter's own state). The handler derives the
    /// stamp over the same rows the service's step 10 reads.
    /// </summary>
    private async Task<ToolResult> DraftRegardingAnInvoiceUnder(Action<Directory> matter)
    {
        matter(_world);
        _world.WithRecord("sprk_invoice", DraftInvoice, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", DraftMatter) });
        var stamps = new StampWorld()
            .Row("sprk_matter", DraftMatter)
            .Row("sprk_invoice", DraftInvoice, [("sprk_matter", "sprk_matter", DraftMatter)]);
        return await Draft(stamps.Resolver, DraftCreation(stamps.Restamper), "sprk_invoice", DraftInvoice);
    }

    private async Task<ToolResult> Draft(
        CoreAncestorResolver ancestors, RecordCreationService creation, string? regardingTable = null, Guid? regardingId = null)
    {
        var (visibility, visibilityAccessor) = new CommunicationVisibilityFixtures().Build();
        var handler = new EmailDraftToolHandler(
            _user, Mock.Of<IEmailDraftAi>(), ancestors, CreateLogger<EmailDraftToolHandler>(), creation, visibility,
            visibilityAccessor);
        var args = regardingTable is null
            ? JsonSerializer.Serialize(new { subject = "Hello", body = "Draft.", to = new[] { "a@b.example" } })
            : JsonSerializer.Serialize(new
            {
                subject = "Invoice query",
                body = "Draft.",
                to = new[] { "a@b.example" },
                regarding = new { table = regardingTable, recordId = regardingId },
            });

        return await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: args) with { UserId = CallerOid.ToString() },
            BuildAnalysisTool(nameof(EmailDraftToolHandler)), CancellationToken.None);
    }

    /// <summary>The one bind of <paramref name="navigation"/> in the create body, whichever spelling wrote it.</summary>
    private static string? DraftBind(IReadOnlyDictionary<string, object?> fields, string navigation)
    {
        var binds = fields
            .Where(f => string.Equals(f.Key, $"{navigation}@odata.bind", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Value switch
            {
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                string text => text,
                _ => null,
            })
            .ToList();
        binds.Should().HaveCountLessThanOrEqualTo(1, $"the body binds {navigation} once");
        return binds.SingleOrDefault();
    }

    private IReadOnlyDictionary<string, object?> TheDraftCreate() =>
        _appCreates.Should().ContainSingle(c => c.Table == "sprk_communication").Subject.Fields;

    // ── owner and sender: today's answer, through the service ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EmailDraft_RegardingAnOrdinaryMatter_IsOwnedByThatMattersTeam_WithTheDrafterAsSender()
    {
        var result = await Draft(CoreAncestorResolverFixtures.Inert(), DraftCreation(), "sprk_matter", OrdinaryMatter);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Posts.Should().BeEmpty("a filed draft is created by the application, never as the user");
        var fields = TheDraftCreate();
        Owner(fields).Should().Be(Directory.ChildTeam);
        DraftBind(fields, "sprk_RegardingMatter").Should().Be($"/sprk_matters({OrdinaryMatter:D})");
        DraftBind(fields, "sprk_SentBy").Should().Be($"/systemusers({Caller:D})", "the drafter is the sender (S1)");
        ((JsonElement)fields["statuscode"]!).GetInt32().Should().Be(1, "still a DRAFT");
    }

    [Fact]
    public async Task EmailDraft_RegardingAnInvoiceUnderAnOrdinaryMatter_CarriesTheMatterStamp_AndIsOwnedByTheInvoicesTeam()
    {
        var result = await DraftRegardingAnInvoiceUnder(w => w.WithOrdinaryRoot("sprk_matter", DraftMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Posts.Should().BeEmpty();
        var fields = TheDraftCreate();
        Owner(fields).Should().Be(Directory.ChildTeam);
        DraftBind(fields, "sprk_RegardingInvoice").Should().Be($"/sprk_invoices({DraftInvoice:D})");
        DraftBind(fields, "sprk_RegardingMatter").Should().Be($"/sprk_matters({DraftMatter:D})",
            "the I-1 stamp (the invoice's matter) is in the create payload, once");
        DraftBind(fields, "sprk_SentBy").Should().Be($"/systemusers({Caller:D})");
    }

    [Fact]
    public async Task EmailDraft_RegardingAnOrdinaryInvoiceUnderASecureMatter_IsOwnedByTheNamedSecureTeam_AsBefore()
    {
        // The invoice is still team-owned in the ordinary unit (before the reconciler moves it). The stamped matter is in the
        // owner question, so secure-if-any decides — the answer the owned create gave. Without the stamp in the item the
        // resolver would give the ordinary team (task 017 d4 probe).
        var result = await DraftRegardingAnInvoiceUnder(w => w.WithSecureRoot("sprk_matter", DraftMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var fields = TheDraftCreate();
        Owner(fields).Should().Be(Directory.SecureNamedTeam);
        DraftBind(fields, "sprk_RegardingMatter").Should().Be($"/sprk_matters({DraftMatter:D})");
        DraftBind(fields, "sprk_SentBy").Should().Be($"/systemusers({Caller:D})");
    }

    [Fact]
    public async Task EmailDraft_UnfiledDraft_IsStillCreatedAsTheDrafter_WithNoSenderAndNoServerCreate()
    {
        var creation = DraftCreation();

        var result = await Draft(CoreAncestorResolverFixtures.Inert(), creation);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _appCreates.Should().BeEmpty("an unfiled draft keeps its creator (E1): it is not the service's create");
        var post = _user.Posts.Should().ContainSingle().Subject;
        post.Body.Should().NotContain("sprk_SentBy", "only a draft the application creates records its sender");
    }

    // ── refusals: today's codes, nothing created ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmailDraft_RegardingAnInvoiceUnderAFlaggedButNotIsolatedMatter_IsRefusedWithTheResolversCode_AndCreatesNothing()
    {
        var result = await DraftRegardingAnInvoiceUnder(w =>
            w.WithRecord("sprk_matter", DraftMatter, Directory.ChildBu, isSecure: true, owningTeam: Directory.ChildTeam));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated, "the resolver's code is carried, as before");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty("no fallback create as the user");
    }

    [Fact]
    public async Task EmailDraft_WhenTheCallerMayAppendToTheInvoiceButNotItsMatter_IsDenied_AndCreatesNothing()
    {
        _user.NoAppendTo.Add(DraftMatter);

        var result = await DraftRegardingAnInvoiceUnder(w => w.WithOrdinaryRoot("sprk_matter", DraftMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            "the stamped matter is a record the draft names, so filing under it costs AppendTo, as before");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty("no fallback create as the user");
    }

    [Fact]
    public async Task EmailDraft_WhenTheServerCreateFaults_FailsAsAnInternalError_AndNothingFallsBackToTheUser()
    {
        _appOnly
            .Setup(a => a.UpdateRecordFieldsAsync(
                "sprk_communication", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .ThrowsAsync(new HttpRequestException("Test: the app-only create faulted."));

        var result = await Draft(CoreAncestorResolverFixtures.Inert(), DraftCreation(), "sprk_matter", OrdinaryMatter);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.ErrorMessage.Should().NotContain("Test: the app-only create faulted", "no fault text reaches the model");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty("a create that faulted is never retried as the user");
    }

    // ── the refusal map: each kind keeps the tool code the owned create gave; the text is the shared map's ────────────

    public static TheoryData<string, string, string> RefusalRows() => new()
    {
        { nameof(RecordCreationRefusalKind.NotFoundOrDenied), RecordCreationRefusal.NotFoundCode, DataverseUserClientErrorCodes.AccessDenied },
        { nameof(RecordCreationRefusalKind.Forbidden), RecordCreationRefusal.DeniedCode, DataverseUserClientErrorCodes.AccessDenied },
        { nameof(RecordCreationRefusalKind.BlockedColumn), RecordCreationRefusal.BlockedColumnCode, DataverseUserClientErrorCodes.AccessDenied },
        { nameof(RecordCreationRefusalKind.Invalid), RecordCreationRefusal.InvalidPayloadCode, ToolErrorCodes.ValidationFailed },
        { nameof(RecordCreationRefusalKind.Invalid), RecordCreationRefusal.StampUnderivableCode, ToolErrorCodes.ValidationFailed },
        { nameof(RecordCreationRefusalKind.Unavailable), RecordCreationRefusal.StampUnavailableCode, ToolErrorCodes.InternalError },
        { nameof(RecordCreationRefusalKind.Unavailable), RecordCreationRefusal.OwnerUnavailableCode, ToolErrorCodes.InternalError },
        { nameof(RecordCreationRefusalKind.Unavailable), RecordCreationRefusal.CheckFailedCode, ToolErrorCodes.InternalError },
        { nameof(RecordCreationRefusalKind.Undecided), RecordCreationRefusal.NoDecisionCode, ToolErrorCodes.InternalError },
        { nameof(RecordCreationRefusalKind.Conflict), RecordCreationRefusal.NumberUnavailableCode, ToolErrorCodes.InternalError },
        { nameof(RecordCreationRefusalKind.OwnerUnresolved), RecordOwnerRefusal.SecureParentNotIsolated, RecordOwnerRefusal.SecureParentNotIsolated },
        { nameof(RecordCreationRefusalKind.OwnerUnresolved), RecordOwnerRefusal.ParentUnresolved, RecordOwnerRefusal.ParentUnresolved },
    };

    [Theory]
    [MemberData(nameof(RefusalRows))]
    public void EmailDraft_ARefusalKind_KeepsTheToolCodeTheOwnedCreateGave_WithTheSharedMapsText(
        string kindName, string serviceCode, string toolCode)
    {
        var kind = Enum.Parse<RecordCreationRefusalKind>(kindName);
        var refusal = new RecordCreationRefusal(kind, serviceCode, "the service's own words");

        var (code, message) = EmailDraftToolHandler.RefusalCodeAndText(refusal, "c0ffee");

        code.Should().Be(toolCode);
        message.Should().Be(RecordCreationProblemMap.ToProblemDetails(refusal, "sprk_communication", "c0ffee").Detail);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(400)]
    [InlineData(500)]
    public void EmailDraft_AnUpstreamFailureThatNamesNoCode_IsPassedThroughAsBefore_NotMadeIntoInternalError(int status)
    {
        // Task 017 review F-6 (NFR-07): MapClientError gave the result the failure's own ErrorCode, null included.
        var failure = new DataverseUserResponse { IsSuccess = false, StatusCode = status, ErrorCode = null, ErrorMessage = "Test: words" };
        var refusal = new RecordCreationRefusal(RecordCreationRefusalKind.Upstream, "record_creation.dataverse", "x")
        {
            ClientFailure = failure,
        };

        var (code, message) = EmailDraftToolHandler.RefusalCodeAndText(refusal, "c0ffee");

        code.Should().BeNull($"as MapClientError gave it, for a {status}");
        message.Should().NotContain("Test: words");
    }

    [Theory]
    [InlineData(nameof(RecordCreationRefusalKind.PrincipalUnresolved))]
    [InlineData(nameof(RecordCreationRefusalKind.Upstream))]
    public void EmailDraft_AQuestionAskedAsTheCallerThatFailed_KeepsTheFailuresOwnCode_WithoutItsText(string kindName)
    {
        var kind = Enum.Parse<RecordCreationRefusalKind>(kindName);
        var failure = new DataverseUserResponse
        {
            IsSuccess = false,
            StatusCode = 503,
            ErrorCode = DataverseUserClientErrorCodes.ServiceError,
            ErrorMessage = "Test: upstream words",
        };
        var refusal = new RecordCreationRefusal(kind, "record_creation.dataverse", "x") { ClientFailure = failure };

        var (code, message) = EmailDraftToolHandler.RefusalCodeAndText(refusal, "c0ffee");

        code.Should().Be(DataverseUserClientErrorCodes.ServiceError);
        message.Should().NotContain("Test: upstream words");
    }
}
