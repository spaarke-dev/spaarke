using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// unified-access-control-r2 task 185 (owner round 98 Q4 iii): <see cref="EmailChannelSender"/> sends a system-mail
/// message with <c>SaveToSentItems = false</c> (and so does not look it up in Sent Items), and every other message with
/// <c>SaveToSentItems = true</c> as before.
/// MUTATION: hard-coding <c>SaveToSentItems = true</c> again fails <see cref="SaveToSentItems_IsFalseOnlyForSystemMail"/>.
/// </summary>
public class EmailChannelSenderSystemMailTests
{
    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 1)]
    public async Task SaveToSentItems_IsFalseOnlyForSystemMail(bool systemMail, bool expectedSave, int expectedSentItemsReads)
    {
        var transport = new Transport();
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(new GraphServiceClient(new HttpClient(transport)));
        var sender = new EmailChannelSender(factory.Object, NullLogger<EmailChannelSender>.Instance);

        await sender.SendAsync(new ChannelSendRequest
        {
            Communication = new SendCommunicationRequest
            {
                To = ["invitee@example.com"],
                Subject = "Your invitation",
                Body = "<p>body</p>",
                SystemMail = systemMail ? new SystemMailOptions { StoredBody = "<p>stored</p>" } : null,
            },
            FromAddress = "noreply@contoso.com",
            UserMode = false,
            CorrelationId = "task-185",
        });

        var sendMail = transport.Bodies.Should().ContainSingle(b => b.Path.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase)).Subject;
        JsonDocument.Parse(sendMail.Body!).RootElement.Prop("saveToSentItems").GetBoolean().Should().Be(expectedSave);
        transport.Bodies.Count(b => b.Path.Contains("sentitems", StringComparison.OrdinalIgnoreCase))
            .Should().Be(expectedSentItemsReads);
    }

    private sealed class Transport : HttpMessageHandler
    {
        public List<(string Path, string? Body)> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add((request.RequestUri!.AbsolutePath,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return request.RequestUri.AbsolutePath.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase)
                ? new HttpResponseMessage(HttpStatusCode.Accepted)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"value":[]}""", System.Text.Encoding.UTF8, "application/json"),
                };
        }
    }
}

/// <summary>Graph's action bodies are PascalCase (<c>SaveToSentItems</c>, <c>Message</c>); read properties case-insensitively.</summary>
internal static class GraphJsonExtensions
{
    public static JsonElement Prop(this JsonElement element, string name)
        => element.EnumerateObject().Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
