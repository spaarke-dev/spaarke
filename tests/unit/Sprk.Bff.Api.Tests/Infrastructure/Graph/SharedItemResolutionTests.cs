using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Graph;

/// <summary>
/// How every Graph <c>/shares</c> answer is classified (spaarkeai-word-add-in-r1 task 012). This is where a mistake
/// would be silent: an outage classified as "not found" becomes "not a Spaarke document", and the pane saves a
/// Spaarke document as new — a duplicate row. Exercised through
/// <see cref="DriveItemOperations.ResolveAcrossFormsAsync"/> with a fake fetch; a transport mock is banned (ADR-038 B1).
/// </summary>
public class SharedItemResolutionTests
{
    private const string ItemId = "01BYE5RZ6QN3ZWBTUFOFD3GSPGOHDJD36K";
    private const string DriveId = "b!yLMdWD2AdkaWXsktRe9yIW7Hn0uXvZVBnuXhwwvLvZWY-YU6-G3sQ7t6c1tKzXJM";

    private static readonly IReadOnlyList<(string Form, string Url)> TwoForms = new[]
    {
        (SharingUrlToken.EncodedForm, "https://a.sharepoint.com/Document%20Library/x.docx"),
        (SharingUrlToken.RawForm, "https://a.sharepoint.com/Document Library/x.docx"),
    };

    [Fact]
    public async Task ADriveItemWithBothIds_OnTheFirstForm_IsResolved_WithoutTryingTheSecond()
    {
        var fetch = new Fetch((ItemId, DriveId));

        var result = await Resolve(fetch);

        result.Outcome.Should().Be(SpeSharedItemOutcome.Resolved);
        result.ItemId.Should().Be(ItemId);
        result.DriveId.Should().Be(DriveId);
        result.ResolvedForm.Should().Be(SharingUrlToken.EncodedForm);
        fetch.Calls.Should().Be(1);
    }

    [Fact]
    public async Task NotFoundOnTheFirstForm_ResolvesOnTheSecond_AndRecordsBothAttempts()
    {
        var result = await Resolve(new Fetch(GraphError(404, "itemNotFound"), (ItemId, DriveId)));

        result.Outcome.Should().Be(SpeSharedItemOutcome.Resolved);
        result.ResolvedForm.Should().Be(SharingUrlToken.RawForm);
        result.Attempts.Select(a => (a.Form, a.StatusCode)).Should().Equal(
            (SharingUrlToken.EncodedForm, 404),
            (SharingUrlToken.RawForm, 200));
    }

    [Fact]
    public async Task BadRequestAndNotFound_OnEveryForm_IsNotFound()
    {
        var result = await Resolve(new Fetch(GraphError(400, "invalidRequest"), GraphError(404, "itemNotFound")));

        result.Outcome.Should().Be(SpeSharedItemOutcome.NotFound);
    }

    [Fact]
    public async Task AnErrorKiotaCouldNotParseAsOData_IsClassifiedByItsStatus_AndTheNextFormIsTried()
    {
        // An empty or non-JSON error body arrives as the base ApiException, not ODataError.
        var result = await Resolve(new Fetch(new ApiException("empty body") { ResponseStatusCode = 404 }, (ItemId, DriveId)));

        result.Outcome.Should().Be(SpeSharedItemOutcome.Resolved);
    }

    [Fact]
    public async Task Forbidden_OnEveryForm_IsAccessDenied()
    {
        var result = await Resolve(new Fetch(GraphError(403, "accessDenied"), GraphError(403, "accessDenied")));

        result.Outcome.Should().Be(SpeSharedItemOutcome.AccessDenied);
    }

    [Fact]
    public async Task Forbidden_ThenResolvedOnAnotherForm_IsResolved()
    {
        var result = await Resolve(new Fetch(GraphError(403, "accessDenied"), (ItemId, DriveId)));

        result.Outcome.Should().Be(SpeSharedItemOutcome.Resolved);
    }

    [Theory]
    [InlineData(401)] // describes the token, not the item
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task StatusesThatSayNothingAboutTheItem_AreUnavailable_AndStopAtOnce(int status)
    {
        var fetch = new Fetch(GraphError(status, "whatever"));

        var result = await Resolve(fetch);

        result.Outcome.Should().Be(SpeSharedItemOutcome.Unavailable);
        fetch.Calls.Should().Be(1, "an outage on one spelling is an outage, not a reason to try another");
    }

    public static TheoryData<Exception> Outages => new()
    {
        new HttpRequestException("No such host is known."),
        new TimeoutRejectedException("The delegate executed through TimeoutPolicy did not complete within the timeout."),
        new BrokenCircuitException("The circuit is now open and is not allowing calls."),
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."),
    };

    [Theory]
    [MemberData(nameof(Outages))]
    public async Task TransportFailures_PollyTimeoutsAndOpenCircuits_AreUnavailable(Exception outage)
    {
        var result = await Resolve(new Fetch(outage));

        result.Outcome.Should().Be(SpeSharedItemOutcome.Unavailable);
    }

    [Fact]
    public async Task ADriveItemMissingItsIds_OnEveryForm_IsUnavailable_NotNotFound()
    {
        var result = await Resolve(new Fetch(((string?)null, DriveId), (ItemId, (string?)null)));

        result.Outcome.Should().Be(SpeSharedItemOutcome.Unavailable);
    }

    [Fact]
    public async Task ACancellationTheCallerRequested_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => DriveItemOperations.ResolveAcrossFormsAsync(
            TwoForms, new Fetch(new OperationCanceledException(cts.Token)).Invoke, NullLogger.Instance, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AnUnexpectedFault_Propagates_RatherThanBeingReportedAsAnOutage()
    {
        var act = () => Resolve(new Fetch(new NullReferenceException("a defect")));

        await act.Should().ThrowAsync<NullReferenceException>();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static Task<SpeSharedItemResolution> Resolve(Fetch fetch)
        => DriveItemOperations.ResolveAcrossFormsAsync(TwoForms, fetch.Invoke, NullLogger.Instance, CancellationToken.None);

    private static ODataError GraphError(int status, string code)
        => new() { ResponseStatusCode = status, Error = new MainError { Code = code } };

    /// <summary>One scripted answer per form, in order: an (itemId, driveId) pair, or an exception to throw.</summary>
    private sealed class Fetch
    {
        private readonly Queue<object> _answers;

        public Fetch(params object[] answers) => _answers = new Queue<object>(answers);

        public int Calls { get; private set; }

        public Task<(string? ItemId, string? DriveId)> Invoke(string url, CancellationToken ct)
        {
            Calls++;
            return _answers.Dequeue() switch
            {
                Exception ex => Task.FromException<(string?, string?)>(ex),
                ValueTuple<string?, string?> ids => Task.FromResult(ids),
                var other => throw new InvalidOperationException($"Unscripted answer {other}"),
            };
        }
    }
}
