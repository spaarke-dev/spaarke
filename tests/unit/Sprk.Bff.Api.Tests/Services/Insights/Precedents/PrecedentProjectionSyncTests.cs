using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Insights.Precedents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Insights.Precedents;

/// <summary>
/// Unit tests for <see cref="PrecedentProjectionSync"/> — the D-P4 Zone B service that
/// projects Confirmed <c>sprk_precedent</c> rows to <c>spaarke-insights-index</c>.
/// </summary>
/// <remarks>
/// <para>
/// Covers acceptance criteria 1, 2, 3, 5 from task 041 POML. Criterion 4 (IndexRetrieveNode
/// round-trip) is a real-AI-Search integration scenario that belongs in task 070 (D-P16 smoke
/// test) — documented as deferred in the task return briefing.
/// </para>
/// <para>
/// <b>SearchIndexClient + SearchClient mocking</b>: both are non-sealed Azure SDK classes
/// with protected/virtual members; <c>Mock&lt;T&gt;</c> creates a proxy that overrides the
/// virtual methods we exercise (<c>GetSearchClient</c>, <c>MergeOrUploadDocumentsAsync</c>).
/// Results are built via <see cref="SearchModelFactory"/> per the Azure SDK testing pattern
/// used elsewhere in this codebase (see <c>VisualizationServiceTests</c>).
/// </para>
/// </remarks>
public class PrecedentProjectionSyncTests
{
    private static readonly Guid PrecedentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string TenantId = "tenant-acme";

    private readonly Mock<IPrecedentBoard> _boardMock = new(MockBehavior.Strict);
    private readonly Mock<IInsightsAi> _insightsAiMock = new(MockBehavior.Strict);
    private readonly Mock<SearchIndexClient> _searchIndexClientMock = new();
    private readonly Mock<SearchClient> _searchClientMock = new();
    private readonly FixedTimeProvider _timeProvider = new(new DateTimeOffset(2026, 5, 28, 14, 22, 0, TimeSpan.Zero));

    public PrecedentProjectionSyncTests()
    {
        // GetSearchClient(indexName) is virtual on SearchIndexClient — Moq's proxy intercepts it.
        _searchIndexClientMock
            .Setup(c => c.GetSearchClient(PrecedentProjectionSync.TargetIndexName))
            .Returns(_searchClientMock.Object);
    }

