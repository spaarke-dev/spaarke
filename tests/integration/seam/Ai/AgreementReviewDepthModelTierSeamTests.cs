// ai-advanced-capabilities-agreements-r1 task 070 (UAT2 review-depth selector) — vertical-slice seam
// test (KEEP path: tests/integration/seam/** — end-to-end across the dispatch -> tier-resolution seam
// with production types; ADR-038 §E-40 / tests/CLAUDE.md).
//
// THE GAP THIS CLOSES: the review kick-off UI now lets the user pick "Quick" (~20s, fast model scan)
// or "Thorough" (~2-3min, gpt-5 reasoning; default) before a review runs. The choice is carried as a
// CLOSED client intent (`slots.reviewDepth: 'quick'|'thorough'` — never a model/deployment name, per
// ADR-039) in the SAME dispatch args every Click-path binding already carries. This test proves the
// full wire-to-effective-tier threading over the REAL app (WebApplicationFactory<Program>, REAL
// SessionDispatchOrchestrator), reusing AgreementReviewKnowledgeScopeSeamFixture (task 021, §11
// reuse-first — no second WebApplicationFactory boilerplate):
//   (a) `reviewDepth:"quick"` overrides the effective AiModelTier to Standard, regardless of the
//       Action's own catalog default;
//   (b) `reviewDepth:"thorough"` resolves EXPLICITLY to Reasoning;
//   (c) no `reviewDepth` arg (every pre-070 caller, and every non-review Binding) leaves the Action's
//       own catalog tier untouched — byte-identical to pre-070 behavior;
//   (d) an invalid/unrecognized `reviewDepth` value degrades to no override — never rejects the
//       dispatch, never throws — falling through to the existing Binding/Action tier (the safe
//       "reject/default" contract, ADR-039: server-side validation, closed set).

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

public sealed class AgreementReviewDepthModelTierSeamTests : IClassFixture<AgreementReviewKnowledgeScopeSeamFixture>
{
    private readonly AgreementReviewKnowledgeScopeSeamFixture _fx;

    public AgreementReviewDepthModelTierSeamTests(AgreementReviewKnowledgeScopeSeamFixture fx) => _fx = fx;

    private async Task<string> SeedSessionAsync(string fileId = "depth-review-file-1")
    {
        var sessionId = Guid.NewGuid().ToString("D");
        await _fx.Sessions.UpdateSessionCacheAsync(new ChatSession(
            SessionId: sessionId,
            TenantId: AgreementReviewKnowledgeScopeSeamFixture.TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: new[]
            {
                new ChatSessionFile(
                    FileId: fileId, FileName: "acme-nda.pdf", ContentType: "application/pdf",
                    SizeBytes: 64, SearchDocumentIdsCsv: $"{fileId}_s_0",
                    UploadedAt: DateTimeOffset.UtcNow)
                {
                    ExtractedText = "This Mutual NDA governs...",
                },
            }));
        return sessionId;
    }

