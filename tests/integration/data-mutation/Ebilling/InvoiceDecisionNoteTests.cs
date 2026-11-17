using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 task 095f-2a (EB-D-236, EB-D-240; design 1, 2 and 5.3 as settled by sections 14 and 15): the note on reject, return and approve,
/// over the in-memory Dataverse (<see cref="ApprovalWorld"/>) with the REAL services, resolver, recorder and writer harness. Each test proves the
/// stored state: the Decision Record's <c>Values</c> and content hash, and the invoice's internal <c>sprk_decisionnote</c> column, written with
/// the transition.
/// </summary>
/// <remarks>
/// <para><b>Golden hashes (N2).</b> Captured from the code BEFORE 095f-2a (base 5508b576fe) on the same fixed world (submission
/// <see cref="FixedSubmission"/>, decider Alice, the world's default reasons, two decided lines for approve). A decision without a note, or with a
/// whitespace-only one, must hash byte-identically, so a retry of a pre-change decision is never refused by the writer.</para>
/// <para><b>K-1 (design 2 Change 3).</b> No "note differs" refusal on the repeat path: once the record exists an edited note is silently dropped;
/// before it exists the record takes the repeat's note while the column keeps the first.</para>
/// </remarks>
public sealed class InvoiceDecisionNoteTests
{
    private static readonly Guid FixedSubmission = Guid.Parse("09500000-0000-4000-8000-00000000f2a1");

    /// <summary>Golden billing hashes of a decision with no note, captured on base 5508b576fe (before the change).</summary>
    private const string RejectNoNoteHash = "1E91C99B7444B0C67B664ACBB030DAB3B34BAE56A7945B8B7A4FCDA41B48043D";
    private const string ReturnNoNoteHash = "832DF98E3F4C689526E02C315F79EE1790ED5EA909622490449274DE0362F198";
    private const string ApproveNoNoteHash = "A3AB0EB79298A7E6779A6A9AEE92B3A14769C2C7461BEDCCD2AB78E0F9679B45";

    private const string Sentinel = "NOTE-095F2A-SENTINEL";

    public static TheoryData<InvoiceDecisionKind> Kinds => new() { InvoiceDecisionKind.Reject, InvoiceDecisionKind.Return };

    private static Task<InvoiceDecisionResult> Decide(ApprovalWorld w, InvoiceDecisionKind kind, string? note, string? reason = null) =>
        kind == InvoiceDecisionKind.Reject
            ? w.RejectAs(AliceUser, reason ?? "Rate card not followed", note)
            : w.ReturnAs(AliceUser, reason ?? "Please correct the task codes", note);

    private static string ActionOf(InvoiceDecisionKind kind) => kind == InvoiceDecisionKind.Reject ? "reject-invoice" : "return-to-firm";

    private static string DefaultReason(InvoiceDecisionKind kind) => kind == InvoiceDecisionKind.Reject ? "Rate card not followed" : "Please correct the task codes";

    private static string GoldenOf(InvoiceDecisionKind kind) => kind == InvoiceDecisionKind.Reject ? RejectNoNoteHash : ReturnNoNoteHash;

    private static JsonNode Steps(Entity record) => JsonNode.Parse(record.GetAttributeValue<string>("sprk_steps"))!;

    private static string HashOf(Entity record) => Steps(record)["reviewHash"]!.GetValue<string>();

    private static JsonObject ValuesOf(Entity record) => Steps(record)["steps"]!.AsArray().Single()!["values"]!.AsObject();

    private static object? DecisionNote(ApprovalWorld w) => w.Invoice.Contains(InvoiceCorrectionNote.DecisionColumn) ? w.Invoice[InvoiceCorrectionNote.DecisionColumn] : null;

