using System.Data;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// <see cref="InvoiceLifecycleService"/> (ebilling-r1 task 014, FR-06, I-6): the only writer of invoice statecode / statuscode.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the FR-06 matrix decides which money states are reachable (Paid is never reopened, a Rejected invoice is
/// never approved), and the conditional write decides whether two approvers can overwrite each other. The Dataverse boundary is a Moq
/// double of <see cref="IFieldMappingDataverseService"/> (ADR-038 bans mocking HttpMessageHandler).
/// </remarks>
public sealed class InvoiceLifecycleServiceTests
{
    private static readonly Guid InvoiceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<IFieldMappingDataverseService> _records = new();

    private InvoiceLifecycleService CreateService() =>
        new(_records.Object, new Mock<ILogger<InvoiceLifecycleService>>().Object);

    private static Dictionary<string, object?> Row(InvoiceLifecycleStatus status, long version) => new()
    {
        ["statuscode"] = (long)InvoiceStatusCodes.StatusCodeOf(status),
        ["versionnumber"] = version,
    };

    /// <summary>Queues the rows successive reads return (the last one repeats).</summary>
    private void ReadsReturn(params Dictionary<string, object?>[] rows)
    {
        var queue = new Queue<Dictionary<string, object?>>(rows);
        _records
            .Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => queue.Count > 1 ? queue.Dequeue() : queue.Peek());
    }

    private void VerifyNoWrite() =>
        _records.Verify(
            r => r.UpdateRecordFieldsIfUnchangedAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);

    public static IEnumerable<object[]> AllowedTransitions() =>
    [
        [InvoiceLifecycleStatus.InReview, InvoiceLifecycleStatus.Approved, 0],
        [InvoiceLifecycleStatus.InReview, InvoiceLifecycleStatus.Rejected, 1],
        [InvoiceLifecycleStatus.InReview, InvoiceLifecycleStatus.ReturnedToFirm, 1],
        [InvoiceLifecycleStatus.InReview, InvoiceLifecycleStatus.Cancelled, 1],
        [InvoiceLifecycleStatus.Approved, InvoiceLifecycleStatus.SentToAp, 0],
        [InvoiceLifecycleStatus.Approved, InvoiceLifecycleStatus.Cancelled, 1],
        [InvoiceLifecycleStatus.SentToAp, InvoiceLifecycleStatus.Paid, 1],
    ];

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public async Task AllowedTransition_WritesTargetStatusAndItsPairedState_ConditionalOnTheReadVersion(
        InvoiceLifecycleStatus from, InvoiceLifecycleStatus to, int expectedStateCode)
    {
        ReadsReturn(Row(from, version: 42));
        Dictionary<string, object?>? written = null;
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync(
                "sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 42, It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, long, CancellationToken>((_, _, fields, _, _) => written = fields)
            .Returns(Task.CompletedTask);

        var result = await CreateService().TransitionAsync(InvoiceId, to);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Transitioned);
        result.Succeeded.Should().BeTrue();
        result.Current.Should().Be(from);
        result.Requested.Should().Be(to);
        result.Reason.Should().BeNull();
        written.Should().NotBeNull();
        written!.Keys.Should().BeEquivalentTo("statuscode", "statecode");
        written["statuscode"].Should().Be(InvoiceStatusCodes.StatusCodeOf(to));
        written["statecode"].Should().Be(expectedStateCode);
    }

    public static IEnumerable<object[]> DisallowedTransitions() =>
    [
        [InvoiceLifecycleStatus.Paid, InvoiceLifecycleStatus.InReview],
        [InvoiceLifecycleStatus.Rejected, InvoiceLifecycleStatus.Approved],
        [InvoiceLifecycleStatus.InReview, InvoiceLifecycleStatus.Paid],
        [InvoiceLifecycleStatus.SentToAp, InvoiceLifecycleStatus.Approved],
        // Task 097a: Superseded is reached and left only through the transaction request builders, never TransitionAsync.
        [InvoiceLifecycleStatus.InReview, InvoiceLifecycleStatus.Superseded],
        [InvoiceLifecycleStatus.Superseded, InvoiceLifecycleStatus.InReview],
    ];

    [Theory]
    [MemberData(nameof(DisallowedTransitions))]
    public async Task DisallowedTransition_IsRefusedWithStableReason_AndNothingIsWritten(InvoiceLifecycleStatus from, InvoiceLifecycleStatus to)
    {
        ReadsReturn(Row(from, version: 7));

        var result = await CreateService().TransitionAsync(InvoiceId, to);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Refused);
        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be("invoice.transition_not_allowed");
        result.Current.Should().Be(from);
        VerifyNoWrite();
    }

    /// <summary>
    /// The closed set: of all 64 (current, requested) pairs exactly the seven FR-06 moves write, the eight same-status pairs are
    /// no-ops, and the other 49 (every move out of a final status, and every move into or out of Superseded, among them) are refused with
    /// no write.
    /// </summary>
    [Fact]
    public async Task EveryPair_BehavesPerTheFr06Matrix()
    {
        var allowed = AllowedTransitions().Select(a => ((InvoiceLifecycleStatus)a[0], (InvoiceLifecycleStatus)a[1])).ToHashSet();
        var all = Enum.GetValues<InvoiceLifecycleStatus>();
        var wrote = 0; var noops = 0; var refused = 0;

        foreach (var from in all)
        {
            foreach (var to in all)
            {
                var records = new Mock<IFieldMappingDataverseService>();
                records
                    .Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Row(from, 1));
                var service = new InvoiceLifecycleService(records.Object, new Mock<ILogger<InvoiceLifecycleService>>().Object);

                var result = await service.TransitionAsync(InvoiceId, to);

                var writes = records.Invocations.Count(i => i.Method.Name == nameof(IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync));
                if (from == to)
                {
                    result.Outcome.Should().Be(InvoiceTransitionOutcome.AlreadyInTarget, $"{from} to {to}");
                    writes.Should().Be(0, $"{from} to {to}");
                    noops++;
                }
                else if (allowed.Contains((from, to)))
                {
                    result.Outcome.Should().Be(InvoiceTransitionOutcome.Transitioned, $"{from} to {to}");
                    writes.Should().Be(1, $"{from} to {to}");
                    wrote++;
                }
                else
                {
                    result.Outcome.Should().Be(InvoiceTransitionOutcome.Refused, $"{from} to {to}");
                    result.Reason.Should().Be(InvoiceTransitionReasons.NotAllowed, $"{from} to {to}");
                    writes.Should().Be(0, $"{from} to {to}");
                    refused++;
                }
            }
        }

        (wrote, noops, refused).Should().Be((7, 8, 49));
    }

    [Theory]
    [InlineData(InvoiceLifecycleStatus.InReview)]
    [InlineData(InvoiceLifecycleStatus.Approved)]
    [InlineData(InvoiceLifecycleStatus.Paid)]
    [InlineData(InvoiceLifecycleStatus.Cancelled)]
    public async Task TargetEqualsCurrent_IsAlreadyInTarget_AndNothingIsWritten(InvoiceLifecycleStatus status)
    {
        ReadsReturn(Row(status, version: 3));

        var result = await CreateService().TransitionAsync(InvoiceId, status);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.AlreadyInTarget);
        result.Succeeded.Should().BeTrue();
        result.Current.Should().Be(status);
        VerifyNoWrite();
    }

    [Fact]
    public async Task ConcurrencyConflict_ThatMovedInvoiceToDisallowedStatus_RefusesOnRetryInsteadOfOverwriting()
    {
        // Reader A sees In Review v1 and asks for Approved; reader B rejects the invoice first (v2), so A's write is a 412.
        ReadsReturn(Row(InvoiceLifecycleStatus.InReview, 1), Row(InvoiceLifecycleStatus.Rejected, 2));
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync(
                "sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DBConcurrencyException("changed"));

        var result = await CreateService().TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Refused);
        result.Reason.Should().Be(InvoiceTransitionReasons.NotAllowed);
        result.Current.Should().Be(InvoiceLifecycleStatus.Rejected);
        _records.Verify(
            r => r.UpdateRecordFieldsIfUnchangedAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _records.Verify(
            r => r.UpdateRecordFieldsIfUnchangedAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), 2, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConcurrencyConflict_ThatMovedInvoiceToTheTarget_ReturnsAlreadyInTarget()
    {
        // The other writer made the same approval: the retry sees Approved and the second approval is a no-op.
        ReadsReturn(Row(InvoiceLifecycleStatus.InReview, 1), Row(InvoiceLifecycleStatus.Approved, 2));
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync(
                "sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DBConcurrencyException("changed"));

        var result = await CreateService().TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.AlreadyInTarget);
    }

    [Fact]
    public async Task ConcurrencyConflict_ThatLeavesTheMoveAllowed_RetriesOnTheFreshVersion()
    {
        ReadsReturn(Row(InvoiceLifecycleStatus.InReview, 1), Row(InvoiceLifecycleStatus.InReview, 2));
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync(
                "sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DBConcurrencyException("changed"));
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync(
                "sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 2, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateService().TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Transitioned);
        _records.Verify(
            r => r.UpdateRecordFieldsIfUnchangedAsync(
                "sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 2, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ConflictOnEveryAttempt_StopsAtTheAttemptLimit_RefusesAndNeverBlindWrites()
    {
        ReadsReturn(Row(InvoiceLifecycleStatus.InReview, 1));
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DBConcurrencyException("changed"));

        var result = await CreateService().TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Refused);
        result.Reason.Should().Be("invoice.concurrent_change");
        _records.Verify(
            r => r.UpdateRecordFieldsIfUnchangedAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Exactly(InvoiceReviewService.MaxConditionalWriteAttempts));
        // Only the conditional write exists on the service path: no unconditional update was used.
        _records.Verify(
            r => r.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task MissingInvoice_IsRefusedNotFound_AndNothingIsWritten()
    {
        ReadsReturn(new Dictionary<string, object?>());

        var result = await CreateService().TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Refused);
        result.Reason.Should().Be("invoice.not_found");
        result.Current.Should().BeNull();
        VerifyNoWrite();
    }

    [Fact]
    public async Task InvoiceDeletedBetweenReadAndWrite_IsRefusedNotFound()
    {
        ReadsReturn(Row(InvoiceLifecycleStatus.InReview, 1));
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("gone"));

        var result = await CreateService().TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Refused);
        result.Reason.Should().Be("invoice.not_found");
    }

    [Theory]
    [InlineData(5L)]
    [InlineData(0L)]
    [InlineData(null)]
    public async Task UnknownStoredStatus_FailsClosed_AndNothingIsWritten(long? storedStatus)
    {
        ReadsReturn(new Dictionary<string, object?> { ["statuscode"] = storedStatus, ["versionnumber"] = 1L });

        var result = await CreateService().TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Refused);
        result.Reason.Should().Be("invoice.status_unknown");
        result.Current.Should().BeNull();
        VerifyNoWrite();
    }

    [Fact]
    public async Task AllowedMoveWithNoReadableVersion_Throws_AndNothingIsWritten()
    {
        ReadsReturn(new Dictionary<string, object?> { ["statuscode"] = 1L, ["versionnumber"] = null });

        var act = () => CreateService().TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        await act.Should().ThrowAsync<InvalidOperationException>();
        VerifyNoWrite();
    }

    /// <summary>
    /// The values task 012 created in dev (docs/data-model/sprk_invoice.md "Status"). A drift here writes a status the platform rejects or,
    /// worse, a different valid one.
    /// </summary>
    [Fact]
    public void StatusCodes_MatchTheLiveOptionValues_AndPairWithTheirStateCode()
    {
        var expected = new (InvoiceLifecycleStatus Status, int StatusCode, int StateCode)[]
        {
            (InvoiceLifecycleStatus.InReview, 1, 0),
            (InvoiceLifecycleStatus.Approved, 659490000, 0),
            (InvoiceLifecycleStatus.SentToAp, 659490001, 0),
            (InvoiceLifecycleStatus.Paid, 659490002, 1),
            (InvoiceLifecycleStatus.Rejected, 659490003, 1),
            (InvoiceLifecycleStatus.ReturnedToFirm, 659490004, 1),
            (InvoiceLifecycleStatus.Cancelled, 2, 1),
            (InvoiceLifecycleStatus.Superseded, 659490005, 1), // task 088 F5 (docs/data-model/sprk_invoice.md)
        };

        Enum.GetValues<InvoiceLifecycleStatus>().Should().HaveCount(expected.Length);
        foreach (var (status, statusCode, stateCode) in expected)
        {
            InvoiceStatusCodes.StatusCodeOf(status).Should().Be(statusCode, status.ToString());
            InvoiceStatusCodes.StateCodeOf(status).Should().Be(stateCode, status.ToString());
            InvoiceStatusCodes.TryFromStatusCode(statusCode, out var roundTrip).Should().BeTrue();
            roundTrip.Should().Be(status);
        }
    }

    // ---- ebilling-r1 task 049 part A, G2 / D-101: the create-time status. The lifecycle service stays the single owner of statuscode;
    // it builds the create request (so 049 can put it inside one ExecuteTransactionAsync) and refuses every status but In Review / Rejected.

    private static Entity NewInvoice() => new("sprk_invoice", InvoiceId)
    {
        ["sprk_invoicenumber"] = "INV-1",
        ["sprk_totalamount"] = new Money(1500m),
    };

    [Theory]
    [InlineData(InvoiceLifecycleStatus.InReview, 0, 1)]
    [InlineData(InvoiceLifecycleStatus.Rejected, 1, 659490003)]
    public void BuildCreateRequest_ForInReviewAndRejected_AddsTheStatePairToACopyOfTheRow(
        InvoiceLifecycleStatus initial, int expectedState, int expectedStatus)
    {
        var invoice = NewInvoice();

        var result = CreateService().BuildInvoiceCreateRequest(invoice, initial);

        result.Succeeded.Should().BeTrue();
        result.Reason.Should().BeNull();
        result.Requested.Should().Be(initial);
        var target = result.Requests.Should().ContainSingle().Which.Should().BeOfType<CreateRequest>().Which.Target;
        target.LogicalName.Should().Be("sprk_invoice");
        target.Id.Should().Be(InvoiceId, "the caller's client-chosen id is kept, so the transaction's other rows can point at it");
        target.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(expectedState);
        target.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(expectedStatus);
        target.GetAttributeValue<string>("sprk_invoicenumber").Should().Be("INV-1", "the caller's columns are carried over unchanged");
        target.GetAttributeValue<Money>("sprk_totalamount").Value.Should().Be(1500m);
        invoice.Attributes.Should().NotContainKey("statuscode", "the caller's row is never changed behind its back");
        invoice.Attributes.Should().NotContainKey("statecode");
    }

    [Theory]
    [InlineData(InvoiceLifecycleStatus.Approved)]
    [InlineData(InvoiceLifecycleStatus.SentToAp)]
    [InlineData(InvoiceLifecycleStatus.Paid)]
    [InlineData(InvoiceLifecycleStatus.ReturnedToFirm)]
    [InlineData(InvoiceLifecycleStatus.Cancelled)]
    public void BuildCreateRequest_ForAnyOtherStatus_IsRefused_WithNoRequest(InvoiceLifecycleStatus initial)
    {
        var result = CreateService().BuildInvoiceCreateRequest(NewInvoice(), initial);

        result.Succeeded.Should().BeFalse();
        result.Requests.Should().BeEmpty("a refused create can never be sent by mistake");
        result.Reason.Should().Be(InvoiceTransitionReasons.InitialStatusNotAllowed);
        result.Requested.Should().Be(initial);
    }

    [Fact]
    public void BuildCreateRequest_ForAnUndefinedStatus_IsRefused()
    {
        var result = CreateService().BuildInvoiceCreateRequest(NewInvoice(), (InvoiceLifecycleStatus)99);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be(InvoiceTransitionReasons.InitialStatusNotAllowed);
    }

    [Theory]
    [InlineData("statuscode")]
    [InlineData("statecode")]
    [InlineData("StatusCode")]
    [InlineData("STATECODE")]
    public void BuildCreateRequest_WhenTheRowAlreadyCarriesAStatusColumn_IsRefused_BecauseOnlyTheLifecycleServiceWritesIt(string column)
    {
        var invoice = NewInvoice();
        invoice[column] = new OptionSetValue(InvoiceStatusCodes.Approved);

        var result = CreateService().BuildInvoiceCreateRequest(invoice, InvoiceLifecycleStatus.InReview);

        result.Succeeded.Should().BeFalse();
        result.Requests.Should().BeEmpty();
        result.Reason.Should().Be(InvoiceTransitionReasons.StatusSuppliedByCaller);
    }

    [Fact]
    public void BuildCreateRequest_ForARowOfAnotherTable_Throws_BecauseItIsAProgrammingError()
    {
        var act = () => CreateService().BuildInvoiceCreateRequest(new Entity("sprk_billingevent", Guid.NewGuid()), InvoiceLifecycleStatus.InReview);

        act.Should().Throw<ArgumentException>().WithParameterName("invoice");
        FluentActions.Invoking(() => CreateService().BuildInvoiceCreateRequest(null!, InvoiceLifecycleStatus.InReview)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildCreateRequest_BuildsAnOrdinaryCreateRequest_ThatSitsInsideOneTransaction()
    {
        var created = CreateService().BuildInvoiceCreateRequest(NewInvoice(), InvoiceLifecycleStatus.Rejected).Requests.Single();
        var line = new CreateRequest { Target = new Entity("sprk_billingevent", Guid.NewGuid()) };

        var transaction = DataverseServiceClientImpl.BuildTransaction([created, line]);

        transaction.Requests.Should().HaveCount(2);
        transaction.Requests[0].Should().BeSameAs(created);
    }

    [Fact]
    public void BuildCreateRequest_Performs_NoIo()
    {
        CreateService().BuildInvoiceCreateRequest(NewInvoice(), InvoiceLifecycleStatus.InReview);
        CreateService().BuildInvoiceCreateRequest(NewInvoice(), InvoiceLifecycleStatus.Approved);

        _records.VerifyNoOtherCalls();
    }

    // ---- ebilling-r1 task 097a: Superseded. A correction closes the invoice it replaces (In Review to Superseded) inside its commit
    // transaction, and the correction's undo reopens it (Superseded to In Review). Both are requests the lifecycle service builds and the
    // caller's transaction carries, conditional on the row version the caller read.

    private static InvoiceTransitionRequestResult Supersede(InvoiceLifecycleService service, InvoiceLifecycleStatus current, long version = 7) =>
        service.BuildSupersedeRequest(InvoiceId, current, version);

    private static InvoiceTransitionRequestResult Undo(InvoiceLifecycleService service, InvoiceLifecycleStatus current, long version = 7) =>
        service.BuildSupersedeUndoRequest(InvoiceId, current, version);

    /// <summary>
    /// Dataverse's optimistic concurrency inside one transaction (as <c>CorrectionWorld</c> models it): a conditional update whose row version
    /// is not the stored row's faults, and nothing in the transaction is applied; otherwise every update is applied and the version moves.
    /// An update that is NOT conditional is applied whatever the stored version (a blind write).
    /// </summary>
    private static bool CommitAgainst(Entity stored, IReadOnlyList<OrganizationRequest> requests)
    {
        var updates = requests.OfType<UpdateRequest>().Where(u => u.Target.Id == stored.Id).ToList();
        if (updates.Any(u => u.ConcurrencyBehavior == ConcurrencyBehavior.IfRowVersionMatches
                             && u.Target.RowVersion != stored.GetAttributeValue<long>("versionnumber").ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            return false;
        }

        foreach (var update in updates)
        {
            foreach (var attribute in update.Target.Attributes)
            {
                stored[attribute.Key] = attribute.Value;
            }

            stored["versionnumber"] = stored.GetAttributeValue<long>("versionnumber") + 1;
        }

        return true;
    }

    private static Entity StoredInvoice(InvoiceLifecycleStatus status, long version) => new("sprk_invoice", InvoiceId)
    {
        ["statecode"] = new OptionSetValue(InvoiceStatusCodes.StateCodeOf(status)),
        ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.StatusCodeOf(status)),
        ["versionnumber"] = version,
    };

    [Fact]
    public void BuildSupersedeRequest_FromInReview_IsAConditionalUpdateOfTheStatusPairOnly_AtTheReadVersion()
    {
        var result = Supersede(CreateService(), InvoiceLifecycleStatus.InReview, version: 42);

        result.Succeeded.Should().BeTrue();
        result.Reason.Should().BeNull();
        result.Current.Should().Be(InvoiceLifecycleStatus.InReview);
        result.Requested.Should().Be(InvoiceLifecycleStatus.Superseded);
        var request = result.Request!;
        request.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches, "an invoice changed since the read must fault the commit");
        request.Target.LogicalName.Should().Be("sprk_invoice");
        request.Target.Id.Should().Be(InvoiceId);
        request.Target.RowVersion.Should().Be("42");
        request.Target.Attributes.Keys.Should().BeEquivalentTo("statecode", "statuscode");
        request.Target.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(1, "Superseded is Inactive");
        request.Target.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(659490005);
    }

    [Fact]
    public void BuildSupersedeUndoRequest_FromSuperseded_IsAConditionalUpdateBackToInReview_AtTheReadVersion()
    {
        var result = Undo(CreateService(), InvoiceLifecycleStatus.Superseded, version: 43);

        result.Succeeded.Should().BeTrue();
        result.Current.Should().Be(InvoiceLifecycleStatus.Superseded);
        result.Requested.Should().Be(InvoiceLifecycleStatus.InReview);
        var request = result.Request!;
        request.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        request.Target.RowVersion.Should().Be("43");
        request.Target.Attributes.Keys.Should().BeEquivalentTo("statecode", "statuscode");
        request.Target.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(0, "In Review is Active");
        request.Target.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(1);
    }

    public static IEnumerable<object[]> StatusesOtherThan(InvoiceLifecycleStatus allowed) =>
        Enum.GetValues<InvoiceLifecycleStatus>().Where(s => s != allowed).Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(StatusesOtherThan), InvoiceLifecycleStatus.InReview)]
    public void BuildSupersedeRequest_FromAnyStatusButInReview_IsRefused_WithNoRequest(InvoiceLifecycleStatus current)
    {
        var result = Supersede(CreateService(), current);

        result.Succeeded.Should().BeFalse();
        result.Request.Should().BeNull("a refused move can never be sent by mistake");
        result.Reason.Should().Be(InvoiceTransitionReasons.NotAllowed);
        result.Current.Should().Be(current);
    }

    [Theory]
    [MemberData(nameof(StatusesOtherThan), InvoiceLifecycleStatus.Superseded)]
    public void BuildSupersedeUndoRequest_FromAnyStatusButSuperseded_IsRefused_WithNoRequest(InvoiceLifecycleStatus current)
    {
        var result = Undo(CreateService(), current);

        result.Succeeded.Should().BeFalse();
        result.Request.Should().BeNull("the undo reopens only a Superseded invoice; Paid, Rejected or Approved is never reopened this way");
        result.Reason.Should().Be(InvoiceTransitionReasons.NotAllowed);
        result.Current.Should().Be(current, "a caller that finds the invoice already In Review can tell there is nothing to undo");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void TheBuilders_WithNoReadVersion_Throw_BecauseTheMoveCouldNotBeConditional(long version)
    {
        FluentActions.Invoking(() => Supersede(CreateService(), InvoiceLifecycleStatus.InReview, version)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Undo(CreateService(), InvoiceLifecycleStatus.Superseded, version)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheBuilders_Perform_NoIo()
    {
        Supersede(CreateService(), InvoiceLifecycleStatus.InReview);
        Undo(CreateService(), InvoiceLifecycleStatus.Superseded);
        Supersede(CreateService(), InvoiceLifecycleStatus.Approved);

        _records.VerifyNoOtherCalls();
    }

    [Fact]
    public void BuildSupersedeUndoRequest_AtTheCurrentVersion_ReopensTheInvoice_InsideOneTransaction()
    {
        var stored = StoredInvoice(InvoiceLifecycleStatus.Superseded, version: 8);
        var undo = Undo(CreateService(), InvoiceLifecycleStatus.Superseded, version: 8).Request!;
        var other = new DeleteRequest { Target = new EntityReference("sprk_invoice", Guid.NewGuid()) };

        DataverseServiceClientImpl.BuildTransaction([other, undo]).Requests.Should().HaveCount(2, "the undo sits in the caller's transaction");
        CommitAgainst(stored, [other, undo]).Should().BeTrue();

        stored.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview);
        stored.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(InvoiceStatusCodes.StateActive);
    }

    [Fact]
    public void BuildSupersedeUndoRequest_AtAStaleVersion_IsRefusedByTheRowVersionGuard_AndTheInvoiceStaysAsItIs()
    {
        // The undo read the old invoice Superseded at version 8; something changed it since (version 9). The reopen must not overwrite that.
        var stored = StoredInvoice(InvoiceLifecycleStatus.Superseded, version: 9);
        stored["sprk_note"] = "changed since the read";
        var undo = Undo(CreateService(), InvoiceLifecycleStatus.Superseded, version: 8).Request!;

        CommitAgainst(stored, [undo]).Should().BeFalse("the row-version guard faults the stale reopen and the transaction rolls back");

        stored.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.Superseded);
        stored.GetAttributeValue<long>("versionnumber").Should().Be(9);
    }

    [Fact]
    public void BuildSupersedeRequest_AtAStaleVersion_IsRefusedByTheRowVersionGuard_SoAnApprovalSinceTheReadIsNeverOverwritten()
    {
        // Read In Review at version 5; approved since (version 6). The close must fault, not move an Approved invoice to Superseded.
        var stored = StoredInvoice(InvoiceLifecycleStatus.Approved, version: 6);
        var close = Supersede(CreateService(), InvoiceLifecycleStatus.InReview, version: 5).Request!;

        CommitAgainst(stored, [close]).Should().BeFalse();

        stored.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact]
    public void Superseded_IsNeverCountedInSpend_NorAsPendingSpend()
    {
        FinanceBaseValues.CountedInvoiceStatuses.Should().NotContain(InvoiceStatusCodes.Superseded);
        FinanceBaseValues.PendingInvoiceStatuses.Should().NotContain(InvoiceStatusCodes.Superseded);
    }

    // ADR-038 B3 note: this is not a plain "service resolves" wiring test. It pins the gating contract the task 014 acceptance criterion
    // requires (a write-path owner is never behind a feature flag; precedent: AnalysisServicesModuleGatingTests): wrapping the
    // registration in `if (documentIntelligenceEnabled)` or `if (ebillingEnabled)` makes it fail. Drop it if a structural guard replaces it.
    [Fact]
    public void LifecycleService_IsNotBehindAnyFeatureGate_AndResolvesWithEveryFlagOff()
    {
        // No Finance / DocumentIntelligence / Ebilling flag is set: a write-path owner must not sit behind a feature gate.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_records.Object);
        services.AddFinanceModule(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IInvoiceLifecycleService>().Should().BeOfType<InvoiceLifecycleService>();
    }
}
