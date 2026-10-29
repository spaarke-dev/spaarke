using FluentAssertions;
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

    [Fact(DisplayName = "101 B2a: 049's rule - a resolved Signal with the same fingerprint is skipped (nothing written)")]
    public async Task Resolved_SameFingerprint_UnderTheSubmissionRule_IsSkipped()
    {
        var (lifecycle, written) = Lifecycle(resolved: true, storedFingerprint: "fp|Flag");

        var step = await lifecycle.WriteAsync(Request(), "fp|Flag", "Invoice submission", Guid.NewGuid(), ResolvedEpisodeRule.SkipWhenSameFingerprint, CancellationToken.None);

        step.Wrote.Should().BeFalse();
        step.FailureCode.Should().BeNull();
        written.Should().BeEmpty();
    }

    [Fact(DisplayName = "101 B2a: B2b's rule - a resolved Signal with the same fingerprint is reopened as episode n+1")]
    public async Task Resolved_SameFingerprint_UnderTheReopenRule_OpensTheNextEpisode()
    {
        var (lifecycle, written) = Lifecycle(resolved: true, storedFingerprint: "fp|Flag");

        var step = await lifecycle.WriteAsync(Request(), "fp|Flag", "Invoice correction", Guid.NewGuid(), ResolvedEpisodeRule.ReopenAsNextEpisode, CancellationToken.None);

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
        (await resolvedLifecycle.WriteAsync(Request(), "new|Flag", "x", Guid.NewGuid(), rule, CancellationToken.None)).Wrote.Should().BeTrue();
        resolvedWritten.Should().Equal(2);

        var (openLifecycle, openWritten) = Lifecycle(resolved: false, storedFingerprint: "fp|Flag");
        (await openLifecycle.WriteAsync(Request(), "fp|Flag", "x", Guid.NewGuid(), rule, CancellationToken.None)).Wrote.Should().BeTrue();
        openWritten.Should().Equal(1);
    }
}
