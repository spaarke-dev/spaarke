using System.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 task 095f-2a (EB-D-240; design 2 Change 1): the lifecycle overload writes the decision note (<c>sprk_invoice.sprk_decisionnote</c>)
/// in the SAME conditional update as the status, or not at all. Over one stored invoice whose conditional write honours the row version as
/// Dataverse does (a stale version faults and applies nothing), driven through the REAL <see cref="InvoiceLifecycleService"/>.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the note is the internal record of who decided what on the invoice, and its atomicity with the status is the
/// invariant that keeps a refused or concurrent decision from leaving a note behind. Seeding: removing the note line from
/// <c>InvoiceLifecycleService.TransitionAsync</c> fails <see cref="Note_LandsInTheSameUpdateAsTheStatus"/>; writing it in a second update fails
/// that test's single-write assertion.
/// </remarks>
public sealed class InvoiceLifecycleDecisionNoteTests
{
    private static readonly Guid InvoiceId = Guid.Parse("095f2a00-0000-4000-8000-0000000000a1");
    private const string Note = "Spoke to the partner.\nAgreed\tto reject.";

    /// <summary>One stored invoice: reads return its columns; the conditional write applies all fields or none (a stale version faults).</summary>
    private sealed class StoredInvoice
    {
        public readonly Dictionary<string, object?> Row = new(StringComparer.Ordinal)
        {
            ["statuscode"] = (long)InvoiceStatusCodes.InReview,
            ["statecode"] = (long)InvoiceStatusCodes.StateActive,
            ["versionnumber"] = 1L,
        };

        public readonly Mock<IFieldMappingDataverseService> Records = new();
        public readonly List<Dictionary<string, object?>> Writes = [];

        /// <summary>Runs before each conditional write is checked (a concurrent writer lands here).</summary>
        public Action? BeforeWrite { get; set; }

