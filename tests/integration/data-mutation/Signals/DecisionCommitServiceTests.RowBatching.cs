using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Ontology task 205 (DI-146, #2650): a row decision (the decided item plus its Also resolve items, D-214) costs Dataverse a near-constant
/// number of calls, not about 18 more per item. Counted on this rig's fakes (the caller-identity client, the BFF's app-only service, the
/// writer's SDK seam and the caller-rights probe), never by wall time. Every per-item rule stays: each item is still read AS THE CALLER,
/// each close is still its own row-version update, one item's failure never stops another, and an access answer is reused only for the
/// same record and the same right.
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    /// <summary>A row of <paramref name="items"/> Decide items on one matter. By default every item's subject is that matter (one subject
    /// record, so the rights questions are the same for all of them); with <paramref name="ownSubjects"/> each item has a To Do of its own.</summary>
    private static Rig RowRig(int items, bool ownSubjects = false)
    {
        var rig = new Rig();
        rig.Signals.Remove(Primary);
        for (var i = 0; i < items; i++)
        {
            rig.AddSignal(i == 0 ? Primary : RowItem(i), DecisionLane.Decide, Matter, PathB,
                subjectEntity: ownSubjects ? "sprk_todo" : "sprk_matter", subjectId: ownSubjects ? RowSubject(i) : Matter);
        }

        return rig;
    }

    private static Guid RowSubject(int i) => Guid.Parse($"c0430000-0000-4000-8000-0000002052{i:D2}");

    private static Guid RowItem(int i) => Guid.Parse($"c0430000-0000-4000-8000-0000002050{i:D2}");

    private static IReadOnlyList<Guid> RowAlso(int items) => Enumerable.Range(1, items - 1).Select(RowItem).ToList();

    /// <summary>The Dataverse calls a commit made, by seam. The entity-set name is left out: the shipped service keeps it for 15 minutes
    /// (ADR-009 metadata), so it is not a call per commit, let alone per item.</summary>
    private sealed record WireCalls(int AsCaller, int AppOnly, int Writer, int Rights)
    {
        public int Total => AsCaller + AppOnly + Writer + Rights;

        public static WireCalls Of(Rig rig) => new(
            rig.User.Invocations.Count,
            rig.Entities.Invocations.Count(i => i.Method.Name != nameof(IGenericEntityService.GetEntitySetNameAsync)),
            rig.Org.Invocations.Count,
            rig.Probe.Invocations.Count(i => i.Method.Name == nameof(CallerRecordAccessProbe.GetCallerRightsAsync)));

        public override string ToString() =>
            $"total {Total} (as the caller {AsCaller}, app-only {AppOnly}, writer {Writer}, rights {Rights})";
    }

    private static async Task<(DecisionCommitResult Result, WireCalls Calls, Rig Rig)> CommitRowAsync(int items, bool ownSubjects = false)
    {
        var rig = RowRig(items, ownSubjects);
        var result = await rig.CommitAsync(PathBReview(also: items > 1 ? RowAlso(items) : null));
        return (result, WireCalls.Of(rig), rig);
    }

    [Fact]
    [Trait("status", "new")]
    public async Task T205_ARowDecision_CostsAtMostThreeDataverseCallsPerExtraItem_AndClosesEveryItem()
    {
        var (two, small, _) = await CommitRowAsync(2);
        var (eighteen, large, rig) = await CommitRowAsync(18);

        two.Status.Should().Be(DecisionCommitStatus.Recorded, two.Detail);
        eighteen.Status.Should().Be(DecisionCommitStatus.Recorded, eighteen.Detail);
        eighteen.Signals.Should().HaveCount(18).And.OnlyContain(s => s.State == "Acted" && s.Warning == null);
        eighteen.Signals.Select(s => s.SignalId).Should().Equal([Primary, .. RowAlso(18)], "the response lists the items in the review's order");
        rig.SignalUpdates.Should().HaveCount(18, "each item is still closed by its own row-version update");

        var perExtraItem = (large.Total - small.Total) / 16.0;
        perExtraItem.Should().BeLessThanOrEqualTo(3,
            $"DI-146: 2 items made {small}; 18 items made {large}");

        // The questions that do not depend on the item: the matter (as the caller), the rights on the one subject and the matter, and the
        // app-only Signal reads, each asked a fixed number of times however many items the row has.
        rig.User.Invocations.Count(i => ((string)i.Arguments[0]).StartsWith("sprk_matters(", StringComparison.Ordinal))
            .Should().Be(1, "the caller's read of the row's one matter is asked once per commit");
        large.Rights.Should().Be(small.Rights, "the rights on one subject record and one matter are asked once each");
        rig.Org.Invocations.Count(i => i.Method.Name == "RetrieveAsync")
            .Should().Be(0, "the row's Signals are read in pages, not one Retrieve per item");
        rig.User.Invocations.Count(i => ((string)i.Arguments[0]).StartsWith("sprk_signals(", StringComparison.Ordinal))
            .Should().Be(18, "each item is still read AS THE CALLER (its own sharing decides)");
    }

    [Fact]
    [Trait("status", "new")]
    public async Task T205_ARowWhoseItemsEachHaveTheirOwnSubject_AsksEachSubjectsRightOnce_AndStaysWithinThreeCallsPerExtraItem()
    {
        // The rights are per record and never borrowed, so a subject of its own adds its own Write question; nothing else grows.
        var (two, small, _) = await CommitRowAsync(2, ownSubjects: true);
        var (eighteen, large, rig) = await CommitRowAsync(18, ownSubjects: true);

        two.Status.Should().Be(DecisionCommitStatus.Recorded, two.Detail);
        eighteen.Status.Should().Be(DecisionCommitStatus.Recorded, eighteen.Detail);
        eighteen.Signals.Should().HaveCount(18).And.OnlyContain(s => s.State == "Acted" && s.Warning == null);
        (large.Rights - small.Rights).Should().Be(16, "one Write question per added subject");
        foreach (var i in Enumerable.Range(0, 18))
            rig.RightsAsked.Count(r => r.Equals(("sprk_todos", RowSubject(i)))).Should().Be(1, $"subject {i} is asked once");
        ((large.Total - small.Total) / 16.0).Should().BeLessThanOrEqualTo(3, $"2 items made {small}; 18 items made {large}");
    }

    [Fact]
    [Trait("status", "new")]
    public async Task T205_OneItemsRowVersionKeepsChanging_ItAloneIsReported_AndTheOtherItemsStillClose()
    {
        var rig = RowRig(18);
        var busy = RowItem(7);
        rig.BeforeSignalUpdate = id =>
        {
            if (id == busy) rig.Versions[id]++; // changed again between every read and its update
        };

        var result = await rig.CommitAsync(PathBReview(also: RowAlso(18)));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        // A row that changes again after its re-read gets the declared signal_changed outcome (DI-167, #3028), not a failed close.
        result.Signals.Single(s => s.SignalId == busy).Should().Be(new DecisionCommitSignal(busy, "Unchanged", "signal_changed"));
        result.Signals.Where(s => s.SignalId != busy).Should().HaveCount(17).And.OnlyContain(s => s.State == "Acted" && s.Warning == null);
        result.Warnings.Should().Equal(["signal_changed"]);
        rig.Status(busy).Should().Be(Open);
        foreach (var id in RowAlso(18).Where(id => id != busy).Prepend(Primary))
        {
            rig.Status(id).Should().Be(Resolved);
            rig.LinkedRecord(id).Should().Be(RecordId);
        }
    }

    private static readonly AsyncLocal<Guid> CloseMetricScope = new();

    /// <summary>The reasons metered on <c>ontology.writer.failures</c> by commits running in this test's async scope.</summary>
    private static (MeterListener Listener, List<string?> Reasons) ListenCloseFailures(Guid scope)
    {
        CloseMetricScope.Value = scope;
        var reasons = new List<string?>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == "ontology.writer.failures")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (CloseMetricScope.Value != scope) return;
            foreach (var tag in tags)
                if (tag.Key == "reason") lock (reasons) reasons.Add(tag.Value as string);
        });
        listener.Start();
        return (listener, reasons);
    }

    [Fact]
    [Trait("status", "new")]
    public async Task T208_AnItemThatChangesTwiceWhileItIsClosed_ReportsSignalChanged_AndIsNotCountedAsAWriterFailure()
    {
        var rig = new Rig();
        rig.BeforeSignalUpdate = id => rig.Versions[id]++; // changed again between every read and its update
        var (listener, reasons) = ListenCloseFailures(Guid.NewGuid());
        using var _ = listener;

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Signals.Single().Should().Be(new DecisionCommitSignal(Primary, "Unchanged", "signal_changed"));
        result.Warnings.Should().Equal(["signal_changed"]);
        rig.Status(Primary).Should().Be(Open);
        rig.SignalUpdates.Should().BeEmpty("the item was left as it is");
        reasons.Should().NotContain("decision_commit_signal_close_failed", "an expected race is not a writer failure");
    }

    [Fact]
    [Trait("status", "new")]
    public async Task T208_OneMismatchThenSuccess_StillClosesTheItem_AndMetersNothing()
    {
        var rig = new Rig();
        rig.BeforeSignalUpdate = id =>
        {
            rig.BeforeSignalUpdate = null;
            rig.Versions[id]++; // changed once only
        };
        var (listener, reasons) = ListenCloseFailures(Guid.NewGuid());
        using var _ = listener;

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Signals.Single().Should().Be(new DecisionCommitSignal(Primary, "Acted", null));
        rig.Status(Primary).Should().Be(Resolved);
        reasons.Should().BeEmpty();
    }

    [Fact]
    [Trait("status", "new")]
    public async Task T208_AMismatchThenAnotherKindOfFailure_IsStillASignalCloseFailed_AndCounted()
    {
        var rig = new Rig();
        var calls = 0;
        rig.BeforeSignalUpdate = id =>
        {
            if (calls++ == 0)
                rig.Versions[id]++; // attempt 0: a mismatch, so the item is read again
            else
                throw new TimeoutException("no answer"); // attempt 1: not a mismatch
        };
        var (listener, reasons) = ListenCloseFailures(Guid.NewGuid());
        using var _ = listener;

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Signals.Single().Should().Be(new DecisionCommitSignal(Primary, "Unchanged", "signal_close_failed"));
        rig.Status(Primary).Should().Be(Open);
        reasons.Should().Equal(["decision_commit_signal_close_failed"]);
    }

    [Fact]
    [Trait("status", "new")]
    public async Task T205_AnItemChangedAfterTheRowWasRead_IsReadAgain_AndClosedOnItsNewVersion()
    {
        var rig = RowRig(4);
        var changed = RowItem(2);
        rig.BeforeSignalUpdate = id =>
        {
            if (id != changed) return;
            rig.BeforeSignalUpdate = null;
            rig.Signals[id]["sprk_factsnapshot"] = "{\"variancePct\":14}";
            rig.Versions[id]++; // still open: only its version moved
        };

        var result = await rig.CommitAsync(PathBReview(also: RowAlso(4)));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Signals.Should().OnlyContain(s => s.State == "Acted" && s.Warning == null);
        rig.Status(changed).Should().Be(Resolved);
        rig.SignalUpdates.Count(u => u.Id == changed).Should().Be(1);
        rig.Org.Invocations.Count(i => i.Method.Name == "RetrieveAsync").Should().Be(1, "only the changed item is read again, by id");
    }

    [Fact]
    [Trait("status", "new")]
    public async Task T205_TheCallersReadOfOneMatter_IsNeverReusedForAnotherMatter()
    {
        // The also-resolve item sits on a matter the caller cannot read: the readability check denies it (the uniform 404) even though
        // the first item's matter was just read successfully. A reused answer may only be for the same record.
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, OtherMatter, PathB);
        rig.CallerCannotReadCore.Add(OtherMatter);

        var result = await rig.CommitAsync(PathBReview(also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.NotFound);
        rig.User.Invocations.Count(i => ((string)i.Arguments[0]).StartsWith($"sprk_matters({OtherMatter}", StringComparison.Ordinal))
            .Should().Be(1, "the other matter is asked about as the caller");
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    [Trait("status", "new")]
    public async Task T205_AnItemTheCallerCannotRead_IsDenied_ThoughItSharesTheRowsMatter()
    {
        var rig = RowRig(6);
        rig.CallerCannotRead.Add(RowItem(4));

        var result = await rig.CommitAsync(PathBReview(also: RowAlso(6)));

        result.Status.Should().Be(DecisionCommitStatus.NotFound, "each item's own read as the caller still decides");
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    [Trait("status", "new")]
    public async Task T205_AWriteDenialOnOneSubject_DeniesThatSubjectsItems_AndEachSubjectIsAskedOnce()
    {
        // Items on two subject records under one matter: the caller may write the first subject, not the second. The decision covers
        // the second subject's items, so it is refused as before (nothing written); each subject's right is asked once, never borrowed.
        var subjectA = Guid.Parse("c0430000-0000-4000-8000-0000002051aa");
        var subjectB = Guid.Parse("c0430000-0000-4000-8000-0000002051bb");
        var rig = new Rig();
        rig.Signals.Remove(Primary);
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_todo", subjectId: subjectA);
        for (var i = 1; i < 6; i++)
            rig.AddSignal(RowItem(i), DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_todo", subjectId: i <= 2 ? subjectA : subjectB);
        rig.Rights[("sprk_todos", subjectB)] = AccessRights.Read;

        var result = await rig.CommitAsync(PathBReview(also: RowAlso(6)));

        result.Status.Should().Be(DecisionCommitStatus.Forbidden);
        result.ReasonCode.Should().Be(DecisionCommitReasons.NotAuthorized);
        rig.RightsAsked.Count(r => r == ("sprk_todos", subjectA)).Should().Be(1);
        rig.RightsAsked.Count(r => r == ("sprk_todos", subjectB)).Should().Be(1);
        rig.Writes.Should().BeEmpty();

        var allowed = new Rig();
        allowed.Signals.Remove(Primary);
        allowed.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_todo", subjectId: subjectA);
        for (var i = 1; i < 6; i++)
            allowed.AddSignal(RowItem(i), DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_todo", subjectId: i <= 2 ? subjectA : subjectB);
        (await allowed.CommitAsync(PathBReview(also: RowAlso(6)))).Status.Should().Be(DecisionCommitStatus.Recorded, "control: both subjects granted");
    }
    [Fact]
    [Trait("status", "new")]
    public async Task T205_WhenThePagedReadFaultsBeforeTheRecord_NothingIsWritten_AndTheDecisionIsUnavailable()
    {
        var rig = RowRig(4);
        rig.SignalPageFaults = () => true;

        var result = await rig.CommitAsync(PathBReview(also: RowAlso(4)));

        result.Status.Should().Be(DecisionCommitStatus.Unavailable, result.Detail);
        result.ReasonCode.Should().Be(DecisionCommitReasons.SignalUnreadable, "the same answer the per-item read gave for an item it could not read");
        rig.Records.Should().BeEmpty();
        rig.SignalUpdates.Should().BeEmpty();
        foreach (var id in RowAlso(4).Prepend(Primary))
            rig.Status(id).Should().Be(Open);
    }

    [Fact]
    [Trait("status", "new")]
    public async Task T205_WhenThePagedReadFaultsAfterTheRecord_EachItemIsReadOnItsOwn_AndEveryItemStillCloses()
    {
        var rig = RowRig(4);
        rig.SignalPageFaults = () => rig.Records.Count > 0;

        var result = await rig.CommitAsync(PathBReview(also: RowAlso(4)));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Signals.Should().HaveCount(4).And.OnlyContain(s => s.State == "Acted" && s.Warning == null);
        rig.Org.Invocations.Count(i => i.Method.Name == "RetrieveAsync").Should().Be(4, "each item falls back to its own read by id");
        foreach (var id in RowAlso(4).Prepend(Primary))
        {
            rig.Status(id).Should().Be(Resolved);
            rig.LinkedRecord(id).Should().Be(RecordId);
        }
    }

    [Fact]
    [Trait("status", "new")]
    public async Task T205_WhenThePagedReadFaultsOnAReplay_TheOpenItemsAreReportedNotChecked_AndLeftOpen()
    {
        var rig = RowRig(3);
        FirstCloseFails(rig);
        var review = PathBReview(inquiry: false, todo: false, also: RowAlso(3));
        var first = await rig.CommitAsync(review);
        first.Status.Should().Be(DecisionCommitStatus.Recorded, first.Detail);
        var open = first.Signals.Where(s => s.Warning is not null).Select(s => s.SignalId).ToList();
        open.Should().ContainSingle("the first close failed and the others closed");

        rig.SignalPageFaults = () => true;
        var replay = await rig.CommitAsync(review);

        replay.Replayed.Should().BeTrue(replay.Detail);
        replay.Signals.Single(s => s.SignalId == open[0]).Warning.Should().Be("signal_close_not_checked");
        rig.Status(open[0]).Should().Be(Open);
        rig.Records.Should().ContainSingle("the replay writes no second record");
    }
    [Fact]
    [Trait("status", "new")]
    public async Task T205_TheClosesOverlap_AtMostCloseParallelismAtOnce_AndTheResultsAndWarningsKeepTheReviewsOrder()
    {
        const int items = 8;
        var rig = RowRig(items);
        var order = RowAlso(items).Prepend(Primary).ToList();
        // Later items answer sooner (the reverse of the review order), so completion order is not the review order.
        rig.SignalUpdateDelay = id => TimeSpan.FromMilliseconds((items - order.IndexOf(id)) * 25);
        var failing = RowItem(1);
        var reclosed = RowItem(6);
        var systemClosed = false;
        rig.BeforeSignalUpdate = id =>
        {
            if (id == failing)
                throw new TimeoutException("no answer");
            if (id != reclosed || systemClosed)
                return;
            systemClosed = true; // a Classification trigger closes it ConditionCleared between the read and the update
            rig.Signals[id]["sprk_signalstatus"] = new OptionSetValue(Resolved);
            rig.Signals[id]["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionConditionCleared);
            rig.Signals[id]["sprk_resolvedon"] = Now.UtcDateTime;
            rig.Versions[id]++;
        };

        var result = await rig.CommitAsync(PathBReview(also: RowAlso(items)));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.PeakSignalUpdatesInFlight.Should().BeGreaterThan(1, "the closes run at the same time")
            .And.BeLessThanOrEqualTo(DecisionCommitService.CloseParallelism, "never more than the bound at once");
        result.Signals.Select(s => s.SignalId).Should().Equal(order, "results are in the review's order, not the order closes finished");
        result.Signals.Single(s => s.SignalId == failing).Warning.Should().Be("signal_close_failed");
        result.Signals.Single(s => s.SignalId == reclosed).Warning.Should().Be("reclosed_after_system_resolution");
        result.Warnings.Should().Equal(["signal_close_failed", "reclosed_after_system_resolution"], "warnings follow the review's order");
        foreach (var id in order.Where(id => id != failing))
        {
            rig.Status(id).Should().Be(Resolved);
            rig.LinkedRecord(id).Should().Be(RecordId);
        }

        rig.Status(failing).Should().Be(Open);
    }
}
