using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Azure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Files;

/// <summary>
/// The Finance file store's ONE scheduled job (ebilling-r1 task 090; procedure-and-file-model 4.2; ADR-036 A1): parent-gone files,
/// row-less blobs after the grace (never a non-terminal submission's), the Not-yet-servable detector, fail-safe faults, idempotence,
/// cancellation and the heartbeat.
/// </summary>
public sealed class StoredFileReconcileJobTests
{
    private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 reconcile");
    private const string SubmissionTable = "sprk_invoicesubmission";
    private const string SubmissionStatus = "sprk_processingstatus";

    private sealed class Harness
    {
        public Harness(bool ebilling = true, bool configured = true, int graceMinutes = 1440, int alertMinutes = 60)
        {
            Rig = new StoredFileRig(configured: configured);
            Clock = new FakeTimeProvider(Rig.Gateway.Now.AddHours(25));
            var services = new ServiceCollection();
            services.AddSingleton(Rig.Dataverse.Service);
            var provider = services.BuildServiceProvider();
            Job = new StoredFileReconcileJob(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Rig.Blobs,
                Options.Create(new EbillingOptions { Enabled = ebilling }),
                Options.Create(new FinanceOptions { FileStore = new FinanceFileStoreOptions { OrphanGraceMinutes = graceMinutes, NotServableAlertMinutes = alertMinutes } }),
                Clock,
                Log);
        }

        public StoredFileRig Rig { get; }
        public FakeTimeProvider Clock { get; }
        public CapturingLogger<StoredFileReconcileJob> Log { get; } = new();
        public StoredFileReconcileJob Job { get; }

        public Task<JobRunResult> RunAsync(CancellationToken ct = default) =>
            Job.ExecuteAsync(new JobRunContext(Guid.NewGuid(), "corr-090", JobRunTrigger.Scheduled, new Dictionary<string, object>()), ct);

        public string PathOf(StoredFileParent parent, Guid file) => StoredFileBlobName.Build(StoredFileRig.Tenant, parent, file);

        /// <summary>A live invoice parent.</summary>
        public StoredFileParent LiveInvoice()
        {
            var parent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
            Rig.Dataverse.AddOther("sprk_invoice", parent.Id);
            return parent;
        }

        /// <summary>A row-less blob, written at <paramref name="at"/>, optionally naming a submission.</summary>
        public string SeedOrphan(DateTimeOffset at, string? submission = null)
        {
            var path = PathOf(new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid()), Guid.NewGuid());
            var metadata = new Dictionary<string, string> { ["sha256"] = "00" };
            if (submission is not null)
            {
                metadata["submissionid"] = submission;
            }

            Rig.Gateway.Seed("invoice-files", path, Pdf, metadata, lastModified: at);
            return path;
        }

        public Dictionary<string, JsonElement> Counts(JobRunResult result) =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(result.ResultJson!)!;
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // (A) parent gone
    // ---------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Parent gone: the file's blob and row are deleted; a live parent's file is kept; a second run does nothing new")]
    public async Task ParentGone_BlobAndRowDeleted_Idempotent()
    {
        var h = new Harness();
        var goneParent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        var doomed = Guid.NewGuid();
        h.Rig.SeedFile(doomed, goneParent, Pdf);
        var live = h.LiveInvoice();
        var kept = Guid.NewGuid();
        h.Rig.SeedFile(kept, live, Pdf);

        var first = await h.RunAsync();

        first.Success.Should().BeTrue();
        h.Rig.Dataverse.Rows.Keys.Should().BeEquivalentTo([kept]);
        h.Rig.Gateway.Get("invoice-files", h.PathOf(goneParent, doomed)).Should().BeNull();
        h.Rig.Gateway.Get("invoice-files", h.PathOf(live, kept)).Should().NotBeNull();
        h.Counts(first)["rowsDeleted"].GetInt32().Should().Be(1);
        h.Counts(first)["blobsDeleted"].GetInt32().Should().Be(1);

        var writesBefore = h.Rig.Dataverse.Writes;
        var deletesBefore = h.Rig.Gateway.Calls.Count(c => c.StartsWith("delete", StringComparison.Ordinal));
        var second = await h.RunAsync();

        second.ProcessedItems.Should().Be(0);
        h.Rig.Dataverse.Writes.Should().Be(writesBefore);
        h.Rig.Gateway.Calls.Count(c => c.StartsWith("delete", StringComparison.Ordinal)).Should().Be(deletesBefore);
    }

