// spaarkeai-compose-r2 — unit tests for the OBO document-profile facade.
//
// The facade invokes the "Document Profiler" Action (ACT-011) directly on the ADR-043
// completion-engine spine (IActionResolver → IDocumentTextSource(OBO) → IActionRunner →
// DocumentProfileOutputMapper → UpdateDocumentFieldsAsync) — NOT the retired playbook/node engine.
// These are BEHAVIOR tests of that boundary:
//   • Does it resolve the "Document Profiler" Action via the document-profile Binding
//     (IActionResolver.ResolveAsync(ConsumerTypes.DocumentProfile))?
//   • Does it run the Action via IActionRunner (the completion engine) — and NEVER touch a playbook
//     orchestrator (there is no such dependency)?
//   • Does it map the Action's structured output onto sprk_document columns and persist via
//     UpdateDocumentFieldsAsync?
//   • Does it extract text under OBO (IDocumentTextSource, which downloads via OBO)?
//   • Is it best-effort (never throws; skips/fails degrade to a result)?
//
// Mocking boundary (ADR-038 §4 module-boundary mocks — not banned collaborator mocking): every mock
// here is a genuine seam — IDocumentDataverseService (Dataverse), IActionResolver / IActionRunner
// (the AI execution spine), IDocumentTextSource (SPE download + extraction).

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

public sealed class DocumentProfileAiTests
{
    private const string DriveId = "drive-obo-1";
    private const string ItemId = "item-obo-1";
    private const string FileName = "draft.docx";

    private readonly Mock<IDocumentDataverseService> _documents = new(MockBehavior.Strict);
    private readonly Mock<IActionResolver> _actionResolver = new(MockBehavior.Strict);
    private readonly Mock<IDocumentTextSource> _textSource = new(MockBehavior.Strict);
    private readonly Mock<IActionRunner> _actionRunner = new(MockBehavior.Strict);

    private DocumentProfileAi CreateSut() => new(
        _documents.Object,
        NullLogger<DocumentProfileAi>.Instance,
        _actionResolver.Object,
        _textSource.Object,
        _actionRunner.Object);

