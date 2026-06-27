using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Jobs;

/// <summary>
/// Unit tests for RecordSyncJob — AIPU2-041.
///
/// Tests cover:
///   (a) Watermark is read on start and updated on success.
///   (b) Watermark is NOT updated on failure — next run re-syncs failed records.
///   (c) Documents are batched in groups of 50.
///   (d) 429 response triggers retry with backoff.
///   (e) Cancellation token is honoured — job stops cleanly mid-batch.
///   (f) Disabled kill-switch prevents any work.
///   (g) Missing configuration short-circuits cleanly.
///   (h) MapToSearchDocument produces correct field values.
/// </summary>
[Trait("status", "repaired")]
public class RecordSyncJobTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static RecordSyncOptions DefaultOptions() => new()
    {
        Enabled = true,
        IntervalMinutes = 30,
        AiSearchEndpoint = "https://test.search.windows.net",
        AiSearchApiKey = "test-key",
        DataverseEnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    };

    private static (RecordSyncJob job, Mock<IDistributedCache> cache, Mock<IHttpClientFactory> factory)
        CreateJob(RecordSyncOptions? options = null)
    {
        var cacheMock = new Mock<IDistributedCache>();
        var factoryMock = new Mock<IHttpClientFactory>();
        var loggerMock = new Mock<ILogger<RecordSyncJob>>();
        var opts = Options.Create(options ?? DefaultOptions());

        var configuration = new ConfigurationBuilder().Build();
        var job = new RecordSyncJob(
            cacheMock.Object,
            factoryMock.Object,
            configuration,
            opts,
            new DefaultAzureCredential(),
            loggerMock.Object);

        return (job, cacheMock, factoryMock);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (f) Disabled — no work done
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_DoesNotQueryDataverseOrSearch()
    {
        // Arrange
        var (job, cache, factory) = CreateJob(new RecordSyncOptions { Enabled = false });
        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(50); // let the background loop tick once
        await job.StopAsync(CancellationToken.None);

        // Assert
        cache.Verify(
            c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "Watermark should never be read when the job is disabled");

        factory.Verify(
            f => f.CreateClient(It.IsAny<string>()),
            Times.Never, "No HTTP clients should be created when the job is disabled");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (g) Missing endpoint — no crash, no work
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenAiSearchEndpointMissing_DoesNotStart()
    {
        // Arrange
        var opts = DefaultOptions();
        opts.AiSearchEndpoint = string.Empty;
        var (job, cache, _) = CreateJob(opts);
        using var cts = new CancellationTokenSource();

        // Act — should not throw
        await job.StartAsync(cts.Token);
        await Task.Delay(50);
        await job.StopAsync(CancellationToken.None);

        // Assert
        cache.Verify(
            c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (a) Watermark is read at the start of each entity sync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadWatermarkAsync_WhenCacheEmpty_ReturnsDataverseSafeMinWatermark()
    {
        // Arrange
        var (job, cache, _) = CreateJob();

        cache.Setup(c => c.GetAsync("recordsync:watermark:sprk_matter", It.IsAny<CancellationToken>()))
             .ReturnsAsync((byte[]?)null);

        // Act
        var watermark = await job.ReadWatermarkAsync("sprk_matter", CancellationToken.None);

        // Assert
        // Production returns 1900-01-01 (DataverseSafeMinWatermark), not DateTime.MinValue,
        // because Dataverse's CrmDateTime rejects year 0001 with error 0x80040239.
        // See RecordSyncJob.DataverseSafeMinWatermark for full rationale.
        var expected = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
        watermark.Should().Be(expected,
            "a missing watermark should default to the Dataverse-safe minimum (1900-01-01) so a full initial sync is triggered without violating CrmDateTime bounds");
    }

    [Fact]
    public async Task ReadWatermarkAsync_WhenCacheHasValue_ReturnsParsedDateTimeOffset()
    {
        // Arrange
        var (job, cache, _) = CreateJob();
        var stored = new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
        var bytes = Encoding.UTF8.GetBytes(stored.ToString("O"));

        cache.Setup(c => c.GetAsync("recordsync:watermark:sprk_matter", It.IsAny<CancellationToken>()))
             .ReturnsAsync(bytes);

        // Act
        var watermark = await job.ReadWatermarkAsync("sprk_matter", CancellationToken.None);

        // Assert
        watermark.Should().BeCloseTo(stored, TimeSpan.FromSeconds(1));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (a) Watermark is written after a successful sync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteWatermarkAsync_PersistsValueToCache()
    {
        // Arrange
        var (job, cache, _) = CreateJob();
        var watermark = new DateTimeOffset(2026, 5, 17, 9, 0, 0, TimeSpan.Zero);
        byte[]? captured = null;

        cache.Setup(c => c.SetAsync(
                "recordsync:watermark:sprk_matter",
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
             .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                 (_, data, _, _) => captured = data)
             .Returns(Task.CompletedTask);

        // Act
        await job.WriteWatermarkAsync("sprk_matter", watermark, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull("watermark bytes must be written to the cache");
        var written = DateTimeOffset.Parse(Encoding.UTF8.GetString(captured!));
        written.Should().BeCloseTo(watermark, TimeSpan.FromSeconds(1));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (b) Watermark NOT advanced when AI Search upload fails
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncEntityAsync_WhenUploadThrows_WatermarkIsNotAdvanced()
    {
        // Arrange
        var (job, cache, _) = CreateJob();

        // Watermark returns epoch
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((byte[]?)null);

        // Create a testable subclass that stubs out the I/O methods.
        var testJob = new TestableRecordSyncJob(
            cache.Object,
            Mock.Of<IHttpClientFactory>(),
            Options.Create(DefaultOptions()),
            Mock.Of<ILogger<RecordSyncJob>>());

        testJob.DataverseRecordsToReturn = BuildFakeDataverseRecords(1);
        testJob.UploadShouldThrow = true; // upload always fails

        // Act
        Func<Task> act = async () =>
            await testJob.SyncEntityAsync(EntityConfigs[0], CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>("upload failure should propagate");

        cache.Verify(
            c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "watermark MUST NOT be advanced when the upload fails");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (c) Documents are batched in groups of 50
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncEntityAsync_With110Records_UploadsThreeBatches()
    {
        // Arrange
        var (_, cache, _) = CreateJob();

        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((byte[]?)null);

        cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var testJob = new TestableRecordSyncJob(
            cache.Object,
            Mock.Of<IHttpClientFactory>(),
            Options.Create(DefaultOptions()),
            Mock.Of<ILogger<RecordSyncJob>>());

        testJob.DataverseRecordsToReturn = BuildFakeDataverseRecords(110);

        // Act
        var count = await testJob.SyncEntityAsync(EntityConfigs[0], CancellationToken.None);

        // Assert
        count.Should().Be(110);
        testJob.UploadCallBatchSizes.Should().HaveCount(3,
            "110 records / 50 per batch = 3 upload calls");
        testJob.UploadCallBatchSizes[0].Should().Be(50);
        testJob.UploadCallBatchSizes[1].Should().Be(50);
        testJob.UploadCallBatchSizes[2].Should().Be(10);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (d) 429 triggers retry with backoff
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadBatchWithRetryAsync_On429_RetriesUpToMaxRetries()
    {
        // Arrange — use a testable job that counts retry attempts
        var testJob = new TestableRecordSyncJob(
            Mock.Of<IDistributedCache>(),
            Mock.Of<IHttpClientFactory>(),
            Options.Create(DefaultOptions()),
            Mock.Of<ILogger<RecordSyncJob>>());

        // Fail with 429 on attempts 1 and 2, succeed on attempt 3.
        testJob.UploadFailuresBeforeSuccess = 2;

        var batch = BuildFakeSearchDocuments(5);

        // Act — should not throw (succeeds on third attempt)
        await testJob.UploadBatchWithRetryAsync(batch, "sprk_matter", CancellationToken.None);

        // Assert
        testJob.UploadAttempts.Should().Be(3,
            "should retry twice before succeeding on the third attempt");
    }

    [Fact]
    public async Task UploadBatchWithRetryAsync_On429AllRetries_ThrowsAfterMaxRetries()
    {
        // Arrange
        var testJob = new TestableRecordSyncJob(
            Mock.Of<IDistributedCache>(),
            Mock.Of<IHttpClientFactory>(),
            Options.Create(DefaultOptions()),
            Mock.Of<ILogger<RecordSyncJob>>());

        testJob.UploadFailuresBeforeSuccess = int.MaxValue; // never succeeds

        var batch = BuildFakeSearchDocuments(5);

        // Act
        Func<Task> act = async () =>
            await testJob.UploadBatchWithRetryAsync(batch, "sprk_matter", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<RequestFailedException>(
            "after 3 retries all resulting in 429, the exception should be rethrown");

        testJob.UploadAttempts.Should().Be(3, "exactly 3 attempts before giving up");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (e) Cancellation token is honoured
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncEntityAsync_WhenCancelledMidBatch_StopsCleanly()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        var cacheMock = new Mock<IDistributedCache>();
        cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((byte[]?)null);

        var testJob = new TestableRecordSyncJob(
            cacheMock.Object,
            Mock.Of<IHttpClientFactory>(),
            Options.Create(DefaultOptions()),
            Mock.Of<ILogger<RecordSyncJob>>());

        // 100 records => 2 batches of 50.  Cancel after first batch upload.
        testJob.DataverseRecordsToReturn = BuildFakeDataverseRecords(100);
        testJob.OnUploadCallback = () => cts.Cancel();

        // Act
        Func<Task> act = async () =>
            await testJob.SyncEntityAsync(EntityConfigs[0], cts.Token);

        // Assert — OperationCanceledException is expected (clean shutdown)
        await act.Should().ThrowAsync<OperationCanceledException>(
            "cancellation between batches should surface as OperationCanceledException");

        testJob.UploadAttempts.Should().Be(1,
            "should only complete the first batch before cancellation stops the loop");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (h) MapToSearchDocument — field mapping correctness
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MapToSearchDocument_Matter_MapsAllFieldsCorrectly()
    {
        // Arrange
        var config = EntityConfigs[0]; // sprk_matter

        var json = JsonDocument.Parse("""
            {
                "sprk_matterid": "abc-123",
                "sprk_mattername": "Acme Corp Matter",
                "sprk_matterdescription": "Employment dispute",
                "sprk_matternumber": "MAT-2026-001",
                "modifiedon": "2026-05-17T09:00:00Z"
            }
            """);
        var record = json.RootElement;

        // Act
        var doc = RecordSyncJob.MapToSearchDocument(record, config);

        // Assert
        doc.Id.Should().Be("sprk_matter_abc-123");
        doc.RecordType.Should().Be("sprk_matter");
        doc.RecordName.Should().Be("Acme Corp Matter");
        doc.RecordDescription.Should().Be("Employment dispute");
        doc.ReferenceNumbers.Should().ContainSingle().Which.Should().Be("MAT-2026-001");
        doc.Keywords.Should().Contain("Acme Corp Matter");
        doc.Keywords.Should().Contain("MAT-2026-001");
        doc.DataverseRecordId.Should().Be("abc-123");
        doc.DataverseEntityName.Should().Be("sprk_matter");
        doc.LastModified.Year.Should().Be(2026);
        doc.Organizations.Should().BeEmpty();
        doc.People.Should().BeEmpty();
    }

    [Fact]
    public void MapToSearchDocument_Contact_MapsNoReferenceNumber()
    {
        // Arrange — contact has no ReferenceField
        var config = EntityConfigs.First(e => e.EntityLogicalName == "contact");

        var json = JsonDocument.Parse("""
            {
                "contactid": "cid-456",
                "fullname": "Jane Doe",
                "description": "Senior counsel",
                "jobtitle": "Partner",
                "modifiedon": "2026-05-17T10:00:00Z"
            }
            """);
        var record = json.RootElement;

        // Act
        var doc = RecordSyncJob.MapToSearchDocument(record, config);

        // Assert
        doc.Id.Should().Be("contact_cid-456");
        doc.RecordName.Should().Be("Jane Doe");
        doc.ReferenceNumbers.Should().BeEmpty("contacts have no reference field");
        doc.Keywords.Should().Be("Jane Doe");
    }


    // ─────────────────────────────────────────────────────────────────────────
    // Convenience — expose EntityConfigs for tests without duplicating config
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<EntityConfig> EntityConfigs =
        new[]
        {
            new EntityConfig(
                "sprk_matter", "sprk_matters", "sprk_matterid", "sprk_mattername",
                "sprk_matterdescription", "sprk_matternumber",
                "sprk_matterid,sprk_mattername,sprk_matterdescription,sprk_matternumber,modifiedon"),
            new EntityConfig(
                "sprk_project", "sprk_projects", "sprk_projectid", "sprk_projectname",
                "sprk_projectdescription", "sprk_projectnumber",
                "sprk_projectid,sprk_projectname,sprk_projectdescription,sprk_projectnumber,modifiedon"),
            new EntityConfig(
                "contact", "contacts", "contactid", "fullname",
                "description", null,
                "contactid,fullname,description,jobtitle,parentcustomerid,modifiedon"),
            new EntityConfig(
                "account", "accounts", "accountid", "name",
                "description", "accountnumber",
                "accountid,name,description,accountnumber,modifiedon"),
        };

    // ─────────────────────────────────────────────────────────────────────────
    // Test data builders
    // ─────────────────────────────────────────────────────────────────────────

    private static List<JsonElement> BuildFakeDataverseRecords(int count)
    {
        var records = new List<JsonElement>();
        var baseTime = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < count; i++)
        {
            var modifiedOn = baseTime.AddHours(i);
            var jsonStr = string.Format(
                "{{\"sprk_matterid\":\"matter-{0}\",\"sprk_mattername\":\"Matter {0}\"," +
                "\"sprk_matterdescription\":\"Description {0}\"," +
                "\"sprk_matternumber\":\"MAT-{0:D4}\"," +
                "\"modifiedon\":\"{1:O}\"}}",
                i, modifiedOn);
            var json = JsonDocument.Parse(jsonStr);
            records.Add(json.RootElement.Clone());
        }

        return records;
    }

    private static List<RecordSearchDocument> BuildFakeSearchDocuments(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new RecordSearchDocument
            {
                Id = $"sprk_matter_matter-{i}",
                RecordType = "sprk_matter",
                RecordName = $"Matter {i}",
                DataverseRecordId = $"matter-{i}",
                DataverseEntityName = "sprk_matter",
                LastModified = DateTimeOffset.UtcNow,
            })
            .ToList();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Testable subclass — overrides I/O methods for deterministic unit tests
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Derives from RecordSyncJob and stubs out external I/O (Dataverse + AI Search)
/// so tests exercise the orchestration logic without real network calls.
/// </summary>
internal sealed class TestableRecordSyncJob : RecordSyncJob
{
    // ── Test inputs ──────────────────────────────────────────────────────────
    public List<JsonElement> DataverseRecordsToReturn { get; set; } = new();
    public bool UploadShouldThrow { get; set; } = false;
    public int UploadFailuresBeforeSuccess { get; set; } = 0;
    public Action? OnUploadCallback { get; set; }

    // ── Test outputs ─────────────────────────────────────────────────────────
    public List<int> UploadCallBatchSizes { get; } = new();
    public int UploadAttempts { get; private set; }

    private int _consecutiveUploadFailures = 0;

    public TestableRecordSyncJob(
        IDistributedCache cache,
        IHttpClientFactory factory,
        IOptions<RecordSyncOptions> options,
        ILogger<RecordSyncJob> logger)
        : base(cache, factory, new ConfigurationBuilder().Build(), options, new DefaultAzureCredential(), logger)
    {
    }

    public override Task<List<JsonElement>> QueryDataverseAsync(
        EntityConfig entity,
        DateTimeOffset watermark,
        CancellationToken ct)
    {
        return Task.FromResult(DataverseRecordsToReturn);
    }

    /// <summary>
    /// Overrides only the innermost upload call so the base-class retry loop
    /// in UploadBatchWithRetryAsync is exercised end-to-end.
    /// </summary>
    public override Task DoUploadBatchAsync(
        List<RecordSearchDocument> batch,
        string entityType,
        CancellationToken ct)
    {
        UploadAttempts++;
        UploadCallBatchSizes.Add(batch.Count);

        if (UploadShouldThrow)
        {
            throw new InvalidOperationException("Simulated upload failure");
        }

        if (_consecutiveUploadFailures < UploadFailuresBeforeSuccess)
        {
            _consecutiveUploadFailures++;
            throw new RequestFailedException((int)HttpStatusCode.TooManyRequests, "Simulated 429");
        }

        // Invoke cancellation callback (used by the cancellation test), then check token.
        OnUploadCallback?.Invoke();
        ct.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }

    // Eliminate real delays in retry tests — return zero wait time.
    public override TimeSpan GetRetryDelay(int attempt) => TimeSpan.Zero;

    // Base methods are already public virtual — no re-exposure needed.
    // Tests call ReadWatermarkAsync / WriteWatermarkAsync / SyncEntityAsync directly on this instance.
}
