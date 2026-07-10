using System.Net;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// Service tests for <see cref="MemoryItemStore"/> (task AIR2-050, FR-B-01) — the generalization
/// of the retired matter-only <c>MatterMemoryService</c>. Cosmos interactions are verified against
/// a mocked <see cref="Container"/> (same pattern as the retired suite + PinnedContextRepositoryTests);
/// the reused prompt-fragment logic is tested via the internal static helpers, ported from the
/// legacy suite so the verbatim-reuse claim is pinned by the SAME assertions.
///
/// Acceptance-criteria coverage (task 050):
/// (a) Record memory persists + reads by GENERIC (entityType, entityId) — proven for project + invoice.
/// (b) User memory persists + reads by userId as ONE general per-user store.
/// (c) Partition key is the SUBJECT (entityId/userId) — never tenantId.
/// (d) ETag optimistic concurrency preserved (IfMatchEtag threaded; 412 propagates).
/// (e) GDPR erasure preserved (partition-wide hard delete; idempotent).
/// (f) Upsert-by-(Type,Key) supersession — deterministic id; repeated capture REPLACES, preserving creation stamps.
/// (g) Prompt fragment reuse — grouping/filtering/budget identical; matter heading byte-identical, others generalized.
/// (h) NEGATIVE: a record fact that mirrors a Dataverse field is rejected (documented-as-disallowed guard).
/// (i) Consumable shape is MemoryItem v1 (mapping round-trip preserves the governance envelope).
/// </summary>
public class MemoryItemStoreTests
{
    private const string TenantId = "tenant-test-001";
    private const string DatabaseName = "spaarke-ai";
    private const string ProjectId = "3f6a4bc2-0001-4c4e-9d2a-aaaaaaaaaaaa";
    private const string InvoiceId = "3f6a4bc2-0002-4c4e-9d2a-bbbbbbbbbbbb";
    private const string UserId = "9d81c0de-0003-4c4e-9d2a-cccccccccccc"; // Dataverse systemuserid (canonical)

    // =========================================================================
    // Helpers
    // =========================================================================

    private static (MemoryItemStore Store, Mock<Container> ContainerMock) CreateSut()
    {
        var containerMock = new Mock<Container>();
        var clientMock = new Mock<CosmosClient>();
        clientMock
            .Setup(c => c.GetContainer(DatabaseName, "memory-items"))
            .Returns(containerMock.Object);

        var loggerMock = new Mock<ILogger<MemoryItemStore>>();
        var sut = new MemoryItemStore(clientMock.Object, DatabaseName, loggerMock.Object);
        return (sut, containerMock);
    }

    private static MemoryItem BuildRecordItem(
        string subjectType,
        string subjectId,
        string key = "Trial Strategy",
        string value = "Bifurcate liability and damages",
        MemoryFactType type = MemoryFactType.KeyFact) => new()
        {
            Version = MemoryItemContract.SchemaVersion,
            Scope = MemoryScope.Record,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Fact = new MemoryFact { Type = type, Key = key, Value = value, ConfirmedByUser = true },
            Source = MemoryOrigin.AiDerived,
            BindingId = "BND-001",
            LedgerRef = "BND-001@t3",
            SessionId = "session-1",
            TurnId = 3,
            TrustLevel = "session-derived",
            DeletionPolicy = "user-erasable",
            RetentionClass = "tier-3-user-owned",
        };

    private static MemoryItem BuildUserItem(
        string userId,
        string key = "Drafting Style",
        string value = "Plain-English, short sentences") => new()
        {
            Version = MemoryItemContract.SchemaVersion,
            Scope = MemoryScope.User,
            UserId = userId,
            Fact = new MemoryFact { Type = MemoryFactType.KeyFact, Key = key, Value = value, ConfirmedByUser = true },
            Source = MemoryOrigin.User,
        };

    private static CosmosException NotFoundCosmosException()
        => new("Not Found", HttpStatusCode.NotFound, 0, string.Empty, 0);

    private static CosmosException PreconditionFailedCosmosException()
        => new("Precondition Failed", HttpStatusCode.PreconditionFailed, 0, string.Empty, 0);

    private static Mock<ItemResponse<MemoryItemDocument>> MockReadResponse(MemoryItemDocument document, string etag = "\"etag-v1\"")
    {
        var responseMock = new Mock<ItemResponse<MemoryItemDocument>>();
        responseMock.Setup(r => r.Resource).Returns(document);
        responseMock.Setup(r => r.ETag).Returns(etag);
        return responseMock;
    }