    private static void AssertRefusedBeforeAnyLockOrRead(ApprovalWorld w)
    {
        w.Locks.Acquired.Should().BeEmpty("the note is checked before any lock");
        w.Entities.Invocations.Should().BeEmpty("and before any read");
        w.Transitions.Should().Be(0);
        w.Writes.Should().Be(0);
        w.H.Created.Should().BeEmpty();
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    private static ApprovalWorld ApproveWorld()
    {
        var w = new ApprovalWorld(ownSubmissionId: FixedSubmission);
        w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        w.AddLine(InvoiceLineStatus.Reduced, 600m, approved: 450m);
        return w;
    }

    // ── Reject and return ──

    [Theory(DisplayName = "095f-2a: a reject / return note is in the record's Values and in sprk_decisionnote, trimmed, with \\n and \\t kept; the reason is unchanged")]
    [MemberData(nameof(Kinds))]
    public async Task Note_IsInValuesAndInTheColumn(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();

        var result = await Decide(w, kind, "  Spoke to the partner.\r\nAgreed\tto this.  ");

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        var record = w.RecordsOf(ActionOf(kind)).Should().ContainSingle().Subject;
        var values = ValuesOf(record);
        values["note"]!.GetValue<string>().Should().Be("Spoke to the partner.\r\nAgreed\tto this.");
        values["reason"]!.GetValue<string>().Should().Be(DefaultReason(kind));
        record.GetAttributeValue<string>("sprk_reason").Should().Be(DefaultReason(kind), "the reason stays the record's sprk_reason");
        DecisionNote(w).Should().Be("Spoke to the partner.\r\nAgreed\tto this.", "the column takes the note");
        w.TransitionNotes.Should().Equal(["Spoke to the partner.\r\nAgreed\tto this."], "passed to the one transition, so it lands with the status");
        w.Invoice.Contains(InvoiceCorrectionNote.Column).Should().BeFalse("a decision never writes the firm's correction note (EB-D-240)");
    }

    [Theory(DisplayName = "095f-2a N2: with no note, or a whitespace-only note, the record has no note key and hashes byte-identically to before (golden); the column takes the reason")]
    [InlineData(InvoiceDecisionKind.Reject, null)]
    [InlineData(InvoiceDecisionKind.Reject, "")]
    [InlineData(InvoiceDecisionKind.Reject, "   ")]
    [InlineData(InvoiceDecisionKind.Return, null)]
    [InlineData(InvoiceDecisionKind.Return, " \t\r\n ")]
    public async Task NoNote_HashesAsBefore_AndTheColumnTakesTheReason(InvoiceDecisionKind kind, string? note)
    {
        var w = new ApprovalWorld(ownSubmissionId: FixedSubmission);

        (await Decide(w, kind, note)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);

        var record = w.RecordsOf(ActionOf(kind)).Should().ContainSingle().Subject;
        ValuesOf(record).Should().NotContainKey("note", "never a note key, and never note: null (design 1.2)");
        HashOf(record).Should().Be(GoldenOf(kind), "a decision without a note hashes exactly as before 095f-2a");
        DecisionNote(w).Should().Be(DefaultReason(kind), "EB-D-240: with no note the column takes the required reason");
    }

    [Theory(DisplayName = "095f-2a: a note changes the hash (it is part of what was decided) but never the reason")]
    [MemberData(nameof(Kinds))]
    public async Task Note_IsInsideTheHash(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld(ownSubmissionId: FixedSubmission);

        (await Decide(w, kind, "A note")).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);

        HashOf(w.RecordsOf(ActionOf(kind)).Single()).Should().NotBe(GoldenOf(kind));
    }

    [Theory(DisplayName = "095f-2a N1: a 2000-character note is accepted; \\n, \\r and \\t are accepted")]
    [InlineData(InvoiceDecisionKind.Reject, 2000)]
    [InlineData(InvoiceDecisionKind.Return, 2000)]
    [InlineData(InvoiceDecisionKind.Reject, 1)]
    public async Task NoteUpToTheLimit_IsAccepted(InvoiceDecisionKind kind, int length)
    {
        var w = new ApprovalWorld();
        var note = "a\nb\tc\r\n" + new string('x', length - 7 > 0 ? length - 7 : 0);
        note = note[..length];

        (await Decide(w, kind, note)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);

        ValuesOf(w.RecordsOf(ActionOf(kind)).Single())["note"]!.GetValue<string>().Should().Be(note, "never truncated or stripped");
        DecisionNote(w).Should().Be(note);
    }

