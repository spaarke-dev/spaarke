using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// Resolution-correctness tests for <see cref="ContextBinder"/> (ADR-043 E-10) — the operand precedence,
/// the deterministic <see cref="IContextBinder.HasStructuredOperand"/> branch predicate, and the
/// <b>anti-recurrence proof</b> that the seam fits the E-12 consumers (node engine + narrator) without
/// reshaping: a pre-resolved <c>inputBinding</c>-shaped element renders to the byte-identical single-source
/// <c>## Input</c> that <c>AiCompletionNodeExecutor</c> / <c>DailyBriefingNarrator</c> emit today.
/// </summary>
public sealed class ContextBinderResolutionTests
{
    private static ContextBinder NewBinder() =>
        new(new ChatSessionManager(new InMemoryTenantCache(),
                Mock.Of<IChatDataverseRepository>(), Mock.Of<ILogger<ChatSessionManager>>()),
            Mock.Of<ILogger<ContextBinder>>());

    private const string ComposeInputSchema =
        """{"type":"object","required":["selectionText"],"properties":{"selectionText":{"type":"string"}}}""";

    private const string SummarizeInputSchema =
        """{"type":"object","properties":{"fileIds":{"type":"array","items":{"type":"string"}},"styleHint":{"type":"string"}}}""";

    // ─── HasStructuredOperand — the deterministic dispatch branch predicate ───

    [Fact]
    public void HasStructuredOperand_ComposeSchemaWithSelectionArg_IsTrue()
    {
        var args = JsonSerializer.SerializeToElement(new { selectionText = "a clause" });
        NewBinder().HasStructuredOperand(ComposeInputSchema, args).Should().BeTrue();
    }

    [Fact]
    public void HasStructuredOperand_SummarizeSchemaWithOnlyFileIds_IsFalse()
    {
        // fileIds/styleHint are NOT structured operands → the file/`## Document` branch (non-regression).
        var args = JsonSerializer.SerializeToElement(new { fileIds = new[] { "file-1" }, styleHint = "executive" });
        NewBinder().HasStructuredOperand(SummarizeInputSchema, args).Should().BeFalse();
    }

    [Fact]
    public void HasStructuredOperand_LedgerResolutionReference_IsTrue()
    {
        var args = JsonSerializer.SerializeToElement(new { ledger_resolution = new { key = "b@t1" } });
        NewBinder().HasStructuredOperand(inputSchemaJson: null, args).Should().BeTrue();
    }

    [Fact]
    public void HasStructuredOperand_UndeclaredOperandField_IsIgnored()
    {
        // selectionText NOT declared by the schema → "accepted and ignored" (the pre-E-10 contract).
        var args = JsonSerializer.SerializeToElement(new { selectionText = "x" });
        NewBinder().HasStructuredOperand(SummarizeInputSchema, args).Should().BeFalse();
    }

    // ─── BindAsync operand resolution ───

    [Fact]
    public async Task BindAsync_DeclaredSelectionArg_ResolvesInputOperand()
    {
        var args = JsonSerializer.SerializeToElement(new { selectionText = "the clause" });
        var bound = await NewBinder().BindAsync(
            new ContextBindingRequest { InputSchemaJson = ComposeInputSchema, Args = args }, CancellationToken.None);

        bound.Operand.Channel.Should().Be(OperandChannel.Input);
        bound.Operand.Kind.Should().Be(OperandKind.SelectionText);
        bound.Operand.Input!.Value.GetProperty("selectionText").GetString().Should().Be("the clause");
        bound.Fingerprint.Should().BeNull("no session coordinates were supplied");
    }

    [Fact]
    public async Task BindAsync_FileDocument_ResolvesDocumentOperand()
    {
        var doc = new DocumentText { FileName = "f.pdf", ExtractedText = "grounding text" };
        var bound = await NewBinder().BindAsync(
            new ContextBindingRequest { FileDocument = doc }, CancellationToken.None);

        bound.Operand.Channel.Should().Be(OperandChannel.Document);
        bound.Operand.Kind.Should().Be(OperandKind.FileDocument);
        bound.Operand.Document!.ExtractedText.Should().Be("grounding text");
    }

    [Fact]
    public async Task BindAsync_LedgerResolutionDanglingKey_ThrowsLoudly()
    {
        var args = JsonSerializer.SerializeToElement(new { ledger_resolution = new { key = "missing@t9" } });
        var act = async () => await NewBinder().BindAsync(
            new ContextBindingRequest { Args = args, LedgerOutputs = Array.Empty<SessionOutput>() },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*dangling*", "ADR-040: a dangling ledger reference fails loudly, never silently");
    }

    [Fact]
    public async Task BindAsync_LedgerResolutionTruncatedEntry_ThrowsLoudly()
    {
        // A truncation marker in the referenced entry must not be fed to a capability (ADR-040 size cap).
        var marker = SessionLedger.CapInlinePayload(
            JsonSerializer.SerializeToElement(new { blob = new string('x', SessionLedger.InlinePayloadCapBytes + 1) }));
        var truncated = new SessionOutput
        {
            Key = "b@t1", BindingId = "b", UcId = "uc", Turn = 1, Disposition = "informational",
            Payload = marker.Payload, CreatedAt = DateTimeOffset.UtcNow,
        };
        var args = JsonSerializer.SerializeToElement(new { ledger_resolution = new { key = "b@t1" } });

        var act = async () => await NewBinder().BindAsync(
            new ContextBindingRequest { Args = args, LedgerOutputs = new[] { truncated } }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*truncated*");
    }

    // ─── Anti-recurrence proof: the seam fits the E-12 consumers (node engine / narrator) ───

    [Fact]
    public async Task BindAsync_PreResolvedInput_PassesThroughForNodeEngineAndNarrator()
    {
        // The shape AiCompletionNodeExecutor extracts from configJson `inputBinding` (and the narrator
        // serializes from its typed payload) — a JsonElement object. The binder passes it straight to the
        // Input channel; consumer 2/3 (E-12) supply THIS, dispatch supplies args instead. One seam, no reshape.
        var inputBinding = JsonSerializer.SerializeToElement(new { totalNotificationCount = 3, keyDates = new[] { "2026-07-09" } });
        var bound = await NewBinder().BindAsync(
            new ContextBindingRequest { PreResolvedInput = inputBinding }, CancellationToken.None);

        bound.Operand.Channel.Should().Be(OperandChannel.Input);
        bound.Operand.Kind.Should().Be(OperandKind.PreResolved);

        // And it renders through the SAME single-source `## Input` producer the node engine uses today —
        // byte-identical by construction. This is the anti-recurrence guarantee.
        var actionRunnerRendered = PromptInputSection.Render(bound.Operand.Input);
        var nodeEngineRendered = PromptInputSection.Render(inputBinding);
        actionRunnerRendered.Should().Be(nodeEngineRendered).And.StartWith("## Input\n\n{");
    }

    [Fact]
    public async Task BindAsync_NoOperandSources_ResolvesNone()
    {
        var bound = await NewBinder().BindAsync(new ContextBindingRequest(), CancellationToken.None);
        bound.Operand.Channel.Should().Be(OperandChannel.None);
    }
}
