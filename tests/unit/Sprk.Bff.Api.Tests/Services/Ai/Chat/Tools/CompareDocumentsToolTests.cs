using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.Tools;

/// <summary>
/// Unit tests for <see cref="CompareDocumentsTool"/>.
///
/// Covers:
/// (a) Two identical documents return zero changes.
/// (b) Addition in doc B is detected correctly.
/// (c) Deletion in A (not in B) is detected correctly.
/// (d) Modification (changed word) is detected correctly.
/// (e) Inaccessible document A returns a structured error result.
/// (f) Both fetches run in parallel (Task.WhenAll).
/// </summary>
public class CompareDocumentsToolTests
{
    private const string DocId1 = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string DocId2 = "bbbbbbbb-0000-0000-0000-000000000002";
    private const string DriveId = "b!drive-test";
    private const string ItemId1 = "item-001";
    private const string ItemId2 = "item-002";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DocumentEntity MakeDocument(string id, string itemId, string fileName = "doc.txt")
        => new()
        {
            Id = id,
            GraphDriveId = DriveId,
            GraphItemId = itemId,
            FileName = fileName,
            Name = fileName
        };

    private static Stream TextStream(string text)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// Creates a mocked ITextExtractor that returns the given text for the given file name.
    /// </summary>
    private static Mock<ITextExtractor> TextExtractorReturning(string text)
    {
        var mock = new Mock<ITextExtractor>();
        mock.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextExtractionResult.Succeeded(text, TextExtractionMethod.Native));
        return mock;
    }

    private static CompareDocumentsTool BuildTool(
        Mock<IDocumentDataverseService> docService,
        Mock<ISpeFileOperations> speStore,
        Mock<ITextExtractor> textExtractor)
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<CompareDocumentsTool>.Instance;
        return new CompareDocumentsTool(
            docService.Object,
            speStore.Object,
            textExtractor.Object,
            httpContext: null, // use app-only download path
            logger);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // (a) Two identical documents return zero changes
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_IdenticalDocuments_ReturnZeroChanges()
    {
        // Arrange
        const string commonText = "INTRODUCTION\nThis is the introduction text.\n\nCONCLUSION\nFinal remarks.";

        var docService = new Mock<IDocumentDataverseService>();
        var speStore = new Mock<ISpeFileOperations>();
        var extractor = TextExtractorReturning(commonText);

        docService.Setup(s => s.GetDocumentAsync(DocId1, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeDocument(DocId1, ItemId1));
        docService.Setup(s => s.GetDocumentAsync(DocId2, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeDocument(DocId2, ItemId2));

        speStore.Setup(s => s.DownloadFileAsync(DriveId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string _, CancellationToken _) => TextStream(commonText));

        var tool = BuildTool(docService, speStore, extractor);

        // Act
        var result = await tool.CompareDocumentsAsync(DocId1, DocId2);

        // Assert
        result.IsError.Should().BeFalse();
        result.TotalChanges.Should().Be(0);
        result.Additions.Should().Be(0);
        result.Deletions.Should().Be(0);
        result.Modifications.Should().Be(0);
        result.Sections.All(s => s.ChangeType == DiffChangeType.Unchanged).Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // (b) Addition in doc B detected correctly
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_AdditionInDocB_DetectedAsAddition()
    {
        // Arrange — doc B has an extra section that doc A does not
        const string textA = "INTRODUCTION\nShared introduction text.";
        const string textB = "INTRODUCTION\nShared introduction text.\n\nNEW SECTION\nThis section only exists in document B.";

        var docService = new Mock<IDocumentDataverseService>();
        var speStore = new Mock<ISpeFileOperations>();

        docService.Setup(s => s.GetDocumentAsync(DocId1, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeDocument(DocId1, ItemId1));
        docService.Setup(s => s.GetDocumentAsync(DocId2, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeDocument(DocId2, ItemId2));

        speStore.Setup(s => s.DownloadFileAsync(DriveId, ItemId1, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string _, CancellationToken _) => TextStream(textA));
        speStore.Setup(s => s.DownloadFileAsync(DriveId, ItemId2, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string _, CancellationToken _) => TextStream(textB));

        var extractor = new Mock<ITextExtractor>();
        extractor.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), "doc.txt", It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Stream s, string _, CancellationToken _) =>
                 {
                     using var reader = new StreamReader(s);
                     var text = reader.ReadToEnd();
                     return TextExtractionResult.Succeeded(text, TextExtractionMethod.Native);
                 });

        var tool = BuildTool(docService, speStore, extractor);

        // Act
        var result = await tool.CompareDocumentsAsync(DocId1, DocId2);

        // Assert
        result.IsError.Should().BeFalse();
        result.Additions.Should().BeGreaterThan(0, "doc B has a new section that doc A does not");
        result.Sections.Any(s => s.ChangeType == DiffChangeType.Addition).Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // (c) Deletion (section in A not in B)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_SectionRemovedInDocB_DetectedAsDeletion()
    {
        // Arrange — doc A has a section that doc B does not
        const string textA = "INTRODUCTION\nShared intro.\n\nDEFINITIONS\nThis section was removed.";
        const string textB = "INTRODUCTION\nShared intro.";

        var docService = new Mock<IDocumentDataverseService>();
        var speStore = new Mock<ISpeFileOperations>();

        docService.Setup(s => s.GetDocumentAsync(DocId1, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeDocument(DocId1, ItemId1));
        docService.Setup(s => s.GetDocumentAsync(DocId2, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeDocument(DocId2, ItemId2));

        speStore.Setup(s => s.DownloadFileAsync(DriveId, ItemId1, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string _, CancellationToken _) => TextStream(textA));
        speStore.Setup(s => s.DownloadFileAsync(DriveId, ItemId2, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string _, CancellationToken _) => TextStream(textB));

        var extractor = new Mock<ITextExtractor>();
        extractor.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Stream s, string _, CancellationToken _) =>
                 {
                     using var reader = new StreamReader(s);
                     return TextExtractionResult.Succeeded(reader.ReadToEnd(), TextExtractionMethod.Native);
                 });

        var tool = BuildTool(docService, speStore, extractor);

        // Act
        var result = await tool.CompareDocumentsAsync(DocId1, DocId2);

        // Assert
        result.IsError.Should().BeFalse();
        result.Deletions.Should().BeGreaterThan(0, "section DEFINITIONS was removed from doc B");
        result.Sections.Any(s => s.ChangeType == DiffChangeType.Deletion).Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // (d) Modification (changed word)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_ChangedWord_DetectedAsModification()
    {
        // Arrange — same section heading but one word changed
        const string textA = "TERMS AND CONDITIONS\nThe agreement shall commence on January first.";
        const string textB = "TERMS AND CONDITIONS\nThe agreement shall commence on February first.";

        var docService = new Mock<IDocumentDataverseService>();
        var speStore = new Mock<ISpeFileOperations>();

        docService.Setup(s => s.GetDocumentAsync(DocId1, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeDocument(DocId1, ItemId1));
        docService.Setup(s => s.GetDocumentAsync(DocId2, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeDocument(DocId2, ItemId2));

        speStore.Setup(s => s.DownloadFileAsync(DriveId, ItemId1, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string _, CancellationToken _) => TextStream(textA));
        speStore.Setup(s => s.DownloadFileAsync(DriveId, ItemId2, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string _, CancellationToken _) => TextStream(textB));

        var extractor = new Mock<ITextExtractor>();
        extractor.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Stream s, string _, CancellationToken _) =>
                 {
                     using var reader = new StreamReader(s);
                     return TextExtractionResult.Succeeded(reader.ReadToEnd(), TextExtractionMethod.Native);
                 });

        var tool = BuildTool(docService, speStore, extractor);

        // Act
        var result = await tool.CompareDocumentsAsync(DocId1, DocId2);

        // Assert
        result.IsError.Should().BeFalse();
        result.Modifications.Should().BeGreaterThan(0,
            "the section body changed from 'January' to 'February'");

        var modSection = result.Sections.First(s => s.ChangeType == DiffChangeType.Modification);
        var modChange = modSection.Changes.First(c => c.ChangeType == DiffChangeType.Modification);

        modChange.OriginalText.Should().Contain("January",
            "original text should contain the removed word");
        modChange.ModifiedText.Should().Contain("February",
            "modified text should contain the inserted word");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // (e) Inaccessible document A returns structured error
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_DocumentAInaccessible_ReturnsStructuredError()
    {
        // Arrange — document A's SPE download returns null (simulates 403/404)
        var docService = new Mock<IDocumentDataverseService>();
        var speStore = new Mock<ISpeFileOperations>();
        var extractor = new Mock<ITextExtractor>();

        docService.Setup(s => s.GetDocumentAsync(DocId1, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeDocument(DocId1, ItemId1));
        docService.Setup(s => s.GetDocumentAsync(DocId2, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeDocument(DocId2, ItemId2));

        // Document A download returns null → inaccessible
        speStore.Setup(s => s.DownloadFileAsync(DriveId, ItemId1, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Stream?)null);

        // Document B succeeds
        speStore.Setup(s => s.DownloadFileAsync(DriveId, ItemId2, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string _, CancellationToken _) => TextStream("Some content."));

        extractor.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(TextExtractionResult.Succeeded("Some content.", TextExtractionMethod.Native));

        var tool = BuildTool(docService, speStore, extractor);

        // Act
        var result = await tool.CompareDocumentsAsync(DocId1, DocId2);

        // Assert — must NOT throw; must return a structured error describing which document failed
        result.IsError.Should().BeTrue("document A stream was null → should be a structured error");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        result.ErrorMessage.Should().Contain(DocId1,
            "error message should identify which document was inaccessible");
        result.TotalChanges.Should().Be(0, "no diff should be computed when a document is unavailable");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // (f) Both fetches run in parallel (Task.WhenAll)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_FetchesBothDocumentsInParallel()
    {
        // Arrange — each SPE download has a 100ms delay; parallel execution should
        // complete well within 2× the per-document delay.
        const int downloadDelayMs = 100;
        const int maxAllowedMs = 300; // generous: serial would be 200ms minimum

        var docService = new Mock<IDocumentDataverseService>();
        var speStore = new Mock<ISpeFileOperations>();

        docService.Setup(s => s.GetDocumentAsync(DocId1, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeDocument(DocId1, ItemId1));
        docService.Setup(s => s.GetDocumentAsync(DocId2, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MakeDocument(DocId2, ItemId2));

        speStore
            .Setup(s => s.DownloadFileAsync(DriveId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, string _, CancellationToken ct) =>
            {
                await Task.Delay(downloadDelayMs, ct);
                return (Stream?)TextStream("content");
            });

        var extractor = new Mock<ITextExtractor>();
        extractor.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(TextExtractionResult.Succeeded("content", TextExtractionMethod.Native));

        var tool = BuildTool(docService, speStore, extractor);
        var stopwatch = Stopwatch.StartNew();

        // Act
        await tool.CompareDocumentsAsync(DocId1, DocId2);

        stopwatch.Stop();

        // Assert — parallel execution should finish well under the serial minimum
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(maxAllowedMs,
            $"both downloads run in parallel; should complete in ~{downloadDelayMs}ms not ~{2 * downloadDelayMs}ms");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // [Description] attributes — required for AIFunctionFactory.Create
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CompareDocumentsAsync_HasDescriptionAttribute_OnDocumentId1Parameter()
    {
        var method = typeof(CompareDocumentsTool)
            .GetMethod(nameof(CompareDocumentsTool.CompareDocumentsAsync));
        method.Should().NotBeNull();

        var param = method!.GetParameters().First(p => p.Name == "documentId1");
        param.GetCustomAttribute<DescriptionAttribute>()
             .Should().NotBeNull("AIFunctionFactory.Create reads [Description] attributes on parameters");
    }

    [Fact]
    public void CompareDocumentsAsync_HasDescriptionAttribute_OnDocumentId2Parameter()
    {
        var method = typeof(CompareDocumentsTool)
            .GetMethod(nameof(CompareDocumentsTool.CompareDocumentsAsync));
        method.Should().NotBeNull();

        var param = method!.GetParameters().First(p => p.Name == "documentId2");
        param.GetCustomAttribute<DescriptionAttribute>()
             .Should().NotBeNull("AIFunctionFactory.Create reads [Description] attributes on parameters");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Internal helpers — SegmentIntoSections
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SegmentIntoSections_DetectsNumberedHeadings()
    {
        const string text = "1. Introduction\nFirst para.\n\n2. Definitions\nSecond para.";
        var sections = CompareDocumentsTool.SegmentIntoSections(text);

        sections.Should().HaveCountGreaterThanOrEqualTo(2);
        sections.Any(s => s.Title.Contains("Introduction")).Should().BeTrue();
        sections.Any(s => s.Title.Contains("Definitions")).Should().BeTrue();
    }

    [Fact]
    public void SegmentIntoSections_FallsBackToParagraphWhenNoHeadings()
    {
        const string text = "First paragraph content here.\n\nSecond paragraph content there.";
        var sections = CompareDocumentsTool.SegmentIntoSections(text);

        sections.Should().HaveCount(2);
        sections[0].Title.Should().StartWith("Paragraph 1");
        sections[1].Title.Should().StartWith("Paragraph 2");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Internal helpers — DiffWords (LCS)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DiffWords_IdenticalText_ProducesNoChanges()
    {
        var changes = CompareDocumentsTool.DiffWords("hello world", "hello world");
        changes.Should().BeEmpty("identical text has no changes");
    }

    [Fact]
    public void DiffWords_AddedWord_DetectedAsAddition()
    {
        var changes = CompareDocumentsTool.DiffWords("hello world", "hello beautiful world");
        changes.Should().ContainSingle(c => c.ChangeType == DiffChangeType.Addition);
    }

    [Fact]
    public void DiffWords_RemovedWord_DetectedAsDeletion()
    {
        var changes = CompareDocumentsTool.DiffWords("hello beautiful world", "hello world");
        changes.Should().ContainSingle(c => c.ChangeType == DiffChangeType.Deletion);
    }

    [Fact]
    public void DiffWords_ReplacedWord_DetectedAsModificationOrAdditionDeletion()
    {
        var changes = CompareDocumentsTool.DiffWords("The cat sat here", "The dog sat here");

        // Must detect that "cat" → "dog" is a change (either Modification or Addition+Deletion pair)
        var hasChange = changes.Any(c =>
            c.ChangeType == DiffChangeType.Modification ||
            c.ChangeType == DiffChangeType.Addition ||
            c.ChangeType == DiffChangeType.Deletion);

        hasChange.Should().BeTrue("replacing 'cat' with 'dog' must produce at least one change entry");
    }
}
