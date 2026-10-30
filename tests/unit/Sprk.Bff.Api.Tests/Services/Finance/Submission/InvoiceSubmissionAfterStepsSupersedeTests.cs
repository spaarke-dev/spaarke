using System.ServiceModel;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 097d (model 9.1 "Existing invoice"): after a correction's commit passed the gate, every open Signal on the invoice it
/// superseded and on that invoice's lines is closed Superseded through the existing close, before the terminal status, durably and once.
/// Same world as task 049's after-steps tests; the old invoice, its line, the earlier submission and the Signal re-read are added here.
/// </summary>
public partial class InvoiceSubmissionAfterStepsTests
{
    private static readonly Guid Earlier = Guid.Parse("09700000-0000-4000-8000-0000000000e1");
    private static readonly Guid OldInvoice = InvoiceSubmissionPersister.InvoiceIdFor(Earlier);
    private static readonly Guid OldLine = InvoiceSubmissionPersister.LineIdFor(Earlier, 0);
    private static readonly Guid ThirdInvoice = Guid.Parse("09700000-0000-4000-8000-0000000000f3");

    /// <summary>The invoices the step reads: (supersedes link, statuscode) per id. The old invoice is Superseded by this submission's invoice.</summary>
    private sealed class Invoices
    {
        public Dictionary<Guid, Entity> Rows { get; } = new()
        {
            [InvoiceId] = new Entity(InvoiceReviewService.InvoiceEntity, InvoiceId)
            {
                [InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn] = new EntityReference(InvoiceReviewService.InvoiceEntity, OldInvoice),
                ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.InReview),
            },
            [OldInvoice] = new Entity(InvoiceReviewService.InvoiceEntity, OldInvoice) { ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Superseded) },
        };

        public List<Guid> InvoiceReads { get; } = [];
        public List<Guid> SignalReads { get; } = [];

        /// <summary>The old invoice's read throws this once (a transient fault).</summary>
        public Exception? OldInvoiceFault { get; set; }

        /// <summary>The invoice the earlier submission links (097b accepts only the one the job created from it).</summary>
        public Guid EarlierLinks { get; set; } = OldInvoice;

        /// <summary>The old invoice no longer exists (Dataverse's ObjectDoesNotExist on its read).</summary>
        public bool OldInvoiceGone { get; set; }
    }

    private static FaultException<OrganizationServiceFault> NotFound() =>
        new(new OrganizationServiceFault { ErrorCode = -2147220969, Message = "does not exist" }, "does not exist");

    /// <summary>A correction (sprk_resubmissionof = <see cref="Earlier"/>, unless <paramref name="correction"/> is false) over an accepted outcome.</summary>
    private static async Task<(World World, Invoices Invoices)> CorrectionWorldAsync(bool correction = true)
    {
        var world = new World(await Accepted());
        if (correction)
        {
            world.Row[InvoiceSubmissionService.ResubmissionOfColumn] = new EntityReference(FinanceBaseValues.SubmissionEntity, Earlier);
        }

        var invoices = new Invoices();

        // The earlier submission links the invoice the job created from it (097b's fail-closed resolution).
        world.Entities.Setup(e => e.RetrieveAsync(FinanceBaseValues.SubmissionEntity, Earlier, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid _, string[] columns, CancellationToken _) => World.Copy(
                new Entity(FinanceBaseValues.SubmissionEntity, Earlier) { [InvoiceSubmissionService.InvoiceColumn] = new EntityReference("sprk_invoice", invoices.EarlierLinks) },
                columns));

        world.Entities.Setup(e => e.RetrieveAsync(InvoiceReviewService.InvoiceEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, string[] columns, CancellationToken _) =>
            {
                invoices.InvoiceReads.Add(id);
                if (id == OldInvoice && invoices.OldInvoiceFault is { } fault)
                {
                    invoices.OldInvoiceFault = null;
                    return Task.FromException<Entity>(fault);
                }

                if (id == OldInvoice && invoices.OldInvoiceGone)
                {
                    return Task.FromException<Entity>(NotFound());
                }

                return Task.FromResult(World.Copy(invoices.Rows[id], columns));
            });

        // The re-read of one Signal before its close (status and row version, as the table holds them now).
        world.Entities.Setup(e => e.RetrieveAsync("sprk_signal", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] columns, CancellationToken _) =>
            {
                invoices.SignalReads.Add(id);
                return World.Copy(world.Signals.Values.Single(s => s.Id == id), columns);
            });

        // The old invoice's lines (the open-set read asks for them by the old invoice).
        world.Entities.Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_billingevent"
                    && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_invoice" && (Guid)c.Values[0] == OldInvoice)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection([new Entity("sprk_billingevent", OldLine)]));

        return (world, invoices);
    }

    private static string OldInvoiceKey(string policyCode, int episode = 1) =>
        SignalWriter.BuildDedupeKey(policyCode, "sprk_invoice", OldInvoice.ToString("D"), episode);

    private static string OldLineKey(string policyCode, int episode = 1) =>
        SignalWriter.BuildDedupeKey(policyCode, "sprk_billingevent", OldLine.ToString("D"), episode);

    [Fact(DisplayName = "097d: a superseded invoice is left with no open Signal - its and its lines' Open and Acknowledged Signals close Superseded, before the terminal status")]
    public async Task Correction_ClosesTheOldInvoicesAndItsLinesOpenSignals()
    {
        var (world, _) = await CorrectionWorldAsync();
        var onInvoice = world.Seed(OldInvoiceKey("BILL-RATE"), SignalWriter.SignalStatusOpen, "[]");
        var onLine = world.Seed(OldLineKey("BILL-AGE"), SignalWriter.SignalStatusAcknowledged, "[]");
        var onNewInvoice = world.Seed(InvoiceKey("BILL-RATE", 1), SignalWriter.SignalStatusOpen, "[]");
        var terminalBeforeClose = false;
        world.OnTerminalWrite = () => terminalBeforeClose = world.Superseded.Count < 2;

        var result = await world.Build().RunAsync(Persisted(await Accepted()), "corr-1");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Superseded.Should().BeEquivalentTo(new[] { onInvoice.Id, onLine.Id }, "every open Signal on the old invoice and its line, through the Superseded close");
        StatusOf(onInvoice).Should().Be(SignalWriter.SignalStatusResolved);
        StatusOf(onLine).Should().Be(SignalWriter.SignalStatusResolved);
        StatusOf(onNewInvoice).Should().Be(SignalWriter.SignalStatusOpen, "the new invoice's own Signals are not this step's");
        world.Closes.Should().BeEmpty("never CloseActedByDecisionAsync on this path");
        world.TerminalWrites.Should().ContainSingle();
        terminalBeforeClose.Should().BeFalse("the terminal status is written only after every close");
    }

    [Fact(DisplayName = "097d: a Signal a person closed (before the set read, or between the read and its close) is left as it is")]
    public async Task Correction_LeavesSignalsAPersonClosed()
    {
        var (world, invoices) = await CorrectionWorldAsync();
        var closedBefore = world.Seed(OldInvoiceKey("BILL-RATE"), SignalWriter.SignalStatusResolved, "[]");
        closedBefore["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionDismissed);
        var closedMeanwhile = world.Seed(OldLineKey("BILL-AGE"), SignalWriter.SignalStatusOpen, "[]");
        var open = world.Seed(OldInvoiceKey("BILL-HOURS"), SignalWriter.SignalStatusOpen, "[]");
        world.OnSetRead = () =>
        {
            // A person resolves it after the open set was read: the re-read before the close sees it.
            closedMeanwhile["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
            closedMeanwhile["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionActed);
            closedMeanwhile.RowVersion = "2";
        };

        var result = await world.Build().RunAsync(Persisted(await Accepted()), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Superseded.Should().Equal(open.Id);
        invoices.SignalReads.Should().Contain(closedMeanwhile.Id, "each Signal is re-read before its close");
        closedBefore.GetAttributeValue<OptionSetValue>("sprk_resolutiontype").Value.Should().Be(SignalWriter.ResolutionDismissed);
        closedMeanwhile.GetAttributeValue<OptionSetValue>("sprk_resolutiontype").Value.Should().Be(SignalWriter.ResolutionActed);
        closedMeanwhile.RowVersion.Should().Be("2", "nothing was written to it");
    }

    [Theory(DisplayName = "097d: an old invoice that is not Superseded closes nothing")]
    [InlineData(InvoiceStatusCodes.InReview)]
    [InlineData(InvoiceStatusCodes.Approved)]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm)]
    public async Task Correction_OldInvoiceNotSuperseded_ClosesNothing(int statusCode)
    {
        var (world, invoices) = await CorrectionWorldAsync();
        invoices.Rows[OldInvoice]["statuscode"] = new OptionSetValue(statusCode);
        var open = world.Seed(OldInvoiceKey("BILL-RATE"), SignalWriter.SignalStatusOpen, "[]");

        var result = await world.Build().RunAsync(Persisted(await Accepted()), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Superseded.Should().BeEmpty();
        StatusOf(open).Should().Be(SignalWriter.SignalStatusOpen);
        world.SignalQueries.Should().BeEmpty("the open set is not read");
        invoices.SignalReads.Should().BeEmpty();
    }

    /// <summary>Seeded (notes 097d): with the supersedes-link condition removed, both rows close the old invoice's Signal and fail.</summary>
    [Theory(DisplayName = "097d: an old invoice Superseded by a different invoice (this one links another or none, e.g. an automatic rejection) closes nothing")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Correction_SupersededByAnotherInvoice_ClosesNothing(bool linksAnother)
    {
        var (world, invoices) = await CorrectionWorldAsync();
        invoices.Rows[InvoiceId][InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn] =
            linksAnother ? new EntityReference(InvoiceReviewService.InvoiceEntity, ThirdInvoice) : null;
        var open = world.Seed(OldInvoiceKey("BILL-RATE"), SignalWriter.SignalStatusOpen, "[]");

        var result = await world.Build().RunAsync(Persisted(await Accepted()), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Superseded.Should().BeEmpty();
        StatusOf(open).Should().Be(SignalWriter.SignalStatusOpen);
        invoices.InvoiceReads.Should().NotContain(OldInvoice, "the link is checked before the old invoice is read");
    }

    [Fact(DisplayName = "097d: the close fails once mid-way -> Retry before the terminal status; the resume closes the rest and closes nothing twice")]
    public async Task Correction_CloseFailsOnce_RetryThenResumeCompletes()
    {
        var (world, _) = await CorrectionWorldAsync();
        var signals = new[]
        {
            world.Seed(OldInvoiceKey("BILL-RATE"), SignalWriter.SignalStatusOpen, "[]"),
            world.Seed(OldInvoiceKey("BILL-HOURS"), SignalWriter.SignalStatusOpen, "[]"),
            world.Seed(OldLineKey("BILL-AGE"), SignalWriter.SignalStatusAcknowledged, "[]"),
        }.OrderBy(s => s.Id).ToArray(); // the set's order
        var failing = signals[1];
        var failingStatus = StatusOf(failing);
        world.SupersedeFaults[failing.Id] = new TimeoutException("the close timed out");
        var sut = world.Build();

        var first = await sut.RunAsync(Persisted(await Accepted()), null);

        first.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        first.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.SupersedeSignalsFailedCode);
        world.TerminalWrites.Should().BeEmpty();
        world.Notified.Should().BeEmpty();
        world.Status.Should().Be(InvoiceSubmissionStatus.Processing);
        world.Superseded.Should().BeEquivalentTo(new[] { signals[0].Id, signals[2].Id }, "every Signal is still attempted");
        StatusOf(failing).Should().Be(failingStatus, "the faulted close wrote nothing");

        var resumed = await sut.ResumeAsync(Submission, null);

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Completed, resumed.ReasonCode);
        world.Superseded.Should().OnlyHaveUniqueItems("nothing is closed twice");
        world.Superseded.Should().BeEquivalentTo(signals.Select(s => s.Id));
        world.TerminalWrites.Should().ContainSingle();
    }

    [Theory(DisplayName = "097d follow-up (ontology-r1, bounded and visible): a failing run logs ONE Warning naming the submission, the code and exactly the Signals left open (ids only)")]
    [InlineData("changed")]
    [InlineData("fault")]
    public async Task Correction_FailingRun_WarnsWithTheSignalsLeftOpen(string why)
    {
        var (world, _) = await CorrectionWorldAsync();
        var signals = new[]
        {
            world.Seed(OldInvoiceKey("BILL-RATE"), SignalWriter.SignalStatusOpen, "[]"),
            world.Seed(OldInvoiceKey("BILL-HOURS"), SignalWriter.SignalStatusOpen, "[]"),
        }.OrderBy(s => s.Id).ToArray();
        var stuck = signals[1];
        string code;
        if (why == "changed")
        {
            // A write lands between the re-read and the close of the second Signal only.
            code = InvoiceSubmissionAfterSteps.SignalChangedCode;
            world.Entities.Setup(e => e.RetrieveAsync("sprk_signal", stuck.Id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid _, string[] columns, CancellationToken _) =>
                {
                    var copy = World.Copy(stuck, columns);
                    stuck.RowVersion = "2"; // after the re-read: the close sees a moved version
                    return copy;
                });
        }
        else
        {
            world.SupersedeFaults[stuck.Id] = new TimeoutException("the close timed out");
            code = InvoiceSubmissionAfterSteps.SupersedeSignalsFailedCode;
        }

        var result = await world.Build().RunAsync(Persisted(await Accepted()), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        result.ReasonCode.Should().Be(code);
        var warning = world.Log.Entries.Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("not closed (", StringComparison.Ordinal))
            .Should().ContainSingle().Subject.Message;
        warning.Should().Contain(Submission.ToString()).And.Contain(stuck.Id.ToString()).And.Contain(code);
        warning.Should().NotContain(signals[0].Id.ToString(), "the closed Signal is not listed");
        world.Superseded.Should().Equal(signals[0].Id);
    }

    [Theory(DisplayName = "097d: a read fault (the old invoice, or its open Signal set) -> Retry with nothing closed; the resume completes")]
    [InlineData("old-invoice")]
    [InlineData("open-set")]
    public async Task Correction_ReadFault_RetryThenResumeCompletes(string where)
    {
        var (world, invoices) = await CorrectionWorldAsync();
        if (where == "old-invoice")
        {
            invoices.OldInvoiceFault = new TimeoutException("the read timed out");
        }
        else
        {
            var faulted = false;
            world.OnSetRead = () =>
            {
                if (!faulted)
                {
                    faulted = true;
                    throw new TimeoutException("the read timed out");
                }
            };
        }

        var open = world.Seed(OldInvoiceKey("BILL-RATE"), SignalWriter.SignalStatusOpen, "[]");
        var sut = world.Build();

        var first = await sut.RunAsync(Persisted(await Accepted()), null);

        first.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        first.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.SupersedeSignalsFailedCode);
        world.Superseded.Should().BeEmpty();
        world.TerminalWrites.Should().BeEmpty();

        var resumed = await sut.ResumeAsync(Submission, null);

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Completed, resumed.ReasonCode);
        world.Superseded.Should().Equal(open.Id);
    }

    [Fact(DisplayName = "097d: a Signal that changed between its re-read and its close (ChangedSinceRead) is not written; Retry, and the resume closes it")]
    public async Task Correction_ChangedSinceRead_LeftAsIs_RetryThenResumeCloses()
    {
        var (world, _) = await CorrectionWorldAsync();
        var open = world.Seed(OldInvoiceKey("BILL-RATE"), SignalWriter.SignalStatusOpen, "[]");
        world.CloseChanges = true;
        var sut = world.Build();

        var first = await sut.RunAsync(Persisted(await Accepted()), null);

        first.Status.Should().Be(SubmissionAfterStepsStatus.Retry);
        first.ReasonCode.Should().Be(InvoiceSubmissionAfterSteps.SignalChangedCode);
        StatusOf(open).Should().Be(SignalWriter.SignalStatusOpen);
        open.RowVersion.Should().Be("1", "nothing was written");
        world.TerminalWrites.Should().BeEmpty();

        world.CloseChanges = false;
        var resumed = await sut.ResumeAsync(Submission, null);

        resumed.Status.Should().Be(SubmissionAfterStepsStatus.Completed, resumed.ReasonCode);
        world.Superseded.Should().Equal(open.Id);
    }

    [Theory(DisplayName = "097d: a target that no longer resolves (the earlier submission links another invoice) or an old invoice that no longer exists closes nothing and completes")]
    [InlineData("unresolved")]
    [InlineData("gone")]
    public async Task Correction_TargetUnresolvedOrGone_ClosesNothing(string why)
    {
        var (world, invoices) = await CorrectionWorldAsync();
        if (why == "unresolved")
        {
            invoices.EarlierLinks = ThirdInvoice;
        }
        else
        {
            invoices.OldInvoiceGone = true;
        }

        var open = world.Seed(OldInvoiceKey("BILL-RATE"), SignalWriter.SignalStatusOpen, "[]");

        var result = await world.Build().RunAsync(Persisted(await Accepted()), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Superseded.Should().BeEmpty();
        StatusOf(open).Should().Be(SignalWriter.SignalStatusOpen);
        world.TerminalWrites.Should().ContainSingle();
    }

    [Fact(DisplayName = "097d: the after-steps' Superseded close (the delegate the step calls) writes resolution Superseded, no person, no Decision Record, on the row version")]
    public async Task SupersedeClose_WritesResolutionSuperseded_NoPerson()
    {
        var (writer, org) = RealWriterOverFakeDataverse();
        var signalId = Guid.Parse("09700000-0000-4000-8000-0000000005d1");
        org.Rows[signalId] = new Entity("sprk_signal", signalId)
        {
            ["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusAcknowledged),
            RowVersion = "7",
        };
        var close = InvoiceSubmissionAfterSteps.SupersedeClose(writer);

        var stale = await close(signalId, "6", "BILL-RATE", CancellationToken.None);
        var outcome = await close(signalId, "7", "BILL-RATE", CancellationToken.None);

        stale.Should().Be(SignalCloseOutcome.ChangedSinceRead, "conditional on the row version read");
        outcome.Should().Be(SignalCloseOutcome.Closed);
        var row = org.Rows[signalId];
        StatusOf(row).Should().Be(SignalWriter.SignalStatusResolved);
        row.GetAttributeValue<OptionSetValue>("sprk_resolutiontype").Value.Should().Be(SignalWriter.ResolutionSuperseded);
        row.Contains("sprk_resolvedby").Should().BeFalse("a system resolution names no person");
        row.Contains("sprk_decisionrecord").Should().BeFalse();
    }

    [Fact(DisplayName = "097d: a New submission - 104's automatic link included - runs no supersede close and reads no invoice")]
    public async Task NewSubmission_With104Link_ClosesNothing()
    {
        // The new invoice links the old one (as 104 links a Returned to Firm predecessor) and the old one is even Superseded: no
        // sprk_resubmissionof, so the step does not run at all.
        var (world, invoices) = await CorrectionWorldAsync(correction: false);
        var open = world.Seed(OldInvoiceKey("BILL-RATE"), SignalWriter.SignalStatusOpen, "[]");

        var result = await world.Build().RunAsync(Persisted(await Accepted()), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Superseded.Should().BeEmpty();
        StatusOf(open).Should().Be(SignalWriter.SignalStatusOpen);
        invoices.InvoiceReads.Should().BeEmpty();
        world.Entities.Verify(e => e.RetrieveAsync(FinanceBaseValues.SubmissionEntity, Earlier, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory(DisplayName = "097d: an undone commit (the resume's gate failed or holds the undo) closes nothing")]
    [InlineData(SubmissionCommitGateStatus.Undone)]
    [InlineData(SubmissionCommitGateStatus.UndoHeld)]
    public async Task UndoneCommit_ClosesNothing(SubmissionCommitGateStatus gate)
    {
        var (world, invoices) = await CorrectionWorldAsync();
        world.CommitGate = (_, _) => Task.FromResult(new SubmissionCommitGateResult(gate, SubmissionReasonCodes.FileUnservable));
        var open = world.Seed(OldInvoiceKey("BILL-RATE"), SignalWriter.SignalStatusOpen, "[]");

        var result = await world.Build().ResumeAsync(Submission, null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Undone);
        world.Superseded.Should().BeEmpty();
        StatusOf(open).Should().Be(SignalWriter.SignalStatusOpen);
        invoices.InvoiceReads.Should().BeEmpty();
    }
}
