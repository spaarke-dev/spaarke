using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 049c (NFR-02, round 2): the create path's step split, reproduced with delayed
/// doubles (every Dataverse round trip, caller and app-only, takes <see cref="RoundTrip"/>), and the three speed-ups pinned:
/// step 3's reads for a NAMED source start beside step 2 (option b); steps 2 and 3 ask each table-metadata read once
/// (option a); step 5's two checks of a mapped row are asked together (option b). Each test also pins that the answer — the
/// records written, the warnings, the refusal and which refusal — is the one the sequential pipeline gave.
/// </summary>
/// <remarks>The split comes from the pipeline's own step lines (<c>stepMs</c>, task 049c). Bounds are in round trips with wide
/// margins; the sequential pipeline exceeds each by at least two round trips (task-049c-notes.md, "Local before and after").
/// Set <c>SPRK_049C_TIMING_OUT</c> to a file path to write the measured split there.</remarks>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private static readonly TimeSpan RoundTrip = TimeSpan.FromMilliseconds(100);

    /// <summary>Every round trip delayed: the caller's reads, the probe, the app-only reads and the owner resolver.</summary>
    private StepTimingLogger EveryReadDelayed()
    {
        _user.DelayOf = _ => RoundTrip;
        _probe.Delay = RoundTrip;
        _appOnlyDelay = RoundTrip;
        _ownerDelay = RoundTrip;
        var timings = new StepTimingLogger();
        _logger = timings;
        return timings;
    }

    [Fact]
    public async Task CreatePath_ANamedSourceMappedCreate_WithDelayedReads_OverlapsTheMappingReads_AndAnswersAsTheSequentialRun()
    {
        MatterToEventProfile();
        var request = EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", OrdinaryMatter));

        // Today's answer: the same create with every double answering at once (each read completes before the next starts).
        var immediate = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);
        var timings = EveryReadDelayed();
        var delayed = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        timings.WriteSplitIfAsked(nameof(CreatePath_ANamedSourceMappedCreate_WithDelayedReads_OverlapsTheMappingReads_AndAnswersAsTheSequentialRun));
        immediate.Succeeded.Should().BeTrue(immediate.Refusal?.Detail);
        delayed.Succeeded.Should().BeTrue(delayed.Refusal?.Detail);
        _creates.Should().HaveCount(2);
        _creates[1].Fields.Where(f => f.Key != "sprk_eventid")
            .Select(f => (f.Key, Value: JsonSerializer.Serialize(f.Value)))
            .Should().Equal(_creates[0].Fields.Where(f => f.Key != "sprk_eventid").Select(f => (f.Key, Value: JsonSerializer.Serialize(f.Value))),
                "the same row is written");
        StringOf(_creates[1].Fields, "sprk_AssignedTo@odata.bind").Should().Be($"/contacts({Assignee:D})");
        delayed.Warnings.Should().Equal(immediate.Warnings);
        delayed.MappingWarnings.Should().Equal(immediate.MappingWarnings);
        delayed.Steps.Should().Equal(immediate.Steps, "the same steps, states and notes, in the same order");

        // Step 3 waits only for what is left of its reads after step 2 (they started beside it), then maps: the sequential
        // step was profile, probe, source read, mask, target columns and the merge's three metadata reads (8 round trips).
        timings.Of(RecordCreationStep.FieldMapping).Should().BeLessThan(RoundTrip * 5, timings.Split);

        // Step 5's two checks of the mapped row run together: about one check's time, not two.
        timings.Of(RecordCreationStep.CallerChecks).Should().BeLessThan(RoundTrip * 6, timings.Split);
    }

    [Fact]
    public async Task CreatePath_StepsTwoAndThree_AskEachTableMetadataReadOnce_ButEveryAccessQuestionIsAskedAgain()
    {
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", OrdinaryMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _user.Paths.Count(p => p.Contains("ManyToOneRelationships", StringComparison.Ordinal)
                               && p.Contains("'sprk_event'", StringComparison.Ordinal))
            .Should().Be(1, "step 3's merge takes step 2's answer for the table's relationships");
        _user.Paths.Count(p => p == "EntityDefinitions(LogicalName='sprk_matter')?$select=EntitySetName")
            .Should().Be(1, "and for the matter's entity set");
        _user.Paths.Count(p => p == "EntityDefinitions(LogicalName='contact')?$select=EntitySetName")
            .Should().Be(1, "a table step 2 never read is read once, by step 3");
        _user.Paths.Count(p => p.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                               && p.Contains(OrdinaryMatter.ToString("D"), StringComparison.Ordinal))
            .Should().BeGreaterThan(1, "AppendTo on the matter is asked in each check, never remembered (ADR-009)");
    }

    [Fact]
    public async Task CreatePath_TwoChecksWouldFail_TheCallersOwnRefusalAnswers_NeverTheMergedChecksFault()
    {
        // Seeded two-refusal case (NFR-07): the caller's own value fails AppendTo (refused, uniform not-found) and the mapped
        // filing lookup's check faults. The merged check is asked beside the caller's own now; the caller's own still answers,
        // as when the merged check was never asked.
        Profile("sprk_matter", "sprk_event",
            Rule("sprk_matterdescription", "sprk_description", Memo),
            Rule("sprk_primaryproject", "sprk_regardingproject", Lookup));
        _scripted.NoAppendToIn.Add(("contacts", CallersContact));
        var request = EventUnder(OrdinaryMatter, assignedTo: CallersContact, mappingSource: ("sprk_matter", OrdinaryMatter));

        var expected = await Service().CreateAsync(request with { Mapping = null }, AsCaller(), CancellationToken.None);
        EveryReadDelayed();
        _user.ThrowOn = path => path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                                && path.Contains(OtherProject.ToString("D"), StringComparison.Ordinal)
            ? new TimeoutException("access read timed out at 10.0.0.7")
            : null;

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        expected.Refusal.Should().NotBeNull();
        outcome.Refusal.Should().NotBeNull();
        outcome.Refusal!.Kind.Should().Be(expected.Refusal!.Kind);
        outcome.Refusal.Code.Should().Be(expected.Refusal.Code);
        outcome.Refusal.Detail.Should().Be(expected.Refusal.Detail);
        Problem(outcome, "sprk_event").Status.Should().Be(Problem(expected, "sprk_event").Status).And.Be(StatusCodes.Status404NotFound);
        _user.Paths.Should().Contain(p => p.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                                          && p.Contains(OtherProject.ToString("D"), StringComparison.Ordinal),
            "the merged check was asked (and faulted) beside the caller's own");
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatePath_AStepTwoRefusal_AnswersAsBefore_WhileTheEarlyMappingReadsFault_AndNothingIsCreated()
    {
        // Seeded two-failure case: step 2 refuses (the platform's number in the caller's payload) while step 3's early reads
        // fault (the target columns' read). Step 2's refusal answers, exactly as with no mapping asked; the fault is dropped.
        MatterToEventProfile();
        var request = EventUnder(OrdinaryMatter, extra: ("sprk_eventnumber", "EVT-1"), mappingSource: ("sprk_matter", OrdinaryMatter));
        var expected = await Service().CreateAsync(request with { Mapping = null }, AsCaller(), CancellationToken.None);
        EveryReadDelayed();
        // Step 2 outlasts the early reads, so their fault has happened before step 2 refuses.
        _user.DelayOf = path => path.Contains("ManyToOneRelationships", StringComparison.Ordinal) ? RoundTrip * 6 : RoundTrip;
        _user.ThrowOn = path => path.Contains("AttributeType", StringComparison.Ordinal)
            ? new TimeoutException("metadata read timed out at 10.0.0.9")
            : null;
        _journal.Clear();
        _user.Paths.Clear();

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);
        int readsWhenAnswered;
        lock (_journal)
            readsWhenAnswered = _user.Paths.Count;

        expected.Refusal!.Code.Should().Be(RecordCreationRefusal.BlockedColumnCode);
        outcome.Refusal.Should().BeEquivalentTo(expected.Refusal);
        outcome.Steps.Should().Equal(expected.Steps, "refused at step 2, recorded the same; step 3 never ran");
        _journal.Should().Contain("profile", "the early mapping reads ran beside step 2");
        _user.Paths.Should().Contain(p => p.Contains("AttributeType", StringComparison.Ordinal),
            "the early target-columns read was asked, and faulted, before step 2 refused");
        _creates.Should().BeEmpty();
        _journal.Should().NotContain(["owner", "create"]);

        // Nothing of the early reads runs after the answer: they were cancelled and awaited.
        await Task.Delay(RoundTrip * 3);
        lock (_journal)
            _user.Paths.Count.Should().Be(readsWhenAnswered, "no read is made after the create answered");
    }

    [Fact]
    public async Task CreatePath_ASourceTheCallerMayNotRead_AnswersAsBefore_AndTheTargetColumnsReadBesideItIsCancelled()
    {
        // The target columns' read is asked beside the source's; when the source answers "unavailable" first, that read's
        // answer is never taken: it is cancelled and awaited, so it never runs on after the create has answered.
        MatterToEventProfile();
        _sources[("sprk_matter", DeniedMatter)] = _sources[("sprk_matter", OrdinaryMatter)];
        _rights[DeniedMatter] = AccessRights.Write | AccessRights.AppendTo | AccessRights.Append;
        var request = EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", DeniedMatter));
        var expected = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);
        EveryReadDelayed();
        _user.DelayOf = path => path.Contains("AttributeType", StringComparison.Ordinal) ? RoundTrip * 15 : RoundTrip;
        _user.Paths.Clear();

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);
        await Task.Delay(RoundTrip * 16);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.MappingWarnings.Should().Equal(expected.MappingWarnings);
        outcome.MappingWarnings.Should().ContainSingle().Which.Code.Should().Be(RecordCreationWarning.MappingSourceUnavailableCode);
        _sourceReads.Should().BeEmpty("the probe answered first");
        lock (_journal)
            _user.Paths.Should().NotContain(p => p.Contains("AttributeType", StringComparison.Ordinal),
                "the target columns' read was cancelled when the source answered unavailable, never completed after");
    }

    [Fact]
    public async Task CreatePath_WhenTheEarlyMappingReadsFault_NothingIsMapped_AndTheRowIsCreatedWithTheCallersValues()
    {
        // AC5 / NFR-01: a fault in reads that ran beside step 2 is step 3's fault, as before: nothing mapped, one warning.
        MatterToEventProfile();
        EveryReadDelayed();
        _user.ThrowOn = path => path.Contains("AttributeType", StringComparison.Ordinal)
            ? new TimeoutException("metadata read timed out at 10.0.0.9")
            : null;

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", OrdinaryMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.MappingWarnings.Should().ContainSingle().Which.Should().Be(new RecordCreationWarning(
            RecordCreationWarning.MappingUnavailableCode, null, RecordCreationService.MappingFaulted));
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description").And.NotContainKey("sprk_AssignedTo@odata.bind");
        StringOf(fields, "sprk_eventname").Should().Be("Hearing");
    }

    [Fact]
    public async Task CreatePath_WhenACheckBothStepFiveChecksAskFaults_TheCreateIsRefused503_AndNothingIsWritten()
    {
        // AC5 (negative): the two checks run together, and a fault in a read both make still refuses (fail closed).
        MatterToEventProfile();
        EveryReadDelayed();
        _user.ThrowOn = path => path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                                && path.Contains(OrdinaryMatter.ToString("D"), StringComparison.Ordinal)
            ? new TimeoutException("access read timed out at 10.0.0.7")
            : null;

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", OrdinaryMatter)), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.CheckFailedCode);
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _creates.Should().BeEmpty();
        _journal.Should().NotContain("create");
    }

    /// <summary>An app-only read's round trip: at once when no delay is set (as before), else after the delay, journaled
    /// under the journal's lock.</summary>
    private Task<T> AppOnlyRoundTrip<T>(Func<Task<T>> answer)
    {
        return _appOnlyDelay > TimeSpan.Zero ? DelayedAsync() : answer();

        async Task<T> DelayedAsync()
        {
            await Task.Delay(_appOnlyDelay).ConfigureAwait(false);
            Task<T> answered;
            lock (_journal)
                answered = answer();
            return await answered.ConfigureAwait(false);
        }
    }

    /// <summary>The pipeline's step lines (<c>[RECORD-CREATE] … step …</c>), kept by step: the last create's split.</summary>
    private sealed class StepTimingLogger : ILogger<RecordCreationService>
    {
        private readonly List<(RecordCreationStep Step, long Ms)> _steps = [];

        public string Split
        {
            get
            {
                lock (_steps)
                    return string.Join(", ", _steps.Select(s => $"{s.Step}={s.Ms}ms"));
            }
        }

        public TimeSpan Of(RecordCreationStep step)
        {
            lock (_steps)
                return TimeSpan.FromMilliseconds(_steps.LastOrDefault(s => s.Step == step).Ms);
        }

        public void WriteSplitIfAsked(string test)
        {
            if (Environment.GetEnvironmentVariable("SPRK_049C_TIMING_OUT") is { Length: > 0 } path)
                File.AppendAllText(path, $"{test}: {Split}{Environment.NewLine}");
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is not IReadOnlyList<KeyValuePair<string, object?>> values)
                return;
            var step = values.FirstOrDefault(v => v.Key == "Step").Value;
            var ms = values.FirstOrDefault(v => v.Key == "StepMs").Value;
            if (step is RecordCreationStep named && ms is long elapsed)
            {
                lock (_steps)
                    _steps.Add((named, elapsed));
            }
        }
    }
}
