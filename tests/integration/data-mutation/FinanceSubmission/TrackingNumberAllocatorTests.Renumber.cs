using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// ebilling-r1 task 095d-1a: <see cref="TrackingNumberAllocator.RenumberSubmissionAsync"/>, the renumber of a no-firm submission into a firm's
/// scope (D-37; notes/095c-from-email-firm-optional.md section 16.6).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, data-mutation KEEP path: optimistic concurrency). It decides whether the set-firm apply can give a submission a
/// number another submission of that firm holds, whether a lost race lands part of the apply, and whether a stale repeat allocates twice. The
/// in-memory table (<c>SubmissionStore.Transact</c>) checks a submission update the way Dataverse does on update: the row version, then the
/// scope key and the firm key over every other row, refusing the transaction whole.
/// </remarks>
public sealed partial class TrackingNumberAllocatorTests
{
    private const string Number = "INV-7";

    /// <summary>A no-firm submission in P2, as the from-email intake left it: <c>INV-7.1</c> in the <c>none</c> scope, at version 1.</summary>
    private Guid HeldNoFirmSubmission()
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        return _store.AddScoped(null, Number, 1);
    }

    /// <summary>A caller apply naming the candidate: the findings on the submission, and one invoice update carrying the number.</summary>
    private static RenumberApply ApplyFor(RenumberCandidate candidate, Guid invoiceId) => new(
        new Dictionary<string, object> { ["sprk_findingsjson"] = $"{{\"trackingNumber\":\"{candidate.TrackingNumber}\"}}" },
        [new UpdateRequest { Target = new Entity("sprk_invoice", invoiceId) { ["sprk_trackingnumber"] = candidate.TrackingNumber } }]);

    private static List<OrganizationRequest> Requests(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => (OrganizationRequest)new UpdateRequest { Target = new Entity("sprk_invoiceline", Guid.NewGuid()) { ["sprk_name"] = "x" } })
            .ToList();

    [Fact(DisplayName = "095d-1a: the renumber gives the firm's next n in the firm's scope, in ONE transaction with the caller's apply, at the read version")]
    public async Task Renumber_GivesTheFirmsNextNumber_InOneTransactionWithTheCallersApply()
    {
        var submission = HeldNoFirmSubmission();
        _store.AddScoped(FirmA, Number, 1);
        _store.AddScoped(FirmA, Number, 2);
        _store.AddScoped(FirmB, Number, 9); // another firm's scope never counts
        var invoice = Guid.NewGuid();
        var candidates = new List<RenumberCandidate>();

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, "  INV-7 ", c =>
        {
            candidates.Add(c);
            return ApplyFor(c, invoice);
        });

        result.Should().Be(new RenumberResult(RenumberOutcome.Renumbered, "INV-7.3", 3, null));
        candidates.Should().Equal(new RenumberCandidate("INV-7.3", 3));
        _store.TransactionCalls.Should().Be(1);
        var sent = _store.Transactions.Single();
        sent.Should().HaveCount(2, "the submission update, then the caller's request");
        var update = sent[0].Should().BeOfType<UpdateRequest>().Subject;
        update.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        update.Target.RowVersion.Should().Be("1", "conditioned on the caller's read");
        update.Target.Attributes.Keys.Should().NotContain("sprk_firminvoicenumber", "the firm invoice number does not change");

        var row = _store.Row(submission);
        row.GetAttributeValue<EntityReference>("sprk_vendororg").Should().BeEquivalentTo(new EntityReference("sprk_organization", FirmA));
        row.GetAttributeValue<string>("sprk_trackingscope").Should().Be(FirmA.ToString("D"));
        row.GetAttributeValue<int?>("sprk_submissionsequence").Should().Be(3);
        row.GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-7.3");
        row.GetAttributeValue<string>("sprk_firminvoicenumber").Should().Be(Number);
        row.GetAttributeValue<string>("sprk_findingsjson").Should().Be("{\"trackingNumber\":\"INV-7.3\"}");
        _store.Applied.Should().ContainSingle().Which.Should().BeSameAs(sent[1]);
    }

    [Fact(DisplayName = "095d-1a: a firm with no submission of that number gets .1, though the no-firm row held .1 in the none scope")]
    public async Task Renumber_IntoAFirmWithNoSuchNumber_GetsDotOne()
    {
        var submission = HeldNoFirmSubmission();

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        result.TrackingNumber.Should().Be("INV-7.1");
        _store.Row(submission).GetAttributeValue<string>("sprk_trackingscope").Should().Be(FirmA.ToString("D"));
    }

    [Fact(DisplayName = "095d-1a (must fire): a lost race on the scope key rolls the whole apply back, re-reads and retries with the next n - no duplicate")]
    public async Task Renumber_ALostRaceOnTheScopeKey_RetriesWithTheNextNumber_AndAppliesOnce()
    {
        var submission = HeldNoFirmSubmission();
        Guid? competitor = null;
        // An intake for FirmA commits INV-7.1 between this call's read and its transaction.
        _store.BeforeTransaction = (call, _) =>
        {
            if (call == 1)
            {
                competitor = _store.AddScoped(FirmA, Number, 1);
            }
        };
        var candidates = new List<RenumberCandidate>();

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c =>
        {
            candidates.Add(c);
            return ApplyFor(c, Guid.NewGuid());
        });

        result.Outcome.Should().Be(RenumberOutcome.Renumbered);
        result.TrackingNumber.Should().Be("INV-7.2");
        candidates.Select(c => c.TrackingNumber).Should().Equal("INV-7.1", "INV-7.2");
        _store.TransactionCalls.Should().Be(2);
        _store.SequenceReads.Should().Be(2, "the retry re-reads the highest sequence");
        _store.Applied.Should().ContainSingle("the lost attempt's apply rolled back with it")
            .Which.Should().BeSameAs(_store.Transactions[1][1]);
        _store.Row(competitor!.Value).GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-7.1", "the winner keeps its number");
        _store.Row(submission).GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-7.2");
    }

    [Theory(DisplayName = "095d-1a: the retry is bounded at MaxAttempts; exhausted, it answers Unavailable fail-closed and writes nothing")]
    [InlineData(TrackingNumberAllocator.MaxAttempts - 1, true)]
    [InlineData(TrackingNumberAllocator.MaxAttempts, false)]
    public async Task Renumber_TheRetryIsBounded(int lostRaces, bool expectRenumbered)
    {
        var submission = HeldNoFirmSubmission();
        // Every attempt up to lostRaces loses: a competitor commits exactly the number this attempt is about to write.
        _store.BeforeTransaction = (call, requests) =>
        {
            if (call <= lostRaces)
            {
                _store.AddScoped(FirmA, Number, ((UpdateRequest)requests[0]).Target.GetAttributeValue<int>("sprk_submissionsequence"));
            }
        };

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        if (expectRenumbered)
        {
            result.Outcome.Should().Be(RenumberOutcome.Renumbered);
            result.Sequence.Should().Be(TrackingNumberAllocator.MaxAttempts);
            _store.Applied.Should().ContainSingle();
        }
        else
        {
            result.Should().Be(new RenumberResult(RenumberOutcome.Refused, null, null, TrackingNumberReasons.Unavailable));
            _store.TransactionCalls.Should().Be(TrackingNumberAllocator.MaxAttempts);
            _store.Applied.Should().BeEmpty();
            var row = _store.Row(submission);
            row.GetAttributeValue<string>("sprk_trackingscope").Should().Be("none", "nothing was written");
            row.GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-7.1");
            row.RowVersion.Should().Be("1");
        }
    }

    [Fact(DisplayName = "095d-1a (F4, must not fire): a stale version is a Conflict at the precondition read: nothing is read for a number, nothing is sent")]
    public async Task Renumber_AStaleVersion_IsAConflictAtThePreconditionRead_NothingIsSent()
    {
        var submission = HeldNoFirmSubmission();
        _store.Touch(submission); // another write since the caller read version 1
        var built = false;

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c =>
        {
            built = true;
            return ApplyFor(c, Guid.NewGuid());
        });

        result.Should().Be(new RenumberResult(RenumberOutcome.Conflict, null, null, null));
        _store.IdReads.Should().Be(1, "the precondition read decided it");
        _store.TransactionCalls.Should().Be(0, "nothing is sent");
        _store.SequenceReads.Should().Be(0, "no number is read for a row that changed");
        built.Should().BeFalse();
        _store.Applied.Should().BeEmpty();
        _store.Row(submission).GetAttributeValue<string>("sprk_trackingscope").Should().Be("none");
    }

    [Fact(DisplayName = "095d-1a (must not fire): a change between the precondition read and the write is the transaction's Conflict, never retried as a race")]
    public async Task Renumber_AChangeAfterThePreconditionRead_IsTheTransactionsConflict_NotARetry()
    {
        var submission = HeldNoFirmSubmission();
        // Another write lands after the allocator read the row at version 1 and before its conditional update is checked.
        _store.BeforeTransaction = (call, _) =>
        {
            if (call == 1)
            {
                _store.Touch(submission);
            }
        };

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        result.Should().Be(new RenumberResult(RenumberOutcome.Conflict, null, null, null));
        _store.TransactionCalls.Should().Be(1, "a conflict is not a race: no second attempt, no second number");
        _store.SequenceReads.Should().Be(1);
        _store.Applied.Should().BeEmpty();
        _store.Row(submission).GetAttributeValue<string>("sprk_trackingscope").Should().Be("none");
    }

    [Fact(DisplayName = "095d-1a: a submission that no longer exists is a Conflict, nothing is sent")]
    public async Task Renumber_AMissingSubmission_IsAConflict()
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);

        var result = await CreateService().RenumberSubmissionAsync(Guid.NewGuid(), "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        result.Outcome.Should().Be(RenumberOutcome.Conflict);
        _store.TransactionCalls.Should().Be(0);
        _store.SequenceReads.Should().Be(0);
    }

    [Fact(DisplayName = "095d-1a (must not fire): a repeat at the version the first renumber replaced is a Conflict, never a second number")]
    public async Task Renumber_ARepeatAtTheOldVersion_IsAConflict_AndKeepsTheFirstNumber()
    {
        var submission = HeldNoFirmSubmission();
        var service = CreateService();
        (await service.RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()))).TrackingNumber.Should().Be("INV-7.1");

        var repeat = await service.RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        repeat.Outcome.Should().Be(RenumberOutcome.Conflict);
        _store.TransactionCalls.Should().Be(1, "the repeat sent nothing");
        _store.Row(submission).GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-7.1");
        _store.Applied.Should().ContainSingle();
    }

    [Fact(DisplayName = "095d-1a: a row-version conflict on a caller request (the invoice) is a Conflict too, not a retry")]
    public async Task Renumber_ARowVersionConflictOnACallerRequest_IsAConflict()
    {
        var submission = HeldNoFirmSubmission();
        _store.CallerRequestFault = DataverseServiceClientImpl.ConcurrencyVersionMismatch;

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        result.Outcome.Should().Be(RenumberOutcome.Conflict);
        _store.TransactionCalls.Should().Be(1);
        _store.Row(submission).GetAttributeValue<string>("sprk_trackingscope").Should().Be("none", "the submission update rolled back with it");
    }

    [Fact(DisplayName = "095d-1a (must fire): {number}.{n} held in the firm scope by an undone attempt is skipped to the next free n, as at create")]
    public async Task Renumber_ANumberHeldByAnUndoneAttemptInTheFirmScope_IsSkipped()
    {
        var submission = HeldNoFirmSubmission();
        _store.AddScoped(FirmA, Number, 1);
        _store.AddScopedUndone(FirmA, Number, 2); // the sequence read cannot see it; the key refuses .2

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        result.TrackingNumber.Should().Be("INV-7.3");
        _store.TransactionCalls.Should().Be(2);
        _store.Applied.Should().ContainSingle();
    }

    [Fact(DisplayName = "095d-1a (must not fire): undone attempts beyond MaxHiddenSkips refuse Unavailable, writing nothing")]
    public async Task Renumber_UndoneAttemptsBeyondTheSkipBound_AreRefused()
    {
        var submission = HeldNoFirmSubmission();
        for (var n = 1; n <= TrackingNumberAllocator.MaxHiddenSkips + 1; n++)
        {
            _store.AddScopedUndone(FirmA, Number, n);
        }

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        result.Reason.Should().Be(TrackingNumberReasons.Unavailable);
        _store.Applied.Should().BeEmpty();
        _store.Row(submission).GetAttributeValue<string>("sprk_trackingscope").Should().Be("none");
    }

    [Fact(DisplayName = "095d-1a: the ceiling is one transaction (InvoiceSubmissionPersister.MaxRequestsPerTransaction) less the submission update")]
    public void TheRenumberCeiling_IsTheTransactionBoundLessTheSubmissionUpdate()
    {
        TrackingNumberAllocator.MaxRenumberCallerRequests.Should().Be(InvoiceSubmissionPersister.MaxRequestsPerTransaction - 1);
        TrackingNumberAllocator.FitsRenumberTransaction(TrackingNumberAllocator.MaxRenumberCallerRequests).Should().BeTrue("at the ceiling");
        TrackingNumberAllocator.FitsRenumberTransaction(TrackingNumberAllocator.MaxRenumberCallerRequests + 1).Should().BeFalse("one above it");
        TrackingNumberAllocator.FitsRenumberTransaction(-1).Should().BeFalse();
    }

    [Theory(DisplayName = "095d-1a: at the ceiling the apply runs as one transaction of 1000; one above it is refused before anything is sent")]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task Renumber_AtTheCeilingRuns_OneAboveIsRefusedBeforeTheApply(int overCeiling, bool expectRenumbered)
    {
        var submission = HeldNoFirmSubmission();
        var requests = Requests(TrackingNumberAllocator.MaxRenumberCallerRequests + overCeiling);

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, _ => new RenumberApply(null, requests));

        if (expectRenumbered)
        {
            result.Outcome.Should().Be(RenumberOutcome.Renumbered);
            _store.Transactions.Single().Should().HaveCount(InvoiceSubmissionPersister.MaxRequestsPerTransaction);
        }
        else
        {
            result.Should().Be(new RenumberResult(RenumberOutcome.Refused, null, null, TrackingNumberReasons.TooManyRequests));
            _store.TransactionCalls.Should().Be(0, "refused before the apply");
            _store.Row(submission).GetAttributeValue<string>("sprk_trackingscope").Should().Be("none");
        }
    }

    [Theory(DisplayName = "095d-1a: before P2 a renumber is refused ScopeKeyInactive, reading and writing no row")]
    [InlineData(TrackingScopePhase.P0NoScopeColumn)]
    [InlineData(TrackingScopePhase.P1ScopeColumn)]
    public async Task Renumber_BeforeP2_IsRefused_AndNothingIsReadOrWritten(TrackingScopePhase phase)
    {
        var submission = _store.AddScoped(null, Number, 1);
        InPhase(phase);
        var built = false;

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c =>
        {
            built = true;
            return ApplyFor(c, Guid.NewGuid());
        });

        result.Should().Be(new RenumberResult(RenumberOutcome.Refused, null, null, TrackingNumberReasons.ScopeKeyInactive));
        built.Should().BeFalse();
        _store.IdReads.Should().Be(0, "no row is read before P2");
        _store.SequenceReads.Should().Be(0);
        _store.TransactionCalls.Should().Be(0);
    }

    [Fact(DisplayName = "095d-1a: a key fault on a caller request is not a race for this number: it propagates on the first attempt")]
    public async Task Renumber_AKeyFaultOnACallerRequest_Propagates_WithoutARetry()
    {
        var submission = HeldNoFirmSubmission();
        _store.CallerRequestFault = unchecked((int)0x80060892);

        var act = () => CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        (await act.Should().ThrowAsync<DataverseTransactionException>()).Which.FailedRequestIndex.Should().Be(1);
        _store.TransactionCalls.Should().Be(1);
    }

    [Fact(DisplayName = "095d-1a: any other fault propagates on the first attempt")]
    public async Task Renumber_AnotherFault_Propagates_WithoutARetry()
    {
        var submission = HeldNoFirmSubmission();
        _store.CallerRequestFault = unchecked((int)0x80040220); // access denied

        var act = () => CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));

        await act.Should().ThrowAsync<DataverseTransactionException>();
        _store.TransactionCalls.Should().Be(1);
        _store.Applied.Should().BeEmpty();
    }

    [Theory(DisplayName = "095d-1a: the caller cannot write a column the allocator owns, nor any submission row: refused before anything is sent")]
    [InlineData("sprk_trackingnumber", false)]
    [InlineData("sprk_trackingscope", false)]
    [InlineData("sprk_vendororg", false)]
    [InlineData("sprk_submissionsequence", false)]
    [InlineData("sprk_firminvoicenumber", false)]
    [InlineData("sprk_invoicesubmissionid", false)]
    [InlineData(null, true)]
    public async Task Renumber_TheCallerCannotWriteTheNumberColumnsOrASubmission(string? ownedColumn, bool submissionRequest)
    {
        var submission = HeldNoFirmSubmission();
        RenumberApply Build(RenumberCandidate c) => new(
            ownedColumn is null ? null : new Dictionary<string, object> { [ownedColumn] = "x" },
            submissionRequest
                ? [new UpdateRequest { Target = new Entity("sprk_invoicesubmission", Guid.NewGuid()) { ["sprk_name"] = "x" } }]
                : []);

        var act = () => CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, Build);

        await act.Should().ThrowAsync<ArgumentException>();
        _store.TransactionCalls.Should().Be(0);
    }

    [Theory(DisplayName = "095d-1a: an empty id, an empty firm, a blank version or firm invoice number, or no builder is refused before any read")]
    [InlineData(true, false, "1", Number, false)]
    [InlineData(false, true, "1", Number, false)]
    [InlineData(false, false, " ", Number, false)]
    [InlineData(false, false, "1", " ", false)]
    [InlineData(false, false, "1", Number, true)]
    public async Task Renumber_BadArguments_AreRefusedBeforeAnyRead(
        bool emptySubmission, bool emptyFirm, string version, string firmInvoiceNumber, bool noBuilder)
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);

        var act = () => CreateService().RenumberSubmissionAsync(
            emptySubmission ? Guid.Empty : Guid.NewGuid(),
            version,
            emptyFirm ? Guid.Empty : FirmA,
            firmInvoiceNumber,
            noBuilder ? null! : c => ApplyFor(c, Guid.NewGuid()));

        await act.Should().ThrowAsync<ArgumentException>();
        _store.IdReads.Should().Be(0);
        _store.SequenceReads.Should().Be(0);
        _store.TransactionCalls.Should().Be(0);
        _phaseReads.Should().Be(0);
    }

    // ── Review F4: the precondition read ─────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "095d-1a (F4, must fire): a repeat at the version the commit left returns the held number, sends nothing and allocates no second number")]
    public async Task Renumber_ARepeatAtThePostCommitVersion_ReturnsTheHeldNumber_SendsNothing_AndAllocatesNoSecondNumber()
    {
        var submission = HeldNoFirmSubmission();
        var service = CreateService();
        var first = await service.RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()));
        first.Should().Be(new RenumberResult(RenumberOutcome.Renumbered, "INV-7.1", 1, null));
        var postCommitVersion = _store.Row(submission).RowVersion;
        postCommitVersion.Should().NotBe("1", "the commit moved the version on");
        _store.AddScoped(FirmA, Number, 2); // a later arrival of the firm: the next number would now be .3
        var transactions = _store.TransactionCalls;
        var sequenceReads = _store.SequenceReads;
        var built = false;

        var repeat = await service.RenumberSubmissionAsync(submission, postCommitVersion, FirmA, Number, c =>
        {
            built = true;
            return ApplyFor(c, Guid.NewGuid());
        });

        repeat.Should().Be(new RenumberResult(RenumberOutcome.AlreadyRenumbered, "INV-7.1", 1, null), "the held number, not a second one");
        built.Should().BeFalse("nothing is built for an apply that is not sent");
        _store.TransactionCalls.Should().Be(transactions, "nothing is sent");
        _store.SequenceReads.Should().Be(sequenceReads, "no number is read");
        _store.Applied.Should().ContainSingle("the first commit's apply stands, and the repeat applied nothing");
        var row = _store.Row(submission);
        row.GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-7.1");
        row.GetAttributeValue<int?>("sprk_submissionsequence").Should().Be(1);
        row.RowVersion.Should().Be(postCommitVersion, "the repeat wrote nothing");
    }

    [Fact(DisplayName = "095d-1a (F4): a repeat at the post-commit version that names another firm is refused NotUnnumbered, nothing sent")]
    public async Task Renumber_ARepeatAtThePostCommitVersionNamingAnotherFirm_IsRefused()
    {
        var submission = HeldNoFirmSubmission();
        var service = CreateService();
        (await service.RenumberSubmissionAsync(submission, "1", FirmA, Number, c => ApplyFor(c, Guid.NewGuid()))).TrackingNumber.Should().Be("INV-7.1");
        var postCommitVersion = _store.Row(submission).RowVersion;
        var transactions = _store.TransactionCalls;

        var repeat = await service.RenumberSubmissionAsync(submission, postCommitVersion, FirmB, Number, c => ApplyFor(c, Guid.NewGuid()));

        repeat.Should().Be(new RenumberResult(RenumberOutcome.Refused, null, null, TrackingNumberReasons.NotUnnumbered));
        _store.TransactionCalls.Should().Be(transactions, "nothing is sent");
        var row = _store.Row(submission);
        row.GetAttributeValue<EntityReference>("sprk_vendororg").Id.Should().Be(FirmA, "the firm it was renumbered into stands");
        row.GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-7.1");
        _store.Applied.Should().ContainSingle();
    }

    [Theory(DisplayName = "095d-1a (F4): a submission that is not a no-firm row in the none scope is refused NotUnnumbered, reading no number and sending nothing")]
    [InlineData("another firm's row")]
    [InlineData("this firm, filed in the none scope")]
    [InlineData("no firm, another firm's scope")]
    [InlineData("this firm and scope, no sequence held")]
    public async Task Renumber_ASubmissionThatIsNotUnnumbered_IsRefused(string state)
    {
        InPhase(TrackingScopePhase.P2ScopeKeyActive);
        var submission = state switch
        {
            "another firm's row" => _store.AddScoped(FirmB, Number, 1),
            "this firm, filed in the none scope" => _store.AddWithScope(FirmA, "none", Number, 1),
            "no firm, another firm's scope" => _store.AddWithScope(null, FirmB.ToString("D"), Number, 1),
            _ => _store.WithoutColumn(_store.AddScoped(FirmA, Number, 1), "sprk_submissionsequence"),
        };
        var before = _store.Row(submission);
        var built = false;

        var result = await CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c =>
        {
            built = true;
            return ApplyFor(c, Guid.NewGuid());
        });

        result.Should().Be(new RenumberResult(RenumberOutcome.Refused, null, null, TrackingNumberReasons.NotUnnumbered));
        built.Should().BeFalse();
        _store.TransactionCalls.Should().Be(0);
        _store.SequenceReads.Should().Be(0);
        var after = _store.Row(submission);
        after.Attributes.Should().BeEquivalentTo(before.Attributes, "the row is as it was");
        after.RowVersion.Should().Be(before.RowVersion);
    }

    [Theory(DisplayName = "095d-1a (F4): a firm invoice number that is not the submission's own throws ArgumentException before any number is read or anything is sent")]
    [InlineData("INV-8")]
    [InlineData("inv-7")]
    public async Task Renumber_AFirmInvoiceNumberThatIsNotTheRows_Throws(string firmInvoiceNumber)
    {
        var submission = HeldNoFirmSubmission();
        var before = _store.Row(submission);

        var act = () => CreateService().RenumberSubmissionAsync(submission, "1", FirmA, firmInvoiceNumber, c => ApplyFor(c, Guid.NewGuid()));

        await act.Should().ThrowAsync<ArgumentException>();
        _store.TransactionCalls.Should().Be(0);
        _store.SequenceReads.Should().Be(0);
        _store.Row(submission).Attributes.Should().BeEquivalentTo(before.Attributes);
    }

    // ── Review K1, K2: which key faults are a race ───────────────────────────────────────────────────────────

    [Theory(DisplayName = "095d-1a (K1, K2): a key duplicate is a lost race only on the submission update (0x80060892 at index 0 or none, 0x80040237 at index 0); anywhere else it propagates")]
    [InlineData(unchecked((int)0x80060892), null, true)]
    [InlineData(unchecked((int)0x80040237), 0, true)]
    [InlineData(unchecked((int)0x80040237), 1, false)]
    [InlineData(unchecked((int)0x80040237), null, false)]
    public async Task Renumber_AKeyDuplicate_IsARaceOnlyOnTheSubmissionUpdate(int code, int? faultedIndex, bool expectRace)
    {
        var submission = HeldNoFirmSubmission();
        _store.BeforeTransaction = (call, requests) =>
        {
            if (call == 1)
            {
                throw SubmissionStore.TransactionFault(requests, faultedIndex, code, "Cannot insert duplicate key.");
            }
        };
        var candidates = new List<RenumberCandidate>();
        var act = () => CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, c =>
        {
            candidates.Add(c);
            return ApplyFor(c, Guid.NewGuid());
        });

        if (expectRace)
        {
            var result = await act();

            result.Outcome.Should().Be(RenumberOutcome.Renumbered);
            candidates.Select(c => c.TrackingNumber).Should().Equal("INV-7.1", "INV-7.2");
            _store.TransactionCalls.Should().Be(2, "the refused transaction is retried once, with the next n");
            _store.Applied.Should().ContainSingle("the refused attempt applied nothing");
        }
        else
        {
            (await act.Should().ThrowAsync<DataverseTransactionException>()).Which.FailedRequestIndex.Should().Be(faultedIndex);
            candidates.Should().ContainSingle("no retry");
            _store.TransactionCalls.Should().Be(1);
            _store.Applied.Should().BeEmpty();
            _store.Row(submission).GetAttributeValue<string>("sprk_trackingscope").Should().Be("none");
        }
    }

    // ── Review K3: the caller's requests are an allowlist ────────────────────────────────────────────────────

    private static OrganizationRequest CallerRequest(string kind, Guid id) => kind switch
    {
        "update, another table" => new UpdateRequest { Target = new Entity("sprk_invoice", id) { ["sprk_name"] = "x" } },
        "create, another table" => new CreateRequest { Target = new Entity("sprk_invoiceline", id) { ["sprk_name"] = "x" } },
        "create, the submission table" => new CreateRequest { Target = new Entity("sprk_invoicesubmission", id) { ["sprk_name"] = "x" } },
        "update, no target" => new UpdateRequest(),
        "delete" => new DeleteRequest { Target = new EntityReference("sprk_invoiceline", id) },
        "associate" => new AssociateRequest
        {
            Target = new EntityReference("sprk_invoice", id),
            Relationship = new Relationship("sprk_invoice_sprk_invoiceline"),
            RelatedEntities = new EntityReferenceCollection { new EntityReference("sprk_invoiceline", Guid.NewGuid()) },
        },
        "set state of the submission" => new Microsoft.Crm.Sdk.Messages.SetStateRequest
        {
            EntityMoniker = new EntityReference("sprk_invoicesubmission", id),
            State = new OptionSetValue(1),
            Status = new OptionSetValue(2),
        },
        "unknown request" => new OrganizationRequest("sprk_Anything"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    [Theory(DisplayName = "095d-1a (K3): only an UpdateRequest or CreateRequest on another table is accepted; any other request type, or a create of a submission, is refused before anything is sent")]
    [InlineData("update, another table", true)]
    [InlineData("create, another table", true)]
    [InlineData("create, the submission table", false)]
    [InlineData("update, no target", false)]
    [InlineData("delete", false)]
    [InlineData("associate", false)]
    [InlineData("set state of the submission", false)]
    [InlineData("unknown request", false)]
    public async Task Renumber_TheCallersRequests_AreAnAllowlist(string kind, bool accepted)
    {
        var submission = HeldNoFirmSubmission();
        var request = CallerRequest(kind, Guid.NewGuid());

        var act = () => CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, _ => new RenumberApply(null, [request]));

        if (accepted)
        {
            (await act()).Outcome.Should().Be(RenumberOutcome.Renumbered);
            _store.Applied.Should().ContainSingle().Which.Should().BeSameAs(request);
        }
        else
        {
            await act.Should().ThrowAsync<ArgumentException>();
            _store.TransactionCalls.Should().Be(0);
            _store.Row(submission).GetAttributeValue<string>("sprk_trackingscope").Should().Be("none");
        }
    }

    [Fact(DisplayName = "095d-1a (K3): a null request in the apply is refused before anything is sent")]
    public async Task Renumber_ANullRequest_IsRefused()
    {
        var submission = HeldNoFirmSubmission();

        var act = () => CreateService().RenumberSubmissionAsync(submission, "1", FirmA, Number, _ => new RenumberApply(null, [null!]));

        await act.Should().ThrowAsync<ArgumentException>();
        _store.TransactionCalls.Should().Be(0);
    }
}