    private static Mock<ItemResponse<MemoryItemDocument>> MockWriteResponse() => new();

    private static Mock<FeedIterator<T>> MockFeedIterator<T>(IEnumerable<T> items)
    {
        var iterator = new Mock<FeedIterator<T>>();
        var calls = 0;
        iterator.SetupGet(i => i.HasMoreResults).Returns(() => calls == 0);

        var feedResponse = new Mock<FeedResponse<T>>();
        feedResponse.Setup(r => r.GetEnumerator()).Returns(items.GetEnumerator());

        iterator
            .Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(feedResponse.Object)
            .Callback(() => calls++);

        return iterator;
    }

    /// <summary>Arranges a not-found point-read (create path) + captures the upserted document and options.</summary>
    private static (Func<MemoryItemDocument?> Doc, Func<ItemRequestOptions?> Options, Func<PartitionKey?> Partition) ArrangeCreatePath(Mock<Container> containerMock)
    {
        containerMock
            .Setup(c => c.ReadItemAsync<MemoryItemDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFoundCosmosException());

        MemoryItemDocument? captured = null;
        ItemRequestOptions? capturedOptions = null;
        PartitionKey? capturedPartition = null;
        containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<MemoryItemDocument>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<MemoryItemDocument, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (doc, pk, options, _) => { captured = doc; capturedPartition = pk; capturedOptions = options; })
            .ReturnsAsync(MockWriteResponse().Object);

