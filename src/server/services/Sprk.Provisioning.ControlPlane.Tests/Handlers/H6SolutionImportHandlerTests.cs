// -----------------------------------------------------------------------------
// H6SolutionImportHandlerTests.cs
//
// Unit tests over H6SolutionImportHandler (task 049 — wave C4 Batch 3D).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live pwsh / pac CLI / Dataverse / HTTP.
//   Fakes replace the repository + catalog + importer + verifier seams so
//   the handler orchestration logic is exercised in isolation. Live-Dataverse
//   coverage belongs in env-guarded smoke tests (H6 is not exercised end-
//   to-end at CI time by design — a real 8-solution import is up to 60 min).
//
// COVERAGE:
//   T1  Happy path — importer Success + verifier AllPresent → Success + Cosmos
//       state advances + interStepState.ImportedSolutions populated + gate
//       Verified with per-solution evidence.
//   T2  Idempotent no-op — CompletedPhases already contains H6 with matching
//       key → Success (no importer call, no verifier call, no state mutation).
//   T3  Missing tenantId (§4D I1) → Failure(Resumable, missing-tenant-id) +
//       NO importer call fired.
//   T4  Missing dataverse URL (H5 not done) → Failure(Resumable, missing-dataverse-url).
//   T5  Missing bffAppRegId (H3 not done) → Failure(Resumable, missing-bff-app-reg-id).
//   T6  Missing ClientSecret (config unpopulated) → Failure(Resumable, missing-client-secret).
//   T7  Retired-solution catalog match (ADR-039) → Failure(QuarantineRequired,
//       retired-solution-reintroduction) + Cosmos marked Quarantined + NO
//       importer call fired.
//   T8  Importer PartialImport → Failure(QuarantineRequired, partial-import-detected)
//       + Cosmos marked Quarantined with QuarantineInfo.
//   T9  Importer AuthFailure → Failure(Resumable, pac-auth-failure).
//   T10 Importer RateLimited → Failure(Resumable, rate-limited).
//   T11 Importer QuotaExhausted → Failure(Resumable, quota-exhausted).
//   T12 Importer Timeout → Failure(Resumable, import-timeout).
//   T13 Importer MissingSolutionZips → Failure(Resumable, missing-solution-zips).
//   T14 Importer UnknownInvocationFailure → Failure(Resumable, import-invocation-failed).
//   T15 Verifier Missing (script said success but list disagrees) →
//       Failure(QuarantineRequired, verification-failed).
//   T16 Importer infrastructure exception → Failure(Resumable, import-invocation-failed).
//   T17 Verifier infrastructure exception → Failure(QuarantineRequired, verification-failed).
//   T18 HandlerId mismatch → throws InvalidOperationException.
//   T19 Idempotency key format — same customerId + catalogHash → same key
//       (solimport-{customerId}-{catalogHash}).
//   T20 Run not found → Failure(Resumable, run-not-found).
//   T21 MapImporterFailure round-trip — every failure kind maps to (rejection, class).
//   T22 R5 binding — CanonicalSolutionCatalog mirrors the PS script's
//       $SolutionImportOrder exactly (8 solutions with expected SolutionUniqueName
//       per Tier). Parses scripts/Deploy-DataverseSolutions.ps1 at test time.
//   T23 R5 binding — importer does NOT pass a hardcoded C# solution list
//       to the PS script (grep-zero check on ArgumentList content — no
//       -SolutionsToImport flag is set).
//   T24 Catalog hash determinism — CatalogHash is stable + non-empty +
//       lowercase hex.
//   T25 FindRetiredMatch — happy path (clean catalog returns null); positive
//       case (a retired match returns the pair).
//   T26 ClassifyOutput mapping table (importer stderr → failure kind).
//   T27 Verifier ParseListOutput — parses a canned pac output with versions
//       + solutionIds; missing solution → Missing outcome.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H6SolutionImportHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h6-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string EnvUrl = "https://acme.crm.dynamics.com/";
    private const string ClientSecret = "test-client-secret-placeholder";

    // ---------- T1 happy path ----------

    [Fact]
    public async Task HappyPath_ImporterSuccessAndVerifierAllPresent_AdvancesStateAndWritesManifest()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var catalog = new CanonicalSolutionCatalog();
        var importer = FakeSolutionImporter.Success();
        var verifier = FakeSolutionVerifier.AllPresent(BuildExpectedManifest(catalog));
        var handler = BuildHandler(repo, catalog, importer, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(
            H6SolutionImportHandler.BuildIdempotencyKey(CustomerId, catalog.CatalogHash));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H6");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle().Which.Phase.Should().Be("H6");

        repo.LastWrittenRun.InterStepState.ImportedSolutions.Should().NotBeNull();
        repo.LastWrittenRun.InterStepState.ImportedSolutions!.Should().HaveCount(8);
        repo.LastWrittenRun.InterStepState.ImportedSolutions!.Select(s => s.SolutionUniqueName)
            .Should().Contain(new[] { "SpaarkeCore", "SpaarkeWebResources", "LegalWorkspace" });

        var gate = repo.LastWrittenRun.GateStates[H6SolutionImportHandler.SolutionsImportedGateId];
        gate.Status.Should().Be(GateState.Verified);
        gate.VerifierHandler.Should().Be("H6");
        gate.Evidence.Should().NotBeNull();
        gate.Evidence!.Value.GetProperty("solutionCount").GetInt32().Should().Be(8);
        gate.Evidence.Value.GetProperty("catalogHash").GetString().Should().Be(catalog.CatalogHash);

        importer.CallCount.Should().Be(1);
        verifier.CallCount.Should().Be(1);
        importer.LastRequest!.TenantId.Should().Be(TenantId);
        importer.LastRequest.ClientId.Should().Be(BffAppRegId);
        importer.LastRequest.ClientSecret.Should().Be(ClientSecret);
        importer.LastRequest.TargetDataverseUrl.Should().Be(EnvUrl);
    }

    // ---------- T2 idempotency ----------

    [Fact]
    public async Task Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun();
        var catalog = new CanonicalSolutionCatalog();
        var expectedKey = H6SolutionImportHandler.BuildIdempotencyKey(CustomerId, catalog.CatalogHash);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H6",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-45),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-40),
            JobId = "prior-run",
        });
        run.InterStepState.ImportedSolutions = BuildExpectedManifest(catalog).ToList();

        var repo = new FakeRepository(run, etag: "etag-2");
        var importer = FakeSolutionImporter.Success();
        var verifier = FakeSolutionVerifier.AllPresent(BuildExpectedManifest(catalog));
        var handler = BuildHandler(repo, catalog, importer, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        importer.CallCount.Should().Be(0, "no re-import");
        verifier.CallCount.Should().Be(0, "no re-verify");
    }

    // ---------- T3 missing tenantId ----------

    [Fact]
    public async Task MissingTenantId_FailsResumable_NoImporterCall()
    {
        var run = BuildRun(includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-3");
        var importer = FakeSolutionImporter.Success();
        var handler = BuildHandler(repo, new CanonicalSolutionCatalog(), importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest(new CanonicalSolutionCatalog())));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.MissingTenantId);
        failure.Diagnostic.Should().Contain("§4D I1");
        importer.CallCount.Should().Be(0);
    }

    // ---------- T4 missing dataverse URL ----------

    [Fact]
    public async Task MissingDataverseUrl_FailsResumable_NoImporterCall()
    {
        var run = BuildRun();
        run.InterStepState.DataverseEnvUrl = null;
        var repo = new FakeRepository(run, etag: "etag-4");
        var importer = FakeSolutionImporter.Success();
        var handler = BuildHandler(repo, new CanonicalSolutionCatalog(), importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest(new CanonicalSolutionCatalog())));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.MissingDataverseUrl);
        importer.CallCount.Should().Be(0);
    }

    // ---------- T5 missing bffAppRegId ----------

    [Fact]
    public async Task MissingBffAppRegId_FailsResumable_NoImporterCall()
    {
        var run = BuildRun();
        run.InterStepState.BffAppRegId = null;
        var repo = new FakeRepository(run, etag: "etag-5");
        var importer = FakeSolutionImporter.Success();
        var handler = BuildHandler(repo, new CanonicalSolutionCatalog(), importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest(new CanonicalSolutionCatalog())));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.MissingBffAppRegId);
        importer.CallCount.Should().Be(0);
    }

    // ---------- T6 missing client secret ----------

    [Fact]
    public async Task MissingClientSecret_FailsResumable_NoImporterCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-6");
        var importer = FakeSolutionImporter.Success();
        var handler = BuildHandler(repo, new CanonicalSolutionCatalog(), importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest(new CanonicalSolutionCatalog())),
            clientSecret: null);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.MissingClientSecret);
        importer.CallCount.Should().Be(0);
    }

    // ---------- T7 retired-solution catalog match (ADR-039) ----------

    [Fact]
    public async Task RetiredSolutionCatalogMatch_FailsQuarantineRequired_NoImporterCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-7");
        // Craft a catalog that violates ADR-039 by including a solution whose
        // unique-name matches a retired-artifact pattern.
        var badCatalog = new StubSolutionCatalog(
            solutions: ImmutableArray.Create(
                new CanonicalSolutionEntry("SpaarkeCore", "SpaarkeCore", "Spaarke Core", 1),
                new CanonicalSolutionEntry("SprkDispatcherLegacy", "SprkDispatcherLegacy", "Old Dispatcher", 2)),
            retired: ImmutableArray.Create("SprkDispatcher"));
        var importer = FakeSolutionImporter.Success();
        var handler = BuildHandler(repo, badCatalog, importer,
            FakeSolutionVerifier.AllPresent(ImmutableArray<ImportedSolutionRecord>.Empty));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.RetiredSolutionReintroduction);
        failure.Diagnostic.Should().Contain("SprkDispatcher");
        failure.Diagnostic.Should().Contain("ADR-039");
        importer.CallCount.Should().Be(0);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.Quarantine.Should().NotBeNull();
        repo.LastWrittenRun.Quarantine!.QuarantinedByHandler.Should().Be("H6");
    }

    // ---------- T8 importer PartialImport ----------

    [Fact]
    public async Task ImporterPartialImport_FailsQuarantineRequired_MarksRunQuarantined()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-8");
        var importer = FakeSolutionImporter.Failure(
            SolutionImportFailureKind.PartialImport,
            "CRITICAL: Tier 3 had import failure(s): LegalWorkspace. Aborting BEFORE attempting subsequent tiers.");
        var handler = BuildHandler(repo, new CanonicalSolutionCatalog(), importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest(new CanonicalSolutionCatalog())));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.PartialImportDetected);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.Quarantine.Should().NotBeNull();
        repo.LastWrittenRun.Quarantine!.QuarantinedByHandler.Should().Be("H6");
    }

    // ---------- T9 importer AuthFailure ----------

    [Fact]
    public async Task ImporterAuthFailure_FailsResumable_WithPacAuthFailureCode()
    {
        await AssertImporterFailureMapsTo(
            SolutionImportFailureKind.AuthFailure,
            SolutionImportRejectionCodes.PacAuthFailure,
            FailureClass.Resumable);
    }

    // ---------- T10 importer RateLimited ----------

    [Fact]
    public async Task ImporterRateLimited_FailsResumable_WithRateLimitedCode()
    {
        await AssertImporterFailureMapsTo(
            SolutionImportFailureKind.RateLimited,
            SolutionImportRejectionCodes.RateLimited,
            FailureClass.Resumable);
    }

    // ---------- T11 importer QuotaExhausted ----------

    [Fact]
    public async Task ImporterQuotaExhausted_FailsResumable_WithQuotaExhaustedCode()
    {
        await AssertImporterFailureMapsTo(
            SolutionImportFailureKind.QuotaExhausted,
            SolutionImportRejectionCodes.QuotaExhausted,
            FailureClass.Resumable);
    }

    // ---------- T12 importer Timeout ----------

    [Fact]
    public async Task ImporterTimeout_FailsResumable_WithDistinctImportTimeoutCode()
    {
        await AssertImporterFailureMapsTo(
            SolutionImportFailureKind.Timeout,
            SolutionImportRejectionCodes.ImportTimeout,
            FailureClass.Resumable);
    }

    // ---------- T13 importer MissingSolutionZips ----------

    [Fact]
    public async Task ImporterMissingSolutionZips_FailsResumable_WithMissingSolutionZipsCode()
    {
        await AssertImporterFailureMapsTo(
            SolutionImportFailureKind.MissingSolutionZips,
            SolutionImportRejectionCodes.MissingSolutionZips,
            FailureClass.Resumable);
    }

    // ---------- T14 importer UnknownInvocationFailure ----------

    [Fact]
    public async Task ImporterUnknownInvocationFailure_FailsResumable_WithImportInvocationFailedCode()
    {
        await AssertImporterFailureMapsTo(
            SolutionImportFailureKind.UnknownInvocationFailure,
            SolutionImportRejectionCodes.ImportInvocationFailed,
            FailureClass.Resumable);
    }

    // ---------- T15 verifier Missing ----------

    [Fact]
    public async Task VerifierMissing_FailsQuarantineRequired_WithVerificationFailedCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-15");
        var importer = FakeSolutionImporter.Success();
        var verifier = new FakeSolutionVerifier(new SolutionVerificationOutcome.Missing(
            ImmutableArray.Create("LegalWorkspace", "EventsPage"),
            "pac solution list did not return the following expected solutions after import: LegalWorkspace, EventsPage"));
        var handler = BuildHandler(repo, new CanonicalSolutionCatalog(), importer, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.VerificationFailed);
        failure.Diagnostic.Should().Contain("LegalWorkspace");
        failure.Diagnostic.Should().Contain("EventsPage");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- T16 importer infrastructure exception ----------

    [Fact]
    public async Task ImporterInfrastructureException_FailsResumable_WithImportInvocationFailedCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-16");
        var importer = new ThrowingSolutionImporter(
            new InvalidOperationException("pwsh binary not found on PATH"));
        var handler = BuildHandler(repo, new CanonicalSolutionCatalog(), importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest(new CanonicalSolutionCatalog())));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.ImportInvocationFailed);
        failure.Diagnostic.Should().Contain("InvalidOperationException");
        failure.Diagnostic.Should().Contain("pwsh binary not found");
    }

    // ---------- T17 verifier infrastructure exception ----------

    [Fact]
    public async Task VerifierInfrastructureException_FailsQuarantineRequired_WithVerificationFailedCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-17");
        var importer = FakeSolutionImporter.Success();
        var verifier = new ThrowingSolutionVerifier(
            new InvalidOperationException("pac binary not found on PATH"));
        var handler = BuildHandler(repo, new CanonicalSolutionCatalog(), importer, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.VerificationFailed);
        failure.Diagnostic.Should().Contain("InvalidOperationException");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- T18 handler-id mismatch ----------

    [Fact]
    public async Task HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-18");
        var handler = BuildHandler(repo, new CanonicalSolutionCatalog(),
            FakeSolutionImporter.Success(),
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest(new CanonicalSolutionCatalog())));

        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H0",
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- T19 idempotency key format ----------

    [Fact]
    public void IdempotencyKey_IsDeterministicByCustomerIdAndCatalogHash()
    {
        var catalog = new CanonicalSolutionCatalog();
        var k1 = H6SolutionImportHandler.BuildIdempotencyKey("acme", catalog.CatalogHash);
        var k2 = H6SolutionImportHandler.BuildIdempotencyKey("acme", catalog.CatalogHash);
        k1.Should().Be(k2);
        k1.Should().StartWith("solimport-acme-");
        k1.Should().Be($"solimport-acme-{catalog.CatalogHash}");
    }

    // ---------- T20 run not found ----------

    [Fact]
    public async Task RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var importer = FakeSolutionImporter.Success();
        var handler = BuildHandler(repo, new CanonicalSolutionCatalog(), importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest(new CanonicalSolutionCatalog())));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.RunNotFound);
        importer.CallCount.Should().Be(0);
    }

    // ---------- T21 failure-kind mapping table ----------

    [Theory]
    [InlineData(SolutionImportFailureKind.AuthFailure, SolutionImportRejectionCodes.PacAuthFailure, FailureClass.Resumable)]
    [InlineData(SolutionImportFailureKind.RateLimited, SolutionImportRejectionCodes.RateLimited, FailureClass.Resumable)]
    [InlineData(SolutionImportFailureKind.QuotaExhausted, SolutionImportRejectionCodes.QuotaExhausted, FailureClass.Resumable)]
    [InlineData(SolutionImportFailureKind.MissingSolutionZips, SolutionImportRejectionCodes.MissingSolutionZips, FailureClass.Resumable)]
    [InlineData(SolutionImportFailureKind.PartialImport, SolutionImportRejectionCodes.PartialImportDetected, FailureClass.QuarantineRequired)]
    [InlineData(SolutionImportFailureKind.Timeout, SolutionImportRejectionCodes.ImportTimeout, FailureClass.Resumable)]
    [InlineData(SolutionImportFailureKind.UnknownInvocationFailure, SolutionImportRejectionCodes.ImportInvocationFailed, FailureClass.Resumable)]
    public void MapImporterFailure_ProducesExpectedRejectionAndClass(
        SolutionImportFailureKind kind,
        string expectedRejection,
        FailureClass expectedClass)
    {
        var (rejection, cls) = H6SolutionImportHandler.MapImporterFailure(kind);
        rejection.Should().Be(expectedRejection);
        cls.Should().Be(expectedClass);
    }

    // ---------- T22 R5 binding — CanonicalSolutionCatalog mirrors PS script $SolutionImportOrder ----------
    //
    // task 008 R5: "H6 handler MUST source its input list from $SolutionImportOrder,
    // never from src/solutions/ folder enumeration". The PS script IS the runtime
    // authority (the importer does not pass -SolutionsToImport). This test asserts
    // the C#-side mirror (CanonicalSolutionCatalog) stays in lockstep with the PS
    // script's $SolutionImportOrder hashtable — if the PS list changes and the C#
    // catalog doesn't (or vice versa), this test fails at CI time forcing the fix.

    [Fact]
    public void CanonicalSolutionCatalog_MirrorsPowerShellScriptSolutionImportOrder()
    {
        var catalog = new CanonicalSolutionCatalog();

        // Read the PS script from disk to extract the $SolutionImportOrder block.
        var scriptPath = LocatePowerShellScript();
        var scriptText = File.ReadAllText(scriptPath);

        // Simple regex-parse of the ordered hashtable entries. Format per task 012:
        //   "FolderName" = @{ DisplayName = "..."; SolutionName = "..."; Tier = N }
        var entryRegex = new System.Text.RegularExpressions.Regex(
            @"""(?<folder>[A-Za-z0-9_]+)""\s+=\s+@\{\s*DisplayName\s+=\s+""(?<display>[^""]+)"";\s+SolutionName\s+=\s+""(?<solution>[A-Za-z0-9_]+)"";\s+Tier\s+=\s+(?<tier>\d+)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var psEntries = entryRegex.Matches(scriptText)
            .Select(m => new
            {
                Folder = m.Groups["folder"].Value,
                Solution = m.Groups["solution"].Value,
                Display = m.Groups["display"].Value,
                Tier = int.Parse(m.Groups["tier"].Value),
            })
            .ToList();

        // Test-safety guard: if the regex fails to parse ANY entries, the PS
        // script format has drifted and the test must fail loud rather than
        // silently pass on an empty match set.
        psEntries.Should().NotBeEmpty(
            $"the R5-binding test regex must parse at least one entry from {scriptPath}");
        psEntries.Should().HaveCount(8, "the PS script's $SolutionImportOrder is authoritative for the 8 solutions");

        // Structural equality: same count + same (folder, solution, tier) tuples
        // in the same order.
        catalog.Solutions.Should().HaveCount(psEntries.Count,
            "the C# catalog mirror MUST have the same count as the PS $SolutionImportOrder");

        for (int i = 0; i < psEntries.Count; i++)
        {
            catalog.Solutions[i].FolderName.Should().Be(psEntries[i].Folder,
                $"C# catalog entry {i} folder must match PS entry {i}");
            catalog.Solutions[i].SolutionUniqueName.Should().Be(psEntries[i].Solution,
                $"C# catalog entry {i} solution must match PS entry {i}");
            catalog.Solutions[i].Tier.Should().Be(psEntries[i].Tier,
                $"C# catalog entry {i} tier must match PS entry {i}");
        }
    }

    // ---------- T23 R5 binding — importer does NOT pass -SolutionsToImport ----------
    //
    // Guards the "PS script is runtime authority" invariant: if a future change
    // adds -SolutionsToImport to the runner's ArgumentList, the PS script's
    // $SolutionImportOrder would be filtered/overridden by a C#-side list, which
    // is exactly what R5 forbids. This test greps the runner source for the flag.

    [Fact]
    public void ImporterRunnerSource_DoesNotPassSolutionsToImportFlag()
    {
        var runnerSourcePath = LocateImporterSource();
        var runnerSource = File.ReadAllText(runnerSourcePath);

        // Grep-check that no ArgumentList.Add invocation passes "-SolutionsToImport"
        // as a command-line arg to the PS child process. The comment that explains
        // the omission is fine (and expected); the invariant is: no arg-list add
        // of the flag string. Same for the POSIX-style long-flag variant.
        var argAddPattern = new System.Text.RegularExpressions.Regex(
            @"ArgumentList\.Add\s*\(\s*""--?SolutionsToImport""\s*\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        argAddPattern.IsMatch(runnerSource).Should().BeFalse(
            "R5 binding: the PS script's $SolutionImportOrder is runtime authority — " +
            "the importer MUST NOT pass a filtered list from C# code (no ArgumentList.Add " +
            "of a -SolutionsToImport flag is allowed).");
    }

    // ---------- T24 CatalogHash determinism ----------

    [Fact]
    public void CatalogHash_IsDeterministicNonEmptyLowercaseHex()
    {
        var c1 = new CanonicalSolutionCatalog();
        var c2 = new CanonicalSolutionCatalog();
        c1.CatalogHash.Should().NotBeNullOrWhiteSpace();
        c1.CatalogHash.Should().Be(c2.CatalogHash, "hash is deterministic across instances");
        c1.CatalogHash.Should().HaveLength(64, "SHA-256 hex is 64 chars");
        c1.CatalogHash.Should().MatchRegex("^[0-9a-f]{64}$", "lowercase hex only");
    }

    // ---------- T25 FindRetiredMatch ----------

    [Fact]
    public void FindRetiredMatch_CleanCatalog_ReturnsNull()
    {
        var catalog = new CanonicalSolutionCatalog();
        var match = H6SolutionImportHandler.FindRetiredMatch(catalog);
        match.Should().BeNull("the 8 authoritative solutions do NOT overlap the retired-artifact list");
    }

    [Fact]
    public void FindRetiredMatch_ViolatingCatalog_ReturnsPair()
    {
        var bad = new StubSolutionCatalog(
            solutions: ImmutableArray.Create(
                new CanonicalSolutionEntry("Good", "Good", "Good", 1),
                new CanonicalSolutionEntry("SpaarkePlaybookEmbeddings", "SpaarkePlaybookEmbeddings", "Bad", 2)),
            retired: ImmutableArray.Create("PlaybookEmbeddings"));
        var match = H6SolutionImportHandler.FindRetiredMatch(bad);
        match.Should().NotBeNull();
        match!.Value.SolutionUniqueName.Should().Be("SpaarkePlaybookEmbeddings");
        match.Value.Pattern.Should().Be("PlaybookEmbeddings");
    }

    // ---------- T26 ClassifyOutput mapping ----------

    [Theory]
    [InlineData("CRITICAL: Tier 3 had import failure(s): LegalWorkspace", SolutionImportFailureKind.PartialImport)]
    [InlineData("aborting BEFORE attempting subsequent tiers", SolutionImportFailureKind.PartialImport)]
    [InlineData("PAC CLI authentication failed", SolutionImportFailureKind.AuthFailure)]
    [InlineData("System Administrator role required", SolutionImportFailureKind.AuthFailure)]
    [InlineData("HTTP 429 too many requests", SolutionImportFailureKind.RateLimited)]
    [InlineData("throttled by Dataverse", SolutionImportFailureKind.RateLimited)]
    [InlineData("tenant quota exhausted", SolutionImportFailureKind.QuotaExhausted)]
    [InlineData("env storage limit reached", SolutionImportFailureKind.QuotaExhausted)]
    [InlineData("No solution ZIPs found", SolutionImportFailureKind.MissingSolutionZips)]
    [InlineData("Ensure managed solution ZIP files exist", SolutionImportFailureKind.MissingSolutionZips)]
    [InlineData("Something went wrong on the server", SolutionImportFailureKind.UnknownInvocationFailure)]
    public void DeployDataverseSolutionsScriptImporter_ClassifyOutput_MapsExpectedFailureKind(
        string combinedOutput,
        SolutionImportFailureKind expected)
    {
        var kind = DeployDataverseSolutionsScriptImporter.ClassifyOutput(combinedOutput);
        kind.Should().Be(expected);
    }

    // ---------- T27 verifier ParseListOutput ----------

    [Fact]
    public void PacCliSolutionVerifier_ParseListOutput_AllPresent_ReturnsFullManifest()
    {
        // Canonical `pac solution list` tabular output (line-format may vary
        // slightly across PAC versions; regex tolerates both with-solutionId
        // and without-solutionId).
        var stdoutText = @"
Unique Name              Friendly Name            Version           Solution Id                            Managed
--------------------------------------------------------------------------------------------------------------
SpaarkeCore              Spaarke Core             1.0.0.0           11111111-2222-3333-4444-555555555555   True
SpaarkeWebResources      Spaarke Web Resources    1.0.0.0           22222222-3333-4444-5555-666666666666   True
CalendarSidePane         Calendar Side Pane       1.0.0.0           33333333-4444-5555-6666-777777777777   True
DocumentUploadWizard     Document Upload Wizard   1.0.0.0           44444444-5555-6666-7777-888888888888   True
EventRibbons             Event Ribbon Commands    1.0.0.0           55555555-6666-7777-8888-999999999999   True
EventDetailSidePane      Event Detail Side Pane   1.0.0.0           66666666-7777-8888-9999-aaaaaaaaaaaa   True
EventsPage               Events Page              1.0.0.0           77777777-8888-9999-aaaa-bbbbbbbbbbbb   True
LegalWorkspace           Legal Workspace          1.0.0.0           88888888-9999-aaaa-bbbb-cccccccccccc   True
";

        var catalog = new CanonicalSolutionCatalog();
        var outcome = PacCliSolutionVerifier.ParseListOutput(stdoutText, catalog.Solutions);
        var allPresent = outcome.Should().BeOfType<SolutionVerificationOutcome.AllPresent>().Subject;
        allPresent.ImportedRecords.Should().HaveCount(8);
        allPresent.ImportedRecords[0].SolutionUniqueName.Should().Be("SpaarkeCore");
        allPresent.ImportedRecords[0].Version.Should().Be("1.0.0.0");
        allPresent.ImportedRecords[0].SolutionId.Should().Be("11111111-2222-3333-4444-555555555555");
        allPresent.ImportedRecords[0].Tier.Should().Be(1);

        var webRes = allPresent.ImportedRecords.Single(r => r.SolutionUniqueName == "SpaarkeWebResources");
        webRes.Tier.Should().Be(2);
        webRes.SolutionId.Should().Be("22222222-3333-4444-5555-666666666666");

        var eventRibbons = allPresent.ImportedRecords.Single(r => r.SolutionUniqueName == "EventRibbons");
        eventRibbons.Tier.Should().Be(3);
    }

    [Fact]
    public void PacCliSolutionVerifier_ParseListOutput_OneMissing_ReturnsMissingOutcome()
    {
        // Same output as T27 happy but LegalWorkspace omitted.
        var stdoutText = @"
Unique Name              Friendly Name            Version           Solution Id                            Managed
--------------------------------------------------------------------------------------------------------------
SpaarkeCore              Spaarke Core             1.0.0.0           11111111-2222-3333-4444-555555555555   True
SpaarkeWebResources      Spaarke Web Resources    1.0.0.0           22222222-3333-4444-5555-666666666666   True
CalendarSidePane         Calendar Side Pane       1.0.0.0           33333333-4444-5555-6666-777777777777   True
DocumentUploadWizard     Document Upload Wizard   1.0.0.0           44444444-5555-6666-7777-888888888888   True
EventRibbons             Event Ribbon Commands    1.0.0.0           55555555-6666-7777-8888-999999999999   True
EventDetailSidePane      Event Detail Side Pane   1.0.0.0           66666666-7777-8888-9999-aaaaaaaaaaaa   True
EventsPage               Events Page              1.0.0.0           77777777-8888-9999-aaaa-bbbbbbbbbbbb   True
";
        var catalog = new CanonicalSolutionCatalog();
        var outcome = PacCliSolutionVerifier.ParseListOutput(stdoutText, catalog.Solutions);
        var missing = outcome.Should().BeOfType<SolutionVerificationOutcome.Missing>().Subject;
        missing.MissingUniqueNames.Should().ContainSingle().Which.Should().Be("LegalWorkspace");
    }

    // ---------- helpers ----------

    private async Task AssertImporterFailureMapsTo(
        SolutionImportFailureKind kind,
        string expectedCode,
        FailureClass expectedClass)
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-fail");
        var importer = FakeSolutionImporter.Failure(kind, $"canned failure: {kind}");
        var handler = BuildHandler(repo, new CanonicalSolutionCatalog(), importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest(new CanonicalSolutionCatalog())));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(expectedClass);
        failure.RejectionCode.Should().Be(expectedCode);
    }

    private static H6SolutionImportHandler BuildHandler(
        FakeRepository repo,
        ISolutionCatalog catalog,
        ISolutionImporter importer,
        ISolutionVerifier verifier,
        string? clientSecret = ClientSecret)
    {
        var options = Options.Create(new SolutionImportOptions
        {
            ClientSecret = clientSecret,
            ImportTimeout = TimeSpan.FromSeconds(10),
            VerifierCallTimeout = TimeSpan.FromSeconds(5),
        });
        return new H6SolutionImportHandler(
            repo, catalog, importer, verifier, options,
            TimeProvider.System,
            NullLogger<H6SolutionImportHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H6SolutionImportHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(bool includeTenantId = true)
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-guid",
            TenancyModel = "Model2Dedicated",
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model2",
        };
        if (includeTenantId)
        {
            run.Parameters.NonSecret[H6SolutionImportHandler.TenantIdParameterKey] = TenantId;
        }
        // Upstream H3 output — bffAppRegId.
        run.InterStepState.BffAppRegId = BffAppRegId;
        // Upstream H5 output — dataverseEnvUrl.
        run.InterStepState.DataverseEnvUrl = EnvUrl;
        return run;
    }

    private static ImmutableArray<ImportedSolutionRecord> BuildExpectedManifest(ISolutionCatalog catalog)
    {
        return catalog.Solutions
            .Select(e => new ImportedSolutionRecord(
                SolutionUniqueName: e.SolutionUniqueName,
                Version: "1.0.0.0",
                SolutionId: Guid.NewGuid().ToString(),
                Tier: e.Tier))
            .ToImmutableArray();
    }

    private static string LocatePowerShellScript()
    {
        // Walk up from the test bin dir to the repo root, then locate the PS
        // script under scripts/. Same navigation strategy as
        // <c>ProvisionCustomerScriptBicepDeployRunner</c> tests in H2a.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "Deploy-DataverseSolutions.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate scripts/Deploy-DataverseSolutions.ps1 by walking up from " +
            $"{AppContext.BaseDirectory}. Ensure the test is running inside the repository tree.");
    }

    private static string LocateImporterSource()
    {
        // Path updated by task 100 (DS-3 Option 2 L2 project split, 2026-08-19):
        // pre-split the handler source lived under Sprk.Provisioning.ControlPlane/
        // Handlers/SolutionImport/; post-split it lives under
        // Sprk.Provisioning.ControlPlane.Core/Handlers/SolutionImport/ (Handlers
        // moved into the Core class library so both .Api and .Worker consume the
        // same handler types). Namespace preserved.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName,
                "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core",
                "Handlers", "SolutionImport", "DeployDataverseSolutionsScriptImporter.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate DeployDataverseSolutionsScriptImporter.cs by walking up from " +
            $"{AppContext.BaseDirectory}. Post-task-100 path: src/server/services/" +
            "Sprk.Provisioning.ControlPlane.Core/Handlers/SolutionImport/.");
    }

    // ---------- fakes ----------

    /// <summary>Repository fake — records last written run + last write etag.</summary>
    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private ProvisioningRun? _run;
        private string? _etag;
        public ProvisioningRun? LastWrittenRun { get; private set; }

        public FakeRepository(ProvisioningRun? run, string? etag)
        {
            _run = run;
            _etag = etag;
        }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult(_run is null || _etag is null
                ? null
                : new ProvisioningRunReadResult(_run, _etag));

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
        {
            LastWrittenRun = run;
            _run = run;
            _etag = ifMatchEtag + "-next";
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, _etag));
        }
    }

    /// <summary>Importer fake — returns a fixed outcome + records the last request.</summary>
    private sealed class FakeSolutionImporter : ISolutionImporter
    {
        private readonly SolutionImportOutcome _outcome;
        public int CallCount { get; private set; }
        public SolutionImportRequest? LastRequest { get; private set; }

        private FakeSolutionImporter(SolutionImportOutcome outcome) => _outcome = outcome;

        public static FakeSolutionImporter Success()
            => new(new SolutionImportOutcome.Success());

        public static FakeSolutionImporter Failure(SolutionImportFailureKind kind, string diagnostic)
            => new(new SolutionImportOutcome.Failure(kind, diagnostic));

        public Task<SolutionImportOutcome> ImportAsync(
            SolutionImportRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }

    /// <summary>Verifier fake — returns a fixed outcome.</summary>
    private sealed class FakeSolutionVerifier : ISolutionVerifier
    {
        private readonly SolutionVerificationOutcome _outcome;
        public int CallCount { get; private set; }

        public FakeSolutionVerifier(SolutionVerificationOutcome outcome) => _outcome = outcome;

        public static FakeSolutionVerifier AllPresent(ImmutableArray<ImportedSolutionRecord> manifest)
            => new(new SolutionVerificationOutcome.AllPresent(manifest));

        public Task<SolutionVerificationOutcome> VerifyAsync(
            SolutionVerificationRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_outcome);
        }
    }

    /// <summary>Importer that always throws — models pwsh binary missing / PATH issue.</summary>
    private sealed class ThrowingSolutionImporter : ISolutionImporter
    {
        private readonly Exception _exception;
        public ThrowingSolutionImporter(Exception ex) => _exception = ex;

        public Task<SolutionImportOutcome> ImportAsync(SolutionImportRequest request, CancellationToken ct)
            => throw _exception;
    }

    /// <summary>Verifier that always throws — models pac binary missing.</summary>
    private sealed class ThrowingSolutionVerifier : ISolutionVerifier
    {
        private readonly Exception _exception;
        public ThrowingSolutionVerifier(Exception ex) => _exception = ex;

        public Task<SolutionVerificationOutcome> VerifyAsync(SolutionVerificationRequest request, CancellationToken ct)
            => throw _exception;
    }

    /// <summary>Catalog stub — arbitrary solutions + retired-list for negative-path tests.</summary>
    private sealed class StubSolutionCatalog : ISolutionCatalog
    {
        public StubSolutionCatalog(
            ImmutableArray<CanonicalSolutionEntry> solutions,
            ImmutableArray<string> retired)
        {
            Solutions = solutions;
            RetiredSolutionUniqueNames = retired;
            // Deterministic hash mirrors CanonicalSolutionCatalog.ComputeCatalogHash.
            CatalogHash = CanonicalSolutionCatalog.ComputeCatalogHash(solutions);
        }

        public ImmutableArray<CanonicalSolutionEntry> Solutions { get; }
        public ImmutableArray<string> RetiredSolutionUniqueNames { get; }
        public string CatalogHash { get; }
    }
}
