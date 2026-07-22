using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Notifications.Envelopes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Notifications;

/// <summary>
/// Contract-anchor tests for the notification-spine envelope types (task 013,
/// design.md §5A.3 / §5B.4 / §10 #5). These pin the wire shape every downstream
/// producer (Phase 2/4/5) writes and every consumer (SignalR client, poll
/// endpoint, Assistant renderer) reads — MAINTAIN-class: deleting them would let
/// a field-list drift or a `kind` taxonomy break ship silently (NFR-02/NFR-03
/// privacy invariant + the "one envelope shape" project guarantee).
///
/// Self-contained: no DI, no WebApplicationFactory, no mocks — pure domain-logic
/// serialization/shape tests (ADR-038 domain-logic category).
/// </summary>
public sealed class EnvelopeSerializationTests
{
    // ── Closed field lists, transcribed verbatim from design.md §5A.3 / §5B.4 ──
    // These are the SOLE source of truth the reflection test (below) checks against.

    private static readonly string[] CommunicationEnvelopeFields =
    {
        "Kind", "CommunicationId", "ThreadId", "Channel", "Direction",
        "RegardingRecordId", "SenderDisplay", "Snippet", "BadgeDelta",
    };

    private static readonly string[] SuggestionEnvelopeFields =
    {
        "Kind", "SuggestionId", "Source", "RegardingRecordId", "Title",
        "Snippet", "ActionHint", "ExpiresAt",
    };

    // Substrings that would signal a forbidden field slipped in (NFR-02/NFR-03):
    // a message body, a privileged-content excerpt beyond `Snippet`, or a
    // pre-authorized action token.
    private static readonly string[] ForbiddenNameSubstrings =
    {
        "body", "content", "message", "token", "authorization", "auth", "payload", "html", "text",
    };

    private static CommunicationEnvelope NewCommunicationEnvelope(
        NotificationKind kind = NotificationKind.CommunicationArrived,
        string? snippet = "Quick preview of the thread.") => new()
    {
        Kind = kind,
        CommunicationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ThreadId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Channel = "email",
        Direction = "inbound",
        RegardingRecordId = "33333333-3333-3333-3333-333333333333",
        SenderDisplay = "Alice Attorney",
        Snippet = snippet,
        BadgeDelta = 1,
    };

    private static SuggestionEnvelope NewSuggestionEnvelope(string? snippet = "Consider opening a matter for this.") => new()
    {
        Kind = NotificationKind.Suggestion,
        SuggestionId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
        Source = "daily-briefing",
        RegardingRecordId = "55555555-5555-5555-5555-555555555555",
        Title = "New matter suggested",
        Snippet = snippet,
        ActionHint = "create-matter",
        ExpiresAt = DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
    };

    // ── Round-trip: CommunicationEnvelope ──────────────────────────────────────

    [Theory]
    [InlineData(NotificationKind.CommunicationArrived)]
    [InlineData(NotificationKind.CommunicationAssessed)]
    public void CommunicationEnvelope_RoundTrip_WithSnippet_EqualsOriginal(NotificationKind kind)
    {
        var original = NewCommunicationEnvelope(kind).Validate();

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<CommunicationEnvelope>(json);

        roundTripped.Should().Be(original);
    }

    [Fact]
    public void CommunicationEnvelope_RoundTrip_WithNullSnippet_EqualsOriginal()
    {
        var original = NewCommunicationEnvelope(snippet: null).Validate();

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<CommunicationEnvelope>(json);

        roundTripped.Should().Be(original);
        roundTripped!.Snippet.Should().BeNull();
    }

    [Fact]
    public void CommunicationEnvelope_Serialize_UsesCamelCaseWireFieldNames()
    {
        var json = JsonSerializer.Serialize(NewCommunicationEnvelope());
        using var doc = JsonDocument.Parse(json);

        foreach (var expectedField in new[]
                 {
                     "kind", "communicationId", "threadId", "channel", "direction",
                     "regardingRecordId", "senderDisplay", "snippet", "badgeDelta",
                 })
        {
            doc.RootElement.TryGetProperty(expectedField, out _).Should().BeTrue(
                $"the wire contract (design.md §5A.3) names the field '{expectedField}'");
        }
    }

