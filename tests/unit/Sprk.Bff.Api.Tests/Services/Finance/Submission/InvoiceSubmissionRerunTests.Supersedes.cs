using System.Data.SqlTypes;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095d-2b-3 (EB-D-206): the post-Set-firm re-run writes the automatic supersedes link in its own commit, by the persister's
/// rule (<see cref="InvoiceSubmissionPersister.FindSupersededInvoiceAsync"/>, task 104, EB-D-174) with the re-run's narrowings: (a) only
/// invoices created before this one, (c) only a predecessor whose Return was decided before this invoice was created (amended EB-D-206), and
/// (b) never a predecessor another invoice already supersedes, re-checked under a lock on the predecessor in the commit. Over the same in-memory
/// world as the commit.
/// </summary>
public sealed partial class InvoiceSubmissionRerunTests
{
    private static readonly DateTime BeforeThisInvoice = World.InvoiceCreatedOn.AddDays(-20);
    private static readonly DateTime JustBeforeThisInvoice = World.InvoiceCreatedOn.AddDays(-2);
    private static readonly DateTime AfterThisInvoice = World.InvoiceCreatedOn.AddDays(3);
    private static readonly Guid Predecessor = Guid.Parse("095d2b30-0000-4000-8000-0000000000a1");
    private static readonly Guid NewerEarlier = Guid.Parse("095d2b30-0000-4000-8000-0000000000a2");

    private static EntityReference? SupersedesIn(OrganizationRequest invoiceUpdate) =>
        ((UpdateRequest)invoiceUpdate).Target.GetAttributeValue<EntityReference>(InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn);

    // ── L1 ──

