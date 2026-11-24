// -----------------------------------------------------------------------------
// DataverseWebApiSolutionImporterTests.cs
//
// L2 CONTROL-PLANE unit tests for DataverseWebApiSolutionImporter (task 141;
// T218b — one package, SpaarkeMaster, managed by default).
//
// ADR-038 alignment: pure C# unit tests over a real HttpClient wrapping a
// hand-rolled fake HttpMessageHandler (NOT Mock&lt;HttpMessageHandler&gt;,
// banned per testing.md) for the Dataverse Web API surface, PLUS the shared
// ArmSdkTestFakes.NewBlobContainerClient fake-transport helper for the
// artifact-manifest / package-ZIP blob surface. Polling-loop timing tests use
// TimeProvider.System with tiny real TimeSpans.
//
// COVERAGE:
//   Fresh install (managed / unmanaged blob picked by the request), upgrade
//   (StageAndUpgradeAsync for managed; ImportSolutionAsync for unmanaged), equal-version skip (incl. "1.2" == "1.2.0.0"), the
//   T218b refusals (other type installed; newer version installed) — no import
//   POST in either; failed upgrade → PartialImport; failed fresh import → not
//   promoted; timeout never promoted; token failure; manifest 404 / old format
//   / missing blob for the requested type (never falls back); package ZIP 404;
//   async-operation polling: transient statuses retried, a fresh token per
//   poll, throttling until the deadline is Timeout (never PartialImport);
//   unreadable installed state (500 / 403) → failure, never "assume fresh";
//   undeterminable version; pure parsers; source-grep defense-in-depth.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class DataverseWebApiSolutionImporterTests
{
    private const string CustomerId = "acme";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string ClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string ClientSecret = "test-client-secret-placeholder";
    private const string EnvUrl = "https://acme.crm.dynamics.com/";
    private const string ManagedBlob = "dataverse-solution-SpaarkeMaster-1.2.0.0-managed.zip";
    private const string UnmanagedBlob = "dataverse-solution-SpaarkeMaster-1.2.0.0-unmanaged.zip";

    // ---------- fresh install ----------

    [Fact]
    public async Task ImportAsync_NotInstalled_Managed_ImportsManagedBlobWithImportSolution()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson()),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncSucceeded)),
        };
        var blob = new FakeBlobs(ManifestJson("1.2.0.0"));
        var importer = BuildImporter(dv, blob);

        var outcome = await importer.ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>().Which.PackageVersion.Should().Be("1.2.0.0");
        blob.Requested.Should().Contain(ManagedBlob).And.NotContain(UnmanagedBlob);

        var post = dv.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post).Which;
        post.Uri.AbsolutePath.Should().EndWith("/ImportSolutionAsync");
        using var doc = JsonDocument.Parse(post.Body!);
        doc.RootElement.GetProperty("OverwriteUnmanagedCustomizations").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("PublishWorkflows").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("CustomizationFile").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("ImportJobId").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("HoldingSolution").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("SkipProductUpdateDependencies").GetBoolean().Should().BeFalse();

        var get = dv.Requests.First(r => r.Method == HttpMethod.Get && r.Uri.AbsolutePath.EndsWith("/solutions", StringComparison.Ordinal));
        Uri.UnescapeDataString(get.Uri.Query).Should().Contain("uniquename eq 'SpaarkeMaster'").And.Contain("ismanaged");
    }

    [Fact]
    public async Task ImportAsync_NotInstalled_Unmanaged_ImportsUnmanagedBlob()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson()),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncSucceeded)),
        };
        var blob = new FakeBlobs(ManifestJson("1.2.0.0"));

        var outcome = await BuildImporter(dv, blob).ImportAsync(BuildRequest(managed: false), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        blob.Requested.Should().Contain(UnmanagedBlob).And.NotContain(ManagedBlob);
    }

    // ---------- upgrade / skip ----------

    [Fact]
    public async Task ImportAsync_OlderManagedVersion_FiresStageAndUpgradeAsync()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncSucceeded)),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        dv.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post)
            .Which.Uri.AbsolutePath.Should().EndWith("/StageAndUpgradeAsync");
    }

    [Theory]
    [InlineData("1.2.0.0", "1.2.0.0")]
    [InlineData("1.2.0.0", "1.2")]
    public async Task ImportAsync_SameVersionOfSameType_SkipsImport(string installed, string package)
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson((installed, true))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson(package)))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post, "an equal version is already installed");
    }

    // ---------- T218b refusals ----------

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ImportAsync_OtherTypeInstalled_RefusesWithPackageTypeMismatch_NoImport(bool installedManaged, bool requestManaged)
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", installedManaged))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: requestManaged), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.PackageTypeMismatch);
        failure.Diagnostic.Should().Contain(installedManaged ? "as managed" : "as unmanaged");
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post, "an environment's package type is never switched");
    }

    [Fact]
    public async Task ImportAsync_NewerVersionInstalled_RefusesDowngrade_NoImport()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.3.0.0", true))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.DowngradeRefused);
        failure.Diagnostic.Should().Contain("1.3.0.0").And.Contain("1.2.0.0");
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    // ---------- import job outcomes ----------

    [Fact]
    public async Task ImportAsync_UpgradeImportJobFails_ReturnsPartialImport()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncFailed)),
            OnImportJobGet = _ => JsonResponse(HttpStatusCode.OK, ImportJobDataJson(FailureDataXml("boom"))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.PartialImport,
            "a managed StageAndUpgrade that Dataverse reports as failed may leave the holding solution behind");
        failure.Diagnostic.Should().Contain("boom");
    }

    [Fact]
    public async Task ImportAsync_FreshImportJobFails_NotPromotedToPartialImport()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson()),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncFailed)),
            OnImportJobGet = _ => JsonResponse(HttpStatusCode.OK, ImportJobDataJson(FailureDataXml("bad zip"))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.UnknownInvocationFailure);
        failure.Diagnostic.Should().Contain("bad zip");
    }

    [Fact]
    public async Task ImportAsync_UpgradePollNeverCompletes_ClassifiedTimeout_NotPromoted()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(InProgress, 20)),
        };
        var importer = BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")),
            importTimeout: TimeSpan.FromMilliseconds(80), pollInterval: TimeSpan.FromMilliseconds(10));

        var outcome = await importer.ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.FailureKind.Should().Be(SolutionImportFailureKind.Timeout,
                "timeout is never promoted to PartialImport — no confirmed failure, resume is safe");
    }

    [Fact]
    public async Task ImportAsync_OlderUnmanagedVersion_UpdatesWithImportSolutionAsync_NotStageAndUpgrade()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", false))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncFailed)),
            OnImportJobGet = _ => JsonResponse(HttpStatusCode.OK, ImportJobDataJson(FailureDataXml("unmanaged boom"))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: false), CancellationToken.None);

        dv.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post)
            .Which.Uri.AbsolutePath.Should().EndWith("/ImportSolutionAsync", "an unmanaged package has no holding solution");
        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.FailureKind.Should().Be(SolutionImportFailureKind.UnknownInvocationFailure,
                "PartialImport (holding solution left behind) applies only to a managed upgrade");
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(401)]
    [InlineData(404)]
    public async Task ImportAsync_TransientPollStatus_IsRetried_ThenSucceeds(int transientStatus)
    {
        var polls = 0;
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => ++polls == 1
                ? new HttpResponseMessage((HttpStatusCode)transientStatus) { Content = new StringContent("busy") }
                : JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncSucceeded)),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        polls.Should().Be(2);
    }

    [Fact]
    public async Task ImportAsync_ManagedUpgradePollThrottledUntilDeadline_IsTimeout_NeverPartialImport()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("throttled") },
        };
        var importer = BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")),
            importTimeout: TimeSpan.FromMilliseconds(80), pollInterval: TimeSpan.FromMilliseconds(10));

        var outcome = await importer.ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.FailureKind.Should().Be(SolutionImportFailureKind.Timeout,
                "an unreadable status is not a failure Dataverse reported — the run must not be quarantined");
    }

    [Fact]
    public async Task ImportAsync_NonTransientPollStatus_FailsWithoutPromotion()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("no") },
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.FailureKind.Should().Be(SolutionImportFailureKind.AuthFailure);
    }

    [Fact]
    public async Task ImportAsync_PollRequestsAFreshTokenEachTime()
    {
        var polls = 0;
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson()),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => ++polls < 3
                ? JsonResponse(HttpStatusCode.OK, AsyncOpJson(InProgress, 20))
                : JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncSucceeded)),
        };
        var credential = new CountingCredential();

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")), credential: credential)
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        credential.Calls.Should().Be(1 + 3, "one token for the setup calls, then one per poll (a long import outlives a token)");
    }

    [Fact]
    public async Task ImportAsync_StartResponseWithoutAsyncOperationId_Fails()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson()),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, "{}"),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("AsyncOperationId");
    }

    [Fact]
    public async Task ImportAsync_PollRequestTimesOutOnce_IsRetried_ThenSucceeds()
    {
        var polls = 0;
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson()),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => ++polls == 1
                ? throw new TaskCanceledException("HttpClient.Timeout elapsed")   // per-request timeout, caller token live
                : JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncSucceeded)),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        polls.Should().Be(2);
    }

    [Fact]
    public async Task ImportAsync_CallerCancelsDuringPoll_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson()),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ =>
            {
                cts.Cancel();
                throw new TaskCanceledException("cancelled by the caller");
            },
        };

        var act = async () => await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ImportAsync_OperationSucceededButJobReportsFailure_IsFailure()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson()),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncSucceeded)),
            OnImportJobGet = _ => JsonResponse(HttpStatusCode.OK, ImportJobDataJson(FailureDataXml("component failed"))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("component failed");
    }

    [Fact]
    public async Task ImportAsync_FailedOperationWithoutJobDetail_SaysSo_NotSuccess()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson()),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncFailed)),
            OnImportJobGet = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var diagnostic = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject.Diagnostic;
        diagnostic.Should().Contain("no import job detail available").And.NotContain("treated as success");
    }

    [Fact]
    public async Task ImportAsync_ManifestVersionNotAVersion_RefusesBeforeImport()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson()),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("latest")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.FailureKind.Should().Be(SolutionImportFailureKind.MissingSolutionZips);
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData((HttpStatusCode)429, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public void IsTransientPollStatus_Classifies(HttpStatusCode status, bool expected)
        => DataverseWebApiSolutionImporter.IsTransientPollStatus(status).Should().Be(expected);

    [Fact]
    public void TryReadAsyncOperationId_ReadsGuidOrNull()
    {
        DataverseWebApiSolutionImporter.TryReadAsyncOperationId(StartedJson()).Should().Be(Guid.Parse(AsyncOperationId));
        DataverseWebApiSolutionImporter.TryReadAsyncOperationId("{}").Should().BeNull();
        DataverseWebApiSolutionImporter.TryReadAsyncOperationId("not json").Should().BeNull();
    }

    // ---------- auth / artifacts ----------

    [Fact]
    public async Task ImportAsync_TokenAcquisitionFails_ReturnsAuthFailure_NoDataverseCallsMade()
    {
        var dv = new FakeDataverseHandler();

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")), throwingCredential: true)
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.FailureKind.Should().Be(SolutionImportFailureKind.AuthFailure);
        dv.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_ManifestBlobNotFound_ReturnsMissingSolutionZips()
    {
        var dv = new FakeDataverseHandler();

        var outcome = await BuildImporter(dv, new FakeBlobs(manifest: null))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.FailureKind.Should().Be(SolutionImportFailureKind.MissingSolutionZips);
        dv.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_ManifestInOldPerSolutionFormat_ReturnsMissingSolutionZipsNamingSpaarkeMaster()
    {
        var dv = new FakeDataverseHandler();
        const string oldFormat = """{"solutions":{"SpaarkeCore":{"blobName":"SpaarkeCore.zip","version":"1.1.0.0"}}}""";

        var outcome = await BuildImporter(dv, new FakeBlobs(oldFormat))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.MissingSolutionZips);
        failure.Diagnostic.Should().Contain("SpaarkeMaster");
        dv.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_NoBlobForRequestedType_NeverFallsBackToTheOtherType()
    {
        var dv = new FakeDataverseHandler();
        var blob = new FakeBlobs(ManifestJson("1.2.0.0", unmanagedBlob: null));

        var outcome = await BuildImporter(dv, blob).ImportAsync(BuildRequest(managed: false), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.MissingSolutionZips);
        failure.Diagnostic.Should().Contain("unmanagedBlobName");
        blob.Requested.Should().NotContain(ManagedBlob);
        dv.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_PackageZipBlobNotFound_ReturnsMissingSolutionZips()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson()),
        };
        var blob = new FakeBlobs(ManifestJson("1.2.0.0")) { ZipMissing = true };

        var outcome = await BuildImporter(dv, blob).ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.FailureKind.Should().Be(SolutionImportFailureKind.MissingSolutionZips);
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ImportAsync_VersionUnknown_RefusesBeforeImport()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson()),
        };
        var blob = new FakeBlobs(ManifestJson(version: null)) { ZipVersion = null };

        var outcome = await BuildImporter(dv, blob).ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.FailureKind.Should().Be(SolutionImportFailureKind.MissingSolutionZips);
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ImportAsync_VersionOnlyInZip_IsUsed()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.2.0.0", true))),
        };
        var blob = new FakeBlobs(ManifestJson(version: null)) { ZipVersion = "1.2.0.0" };

        var outcome = await BuildImporter(dv, blob).ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    // ---------- installed state unreadable → failure, never "assume fresh" ----------

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, SolutionImportFailureKind.UnknownInvocationFailure)]
    [InlineData(HttpStatusCode.Forbidden, SolutionImportFailureKind.AuthFailure)]
    public async Task ImportAsync_InstalledStateUnreadable_FailsWithoutImport(HttpStatusCode status, SolutionImportFailureKind expected)
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => new HttpResponseMessage(status) { Content = new StringContent("nope") },
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Which.FailureKind.Should().Be(expected);
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post,
            "guessing 'fresh install' would bypass the type-switch and downgrade refusals");
    }

    // ---------- T186 audit: first-use propagation (Entra FIC / Dataverse application user) ----------

    private static WorkerCredentialSelectionOptions Propagation(TimeSpan budget) => new()
    {
        EntraPropagationTimeout = budget,
        EntraPropagationRetryInterval = TimeSpan.FromMilliseconds(5),
        DataverseAppUserPropagationTimeout = budget,
        DataverseAppUserPropagationRetryInterval = TimeSpan.FromMilliseconds(5),
    };

    private static FakeDataverseHandler FreshInstallDataverse(Func<HttpRequestMessage, HttpResponseMessage>? solutionsGet = null) => new()
    {
        OnSolutionsGet = solutionsGet ?? (_ => JsonResponse(HttpStatusCode.OK, InstalledJson())),
        OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
        OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncSucceeded)),
    };

    [Theory]
    [InlineData("AADSTS70025")]
    [InlineData("AADSTS700211")]
    [InlineData("AADSTS700016")]
    public async Task ImportAsync_FirstTokenExchangeHitsEntraPropagation_IsWaitedOut_ThenImports(string code)
    {
        var credential = new FlakyCredential(failures: 2, $"{code}: not replicated yet.");
        var dv = FreshInstallDataverse();

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")),
                credential: credential, credentials: Propagation(TimeSpan.FromSeconds(10)))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        credential.Calls.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task ImportAsync_UnrelatedAadstsCode_IsNotRetried_StaysAuthFailure()
    {
        var credential = new FlakyCredential(failures: int.MaxValue, "AADSTS7000215: Invalid client secret provided.");

        var outcome = await BuildImporter(FreshInstallDataverse(), new FakeBlobs(ManifestJson("1.2.0.0")),
                credential: credential, credentials: Propagation(TimeSpan.FromSeconds(10)))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Which;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.AuthFailure);
        failure.Diagnostic.Should().StartWith("Token acquisition failed").And.Contain("AADSTS7000215");
        credential.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ImportAsync_EntraPropagationNeverClears_FailsAsAuthFailureAfterTheBudget()
    {
        var credential = new FlakyCredential(failures: int.MaxValue, "AADSTS70025: no federated identity credentials.");

        var outcome = await BuildImporter(FreshInstallDataverse(), new FakeBlobs(ManifestJson("1.2.0.0")),
                credential: credential, credentials: Propagation(TimeSpan.FromMilliseconds(80)))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Which;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.AuthFailure);
        failure.Diagnostic.Should().StartWith("Token acquisition failed").And.Contain("AADSTS70025");
        credential.Calls.Should().BeGreaterThan(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ImportAsync_FirstDataverseReadRefusedWhileTheAppUserPropagates_IsWaitedOut_ThenImports(HttpStatusCode refused)
    {
        var reads = 0;
        var dv = FreshInstallDataverse(_ => ++reads <= 2
            ? new HttpResponseMessage(refused) { Content = new StringContent("principal not found") }
            : JsonResponse(HttpStatusCode.OK, InstalledJson()));

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")),
                credentials: Propagation(TimeSpan.FromSeconds(10)))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        reads.Should().Be(3);
    }

    [Fact]
    public async Task ImportAsync_FirstDataverseReadForbiddenAfterTheBudget_KeepsTheAuthFailureMapping_NoImport()
    {
        var dv = FreshInstallDataverse(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("nope") });

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")),
                credentials: Propagation(TimeSpan.FromMilliseconds(80)))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Which.FailureKind.Should().Be(SolutionImportFailureKind.AuthFailure);
        dv.Requests.Count(r => r.Uri.AbsolutePath.EndsWith("/solutions", StringComparison.Ordinal)).Should().BeGreaterThan(1);
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ImportAsync_ServerErrorOnFirstRead_IsNotRetried()
    {
        var dv = FreshInstallDataverse(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")),
                credentials: Propagation(TimeSpan.FromSeconds(10)))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.FailureKind.Should().Be(SolutionImportFailureKind.UnknownInvocationFailure);
        dv.Requests.Count(r => r.Uri.AbsolutePath.EndsWith("/solutions", StringComparison.Ordinal)).Should().Be(1);
    }

    // ---------- ISS-032: the import decides, not the async operation; wait on an import in flight ----------

    private static readonly string CleanJobData =
        """<importexportxml><solutionManifest><result result="success" /></solutionManifest></importexportxml>""";

    private static string NowIso(TimeSpan? ago = null) => (DateTimeOffset.UtcNow - (ago ?? TimeSpan.Zero)).ToString("o");

    private static string ImportJobJson(bool completed, string? data = null, double progress = 50)
        => $$"""{"completedon":{{(completed ? $"\"{NowIso()}\"" : "null")}},"startedon":"{{NowIso(TimeSpan.FromMinutes(30))}}","progress":{{progress}},"data":{{(data is null ? "null" : JsonSerializer.Serialize(data))}}}""";

    private static string ImportJobListJson(Guid jobId, TimeSpan startedAgo)
        => $$"""{"value":[{"importjobid":"{{jobId:D}}","createdon":"{{NowIso(startedAgo)}}","startedon":"{{NowIso(startedAgo)}}","completedon":null}]}""";

    /// <summary>History entries of the package version being imported (<c>1.2.0.0</c> in these tests).</summary>
    private static string HistoryJson(params (int Status, bool? Result, TimeSpan StartedAgo)[] entries)
        => HistoryJsonOfVersion("1.2.0.0", entries);

    private static string HistoryJsonOfVersion(string? version, params (int Status, bool? Result, TimeSpan StartedAgo)[] entries)
    {
        var items = entries.Select(e =>
            $$"""{"msdyn_name":"SpaarkeMaster","msdyn_solutionversion":{{(version is null ? "null" : $"\"{version}\"")}},"msdyn_operation":0,"msdyn_suboperation":5,"msdyn_status":{{e.Status}},"msdyn_result":{{(e.Result is null ? "null" : e.Result.Value ? "true" : "false")}},"msdyn_starttime":"{{NowIso(e.StartedAgo)}}","msdyn_endtime":{{(e.Status == 1 ? $"\"{NowIso()}\"" : "null")}},"msdyn_exceptionmessage":null}""");
        return $$"""{"value":[{{string.Join(",", items)}}]}""";
    }

    private static Guid PostedImportJobId(FakeDataverseHandler dv)
    {
        var post = dv.Requests.Single(r => r.Method == HttpMethod.Post);
        using var doc = JsonDocument.Parse(post.Body!);
        return Guid.Parse(doc.RootElement.GetProperty("ImportJobId").GetString()!);
    }

    [Fact]
    public async Task ImportAsync_AsyncOperationFailed_ButImportJobCompletedSuccessfully_IsSuccess()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncFailed)),
            OnImportJobGet = _ => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, CleanJobData)),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>(
            "the import job completed with no failure; the async operation's Failed is advisory");
    }

    [Fact]
    public async Task ImportAsync_AsyncOperationFailedWhileImportStillRuns_WaitsAndSucceedsWhenTheImportCompletes()
    {
        // The ACME 2026-11-22 sequence: Dataverse fails the retried operation while the first attempt is importing.
        var jobReads = 0;
        var started = false;
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ =>
            {
                started = true;
                return JsonResponse(HttpStatusCode.OK, StartedJson());
            },
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncFailed)),
            OnImportJobGet = _ => ++jobReads < 4
                ? JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: false))
                : JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, CleanJobData)),
            // The import's own history entry exists once it has started.
            OnHistoryGet = _ => started
                ? JsonResponse(HttpStatusCode.OK, HistoryJson((0, null, TimeSpan.FromSeconds(10))))
                : JsonResponse(HttpStatusCode.OK, """{"value":[]}"""),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        jobReads.Should().BeGreaterThanOrEqualTo(4, "the importer kept polling the import job after the operation reported Failed");
        dv.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1);
    }

    [Fact]
    public async Task ImportAsync_AsyncOperationFailed_ImportJobMissing_HistoryShowsSuccess_IsSuccess()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncFailed)),
            OnImportJobGet = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            // The failed retry's entry sits beside the first attempt's successful one.
            OnHistoryGet = _ => JsonResponse(HttpStatusCode.OK,
                HistoryJson((1, false, TimeSpan.FromMinutes(1)), (1, true, TimeSpan.FromMinutes(2)))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
    }

    [Fact]
    public async Task ImportAsync_AsyncOperationFailed_ImportJobStillRunning_NothingOpen_KeepsWaiting_NeverEndsOnTheGrace()
    {
        // A readable, not-completed import job is the import running: the grace must not end the wait. Only the
        // overall ImportTimeout bounds a genuinely dead job, and a timeout is never promoted to PartialImport.
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncFailed)),
            OnImportJobGet = _ => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: false)),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")),
                importTimeout: TimeSpan.FromMilliseconds(400), pollInterval: TimeSpan.FromMilliseconds(5), failureGrace: TimeSpan.FromMilliseconds(40))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.FailureKind.Should().Be(SolutionImportFailureKind.Timeout,
                "the running job outlived the grace; only the overall timeout ends it, and a timeout is resumable");
    }

    [Fact]
    public async Task ImportAsync_AsyncOperationFailed_ImportJobUnreadable_HistoryReadableNothingOpen_FailsOnceTheGracePasses()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncFailed)),
            OnImportJobGet = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")),
                importTimeout: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(5), failureGrace: TimeSpan.FromMilliseconds(40))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.PartialImport,
            "a reported failure of a managed upgrade stands once the grace passes with no readable sign of the import running");
    }

    [Fact]
    public async Task ImportAsync_AsyncOperationFailed_ImportJobMissing_HistoryReadFailsOnce_DoesNotCountAsNothingOpen()
    {
        // An unreadable history says nothing: one throttled read must not end the wait as "no open entry".
        var started = false;
        var historyReads = 0;
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ =>
            {
                started = true;
                return JsonResponse(HttpStatusCode.OK, StartedJson());
            },
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncFailed)),
            OnImportJobGet = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            OnHistoryGet = _ => !started
                ? JsonResponse(HttpStatusCode.OK, """{"value":[]}""")
                : ++historyReads == 1
                    ? new HttpResponseMessage((HttpStatusCode)429)
                    : JsonResponse(HttpStatusCode.OK, HistoryJson((1, true, TimeSpan.FromSeconds(30)))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        historyReads.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ImportAsync_HistoryReadFailsAfterTheGrace_WhileTheImportJobRuns_KeepsWaitingAndSucceeds()
    {
        // The real ACME timeline: the async retry failed, the first attempt's job completed 10m44s later; a 429 on the
        // history read after the grace used to read as "nothing open" and ended the wait with PartialImport.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var started = false;
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ =>
            {
                started = true;
                sw.Restart();
                return JsonResponse(HttpStatusCode.OK, StartedJson());
            },
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncFailed)),
            OnImportJobGet = _ => sw.ElapsedMilliseconds < 400
                ? JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: false))
                : JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, CleanJobData)),
            OnHistoryGet = _ => !started
                ? JsonResponse(HttpStatusCode.OK, """{"value":[]}""")
                : sw.ElapsedMilliseconds is > 150 and < 170
                    ? new HttpResponseMessage((HttpStatusCode)429)
                    : JsonResponse(HttpStatusCode.OK, HistoryJson((0, null, TimeSpan.FromSeconds(10)))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")),
                importTimeout: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(5), failureGrace: TimeSpan.FromMilliseconds(100))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>(
            (outcome as SolutionImportOutcome.Failure)?.FailureKind + " :: " + (outcome as SolutionImportOutcome.Failure)?.Diagnostic);
    }

    [Fact]
    public async Task ImportAsync_PriorFailedEntryInsideTheSkewWindow_BeforeTheOwnJobIsVisible_DoesNotDecide()
    {
        // A quickly failed earlier attempt (same version) started 2 minutes ago; this import's own job row (404 on the
        // first read) and own history entry are not visible yet. The history must not decide before the job is seen.
        var jobReads = 0;
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(InProgress, 20)),
            OnImportJobGet = _ => ++jobReads == 1
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: jobReads > 3, jobReads > 3 ? CleanJobData : null)),
            OnHistoryGet = _ => JsonResponse(HttpStatusCode.OK, HistoryJson((1, false, TimeSpan.FromMinutes(2)))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>(
            (outcome as SolutionImportOutcome.Failure)?.FailureKind + " :: " + (outcome as SolutionImportOutcome.Failure)?.Diagnostic);
    }

    [Fact]
    public async Task ImportAsync_StaleSuccessEntryOfAnOlderVersionInsideTheSkewWindow_DoesNotYieldSuccessBeforeTheJobIsSeen()
    {
        // History holds a success of 1.1.0.0 from 2 minutes ago. The import of 1.2.0.0 then fails: the stale success
        // must not turn that into Success (neither before the job is seen nor by version).
        var jobReads = 0;
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(InProgress, 20)),
            OnImportJobGet = _ => ++jobReads == 1
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, FailureDataXml("component failed"))),
            OnHistoryGet = _ => JsonResponse(HttpStatusCode.OK,
                HistoryJsonOfVersion("1.1.0.0", (1, true, TimeSpan.FromMinutes(2)))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.PartialImport);
        failure.Diagnostic.Should().Contain("component failed");
    }

    [Fact]
    public async Task ImportAsync_StaleSuccessOfTheSameVersionInsideTheSkewWindow_DoesNotYieldSuccessBeforeTheJobIsSeen()
    {
        var jobReads = 0;
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(InProgress, 20)),
            OnImportJobGet = _ => ++jobReads == 1
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, FailureDataXml("component failed"))),
            OnHistoryGet = _ => JsonResponse(HttpStatusCode.OK, HistoryJson((1, true, TimeSpan.FromMinutes(2)))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Which.Diagnostic.Should().Contain("component failed");
    }

    [Fact]
    public async Task ImportAsync_HistoryEntryOfADifferentVersion_IsIgnored_EvenWhenTheOperationIsTerminal()
    {
        // Async operation Failed, job never readable, and the only history entry is a success of another version.
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncFailed)),
            OnImportJobGet = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            OnHistoryGet = _ => JsonResponse(HttpStatusCode.OK,
                HistoryJsonOfVersion("1.1.0.0", (1, true, TimeSpan.FromMinutes(1)))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Which.FailureKind.Should().Be(SolutionImportFailureKind.PartialImport);
    }

    [Fact]
    public void SummarizeHistory_WithARequiredVersion_KeepsOnlyEntriesOfThatVersion()
    {
        var since = DateTimeOffset.UtcNow.AddHours(-1);
        var entries = DataverseWebApiSolutionImporter.ParseSolutionHistory(JsonDocument.Parse(
            HistoryJsonOfVersion("1.1.0.0", (1, true, TimeSpan.FromMinutes(2)))).RootElement);

        DataverseWebApiSolutionImporter.SummarizeHistory(entries, since, "1.2.0.0").HasSuccess.Should().BeFalse();
        DataverseWebApiSolutionImporter.SummarizeHistory(entries, since, "1.1").HasSuccess.Should().BeTrue("1.1 == 1.1.0.0");
        DataverseWebApiSolutionImporter.SummarizeHistory(entries, since, null).HasSuccess.Should().BeTrue("no version filter on the attach path");
    }

    [Fact]
    public void SummarizeHistory_UnreadableHistory_IsMarkedUnreadable_NotJustEmpty()
        => DataverseWebApiSolutionImporter.SummarizeHistory(null, DateTimeOffset.UtcNow, "1.2.0.0").Unreadable.Should().BeTrue();

    [Fact]
    public async Task ImportAsync_ImportJobCompletedWithFailure_StillFails_WhateverTheAsyncOperationSays()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncSucceeded)),
            OnImportJobGet = _ => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, FailureDataXml("component failed"))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.PartialImport);
        failure.Diagnostic.Should().Contain("component failed");
    }

    [Fact]
    public async Task ImportAsync_ImportJobSeenThenGone_HistoryShowsOnlyAFailedImport_Fails()
    {
        // History decides an import this run started only after the job has been seen (or the operation is terminal).
        var jobReads = 0;
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(InProgress, 20)),
            OnImportJobGet = _ => ++jobReads == 1
                ? JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: false))
                : new HttpResponseMessage(HttpStatusCode.NotFound),
            OnHistoryGet = _ => JsonResponse(HttpStatusCode.OK, HistoryJson((1, false, TimeSpan.FromMinutes(1)))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.PartialImport);
        failure.Diagnostic.Should().Contain("solution history reports a failed import");
    }

    [Fact]
    public async Task ImportAsync_NoImportInFlight_LooksFirst_ThenStartsNormally()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncSucceeded)),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        var lookup = dv.Requests.FindIndex(r => r.Method == HttpMethod.Get && r.Uri.AbsolutePath.EndsWith("/importjobs", StringComparison.Ordinal));
        var post = dv.Requests.FindIndex(r => r.Method == HttpMethod.Post);
        lookup.Should().BeGreaterThanOrEqualTo(0, "the in-flight lookup runs before any import starts");
        post.Should().BeGreaterThan(lookup);
        Uri.UnescapeDataString(dv.Requests[lookup].Uri.Query).Should().Contain("solutionname eq 'SpaarkeMaster'").And.Contain("completedon eq null");
    }

    [Fact]
    public async Task ImportAsync_ImportJobInFlight_DoesNotStartASecondImport_WaitsOnTheRunningOne()
    {
        var jobId = Guid.NewGuid();
        var jobReads = 0;
        var done = false;
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(done ? ("1.2.0.0", true) : ("1.1.0.0", true))),
            OnImportJobList = _ => JsonResponse(HttpStatusCode.OK, ImportJobListJson(jobId, TimeSpan.FromMinutes(20))),
            OnImportJobGet = _ =>
            {
                if (++jobReads < 3)
                {
                    return JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: false));
                }
                done = true;
                return JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, CleanJobData));
            },
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>().Which.PackageVersion.Should().Be("1.2.0.0");
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post, "an import is already running; a second would collide with it");
        dv.Requests.Where(r => r.Uri.AbsolutePath.Contains("/importjobs(", StringComparison.Ordinal))
            .Should().OnlyContain(r => r.Uri.AbsolutePath.Contains(jobId.ToString("D"), StringComparison.Ordinal));
        jobReads.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task ImportAsync_OpenSolutionHistoryEntryOnly_DoesNotStartASecondImport()
    {
        var historyReads = 0;
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK,
                InstalledJson(historyReads >= 3 ? ("1.2.0.0", true) : ("1.1.0.0", true))),
            OnHistoryGet = _ => ++historyReads < 3
                ? JsonResponse(HttpStatusCode.OK, HistoryJson((0, null, TimeSpan.FromMinutes(20))))
                : JsonResponse(HttpStatusCode.OK, HistoryJson((1, true, TimeSpan.FromMinutes(20)))),
            // The job is not visible through the lookup; once the history shows a completed success, that settles it.
            OnImportJobGet = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ImportAsync_InFlightImportFails_FailsThroughTheExistingPath_NoSecondImport()
    {
        var jobId = Guid.NewGuid();
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportJobList = _ => JsonResponse(HttpStatusCode.OK, ImportJobListJson(jobId, TimeSpan.FromMinutes(20))),
            OnImportJobGet = _ => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, FailureDataXml("upgrade broke"))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.PartialImport);
        failure.Diagnostic.Should().Contain("upgrade broke");
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ImportAsync_InFlightImportCompleted_ButVersionIsStillOlder_ResumableFailure_NoSecondImport()
    {
        var jobId = Guid.NewGuid();
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportJobList = _ => JsonResponse(HttpStatusCode.OK, ImportJobListJson(jobId, TimeSpan.FromMinutes(20))),
            OnImportJobGet = _ => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, CleanJobData)),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.UnknownInvocationFailure);
        failure.Diagnostic.Should().Contain("1.1.0.0").And.Contain("1.2.0.0");
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ImportAsync_AbandonedImportJobOlderThanMaxAge_IsNotInFlight_StartsNormally()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportJobList = _ => JsonResponse(HttpStatusCode.OK, ImportJobListJson(Guid.NewGuid(), TimeSpan.FromHours(12))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(AsyncCompleted, AsyncSucceeded)),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        dv.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ImportAsync_InFlightLookupUnreadable_FailsClosed_NothingStarted()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportJobList = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") },
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Failure>()
            .Which.FailureKind.Should().Be(SolutionImportFailureKind.UnknownInvocationFailure);
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post,
            "an unreadable lookup must not be taken as 'nothing running'");
    }

    [Fact]
    public async Task ImportAsync_Timeout_RecordsTheImportJobIdAndAsyncOperationId()
    {
        var dv = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(InProgress, 20)),
            OnImportJobGet = _ => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: false)),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")),
                importTimeout: TimeSpan.FromMilliseconds(80), pollInterval: TimeSpan.FromMilliseconds(10))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.Timeout);
        failure.Diagnostic.Should()
            .Contain($"importJobId={PostedImportJobId(dv):D}")
            .And.Contain($"asyncOperationId={AsyncOperationId}");
    }

    [Fact]
    public async Task ImportAsync_ResumeAfterTimeout_AttachesToTheRecordedImport_AndReportsSuccessWhenItCompletes()
    {
        // Run 1: the wait times out; the diagnostic names the import job.
        var first = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.1.0.0", true))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, StartedJson()),
            OnAsyncOperationPoll = _ => JsonResponse(HttpStatusCode.OK, AsyncOpJson(InProgress, 20)),
            OnImportJobGet = _ => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: false)),
        };
        var timedOut = await BuildImporter(first, new FakeBlobs(ManifestJson("1.2.0.0")),
                importTimeout: TimeSpan.FromMilliseconds(80), pollInterval: TimeSpan.FromMilliseconds(10))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);
        var diagnostic = timedOut.Should().BeOfType<SolutionImportOutcome.Failure>().Subject.Diagnostic;
        var recorded = Guid.Parse(System.Text.RegularExpressions.Regex.Match(diagnostic, "importJobId=([0-9a-f-]{36})").Groups[1].Value);

        // Run 2 (the resume): Dataverse is still importing that job. Nothing is started; it is waited on to the end.
        var reads = 0;
        var done = false;
        var resumed = new FakeDataverseHandler
        {
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(done ? ("1.2.0.0", true) : ("1.1.0.0", true))),
            OnImportJobList = _ => JsonResponse(HttpStatusCode.OK, ImportJobListJson(recorded, TimeSpan.FromMinutes(121))),
            OnImportJobGet = _ =>
            {
                if (++reads < 3)
                {
                    return JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: false));
                }
                done = true;
                return JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, CleanJobData));
            },
        };
        var outcome = await BuildImporter(resumed, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        resumed.Requests.Should().NotContain(r => r.Method == HttpMethod.Post, "the resume attaches; it never starts another import");
        resumed.Requests.Where(r => r.Uri.AbsolutePath.Contains("/importjobs(", StringComparison.Ordinal))
            .Should().OnlyContain(r => r.Uri.AbsolutePath.Contains(recorded.ToString("D"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportAsync_ResumeAfterTimeout_ImportAlreadyCompleted_ReportsSuccessWithoutImporting()
    {
        var dv = new FakeDataverseHandler
        {
            // The import finished between the timeout and the resume: 1.2.0.0 is installed.
            OnSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, InstalledJson(("1.2.0.0", true))),
        };

        var outcome = await BuildImporter(dv, new FakeBlobs(ManifestJson("1.2.0.0")))
            .ImportAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void SolutionImportOptions_DefaultImportTimeout_Is120Minutes_AndConfigurable()
    {
        new SolutionImportOptions().ImportTimeout.Should().Be(TimeSpan.FromMinutes(120));
        new SolutionImportOptions { ImportTimeout = TimeSpan.FromMinutes(200) }.ImportTimeout
            .Should().Be(TimeSpan.FromMinutes(200));
    }

    [Fact]
    public void SummarizeHistory_AFailedEntryBesideASuccessfulOne_IsStillSuccess_AndOlderEntriesAreIgnored()
    {
        var since = DateTimeOffset.UtcNow.AddHours(-2);
        var entries = DataverseWebApiSolutionImporter.ParseSolutionHistory(JsonDocument.Parse(
            HistoryJson(
                (1, false, TimeSpan.FromMinutes(5)),    // the retried operation's failed entry
                (1, true, TimeSpan.FromMinutes(90)),    // the first attempt, completed
                (0, null, TimeSpan.FromHours(30)))      // an old open entry from before this import
            ).RootElement);

        var summary = DataverseWebApiSolutionImporter.SummarizeHistory(entries, since);

        summary.HasSuccess.Should().BeTrue();
        summary.HasFailure.Should().BeTrue();
        summary.HasOpen.Should().BeFalse("the open entry started before this import");
    }

    [Fact]
    public void ParseSolutionHistory_SkipsOtherSolutionsAndNonImportOperations()
    {
        const string json = """
            {"value":[
              {"msdyn_name":"OtherSolution","msdyn_operation":0,"msdyn_status":0},
              {"msdyn_name":"SpaarkeMaster","msdyn_operation":2,"msdyn_status":0},
              {"msdyn_name":"SpaarkeMaster","msdyn_operation":0,"msdyn_status":2}
            ]}
            """;

        var entries = DataverseWebApiSolutionImporter.ParseSolutionHistory(JsonDocument.Parse(json).RootElement);

        entries.Should().ContainSingle().Which.IsOpen.Should().BeTrue("status 2 is Queued");
    }

    // ---------- pure parsers ----------

    [Fact]
    public void ParsePackageEntry_OnTheSampleTheCiPublisherProduces_ReadsAllThreeFields()
    {
        // T218d: publish-dataverse-solutions-manifest.yml writes the manifest with New-SpaarkeMasterManifest
        // (scripts/solution-authoring/SpaarkePackageScope.psm1), whose Pester test pins its shape to this same
        // sample — so producer and consumer cannot drift apart unnoticed.
        var path = Path.Combine(RepoRoot(), "src", "server", "services", "Sprk.Provisioning.ControlPlane.Tests",
            "Fixtures", "spaarkemaster-manifest.sample.json");

        var entry = DataverseWebApiSolutionImporter.ParsePackageEntry(File.ReadAllText(path), "sample");

        entry.Version.Should().Be("1.2.0.0");
        entry.ManagedBlobName.Should().Be("dataverse-solution-SpaarkeMaster-1.2.0.0-managed.zip");
        entry.UnmanagedBlobName.Should().Be("dataverse-solution-SpaarkeMaster-1.2.0.0-unmanaged.zip");
    }

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory (parity with ArmTemplateInspectorTests).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var gitMarker = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker)) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate the repo root walking up from '{AppContext.BaseDirectory}'.");
    }

    [Fact]
    public void ParsePackageEntry_ReadsAllThreeFields()
    {
        var entry = DataverseWebApiSolutionImporter.ParsePackageEntry(ManifestJson("1.2.0.0"), "m.json");

        entry.Version.Should().Be("1.2.0.0");
        entry.ManagedBlobName.Should().Be(ManagedBlob);
        entry.UnmanagedBlobName.Should().Be(UnmanagedBlob);
    }

    [Fact]
    public void ParseInstalledPackage_AbsentAndPresent()
    {
        DataverseWebApiSolutionImporter.ParseInstalledPackage(InstalledJson())
            .Should().BeOfType<DataverseWebApiSolutionImporter.InstalledPackage.Absent>();
        DataverseWebApiSolutionImporter.ParseInstalledPackage(InstalledJson(("1.1.0.0", true)))
            .Should().Be(new DataverseWebApiSolutionImporter.InstalledPackage.Present("1.1.0.0", true));
    }

    [Theory]
    [InlineData("1.2.0.0", "1.2.0.0", 0)]
    [InlineData("1.2", "1.2.0.0", 0)]
    [InlineData("1.10.0.0", "1.9.0.0", 1)]
    [InlineData("1.2.0.0", "1.2.0.1", -1)]
    public void CompareVersions_NumericAndPadded(string left, string right, int expected)
        => SpaarkePackage.CompareVersions(left, right).Should().Be(expected);

    [Fact]
    public void CompareVersions_NotAVersion_ReturnsNull()
        => SpaarkePackage.CompareVersions("latest", "1.2.0.0").Should().BeNull();

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "", SolutionImportFailureKind.AuthFailure)]
    [InlineData(HttpStatusCode.Forbidden, "", SolutionImportFailureKind.AuthFailure)]
    [InlineData(HttpStatusCode.BadRequest, "access is denied", SolutionImportFailureKind.AuthFailure)]
    [InlineData((HttpStatusCode)429, "", SolutionImportFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, "throttled by Dataverse", SolutionImportFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, "storage limit reached", SolutionImportFailureKind.QuotaExhausted)]
    [InlineData(HttpStatusCode.InternalServerError, "unexpected", SolutionImportFailureKind.UnknownInvocationFailure)]
    public void ClassifyHttpFailure_MapsExpectedFailureKind(HttpStatusCode statusCode, string body, SolutionImportFailureKind expected)
        => DataverseWebApiSolutionImporter.ClassifyHttpFailure(statusCode, body).Should().Be(expected);

    [Fact]
    public void EvaluateImportJobData_ExplicitFailure_ReturnsFalseWithErrorText()
    {
        var (success, diagnostic) = DataverseWebApiSolutionImporter.EvaluateImportJobData(FailureDataXml("dependency missing"));
        success.Should().BeFalse();
        diagnostic.Should().Contain("dependency missing");
    }

    [Fact]
    public void EvaluateImportJobData_WarningOnly_ReturnsTrueWithWarningNoted()
    {
        var data = """<importexportxml><solutionManifest><result result="warning" errortext="deprecated component" /></solutionManifest></importexportxml>""";
        var (success, diagnostic) = DataverseWebApiSolutionImporter.EvaluateImportJobData(data);
        success.Should().BeTrue();
        diagnostic.Should().Contain("deprecated component");
    }

    [Fact]
    public void EvaluateImportJobData_CleanSuccess_ReturnsTrueEmptyDiagnostic()
    {
        var data = """<importexportxml><solutionManifest><result result="success" /></solutionManifest></importexportxml>""";
        var (success, diagnostic) = DataverseWebApiSolutionImporter.EvaluateImportJobData(data);
        success.Should().BeTrue();
        diagnostic.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateImportJobData_EmptyData_TreatedAsProvisionalSuccess()
    {
        var (success, diagnostic) = DataverseWebApiSolutionImporter.EvaluateImportJobData(null);
        success.Should().BeTrue();
        diagnostic.Should().Contain("independent post-import verifier");
    }

    [Fact]
    public void EvaluateImportJobData_UnparseableXml_TreatedAsProvisionalSuccess_NotSilentlySwallowed()
    {
        var (success, diagnostic) = DataverseWebApiSolutionImporter.EvaluateImportJobData("<not-well-formed");
        success.Should().BeTrue();
        diagnostic.Should().Contain("could not be parsed", "an unparseable data field must be explicitly noted, not silently treated as clean");
    }

    [Fact]
    public void TryReadSolutionVersionFromZip_ValidZip_ReturnsVersion()
        => DataverseWebApiSolutionImporter.TryReadSolutionVersionFromZip(BuildSolutionZip("2.1.3.7")).Should().Be("2.1.3.7");

    [Fact]
    public void TryReadSolutionVersionFromZip_MissingSolutionXml_ReturnsNull()
    {
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("other.txt");
        }
        DataverseWebApiSolutionImporter.TryReadSolutionVersionFromZip(ms.ToArray()).Should().BeNull();
    }

    [Fact]
    public void TryReadSolutionVersionFromZip_CorruptBytes_ReturnsNullWithoutThrowing()
    {
        var act = () => DataverseWebApiSolutionImporter.TryReadSolutionVersionFromZip(new byte[] { 1, 2, 3, 4 });
        act.Should().NotThrow();
        DataverseWebApiSolutionImporter.TryReadSolutionVersionFromZip(new byte[] { 1, 2, 3, 4 }).Should().BeNull();
    }

    [Fact]
    public void ProductionSource_ContainsNoPacSolutionOrProcessStartInfoReferences()
    {
        var text = File.ReadAllText(LocateSourceFile("DataverseWebApiSolutionImporter.cs"));
        text.Should().NotContain("pac solution");
        text.Should().NotContain("ProcessStartInfo");
    }

    // ---------- helpers ----------

    private static SolutionImportRequest BuildRequest(bool managed) => new(
        CustomerId: CustomerId,
        TenantId: TenantId,
        ClientId: ClientId,
        ClientSecret: ClientSecret,
        TargetDataverseUrl: EnvUrl,
        Managed: managed);

    private static DataverseWebApiSolutionImporter BuildImporter(
        FakeDataverseHandler dvHandler,
        FakeBlobs blobs,
        bool throwingCredential = false,
        TimeSpan? importTimeout = null,
        TimeSpan? pollInterval = null,
        TimeSpan? failureGrace = null,
        TokenCredential? credential = null,
        WorkerCredentialSelectionOptions? credentials = null)
    {
        TokenCredential Factory(string tenantId, string clientId, string clientSecret)
            => credential ?? (throwingCredential ? new ThrowingCredential() : new FakeCredential());

        return new DataverseWebApiSolutionImporter(
            new HttpClient(dvHandler),
            ArmSdkTestFakes.NewBlobContainerClient((FakeArmHttpMessageHandler)blobs.Handler),
            Options.Create(new SolutionImportOptions
            {
                ProvisioningArtifactsContainerUri = "https://faketest.blob.core.windows.net/provisioning-artifacts",
                SolutionArtifactManifestBlobName = "dataverse-solutions-latest.json",
                ImportTimeout = importTimeout ?? TimeSpan.FromSeconds(5),
                ImportJobPollInterval = pollInterval ?? TimeSpan.FromMilliseconds(5),
                AsyncOperationFailureGrace = failureGrace ?? TimeSpan.FromMinutes(10),
                // T186 audit: the propagation waits are OFF unless a test opts in, so the existing 401/403 and
                // token-failure cases keep their immediate outcomes.
                Credentials = credentials ?? new WorkerCredentialSelectionOptions
                {
                    EntraPropagationTimeout = TimeSpan.Zero,
                    DataverseAppUserPropagationTimeout = TimeSpan.Zero,
                },
            }),
            NullLogger<DataverseWebApiSolutionImporter>.Instance,
            TimeProvider.System,
            Factory);
    }

    /// <summary>Fake provisioning-artifacts container: the manifest (or 404) and the package ZIPs; records blob names asked for.</summary>
    private sealed class FakeBlobs
    {
        public FakeBlobs(string? manifest)
        {
            Handler = ArmSdkTestFakes.NewHandler(req =>
            {
                var name = Uri.UnescapeDataString(req.RequestUri!.AbsolutePath.Split('/').Last());
                Requested.Add(name);
                if (name == "dataverse-solutions-latest.json")
                {
                    return manifest is null
                        ? ArmSdkTestFakes.JsonResponse(HttpStatusCode.NotFound, ArmSdkTestFakes.ArmErrorBody("BlobNotFound", "not found"))
                        : ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, manifest);
                }
                if (ZipMissing)
                {
                    return ArmSdkTestFakes.JsonResponse(HttpStatusCode.NotFound, ArmSdkTestFakes.ArmErrorBody("BlobNotFound", "not found"));
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(BuildSolutionZip(ZipVersion)) };
            });
        }

        public HttpMessageHandler Handler { get; }
        public List<string> Requested { get; } = new();
        public bool ZipMissing { get; init; }
        public string? ZipVersion { get; init; } = "1.2.0.0";
    }

    private static byte[] BuildSolutionZip(string? version)
    {
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("solution.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(version is null
                ? "<ImportExportXml><SolutionManifest /></ImportExportXml>"
                : $"<ImportExportXml><SolutionManifest><Version>{version}</Version></SolutionManifest></ImportExportXml>");
        }
        return ms.ToArray();
    }

    private static string ManifestJson(string? version, string? managedBlob = ManagedBlob, string? unmanagedBlob = UnmanagedBlob)
    {
        var fields = new List<string>();
        if (version is not null) fields.Add($"\"version\":\"{version}\"");
        if (managedBlob is not null) fields.Add($"\"managedBlobName\":\"{managedBlob}\"");
        if (unmanagedBlob is not null) fields.Add($"\"unmanagedBlobName\":\"{unmanagedBlob}\"");
        return "{\"solutions\":{\"SpaarkeMaster\":{" + string.Join(",", fields) + "}}}";
    }

    private static string InstalledJson(params (string Version, bool IsManaged)[] entries)
    {
        var items = entries.Select(e =>
            $$"""{"uniquename":"SpaarkeMaster","version":"{{e.Version}}","ismanaged":{{(e.IsManaged ? "true" : "false")}}}""");
        return $$"""{"value":[{{string.Join(",", items)}}]}""";
    }

    private const string AsyncOperationId = "99999999-8888-7777-6666-555555555555";
    private const int InProgress = 2;
    private const int AsyncCompleted = 3;
    private const int AsyncSucceeded = 30;
    private const int AsyncFailed = 31;

    private static string StartedJson()
        => $$"""{"AsyncOperationId":"{{AsyncOperationId}}","ImportJobKey":"k"}""";

    private static string AsyncOpJson(int stateCode, int statusCode)
        => $$"""{"statecode":{{stateCode}},"statuscode":{{statusCode}},"message":null}""";

    private static string ImportJobDataJson(string? data)
        => $$"""{"data":{{(data is null ? "null" : JsonSerializer.Serialize(data))}}}""";

    private static string FailureDataXml(string errorText)
        => $"""<importexportxml><solutionManifest><result result="failure" errortext="{errorText}" /></solutionManifest></importexportxml>""";

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        => new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string LocateSourceFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName,
                "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core",
                "Handlers", "SolutionImport", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate {fileName} by walking up from {AppContext.BaseDirectory}.");
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-dataverse-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class CountingCredential : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            return new($"token-{Calls}", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class FlakyCredential(int failures, string message) : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            return Calls <= failures
                ? throw new Azure.Identity.AuthenticationFailedException(message)
                : new AccessToken("fake-dataverse-test-token", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class ThrowingCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated credential chain failure");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated credential chain failure");
    }

    /// <summary>
    /// Hand-rolled fake <see cref="HttpMessageHandler"/> (NOT Mock&lt;HttpMessageHandler&gt; — banned per testing.md)
    /// that routes requests to solutions-GET / import-action / asyncoperations-poll / importjobs-detail delegates.
    /// </summary>
    private sealed class FakeDataverseHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? OnSolutionsGet { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnImportPost { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnAsyncOperationPoll { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnImportJobGet { get; set; }

        /// <summary>GET importjobs?$filter=... (the in-flight and attach lookups). Default: no rows.</summary>
        public Func<HttpRequestMessage, HttpResponseMessage>? OnImportJobList { get; set; }

        /// <summary>GET msdyn_solutionhistories. Default: no rows.</summary>
        public Func<HttpRequestMessage, HttpResponseMessage>? OnHistoryGet { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add((request.Method, request.RequestUri!, body));

            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post
                && (path.EndsWith("/ImportSolutionAsync", StringComparison.Ordinal) || path.EndsWith("/StageAndUpgradeAsync", StringComparison.Ordinal)))
            {
                return (OnImportPost ?? throw new InvalidOperationException("unexpected import POST — no OnImportPost wired"))(request);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/asyncoperations(", StringComparison.Ordinal))
            {
                return (OnAsyncOperationPoll ?? throw new InvalidOperationException("unexpected asyncoperations poll — no OnAsyncOperationPoll wired"))(request);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/importjobs(", StringComparison.Ordinal))
            {
                return OnImportJobGet?.Invoke(request) ?? JsonResponse(HttpStatusCode.OK, ImportJobDataJson(null));
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/importjobs", StringComparison.Ordinal))
            {
                return OnImportJobList?.Invoke(request) ?? JsonResponse(HttpStatusCode.OK, """{"value":[]}""");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/msdyn_solutionhistories", StringComparison.Ordinal))
            {
                return OnHistoryGet?.Invoke(request) ?? JsonResponse(HttpStatusCode.OK, """{"value":[]}""");
            }

            if (request.Method == HttpMethod.Get && path.Contains("/solutions", StringComparison.Ordinal))
            {
                return (OnSolutionsGet ?? throw new InvalidOperationException("unexpected solutions GET — no OnSolutionsGet wired"))(request);
            }

            throw new InvalidOperationException($"unexpected request: {request.Method} {request.RequestUri}");
        }
    }
}
