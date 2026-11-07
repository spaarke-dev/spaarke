using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Services.Finance.Submission;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 165a (EB-D-200): hold decisions read only server-written sources. <see cref="InvoiceBlockedHold"/> decides the FR-20 hold from
/// the column state (a hold only), the recorded outcome in the field-secured findings, and the invoice's own submission. Pure cases over
/// <see cref="InvoiceBlockedHold.Of"/>, then approve and reject over the in-memory Dataverse (<see cref="ApprovalWorld"/>). Each must-fire case
/// (the column state differs from the recorded outcome, or the own submission is absent: held) has a must-not-fire control (a server-written
/// release still releases).
/// </summary>
/// <remarks>
/// <para><b>Seed proofs</b> (each restored byte-identical; recorded in <c>projects/ebilling-r1/notes/165a-hold-sources.md</c>): (1) the recorded-outcome
/// check dropped fails the "column state differs" must-fire cases; (2) the own-submission check dropped fails the "own submission absent" cases;
/// (3) the release-stamp check dropped (Blocked always holds) fails the Override and Return controls.</para>
/// </remarks>
public sealed class InvoiceBlockedHoldTests
{
    private static readonly Guid SubmissionId = Guid.Parse("16500000-0000-4000-8000-000000000001");
    private static readonly Guid InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(SubmissionId);

    private static string Stamp(string member, Guid? recordId = null) =>
        $$""","{{member}}":{"recordId":"{{(recordId ?? Guid.Parse("16500000-0000-4000-8000-0000000000aa")):D}}","bySystemUserId":"16500000-0000-4000-8000-0000000000bb","on":"2026-11-06T10:00:00.0000000Z","reason":"r"}""";

    private static string Findings(string kind, Guid invoiceId, string extra = "") =>
        $$"""{"schema":1,"kind":"{{kind}}","invoiceId":"{{invoiceId:D}}"{{extra}}}""";

    private static Entity Submission(Guid id, Guid? invoiceId, InvoiceSubmissionStatus status, string? reasonCode, string? findings)
    {
        var row = new Entity(FinanceBaseValues.SubmissionEntity, id)
        {
            [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)status),
        };
        if (invoiceId is { } invoice)
        {
            row[InvoiceSubmissionPersister.SubmissionInvoice] = new EntityReference(FinanceBaseValues.InvoiceEntity, invoice);
        }

        if (reasonCode is not null)
        {
            row[InvoiceSubmissionPersister.SubmissionReasonCode] = reasonCode;
        }

        if (findings is not null)
        {
            row[InvoiceSubmissionPersister.SubmissionFindingsJson] = findings;
        }