    [Fact(DisplayName = "Parent gone, blob delete faults → the row is kept (blob-then-row order), the fault is counted, the run completes")]
    public async Task ParentGone_BlobDeleteFaults_RowKept()
    {
        var h = new Harness();
        var goneParent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        var file = Guid.NewGuid();
        h.Rig.SeedFile(file, goneParent, Pdf);
        h.Rig.Gateway.FailOp["delete"] = new RequestFailedException(503, "busy");

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Rig.Dataverse.Rows.Should().ContainKey(file);
        h.Counts(result)["unitFaults"].GetInt32().Should().Be(1);
        result.ErrorMessage.Should().Contain("faulted");
    }

    [Fact(DisplayName = "Parent existence check faults → the file is kept (a fault is never 'gone'), counted, the run completes")]
    public async Task ParentCheckFaults_FileKept()
    {
        var h = new Harness();
        var parent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        var file = Guid.NewGuid();
        h.Rig.SeedFile(file, parent, Pdf);
        h.Rig.Dataverse.FaultingTables.Add("sprk_invoice");

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Rig.Dataverse.Rows.Should().ContainKey(file);
        h.Rig.Gateway.Get("invoice-files", h.PathOf(parent, file)).Should().NotBeNull();
        h.Counts(result)["unitFaults"].GetInt32().Should().Be(1);
    }

    [Fact(DisplayName = "A parentless row (every lookup cleared by RemoveLink) is deleted with its blob")]
    public async Task Parentless_Deleted()
    {
        var h = new Harness();
        var parent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        var file = Guid.NewGuid();
        h.Rig.SeedFile(file, parent, Pdf).Attributes.Remove("sprk_invoice");

        await h.RunAsync();

        h.Rig.Dataverse.Rows.Should().NotContainKey(file);
        h.Rig.Gateway.Get("invoice-files", h.PathOf(parent, file)).Should().BeNull();
    }

