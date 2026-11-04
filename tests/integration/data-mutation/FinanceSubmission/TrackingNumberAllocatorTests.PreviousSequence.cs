using System.ServiceModel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// ebilling-r1 task 095d-1a, K4 (owner decision 2026-11-04, "new column, never reuse"; notes/095c-from-email-firm-optional.md section 16.6): a
/// renumber moves a submission out of the <c>none</c> scope and overwrites its sequence, so the <c>none</c> scope must remember the n it gave
/// up: <c>sprk_invoicesubmission.sprk_previoussequence</c>, written by the renumber and counted by the <c>none</c> scope's next-number read.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, data-mutation KEEP path: optimistic concurrency). It decides whether a tracking number can name two submissions over
/// time. The in-memory table evaluates the allocator's own queries, so a dropped previous-sequence term changes the outcome of the reuse tests
/// (the seed proof removes it); it also models an environment whose schema script has not run (<c>PreviousSequenceColumnExists</c>).
/// </remarks>
public sealed partial class TrackingNumberAllocatorTests
{
    /// <summary>P2 with a no-firm intake of INV-7: what the next none-scope number is.</summary>
    private async Task<string?> NextNoFirmNumberAsync(string number = Number)
    {
        var id = Guid.NewGuid();
        var result = await CreateService().CreateSubmissionAsync(id, null, number);
        result.Outcome.Should().Be(TrackingNumberOutcome.Created);
        _store.Row(id).GetAttributeValue<string>("sprk_trackingscope").Should().Be("none", "a no-firm submission is numbered in the none scope");
        return result.TrackingNumber;
    }