        return (() => captured, () => capturedOptions, () => capturedPartition);
    }

    // =========================================================================
    // (a) Record scope — GENERIC (entityType, entityId) keying, proven non-matter
    // =========================================================================

    [Theory]
    [InlineData("project", ProjectId)]
    [InlineData("invoice", InvoiceId)]
    public async Task UpsertAsync_RecordScope_PersistsByGenericSubjectKeying(string subjectType, string subjectId)
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        var (doc, _, partition) = ArrangeCreatePath(containerMock);
        var item = BuildRecordItem(subjectType, subjectId);

        // Act
        var persisted = await sut.UpsertAsync(item, TenantId);

        // Assert — generic keying persisted; partition is the SUBJECT (criterion c)
        doc().Should().NotBeNull();
        doc()!.Scope.Should().Be(MemoryScope.Record);
        doc()!.SubjectType.Should().Be(subjectType);
        doc()!.SubjectId.Should().Be(subjectId);
        doc()!.TenantId.Should().Be(TenantId, "tenantId is plain metadata on the doc — not the partition");
        partition().Should().Be(new PartitionKey(subjectId));
        persisted.SubjectId.Should().Be(subjectId);
        persisted.Version.Should().Be(MemoryItemContract.SchemaVersion);
    }

    [Fact]
    public async Task GetForRecordAsync_ReturnsMemoryItemV1Shapes_ForSubject()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        var stored = MemoryItemDocument.FromItem(
            BuildRecordItem("project", ProjectId, key: "Scope Risk", value: "Fixed-fee overrun risk on phase 2"),
            id: MemoryItemStore.BuildItemId(MemoryScope.Record, MemoryFactType.KeyFact, "Scope Risk"),
            subjectKey: ProjectId,
            tenantId: TenantId);

        QueryRequestOptions? capturedOptions = null;
        containerMock
            .Setup(c => c.GetItemQueryIterator<MemoryItemDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Callback<QueryDefinition, string, QueryRequestOptions>((_, _, options) => capturedOptions = options)
            .Returns(MockFeedIterator(new[] { stored }).Object);

        // Act
        var items = await sut.GetForRecordAsync("project", ProjectId);

        // Assert — MemoryItem v1 is the consumable shape (ADR-013); read is partition-scoped to the subject
        items.Should().HaveCount(1);
        items[0].Scope.Should().Be(MemoryScope.Record);
        items[0].SubjectType.Should().Be("project");
        items[0].SubjectId.Should().Be(ProjectId);
        items[0].Fact.Value.Should().Be("Fixed-fee overrun risk on phase 2");
        items[0].LedgerRef.Should().Be("BND-001@t3", "the provenance envelope survives the storage round-trip");
        capturedOptions!.PartitionKey.Should().Be(new PartitionKey(ProjectId));
    }

    // =========================================================================
    // (b) User scope — ONE general per-user store keyed by userId
    // =========================================================================

    [Fact]
    public async Task UpsertAsync_UserScope_PersistsByUserIdAsPartition()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        var (doc, _, partition) = ArrangeCreatePath(containerMock);

        // Act
        await sut.UpsertAsync(BuildUserItem(UserId), TenantId);

        // Assert — user memory partitions by userId (the subject), not per-matter, not tenant
        doc()!.Scope.Should().Be(MemoryScope.User);
        doc()!.UserId.Should().Be(UserId);
        doc()!.SubjectId.Should().Be(UserId, "the userId IS the subject partition value for User scope");
        doc()!.SubjectType.Should().BeNull("user memory is general per-user, not per-record");
        partition().Should().Be(new PartitionKey(UserId));
    }

    [Fact]
    public async Task GetForUserAsync_ReadsSingleUserPartition()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        var stored = MemoryItemDocument.FromItem(
            BuildUserItem(UserId),
            id: MemoryItemStore.BuildItemId(MemoryScope.User, MemoryFactType.KeyFact, "Drafting Style"),
            subjectKey: UserId,
            tenantId: TenantId);

        QueryRequestOptions? capturedOptions = null;
        containerMock
            .Setup(c => c.GetItemQueryIterator<MemoryItemDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Callback<QueryDefinition, string, QueryRequestOptions>((_, _, options) => capturedOptions = options)
            .Returns(MockFeedIterator(new[] { stored }).Object);

        // Act
        var items = await sut.GetForUserAsync(UserId);

        // Assert
        items.Should().HaveCount(1);
        items[0].Scope.Should().Be(MemoryScope.User);
        items[0].UserId.Should().Be(UserId);
        capturedOptions!.PartitionKey.Should().Be(new PartitionKey(UserId));
    }

    // =========================================================================
    // (c) Partition discipline — subject, NEVER tenant
    // =========================================================================

    [Fact]
    public async Task UpsertAsync_NeverPartitionsByTenantId()
    {
        // Arrange — tenant and subject deliberately different values
        var (sut, containerMock) = CreateSut();
        var (_, _, partition) = ArrangeCreatePath(containerMock);

        // Act
        await sut.UpsertAsync(BuildRecordItem("matter", "matter-guid-42"), "tenant-guid-99");

        // Assert — FR-B-01 MUST rule: the partition is the subject, never the tenant
        partition().Should().Be(new PartitionKey("matter-guid-42"));
        partition().Should().NotBe(new PartitionKey("tenant-guid-99"));
    }

    // =========================================================================
    // (d) ETag optimistic concurrency (legacy behavior preserved)
    // =========================================================================

    [Fact]
    public async Task UpsertAsync_ThreadsETag_WhenSupersedingExistingFact()
    {
        // Arrange — existing doc for the same (scope, Type, Key)
        var (sut, containerMock) = CreateSut();
        var key = "Trial Strategy";
        var id = MemoryItemStore.BuildItemId(MemoryScope.Record, MemoryFactType.KeyFact, key);
        var created = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        var existing = MemoryItemDocument.FromItem(
            BuildRecordItem("matter", "matter-1", key: key, value: "OLD conclusion") with { CreatedAt = created, CreatedBy = "creator-user" },
            id, "matter-1", TenantId);

        containerMock
            .Setup(c => c.ReadItemAsync<MemoryItemDocument>(
                id, new PartitionKey("matter-1"), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockReadResponse(existing, "\"etag-v5\"").Object);

        MemoryItemDocument? captured = null;
        ItemRequestOptions? capturedOptions = null;
        containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<MemoryItemDocument>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<MemoryItemDocument, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (doc, _, options, _) => { captured = doc; capturedOptions = options; })
            .ReturnsAsync(MockWriteResponse().Object);

        // Act — repeated capture for the same key with a NEW value
        var updated = await sut.UpsertAsync(
            BuildRecordItem("matter", "matter-1", key: key, value: "NEW superseding conclusion"), TenantId);

        // Assert — ETag threaded (concurrent writer between read and write → 412, legacy contract)
        capturedOptions!.IfMatchEtag.Should().Be("\"etag-v5\"");

        // Supersession semantics (criterion f): same id; value replaced; creation stamps preserved; update stamped
        captured!.Id.Should().Be(id);
        captured.Fact.Value.Should().Be("NEW superseding conclusion");
        captured.CreatedAt.Should().Be(created);
        captured.CreatedBy.Should().Be("creator-user");
        captured.UpdatedAt.Should().NotBeNull();
        updated.Fact.Value.Should().Be("NEW superseding conclusion");
    }

    [Fact]
    public async Task UpsertAsync_Propagates412_WhenConcurrentWriterWins()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        var item = BuildRecordItem("matter", "matter-1");
        var existing = MemoryItemDocument.FromItem(
            item,
            MemoryItemStore.BuildItemId(MemoryScope.Record, item.Fact.Type, item.Fact.Key),
            "matter-1", TenantId);

        containerMock
            .Setup(c => c.ReadItemAsync<MemoryItemDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockReadResponse(existing).Object);
        containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<MemoryItemDocument>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(PreconditionFailedCosmosException());

        // Act + Assert — callers retry with a fresh read (legacy contract preserved)
        var act = () => sut.UpsertAsync(item, TenantId);
        (await act.Should().ThrowAsync<CosmosException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    // =========================================================================
    // (e) GDPR erasure — partition-wide hard delete, idempotent
    // =========================================================================

    [Fact]
    public async Task EraseSubjectAsync_DeletesEveryItemInSubjectPartition()
    {
        // Arrange — two facts live in the subject's partition
        var (sut, containerMock) = CreateSut();
        var docA = MemoryItemDocument.FromItem(
            BuildRecordItem("project", ProjectId, key: "Scope Risk"),
            "mem_record_3_aaaa", ProjectId, TenantId);
        var docB = MemoryItemDocument.FromItem(
            BuildRecordItem("project", ProjectId, key: "Budget Posture", type: MemoryFactType.PriorAnalysis),
            "mem_record_2_bbbb", ProjectId, TenantId);

        containerMock
            .Setup(c => c.GetItemQueryIterator<MemoryItemDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(MockFeedIterator(new[] { docA, docB }).Object);

        var deletedIds = new List<string>();
        containerMock
            .Setup(c => c.DeleteItemAsync<MemoryItemDocument>(
                It.IsAny<string>(), new PartitionKey(ProjectId), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, PartitionKey, ItemRequestOptions?, CancellationToken>((id, _, _, _) => deletedIds.Add(id))
            .ReturnsAsync(MockWriteResponse().Object);

        // Act
        await sut.EraseSubjectAsync(ProjectId);

        // Assert — every doc in the subject partition hard-deleted (Art. 17); audit container untouched by construction
        deletedIds.Should().BeEquivalentTo(new[] { "mem_record_3_aaaa", "mem_record_2_bbbb" });
    }

    [Fact]
    public async Task DeleteItemAsync_IsIdempotent_WhenItemNotFound()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        containerMock
            .Setup(c => c.DeleteItemAsync<MemoryItemDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFoundCosmosException());

        // Act + Assert — 404 swallowed (review/delete surface calls this; deleting twice is fine)
        var act = () => sut.DeleteItemAsync(ProjectId, "mem_record_3_aaaa");
        await act.Should().NotThrowAsync();
    }

    // =========================================================================
    // (f) Deterministic id — upsert-by-(Type,Key) supersession
    // =========================================================================

    [Fact]
    public void BuildItemId_IsDeterministic_OverScopeTypeAndNormalizedKey()
    {
        var a = MemoryItemStore.BuildItemId(MemoryScope.Record, MemoryFactType.KeyFact, "Trial Strategy");
        var b = MemoryItemStore.BuildItemId(MemoryScope.Record, MemoryFactType.KeyFact, "  trial strategy ");
        var differentKey = MemoryItemStore.BuildItemId(MemoryScope.Record, MemoryFactType.KeyFact, "Damages Model");
        var differentType = MemoryItemStore.BuildItemId(MemoryScope.Record, MemoryFactType.KeyDate, "Trial Strategy");
        var differentScope = MemoryItemStore.BuildItemId(MemoryScope.User, MemoryFactType.KeyFact, "Trial Strategy");

        a.Should().Be(b, "the id normalizes case + whitespace so a repeated capture supersedes rather than duplicates");
        a.Should().NotBe(differentKey);
        a.Should().NotBe(differentType);
        a.Should().NotBe(differentScope);
        a.Should().StartWith("mem_record_3_");
    }

    // =========================================================================
    // (g) Prompt fragment — reused logic pinned by the legacy suite's assertions
    // =========================================================================

    [Fact]
    public void BuildPromptFragment_TypicalPayload_IsWithinTokenBudget()
    {
        var facts = new List<MemoryFact>
        {
            new() { Type = MemoryFactType.Party, Key = "Plaintiff", Value = "Company X", ConfirmedByUser = true },
            new() { Type = MemoryFactType.Party, Key = "Defendant", Value = "Company Y", ConfirmedByUser = true },
            new() { Type = MemoryFactType.Party, Key = "Plaintiff Counsel", Value = "Smith & Partners LLP", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyDate, Key = "Markman Hearing", Value = "July 15, 2026", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyDate, Key = "Trial Start", Value = "January 12, 2027", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyDate, Key = "Summary Judgment Deadline", Value = "October 1, 2026", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyDate, Key = "Fact Discovery Close", Value = "August 31, 2026", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyDate, Key = "Expert Disclosure", Value = "September 15, 2026", ConfirmedByUser = true },
            new() { Type = MemoryFactType.PriorAnalysis, Key = "Session 2026-05-12", Value = "3 weak claims identified; claim 4 upheld", ConfirmedByUser = true },
            new() { Type = MemoryFactType.PriorAnalysis, Key = "Session 2026-05-14", Value = "Prior art search complete; 2 references found", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyFact, Key = "Contract Value", Value = "$2.4M", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyFact, Key = "Contract Term", Value = "3 years", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyFact, Key = "Governing Law", Value = "California", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyFact, Key = "Jurisdiction", Value = "NDCA", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyFact, Key = "Patent Count", Value = "4 asserted patents", ConfirmedByUser = true },
        };

        var fragment = MemoryItemStore.BuildPromptFragment(facts, "matter");

        fragment.Should().NotBeEmpty();
        fragment.Should().Contain("### Matter Context (from prior sessions)",
            because: "the matter heading stays BYTE-IDENTICAL to the pre-generalization fragment");
        fragment.Should().Contain("**Parties**:");
        fragment.Should().Contain("**Key Dates**:");
        fragment.Should().Contain("**Prior Analyses**:");
        fragment.Should().Contain("**Key Facts**:");

        var estimatedTokens = fragment.Length / 1.3;
        estimatedTokens.Should().BeGreaterThan(200);
        estimatedTokens.Should().BeLessThanOrEqualTo(500);
    }

    [Fact]
    public void BuildPromptFragment_GroupsFactsByType_UnderCorrectHeadings()
    {
        var facts = new List<MemoryFact>
        {
            new() { Type = MemoryFactType.Party, Key = "Plaintiff", Value = "Acme", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyDate, Key = "Hearing", Value = "June 1", ConfirmedByUser = true },
            new() { Type = MemoryFactType.PriorAnalysis, Key = "Session A", Value = "2 claims weak", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyFact, Key = "Jurisdiction", Value = "SDNY", ConfirmedByUser = true },
        };

        var fragment = MemoryItemStore.BuildPromptFragment(facts, "matter");

        var partiesIndex = fragment.IndexOf("**Parties**:", StringComparison.Ordinal);
        var datesIndex = fragment.IndexOf("**Key Dates**:", StringComparison.Ordinal);
        var analysesIndex = fragment.IndexOf("**Prior Analyses**:", StringComparison.Ordinal);
        var factsIndex = fragment.IndexOf("**Key Facts**:", StringComparison.Ordinal);

        partiesIndex.Should().BeGreaterThan(-1);
        datesIndex.Should().BeGreaterThan(partiesIndex);
        analysesIndex.Should().BeGreaterThan(datesIndex);
        factsIndex.Should().BeGreaterThan(analysesIndex);

        fragment.Should().Contain("Plaintiff — Acme");
        fragment.Should().Contain("Hearing — June 1");
        fragment.Should().Contain("Session A — 2 claims weak");
        fragment.Should().Contain("Jurisdiction — SDNY");
    }

    [Fact]
    public void BuildPromptFragment_ExcludesLowConfidenceUnconfirmedFacts()
    {
        var facts = new List<MemoryFact>
        {
            new() { Type = MemoryFactType.KeyFact, Key = "Contract Value", Value = "$2.4M", ConfirmedByUser = false, Confidence = 0.9 },
            new() { Type = MemoryFactType.KeyFact, Key = "Secret Fact", Value = "should not appear", ConfirmedByUser = false, Confidence = 0.5 },
            new() { Type = MemoryFactType.Party, Key = "Plaintiff", Value = "Company X", ConfirmedByUser = true, Confidence = 0.3 },
        };

        var fragment = MemoryItemStore.BuildPromptFragment(facts, "matter");

        fragment.Should().Contain("Contract Value — $2.4M");
        fragment.Should().Contain("Plaintiff — Company X");
        fragment.Should().NotContain("should not appear");
    }

    [Fact]
    public void BuildPromptFragment_ReturnsEmpty_WhenAllFactsBelowThreshold()
    {
        var facts = new List<MemoryFact>
        {
            new() { Type = MemoryFactType.KeyFact, Key = "Guess", Value = "not sure", ConfirmedByUser = false, Confidence = 0.2 },
        };

        MemoryItemStore.BuildPromptFragment(facts, "matter").Should().BeEmpty();
    }

    [Fact]
    public void BuildFragmentHeading_GeneralizesForNonMatterSubjects()
    {
        MemoryItemStore.BuildFragmentHeading("matter").Should().Be("### Matter Context (from prior sessions)");
        MemoryItemStore.BuildFragmentHeading("project").Should().Be("### Project Context (from prior sessions)");
        MemoryItemStore.BuildFragmentHeading("invoice").Should().Be("### Invoice Context (from prior sessions)");
    }

    // =========================================================================
    // (h) NEGATIVE — record memory is not a Dataverse-field mirror (FR-B-01)
    // =========================================================================

    [Theory]
    [InlineData("sprk_matternumber")]
    [InlineData("new_customfield")]
    [InlineData("msdyn_slaid")]
    [InlineData(" SPRK_BudgetAmount ")]
    [InlineData("_sprk_matterid_value@odata.bind")]
    public async Task UpsertAsync_RejectsRecordFact_ThatMirrorsADataverseField(string mirroredKey)
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        ArrangeCreatePath(containerMock);
        var item = BuildRecordItem("matter", "matter-1", key: mirroredKey, value: "whatever the column holds");

        // Act + Assert — record memory holds DERIVED knowledge; the Binder reads live fields (FR-B-04)
        var act = () => sut.UpsertAsync(item, TenantId);
        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("derived/synthesized knowledge");

        containerMock.Verify(
            c => c.UpsertItemAsync(It.IsAny<MemoryItemDocument>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a mirrored field must never reach the store");
    }

    [Fact]
    public async Task UpsertAsync_AcceptsDerivedKnowledgeKeys()
    {
        // Arrange — human/derived keys must NOT be caught by the mirror guard
        var (sut, containerMock) = CreateSut();
        var (doc, _, _) = ArrangeCreatePath(containerMock);

        // Act
        await sut.UpsertAsync(BuildRecordItem("matter", "matter-1", key: "Settlement Posture", value: "Opposing counsel signaled openness below $1M"), TenantId);

        // Assert
        doc().Should().NotBeNull();
        doc()!.Fact.Key.Should().Be("Settlement Posture");
    }

    [Fact]
    public async Task UpsertAsync_RejectsConversationScope()
    {
        // Arrange — conversation memory is the ADR-040 ledger facade, never a MemoryItem store
        var (sut, _) = CreateSut();
        var item = BuildUserItem(UserId) with { Scope = MemoryScope.Conversation };

        // Act + Assert
        var act = () => sut.UpsertAsync(item, TenantId);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // =========================================================================
    // (i) Document ↔ MemoryItem v1 mapping — envelope survives round-trip
    // =========================================================================

    [Fact]
    public void MemoryItemDocument_RoundTrip_PreservesGovernanceEnvelope()
    {
        var item = BuildRecordItem("workassignment", "wa-77", key: "Staffing Constraint", value: "Associate rotation ends Q3") with
        {
            Sensitivity = "confidential",
            Expiration = DateTimeOffset.Parse("2027-01-01T00:00:00Z"),
        };

        var doc = MemoryItemDocument.FromItem(item, "mem_record_3_abc", "wa-77", TenantId);
        var roundTripped = doc.ToItem();

        roundTripped.Scope.Should().Be(MemoryScope.Record);
        roundTripped.SubjectType.Should().Be("workassignment");
        roundTripped.SubjectId.Should().Be("wa-77");
        roundTripped.Fact.Key.Should().Be("Staffing Constraint");
        roundTripped.Source.Should().Be(MemoryOrigin.AiDerived);
        roundTripped.BindingId.Should().Be("BND-001");
        roundTripped.LedgerRef.Should().Be("BND-001@t3");
        roundTripped.SessionId.Should().Be("session-1");
        roundTripped.TurnId.Should().Be(3);
        roundTripped.TrustLevel.Should().Be("session-derived");
        roundTripped.Sensitivity.Should().Be("confidential");
        roundTripped.Expiration.Should().Be(DateTimeOffset.Parse("2027-01-01T00:00:00Z"));
        roundTripped.DeletionPolicy.Should().Be("user-erasable");
        roundTripped.RetentionClass.Should().Be("tier-3-user-owned");
        roundTripped.Version.Should().Be(MemoryItemContract.SchemaVersion);
    }

    // =========================================================================
    // (k) Subject-key normalization at the store chokepoint (consolidation fix 2026-07-10):
    // cross-caller casing/brace variance must land on ONE canonical partition + subjectType,
    // or capture→recall silently misses (LLM-supplied subjectType; Dataverse "{UPPERCASE}" GUIDs).
    // =========================================================================

    [Fact]
    public async Task UpsertAsync_MixedCaseSubjectTypeAndBracedUppercaseGuid_PersistsCanonicalKeys()
    {
        // Arrange — the shapes real callers send: LLM-cased subjectType + Dataverse-braced GUID
        var (sut, containerMock) = CreateSut();
        var (doc, _, partition) = ArrangeCreatePath(containerMock);
        var item = BuildRecordItem("Matter", "{" + ProjectId.ToUpperInvariant() + "}");

        // Act
        var persisted = await sut.UpsertAsync(item, TenantId);

        // Assert — one canonical form everywhere: document keys, partition, returned item
        doc()!.SubjectType.Should().Be("matter");
        doc()!.SubjectId.Should().Be(ProjectId);
        partition().Should().Be(new PartitionKey(ProjectId));
        persisted.SubjectType.Should().Be("matter");
        persisted.SubjectId.Should().Be(ProjectId);
    }

    [Fact]
    public async Task GetForRecordAsync_MixedCaseSubjectTypeAndBracedUppercaseGuid_QueriesCanonicalKeys()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        QueryDefinition? capturedQuery = null;
        QueryRequestOptions? capturedOptions = null;
        containerMock
            .Setup(c => c.GetItemQueryIterator<MemoryItemDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Callback<QueryDefinition, string, QueryRequestOptions>((q, _, options) => { capturedQuery = q; capturedOptions = options; })
            .Returns(MockFeedIterator(Array.Empty<MemoryItemDocument>()).Object);

        // Act — a reader using host-context casing must hit the same partition + subjectType a writer used
        await sut.GetForRecordAsync("MATTER", "{" + ProjectId.ToUpperInvariant() + "}");

        // Assert
        capturedOptions!.PartitionKey.Should().Be(new PartitionKey(ProjectId));
        capturedQuery!.GetQueryParameters()
            .Should().Contain(p => p.Name == "@subjectType" && (string)p.Value! == "matter");
    }

    // =========================================================================
    // (j) NEGATIVE (task 051, FR-B-08) — TrustLevel is INERT at the store write path
    // =========================================================================

    [Fact]
    public async Task UpsertAsync_UntrustedTrustLevel_StillPersists_TrustLevelIsInert()
    {
        // Arrange — the WORST-CASE provenance value; enforcement is deferred to the governance
        // project (FR-B-08), so the store must carry it verbatim and never deny on it.
        var (sut, containerMock) = CreateSut();
        var (doc, _, _) = ArrangeCreatePath(containerMock);
        var item = BuildRecordItem("project", ProjectId) with { TrustLevel = "untrusted" };

        // Act
        var persisted = await sut.UpsertAsync(item, TenantId);

        // Assert — write proceeded; value carried unmodified into the document and back out
        doc().Should().NotBeNull("trustLevel participates in NO deny/reject path (inert field)");
        doc()!.TrustLevel.Should().Be("untrusted");
        persisted.TrustLevel.Should().Be("untrusted");
        containerMock.Verify(c => c.UpsertItemAsync(
            It.IsAny<MemoryItemDocument>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
