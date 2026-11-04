using System.ServiceModel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// <see cref="TrackingNumberAllocator"/> (ebilling-r1 task 045, FR-10, D-37, D-87): the only creator of a submission and the only writer of its
/// tracking number and sequence.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, data-mutation KEEP path: optimistic concurrency): this decides whether two firms' (or one firm's two parallel)
/// submissions can share a number, whether a lost race double-writes or loses a row, and whether a redelivered create mints a second number.
/// The Dataverse boundary is a Moq double of <see cref="IGenericEntityService"/> backed by a small in-memory table that evaluates the allocator's
/// own queries (so a dropped firm filter or an added state filter changes the outcome, not just a call count) and enforces the two keys the way
/// Dataverse does, with the faults measured in dev (2026-10-16) through the SDK client: the primary key first (<c>0x80040237</c>), then the
/// (firm, tracking number) alternate key, case-insensitively (<c>0x80060892</c>), each refusing the write whole. The same properties against the
/// real keys are proven live in <c>TrackingNumberAllocatorLiveTests</c> (tests/integration/data-mutation).
/// <para>Task 095c: the table also models the tracking-scope schema (<see cref="TrackingScopePhase"/>): the scope key on (tracking number,
/// scope), which like the firm key does not cover a row whose column is null, the firm key's retirement, and the scope column at SystemRequired
/// (<c>0x80040200</c>). The phase the allocator reads is the test's (<see cref="_phase"/>); every test that does not set it runs in P0, today's
/// behaviour.</para>
/// </remarks>
public sealed class TrackingNumberAllocatorTests
{
    private static readonly Guid FirmA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid FirmB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly SubmissionStore _store = new();
    private readonly TrackingScopePhaseCache _phases = new(TimeProvider.System);
    private readonly Queue<TrackingScopePhase?> _phaseAnswers = new();
    private TrackingScopePhase? _phase = TrackingScopePhase.P0NoScopeColumn;
    private int _phaseReads;

    /// <summary>The phase reader the allocator is given: the next queued answer, else <see cref="_phase"/>; every read is counted.</summary>
    private Task<TrackingScopePhase?> ReadPhase(CancellationToken _)
    {
        Interlocked.Increment(ref _phaseReads);
        lock (_phaseAnswers)
        {
            return Task.FromResult(_phaseAnswers.Count > 0 ? _phaseAnswers.Dequeue() : _phase);
        }
    }

    private TrackingNumberAllocator CreateService(ILogger<TrackingNumberAllocator>? logger = null) =>
        new(_store.Entities.Object, ReadPhase, _phases, logger ?? NullLogger<TrackingNumberAllocator>.Instance);

    /// <summary>Puts the environment in <paramref name="phase"/>, keys and required level as the schema script leaves them at that phase.</summary>
    private void InPhase(TrackingScopePhase phase, bool firmKeyRetired = false)
    {
        _phase = phase;
        _store.ScopeKeyActive = phase == TrackingScopePhase.P2ScopeKeyActive;
        _store.ScopeRequired = phase == TrackingScopePhase.P2ScopeKeyActive;
        _store.FirmKeyActive = !firmKeyRetired;
    }

    // ── Numbering (acceptance 1 and 2) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstSubmissionOfAFirmInvoiceNumber_IsCreatedWithDotOne_AndTheCallersColumnsInTheSameWrite()
    {
        var id = Guid.NewGuid();
        var document = new EntityReference("sprk_document", Guid.NewGuid());
        var fields = new Dictionary<string, object>
        {
            ["sprk_name"] = "INV-002 from the portal",
            ["sprk_processingstatus"] = new OptionSetValue(100000000),
            ["sprk_document"] = document,
        };

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "INV-002", fields);

