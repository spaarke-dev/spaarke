using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="ChatDataverseRepository"/> — specifically the
/// ai-advanced-capabilities-analysis-hub-r1 task 020 session↔Analysis binding FK work.
///
/// Verifies:
/// - <see cref="ChatDataverseRepository.CreateSessionAsync"/> writes the <c>sprk_analysis</c>
///   lookup FK on the <c>sprk_aichatsummary</c> record for Analysis-owned sessions (HostContext
///   EntityType == "sprk_analysisoutput" + a parseable analysis GUID), alongside the existing
///   <c>sprk_sessionid</c> grouping key.
/// - Loose (non-Analysis) sessions write NO <c>sprk_analysis</c> FK — only <c>sprk_sessionid</c>.
/// - The write path never touches the dead <c>sprk_analysischatmessage</c> entity.
/// - <see cref="ChatDataverseRepository.GetSessionsByAnalysisAsync"/> filters
///   <c>sprk_aichatsummary</c> by the <c>sprk_analysis</c> FK and projects each row into an
///   <see cref="AnalysisSessionSummary"/> — "one Analysis → many sessions", grouped by
///   <c>sprk_sessionid</c>.
/// </summary>
public class ChatDataverseRepositoryTests
{
    private const string TenantId = "tenant-abc";

    private readonly Mock<IGenericEntityService> _entityServiceMock;
    private readonly Mock<ILogger<ChatDataverseRepository>> _loggerMock;
    private readonly ChatDataverseRepository _sut;

    public ChatDataverseRepositoryTests()
    {
        _entityServiceMock = new Mock<IGenericEntityService>();
        _loggerMock = new Mock<ILogger<ChatDataverseRepository>>();
        _sut = new ChatDataverseRepository(_entityServiceMock.Object, _loggerMock.Object);
    }

    private static ChatSession BuildSession(ChatHostContext? hostContext = null) =>
        new(
            SessionId: Guid.NewGuid().ToString("N"),
            TenantId: TenantId,
            DocumentId: "doc-001",
            PlaybookId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: [],
            HostContext: hostContext);

    // =========================================================================
    // CreateSessionAsync — sprk_analysis FK binding (task 020, spec FR-05)
    // =========================================================================

    [Fact]
    public async Task CreateSessionAsync_WithAnalysisOwnedHostContext_WritesAnalysisFkAlongsideSessionId()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var hostContext = new ChatHostContext(EntityType: "sprk_analysisoutput", EntityId: analysisId.ToString());
        var session = BuildSession(hostContext);

        Entity? captured = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _sut.CreateSessionAsync(session);

