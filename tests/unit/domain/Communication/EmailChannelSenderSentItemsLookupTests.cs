using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ontology task 214 (#2774, DI-150): after a send, <see cref="EmailChannelSender"/> reads the just-sent message's
/// Internet-Message-Id from Sent Items. Graph answers 400 <c>InefficientFilter</c> to a messages query whose <c>$orderby</c>
/// property is not also in <c>$filter</c>, first (Microsoft Learn, "List messages": "Using filter and orderby in the same
/// query"), so the old <c>subject eq '...'</c> + <c>sentDateTime desc</c> pair never returned a message. The fake here
/// refuses a query that breaks that rule, as Graph does, so the test fails on the old query and passes on the fixed one.
/// MUTATION: dropping <c>sentDateTime ge ...</c> from the filter (either branch), or putting it after <c>subject eq</c>, makes
/// the fake answer 400 and the Internet-Message-Id come back null.
/// </summary>
public class EmailChannelSenderSentItemsLookupTests
{
    private const string FoundId = "<found-id@example.test>";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSentItemsLookup_SatisfiesGraphsFilterOrderbyRule_AndReturnsTheInternetMessageId(bool userMode)
    {
        var transport = new Transport(Reply.Messages(FoundId));
        var before = DateTimeOffset.UtcNow;

        var result = await Sender(transport, userMode).SendAsync(Request(userMode, "Budget inquiry"));

        result.ProviderMessageId.Should().Be(FoundId);
        var lookup = transport.Lookups.Should().ContainSingle().Subject;
        lookup.Path.Should().Contain(userMode ? "/me/" : "/users/", "the branch decides which mailbox is read");
        lookup.Path.Should().EndWith("/mailFolders/sentitems/messages");
        lookup.Orderby.Should().Be("sentDateTime desc");
        lookup.Filter.Should().MatchRegex(@"^sentDateTime ge \S+ and subject eq 'Budget inquiry'$");
        lookup.GraphRefusal.Should().BeNull();

        var bound = DateTimeOffset.Parse(Regex.Match(lookup.Filter!, @"sentDateTime ge (\S+)").Groups[1].Value,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        bound.Should().BeOnOrBefore(before, "the lower bound is the send start minus a margin, never in the future");
        bound.Should().BeAfter(before.AddMinutes(-10), "the bound narrows the read to this send, not the whole folder");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSubjectIsStillEscaped_WithTheDateBoundFirst(bool userMode)
    {
        var transport = new Transport(Reply.Messages(FoundId));

        await Sender(transport, userMode).SendAsync(Request(userMode, "O'Brien's budget"));

        var lookup = transport.Lookups.Should().ContainSingle().Subject;
        lookup.Filter.Should().EndWith("subject eq 'O''Brien''s budget'");
        lookup.GraphRefusal.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANotFoundLookup_ReturnsNull_AndTheSendStillSucceeds(bool userMode)
    {
        var transport = new Transport(Reply.Messages());

        var result = await Sender(transport, userMode).SendAsync(Request(userMode, "Budget inquiry"));

        result.ProviderMessageId.Should().BeNull();
        result.FromAddress.Should().Be("mailbox@contoso.com");
        transport.Lookups.Should().ContainSingle().Which.GraphRefusal.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AGraphErrorOnTheLookup_IsSwallowed_AndTheSendStillSucceeds(bool userMode)
    {
        var transport = new Transport(Reply.Failure(HttpStatusCode.InternalServerError));

        var result = await Sender(transport, userMode).SendAsync(Request(userMode, "Budget inquiry"));

        result.ProviderMessageId.Should().BeNull();
        transport.SendMailRequests.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASystemMailSend_StillSkipsTheLookup(bool userMode)
    {
        var transport = new Transport(Reply.Messages(FoundId));
        var request = Request(userMode, "Your invitation");
        request = new ChannelSendRequest
        {
            Communication = new SendCommunicationRequest
            {
                To = ["invitee@example.com"],
                Subject = "Your invitation",
                Body = "<p>body</p>",
                SystemMail = new SystemMailOptions { StoredBody = "<p>stored</p>" },
            },
            FromAddress = request.FromAddress,
            UserMode = userMode,
            HttpContext = request.HttpContext,
            CorrelationId = request.CorrelationId,
        };

        var result = await Sender(transport, userMode).SendAsync(request);

        result.ProviderMessageId.Should().BeNull();
        transport.Lookups.Should().BeEmpty();
    }

    // Must-not-fire control: the fake itself accepts a query that meets the rule and refuses one that does not, so a pass above
    // is the query's doing and not a fake that never refuses.
    [Theory]
    [InlineData("subject eq 'x'", "sentDateTime desc", true)]
    [InlineData("subject eq 'x' and sentDateTime ge 2026-01-01T00:00:00Z", "sentDateTime desc", true)]
    [InlineData("sentDateTime ge 2026-01-01T00:00:00Z and subject eq 'x'", "sentDateTime desc", false)]
    [InlineData("sentDateTime ge 2026-01-01T00:00:00Z", "sentDateTime desc", false)]
    [InlineData("subject eq 'x'", null, false)]
    public void TheFakesRuleMatchesMicrosoftLearn(string filter, string? orderby, bool refused)
    {
        (Transport.Refusal(filter, orderby) is not null).Should().Be(refused);
    }

    private static EmailChannelSender Sender(Transport transport, bool userMode)
    {
        var client = new GraphServiceClient(new HttpClient(transport));
        var factory = new Mock<IGraphClientFactory>();
        if (userMode)
            factory.Setup(f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(client);
        else
            factory.Setup(f => f.ForApp()).Returns(client);
        return new EmailChannelSender(factory.Object, NullLogger<EmailChannelSender>.Instance);
    }

    private static ChannelSendRequest Request(bool userMode, string subject) => new()
    {
        Communication = new SendCommunicationRequest { To = ["firm@example.com"], Subject = subject, Body = "Please explain." },
        FromAddress = "mailbox@contoso.com",
        UserMode = userMode,
        HttpContext = userMode ? new DefaultHttpContext() : null,
        CorrelationId = "task-214",
    };

    private sealed record Reply(HttpStatusCode Status, string Body)
    {
        public static Reply Messages(params string[] internetMessageIds) => new(HttpStatusCode.OK,
            "{\"value\":[" + string.Join(",", internetMessageIds.Select(id => "{\"internetMessageId\":\"" + id + "\"}")) + "]}");

        public static Reply Failure(HttpStatusCode status) => new(status, "{}");
    }

    private sealed record Lookup(string Path, string? Filter, string? Orderby, string? GraphRefusal);

    /// <summary>sendMail answers 202; a Sent Items read answers <paramref name="reply"/>, unless the query breaks Graph's rule.</summary>
    private sealed class Transport(Reply reply) : HttpMessageHandler
    {
        public List<Lookup> Lookups { get; } = new();
        public int SendMailRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase))
            {
                SendMailRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
            }

            var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
            var filter = query["$filter"];
            var orderby = query["$orderby"];
            var refusal = Refusal(filter, orderby);
            Lookups.Add(new Lookup(path, filter, orderby, refusal));

            return Task.FromResult(refusal is not null
                ? Json(HttpStatusCode.BadRequest,
                    "{\"error\":{\"code\":\"InefficientFilter\",\"message\":\"The restriction or sort order is too complex for this operation.\"}}")
                : Json(reply.Status, reply.Body));
        }

        /// <summary>
        /// Graph's documented rule for messages (Microsoft Learn, "List messages"): every <c>$orderby</c> property appears in
        /// <c>$filter</c>, in the same order, ahead of any filter property that is not ordered. Graph also infers an order from a
        /// <c>$filter</c>, so a filter with no <c>$orderby</c> is accepted.
        /// </summary>
        public static string? Refusal(string? filter, string? orderby)
        {
            if (string.IsNullOrEmpty(orderby)) return null;
            if (string.IsNullOrEmpty(filter)) return "InefficientFilter";
            var ordered = orderby.Split(',', StringSplitOptions.TrimEntries).Select(o => o.Split(' ')[0]).ToList();
            var filterProps = Regex.Matches(filter, @"(?<![\w'])(\w+)\s+(?:eq|ne|ge|gt|le|lt)\s").Select(m => m.Groups[1].Value).Distinct().ToList();
            var leading = filterProps.Take(ordered.Count).ToList();
            return leading.SequenceEqual(ordered) ? null : "InefficientFilter";
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
