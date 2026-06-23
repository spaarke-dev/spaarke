using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models.Ai;

/// <summary>
/// BINDING INVARIANT (chat-routing-redesign-r1 FR-14f): NodeRoutingConfig.Parse MUST
/// return { Destination = NodeDestination.Chat, WidgetType = null } when given a null,
/// empty, or whitespace-only configJson input. This guarantees backward compatibility
/// for the 6 production-bound playbooks whose sprk_configjson is null, and for any
/// future playbook authored without an explicit routing config.
/// </summary>
/// <remarks>
/// <para>
/// DO NOT REGRESS. If a future change to NodeRoutingConfig.Parse alters this default
/// behavior, the 6 production-bound playbooks will start routing to a non-Chat
/// destination at runtime. Per spec FR-14f acceptance criterion, this must never happen.
/// </para>
/// <para>
/// This class is intentionally separate from <see cref="NodeRoutingConfigTests"/> so that
/// the invariant-vs-regular-coverage distinction is visible in the file system. The
/// "BackwardCompatInvariant" suffix is the signal to future maintainers (human or AI)
/// that these tests pin behavior that the 6 production playbooks structurally depend on
/// — modifying or deleting them requires explicit spec-level justification, not a
/// drive-by refactor.
/// </para>
/// <para>
/// Coverage map:
/// <list type="bullet">
///   <item><see cref="Parse_NullInput_ReturnsChatDefault"/> — null bypass via <c>string.IsNullOrWhiteSpace</c></item>
///   <item><see cref="Parse_EmptyStringInput_ReturnsChatDefault"/> — <c>string.Empty</c> bypass</item>
///   <item><see cref="Parse_WhitespaceOnlyInput_ReturnsChatDefault"/> — spaces/tabs/newlines bypass</item>
///   <item><see cref="Parse_EmptyJsonObjectInput_ReturnsChatDefault"/> — <c>"{}"</c> deserializes but defaults all fields</item>
/// </list>
/// </para>
/// </remarks>
public sealed class NodeRoutingConfigBackwardCompatInvariantTests
{
    [Fact]
    public void Parse_NullInput_ReturnsChatDefault()
    {
        // FR-14f binding invariant: null input MUST default to Chat (do not regress).
        var config = NodeRoutingConfig.Parse(null);

        config.Should().NotBeNull();
        config.Destination.Should().Be(NodeDestination.Chat,
            "FR-14f: Parse(null) MUST return Destination = Chat for backward compatibility " +
            "with the 6 production-bound playbooks whose sprk_configjson is null");
        config.WidgetType.Should().BeNull(
            "FR-14f: Parse(null) MUST return WidgetType = null (no widget routing implied)");
    }

    [Fact]
    public void Parse_EmptyStringInput_ReturnsChatDefault()
    {
        // FR-14f binding invariant: empty input MUST default to Chat (do not regress).
        var config = NodeRoutingConfig.Parse(string.Empty);

        config.Should().NotBeNull();
        config.Destination.Should().Be(NodeDestination.Chat,
            "FR-14f: Parse(\"\") MUST return Destination = Chat");
        config.WidgetType.Should().BeNull();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData(" \r\n\t ")]
    public void Parse_WhitespaceOnlyInput_ReturnsChatDefault(string whitespaceInput)
    {
        // FR-14f binding invariant: whitespace-only input MUST default to Chat (do not regress).
        // Parse uses string.IsNullOrWhiteSpace as the guard — this test pins that contract
        // across all common whitespace characters (space, tab, newline, mixed).
        var config = NodeRoutingConfig.Parse(whitespaceInput);

        config.Should().NotBeNull();
        config.Destination.Should().Be(NodeDestination.Chat,
            "FR-14f: Parse(whitespace) MUST default to Chat — string.IsNullOrWhiteSpace " +
            "guard in NodeRoutingConfig.Parse pins this contract");
        config.WidgetType.Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyJsonObjectInput_ReturnsChatDefault()
    {
        // FR-14f binding invariant: empty JSON object "{}" MUST default to Chat (do not regress).
        //
        // Behavior note: unlike null/empty/whitespace (which short-circuit via
        // string.IsNullOrWhiteSpace), "{}" actually deserializes — System.Text.Json constructs
        // a NodeRoutingConfig with all property defaults. Per the property declarations in
        // NodeRoutingConfig.cs:
        //   - Destination = NodeDestination.Chat  (init-only default)
        //   - WidgetType  = null                  (nullable string default)
        // So the post-deserialization config matches the null/empty/whitespace short-circuit
        // result bit-for-bit. This is a CO-DEPENDENCY between the property defaults and the
        // Parse contract; if either changes, this test catches the drift.
        var config = NodeRoutingConfig.Parse("{}");

        config.Should().NotBeNull();
        config.Destination.Should().Be(NodeDestination.Chat,
            "FR-14f: Parse(\"{}\") MUST default to Chat — this is the structural compat " +
            "guarantee. \"{}\" deserializes successfully but all properties use their " +
            "init-only defaults (Destination = Chat, WidgetType = null), so the behavior " +
            "matches the short-circuit path. Co-dependency: if NodeRoutingConfig property " +
            "defaults change, this test must be re-evaluated against FR-14f.");
        config.WidgetType.Should().BeNull(
            "FR-14f: Parse(\"{}\") MUST return WidgetType = null (no widget routing implied)");
    }
}
