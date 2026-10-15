using System.ClientModel.Primitives;
using System.Net;
using Azure;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Spaarke.Contracts.Provisioning;
using Sprk.Bff.Api.Infrastructure.Diagnostics;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Platform;

/// <summary>
/// Task 230b — how one keyless probe's result or exception becomes an outcome. H13 fails acceptance on
/// <c>refused</c> and resumes on <c>unreachable</c>, so a 401/403 must never be classified as anything but
/// refused, and a transient fault never as refused.
/// </summary>
public class KeylessProbeRunnerTests
{
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void Classify_AnAzureSdk401Or403_IsRefused(int status)
    {
        var (outcome, observed, code) = KeylessProbeRunner.Classify(new RequestFailedException(status, "denied"));

        outcome.Should().Be(KeylessProofContract.Outcomes.Refused);
        observed.Should().Be(status);
        code.Should().Be($"http-{status}");
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void Classify_ThrottlingTimeoutOrServerError_IsUnreachable(int status)
    {
        KeylessProbeRunner.Classify(new RequestFailedException(status, "busy")).Outcome
            .Should().Be(KeylessProofContract.Outcomes.Unreachable);
    }

    [Fact]
    public void Classify_ANotFound_IsFailed_NotRefusedAndNotUnreachable()
    {
        // A missing deployment or index is a provisioning defect — retrying does not fix it.
        KeylessProbeRunner.Classify(new RequestFailedException(404, "no such index")).Outcome
            .Should().Be(KeylessProofContract.Outcomes.Failed);
    }

    [Fact]
    public void Classify_AnOpenAiSdk403_IsRefused()
    {
        var response = new StubPipelineResponse(403);

        KeylessProbeRunner.Classify(new System.ClientModel.ClientResultException(response)).Outcome
            .Should().Be(KeylessProofContract.Outcomes.Refused);
    }

    [Fact]
    public void Classify_ACosmos403_IsRefused()
    {
        var ex = new Microsoft.Azure.Cosmos.CosmosException("forbidden", HttpStatusCode.Forbidden, 0, "a", 0);

        KeylessProbeRunner.Classify(ex).Outcome.Should().Be(KeylessProofContract.Outcomes.Refused);
    }

    [Fact]
    public void Classify_AnHttpClient401_IsRefused_AndATransportFaultIsUnreachable()
    {
        KeylessProbeRunner.Classify(new HttpRequestException("x", null, HttpStatusCode.Unauthorized)).Outcome
            .Should().Be(KeylessProofContract.Outcomes.Refused);
        KeylessProbeRunner.Classify(new HttpRequestException("dns")).Outcome
            .Should().Be(KeylessProofContract.Outcomes.Unreachable);
    }

    [Fact]
    public void Classify_ACredentialThatProducesNoToken_IsRefused()
    {
        KeylessProbeRunner.Classify(new CredentialUnavailableException("no identity")).Should()
            .Be((KeylessProofContract.Outcomes.Refused, (int?)null, "token-unavailable"));
        KeylessProbeRunner.Classify(new AuthenticationFailedException("AADSTS")).Outcome
            .Should().Be(KeylessProofContract.Outcomes.Refused);
    }

    [Fact]
    public void Classify_ServiceBusUnauthorized_IsRefused_AndACommunicationProblemIsUnreachable()
    {
        KeylessProbeRunner.Classify(new UnauthorizedAccessException("Unauthorized access. 'Listen' claim(s) are required"))
            .Outcome.Should().Be(KeylessProofContract.Outcomes.Refused);
        KeylessProbeRunner.Classify(new ServiceBusException("down", ServiceBusFailureReason.ServiceCommunicationProblem))
            .Should().Be((KeylessProofContract.Outcomes.Unreachable, (int?)null, "service-bus-service-communication-problem"));
        KeylessProbeRunner.Classify(new ServiceBusException("gone", ServiceBusFailureReason.MessagingEntityNotFound))
            .Outcome.Should().Be(KeylessProofContract.Outcomes.Failed);
    }

    [Fact]
    public void Classify_AnSdkRetryAggregateOfTransportFaults_IsUnreachable_NotFailed()
    {
        // Azure.Core / System.ClientModel throw AggregateException("Retry failed after N tries") when every attempt failed.
        var aggregate = new AggregateException("Retry failed after 4 tries.",
            new RequestFailedException("dns"), new HttpRequestException("connection refused"));

        KeylessProbeRunner.Classify(aggregate).Outcome.Should().Be(KeylessProofContract.Outcomes.Unreachable);
    }

    [Fact]
    public void Classify_AnAggregateOfPerAttemptTimeoutsAndSocketFaults_IsUnreachable()
    {
        var aggregate = new AggregateException(new TaskCanceledException("attempt timeout"), new IOException("connection reset"));

        KeylessProbeRunner.Classify(aggregate).Outcome.Should().Be(KeylessProofContract.Outcomes.Unreachable);
    }

    [Fact]
    public void Classify_AnSdkRetryAggregateContainingARefusal_IsRefused()
    {
        var aggregate = new AggregateException(new RequestFailedException(503, "busy"), new RequestFailedException(403, "denied"));

        KeylessProbeRunner.Classify(aggregate).Should().Be((KeylessProofContract.Outcomes.Refused, (int?)403, "http-403"));
    }

    [Fact]
    public async Task RunAsync_WhenTheProbeReturns_IsProved_WithItsStatus()
    {
        var result = await KeylessProbeRunner.RunAsync("svc", _ => Task.FromResult<int?>(200), NullLogger.Instance, CancellationToken.None);

        result.Should().BeEquivalentTo(new { Service = "svc", Outcome = KeylessProofContract.Outcomes.Proved, StatusCode = (int?)200, Code = "ok" });
    }

    [Fact]
    public async Task RunAsync_WhenTheProbeOutlivesItsLimit_IsUnreachableTimeout()
    {
        var clock = new FakeTimeProvider();
        var neverAnswers = new TaskCompletionSource<int?>();

        var run = KeylessProbeRunner.RunAsync(
            "svc",
            token => { token.Register(() => neverAnswers.TrySetCanceled(token)); return neverAnswers.Task; },
            NullLogger.Instance,
            CancellationToken.None,
            timeout: TimeSpan.FromSeconds(30),
            timeProvider: clock);
        clock.Advance(TimeSpan.FromSeconds(31));
        var result = await run;

        result.Outcome.Should().Be(KeylessProofContract.Outcomes.Unreachable);
        result.Code.Should().Be("timeout");
        result.ElapsedMs.Should().Be(31_000);
    }

    [Fact]
    public async Task RunAsync_NeverReturnsExceptionText_OnlyACode()
    {
        var result = await KeylessProbeRunner.RunAsync(
            "svc",
            _ => throw new RequestFailedException(403, "Principal 9fd47efb does not have access; secret=hunter2"),
            NullLogger.Instance,
            CancellationToken.None);

        result.Code.Should().Be("http-403");
        result.ToString().Should().NotContain("hunter2").And.NotContain("Principal");
    }

    private sealed class StubPipelineResponse : PipelineResponse
    {
        private readonly int _status;

        public StubPipelineResponse(int status) => _status = status;

        public override int Status => _status;
        public override string ReasonPhrase => "stub";
        public override Stream? ContentStream { get; set; } = new MemoryStream();
        public override BinaryData Content => BinaryData.FromString("{}");
        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(Content);
        public override void Dispose() { }
    }
}