        result.Outcome.Should().Be(TrackingNumberOutcome.Created);
        result.TrackingNumber.Should().Be("INV-002.1");
        result.Sequence.Should().Be(1);
        var row = _store.Row(id);
        row.Id.Should().Be(id, "the caller's submission id is the new row's id");
        row.GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-002.1");
        row.GetAttributeValue<int?>("sprk_submissionsequence").Should().Be(1);
        row.GetAttributeValue<string>("sprk_firminvoicenumber").Should().Be("INV-002");
        row.GetAttributeValue<EntityReference>("sprk_vendororg").Id.Should().Be(FirmA, "the allocator writes the firm itself: the firm the sequence was counted for is the firm the key guards");
        row.GetAttributeValue<EntityReference>("sprk_vendororg").LogicalName.Should().Be("sprk_organization");
        row.GetAttributeValue<string>("sprk_name").Should().Be("INV-002 from the portal");
        row.GetAttributeValue<OptionSetValue>("sprk_processingstatus").Value.Should().Be(100000000);
        row.GetAttributeValue<EntityReference>("sprk_document").Should().BeSameAs(document);
        _store.CreateCalls.Should().Be(1, "the row is created once, with its number: no create-then-update");
        fields.Should().HaveCount(3, "the caller's dictionary is not modified");
    }

    [Fact]
    public async Task SecondSubmissionOfTheSameFirmInvoiceNumber_GetsDotTwo()
    {
        _store.AddNumbered(FirmA, "INV-002", 1);
        var id = Guid.NewGuid();

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        result.TrackingNumber.Should().Be("INV-002.2");
        result.Sequence.Should().Be(2);
    }

    [Fact]
    public async Task ARepeatedFirmInvoiceNumber_IsNeverRefused_EachArrivalGetsItsOwnN()
    {
        // D-88: a repeat is a Duplicate-rule finding with a configured consequence, decided elsewhere. The allocator numbers it.
        var results = new List<TrackingNumberResult>();
        for (var i = 0; i < 3; i++)
        {
            results.Add(await CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, "INV-002"));
        }

        results.Should().OnlyContain(r => r.Outcome == TrackingNumberOutcome.Created);
        results.Select(r => r.TrackingNumber).Should().Equal("INV-002.1", "INV-002.2", "INV-002.3");
    }

    [Fact]
    public async Task WithDotOneAndDotTwoHeld_TheNextIsDotThree_AndAClosedSubmissionStillOwnsItsNumber()
    {
        _store.AddNumbered(FirmA, "INV-002", 1);
        // Returned to Firm / Rejected: statecode 1. A closed attempt keeps its number, so a state filter would hand .2 out again.
        _store.AddNumbered(FirmA, "INV-002", 2, stateCode: 1);

        var result = await CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, "INV-002");

        result.TrackingNumber.Should().Be("INV-002.3");
        result.Sequence.Should().Be(3);
    }

    [Fact]
    public async Task TheSameFirmInvoiceNumberUnderADifferentFirm_StartsAtDotOne()
    {
        _store.AddNumbered(FirmA, "INV-002", 1);
        _store.AddNumbered(FirmA, "INV-002", 2);

        var result = await CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmB, "INV-002");

        result.TrackingNumber.Should().Be("INV-002.1");
        result.Sequence.Should().Be(1);
    }

    [Fact]
    public async Task TheSequenceIsPerFirmInvoiceNumber_NotGlobalAndNotPerFirm()
    {
        _store.AddNumbered(FirmA, "INV-002", 5);
        _store.AddNumbered(FirmA, "INV-003", 9);

        var result = await CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, "INV-004");

        result.TrackingNumber.Should().Be("INV-004.1");
    }

    [Fact]
    public async Task TheFirmInvoiceNumberIsTrimmedAsSubmitted_AndMatchedWithoutRegardToCase()
    {
        _store.AddNumbered(FirmA, "inv-002", 1);
        var id = Guid.NewGuid();

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "  INV-002  ");

        // Dataverse compares strings, and its key compares tracking numbers, without regard to case: INV-002.1 and inv-002.1 are one number.
        result.TrackingNumber.Should().Be("INV-002.2");
        _store.Row(id).GetAttributeValue<string>("sprk_firminvoicenumber").Should().Be("INV-002");
    }

    // ── Retry rule (acceptance 3 and 4) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ADuplicateOnTheFirstCreate_ReReadsAndCreatesTheNextNumber()
    {
        var id = Guid.NewGuid();
        // Another request commits INV-002.1 between this call's read and its create.
        _store.BeforeCreate = (call, row) =>
        {
            if (call == 1)
            {
                _store.AddNumbered(FirmA, "INV-002", 1);
            }
        };

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.Created);
        result.TrackingNumber.Should().Be("INV-002.2");
        _store.CreateCalls.Should().Be(2, "the refused create, then the retry");
        _store.SequenceReads.Should().Be(2, "the retry re-reads the highest sequence instead of incrementing a stale one");
        _store.Row(id).GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-002.2");
    }

    [Theory]
    [InlineData(9, true, 10)]
    [InlineData(10, false, 10)]
    public async Task TheRetryIsBoundedAtTenAttempts(int duplicates, bool expectCreated, int expectedCreates)
    {
        var id = Guid.NewGuid();
        // Every one of the first `duplicates` creates loses to a competitor that takes exactly the number being written.
        _store.BeforeCreate = (call, row) =>
        {
            if (call <= duplicates)
            {
                _store.AddNumbered(FirmA, "INV-002", row.GetAttributeValue<int>("sprk_submissionsequence"));
            }
        };

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        _store.CreateCalls.Should().Be(expectedCreates);
        _store.SequenceReads.Should().Be(expectedCreates);
        if (expectCreated)
        {
            result.Outcome.Should().Be(TrackingNumberOutcome.Created);
            result.TrackingNumber.Should().Be("INV-002.10");
            _store.Has(id).Should().BeTrue();
        }
        else
        {
            result.Outcome.Should().Be(TrackingNumberOutcome.Refused);
            result.Succeeded.Should().BeFalse();
            result.Reason.Should().Be("ebilling.tracking.unavailable");
            result.TrackingNumber.Should().BeNull();
            result.Sequence.Should().BeNull();
            _store.Has(id).Should().BeFalse("a refusal creates nothing");
            _store.RowCount.Should().Be(duplicates, "only the competitors' rows exist");
        }
    }

    // ── Task 094 (EB-D-167): a number held by an UNDONE attempt (its firm invoice number cleared by the undo) is skipped ──────────

    [Fact(DisplayName = "094 EB-D-167 (must fire): a number held by an undone attempt is skipped - the retry gets {number}.2, not an endless .1 collision")]
    public async Task ANumberHeldByAnUndoneAttempt_IsSkipped()
    {
        _store.AddUndone(FirmA, "INV-002", 1);
        var id = Guid.NewGuid();

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.Created);
        result.TrackingNumber.Should().Be("INV-002.2");
        _store.CreateCalls.Should().Be(2, "the .1 collision, then .2");
    }

    [Fact(DisplayName = "094 EB-D-167 (must fire): more undone attempts than MaxAttempts are all skipped - a skip is not a lost race")]
    public async Task MoreUndoneAttemptsThanMaxAttempts_AreAllSkipped()
    {
        for (var n = 1; n <= TrackingNumberAllocator.MaxAttempts + 2; n++)
        {
            _store.AddUndone(FirmA, "INV-002", n);
        }

        var result = await CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.Created);
        result.TrackingNumber.Should().Be($"INV-002.{TrackingNumberAllocator.MaxAttempts + 3}");
    }

    [Fact(DisplayName = "094 EB-D-167 (must not fire): undone attempts beyond MaxHiddenSkips refuse unavailable (bounded), writing nothing")]
    public async Task UndoneAttemptsBeyondTheSkipBound_AreRefused()
    {
        for (var n = 1; n <= TrackingNumberAllocator.MaxHiddenSkips + 1; n++)
        {
            _store.AddUndone(FirmA, "INV-002", n);
        }

        var id = Guid.NewGuid();
        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.Refused);
        result.Reason.Should().Be("ebilling.tracking.unavailable");
        _store.Has(id).Should().BeFalse();
    }

    [Fact(DisplayName = "094 EB-D-167 (must not fire): a number held by a row that KEEPS its firm invoice number is a lost race (counted), never a skip")]
    public async Task ANumberHeldByANumberedRow_IsALostRace_NotASkip()
    {
        // Every create loses to a competitor that commits exactly the number being written AND keeps its firm invoice number.
        _store.BeforeCreate = (_, row) => _store.AddNumbered(FirmA, "INV-002", row.GetAttributeValue<int>("sprk_submissionsequence"));

        var result = await CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.Refused);
        _store.CreateCalls.Should().Be(TrackingNumberAllocator.MaxAttempts);
    }

    [Fact]
    public async Task AFaultThatIsNotAnAlternateKeyDuplicate_PropagatesOnTheFirstAttempt_WithoutARetry()
    {
        var id = Guid.NewGuid();
        // A timeout may have created the row; a retry could number a second row, so the caller sees the fault (and its redelivery finds the row).
        _store.CreateFault = new InvalidOperationException("Failed to create sprk_invoicesubmission record: The operation timed out.");

        var act = () => CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*timed out*");
        _store.CreateCalls.Should().Be(1);
        _store.SequenceReads.Should().Be(1, "no re-read: a retry was never attempted");
    }

    [Fact]
    public async Task ADataverseFaultWithAnotherErrorCode_PropagatesOnTheFirstAttempt_WithoutARetry()
    {
        _store.CreateFault = new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = unchecked((int)0x8004431A) }, new FaultReason("Attribute value is outside the valid range."));

        var act = () => CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, "INV-002");

        await act.Should().ThrowAsync<FaultException<OrganizationServiceFault>>();
        _store.CreateCalls.Should().Be(1);
    }

    // ── Safe to repeat: the submission id is the idempotency key ───────────────────────────────────────────────

    [Theory]
    [InlineData("INV-002")]
    [InlineData("  inv-002 ")]
    public async Task ARedeliveredCreate_ForASubmissionThatAlreadyHoldsTheNumber_ReturnsItUnchanged(string requested)
    {
        var id = _store.AddNumbered(FirmA, "INV-002", 2);

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, requested, new Dictionary<string, object> { ["sprk_name"] = "second try" });

        result.Outcome.Should().Be(TrackingNumberOutcome.AlreadyCreated);
        result.Succeeded.Should().BeTrue();
        result.TrackingNumber.Should().Be("INV-002.2");
        result.Sequence.Should().Be(2);
        _store.CreateCalls.Should().Be(0, "a redelivered create must not mint a second row or a .3");
        _store.SequenceReads.Should().Be(0);
        _store.Row(id).Contains("sprk_name").Should().BeFalse("the existing row stands; the redelivery's columns are not applied");
    }

    [Fact]
    public async Task ARedeliveredCreateThatRacesItsOwnEarlierCreate_ReturnsTheNumberTheEarlierOneWrote_NotTheNextOne()
    {
        var id = Guid.NewGuid();
        // This call found no row, then an earlier call with the SAME id committed INV-002.1 before this call's sequence read. The read now
        // sees .1 and would create .2, but the id is taken: the primary key refuses it, and the answer is the earlier call's row.
        _store.BeforeSequenceRead = () =>
        {
            _store.AddNumbered(FirmA, "INV-002", 1, id: id);
            return Task.CompletedTask;
        };

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.AlreadyCreated);
        result.TrackingNumber.Should().Be("INV-002.1");
        _store.RowCount.Should().Be(1, "one submission, one number");
        _store.Row(id).GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-002.1");
    }

    [Fact]
    public async Task ARedeliveredCreateWhoseEarlierCreateCommittedAfterThisCallsRead_IsNotCountedAsALostRace()
    {
        var id = Guid.NewGuid();
        // The earlier call commits between this call's sequence read (which saw nothing) and its create: both keys are violated, the primary
        // key answers first, and this call must settle on the existing row without a retry.
        _store.BeforeCreate = (call, row) => _store.AddNumbered(FirmA, "INV-002", 1, id: id);

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.AlreadyCreated);
        result.TrackingNumber.Should().Be("INV-002.1");
        _store.CreateCalls.Should().Be(1);
        _store.SequenceReads.Should().Be(1);
    }

    [Fact]
    public async Task APrimaryKeyDuplicateForARowThatIsNoLongerThere_PropagatesInsteadOfLooping()
    {
        _store.CreateFault = new InvalidOperationException(
            "Failed to create sprk_invoicesubmission record: Cannot insert duplicate key.",
            new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040237) }, new FaultReason("Cannot insert duplicate key.")));

        var act = () => CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, "INV-002");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*duplicate key*");
        _store.CreateCalls.Should().Be(1, "the id is not a number to race for");
    }

    [Fact]
    public async Task ASubmissionIdThatHoldsADifferentFirmInvoiceNumber_IsNotRenumbered()
    {
        var id = _store.AddNumbered(FirmA, "INV-002", 1);

        var act = () => CreateService().CreateSubmissionAsync(id, FirmA, "INV-003");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different firm invoice number*");
        _store.CreateCalls.Should().Be(0);
        _store.Row(id).GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-002.1");
    }

    [Fact]
    public async Task ASubmissionIdThatBelongsToAnotherFirm_IsNotReturned()
    {
        // A caller reusing an id across firms is a bug; returning the other firm's number would leak it.
        var id = _store.AddNumbered(FirmB, "INV-002", 1);

        var act = () => CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different firm*");
        _store.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task ASubmissionIdThatAlreadyExistsUnnumbered_IsNotNumberedByTheAllocator()
    {
        var id = _store.AddUnnumbered(FirmA);

        var act = () => CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not numbered by the tracking number allocator*");
        _store.CreateCalls.Should().Be(0);
        _store.Row(id).Contains("sprk_submissionsequence").Should().BeFalse();
    }

    // ── Caller bugs write nothing ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_trackingnumber")]
    [InlineData("sprk_submissionsequence")]
    [InlineData("sprk_firminvoicenumber")]
    [InlineData("sprk_vendororg")]
    [InlineData("sprk_trackingscope")]
    [InlineData("sprk_invoicesubmissionid")]
    [InlineData("SPRK_TrackingNumber")]
    public async Task AColumnTheAllocatorOwns_CannotBeSuppliedByTheCaller(string column)
    {
        // The allocator is the single owner of the tracking number (ADR-002): a caller-supplied value would set a number without the sequence behind it.
        var fields = new Dictionary<string, object> { ["sprk_name"] = "x", [column] = "anything" };

        var act = () => CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, "INV-002", fields);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage($"*{column}*");
        _store.SequenceReads.Should().Be(0);
        _store.CreateCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnEmptyId_IsRefusedBeforeAnyRead(bool emptySubmission)
    {
        var act = () => CreateService().CreateSubmissionAsync(
            emptySubmission ? Guid.Empty : Guid.NewGuid(), emptySubmission ? FirmA : Guid.Empty, "INV-002");

        await act.Should().ThrowAsync<ArgumentException>();
        _store.SequenceReads.Should().Be(0);
        _store.CreateCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankFirmInvoiceNumber_IsRefusedBeforeAnyRead(string? blank)
    {
        var act = () => CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, blank!);

        await act.Should().ThrowAsync<ArgumentException>();
        _store.SequenceReads.Should().Be(0);
        _store.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task AFirmInvoiceNumberThatCannotFitTheTrackingNumberColumn_IsRefusedBeforeAnyRead()
    {
        var tooLong = new string('7', TrackingNumberAllocator.MaxFirmInvoiceNumberLength + 1);

        var act = () => CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, tooLong);

        await act.Should().ThrowAsync<ArgumentException>();
        _store.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task TheLongestAcceptedFirmInvoiceNumber_WithTheLongestSequence_StillFitsTheTrackingNumberColumn()
    {
        var longest = new string('7', TrackingNumberAllocator.MaxFirmInvoiceNumberLength);
        _store.AddNumbered(FirmA, longest, int.MaxValue - 1);

        var result = await CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, longest);

        result.TrackingNumber.Should().HaveLength(100, "89 characters, a dot and a 10-digit sequence is exactly the column");
    }

    // ── Parallel callers ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TenParallelCreates_AllGetADistinctNumber_AndNoRowIsLeftOverOrDoubled()
    {
        const int callers = 10;
        var ids = Enumerable.Range(0, callers).Select(_ => Guid.NewGuid()).ToList();

        // Hold every caller's FIRST sequence read until all ten have read, so all ten pick .1 and contend for it (the worst case).
        var firstReads = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.BeforeSequenceRead = async () =>
        {
            var arrival = Interlocked.Increment(ref firstReads);
            if (arrival <= callers)
            {
                if (arrival == callers)
                {
                    release.TrySetResult();
                }

                await release.Task;
            }
        };

        var results = await Task.WhenAll(ids.Select(id => Task.Run(() => CreateService().CreateSubmissionAsync(id, FirmA, "INV-002"))));

        results.Should().OnlyContain(r => r.Outcome == TrackingNumberOutcome.Created, "k parallel callers need at most k attempts, and the bound is 10");
        results.Select(r => r.TrackingNumber).Should().OnlyHaveUniqueItems("two parallel submissions must never share a tracking number");
        results.Select(r => r.Sequence!.Value).OrderBy(n => n).Should().Equal(Enumerable.Range(1, callers),
            "the sequence has no gaps: each winner wrote the highest + 1 it re-read");
        _store.CreateCalls.Should().BeGreaterThan(callers, "the ten contended for .1, so the nine losers retried");
        _store.RowCount.Should().Be(callers, "one row per submission");
        foreach (var (id, result) in ids.Zip(results))
        {
            _store.Row(id).GetAttributeValue<string>("sprk_trackingnumber").Should().Be(result.TrackingNumber);
        }
    }

    // ── Tracking scope (task 095c): one test per phase, the stale instance, the no-firm race ───────────────────

    [Fact]
    public async Task P0_AFirmSubmission_IsNumberedPerFirm_AndWritesNoScope()
    {
        _store.AddNumbered(FirmA, "INV-002", 1);
        var id = Guid.NewGuid();

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        result.TrackingNumber.Should().Be("INV-002.2");
        _store.Row(id).Contains("sprk_trackingscope").Should().BeFalse("in P0 the column does not exist: today's write, unchanged");
        _store.Row(id).GetAttributeValue<EntityReference>("sprk_vendororg").Id.Should().Be(FirmA);
    }

    [Theory]
    [InlineData(TrackingScopePhase.P0NoScopeColumn)]
    [InlineData(TrackingScopePhase.P1ScopeColumn)]
    public async Task BeforeP2_ASubmissionWithNoFirm_IsRefusedAsFirmRequired_AndNothingIsReadOrWritten(TrackingScopePhase phase)
    {
        InPhase(phase);

        var result = await CreateService().CreateSubmissionAsync(Guid.NewGuid(), null, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.Refused);
        result.Reason.Should().Be(TrackingNumberReasons.FirmRequired, "nothing guards a no-firm number before the scope key is Active");
        _store.SequenceReads.Should().Be(0);
        _store.CreateCalls.Should().Be(0);
        _store.RowCount.Should().Be(0);
    }

    [Theory(DisplayName = "095c-2: NumbersSubmissionsWithoutFirmAsync (the intake's early check) is true only in P2, and reads and writes no row")]
    [InlineData(TrackingScopePhase.P0NoScopeColumn, false)]
    [InlineData(TrackingScopePhase.P1ScopeColumn, false)]
    [InlineData(TrackingScopePhase.P2ScopeKeyActive, true)]
    public async Task NumbersSubmissionsWithoutFirm_IsTrueOnlyInP2(TrackingScopePhase phase, bool expected)
    {
        InPhase(phase);

        (await CreateService().NumbersSubmissionsWithoutFirmAsync()).Should().Be(expected);
        _store.SequenceReads.Should().Be(0);
        _store.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task P1_AFirmSubmission_WritesItsFirmAsTheScope_AndStillCountsPerFirm()
    {
        InPhase(TrackingScopePhase.P1ScopeColumn);
        _store.AddNumbered(FirmA, "INV-002", 1); // written before the backfill: no scope yet, still counted
        var id = Guid.NewGuid();

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        result.TrackingNumber.Should().Be("INV-002.2", "P1 counts per firm, so a row the backfill has not reached is still counted");
        _store.Row(id).GetAttributeValue<string>("sprk_trackingscope").Should().Be(FirmA.ToString("D"));
        _store.Row(id).GetAttributeValue<EntityReference>("sprk_vendororg").Id.Should().Be(FirmA);
        _store.SequenceReads.Should().Be(1, "one read of the highest sequence");
        _store.CreateCalls.Should().Be(1, "numbered on the first attempt: .2 was not reached through a lost race");
    }

    [Theory]
    [InlineData(TrackingScopePhase.P0NoScopeColumn)]
    [InlineData(TrackingScopePhase.P1ScopeColumn)]
    public async Task BeforeP2_ARedeliveredNoFirmCreate_ThatAP2InstanceAlreadyNumbered_ReturnsThatRow_NotFirmRequired(TrackingScopePhase phase)
    {
        // A P2 instance created the no-firm row and its reply was lost; the redelivery lands on this instance, whose cached phase is stale.
        InPhase(phase);
        var id = _store.AddScoped(null, "INV-002", 1);

        var result = await CreateService().CreateSubmissionAsync(id, null, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.AlreadyCreated);
        result.TrackingNumber.Should().Be("INV-002.1");
        _store.SequenceReads.Should().Be(0, "the existing row is the answer: no number is computed");
        _store.CreateCalls.Should().Be(0);
        _store.RowCount.Should().Be(1);
    }

    [Fact]
    public async Task P2_AFirmSubmission_IsCountedPerScope_AndWritesTheFirmAndItsScope()
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive, firmKeyRetired: true);
        _store.AddScoped(FirmA, "INV-002", 1);
        _store.AddScoped(null, "INV-002", 1);
        _store.AddScoped(FirmB, "INV-002", 4);
        // A firm-A row filed in the no-firm scope (assigning a firm to a no-firm row does this; notes/095c-from-email-firm-optional.md 8.9).
        // Counted per firm it would make the result .8; per scope it is not this scope's row.
        _store.AddWithScope(FirmA, TrackingNumberAllocator.NoFirmScope, "INV-002", 7);
        var id = Guid.NewGuid();

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        result.TrackingNumber.Should().Be("INV-002.2", "the firm's own scope only: neither the no-firm scope (even on a firm-A row) nor another firm's counts");
        _store.Row(id).GetAttributeValue<string>("sprk_trackingscope").Should().Be(FirmA.ToString("D"));
        _store.Row(id).GetAttributeValue<EntityReference>("sprk_vendororg").Id.Should().Be(FirmA);
        _store.SequenceReads.Should().Be(1);
        _store.CreateCalls.Should().Be(1, "exactly one create: the count is per scope, not reached through a lost race");
    }

    [Fact]
    public async Task P2_ASubmissionWithNoFirm_IsNumberedInTheNoFirmScope_AndHasNoFirm()
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        _store.AddScoped(FirmA, "INV-002", 3);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var one = await CreateService().CreateSubmissionAsync(first, null, "INV-002");
        var two = await CreateService().CreateSubmissionAsync(second, null, "INV-002");

        one.TrackingNumber.Should().Be("INV-002.1", "a firm's sequence is not the no-firm sequence");
        two.TrackingNumber.Should().Be("INV-002.2");
        foreach (var id in new[] { first, second })
        {
            _store.Row(id).Contains("sprk_vendororg").Should().BeFalse("no firm is written, never a placeholder");
            _store.Row(id).GetAttributeValue<string>("sprk_trackingscope").Should().Be(TrackingNumberAllocator.NoFirmScope);
        }
    }

    [Fact]
    public async Task P2_ARedeliveredNoFirmCreate_ReturnsItsNumber_AndARedeliveryWithAFirmIsNotRenumbered()
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        var id = _store.AddScoped(null, "INV-002", 1);

        var again = await CreateService().CreateSubmissionAsync(id, null, "INV-002");
        var withFirm = () => CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        again.Outcome.Should().Be(TrackingNumberOutcome.AlreadyCreated);
        again.TrackingNumber.Should().Be("INV-002.1");
        await withFirm.Should().ThrowAsync<InvalidOperationException>("the row has no firm, so it is not this firm's submission");
        _store.RowCount.Should().Be(1);
    }

    [Fact]
    public async Task AStaleP0Instance_RefusedByTheRequiredScopeColumn_ReReadsThePhaseOnce_AndRetriesWithTheScope()
    {
        // Schema script steps 3 to 5: this instance cached P0, then the column was added and raised to SystemRequired.
        _store.ScopeRequired = true;
        _phaseAnswers.Enqueue(TrackingScopePhase.P0NoScopeColumn);
        _phaseAnswers.Enqueue(TrackingScopePhase.P1ScopeColumn);
        var id = Guid.NewGuid();

        var result = await CreateService().CreateSubmissionAsync(id, FirmA, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.Created);
        result.TrackingNumber.Should().Be("INV-002.1");
        _store.Row(id).GetAttributeValue<string>("sprk_trackingscope").Should().Be(FirmA.ToString("D"));
        _store.CreateCalls.Should().Be(2, "the refused create wrote nothing; the retry in P1 wrote the row");
        _phaseReads.Should().Be(2, "the refusal forced one re-read inside the cache window");
    }

    [Fact]
    public async Task AStaleP0Instance_WhosePhaseStillReadsP0_AnswersTheRetryableRefusal_NotAFault_AndWritesNothing()
    {
        _store.ScopeRequired = true; // P0 read (an unreadable metadata read keeps P0), yet the column is required

        var result = await CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.Refused);
        result.Reason.Should().Be(TrackingNumberReasons.Unavailable, "a retryable 503 at the intake, never a 500");
        _store.RowCount.Should().Be(0);
        _store.CreateCalls.Should().Be(1, "one re-read, no retry loop");
        _phaseReads.Should().Be(2);
    }

    [Fact]
    public async Task ARequiredColumnRefusalOutsideP0_Propagates_ItIsNotTheStaleCase()
    {
        InPhase(TrackingScopePhase.P1ScopeColumn);
        _store.CreateFault = new InvalidOperationException("Failed to create", new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040200) }, new FaultReason("Attribute 'sprk_other' cannot be NULL")));

        var act = () => CreateService().CreateSubmissionAsync(Guid.NewGuid(), FirmA, "INV-002");

        await act.Should().ThrowAsync<InvalidOperationException>();
        _phaseReads.Should().Be(1, "no forced re-read: the scope was written, so the refusal is about something else");
    }

    [Fact]
    public async Task TenParallelCreatesWithNoFirm_AllGetADistinctNumberInTheNoFirmScope()
    {
        // The firm key is still Active (steps 7 and 8, before it is retired) and does not cover a no-firm row: only the scope key guards these.
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        const int callers = 10;
        var ids = Enumerable.Range(0, callers).Select(_ => Guid.NewGuid()).ToList();
        var firstReads = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.BeforeSequenceRead = async () =>
        {
            var arrival = Interlocked.Increment(ref firstReads);
            if (arrival <= callers)
            {
                if (arrival == callers)
                {
                    release.TrySetResult();
                }

                await release.Task;
            }
        };

        var results = await Task.WhenAll(ids.Select(id => Task.Run(() => CreateService().CreateSubmissionAsync(id, null, "INV-002"))));

        results.Should().OnlyContain(r => r.Outcome == TrackingNumberOutcome.Created);
        results.Select(r => r.TrackingNumber).Should().OnlyHaveUniqueItems("two parallel no-firm submissions must never share a tracking number");
        results.Select(r => r.Sequence!.Value).OrderBy(n => n).Should().Equal(Enumerable.Range(1, callers));
        _store.CreateCalls.Should().BeGreaterThan(callers, "all ten contended for .1 and the scope key refused the losers");
        _store.RowCount.Should().Be(callers);
        foreach (var id in ids)
        {
            _store.Row(id).Contains("sprk_vendororg").Should().BeFalse();
            _store.Row(id).GetAttributeValue<string>("sprk_trackingscope").Should().Be(TrackingNumberAllocator.NoFirmScope);
        }
    }

    /// <summary>An allocator that holds <paramref name="phase"/> in a cache of its own: one instance of a rolling deployment.</summary>
    private TrackingNumberAllocator CreateInstanceIn(TrackingScopePhase phase) =>
        new(_store.Entities.Object, _ => Task.FromResult<TrackingScopePhase?>(phase), new TrackingScopePhaseCache(TimeProvider.System),
            NullLogger<TrackingNumberAllocator>.Instance);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AP1InstanceAndAP2Instance_NumberingOneFirmAndNumberTogether_NeverShareANumber_AndNeitherFaults(bool firmKeyRetired)
    {
        // Steps 7 to 9 of the schema script on a rolling deployment: the scope key is Active (P2 instances count per scope), and the firm key is
        // still there (both keys guard a firm's row) or already retired (only the scope key does, so a P1 instance's retry meets the scope key).
        // Both instances read .1, both write FirmA + scope FirmA; one wins, the other is refused by a key and takes the next number.
        InPhase(TrackingScopePhase.P2ScopeKeyActive, firmKeyRetired);
        var p1 = CreateInstanceIn(TrackingScopePhase.P1ScopeColumn);
        var p2 = CreateInstanceIn(TrackingScopePhase.P2ScopeKeyActive);
        var arrivals = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.AfterSequenceRead = async () =>
        {
            if (Interlocked.Increment(ref arrivals) <= 2)
            {
                if (arrivals == 2)
                {
                    release.TrySetResult();
                }

                await release.Task;
            }
        };
        var idP1 = Guid.NewGuid();
        var idP2 = Guid.NewGuid();

        var results = await Task.WhenAll(
            Task.Run(() => p1.CreateSubmissionAsync(idP1, FirmA, "INV-002")),
            Task.Run(() => p2.CreateSubmissionAsync(idP2, FirmA, "INV-002")));

        results.Should().OnlyContain(r => r.Outcome == TrackingNumberOutcome.Created, "the loser retries to the next number: never refused, never a fault");
        results.Select(r => r.TrackingNumber).Should().BeEquivalentTo(["INV-002.1", "INV-002.2"], "one of them took .1 and the other .2, never both .1");
        _store.CreateCalls.Should().Be(3, "two contended for .1; the loser's second create took .2");
        _store.RowCount.Should().Be(2);
        _store.Row(idP1).GetAttributeValue<string>("sprk_trackingscope").Should().Be(FirmA.ToString("D"), "a P1 instance writes the scope too");
        _store.Row(idP2).GetAttributeValue<string>("sprk_trackingscope").Should().Be(FirmA.ToString("D"));
    }

    // ── Logging (ADR-015) ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFirmInvoiceNumberIsBusinessData_AndIsLoggedAtDebugOnly()
    {
        const string secret = "SECRET-INV-7731";
        const string other = "SECRET-INV-9999";
        var log = new CapturingLogger();
        var raced = Guid.NewGuid();
        var exhausted = Guid.NewGuid();
        var repeated = _store.AddNumbered(FirmA, other, 40);

        // a win after one lost race
        _store.BeforeCreate = (call, row) =>
        {
            if (call == 1)
            {
                _store.AddNumbered(FirmA, secret, 1);
            }
        };
        (await CreateService(log).CreateSubmissionAsync(raced, FirmA, secret)).TrackingNumber.Should().Be(secret + ".2");

        // exhaustion: every create loses to a competitor that takes exactly the number being written
        _store.BeforeCreate = (call, row) => _store.AddNumbered(FirmA, secret, row.GetAttributeValue<int>("sprk_submissionsequence"));
        (await CreateService(log).CreateSubmissionAsync(exhausted, FirmA, secret)).Outcome.Should().Be(TrackingNumberOutcome.Refused);

        // an already-created submission
        _store.BeforeCreate = null;
        (await CreateService(log).CreateSubmissionAsync(repeated, FirmA, other)).Outcome.Should().Be(TrackingNumberOutcome.AlreadyCreated);

        var important = log.Entries.Where(e => e.Level >= LogLevel.Information).ToList();
        important.Should().Contain(e => e.Level == LogLevel.Warning, "the lost race is logged");
        important.Should().Contain(e => e.Level == LogLevel.Error, "the exhaustion is logged");
        important.Should().OnlyContain(
            e => !e.Message.Contains(secret, StringComparison.OrdinalIgnoreCase) && !e.Message.Contains(other, StringComparison.OrdinalIgnoreCase),
            "the firm invoice number and the tracking number built from it are business data");
        important.Should().Contain(e => e.Message.Contains(raced.ToString()), "the submission id is what an operator searches by");
    }

    private sealed class CapturingLogger : ILogger<TrackingNumberAllocator>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception) + (exception is null ? string.Empty : " | " + exception)));
    }

    /// <summary>
    /// An in-memory <c>sprk_invoicesubmission</c> table behind a Moq <see cref="IGenericEntityService"/>. Strict: a call the allocator is not
    /// meant to make (an update, a retrieve) fails the test.
    /// </summary>
    private sealed class SubmissionStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, Entity> _rows = [];
        private int _createCalls;
        private int _sequenceReads;

        public SubmissionStore()
        {
            Entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns(async (QueryExpression query, CancellationToken _) =>
                {
                    // A lookup by the submission id is the idempotency read; one on the sequence is the highest-sequence read; one on
                    // the tracking number is the held-number check (task 094).
                    var byId = !query.Criteria.Conditions.Any(c => c.AttributeName == "sprk_submissionsequence");
                    if (!byId)
                    {
                        if (BeforeSequenceRead is { } gate)
                        {
                            await gate().ConfigureAwait(false);
                        }
                    }

                    await Task.Yield();
                    EntityCollection answer;
                    lock (_gate)
                    {
                        if (!byId)
                        {
                            _sequenceReads++;
                        }

                        answer = Run(query);
                    }

                    if (!byId && AfterSequenceRead is { } after)
                    {
                        await after().ConfigureAwait(false);
                    }

                    return answer;
                });

            Entities
                .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .Returns(async (Entity entity, CancellationToken _) =>
                {
                    await Task.Yield();
                    lock (_gate)
                    {
                        var call = ++_createCalls;
                        BeforeCreate?.Invoke(call, entity);
                        if (CreateFault is not null)
                        {
                            throw CreateFault;
                        }

                        entity.LogicalName.Should().Be("sprk_invoicesubmission");
                        var id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
                        if (_rows.ContainsKey(id))
                        {
                            // What DataverseServiceClientImpl.CreateAsync raises for a taken id: checked before the alternate key (measured).
                            throw new InvalidOperationException(
                                "Failed to create sprk_invoicesubmission record: Cannot insert duplicate key.",
                                new FaultException<OrganizationServiceFault>(
                                    new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040237) }, new FaultReason("Cannot insert duplicate key.")));
                        }

                        var scope = entity.GetAttributeValue<string>("sprk_trackingscope");
                        if (ScopeRequired && string.IsNullOrEmpty(scope))
                        {
                            // Task 095c: the scope column at SystemRequired (schema script step 5), as the SDK client raises it (measured, notes/027).
                            throw new InvalidOperationException(
                                "Failed to create sprk_invoicesubmission record: Attribute 'sprk_trackingscope' cannot be NULL.",
                                new FaultException<OrganizationServiceFault>(
                                    new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040200) },
                                    new FaultReason("Attribute 'sprk_trackingscope' cannot be NULL")));
                        }

                        // Each key covers only rows whose key columns all hold a value (notes/095c section 2.3): a null firm is not a value.
                        var firm = entity.GetAttributeValue<EntityReference>("sprk_vendororg")?.Id;
                        var tracking = entity.GetAttributeValue<string>("sprk_trackingnumber");
                        bool SameNumber(Entity other) =>
                            string.Equals(other.GetAttributeValue<string>("sprk_trackingnumber"), tracking, StringComparison.OrdinalIgnoreCase);
                        var firmClash = FirmKeyActive && firm is not null && _rows.Values.Any(other =>
                            other.GetAttributeValue<EntityReference>("sprk_vendororg")?.Id == firm && SameNumber(other));
                        var scopeClash = ScopeKeyActive && !string.IsNullOrEmpty(scope) && _rows.Values.Any(other =>
                            string.Equals(other.GetAttributeValue<string>("sprk_trackingscope"), scope, StringComparison.OrdinalIgnoreCase)
                            && SameNumber(other));
                        if (firmClash || scopeClash)
                        {
                            // The key violation as the SDK client raises it: the SDK fault, wrapped.
                            throw new InvalidOperationException(
                                "Failed to create sprk_invoicesubmission record: Entity Key Firm and Tracking Number violated.",
                                new FaultException<OrganizationServiceFault>(
                                    new OrganizationServiceFault { ErrorCode = unchecked((int)0x80060892) },
                                    new FaultReason("A record with the same value for Tracking Number, Firm already exists.")));
                        }

                        _rows[id] = Clone(entity, id);
                        return id;
                    }
                });
        }

        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);

        /// <summary>Runs with the table locked just before a create is checked against the keys (another request committing first).</summary>
        public Action<int, Entity>? BeforeCreate { get; set; }

        /// <summary>Awaited at the start of every highest-sequence read.</summary>
        public Func<Task>? BeforeSequenceRead { get; set; }

        /// <summary>Awaited after every highest-sequence read has been answered (the answer is already taken): holds a caller with a stale number.</summary>
        public Func<Task>? AfterSequenceRead { get; set; }

        /// <summary>When set, every create raises it.</summary>
        public Exception? CreateFault { get; set; }

        /// <summary>The firm key on (firm, tracking number) (task 020); retired by the schema script's last step.</summary>
        public bool FirmKeyActive { get; set; } = true;

        /// <summary>Task 095c: the scope key on (tracking number, scope), Active from P2.</summary>
        public bool ScopeKeyActive { get; set; }

        /// <summary>Task 095c: the scope column at SystemRequired (schema script step 5): a create without it is refused whole.</summary>
        public bool ScopeRequired { get; set; }

        public int CreateCalls => Volatile.Read(ref _createCalls);

        public int SequenceReads => Volatile.Read(ref _sequenceReads);

        public int RowCount
        {
            get
            {
                lock (_gate)
                {
                    return _rows.Count;
                }
            }
        }

        public bool Has(Guid id)
        {
            lock (_gate)
            {
                return _rows.ContainsKey(id);
            }
        }

        /// <summary>A submission that already holds <c>{number}.{sequence}</c>.</summary>
        public Guid AddNumbered(Guid firmId, string firmInvoiceNumber, int sequence, int stateCode = 0, Guid? id = null) =>
            Add(firmId, scope: null, firmInvoiceNumber, sequence, stateCode, id);

        /// <summary>Task 095c: a numbered submission as the schema script leaves it after the backfill: its scope is its firm's id, or
        /// <c>none</c> with no firm.</summary>
        public Guid AddScoped(Guid? firmId, string firmInvoiceNumber, int sequence, Guid? id = null) =>
            Add(firmId, firmId?.ToString("D") ?? "none", firmInvoiceNumber, sequence, 0, id);

        /// <summary>Task 095c: a numbered submission with an explicit scope, whatever its firm (e.g. a firm filed in the <c>none</c> scope).</summary>
        public Guid AddWithScope(Guid? firmId, string scope, string firmInvoiceNumber, int sequence) =>
            Add(firmId, scope, firmInvoiceNumber, sequence, 0, null);

        private Guid Add(Guid? firmId, string? scope, string firmInvoiceNumber, int sequence, int stateCode, Guid? id)
        {
            lock (_gate)
            {
                var row = new Entity("sprk_invoicesubmission", id ?? Guid.NewGuid())
                {
                    ["statecode"] = new OptionSetValue(stateCode),
                    ["sprk_firminvoicenumber"] = firmInvoiceNumber,
                    ["sprk_submissionsequence"] = sequence,
                    ["sprk_trackingnumber"] = $"{firmInvoiceNumber}.{sequence}",
                };
                if (firmId is { } firm)
                {
                    row["sprk_vendororg"] = new EntityReference("sprk_organization", firm);
                }

                if (scope is not null)
                {
                    row["sprk_trackingscope"] = scope;
                }

                _rows[row.Id] = row;
                return row.Id;
            }
        }

        /// <summary>Task 094: a submission whose commit was UNDONE: it keeps <c>{number}.{sequence}</c> and its sequence, but the undo cleared
        /// its firm invoice number (SubmissionCommitGate), so the highest-sequence read does not see it.</summary>
        public Guid AddUndone(Guid firmId, string firmInvoiceNumber, int sequence)
        {
            var id = AddNumbered(firmId, firmInvoiceNumber, sequence);
            lock (_gate)
            {
                _rows[id].Attributes.Remove("sprk_firminvoicenumber");
            }

            return id;
        }

        /// <summary>A submission that exists with a firm and a tracking number but was never numbered by the allocator (no sequence).</summary>
        public Guid AddUnnumbered(Guid firmId)
        {
            lock (_gate)
            {
                var row = new Entity("sprk_invoicesubmission", Guid.NewGuid())
                {
                    ["sprk_vendororg"] = new EntityReference("sprk_organization", firmId),
                    ["sprk_trackingnumber"] = "TYPED-BY-HAND",
                };
                _rows[row.Id] = row;
                return row.Id;
            }
        }

        public Entity Row(Guid id)
        {
            lock (_gate)
            {
                return Clone(_rows[id], id);
            }
        }

        private static Entity Clone(Entity source, Guid id)
        {
            var copy = new Entity(source.LogicalName, id);
            foreach (var attribute in source.Attributes)
            {
                copy[attribute.Key] = attribute.Value;
            }

            return copy;
        }

        /// <summary>Evaluates the allocator's queries: AND of Equal / NotNull conditions, one descending order, TopCount. Anything else fails loudly.</summary>
        private EntityCollection Run(QueryExpression query)
        {
            query.EntityName.Should().Be("sprk_invoicesubmission");
            query.Criteria.Filters.Should().BeEmpty();
            query.Criteria.FilterOperator.Should().Be(LogicalOperator.And);
            query.LinkEntities.Should().BeEmpty();

            IEnumerable<Entity> rows = _rows.Values;
            foreach (var condition in query.Criteria.Conditions)
            {
                rows = rows.Where(row => Matches(row, condition));
            }

            foreach (var order in query.Orders.Reverse())
            {
                order.OrderType.Should().Be(OrderType.Descending);
                rows = rows.OrderByDescending(row => row.GetAttributeValue<int?>(order.AttributeName) ?? int.MinValue);
            }

            if (query.TopCount is { } top)
            {
                rows = rows.Take(top);
            }

            var found = new EntityCollection();
            foreach (var row in rows)
            {
                found.Entities.Add(Clone(row, row.Id));
            }

            return found;
        }

        private static bool Matches(Entity row, ConditionExpression condition)
        {
            if (condition.AttributeName == "sprk_invoicesubmissionid")
            {
                // The primary key is the row's Id, not an attribute.
                return condition.Operator == ConditionOperator.Equal
                    ? row.Id == (Guid)condition.Values.Single()
                    : throw new NotSupportedException($"The in-memory table does not model {condition.Operator} on the primary key.");
            }

            var has = row.Contains(condition.AttributeName) && row[condition.AttributeName] is not null;
            return condition.Operator switch
            {
                ConditionOperator.NotNull => has,
                ConditionOperator.Equal => has && ValueEquals(row[condition.AttributeName], condition.Values.Single()),
                _ => throw new NotSupportedException($"The in-memory table does not model {condition.Operator}."),
            };
        }

        private static bool ValueEquals(object actual, object expected) => actual switch
        {
            EntityReference reference => expected is Guid id && reference.Id == id,
            string text => expected is string other && string.Equals(text, other, StringComparison.OrdinalIgnoreCase),
            OptionSetValue option => expected is int value && option.Value == value,
            _ => Equals(actual, expected),
        };
    }
}