    /// <summary>Seeds the Binding + an Action whose catalog default tier is Reasoning — mirrors the
    /// REAL agreement-review Action's declared `modelTier: "Reasoning"` (infra/dataverse/actions/
    /// agreement-review.action.json), so these tests prove the override behaves correctly against a
    /// realistic default, not an unset one.</summary>
    private void SeedReasoningDefaultBinding()
    {
        _fx.SeedAgreementReviewBinding();
        _fx.ScopeResolverMock
            .Setup(s => s.GetActionAsync(AgreementReviewKnowledgeScopeSeamFixture.ReviewActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction
            {
                Id = AgreementReviewKnowledgeScopeSeamFixture.ReviewActionId,
                Name = "Agreement Review",
                SystemPrompt = "Review the document against the retrieved standard.",
                OutputSchemaJson = """{"type":"object","additionalProperties":false,"required":["overallRisk","flaggedSections"],"properties":{"overallRisk":{"type":"string"},"flaggedSections":{"type":"array"}}}""",
                AllowsKnowledge = true,
                ModelTier = AiModelTier.Reasoning,
            });
    }

    [Fact]
    public async Task Dispatch_WithReviewDepthQuick_OverridesEffectiveTierToStandard()
    {
        SeedReasoningDefaultBinding();
        var sessionId = await SeedSessionAsync("depth-quick");
        var client = _fx.CreateAuthenticatedClient();

        var dispatch = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/dispatch",
            new { bindingId = AgreementReviewKnowledgeScopeSeamFixture.ReviewBindingId.ToString(), args = new { fileIds = new[] { "depth-quick" }, reviewDepth = "quick" } });

        dispatch.StatusCode.Should().Be(HttpStatusCode.OK);
        _fx.CapturedAction.Should().NotBeNull("the REAL SessionDispatchOrchestrator must have invoked the mocked IActionRunner");
        _fx.CapturedAction!.ModelTier.Should().Be(AiModelTier.Standard,
            "reviewDepth:'quick' overrides the Action's own Reasoning catalog default to the fast/cheap Standard tier for THIS run");
    }

    [Fact]
    public async Task Dispatch_WithReviewDepthThorough_ResolvesEffectiveTierToReasoning()
    {
        SeedReasoningDefaultBinding();
        var sessionId = await SeedSessionAsync("depth-thorough");
        var client = _fx.CreateAuthenticatedClient();

        var dispatch = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/dispatch",
            new { bindingId = AgreementReviewKnowledgeScopeSeamFixture.ReviewBindingId.ToString(), args = new { fileIds = new[] { "depth-thorough" }, reviewDepth = "thorough" } });

        dispatch.StatusCode.Should().Be(HttpStatusCode.OK);
        _fx.CapturedAction.Should().NotBeNull();
        _fx.CapturedAction!.ModelTier.Should().Be(AiModelTier.Reasoning,
            "reviewDepth:'thorough' resolves EXPLICITLY to Reasoning (self-contained mapping, not merely a catalog-default pass-through)");
    }

    [Fact]
    public async Task Dispatch_WithNoReviewDepthArg_LeavesCatalogTierUntouched_AdditiveSafetyPin()
    {
        SeedReasoningDefaultBinding();
        var sessionId = await SeedSessionAsync("depth-absent");
        var client = _fx.CreateAuthenticatedClient();

        var dispatch = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/dispatch",
            new { bindingId = AgreementReviewKnowledgeScopeSeamFixture.ReviewBindingId.ToString(), args = new { fileIds = new[] { "depth-absent" } } });

        dispatch.StatusCode.Should().Be(HttpStatusCode.OK);
        _fx.CapturedAction.Should().NotBeNull();
        _fx.CapturedAction!.ModelTier.Should().Be(AiModelTier.Reasoning,
            "every pre-070 dispatch (no reviewDepth arg) must fall through to the Action's own catalog tier — byte-identical to prior behavior");
    }

    [Fact]
    public async Task Dispatch_WithInvalidReviewDepthValue_DegradesToNoOverride_NeverRejectsDispatch()
    {
        SeedReasoningDefaultBinding();
        var sessionId = await SeedSessionAsync("depth-invalid");
        var client = _fx.CreateAuthenticatedClient();

        var dispatch = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/dispatch",
            new { bindingId = AgreementReviewKnowledgeScopeSeamFixture.ReviewBindingId.ToString(), args = new { fileIds = new[] { "depth-invalid" }, reviewDepth = "blazing-fast" } });

        dispatch.StatusCode.Should().Be(HttpStatusCode.OK,
            "an unrecognized reviewDepth value must never fail the dispatch — server-side reject/default (ADR-039)");
        _fx.CapturedAction.Should().NotBeNull();
        _fx.CapturedAction!.ModelTier.Should().Be(AiModelTier.Reasoning,
            "an invalid value degrades to no override, falling through to the Action's own catalog tier — the safe default");
    }
}
