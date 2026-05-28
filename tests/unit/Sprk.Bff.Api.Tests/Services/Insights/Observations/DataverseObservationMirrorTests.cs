using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Insights.Observations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Insights.Observations;

/// <summary>
/// Unit tests for <see cref="DataverseObservationMirror"/> — the Zone B D-P11 mirror
/// sync (task 051) that writes one <c>sprk_analysis</c> row per emitted Observation.
/// </summary>
/// <remarks>
/// <para>
/// Covers all five acceptance criteria from task 051 POML:
/// (1) sprk_analysis polymorphic source-type field identified and used (via discriminator),
/// (2) each emitted Observation produces one mirror row,
/// (3) mirror row contains all expected fields,
/// (4) re-mirror is idempotent (no-op),
/// (5) mirror failure does not propagate (caller's try/catch governs; we verify no throw).
/// </para>
/// </remarks>
public class DataverseObservationMirrorTests
{
    private static readonly Guid ActionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DocumentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset FixedAsOf = new(2026, 5, 28, 14, 22, 0, TimeSpan.Zero);

    private readonly Mock<IGenericEntityService> _entityServiceMock = new(MockBehavior.Strict);

    private DataverseObservationMirror CreateSut(
        InsightsMirrorOptions? options = null)
    {
        options ??= new InsightsMirrorOptions
        {
            InsightsObservationActionId = ActionId,
            EnableMirror = true,
            EnableIdempotencyCheck = true,
        };
        return new DataverseObservationMirror(
            _entityServiceMock.Object,
            Options.Create(options),
            NullLogger<DataverseObservationMirror>.Instance);
    }

    private static ObservationArtifact MakeObservation(
        string id = "obs:M-2024-0341:outcomeCategory:doc-abc123",
        string predicate = "outcomeCategory",
        string evidenceRef = "spe://drive/drive-xyz/item/item-abc123",
        string? evidenceRefType = "document",
        string? quote = "The matter was resolved by a favorable settlement.")
    {
        var value = JsonDocument.Parse("\"favorable\"").RootElement.Clone();
        var evidence = evidenceRefType is null
            ? Array.Empty<EvidenceRef>()
            : new[]
              {
                  new EvidenceRef
                  {
                      RefType = evidenceRefType,
                      Ref = evidenceRef,
                      Quote = quote,
                  },
              };

        return new ObservationArtifact
        {
            Id = id,
            Subject = "matter:M-2024-0341",
            Predicate = predicate,
            Value = new Value { Raw = value, DisplayHint = "enum" },
            Evidence = evidence,
            AsOf = FixedAsOf,
            ProducedBy = new ProducedBy { Kind = "playbook", Id = "playbook://outcome-extraction", Version = "v1" },
            Scope = new Scope { TenantId = "tenant-acme", MatterId = "M-2024-0341" },
            TenantId = "tenant-acme",
            Confidence = 0.92,
        };
    }