    private void ArrangeDocument(string? driveId = DriveId, string? itemId = ItemId)
    {
        _documents
            .Setup(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Draft",
                FileName = FileName,
                GraphDriveId = driveId,
                GraphItemId = itemId,
            });
    }

    private static AnalysisAction ProfilerAction() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Document Profiler",
        SystemPrompt = "profile the document",
        OutputSchemaJson = "{\"type\":\"object\"}",
    };

    private void ArrangeText() =>
        _textSource
            .Setup(t => t.ExtractFromDocumentIdAsync(It.IsAny<Guid>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentText
            {
                DocumentId = Guid.NewGuid(),
                FileName = FileName,
                ExtractedText = "extracted body text",
                GraphDriveId = DriveId,
                GraphItemId = ItemId,
            });

    private static JsonElement Output(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ── Happy path: resolves the Document Profiler Action, runs it via the completion engine,
    //    maps output → columns, and writes via UpdateDocumentFieldsAsync. No playbook engine. ──────
    [Fact]
    public async Task ProfileDocumentAsUserAsync_ResolvesProfilerAction_RunsViaActionRunner_MapsAndWrites()
    {
        var documentId = Guid.NewGuid();
        ArrangeDocument();
        ArrangeText();

        var action = ProfilerAction();
        _actionResolver
            .Setup(r => r.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(action);

        AnalysisAction? ranAction = null;
        _actionRunner
            .Setup(r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
                It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
            .Callback<AnalysisAction, DocumentText, LinearRunContext, CancellationToken>(
                (a, _, _, _) => ranAction = a)
            .ReturnsAsync(Output(
                "{\"sprk_filesummary\":\"A summary\",\"sprk_filetldr\":\"tldr\",\"sprk_documenttype\":\"contract\"}"));

        Dictionary<string, object?>? written = null;
        _documents
            .Setup(d => d.UpdateDocumentFieldsAsync(
                documentId.ToString(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object?>, CancellationToken>((_, f, _) => written = f)
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ProfileDocumentAsUserAsync(documentId, new DefaultHttpContext(), CancellationToken.None);

        result.Success.Should().BeTrue();

        // Resolved the Document Profiler Action via the document-profile Binding (single routing surface).
        _actionResolver.Verify(r => r.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<CancellationToken>()), Times.Once);

        // Ran the Action via the ADR-043 completion engine (IActionRunner) — the SAME action instance.
        _actionRunner.Verify(r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
            It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()), Times.Once);
        ranAction.Should().BeSameAs(action);

        // Text was extracted under OBO (IDocumentTextSource takes the HttpContext for OBO exchange).
        _textSource.Verify(t => t.ExtractFromDocumentIdAsync(documentId, It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Once);

        // Output mapped → sprk_document columns and written via the SDK field-update path.
        _documents.Verify(d => d.UpdateDocumentFieldsAsync(
            documentId.ToString(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Once);
        written.Should().NotBeNull();
        written!.Should().ContainKey("sprk_filesummary");
        written.Should().ContainKey("sprk_filetldr");
        written.Should().ContainKey("sprk_documenttype");   // Choice-coerced (OptionSetValue)
        written!["sprk_documenttype"].Should().BeOfType<Microsoft.Xrm.Sdk.OptionSetValue>();
        written.Should().ContainKey("sprk_filetype");        // deterministic from extension (DOCX)
    }

    // ── Skip cleanly (no action run, no write) when the record has no SPE pointer ────────────────
    [Fact]
    public async Task ProfileDocumentAsUserAsync_WhenNoSpePointer_SkipsWithoutRunningAction()
    {
        var documentId = Guid.NewGuid();
        ArrangeDocument(driveId: null, itemId: null);

        var result = await CreateSut().ProfileDocumentAsUserAsync(documentId, new DefaultHttpContext(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.SkipReason.Should().NotBeNullOrEmpty();
        _actionResolver.Verify(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _actionRunner.Verify(r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
            It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()), Times.Never);
        _documents.Verify(d => d.UpdateDocumentFieldsAsync(
            It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Skip when the AI execution seams are unavailable (compound AI gate OFF → null seams) ─────
    [Fact]
    public async Task ProfileDocumentAsUserAsync_WhenAiSeamsNull_SkipsWithoutTouchingDataverse()
    {
        var sut = new DocumentProfileAi(_documents.Object, NullLogger<DocumentProfileAi>.Instance);

        var result = await sut.ProfileDocumentAsUserAsync(Guid.NewGuid(), new DefaultHttpContext(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.SkipReason.Should().NotBeNullOrEmpty();
        _documents.Verify(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Empty extracted text degrades to Failed (best-effort), no action run needed to write ─────
    [Fact]
    public async Task ProfileDocumentAsUserAsync_WhenNoExtractableText_ReturnsFailed()
    {
        var documentId = Guid.NewGuid();
        ArrangeDocument();
        _actionResolver
            .Setup(r => r.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfilerAction());
        _textSource
            .Setup(t => t.ExtractFromDocumentIdAsync(It.IsAny<Guid>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentText { DocumentId = documentId, FileName = FileName, ExtractedText = "" });

        var result = await CreateSut().ProfileDocumentAsUserAsync(documentId, new DefaultHttpContext(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
        _actionRunner.Verify(r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
            It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()), Times.Never);
        _documents.Verify(d => d.UpdateDocumentFieldsAsync(
            It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Best-effort: a throwing action run is swallowed into a Failed result (never propagates) ──
    [Fact]
    public async Task ProfileDocumentAsUserAsync_WhenActionRunThrows_ReturnsFailedAndDoesNotThrow()
    {
        var documentId = Guid.NewGuid();
        ArrangeDocument();
        ArrangeText();
        _actionResolver
            .Setup(r => r.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfilerAction());
        _actionRunner
            .Setup(r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
                It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("llm exploded"));

        var act = async () => await CreateSut().ProfileDocumentAsUserAsync(documentId, new DefaultHttpContext(), CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Success.Should().BeFalse();
        result.Subject.FailureReason.Should().NotBeNullOrEmpty();
        _documents.Verify(d => d.UpdateDocumentFieldsAsync(
            It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