        return row;
    }

    // ── Pure: must-not-fire controls ──

    [Fact(DisplayName = "165a control: the invoice's own submission, Accepted, with findings naming the invoice, is not held")]
    public void Accepted_OwnSubmission_IsOpen() =>
        InvoiceBlockedHold.Of(InvoiceId, [Submission(SubmissionId, InvoiceId, InvoiceSubmissionStatus.Accepted, null, Findings("Accepted", InvoiceId))])
            .Held.Should().BeFalse();

    [Fact(DisplayName = "165a control: the Override release, as its writer leaves it, releases the hold")]
    public void OverrideRelease_Releases() =>
        InvoiceBlockedHold.Of(InvoiceId, [Submission(SubmissionId, InvoiceId, InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.Overridden,
            Findings("Blocked", InvoiceId, Stamp(SubmissionOverrideService.OverrideMember)))]).Held.Should().BeFalse();

    [Fact(DisplayName = "165a control: the Return, as its writer leaves it, releases the hold")]
    public void ReturnRelease_Releases() =>
        InvoiceBlockedHold.Of(InvoiceId, [Submission(SubmissionId, InvoiceId, InvoiceSubmissionStatus.Rejected, SubmissionReasonCodes.Returned,
            Findings("Blocked", InvoiceId, Stamp(SubmissionOverrideService.ReturnMember)))]).Held.Should().BeFalse();

    // ── The re-run commit, through the real re-run (InvoiceSubmissionRerun.FindingsAfter over the in-memory rows of its own tests) ──

    private static string RerunFindings(InvoiceSubmissionRerunTests.World world) =>
        world.Row(InvoiceSubmissionRerunTests.SubmissionId).GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);

    private static InvoiceBlockedHold.Verdict RerunVerdict(InvoiceSubmissionRerunTests.World world) =>
        InvoiceBlockedHold.Of(InvoiceSubmissionRerunTests.InvoiceId, [world.Row(InvoiceSubmissionRerunTests.SubmissionId)]);

    [Fact(DisplayName = "165a control: a re-run that commits Accepted over a Blocked recorded outcome releases the hold")]
    public async Task RerunCommit_Accepted_Releases()
    {
        var world = await InvoiceSubmissionRerunTests.World.SeedAsync();
        world.EditFindings(f =>
        {
            f[InvoiceBlockedHold.KindMember] = nameof(SubmissionEvaluationKind.Blocked);
            f.Remove(SubmissionOverrideService.OverrideMember);
        });
        RerunVerdict(world).ReasonCode.Should().Be(InvoiceBlockedHold.HeldByRecordedOutcome, "the setup: held before the re-run");

        (await world.RunAsync()).Kind.Should().Be(SubmissionEvaluationKind.Accepted);

        InvoiceBlockedHold.RecordedOutcomeOf(RerunFindings(world)).Should().Be(InvoiceBlockedHold.RecordedOutcome.NotHeld);
        RerunVerdict(world).Should().Be(InvoiceBlockedHold.Verdict.Open);
    }

    [Theory(DisplayName = "165a must-fire: a re-run that re-holds a released submission (kind Blocked) drops its release stamp, so the recorded outcome holds again")]
    [InlineData(SubmissionOverrideService.OverrideMember)]
    [InlineData(SubmissionOverrideService.ReturnMember)]
    public async Task RerunReHold_DropsTheReleaseStamp_AndHolds(string member)
    {
        var world = await InvoiceSubmissionRerunTests.World.SeedAsync([InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))]);
        world.EditFindings(f =>
        {
            f.Remove(SubmissionOverrideService.OverrideMember);
            f[InvoiceBlockedHold.KindMember] = nameof(SubmissionEvaluationKind.Blocked);
            f[member] = new JsonObject
            {
                ["recordId"] = "16500000-0000-4000-8000-0000000000aa", ["bySystemUserId"] = "16500000-0000-4000-8000-0000000000bb",
                ["on"] = "2026-11-06T10:00:00.0000000Z", ["reason"] = "r",
            };
        });
        InvoiceBlockedHold.RecordedOutcomeOf(RerunFindings(world)).Should().Be(InvoiceBlockedHold.RecordedOutcome.NotHeld, "the setup: released");
        world.Processor = new InvoiceSubmissionProcessorTests.Harness
        {
            Channel = InvoiceSubmissionChannel.Internal, Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()],
        };

        (await world.RunAsync()).Kind.Should().Be(SubmissionEvaluationKind.Blocked);

        JsonNode.Parse(RerunFindings(world))!.AsObject().ContainsKey(member).Should().BeFalse("the re-hold drops the stamp in its own commit");
        InvoiceBlockedHold.RecordedOutcomeOf(RerunFindings(world)).Should().Be(InvoiceBlockedHold.RecordedOutcome.BlockedOpen);
        RerunVerdict(world).Held.Should().BeTrue();
    }

    // ── Pure: must-fire ──

    [Fact(DisplayName = "165a: the held column state still holds, with its own reason code on the submission row")]
    public void ColumnState_Holds()
    {
        var verdict = InvoiceBlockedHold.Of(InvoiceId,
            [Submission(SubmissionId, InvoiceId, InvoiceSubmissionStatus.Rejected, SubmissionReasonCodes.Blocked, Findings("Blocked", InvoiceId))]);

        verdict.Should().Be(new InvoiceBlockedHold.Verdict(true, InvoiceBlockedHold.HeldBySubmissionState, FinanceBaseValues.SubmissionEntity, SubmissionId));
    }

    [Theory(DisplayName = "165a must-fire: a column state that differs from a Blocked recorded outcome with no release does not release it")]
    [InlineData(InvoiceSubmissionStatus.Rejected, null)]
    [InlineData(InvoiceSubmissionStatus.Rejected, SubmissionReasonCodes.Returned)]
    [InlineData(InvoiceSubmissionStatus.Accepted, null)]
    [InlineData(InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.Overridden)]
    [InlineData(InvoiceSubmissionStatus.Failed, SubmissionReasonCodes.Blocked)]
    public void ColumnStateDiffers_DoesNotRelease(InvoiceSubmissionStatus status, string? reasonCode)
    {
        var verdict = InvoiceBlockedHold.Of(InvoiceId, [Submission(SubmissionId, InvoiceId, status, reasonCode, Findings("Blocked", InvoiceId))]);

        verdict.Should().Be(new InvoiceBlockedHold.Verdict(true, InvoiceBlockedHold.HeldByRecordedOutcome, FinanceBaseValues.SubmissionEntity, SubmissionId));
    }

    [Fact(DisplayName = "165a must-fire: an invalid release stamp does not release")]
    public void InvalidStamp_DoesNotRelease() =>
        InvoiceBlockedHold.Of(InvoiceId, [Submission(SubmissionId, InvoiceId, InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.Overridden,
            Findings("Blocked", InvoiceId, Stamp(SubmissionOverrideService.OverrideMember, Guid.Empty)))]).ReasonCode
            .Should().Be(InvoiceBlockedHold.HeldByRecordedOutcome);

    [Fact(DisplayName = "165a: unreadable recorded findings on the invoice's own submission hold the invoice")]
    public void UnreadableOwnFindings_Hold() =>
        InvoiceBlockedHold.Of(InvoiceId, [Submission(SubmissionId, InvoiceId, InvoiceSubmissionStatus.Accepted, null, "{not json")])
            .ReasonCode.Should().Be(InvoiceBlockedHold.HeldOwnSubmissionUnlinked);

    [Fact(DisplayName = "165a: unreadable findings on another linked submission are left to the firm and Procedure holds (their codes, fail closed)")]
    public void UnreadableOtherFindings_LeftToTheFirmAndProcedureHolds()
    {
        InvoiceBlockedHold.RecordedOutcomeOf("{not json").Should().Be(InvoiceBlockedHold.RecordedOutcome.Unreadable);
        InvoiceFirmHold.IsHeld(null, "{not json", out var unreadable).Should().BeTrue();
        unreadable.Should().BeTrue();
        InvoiceBlockedHold.Of(InvoiceId,
        [
            Submission(SubmissionId, InvoiceId, InvoiceSubmissionStatus.Accepted, null, Findings("Accepted", InvoiceId)),
            Submission(Guid.NewGuid(), InvoiceId, InvoiceSubmissionStatus.Accepted, null, "{not json"),
        ]).Held.Should().BeFalse();
    }

    [Fact(DisplayName = "165a must-fire: own submission absent: held, on the invoice row")]
    public void OwnSubmissionUnlinked_Holds() =>
        InvoiceBlockedHold.Of(InvoiceId, []).Should()
            .Be(new InvoiceBlockedHold.Verdict(true, InvoiceBlockedHold.HeldOwnSubmissionUnlinked, FinanceBaseValues.InvoiceEntity, InvoiceId));

    [Fact(DisplayName = "165a must-fire: own submission absent while another submission is present: held")]
    public void OnlyAnotherSubmissionLinked_Holds() =>
        InvoiceBlockedHold.Of(InvoiceId, [Submission(Guid.NewGuid(), InvoiceId, InvoiceSubmissionStatus.Accepted, null, Findings("Accepted", InvoiceId))])
            .ReasonCode.Should().Be(InvoiceBlockedHold.HeldOwnSubmissionUnlinked);

    [Fact(DisplayName = "165a must-fire: own submission with no recorded outcome: held")]
    public void OwnSubmissionWithoutFindings_Holds() =>
        InvoiceBlockedHold.Of(InvoiceId, [Submission(SubmissionId, InvoiceId, InvoiceSubmissionStatus.Accepted, null, null)])
            .ReasonCode.Should().Be(InvoiceBlockedHold.HeldOwnSubmissionUnlinked);

    [Fact(DisplayName = "165a must-fire: own submission whose recorded outcome is not this invoice's: held")]
    public void OwnSubmissionNamingAnotherInvoice_Holds() =>
        InvoiceBlockedHold.Of(InvoiceId, [Submission(SubmissionId, InvoiceId, InvoiceSubmissionStatus.Accepted, null, Findings("Accepted", Guid.NewGuid()))])
            .ReasonCode.Should().Be(InvoiceBlockedHold.HeldOwnSubmissionUnlinked);

    // ── Approve and reject over the in-memory Dataverse ──

    private static Entity Own(ApprovalWorld w) => w.Rows[("sprk_invoicesubmission", w.OwnSubmissionId)];

    /// <summary>The invoice's own submission as a Blocked outcome leaves it (columns and recorded findings).</summary>
    private static void BlockOwn(ApprovalWorld w)
    {
        var own = Own(w);
        own["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Rejected);
        own["sprk_outcomereasoncode"] = SubmissionReasonCodes.Blocked;
        own["sprk_findingsjson"] = Findings("Blocked", w.InvoiceId);
    }

    /// <summary>The column state set to differ from the recorded outcome; the recorded findings are untouched.</summary>
    private static void ColumnStateDiffers(ApprovalWorld w)
    {
        var own = Own(w);
        own["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted);
        own["sprk_outcomereasoncode"] = SubmissionReasonCodes.Overridden;
    }

    /// <summary>The Override release as SubmissionOverrideService writes it: the column state and the stamp in one write.</summary>
    private static void OverrideReleases(ApprovalWorld w)
    {
        ColumnStateDiffers(w);
        Own(w)["sprk_findingsjson"] = Findings("Blocked", w.InvoiceId, Stamp(SubmissionOverrideService.OverrideMember));
    }

    [Fact(DisplayName = "165a must-fire (approve): a column state that differs from the Blocked recorded outcome is still 409 held; nothing written")]
    public async Task Approve_ColumnStateDiffers_StillHeld()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        BlockOwn(w);
        ColumnStateDiffers(w);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Held);
        result.Detail.Should().Be("The invoice is held until its blocked submission is decided.");
        w.Writes.Should().Be(0);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        w.Log.Entries.Select(e => e.Message).Should().Contain(m => m.Contains(InvoiceBlockedHold.HeldByRecordedOutcome, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "165a control (approve): the Override release approves")]
    public async Task Approve_OverrideRelease_Approves()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        BlockOwn(w);
        OverrideReleases(w);

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
    }

    [Theory(DisplayName = "165a must-fire (approve): own submission absent: 409 held; nothing written")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Approve_OwnSubmissionAbsent_Held(bool variant)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        BlockOwn(w);
        if (variant)
        {
            Own(w)["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());
        }
        else
        {
            Own(w).Attributes.Remove("sprk_invoice");
        }

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Held);
        result.Code.Should().Be(InvoiceApprovalCodes.Held, "the code is unchanged");
        result.Detail.Should().Be(InvoiceBlockedHold.OwnSubmissionUnlinkedDetail, "deciding a submission cannot release it");
        w.Writes.Should().Be(0);
        w.Log.Entries.Select(e => e.Message).Should().Contain(m => m.Contains(InvoiceBlockedHold.HeldOwnSubmissionUnlinked, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "165a must-fire (state view): a column state that differs from the Blocked recorded outcome still shows held, and Approve is not offered")]
    public async Task Get_ColumnStateDiffers_ShowsHeld()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        BlockOwn(w);
        ColumnStateDiffers(w);

        var state = (await w.GetAs(AliceUser))!;

        state.Held.Should().BeTrue();
        state.CanApprove.Should().BeFalse();
    }

    [Fact(DisplayName = "165a must-fire (reject): a column state that differs from the Blocked recorded outcome is still held")]
    public async Task Reject_ColumnStateDiffers_StillHeld()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        BlockOwn(w);
        ColumnStateDiffers(w);

        var result = await w.RejectAs(AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Held);
        result.Code.Should().Be("ebilling.invoice.held");
        result.Detail.Should().Be("The invoice is held until its blocked submission is decided (override or return the submission).");
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    [Theory(DisplayName = "165a (F3): reject and return with the own submission absent keep the held code and say an administrator must restore the link")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectOrReturn_OwnSubmissionUnlinked_SaysRestoreTheLink(bool isReturn)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        Own(w).Attributes.Remove("sprk_invoice");

        var result = isReturn ? await w.ReturnAs(AliceUser) : await w.RejectAs(AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Held);
        result.Code.Should().Be("ebilling.invoice.held");
        result.Detail.Should().Be(InvoiceBlockedHold.OwnSubmissionUnlinkedDetail);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    [Fact(DisplayName = "165a control (reject): after the Override release the invoice can be rejected")]
    public async Task Reject_OverrideRelease_Decides()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        BlockOwn(w);
        OverrideReleases(w);

        (await w.RejectAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
    }
}
