using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models.Ai.Chat;

/// <summary>
/// Unit tests for the EntityType boundary normalization introduced by R7 Wave 12 task 150
/// (audit 120 Gap A). Covers:
///   • The static <see cref="EntityTypeNormalizer"/> helper (raw → canonical mapping,
///     pass-through for unknown types, null/whitespace handling, case insensitivity)
///   • The integration with <see cref="ChatHostContext"/>: every construction path
///     (primary constructor, <c>with</c> expression, System.Text.Json deserialization)
///     funnels through the normalizer so downstream consumers (PlaybookChatContextProvider
///     entity-enrichment + matter-memory, RagService parent-entity filter, etc.) always
///     read canonical form.
///
/// Behavioral protection (per tests/CLAUDE.md "Expect to Defend"):
///   These tests defend the binding-shape contract of ChatHostContext — that EntityType
///   is canonical at every read site. Deleting them would allow regressions where a new
///   `with` expression or DTO deserialization path silently re-introduces the
///   raw-vs-normalized split that audit 120 identified as the root cause of broken
///   matter-scoped chat UAT scenarios.
/// </summary>
public class EntityTypeNormalizerTests
{
    // =========================================================================
    // EntityTypeNormalizer.Normalize — the helper itself
    // =========================================================================

    [Theory]
    [InlineData("sprk_matter", "matter")]
    [InlineData("sprk_project", "project")]
    [InlineData("sprk_invoice", "invoice")]
    public void Normalize_GivenRawDataverseLogicalName_ReturnsCanonicalShortForm(string input, string expected)
    {
        // This is the load-bearing scenario from audit 120 Gap A: SpaarkeAi clients
        // pass `sprk_matter` via URL params; BFF enrichment + RAG filter expect `matter`.
        EntityTypeNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("matter")]
    [InlineData("project")]
    [InlineData("invoice")]
    [InlineData("account")]
    [InlineData("contact")]
    public void Normalize_GivenAlreadyCanonicalName_ReturnsSameValue(string input)
    {
        // Idempotence: callers that have already normalized (or use canonical from
        // useEntityResolver) get a stable identity result.
        EntityTypeNormalizer.Normalize(input).Should().Be(input);
    }

    [Theory]
    [InlineData("SPRK_MATTER", "matter")]
    [InlineData("Sprk_Matter", "matter")]
    [InlineData("  sprk_matter  ", "matter")]
    [InlineData("MATTER", "matter")]
    public void Normalize_IsCaseInsensitiveAndTrimsWhitespace(string input, string expected)
    {
        // Robustness against client-side casing/whitespace variation; the boundary
        // must be tolerant so we don't reintroduce the convention split via mismatch.
        EntityTypeNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("sprk_analysisoutput")]
    [InlineData("sprk_document")]
    [InlineData("opportunity")]
    [InlineData("incident")]
    [InlineData("unknown_type")]
    public void Normalize_GivenUnknownEntityType_ReturnsInputUnchanged(string input)
    {
        // CRITICAL: non-parent-business entity types (analysisoutput is used by the
        // analysis-session HostContext slot per ChatEndpoints.cs SendMessageAsync;
        // StandaloneChatContextProvider intentionally operates on raw `sprk_matter`)
        // MUST pass through unchanged so we do NOT break those bounded contexts.
        EntityTypeNormalizer.Normalize(input).Should().Be(input);
    }