        // Assert
        captured.Should().NotBeNull();
        captured!.LogicalName.Should().Be("sprk_aichatsummary");
        captured["sprk_sessionid"].Should().Be(session.SessionId);
        captured.Contains("sprk_analysis").Should().BeTrue();
        var fk = (EntityReference)captured["sprk_analysis"];
        fk.LogicalName.Should().Be("sprk_analysis");
        fk.Id.Should().Be(analysisId);
    }

    [Fact]
    public async Task CreateSessionAsync_WithNoHostContext_WritesNoAnalysisFk()
    {
        // Arrange — loose session (no analysisId in HostContext)
        var session = BuildSession(hostContext: null);

        Entity? captured = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _sut.CreateSessionAsync(session);

        // Assert — record persists with only sprk_sessionid, no FK
        captured.Should().NotBeNull();
        captured!.Contains("sprk_analysis").Should().BeFalse();
        captured["sprk_sessionid"].Should().Be(session.SessionId);
    }

    [Fact]
    public async Task CreateSessionAsync_WithNonAnalysisHostContext_WritesNoAnalysisFk()
    {
        // Arrange — HostContext present but scoped to a different (non-Analysis) entity type,
        // e.g. a matter-embedded chat session. Must not be mistaken for an Analysis-owned session.
        var hostContext = new ChatHostContext(EntityType: "matter", EntityId: Guid.NewGuid().ToString());
        var session = BuildSession(hostContext);

        Entity? captured = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _sut.CreateSessionAsync(session);

        // Assert
        captured.Should().NotBeNull();
        captured!.Contains("sprk_analysis").Should().BeFalse();
    }

    [Fact]
    public async Task CreateSessionAsync_WithAnalysisHostContextButUnparsableEntityId_WritesNoAnalysisFk()
    {
        // Arrange — EntityType matches the Analysis convention but EntityId is not a GUID
        // (defensive: malformed HostContext must not throw or write a garbage FK).
        var hostContext = new ChatHostContext(EntityType: "sprk_analysisoutput", EntityId: "not-a-guid");
        var session = BuildSession(hostContext);

        Entity? captured = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _sut.CreateSessionAsync(session);

        // Assert
        captured.Should().NotBeNull();
        captured!.Contains("sprk_analysis").Should().BeFalse();
    }

    [Fact]
    public async Task CreateSessionAsync_NeverWritesTheDeadAnalysisChatMessageEntity()
    {
        // Arrange — Analysis-owned session (the shape most likely to tempt a dead-entity write)
        var hostContext = new ChatHostContext(EntityType: "sprk_analysisoutput", EntityId: Guid.NewGuid().ToString());
        var session = BuildSession(hostContext);

        var createdEntityNames = new List<string>();
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => createdEntityNames.Add(e.LogicalName))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _sut.CreateSessionAsync(session);

        // Assert — only the live sprk_aichatsummary entity is ever created; the dead
        // sprk_analysischatmessage entity is never touched (project constraint, §11 reuse).
        createdEntityNames.Should().ContainSingle().Which.Should().Be("sprk_aichatsummary");
        createdEntityNames.Should().NotContain("sprk_analysischatmessage");
    }

    // =========================================================================
    // GetSessionsByAnalysisAsync — "one Analysis → many sessions" (task 020, spec FR-05)
    // =========================================================================

    [Fact]
    public async Task GetSessionsByAnalysisAsync_FiltersOnAnalysisFk_AndReturnsSessionsGroupedBySessionId()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var sessionIdA = Guid.NewGuid().ToString("N");
        var sessionIdB = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        var entityA = new Entity("sprk_aichatsummary")
        {
            ["sprk_sessionid"] = sessionIdA,
            ["sprk_messagecount"] = 4,
            ["sprk_isarchived"] = false,
            ["createdon"] = now
        };
        var entityB = new Entity("sprk_aichatsummary")
        {
            ["sprk_sessionid"] = sessionIdB,
            ["sprk_messagecount"] = 12,
            ["sprk_isarchived"] = true,
            ["createdon"] = now.AddMinutes(-30)
        };

        QueryExpression? capturedQuery = null;
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => capturedQuery = q)
            .ReturnsAsync(new EntityCollection(new List<Entity> { entityA, entityB }));

        // Act
        var results = await _sut.GetSessionsByAnalysisAsync(TenantId, analysisId);

        // Assert — query shape: filters sprk_aichatsummary by the sprk_analysis FK,
        // tenant-scoped alongside it (ADR-014/ADR-028 — mirrors every sibling read method).
        capturedQuery.Should().NotBeNull();
        capturedQuery!.EntityName.Should().Be("sprk_aichatsummary");
        capturedQuery.Criteria.Conditions.Should().HaveCount(2);

        var analysisCondition = capturedQuery.Criteria.Conditions.Single(c => c.AttributeName == "sprk_analysis");
        analysisCondition.Operator.Should().Be(ConditionOperator.Equal);
        analysisCondition.Values.Should().ContainSingle().Which.Should().Be(analysisId);

        var tenantCondition = capturedQuery.Criteria.Conditions.Single(c => c.AttributeName == "sprk_tenantid");
        tenantCondition.Operator.Should().Be(ConditionOperator.Equal);
        tenantCondition.Values.Should().ContainSingle().Which.Should().Be(TenantId);

        // Assert — "one Analysis → many sessions": both sessions returned, each its own row
        // (grouping key sprk_sessionid — no further server-side grouping needed).
        results.Should().HaveCount(2);
        results.Select(r => r.SessionId).Should().BeEquivalentTo(new[] { sessionIdA, sessionIdB });

        var summaryA = results.Single(r => r.SessionId == sessionIdA);
        summaryA.MessageCount.Should().Be(4);
        summaryA.IsArchived.Should().BeFalse();

        var summaryB = results.Single(r => r.SessionId == sessionIdB);
        summaryB.MessageCount.Should().Be(12);
        summaryB.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task GetSessionsByAnalysisAsync_WhenNoSessionsBoundToAnalysis_ReturnsEmpty()
    {
        // Arrange
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>()));

        // Act
        var results = await _sut.GetSessionsByAnalysisAsync(TenantId, Guid.NewGuid());

        // Assert
        results.Should().BeEmpty();
    }

    // =========================================================================
    // ArchiveSessionAsync — durable sprk_isarchived flip (task 022, AIPL-054, spec FR-06)
    // Option B: resolve the sprk_aichatsummary GUID via a tenant-scoped sprk_sessionid query,
    // then sparse-update sprk_isarchived = true. Cosmos transcript is the store-of-record
    // (ADR-040 Path A) and is NEVER deleted here — this is a marker update only.
    // =========================================================================

    [Fact]
    public async Task ArchiveSessionAsync_WhenSummaryRecordExists_DurablyFlipsIsArchivedTenantScoped()
    {
        // Arrange — a persisted session summary row for this tenant.
        var sessionId = Guid.NewGuid().ToString("N");
        var recordId = Guid.NewGuid();
        var summaryRow = new Entity("sprk_aichatsummary", recordId)
        {
            ["sprk_isarchived"] = false
        };

        QueryExpression? capturedQuery = null;
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => capturedQuery = q)
            .ReturnsAsync(new EntityCollection(new List<Entity> { summaryRow }));

        string? updatedEntity = null;
        Guid updatedId = Guid.Empty;
        Dictionary<string, object>? updatedFields = null;
        _entityServiceMock
            .Setup(s => s.UpdateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((name, id, fields, _) =>
            {
                updatedEntity = name;
                updatedId = id;
                updatedFields = fields;
            })
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ArchiveSessionAsync(TenantId, sessionId);

        // Assert — GUID resolved via a tenant-scoped query on sprk_sessionid (ADR-014/ADR-028:
        // a cross-tenant sessionId guess must not flip another tenant's flag).
        capturedQuery.Should().NotBeNull();
        capturedQuery!.EntityName.Should().Be("sprk_aichatsummary");
        capturedQuery.Criteria.Conditions.Should().HaveCount(2);

        var sessionCondition = capturedQuery.Criteria.Conditions.Single(c => c.AttributeName == "sprk_sessionid");
        sessionCondition.Operator.Should().Be(ConditionOperator.Equal);
        sessionCondition.Values.Should().ContainSingle().Which.Should().Be(sessionId);

        var tenantCondition = capturedQuery.Criteria.Conditions.Single(c => c.AttributeName == "sprk_tenantid");
        tenantCondition.Operator.Should().Be(ConditionOperator.Equal);
        tenantCondition.Values.Should().ContainSingle().Which.Should().Be(TenantId);

        // Assert — sprk_isarchived durably flipped true on the resolved record (sparse update).
        updatedEntity.Should().Be("sprk_aichatsummary");
        updatedId.Should().Be(recordId);
        updatedFields.Should().NotBeNull();
        updatedFields!.Should().ContainKey("sprk_isarchived");
        updatedFields!["sprk_isarchived"].Should().Be(true);
    }

    [Fact]
    public async Task ArchiveSessionAsync_NeverDeletesTheCosmosTranscript_MarkerUpdateOnly()
    {
        // Arrange — ADR-040 Path A: archive is a Dataverse MARKER flip. The repository must never
        // issue a delete (the transcript store-of-record is Cosmos, owned by ChatSessionManager,
        // not this cold-tier repo). Guards against a regression to a hard-delete archive.
        var sessionId = Guid.NewGuid().ToString("N");
        var summaryRow = new Entity("sprk_aichatsummary", Guid.NewGuid()) { ["sprk_isarchived"] = false };

        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { summaryRow }));
        _entityServiceMock
            .Setup(s => s.UpdateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ArchiveSessionAsync(TenantId, sessionId);

        // Assert — no DeleteAsync ever issued from the archive path (transcript preserved).
        _entityServiceMock.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _entityServiceMock.Verify(
            s => s.UpdateAsync("sprk_aichatsummary", It.IsAny<Guid>(),
                It.Is<Dictionary<string, object>>(f => f.ContainsKey("sprk_isarchived") && (bool)f["sprk_isarchived"]),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ArchiveSessionAsync_WhenNoSummaryRow_DoesNotThrowAndDoesNotUpdate()
    {
        // Arrange — no cold-tier anchor row (CreateSessionAsync tolerates a Dataverse write failure
        // and continues Redis-only). Archive must be non-fatal: no throw, no blind update; the Cosmos
        // transcript remains the store-of-record and retrievable.
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>()));

        // Act
        var act = async () => await _sut.ArchiveSessionAsync(TenantId, Guid.NewGuid().ToString("N"));

        // Assert
        await act.Should().NotThrowAsync();
        _entityServiceMock.Verify(
            s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
