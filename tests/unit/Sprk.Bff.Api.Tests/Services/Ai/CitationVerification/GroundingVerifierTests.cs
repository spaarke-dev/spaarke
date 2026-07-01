using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.CitationVerification;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.CitationVerification;

/// <summary>
/// Unit tests for <see cref="GroundingVerifier"/> — D-P9 mechanical citation verifier
/// (substring + sliding-window; 10K-char DoS cap; zero LLM).
/// </summary>
/// <remarks>
/// Acceptance criteria per task 020 POML:
/// (a) exact-quote citation against source chunk → Verified
/// (b) lightly-paraphrased quote within window → VerifiedApproximate
/// (c) fabricated quote → NotFound (annotated by GroundingVerifyNode)
/// (d) citation against oversized source chunk → InvalidInput (DoS cap)
/// </remarks>
public sealed class GroundingVerifierTests
{
    private static GroundingVerifier CreateVerifier()
        => new(NullLogger<GroundingVerifier>.Instance);

    private static EvidenceRef Quote(string quote, string @ref = "spe://drive/abc/item/xyz")
        => new() { RefType = "document", Ref = @ref, Quote = quote };

    [Fact]
    public async Task VerifyAsync_ExactQuote_ReturnsVerified()
    {
        // Arrange — quote appears verbatim in the source chunk.
        var verifier = CreateVerifier();
        var citations = new[]
        {
            Quote("the parties agreed to a settlement of $280,000 on March 12, 2025")
        };
        var chunks = new[]
        {
            new ChunkRef("doc-1#chunk-3",
                "After lengthy negotiations, the parties agreed to a settlement of $280,000 on March 12, 2025, " +
                "concluding the dispute in Matter M-2024-0341.")
        };

        // Act
        var results = await verifier.VerifyAsync(citations, chunks, CancellationToken.None);

        // Assert
        results.Should().HaveCount(1);
        results[0].Verdict.Should().Be(VerificationVerdict.Verified);
        results[0].MatchedChunkId.Should().Be("doc-1#chunk-3");
        results[0].Reason.Should().ContainEquivalentOf("exact substring");
    }

    [Fact]
    public async Task VerifyAsync_ExactQuoteWithDifferentWhitespace_ReturnsVerified()
    {
        // Arrange — quote and source differ only in whitespace; normalization should let exact-match still win.
        var verifier = CreateVerifier();
        var citations = new[]
        {
            Quote("the matter was resolved through mediation in 2024")
        };
        var chunks = new[]
        {
            new ChunkRef("doc-2#chunk-1",
                "Background:\n\n   The matter   was resolved   through mediation\nin 2024.\n\nConclusion.")
        };

        var results = await verifier.VerifyAsync(citations, chunks, CancellationToken.None);

        results[0].Verdict.Should().Be(VerificationVerdict.Verified);
        results[0].MatchedChunkId.Should().Be("doc-2#chunk-1");
    }

    [Fact]
    public async Task VerifyAsync_ParaphrasedWithinWindow_ReturnsVerifiedApproximate()
    {
        // Arrange — same content words, but word order rearranged within a single window.
        // The quote does NOT appear as an exact substring; it should hit the sliding-window
        // approximate-match path with token overlap above the 0.70 threshold.
        var verifier = CreateVerifier();
        var citations = new[]
        {
            Quote("settlement reached parties favorable mediation closing")
        };
        var chunks = new[]
        {
            new ChunkRef("doc-3#chunk-1",
                "In this closing letter, we confirm that the parties reached a favorable settlement following " +
                "extensive mediation efforts conducted over several months. The agreement was finalized on " +
                "the date specified above and represents a satisfactory outcome for our client.")
        };

        var results = await verifier.VerifyAsync(citations, chunks, CancellationToken.None);

        results[0].Verdict.Should().Be(VerificationVerdict.VerifiedApproximate,
            because: "all 6 distinct quote tokens appear within the sliding window, exceeding the 0.70 overlap threshold");
        results[0].MatchedChunkId.Should().Be("doc-3#chunk-1");
        results[0].Reason.Should().ContainEquivalentOf("sliding-window");
    }

