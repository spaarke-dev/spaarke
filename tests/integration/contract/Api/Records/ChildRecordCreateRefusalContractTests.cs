using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Api.Records;

/// <summary>
/// field-mapping-server-write-path-r1 task 017 (NFR-07): POST /api/v1/child-records/{table} now creates through
/// <see cref="RecordCreationService"/>, and renders the service's refusal at its own edge through ONE mapping,
/// <see cref="ChildRecordEndpoints.RefusalResult"/>. Every refusal the route gave before keeps its HTTP status, its
/// <c>reasonCode</c> and its words; where the route's own builders survive (<see cref="ChildRecordEndpoints.PlanRefused"/>,
/// <see cref="ChildRecordEndpoints.InvoiceFamilyRefused"/>, <see cref="ProblemDetailsHelper.RecordOwnerRefused(RecordOwnerResolution, string, string?)"/>),
/// the response is byte-identical to theirs. The expected values are the pre-change route's, read from
/// <c>ChildRecordEndpoints.CreateAsync</c> at f758480f3c (task 017 notes, "NFR-07 table").
/// </summary>
/// <remarks>Pure: no host, no Dataverse — the mapping is a function of the refusal, so it is pinned as one.</remarks>
public class ChildRecordCreateRefusalContractTests
{
    private const string Todo = "sprk_todo";
    private const string Trace = "trace-017";

    private static ProblemHttpResult Render(RecordCreationRefusal refusal, string entity = Todo) =>
        ChildRecordEndpoints.RefusalResult(refusal, entity, Trace).Should().BeOfType<ProblemHttpResult>().Subject;

