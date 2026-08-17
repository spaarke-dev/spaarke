// -----------------------------------------------------------------------------
// H12aAiSeedChainHandlerTests.cs
//
// Unit tests over H12aAiSeedChainHandler (task 070 — wave C').
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live pwsh / Dataverse / Azure API. Fakes
//   replace the repository + both collaborator seams (manifest reader + seed
//   runner) so the handler orchestration logic is exercised in isolation.
//   Live-Dataverse coverage belongs in env-guarded smoke tests (H12a is not
//   exercised end-to-end at CI time by design — a real seed against a fresh
//   Dataverse env is 10-20 min).
//
// COVERAGE:
//   T1  Happy path: manifest passes retired-check + dataverseUrl present +
//       runner returns Success → HandlerResult.Success + Cosmos state
//       advances (CompletedPhase recorded + seed summary in ErrorDetail).
//   T2  Idempotent no-op: run already has H12a CompletedPhase with matching
//       key → Success (no runner call, no state mutation).
//   T3  Missing tenantId (§4D I1): Failure(Resumable, missing-tenant-id) +
//       no runner call + Cosmos marked Failed.
//   T4  Missing target Dataverse URL (POML criterion 6): Failure(Resumable,
//       missing-dataverse-url) + no runner call + Cosmos marked Failed.
//   T5  Manifest not found: Failure(Resumable, manifest-not-found) + no
//       runner call + Cosmos marked Failed.
//   T6  Manifest contains retired-artifact (POML criterion 4): Failure(
//       QuarantineRequired, manifest-contains-retired-artifact) + no runner
//       call + Cosmos marked Quarantined.
//   T7  Runner returns Failure (POML criterion 5): Failure(QuarantineRequired,
//       seed-manifest-invocation-failed) + Cosmos ErrorDetail contains
//       stderr captured by the runner.
//   T8  HandlerId mismatch: throws InvalidOperationException.
//   T9  Idempotency key format determinism: same customerId + manifestHash
//       produce same key.
//   T10 Manifest hash determinism: same bytes = same SHA-256 hex (verified
//       against FileSeedManifestReader.ComputeSha256Hex directly).
//   T11 Manifest hash change forces re-seed: different bytes = different
//       hash = different key = existing CompletedPhase does NOT match.
//   T12 Runner throws (infrastructure fault): Failure(QuarantineRequired,
//       seed-manifest-invocation-failed) + diagnostic cites exception type.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H12aAiSeedChainHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h12a-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string DataverseUrl = "https://spaarke-acme.crm.dynamics.com";
    private const string ManifestHash = "d3adb33fd3adb33fd3adb33fd3adb33fd3adb33fd3adb33fd3adb33fd3adb33f";

    // ---------- T1 happy path ----------

    [Fact]
    public async Task HappyPath_ManifestClean_RunnerSucceeds_AdvancesState()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var reader = FakeSeedManifestReader.CleanWithHash(ManifestHash);
        var runner = FakeSeedManifestRunner.Success("Seed complete. 10 artifacts OK, 5 PENDING, 1 PLACEHOLDER.");
        var handler = BuildHandler(repo, reader, runner);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H12aAiSeedChainHandler.BuildIdempotencyKey(CustomerId, ManifestHash));

        // Cosmos state advanced with completed phase + seed summary in ErrorDetail.
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H12a");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle()
            .Which.Phase.Should().Be("H12a");
        repo.LastWrittenRun.ErrorDetail.Should().StartWith("[SEED-SUMMARY]");
        repo.LastWrittenRun.ErrorDetail.Should().Contain("10 artifacts OK");

        reader.CallCount.Should().Be(1);
        runner.CallCount.Should().Be(1);
        runner.LastRequest.Should().NotBeNull();
        runner.LastRequest!.CustomerId.Should().Be(CustomerId);
        runner.LastRequest.TenantId.Should().Be(TenantId);
        runner.LastRequest.TargetDataverseUrl.Should().Be(DataverseUrl);
    }

    // ---------- T2 idempotency ----------

    [Fact]
    public async Task Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun();
        var expectedKey = H12aAiSeedChainHandler.BuildIdempotencyKey(CustomerId, ManifestHash);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H12a",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-2");
        var reader = FakeSeedManifestReader.CleanWithHash(ManifestHash);
        var runner = FakeSeedManifestRunner.Success("should-not-be-invoked");
        var handler = BuildHandler(repo, reader, runner);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        runner.CallCount.Should().Be(0, "idempotent no-op skips runner");
    }

    // ---------- T3 missing tenantId (§4D I1) ----------

    [Fact]
    public async Task MissingTenantId_FailsResumable_NoRunnerCall()
    {
        var run = BuildRun(includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-3");
        var reader = FakeSeedManifestReader.CleanWithHash(ManifestHash);
        var runner = FakeSeedManifestRunner.Success("nope");
        var handler = BuildHandler(repo, reader, runner);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSeedChainRejectionCodes.MissingTenantId);
        failure.Diagnostic.Should().Contain("§4D I1");
        runner.CallCount.Should().Be(0);
        reader.CallCount.Should().Be(0, "tenant-id guard fires BEFORE manifest read");
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- T4 missing target Dataverse URL (POML criterion 6) ----------

    [Fact]
    public async Task MissingDataverseUrl_FailsResumable_DoesNotInvokeScript()
    {
        var run = BuildRun(includeDataverseUrl: false);
        var repo = new FakeRepository(run, etag: "etag-4");
        var reader = FakeSeedManifestReader.CleanWithHash(ManifestHash);
        var runner = FakeSeedManifestRunner.Success("nope");
        var handler = BuildHandler(repo, reader, runner);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSeedChainRejectionCodes.MissingDataverseUrl);
        failure.Diagnostic.Should().Contain("interStepState.dataverseEnvUrl");
        failure.Diagnostic.Should().Contain("did NOT invoke the seeder script");
        runner.CallCount.Should().Be(0, "POML criterion 6: MUST NOT invoke script");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- T5 manifest not found ----------

    [Fact]
    public async Task ManifestNotFound_FailsResumable_NoRunnerCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-5");
        var reader = FakeSeedManifestReader.NotFound("/publish/scripts/seed-data/manifest.yaml");
        var runner = FakeSeedManifestRunner.Success("nope");
        var handler = BuildHandler(repo, reader, runner);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSeedChainRejectionCodes.ManifestNotFound);
        failure.Diagnostic.Should().Contain("manifest.yaml");
        runner.CallCount.Should().Be(0);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- T6 manifest contains retired-artifact (POML criterion 4) ----------

    [Fact]
    public async Task ManifestContainsRetiredArtifact_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-6");
        var reader = FakeSeedManifestReader.WithRetiredArtifactViolation(
            hash: ManifestHash,
            pattern: "spaarke-playbook-embeddings",
            lineNumber: 42,
            lineExcerpt: "  - id: spaarke-playbook-embeddings-index");
        var runner = FakeSeedManifestRunner.Success("nope");
        var handler = BuildHandler(repo, reader, runner);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSeedChainRejectionCodes.ManifestContainsRetiredArtifact);
        failure.Diagnostic.Should().Contain("spaarke-playbook-embeddings");
        failure.Diagnostic.Should().Contain("line 42");
        failure.Diagnostic.Should().Contain("ADR-039 amendment 2026-07-05");
        failure.Diagnostic.Should().Contain("did NOT invoke the seeder script");
        runner.CallCount.Should().Be(0, "retired-artifact violation fires BEFORE runner");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.Quarantine.Should().NotBeNull();
        repo.LastWrittenRun.Quarantine!.QuarantinedByHandler.Should().Be("H12a");
    }

    // ---------- T7 runner returns Failure (POML criterion 5) ----------

    [Fact]
    public async Task RunnerReturnsFailure_FailsQuarantineRequired_CapturesStderrInErrorDetail()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-7");
        var reader = FakeSeedManifestReader.CleanWithHash(ManifestHash);
        var stderrDiag = "Invoke-SeedManifest.ps1 exited 1 for customerId 'acme'. Stderr: Deploy-Playbooks.ps1: Dataverse 401 Unauthorized. Stdout tail: PB-001 seeded, PB-002 seeded, PB-011 FAILED";
        var runner = FakeSeedManifestRunner.Failure(stderrDiag);
        var handler = BuildHandler(repo, reader, runner);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSeedChainRejectionCodes.SeedManifestInvocationFailed);
        failure.Diagnostic.Should().Contain("Dataverse 401 Unauthorized");
        // POML criterion 5: stderr captured in Cosmos runNotes (ErrorDetail on the run).
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.ErrorDetail.Should().Contain("Dataverse 401 Unauthorized");
        repo.LastWrittenRun.ErrorDetail.Should().StartWith("[" + AiSeedChainRejectionCodes.SeedManifestInvocationFailed + "]");
    }

    // ---------- T8 handler-id mismatch ----------

    [Fact]
    public async Task HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-8");
        var handler = BuildHandler(repo,
            FakeSeedManifestReader.CleanWithHash(ManifestHash),
            FakeSeedManifestRunner.Success("nope"));

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

    // ---------- T9 idempotency key format ----------

    [Fact]
    public void IdempotencyKey_IsDeterministicByCustomerAndManifestHash()
    {
        var k1 = H12aAiSeedChainHandler.BuildIdempotencyKey("acme", ManifestHash);
        var k2 = H12aAiSeedChainHandler.BuildIdempotencyKey("acme", ManifestHash);
        k1.Should().Be(k2);
        k1.Should().Be($"h12a-acme-{ManifestHash}");
    }

    // ---------- T10 manifest hash determinism ----------

    [Fact]
    public void ManifestHash_IsDeterministicSha256_OverRawBytes()
    {
        var bytes1 = System.Text.Encoding.UTF8.GetBytes("schemaVersion: 1\nartifacts: []\n");
        var bytes2 = System.Text.Encoding.UTF8.GetBytes("schemaVersion: 1\nartifacts: []\n");
        var bytes3 = System.Text.Encoding.UTF8.GetBytes("schemaVersion: 2\nartifacts: []\n");

        var h1 = FileSeedManifestReader.ComputeSha256Hex(bytes1);
        var h2 = FileSeedManifestReader.ComputeSha256Hex(bytes2);
        var h3 = FileSeedManifestReader.ComputeSha256Hex(bytes3);

        h1.Should().Be(h2, "identical bytes must hash identically");
        h1.Should().NotBe(h3, "different content must produce different hash");
        h1.Should().MatchRegex("^[0-9a-f]{64}$", "lowercase-hex SHA-256 is 64 chars");
    }

    // ---------- T11 manifest hash change forces re-seed ----------

    [Fact]
    public async Task ManifestHashChange_DoesNotMatchPriorCompletedPhase_ReSeeds()
    {
        var priorHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var newHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        var run = BuildRun();
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H12a",
            IdempotencyKey = H12aAiSeedChainHandler.BuildIdempotencyKey(CustomerId, priorHash),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-29),
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-11");
        // Reader returns a NEW hash — simulating manifest edit between runs.
        var reader = FakeSeedManifestReader.CleanWithHash(newHash);
        var runner = FakeSeedManifestRunner.Success("re-seed OK");
        var handler = BuildHandler(repo, reader, runner);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        runner.CallCount.Should().Be(1, "new hash must NOT match prior CompletedPhase idempotency key");
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.CompletedPhases.Should().HaveCount(2);
    }

    // ---------- T12 runner throws (infrastructure fault) ----------

    [Fact]
    public async Task RunnerThrows_FailsQuarantineRequired_CitesExceptionType()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-12");
        var reader = FakeSeedManifestReader.CleanWithHash(ManifestHash);
        var runner = FakeSeedManifestRunner.Throws(new FileNotFoundException("pwsh missing"));
        var handler = BuildHandler(repo, reader, runner);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSeedChainRejectionCodes.SeedManifestInvocationFailed);
        failure.Diagnostic.Should().Contain("FileNotFoundException");
        failure.Diagnostic.Should().Contain("pwsh missing");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- helpers ----------

    private static H12aAiSeedChainHandler BuildHandler(
        FakeRepository repo,
        FakeSeedManifestReader reader,
        FakeSeedManifestRunner runner)
        => new(repo, reader, runner, NullLogger<H12aAiSeedChainHandler>.Instance);

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H12aAiSeedChainHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(
        bool includeTenantId = true,
        bool includeDataverseUrl = true)
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
            run.Parameters.NonSecret[H12aAiSeedChainHandler.TenantIdParameterKey] = TenantId;
        }
        if (includeDataverseUrl)
        {
            run.InterStepState.DataverseEnvUrl = DataverseUrl;
        }
        return run;
    }

    /// <summary>Repository fake — records last written run.</summary>
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

    /// <summary>Manifest-reader fake.</summary>
    private sealed class FakeSeedManifestReader : ISeedManifestReader
    {
        private readonly SeedManifestReadResult _result;
        public int CallCount { get; private set; }

        private FakeSeedManifestReader(SeedManifestReadResult result) => _result = result;

        public static FakeSeedManifestReader CleanWithHash(string hash)
            => new(new SeedManifestReadResult.Success(hash, RetiredArtifactViolation: null));

        public static FakeSeedManifestReader NotFound(string path)
            => new(new SeedManifestReadResult.NotFound(path));

        public static FakeSeedManifestReader WithRetiredArtifactViolation(
            string hash, string pattern, int lineNumber, string lineExcerpt)
            => new(new SeedManifestReadResult.Success(
                hash,
                new RetiredArtifactViolation(pattern, lineNumber, lineExcerpt)));

        public Task<SeedManifestReadResult> ReadAsync(CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    /// <summary>Seed-runner fake — records last request + returns canned outcomes (or throws).</summary>
    private sealed class FakeSeedManifestRunner : ISeedManifestRunner
    {
        private readonly SeedManifestInvocationOutcome? _outcome;
        private readonly Exception? _throw;
        public int CallCount { get; private set; }
        public SeedManifestInvocationRequest? LastRequest { get; private set; }

        private FakeSeedManifestRunner(SeedManifestInvocationOutcome outcome)
        {
            _outcome = outcome;
            _throw = null;
        }

        private FakeSeedManifestRunner(Exception ex)
        {
            _outcome = null;
            _throw = ex;
        }

        public static FakeSeedManifestRunner Success(string stdoutSummary)
            => new(new SeedManifestInvocationOutcome.Success(stdoutSummary));

        public static FakeSeedManifestRunner Failure(string diagnostic)
            => new(new SeedManifestInvocationOutcome.Failure(diagnostic));

        public static FakeSeedManifestRunner Throws(Exception ex) => new(ex);

        public Task<SeedManifestInvocationOutcome> InvokeAsync(
            SeedManifestInvocationRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            if (_throw is not null) throw _throw;
            return Task.FromResult(_outcome!);
        }
    }
}