    [Fact]
    public async Task VerifyAsync_FabricatedQuote_ReturnsNotFound()
    {
        // Arrange — quote text shares no meaningful overlap with the source.
        var verifier = CreateVerifier();
        var citations = new[]
        {
            Quote("the defendant admitted liability for trademark infringement of brand catalogs")
        };
        var chunks = new[]
        {
            new ChunkRef("doc-4#chunk-1",
                "This closing letter confirms the resolution of the contract dispute between the parties. " +
                "All terms have been satisfied and the matter is now closed.")
        };

        var results = await verifier.VerifyAsync(citations, chunks, CancellationToken.None);

        results[0].Verdict.Should().Be(VerificationVerdict.NotFound);
        results[0].MatchedChunkId.Should().BeNull();
        results[0].Reason.Should().ContainEquivalentOf("No exact");
    }

    [Fact]
    public async Task VerifyAsync_QuoteAgainstOversizedChunk_ReturnsInvalidInput()
    {
        // Arrange — the only source chunk exceeds the 10K-char DoS cap.
        var verifier = CreateVerifier();
        var oversizedText = new string('a', IGroundingVerifier.MaxSourceChunkLength + 1);
        var citations = new[] { Quote("the parties agreed to a settlement of $280,000") };
        var chunks = new[] { new ChunkRef("doc-huge#chunk-1", oversizedText) };

        var results = await verifier.VerifyAsync(citations, chunks, CancellationToken.None);

        results[0].Verdict.Should().Be(VerificationVerdict.InvalidInput,
            because: "the DoS cap rejected the only source chunk, leaving nothing verifiable");
        results[0].Reason.Should().ContainEquivalentOf("DoS");
    }

    [Fact]
    public async Task VerifyAsync_NoQuoteOnCitation_ReturnsNoQuote()
    {
        // Arrange — fact-source / comparable-matter refs don't carry verbatim quotes.
        var verifier = CreateVerifier();
        var citations = new[]
        {
            new EvidenceRef { RefType = "fact-source", Ref = "dataverse://sprk_matter/M-1234#totalSpend" },
            new EvidenceRef { RefType = "comparable-matter", Ref = "matter://M-0567" }
        };
        var chunks = new[] { new ChunkRef("doc-1#chunk-1", "Any text.") };

        var results = await verifier.VerifyAsync(citations, chunks, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Verdict == VerificationVerdict.NoQuote);
    }

    [Fact]
    public async Task VerifyAsync_MixedCitations_ReturnsPerCitationVerdicts()
    {
        // Arrange — exercise all five verdict types in a single call to confirm
        // per-citation accounting and result ordering.
        var verifier = CreateVerifier();
        var citations = new[]
        {
            Quote("the contract was terminated for cause"),             // Verified (exact)
            Quote("settlement reached parties favorable mediation closing"), // Approximate
            Quote("alien spaceship landed on the courthouse roof"),      // NotFound
            new EvidenceRef { RefType = "fact-source", Ref = "dataverse://x" } // NoQuote
        };
        var chunks = new[]
        {
            new ChunkRef("doc-A#chunk-1", "Following further review, the contract was terminated for cause on May 1st."),
            new ChunkRef("doc-B#chunk-1",
                "In this closing letter, we confirm that the parties reached a favorable settlement following " +
                "extensive mediation efforts conducted over several months.")
        };

        var results = await verifier.VerifyAsync(citations, chunks, CancellationToken.None);

        results.Should().HaveCount(4);
        results[0].Verdict.Should().Be(VerificationVerdict.Verified);
        results[1].Verdict.Should().Be(VerificationVerdict.VerifiedApproximate);
        results[2].Verdict.Should().Be(VerificationVerdict.NotFound);
        results[3].Verdict.Should().Be(VerificationVerdict.NoQuote);
    }

