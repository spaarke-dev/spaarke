using System.Collections.Generic;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models.Ai;

/// <summary>
/// Unit tests for <see cref="DispatchResult"/> — verifies the spec FR-14b additive
/// extension of the dispatch contract with <see cref="NodeDestination"/> +
/// <see cref="DispatchResult.WidgetType"/> properties.
///
/// <para>
/// The extension is binary-compat: existing call sites that construct
/// <see cref="DispatchResult"/> with the original 8-parameter shape MUST continue to
/// compile and behave identically. Task 047 populates the new properties from
/// <see cref="NodeRoutingConfig"/>; until then the defaults
/// (<see cref="NodeDestination.Chat"/> + null widget) preserve the implicit pre-R6
/// chat-destination behavior.
/// </para>
/// </summary>
public class DispatchResultTests
{
    // -------------------------------------------------------------------------
    // Default construction — defaults preserve pre-R6 implicit chat destination
    // -------------------------------------------------------------------------

    [Fact]
    public void DispatchResult_DefaultConstruction_HasChatDestinationAndNullWidgetType()
    {
        // Arrange + Act: construct using ONLY the original 8 parameters (pre-R6 shape).
        var result = new DispatchResult(
            Matched: true,
            PlaybookId: "00000000-0000-0000-0000-000000000001",
            PlaybookName: "Summarize Document",
            Confidence: 0.92,
            OutputType: OutputType.Text,
            RequiresConfirmation: false,
            ExtractedParameters: new Dictionary<string, string>(),
            TargetPage: null);

        // Assert: defaults populate NodeDestination = Chat, WidgetType = null.
        result.NodeDestination.Should().Be(NodeDestination.Chat,
            "FR-14b: default NodeDestination MUST be Chat to preserve implicit pre-R6 behavior");
        result.WidgetType.Should().BeNull(
            "FR-14b: default WidgetType MUST be null — only set when destination = Workspace");

        // Sanity: original fields unaffected.
        result.Matched.Should().BeTrue();
        result.PlaybookId.Should().Be("00000000-0000-0000-0000-000000000001");
        result.Confidence.Should().Be(0.92);
    }

    [Fact]
    public void DispatchResult_NoMatchStatic_HasChatDestinationAndNullWidgetType()
    {
        // Arrange + Act: the canonical NoMatch sentinel uses the original 8-param shape.
        var result = DispatchResult.NoMatch;

        // Assert: defaults flow through the static initializer too.
        result.Matched.Should().BeFalse();
        result.NodeDestination.Should().Be(NodeDestination.Chat);
        result.WidgetType.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Explicit construction — Workspace destination + structured stream widget
    // -------------------------------------------------------------------------

    [Fact]
    public void DispatchResult_ExplicitConstruction_PreservesNodeDestinationAndWidgetType()
    {
        // Arrange + Act: construct with the new 10-param shape, routing to Workspace
        // with a structured-output-stream widget (design.md WP3 step 2).
        var result = new DispatchResult(
            Matched: true,
            PlaybookId: "00000000-0000-0000-0000-000000000002",
            PlaybookName: "Summarize Document for Workspace",
            Confidence: 0.87,
            OutputType: OutputType.Text,
            RequiresConfirmation: false,
            ExtractedParameters: new Dictionary<string, string>(),
            TargetPage: null,
            NodeDestination: NodeDestination.Workspace,
            WidgetType: "structured-output-stream");

        // Assert: both new properties are carried verbatim.
        result.NodeDestination.Should().Be(NodeDestination.Workspace,
            "explicit Workspace destination must round-trip into the record");
        result.WidgetType.Should().Be("structured-output-stream",
            "explicit widget type must round-trip into the record");

        // Sanity: original fields unaffected.
        result.Matched.Should().BeTrue();
        result.PlaybookName.Should().Be("Summarize Document for Workspace");
    }

    // -------------------------------------------------------------------------
    // Regression: real existing-style caller (mimics PlaybookDispatcher.cs:397)
    // must still compile + behave identically.
    // -------------------------------------------------------------------------

    [Fact]
    public void DispatchResult_RegressionExistingDispatcherStyleConstruction_CompilesAndDefaultsToChat()
    {
        // Arrange + Act: this is byte-for-byte the call pattern at
        // src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs:397
        // (BuildResultFromCandidate). It MUST keep compiling unchanged per spec FR-14b.
        var extracted = new Dictionary<string, string>
        {
            { "recipient", "alice@example.com" },
            { "date", "2026-06-22" }
        };

        var result = new DispatchResult(
            Matched: true,
            PlaybookId: "candidate-1",
            PlaybookName: "Email Composer",
            Confidence: 0.75,
            OutputType: OutputType.Navigation,
            RequiresConfirmation: true,
            ExtractedParameters: extracted,
            TargetPage: "sprk_emailcomposer");

        // Assert: original fields populated exactly as the pre-extension test would expect.
        result.Matched.Should().BeTrue();
        result.PlaybookId.Should().Be("candidate-1");
        result.PlaybookName.Should().Be("Email Composer");
        result.Confidence.Should().Be(0.75);
        result.OutputType.Should().Be(OutputType.Navigation);
        result.RequiresConfirmation.Should().BeTrue();
        result.ExtractedParameters.Should().BeEquivalentTo(extracted);
        result.TargetPage.Should().Be("sprk_emailcomposer");

        // Assert: defaults fill in for the new properties — chat destination, no widget.
        // This is the FR-14b binary-compat guarantee.
        result.NodeDestination.Should().Be(NodeDestination.Chat,
            "existing 8-param call sites MUST default to Chat destination (FR-14b)");
        result.WidgetType.Should().BeNull(
            "existing 8-param call sites MUST default to null widget (FR-14b)");
    }

    // -------------------------------------------------------------------------
    // `with`-expression compatibility — record semantics preserved for callers
    // that mutate via non-destructive copy.
    // -------------------------------------------------------------------------

    [Fact]
    public void DispatchResult_WithExpression_CanOverrideOnlyNewProperties()
    {
        // Arrange: start from a Chat-default result, mutate ONLY the new properties.
        var original = new DispatchResult(
            Matched: true,
            PlaybookId: "pid",
            PlaybookName: "name",
            Confidence: 0.5,
            OutputType: OutputType.Text,
            RequiresConfirmation: false,
            ExtractedParameters: new Dictionary<string, string>(),
            TargetPage: null);

        // Act: `with` expression overrides ONLY NodeDestination + WidgetType.
        var routed = original with
        {
            NodeDestination = NodeDestination.Workspace,
            WidgetType = "Summary"
        };

        // Assert: original is unmodified (record value semantics).
        original.NodeDestination.Should().Be(NodeDestination.Chat);
        original.WidgetType.Should().BeNull();

        // Assert: routed copy carries new values + preserves all original fields.
        routed.NodeDestination.Should().Be(NodeDestination.Workspace);
        routed.WidgetType.Should().Be("Summary");
        routed.PlaybookId.Should().Be("pid");
        routed.Confidence.Should().Be(0.5);
    }
}