        public StoredInvoice()
        {
            Records.Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid _, string[] columns, CancellationToken _) => columns.ToDictionary(c => c, c => Row.GetValueOrDefault(c)));
            Records.Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, Dictionary<string, object?> fields, long version, CancellationToken _) =>
                {
                    BeforeWrite?.Invoke();
                    if ((long)Row["versionnumber"]! != version)
                    {
                        throw new DBConcurrencyException("simulated: the invoice changed since it was read; nothing applied");
                    }

                    Writes.Add(new Dictionary<string, object?>(fields));
                    foreach (var (column, value) in fields)
                    {
                        Row[column] = value is int number ? (long)number : value;
                    }

                    Row["versionnumber"] = (long)Row["versionnumber"]! + 1;
                    return Task.CompletedTask;
                });
        }

        public InvoiceLifecycleStatus Status =>
            InvoiceStatusCodes.TryFromStatusCode(Convert.ToInt64(Row["statuscode"]), out var status) ? status : throw new InvalidOperationException("unknown status");
    }

    private static InvoiceLifecycleService Lifecycle(StoredInvoice stored, ILogger<InvoiceLifecycleService>? logger = null) =>
        new(stored.Records.Object, logger ?? new ApprovalListLogger<InvoiceLifecycleService>()) { ReviewDetailProbe = ReviewDetailColumnProbe.Fixed(present: true) };

    [Theory(DisplayName = "095f-2a: the decision note lands in the same conditional update as the status (approve, reject, return)")]
    [InlineData(InvoiceLifecycleStatus.Approved)]
    [InlineData(InvoiceLifecycleStatus.Rejected)]
    [InlineData(InvoiceLifecycleStatus.ReturnedToFirm)]
    public async Task Note_LandsInTheSameUpdateAsTheStatus(InvoiceLifecycleStatus target)
    {
        var stored = new StoredInvoice();

        var result = await Lifecycle(stored).TransitionAsync(InvoiceId, target, Note, CancellationToken.None);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Transitioned);
        var write = stored.Writes.Should().ContainSingle("one conditional update carries the status and the note").Subject;
        write["statuscode"].Should().Be(InvoiceStatusCodes.StatusCodeOf(target));
        write[InvoiceCorrectionNote.DecisionColumn].Should().Be(Note, "written as given: never trimmed, stripped or truncated by the lifecycle");
        stored.Row[InvoiceCorrectionNote.DecisionColumn].Should().Be(Note);
        stored.Records.Verify(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 1L, It.IsAny<CancellationToken>()),
            Times.Once, "conditional on the version read with the status");
    }

    [Fact(DisplayName = "095f-2a: a concurrent change writes neither the status nor the note; the re-read refuses the move and nothing of this call lands")]
    public async Task ConcurrentChange_WritesNeitherTheStatusNorTheNote()
    {
        var stored = new StoredInvoice();
        var raced = false;
        stored.BeforeWrite = () =>
        {
            if (raced)
            {
                return;
            }

            // Another approver approves between this call's read and its write.
            raced = true;
            stored.Row["statuscode"] = (long)InvoiceStatusCodes.Approved;
            stored.Row["versionnumber"] = 2L;
        };

        var result = await Lifecycle(stored).TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Rejected, Note, CancellationToken.None);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Refused, "Approved to Rejected is not an FR-06 move");
        stored.Writes.Should().BeEmpty("the faulted write applied nothing and no second write was made");
        stored.Status.Should().Be(InvoiceLifecycleStatus.Approved, "the concurrent writer's status stands");
        stored.Row.Should().NotContainKey(InvoiceCorrectionNote.DecisionColumn, "the note never lands without its decision");
    }

    [Fact(DisplayName = "095f-2a: a version that keeps changing gives up (ConcurrentChange) with no note written")]
    public async Task KeepsChanging_GivesUp_WithNoNote()
    {
        var stored = new StoredInvoice();
        stored.BeforeWrite = () => stored.Row["versionnumber"] = (long)stored.Row["versionnumber"]! + 1; // a writer lands before every attempt

        var result = await Lifecycle(stored).TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Rejected, Note, CancellationToken.None);

        result.Reason.Should().Be(InvoiceTransitionReasons.ConcurrentChange);
        stored.Writes.Should().BeEmpty();
        stored.Row.Should().NotContainKey(InvoiceCorrectionNote.DecisionColumn);
        stored.Status.Should().Be(InvoiceLifecycleStatus.InReview);
    }

    [Fact(DisplayName = "095f-2a must-not-fire: the three-argument call, and the overload with a null note, write no decision note column")]
    public async Task ThreeArgumentCall_AndNullNote_WriteNoNoteColumn()
    {
        var threeArg = new StoredInvoice();
        (await Lifecycle(threeArg).TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved, CancellationToken.None)).Outcome
            .Should().Be(InvoiceTransitionOutcome.Transitioned);
        threeArg.Writes.Should().ContainSingle().Which.Keys.Should().BeEquivalentTo("statuscode", "statecode", InvoiceReviewDetail.Column);

        var nullNote = new StoredInvoice();
        (await Lifecycle(nullNote).TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved, decisionNote: null, CancellationToken.None)).Outcome
            .Should().Be(InvoiceTransitionOutcome.Transitioned);
        nullNote.Writes.Should().ContainSingle().Which.Should().NotContainKey(InvoiceCorrectionNote.DecisionColumn);
    }

    [Fact(DisplayName = "095f-2a (K-1): an invoice already in the target writes nothing, so the note a repeat carries never replaces the first one")]
    public async Task AlreadyInTarget_WritesNoNote()
    {
        var stored = new StoredInvoice();
        stored.Row["statuscode"] = (long)InvoiceStatusCodes.Rejected;
        stored.Row[InvoiceCorrectionNote.DecisionColumn] = "The first note";

        var result = await Lifecycle(stored).TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Rejected, "An edited note", CancellationToken.None);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.AlreadyInTarget);
        stored.Writes.Should().BeEmpty();
        stored.Row[InvoiceCorrectionNote.DecisionColumn].Should().Be("The first note");
    }

    [Fact(DisplayName = "095f-2a: no lifecycle log line carries the note text")]
    public async Task NoLogLine_CarriesTheNote()
    {
        var log = new ApprovalListLogger<InvoiceLifecycleService>();
        var stored = new StoredInvoice();

        await Lifecycle(stored, log).TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Rejected, Note, CancellationToken.None);
        await Lifecycle(stored, log).TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved, Note, CancellationToken.None); // refused: logged

        log.Entries.Should().NotBeEmpty();
        log.Entries.Should().NotContain(e => e.Message.Contains("partner", StringComparison.Ordinal) || e.Message.Contains("Agreed", StringComparison.Ordinal));
    }
}