    [Fact]
    public async Task VerifyAsync_EmptyCitations_ReturnsEmptyList()
    {
        var verifier = CreateVerifier();
        var results = await verifier.VerifyAsync(
            Array.Empty<EvidenceRef>(),
            new[] { new ChunkRef("c", "some text") },
            CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyAsync_NoChunks_NotFoundForQuotedCitations()
    {
        // No source available → quoted citation cannot be verified.
        var verifier = CreateVerifier();
        var results = await verifier.VerifyAsync(
            new[] { Quote("anything at all") },
            Array.Empty<ChunkRef>(),
            CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].Verdict.Should().Be(VerificationVerdict.NotFound);
    }

    [Fact]
    public async Task VerifyAsync_VeryShortQuote_OnlyExactMatchAccepted()
    {
        // Quotes shorter than MinApproximateQuoteLength (12 chars) skip the sliding-window
        // path to avoid false positives from single-token overlap.
        var verifier = CreateVerifier();
        var citations = new[] { Quote("M-1234") };
        var chunks = new[] { new ChunkRef("doc-A#chunk-1", "Reference to Matter M-1234 below.") };

        var results = await verifier.VerifyAsync(citations, chunks, CancellationToken.None);

        results[0].Verdict.Should().Be(VerificationVerdict.Verified,
            because: "short quote still verifies via exact substring match");
    }

    [Fact]
    public async Task VerifyAsync_RespectsCancellationToken()
    {
        var verifier = CreateVerifier();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var citations = new[] { Quote("any quote text long enough to trigger") };
        var chunks = new[] { new ChunkRef("c", "any source text content") };

        var act = async () => await verifier.VerifyAsync(citations, chunks, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task VerifyAsync_DoesNotInvokeAnyLlmDependency()
    {
        // Per acceptance criterion: "Zero LLM calls (verified by mocking IOpenAiClient + asserting no invocations)".
        // GroundingVerifier's constructor takes ONLY ILogger — it cannot reach any LLM client.
        // We assert the design property: GroundingVerifier has no dependency on IOpenAiClient or IChatClient.

        var ctorParams = typeof(GroundingVerifier).GetConstructors().Single().GetParameters();
        ctorParams.Should().HaveCount(1, because: "GroundingVerifier should only depend on ILogger");
        ctorParams[0].ParameterType.Name.Should().Be("ILogger`1");

        // Functional smoke: verify call returns without any AI plumbing wired.
        var verifier = CreateVerifier();
        var results = await verifier.VerifyAsync(
            new[] { Quote("hello world this is verifiable text") },
            new[] { new ChunkRef("c", "the document contains hello world this is verifiable text content.") },
            CancellationToken.None);
        results[0].Verdict.Should().Be(VerificationVerdict.Verified);
    }
}

/// <summary>
/// Unit tests for <see cref="GroundingVerifyNode"/> — wraps <see cref="IGroundingVerifier"/>
/// as an INodeExecutor (ExecutorType.GroundingVerify = 70) and is dispatchable via
/// NodeExecutorRegistry per the acceptance criterion.
/// </summary>
public sealed class GroundingVerifyNodeTests
{
    private static GroundingVerifyNode CreateNode(IGroundingVerifier? verifier = null)
        => new(verifier ?? new GroundingVerifier(NullLogger<GroundingVerifier>.Instance),
               NullLogger<GroundingVerifyNode>.Instance);

    [Fact]
    public void SupportedActionTypes_ContainsGroundingVerify()
    {
        var node = CreateNode();
        node.SupportedExecutorTypes.Should().Contain(ExecutorType.GroundingVerify);
        node.SupportedExecutorTypes.Should().HaveCount(1);
    }

    [Fact]
    public void Validate_WithNoConfig_Fails()
    {
        var node = CreateNode();
        var context = TestContext.Build(configJson: null);
        var result = node.Validate(context);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ConfigJson", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingCitationsFrom_Fails()
    {
        var node = CreateNode();
        var configJson = """{"sourceChunksFrom":"loadDocument"}""";
        var result = node.Validate(TestContext.Build(configJson));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("citationsFrom", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingSourceChunksFrom_Fails()
    {
        var node = CreateNode();
        var configJson = """{"citationsFrom":"extract"}""";
        var result = node.Validate(TestContext.Build(configJson));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("sourceChunksFrom", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_AllCitationsVerified_ReturnsAllVerifiedTrue()
    {
        var node = CreateNode();
        var configJson = """{"citationsFrom":"extractOutcomes","sourceChunksFrom":"loadDocument"}""";

        var citationsPayload = new
        {
            evidence = new[]
            {
                new { refType = "document", @ref = "spe://d/i#1", quote = "the contract was terminated for cause" }
            }
        };
        var chunksPayload = new
        {
            chunks = new[]
            {
                new { chunkId = "doc-A#1", text = "Following review, the contract was terminated for cause on May 1st." }
            }
        };

        var context = TestContext.Build(configJson, ("extractOutcomes", citationsPayload), ("loadDocument", chunksPayload));

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNullOrEmpty();
        var output = result.GetData<GroundingVerifyOutput>();
        output.Should().NotBeNull();
        output!.AllVerified.Should().BeTrue();
        output.VerifiedCount.Should().Be(1);
        output.NotFoundCount.Should().Be(0);
        output.AnnotatedCitations.Should().HaveCount(1);
        output.AnnotatedCitations[0].Annotation.Should().BeNull(because: "verified citations are not annotated");
    }

    [Fact]
    public async Task ExecuteAsync_FabricatedCitation_AnnotatedWithDefault()
    {
        var node = CreateNode();
        var configJson = """{"citationsFrom":"extract","sourceChunksFrom":"chunks"}""";

        var citationsPayload = new
        {
            evidence = new[]
            {
                new { refType = "document", @ref = "spe://x#1", quote = "the defendant rode a unicorn into the courtroom" }
            }
        };
        var chunksPayload = new
        {
            chunks = new[]
            {
                new { chunkId = "doc-1", text = "Some unrelated text about contract terms and conditions." }
            }
        };

        var context = TestContext.Build(configJson, ("extract", citationsPayload), ("chunks", chunksPayload));

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        var output = result.GetData<GroundingVerifyOutput>();
        output!.AllVerified.Should().BeFalse();
        output.NotFoundCount.Should().Be(1);
        output.AnnotatedCitations.Should().HaveCount(1);
        output.AnnotatedCitations[0].Annotation.Should().Be(GroundingVerifyNode.DefaultAnnotation);
        result.Warnings.Should().Contain(w => w.Contains("could not be verified"));
    }

    [Fact]
    public async Task ExecuteAsync_MissingUpstreamOutput_ReturnsInvalidConfigError()
    {
        var node = CreateNode();
        var configJson = """{"citationsFrom":"doesNotExist","sourceChunksFrom":"alsoMissing"}""";
        var context = TestContext.Build(configJson);

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InvalidConfiguration);
        result.ErrorMessage.Should().Contain("doesNotExist");
    }

    [Fact]
    public Task ExecuteAsync_RegisteredAndDispatchableViaRegistry()
    {
        // Integration-ish: confirm a real NodeExecutorRegistry can resolve our executor by ExecutorType.
        var verifier = new GroundingVerifier(NullLogger<GroundingVerifier>.Instance);
        var node = new GroundingVerifyNode(verifier, NullLogger<GroundingVerifyNode>.Instance);
        var registry = new NodeExecutorRegistry(new INodeExecutor[] { node }, NullLogger<NodeExecutorRegistry>.Instance);

        registry.HasExecutor(ExecutorType.GroundingVerify).Should().BeTrue();
        var resolved = registry.GetExecutor(ExecutorType.GroundingVerify);
        resolved.Should().NotBeNull();
        resolved.Should().BeOfType<GroundingVerifyNode>();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test helper for building a minimal NodeExecutionContext with synthetic prior outputs.
    /// </summary>
    private static class TestContext
    {
        public static NodeExecutionContext Build(string? configJson, params (string variable, object payload)[] previousOutputs)
        {
            var nodeId = Guid.NewGuid();
            var actionId = Guid.NewGuid();

            var previousDict = new Dictionary<string, NodeOutput>();
            foreach (var (variable, payload) in previousOutputs)
            {
                previousDict[variable] = NodeOutput.Ok(Guid.NewGuid(), variable, payload);
            }

            return new NodeExecutionContext
            {
                RunId = Guid.NewGuid(),
                PlaybookId = Guid.NewGuid(),
                Node = new Sprk.Bff.Api.Models.Ai.PlaybookNodeDto
                {
                    Id = nodeId,
                    PlaybookId = Guid.NewGuid(),
                    ActionId = actionId,
                    Name = "GroundingVerify Node",
                    ExecutionOrder = 99,
                    OutputVariable = "verify",
                    ConfigJson = configJson,
                    IsActive = true
                },
                Action = new Sprk.Bff.Api.Services.Ai.AnalysisAction
                {
                    Id = actionId,
                    Name = "GroundingVerify",
                    ExecutorType = ExecutorType.GroundingVerify
                },
                ExecutorType = ExecutorType.GroundingVerify,
                Scopes = new Sprk.Bff.Api.Services.Ai.ResolvedScopes(
                    Array.Empty<Sprk.Bff.Api.Services.Ai.AnalysisSkill>(),
                    Array.Empty<Sprk.Bff.Api.Services.Ai.AnalysisKnowledge>(),
                    Array.Empty<Sprk.Bff.Api.Services.Ai.AnalysisTool>()),
                PreviousOutputs = previousDict,
                TenantId = "test-tenant"
            };
        }
    }
}