    private static EntityCollection DocumentLookupResult(Guid documentId)
    {
        var entity = new Entity("sprk_document", documentId);
        entity["sprk_documentid"] = documentId;
        var collection = new EntityCollection();
        collection.Entities.Add(entity);
        return collection;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor + arg validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullEntityService_Throws()
    {
        var opts = Options.Create(new InsightsMirrorOptions { InsightsObservationActionId = ActionId });
        Action act = () => _ = new DataverseObservationMirror(null!, opts, NullLogger<DataverseObservationMirror>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("entityService");
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Action act = () => _ = new DataverseObservationMirror(_entityServiceMock.Object, null!, NullLogger<DataverseObservationMirror>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var opts = Options.Create(new InsightsMirrorOptions { InsightsObservationActionId = ActionId });
        Action act = () => _ = new DataverseObservationMirror(_entityServiceMock.Object, opts, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task MirrorAsync_NullObservation_Throws()
    {
        var sut = CreateSut();
        Func<Task> act = () => sut.MirrorAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("observation");
    }

    [Fact]
    public async Task MirrorAsync_CancelledToken_Throws()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        Func<Task> act = () => sut.MirrorAsync(MakeObservation(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Kill switch + dev-safe fallback
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MirrorAsync_DisabledByOption_SkipsWriteSilently()
    {
        var sut = CreateSut(new InsightsMirrorOptions
        {
            InsightsObservationActionId = ActionId,
            EnableMirror = false,
        });

        await sut.MirrorAsync(MakeObservation(), CancellationToken.None);

        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Disabled mirror must not write");
        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Disabled mirror must not query");
    }

    [Fact]
    public async Task MirrorAsync_UnconfiguredActionId_SkipsWriteSilently()
    {
        var sut = CreateSut(new InsightsMirrorOptions
        {
            InsightsObservationActionId = Guid.Empty, // unset = dev-safe fallback
            EnableMirror = true,
        });

        await sut.MirrorAsync(MakeObservation(), CancellationToken.None);

        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Unconfigured action id must trigger dev-safe no-op");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Evidence resolution
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MirrorAsync_NoDocumentEvidence_SkipsWriteWithWarning()
    {
        var sut = CreateSut();
        var obsNoEvidence = MakeObservation(evidenceRefType: null);

        await sut.MirrorAsync(obsNoEvidence, CancellationToken.None);

        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MirrorAsync_NonSpeEvidenceRef_SkipsWriteWithWarning()
    {
        var sut = CreateSut();
        var obs = MakeObservation(
            evidenceRef: "matter://M-1234",          // not an SPE document ref
            evidenceRefType: "comparable-matter");

        await sut.MirrorAsync(obs, CancellationToken.None);

        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MirrorAsync_DocumentNotFoundInDataverse_SkipsWriteWithWarning()
    {
        // Document-lookup query returns 0 results
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        var sut = CreateSut();
        await sut.MirrorAsync(MakeObservation(), CancellationToken.None);

        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MirrorAsync_DocumentLookupThrows_SkipsWriteWithWarning()
    {
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"));

        var sut = CreateSut();
        // Must NOT throw (fire-and-forget contract)
        Func<Task> act = () => sut.MirrorAsync(MakeObservation(), CancellationToken.None);
        await act.Should().NotThrowAsync();

        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Happy path — writes a row with all expected fields (criterion 2 + 3)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MirrorAsync_HappyPath_WritesSprkAnalysisRow()
    {
        Entity? capturedEntity = null;

        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysis"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection()); // no existing row

        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentLookupResult(DocumentId));

        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => capturedEntity = e)
            .ReturnsAsync(AnalysisId);

        var sut = CreateSut();
        await sut.MirrorAsync(MakeObservation(), CancellationToken.None);

        capturedEntity.Should().NotBeNull();
        capturedEntity!.LogicalName.Should().Be("sprk_analysis");

        // Acceptance criterion 1: discriminator field populated
        capturedEntity["sprk_searchprofile"].Should().Be("insights-observation@v1");

        // Acceptance criterion 3: all expected fields present
        capturedEntity.Contains("sprk_name").Should().BeTrue();
        capturedEntity.Contains("sprk_actionid").Should().BeTrue();
        capturedEntity.Contains("sprk_documentid").Should().BeTrue();
        capturedEntity.Contains("sprk_finaloutput").Should().BeTrue();
        capturedEntity.Contains("sprk_chathistory").Should().BeTrue();
        capturedEntity.Contains("sprk_workingdocument").Should().BeTrue();
        capturedEntity.Contains("sprk_sessionid").Should().BeTrue();
        capturedEntity.Contains("sprk_analysisstatus").Should().BeTrue();
        capturedEntity.Contains("sprk_startedon").Should().BeTrue();
        capturedEntity.Contains("sprk_completedon").Should().BeTrue();

        // Required lookups resolved correctly
        ((EntityReference)capturedEntity["sprk_actionid"]).Id.Should().Be(ActionId);
        ((EntityReference)capturedEntity["sprk_documentid"]).Id.Should().Be(DocumentId);

        // Working document carries the verbatim quote
        capturedEntity["sprk_workingdocument"].Should().Be("The matter was resolved by a favorable settlement.");

        // Final output round-trips as a valid Observation
        var envelope = JsonSerializer.Deserialize<InsightArtifact>((string)capturedEntity["sprk_finaloutput"]);
        envelope.Should().BeOfType<ObservationArtifact>();
        envelope!.Predicate.Should().Be("outcomeCategory");
    }

    [Fact]
    public async Task MirrorAsync_HappyPath_ResolvesDocumentByDriveItemId()
    {
        QueryExpression? capturedDocQuery = null;

        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"),
                It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => capturedDocQuery = q)
            .ReturnsAsync(DocumentLookupResult(DocumentId));

        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysis"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnalysisId);

        var sut = CreateSut();
        await sut.MirrorAsync(MakeObservation(), CancellationToken.None);

        capturedDocQuery.Should().NotBeNull();
        capturedDocQuery!.Criteria.Conditions
            .Should().ContainSingle(c => c.AttributeName == "sprk_driveitemid"
                                       && (string)c.Values[0] == "item-abc123");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Idempotency (acceptance criterion 4)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MirrorAsync_AlreadyMirrored_NoOps()
    {
        // sprk_analysis query returns an existing row (idempotency check finds duplicate)
        var existingRow = new Entity("sprk_analysis", AnalysisId);
        existingRow["sprk_analysisid"] = AnalysisId;
        var existingCollection = new EntityCollection();
        existingCollection.Entities.Add(existingRow);

        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentLookupResult(DocumentId));

        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysis"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCollection);

        var sut = CreateSut();
        await sut.MirrorAsync(MakeObservation(), CancellationToken.None);

        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Idempotency check must prevent duplicate writes");
    }

    [Fact]
    public async Task MirrorAsync_IdempotencyCheckDisabled_AlwaysWrites()
    {
        // Even though a duplicate exists in Dataverse, the disabled check causes the
        // insert to proceed (caller has assumed no re-runs).
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentLookupResult(DocumentId));

        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnalysisId);

        var sut = CreateSut(new InsightsMirrorOptions
        {
            InsightsObservationActionId = ActionId,
            EnableMirror = true,
            EnableIdempotencyCheck = false, // skip the pre-insert query
        });

        await sut.MirrorAsync(MakeObservation(), CancellationToken.None);

        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysis"),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "Idempotency-check skip must elide the dedup query");
        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MirrorAsync_IdempotencyKey_FiltersOnHashedObservationId()
    {
        QueryExpression? capturedDedupQuery = null;

        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentLookupResult(DocumentId));

        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysis"),
                It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => capturedDedupQuery = q)
            .ReturnsAsync(new EntityCollection());

        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnalysisId);

        var observation = MakeObservation();
        var expectedKey = ObservationMirrorMapper.ComputeIdempotencyKey(observation.Id);

        var sut = CreateSut();
        await sut.MirrorAsync(observation, CancellationToken.None);

        capturedDedupQuery.Should().NotBeNull();
        capturedDedupQuery!.Criteria.Conditions
            .Should().Contain(c => c.AttributeName == "sprk_sessionid"
                                 && (string)c.Values[0] == expectedKey);
        capturedDedupQuery.Criteria.Conditions
            .Should().Contain(c => c.AttributeName == "sprk_searchprofile"
                                 && (string)c.Values[0] == "insights-observation@v1");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Failure non-propagation (acceptance criterion 5)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MirrorAsync_DataverseCreateThrows_DoesNotPropagate()
    {
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentLookupResult(DocumentId));

        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysis"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse throttled"));

        var sut = CreateSut();
        Func<Task> act = () => sut.MirrorAsync(MakeObservation(), CancellationToken.None);

        // The mirror MUST swallow the exception (fire-and-forget contract)
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MirrorAsync_DataverseCreateThrowsOperationCanceled_DoesPropagate()
    {
        // OperationCanceledException is special: it must propagate so the orchestrator's
        // outer cancellation respects the contract. The mirror's failure-swallow logic
        // explicitly re-throws OCE.
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentLookupResult(DocumentId));

        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysis"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = CreateSut();
        Func<Task> act = () => sut.MirrorAsync(MakeObservation(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
