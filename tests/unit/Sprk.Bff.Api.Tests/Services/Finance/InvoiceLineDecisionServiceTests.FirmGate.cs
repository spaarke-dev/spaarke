using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Api.Finance;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

// ebilling-r1 task 095d-0 (EB-D-148): no line decision without a firm. Accept, Reduce and Disallow - the single-line route, the multi-line route and
// Accept all Clean - are refused 409 ebilling.line_decision.firm_missing while the invoice has no firm, is held for one, or its post-Set-firm re-run is
// pending (InvoiceFirmGate), after the caller's rights (403 first) and before any write.
public sealed partial class InvoiceLineDecisionServiceTests
{
    public enum GateState { NoFirm, HeldOwnColumn, HeldBySubmission, RerunPending, RerunUnreadable }

    public enum Route { SingleLine, MultiLine, AcceptAllClean }

    public static TheoryData<Route, GateState> AllGateCombinations()
    {
        var data = new TheoryData<Route, GateState>();
        foreach (var route in Enum.GetValues<Route>())
        {
            foreach (var state in Enum.GetValues<GateState>())
            {
                data.Add(route, state);
            }
        }

        return data;
    }

    private static InvoiceFirmGateResult ExpectedGate(GateState state) => state switch
    {
        GateState.NoFirm => InvoiceFirmGateResult.NoFirm,
        GateState.HeldOwnColumn or GateState.HeldBySubmission => InvoiceFirmGateResult.HeldForFirm,
        _ => InvoiceFirmGateResult.RerunPending,
    };

    private static Entity LinkedSubmission(World w, string? findings, string? reasonCode = null) =>
        new("sprk_invoicesubmission", Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", w.InvoiceId),
            ["sprk_outcomereasoncode"] = reasonCode,
            ["sprk_findingsjson"] = findings,
        };

    /// <summary>A world in <paramref name="state"/>: two Flagged lines (one with an open Signal, so a close would show) and one Clean line.</summary>
    private static (World W, Guid FlaggedA, Guid FlaggedB, Guid Clean, Guid Signal) GatedWorld(GateState state)
    {
        var w = new World(billedTotal: 1000m);
        var a = w.AddLine(600m, InvoiceLineStatus.Flagged);
        var b = w.AddLine(300m, InvoiceLineStatus.Flagged);
        var c = w.AddLine(100m, InvoiceLineStatus.Clean);
        var signal = w.AddSignal(a.Id);
        switch (state)
        {
            case GateState.NoFirm:
                w.Invoice.Attributes.Remove("sprk_vendororg");
                break;
            case GateState.HeldOwnColumn:
                w.Invoice["sprk_firmhold"] = SubmissionReasonCodes.FirmMissing;
                break;
            case GateState.HeldBySubmission:
                w.Submissions.Add(LinkedSubmission(w, "{\"pipelineFindings\":[],\"lineFindings\":[]}", SubmissionReasonCodes.FirmMissing));
                break;
            case GateState.RerunPending:
                w.Submissions.Add(LinkedSubmission(w, InvoiceFirmGateTests.FindingsWithAssignFirm(InvoiceAssignFirmService.RulesRerunPending)));
                break;
            case GateState.RerunUnreadable:
                w.Submissions.Add(LinkedSubmission(w, InvoiceFirmGateTests.FindingsWithUnreadableAssignFirm()));
                break;
        }

        return (w, a.Id, b.Id, c.Id, signal);
    }

