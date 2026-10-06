using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// The BFF writers of <c>sprk_analysis</c> that create a row in a DOCUMENT's context ALWAYS record that document as its
/// anchor (unified-access-control-r2 task 162 f1; owner round 15 item 2 — "an analysis created in a record's context
/// ALWAYS records its anchor", ADR-002 WP-1). Each test fails if its writer drops the anchor.
/// </summary>
/// <remarks>
/// <para>The writers, as found 2026-10-04 (grep of src/** for <c>CreateAnalysisAsync(</c>, <c>new Entity("sprk_analysis")</c>,
/// <c>createRecord('sprk_analysis'</c>): <c>AppOnlyAnalysisService</c> document profile and email analysis (here),
/// <c>AnalysisResultPersistence.StoreDocumentProfileOutputsAsync</c>'s create branch (here), <c>POST /api/ai/analysis/create</c>
/// and <c>/promote</c> (AnalysisEndpointsAuthorizationContractTests: the created row's document / regarding), the insights
/// observation mirror (DataverseObservationMirrorTests: <c>sprk_documentid</c>), and the client Create Analysis wizard
/// (CreateAnalysisWizardWidget.test.tsx: <c>sprk_documentid@odata.bind</c>). The shared seam
/// <c>DataverseServiceClientImpl.CreateAnalysisAsync</c> refuses a create with neither a document nor a regarding target
/// (FR-D9). The MDA form holds <c>sprk_documentid</c> ApplicationRequired (live metadata, 2026-10-04).</para>
/// <para>Substitution at the module boundary only (<see cref="IDataverseService"/>, SPE, text extraction, routing) —
/// ADR-038: no HTTP-handler mocks, no DI-registration or constructor tests.</para>
/// </remarks>
public class AnalysisWriterAnchorTests
{
    private readonly Mock<IDataverseService> _dataverse = new();
    private readonly Mock<ISpeFileOperations> _spe = new();
    private readonly Mock<ITextExtractor> _extractor = new();
    private readonly Mock<IConsumerRoutingService> _routing = new();
    private readonly Mock<IPlaybookLookupService> _playbookLookup = new();

    private AppOnlyAnalysisService CreateAppOnlyService() => new(
        _dataverse.Object,
        _dataverse.Object,
        _spe.Object,
        _extractor.Object,
        Mock.Of<IPlaybookService>(),
        _playbookLookup.Object,
        _routing.Object,
        Mock.Of<IScopeResolverService>(),
        Mock.Of<IToolHandlerRegistry>(),
        Mock.Of<INodeService>(),
        Mock.Of<IPlaybookOrchestrationService>(),
        new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), // task 146 (sweep integration)
        Mock.Of<ILogger<AppOnlyAnalysisService>>(),
        // Task 166 (batch-4 integration): every app-only download verifies the document's pointer first.
        TestRecordContainerResolver.ForBusinessUnitContainers("drive-162"));

    private void ExtractableFile(DocumentEntity document)
    {
        _extractor.Setup(x => x.IsSupported(It.IsAny<string>())).Returns(true);
        _spe.Setup(x => x.DownloadFileAsync(document.GraphDriveId!, document.GraphItemId!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream("contract text"u8.ToArray()));
        _extractor.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextExtractionResult { Success = true, Text = "contract text" });
        _dataverse.Setup(x => x.CreateAnalysisAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
    }

    private static DocumentEntity Document(Guid id, string fileName) => new()
    {
        Id = id.ToString(),
        Name = fileName,
        FileName = fileName,
        GraphDriveId = "drive-162",
        GraphItemId = $"item-{id:N}",
        Status = DocumentStatus.Active,
    };

    [Fact(DisplayName = "162 f1 writer: the app-only document profile creates its analysis anchored to the profiled document")]
    public async Task DocumentProfile_AnchorsTheAnalysisToItsDocument()
    {
        var documentId = Guid.NewGuid();
        var document = Document(documentId, "nda.pdf");
        _dataverse.Setup(x => x.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>())).ReturnsAsync(document);
        ExtractableFile(document);

        await CreateAppOnlyService().AnalyzeDocumentAsync(documentId);

        _dataverse.Verify(x => x.CreateAnalysisAsync(
                documentId, It.Is<string?>(n => n!.StartsWith("Document Profile")), It.IsAny<Guid?>(), null, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _dataverse.Verify(x => x.CreateAnalysisAsync(
                It.Is<Guid?>(d => d == null || d != documentId), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never, "no analysis may be created without the document it was created for");
    }

    [Fact(DisplayName = "162 f1 writer: the app-only email analysis creates its analysis anchored to the email's .eml document")]
    public async Task EmailAnalysis_AnchorsTheAnalysisToTheEmailDocument()
    {
        var emailId = Guid.NewGuid();
        var mainDocumentId = Guid.NewGuid();
        var mainDocument = Document(mainDocumentId, "Re: NDA.eml");
        _dataverse.Setup(x => x.GetDocumentByEmailLookupAsync(emailId, It.IsAny<CancellationToken>())).ReturnsAsync(mainDocument);
        _dataverse.Setup(x => x.GetDocumentsByParentAsync(mainDocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentEntity>());
        ExtractableFile(mainDocument);

        await CreateAppOnlyService().AnalyzeEmailAsync(emailId);

        _dataverse.Verify(x => x.CreateAnalysisAsync(
                mainDocumentId, It.Is<string?>(n => n!.StartsWith("Email Analysis")), It.IsAny<Guid?>(), null, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "162 f1 writer: profile-output storage, when it has to create the analysis itself, anchors it to the document")]
    public async Task ProfileOutputStorage_CreateBranch_AnchorsTheAnalysisToItsDocument()
    {
        var documentId = Guid.NewGuid();
        _dataverse.Setup(x => x.CreateAnalysisAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        var persistence = new AnalysisResultPersistence(
            _dataverse.Object,
            _dataverse.Object,
            Mock.Of<IWorkingDocumentService>(),
            Mock.Of<IStorageRetryPolicy>(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), // task 146 (sweep integration)
            Mock.Of<ILogger<AnalysisResultPersistence>>());

        await persistence.StoreDocumentProfileOutputsAsync(
            Guid.Empty, documentId, "Summarize File", new Dictionary<string, string?> { ["Summary"] = "s" }, CancellationToken.None);

        _dataverse.Verify(x => x.CreateAnalysisAsync(
                documentId, It.Is<string?>(n => n!.StartsWith("Document Profile")), It.IsAny<Guid?>(), null, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
