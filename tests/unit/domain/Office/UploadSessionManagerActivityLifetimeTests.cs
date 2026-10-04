using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Office;

/// <summary>
/// Task 093 (spaarkeai-word-add-in-r1, GitHub #1084 follow-on): <see cref="UploadSessionManager.UploadSmallAsync"/>
/// must not stop the CALLER's ambient request <see cref="Activity"/>.
/// </summary>
/// <remarks>
/// <para><b>The defect, evidenced live.</b> <c>UploadSmallAsync</c> read the request's own Activity via
/// <c>Activity.Current</c> to attach tags (<c>operation</c>, <c>driveId</c>, <c>filePath</c>) and did so through
/// <c>using var activity = Activity.Current;</c> — disposing (= stopping) a span this method never started. On
/// 2026-10-03 this turned a real 3-second, successful Word save into a request App Insights recorded as
/// <c>resultCode 0</c>, <c>success=false</c>, duration 1.4s (notes/093-job-row-and-telemetry.md §2). The same
/// shape existed at 19 other sites in <c>Infrastructure/Graph</c>; the structural guard
/// (<see cref="Spaarke.ArchTests.ActivityCurrentDisposalGuardTests"/>) proves none of the twenty do this any
/// more. This test proves the CALLER-VISIBLE SYMPTOM is gone for the one method the evidence names directly:
/// an <see cref="ActivityListener"/> watching the request span sees no <c>ActivityStopped</c> for it while
/// <c>UploadSmallAsync</c> runs and after it returns — only once the TEST, standing in for the real caller
/// (ASP.NET Core's request-tracing middleware), stops it itself.</para>
/// <para><b>Why the Graph boundary is made to fail.</b> The assertion is about <c>Activity.Current</c>'s
/// lifetime, not about Graph's response — so the Kiota client is never constructed at all:
/// <c>IGraphClientFactory.ForApp()</c> throws before any HTTP work, and the method's own
/// <c>catch (Exception ex) { ...; throw; }</c> propagates it. Activity.Current is read and tagged BEFORE
/// <c>ForApp()</c> is called (the very first statement in the method), so whether the call beyond that point
/// succeeds or throws is irrelevant to what this test checks; a thrown exception is simply the cheapest way to
/// make the method return without a live Graph dependency.</para>
/// </remarks>
[Trait("status", "new")]
public class UploadSessionManagerActivityLifetimeTests
{
    [Fact]
    public async Task UploadSmallAsync_DoesNotStopTheCallersRequestActivity_UntilTheCallerStopsItItself()
    {
        // Arrange — the Graph boundary fails fast; Activity.Current is read/tagged before it is ever reached.
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Throws(new InvalidOperationException("Graph boundary — never reached"));
        var sut = new UploadSessionManager(
            factory.Object,
            Mock.Of<IHttpClientFactory>(),
            NullLogger<UploadSessionManager>.Instance);

        // The request's own Activity, exactly as ASP.NET Core's request-tracing middleware would start one and
        // leave it running as Activity.Current for the lifetime of the request — UploadSmallAsync is called
        // from somewhere in the middle of that request, not at its start or end.
        using var source = new ActivitySource("Tests.Office.ActivityLifetime." + Guid.NewGuid());
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => stopped.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        var started = source.StartActivity("request");
        started.Should().NotBeNull(
            "the listener samples this source, so starting an activity on it must produce one");
        var requestActivity = started!;
        Activity.Current.Should().BeSameAs(
            requestActivity,
            "UploadSmallAsync reads the AMBIENT activity — the test must set one up exactly as a real request does");

        // Act — the call that used to `using`-dispose (= stop) requestActivity.
        var act = () => sut.UploadSmallAsync("drive-1", "file.docx", new MemoryStream([1, 2, 3]));
        await act.Should().ThrowAsync<InvalidOperationException>(
            "the Graph boundary was made to fail; the method must still return (by throwing) without touching "
            + "the caller's Activity");

        // Assert — the request's span is still running: no Stop callback for it, and its Duration (only set by
        // Stop()) is still the unset default.
        stopped.Should().NotContain(
            requestActivity,
            "UploadSmallAsync must not stop the caller's request Activity. Before this fix, "
            + "`using var activity = Activity.Current;` stopped the REQUEST's own span the moment the method "
            + "returned — truncating the duration App Insights recorded and reporting resultCode 0 / "
            + "success=false for a call that actually succeeded.");
        requestActivity.Duration.Should().Be(
            TimeSpan.Zero,
            "an Activity's Duration is set only when Stop() runs; it must still be unset here");

        // The TRUE owner — the caller, standing in for ASP.NET Core's request middleware — stops it later.
        requestActivity.Stop();
        stopped.Should().Contain(
            requestActivity,
            "the activity legitimately stops once its real owner (the caller) stops it — this proves the "
            + "listener would have seen an earlier Stop had UploadSmallAsync caused one");
    }
}
