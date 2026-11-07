using System.Data;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Domain.Signals.Billing;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095d-2b-2 (ontology D-201 095d-2 Q1 and Q2; brief section 3 step 4; T19, T20): the re-run's after-commit Signal steps,
/// over the same in-memory world as the commit, with the writer's calls faked as the writer behaves (dedupe key, conditional close, link).
/// </summary>
public sealed partial class InvoiceSubmissionRerunTests
{
    private static readonly string SystemCode = BillingRuleCatalogue.SystemPolicyCode;
    private static readonly Guid AssignFirmRecord = Guid.Parse("095d2b20-0000-4000-8000-0000000000a1");

    // ── Q1 / brief step 4: the close condition ──

    [Fact(DisplayName = "095d-2b-2 (Q1, T1): no hold reason left - the shared Signal is closed ConditionCleared and linked to the assign-firm record (resolvedBy = the decider, no expected prior); the state is done")]
    public async Task NoReasonLeft_ClosesTheSharedSignal_AndLinksTheAssignFirmRecord()
    {
        var world = await CommittedAsync();
        var shared = world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, "fp-firm-missing");

        var result = await world.Rerun().RemapSignalsAsync(SubmissionId);

        result.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);
        result.ConditionCleared.Should().BeTrue();
        world.SignalCloses.Should().Equal((shared.Id, SignalWriter.ResolutionConditionCleared));
        world.Links.Should().Equal((shared.Id, AssignFirmRecord, (Guid?)null, (Guid?)Reviewer));
        shared.GetAttributeValue<EntityReference>("sprk_decisionrecord").Id.Should().Be(AssignFirmRecord);
        world.RerunState().Should().Be(InvoiceSubmissionRerun.StateDone);
        world.Applied.Should().ContainSingle("the Signal steps never send a transaction: the commit is the only one");
    }

    [Fact(DisplayName = "095d-2b-2 (T19, T18): a firm hold on another linked submission is a reason left - the shared Signal stays open, nothing is closed or linked")]
    public async Task AReasonOnAnotherSubmission_LeavesTheSharedSignalOpen()
    {
        var world = await World.SeedAsync();
        var otherFindings = JsonNode.Parse(world.IntakeFindings!)!.AsObject();
        world.AddOtherSubmission(world.IntakeReason, otherFindings.ToJsonString());
        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        var shared = world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, "fp-firm-missing");

        var result = await world.Rerun().RemapSignalsAsync(SubmissionId);

        result.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);
        result.ConditionCleared.Should().BeFalse();
        world.SignalCloses.Should().BeEmpty("T19: never ConditionCleared while InvoiceOpenHolds.ReasonsAsync is not empty");
        world.Links.Should().BeEmpty();
        shared.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value.Should().Be(SignalWriter.SignalStatusOpen);
        world.RerunState().Should().Be(InvoiceSubmissionRerun.StateDone);
    }

    [Fact(DisplayName = "095d-2b-2 (EB-D-187, T19): a new hold (a Procedure gap) keeps the shared Signal open and re-maps it through the lifecycle (Superseded record-free, episode 2), never ConditionCleared")]
    public async Task ANewHold_KeepsTheSharedSignalOpen_AsTheNextEpisode()
    {
        var world = await World.SeedAsync();
        world.Processor = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Procedures = GappedProcedures() };
        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        var shared = world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, "fp-firm-missing");

        var result = await world.Rerun().RemapSignalsAsync(SubmissionId);

        result.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);
        world.SignalCloses.Should().Equal((shared.Id, SignalWriter.ResolutionSuperseded));
        world.Links.Should().BeEmpty();
        shared.GetAttributeValue<EntityReference>("sprk_decisionrecord").Should().BeNull("T20: the Superseded close writes no record");
        var episodes = world.SignalsOf(SystemCode, InvoiceId);
        episodes.Select(e => (e.Episode, e.Resolved)).Should().Equal((1, true), (2, false));
        episodes[1].Fingerprint.Should().Be(world.ExpectedInvoiceFingerprint(), "the new reason set (the gap), keyed by D-13");
    }

    [Fact(DisplayName = "095d-2b-2 (Q2): an unchanged invoice-level reason set refreshes the open Signal only - no Superseded close, no new episode")]
    public async Task AnUnchangedInvoiceReasonSet_RefreshesOnly()
    {
        var world = await World.SeedAsync();
        world.Processor = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Procedures = GappedProcedures() };
        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        var shared = world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, world.ExpectedInvoiceFingerprint());

        (await world.Rerun().RemapSignalsAsync(SubmissionId)).Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);

        world.SignalCloses.Should().BeEmpty();
        world.SignalsOf(SystemCode, InvoiceId).Should().ContainSingle().Which.Resolved.Should().BeFalse();
        shared.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value.Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Theory(DisplayName = "095d-2b-2 (Q2, line Signals): a line's changed reason set is Superseded and opens episode 2; an unchanged one is refreshed only")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALineReasonSet_SupersedesOnlyWhenItChanged(bool changed)
    {
        var world = await CommittedAsync();
        world.EditFindings(root => root["codingFindings"] = new JsonArray(new JsonObject
        {
            ["line"] = 0,
            ["refused"] = new JsonArray(new JsonObject { ["kind"] = "Task", ["code"] = "L999", ["reason"] = "OutsideCodeSet" }),
        }));
        var line = InvoiceSubmissionPersister.LineIdFor(SubmissionId, 0);
        var expected = world.ExpectedLineFingerprint(line);
        var prior = world.AddSignal(SystemCode, BillingFindingSignalMapper.LineEntity, line, 1, changed ? "fp-before" : expected);

        (await world.Rerun().RemapSignalsAsync(SubmissionId)).Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);

        var episodes = world.SignalsOf(SystemCode, line);
        if (changed)
        {
            world.SignalCloses.Should().Contain((prior.Id, SignalWriter.ResolutionSuperseded));
            episodes.Select(e => (e.Episode, e.Resolved, e.Fingerprint)).Should().Equal((1, true, "fp-before"), (2, false, expected));
        }
        else
        {
            world.SignalCloses.Should().NotContain(c => c.SignalId == prior.Id);
            episodes.Should().ContainSingle().Which.Resolved.Should().BeFalse();
        }
    }

    [Fact(DisplayName = "095d-2b-2: an open system Signal on a line of this submission with no finding left is closed ConditionCleared, record-free; a line of another submission is untouched")]
    public async Task AClearedLine_IsClosedConditionCleared_RecordFree()
    {
        var world = await CommittedAsync();
        var line = InvoiceSubmissionPersister.LineIdFor(SubmissionId, 0);
        var stale = world.AddSignal(SystemCode, BillingFindingSignalMapper.LineEntity, line, 1, "fp-before");
        var foreignLine = world.AddForeignLine();
        var foreign = world.AddSignal(SystemCode, BillingFindingSignalMapper.LineEntity, foreignLine, 1, "fp-other");

        (await world.Rerun().RemapSignalsAsync(SubmissionId)).Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);

        world.SignalCloses.Should().Contain((stale.Id, SignalWriter.ResolutionConditionCleared));
        world.SignalCloses.Should().NotContain(c => c.SignalId == foreign.Id, "a line another submission added keeps its Signal (fail toward open)");
        world.Links.Should().NotContain(l => l.SignalId == stale.Id, "only the shared Signal's close is linked (Q1)");
        stale.GetAttributeValue<EntityReference>("sprk_decisionrecord").Should().BeNull();
    }

    [Fact(DisplayName = "095d-2b-2 (Q2 must-not-fire): an invoice-level finding InvoiceOpenHolds does not count (an invoice-number mismatch) keeps the shared Signal out of the ConditionCleared close although ReasonsAsync is empty - no ConditionCleared close, no link")]
    public async Task AnInvoiceLevelFindingThatIsNoHoldReason_NeverClosesTheSharedSignal()
    {
        var world = await CommittedAsync();
        world.EditFindings(root => ((JsonArray)root["pipelineFindings"]!).Add(new JsonObject
        {
            ["reasonCode"] = SubmissionReasonCodes.InvoiceNumberMismatch,
            ["values"] = new JsonObject { ["typed"] = "INV-100", ["read"] = "INV-101" },
        }));
        var shared = world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, "fp-firm-missing");
        (await world.OpenHoldReasonsAsync())
            .Should().BeEmpty("the setup: the mismatch is no hold reason, so only the group guard keeps the close out");

        var result = await world.Rerun().RemapSignalsAsync(SubmissionId);

        result.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);
        result.ConditionCleared.Should().BeFalse();
        world.SignalCloses.Should().NotContain(c => c.Resolution == SignalWriter.ResolutionConditionCleared,
            "the group is not empty: a lifecycle write (Q2), never a ConditionCleared close");
        world.Links.Should().BeEmpty();
        shared.GetAttributeValue<EntityReference>("sprk_decisionrecord").Should().BeNull();
        world.SignalsOf(SystemCode, InvoiceId).Should().Contain(e => !e.Resolved, "the mismatch keeps a shared Signal open");
    }

    // ── Q1 must-not-fire: only this run's close is linked ──

    [Fact(DisplayName = "095d-2b-2 (Q1 must-not-fire): a shared Signal another path resolved before the step is not linked and not closed again")]
    public async Task ASharedSignalResolvedByAnotherPath_IsNotLinked()
    {
        var world = await CommittedAsync();
        var shared = world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, "fp-firm-missing",
            resolution: SignalWriter.ResolutionConditionCleared);

        (await world.Rerun().RemapSignalsAsync(SubmissionId)).Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);

        world.SignalCloses.Should().BeEmpty();
        world.Links.Should().BeEmpty("a Signal cleared by another path is never linked to the assign-firm record");
        shared.GetAttributeValue<EntityReference>("sprk_decisionrecord").Should().BeNull();
    }

    [Fact(DisplayName = "095d-2b-2 (Q1 must-not-fire): a shared Signal another path closes between the read and the close is not linked, now or on the retry")]
    public async Task ASharedSignalClosedUnderTheStep_IsNotLinkedOnTheRetry()
    {
        var world = await CommittedAsync();
        var shared = world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, "fp-firm-missing");
        world.BeforeClose = id =>
        {
            // Another path resolves it after the step read it: the version moves, so the conditional close is refused.
            var row = world.SignalRow(id);
            row["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
            row["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionConditionCleared);
            row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
        };

        var first = await world.Rerun().RemapSignalsAsync(SubmissionId);
        world.BeforeClose = null;
        var retry = await world.Rerun().RemapSignalsAsync(SubmissionId);

        first.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Retry);
        world.RerunStateObject()[InvoiceSubmissionRerun.ConditionClearedMember].Should().BeNull("the intent is withdrawn when the close does not land");
        retry.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);
        world.Links.Should().BeEmpty();
        shared.GetAttributeValue<EntityReference>("sprk_decisionrecord").Should().BeNull();
    }

    [Fact(DisplayName = "095d-2b-2 (Q1): a link that faults after the close is retried, and the retry links the Signal this run closed (once)")]
    public async Task ALinkFault_IsRetried_AndLinksTheSignalThisRunClosed()
    {
        var world = await CommittedAsync();
        var shared = world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, "fp-firm-missing");
        world.LinkFault = new InvalidOperationException("simulated link fault");

        var first = await world.Rerun().RemapSignalsAsync(SubmissionId);
        world.LinkFault = null;
        var retry = await world.Rerun().RemapSignalsAsync(SubmissionId);

        first.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Retry);
        retry.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);
        world.SignalCloses.Should().Equal((shared.Id, SignalWriter.ResolutionConditionCleared));
        world.Links.Should().Equal((shared.Id, AssignFirmRecord, (Guid?)null, (Guid?)Reviewer));
    }

    [Theory(DisplayName = "095d-2b-2 (K2): a link answered NotApplicable (re-closed by a person after this run's close) or ForeignLink (another record) is final - the run is done, but the result does not report a ConditionCleared close and the log says what happened")]
    [InlineData(SignalRecordLinkOutcome.NotApplicable)]
    [InlineData(SignalRecordLinkOutcome.ForeignLink)]
    public async Task ALinkThatDoesNotLand_IsNotReportedAsCleared(SignalRecordLinkOutcome how)
    {
        var world = await CommittedAsync();
        var shared = world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, "fp-firm-missing");
        world.BeforeLink = id =>
        {
            var row = world.SignalRow(id);
            if (how == SignalRecordLinkOutcome.NotApplicable)
            {
                row["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionActed); // a person's decision re-closed it
            }
            else
            {
                row["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", Guid.NewGuid()); // another record is linked
            }
        };

        var result = await world.Rerun().RemapSignalsAsync(SubmissionId);

        result.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);
        result.ConditionCleared.Should().BeFalse("only a Linked or AlreadyLinked close is reported");
        world.SignalCloses.Should().Equal((shared.Id, SignalWriter.ResolutionConditionCleared));
        world.Links.Should().BeEmpty();
        world.Logger.Entries.Should().NotContain(e => e.Message.Contains("closed ConditionCleared"));
        world.Logger.Entries.Should().Contain(e => e.Message.Contains("was not linked") && e.Message.Contains(how.ToString()));
        world.RerunState().Should().Be(InvoiceSubmissionRerun.StateDone);
    }

    [Fact(DisplayName = "095d-2b-2 (brief R10): the assign-firm record not found yet is a retry; nothing is closed")]
    public async Task NoAssignFirmRecord_IsARetry_NothingClosed()
    {
        var world = await CommittedAsync();
        world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, "fp-firm-missing");
        world.AssignFirmRecordId = null;

        var result = await world.Rerun().RemapSignalsAsync(SubmissionId);

        result.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Retry);
        result.Code.Should().Be(InvoiceSubmissionRerun.AssignFirmRecordMissingCode);
        world.SignalCloses.Should().BeEmpty();
        world.RerunState().Should().Be(InvoiceSubmissionRerun.StateCommitted);
    }

    // ── EB-D-187: a would-be auto-reject held for review ──

    [Fact(DisplayName = "095d-2b-2 (Q2, EB-D-187): a would-be rejection held for review raises its Signal with the D-13 key and claims no Decision Record; nothing is closed as Acted")]
    public async Task AWouldBeRejection_RaisesItsSignal_WithoutARecord()
    {
        var aged = new[] { InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20)) };
        var world = await World.SeedAsync(aged);
        world.Processor = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        var plan = InvoiceSubmissionAfterSteps.StoredFindings.Parse(world.Findings().ToJsonString())!.Plan;
        plan.Should().NotBeEmpty("the setup: the rule's finding is in the committed plan");

        (await world.Rerun().RemapSignalsAsync(SubmissionId)).Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);

        foreach (var entry in plan)
        {
            var key = SignalWriter.BuildDedupeKey(entry.Request.PolicyCode, entry.Request.SubjectEntityLogicalName,
                entry.Request.SubjectId.ToString("D").ToLowerInvariant(), 1);
            world.SignalRows().Should().Contain(r => r.GetAttributeValue<string>("sprk_dedupekey") == key, "D-13: the writer's dedupe key");
        }

        world.SignalRows().Should().OnlyContain(r => !r.Contains("sprk_decisionrecord"), "no Decision Record is claimed on a held finding");
        world.SignalCloses.Should().NotContain(c => c.Resolution == SignalWriter.ResolutionActed);
    }

    // ── Idempotent, faults, refusals ──

    [Fact(DisplayName = "095d-2b-2: a repeat after done writes nothing (no Signal, no close, no state write)")]
    public async Task ARepeatAfterDone_WritesNothing()
    {
        var world = await CommittedAsync();
        world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, "fp-firm-missing");
        (await world.Rerun().RemapSignalsAsync(SubmissionId)).Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);
        var before = world.Snapshot();
        var (writes, closes, links, states) = (world.SignalWrites.Count, world.SignalCloses.Count, world.Links.Count, world.StateWrites);

        var repeat = await world.Rerun().RemapSignalsAsync(SubmissionId);

        repeat.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.AlreadyDone);
        (world.SignalWrites.Count, world.SignalCloses.Count, world.Links.Count, world.StateWrites).Should().Be((writes, closes, links, states));
        world.Snapshot().Should().Equal(before);
    }

    [Fact(DisplayName = "095d-2b-2: a redelivery after a retryable fault writes no new Signal (the lifecycle refreshes; one close, one link)")]
    public async Task ARedeliveryAfterAFault_WritesNothingNew()
    {
        var world = await CommittedAsync();
        world.EditFindings(root => root["codingFindings"] = new JsonArray(new JsonObject
        {
            ["line"] = 0,
            ["refused"] = new JsonArray(new JsonObject { ["kind"] = "Task", ["code"] = "L999", ["reason"] = "OutsideCodeSet" }),
        }));
        var shared = world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, "fp-firm-missing");
        world.LinkFault = new InvalidOperationException("simulated link fault");
        (await world.Rerun().RemapSignalsAsync(SubmissionId)).Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Retry);
        var rows = world.SignalRows().Count;
        world.LinkFault = null;

        (await world.Rerun().RemapSignalsAsync(SubmissionId)).Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);

        world.SignalRows().Should().HaveCount(rows, "a redelivery creates no Signal row");
        world.SignalCloses.Should().ContainSingle(c => c.SignalId == shared.Id);
        world.Links.Should().ContainSingle();
    }

    [Fact(DisplayName = "095d-2b-2: a permanent writer refusal is recorded in the findings and sprk_adminattention (D-133); the run goes on and finishes")]
    public async Task APermanentRefusal_IsRecorded_AndTheRunFinishes()
    {
        var world = await CommittedAsync();
        world.EditFindings(root => root["codingFindings"] = new JsonArray(new JsonObject
        {
            ["line"] = 0,
            ["refused"] = new JsonArray(new JsonObject { ["kind"] = "Task", ["code"] = "L999", ["reason"] = "OutsideCodeSet" }),
        }));
        world.SignalWriteFault = request => request.SubjectEntityLogicalName == BillingFindingSignalMapper.LineEntity
            ? new ArgumentException("simulated refusal")
            : null;

        var result = await world.Rerun().RemapSignalsAsync(SubmissionId);

        result.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);
        result.Refused.Should().Be(1);
        world.Findings()[InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember].Should().BeOfType<JsonArray>().Which.Should().ContainSingle();
        world.Row(SubmissionId).GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention).Should().NotBeNullOrWhiteSpace();
        world.RerunState().Should().Be(InvoiceSubmissionRerun.StateDone);
    }

    [Fact(DisplayName = "095d-2b-2: a stored state that is not a committed re-run of this decision is refused permanently; nothing is written")]
    public async Task AStateNotCommitted_IsRefused()
    {
        var world = await World.SeedAsync();
        var before = world.Snapshot();

        var result = await world.Rerun().RemapSignalsAsync(SubmissionId);

        result.Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Refused);
        world.Snapshot().Should().Equal(before);
        world.SignalWrites.Should().BeEmpty();
        world.Logger.Entries.Should().Contain(e => e.Message.Contains("Permanent=True"));
    }

    // ── The job: a Signal fault never lifts or adds a hold ──

    [Fact(DisplayName = "095d-2b-2 (job): a Signal fault on the final delivery dead-letters (Poisoned); the faulting run and the replay leave the invoice (its firm hold, status, review detail), its lines and the submission's reason code as the commit left them; the state stays committed until the replay finishes")]
    public async Task ASignalFaultOnTheFinalDelivery_LeavesTheHold_AndAReplayFinishes()
    {
        var world = await World.SeedAsync();
        world.Processor = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Procedures = GappedProcedures() };
        world.AddSignal(SystemCode, BillingFindingSignalMapper.InvoiceEntity, InvoiceId, 1, "fp-firm-missing");
        world.SignalWriteFault = _ => new TimeoutException("simulated writer timeout");
        var harness = new JobHarness(world);

        var outcome = await harness.ProcessAsync(final: true);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        world.RerunState().Should().Be(InvoiceSubmissionRerun.StateCommitted);
        var invoice = world.Row(InvoiceId);
        invoice.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value.Should().Be(InvoiceReviewDetail.WaitingForBillingTerms, "the commit's hold stands");
        invoice.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview);
        // The faulting run itself: what RemapSignals found on entry (after the commit) is what it left.
        var atEntry = harness.RemapEntrySnapshots.Should().ContainSingle("the Signal steps ran once, after the commit").Subject;
        atEntry.Keys.Should().Contain(
            [$"{InvoiceId:D}.{InvoiceFirmHold.InvoiceHoldColumn}", $"{InvoiceId:D}.statuscode", $"{SubmissionId:D}.{InvoiceSubmissionPersister.SubmissionReasonCode}"],
            "the setup: the snapshot holds the firm hold, the status and the reason code");
        world.HoldSnapshot().Should().Equal(atEntry, "the faulting Signal step wrote no hold column");

        world.SignalWriteFault = null;
        var replayHarness = new JobHarness(world);
        var replay = await replayHarness.ProcessAsync(final: false);

        replay.Status.Should().Be(JobStatus.Completed);
        world.RerunState().Should().Be(InvoiceSubmissionRerun.StateDone);
        world.Applied.Should().ContainSingle("the replay never commits a second time");
        replayHarness.RemapEntrySnapshots.Should().ContainSingle().Which.Should().Equal(atEntry, "nothing moved the hold between the runs");
        world.HoldSnapshot().Should().Equal(atEntry, "the Signal steps never write the invoice, a line or the reason code");
    }

    [Fact(DisplayName = "095d-2b-2 (job): a refused Signal step is Poisoned with Permanent=true and counted; the commit stands")]
    public async Task ARefusedSignalStep_IsPoisoned()
    {
        var harness = new JobHarness((_, _, _, _) => Task.FromResult(Result(InvoiceSubmissionRerunStatus.Completed)))
        {
            RemapSignals = (_, _) => Task.FromResult(new InvoiceSubmissionRerunSignalsResult(InvoiceSubmissionRerunSignalsStatus.Refused, InvoiceSubmissionRerunCodes.FindingsUnreadable)),
        };

        var outcome = await harness.ProcessAsync(final: false);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        harness.Log.Entries.Should().Contain(e => e.Message.Contains("Permanent=True"));
    }

    [Fact(DisplayName = "095d-2b-2 re-check K2 (job): a refused Signal step after the commit is logged as leaving decisions refused as re-run pending until the data is repaired")]
    public async Task ARefusedSignalStep_LogsThatDecisionsStayRefused()
    {
        var harness = new JobHarness((_, _, _, _) => Task.FromResult(Result(InvoiceSubmissionRerunStatus.Completed)))
        {
            RemapSignals = (_, _) => Task.FromResult(new InvoiceSubmissionRerunSignalsResult(InvoiceSubmissionRerunSignalsStatus.Refused, InvoiceSubmissionRerunCodes.FindingsUnreadable)),
        };

        await harness.ProcessAsync(final: false);

        harness.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Message.Contains("Permanent=True")).Which.Message
            .Should().Contain("decisions on the invoice stay refused as re-run pending").And.Contain("until the data is repaired");
    }

    [Theory(DisplayName = "095d-2b-2 re-check K4 (job): a final delivery that finds the submission busy (lock held, or a Busy re-run) does not log that the invoice stays held - the row may be committed - and says decisions stay refused as re-run pending")]
    [InlineData("lock")]
    [InlineData("run")]
    public async Task ABusyFinalDelivery_DoesNotClaimTheInvoiceStaysHeld(string source)
    {
        var harness = new JobHarness((_, _, _, _) => Task.FromResult(Result(InvoiceSubmissionRerunStatus.Busy, InvoiceSubmissionRerunCodes.Busy)));
        if (source == "lock")
        {
            harness.Locks.BusyKeys.Add(InvoiceSubmissionProcessingJobHandler.LockKeyPrefix + SubmissionId.ToString("N"));
        }

        var outcome = await harness.ProcessAsync(final: true);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        var logged = harness.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject.Message;
        logged.Should().NotContain("stays held").And.Contain("decisions on the invoice stay refused as re-run pending");
    }

    [Fact(DisplayName = "095d-2b-2 re-check K4 (must not change): a non-Busy final delivery before the commit still logs that the invoice stays held")]
    public async Task AFailedFinalDelivery_BeforeTheCommit_StillLogsTheInvoiceStaysHeld()
    {
        var harness = new JobHarness((_, _, _, _) => Task.FromResult(Result(InvoiceSubmissionRerunStatus.Failed, "ebilling.rerun.failed")));

        await harness.ProcessAsync(final: true);

        harness.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.Message.Should().Contain("the invoice stays held");
    }

    [Fact(DisplayName = "095d-2b-2 (job): a retryable Signal step is a Failure (redelivered) before the final delivery")]
    public async Task ARetryableSignalStep_IsAFailure()
    {
        var harness = new JobHarness((_, _, _, _) => Task.FromResult(Result(InvoiceSubmissionRerunStatus.Completed)))
        {
            RemapSignals = (_, _) => Task.FromResult(new InvoiceSubmissionRerunSignalsResult(InvoiceSubmissionRerunSignalsStatus.Retry, "x")),
        };

        (await harness.ProcessAsync(final: false)).Status.Should().Be(JobStatus.Failed);
    }

    /// <summary>The seeded world after a completed commit (the clean outcome: no reason left).</summary>
    private static async Task<World> CommittedAsync()
    {
        var world = await World.SeedAsync();
        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed, "the setup: the commit landed");
        return world;
    }

    /// <summary>The Signal store and the writer's calls (095d-2b-2), faked as the writer behaves.</summary>
    private sealed partial class World
    {
        public List<(string PolicyCode, Guid SubjectId, int Episode)> SignalWrites { get; } = [];

        public List<(Guid SignalId, int Resolution)> SignalCloses { get; } = [];

        public List<(Guid SignalId, Guid RecordId, Guid? ExpectedPrior, Guid? ResolvedBy)> Links { get; } = [];

        public int StateWrites { get; private set; }

        public Func<SignalWriteRequest, Exception?>? SignalWriteFault { get; set; }

        public Exception? LinkFault { get; set; }

        /// <summary>Runs just before a close is applied (another path acting between the read and the close).</summary>
        public Action<Guid>? BeforeClose { get; set; }

        /// <summary>Runs just before a link is applied (another path acting between the close and the link).</summary>
        public Action<Guid>? BeforeLink { get; set; }

        /// <summary>What a Signal step must never write: every attribute of the invoice (its firm hold, status, review detail) and of its lines,
        /// and the submission's reason code and processing status. Row versions are left out.</summary>
        public Dictionary<string, string> HoldSnapshot()
        {
            var rows = new HashSet<string>(LineIds.Select(l => l.ToString("D"))) { InvoiceId.ToString("D") };
            var submission = new HashSet<string>
            {
                $"{SubmissionId:D}.{InvoiceSubmissionPersister.SubmissionReasonCode}", $"{SubmissionId:D}.{InvoiceSubmissionPersister.SubmissionStatus}",
            };
            return Snapshot()
                .Where(kv => !kv.Key.EndsWith(".versionnumber", StringComparison.Ordinal)
                    && (rows.Contains(kv.Key[..36]) || submission.Contains(kv.Key)))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        /// <summary><see cref="InvoiceOpenHolds.ReasonsAsync"/> over this world as stored now (the read the Signal step makes).</summary>
        public async Task<IReadOnlySet<string>> OpenHoldReasonsAsync()
        {
            var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression query, CancellationToken _) => Retrieve(query));
            return await InvoiceOpenHolds.ReasonsAsync(entities.Object, Row(InvoiceId), CancellationToken.None);
        }

        public Guid? AssignFirmRecordId { get; set; } = AssignFirmRecord;

        public BillingRuleCandidate? SystemPolicy { get; set; } =
            BillingEvaluationKit.Version(BillingRuleCatalogue.SystemPolicyCode, RuleType.SubmissionIntegrity, "{}", lane: SignalWriter.LaneDecide);

        public IReadOnlyList<Entity> SignalRows() => _rows.Values.Where(r => r.LogicalName == "sprk_signal").ToList();

        public Entity SignalRow(Guid id) => _rows[id];

        /// <summary>A line of the invoice whose id does not derive from this submission (another linked submission's line).</summary>
        public Guid AddForeignLine()
        {
            var foreign = Copy(_rows[LineIds[0]]);
            foreign.Id = Guid.NewGuid();
            _rows[foreign.Id] = foreign;
            return foreign.Id;
        }

        public string? RerunState() => RerunStateObject()["state"]?.GetValue<string>();

        public JsonObject RerunStateObject() => (JsonObject)Findings()[InvoiceSubmissionRerun.StateMember]!;

        public Entity AddSignal(string policyCode, string subjectEntity, Guid subjectId, int episode, string fingerprint, int? resolution = null)
        {
            var row = new Entity("sprk_signal", Guid.NewGuid())
            {
                ["sprk_dedupekey"] = SignalWriter.BuildDedupeKey(policyCode, subjectEntity, subjectId.ToString("D").ToLowerInvariant(), episode),
                ["sprk_signalstatus"] = new OptionSetValue(resolution is null ? SignalWriter.SignalStatusOpen : SignalWriter.SignalStatusResolved),
                ["sprk_evidencerefs"] = Evidence(fingerprint),
                ["sprk_regardingrecordid"] = subjectId.ToString("D"),
                [SignalWriter.RegardingLookupByEntity[subjectEntity]] = new EntityReference(subjectEntity, subjectId),
                ["versionnumber"] = 1L,
            };
            if (resolution is { } r)
            {
                row["sprk_resolutiontype"] = new OptionSetValue(r);
            }

            _rows[row.Id] = row;
            return row;
        }

        public List<(int Episode, bool Resolved, int? Resolution, string? Fingerprint)> SignalsOf(string policyCode, Guid subjectId) =>
            SignalRows()
                .Select(r => (Row: r, Ok: SignalWriter.TryParseDedupeKey(r.GetAttributeValue<string>("sprk_dedupekey"), out var code, out _, out var id, out var episode),
                    Code: code, Id: id, Episode: episode))
                .Where(x => x.Ok && x.Code == policyCode && x.Id == subjectId)
                .Select(x => (x.Episode,
                    x.Row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value == SignalWriter.SignalStatusResolved,
                    x.Row.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value,
                    SignalEpisodeLifecycle.FingerprintOf(x.Row.GetAttributeValue<string>("sprk_evidencerefs"))))
                .OrderBy(x => x.Episode)
                .ToList();

        /// <summary>The invoice-level reason-set fingerprint the committed findings map to (BillingFindingSignalMapper, as the step).</summary>
        public string ExpectedInvoiceFingerprint() => Mapped().Single(m => m.Request.SubjectEntityLogicalName == BillingFindingSignalMapper.InvoiceEntity).Fingerprint;

        public string ExpectedLineFingerprint(Guid lineId) => Mapped().Single(m => m.Request.SubjectId == lineId).Fingerprint;

        private IReadOnlyList<BillingSignalMapping> Mapped()
        {
            var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(Findings().ToJsonString())!;
            var rule = SystemPolicy!;
            var findings = stored.SystemFindings(new BillingSystemPolicyVersion(rule.PolicyId, rule.PolicyCode, rule.PolicyVersionId));
            var lines = findings.Where(f => f.Subject.LineIndex is not null).Select(f => f.Subject.LineIndex!.Value).Distinct().ToList();
            return BillingFindingSignalMapper.MapAll(findings, new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule },
                new BillingSignalSubjects(InvoiceId, stored.InvoiceName,
                    lines.ToDictionary(i => i, i => InvoiceSubmissionPersister.LineIdFor(SubmissionId, i)),
                    lines.ToDictionary(i => i, i => InvoiceSubmissionPersister.LineNameFor(stored.TrackingNumber, i))));
        }

        private static string Evidence(string fingerprint) =>
            "[{\"kind\":\"" + BillingFindingSignalMapper.FingerprintEvidenceKind + "\",\"ref\":\"" + fingerprint + "\"}]";

        private RerunSignalSeams SignalSeams() => new(WriteSignalAsync, CloseSignalAsync, LinkAsync,
            (invoice, decision, _) => Task.FromResult(invoice == InvoiceId && decision == DecisionId ? AssignFirmRecordId : null),
            (_, _) => Task.FromResult((SystemPolicy, SystemPolicy is null ? "missing" : "ok")));

        /// <summary>The writer's create-or-refresh by dedupe key (a row of the same key is refreshed in place and stays Open).</summary>
        private Task<SignalWriteResult> WriteSignalAsync(SignalWriteRequest request, CancellationToken ct)
        {
            if (SignalWriteFault?.Invoke(request) is { } fault)
            {
                return Task.FromException<SignalWriteResult>(fault);
            }

            SignalWrites.Add((request.PolicyCode, request.SubjectId, request.Episode));
            var key = SignalWriter.BuildDedupeKey(request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId.ToString("D").ToLowerInvariant(), request.Episode);
            var fingerprint = (request.EvidenceRefs ?? []).FirstOrDefault(e => e.Kind == BillingFindingSignalMapper.FingerprintEvidenceKind)?.Ref ?? string.Empty;
            var row = SignalRows().FirstOrDefault(r => r.GetAttributeValue<string>("sprk_dedupekey") == key);
            var created = row is null;
            if (row is null)
            {
                row = new Entity("sprk_signal", Guid.NewGuid())
                {
                    ["sprk_dedupekey"] = key,
                    ["sprk_regardingrecordid"] = request.SubjectId.ToString("D"),
                    [SignalWriter.RegardingLookupByEntity[request.SubjectEntityLogicalName]] = new EntityReference(request.SubjectEntityLogicalName, request.SubjectId),
                    ["versionnumber"] = 0L,
                };
                _rows[row.Id] = row;
            }

            row["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusOpen);
            row["sprk_evidencerefs"] = Evidence(fingerprint);
            row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
            return Task.FromResult(new SignalWriteResult(row.Id, created, Guid.Empty, Guid.Empty));
        }

        /// <summary>The writer's system close, conditional on the row version (ChangedSinceRead when it moved). Never a record.</summary>
        private Task<SignalCloseOutcome> CloseSignalAsync(Guid id, string rowVersion, int resolution, string policyCode, CancellationToken ct)
        {
            BeforeClose?.Invoke(id);
            var row = _rows[id];
            if (row.GetAttributeValue<long>("versionnumber").ToString(System.Globalization.CultureInfo.InvariantCulture) != rowVersion)
            {
                return Task.FromResult(SignalCloseOutcome.ChangedSinceRead);
            }

            row["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
            row["sprk_resolutiontype"] = new OptionSetValue(resolution);
            row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
            SignalCloses.Add((id, resolution));
            return Task.FromResult(SignalCloseOutcome.Closed);
        }

        /// <summary>The writer's link (LinkConditionClearedRecordAsync): only a Signal Resolved as ConditionCleared; the link it names, or none.</summary>
        private Task<SignalRecordLinkOutcome> LinkAsync(Guid signalId, Guid recordId, Guid? expectedPrior, Guid? resolvedBy, string policyCode, CancellationToken ct)
        {
            if (LinkFault is { } fault)
            {
                return Task.FromException<SignalRecordLinkOutcome>(fault);
            }

            BeforeLink?.Invoke(signalId);

            var row = _rows[signalId];
            var linked = row.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id;
            if (linked == recordId)
            {
                return Task.FromResult(SignalRecordLinkOutcome.AlreadyLinked);
            }

            if (row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value != SignalWriter.SignalStatusResolved
                || row.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value != SignalWriter.ResolutionConditionCleared)
            {
                return Task.FromResult(SignalRecordLinkOutcome.NotApplicable);
            }

            if (linked is not null && linked != expectedPrior)
            {
                return Task.FromResult(SignalRecordLinkOutcome.ForeignLink);
            }

            row["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", recordId);
            if (resolvedBy is { } by)
            {
                row["sprk_resolvedby"] = new EntityReference("systemuser", by);
            }

            row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
            Links.Add((signalId, recordId, expectedPrior, resolvedBy));
            return Task.FromResult(SignalRecordLinkOutcome.Linked);
        }

        /// <summary>IGenericEntityService.UpdateIfRowVersionMatchesAsync: applied only when the version matches, else DBConcurrencyException.</summary>
        private Task UpdateIfVersion(string entity, Guid id, string version, Dictionary<string, object> fields)
        {
            if (!_rows.TryGetValue(id, out var row) || row.LogicalName != entity
                || row.GetAttributeValue<long>("versionnumber").ToString(System.Globalization.CultureInfo.InvariantCulture) != version)
            {
                return Task.FromException(new DBConcurrencyException("simulated row version mismatch"));
            }

            foreach (var (name, value) in fields)
            {
                row[name] = value;
            }

            row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
            StateWrites++;
            return Task.CompletedTask;
        }
    }
}
