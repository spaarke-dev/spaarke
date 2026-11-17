using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using World = Sprk.Bff.Api.Tests.Services.Finance.SubmissionOverrideServiceTests.World;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 task 095f-2c (EB-D-236, EB-D-240; design 4 as settled by sections 1 and 15): the note on a blocked submission's Override and Return,
/// over the real <see cref="SubmissionOverrideService"/> and the real Decision Record writer harness of <see cref="SubmissionOverrideServiceTests.World"/>.
/// Each test proves the stored state: the Decision Record's <c>Values</c> and content hash, the invoice's internal <c>sprk_decisionnote</c> column
/// (for an Override, in the release transaction's invoice update; for a Return, through the lifecycle's note overload), and that no status is written
/// by the release.
/// </summary>
/// <remarks>
/// <para><b>Golden hashes (N2).</b> Captured from the code BEFORE 095f-2c (base a5ad76a8de) on the same fixed worlds: an ordinary blocked internal
/// submission overridden and returned with the world's default reason, and a Procedure-gap hold overridden. A decision without a note, or with a
/// whitespace-only one, must hash byte-identically, so a retry of a pre-change decision is never refused by the writer.</para>
/// <para><b>Repeat.</b> A repeat after the record was written but before the state change must carry the first note: another note is refused by
/// the writer as a decision conflict (the same as another reason), not silently dropped.</para>
/// </remarks>
public sealed class SubmissionOverrideDecisionNoteTests
{
    private const string OverrideNoNoteHash = "174D456674D962F45A84422B1AF435E71C5F4782AE92FE38EE906C675AFBA1B7";
    private const string ReturnNoNoteHash = "779A8F87C3A2574B0B5915AD926AD5A5B0DEF04496956D73A3D04CAB8C5D5A98";
    private const string GapOverrideNoNoteHash = "10BFFB69C9AB91B72DECF8E447F204469772A5BE45A17209549DD3B08FBE6CFF";

    /// <summary>The reason the world's <c>OverrideAsync</c> / <c>ReturnAsync</c> default to (the golden hashes were captured with it).</summary>
    private const string Reason = "Negotiated rate is not yet in the rate card.";

    private const string Sentinel = "NOTE-095F2C-SENTINEL";

    private static Entity DecisionRecordOf(World w) => w.Records.Created.Single(e => e.LogicalName == "sprk_decisionrecord");

    private static JsonNode Steps(Entity record) => JsonNode.Parse(record.GetAttributeValue<string>("sprk_steps"))!;

    private static string HashOf(World w) => Steps(DecisionRecordOf(w))["reviewHash"]!.GetValue<string>();

    private static JsonObject ValuesOf(World w) => Steps(DecisionRecordOf(w))["steps"]!.AsArray().Single()!["values"]!.AsObject();

    private static string? DecisionNoteOf(World w) =>
        w.Invoice!.Contains(InvoiceCorrectionNote.DecisionColumn) ? w.Invoice[InvoiceCorrectionNote.DecisionColumn] as string : null;

    private static UpdateRequest InvoiceUpdateOf(World w) =>
        w.Transactions.Single().Cast<UpdateRequest>().Single(u => u.Target.LogicalName == "sprk_invoice");

    private static Task<BlockedDecisionResult> Decide(World w, string verb, string? note, string? reason = Reason) =>
        verb == "override"
            ? w.Service.OverrideAsync(World.SubmissionId, reason, note, World.Token, CancellationToken.None)
            : w.Service.ReturnAsync(World.SubmissionId, reason, note, World.Token, CancellationToken.None);

    /// <summary>What the invoice's decision note column was asked to hold: an Override's in the release transaction, a Return's in its one transition.</summary>
    private static string? ColumnWrittenBy(World w, string verb) =>
        verb == "override" ? DecisionNoteOf(w) : w.TransitionNotes.Single();