    [Theory(DisplayName = "095f-2a N1: a 2001-character note is 400 ebilling.note_too_long; \\u0007 and \\u001b are 400 ebilling.note_invalid_characters; each before any lock or read")]
    [InlineData(InvoiceDecisionKind.Reject, "too-long", "ebilling.note_too_long")]
    [InlineData(InvoiceDecisionKind.Return, "too-long", "ebilling.note_too_long")]
    [InlineData(InvoiceDecisionKind.Reject, "bell", "ebilling.note_invalid_characters")]
    [InlineData(InvoiceDecisionKind.Return, "bell", "ebilling.note_invalid_characters")]
    [InlineData(InvoiceDecisionKind.Reject, "escape", "ebilling.note_invalid_characters")]
    public async Task RefusedNote_Is400_BeforeAnyLockOrRead(InvoiceDecisionKind kind, string shape, string code)
    {
        var w = new ApprovalWorld();
        var note = shape switch
        {
            "too-long" => new string('x', InvoiceCorrectionNote.MaxLength + 1),
            "bell" => "Please see\u0007 this",
            _ => "Escape \u001b[31m",
        };

        var result = await Decide(w, kind, note);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.InvalidRequest);
        result.Code.Should().Be(code);
        result.Detail.Should().NotContain("Please see").And.NotContain("xxxx", "the refusal never echoes the note");
        AssertRefusedBeforeAnyLockOrRead(w);
    }

    [Theory(DisplayName = "095f-2a: a missing reason is still refused first (ebilling.reason_required), whatever the note")]
    [MemberData(nameof(Kinds))]
    public async Task MissingReason_IsRefusedBeforeTheNote(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();

        var result = kind == InvoiceDecisionKind.Reject
            ? await w.RejectAs(AliceUser, reason: "  ", note: new string('x', 3000))
            : await w.ReturnAs(AliceUser, reason: null, note: "\u0007");

        result.Code.Should().Be("ebilling.reason_required");
        AssertRefusedBeforeAnyLockOrRead(w);
    }

    [Theory(DisplayName = "095f-2a K-1 (record exists): a repeat after 'signals' or 'link' with an EDITED note answers Decided, writes no second record, and the record and the column keep the first note")]
    [InlineData(InvoiceDecisionKind.Reject, "signals")]
    [InlineData(InvoiceDecisionKind.Reject, "link")]
    [InlineData(InvoiceDecisionKind.Return, "signals")]
    [InlineData(InvoiceDecisionKind.Return, "link")]
    public async Task K1_RepeatAfterTheRecord_DropsTheEditedNote(InvoiceDecisionKind kind, string failingStep)
    {
        var w = new ApprovalWorld();
        var fails = w.AddInvoiceSignal("EB-TOTAL");
        if (failingStep == "signals")
        {
            w.CloseFailsOnce[fails] = SignalAutomatedCloseOutcome.ChangedSinceRead;
        }
        else
        {
            w.FaultsOnce["link"] = new InvalidOperationException("dataverse down");
        }

        var first = await Decide(w, kind, "The first note");
        first.Outcome.Should().Be(InvoiceDecisionOutcome.Incomplete);
        first.FailedStep.Should().Be(failingStep);
        var created = w.H.Created.Count;

        var repeat = await Decide(w, kind, "An edited note");

        // Had the repeat asked RecordAsync again with the edited note, the writer would have refused the different content (ReviewInvalid) and the
        // call would be 503 at "record", not Decided: Decided with no new create proves the record was never asked again.
        repeat.Outcome.Should().Be(InvoiceDecisionOutcome.Decided, "the record exists; the edited note is dropped, not refused (K-1)");
        w.H.Created.Count.Should().Be(created, "no second Decision Record (no RecordAsync on the repeat)");
        var record = w.RecordsOf(ActionOf(kind)).Should().ContainSingle().Subject;
        ValuesOf(record)["note"]!.GetValue<string>().Should().Be("The first note");
        DecisionNote(w).Should().Be("The first note", "the column landed with the first transition");
        w.Transitions.Should().Be(1, "the repeat does not transition again");
        w.TransitionNotes.Should().Equal(["The first note"]);
    }

    [Theory(DisplayName = "095f-2a K-1 (record not yet written): a repeat after 'record' with an EDITED note records the edited note, while the column keeps the first")]
    [MemberData(nameof(Kinds))]
    public async Task K1_RepeatBeforeTheRecord_RecordsTheEditedNote_ColumnKeepsTheFirst(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        var failOnce = true;
        w.H.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns((Entity row, CancellationToken _) =>
            {
                if (failOnce)
                {
                    failOnce = false;
                    return Task.FromException<Guid>(new InvalidOperationException("dataverse down"));
                }

                row.Id = Guid.NewGuid();
                w.H.Created.Add(row);
                return Task.FromResult(row.Id);
            });

        var first = await Decide(w, kind, "The first note");
        first.Outcome.Should().Be(InvoiceDecisionOutcome.Incomplete);
        first.FailedStep.Should().Be("record");
        w.RecordsOf(ActionOf(kind)).Should().BeEmpty();

        var repeat = await Decide(w, kind, "An edited note");

        repeat.Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        ValuesOf(w.RecordsOf(ActionOf(kind)).Should().ContainSingle().Subject)["note"]!.GetValue<string>().Should().Be("An edited note");
        DecisionNote(w).Should().Be("The first note", "the transition, and so the column, landed with the first call");
        w.Transitions.Should().Be(1);
    }

    [Theory(DisplayName = "095f-2a N5: no reject / return log line carries the note text")]
    [MemberData(nameof(Kinds))]
    public async Task NoLogLine_CarriesTheNote(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        w.FaultsOnce["link"] = new InvalidOperationException("dataverse down");

        (await Decide(w, kind, Sentinel)).Outcome.Should().Be(InvoiceDecisionOutcome.Incomplete);
        (await Decide(w, kind, Sentinel)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);

        w.RejectReturnLog.Entries.Should().NotBeEmpty();
        w.RejectReturnLog.Entries.Should().NotContain(e => e.Message.Contains(Sentinel, StringComparison.Ordinal));
        w.ResolverLog.Entries.Should().NotContain(e => e.Message.Contains(Sentinel, StringComparison.Ordinal));
    }

    // ── Approve (design 5.3) ──

    [Theory(DisplayName = "095f-2a N2: approve with no note (or a whitespace-only one) hashes byte-identically to before (golden), adds no note key and leaves the column unchanged")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public async Task Approve_NoNote_IsUnchanged(string? note)
    {
        var w = ApproveWorld();
        w.Invoice[InvoiceCorrectionNote.DecisionColumn] = "An older decision's note";

        (await w.ApproveAs(AliceUser, note)).Outcome.Should().Be(ApprovalOutcome.Approved);

        var record = w.ApproveRecords.Should().ContainSingle().Subject;
        ValuesOf(record).Should().NotContainKey("note");
        HashOf(record).Should().Be(ApproveNoNoteHash, "an approve without a note hashes exactly as before 095f-2a");
        DecisionNote(w).Should().Be("An older decision's note", "EB-D-240: an approve with no note leaves the column unchanged");
        w.TransitionNotes.Should().Equal([(string?)null], "no note is passed to the transition");
    }

    [Fact(DisplayName = "095f-2a: an approval note is in the record's Values and in sprk_decisionnote")]
    public async Task Approve_Note_IsInValuesAndInTheColumn()
    {
        var w = ApproveWorld();

        (await w.ApproveAs(AliceUser, "  Approved after the call.\nThanks  ")).Outcome.Should().Be(ApprovalOutcome.Approved);

        var record = w.ApproveRecords.Should().ContainSingle().Subject;
        var values = ValuesOf(record);
        values["note"]!.GetValue<string>().Should().Be("Approved after the call.\nThanks");
        values["approvedAmount"]!.GetValue<decimal>().Should().Be(850m);
        HashOf(record).Should().NotBe(ApproveNoNoteHash);
        DecisionNote(w).Should().Be("Approved after the call.\nThanks");
        w.Invoice.Contains(InvoiceCorrectionNote.Column).Should().BeFalse("an approval never writes the firm's correction note (EB-D-240)");
    }

    [Theory(DisplayName = "095f-2a: an approval note of 2001 characters or with \\u0007 is 400 before any lock or read")]
    [InlineData("too-long", "ebilling.note_too_long")]
    [InlineData("bell", "ebilling.note_invalid_characters")]
    public async Task Approve_RefusedNote_Is400_BeforeAnyLockOrRead(string shape, string code)
    {
        var w = ApproveWorld();
        w.WriteLog.Clear();

        var result = await w.ApproveAs(AliceUser, shape == "too-long" ? new string('x', InvoiceCorrectionNote.MaxLength + 1) : "ok\u0007");

        result.Outcome.Should().Be(ApprovalOutcome.InvalidRequest);
        result.Code.Should().Be(code);
        AssertRefusedBeforeAnyLockOrRead(w);
        w.ApproveRecords.Should().BeEmpty();
    }

    [Fact(DisplayName = "095f-2a (5.1 must-not-fire): the bulk accept an approve runs carries no note; only the approve record does")]
    public async Task Approve_BulkAccept_CarriesNoNote()
    {
        var w = ApproveWorld();
        w.AddLine(InvoiceLineStatus.Clean, 100m);

        (await w.ApproveAs(AliceUser, Sentinel)).Outcome.Should().Be(ApprovalOutcome.Approved);

        var others = w.H.Created.Where(r => r.GetAttributeValue<string>("sprk_actioncode") != "approve-invoice").ToList();
        others.Should().NotBeEmpty("the Clean line was accepted in bulk, with its own record");
        others.Should().OnlyContain(r => !r.GetAttributeValue<string>("sprk_steps").Contains(Sentinel), "a bulk accept never carries the approval's note");
        ValuesOf(w.ApproveRecords.Single())["note"]!.GetValue<string>().Should().Be(Sentinel);
        w.Log.Entries.Should().NotContain(e => e.Message.Contains(Sentinel, StringComparison.Ordinal), "N5: no log line carries the note");
    }
}