    // ── Round-trip: SuggestionEnvelope ─────────────────────────────────────────

    [Fact]
    public void SuggestionEnvelope_RoundTrip_WithSnippet_EqualsOriginal()
    {
        var original = NewSuggestionEnvelope().Validate();

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<SuggestionEnvelope>(json);

        roundTripped.Should().Be(original);
    }

    [Fact]
    public void SuggestionEnvelope_RoundTrip_WithNullSnippet_EqualsOriginal()
    {
        var original = NewSuggestionEnvelope(snippet: null).Validate();

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<SuggestionEnvelope>(json);

        roundTripped.Should().Be(original);
        roundTripped!.Snippet.Should().BeNull();
    }

    [Fact]
    public void SuggestionEnvelope_Serialize_UsesCamelCaseWireFieldNames()
    {
        var json = JsonSerializer.Serialize(NewSuggestionEnvelope());
        using var doc = JsonDocument.Parse(json);

        foreach (var expectedField in new[]
                 {
                     "kind", "suggestionId", "source", "regardingRecordId", "title",
                     "snippet", "actionHint", "expiresAt",
                 })
        {
            doc.RootElement.TryGetProperty(expectedField, out _).Should().BeTrue(
                $"the wire contract (design.md §5B.4) names the field '{expectedField}'");
        }
    }

    // ── `kind` discriminator round-trip (every taxonomy value, active + reserved) ──

    [Theory]
    [InlineData(NotificationKind.Suggestion, "suggestion")]
    [InlineData(NotificationKind.CommunicationAssessed, "communication-assessed")]
    [InlineData(NotificationKind.CommunicationArrived, "communication-arrived")]
    [InlineData(NotificationKind.JobComplete, "job-complete")]
    [InlineData(NotificationKind.Share, "share")]
    [InlineData(NotificationKind.SystemAlert, "system-alert")]
    public void NotificationKind_RoundTrip_SerializesToLockedWireValueAndBack(NotificationKind kind, string expectedWireValue)
    {
        var json = JsonSerializer.Serialize(kind);

        json.Should().Be($"\"{expectedWireValue}\"");
        JsonSerializer.Deserialize<NotificationKind>(json).Should().Be(kind);
    }

    // ── Unrecognized `kind` FAILS deserialization (not a silent default) ──────

