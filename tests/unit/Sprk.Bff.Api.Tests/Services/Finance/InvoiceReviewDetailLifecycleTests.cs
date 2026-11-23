using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 082c-2 (uac-r2 condition (b); ontology D-205 writers 6 and 7): the review detail never outlives In Review. Over one stored invoice
/// that the real <see cref="InvoiceLifecycleService"/> moves through every exit path: after every move, a status other than In Review carries no
/// detail. And where the column does not exist yet, the moves still happen (the column is only left out).
/// </summary>
public sealed class InvoiceReviewDetailLifecycleTests
{
    private static readonly Guid InvoiceId = Guid.Parse("082c2000-0000-4000-8000-00000000c0de");

    /// <summary>One stored invoice: TransitionAsync reads and writes it as Dataverse would (a conditional write moves its version).</summary>
    private sealed class StoredInvoice
    {
        public readonly Dictionary<string, object?> Row = new(StringComparer.Ordinal)
        {
            ["statuscode"] = (long)InvoiceStatusCodes.InReview,
            ["statecode"] = (long)InvoiceStatusCodes.StateActive,
            ["versionnumber"] = 1L,
            [InvoiceReviewDetail.Column] = new OptionSetValue(InvoiceReviewDetail.WaitingForBillingTerms),
        };

        public readonly Mock<IFieldMappingDataverseService> Records = new();