    // ── The renumber writes the column ─────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "095d-1a K4: the renumber writes the none-scope sequence the row gives up to sprk_previoussequence, in the same update as the move")]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Renumber_WritesThePreviousSequence_InTheSubmissionUpdate(int heldInNone)
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        var submission = _store.AddScoped(null, Number, heldInNone);

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        result.Should().Be(new RenumberResult(RenumberOutcome.Renumbered, "INV-7.1", 1, null), "the firm had no INV-7, whatever the none scope held");
        var update = _store.Transactions.Single()[0].Should().BeOfType<UpdateRequest>().Subject;
        update.Target.GetAttributeValue<int?>("sprk_previoussequence").Should().Be(heldInNone, "in the one update that moves the row, so both land or neither");
        var row = _store.Row(submission);
        row.GetAttributeValue<int?>("sprk_previoussequence").Should().Be(heldInNone);
        row.GetAttributeValue<int?>("sprk_submissionsequence").Should().Be(1, "the firm scope's number replaced the sequence");
        row.GetAttributeValue<string>("sprk_trackingscope").Should().Be(FirmA.ToString("D"));
    }

    [Fact(DisplayName = "095d-1a K4 (must not fire): a lost race rolls back the previous sequence with the move; the retry writes it again")]
    public async Task Renumber_ALostRace_WritesThePreviousSequenceOnlyWithTheCommittedAttempt()
    {
        var submission = HeldNoFirmSubmission();
        _store.BeforeTransaction = (call, _) =>
        {
            if (call == 1)
            {
                _store.AddScoped(FirmA, Number, 1);
            }
        };

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        result.TrackingNumber.Should().Be("INV-7.2");
        _store.TransactionCalls.Should().Be(2);
        _store.Transactions.Select(t => ((UpdateRequest)t[0]).Target.GetAttributeValue<int?>("sprk_previoussequence"))
            .Should().Equal(new int?[] { 1, 1 }, "every attempt carries it; only the committed one is applied");
        _store.Row(submission).GetAttributeValue<int?>("sprk_previoussequence").Should().Be(1);
    }

    // ── The none scope never issues it again ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "095d-1a K4 (must fire): after INV-7.1 is renumbered out of the none scope, a new no-firm intake of INV-7 gets INV-7.2, not INV-7.1")]
    public async Task AfterARenumber_TheNextNoFirmIntake_IsNotNumberedWithTheNumberTheRenumberGaveUp()
    {
        var submission = HeldNoFirmSubmission();
        var renumbered = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));
        renumbered.TrackingNumber.Should().Be("INV-7.1", "the firm's first INV-7; the none scope's INV-7.1 is a different scope's number");

        (await NextNoFirmNumberAsync()).Should().Be("INV-7.2", "INV-7.1 named a no-firm submission once; it is not issued again");
        (await NextNoFirmNumberAsync()).Should().Be("INV-7.3");
    }

    [Fact(DisplayName = "095d-1a K4 (must fire): several renumbered rows make the floor the highest previous sequence")]
    public async Task SeveralRenumberedRows_MakeTheFloorTheHighestPreviousSequence()
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        _store.AddRenumbered(FirmA, Number, 1, previousSequence: 2);
        _store.AddRenumbered(FirmB, Number, 1, previousSequence: 5);
        _store.AddRenumbered(FirmA, Number, 2, previousSequence: 3);

        (await NextNoFirmNumberAsync()).Should().Be("INV-7.6");
    }

    [Theory(DisplayName = "095d-1a K4 (must fire): live none rows and renumbered rows together: the floor is the highest of both")]
    [InlineData(4, new[] { 2 }, "INV-7.5")]
    [InlineData(2, new[] { 7 }, "INV-7.8")]
    [InlineData(3, new[] { 1, 6, 2 }, "INV-7.7")]
    [InlineData(0, new[] { 1 }, "INV-7.2")]
    [InlineData(5, new int[0], "INV-7.6")]
    [InlineData(0, new int[0], "INV-7.1")]
    public async Task LiveNoneRowsAndRenumberedRows_TheFloorIsTheHighestOfBoth(int highestLiveInNone, int[] previousSequences, string expected)
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        for (var n = 1; n <= highestLiveInNone; n++)
        {
            _store.AddScoped(null, Number, n);
        }

        var firms = new[] { FirmA, FirmB };
        for (var i = 0; i < previousSequences.Length; i++)
        {
            _store.AddRenumbered(firms[i % 2], Number, i / 2 + 1, previousSequences[i]);
        }

        (await NextNoFirmNumberAsync()).Should().Be(expected);
    }

    [Fact(DisplayName = "095d-1a K4 (must not fire): another firm invoice number's previous sequences do not count, nor does a live row of another number")]
    public async Task APreviousSequence_CountsOnlyForItsOwnFirmInvoiceNumber()
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        _store.AddRenumbered(FirmA, "INV-8", 1, previousSequence: 9);
        _store.AddScoped(null, "INV-8", 4);

        (await NextNoFirmNumberAsync()).Should().Be("INV-7.1");
        (await NextNoFirmNumberAsync("inv-8")).Should().Be("inv-8.10", "its own number's previous sequence counts, matched without regard to case");
    }

    [Fact(DisplayName = "095d-1a K4 (must not fire): a firm's scope never counts the column: it refers to the none scope only")]
    public async Task AFirmsScope_DoesNotCountPreviousSequences_AndNeverReadsTheColumn()
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        _store.AddRenumbered(FirmA, Number, 1, previousSequence: 9);

        var result = await CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, Number);

        result.TrackingNumber.Should().Be("INV-7.2", "FirmA holds INV-7.1; the 9 was the none scope's");
        _store.PreviousSequenceReads.Should().Be(0, "a firm's number is not read against the column");
    }

    [Fact(DisplayName = "095d-1a K4 (must not fire): before P2 there is no none scope, so the column is not read")]
    public async Task BeforeP2_TheColumnIsNotRead()
    {
        InPhase(TrackingScopePhase.P1ScopeColumn);
        _store.AddScoped(FirmA, Number, 1);

        var result = await CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, Number);

        result.TrackingNumber.Should().Be("INV-7.2");
        _store.PreviousSequenceReads.Should().Be(0);
    }

    // ── A renumber that commits while a no-firm intake reads ───────────────────────────────────────────────

    [Theory(DisplayName = "095d-1a K4 (must fire): a renumber that commits just before, or between, the intake's two reads is still counted - the none-scope read comes first")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARenumberCommittingWhileTheIntakeReads_IsStillCounted(bool beforeTheNoneScopeRead)
    {
        var submission = HeldNoFirmSubmission(); // INV-7.1 in the none scope
        var moved = false;

        async Task MoveIt()
        {
            if (moved)
            {
                return;
            }

            moved = true;
            (await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid())))
                .Outcome.Should().Be(RenumberOutcome.Renumbered);
        }

        if (beforeTheNoneScopeRead)
        {
            // The row leaves none before the first read: only the previous-sequence read (the second) can still see it. A previous-sequence read
            // made first would run before this and miss it.
            _store.BeforeSequenceRead = MoveIt;
        }
        else
        {
            // The row leaves none after the first read has seen it: the first read already counted it.
            _store.AfterSequenceRead = MoveIt;
        }

        var intake = await CreateService().CreateSubmissionAsync(Guid.NewGuid(), null, Number);

        moved.Should().BeTrue();
        intake.Outcome.Should().Be(TrackingNumberOutcome.Created);
        intake.TrackingNumber.Should().Be("INV-7.2", "INV-7.1 was given up by a renumber; it is not issued again whenever the renumber commits");
    }

    // ── An environment without the column ──────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "095d-1a K4 (must fire): without the column the renumber is Refused schema_missing at the precondition read: nothing is sent or built")]
    public async Task Renumber_WithoutTheColumn_IsRefusedSchemaMissing_AndNothingIsSent()
    {
        var submission = HeldNoFirmSubmission();
        _store.PreviousSequenceColumnExists = false;
        var built = false;
        var log = new CapturingLogger();

        var result = await CreateService(log).RenumberSubmissionAsync(submission, "1", FirmA, Number, c =>
        {
            built = true;
            return ApplyFor(c, Guid.NewGuid());
        });

        result.Should().Be(new RenumberResult(RenumberOutcome.Refused, null, null, TrackingNumberReasons.SchemaMissing));
        TrackingNumberReasons.SchemaMissing.Should().Be("ebilling.tracking.schema_missing");
        built.Should().BeFalse();
        _store.TransactionCalls.Should().Be(0, "nothing is sent");
        _store.SequenceReads.Should().Be(0, "no number is read for a renumber that cannot record the one it gives up");
        var row = _store.Row(submission);
        row.GetAttributeValue<string>("sprk_trackingscope").Should().Be("none");
        row.GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-7.1");
        row.RowVersion.Should().Be("1");
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains(TrackingNumberReasons.SchemaMissing) && e.Message.Contains(submission.ToString()));
    }

    [Fact(DisplayName = "095d-1a K4 (must not fire): before P2 the renumber still refuses scope_key_inactive, before the row is read - a missing column is never reached")]
    public async Task Renumber_BeforeP2_StillRefusesScopeKeyInactive_EvenWithoutTheColumn()
    {
        InPhase(TrackingScopePhase.P1ScopeColumn);
        _store.PreviousSequenceColumnExists = false;
        var submission = _store.AddScoped(null, Number, 1);

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        result.Should().Be(new RenumberResult(RenumberOutcome.Refused, null, null, TrackingNumberReasons.ScopeKeyInactive));
        _store.IdReads.Should().Be(0);
    }

    [Fact(DisplayName = "095d-1a K4 (must not fire): with the column present, a stale version is still a Conflict (the schema check does not change it)")]
    public async Task Renumber_WithTheColumn_AStaleVersion_IsStillAConflict()
    {
        var submission = HeldNoFirmSubmission();
        _store.Touch(submission);

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        result.Outcome.Should().Be(RenumberOutcome.Conflict);
        _store.TransactionCalls.Should().Be(0);
    }

    [Fact(DisplayName = "095d-1a K4 (must fire): without the column a no-firm create keeps working from the none scope alone, and warns once")]
    public async Task Create_WithoutTheColumn_FallsBackToTheScopeOnlyRead_AndWarnsOnce()
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        _store.PreviousSequenceColumnExists = false;
        _store.AddScoped(null, Number, 1);
        var log = new CapturingLogger();

        var first = await CreateService(log).CreateSubmissionAsync(Guid.NewGuid(), null, Number);
        var second = await CreateService(log).CreateSubmissionAsync(Guid.NewGuid(), null, Number);

        first.TrackingNumber.Should().Be("INV-7.2");
        second.TrackingNumber.Should().Be("INV-7.3", "each arrival still gets its own n");
        _store.PreviousSequenceReads.Should().Be(2, "it asked each time (no cache of the missing column), and fell back each time");
        log.Entries.Where(e => e.Level == LogLevel.Warning && e.Message.Contains(TrackingNumberReasons.SchemaMissing)).Should()
            .ContainSingle("the missing column is logged once, not per create")
            .Which.Message.Should().Contain("sprk_previoussequence");
    }

    [Fact(DisplayName = "095d-1a K4 (must not fire): without the column a firm's create is unaffected and never reads it")]
    public async Task Create_ForAFirm_WithoutTheColumn_IsUnaffected()
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        _store.PreviousSequenceColumnExists = false;
        _store.AddScoped(FirmA, Number, 1);

        var result = await CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, Number);

        result.TrackingNumber.Should().Be("INV-7.2");
        _store.PreviousSequenceReads.Should().Be(0);
    }

    [Fact(DisplayName = "095d-1a K4 (must not fire): a fault of the previous-sequence read that is not a missing column propagates - it is never read as an empty column")]
    public async Task Create_AFaultOfThePreviousSequenceRead_ThatIsNotAMissingColumn_Propagates()
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        _store.PreviousSequenceReadFault = new InvalidOperationException("The operation timed out.");
        var id = Guid.NewGuid();

        var act = () => CreateService().CreateSubmissionAsync(id, null, Number);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*timed out*");
        _store.CreateCalls.Should().Be(0, "no number is issued from a floor that could not be read");
        _store.Has(id).Should().BeFalse();
    }

    // ── Telling "the column does not exist" from any other fault ───────────────────────────────────────────

    [Fact(DisplayName = "095d-1a K4: IsPreviousSequenceColumnMissing sees Dataverse's attribute-does-not-exist fault, typed or as text, wrapped or not")]
    public void TheMissingColumnDetector_SeesTheFault_InEachShapeTheClientRaises()
    {
        var typed = SubmissionStore.MissingPreviousSequenceFault();
        TrackingNumberAllocator.IsPreviousSequenceColumnMissing(typed).Should().BeTrue("the typed fault 0x80041103 naming the column");
        TrackingNumberAllocator.IsPreviousSequenceColumnMissing(new InvalidOperationException("wrapped", typed)).Should().BeTrue("the client may wrap the fault");

        var innerFault = new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault
            {
                ErrorCode = unchecked((int)0x80040000), Message = "The transaction failed.",
                InnerFault = new OrganizationServiceFault { ErrorCode = unchecked((int)0x80041103), Message = SubmissionStore.MissingPreviousSequenceMessage },
            },
            new FaultReason("The transaction failed."));
        TrackingNumberAllocator.IsPreviousSequenceColumnMissing(innerFault).Should().BeTrue("the request's own fault is an inner fault in a transaction");

        TrackingNumberAllocator.IsPreviousSequenceColumnMissing(new InvalidOperationException(SubmissionStore.MissingPreviousSequenceMessage))
            .Should().BeTrue("where the client surfaces only the text");
        TrackingNumberAllocator.IsPreviousSequenceColumnMissing(new InvalidOperationException(
                "Dataverse returned 0x80041103 for sprk_previoussequence"))
            .Should().BeTrue("the code in the text");
    }

    [Fact(DisplayName = "095d-1a K4 (must not fire): the detector does not read another column's fault, another code, or any other failure as this column missing")]
    public void TheMissingColumnDetector_DoesNotFire_OnAnythingElse()
    {
        var otherColumn = new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault
            {
                ErrorCode = unchecked((int)0x80041103),
                Message = "'sprk_invoicesubmission' entity doesn't contain attribute with Name = 'sprk_trackingscope' and NameMapping = 'Logical'.",
            },
            new FaultReason("x"));
        TrackingNumberAllocator.IsPreviousSequenceColumnMissing(otherColumn).Should().BeFalse("it is another column that is missing");

        var otherCode = new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040220), Message = "Access denied to sprk_previoussequence." },
            new FaultReason("x"));
        TrackingNumberAllocator.IsPreviousSequenceColumnMissing(otherCode).Should().BeFalse("a refusal naming the column is not its absence");

        TrackingNumberAllocator.IsPreviousSequenceColumnMissing(new TimeoutException("The operation timed out.")).Should().BeFalse();
        TrackingNumberAllocator.IsPreviousSequenceColumnMissing(new InvalidOperationException("Failed to read sprk_previoussequence")).Should().BeFalse();
        TrackingNumberAllocator.IsPreviousSequenceColumnMissing(new InvalidOperationException(string.Empty)).Should().BeFalse();
    }
}
