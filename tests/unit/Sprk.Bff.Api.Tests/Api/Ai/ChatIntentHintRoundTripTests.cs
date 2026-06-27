using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Wire-format round-trip tests for the <see cref="ChatSendMessageRequest.IntentHint"/>
/// field. Added by spaarke-ai-platform-chat-routing-redesign-r1 / task 022 (2026-06-22).
///
/// What changed: the chat-request body field formerly named <c>commandIntent</c>
/// was renamed atomically (FE + BE) to <c>intentHint</c> per spec FR-07 + Owner
/// Clarification Q5 (no back-compat alias).
///
/// These tests assert:
///   • The C# property <c>IntentHint</c> deserializes from JSON key <c>intentHint</c>
///     (the .NET minimal-API default camelCase JSON contract).
///   • A null / absent <c>intentHint</c> deserializes to a record with
///     <c>IntentHint == null</c> (NFR-11 backward-compat: silently ignored).
///   • The old key <c>commandIntent</c> is silently ignored — the renamed field
///     becomes optional (default null), so a stale-client payload does NOT
///     produce a 400 — it just routes via Layer 1 keyword scoring (NFR-11).
///   • Serializing a record with a populated <c>IntentHint</c> emits the
///     <c>intentHint</c> JSON key (FE will deserialize this key on streamed
///     responses if a future tool surfaces the value).
///
/// These are pure JSON round-trip assertions — no WebApplicationFactory required.
/// The minimal-API <c>JsonSerializerOptions.PropertyNamingPolicy = CamelCase</c>
/// is the ASP.NET 8 default; we replicate it explicitly here.
/// </summary>
public class ChatIntentHintRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // -------------------------------------------------------------------------
    // Deserialization: new field name binds correctly
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("summarize")]
    [InlineData("draft")]
    [InlineData("extract-entities")]
    [InlineData("analyze")]
    public void Deserialize_NewFieldName_intentHint_PopulatesIntentHintProperty(string value)
    {
        var json = $$"""
        {
            "message": "anything",
            "intentHint": "{{value}}"
        }
        """;

        var request = JsonSerializer.Deserialize<ChatSendMessageRequest>(json, JsonOptions);

        request.Should().NotBeNull();
        request!.Message.Should().Be("anything");
        request.IntentHint.Should().Be(value,
            because: "wire field `intentHint` binds to the C# property of the same name");
    }

    // -------------------------------------------------------------------------
    // Deserialization: absent field → null property (NFR-11 backward compat)
    // -------------------------------------------------------------------------

    [Fact]
    public void Deserialize_FieldAbsent_IntentHintIsNull()
    {
        const string json = """
        {
            "message": "anything"
        }
        """;

        var request = JsonSerializer.Deserialize<ChatSendMessageRequest>(json, JsonOptions);

        request.Should().NotBeNull();
        request!.IntentHint.Should().BeNull(
            because: "absent field defaults to null per the record's optional parameter default — NFR-11");
    }

    [Fact]
    public void Deserialize_FieldExplicitlyNull_IntentHintIsNull()
    {
        const string json = """
        {
            "message": "anything",
            "intentHint": null
        }
        """;

        var request = JsonSerializer.Deserialize<ChatSendMessageRequest>(json, JsonOptions);

        request.Should().NotBeNull();
        request!.IntentHint.Should().BeNull(
            because: "explicit null parses to null property — NFR-11");
    }

    // -------------------------------------------------------------------------
    // Deserialization: OLD field name `commandIntent` is silently ignored.
    //   Stale FE clients that haven't shipped the rename send `commandIntent`;
    //   per spec FR-07 (no back-compat alias), the BE neither errors nor binds.
    //   The renamed field becomes optional (default null), so the request still
    //   deserializes — it just routes via the Layer 1 keyword path (NFR-11).
    // -------------------------------------------------------------------------

    [Fact]
    public void Deserialize_OldFieldName_commandIntent_IsSilentlyIgnored()
    {
        const string json = """
        {
            "message": "anything",
            "commandIntent": "summarize"
        }
        """;

        // Default JsonSerializerDefaults.Web ignores unknown properties.
        var request = JsonSerializer.Deserialize<ChatSendMessageRequest>(json, JsonOptions);

        request.Should().NotBeNull();
        request!.Message.Should().Be("anything");
        request.IntentHint.Should().BeNull(
            because: "old field name `commandIntent` is unknown and silently ignored — per FR-07 (no back-compat alias)");
    }

    // -------------------------------------------------------------------------
    // Serialization: populated property emits the new JSON key
    // -------------------------------------------------------------------------

    [Fact]
    public void Serialize_PopulatedIntentHint_EmitsIntentHintJsonKey()
    {
        var request = new ChatSendMessageRequest(
            Message: "anything",
            IntentHint: "summarize");

        var json = JsonSerializer.Serialize(request, JsonOptions);

        json.Should().Contain("\"intentHint\":\"summarize\"",
            because: "the C# property `IntentHint` serializes to the camelCase wire key `intentHint`");
        json.Should().NotContain("commandIntent",
            because: "the old key MUST NOT appear in any serialized payload — atomic rename per FR-07");
    }

    [Fact]
    public void Serialize_NullIntentHint_OmitsTheField_OrEmitsNull()
    {
        // Either behavior is acceptable; both are NFR-11 backward-compatible.
        // What we assert is the ABSENCE of the old key.
        var request = new ChatSendMessageRequest(
            Message: "anything",
            IntentHint: null);

        var json = JsonSerializer.Serialize(request, JsonOptions);

        json.Should().NotContain("commandIntent",
            because: "the old key MUST NOT appear in any serialized payload — atomic rename per FR-07");
    }

    // -------------------------------------------------------------------------
    // Complete round-trip — FE shape (intentHint) survives BE binding
    // -------------------------------------------------------------------------

    [Fact]
    public void RoundTrip_FeShape_intentHint_BindsAndRebuilds()
    {
        const string feShape = """
        {
            "message": "/summarize the engagement letter",
            "documentId": "doc-123",
            "intentHint": "summarize"
        }
        """;

        var request = JsonSerializer.Deserialize<ChatSendMessageRequest>(feShape, JsonOptions);

        request.Should().NotBeNull();
        request!.Message.Should().Be("/summarize the engagement letter");
        request.DocumentId.Should().Be("doc-123");
        request.IntentHint.Should().Be("summarize");

        var rebuilt = JsonSerializer.Serialize(request, JsonOptions);
        rebuilt.Should().Contain("\"intentHint\":\"summarize\"");
        rebuilt.Should().NotContain("commandIntent");
    }
}
