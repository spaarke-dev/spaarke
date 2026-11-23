using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 101 part B2a: the extracted option-a lifecycle. Task 049's behaviour is pinned by <see cref="InvoiceSubmissionAfterStepsTests"/>
/// (unchanged); this class pins the one new input, the rule for a RESOLVED latest episode, which B2b passes.
/// </summary>
public sealed class SignalEpisodeLifecycleTests
{
    private static readonly Guid Subject = Guid.Parse("04900000-0000-4000-8000-0000000000c1");
    private const string Policy = "BILL-TEST";
    private const string Entity = "sprk_billingevent";

    private static SignalWriteRequest Request() => new(
        Guid.NewGuid(), Policy, Guid.NewGuid(), Entity, Subject, "Line 1", "Headline", 100000000, null, "Template", new Dictionary<string, object?>());

    private static (SignalEpisodeLifecycle Lifecycle, List<int> WrittenEpisodes) Lifecycle(bool resolved, string storedFingerprint)
    {
        var row = new Microsoft.Xrm.Sdk.Entity("sprk_signal", Guid.NewGuid())
        {
            ["sprk_dedupekey"] = SignalWriter.BuildDedupeKey(Policy, Entity, Subject.ToString("D").ToLowerInvariant(), 1),
            ["sprk_signalstatus"] = new OptionSetValue(resolved ? SignalWriter.SignalStatusResolved : SignalWriter.SignalStatusOpen),
            ["sprk_evidencerefs"] = $$"""[{"kind":"{{BillingFindingSignalMapper.FingerprintEvidenceKind}}","ref":"{{storedFingerprint}}"}]""",
            ["versionnumber"] = 5L,
        };
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection([row]));
        var written = new List<int>();
        var lifecycle = new SignalEpisodeLifecycle(
            entities.Object,
            (request, _) =>
            {
                written.Add(request.Episode);
                return Task.FromResult(new SignalWriteResult(Guid.NewGuid(), true, Guid.Empty, Guid.Empty));
            },
            (_, _, _, _) => Task.FromResult(SignalCloseOutcome.Closed),
            NullLogger.Instance);
        return (lifecycle, written);
    }

    private sealed class StateLogger : ILogger
    {
        public readonly List<(string Template, Dictionary<string, object?> Properties)> Entries = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = (state as IEnumerable<KeyValuePair<string, object?>> ?? []).ToDictionary(p => p.Key, p => p.Value);
            Entries.Add(((string)properties["{OriginalFormat}"]!, properties));
        }
    }

    private static async Task<StateLogger> SkippedWriteLogAsync(SignalLogContext context, Guid contextId)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection([]));
        var logger = new StateLogger();
        var lifecycle = new SignalEpisodeLifecycle(
            entities.Object,
            (_, _) => Task.FromResult(new SignalWriteResult(Guid.Empty, false, Guid.Empty, Guid.Empty) { SkippedRefusalCode = "some.retryable.refusal" }),
            (_, _, _, _) => Task.FromResult(SignalCloseOutcome.Closed),
            logger);

        var step = await lifecycle.WriteAsync(Request(), "fp|Flag", context, contextId, ResolvedEpisodeRule.SkipWhenSameFingerprint, CancellationToken.None);

        step.FailureCode.Should().Be(SignalEpisodeLifecycle.SignalWriteSkippedCode);
        return logger;
    }

    [Fact(DisplayName = "101 fix 1 K-e: 049's skipped-write log keeps its original template and its SubmissionId property")]
    public async Task SkippedWrite_UnderTheSubmissionContext_LogsTheOriginalShape()
    {
        var id = Guid.NewGuid();

        var logger = await SkippedWriteLogAsync(SignalLogContext.Submission, id);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Template.Should().Be("Invoice submission {SubmissionId}: the Signal of policy {PolicyCode} on {Subject} {SubjectId} was skipped ({Refusal})");
        entry.Properties["SubmissionId"].Should().Be(id);
        entry.Properties.Should().NotContainKey("ContextId");
    }

    [Fact(DisplayName = "101 fix 1 K-e: a correction's skipped-write log carries InvoiceId, not SubmissionId")]
    public async Task SkippedWrite_UnderTheCorrectionContext_LogsInvoiceId()
    {
        var id = Guid.NewGuid();

        var logger = await SkippedWriteLogAsync(SignalLogContext.Correction, id);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Template.Should().StartWith("Invoice correction {InvoiceId}:");
        entry.Properties["InvoiceId"].Should().Be(id);
        entry.Properties.Should().NotContainKey("SubmissionId");
    }

    [Fact(DisplayName = "101 B2a: 049's rule - a resolved Signal with the same fingerprint is skipped (nothing written)")]
    public async Task Resolved_SameFingerprint_UnderTheSubmissionRule_IsSkipped()
    {
        var (lifecycle, written) = Lifecycle(resolved: true, storedFingerprint: "fp|Flag");

        var step = await lifecycle.WriteAsync(Request(), "fp|Flag", SignalLogContext.Submission, Guid.NewGuid(), ResolvedEpisodeRule.SkipWhenSameFingerprint, CancellationToken.None);

        step.Wrote.Should().BeFalse();
        step.FailureCode.Should().BeNull();
        written.Should().BeEmpty();
    }

    [Fact(DisplayName = "101 B2a: B2b's rule - a resolved Signal with the same fingerprint is reopened as episode n+1")]
    public async Task Resolved_SameFingerprint_UnderTheReopenRule_OpensTheNextEpisode()
    {
        var (lifecycle, written) = Lifecycle(resolved: true, storedFingerprint: "fp|Flag");

        var step = await lifecycle.WriteAsync(Request(), "fp|Flag", SignalLogContext.Correction, Guid.NewGuid(), ResolvedEpisodeRule.ReopenAsNextEpisode, CancellationToken.None);

        step.Wrote.Should().BeTrue();
        written.Should().Equal(2);
    }

    [Theory(DisplayName = "101 B2a: a resolved Signal with a different fingerprint opens episode n+1 under either rule; an open one with the same fingerprint is refreshed")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolvedDifferent_OpensNext_AndOpenSame_RefreshesTheSameEpisode(bool reopen)
    {
        var rule = reopen ? ResolvedEpisodeRule.ReopenAsNextEpisode : ResolvedEpisodeRule.SkipWhenSameFingerprint;
        var (resolvedLifecycle, resolvedWritten) = Lifecycle(resolved: true, storedFingerprint: "old|Flag");
        (await resolvedLifecycle.WriteAsync(Request(), "new|Flag", SignalLogContext.Submission, Guid.NewGuid(), rule, CancellationToken.None)).Wrote.Should().BeTrue();
        resolvedWritten.Should().Equal(2);

        var (openLifecycle, openWritten) = Lifecycle(resolved: false, storedFingerprint: "fp|Flag");
        (await openLifecycle.WriteAsync(Request(), "fp|Flag", SignalLogContext.Submission, Guid.NewGuid(), rule, CancellationToken.None)).Wrote.Should().BeTrue();
        openWritten.Should().Equal(1);
    }
}
