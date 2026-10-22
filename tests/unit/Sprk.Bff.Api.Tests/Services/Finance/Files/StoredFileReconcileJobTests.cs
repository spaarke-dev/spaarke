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
        public Harness(
            bool ebilling = true, bool configured = true, int graceMinutes = 1440, int alertMinutes = 60, int pageSize = 5000, int maxPages = 50,
            Guid? organizationId = null)
        {
            Rig = new StoredFileRig(
                configured: configured, organizationId: organizationId is { } org ? _ => Task.FromResult(org) : null);
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
                Log)
            {
                PageSize = pageSize,
                MaxPages = maxPages,
            };
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
        public string SeedOrphan(DateTimeOffset at, string? submission = null, string? environment = StoredFileBlobs.TestEnvironment)
        {
            var path = PathOf(new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid()), Guid.NewGuid());
            var metadata = new Dictionary<string, string> { ["sha256"] = "00" };
            if (environment is not null)
            {
                metadata["environment"] = environment;
            }
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
        h.Rig.Dataverse.Calls.Should().Contain($"retrieve sprk_invoice {parent.Id}", "the path's parent is confirmed gone by its own read");
    }

    [Fact(DisplayName = "Must-fire (uac-r2 round 1): a parentless row whose PATH's parent still exists is kept with its blob (the null lookups prove nothing)")]
    public async Task Parentless_PathParentAlive_Kept()
    {
        var h = new Harness();
        var parent = h.LiveInvoice();
        var file = Guid.NewGuid();
        h.Rig.SeedFile(file, parent, Pdf).Attributes.Remove("sprk_invoice");

        var result = await h.RunAsync();

        h.Rig.Dataverse.Rows.Should().ContainKey(file);
        h.Rig.Gateway.Get("invoice-files", h.PathOf(parent, file)).Should().NotBeNull();
        h.Counts(result)["rowsParentlessKept"].GetInt32().Should().Be(1);
        h.Counts(result)["rowsDeleted"].GetInt32().Should().Be(0);
    }

    [Fact(DisplayName = "Must-fire (uac-r2 round 1): a parentless row whose path's parent read faults is kept and the fault counted")]
    public async Task Parentless_PathParentReadFaults_Kept()
    {
        var h = new Harness();
        var parent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        var file = Guid.NewGuid();
        h.Rig.SeedFile(file, parent, Pdf).Attributes.Remove("sprk_invoice");
        h.Rig.Dataverse.RetrieveFault = new TimeoutException("Dataverse slow");

        var result = await h.RunAsync();

        h.Rig.Dataverse.Rows.Should().ContainKey(file);
        h.Rig.Gateway.Get("invoice-files", h.PathOf(parent, file)).Should().NotBeNull();
        h.Counts(result)["unitFaults"].GetInt32().Should().Be(1);
    }

    [Fact(DisplayName = "Must-fire (uac-r2 round 1): a parentless row whose path names no parent of this tenant is kept (nothing to confirm)")]
    public async Task Parentless_UnparseablePath_Kept()
    {
        var h = new Harness();
        var parent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        var file = Guid.NewGuid();
        h.Rig.SeedFile(file, parent, Pdf, blobPathOverride: "not/a/stored/file").Attributes.Remove("sprk_invoice");

        var result = await h.RunAsync();

        h.Rig.Dataverse.Rows.Should().ContainKey(file);
        h.Counts(result)["rowsParentlessKept"].GetInt32().Should().Be(1);
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

    [Theory(DisplayName = "Must-fire (uac-r2 round 1 F1): a parent-gone row whose blob carries ANOTHER environment's marker, or none, keeps its blob AND its row")]
    [InlineData("prod.crm.dynamics.com")]
    [InlineData("STOREDFILE-TEST.CRM.DYNAMICS.COM")]
    [InlineData(null)]
    public async Task ParentGone_OtherEnvironmentsBlob_BlobAndRowKept(string? marker)
    {
        // A copy of production in UAT (shared account and name segment): the invoice is deleted in UAT, prod still serves the blob.
        var h = new Harness();
        var goneParent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        var file = Guid.NewGuid();
        h.Rig.SeedFile(file, goneParent, Pdf, environment: marker);

        var result = await h.RunAsync();

        h.Rig.Gateway.Get("invoice-files", h.PathOf(goneParent, file)).Should().NotBeNull("another environment's bytes are never deleted");
        h.Rig.Dataverse.Rows.Should().ContainKey(file, "the row is the only record of the blob; it is kept and counted");
        h.Rig.Gateway.Calls.Should().NotContain(c => c.StartsWith("delete", StringComparison.Ordinal));
        h.Counts(result)["blobsOfOtherEnvironments"].GetInt32().Should().Be(1);
        h.Counts(result)["rowsDeleted"].GetInt32().Should().Be(0);
        h.Counts(result)["blobsDeleted"].GetInt32().Should().Be(0);
    }

    [Fact(DisplayName = "Must-fire (uac-r2 round 2 K1): a parent-gone row's blob rewritten between the marker read and the delete (412) is kept with its row, counted, not a fault")]
    public async Task ParentGone_BlobChangedAfterJudged_KeptNotFault()
    {
        var h = new Harness();
        var goneParent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        var file = Guid.NewGuid();
        h.Rig.SeedFile(file, goneParent, Pdf);
        var path = h.PathOf(goneParent, file);
        h.Rig.Gateway.BeforeOp = (op, name) =>
        {
            if (op == "delete" && name == "invoice-files/" + path)
            {
                h.Rig.Gateway.Overwrite("invoice-files", path, [1, 2, 3], h.Rig.Gateway.Now);
            }
        };

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull("a changed blob is kept, not a faulted unit");
        h.Rig.Gateway.Get("invoice-files", path).Should().NotBeNull("only the blob that was judged may be deleted");
        h.Rig.Dataverse.Rows.Should().ContainKey(file, "the row is the only record of the kept blob");
        h.Counts(result)["blobsChangedSinceJudged"].GetInt32().Should().Be(1);
        h.Counts(result)["unitFaults"].GetInt32().Should().Be(0);
        h.Counts(result)["rowsDeleted"].GetInt32().Should().Be(0);
        h.Counts(result)["blobsDeleted"].GetInt32().Should().Be(0);
        h.Log.Entries.Should().Contain(e => e.Message.Contains("412", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Must-not-fire (uac-r2 round 1 F1): a parent-gone row whose blob is already gone is deleted (no blob delete is attempted)")]
    public async Task ParentGone_BlobAlreadyGone_RowDeleted()
    {
        var h = new Harness();
        var goneParent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        var file = Guid.NewGuid();
        h.Rig.SeedFile(file, goneParent, Pdf);
        (await h.Rig.Gateway.DeleteIfExistsAsync("invoice-files", h.PathOf(goneParent, file), ifMatch: null, CancellationToken.None)).Should().BeTrue();
        h.Rig.Gateway.FailOp["delete"] = new RequestFailedException(503, "a delete must not be attempted");

        var result = await h.RunAsync();

        h.Rig.Dataverse.Rows.Should().NotContainKey(file);
        h.Counts(result)["rowsDeleted"].GetInt32().Should().Be(1);
        h.Counts(result)["unitFaults"].GetInt32().Should().Be(0);
    }

    [Fact(DisplayName = "Must-fire (review F1): a row whose secured columns come back masked (absent) is NOT parentless: row and blob kept, counted unreadable")]
    public async Task MaskedRow_Kept()
    {
        var h = new Harness();
        var parent = h.LiveInvoice();
        var file = Guid.NewGuid();
        var row = h.Rig.SeedFile(file, parent, Pdf);
        foreach (var column in new[] { "sprk_invoice", "sprk_filestatus", "sprk_purpose", "sprk_blobpath" })
        {
            row.Attributes.Remove(column);
        }

        var result = await h.RunAsync();

        h.Rig.Dataverse.Rows.Should().ContainKey(file);
        h.Rig.Gateway.Get("invoice-files", h.PathOf(parent, file)).Should().NotBeNull();
        h.Counts(result)["rowsUnreadable"].GetInt32().Should().Be(1);
        h.Counts(result)["rowsDeleted"].GetInt32().Should().Be(0);
    }

    [Fact(DisplayName = "Must-fire (review F2): a parent the set query omits but a read by id finds (privilege depth) is alive: file kept")]
    public async Task ParentHiddenFromQuery_Kept()
    {
        var h = new Harness();
        var parent = h.LiveInvoice();
        h.Rig.Dataverse.HiddenFromQueries.Add(parent.Id);
        var file = Guid.NewGuid();
        h.Rig.SeedFile(file, parent, Pdf);

        var result = await h.RunAsync();

        h.Rig.Dataverse.Rows.Should().ContainKey(file);
        h.Counts(result)["rowsDeleted"].GetInt32().Should().Be(0);
        h.Rig.Dataverse.Calls.Should().Contain($"retrieve sprk_invoice {parent.Id}", "a candidate is confirmed by its own read");
    }

    [Fact(DisplayName = "Must-fire (review F2): the confirming read faulting keeps the file and counts the fault")]
    public async Task ConfirmReadFaults_Kept()
    {
        var h = new Harness();
        var goneParent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        var file = Guid.NewGuid();
        h.Rig.SeedFile(file, goneParent, Pdf);
        h.Rig.Dataverse.RetrieveFault = new TimeoutException("Dataverse slow");

        var result = await h.RunAsync();

        h.Rig.Dataverse.Rows.Should().ContainKey(file);
        h.Counts(result)["unitFaults"].GetInt32().Should().Be(1);
    }

    [Fact(DisplayName = "Review F4: a row already deleted when the job deletes it (the client WRAPS the not-found) is done, not a fault")]
    public async Task RowAlreadyGone_WrappedNotFound_IsDone()
    {
        var h = new Harness();
        var goneParent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        var file = Guid.NewGuid();
        h.Rig.SeedFile(file, goneParent, Pdf);
        h.Rig.Gateway.BeforeOp = (op, name) =>
        {
            if (op == "delete")
            {
                h.Rig.Dataverse.Rows.TryRemove(file, out _); // another run got there first
            }
        };

        var result = await h.RunAsync();

        result.ErrorMessage.Should().BeNull();
        h.Counts(result)["unitFaults"].GetInt32().Should().Be(0);
        h.Counts(result)["rowsDeleted"].GetInt32().Should().Be(1);
    }

    [Fact(DisplayName = "Rows are read page by page with the paging cookie; every page's parent-gone rows are deleted")]
    public async Task Paging_ReadsEveryPage()
    {
        var h = new Harness(pageSize: 2);
        for (var i = 0; i < 5; i++)
        {
            h.Rig.SeedFile(Guid.NewGuid(), new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid()), Pdf);
        }

        var result = await h.RunAsync();

        h.Rig.Dataverse.PagesRequested.Select(p => p.Page).Should().Equal(1, 2, 3);
        h.Rig.Dataverse.Rows.Should().BeEmpty();
        h.Counts(result)["rowsExamined"].GetInt32().Should().Be(5);
        h.Counts(result)["rowsTruncated"].GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "Truncated read (MaxPages): a blob whose row was on an unread page is kept (pass B re-reads the row)")]
    public async Task Truncated_UnreadRowsBlobKept()
    {
        var h = new Harness(pageSize: 1, maxPages: 1);
        var parent = h.LiveInvoice();
        var files = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        foreach (var file in files)
        {
            h.Rig.SeedFile(file, parent, Pdf);
        }

        var result = await h.RunAsync();

        h.Counts(result)["rowsTruncated"].GetBoolean().Should().BeTrue();
        h.Counts(result)["rowsExamined"].GetInt32().Should().Be(1);
        foreach (var file in files)
        {
            h.Rig.Gateway.Get("invoice-files", h.PathOf(parent, file)).Should().NotBeNull();
        }

        h.Counts(result)["blobsDeleted"].GetInt32().Should().Be(0);
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

    [Fact(DisplayName = "Must-fire (uac-r2 round 2 K1): a row-less blob rewritten between the listing and the delete (412) is kept, counted, not a fault")]
    public async Task Orphan_ChangedAfterListed_KeptNotFault()
    {
        var h = new Harness();
        var path = h.SeedOrphan(h.Clock.GetUtcNow().AddDays(-2));
        h.Rig.Gateway.BeforeOp = (op, name) =>
        {
            if (op == "delete" && name == "invoice-files/" + path)
            {
                h.Rig.Gateway.Overwrite("invoice-files", path, [1, 2, 3], h.Clock.GetUtcNow());
            }
        };

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull("a changed blob is kept, not a faulted unit");
        h.Rig.Gateway.Get("invoice-files", path).Should().NotBeNull("the listing judged another version of the blob");
        h.Counts(result)["blobsChangedSinceJudged"].GetInt32().Should().Be(1);
        h.Counts(result)["unitFaults"].GetInt32().Should().Be(0);
        h.Counts(result)["blobsDeleted"].GetInt32().Should().Be(0);
        h.Log.Entries.Should().Contain(e => e.Message.Contains("412", StringComparison.Ordinal));
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

    [Theory(DisplayName = "Must-fire (review pass 1): a row-less blob of ANOTHER environment sharing the account and tenant id, or with no marker, is kept")]
    [InlineData("other-env.crm.dynamics.com")]
    [InlineData("STOREDFILE-TEST.CRM.DYNAMICS.COM")]
    [InlineData(null)]
    public async Task Orphan_OtherEnvironment_Kept(string? marker)
    {
        var h = new Harness();
        var path = h.SeedOrphan(h.Clock.GetUtcNow().AddDays(-30), environment: marker);

        var result = await h.RunAsync();

        h.Rig.Gateway.Get("invoice-files", path).Should().NotBeNull();
        h.Counts(result)["blobsOfOtherEnvironments"].GetInt32().Should().Be(1);
        h.Rig.Dataverse.Calls.Should().NotContain(c => c.StartsWith("retrieve", StringComparison.Ordinal), "another environment's blob is not judged at all");
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

        // A1-5 (review F4): the failed attempt still logs its one heartbeat.
        h.Log.Entries.Where(e => e.EventId.Id == 9092).Should().ContainSingle().Which.Message.Should().Contain("failed=True");
    }

    [Fact(DisplayName = "Must-fire (uac-r2 round 1): a name segment that is not this environment's OrganizationId fails the run (A1-4) and deletes nothing")]
    public async Task OrganizationMismatch_Throws_NothingDeleted()
    {
        var h = new Harness(organizationId: Guid.Parse("0e900000-0000-4000-8000-000000000001"));
        var goneParent = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        h.Rig.SeedFile(Guid.NewGuid(), goneParent, Pdf);
        h.SeedOrphan(h.Clock.GetUtcNow().AddDays(-2));

        var act = () => h.RunAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*OrganizationId*");
        h.Rig.Gateway.StorageCalls.Should().Be(0);
        h.Rig.Dataverse.Writes.Should().Be(0);
        h.Log.Entries.Where(e => e.EventId.Id == 9092).Should().ContainSingle().Which.Message.Should().Contain("failed=True");
    }

    [Fact(DisplayName = "Must-not-fire (uac-r2 round 1): the segment equal to this environment's OrganizationId runs normally")]
    public async Task OrganizationMatch_Runs()
    {
        var h = new Harness(organizationId: StoredFileRig.Tenant);
        var path = h.SeedOrphan(h.Clock.GetUtcNow().AddDays(-2));

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Rig.Gateway.Get("invoice-files", path).Should().BeNull();
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