    private static void AssertRefusedBeforeAnyReadLockOrWrite(World w)
    {
        w.Entities.Invocations.Should().BeEmpty("the note is checked before any read");
        w.LockKeys.Should().BeEmpty("and before any lock");
        w.Recorded.Should().BeEmpty();
        w.Transitions.Should().BeEmpty();
        w.TransitionNotes.Should().BeEmpty();
        w.Transactions.Should().BeEmpty();
        w.RecordCreates.Should().Be(0);
        w.AssertNothingWritten();
        DecisionNoteOf(w).Should().BeNull();
    }

    // ── Override: the release transaction ──

    [Fact(DisplayName = "095f-2c: an Override note is in the record's Values and on the release transaction's invoice update (trimmed, \\n and \\t kept); the release writes no status, no correction note, and the invoice stays In Review")]
    public async Task Override_Note_IsInTheRecord_AndOnTheInvoiceUpdate_WithNoStatus()
    {
        var w = await World.CreateAsync();

        var result = await Decide(w, "override", "  Spoke to the partner.\r\nAgreed\tto this.  ");

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        const string expected = "Spoke to the partner.\r\nAgreed\tto this.";
        ValuesOf(w)["note"]!.GetValue<string>().Should().Be(expected);
        w.Recorded.Single().Reason.Should().Be(Reason, "the reason is unchanged");
        var requests = w.Transactions.Single().Cast<UpdateRequest>().ToList();
        requests.Select(u => u.Target.LogicalName).Should().Equal([FinanceBaseValues.SubmissionEntity, "sprk_invoice"], "the submission's state, then the invoice");
        var invoiceWrite = requests[1].Target;
        invoiceWrite.Attributes.Keys.Should().Equal([InvoiceCorrectionNote.DecisionColumn], "only the decision note: no status, no review detail (no hold lifted), no correction note");
        invoiceWrite.GetAttributeValue<string>(InvoiceCorrectionNote.DecisionColumn).Should().Be(expected);
        requests.Should().OnlyContain(u => u.ConcurrencyBehavior == ConcurrencyBehavior.IfRowVersionMatches);
        w.InvoiceStatus.Should().Be(InvoiceLifecycleStatus.InReview, "an override writes no status");
        w.Transitions.Should().BeEmpty();
        DecisionNoteOf(w).Should().Be(expected);
        w.Invoice!.Contains(InvoiceCorrectionNote.Column).Should().BeFalse("a decision never writes the firm's correction note (EB-D-240)");
    }

    [Fact(DisplayName = "095f-2c: an Override that lifts a Procedure hold writes the note and the review detail in the SAME invoice update, and still no status")]
    public async Task Override_ThatLiftsAHold_WritesTheNoteAndTheDetailTogether()
    {
        var w = await World.CreateHeldAsync(InvoiceSubmissionChannel.Spa);

        (await Decide(w, "override", "Hold accepted")).Kind.Should().Be(BlockedDecisionKind.Decided);

        var invoiceWrite = InvoiceUpdateOf(w).Target;
        invoiceWrite.Attributes.Keys.Should().BeEquivalentTo([InvoiceCorrectionNote.DecisionColumn, InvoiceReviewDetail.Column], "no status, no other column");
        invoiceWrite.GetAttributeValue<string>(InvoiceCorrectionNote.DecisionColumn).Should().Be("Hold accepted");
        w.InvoiceStatus.Should().Be(InvoiceLifecycleStatus.InReview);
    }

