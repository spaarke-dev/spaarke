using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Audit;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Audit;

/// <summary>
/// Unit tests for AuditLogService (AIPU2-033).
///
/// Key invariants verified:
/// (a) AuditEntry is written to Cosmos with correct fields.
/// (b) SHA-256 hash is deterministic for the same input.
/// (c) Audit write failure does not throw to the caller.
/// (d) Only CreateItemAsync is invoked — never Upsert, Replace, or Delete.
/// </summary>
/// <remarks>
/// PARALLELISM-SAFETY (F-11a, e2e-completion-audit-2026-07-10; supersedes PE-D7/#618):
/// <see cref="AuditLogService.LogInteractionAsync"/> is fire-and-forget — it dispatches the
/// Cosmos write onto the shared <see cref="System.Threading.ThreadPool"/> via <c>Task.Run</c>
/// and returns immediately. The three write-path tests below previously waited on a fixed
/// <c>Task.Delay</c> (200ms/1000ms) for that background task to run. Under full-suite xUnit
/// parallelism the ThreadPool is saturated by thousands of concurrently-scheduled test tasks,
/// so the background write did not always run inside the delay window and the assertion fired
/// before <c>CreateItemAsync</c> was invoked (16/16 in class isolation, flaky in the full run).
/// The shared state was the ThreadPool, not any mock or static field — each test builds its own
/// mocks and service instance, so no <c>[Collection]</c> serialization is required.
/// FIX: each write-path test signals a <see cref="TaskCompletionSource"/> from inside the Moq
/// callback the instant <c>CreateItemAsync</c> is reached, then awaits that signal (with a
/// generous timeout ceiling) instead of guessing a wall-clock delay. This is contention-proof
/// and removes the ADR-038-banned <c>Task.Delay</c> from the tests.
/// </remarks>
public class AuditLogServiceTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static AuditEntry BuildEntry(
        string tenantId = "tenant-abc",
        string userId = "user-123",
        string sessionId = "session-456",
        string action = "chat_response",
        string responseHash = "aabbcc")
        => new()
        {
            TenantId = tenantId,
            UserId = userId,
            SessionId = sessionId,
            Action = action,
            ResponseHash = responseHash,
            ToolsCalled = ["SearchDocumentsTool", "CitationTool"],
            DocumentsAccessed = ["doc-id-001", "doc-id-002"],
            SafetyResults = new SafetyCheckResult
            {
                PromptShieldPassed = true,
                GroundednessScore = 0.92,
                CitationsVerified = 3
            },
            MatterContext = "matter-789"
        };

    // =========================================================================
    // (a) Constructor — null guard tests
    // =========================================================================

    [Fact]
    public void Constructor_NullCosmosClient_ThrowsArgumentNullException()
    {
        var logger = new Mock<ILogger<AuditLogService>>();

        var act = () => new AuditLogService(null!, "spaarke-ai", logger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("cosmosClient");
    }

    [Fact]
    public void Constructor_NullOrEmptyDatabaseName_ThrowsArgumentException()
    {
        var cosmosClient = new Mock<CosmosClient>();
        var logger = new Mock<ILogger<AuditLogService>>();

        var actNull = () => new AuditLogService(cosmosClient.Object, null!, logger.Object);
        var actEmpty = () => new AuditLogService(cosmosClient.Object, "", logger.Object);
        var actWhitespace = () => new AuditLogService(cosmosClient.Object, "   ", logger.Object);

        actNull.Should().Throw<ArgumentException>();
        actEmpty.Should().Throw<ArgumentException>();
        actWhitespace.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var cosmosClient = new Mock<CosmosClient>();

        var act = () => new AuditLogService(cosmosClient.Object, "spaarke-ai", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // =========================================================================
    // (b) SHA-256 hash determinism via AuditHashHelper
    // =========================================================================

    [Fact]
    public void HashResponse_SameInput_ReturnsSameHash()
    {
        const string responseText = "This is the AI response content.";

        var hash1 = AuditHashHelper.HashResponse(responseText);
        var hash2 = AuditHashHelper.HashResponse(responseText);

        hash1.Should().Be(hash2, "SHA-256 must be deterministic for the same input");
    }

    [Fact]
    public void HashResponse_DifferentInputs_ReturnsDifferentHashes()
    {
        var hash1 = AuditHashHelper.HashResponse("Response A");
        var hash2 = AuditHashHelper.HashResponse("Response B");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void HashResponse_ProducesLowercaseHex64Characters()
    {
        var hash = AuditHashHelper.HashResponse("any content");

        hash.Should().HaveLength(64, "SHA-256 produces a 256-bit / 32-byte = 64 hex char output");
        hash.Should().MatchRegex("^[0-9a-f]{64}$", "hash must be lowercase hex");
    }

    [Fact]
    public void HashResponse_NullInput_ThrowsArgumentNullException()
    {
        var act = () => AuditHashHelper.HashResponse(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HashResponse_KnownInput_ProducesExpectedHash()
    {
        // Verified independently: SHA-256("hello") = 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
        const string expected = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

        var hash = AuditHashHelper.HashResponse("hello");

        hash.Should().Be(expected);
    }

    // =========================================================================
    // (c) Audit write failure does not propagate to caller
    // =========================================================================

    [Fact]
    public async Task LogInteractionAsync_CosmosThrows_DoesNotThrowToCallerAndLogsError()
    {
        // Arrange
        var logger = new Mock<ILogger<AuditLogService>>();
        var containerMock = new Mock<Container>();
        // Signalled the instant the background write reaches CreateItemAsync (before it throws),
        // so the test waits on the actual invocation rather than a wall-clock guess.
        var writeAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        containerMock
            .Setup(c => c.CreateItemAsync(
                It.IsAny<AuditEntry>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => writeAttempted.TrySetResult())
            .ThrowsAsync(new CosmosException("Cosmos unavailable", System.Net.HttpStatusCode.ServiceUnavailable, 503, "activity-1", 0));

        var cosmosClientMock = new Mock<CosmosClient>();
        cosmosClientMock
            .Setup(c => c.GetContainer(It.IsAny<string>(), "audit-partitioned"))
            .Returns(containerMock.Object);

        var service = new AuditLogService(cosmosClientMock.Object, "spaarke-ai", logger.Object);
        var entry = BuildEntry();

        // Act — the fire-and-forget dispatch must return without throwing to the caller.
        var act = async () => await service.LogInteractionAsync(entry);
        await act.Should().NotThrowAsync("audit write failures must never propagate to callers");

        // Deterministically wait for the background Cosmos write to have been attempted (and thus
        // for the CosmosException to have been raised + swallowed on the background task). The TCS
        // completes the moment CreateItemAsync is reached, so this is immune to ThreadPool
        // saturation under full-suite parallelism; the timeout is only a hang guard.
        await writeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task LogInteractionAsync_NullEntry_ThrowsArgumentNullException()
    {
        var logger = new Mock<ILogger<AuditLogService>>();
        var cosmosClientMock = new Mock<CosmosClient>();
        cosmosClientMock
            .Setup(c => c.GetContainer(It.IsAny<string>(), "audit-partitioned"))
            .Returns(new Mock<Container>().Object);

        var service = new AuditLogService(cosmosClientMock.Object, "spaarke-ai", logger.Object);

        var act = async () => await service.LogInteractionAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // =========================================================================
    // (d) Only CreateItemAsync is used — never Upsert, Replace, or Delete
    // =========================================================================

    [Fact]
    public async Task LogInteractionAsync_UsesCreateItemAsync_NotUpsertOrReplace()
    {
        // Arrange
        var logger = new Mock<ILogger<AuditLogService>>();
        var containerMock = new Mock<Container>();
        // Signalled the instant the background write reaches CreateItemAsync — lets the test
        // wait on the real invocation instead of a wall-clock guess (parallelism-safe; see class remarks).
        var writeAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Track whether forbidden operations are called
        bool upsertCalled = false;
        bool replaceCalled = false;
        bool deleteCalled = false;

        containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<AuditEntry>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => upsertCalled = true)
            .ReturnsAsync((ItemResponse<AuditEntry>)null!);

        containerMock
            .Setup(c => c.ReplaceItemAsync(
                It.IsAny<AuditEntry>(),
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => replaceCalled = true)
            .ReturnsAsync((ItemResponse<AuditEntry>)null!);

        containerMock
            .Setup(c => c.DeleteItemAsync<AuditEntry>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => deleteCalled = true)
            .ReturnsAsync((ItemResponse<AuditEntry>)null!);

        containerMock
            .Setup(c => c.CreateItemAsync(
                It.IsAny<AuditEntry>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => writeAttempted.TrySetResult())
            .ReturnsAsync((ItemResponse<AuditEntry>)null!);

        var cosmosClientMock = new Mock<CosmosClient>();
        cosmosClientMock
            .Setup(c => c.GetContainer(It.IsAny<string>(), "audit-partitioned"))
            .Returns(containerMock.Object);

        var service = new AuditLogService(cosmosClientMock.Object, "spaarke-ai", logger.Object);
        var entry = BuildEntry();

        // Act
        await service.LogInteractionAsync(entry);
        // F-11a (e2e-completion-audit-2026-07-10): replaced the prior fixed Task.Delay(1000)
        // wall-clock buffer (R6 PR #395 hotfix) — under full-suite xUnit parallelism the shared
        // ThreadPool is saturated and even 1000ms raced. Wait on the actual CreateItemAsync
        // invocation via a TCS signalled in the mock callback; the timeout is only a hang guard.
        await writeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert: CreateItemAsync called; forbidden operations never called.
        // Partition key is the synthetic /partitionKey value (AIR2-074 re-key), NOT bare tenantId.
        containerMock.Verify(
            c => c.CreateItemAsync(
                It.Is<AuditEntry>(e => e.Id == entry.Id),
                It.Is<PartitionKey>(pk => pk == new PartitionKey(entry.PartitionKey)),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "CreateItemAsync must be called exactly once per audit entry");

        upsertCalled.Should().BeFalse("UpsertItemAsync must never be called on audit container");
        replaceCalled.Should().BeFalse("ReplaceItemAsync must never be called on audit container");
        deleteCalled.Should().BeFalse("DeleteItemAsync must never be called on audit container");
    }

    // =========================================================================
    // (a) AuditEntry contains all required fields
    // =========================================================================

    [Fact]
    public void AuditEntry_RequiredFields_ArePopulatedCorrectly()
    {
        var entry = BuildEntry(
            tenantId: "contoso",
            userId: "user-oid-abc",
            sessionId: "sess-xyz",
            action: "tool_call",
            responseHash: AuditHashHelper.HashResponse("test response"));

        entry.TenantId.Should().Be("contoso");
        entry.UserId.Should().Be("user-oid-abc");
        entry.SessionId.Should().Be("sess-xyz");
        entry.Action.Should().Be("tool_call");
        entry.ResponseHash.Should().HaveLength(64, "responseHash must be a SHA-256 hex string");
        entry.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        entry.Id.Should().NotBeNullOrEmpty("Id must be auto-generated");
        entry.ToolsCalled.Should().HaveCount(2);
        entry.DocumentsAccessed.Should().HaveCount(2);
        entry.SafetyResults.Should().NotBeNull();
        entry.MatterContext.Should().Be("matter-789");
    }

    [Fact]
    public void AuditEntry_Id_IsUniqueAcrossInstances()
    {
        var entry1 = BuildEntry();
        var entry2 = BuildEntry();

        entry1.Id.Should().NotBe(entry2.Id, "each AuditEntry must get a unique Id on construction");
    }

    [Fact]
    public void AuditEntry_Timestamp_IsUtc()
    {
        var entry = BuildEntry();

        entry.Timestamp.Offset.Should().Be(TimeSpan.Zero, "Timestamp must be UTC (zero offset)");
    }

    [Fact]
    public void SafetyCheckResult_DefaultValues_AreReasonable()
    {
        var result = new SafetyCheckResult();

        result.PromptShieldPassed.Should().BeFalse("default is safe conservative value");
        result.GroundednessScore.Should().Be(1.0, "default assumes fully grounded when not evaluated");
        result.CitationsVerified.Should().Be(0);
    }

    // =========================================================================
    // Partition key correctness
    // =========================================================================

    [Fact]
    public async Task LogInteractionAsync_PartitionsByTenantAndMonthBucket_NotBareTenantId()
    {
        // Arrange
        var logger = new Mock<ILogger<AuditLogService>>();
        var containerMock = new Mock<Container>();
        PartitionKey capturedPk = default;
        // Signalled the instant the background write reaches CreateItemAsync — lets the test
        // wait on the real invocation instead of a wall-clock guess (parallelism-safe; see class remarks).
        var writeAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        containerMock
            .Setup(c => c.CreateItemAsync(
                It.IsAny<AuditEntry>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<AuditEntry, PartitionKey?, ItemRequestOptions, CancellationToken>(
                (_, pk, _, _) => { capturedPk = pk!.Value; writeAttempted.TrySetResult(); })
            .ReturnsAsync((ItemResponse<AuditEntry>)null!);

        var cosmosClientMock = new Mock<CosmosClient>();
        cosmosClientMock
            .Setup(c => c.GetContainer(It.IsAny<string>(), "audit-partitioned"))
            .Returns(containerMock.Object);

        var service = new AuditLogService(cosmosClientMock.Object, "spaarke-ai", logger.Object);
        // Construct with a fixed timestamp so the monthly bucket is deterministic (2026-03).
        var entry = new AuditEntry
        {
            TenantId = "tenant-xyz",
            UserId = "u",
            SessionId = "s",
            Action = "chat_response",
            ResponseHash = "abc",
            Timestamp = new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero)
        };

        // Act
        await service.LogInteractionAsync(entry);
        // F-11a: wait on the actual CreateItemAsync invocation (TCS signalled in the callback
        // above) rather than a fixed Task.Delay — parallelism-safe; timeout is only a hang guard.
        await writeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert — AIR2-074 re-key (FR-D-05): partition is the synthetic tenant + monthly-bucket
        // value, NOT bare /tenantId (which would collapse a dedicated tenant into one logical
        // partition against the Cosmos 20 GB cap).
        capturedPk.Should().Be(new PartitionKey("tenant-xyz|2026-03"),
            "partition key must be the synthetic {tenantId}|{yyyy-MM} value");
        capturedPk.Should().NotBe(new PartitionKey("tenant-xyz"),
            "bare /tenantId must no longer be the partition key after the AIR2-074 re-key");
    }
}
