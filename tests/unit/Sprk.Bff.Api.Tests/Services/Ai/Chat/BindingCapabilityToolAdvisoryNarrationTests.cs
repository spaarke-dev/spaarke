using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// R4 UAT 2026-08-18 (Bug B) guard. On the TEXT/agent path a <c>surface_launch</c> capability used to
/// return a generic "a surface is now opening… draft only" string and DISCARD the grounded advisory
/// narration — so an advisory READ capability (list-tasks / FR-01) relayed "I opened your task list"
/// instead of its cited summary + recommendation (the P1 defect). <see cref="BindingCapabilityTool.TryExtractAdvisoryNarration"/>
/// is the structural discriminator that fixes it: an advisory payload
/// (<c>{ "acknowledgement": &lt;narration&gt; }</c>) yields the narration to relay; a CREATE-capability
/// draft payload has no acknowledgement field and yields null (keeps the generic message — a create
/// draft must NOT be read aloud in chat). No consumerType hardcode (ADR-039).
/// </summary>
public sealed class BindingCapabilityToolAdvisoryNarrationTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void AdvisoryPayload_WithAcknowledgement_ReturnsTheNarrationToRelay()
    {
        var payload = Parse(
            """{ "acknowledgement": "You have 11 overdue tasks. I'd start with the 2 due today." }""");

        var narration = BindingCapabilityTool.TryExtractAdvisoryNarration(payload);

        narration.Should().Be("You have 11 overdue tasks. I'd start with the 2 due today.");
    }

    [Fact]
    public void CreateCapabilityDraftPayload_HasNoAcknowledgement_ReturnsNull()
    {
        // A create-matter/-task draft shape — the generic surface-launch message must be kept
        // (the wizard owns the create; the draft is NOT read aloud in chat).
        var payload = Parse(
            """{ "draftValues": { "sprk_name": "Acme NDA" }, "resolvedLookups": {} }""");

        BindingCapabilityTool.TryExtractAdvisoryNarration(payload).Should().BeNull();
    }

    [Fact]
    public void EmptyOrWhitespaceAcknowledgement_ReturnsNull()
    {
        BindingCapabilityTool.TryExtractAdvisoryNarration(Parse("""{ "acknowledgement": "   " }""")).Should().BeNull();
    }

    [Fact]
    public void NullOrNonObjectPayload_ReturnsNull()
    {
        BindingCapabilityTool.TryExtractAdvisoryNarration(null).Should().BeNull();
        BindingCapabilityTool.TryExtractAdvisoryNarration(Parse("\"just a string\"")).Should().BeNull();
    }

    [Fact]
    public void NonStringAcknowledgement_ReturnsNull()
    {
        // Defensive: the field exists but isn't a string — do not relay a non-narration value.
        BindingCapabilityTool.TryExtractAdvisoryNarration(Parse("""{ "acknowledgement": 42 }""")).Should().BeNull();
    }
}