    [Fact(DisplayName = "095d-2b-3 L1 (EB-D-206): after Set firm, an earlier Returned to Firm invoice for the firm and number under the same core record is linked in the re-run's ONE conditional commit")]
    public async Task L1_AnEarlierReturnedInvoice_IsLinkedInTheCommit()
    {
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice);

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        var commit = world.Applied.Should().ContainSingle("the link is part of the one commit, never an after-commit write").Subject;
        var invoiceUpdate = (UpdateRequest)commit[1];
        invoiceUpdate.Target.Id.Should().Be(InvoiceId);
        invoiceUpdate.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        SupersedesIn(invoiceUpdate).Should().BeEquivalentTo(new EntityReference("sprk_invoice", Predecessor));
        world.Row(InvoiceId).GetAttributeValue<EntityReference>(InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn).Id.Should().Be(Predecessor);
        commit.Select(r => ((UpdateRequest)r).Target.Id).Should().NotContain(Predecessor, "the predecessor's own row is never written (D-43)");
        world.Row(Predecessor).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.ReturnedToFirm);
    }

    [Fact(DisplayName = "095d-2b-3 L1 control: with no earlier invoice for the firm and number, the lookup runs and nothing is linked")]
    public async Task L1_NoEarlierInvoice_IsNotLinked()
    {
        var world = await World.SeedAsync();

        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);

        world.PredecessorQueries.Should().ContainSingle();
        SupersedesIn(world.Applied.Single()[1]).Should().BeNull();
        ((UpdateRequest)world.Applied.Single()[1]).Target.Attributes.Should().NotContainKey(InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn);
    }

    // ── L2 ──

    [Fact(DisplayName = "095d-2b-3 L2 (EB-D-174): an automatic rejection newer than the Returned invoice is walked past, and the Returned one is linked")]
    public async Task L2_AnAutomaticRejection_IsSkipped_AndTheReturnedOneIsLinked()
    {
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice);
        world.AddEarlierInvoice(NewerEarlier, InvoiceStatusCodes.Rejected, JustBeforeThisInvoice, approval: false);

        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);

        SupersedesIn(world.Applied.Single()[1])!.Id.Should().Be(Predecessor);
    }

    [Fact(DisplayName = "095d-2b-3 L2 control: a human-Rejected most-recent invoice ends the chain - nothing is linked")]
    public async Task L2_AHumanRejection_EndsTheChain()
    {
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice);
        world.AddEarlierInvoice(NewerEarlier, InvoiceStatusCodes.Rejected, JustBeforeThisInvoice, approval: true);

        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);

        SupersedesIn(world.Applied.Single()[1]).Should().BeNull();
    }

    // ── L3: narrowing (a) ──

    [Theory(DisplayName = "095d-2b-3 L3 SEEDED (EB-D-206 a): a Returned to Firm invoice created AFTER this one is never linked; the same invoice created before it is")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task L3_OnlyAnInvoiceCreatedBefore_IsACandidate(bool createdBefore)
    {
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, createdBefore ? BeforeThisInvoice : AfterThisInvoice);

        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);

        if (createdBefore)
        {
            SupersedesIn(world.Applied.Single()[1])!.Id.Should().Be(Predecessor);
        }
        else
        {
            SupersedesIn(world.Applied.Single()[1]).Should().BeNull("a later resubmission is never this invoice's predecessor");
        }
    }

    // ── L4: narrowing (b) ──

    [Theory(DisplayName = "095d-2b-3 L4 (EB-D-206 b): a predecessor another invoice already supersedes is not linked; one with no other successor is")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task L4_APredecessorSupersededByAnother_IsNotLinked(bool supersededByAnother)
    {
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice);
        if (supersededByAnother)
        {
            // A later resubmission (created after this one, so not a candidate itself) took the link at its own create.
            world.AddEarlierInvoice(NewerEarlier, InvoiceStatusCodes.InReview, AfterThisInvoice, supersedes: Predecessor);
        }

        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);

        if (supersededByAnother)
        {
            SupersedesIn(world.Applied.Single()[1]).Should().BeNull("the predecessor keeps its one successor");
            world.Logger.Entries.Should().Contain(e => e.Message.Contains("already superseded by another invoice") && e.Message.Contains(Predecessor.ToString()));
        }
        else
        {
            SupersedesIn(world.Applied.Single()[1])!.Id.Should().Be(Predecessor);
        }
    }

    // ── L5 ──

    [Fact(DisplayName = "095d-2b-3 L5 SEEDED (D-43, InvoiceFamilyCoreRecordGuard): a Returned predecessor under another core record is not linked, and the log names ids only")]
    public async Task L5_APredecessorUnderAnotherCoreRecord_IsNotLinked_AndLogsIdsOnly()
    {
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice, otherCore: true);
        var number = world.Row(SubmissionId).GetAttributeValue<string>(TrackingNumberAllocator.FirmInvoiceNumberColumn);
        number.Should().NotBeNullOrWhiteSpace("the setup: the lookup runs only with a firm invoice number");

        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed, "the re-run still commits");

        SupersedesIn(world.Applied.Single()[1]).Should().BeNull();
        var entry = world.Logger.Entries.Should().ContainSingle(e => e.Message.Contains("filed under another core record")).Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(SubmissionId.ToString()).And.Contain(Predecessor.ToString());
        entry.Message.Should().NotContain(number).And.NotContain(Firm.ToString()).And.NotContain(World.OtherCoreId.ToString());
    }

    // ── L6 ──

    public enum NotLooked
    {
        Correction,
        AutomaticRejection,
        AlreadyLinked,
    }

    [Theory(DisplayName = "095d-2b-3 L6: a Correction submission, an automatic-reject outcome or an invoice that already has a link - no lookup, nothing written to the link")]
    [InlineData(NotLooked.Correction)]
    [InlineData(NotLooked.AutomaticRejection)]
    [InlineData(NotLooked.AlreadyLinked)]
    public async Task L6_NoLookup_AndNothingWrittenToTheLink(NotLooked when)
    {
        var aged = new[] { InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20)) };
        var world = when == NotLooked.AutomaticRejection ? await World.SeedAsync(aged) : await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice);
        var existing = new EntityReference("sprk_invoice", Guid.Parse("095d2b30-0000-4000-8000-0000000000e1"));
        switch (when)
        {
            case NotLooked.Correction:
                // The evaluation reads the replaced invoice (097b) through the processor's own read.
                var corrected = Guid.Parse("095d2b30-0000-4000-8000-0000000000c1");
                world.Row(SubmissionId)[InvoiceSubmissionService.ResubmissionOfColumn] = new EntityReference("sprk_invoicesubmission", corrected);
                world.Processor.Entities
                    .Setup(e => e.RetrieveAsync("sprk_invoicesubmission", corrected, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new Entity("sprk_invoicesubmission", corrected)
                    {
                        [InvoiceSubmissionService.InvoiceColumn] = new EntityReference("sprk_invoice", InvoiceSubmissionPersister.InvoiceIdFor(corrected)),
                    });
                break;
            case NotLooked.AutomaticRejection:
                world.Row(SubmissionId)["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Spa);
                world.Processor = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
                break;
            case NotLooked.AlreadyLinked:
                world.Row(InvoiceId)[InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn] = existing;
                break;
        }

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        world.PredecessorQueries.Should().BeEmpty("the lookup is not run");
        ((UpdateRequest)world.Applied.Single()[1]).Target.Attributes.Should().NotContainKey(InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn);
        world.Row(InvoiceId).GetAttributeValue<EntityReference>(InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn)
            .Should().BeEquivalentTo(when == NotLooked.AlreadyLinked ? existing : null, "an existing link is never re-linked");
        if (when == NotLooked.AutomaticRejection)
        {
            result.Kind.Should().Be(SubmissionEvaluationKind.Blocked, "the would-be rejection is held (R5), and still not linked");
        }
    }

    // ── L7 ──

    [Fact(DisplayName = "095d-2b-3 L7: a fault reading the predecessor is a retry with nothing committed (the marker stays); the replay links")]
    public async Task L7_AReadFault_IsARetry_AndTheReplayLinks()
    {
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice);
        var before = world.Snapshot();
        world.QueryFault = q => IsPredecessorQuery(q) ? new HttpRequestException("dataverse down") : null;

        var first = await world.RunAsync();

        first.Status.Should().Be(InvoiceSubmissionRerunStatus.Failed);
        first.Code.Should().Be(InvoiceSubmissionRerunCodes.SupersedesReadFailed);
        first.Permanent.Should().BeFalse("a missed link would be permanent, so it is retried");
        world.Attempts.Should().Be(0, "nothing is sent");
        world.Snapshot().Should().Equal(before);
        InvoiceFirmHold.StateOf(world.Row(InvoiceId)).Should().Be(InvoiceFirmHold.InvoiceHoldState.RerunPending);
        world.Logger.Entries.Should().Contain(e => e.Message.Contains(InvoiceSubmissionRerunCodes.SupersedesReadFailed) && !e.Message.Contains("dataverse down"));

        world.QueryFault = null;
        var replay = await world.RunAsync();

        replay.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        SupersedesIn(world.Applied.Single()[1])!.Id.Should().Be(Predecessor);
    }

    // ── L8 ──

    [Fact(DisplayName = "095d-2b-3 L8: a row-version conflict on the invoice update commits nothing; the re-evaluation re-reads and re-decides the link")]
    public async Task L8_AVersionConflict_CommitsNothing_AndTheReplayReDecides()
    {
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice);
        var sent = new List<IReadOnlyList<OrganizationRequest>>();
        world.BeforeCommit = requests =>
        {
            sent.Add(requests);
            if (sent.Count == 1)
            {
                // Between the read and the commit another invoice takes the link, and the invoice row moves on.
                world.AddEarlierInvoice(NewerEarlier, InvoiceStatusCodes.InReview, AfterThisInvoice, supersedes: Predecessor);
                var invoice = world.Row(InvoiceId);
                invoice["versionnumber"] = invoice.GetAttributeValue<long>("versionnumber") + 1;
            }
        };

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        world.Attempts.Should().Be(2, "the conflicting commit, then the re-evaluated one");
        SupersedesIn(sent[0][1])!.Id.Should().Be(Predecessor, "the first plan carried the link");
        world.Applied.Should().ContainSingle().Which.Should().BeSameAs(sent[1], "the first commit applied nothing");
        world.PredecessorQueries.Should().HaveCount(2, "the re-evaluation reads the predecessor again");
        SupersedesIn(sent[1][1]).Should().BeNull("re-decided: the predecessor now has another successor");
        world.Row(InvoiceId).GetAttributeValue<EntityReference>(InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn).Should().BeNull();
    }

    // ── L3 tie (K3): equal created on, the id in Dataverse's uniqueidentifier order decides ──

    [Theory(DisplayName = "095d-2b-3 L3 tie SEEDED (EB-D-206 a): with the same created on, a candidate is before this invoice only if its id sorts first in Dataverse's uniqueidentifier order (SqlGuid), not Guid.CompareTo")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task L3_ASameSecondCandidate_IsOrderedByTheSqlGuid(bool sortsFirst)
    {
        var candidate = TieId(sortsFirst);
        new SqlGuid(candidate).CompareTo(new SqlGuid(InvoiceId)).Should().Be(sortsFirst ? -1 : 1, "the setup: the SqlGuid order");
        Math.Sign(candidate.CompareTo(InvoiceId)).Should().Be(sortsFirst ? 1 : -1, "the setup: Guid.CompareTo disagrees, so only the SqlGuid order can pass");
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(candidate, InvoiceStatusCodes.ReturnedToFirm, World.InvoiceCreatedOn, returnedOn: World.InvoiceCreatedOn.AddSeconds(-1));

        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);

        if (sortsFirst)
        {
            SupersedesIn(world.Applied.Single()[1])!.Id.Should().Be(candidate);
        }
        else
        {
            SupersedesIn(world.Applied.Single()[1]).Should().BeNull("an invoice after this one in Dataverse's order is never its predecessor");
        }
    }

    /// <summary>A copy of <see cref="InvoiceId"/> one step before (or after) it in SqlGuid order (byte 10 first), and the other way in
    /// Guid.CompareTo order (byte 3 first).</summary>
    private static Guid TieId(bool sortsFirst)
    {
        var bytes = InvoiceId.ToByteArray();
        bytes[10] = (byte)(sortsFirst ? bytes[10] - 1 : bytes[10] + 1);
        bytes[3] = (byte)(sortsFirst ? bytes[3] + 1 : bytes[3] - 1);
        return new Guid(bytes);
    }

    // ── L9: narrowing (c), amended EB-D-206 ──

    public enum ReturnedWhen
    {
        BeforeThisInvoice,
        SameSecond,
        AfterThisInvoice,
    }

    [Theory(DisplayName = "095d-2b-3 L9 SEEDED (EB-D-206 c): a predecessor counts only if its Return to Firm was decided BEFORE this invoice was created; returned in the same second or later - no link")]
    [InlineData(ReturnedWhen.BeforeThisInvoice)]
    [InlineData(ReturnedWhen.SameSecond)]
    [InlineData(ReturnedWhen.AfterThisInvoice)]
    public async Task L9_OnlyAPredecessorReturnedBeforeThisInvoice_IsLinked(ReturnedWhen when)
    {
        var world = await World.SeedAsync();
        var returnedOn = when switch
        {
            ReturnedWhen.BeforeThisInvoice => World.InvoiceCreatedOn.AddSeconds(-1),
            ReturnedWhen.SameSecond => World.InvoiceCreatedOn,
            _ => World.InvoiceCreatedOn.AddDays(1),
        };
        // Created long before this invoice, but still In Review when this invoice was sent unless returned before it.
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice, returnedOn: returnedOn);

        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed, "the re-run still commits");

        world.ReturnRecordQueries.Should().ContainSingle("the Return time is read once, for the predecessor found");
        if (when == ReturnedWhen.BeforeThisInvoice)
        {
            SupersedesIn(world.Applied.Single()[1])!.Id.Should().Be(Predecessor);
        }
        else
        {
            SupersedesIn(world.Applied.Single()[1]).Should().BeNull("the predecessor was still In Review when this invoice was sent");
            world.Row(InvoiceId).GetAttributeValue<EntityReference>(InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn).Should().BeNull();
            var entry = world.Logger.Entries.Should().ContainSingle(e => e.Message.Contains("returned to the firm only after this invoice was created")).Subject;
            entry.Message.Should().Contain(SubmissionId.ToString()).And.Contain(Predecessor.ToString());
        }
    }

    [Fact(DisplayName = "095d-2b-3 L9 (EB-D-206 c): a Returned predecessor with no Return decision record is not linked, and the log names ids only")]
    public async Task L9_NoReturnRecord_IsNotLinked_AndLogsIdsOnly()
    {
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice, returnRecord: false);
        var number = world.Row(SubmissionId).GetAttributeValue<string>(TrackingNumberAllocator.FirmInvoiceNumberColumn);

        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed, "the re-run still commits");

        SupersedesIn(world.Applied.Single()[1]).Should().BeNull();
        var entry = world.Logger.Entries.Should().ContainSingle(e => e.Message.Contains("has no Return decision record")).Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(SubmissionId.ToString()).And.Contain(Predecessor.ToString());
        entry.Message.Should().NotContain(number!).And.NotContain(Firm.ToString()).And.NotContain(InvoiceId.ToString());
    }

    [Fact(DisplayName = "095d-2b-3 L9 (EB-D-206 c): a fault reading the Return record is the lookup's retry - Failed, nothing sent, nothing written")]
    public async Task L9_AReturnRecordReadFault_IsARetry_AndNothingIsWritten()
    {
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice);
        var before = world.Snapshot();
        world.QueryFault = q => IsReturnRecordQuery(q) ? new HttpRequestException("dataverse down") : null;

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Failed);
        result.Code.Should().Be(InvoiceSubmissionRerunCodes.SupersedesReadFailed);
        result.Permanent.Should().BeFalse();
        world.PredecessorQueries.Should().ContainSingle("the fault is in the Return read, after the predecessor was found");
        world.Attempts.Should().Be(0, "nothing is sent");
        world.Snapshot().Should().Equal(before);
        InvoiceFirmHold.StateOf(world.Row(InvoiceId)).Should().Be(InvoiceFirmHold.InvoiceHoldState.RerunPending);
    }

    // ── L10 (K2): the commit re-checks (b) under a lock on the predecessor ──

    [Fact(DisplayName = "095d-2b-3 L10 SEEDED (EB-D-206, amended): another re-run takes the predecessor before this commit - the re-check under the predecessor lock answers Changed, and the re-evaluation writes no link")]
    public async Task L10_APredecessorTakenBeforeTheCommit_IsChanged_AndTheReEvaluationDoesNotLink()
    {
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice);
        var predecessorKey = InvoiceSubmissionRerun.SupersedesLockKey(Predecessor);
        world.OnAcquire = key =>
        {
            if (key == predecessorKey && world.Locks.Acquired.Count(k => k == predecessorKey) == 1)
            {
                // The racing re-run committed its link just before this one took the key; this invoice's own row did not move.
                world.AddEarlierInvoice(NewerEarlier, InvoiceStatusCodes.InReview, AfterThisInvoice, supersedes: Predecessor);
            }
        };

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        world.Attempts.Should().Be(1, "the first commit is refused by the re-check before anything is sent");
        world.Locks.Acquired.Take(3).Should().Equal(
            new[] { SubmissionOverrideService.LockKey(InvoiceId), InvoiceLineDecisionService.LockKeyFor(InvoiceId), predecessorKey },
            "the predecessor lock is taken after the invoice's own locks");
        world.Locks.Released.Should().BeEquivalentTo(world.Locks.Acquired, "every lock taken is released");
        world.Logger.Entries.Should().Contain(e => e.Message.Contains("superseded by another invoice since the read") && e.Message.Contains(Predecessor.ToString()));
        world.PredecessorQueries.Should().HaveCount(2, "the re-evaluation reads the predecessor again");
        SupersedesIn(world.Applied.Single()[1]).Should().BeNull("narrowing (b) drops the link on the re-evaluation");
        world.Row(InvoiceId).GetAttributeValue<EntityReference>(InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn).Should().BeNull();
        world.Row(NewerEarlier).GetAttributeValue<EntityReference>(InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn).Id.Should().Be(Predecessor);
    }

    [Fact(DisplayName = "095d-2b-3 L10 (EB-D-206, amended): a fault in the commit's re-check is the lookup's retry - nothing sent, every lock released")]
    public async Task L10_ARecheckFault_IsARetry_AndNothingIsSent()
    {
        var world = await World.SeedAsync();
        world.AddEarlierInvoice(Predecessor, InvoiceStatusCodes.ReturnedToFirm, BeforeThisInvoice);
        var before = world.Snapshot();
        world.QueryFault = q => q.EntityName == "sprk_invoice"
            && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_invoiceid" && c.Operator == ConditionOperator.NotEqual)
            ? new HttpRequestException("dataverse down")
            : null;

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Failed);
        result.Code.Should().Be(InvoiceSubmissionRerunCodes.SupersedesReadFailed);
        world.Attempts.Should().Be(0);
        world.Snapshot().Should().Equal(before);
        world.Locks.Acquired.Should().Contain(InvoiceSubmissionRerun.SupersedesLockKey(Predecessor));
        world.Locks.Released.Should().BeEquivalentTo(world.Locks.Acquired);
    }

    private static bool IsReturnRecordQuery(QueryExpression q) =>
        q.EntityName == "sprk_decisionrecord" && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_regardinginvoice")
        && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_actioncode" && c.Values.Contains(InvoiceRejectReturnService.ReturnActionCode));

    private static bool IsPredecessorQuery(QueryExpression q) =>
        q.EntityName == "sprk_invoice" && q.Criteria.Conditions.Any(c => c.AttributeName == InvoiceSubmissionPersister.InvoiceVendorOrg)
        && q.Criteria.Conditions.Any(c => c.AttributeName == InvoiceReviewService.InvInvoiceNumber);

    /// <summary>095d-2b-3: the firm's other invoices, their approval rows, and the predecessor lookup's reads.</summary>
    internal sealed partial class World
    {
        /// <summary>The seeded invoice's <c>createdon</c>.</summary>
        public static readonly DateTime InvoiceCreatedOn = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

        /// <summary>The core record <see cref="AddEarlierInvoice"/> files an invoice under when told <c>otherCore</c>.</summary>
        public static readonly Guid OtherCoreId = Guid.Parse("095d2b30-0000-4000-8000-0000000000d1");

        /// <summary>The predecessor lookups the re-run sent (firm and invoice number).</summary>
        public List<QueryExpression> PredecessorQueries { get; } = [];

        /// <summary>A fault for a query (null: answered).</summary>
        public Func<QueryExpression, Exception?>? QueryFault { get; set; }

        /// <summary>Runs as each transaction is sent, before its version checks.</summary>
        public Action<IReadOnlyList<OrganizationRequest>>? BeforeCommit { get; set; }

        /// <summary>Runs as each processing lock is taken (fix round: the predecessor lock).</summary>
        public Action<string>? OnAcquire { get; set; }

        /// <summary>The Return-time reads the re-run sent (narrowing c).</summary>
        public List<QueryExpression> ReturnRecordQueries { get; } = [];

        /// <summary>Another invoice of this firm with this submission's firm invoice number, filed under the seeded invoice's core record
        /// (or another one). Every invoice has its approval row except an automatic rejection (<paramref name="approval"/> false). A Returned
        /// to Firm one also has its Return decision record (unless <paramref name="returnRecord"/> is false), decided at
        /// <paramref name="returnedOn"/> (default: a day after it was created).</summary>
        public void AddEarlierInvoice(
            Guid id, int status, DateTime createdOn, bool approval = true, bool otherCore = false, Guid? supersedes = null,
            DateTime? returnedOn = null, bool returnRecord = true)
        {
            var row = new Entity("sprk_invoice", id)
            {
                ["statuscode"] = new OptionSetValue(status),
                ["createdon"] = createdOn,
                [InvoiceSubmissionPersister.InvoiceVendorOrg] = new EntityReference("sprk_organization", Firm),
                [InvoiceReviewService.InvInvoiceNumber] = Row(SubmissionId).GetAttributeValue<string>(TrackingNumberAllocator.FirmInvoiceNumberColumn),
                ["versionnumber"] = 5L,
            };
            if (otherCore)
            {
                row["sprk_project"] = new EntityReference("sprk_project", OtherCoreId);
            }
            else
            {
                foreach (var lookup in InvoiceFamilyCoreRecordGuard.CoreRecordLookups["sprk_invoice"])
                {
                    if (Row(InvoiceId).GetAttributeValue<EntityReference>(lookup.Column) is { } core)
                    {
                        row[lookup.Column] = core;
                    }
                }
            }

            if (supersedes is { } predecessor)
            {
                row[InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn] = new EntityReference("sprk_invoice", predecessor);
            }

            _rows[id] = row;
            if (status == InvoiceStatusCodes.ReturnedToFirm && returnRecord)
            {
                // The Return's Decision Record, as BillingDecisionRecorder files it: under the invoice, code return-to-firm.
                var record = new Entity("sprk_decisionrecord", Guid.NewGuid())
                {
                    ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", id),
                    ["sprk_actioncode"] = InvoiceRejectReturnService.ReturnActionCode,
                    ["sprk_decidedon"] = returnedOn ?? createdOn.AddDays(1),
                    ["createdon"] = (returnedOn ?? createdOn.AddDays(1)).AddSeconds(2),
                };
                _rows[record.Id] = record;
            }

            if (approval)
            {
                var approvalRow = new Entity(InvoiceSubmissionPersister.ApprovalEntity, Guid.NewGuid())
                {
                    [InvoiceSubmissionPersister.ApprovalInvoice] = new EntityReference("sprk_invoice", id),
                };
                _rows[approvalRow.Id] = approvalRow;
            }
        }

        /// <summary>The predecessor lookup: recorded, faulted when told, and ordered as Dataverse orders it (created on, then the id as a
        /// uniqueidentifier, <see cref="SqlGuid"/>, not <see cref="Guid.CompareTo(Guid)"/>).</summary>
        private IEnumerable<Entity> Observe(QueryExpression query, IEnumerable<Entity> rows)
        {
            if (QueryFault?.Invoke(query) is { } fault)
            {
                throw fault;
            }

            if (IsReturnRecordQuery(query))
            {
                ReturnRecordQueries.Add(query);
            }

            if (!IsPredecessorQuery(query))
            {
                return rows;
            }

            PredecessorQueries.Add(query);
            IOrderedEnumerable<Entity>? ordered = null;
            foreach (var order in query.Orders)
            {
                object? Key(Entity e) => order.AttributeName == "sprk_invoiceid" ? new SqlGuid(e.Id) : e.Contains(order.AttributeName) ? e[order.AttributeName] : null;
                ordered = ordered is null
                    ? (order.OrderType == OrderType.Descending ? rows.OrderByDescending(Key) : rows.OrderBy(Key))
                    : (order.OrderType == OrderType.Descending ? ordered.ThenByDescending(Key) : ordered.ThenBy(Key));
            }

            return (ordered ?? rows).ToList();
        }
    }
}
