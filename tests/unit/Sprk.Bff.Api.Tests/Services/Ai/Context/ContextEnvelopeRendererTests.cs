using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Context;

/// <summary>
/// spaarke-ai-architecture-redesign-r2 task AIR2-053 (FR-B-04): the deterministic
/// <see cref="ContextEnvelopeRenderer"/>. Proves the stable-prefix additions are byte-stable, the
/// environment suffix is the Workspace fragment, and the conversation system message is at parity with
/// the <see cref="ConversationContextProducer"/> (with the envelope's ledger references pinning the same
/// window). NO cache machinery — these are cheap byte-equality/determinism assertions (operator ruling).
/// </summary>
public sealed class ContextEnvelopeRendererTests
{
    [Fact]
    public void RenderStablePrefixAdditions_UserBeforeBusiness_JoinedWithBlankLine()
    {
        var envelope = ContextEnvelopeReferenceProducer.Assemble(
            userFragment: "USER-FRAGMENT", businessFragment: "BUSINESS-FRAGMENT");

        ContextEnvelopeRenderer.RenderStablePrefixAdditions(envelope)
            .Should().Be("USER-FRAGMENT\n\nBUSINESS-FRAGMENT",
                "the canonical relative order is User before Business, joined by a blank line");
    }

    [Fact]
    public void RenderStablePrefixAdditions_RenderedTwiceForSameEnvelope_IsByteIdentical()
    {
        var a = ContextEnvelopeReferenceProducer.Assemble(
            userFragment: "u", businessFragment: "Context: This chat is hosted on the matter record with id x.");
        var b = ContextEnvelopeReferenceProducer.Assemble(
            userFragment: "u", businessFragment: "Context: This chat is hosted on the matter record with id x.");

        ContextEnvelopeRenderer.RenderStablePrefixAdditions(a)
            .Should().Be(ContextEnvelopeRenderer.RenderStablePrefixAdditions(b),
                "the stable-prefix additions carry no timestamp/GUID beyond the caller-supplied ids (NFR-04)");
    }

    [Fact]
    public void RenderStablePrefixAdditions_StatedProfileUserFragment_IsByteStableAndFirst()
    {
        // task 032 (NFR-02/04): the FR-E2 stated-profile block travels as the User fragment through the
        // stable-prefix renderer. Pin that it renders byte-identically twice AND stays ahead of Business —
        // the composed User slice is a stable-prefix slice, so a drift here moves the prompt-cache prefix.
        var statedProfile = StatedProfileRenderer.Render(new StatedProfile
        {
            RoleLabel = "Partner",
            PracticeAreaNames = new[] { "Corporate", "Litigation" },
            AssistantPreferences = "Concise, cite sources",
        });
        var a = ContextEnvelopeReferenceProducer.Assemble(
            userFragment: statedProfile, businessFragment: "BIZ");
        var b = ContextEnvelopeReferenceProducer.Assemble(
            userFragment: statedProfile, businessFragment: "BIZ");

        var rendered = ContextEnvelopeRenderer.RenderStablePrefixAdditions(a);

        rendered.Should().Be(ContextEnvelopeRenderer.RenderStablePrefixAdditions(b),
            "the stated-profile User fragment carries no timestamp/GUID — byte-stable across turns (NFR-04)");
        rendered.Should().StartWith("### Your Profile (stated)",
            "the User (stated-profile) fragment is emitted before Business in the stable prefix");
    }

    [Fact]
    public void RenderStablePrefixAdditions_NoUserOrBusinessFragment_RendersEmpty()
    {
        var envelope = ContextEnvelopeReferenceProducer.Assemble(workspaceFragment: "env-only");

        ContextEnvelopeRenderer.RenderStablePrefixAdditions(envelope)
            .Should().BeEmpty("absent User/Business fragments contribute nothing to the prefix additions");
    }

    [Fact]
    public void RenderEnvironmentSuffix_IsTheWorkspaceFragment()
    {
        var envelope = ContextEnvelopeReferenceProducer.Assemble(workspaceFragment: "\n\n## Current Date\n…");

        ContextEnvelopeRenderer.RenderEnvironmentSuffix(envelope)
            .Should().Be("\n\n## Current Date\n…");
    }

    [Fact]
    public void RenderEnvironmentSuffix_NoWorkspaceFragment_RendersEmpty()
    {
        var envelope = ContextEnvelopeReferenceProducer.Assemble(userFragment: "u");

        ContextEnvelopeRenderer.RenderEnvironmentSuffix(envelope).Should().BeEmpty();
    }

    [Fact]
    public void RenderConversationSystemMessage_IsAtParityWithProducer_AndPinsTheSameReferenceCount()
    {
        var outputs = new[]
        {
            CreateTestOutput("b", 1, "informational", "\"first\""),
            CreateTestOutput("b", 2, "informational", "\"second\""),
            CreateTestOutput("b", 3, "informational", "\"third\""),
        };
        var references = outputs
            .Select(o => new LedgerEntryReference { Key = o.Key, UcId = o.UcId, Disposition = o.Disposition })
            .ToList();
        var envelope = ContextEnvelopeReferenceProducer.Assemble(conversation: references);

        var rendered = ContextEnvelopeRenderer.RenderConversationSystemMessage(envelope, outputs);

        rendered.Should().Be(ConversationContextProducer.BuildLedgerOutputsContext(outputs),
            "the renderer delegates to the ONE conversation producer — no second render path");
        envelope.Memory!.Conversation.Should().HaveCount(3,
            "the envelope's ledger references pin the SAME window the message renders (≤ MaxContextOutputs)");
    }

    [Fact]
    public void RenderConversationSystemMessage_NoOutputs_ReturnsNull()
    {
        var envelope = ContextEnvelopeReferenceProducer.Assemble();

        ContextEnvelopeRenderer.RenderConversationSystemMessage(envelope, Array.Empty<SessionOutput>())
            .Should().BeNull();
    }

    private static SessionOutput CreateTestOutput(
        string bindingId, int turn, string disposition, string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        return new SessionOutput
        {
            Key = SessionLedger.BuildOutputKey(bindingId, turn),
            BindingId = bindingId,
            UcId = "uc.test.capability",
            Turn = turn,
            Disposition = disposition,
            Payload = doc.RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
