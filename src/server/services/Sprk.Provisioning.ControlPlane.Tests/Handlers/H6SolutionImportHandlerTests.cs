// -----------------------------------------------------------------------------
// H6SolutionImportHandlerTests.cs
//
// Unit tests over H6SolutionImportHandler (task 049 — wave C4 Batch 3D).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live Dataverse / HTTP. Fakes replace the
//   repository + importer + verifier seams so the handler orchestration logic
//   is exercised in isolation (importer/verifier HTTP behaviour has its own
//   tests). Live-Dataverse coverage belongs in env-guarded smoke tests.
//
// COVERAGE:
//   T1  Happy path — importer Success + verifier AllPresent → Success + Cosmos
//       state advances + interStepState.ImportedSolutions = the SpaarkeMaster
//       record + gate Verified with packageType evidence; managed by default.
//   T2  Idempotent no-op — CompletedPhases already contains H6 with matching
//       key → Success (no importer call, no verifier call, no state mutation).
//   T3  Missing tenantId (§4D I1) → Failure(Resumable, missing-tenant-id) + NO importer call.
//   T4  Missing dataverse URL (H5 not done) → Failure(Resumable, missing-dataverse-url).
//   T5  Missing bffAppRegId (H3 not done) → Failure(Resumable, missing-bff-app-reg-id).
//   T6  Missing ClientSecret (legacy chain, config unpopulated) → Failure(Resumable, missing-client-secret).
//   T7  T218b package type — unmanaged on explicit instruction reaches the
//       importer + verifier + key + evidence; an invalid stored value →
//       Failure(Resumable, package-type-invalid) with no importer call.
//   T8  Importer PartialImport → Failure(QuarantineRequired, partial-import-detected).
//   T9–T14 Importer failure kinds → (rejection code, §4C class).
//   T15 Verifier Missing → Failure(QuarantineRequired, verification-failed).
//   T16 Importer infrastructure exception → Failure(Resumable, import-invocation-failed).
//   T17 Verifier infrastructure exception → Failure(QuarantineRequired, verification-failed).
//   T18 HandlerId mismatch → throws InvalidOperationException.
//   T19 Idempotency key format — solimport-{customerId}-{packageType}.
//   T20 Run not found → Failure(Resumable, run-not-found).
//   T21 MapImporterFailure round-trip — every failure kind maps to (rejection, class),
//       incl. T218b PackageTypeMismatch / DowngradeRefused (Resumable).
//   HANDLER-07/08 pre-import gates.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
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
        var importer = FakeSolutionImporter.Success();
        var verifier = FakeSolutionVerifier.AllPresent(BuildExpectedManifest());
        var handler = BuildHandler(repo, importer, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(
            H6SolutionImportHandler.BuildIdempotencyKey(CustomerId, "managed"));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H6");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle().Which.Phase.Should().Be("H6");

        repo.LastWrittenRun.InterStepState.ImportedSolutions.Should().NotBeNull();
        repo.LastWrittenRun.InterStepState.ImportedSolutions!.Should().ContainSingle()
            .Which.SolutionUniqueName.Should().Be("SpaarkeMaster");

        var gate = repo.LastWrittenRun.GateStates[H6SolutionImportHandler.SolutionsImportedGateId];
        gate.Status.Should().Be(GateState.Verified);
        gate.VerifierHandler.Should().Be("H6");
        gate.Evidence.Should().NotBeNull();
        gate.Evidence!.Value.GetProperty("packageType").GetString().Should().Be("managed");
        gate.Evidence.Value.GetProperty("solutions")[0].GetProperty("isManaged").GetBoolean().Should().BeTrue();

        importer.CallCount.Should().Be(1);
        verifier.CallCount.Should().Be(1);
        importer.LastRequest!.TenantId.Should().Be(TenantId);
        importer.LastRequest.ClientId.Should().Be(BffAppRegId);
        importer.LastRequest.ClientSecret.Should().Be(ClientSecret);
        importer.LastRequest.TargetDataverseUrl.Should().Be(EnvUrl);
        importer.LastRequest.Managed.Should().BeTrue("managed is the default (ADR-027 §3, owner D8)");
        verifier.LastRequest!.Managed.Should().BeTrue();
    }

    // ---------- T2 idempotency ----------

    [Fact]
    public async Task Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun();
        var expectedKey = H6SolutionImportHandler.BuildIdempotencyKey(CustomerId, "managed");
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H6",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-45),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-40),
            JobId = "prior-run",
        });
        run.InterStepState.ImportedSolutions = BuildExpectedManifest().ToList();

        var repo = new FakeRepository(run, etag: "etag-2");
        var importer = FakeSolutionImporter.Success();
        var verifier = FakeSolutionVerifier.AllPresent(BuildExpectedManifest());
        var handler = BuildHandler(repo, importer, verifier);

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
        var handler = BuildHandler(repo, importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest()));

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
        var handler = BuildHandler(repo, importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest()));

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
        var handler = BuildHandler(repo, importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest()));

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
        var handler = BuildHandler(repo, importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest()),
            clientSecret: null);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.MissingClientSecret);
        importer.CallCount.Should().Be(0);
    }

    // ---------- A44.5 (task 205i): FR-39 secret-free chain ----------

    /// <summary>
    /// Under the MI-FIC-first secret-free chain (§10.2 live contract:
    /// SolutionImportOptions:Credentials:Order:0=ManagedIdentityFederated) an
    /// EMPTY secret slot does NOT fail the run — H6 proceeds to the importer,
    /// which resolves MI-FIC via WorkerDataverseCredentialFactory. Empty is
    /// the signal (auth-v4 §9.1); never a sentinel.
    /// </summary>
    [Fact]
    public async Task SecretFree_MiFicFirstChain_EmptySecret_ProceedsToImporterAndSucceeds()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a44");
        var importer = FakeSolutionImporter.Success();
        var handler = BuildHandler(repo, importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest()),
            clientSecret: null,
            credentials: new Sprk.Provisioning.ControlPlane.Handlers.Credentials.WorkerCredentialSelectionOptions
            {
                Order = { nameof(Sprk.Provisioning.ControlPlane.Handlers.Credentials.CredentialKind.ManagedIdentityFederated) },
                RequireSecretFreeIdentity = true,
            });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        importer.CallCount.Should().Be(1);
    }

    // ---------- T7 T218b package type ----------

    [Fact]
    public async Task UnmanagedOnExplicitInstruction_ReachesImporterVerifierKeyAndEvidence()
    {
        var run = BuildRun();
        run.Parameters.NonSecret[IntakeParameterCatalog.SolutionPackageType] = "unmanaged";
        var repo = new FakeRepository(run, etag: "etag-7");
        var importer = FakeSolutionImporter.Success();
        var verifier = FakeSolutionVerifier.AllPresent(BuildExpectedManifest(isManaged: false));
        var handler = BuildHandler(repo, importer, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>()
            .Which.IdempotencyKey.Should().Be($"solimport-{CustomerId}-unmanaged");
        importer.LastRequest!.Managed.Should().BeFalse();
        verifier.LastRequest!.Managed.Should().BeFalse();
        repo.LastWrittenRun!.GateStates[H6SolutionImportHandler.SolutionsImportedGateId]
            .Evidence!.Value.GetProperty("packageType").GetString().Should().Be("unmanaged");
    }

    [Theory]
    [InlineData("Managed")]
    [InlineData("both")]
    public async Task InvalidStoredPackageType_FailsResumable_NoImporterCall(string stored)
    {
        var run = BuildRun();
        run.Parameters.NonSecret[IntakeParameterCatalog.SolutionPackageType] = stored;
        var repo = new FakeRepository(run, etag: "etag-7b");
        var importer = FakeSolutionImporter.Success();
        var handler = BuildHandler(repo, importer, FakeSolutionVerifier.AllPresent(BuildExpectedManifest()));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.PackageTypeInvalid);
        importer.CallCount.Should().Be(0);
    }

    // ---------- T8 importer PartialImport ----------

    [Fact]
    public async Task ImporterPartialImport_FailsQuarantineRequired_MarksRunQuarantined()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-8");
        var importer = FakeSolutionImporter.Failure(
            SolutionImportFailureKind.PartialImport,
            "ImportJob for 'SpaarkeMaster' completed but reported failure: boom");
        var handler = BuildHandler(repo, importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest()));

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
            ImmutableArray.Create("SpaarkeMaster"),
            "SpaarkeMaster 1.2.0.0 is installed as unmanaged, but the run imported the managed package."));
        var handler = BuildHandler(repo, importer, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.VerificationFailed);
        failure.Diagnostic.Should().Contain("SpaarkeMaster").And.Contain("installed as unmanaged");
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
        var handler = BuildHandler(repo, importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest()));

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
            new InvalidOperationException("verifier transport fault"));
        var handler = BuildHandler(repo, importer, verifier);

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
        var handler = BuildHandler(repo,
            FakeSolutionImporter.Success(),
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest()));

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
    public void IdempotencyKey_IsDeterministicByCustomerIdAndPackageType()
    {
        H6SolutionImportHandler.BuildIdempotencyKey("acme", "managed").Should().Be("solimport-acme-managed");
        H6SolutionImportHandler.BuildIdempotencyKey("acme", "unmanaged").Should().Be("solimport-acme-unmanaged");
    }

    // ---------- T20 run not found ----------

    [Fact]
    public async Task RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var importer = FakeSolutionImporter.Success();
        var handler = BuildHandler(repo, importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest()));

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
    [InlineData(SolutionImportFailureKind.PackageTypeMismatch, SolutionImportRejectionCodes.PackageTypeMismatch, FailureClass.Resumable)]
    [InlineData(SolutionImportFailureKind.DowngradeRefused, SolutionImportRejectionCodes.DowngradeRefused, FailureClass.Resumable)]
    public void MapImporterFailure_ProducesExpectedRejectionAndClass(
        SolutionImportFailureKind kind,
        string expectedRejection,
        FailureClass expectedClass)
    {
        var (rejection, cls) = H6SolutionImportHandler.MapImporterFailure(kind);
        rejection.Should().Be(expectedRejection);
        cls.Should().Be(expectedClass);
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
        var handler = BuildHandler(repo, importer,
            FakeSolutionVerifier.AllPresent(BuildExpectedManifest()));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(expectedClass);
        failure.RejectionCode.Should().Be(expectedCode);
    }

    private static H6SolutionImportHandler BuildHandler(
        FakeRepository repo,
        ISolutionImporter importer,
        ISolutionVerifier verifier,
        string? clientSecret = ClientSecret,
        Sprk.Provisioning.ControlPlane.Handlers.Credentials.WorkerCredentialSelectionOptions? credentials = null,
        IRequiredApplicationsInstaller? requiredAppsInstaller = null,
        IOrgSettingsContractApplier? orgSettingsApplier = null)
    {
        var options = Options.Create(new SolutionImportOptions
        {
            ClientSecret = clientSecret,
            ImportTimeout = TimeSpan.FromSeconds(10),
            VerifierCallTimeout = TimeSpan.FromSeconds(5),
            // A44.5: default (unconfigured) = legacy [ClientSecret] chain —
            // every pre-existing test in this file keeps task-141/204a
            // semantics unchanged.
            Credentials = credentials ?? new Sprk.Provisioning.ControlPlane.Handlers.Credentials.WorkerCredentialSelectionOptions(),
        });
        // HANDLER-07 + HANDLER-08 (Wave 2 pre-dispatch remediation
        // 2026-08-27; both lifted to LIVE impls 2026-08-27 Wave 2.5):
        // default to Success-returning stubs so existing H6 orchestration
        // tests remain focused on H6-level flow (parity with pre-Wave-2.5
        // scaffold behavior that also returned Success unconditionally).
        // HANDLER-07/08-specific H6 tests inject Failure-returning fakes
        // explicitly. Direct coverage of the LIVE
        // `PacRequiredApplicationsInstaller` shell-out lives in
        // <see cref="PacRequiredApplicationsInstallerTests"/>; the LIVE
        // `PacOrgSettingsContractApplier` shell-out in
        // <see cref="PacOrgSettingsContractApplierTests"/>.
        return new H6SolutionImportHandler(
            repo, importer, verifier,
            requiredAppsInstaller ?? new StubRequiredApplicationsInstaller(
                new RequiredApplicationsInstallOutcome.Success(StaticRequiredApplicationsManifest.DefaultRequiredApplicationNames)),
            new StaticRequiredApplicationsManifest(),
            orgSettingsApplier ?? new StubOrgSettingsContractApplier(
                new OrgSettingsContractOutcome.Success(StaticOrgSettingsContractManifest.DefaultOrgSettings)),
            new StaticOrgSettingsContractManifest(),
            options,
            TimeProvider.System,
            NullLogger<H6SolutionImportHandler>.Instance);
    }

    // ---------- HANDLER-07 required-applications gate (Wave 2 pre-dispatch remediation 2026-08-27) ----------

    private sealed class StubRequiredApplicationsInstaller : IRequiredApplicationsInstaller
    {
        private readonly RequiredApplicationsInstallOutcome _outcome;
        public int CallCount { get; private set; }
        public RequiredApplicationsInstallRequest? LastRequest { get; private set; }
        public StubRequiredApplicationsInstaller(RequiredApplicationsInstallOutcome outcome) => _outcome = outcome;
        public Task<RequiredApplicationsInstallOutcome> EnsureInstalledAsync(
            RequiredApplicationsInstallRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }

    private sealed class StubOrgSettingsContractApplier : IOrgSettingsContractApplier
    {
        private readonly OrgSettingsContractOutcome _outcome;
        public int CallCount { get; private set; }
        public OrgSettingsContractApplyRequest? LastRequest { get; private set; }
        public StubOrgSettingsContractApplier(OrgSettingsContractOutcome outcome) => _outcome = outcome;
        public Task<OrgSettingsContractOutcome> ApplyAsync(
            OrgSettingsContractApplyRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }

    [Fact]
    public async Task Handler07_RequiredApps_FailureFromInstaller_FailsResumable_NoImporterCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-h07");
        var importer = FakeSolutionImporter.Success();
        var verifier = FakeSolutionVerifier.AllPresent(BuildExpectedManifest());
        var failingInstaller = new StubRequiredApplicationsInstaller(
            new RequiredApplicationsInstallOutcome.Failure("msft_PowerBI_Anchor install timed out at 6min poll."));
        var handler = BuildHandler(repo, importer, verifier,
            requiredAppsInstaller: failingInstaller);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.MissingRequiredApplication);
        failure.Diagnostic.Should().Contain("msft_PowerBI_Anchor");
        importer.CallCount.Should().Be(0, "importer MUST NOT fire when required-apps gate fails");
        failingInstaller.CallCount.Should().Be(1);
        failingInstaller.LastRequest!.RequiredApplicationNames.Should().Contain("msft_PowerBI_Anchor");
    }

    [Fact]
    public async Task Handler07_RequiredApps_Success_ProceedsToImporter()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-h07-ok");
        var importer = FakeSolutionImporter.Success();
        var verifier = FakeSolutionVerifier.AllPresent(BuildExpectedManifest());
        var okInstaller = new StubRequiredApplicationsInstaller(
            new RequiredApplicationsInstallOutcome.Success(new[] { "msft_PowerBI_Anchor" }));
        var handler = BuildHandler(repo, importer, verifier,
            requiredAppsInstaller: okInstaller);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        okInstaller.CallCount.Should().Be(1);
        importer.CallCount.Should().Be(1, "importer MUST fire when required-apps gate passes");
    }

    // ---------- HANDLER-08 org-settings contract gate (Wave 2 pre-dispatch remediation 2026-08-27) ----------

    [Fact]
    public async Task Handler08_OrgSettings_FailureFromApplier_FailsResumable_NoImporterCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-h08");
        var importer = FakeSolutionImporter.Success();
        var verifier = FakeSolutionVerifier.AllPresent(BuildExpectedManifest());
        var failingApplier = new StubOrgSettingsContractApplier(
            new OrgSettingsContractOutcome.Failure("maxuploadfilesize apply failed: pac org update-settings exit 1."));
        var handler = BuildHandler(repo, importer, verifier,
            orgSettingsApplier: failingApplier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SolutionImportRejectionCodes.OrgSettingsContractFailed);
        failure.Diagnostic.Should().Contain("maxuploadfilesize");
        importer.CallCount.Should().Be(0, "importer MUST NOT fire when org-settings gate fails");
        failingApplier.CallCount.Should().Be(1);
        failingApplier.LastRequest!.OrgSettings.Should().ContainKey("maxuploadfilesize");
        failingApplier.LastRequest.OrgSettings["maxuploadfilesize"].Should().Be("25600000");
    }

    [Fact]
    public void StaticManifests_MatchCanonicalR1Values()
    {
        // Regression guard: the canonical values ship in the constants.
        StaticRequiredApplicationsManifest.DefaultRequiredApplicationNames
            .Should().Contain("msft_PowerBI_Anchor");
        StaticOrgSettingsContractManifest.DefaultOrgSettings
            .Should().ContainKey("maxuploadfilesize");
        StaticOrgSettingsContractManifest.DefaultOrgSettings["maxuploadfilesize"]
            .Should().Be("25600000", "F14 verbatim: 25 MB = 25,600,000 bytes");
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
            TenancyModel = "Model2",
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

    private static ImmutableArray<ImportedSolutionRecord> BuildExpectedManifest(bool isManaged = true)
        => ImmutableArray.Create(new ImportedSolutionRecord(
            SolutionUniqueName: "SpaarkeMaster",
            Version: "1.2.0.0",
            SolutionId: Guid.NewGuid().ToString(),
            IsManaged: isManaged));

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
        public SolutionVerificationRequest? LastRequest { get; private set; }

        public FakeSolutionVerifier(SolutionVerificationOutcome outcome) => _outcome = outcome;

        public static FakeSolutionVerifier AllPresent(ImmutableArray<ImportedSolutionRecord> manifest)
            => new(new SolutionVerificationOutcome.AllPresent(manifest));

        public Task<SolutionVerificationOutcome> VerifyAsync(
            SolutionVerificationRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }

    /// <summary>Importer that always throws — models an infrastructure fault.</summary>
    private sealed class ThrowingSolutionImporter : ISolutionImporter
    {
        private readonly Exception _exception;
        public ThrowingSolutionImporter(Exception ex) => _exception = ex;

        public Task<SolutionImportOutcome> ImportAsync(SolutionImportRequest request, CancellationToken ct)
            => throw _exception;
    }

    /// <summary>Verifier that always throws — models an infrastructure fault.</summary>
    private sealed class ThrowingSolutionVerifier : ISolutionVerifier
    {
        private readonly Exception _exception;
        public ThrowingSolutionVerifier(Exception ex) => _exception = ex;

        public Task<SolutionVerificationOutcome> VerifyAsync(SolutionVerificationRequest request, CancellationToken ct)
            => throw _exception;
    }
}