    [Theory(DisplayName = "095f-2c N2: with no note, or a whitespace-only note, an Override has no note key and hashes byte-identically to before (golden); the column takes the required reason")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \t\r\n ")]
    public async Task Override_NoNote_HashesAsBefore_AndTheColumnTakesTheReason(string? note)
    {
        var w = await World.CreateAsync();

        (await Decide(w, "override", note)).Kind.Should().Be(BlockedDecisionKind.Decided);

        ValuesOf(w).Should().NotContainKey("note", "never a note key, and never note: null (design 1.2)");
        HashOf(w).Should().Be(OverrideNoNoteHash, "a decision without a note hashes exactly as before 095f-2c");
        DecisionNoteOf(w).Should().Be(Reason, "EB-D-240: with no note the column takes the required reason");
        InvoiceUpdateOf(w).Target.Attributes.Keys.Should().Equal([InvoiceCorrectionNote.DecisionColumn]);
    }

    [Fact(DisplayName = "095f-2c N2: an Override that lifts a Procedure hold, with no note, also hashes byte-identically to before (golden)")]
    public async Task Override_OfAHold_NoNote_HashesAsBefore()
    {
        var w = await World.CreateHeldAsync(InvoiceSubmissionChannel.Internal);

        (await Decide(w, "override", null)).Kind.Should().Be(BlockedDecisionKind.Decided);

        ValuesOf(w).Should().NotContainKey("note");
        HashOf(w).Should().Be(GapOverrideNoNoteHash);
        DecisionNoteOf(w).Should().Be(Reason);
    }

    [Theory(DisplayName = "095f-2c: a note changes the hash (it is part of what was decided) but never the reason")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Note_IsInsideTheHash(string verb)
    {
        var w = await World.CreateAsync();

        (await Decide(w, verb, "A note")).Kind.Should().Be(BlockedDecisionKind.Decided);

        HashOf(w).Should().NotBe(verb == "override" ? OverrideNoNoteHash : ReturnNoNoteHash);
        w.Recorded.Single().Reason.Should().Be(Reason);
    }

    [Fact(DisplayName = "095f-2c: a release whose invoice or submission changed after it was read applies NEITHER update (409 concurrent_change, no note on the invoice); the repeat finds the record and lands the note once")]
    public async Task Override_VersionFault_AppliesNeitherUpdate_AndTheRepeatLandsTheNote()
    {
        foreach (var changed in new[] { "invoice", "submission" })
        {
            var w = await World.CreateAsync();
            w.OnUpdate = () =>
            {
                var row = changed == "invoice" ? w.Invoice! : w.Submission;
                row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
            };

            var result = await Decide(w, "override", "First note");

            result.Kind.Should().Be(BlockedDecisionKind.Refused, changed);
            result.StatusCode.Should().Be(409, changed);
            result.ErrorCode.Should().Be(BlockedSubmissionErrors.ConcurrentChange, changed);
            w.Transactions.Should().BeEmpty("all or nothing: " + changed);
            w.Updates.Should().BeEmpty("the submission's state was not applied either: " + changed);
            DecisionNoteOf(w).Should().BeNull("a note never lands without its release: " + changed);
            w.Code.Should().Be(SubmissionReasonCodes.Blocked, "the submission is still held: " + changed);
            w.RecordCreates.Should().Be(1, "the record was written before the release; the repeat finds it");

            w.OnUpdate = null;
            var repeat = await Decide(w, "override", "First note");

            repeat.Kind.Should().Be(BlockedDecisionKind.Decided, changed);
            w.RecordCreates.Should().Be(1, "no second record: " + changed);
            DecisionNoteOf(w).Should().Be("First note", changed);
            w.Transactions.Should().ContainSingle(changed);
        }
    }

    [Fact(DisplayName = "095f-2c: a repeat after the record but before the state change must carry the first note; another note is a 409 decision_conflict (as for another reason) and writes nothing")]
    public async Task Override_RepeatBeforeTheState_WithAnotherNote_IsRefused_AndTheSameNoteCompletes()
    {
        var w = await World.CreateAsync();
        w.UpdateFault = new TimeoutException("dataverse");
        (await Decide(w, "override", "First note")).StatusCode.Should().Be(503);
        w.UpdateFault = null;

        var another = await Decide(w, "override", "Another note");

        another.Kind.Should().Be(BlockedDecisionKind.Refused);
        another.StatusCode.Should().Be(409);
        another.ErrorCode.Should().Be(BlockedSubmissionErrors.DecisionConflict);
        w.Transactions.Should().BeEmpty();
        DecisionNoteOf(w).Should().BeNull();

        var same = await Decide(w, "override", "First note");

        same.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1);
        DecisionNoteOf(w).Should().Be("First note");
    }

    [Fact(DisplayName = "095f-2c: a repeat of a released Override writes nothing: no second transaction, the column keeps the first note")]
    public async Task Override_RepeatAfterTheRelease_WritesNothing_AndKeepsTheFirstNote()
    {
        var w = await World.CreateAsync();
        await Decide(w, "override", "First note");

        var repeat = await Decide(w, "override", "A different note");

        repeat.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        w.Transactions.Should().ContainSingle();
        DecisionNoteOf(w).Should().Be("First note");
        w.RecordCreates.Should().Be(1);
    }

    // ── Blocked return: the lifecycle overload ──

    [Fact(DisplayName = "095f-2c: a blocked-return note is in the record's Values and is passed to the lifecycle's TransitionAsync overload (the note), so it lands with the status")]
    public async Task Return_Note_IsInTheRecord_AndPassedToTheTransition()
    {
        var w = await World.CreateAsync();

        var result = await Decide(w, "return", "  Spoke to the firm.  ");

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        ValuesOf(w)["note"]!.GetValue<string>().Should().Be("Spoke to the firm.");
        w.Recorded.Single().Reason.Should().Be(Reason);
        w.Transitions.Should().Equal([InvoiceLifecycleStatus.ReturnedToFirm]);
        w.TransitionNotes.Should().Equal(["Spoke to the firm."]);
        w.Transactions.Should().BeEmpty("a return writes the submission alone; the invoice's note lands with the lifecycle's own update");
        w.Invoice!.Contains(InvoiceCorrectionNote.Column).Should().BeFalse("a decision never writes the firm's correction note");
    }

    [Theory(DisplayName = "095f-2c N2: with no note, or a whitespace-only note, a blocked return has no note key and hashes byte-identically to before (golden); the transition is given the reason")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \t ")]
    public async Task Return_NoNote_HashesAsBefore_AndTheTransitionTakesTheReason(string? note)
    {
        var w = await World.CreateAsync();

        (await Decide(w, "return", note)).Kind.Should().Be(BlockedDecisionKind.Decided);

        ValuesOf(w).Should().NotContainKey("note");
        HashOf(w).Should().Be(ReturnNoNoteHash);
        w.TransitionNotes.Should().Equal([Reason], "EB-D-240: with no note the column takes the required reason");
    }

    [Fact(DisplayName = "095f-2c K-1: a return that finds its invoice already Returned to Firm moves nothing and so writes no note")]
    public async Task Return_InterruptedReturn_WritesNoNote()
    {
        var w = await World.CreateAsync();
        w.SetInvoiceStatus(InvoiceLifecycleStatus.ReturnedToFirm);

        (await Decide(w, "return", "A note")).Kind.Should().Be(BlockedDecisionKind.Decided);

        w.Transitions.Should().BeEmpty();
        w.TransitionNotes.Should().BeEmpty();
    }

    [Fact(DisplayName = "095f-2c: a lifecycle refusal of the blocked return leaves the submission held and writes no state")]
    public async Task Return_WhenTheTransitionIsRefused_WritesNoState()
    {
        var w = await World.CreateAsync();
        w.TransitionRefusal = InvoiceTransitionReasons.ConcurrentChange;

        var result = await Decide(w, "return", "A note");

        result.StatusCode.Should().Be(409);
        w.TransitionNotes.Should().Equal(["A note"], "the note rode with the one transition the lifecycle refused, so it lands with nothing");
        w.Updates.Should().BeEmpty();
        w.Code.Should().Be(SubmissionReasonCodes.Blocked);
    }

    // ── Validation (design 1.1): the 2a set ──

    [Theory(DisplayName = "095f-2c: a note over 2000 characters, or with a control character other than \\n \\r \\t, is a coded 400 before any read, lock or write - override and return")]
    [InlineData("override", "tooLong", "ebilling.note_too_long")]
    [InlineData("return", "tooLong", "ebilling.note_too_long")]
    [InlineData("override", "bell", "ebilling.note_invalid_characters")]
    [InlineData("return", "bell", "ebilling.note_invalid_characters")]
    [InlineData("override", "escape", "ebilling.note_invalid_characters")]
    [InlineData("return", "escape", "ebilling.note_invalid_characters")]
    public async Task RefusedNote_Is400_BeforeAnyReadLockOrWrite(string verb, string kind, string code)
    {
        var w = await World.CreateAsync();
        var note = kind switch
        {
            "tooLong" => new string('x', 2001),
            "bell" => "bad\u0007note",
            _ => "bad\u001bnote",
        };

        var result = await Decide(w, verb, note);

        result.Kind.Should().Be(BlockedDecisionKind.Refused);
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be(code);
        result.Detail.Should().NotContain("bad").And.NotContain("xxxx", "the refusal never echoes the note");
        AssertRefusedBeforeAnyReadLockOrWrite(w);
    }

    [Theory(DisplayName = "095f-2c: the reason is checked before the note - a missing reason with a refused note is still reason_required")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task MissingReason_IsRefusedBeforeTheNote(string verb)
    {
        var w = await World.CreateAsync();

        var result = await Decide(w, verb, new string('x', 2001), reason: "  ");

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.ReasonRequired);
        AssertRefusedBeforeAnyReadLockOrWrite(w);
    }

    [Theory(DisplayName = "095f-2c N1: a 2000-character note and a 1-character note are accepted untruncated; \\n, \\r and \\t are accepted")]
    [InlineData("override", 2000)]
    [InlineData("return", 2000)]
    [InlineData("override", 1)]
    [InlineData("return", 1)]
    public async Task NoteUpToTheLimit_IsAccepted_Untruncated(string verb, int length)
    {
        var w = await World.CreateAsync();
        var note = ("a\nb\tc\r\n" + new string('x', Math.Max(0, length - 7)))[..length];

        (await Decide(w, verb, note)).Kind.Should().Be(BlockedDecisionKind.Decided);

        ValuesOf(w)["note"]!.GetValue<string>().Should().Be(note, "never truncated or stripped");
        ColumnWrittenBy(w, verb).Should().Be(note);
    }

    // ── Logs ──

    [Fact(DisplayName = "095f-2c: no log line carries the note - success, repeat, a failed write, a version conflict, a failed transition")]
    public async Task NoLogLine_CarriesTheNote()
    {
        var overridden = await World.CreateAsync();
        await Decide(overridden, "override", Sentinel + "-1");
        await Decide(overridden, "override", Sentinel + "-1");

        var failedWrite = await World.CreateAsync();
        failedWrite.UpdateFault = new TimeoutException("dataverse");
        await Decide(failedWrite, "override", Sentinel + "-2");

        var conflicted = await World.CreateAsync();
        conflicted.OnUpdate = () => conflicted.Invoice!["versionnumber"] = conflicted.Invoice!.GetAttributeValue<long>("versionnumber") + 1;
        await Decide(conflicted, "override", Sentinel + "-3");

        var returned = await World.CreateAsync();
        await Decide(returned, "return", Sentinel + "-4");

        var failedReturn = await World.CreateAsync();
        failedReturn.TransitionFault = new TimeoutException("dataverse");
        await Decide(failedReturn, "return", Sentinel + "-5");

        var refused = await World.CreateAsync();
        await Decide(refused, "override", Sentinel + new string('x', 2001));

        foreach (var w in new[] { overridden, failedWrite, conflicted, returned, failedReturn, refused })
        {
            w.Log.Entries.Should().NotContain(e => e.Message.Contains(Sentinel, StringComparison.Ordinal), "a note is never logged");
        }

        overridden.Log.Entries.Should().NotBeEmpty("control: the services log something");
    }
}
