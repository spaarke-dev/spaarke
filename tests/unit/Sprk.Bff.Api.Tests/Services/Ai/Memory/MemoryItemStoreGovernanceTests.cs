using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Audit;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// Governance behavior added to <see cref="MemoryItemStore"/> by task AIR2-052 (FR-B-03):
/// (1) retention — <c>retentionClass</c> → per-item Cosmos <c>ttl</c> at WRITE time; and
/// (2) write audit — every memory write emits a Tier-2 event via the EXISTING
/// <see cref="IAuditLogService"/> carrying identifiers/counts ONLY (NFR-07). Cosmos interactions use
/// a mocked <see cref="Container"/> (same pattern as the task-050 suite).
/// </summary>
public class MemoryItemStoreGovernanceTests
{
    private const string TenantId = "tenant-test-001";
    private const string DatabaseName = "spaarke-ai";
    private const string ProjectId = "3f6a4bc2-0001-4c4e-9d2a-aaaaaaaaaaaa";
    private const string SecretValue = "SUPER SECRET privileged conclusion";

    private static (MemoryItemStore Store, Mock<Container> Container, Mock<IAuditLogService> Audit) CreateSut()
    {
        var containerMock = new Mock<Container>();
        var clientMock = new Mock<CosmosClient>();
        clientMock.Setup(c => c.GetContainer(DatabaseName, "memory-items")).Returns(containerMock.Object);

        var auditMock = new Mock<IAuditLogService>();
        var sut = new MemoryItemStore(clientMock.Object, DatabaseName, Mock.Of<ILogger<MemoryItemStore>>(), auditMock.Object);
        return (sut, containerMock, auditMock);
    }

    private static MemoryItem BuildRecordItem(string? retentionClass, string value = SecretValue) => new()
    {
        Version = MemoryItemContract.SchemaVersion,
        Scope = MemoryScope.Record,
        SubjectType = "project",
        SubjectId = ProjectId,
        Fact = new MemoryFact { Type = MemoryFactType.KeyFact, Key = "Settlement Posture", Value = value, ConfirmedByUser = true },
        Source = MemoryOrigin.AiDerived,
        CreatedBy = "creator-oid-1",
        RetentionClass = retentionClass,
    };

    private static Func<MemoryItemDocument?> ArrangeCreatePath(Mock<Container> containerMock)
    {
        containerMock
            .Setup(c => c.ReadItemAsync<MemoryItemDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not Found", HttpStatusCode.NotFound, 0, string.Empty, 0));

        MemoryItemDocument? captured = null;
        containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<MemoryItemDocument>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<MemoryItemDocument, PartitionKey?, ItemRequestOptions?, CancellationToken>((doc, _, _, _) => captured = doc)
            .ReturnsAsync(Mock.Of<ItemResponse<MemoryItemDocument>>());

        return () => captured;
    }

    // ── Retention: retentionClass → per-item ttl (asserted on the written document) ──

    [Theory]
    [InlineData("tier-3-user-owned", null)]
    [InlineData("ephemeral", 30 * 24 * 60 * 60)]
    [InlineData("unrecognized-class", null)]
    [InlineData(null, null)]
    public async Task UpsertAsync_MapsRetentionClassToPerItemTtl_AtWriteTime(string? retentionClass, int? expectedTtl)
    {
        // Arrange
        var (sut, containerMock, _) = CreateSut();
        var captured = ArrangeCreatePath(containerMock);

        // Act
        await sut.UpsertAsync(BuildRecordItem(retentionClass), TenantId);

        // Assert — Cosmos does the expiry; the value written here is the WHOLE retention mechanism.
        captured().Should().NotBeNull();
        captured()!.Ttl.Should().Be(expectedTtl);
    }

    // ── Write audit: identifiers/counts ONLY, never memory content (NFR-07) ──

    [Fact]
    public async Task UpsertAsync_EmitsWriteAudit_WithIdentifiersOnly_NeverMemoryContent()
    {
        // Arrange
        var (sut, containerMock, auditMock) = CreateSut();
        ArrangeCreatePath(containerMock);

        AuditEntry? captured = null;
        auditMock
            .Setup(a => a.LogInteractionAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEntry, CancellationToken>((e, _) => captured = e)
            .Returns(ValueTask.CompletedTask);

        var item = BuildRecordItem("tier-3-user-owned");
        var expectedId = MemoryItemStore.BuildItemId(MemoryScope.Record, item.Fact.Type, item.Fact.Key);

        // Act
        await sut.UpsertAsync(item, TenantId);

        // Assert — a memory-write audit event was emitted carrying IDENTIFIERS only
        captured.Should().NotBeNull();
        captured!.Action.Should().Be(MemoryAuditEvents.ActionWrite);
        captured.TenantId.Should().Be(TenantId);
        captured.UserId.Should().Be("creator-oid-1");
        captured.DocumentsAccessed.Should().ContainSingle().Which.Should().Be(expectedId);

        // NFR-07: the fact Key/Value NEVER reach the audit sink.
        JsonSerializer.Serialize(captured).Should().NotContain(SecretValue);
        JsonSerializer.Serialize(captured).Should().NotContain("Settlement Posture");
    }

    [Fact]
    public async Task UpsertAsync_Supersession_EmitsSupersedeAuditAction()
    {
        // Arrange — an existing doc for the same (scope, Type, Key) forces the supersession path.
        var (sut, containerMock, auditMock) = CreateSut();
        var item = BuildRecordItem("ephemeral");
        var id = MemoryItemStore.BuildItemId(MemoryScope.Record, item.Fact.Type, item.Fact.Key);
        var existing = MemoryItemDocument.FromItem(item, id, ProjectId, TenantId);

        var readResponse = new Mock<ItemResponse<MemoryItemDocument>>();
        readResponse.Setup(r => r.Resource).Returns(existing);
        readResponse.Setup(r => r.ETag).Returns("\"etag-v1\"");
        containerMock
            .Setup(c => c.ReadItemAsync<MemoryItemDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(readResponse.Object);
        containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<MemoryItemDocument>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<MemoryItemDocument>>());

        AuditEntry? captured = null;
        auditMock
            .Setup(a => a.LogInteractionAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEntry, CancellationToken>((e, _) => captured = e)
            .Returns(ValueTask.CompletedTask);

        // Act
        await sut.UpsertAsync(item, TenantId);

        // Assert
        captured.Should().NotBeNull();
        captured!.Action.Should().Be(MemoryAuditEvents.ActionSupersede);
    }

    [Fact]
    public async Task UpsertAsync_WithNoAuditLog_StillWrites_BackwardCompatible()
    {
        // Arrange — the task-050 construction (no audit log) must keep working.
        var containerMock = new Mock<Container>();
        var clientMock = new Mock<CosmosClient>();
        clientMock.Setup(c => c.GetContainer(DatabaseName, "memory-items")).Returns(containerMock.Object);
        var sut = new MemoryItemStore(clientMock.Object, DatabaseName, Mock.Of<ILogger<MemoryItemStore>>());
        var captured = ArrangeCreatePath(containerMock);

        // Act + Assert — no throw, document written
        var act = () => sut.UpsertAsync(BuildRecordItem("ephemeral"), TenantId);
        await act.Should().NotThrowAsync();
        captured().Should().NotBeNull();
    }
}