    [Fact]
    public void Normalize_GivenNull_ReturnsNull()
    {
        EntityTypeNormalizer.Normalize(null).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Normalize_GivenEmptyOrWhitespace_ReturnsInputUnchanged(string input)
    {
        // Whitespace handling matches the rest of the chat-session DTOs:
        // empty/whitespace is not transformed; downstream validators (ChatHostContext.IsValid,
        // PlaybookChatContextProvider guards) handle absence on their own.
        EntityTypeNormalizer.Normalize(input).Should().Be(input);
    }

    // =========================================================================
    // ChatHostContext — primary constructor path
    // =========================================================================

    [Fact]
    public void ChatHostContext_PrimaryConstructor_NormalizesRawSprkMatterToCanonicalMatter()
    {
        // This is the END-TO-END behavior audit 120 Gap A demands: SpaarkeAi sends
        // `sprk_matter`; every downstream BFF consumer reads `matter` without any
        // additional translation work.
        var ctx = new ChatHostContext(EntityType: "sprk_matter", EntityId: "guid-1");

        ctx.EntityType.Should().Be("matter");
        ctx.EntityId.Should().Be("guid-1");
    }

    [Fact]
    public void ChatHostContext_PrimaryConstructor_PreservesAlreadyCanonicalForm()
    {
        var ctx = new ChatHostContext(EntityType: "matter", EntityId: "guid-1");

        ctx.EntityType.Should().Be("matter");
    }

    [Fact]
    public void ChatHostContext_PrimaryConstructor_PassesThroughUnknownEntityType()
    {
        // The analysis-session HostContext slot pattern (ChatEndpoints.cs:911) reads
        // `sprk_analysisoutput`. The normalizer MUST NOT mangle this — it is a
        // different bounded context per audit 120 disposition §A.
        var ctx = new ChatHostContext(EntityType: "sprk_analysisoutput", EntityId: "guid-1");

        ctx.EntityType.Should().Be("sprk_analysisoutput");
    }

    // =========================================================================
    // ChatHostContext — `with` expression (load-bearing for SwitchContext)
    // =========================================================================

    [Fact]
    public void ChatHostContext_WithExpression_NormalizesUpdatedEntityType()
    {
        // ChatEndpoints.SwitchContextAsync uses `session with { HostContext = ... }`.
        // The boundary contract MUST hold across `with` too — otherwise a switch from
        // canonical → raw would silently re-introduce the convention split.
        var original = new ChatHostContext(EntityType: "matter", EntityId: "guid-1");

        var updated = original with { EntityType = "sprk_project" };

        updated.EntityType.Should().Be("project");
        updated.EntityId.Should().Be("guid-1");
    }

    [Fact]
    public void ChatHostContext_WithExpression_LeavesOtherFieldsUntouched()
    {
        var original = new ChatHostContext(
            EntityType: "sprk_matter",
            EntityId: "guid-1",
            EntityName: "Smith v. Jones",
            WorkspaceType: "spaarke-ai",
            PageType: "AssistantPane");

        var updated = original with { EntityName = "Smith v. Jones (Active)" };

        updated.EntityType.Should().Be("matter"); // still normalized
        updated.EntityId.Should().Be("guid-1");
        updated.EntityName.Should().Be("Smith v. Jones (Active)");
        updated.WorkspaceType.Should().Be("spaarke-ai");
        updated.PageType.Should().Be("AssistantPane");
    }

    // =========================================================================
    // ChatHostContext — System.Text.Json round-trip (Redis hot-tier persistence)
    // =========================================================================

    [Fact]
    public void ChatHostContext_JsonRoundTrip_NormalizesRawInputFromDeserialization()
    {
        // The Redis hot tier persists session.HostContext as JSON. If a session
        // was created BEFORE this fix (raw `sprk_matter` persisted), every read
        // post-fix MUST yield the canonical form so PlaybookChatContextProvider's
        // matter-memory + enrichment fire correctly. Tests the JSON-deserialization
        // path of the normalization contract.
        var json = """
        {
          "EntityType": "sprk_matter",
          "EntityId": "guid-1",
          "EntityName": "Smith v. Jones",
          "WorkspaceType": "spaarke-ai",
          "PageType": "AssistantPane"
        }
        """;

        var deserialized = JsonSerializer.Deserialize<ChatHostContext>(json);

        deserialized.Should().NotBeNull();
        deserialized!.EntityType.Should().Be("matter");
        deserialized.EntityId.Should().Be("guid-1");
        deserialized.EntityName.Should().Be("Smith v. Jones");
    }

    [Fact]
    public void ChatHostContext_JsonRoundTrip_RawCanonicalRawCanonical_StableViaIdempotence()
    {
        // Serialize → deserialize → serialize → deserialize should converge:
        // - First deserialize from raw → EntityType becomes "matter"
        // - Re-serialize → JSON has "matter"
        // - Re-deserialize from canonical → still "matter" (idempotence)
        var firstDeserialized = JsonSerializer.Deserialize<ChatHostContext>(
            """{"EntityType":"sprk_matter","EntityId":"guid-1"}""");

        var reserializedJson = JsonSerializer.Serialize(firstDeserialized);
        var secondDeserialized = JsonSerializer.Deserialize<ChatHostContext>(reserializedJson);

        secondDeserialized!.EntityType.Should().Be("matter");
        // Spot-check: re-serialized JSON should contain canonical form
        reserializedJson.Should().Contain("\"EntityType\":\"matter\"");
    }

    // =========================================================================
    // ChatHostContext.IsValid — the existing dead-code validator now meaningfully tested
    // =========================================================================

    [Fact]
    public void IsValid_GivenRawSprkMatterThatNormalizesToValidType_ReturnsTrue()
    {
        // Before this fix: ChatHostContext.IsValid would return FALSE for `sprk_matter`
        // because ParentEntityContext.EntityTypes.IsValid checks against the canonical
        // allow-list. With boundary normalization, IsValid now matches operator intent.
        var ctx = new ChatHostContext(EntityType: "sprk_matter", EntityId: "guid-1");

        ctx.IsValid().Should().BeTrue();
    }

    [Fact]
    public void IsValid_GivenUnknownEntityType_ReturnsFalse()
    {
        // sprk_analysisoutput is NOT a parent business entity; IsValid remains false
        // (and continues to be — analysis-session paths don't call IsValid anyway).
        var ctx = new ChatHostContext(EntityType: "sprk_analysisoutput", EntityId: "guid-1");

        ctx.IsValid().Should().BeFalse();
    }
}