    [Fact(DisplayName = "Must-fire: a parent-gone row whose path names ANOTHER file's blob is deleted, and the other blob is untouched")]
    public async Task ParentGone_ForeignPath_OtherBlobUntouched()
    {
        var h = new Harness();
        var live = h.LiveInvoice();
        var victim = Guid.NewGuid();
        h.Rig.SeedFile(victim, live, Pdf);
        var goneParent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        var rogue = Guid.NewGuid();
        h.Rig.SeedFile(rogue, goneParent, Pdf, blobPathOverride: h.PathOf(live, victim));

        var result = await h.RunAsync();

        h.Rig.Dataverse.Rows.Should().NotContainKey(rogue);
        h.Rig.Gateway.Get("invoice-files", h.PathOf(live, victim)).Should().NotBeNull("only a row's own blob is deleted");
        h.Counts(result)["rowsWithForeignPath"].GetInt32().Should().Be(1);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // (B) row-less blobs
    // ---------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Row-less blob: inside the grace kept; past the grace deleted")]
    public async Task Orphan_Grace()
    {
        var h = new Harness();
        var now = h.Clock.GetUtcNow();
        var young = h.SeedOrphan(now.AddMinutes(-1439));
        var old = h.SeedOrphan(now.AddMinutes(-1441));

        var result = await h.RunAsync();

        h.Rig.Gateway.Get("invoice-files", young).Should().NotBeNull();
        h.Rig.Gateway.Get("invoice-files", old).Should().BeNull();
        h.Counts(result)["blobsInGrace"].GetInt32().Should().Be(1);
        h.Counts(result)["blobsDeleted"].GetInt32().Should().Be(1);
    }

    [Theory(DisplayName = "Row-less blob past the grace naming a submission: kept while the submission is not terminal")]
    [InlineData(100000000, false)]
    [InlineData(100000001, false)]
    [InlineData(null, false)]
    [InlineData(100000002, true)]
    [InlineData(100000003, true)]
    [InlineData(100000004, true)]
    public async Task Orphan_SubmissionStatus(int? status, bool deleted)
    {
        var h = new Harness();
        var submission = Guid.NewGuid();
        if (status is { } s)
        {
            h.Rig.Dataverse.AddOther(SubmissionTable, submission, (SubmissionStatus, new OptionSetValue(s)));
        }
        else
        {
            h.Rig.Dataverse.AddOther(SubmissionTable, submission);
        }

        var path = h.SeedOrphan(h.Clock.GetUtcNow().AddDays(-2), submission.ToString("D"));

        var result = await h.RunAsync();

        (h.Rig.Gateway.Get("invoice-files", path) is null).Should().Be(deleted);
        h.Counts(result)["blobsOfOpenSubmissions"].GetInt32().Should().Be(deleted ? 0 : 1);
    }

    [Fact(DisplayName = "Row-less blob: a gone submission allows the delete; an unparseable submission id keeps the blob; a submission fault keeps it")]
    public async Task Orphan_SubmissionEdges()
    {
        var h = new Harness();
        var old = h.Clock.GetUtcNow().AddDays(-2);
        var gone = h.SeedOrphan(old, Guid.NewGuid().ToString("D"));
        var garbage = h.SeedOrphan(old, "not-a-guid");

        await h.RunAsync();

        h.Rig.Gateway.Get("invoice-files", gone).Should().BeNull();
        h.Rig.Gateway.Get("invoice-files", garbage).Should().NotBeNull();

        var faulty = new Harness();
        var path = faulty.SeedOrphan(faulty.Clock.GetUtcNow().AddDays(-2), Guid.NewGuid().ToString("D"));
        faulty.Rig.Dataverse.RetrieveFault = new TimeoutException("Dataverse slow");
        var result = await faulty.RunAsync();
        faulty.Rig.Gateway.Get("invoice-files", path).Should().NotBeNull();
        faulty.Counts(result)["unitFaults"].GetInt32().Should().Be(1);
    }

    [Fact(DisplayName = "Another tenant's blob in the same container is never listed or deleted")]
    public async Task OtherTenant_Untouched()
    {
        var h = new Harness();
        var other = Guid.Parse("0ff1e000-0000-4000-8000-000000000999");
        var foreign = StoredFileBlobName.Build(other, new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid()), Guid.NewGuid());
        h.Rig.Gateway.Seed("invoice-files", foreign, Pdf, lastModified: h.Clock.GetUtcNow().AddDays(-30));

        var result = await h.RunAsync();

        h.Rig.Gateway.Get("invoice-files", foreign).Should().NotBeNull();
        h.Counts(result)["blobsExamined"].GetInt32().Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // (C) detector
    // ---------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Detector: a row Not yet servable past the alert window raises ONE warning (9091) with its id; no repair is made")]
    public async Task Detector_Fires()
    {
        var h = new Harness();
        var live = h.LiveInvoice();
        var stuck = Guid.NewGuid();
        h.Rig.SeedFile(stuck, live, Pdf, StoredFileStatus.NotYetServable);
        var fine = Guid.NewGuid();
        h.Rig.SeedFile(fine, live, Pdf);

        var result = await h.RunAsync();

        h.Counts(result)["notServableDetected"].GetInt32().Should().Be(1);
        var warning = h.Log.Entries.Where(e => e.EventId.Id == 9091).Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain(stuck.ToString()).And.NotContain(fine.ToString());
        h.Rig.Dataverse.Rows[stuck].GetAttributeValue<OptionSetValue>("sprk_filestatus").Value.Should().Be(100000000);
    }

    [Fact(DisplayName = "Detector must-not-fire: a row Not yet servable inside the alert window raises nothing")]
    public async Task Detector_Quiet()
    {
        var h = new Harness(alertMinutes: 60);
        h.Rig.Gateway.Now = h.Clock.GetUtcNow().AddMinutes(-59);
        h.Rig.SeedFile(Guid.NewGuid(), h.LiveInvoice(), Pdf, StoredFileStatus.NotYetServable);

        var result = await h.RunAsync();

        h.Counts(result)["notServableDetected"].GetInt32().Should().Be(0);
        h.Log.Entries.Should().NotContain(e => e.EventId.Id == 9091);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // ADR-036 A1
    // ---------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "A1-5: exactly one heartbeat (9092) per attempt, carrying the counts; ResultJson is camelCase")]
    public async Task Heartbeat_OnePerAttempt()
    {
        var h = new Harness();
        await h.RunAsync();
        await h.RunAsync();

        h.Log.Entries.Count(e => e.EventId.Id == 9092).Should().Be(2);
        h.Log.Entries.First(e => e.EventId.Id == 9092).Message.Should().Contain("correlationId=corr-090");
    }

    [Fact(DisplayName = "A1-4: the row query faulting throws (no progress possible), nothing deleted")]
    public async Task RowQueryFault_Throws()
    {
        var h = new Harness();
        h.SeedOrphan(h.Clock.GetUtcNow().AddDays(-2));
        h.Rig.Dataverse.QueryFault = new TimeoutException("Dataverse down");

        var act = () => h.RunAsync();

        await act.Should().ThrowAsync<TimeoutException>();
        h.Rig.Gateway.Calls.Should().NotContain(c => c.StartsWith("delete", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "A1-4: the blob listing faulting throws")]
    public async Task ListingFault_Throws()
    {
        var h = new Harness();
        h.Rig.Gateway.FailOp["list"] = new RequestFailedException(503, "storage down");

        var act = () => h.RunAsync();

        await act.Should().ThrowAsync<RequestFailedException>();
    }

    [Fact(DisplayName = "Cancellation stops the run with OperationCanceledException and deletes nothing")]
    public async Task Cancelled()
    {
        var h = new Harness();
        h.SeedOrphan(h.Clock.GetUtcNow().AddDays(-2));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => h.RunAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Rig.Gateway.Calls.Should().NotContain(c => c.StartsWith("delete", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "eBilling off or the store not configured → Skipped with a heartbeat and no Dataverse or storage call")]
    [InlineData(false, true, "ebilling-disabled")]
    [InlineData(true, false, "store-not-configured")]
    public async Task Skipped(bool ebilling, bool configured, string reason)
    {
        var h = new Harness(ebilling, configured);

        var result = await h.RunAsync();

        result.Skipped.Should().BeTrue();
        h.Counts(result)["skippedReason"].GetString().Should().Be(reason);
        h.Rig.Dataverse.Calls.Should().BeEmpty();
        h.Rig.Gateway.StorageCalls.Should().Be(0);
        h.Log.Entries.Should().ContainSingle(e => e.EventId.Id == 9092);
    }

    [Fact(DisplayName = "Identity: registered id, cron at minute 23 hourly")]
    public void Identity()
    {
        new Harness().Job.JobId.Should().Be("finance-stored-file-reconcile");
        StoredFileReconcileJob.DefaultCronSchedule.Should().Be("23 * * * *");
        StoredFileRetention.SubmissionRetryHorizon.Should().Be(TimeSpan.FromMinutes(100));
    }

    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, eventId, formatter(state, exception)));
    }
}