    [Theory]
    [InlineData("\"communication-recieved\"")] // typo of communication-arrived
    [InlineData("\"suggestions\"")]             // typo of suggestion
    [InlineData("\"COMMUNICATION-ARRIVED\"")]   // wrong case
    [InlineData("\"totally-novel-kind\"")]      // never-declared kind
    [InlineData("42")]                          // non-string token
    public void NotificationKind_Deserialize_WithUnrecognizedValue_ThrowsJsonException(string malformedJson)
    {
        var act = () => JsonSerializer.Deserialize<NotificationKind>(malformedJson);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void CommunicationEnvelope_Deserialize_WithUnrecognizedKind_ThrowsJsonException()
    {
        var json = NewCommunicationEnvelope().ToJsonWithKindOverride("not-a-real-kind");

        var act = () => JsonSerializer.Deserialize<CommunicationEnvelope>(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void SuggestionEnvelope_Deserialize_WithUnrecognizedKind_ThrowsJsonException()
    {
        var json = NewSuggestionEnvelope().ToJsonWithKindOverride("not-a-real-kind");

        var act = () => JsonSerializer.Deserialize<SuggestionEnvelope>(json);

        act.Should().Throw<JsonException>();
    }

    // ── Kind-shape guard: each envelope type only accepts its own kind(s) ─────

    [Theory]
    [InlineData(NotificationKind.Suggestion)]
    [InlineData(NotificationKind.JobComplete)]
    [InlineData(NotificationKind.Share)]
    [InlineData(NotificationKind.SystemAlert)]
    public void CommunicationEnvelope_Validate_WithNonCommunicationKind_Throws(NotificationKind wrongKind)
    {
        var envelope = NewCommunicationEnvelope(wrongKind);

        var act = envelope.Validate;

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(NotificationKind.CommunicationArrived)]
    [InlineData(NotificationKind.CommunicationAssessed)]
    [InlineData(NotificationKind.JobComplete)]
    [InlineData(NotificationKind.Share)]
    [InlineData(NotificationKind.SystemAlert)]
    public void SuggestionEnvelope_Validate_WithNonSuggestionKind_Throws(NotificationKind wrongKind)
    {
        var envelope = NewSuggestionEnvelope() with { Kind = wrongKind };

        var act = envelope.Validate;

        act.Should().Throw<InvalidOperationException>();
    }

    // ── Reflection-based negative test: closed field set (CI guard) ───────────
    // A future PR that adds a forbidden/undeclared field to either envelope
    // fails THIS test rather than silently drifting from design.md §5A.3/§5B.4.

    [Fact]
    public void CommunicationEnvelope_PublicProperties_MatchClosedFieldListExactly()
    {
        var actual = PublicInstancePropertyNames(typeof(CommunicationEnvelope));

        actual.Should().BeEquivalentTo(CommunicationEnvelopeFields,
            "the communication envelope MUST contain exactly the design.md §5A.3 field list — no additions, no omissions");
    }

    [Fact]
    public void SuggestionEnvelope_PublicProperties_MatchClosedFieldListExactly()
    {
        var actual = PublicInstancePropertyNames(typeof(SuggestionEnvelope));

        actual.Should().BeEquivalentTo(SuggestionEnvelopeFields,
            "the suggestion envelope MUST contain exactly the design.md §5B.4 field list — no additions, no omissions");
    }

    [Theory]
    [MemberData(nameof(EnvelopeTypes))]
    public void Envelope_PublicProperties_ContainNoForbiddenContentOrTokenField(Type envelopeType)
    {
        // NFR-02/NFR-03: no field may carry a message body, privileged content beyond
        // the gated optional `Snippet`, or a pre-authorized action token. `Snippet` and
        // `ActionHint` are the two allow-listed exceptions the design explicitly names.
        var allowListed = new[] { "Snippet", "ActionHint" };

        var offending = PublicInstancePropertyNames(envelopeType)
            .Where(name => !allowListed.Contains(name))
            .Where(name => ForbiddenNameSubstrings.Any(bad => name.Contains(bad, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        offending.Should().BeEmpty(
            "no envelope field may carry a message body, privileged content, or an action token beyond the gated Snippet/ActionHint (NFR-02/NFR-03)");
    }

    public static TheoryData<Type> EnvelopeTypes() => new()
    {
        typeof(CommunicationEnvelope),
        typeof(SuggestionEnvelope),
    };

    private static List<string> PublicInstancePropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();
}

/// <summary>
/// Test-only helpers for constructing malformed envelope JSON (unrecognized `kind`)
/// without hand-writing brittle literal JSON per test.
/// </summary>
file static class EnvelopeTestJsonExtensions
{
    public static string ToJsonWithKindOverride(this CommunicationEnvelope envelope, string kindOverride)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(envelope with { }));
        return ReplaceKind(doc, kindOverride);
    }

    public static string ToJsonWithKindOverride(this SuggestionEnvelope envelope, string kindOverride)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(envelope with { }));
        return ReplaceKind(doc, kindOverride);
    }

    private static string ReplaceKind(JsonDocument doc, string kindOverride)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.NameEquals("kind"))
                {
                    writer.WriteString("kind", kindOverride);
                }
                else
                {
                    property.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