    private static Task<LineDecisionResult> RunRouteAsync(World w, Route route, Guid flaggedA, Guid flaggedB) => route switch
    {
        Route.SingleLine => w.Service().DecideAsync(Command(w, LineDecisionAction.Reduce, [new(flaggedA, 450m)], Reason), CancellationToken.None),
        Route.MultiLine => w.Service().DecideAsync(
            Command(w, LineDecisionAction.Disallow, [new(flaggedA), new(flaggedB)], Reason, bulk: true), CancellationToken.None),
        _ => w.Service().AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), Decider, CancellationToken.None, CallerToken),
    };

    private static void AssertNothingWritten(World w, Guid flaggedA, Guid flaggedB, Guid clean, Guid signal)
    {
        w.LineWrites.Should().BeEmpty("no line written");
        w.TotalsWrites.Should().BeEmpty("no totals recomputed");
        w.Corrections.Should().BeEmpty("no correction rows");
        w.H.Created.Should().BeEmpty("no Decision Record");
        w.Closes.Should().BeEmpty("no Signal closed");
        w.Signals.Single(s => s.Id == signal).GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value.Should().Be(SignalWriter.SignalStatusOpen);
        w.Line(flaggedA).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Flagged);
        w.Line(flaggedB).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Flagged);
        w.Line(clean).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Clean);
        w.Queue.Sent.Should().BeEmpty("no re-index queued");
    }

    [Theory(DisplayName = "095d-0 must-fire: a line decision - single line, several lines, Accept all Clean - on an invoice with no firm, held for one, or with the Set-firm re-run pending (or its stored decision unreadable) is 409 ebilling.line_decision.firm_missing; nothing written, recorded or closed")]
    [MemberData(nameof(AllGateCombinations))]
    public async Task LineDecision_WithoutAnOpenFirmGate_IsRefused_AndNothingIsWritten(Route route, GateState state)
    {
        var (w, a, b, clean, signal) = GatedWorld(state);

        var result = await RunRouteAsync(w, route, a, b);

        result.Status.Should().Be(LineDecisionStatus.FirmRequired);
        result.Code.Should().Be("ebilling.line_decision.firm_missing");
        result.Detail.Should().Be(LineDecisionCodes.FirmDetailFor(ExpectedGate(state)));
        AssertNothingWritten(w, a, b, clean, signal);
    }

    [Fact(DisplayName = "095d-0: the route renders FirmRequired as 409 with the stable errorCode and the reviewer's sentence (the shape of the other 409s)")]
    public async Task TheRoute_Renders409()
    {
        var (w, a, b, _, _) = GatedWorld(GateState.NoFirm);
        var result = await RunRouteAsync(w, Route.SingleLine, a, b);

        var http = InvoiceLineReviewEndpoints.ToResult(result, new DefaultHttpContext(), Guid.NewGuid());

        var problem = http.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(409);
        problem.ProblemDetails.Extensions["errorCode"].Should().Be("ebilling.line_decision.firm_missing");
        problem.ProblemDetails.Detail.Should().Be(LineDecisionCodes.FirmMissingDetail);
    }

    [Fact(DisplayName = "095d-0 must-fire: a fault reading the firm gate is 503 unavailable with nothing written (fail closed)")]
    public async Task FirmGateReadFault_IsUnavailable_AndNothingIsWritten()
    {
        var (w, a, b, clean, signal) = GatedWorld(GateState.RerunPending);
        w.Submissions.Clear();
        w.Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_invoicesubmission"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("dataverse"));

        var result = await RunRouteAsync(w, Route.SingleLine, a, b);

        result.Status.Should().Be(LineDecisionStatus.Unavailable);
        result.Code.Should().Be(LineDecisionCodes.Unavailable);
        AssertNothingWritten(w, a, b, clean, signal);
    }

    // ── Order: the caller's rights first (403), then the gate (409) ──

    [Theory(DisplayName = "095d-0 order: a caller without the rights to close the line's Signals on an invoice that is not open gets 403 ebilling.decision.not_authorized, not 409 - for no rights at all and for read-only rights, on each route")]
    [InlineData(Route.SingleLine, false)]
    [InlineData(Route.SingleLine, true)]
    [InlineData(Route.MultiLine, false)]
    [InlineData(Route.MultiLine, true)]
    public async Task UnauthorisedCaller_OnAnInvoiceThatIsNotOpen_Is403_NotConflict(Route route, bool readOnly)
    {
        var (w, a, b, clean, signal) = GatedWorld(GateState.NoFirm);
        w.Rights = readOnly ? (_, _) => AccessRights.Read : (_, _) => AccessRights.None;

        var result = await RunRouteAsync(w, route, a, b);

        result.Status.Should().Be(LineDecisionStatus.Forbidden);
        result.Code.Should().Be(LineDecisionCodes.NotAuthorized);
        AssertNothingWritten(w, a, b, clean, signal);
    }

    // ── Must-not-fire ──

    [Theory(DisplayName = "095d-0 must-not-fire: a firm, no hold and a stored Set-firm decision whose rulesRerun is 'completed' - every route proceeds")]
    [InlineData(Route.SingleLine)]
    [InlineData(Route.MultiLine)]
    [InlineData(Route.AcceptAllClean)]
    public async Task WhenTheRerunIsCompleted_TheDecisionProceeds(Route route)
    {
        var (w, a, b, _, _) = GatedWorld(GateState.RerunPending);
        w.Submissions.Clear();
        w.Submissions.Add(LinkedSubmission(w, InvoiceFirmGateTests.FindingsWithAssignFirm(InvoiceAssignFirmService.RulesRerunCompleted)));

        var result = await RunRouteAsync(w, route, a, b);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        w.LineWrites.Should().NotBeEmpty();
        w.H.Created.Should().NotBeEmpty("the decision is recorded");
    }

    [Theory(DisplayName = "095d-0 must-not-fire: a firm that came with the intake - a linked submission with findings but no assignFirm member - every route proceeds")]
    [InlineData(Route.SingleLine)]
    [InlineData(Route.MultiLine)]
    [InlineData(Route.AcceptAllClean)]
    public async Task WithAFirmAndNoAssignFirmMember_TheDecisionProceeds(Route route)
    {
        var (w, a, b, _, _) = GatedWorld(GateState.RerunPending);
        w.Submissions.Clear();
        w.Submissions.Add(LinkedSubmission(w, "{\"pipelineFindings\":[],\"lineFindings\":[]}"));

        var result = await RunRouteAsync(w, route, a, b);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        w.LineWrites.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "095d-0: the firm_missing sentences are one per gate state, and the code is the line decision's family")]
    public void TheSentences()
    {
        LineDecisionCodes.FirmMissing.Should().Be("ebilling.line_decision.firm_missing");
        var sentences = new[] { InvoiceFirmGateResult.NoFirm, InvoiceFirmGateResult.HeldForFirm, InvoiceFirmGateResult.RerunPending }
            .Select(LineDecisionCodes.FirmDetailFor).ToList();
        sentences.Should().OnlyHaveUniqueItems();
        sentences.Last().Should().Be("This invoice's firm was just assigned and its billing rules are being checked again. Try again when the check finishes.");
    }
}
