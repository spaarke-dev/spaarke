// -----------------------------------------------------------------------------
// DataverseWebApiSolutionImporterTests.cs
//
// L2 CONTROL-PLANE unit tests for DataverseWebApiSolutionImporter (task 141,
// Wave G-4 — H6 Web-API import port).
//
// ADR-038 alignment: pure C# unit tests over a real HttpClient wrapping a
// hand-rolled fake HttpMessageHandler (NOT Mock&lt;HttpMessageHandler&gt;,
// banned per testing.md) for the Dataverse Web API surface, PLUS the shared
// ArmSdkTestFakes.NewBlobContainerClient fake-transport helper (task 123 —
// extend, don't duplicate, per CLAUDE.md §11) for the artifact-manifest /
// solution-ZIP blob surface. Polling-loop timing tests use TimeProvider.System
// with tiny real TimeSpans — same convention as BapRestEnvironmentCreatorTests
// (task 140) / H5DataverseEnvCreationHandlerTests T13.
//
// COVERAGE (maps to POML acceptance criteria):
//   T1  Happy path — 3-tier stub catalog, all fresh installs -> ImportSolution
//       POST fired per solution with the correct body shape
//       (OverwriteUnmanagedCustomizations/PublishWorkflows/CustomizationFile/
//       ImportJobId/HoldingSolution=false), importjobs poll returns
//       completedon immediately -> Success.
//   T2  Upgrade path — existing solution present at an OLDER version ->
//       StageAndUpgrade action fired (not ImportSolution).
//   T3  Already-at-version skip — existing version matches manifest version
//       -> NO import POST fired for that solution — acceptance criterion b.
//   T4  ImportJob explicit failure (data XML result="failure") after a PRIOR
//       solution already succeeded -> Failure(PartialImport) — the
//       ImportJob failure-parsing + promotion path.
//   T5  ImportJob explicit failure on the FIRST solution (nothing imported
//       yet) -> Failure(UnknownInvocationFailure), NOT promoted.
//   T6  Polling-timeout — importjobs poll never returns completedon within a
//       tiny ImportTimeout -> Failure(Timeout), verified NOT promoted to
//       PartialImport even though a prior solution already succeeded.
//   T7  Token acquisition failure -> Failure(AuthFailure), zero Dataverse
//       HTTP calls made.
//   T8  Artifact manifest blob not found (404) -> Failure(MissingSolutionZips).
//   T9  Manifest missing an entry for a specific solution ->
//       Failure(MissingSolutionZips) naming the solution.
//   T10 Solution ZIP blob not found (404) -> Failure(MissingSolutionZips).
//   T11 Existing-solutions query failure (500) -> catch-and-proceed with a
//       fresh-import assumption (parity with the retired PS script's
//       Get-ExistingSolutions behavior) — overall import still succeeds.
//   T12 Per-tier verification gate — POST+poll succeed but the post-tier
//       existing-solutions re-query omits the solution -> Failure(PartialImport).
//   T13 ClassifyHttpFailure theory (pure classifier — status+body -> kind).
//   T14 EvaluateImportJobData theory (pure — the ImportJob failure/warning-
//       parsing function): explicit failure, warning-only success, clean
//       success, empty data, unparseable XML.
//   T15 TryReadSolutionVersionFromZip — valid zip returns version; zip
//       without solution.xml returns null; corrupt bytes returns null
//       (never throws).
//   T16 Source grep defense-in-depth — the production file contains neither
//       "pac solution" nor "ProcessStartInfo" (acceptance criterion #1).
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    // 3-tier stub catalog (parity in shape with CanonicalSolutionCatalog's
    // Tier1/Tier2/Tier3 grouping, compact for test readability).
    private static ImmutableArray<CanonicalSolutionEntry> ThreeTierCatalog() => ImmutableArray.Create(
        new CanonicalSolutionEntry("SolA", "SolA", "Solution A", Tier: 1),
        new CanonicalSolutionEntry("SolB", "SolB", "Solution B", Tier: 2),
        new CanonicalSolutionEntry("SolC", "SolC", "Solution C", Tier: 3));

    // ---------- T1 happy path ----------

    [Fact]
    public async Task ImportAsync_AllFreshInstalls_ImportSolutionActionFiredWithCorrectBodyShape_ReturnsSuccess()
    {
        // Dynamic "installed" state — the per-tier verification gate re-queries
        // existing solutions after each tier, so the fake must reflect each
        // solution as present immediately after its own import POST fires
        // (matches the PS script's own per-tier Test-TierImport gate semantics).
        var installed = new Dictionary<string, string>(StringComparer.Ordinal);
        var orderedNames = new[] { "SolA", "SolB", "SolC" };
        var postCount = 0;
        var dv = new FakeDataverseHandler
        {
            OnExistingSolutionsGet = _ => JsonResponse(HttpStatusCode.OK,
                SolutionsListJson(installed.Select(kv => (kv.Key, kv.Value)).ToArray())),
            OnImportPost = _ =>
            {
                installed[orderedNames[postCount]] = "1.0.0.0";
                postCount++;
                return JsonResponse(HttpStatusCode.OK, "{}");
            },
            OnImportJobPoll = (_, _) => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, data: null)),
        };
        var blob = FakeBlobHandler(manifest: ManifestJson(ThreeTierCatalog(), "1.0.0.0"), zipsVersion: "1.0.0.0");
        var importer = BuildImporter(dv, blob, ThreeTierCatalog());

        var outcome = await importer.ImportAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();

        var posts = dv.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        posts.Should().HaveCount(3);
        posts.Should().OnlyContain(r => r.Uri.AbsolutePath.EndsWith("/ImportSolution", StringComparison.Ordinal));

        using var doc = JsonDocument.Parse(posts[0].Body!);
        doc.RootElement.GetProperty("OverwriteUnmanagedCustomizations").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("PublishWorkflows").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("CustomizationFile").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("ImportJobId").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("HoldingSolution").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("SkipProductUpdateDependencies").GetBoolean().Should().BeFalse();
    }

    // ---------- T2 upgrade path ----------

    [Fact]
    public async Task ImportAsync_ExistingSolutionAtOlderVersion_FiresStageAndUpgradeAction()
    {
        var dv = new FakeDataverseHandler
        {
            OnExistingSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, SolutionsListJson(("SolA", "0.9.0.0"))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, "{}"),
            OnImportJobPoll = (_, _) => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, data: null)),
        };
        var catalog = ImmutableArray.Create(new CanonicalSolutionEntry("SolA", "SolA", "Solution A", Tier: 1));
        var blob = FakeBlobHandler(manifest: ManifestJson(catalog, "1.0.0.0"), zipsVersion: "1.0.0.0");
        var importer = BuildImporter(dv, blob, catalog);

        var outcome = await importer.ImportAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        var post = dv.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post).Which;
        post.Uri.AbsolutePath.Should().EndWith("/StageAndUpgrade");
    }

    // ---------- T3 already-at-version skip ----------

    [Fact]
    public async Task ImportAsync_ExistingSolutionAtSameVersion_SkipsImportCall()
    {
        var dv = new FakeDataverseHandler
        {
            OnExistingSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, SolutionsListJson(("SolA", "1.0.0.0"))),
        };
        var catalog = ImmutableArray.Create(new CanonicalSolutionEntry("SolA", "SolA", "Solution A", Tier: 1));
        var blob = FakeBlobHandler(manifest: ManifestJson(catalog, "1.0.0.0"), zipsVersion: "1.0.0.0");
        var importer = BuildImporter(dv, blob, catalog);

        var outcome = await importer.ImportAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        dv.Requests.Should().NotContain(r => r.Method == HttpMethod.Post, "already-at-version solutions must not be re-imported");
    }

    // ---------- T4 ImportJob failure AFTER a prior success -> PartialImport ----------

    [Fact]
    public async Task ImportAsync_SecondSolutionImportJobFails_AfterFirstSucceeded_ReturnsPartialImport()
    {
        var installed = new Dictionary<string, string>(StringComparer.Ordinal);
        var postCount = 0;
        var dv = new FakeDataverseHandler
        {
            OnExistingSolutionsGet = _ => JsonResponse(HttpStatusCode.OK,
                SolutionsListJson(installed.Select(kv => (kv.Key, kv.Value)).ToArray())),
            OnImportPost = _ =>
            {
                // Only SolA's (first) import is reflected as installed — SolB's
                // import job will report failure below, so it never lands.
                if (postCount == 0)
                {
                    installed["SolA"] = "1.0.0.0";
                }
                postCount++;
                return JsonResponse(HttpStatusCode.OK, "{}");
            },
            OnImportJobPoll = (_, _) =>
            {
                // First import (SolA) succeeds; second (SolB) fails.
                var data = postCount >= 2 ? FailureDataXml("boom") : null;
                return JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, data: data));
            },
        };
        var catalog = ImmutableArray.Create(
            new CanonicalSolutionEntry("SolA", "SolA", "Solution A", Tier: 1),
            new CanonicalSolutionEntry("SolB", "SolB", "Solution B", Tier: 2));
        var blob = FakeBlobHandler(manifest: ManifestJson(catalog, "1.0.0.0"), zipsVersion: "1.0.0.0");
        var importer = BuildImporter(dv, blob, catalog);

        var outcome = await importer.ImportAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.PartialImport);
        failure.Diagnostic.Should().Contain("boom");
    }

    // ---------- T5 ImportJob failure on the FIRST solution -> not promoted ----------

    [Fact]
    public async Task ImportAsync_FirstSolutionImportJobFails_NotPromotedToPartialImport()
    {
        var dv = new FakeDataverseHandler
        {
            OnExistingSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, SolutionsListJson()),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, "{}"),
            OnImportJobPoll = (_, _) => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, data: FailureDataXml("bad zip"))),
        };
        var catalog = ImmutableArray.Create(new CanonicalSolutionEntry("SolA", "SolA", "Solution A", Tier: 1));
        var blob = FakeBlobHandler(manifest: ManifestJson(catalog, "1.0.0.0"), zipsVersion: "1.0.0.0");
        var importer = BuildImporter(dv, blob, catalog);

        var outcome = await importer.ImportAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.UnknownInvocationFailure);
        failure.Diagnostic.Should().Contain("bad zip");
    }

    // ---------- T6 polling timeout — never promoted ----------

    [Fact]
    public async Task ImportAsync_PollNeverCompletes_ClassifiedTimeout_NotPromotedToPartialImport()
    {
        var dv = new FakeDataverseHandler
        {
            OnExistingSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, SolutionsListJson(("SolA", "1.0.0.0"))),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, "{}"),
            OnImportJobPoll = (_, _) => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: false, data: null)),
        };
        var catalog = ImmutableArray.Create(
            new CanonicalSolutionEntry("SolA", "SolA", "Solution A", Tier: 1), // already-at-version, no POST
            new CanonicalSolutionEntry("SolB", "SolB", "Solution B", Tier: 2)); // never completes
        var blob = FakeBlobHandler(manifest: ManifestJson(catalog, "1.0.0.0"), zipsVersion: "1.0.0.0");
        var importer = BuildImporter(
            dv, blob, catalog,
            importTimeout: TimeSpan.FromMilliseconds(80),
            pollInterval: TimeSpan.FromMilliseconds(10));

        var outcome = await importer.ImportAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.Timeout,
            "timeout is never promoted to PartialImport — no confirmed failure, resume is safe (idempotent).");
    }

    // ---------- T7 token acquisition failure ----------

    [Fact]
    public async Task ImportAsync_TokenAcquisitionFails_ReturnsAuthFailure_NoDataverseCallsMade()
    {
        var dv = new FakeDataverseHandler();
        var catalog = ThreeTierCatalog();
        var blob = FakeBlobHandler(manifest: ManifestJson(catalog, "1.0.0.0"), zipsVersion: "1.0.0.0");
        var importer = BuildImporter(dv, blob, catalog, throwingCredential: true);

        var outcome = await importer.ImportAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.AuthFailure);
        dv.Requests.Should().BeEmpty();
    }

    // ---------- T8 manifest not found ----------

    [Fact]
    public async Task ImportAsync_ManifestBlobNotFound_ReturnsMissingSolutionZips()
    {
        var dv = new FakeDataverseHandler();
        var blob = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.NotFound, ArmSdkTestFakes.ArmErrorBody("BlobNotFound", "not found")));
        var importer = BuildImporter(dv, blob, ThreeTierCatalog());

        var outcome = await importer.ImportAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.MissingSolutionZips);
        dv.Requests.Should().BeEmpty();
    }

    // ---------- T9 manifest missing a specific solution entry ----------

    [Fact]
    public async Task ImportAsync_ManifestMissingSolutionEntry_ReturnsMissingSolutionZips()
    {
        var dv = new FakeDataverseHandler
        {
            // SolA is already at the manifest's version — it is SKIPPED
            // (no import POST needed), so the manifest-lookup failure for
            // SolB (Tier 2) is reached without requiring OnImportPost/
            // OnImportJobPoll to be wired for this test.
            OnExistingSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, SolutionsListJson(("SolA", "1.0.0.0"))),
        };
        var catalog = ThreeTierCatalog();
        // Manifest only has SolA — SolB/SolC are absent.
        var partialCatalog = ImmutableArray.Create(catalog[0]);
        var blob = FakeBlobHandler(manifest: ManifestJson(partialCatalog, "1.0.0.0"), zipsVersion: "1.0.0.0");
        var importer = BuildImporter(dv, blob, catalog);

        var outcome = await importer.ImportAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.MissingSolutionZips);
        failure.Diagnostic.Should().Contain("SolB");
    }

    // ---------- T10 zip blob 404 ----------

    [Fact]
    public async Task ImportAsync_SolutionZipBlobNotFound_ReturnsMissingSolutionZips()
    {
        var dv = new FakeDataverseHandler
        {
            OnExistingSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, SolutionsListJson()),
        };
        var catalog = ImmutableArray.Create(new CanonicalSolutionEntry("SolA", "SolA", "Solution A", Tier: 1));
        var manifestJson = ManifestJson(catalog, "1.0.0.0");
        var blob = ArmSdkTestFakes.NewHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("dataverse-solutions-latest.json", StringComparison.Ordinal))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, manifestJson);
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.NotFound, ArmSdkTestFakes.ArmErrorBody("BlobNotFound", "not found"));
        });
        var importer = BuildImporter(dv, blob, catalog);

        var outcome = await importer.ImportAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.MissingSolutionZips);
    }

    // ---------- T11 existing-solutions query failure -> catch-and-proceed ----------

    [Fact]
    public async Task ImportAsync_InitialExistingSolutionsQueryFails_ProceedsWithFreshImportAssumption()
    {
        // Only the FIRST (pre-import snapshot) existing-solutions call fails —
        // the PS script's own Get-ExistingSolutions catch-and-proceed applies
        // to that pre-check specifically. The per-tier POST-import
        // verification gate is a stricter, PS-Test-TierImport-parity check
        // that must NOT silently pass on a query failure (a query failure
        // there is indistinguishable from "solution genuinely absent" and the
        // PS script itself treats it as a verification failure) — so the
        // second call must succeed for this scenario to reach Success.
        var callCount = 0;
        var dv = new FakeDataverseHandler
        {
            OnExistingSolutionsGet = _ =>
            {
                callCount++;
                return callCount == 1
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : JsonResponse(HttpStatusCode.OK, SolutionsListJson(("SolA", "1.0.0.0")));
            },
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, "{}"),
            OnImportJobPoll = (_, _) => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, data: null)),
        };
        var catalog = ImmutableArray.Create(new CanonicalSolutionEntry("SolA", "SolA", "Solution A", Tier: 1));
        var blob = FakeBlobHandler(manifest: ManifestJson(catalog, "1.0.0.0"), zipsVersion: "1.0.0.0");
        var importer = BuildImporter(dv, blob, catalog);

        var outcome = await importer.ImportAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SolutionImportOutcome.Success>();
        dv.Requests.Should().Contain(r => r.Method == HttpMethod.Post,
            "a pre-import query failure must fall back to fresh-import, not abort");
    }

    // ---------- T12 per-tier verification gate ----------

    [Fact]
    public async Task ImportAsync_PostTierVerificationOmitsSolution_ReturnsPartialImport()
    {
        var dv = new FakeDataverseHandler
        {
            // Existing-solutions query ALWAYS returns empty — the just-imported
            // solution never shows up in the post-tier re-query.
            OnExistingSolutionsGet = _ => JsonResponse(HttpStatusCode.OK, SolutionsListJson()),
            OnImportPost = _ => JsonResponse(HttpStatusCode.OK, "{}"),
            OnImportJobPoll = (_, _) => JsonResponse(HttpStatusCode.OK, ImportJobJson(completed: true, data: null)),
        };
        var catalog = ImmutableArray.Create(new CanonicalSolutionEntry("SolA", "SolA", "Solution A", Tier: 1));
        var blob = FakeBlobHandler(manifest: ManifestJson(catalog, "1.0.0.0"), zipsVersion: "1.0.0.0");
        var importer = BuildImporter(dv, blob, catalog);

        var outcome = await importer.ImportAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SolutionImportOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(SolutionImportFailureKind.PartialImport);
        failure.Diagnostic.Should().Contain("SolA");
    }

    // ---------- T13 ClassifyHttpFailure theory ----------

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "", SolutionImportFailureKind.AuthFailure)]
    [InlineData(HttpStatusCode.Forbidden, "", SolutionImportFailureKind.AuthFailure)]
    [InlineData(HttpStatusCode.BadRequest, "access is denied", SolutionImportFailureKind.AuthFailure)]
    [InlineData((HttpStatusCode)429, "", SolutionImportFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, "throttled by Dataverse", SolutionImportFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, "storage limit reached", SolutionImportFailureKind.QuotaExhausted)]
    [InlineData(HttpStatusCode.InternalServerError, "unexpected", SolutionImportFailureKind.UnknownInvocationFailure)]
    public void ClassifyHttpFailure_MapsExpectedFailureKind(HttpStatusCode statusCode, string body, SolutionImportFailureKind expected)
    {
        DataverseWebApiSolutionImporter.ClassifyHttpFailure(statusCode, body).Should().Be(expected);
    }

    // ---------- T14 EvaluateImportJobData theory ----------

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

    // ---------- T15 TryReadSolutionVersionFromZip ----------

    [Fact]
    public void TryReadSolutionVersionFromZip_ValidZip_ReturnsVersion()
    {
        var zip = BuildSolutionZip("2.1.3.7");
        DataverseWebApiSolutionImporter.TryReadSolutionVersionFromZip(zip).Should().Be("2.1.3.7");
    }

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

    // ---------- T16 source grep defense-in-depth ----------

    [Fact]
    public void ProductionSource_ContainsNoPacSolutionOrProcessStartInfoReferences()
    {
        var path = LocateSourceFile("DataverseWebApiSolutionImporter.cs");
        var text = File.ReadAllText(path);
        text.Should().NotContain("pac solution");
        text.Should().NotContain("ProcessStartInfo");
    }

    // ---------- helpers ----------

    private static SolutionImportRequest BuildRequest() => new(
        CustomerId: CustomerId,
        TenantId: TenantId,
        ClientId: ClientId,
        ClientSecret: ClientSecret,
        TargetDataverseUrl: EnvUrl);

    private static DataverseWebApiSolutionImporter BuildImporter(
        FakeDataverseHandler dvHandler,
        HttpMessageHandler blobHandler,
        ImmutableArray<CanonicalSolutionEntry> catalogEntries,
        bool throwingCredential = false,
        TimeSpan? importTimeout = null,
        TimeSpan? pollInterval = null)
    {
        TokenCredential Factory(string tenantId, string clientId, string clientSecret)
            => throwingCredential ? new ThrowingCredential() : new FakeCredential();

        return new DataverseWebApiSolutionImporter(
            new HttpClient(dvHandler),
            ArmSdkTestFakes.NewBlobContainerClient((FakeArmHttpMessageHandler)blobHandler),
            new StubCatalog(catalogEntries),
            Options.Create(new SolutionImportOptions
            {
                ProvisioningArtifactsContainerUri = "https://faketest.blob.core.windows.net/provisioning-artifacts",
                SolutionArtifactManifestBlobName = "dataverse-solutions-latest.json",
                ImportTimeout = importTimeout ?? TimeSpan.FromSeconds(5),
                ImportJobPollInterval = pollInterval ?? TimeSpan.FromMilliseconds(5),
            }),
            NullLogger<DataverseWebApiSolutionImporter>.Instance,
            TimeProvider.System,
            Factory);
    }

    private static HttpMessageHandler FakeBlobHandler(string manifest, string zipsVersion)
        => ArmSdkTestFakes.NewHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("dataverse-solutions-latest.json", StringComparison.Ordinal))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, manifest);
            }
            // Any other blob is treated as a solution ZIP request.
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(BuildSolutionZip(zipsVersion)),
            };
        });

    private static byte[] BuildSolutionZip(string version)
    {
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("solution.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write($"<ImportExportXml><SolutionManifest><Version>{version}</Version></SolutionManifest></ImportExportXml>");
        }
        return ms.ToArray();
    }

    private static string ManifestJson(ImmutableArray<CanonicalSolutionEntry> catalog, string version)
    {
        var entries = catalog.Select(c => $"\"{c.SolutionUniqueName}\":{{\"blobName\":\"{c.SolutionUniqueName}.zip\",\"version\":\"{version}\"}}");
        return "{\"solutions\":{" + string.Join(",", entries) + "}}";
    }

    private static string SolutionsListJson(params (string UniqueName, string Version)[] entries)
    {
        var items = entries.Select(e => $$"""{"uniquename":"{{e.UniqueName}}","version":"{{e.Version}}"}""");
        return $$"""{"value":[{{string.Join(",", items)}}]}""";
    }

    private static string ImportJobJson(bool completed, string? data)
    {
        var completedOn = completed ? "\"2026-08-20T12:00:00Z\"" : "null";
        var dataJson = data is null ? "null" : JsonSerializer.Serialize(data);
        return $$"""{"importjobid":"11111111-2222-3333-4444-555555555555","progress":100,"completedon":{{completedOn}},"data":{{dataJson}},"solutionname":"x"}""";
    }

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

    private sealed class StubCatalog : ISolutionCatalog
    {
        public StubCatalog(ImmutableArray<CanonicalSolutionEntry> solutions)
        {
            Solutions = solutions;
            RetiredSolutionUniqueNames = ImmutableArray<string>.Empty;
            CatalogHash = CanonicalSolutionCatalog.ComputeCatalogHash(solutions);
        }

        public ImmutableArray<CanonicalSolutionEntry> Solutions { get; }
        public ImmutableArray<string> RetiredSolutionUniqueNames { get; }
        public string CatalogHash { get; }
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-dataverse-test-token", DateTimeOffset.UtcNow.AddHours(1));

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
    /// Hand-rolled fake <see cref="HttpMessageHandler"/> (NOT
    /// Mock&lt;HttpMessageHandler&gt; — banned per testing.md) that routes
    /// requests to existing-solutions / import-action / importjobs-poll
    /// delegates based on HTTP method + URL shape.
    /// </summary>
    private sealed class FakeDataverseHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? OnExistingSolutionsGet { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnImportPost { get; set; }
        public Func<HttpRequestMessage, string, HttpResponseMessage>? OnImportJobPoll { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add((request.Method, request.RequestUri!, body));

            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post
                && (path.EndsWith("/ImportSolution", StringComparison.Ordinal) || path.EndsWith("/StageAndUpgrade", StringComparison.Ordinal)))
            {
                return (OnImportPost ?? throw new InvalidOperationException("unexpected import POST — no OnImportPost wired"))(request);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/importjobs(", StringComparison.Ordinal))
            {
                var start = path.IndexOf('(') + 1;
                var end = path.IndexOf(')', start);
                var id = path[start..end];
                return (OnImportJobPoll ?? throw new InvalidOperationException("unexpected importjobs poll — no OnImportJobPoll wired"))(request, id);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/solutions", StringComparison.Ordinal))
            {
                return (OnExistingSolutionsGet ?? throw new InvalidOperationException("unexpected solutions GET — no OnExistingSolutionsGet wired"))(request);
            }

            throw new InvalidOperationException($"unexpected request: {request.Method} {request.RequestUri}");
        }
    }
}
