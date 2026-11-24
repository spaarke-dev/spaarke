using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 task iss029 (ISS-029, #2880): a line decision repeated with a decision id that a chosen line's Decision Record already holds, but
/// with another decision (another action, reason, amount, note or decider), is refused 409 <c>ebilling.decision.id_reused</c> BEFORE any line
/// field, total, Signal close or Decision Record is written. "Other" is decided by the writer's own comparison (its content hash), never a second
/// definition. A repeat of the same content still completes what the first call left unfinished, and a fault in the read the check runs on is a
/// retryable 503 that writes nothing.
/// </summary>
/// <remarks>Over the in-memory Dataverse (<see cref="ApprovalWorld"/>) with the REAL line decision service, totals service, recorder and writer
/// harness. "Writes nothing" is proven on every store: the line rows (their row version), the invoice totals write, the Signal close calls, the
/// correction rows and the Decision Records.</remarks>
public sealed class InvoiceLineDecisionIdReusedTests
{
    private const string Reason = "Rate above the card";

    /// <summary>The line decision service with the world's RECORDING Signal close (<see cref="ApprovalWorld.CloseAsync"/>), so every close is counted.</summary>
    private static InvoiceLineDecisionService Service(ApprovalWorld w) => new(
        w.Entities.Object, w.Totals(), w.Recorder(), w.Locks, w.Ownership.Object, w.Probe.Object, w.CloseAsync,
        NullLogger<InvoiceLineDecisionService>.Instance);

    private static LineDecisionCommand Command(
        ApprovalWorld w, Guid decisionId, LineDecisionAction action, IReadOnlyList<(Guid Line, decimal? Amount)> lines,
        string? reason = Reason, string? note = null, Guid? decider = null) =>
        new(
            w.InvoiceId, decisionId, action, action == LineDecisionAction.Accept ? null : reason,
            lines.Select(l => new LineDecisionLine(l.Line, action == LineDecisionAction.Reduce ? l.Amount ?? 300m : null)).ToList(),
            decider ?? AliceUser, Bulk: lines.Count > 1, CallerToken, note);

    /// <summary>Everything a line decision writes, as one comparable value: the write log (lines, corrections, closes), every line's row version,
    /// the totals writes, the Signal close calls and the Decision Records.</summary>
    private static string Writes(ApprovalWorld w)
    {
        var lineVersions = string.Join(",", w.Rows.Values
            .Where(r => r.LogicalName == InvoiceReviewReadService.LineEntity)
            .OrderBy(r => r.Id)
            .Select(r => $"{r.Id:N}@{r.GetAttributeValue<long>("versionnumber")}"));
        var totalsWrites = w.Records.Invocations.Count(i => i.Method.Name == nameof(IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync));
        return $"log=[{string.Join(";", w.WriteLog)}] lines=[{lineVersions}] totals={totalsWrites} closes={w.Closes.Count} records={w.H.Created.Count} corrections={w.Corrections.Count}";
    }

    private static InvoiceLineStatus StatusOf(ApprovalWorld w, Entity line) =>
        (InvoiceLineStatus)w.Row(InvoiceReviewReadService.LineEntity, line.Id).GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus).Value;

    private static void AssertIdReused(LineDecisionResult result)
    {
        result.Status.Should().Be(LineDecisionStatus.IdReused, result.Code + " " + result.Detail);
        result.Code.Should().Be("ebilling.decision.id_reused");
        result.Detail.Should().NotBeNullOrWhiteSpace();
        result.FailedStep.Should().BeNull("nothing was written, so there is no step to finish");
        result.CommittedLineIds.Should().BeNull();
    }

    // ── AC1: other content is 409 id_reused, and nothing is written ──

    /// <summary>(first action, first reason, first amount, first note) then (repeat action, reason, amount, note, decider).</summary>
    public static TheoryData<string, LineDecisionAction, string?, decimal?, string?, LineDecisionAction, string?, decimal?, string?, bool> OtherContent() => new()
    {
        { "another reason", LineDecisionAction.Reduce, Reason, 300m, null, LineDecisionAction.Reduce, "Another reason", 300m, null, false },
        { "another reason (disallow)", LineDecisionAction.Disallow, Reason, null, null, LineDecisionAction.Disallow, "Another reason", null, null, false },
        { "another amount", LineDecisionAction.Reduce, Reason, 300m, null, LineDecisionAction.Reduce, Reason, 250m, null, false },
        { "another note", LineDecisionAction.Accept, null, null, "The first note", LineDecisionAction.Accept, null, null, "Another note", false },
        { "a note added", LineDecisionAction.Disallow, Reason, null, null, LineDecisionAction.Disallow, Reason, null, "A note now", false },
        { "the note dropped", LineDecisionAction.Reduce, Reason, 300m, "The first note", LineDecisionAction.Reduce, Reason, 300m, null, false },
        { "another decision (action)", LineDecisionAction.Accept, null, null, null, LineDecisionAction.Disallow, Reason, null, null, false },
        { "another decider", LineDecisionAction.Reduce, Reason, 300m, null, LineDecisionAction.Reduce, Reason, 300m, null, true },
    };

    [Theory(DisplayName = "iss029 AC1: a repeat of a recorded decision id with other content (reason, amount, note, action or decider) is 409 id_reused; no line field, total, Signal close or record is written")]
    [MemberData(nameof(OtherContent))]
    public async Task Repeat_WithOtherContent_Is409_AndWritesNothing(
        string because, LineDecisionAction firstAction, string? firstReason, decimal? firstAmount, string? firstNote,
        LineDecisionAction repeatAction, string? repeatReason, decimal? repeatAmount, string? repeatNote, bool anotherDecider)
    {
        var w = new ApprovalWorld();
        var line = w.AddLine(InvoiceLineStatus.Flagged, 400m);
        var signal = w.AddLineSignal(line.Id);
        var decisionId = Guid.NewGuid();
        var first = await Service(w).DecideAsync(Command(w, decisionId, firstAction, [(line.Id, firstAmount)], firstReason, firstNote), CancellationToken.None);
        first.IsApplied.Should().BeTrue(first.Code + " " + first.Detail);
        w.Closes.Should().ContainSingle(c => c.SignalId == signal, "control: the first decision closed the line's Signal");
        var before = Writes(w);
        var statusBefore = StatusOf(w, line);

        var repeat = await Service(w).DecideAsync(
            Command(w, decisionId, repeatAction, [(line.Id, repeatAmount)], repeatReason, repeatNote, anotherDecider ? BobUser : AliceUser),
            CancellationToken.None);

        AssertIdReused(repeat);
        Writes(w).Should().Be(before, $"{because}: refused before any write");
        StatusOf(w, line).Should().Be(statusBefore);
        w.H.Created.Should().ContainSingle("no second record");
    }

    [Fact(DisplayName = "iss029 AC1: a multi-line call where ONE line's recorded decision differs is 409 id_reused, and NO line (the other one included) is written")]
    public async Task MultiLine_OneLineDiffers_Is409_AndNoLineIsWritten()
    {
        var w = new ApprovalWorld();
        var a = w.AddLine(InvoiceLineStatus.Flagged, 400m);
        var b = w.AddLine(InvoiceLineStatus.Flagged, 400m);
        var decisionId = Guid.NewGuid();
        // A strand as a first call can leave it: line A decided and recorded under the id, line B not yet.
        (await Service(w).DecideAsync(Command(w, decisionId, LineDecisionAction.Reduce, [(a.Id, 300m)]), CancellationToken.None)).IsApplied.Should().BeTrue();
        var before = Writes(w);

        var repeat = await Service(w).DecideAsync(
            Command(w, decisionId, LineDecisionAction.Reduce, [(a.Id, 250m), (b.Id, 300m)]), CancellationToken.None);

        AssertIdReused(repeat);
        Writes(w).Should().Be(before, "all or nothing: the line that differs refuses the whole call before any write");
        StatusOf(w, b).Should().Be(InvoiceLineStatus.Flagged, "the other line was not decided");
        w.Row(InvoiceReviewReadService.LineEntity, a.Id).GetAttributeValue<Money>(InvoiceTotalsService.LineApprovedAmount).Value.Should().Be(300m, "the recorded amount stays");
    }

    [Fact(DisplayName = "iss029 AC1: a repeat with other content after a strand at 'signals' is 409, and the unedited repeat still completes it")]
    public async Task AfterAStrand_OtherContent_Is409_AndTheSameContentStillCompletes()
    {
        var w = new ApprovalWorld();
        var line = w.AddLine(InvoiceLineStatus.Flagged, 400m);
        var signal = w.AddLineSignal(line.Id);
        w.CloseFailsOnce[signal] = SignalAutomatedCloseOutcome.ChangedSinceRead;
        var decisionId = Guid.NewGuid();
        var first = await Service(w).DecideAsync(Command(w, decisionId, LineDecisionAction.Reduce, [(line.Id, 300m)]), CancellationToken.None);
        first.Status.Should().Be(LineDecisionStatus.Incomplete);
        first.FailedStep.Should().Be("signals");
        var before = Writes(w);

        var edited = await Service(w).DecideAsync(Command(w, decisionId, LineDecisionAction.Reduce, [(line.Id, 300m)], "Another reason"), CancellationToken.None);

        AssertIdReused(edited);
        Writes(w).Should().Be(before);
        w.SignalStatus(signal).Should().Be(SignalWriter.SignalStatusOpen, "the edited repeat closes nothing");

        var same = await Service(w).DecideAsync(Command(w, decisionId, LineDecisionAction.Reduce, [(line.Id, 300m)]), CancellationToken.None);

        same.IsApplied.Should().BeTrue(same.Code + " " + same.Detail);
        same.Lines!.Single().AlreadyRecorded.Should().BeTrue();
        w.SignalStatus(signal).Should().Be(SignalWriter.SignalStatusResolved, "the unedited repeat completes the close");
        w.H.Created.Should().ContainSingle("one record, never a second");
    }

    [Fact(DisplayName = "iss029 AC1: the route renders IdReused as 409 with errorCode ebilling.decision.id_reused (the shape of the other 409s), never 503")]
    public async Task TheRoute_Renders409()
    {
        var w = new ApprovalWorld();
        var line = w.AddLine(InvoiceLineStatus.Flagged, 400m);
        var decisionId = Guid.NewGuid();
        (await Service(w).DecideAsync(Command(w, decisionId, LineDecisionAction.Reduce, [(line.Id, 300m)]), CancellationToken.None)).IsApplied.Should().BeTrue();
        var repeat = await Service(w).DecideAsync(Command(w, decisionId, LineDecisionAction.Reduce, [(line.Id, 250m)]), CancellationToken.None);

        var http = InvoiceLineReviewEndpoints.ToResult(repeat, new DefaultHttpContext(), decisionId);

        var problem = http.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(409);
        problem.ProblemDetails.Extensions["errorCode"].Should().Be("ebilling.decision.id_reused");
        problem.ProblemDetails.Extensions.Should().NotContainKey("failedStep");
    }

    // ── AC2: the same content still completes ──

    [Fact(DisplayName = "iss029 AC2: the same content over a recorded line and an unrecorded one (a strand) completes: the recorded line is AlreadyRecorded, the other is recorded once")]
    public async Task SameContent_OverARecordedAndAnUnrecordedLine_Completes()
    {
        var w = new ApprovalWorld();
        var a = w.AddLine(InvoiceLineStatus.Flagged, 400m);
        var b = w.AddLine(InvoiceLineStatus.Flagged, 400m);
        var decisionId = Guid.NewGuid();
        (await Service(w).DecideAsync(Command(w, decisionId, LineDecisionAction.Disallow, [(a.Id, null)], note: "Same note"), CancellationToken.None))
            .IsApplied.Should().BeTrue();

        var repeat = await Service(w).DecideAsync(
            Command(w, decisionId, LineDecisionAction.Disallow, [(a.Id, null), (b.Id, null)], note: "Same note"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue(repeat.Code + " " + repeat.Detail);
        repeat.Lines!.Single(l => l.LineId == a.Id).AlreadyRecorded.Should().BeTrue();
        repeat.Lines!.Single(l => l.LineId == b.Id).AlreadyRecorded.Should().BeFalse();
        w.RecordsOf("disallow-line").Should().HaveCount(2, "one record per line, none twice");
        StatusOf(w, b).Should().Be(InvoiceLineStatus.Disallowed);
    }

    [Theory(DisplayName = "iss029 AC2: a repeat of the same content after a record-step strand completes with one record (the check does not fire on the same content)")]
    [InlineData(LineDecisionAction.Accept)]
    [InlineData(LineDecisionAction.Reduce)]
    [InlineData(LineDecisionAction.Disallow)]
    public async Task SameContent_AfterARecordStrand_Completes(LineDecisionAction action)
    {
        var w = new ApprovalWorld();
        var line = w.AddLine(InvoiceLineStatus.Flagged, 400m);
        var decisionId = Guid.NewGuid();
        w.FaultsOnce["identity"] = new TimeoutException("identity down");
        var first = await Service(w).DecideAsync(Command(w, decisionId, action, [(line.Id, 300m)], note: "A note"), CancellationToken.None);
        first.Status.Should().Be(LineDecisionStatus.Incomplete);
        first.FailedStep.Should().Be("record");

        var repeat = await Service(w).DecideAsync(Command(w, decisionId, action, [(line.Id, 300m)], note: "A note"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue(repeat.Code + " " + repeat.Detail);
        w.H.Created.Should().ContainSingle();
    }

    [Fact(DisplayName = "iss029 AC2: a first call (no record under the decision id) is unchanged: it decides and records")]
    public async Task FirstCall_IsUnchanged()
    {
        var w = new ApprovalWorld();
        var line = w.AddLine(InvoiceLineStatus.Flagged, 400m);

        var result = await Service(w).DecideAsync(Command(w, Guid.NewGuid(), LineDecisionAction.Reduce, [(line.Id, 300m)]), CancellationToken.None);

        result.IsApplied.Should().BeTrue(result.Code + " " + result.Detail);
        w.RecordsOf("reduce-line").Should().ContainSingle();
    }

    // ── AC3: a fault in the read the check runs on is 503, nothing written ──

    [Theory(DisplayName = "iss029 AC3: when the recorded-decision read faults the repeat is 503 ebilling.decision.unavailable (retryable), never 409, and writes nothing")]
    [InlineData("Another reason")]
    [InlineData(Reason)]
    public async Task PreCheckReadFault_Is503_AndWritesNothing(string repeatReason)
    {
        var w = new ApprovalWorld();
        var line = w.AddLine(InvoiceLineStatus.Flagged, 400m);
        var decisionId = Guid.NewGuid();
        (await Service(w).DecideAsync(Command(w, decisionId, LineDecisionAction.Reduce, [(line.Id, 300m)]), CancellationToken.None)).IsApplied.Should().BeTrue();
        var before = Writes(w);
        w.H.LookupFault = new TimeoutException("dataverse");

        var repeat = await Service(w).DecideAsync(Command(w, decisionId, LineDecisionAction.Reduce, [(line.Id, 300m)], repeatReason), CancellationToken.None);

        repeat.Status.Should().Be(LineDecisionStatus.Unavailable);
        repeat.Code.Should().Be(LineDecisionCodes.Unavailable);
        Writes(w).Should().Be(before);
    }
}
