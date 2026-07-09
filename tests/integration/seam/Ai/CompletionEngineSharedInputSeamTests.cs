// ADR-043 (Move 1 / E-12) vertical-slice seam tests — completion-engine convergence.
//
// E-10 established the SINGLE-SOURCE `## Input` producer (PromptInputSection) and the golden
// format contract. E-12 converges the two remaining completion consumers onto it:
//
//   * AiCompletionNodeExecutor (playbook node engine) — renders a FULL multi-section JPS prompt,
//     so its convergence onto the one producer is necessarily THROUGH PromptSchemaRenderer, whose
//     Layer-2 `## Input` block delegates to PromptInputSection.Render (E-10). This test drives the
//     REAL node engine + REAL renderer + REAL producer to the LLM boundary and proves the resolved
//     inputBinding reaches the prompt as the byte-identical single-source `## Input` section, and
//     that the node's structured output is bound (the vertical slice completes).
//
//   * DailyBriefingNarrator (shipped flat-text coded workflow) — its Action is flat text so it does
//     NOT go through the renderer; before E-12 it HAND-REPLICATED the `## Input` format. This test
//     drives the REAL narrator to the LLM boundary and proves the TL;DR prompt is now composed
//     EXACTLY as `systemPrompt + "\n\n" + PromptInputSection.Render(payload)` — the replica retired
//     onto the one producer, byte-for-behavior unchanged.
//
// Category rules (tests/integration/seam/README.md): real path, only the external LLM boundary
// (IOpenAiClient) + catalog data boundary (AnalysisActionService) are doubled. A contract-shape
// test is NOT sufficient — these assert the operand actually reaches the LLM prompt through the one
// producer. If either consumer forked its own `## Input` renderer, the producer-tied assertions fail.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Narrators;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// ADR-043 / E-12 vertical-slice seam tests proving the playbook node engine and the shipped
/// DailyBriefingNarrator both render <c>## Input</c> through the SINGLE-SOURCE
/// <see cref="PromptInputSection"/> producer — parity STRUCTURAL, not conventional.
/// </summary>
public sealed class CompletionEngineSharedInputSeamTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Consumer 2 — AiCompletionNodeExecutor (node engine) through the shared producer.
    //   configJson.inputBinding (declared operand) → real executor → real PromptSchemaRenderer
    //   → PromptInputSection → LLM boundary → structured NodeOutput.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NodeEngine_RendersInputBinding_ThroughTheSingleSourceProducer_AndBindsStructuredOutput()
    {
        // Arrange — a JPS Action (so the `## Input` section renders) + a node ConfigJson whose
        // already-resolved inputBinding is the declared operand.
        const string jpsSystemPrompt =
            """{"$schema":"https://spaarke.com/schemas/prompt/v1","instruction":{"role":"You are the matter narrator.","task":"Narrate the matter from the ## Input facts."}}""";
        const string outputSchema =
            """{"type":"object","additionalProperties":false,"required":["narrative"],"properties":{"narrative":{"type":"string"}}}""";
        // Key order here is the byte-order the frozen `## Input` block must preserve.
        const string configJson =
            """{"inputBinding":{"matterName":"Acme 2026","priorityCount":3}}""";

        string? capturedPrompt = null;
        var openAi = new Mock<IOpenAiClient>(MockBehavior.Strict);
        openAi
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BinaryData, string, string?, int?, float?, CancellationToken>(
                (prompt, _, _, _, _, _, _) => capturedPrompt = prompt)
            .ReturnsAsync("""{"narrative":"Acme 2026 is active with 3 priority items."}""");

        var renderer = new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance);
        var sut = new AiCompletionNodeExecutor(openAi.Object, renderer, NullLogger<AiCompletionNodeExecutor>.Instance);
        var ctx = BuildNodeContext(jpsSystemPrompt, outputSchema, configJson);

        // Act — real engine → real renderer → real producer → (mock) LLM boundary.
        var result = await sut.ExecuteAsync(ctx, CancellationToken.None);

        // Assert — the vertical slice completed: the node bound the LLM's structured output.
        result.Success.Should().BeTrue("the node-engine slice runs end-to-end through the shared producer");
        result.StructuredData.Should().NotBeNull("the structured LLM output is bound to the node output");
        result.StructuredData!.Value.GetProperty("narrative").GetString()
            .Should().Contain("Acme 2026", "the node's structured output flows from the LLM boundary");

        // Anti-stub: the resolved inputBinding reached the LLM prompt (not stubbed away).
        capturedPrompt.Should().NotBeNull();
        capturedPrompt!.Should().Contain("Acme 2026").And.Contain("priorityCount");

        // Producer-tied guard: the exact bytes the SINGLE-SOURCE producer emits for THIS operand
        // appear in the node's rendered prompt. (The node prompt's terminal newline is trimmed by
        // PromptSchemaRenderer, so compare against the producer output minus its trailing newline.)
        var operand = JsonDocument.Parse(configJson).RootElement.GetProperty("inputBinding");
        var producerSection = PromptInputSection.Render(operand);
        capturedPrompt.Should().Contain(producerSection.TrimEnd('\n'),
            "the node engine MUST render `## Input` through PromptInputSection (no forked producer)");

        // Frozen-format double-lock (mirrors E-10's golden assertion, on the real node path).
        capturedPrompt.Should().Contain(
            "## Input\n\n{\n  \"matterName\": \"Acme 2026\",\n  \"priorityCount\": 3\n}",
            "the node path emits the frozen single-source `## Input` block (indentation, key order, header)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Consumer 3 — DailyBriefingNarrator (shipped) retired onto the shared producer.
    //   NarrateAsync (real) → TL;DR LLM boundary. The captured prompt is EXACTLY
    //   systemPrompt + "\n\n" + PromptInputSection.Render(payload) — replica retired, byte-identical.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Narrator_ComposesTldrPrompt_ThroughTheSingleSourceProducer_ByteIdentical()
    {
        // Arrange — a deterministic TL;DR facts payload (set explicitly so the narrator does not
        // recompute it) and a known TL;DR system prompt.
        const string tldrSystemPrompt = "ROLE: Compose the daily briefing TL;DR from the provided facts only.";
        var tldrFacts = new TldrFactsDto
        {
            TotalNotificationCount = 5,
            CategoryCounts = new[] { new NotificationCategoryDto { Name = "tasks", Count = 5, UnreadCount = 2 } },
            PriorityItemCount = 2,
            KeyDates = Array.Empty<TldrKeyDateDto>(),
            RecordNames = new[] { "Acme Corp" },
        };
        var req = new DailyBriefingNarrateRequest
        {
            Categories = Array.Empty<NotificationCategoryDto>(),
            PriorityItems = Array.Empty<PriorityItemDto>(),
            Channels = Array.Empty<ChannelNarrationInput>(),
            TotalNotificationCount = 5,
            TldrFacts = tldrFacts,
        };

        string? capturedPrompt = null;
        var (actions, llm, scrubber) = BuildNarratorBoundaryMocks(tldrSystemPrompt, p => capturedPrompt = p);
        var sut = new DailyBriefingNarrator(
            actions.Object, llm.Object, scrubber.Object, NullLogger<DailyBriefingNarrator>.Instance);

        // Act — real narrator → (mock) TL;DR LLM boundary.
        await sut.NarrateAsync(req, CancellationToken.None);

        // Assert — the TL;DR prompt is composed EXACTLY as the single-source producer dictates.
        // Serialize the operand with the SAME casing/null-handling the narrator uses so the expected
        // element is byte-shape-identical; the producer owns the indentation + header + trailing newline.
        var operandElement = JsonSerializer.SerializeToElement(tldrFacts, NarratorInputOptions);
        var expected = tldrSystemPrompt + "\n\n" + PromptInputSection.Render(operandElement);

        capturedPrompt.Should().NotBeNull("the TL;DR LLM call must have been made");
        capturedPrompt.Should().Be(expected,
            "the narrator's hand-replicated `## Input` block is retired onto PromptInputSection — byte-for-behavior unchanged");
    }

    // ─── Node context builder (mirrors AiCompletionNodeExecutorTests.CreateValidContext) ─────

    private static NodeExecutionContext BuildNodeContext(string systemPrompt, string outputSchemaJson, string configJson)
    {
        var actionId = Guid.NewGuid();
        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = Guid.NewGuid(),
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Seam AiCompletion Node",
                ExecutionOrder = 1,
                OutputVariable = "narrativeResult",
                ConfigJson = configJson,
                IsActive = true,
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Seam AiCompletion Action",
                SystemPrompt = systemPrompt,
                OutputSchemaJson = outputSchemaJson,
                Temperature = 0.0m,
            },
            ExecutorType = ExecutorType.AiCompletion,
            Scopes = new ResolvedScopes([], [], []),
            Document = null,
            TenantId = "seam-tenant",
            CorrelationId = "corr-" + Guid.NewGuid().ToString("N"),
        };
    }

    // ─── Narrator boundary mocks (mirrors BriefingAccuracyEvalSuiteTests.BuildBoundaryMocks) ──

    // Mirrors DailyBriefingNarrator.InputSerializerOptions: camelCase + null-omission bake the
    // operand shape into the JsonElement; the shared producer re-serializes indented.
    private static readonly JsonSerializerOptions NarratorInputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private const string TldrActionCode = "BRIEF-NARRATE-TLDR";

    private static (Mock<AnalysisActionService> actions, Mock<IOpenAiClient> llm, Mock<IEntityNameScrubber> scrubber)
        BuildNarratorBoundaryMocks(string tldrSystemPrompt, Action<string> onTldrPrompt)
    {
        var actions = new Mock<AnalysisActionService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://example.crm.dynamics.com/api/data/v9.2/") },
            BuildTestConfiguration(),
            new TestNoopTokenCredential(),
            NullLogger<AnalysisActionService>.Instance);

        actions.Setup(s => s.GetActionByCodeAsync(TldrActionCode, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AnalysisAction
               {
                   Id = Guid.NewGuid(),
                   Name = TldrActionCode,
                   SystemPrompt = tldrSystemPrompt,
                   OutputSchemaJson = """{"type":"object","properties":{"summary":{"type":"string"},"keyTakeaways":{"type":"array","items":{"type":"string"}},"topAction":{"type":"string"}},"required":["summary","keyTakeaways","topAction"],"additionalProperties":false}""",
                   ExecutorType = ExecutorType.AiAnalysis,
                   OwnerType = ScopeOwnerType.System,
                   Temperature = 0.0m,
               });

        var tldrJson = JsonSerializer.Serialize(new
        {
            summary = "You have items to review today.",
            keyTakeaways = Array.Empty<string>(),
            topAction = string.Empty,
        });

        // Strict — the TL;DR action is the ONLY LLM call the narrator may make (task 010).
        var llm = new Mock<IOpenAiClient>(MockBehavior.Strict);
        llm.Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), TldrActionCode.Replace('-', '_'),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BinaryData, string, string?, int?, float?, CancellationToken>(
                (prompt, _, _, _, _, _, _) => onTldrPrompt(prompt))
            .ReturnsAsync(tldrJson);

        var scrubber = new Mock<IEntityNameScrubber>(MockBehavior.Loose);
        scrubber.Setup(s => s.Scrub(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns(new EntityNameScrubResult { ScrubbedText = string.Empty, RemovedTerms = Array.Empty<string>() });

        return (actions, llm, scrubber);
    }

    private static IConfiguration BuildTestConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://example.crm.dynamics.com/api/data/v9.2/",
            })
            .Build();

    private sealed class TestNoopTokenCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext c, CancellationToken ct)
            => new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext c, CancellationToken ct)
            => new(new Azure.Core.AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1)));
    }
}