    /// <summary>
    /// Minimal <see cref="TimeProvider"/> stub returning a fixed UTC time. Lets tests
    /// assert that <c>asOf</c> on the projected document equals a known timestamp without
    /// pulling in <c>Microsoft.Extensions.Time.Testing</c> (not currently referenced).
    /// </summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _fixedNow;
        public FixedTimeProvider(DateTimeOffset fixedNow) => _fixedNow = fixedNow;
        public override DateTimeOffset GetUtcNow() => _fixedNow;
    }

    private PrecedentProjectionSync CreateSut()
        => new(
            _boardMock.Object,
            _insightsAiMock.Object,
            _searchIndexClientMock.Object,
            NullLogger<PrecedentProjectionSync>.Instance,
            _timeProvider);

    private static PrecedentRecord MakeConfirmedRecord(
        string patternStatement = "BigFirm cure-period pattern statement.")
        => new(
            Id: PrecedentId,
            Name: "BigFirm cure-period precedent",
            PatternStatement: patternStatement,
            StatusValue: PrecedentStatus.Confirmed,
            ReviewerByUserId: Guid.NewGuid(),
            ProducedBy: "manual-sme-author");

    private static PrecedentRecord MakeTentativeRecord()
        => new(
            Id: PrecedentId,
            Name: "tentative",
            PatternStatement: "draft pattern",
            StatusValue: PrecedentStatus.Tentative,
            ReviewerByUserId: null,
            ProducedBy: "manual-sme-author");

    private static ReadOnlyMemory<float> MakeVector(int dims = 3072)
    {
        var vec = new float[dims];
        for (var i = 0; i < dims; i++) vec[i] = 0.001f * i;
        return vec;
    }

    private static Response<IndexDocumentsResult> MakeSuccessIndexResponse(string documentId)
    {
        var result = SearchModelFactory.IndexingResult(
            key: documentId,
            errorMessage: null,
            succeeded: true,
            status: 200);
        var indexDocsResult = SearchModelFactory.IndexDocumentsResult(new[] { result });
        return Response.FromValue(indexDocsResult, response: null!);
    }

    private static Response<IndexDocumentsResult> MakeFailureIndexResponse(string documentId, string error)
    {
        var result = SearchModelFactory.IndexingResult(
            key: documentId,
            errorMessage: error,
            succeeded: false,
            status: 500);
        var indexDocsResult = SearchModelFactory.IndexDocumentsResult(new[] { result });
        return Response.FromValue(indexDocsResult, response: null!);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor + argument validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullBoard_Throws()
    {
        Action act = () => new PrecedentProjectionSync(
            null!, _insightsAiMock.Object, _searchIndexClientMock.Object, NullLogger<PrecedentProjectionSync>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("board");
    }

    [Fact]
    public void Constructor_NullInsightsAi_Throws()
    {
        Action act = () => new PrecedentProjectionSync(
            _boardMock.Object, null!, _searchIndexClientMock.Object, NullLogger<PrecedentProjectionSync>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("insightsAi");
    }

    [Fact]
    public void Constructor_NullSearchIndexClient_Throws()
    {
        Action act = () => new PrecedentProjectionSync(
            _boardMock.Object, _insightsAiMock.Object, null!, NullLogger<PrecedentProjectionSync>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("searchIndexClient");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Action act = () => new PrecedentProjectionSync(
            _boardMock.Object, _insightsAiMock.Object, _searchIndexClientMock.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ProjectAsync_EmptyPrecedentId_Throws()
    {
        var sut = CreateSut();
        Func<Task> act = () => sut.ProjectAsync(Guid.Empty, TenantId, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("precedentId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProjectAsync_BlankTenantId_Throws(string tenant)
    {
        var sut = CreateSut();
        Func<Task> act = () => sut.ProjectAsync(PrecedentId, tenant, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("tenantId");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Happy path — Confirmed Precedent projects
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProjectAsync_ConfirmedPrecedent_WritesToIndex()
    {
        // Arrange
        var record = MakeConfirmedRecord();
        var supporting = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var vector = MakeVector();
        var expectedDocId = $"prec:{PrecedentId:N}:v1";

        _boardMock.Setup(b => b.GetAsync(PrecedentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        _boardMock.Setup(b => b.GetSupportingMatterIdsAsync(PrecedentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supporting);
        _insightsAiMock.Setup(a => a.EmbedTextAsync(record.PatternStatement, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vector);
        _searchClientMock.Setup(s => s.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<SearchDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSuccessIndexResponse(expectedDocId));

        var sut = CreateSut();

        // Act
        var result = await sut.ProjectAsync(PrecedentId, TenantId, CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(PrecedentProjectionOutcome.Written);
        result.DocumentId.Should().Be(expectedDocId);
        result.StatusValue.Should().Be(PrecedentStatus.Confirmed);

        _boardMock.Verify();
        _insightsAiMock.Verify(
            a => a.EmbedTextAsync(record.PatternStatement, It.IsAny<CancellationToken>()),
            Times.Once,
            "embedding must route through IInsightsAi facade (§3.5.4 forbids direct IOpenAiClient in Zone B)");
        _searchClientMock.Verify(
            s => s.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<SearchDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProjectAsync_DocumentShape_MatchesSpec342()
    {
        // Capture the SearchDocument passed to MergeOrUploadDocumentsAsync and verify
        // its shape matches SPEC §3.4.2 — the same checks as the mapper unit tests
        // but exercised through the full sync path.
        var record = MakeConfirmedRecord();
        var supporting = new[] { Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa") };
        var vector = MakeVector();

        _boardMock.Setup(b => b.GetAsync(PrecedentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        _boardMock.Setup(b => b.GetSupportingMatterIdsAsync(PrecedentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supporting);
        _insightsAiMock.Setup(a => a.EmbedTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vector);

        SearchDocument? capturedDoc = null;
        _searchClientMock.Setup(s => s.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<SearchDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<SearchDocument>, IndexDocumentsOptions, CancellationToken>(
                (docs, _, _) => capturedDoc = docs.First())
            .ReturnsAsync(MakeSuccessIndexResponse($"prec:{PrecedentId:N}:v1"));

        var sut = CreateSut();

        // Act
        await sut.ProjectAsync(PrecedentId, TenantId, CancellationToken.None);

        // Assert — verify SPEC §3.4.2 shape on the actual document handed to the SDK
        capturedDoc.Should().NotBeNull();
        ((string)capturedDoc![PrecedentProjectionMapper.FieldArtifactType]).Should().Be("precedent");
        ((string)capturedDoc[PrecedentProjectionMapper.FieldStatus]).Should().Be("confirmed");
        ((string)capturedDoc[PrecedentProjectionMapper.FieldTenantId]).Should().Be(TenantId);
        ((float[])capturedDoc[PrecedentProjectionMapper.FieldContentVector]).Length.Should().Be(3072);
        capturedDoc.ContainsKey(PrecedentProjectionMapper.FieldConfidence).Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Status gate — only Confirmed Precedents project per D-P4
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PrecedentStatus.Tentative)]
    [InlineData(PrecedentStatus.UnderDriftReview)]
    [InlineData(PrecedentStatus.Deprecated)]
    [InlineData(PrecedentStatus.Retired)]
    public async Task ProjectAsync_NonConfirmedStatus_SkipsWithoutWriting(int statusValue)
    {
        var record = new PrecedentRecord(
            Id: PrecedentId,
            Name: "x",
            PatternStatement: "y",
            StatusValue: statusValue,
            ReviewerByUserId: null,
            ProducedBy: "manual-sme-author");

        _boardMock.Setup(b => b.GetAsync(PrecedentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var sut = CreateSut();

        var result = await sut.ProjectAsync(PrecedentId, TenantId, CancellationToken.None);

        result.Outcome.Should().Be(PrecedentProjectionOutcome.Skipped);
        result.StatusValue.Should().Be(statusValue);
        result.DocumentId.Should().BeNull();

        _insightsAiMock.Verify(
            a => a.EmbedTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "non-Confirmed Precedents must skip embedding generation");
        _searchClientMock.Verify(
            s => s.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<SearchDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "non-Confirmed Precedents must skip the AI Search write");
    }

    [Fact]
    public async Task ProjectAsync_NotFound_ReturnsNotFoundWithoutWriting()
    {
        _boardMock.Setup(b => b.GetAsync(PrecedentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PrecedentRecord?)null);

        var sut = CreateSut();

        var result = await sut.ProjectAsync(PrecedentId, TenantId, CancellationToken.None);

        result.Outcome.Should().Be(PrecedentProjectionOutcome.NotFound);
        result.DocumentId.Should().BeNull();
        result.StatusValue.Should().BeNull();

        _insightsAiMock.Verify(
            a => a.EmbedTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProjectAsync_ConfirmedButBlankPatternStatement_SkipsWithoutWriting()
    {
        var record = new PrecedentRecord(
            Id: PrecedentId,
            Name: "x",
            PatternStatement: "   ",
            StatusValue: PrecedentStatus.Confirmed,
            ReviewerByUserId: null,
            ProducedBy: "manual-sme-author");

        _boardMock.Setup(b => b.GetAsync(PrecedentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var sut = CreateSut();

        var result = await sut.ProjectAsync(PrecedentId, TenantId, CancellationToken.None);

        result.Outcome.Should().Be(PrecedentProjectionOutcome.Skipped);
        _insightsAiMock.Verify(
            a => a.EmbedTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "blank pattern statement must NOT be embedded");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Idempotency — re-projection produces deterministic document id
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProjectAsync_RepeatedCalls_UseDeterministicDocumentId()
    {
        var record = MakeConfirmedRecord();
        var vector = MakeVector();

        _boardMock.Setup(b => b.GetAsync(PrecedentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        _boardMock.Setup(b => b.GetSupportingMatterIdsAsync(PrecedentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());
        _insightsAiMock.Setup(a => a.EmbedTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vector);

        var capturedIds = new List<string>();
        _searchClientMock.Setup(s => s.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<SearchDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<SearchDocument>, IndexDocumentsOptions, CancellationToken>(
                (docs, _, _) => capturedIds.Add((string)docs.First()[PrecedentProjectionMapper.FieldId]))
            .ReturnsAsync(MakeSuccessIndexResponse($"prec:{PrecedentId:N}:v1"));

        var sut = CreateSut();

        var first = await sut.ProjectAsync(PrecedentId, TenantId, CancellationToken.None);
        var second = await sut.ProjectAsync(PrecedentId, TenantId, CancellationToken.None);

        first.DocumentId.Should().Be(second.DocumentId,
            "idempotency: re-projection must use the same document id so MergeOrUpload overwrites in place");
        capturedIds.Should().HaveCount(2);
        capturedIds[0].Should().Be(capturedIds[1]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Failure surfacing
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProjectAsync_SearchIndexFailureResult_ThrowsForFireAndForgetCatch()
    {
        var record = MakeConfirmedRecord();

        _boardMock.Setup(b => b.GetAsync(PrecedentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        _boardMock.Setup(b => b.GetSupportingMatterIdsAsync(PrecedentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());
        _insightsAiMock.Setup(a => a.EmbedTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeVector());
        _searchClientMock.Setup(s => s.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<SearchDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeFailureIndexResponse($"prec:{PrecedentId:N}:v1", "index temporarily unavailable"));

        var sut = CreateSut();

        Func<Task> act = () => sut.ProjectAsync(PrecedentId, TenantId, CancellationToken.None);
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*index temporarily unavailable*");
    }
}