    private static async Task<(int Status, string Body)> WireOf(IResult result)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            TraceIdentifier = Trace,
        };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    private static void ShouldBeTheRoutesShape(ProblemHttpResult problem, int status, string reasonCode, string detail)
    {
        problem.StatusCode.Should().Be(status);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(reasonCode);
        problem.ProblemDetails.Detail.Should().Be(detail);
        problem.ProblemDetails.Extensions.Should().NotContainKey("errorCode", "the route's body, not the shared map's");
    }

    // ── The route's own refusals ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ACallerDataverseCannotName_IsTheRoutes403_ItsWordsUnchanged() =>
        ShouldBeTheRoutesShape(
            Render(new RecordCreationRefusal(RecordCreationRefusalKind.PrincipalUnresolved, ChildRecordEndpoints.CallerUnresolvedReasonCode,
                "anything")),
            StatusCodes.Status403Forbidden, ChildRecordEndpoints.CallerUnresolvedReasonCode,
            "You could not be identified in Dataverse, so the to-do was not created.");

    /// <summary>K-4 decision: the route keeps the caller's own Dataverse status — an upstream 401 stays 401 here.</summary>
    [Theory]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status429TooManyRequests)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    public void AQuestionAskedAsTheCallerThatFailed_IsTheCallersOwnStatusCodeAndMessage(int status) =>
        ShouldBeTheRoutesShape(
            Render(new RecordCreationRefusal(RecordCreationRefusalKind.Upstream, "dv_code", "the service's words")
            {
                ClientFailure = DataverseUserResponse.Fail(status, "dv_code", "Dataverse said no"),
            }),
            status, "dv_code", "Dataverse said no");

    [Fact]
    public async Task ARecordTheRowNamesThatIsMissingOrDenied_IsTheUniformNotFound_ByteIdenticalToTheInvoiceGuardsNotFound()
    {
        var notFound = Render(new RecordCreationRefusal(RecordCreationRefusalKind.NotFoundOrDenied, RecordCreationRefusal.NotFoundCode,
            "the service's own words") { RefusedColumn = "sprk_regardingmatter" });

        ShouldBeTheRoutesShape(notFound, StatusCodes.Status404NotFound, ChildRecordEndpoints.NotFoundCode,
            "A record this to-do is filed under was not found. The to-do was not saved.");
        var oldBuilder = ChildRecordEndpoints.InvoiceFamilyRefused(
            InvoiceFamilyCoreRecord.Refused(Todo, InvoiceFamilyCoreRecordGuard.RecordNotFoundCode, "x"), Todo);
        (await WireOf(notFound)).Should().Be(await WireOf(oldBuilder), "one uniform not-found, whatever the cause");
    }

    [Theory]
    [InlineData((int)RecordCreationRefusalKind.BlockedColumn, RecordCreationRefusal.BlockedColumnCode)]
    [InlineData((int)RecordCreationRefusalKind.Forbidden, RecordCreationRefusal.DeniedCode)]
    public void AServerOwnedColumnOrTheCallersOwnRights_IsTheRoutes403Denied(int kind, string code) =>
        ShouldBeTheRoutesShape(
            Render(new RecordCreationRefusal((RecordCreationRefusalKind)kind, code, "Column 'sprk_containerid' of a work assignment is set by Spaarke — omit it."),
                "sprk_workassignment"),
            StatusCodes.Status403Forbidden, ChildRecordEndpoints.DeniedCode,
            "Column 'sprk_containerid' of a work assignment is set by Spaarke — omit it.");

    [Fact]
    public void APayloadTheRouteDoesNotAccept_IsTheRoutes400InvalidPayload() =>
        ShouldBeTheRoutesShape(
            Render(new RecordCreationRefusal(RecordCreationRefusalKind.Invalid, RecordCreationRefusal.InvalidPayloadCode, "bad bind")),
            StatusCodes.Status400BadRequest, ChildRecordEndpoints.InvalidPayloadCode, "bad bind");

    [Fact]
    public void ATableTheServiceDoesNotKnow_IsTheRoutes400UnsupportedTable() =>
        ShouldBeTheRoutesShape(
            Render(new RecordCreationRefusal(RecordCreationRefusalKind.Invalid, RecordCreationRefusal.TableUnsupportedCode, "x")),
            StatusCodes.Status400BadRequest, ChildRecordEndpoints.UnsupportedTableCode, "'sprk_todo' records are not created here.");

    [Fact]
    public void NoDecision_IsTheRoutes500NoDecision() =>
        ShouldBeTheRoutesShape(
            Render(new RecordCreationRefusal(RecordCreationRefusalKind.Undecided, RecordCreationRefusal.NoDecisionCode, "x")),
            StatusCodes.Status500InternalServerError, ChildRecordEndpoints.NoDecisionCode, "The record was not created.");

    [Fact]
    public void AnIsolatedRootRemovedAgain_Is500_WithItsCodeAndTheServicesWords() =>
        ShouldBeTheRoutesShape(
            Render(new RecordCreationRefusal(RecordCreationRefusalKind.Undecided, SecureRootInheritance.ReasonUnexpectedResult,
                "The work assignment is filed under a secure record ... Nothing was created."), "sprk_workassignment"),
            StatusCodes.Status500InternalServerError, SecureRootInheritance.ReasonUnexpectedResult,
            "The work assignment is filed under a secure record ... Nothing was created.");

    // ── The invoice-family rule: byte-identical to the route's InvoiceFamilyRefused ──────────────────────────────

    [Theory]
    [InlineData(InvoiceFamilyCoreRecordGuard.ReadFailedCode, true)]
    [InlineData(InvoiceFamilyCoreRecordGuard.UnknownEntityCode, true)]
    [InlineData("invoice_family_multiple_core_records", false)]
    [InlineData("invoice_family_no_core_record", false)]
    public async Task TheInvoiceFamilyRulesRefusal_IsByteIdenticalToTheRoutesOwnBuilder(string code, bool undecided)
    {
        const string reason = "the row names two core records";
        var preWrite = undecided ? PreWriteRefusal.Undecided(code, reason) : PreWriteRefusal.Refused(code, reason);
        var refusal = new RecordCreationRefusal(
            undecided ? RecordCreationRefusalKind.Undecided : RecordCreationRefusalKind.Invalid, code, reason) { PreWrite = preWrite };

        var now = Render(refusal, "sprk_invoice");
        var before = ChildRecordEndpoints.InvoiceFamilyRefused(InvoiceFamilyCoreRecord.Refused("sprk_invoice", code, reason), "sprk_invoice");

        ShouldBeTheRoutesShape(now, undecided ? StatusCodes.Status500InternalServerError : StatusCodes.Status400BadRequest, code,
            $"The invoice was not saved: {reason}");
        (await WireOf(now)).Should().Be(await WireOf(before));
    }

    // ── The secure-create plan: byte-identical to the route's PlanRefused ─────────────────────────────────────────

    public static TheoryData<string, int, string> PlanRefusals() => new()
    {
        { Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint.ReasonCreatorNoAccess, (int)RecordCreationRefusalKind.Forbidden,
            "The work assignment was not created: you are walled off it." },
        { AccessFollowsParent.SecureFlagReasonCode, (int)RecordCreationRefusalKind.Forbidden,
            "The work assignment was not created: you are walled off it." },
        { AccessInheritance.ServerOnlyReasonCode, (int)RecordCreationRefusalKind.Forbidden,
            "The work assignment was not created: you are walled off it." },
        { Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable, (int)RecordCreationRefusalKind.Undecided,
            "The work assignment was not created: you are walled off it." },
        { RecordOwnerRefusal.SecureOwnerTeamUnresolved, (int)RecordCreationRefusalKind.Undecided,
            "The work assignment was not created: you are walled off it." },
        { "sdap.inherit.parent_flag_unreadable", (int)RecordCreationRefusalKind.OwnerUnresolved, "you are walled off it" },
    };

    /// <summary>The service builds each refusal as its <c>PlanRefused</c> does (task 013); the route renders it as before.</summary>
    [Theory]
    [MemberData(nameof(PlanRefusals))]
    public async Task ASecureCreatePlansRefusal_IsByteIdenticalToTheRoutesPlanRefused(string code, int kind, string detail)
    {
        var plan = RecordOwnerResolution.Refused(code, "you are walled off it");
        var refusal = new RecordCreationRefusal((RecordCreationRefusalKind)kind, code, detail) { OwnerDecision = plan };

        (await WireOf(ChildRecordEndpoints.RefusalResult(refusal, "sprk_workassignment", Trace)))
            .Should().Be(await WireOf(ChildRecordEndpoints.PlanRefused(plan, "sprk_workassignment", Trace)));
    }

    // ── No owner: the 409 with traceId ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnOwnerTheResolverRefused_IsByteIdenticalToTheRoutes409_WithTraceId()
    {
        var owner = RecordOwnerResolution.Refused(RecordOwnerRefusal.NoDefaultOwnerTeam, "its business unit has no default team");
        var refusal = new RecordCreationRefusal(RecordCreationRefusalKind.OwnerUnresolved, owner.RefusalCode!, owner.Reason!)
        {
            OwnerDecision = owner,
        };

        var now = Render(refusal);
        now.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        now.ProblemDetails.Extensions["traceId"].Should().Be(Trace, "CR-3: the route keeps traceId; no correlationId is added");
        now.ProblemDetails.Extensions.Should().NotContainKey("correlationId");
        (await WireOf(now)).Should().Be(await WireOf(ProblemDetailsHelper.RecordOwnerRefused(owner, "to-do", Trace)));
    }

    [Fact]
    public async Task ASecureFilingTheServiceRefused_IsByteIdenticalToTheRoutes409NoOwnerSource()
    {
        const string detail = "A 'sprk_todo' record cannot be filed under a secure record from chat, and was NOT created.";
        var refusal = new RecordCreationRefusal(RecordCreationRefusalKind.OwnerUnresolved, RecordOwnerRefusal.NoOwnerSource, detail)
        {
            OwnerDecision = RecordOwnerResolution.Owned(Guid.NewGuid()),
        };

        (await WireOf(Render(refusal))).Should().Be(
            await WireOf(ProblemDetailsHelper.RecordOwnerRefused(RecordOwnerRefusal.NoOwnerSource, detail, "to-do", Trace)));
    }

    // ── Refusals the route never gave: the shared map (ADR-019) ───────────────────────────────────────────────────

    [Fact]
    public void ACheckThatFaulted_IsTheSharedMaps503_NotA500()
    {
        var problem = Render(new RecordCreationRefusal(RecordCreationRefusalKind.Unavailable, RecordCreationRefusal.OwnerUnavailableCode,
            "The owner of this to-do could not be decided right now."));

        problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable, "NFR-01: before task 017 this was an unhandled 500");
        problem.ProblemDetails.Extensions["errorCode"].Should().Be(RecordCreationRefusal.OwnerUnavailableCode);
    }
}
