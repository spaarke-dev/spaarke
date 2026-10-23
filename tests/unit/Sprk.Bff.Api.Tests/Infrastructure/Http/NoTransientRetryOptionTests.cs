using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Http;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Http;

/// <summary>
/// The state machine <see cref="GraphHttpMessageHandler"/> records a non-idempotent Graph request's attempts on, and the
/// container create reads to classify a failure (#1755 rounds 2 and 3).
/// </summary>
public sealed class NoTransientRetryOptionTests
{
    [Fact]
    public void AfterClose_ALaterAttemptIsRefused()
    {
        // K3: the closed-refusal branch. Once the caller has read the outcome, an attempt not yet sent must not be.
        var option = new NoTransientRetryOption();
        option.TryBeginAttempt().Should().BeTrue();
        option.RecordAnswer(HttpStatusCode.TooManyRequests);

        option.CloseAndReadOutcomeUnknown().Should().BeFalse("the only attempt was a 429, which Graph did not process");

        option.TryBeginAttempt().Should().BeFalse("the outcome was read; this attempt must not be sent");
    }

    [Fact]
    public void ClosingWhileAnAttemptIsInFlight_IsUnknown()
    {
        var option = new NoTransientRetryOption();
        option.TryBeginAttempt().Should().BeTrue();

        option.CloseAndReadOutcomeUnknown().Should().BeTrue("the attempt was sent and has no answer");
        option.TryBeginAttempt().Should().BeFalse("closed: nothing more is sent");
    }

    [Fact]
    public void AnAnswerRecordedAfterClose_ChangesNothing()
    {
        var option = new NoTransientRetryOption();
        option.TryBeginAttempt().Should().BeTrue();
        option.CloseAndReadOutcomeUnknown().Should().BeTrue();

        option.RecordAnswer(HttpStatusCode.BadRequest);
        option.RecordNotSent();

        option.TryBeginAttempt().Should().BeFalse("still closed: a late answer does not reopen it");
        option.CloseAndReadOutcomeUnknown().Should().BeFalse("a closed option reads as closed, not as a fresh outcome");
    }

    [Fact]
    public void ARefusedAttempt_CanBeSentAgain_AndIsInFlightAgain()
    {
        // Kiota re-sends a request after a 401 claims challenge: the refusal is followed by a new attempt in flight.
        var option = new NoTransientRetryOption();
        option.TryBeginAttempt().Should().BeTrue();
        option.RecordAnswer(HttpStatusCode.Unauthorized);

        option.TryBeginAttempt().Should().BeTrue();

        option.CloseAndReadOutcomeUnknown().Should().BeTrue("the re-sent attempt is in flight");
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    public void TheLastAnswer_Decides(HttpStatusCode status, bool unknown)
    {
        var option = new NoTransientRetryOption();
        option.TryBeginAttempt().Should().BeTrue();
        option.RecordAnswer(status);

        option.CloseAndReadOutcomeUnknown().Should().Be(unknown);
    }

    [Fact]
    public void AnAttemptThatNeverConnected_IsNotSent()
    {
        // K2 (#1755 round 3): a refused connect or an unresolved name sent nothing.
        var option = new NoTransientRetryOption();
        option.TryBeginAttempt().Should().BeTrue();

        option.RecordNotSent();

        option.CloseAndReadOutcomeUnknown().Should().BeFalse();
    }

    [Theory]
    [InlineData(SocketError.ConnectionRefused, true)]
    [InlineData(SocketError.HostNotFound, true)]
    [InlineData(SocketError.ConnectionReset, false)]
    [InlineData(SocketError.TimedOut, false)]
    public void OnlyAFailureToConnect_CountsAsNeverConnected(SocketError socketError, bool neverConnected)
    {
        var ex = new HttpRequestException("x", new SocketException((int)socketError));

        GraphHttpMessageHandler.NeverConnected(ex).Should().Be(neverConnected);
    }

    [Fact]
    public void AFailedNameResolution_CountsAsNeverConnected_AndAPlainFaultDoesNot()
    {
        GraphHttpMessageHandler.NeverConnected(
            new HttpRequestException(HttpRequestError.NameResolutionError, "x")).Should().BeTrue();
        GraphHttpMessageHandler.NeverConnected(
            new HttpRequestException(HttpRequestError.ResponseEnded, "x")).Should().BeFalse();
        GraphHttpMessageHandler.NeverConnected(new HttpRequestException("x")).Should().BeFalse();
    }
}
