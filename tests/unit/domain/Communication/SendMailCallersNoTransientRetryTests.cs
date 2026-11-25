using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Http;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ontology task 212 (#2779, DI-153): the two sendMail callers task 191 left on the standard Graph retry now mark the send with
/// <see cref="NoTransientRetryOption"/>. <c>sendMail</c> is not idempotent: after a 5xx or a 408 Graph may already have delivered
/// the message, so production's <see cref="GraphHttpMessageHandler"/> must send it ONCE and retry only a 429 (Graph did not
/// process a throttled request). Each test drives the caller's real code through the real handler over a fake transport.
/// MUTATION: removing <c>rc =&gt; rc.Options.Add(sendOption)</c> from either caller sends it four times after a 503.
/// </summary>
public class SendMailCallersNoTransientRetryTests
{
    private const int Retries = 3;

    // --- playbook Send Email node -------------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Node_A5xxOr408OnSendMail_IsSentOnce_AndIsReportedAsAnUnknownOutcome(HttpStatusCode status)
    {
        var transport = new Transport(status);

        var output = await RunNode(transport);

        transport.SendMailRequests.Should().Be(1, "Graph may already have delivered it, so it is never sent again");
        output.Success.Should().BeFalse();
        output.ErrorCode.Should().Be(NodeErrorCodes.InternalError, "the node keeps its existing failure code");
        output.ErrorMessage.Should().StartWith("Failed to send email");
        output.ErrorMessage.Should().Contain("may have been delivered", "a retry by the playbook run would send it twice");
    }

    [Fact]
    public async Task Node_ATransportFaultOnSendMail_IsSentOnce_AndIsReportedAsAnUnknownOutcome()
    {
        var transport = new Transport(HttpStatusCode.Accepted) { Fault = new HttpRequestException("The connection was reset.") };

        var output = await RunNode(transport);

        transport.SendMailRequests.Should().Be(1);
        output.Success.Should().BeFalse();
        output.ErrorMessage.Should().Contain("may have been delivered");
    }

    /// <summary>Must-not-fire: a settled 4xx refusal is a failure that did not send, and carries no "may have been delivered".</summary>
    [Fact]
    public async Task Node_A400OnSendMail_IsSentOnce_AndIsNotReportedAsPossiblyDelivered()
    {
        var transport = new Transport(HttpStatusCode.BadRequest);

        var output = await RunNode(transport);

        transport.SendMailRequests.Should().Be(1);
        output.Success.Should().BeFalse();
        output.ErrorMessage.Should().StartWith("Failed to send email");
        output.ErrorMessage.Should().NotContain("may have been delivered");
    }

    [Fact]
    public async Task Node_A429OnSendMail_IsRetried_AndThenSucceeds()
    {
        var transport = new Transport(HttpStatusCode.TooManyRequests, HttpStatusCode.Accepted);

        var output = await RunNode(transport);

        transport.SendMailRequests.Should().Be(2, "Graph did not process a throttled send, so it is sent again");
        output.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Node_ASuccess_SendsOnce_AndSucceeds()
    {
        var transport = new Transport(HttpStatusCode.Accepted);

        var output = await RunNode(transport);

        transport.SendMailRequests.Should().Be(1);
        output.Success.Should().BeTrue();
    }

    // --- mailbox verification email -----------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Verification_A5xxOr408OnSendMail_IsSentOnce_AndTheAccountFails(HttpStatusCode status)
    {
        var transport = new Transport(status);

        var result = await Verify(transport);

        transport.SendMailRequests.Should().Be(1, "a verification message must not reach the mailbox up to four times");
        result.Status.Should().Be(VerificationStatus.Failed);
        result.SendCapabilityVerified.Should().BeFalse();
        result.FailureReason.Should().StartWith("Send test failed");
    }

    [Fact]
    public async Task Verification_A429OnSendMail_IsRetried_AndThenVerifies()
    {
        var transport = new Transport(HttpStatusCode.TooManyRequests, HttpStatusCode.Accepted);

        var result = await Verify(transport);

        transport.SendMailRequests.Should().Be(2, "Graph did not process a throttled send, so it is sent again");
        result.Status.Should().Be(VerificationStatus.Verified);
    }

    [Fact]
    public async Task Verification_ASuccess_SendsOnce_AndVerifies()
    {
        var transport = new Transport(HttpStatusCode.Accepted);

        var result = await Verify(transport);

        transport.SendMailRequests.Should().Be(1);
        result.Status.Should().Be(VerificationStatus.Verified);
        result.SendCapabilityVerified.Should().BeTrue();
    }

    // --- harness ------------------------------------------------------------------------------------------------

    private static GraphServiceClient Client(Transport transport)
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
        return new GraphServiceClient(new HttpClient(handler));
    }

    private static async Task<NodeOutput> RunNode(Transport transport)
    {
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(Client(transport));

        var templates = new Mock<ITemplateEngine>();
        templates.Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>()))
            .Returns((string template, IDictionary<string, object?> _) => template);
        templates.Setup(t => t.Render(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>()))
            .Returns((string template, Dictionary<string, object?> _) => template);

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext());

        var executor = new SendEmailNodeExecutor(
            templates.Object, factory.Object, accessor.Object, NullLogger<SendEmailNodeExecutor>.Instance);

        var actionId = Guid.NewGuid();
        var context = new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = Guid.NewGuid(),
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Send Email Node",
                ExecutionOrder = 1,
                OutputVariable = "emailResult",
                ConfigJson = """{"to":["firm@example.com"],"subject":"Analysis complete","body":"Done."}""",
                IsActive = true,
            },
            Action = new AnalysisAction { Id = actionId, Name = "Send Email" },
            ExecutorType = ExecutorType.SendEmail,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant",
        };

        return await executor.ExecuteAsync(context, CancellationToken.None);
    }

    private static async Task<VerificationResult> Verify(Transport transport)
    {
        var accountId = Guid.NewGuid();
        var account = new DataverseEntity("sprk_communicationaccount") { Id = accountId };
        account["sprk_emailaddress"] = "shared@contoso.com";
        account["sprk_name"] = "Shared Mailbox";
        account["sprk_sendenabled"] = true;
        account["sprk_receiveenabled"] = false;
        account["sprk_accounttype"] = new Microsoft.Xrm.Sdk.OptionSetValue(100000000);

        var dataverse = new Mock<IDataverseService>();
        dataverse.Setup(d => d.RetrieveAsync("sprk_communicationaccount", accountId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        dataverse.Setup(d => d.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var accountService = new CommunicationAccountService(
            dataverse.Object, dataverse.Object,
            new Mock<Microsoft.Extensions.Caching.Distributed.IDistributedCache>().Object,
            NullLogger<CommunicationAccountService>.Instance);

        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(Client(transport));

        var service = new MailboxVerificationService(
            factory.Object,
            dataverse.Object,
            accountService,
            Options.Create(new CommunicationOptions { WebhookNotificationUrl = "https://localhost/api/webhooks/graph", WebhookClientState = "test-client-state" }),
            NullLogger<MailboxVerificationService>.Instance);

        return await service.VerifyAsync(accountId);
    }

    /// <summary>Answers each sendMail with the next status (the last repeats).</summary>
    private sealed class Transport(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int SendMailRequests { get; private set; }

        /// <summary>When set, every sendMail throws it after the request is counted.</summary>
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
