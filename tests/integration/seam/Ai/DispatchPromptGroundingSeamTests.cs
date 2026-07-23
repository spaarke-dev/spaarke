using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Seam.Ai;

/// <summary>
/// F-1/F-2/F-7 envelope-convergence — D6 / PE-D8(b) dispatch-side seam pins. Drives the REAL
/// <see cref="ActionRunner"/> (consuming the EXISTING dispatch-path <see cref="BoundInputs"/> — no second
/// bind) with a captured <see cref="IOpenAiClient"/> to assert: (a) a dispatch WITH host-record memory
/// renders the memory + host-identity + user-memory grounding into the executor prompt; (b) a dispatch
/// with NO host/memory renders a prompt BYTE-IDENTICAL to the pre-change executor prompt (the regression
/// pin). Vertical-slice seam (production ActionRunner + PromptSchemaRenderer); the model boundary
/// (IOpenAiClient) is the one mocked collaborator, used only to capture the composed prompt.
/// </summary>
public class DispatchPromptGroundingSeamTests
{
    private const string MatterId = "33333333-3333-3333-3333-333333333333";
    private const string FlatInstruction = "You are a legal summarizer. Produce a concise summary.";
    private const string OutputSchema = """{"type":"object","properties":{"result":{"type":"string"}},"required":["result"]}""";

    [Fact]
    public async Task Dispatch_WithHostRecordMemory_RendersGroundingContextIntoExecutorPrompt()
    {
        var (runner, captured) = BuildRunner();

        var hostIdentity = HostIdentityProducer.BuildEnrichmentBlock("matter", MatterId, "Acme Corp v. Beta LLC", null);
        var envelope = ContextEnvelopeReferenceProducer.Assemble(
            userFragment: "### About You (from prior sessions)\n**Key Facts**: Preferred citation style — Bluebook",
            businessFragment: hostIdentity);

        const string recordMemory = "### Matter Context (from prior sessions)\n**Key Dates**: Filing deadline — 2026-09-01";
        var bound = new BoundInputs
        {
            Context = envelope,
            Operand = ResolvedOperand.None,
            RecordMemoryFragment = recordMemory,
        };

        await runner.RunAsync(BuildAction(), bound, RunContext(), CancellationToken.None);

        captured.Prompt.Should().NotBeNull();
        captured.Prompt.Should().Contain(hostIdentity, "the host-identity (Business) fragment is rendered into the dispatch prompt (D6)");
        captured.Prompt.Should().Contain("Bluebook", "the user-memory (User) fragment is rendered into the dispatch prompt (D6/D3)");
        captured.Prompt.Should().Contain("2026-09-01", "the host record-memory fragment is rendered into the dispatch prompt (D6/PE-D8(b))");
        captured.Prompt.Should().Contain(FlatInstruction, "the Action instruction is still present");
        captured.Prompt!.IndexOf(hostIdentity, StringComparison.Ordinal)
            .Should().BeLessThan(captured.Prompt.IndexOf(FlatInstruction, StringComparison.Ordinal),
                "the stable-prefix grounding additions render ABOVE the instruction + operand section");
    }

    [Fact]
    public async Task Dispatch_WithNoHostOrMemory_RendersPromptByteIdenticalToPreChange()
    {
        var (runner, captured) = BuildRunner();

        // An empty envelope (no host, no user memory) + no record memory = the pre-change dispatch shape.
        var bound = new BoundInputs
        {
            Context = ContextEnvelopeReferenceProducer.Assemble(),
            Operand = ResolvedOperand.None,
            RecordMemoryFragment = null,
        };

        await runner.RunAsync(BuildAction(), bound, RunContext(), CancellationToken.None);

        // Pre-change prompt for a flat-text, no-operand action is exactly the Action instruction.
        captured.Prompt.Should().Be(FlatInstruction,
            "a dispatch with NO host/memory composes ZERO grounding context — the executor prompt is byte-identical to pre-change (the seam regression pin)");
    }

    // -------------------------------------------------------------------------------------

    private static (ActionRunner Runner, PromptCapture Capture) BuildRunner()
    {
        var capture = new PromptCapture();
        var openAi = new Mock<IOpenAiClient>();
        openAi
            .Setup(o => o.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BinaryData, string, string?, int?, float?, CancellationToken>(
                (p, _, _, _, _, _, _) => capture.Prompt = p)
            .ReturnsAsync("""{"result":"ok"}""");

        var renderer = new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance);
        var runner = new ActionRunner(openAi.Object, renderer, NullLogger<ActionRunner>.Instance);
        return (runner, capture);
    }

    private static AnalysisAction BuildAction() => new()
    {
        Id = Guid.NewGuid(),
        Name = "SummarizeSeam",
        SystemPrompt = FlatInstruction,      // flat text (NOT JPS) → BuildPrompt returns it unchanged for a no-operand run
        OutputSchemaJson = OutputSchema,
    };

    private static LinearRunContext RunContext() => new()
    {
        ConsumerType = "seam-test",
        CorrelationId = "corr-seam",
        TenantId = "tenant-seam",
    };

    private sealed class PromptCapture
    {
        public string? Prompt { get; set; }
    }
}
