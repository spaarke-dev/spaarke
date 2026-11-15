using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Http;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ontology task 191 (D-223, #2779): <c>sendMail</c> is not idempotent, so <see cref="EmailChannelSender"/> marks it with
/// <see cref="NoTransientRetryOption"/> and production's <see cref="GraphHttpMessageHandler"/> sends it ONCE after a 5xx (Graph may
/// already have delivered it), retrying only a 429. A failure carries <c>sendOutcome</c>: <c>NotSent</c> when it provably did not go
/// out, <c>Unknown</c> when it may have.
/// MUTATION: removing <c>rc =&gt; rc.Options.Add(sendOption)</c> from either send lets the handler re-send after a 503 (4 requests).
/// </summary>
public class EmailChannelSenderSendOutcomeTests
{
    private const int Retries = 3;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A5xxOnSendMail_IsSentOnce_AndIsAnUnknownOutcome(bool userMode)
    {
        var transport = new Transport(HttpStatusCode.ServiceUnavailable);

        var failure = await SendAndFail(transport, userMode);

        transport.SendMailRequests.Should().Be(1, "Graph may already have delivered it, so it is never sent again");
        failure.Code.Should().Be("GRAPH_SEND_FAILED");
        failure.Extensions![EmailChannelSender.SendOutcomeExtension].Should().Be(EmailChannelSender.SendOutcomeUnknown);
    }

    /// <summary>Task 191 fix round 2: the request left and the connection broke or timed out with no answer. Graph may have
    /// delivered it, so it is Unknown and sent once.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ATransportFaultOrTimeoutOnSendMail_IsSentOnce_AndIsAnUnknownOutcome(bool userMode, bool timeout)
    {
        var transport = new Transport(HttpStatusCode.Accepted)
        {
            Fault = timeout ? new TaskCanceledException("The request timed out.", new TimeoutException()) : new HttpRequestException("The connection was reset."),
        };

        var failure = await SendAndFail(transport, userMode);

        transport.SendMailRequests.Should().Be(1, "a request that may have reached Graph is never sent again");
        failure.Code.Should().Be("GRAPH_SEND_FAILED");
        failure.Extensions![EmailChannelSender.SendOutcomeExtension].Should().Be(EmailChannelSender.SendOutcomeUnknown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A400OnSendMail_IsNotSent(bool userMode)
    {
        var transport = new Transport(HttpStatusCode.BadRequest);

        var failure = await SendAndFail(transport, userMode);

        transport.SendMailRequests.Should().Be(1);
        failure.Extensions![EmailChannelSender.SendOutcomeExtension].Should().Be(EmailChannelSender.SendOutcomeNotSent);
    }

    [Fact]
    public async Task A429OnSendMail_IsRetried_AndThenSends()
    {
        var transport = new Transport(HttpStatusCode.TooManyRequests, HttpStatusCode.Accepted);

        await Sender(transport, userMode: true).SendAsync(Request(userMode: true));

        transport.SendMailRequests.Should().Be(2, "Graph did not process a throttled send, so it is sent again");
    }

    [Fact]
    public async Task AFailureBeforeTheRequestLeaves_IsNotSent()
    {
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no token"));
        var sender = new EmailChannelSender(factory.Object, NullLogger<EmailChannelSender>.Instance);

        var act = () => sender.SendAsync(Request(userMode: true));

        var failure = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        failure.Extensions![EmailChannelSender.SendOutcomeExtension].Should().Be(EmailChannelSender.SendOutcomeNotSent);
    }

    private static async Task<SdapProblemException> SendAndFail(Transport transport, bool userMode)
    {
        var act = () => Sender(transport, userMode).SendAsync(Request(userMode));
        return (await act.Should().ThrowAsync<SdapProblemException>()).Which;
    }

    private static EmailChannelSender Sender(Transport transport, bool userMode)
    {
        var options = new GraphResilienceOptions
        {
            RetryCount = Retries,
            RetryBackoffSeconds = 0,
            HonorRetryAfterHeader = false,
            TimeoutSeconds = 30,
            CircuitBreakerFailureThreshold = 100,
        };
        var handler = new GraphHttpMessageHandler(NullLogger<GraphHttpMessageHandler>.Instance, Options.Create(options)) { InnerHandler = transport };
        var client = new GraphServiceClient(new HttpClient(handler));
        var factory = new Mock<IGraphClientFactory>();
        if (userMode)
            factory.Setup(f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(client);
        else
            factory.Setup(f => f.ForApp()).Returns(client);
        return new EmailChannelSender(factory.Object, NullLogger<EmailChannelSender>.Instance);
    }

    private static ChannelSendRequest Request(bool userMode) => new()
    {
        Communication = new SendCommunicationRequest { To = ["firm@example.com"], Subject = "Budget inquiry", Body = "Please explain." },
        FromAddress = "mailbox@contoso.com",
        UserMode = userMode,
        HttpContext = userMode ? new DefaultHttpContext() : null,
        CorrelationId = "task-191",
    };

    /// <summary>Answers each sendMail with the next status (the last repeats); any other request (a Sent Items read) is empty.</summary>
    private sealed class Transport(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int SendMailRequests { get; private set; }

        /// <summary>When set, every sendMail throws it after the request is counted (a transport fault or a timeout).</summary>
        public Exception? Fault { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json(HttpStatusCode.OK, """{"value":[]}"""));

            var status = statuses[Math.Min(SendMailRequests, statuses.Length - 1)];
            SendMailRequests++;
            if (Fault is not null)
                return Task.FromException<HttpResponseMessage>(Fault);
            return Task.FromResult((int)status < 300
                ? new HttpResponseMessage(status)
                : Json(status, """{"error":{"code":"Failed","message":"send failed"}}"""));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
