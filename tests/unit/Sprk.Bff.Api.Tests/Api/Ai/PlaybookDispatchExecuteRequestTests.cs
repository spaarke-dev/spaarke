using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Wire-contract tests for <see cref="PlaybookDispatchExecuteRequest"/>
/// (chat-routing-redesign-r1 Track 1 / FR-50 production-smoke unblocker).
///
/// <para>
/// The request shape is bound to the FE call site at
/// <c>SpaarkeAi/src/components/conversation/ConversationPane.tsx:1565</c> (task 117b).
/// The FE sends camelCase JSON: <c>{ playbookId, sessionAttachmentIds, originalMessage, sessionId }</c>.
/// These tests guard that:
/// <list type="bullet">
///   <item><description>The record exposes exactly the four FR-50 fields.</description></item>
///   <item><description>JSON deserialization with the BFF default options accepts the camelCase wire shape.</description></item>
///   <item><description>Null / missing fields don't throw on deserialization (FE may omit values).</description></item>
/// </list>
/// </para>
/// </summary>
[Trait("category", "binding-invariant")]
public class PlaybookDispatchExecuteRequestTests
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void PlaybookDispatchExecuteRequest_HasExactlyFourFr50Fields()
    {
        var properties = typeof(PlaybookDispatchExecuteRequest)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.GetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        properties.Should().BeEquivalentTo(new[]
        {
            nameof(PlaybookDispatchExecuteRequest.OriginalMessage),
            nameof(PlaybookDispatchExecuteRequest.PlaybookId),
            nameof(PlaybookDispatchExecuteRequest.SessionAttachmentIds),
            nameof(PlaybookDispatchExecuteRequest.SessionId),
        });
    }

    [Fact]
    public void PlaybookDispatchExecuteRequest_DeserializesCamelCaseFromFE()
    {
        var json = """
            {
                "playbookId": "11111111-2222-3333-4444-555555555555",
                "sessionAttachmentIds": ["att-1", "att-2"],
                "originalMessage": "summarize the attached PDF",
                "sessionId": "session-abc"
            }
            """;

        var request = JsonSerializer.Deserialize<PlaybookDispatchExecuteRequest>(json, DefaultOptions);

        request.Should().NotBeNull();
        request!.PlaybookId.Should().Be("11111111-2222-3333-4444-555555555555");
        request.SessionAttachmentIds.Should().NotBeNull();
        request.SessionAttachmentIds!.Should().Equal("att-1", "att-2");
        request.OriginalMessage.Should().Be("summarize the attached PDF");
        request.SessionId.Should().Be("session-abc");
    }

    [Fact]
    public void PlaybookDispatchExecuteRequest_DeserializesWithMissingFields_AsNullProperties()
    {
        // FE may legitimately omit sessionAttachmentIds when no files are attached
        // and originalMessage when the user selected from the Library modal (no NL prompt).
        var json = """
            {
                "playbookId": "11111111-2222-3333-4444-555555555555",
                "sessionId": "session-xyz"
            }
            """;

        var request = JsonSerializer.Deserialize<PlaybookDispatchExecuteRequest>(json, DefaultOptions);

        request.Should().NotBeNull();
        request!.PlaybookId.Should().Be("11111111-2222-3333-4444-555555555555");
        request.SessionAttachmentIds.Should().BeNull();
        request.OriginalMessage.Should().BeNull();
        request.SessionId.Should().Be("session-xyz");
    }
}