        public StoredInvoice()
        {
            Records.Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid _, string[] columns, CancellationToken _) => columns.ToDictionary(c => c, c => Row.GetValueOrDefault(c)));
            Records.Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, Dictionary<string, object?> fields, long _, CancellationToken _) =>
                {
                    foreach (var (column, value) in fields)
                    {
                        Row[column] = value is int number ? (long)number : value;
                    }

                    Row["versionnumber"] = (long)Row["versionnumber"]! + 1;
                    return Task.CompletedTask;
                });
        }

        public void Apply(UpdateRequest request)
        {
            foreach (var attribute in request.Target.Attributes)
            {
                Row[attribute.Key] = attribute.Value is OptionSetValue option && attribute.Key is "statuscode" or "statecode" ? option.Value : attribute.Value;
            }

            Row["versionnumber"] = (long)Row["versionnumber"]! + 1;
        }

        public InvoiceLifecycleStatus Status =>
            InvoiceStatusCodes.TryFromStatusCode(Convert.ToInt64(Row["statuscode"]), out var status) ? status : throw new InvalidOperationException("unknown status");

        public void ShouldHoldTheInvariant(string after)
        {
            if (Status != InvoiceLifecycleStatus.InReview)
            {
                Row[InvoiceReviewDetail.Column].Should().BeNull($"after {after} the invoice is {Status}: a closed invoice never carries a review detail");
            }
        }
    }

    private static InvoiceLifecycleService Lifecycle(StoredInvoice stored, bool columnPresent = true) =>
        new(stored.Records.Object, NullLogger<InvoiceLifecycleService>.Instance) { ReviewDetailProbe = ReviewDetailColumnProbe.Fixed(columnPresent) };

    /// <summary>Every exit path from a held In Review invoice, as the moves the callers make (approve, reject, return, cancel, AP, pay).</summary>
    public static TheoryData<InvoiceLifecycleStatus[]> ExitPaths() => new()
    {
        { [InvoiceLifecycleStatus.Approved] },
        { [InvoiceLifecycleStatus.Approved, InvoiceLifecycleStatus.SentToAp] },
        { [InvoiceLifecycleStatus.Approved, InvoiceLifecycleStatus.SentToAp, InvoiceLifecycleStatus.Paid] },
        { [InvoiceLifecycleStatus.Approved, InvoiceLifecycleStatus.Cancelled] },
        { [InvoiceLifecycleStatus.Rejected] },
        { [InvoiceLifecycleStatus.ReturnedToFirm] },
        { [InvoiceLifecycleStatus.Cancelled] },
    };

    [Theory(DisplayName = "082c-2 (uac-r2 (b)): along every exit path from In Review, a non-In-Review invoice never carries a review detail")]
    [MemberData(nameof(ExitPaths))]
    public async Task EveryExit_LeavesNoDetailOnANonInReviewInvoice(InvoiceLifecycleStatus[] path)
    {
        var stored = new StoredInvoice();
        var lifecycle = Lifecycle(stored);

        foreach (var target in path)
        {
            (await lifecycle.TransitionAsync(InvoiceId, target)).Succeeded.Should().BeTrue();
            stored.Status.Should().Be(target);
            stored.ShouldHoldTheInvariant($"the move to {target}");
        }
    }

    [Fact(DisplayName = "082c-2 (D-205 writer 7): the correction's Superseded close blanks the detail in the same request; the undo writes none unless the caller derived one")]
    public void Supersede_Blanks_TheUndoWritesOnlyADerivedDetail()
    {
        var stored = new StoredInvoice();
        var lifecycle = Lifecycle(stored);

        var close = lifecycle.BuildSupersedeRequest(InvoiceId, InvoiceLifecycleStatus.InReview, (long)stored.Row["versionnumber"]!);
        stored.Apply(close.Request!);
        stored.Status.Should().Be(InvoiceLifecycleStatus.Superseded);
        stored.ShouldHoldTheInvariant("the Superseded close");

        var undo = lifecycle.BuildSupersedeUndoRequest(InvoiceId, InvoiceLifecycleStatus.Superseded, (long)stored.Row["versionnumber"]!);
        undo.Request!.Target.Attributes.Keys.Should().NotContain(InvoiceReviewDetail.Column, "no detail was derived: no hold is open, so blank is right");

        // Fix round 1 (F2): the commit gate derives the detail from the reopened invoice's remaining holds and the undo carries it in the status write.
        var withDetail = lifecycle.BuildSupersedeUndoRequest(
            InvoiceId, InvoiceLifecycleStatus.Superseded, (long)stored.Row["versionnumber"]!, InvoiceReviewDetail.WaitingForBillingTerms);
        withDetail.Request!.Target.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value.Should().Be(659490000);
        withDetail.Request.Target.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview);
        stored.Apply(withDetail.Request);
        stored.Status.Should().Be(InvoiceLifecycleStatus.InReview);
        stored.ShouldHoldTheInvariant("the undo with a derived detail: In Review may carry one");
    }

    [Fact(DisplayName = "082c-2 fix round 1: the undo's detail is left out where the column does not exist yet, and the reopen still happens")]
    public void Undo_WithADetail_ColumnMissing_LeavesItOut()
    {
        var stored = new StoredInvoice();
        var lifecycle = Lifecycle(stored, columnPresent: false);

        var undo = lifecycle.BuildSupersedeUndoRequest(InvoiceId, InvoiceLifecycleStatus.Superseded, 9, InvoiceReviewDetail.WaitingForFirm);

        undo.Succeeded.Should().BeTrue();
        undo.Request!.Target.Attributes.Keys.Should().BeEquivalentTo("statecode", "statuscode");
    }

    [Fact(DisplayName = "082c-2 (mismatch): where the column does not exist yet, every move still happens and leaves the column out")]
    public async Task ColumnMissing_TheMovesStillHappen_WithoutTheColumn()
    {
        var stored = new StoredInvoice();
        stored.Row.Remove(InvoiceReviewDetail.Column);
        var lifecycle = Lifecycle(stored, columnPresent: false);

        (await lifecycle.TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved)).Succeeded.Should().BeTrue();
        stored.Row.Should().NotContainKey(InvoiceReviewDetail.Column);
        lifecycle.BuildSupersedeRequest(InvoiceId, InvoiceLifecycleStatus.InReview, 9).Request!.Target.Attributes.Keys
            .Should().BeEquivalentTo("statecode", "statuscode");
    }
}
