using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// #2085 (DI-116, ontology task 224): the deleted-rule evidence redaction (task 102b fix round 4) finds the codes of deleted policies by reading
/// every Signal whose policy lookup is empty, any status, by id, at most <see cref="SignalEvaluator.MaxPages"/> pages a run. Nothing deletes
/// Signals, so before this fix a space larger than one run's bound was re-read from the same first row every night: codes that sort later
/// were never redacted, and every run failed. Now the read continues after the last id an earlier run read (a cursor in the distributed
/// cache), wraps to the start when a pass reaches the end, and a missing or unreadable cursor starts from the beginning. Reaching the bound is
/// a Warning with the counts, and fails the run only when where it stopped cannot be recorded.
/// These run the REAL job, closure service, suppression service and writer over the job harness (one row per page, so the bound is
/// <see cref="SignalEvaluator.MaxPages"/> rows).
/// </summary>
public partial class SignalReevaluationJobTests
{
    private const string GoneEarly = "POL-GONE-A";
    private const string GoneLate = "POL-GONE-Z";

    /// <summary>A distributed cache whose reads, writes or removes of one key fault (a Redis outage), over a real in-memory cache.</summary>
    private sealed class FaultingCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));
        public string? FailGetOf { get; set; }
        public string? FailSetOf { get; set; }
        public string? FailRemoveOf { get; set; }

        private static void Fault(string? failing, string key)
        {
            if (failing == key) throw new TimeoutException("Redis timed out.");
        }

        public byte[]? Get(string key) { Fault(FailGetOf, key); return _inner.Get(key); }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) { Fault(FailGetOf, key); return _inner.GetAsync(key, token); }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { Fault(FailSetOf, key); _inner.Set(key, value, options); }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Fault(FailSetOf, key);
            return _inner.SetAsync(key, value, options, token);
        }

        public void Refresh(string key) => _inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);
        public void Remove(string key) { Fault(FailRemoveOf, key); _inner.Remove(key); }
        public Task RemoveAsync(string key, CancellationToken token = default) { Fault(FailRemoveOf, key); return _inner.RemoveAsync(key, token); }
    }

    /// <summary>Each read of the deleted-policy codes (no policy lookup, no status condition, no stored-detail filter): the id it continues
    /// after (null: from the start) and its page number. Also records every per-policy prefix read, as <see cref="ObservePerPolicySignalReads"/>.</summary>
    private static (List<(Guid? After, int Page)> CodeReads, List<string> PerPolicy) ObserveDeletedCodeReads(
        Harness h, Func<QueryExpression, Exception?>? fault = null)
    {
        var codeReads = new List<(Guid?, int)>();
        var perPolicy = new List<string>();
        h.Sysadmin.FaultOn = q =>
        {
            if (q.EntityName == "sprk_signal")
            {
                if (q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_dedupekey") is { } key) perPolicy.Add((string)key.Values[0]);
                if (q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_policy" && c.Operator == ConditionOperator.Null)
                    && q.Criteria.Conditions.All(c => c.AttributeName != "sprk_signalstatus" && c.Operator != ConditionOperator.Like)
                    && q.Criteria.Filters.Count == 0)
                {
                    var after = q.Criteria.Conditions.SingleOrDefault(c => c.AttributeName == "sprk_signalid" && c.Operator == ConditionOperator.GreaterThan);
                    codeReads.Add((after is null ? null : (Guid)after.Values[0], q.PageInfo?.PageNumber ?? 0));
                }
            }

            return fault?.Invoke(q);
        };
        return (codeReads, perPolicy);
    }

    /// <summary>
    /// <see cref="SignalEvaluator.MaxPages"/> + 1 Resolved Signals with no policy lookup (one per page, so one more than a run reads). The first
    /// by id is deleted policy <see cref="GoneEarly"/>'s and still quotes a witness since moved behind secure X's wall; the LAST by id (past the
    /// first run's bound) is deleted policy <see cref="GoneLate"/>'s, with the same detail; every other one is <see cref="GoneEarly"/>'s with its
    /// detail already cleared. Returns the ids in id order.
    /// </summary>
    private static List<Guid> SeedPastTheBound(Harness h, string secureSuffix)
    {
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-000000000" + secureSuffix);
        OrdinaryMatterBesideSecureX(h, secureProject);
        var ordered = Enumerable.Range(1, SignalEvaluator.MaxPages + 1)
            .Select(i => SeedEvidenceSignal(h, GoneEarly, policyId: null, EvidenceAlreadyRedacted(), episode: i))
            .OrderBy(id => id).ToList();
        h.Org.Rows[ordered[0]]["sprk_evidencerefs"] = StoredEvidenceQuotingTheWitness();
        h.Org.Rows[ordered[^1]]["sprk_dedupekey"] = Key(GoneLate, "sprk_matter", MatterId, 1);
        h.Org.Rows[ordered[^1]]["sprk_evidencerefs"] = StoredEvidenceQuotingTheWitness();
        RefileTheWitnessUnder(h, secureProject);
        h.Sysadmin.PageSizeCap = 1;
        return ordered;
    }

    private static async Task<string?> CursorOf(Harness h) =>
        await h.Cache.GetStringAsync(SignalReevaluationJob.DeletedPolicyRedactionCursorKey);

    [Fact]
    public async Task Run_MoreSignalsWithNoPolicyThanOneRunReads_TwoRunsTogetherRedactEveryDeletedCode_AndTheThirdStartsOver()
    {
        // Must-fire (#2085). Before the fix every run read the same first MaxPages Signals: GoneLate was never redacted and each run failed.
        var h = new Harness().WithMatterHit(MatterId);
        var ordered = SeedPastTheBound(h, "224");
        var early = ordered[0];
        var late = ordered[^1];
        var alreadyRedacted = ordered.Skip(1).SkipLast(1).ToList();
        var versions = alreadyRedacted.ToDictionary(id => id, h.Org.VersionOf);
        var (codeReads, perPolicy) = ObserveDeletedCodeReads(h);

        // Run 1: from the start, to the bound.
        var first = await h.RunAsync();

        first.Success.Should().BeTrue("reaching the bound with the place recorded is progress, not a failed run: " + first.ErrorMessage);
        codeReads.Should().HaveCount(SignalEvaluator.MaxPages).And.OnlyContain(r => r.After == null, "no cursor yet: the read starts at the start");
        EvidenceWrites(h).Should().Equal(new[] { (early, EvidenceAlreadyRedacted()) }, "run 1 redacts the code it reached, exactly as before");
        h.Org.Rows[late].GetAttributeValue<string>("sprk_evidencerefs").Should().Be(StoredEvidenceQuotingTheWitness(), "past run 1's bound");
        (await CursorOf(h)).Should().Be(ordered[SignalEvaluator.MaxPages - 1].ToString("D"), "the last Signal run 1 read");
        var bound = h.Log.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Message.Contains("the next run continues after Signal", StringComparison.Ordinal)).Subject.Message;
        bound.Should().Contain($"({SignalEvaluator.MaxPages} pages of {SignalEvaluator.PageSize})")
            .And.Contain($"this run read {SignalEvaluator.MaxPages} Signal(s) from the start").And.Contain("found 1 deleted policy code(s)")
            .And.Contain(ordered[SignalEvaluator.MaxPages - 1].ToString("D"));

        // Run 2: continues after run 1's last Signal, reaches the end, and clears the cursor.
        codeReads.Clear();
        perPolicy.Clear();
        var second = await h.RunAsync();

        second.Success.Should().BeTrue(second.ErrorMessage);
        codeReads.Should().Equal(new[] { ((Guid?)ordered[SignalEvaluator.MaxPages - 1], 1) }, "only the Signals after run 1's last one are read");
        perPolicy.Should().Equal(new[] { SignalWriter.DedupeKeyPolicyPrefix(GoneLate) }, "the code past run 1's bound is swept now");
        h.Org.Rows[late].GetAttributeValue<string>("sprk_evidencerefs").Should().Be(EvidenceAlreadyRedacted(), "redacted exactly as run 1 redacted the first");
        EvidenceWrites(h).Select(w => w.Id).Should().Equal(early, late);
        (await CursorOf(h)).Should().BeNull("the pass reached the end: the next run starts at the start again");
        h.Log.Should().Contain(e => e.Level == LogLevel.Information
            && e.Message.Contains("reached the last Signal with no policy lookup", StringComparison.Ordinal));

        // Run 3: wraps to the start; nothing is left to redact, so nothing is written.
        codeReads.Clear();
        var writesBefore = h.Org.ConditionalUpdates.Count;
        var third = await h.RunAsync();

        third.Success.Should().BeTrue(third.ErrorMessage);
        codeReads.Should().HaveCount(SignalEvaluator.MaxPages).And.OnlyContain(r => r.After == null, "a new pass starts at the start");
        h.Org.ConditionalUpdates.Count.Should().Be(writesBefore, "already-redacted evidence is never rewritten (D-61)");
        alreadyRedacted.Should().OnlyContain(id => h.Org.VersionOf(id) == versions[id], "a Signal with nothing left to clear is never written");
    }

    [Fact]
    public async Task Run_UnderTheBound_ReadsFromTheStart_LeavesNoCursor_AndRedactsAsBefore()
    {
        // Must-not-fire: a space smaller than one run's bound is read exactly as before: one read from the start, no cursor written.
        var h = new Harness().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-000000000225");
        OrdinaryMatterBesideSecureX(h, secureProject);
        var redacts = SeedEvidenceSignal(h, GoneEarly, policyId: null, StoredEvidenceQuotingTheWitness());
        var untouched = SeedEvidenceSignal(h, GoneLate, policyId: null, EvidenceAlreadyRedacted());
        RefileTheWitnessUnder(h, secureProject);
        var version = h.Org.VersionOf(untouched);
        var (codeReads, _) = ObserveDeletedCodeReads(h);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        codeReads.Should().Equal(new[] { ((Guid?)null, 1) });
        EvidenceWrites(h).Should().Equal(new[] { (redacts, EvidenceAlreadyRedacted()) });
        h.Org.VersionOf(untouched).Should().Be(version);
        (await CursorOf(h)).Should().BeNull();
        h.Log.Should().NotContain(e => e.Message.Contains("Signals with no policy lookup exist than one run reads", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("fault")]
    public async Task Run_ACursorThatCannotBeRead_StartsFromTheStart_AndSkipsNothing(string stored)
    {
        // Must-not-fire: an unreadable cursor (garbage, the empty id, or a cache fault) is no cursor. The first Signal by id is read and redacted.
        var cache = new FaultingCache();
        var h = new Harness(cache: cache).WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-000000000226");
        OrdinaryMatterBesideSecureX(h, secureProject);
        var redacts = SeedEvidenceSignal(h, GoneEarly, policyId: null, StoredEvidenceQuotingTheWitness());
        RefileTheWitnessUnder(h, secureProject);
        if (stored == "fault") cache.FailGetOf = SignalReevaluationJob.DeletedPolicyRedactionCursorKey;
        else await cache.SetStringAsync(SignalReevaluationJob.DeletedPolicyRedactionCursorKey, stored);
        var (codeReads, _) = ObserveDeletedCodeReads(h);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        codeReads.Should().Equal(new[] { ((Guid?)null, 1) }, "an unreadable cursor reads from the start");
        EvidenceWrites(h).Should().Equal(new[] { (redacts, EvidenceAlreadyRedacted()) });
        if (stored == "fault")
        {
            h.Log.Should().Contain(e => e.Level == LogLevel.Warning
                && e.Message.Contains("cursor could not be read; this run starts at the start", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Run_ACursorPastEverySignal_ReadsNothingThisRun_ClearsIt_AndTheNextRunStartsOver()
    {
        // Must-not-fire: a cursor past the last Signal (a pass that just ended) skips nothing: the run finds the end, clears it, and the
        // next run reads from the start.
        var h = new Harness().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-000000000227");
        OrdinaryMatterBesideSecureX(h, secureProject);
        var redacts = SeedEvidenceSignal(h, GoneEarly, policyId: null, StoredEvidenceQuotingTheWitness());
        RefileTheWitnessUnder(h, secureProject);
        await h.Cache.SetStringAsync(SignalReevaluationJob.DeletedPolicyRedactionCursorKey, "ffffffff-ffff-ffff-ffff-ffffffffffff");
        var (codeReads, _) = ObserveDeletedCodeReads(h);

        var first = await h.RunAsync();

        first.Success.Should().BeTrue(first.ErrorMessage);
        codeReads.Should().Equal(new[] { ((Guid?)Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), 1) });
        EvidenceWrites(h).Should().BeEmpty("nothing after the cursor");
        (await CursorOf(h)).Should().BeNull();

        codeReads.Clear();
        var second = await h.RunAsync();

        second.Success.Should().BeTrue(second.ErrorMessage);
        codeReads.Should().Equal(new[] { ((Guid?)null, 1) });
        EvidenceWrites(h).Should().Equal(new[] { (redacts, EvidenceAlreadyRedacted()) });
    }

    [Fact]
    public async Task Run_TheBoundReached_AndTheCursorCannotBeSaved_IsAFailedRun_ThatStillRedactsWhatItRead()
    {
        // The one bound case that cannot make progress: the next run would read the same Signals again. Reported and failed, as before #2085.
        var cache = new FaultingCache { FailSetOf = SignalReevaluationJob.DeletedPolicyRedactionCursorKey };
        var h = new Harness(cache: cache).WithMatterHit(MatterId);
        var ordered = SeedPastTheBound(h, "228");

        var result = await h.RunAsync();

        result.Success.Should().BeFalse("where the read stopped could not be recorded: the next run would read the same Signals again");
        result.ResultJson.Should().Contain("\"policyFailures\":1");
        EvidenceWrites(h).Should().Equal(new[] { (ordered[0], EvidenceAlreadyRedacted()) }, "what was read is still redacted");
        h.Log.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Message.Contains("where it stopped could not be recorded", StringComparison.Ordinal)
            && e.Message.Contains("the stored evidence of the deleted policies beyond them was not checked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_APassThatEnds_ButTheCursorCannotBeCleared_IsAFailedRun()
    {
        // A cursor left in place would keep the next runs reading only after it (the Signals before it unread until it expires): reported and
        // failed rather than silently skipping them.
        var cache = new FaultingCache();
        var h = new Harness(cache: cache).WithMatterHit(MatterId);
        var ordered = SeedPastTheBound(h, "229");
        (await h.RunAsync()).Success.Should().BeTrue();
        cache.FailRemoveOf = SignalReevaluationJob.DeletedPolicyRedactionCursorKey;

        var second = await h.RunAsync();

        second.Success.Should().BeFalse();
        second.ResultJson.Should().Contain("\"policyFailures\":1");
        h.Org.Rows[ordered[^1]].GetAttributeValue<string>("sprk_evidencerefs").Should().Be(EvidenceAlreadyRedacted(), "the end of the pass is still redacted");
        h.Log.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Message.Contains("could not be cleared", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_TheCodeReadFaultsAfterACursor_KeepsTheCursor_AndTheRunFails()
    {
        // Fail closed, unchanged: a read fault redacts nothing extra and fails the run; the cursor stays, so the next run reads the same window.
        var h = new Harness().WithMatterHit(MatterId);
        var ordered = SeedPastTheBound(h, "230");
        (await h.RunAsync()).Success.Should().BeTrue();
        var cursor = await CursorOf(h);
        ObserveDeletedCodeReads(h, q => q.EntityName == "sprk_signal"
            && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_signalid" && c.Operator == ConditionOperator.GreaterThan)
            ? new TimeoutException("Dataverse timed out.") : null);

        var result = await h.RunAsync();

        result.Success.Should().BeFalse();
        (await CursorOf(h)).Should().Be(cursor, "nothing was read, so the next run retries the same window");
        h.Org.Rows[ordered[^1]].GetAttributeValue<string>("sprk_evidencerefs").Should().Be(StoredEvidenceQuotingTheWitness());
        h.Log.Should().Contain(e => e.Level == LogLevel.Error
            && e.Message.Contains("Signals of deleted policies could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeletedPolicyCodes_AfterACursor_ReadsOnlyLaterSignals_AndNamesWhereToContinue()
    {
        // Service level: the read continues after the given id, counts what it read, and names the last id read when the bound is reached.
        var h = new Harness().WithMatterHit(MatterId);
        var ordered = SeedPastTheBound(h, "231");
        var existing = await h.Closure.ExistingPolicyCodesAsync(default);

        var fromStart = await h.Closure.DeletedPolicyCodesWithSignalsAsync(existing, default);
        var rest = await h.Closure.DeletedPolicyCodesWithSignalsAsync(existing, default, after: fromStart.ResumeAfter);

        fromStart.Codes.Should().Equal(GoneEarly);
        fromStart.MoreRemain.Should().BeTrue();
        fromStart.SignalsRead.Should().Be(SignalEvaluator.MaxPages);
        fromStart.ResumeAfter.Should().Be(ordered[SignalEvaluator.MaxPages - 1]);
        rest.Codes.Should().Equal(GoneLate);
        rest.MoreRemain.Should().BeFalse();
        rest.SignalsRead.Should().Be(1);
        rest.ResumeAfter.Should().BeNull("the end was reached: nothing to continue after");
    }
}
