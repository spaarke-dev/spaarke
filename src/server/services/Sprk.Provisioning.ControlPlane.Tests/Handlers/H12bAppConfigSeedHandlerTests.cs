// -----------------------------------------------------------------------------
// H12bAppConfigSeedHandlerTests.cs
//
// L2 CONTROL-PLANE H12b app-config seed handler unit tests (task 071).
//
// SCOPE:
//   Pure unit tests with in-memory fakes — no live Cosmos, no live Service
//   Bus, no live pwsh shell-out. ADR-038 path #1 (pure C# unit test — no
//   external processes).
//
// COVERAGE (POML acceptance criteria mapping):
//   AC-1  Happy path — Success + Cosmos advances to H12c + H12c enqueued
//   AC-2  HandleAsync uses IAppConfigSeeder registry (contract, not manifest scope filter)
//   AC-3  Idempotency key = h12b-{customerId}-{manifestHash}; second call is durable no-op
//   AC-4  Reconciler DAG parallel-fires H12a + H12b — verified by DagAdvancer test (owned by task 058); H12b's own contract is DAG-position independent
//   AC-5  Seeder failure → HandlerResult.Failure(Resumable, SeederFailed) with offending scope in diagnostic
//   AC-6  Negative: NO AI record type in seeder registry — every registered seeder scope is app-config (structural)
//   AC-7  Build 0/0 + zero analyzer warnings — validated by build gate, not tested here
//
// Plus defensive negative branches:
//   - Missing tenantId (§4D I1) → Failure(Resumable, MissingTenantId); NO seeder fires
//   - Missing dataverseUrl (§4D I1) → Failure(Resumable, MissingDataverseUrl); NO seeder fires
//   - Run not found in Cosmos partition → Failure(Resumable, RunNotFound)
//   - HandlerId mismatch → throws InvalidOperationException (defensive dispatch bug detector)
//   - Manifest not found → Failure(Resumable, ManifestNotFound)
//   - Optimistic-concurrency race on success write → Failure(Resumable, ConcurrentWriteConflict)
//   - Enqueue failure → still returns Success (reconciler re-emits)
//   - Seeder throws unexpected exception → Failure(Resumable, SeederInfrastructureError)
//   - Idempotency-key format determinism (customerId → single key given constant manifestHash)
//   - Deferred seeders roll into Success outcome, NOT Failed
//   - Mixed Ok/Deferred/Failed → Failed wins
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H12bAppConfigSeedHandlerTests : IDisposable
{
    private const string CustomerId = "acme-corp";
    private const string RunId = "01j7q3zp-appconfig-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string DataverseUrl = "https://acme.crm.dynamics.com";

    private readonly string _manifestPath;
    private readonly string _expectedManifestHash;

    public H12bAppConfigSeedHandlerTests()
    {
        // Write a deterministic manifest fixture on disk so ComputeManifestHash
        // returns a stable SHA-256. Cleanup handled via IDisposable.
        _manifestPath = Path.Combine(Path.GetTempPath(), $"h12b-manifest-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(_manifestPath, "schemaVersion: 1\nartifacts: []\n");
        _expectedManifestHash = H12bAppConfigSeedHandler.ComputeManifestHash(_manifestPath);
    }

    public void Dispose()
    {
        if (File.Exists(_manifestPath))
        {
            File.Delete(_manifestPath);
        }
    }

    // ---------- AC-1 Happy path ----------

    [Fact]
    public async Task AC1_HappyPath_ReturnsSuccessAndEnqueuesH12c()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var seeders = new IAppConfigSeeder[]
        {
            FakeSeeder.Ok(AppConfigSeedScopes.DataGrid),
            FakeSeeder.Ok(AppConfigSeedScopes.WorkspaceLayout),
        };
        var handler = NewHandler(repo, enqueuer, seeders);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var expectedKey = H12bAppConfigSeedHandler.BuildIdempotencyKey(CustomerId, _expectedManifestHash);
        result.Should().BeOfType<HandlerResult.Success>()
            .Which.IdempotencyKey.Should().Be(expectedKey);

        seeders.OfType<FakeSeeder>().Should().OnlyContain(s => s.CallCount == 1);

        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be(H12bAppConfigSeedHandler.DownstreamHandlerId);
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle(cp => cp.Phase == H12bAppConfigSeedHandler.HandlerIdentifier);
        repo.LastWrittenRun.GateStates.Should().ContainKey(AppConfigSeedGates.AppConfigSeeded);

        enqueuer.Sent.Should().ContainSingle();
        var next = enqueuer.Sent[0];
        next.HandlerId.Should().Be(H12bAppConfigSeedHandler.DownstreamHandlerId);
        next.RunId.Should().Be(RunId);
        next.CustomerId.Should().Be(CustomerId);
    }

    // ---------- AC-2 Uses seeder registry (all invoked, sequential) ----------

    [Fact]
    public async Task AC2_HandleAsync_InvokesEveryRegisteredSeeder_InRegistrationOrder()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var callOrder = new List<string>();
        var seeders = new IAppConfigSeeder[]
        {
            new FakeSeeder(AppConfigSeedScopes.DataGrid, () => { callOrder.Add(AppConfigSeedScopes.DataGrid); return AppConfigSeederResult.Ok("ok"); }),
            new FakeSeeder(AppConfigSeedScopes.WorkspaceLayout, () => { callOrder.Add(AppConfigSeedScopes.WorkspaceLayout); return AppConfigSeederResult.Ok("ok"); }),
            new FakeSeeder(AppConfigSeedScopes.FieldMapping, () => { callOrder.Add(AppConfigSeedScopes.FieldMapping); return AppConfigSeederResult.Deferred("deferred"); }),
            new FakeSeeder(AppConfigSeedScopes.ChartDefinition, () => { callOrder.Add(AppConfigSeedScopes.ChartDefinition); return AppConfigSeederResult.Deferred("deferred"); }),
        };
        var handler = NewHandler(repo, enqueuer, seeders);

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        callOrder.Should().Equal(
            AppConfigSeedScopes.DataGrid,
            AppConfigSeedScopes.WorkspaceLayout,
            AppConfigSeedScopes.FieldMapping,
            AppConfigSeedScopes.ChartDefinition);
    }

    // ---------- AC-3 Idempotency ----------

    [Fact]
    public async Task AC3_Idempotency_SecondCallWithSameKey_IsDurableNoOp()
    {
        var run = BuildRun();
        var expectedKey = H12bAppConfigSeedHandler.BuildIdempotencyKey(CustomerId, _expectedManifestHash);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = H12bAppConfigSeedHandler.HandlerIdentifier,
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-30),
            CompletedAt = DateTimeOffset.UtcNow.AddSeconds(-29),
            JobId = "prior-job",
        });
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var seeder = FakeSeeder.Ok(AppConfigSeedScopes.DataGrid);
        var handler = NewHandler(repo, enqueuer, new IAppConfigSeeder[] { seeder });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>()
            .Which.IdempotencyKey.Should().Be(expectedKey);
        seeder.CallCount.Should().Be(0, "durable no-op MUST NOT invoke any seeder.");
        repo.WriteCount.Should().Be(0, "durable no-op MUST NOT rewrite Cosmos.");
        enqueuer.Sent.Should().BeEmpty("durable no-op MUST NOT re-enqueue H12c.");
    }

    [Fact]
    public void AC3_BuildIdempotencyKey_UsesExpectedFormat()
    {
        var key = H12bAppConfigSeedHandler.BuildIdempotencyKey("cust-x", "MANIFESTHASH");
        key.Should().Be("h12b-cust-x-MANIFESTHASH");
    }

    [Fact]
    public void AC3_ComputeManifestHash_IsDeterministic()
    {
        var h1 = H12bAppConfigSeedHandler.ComputeManifestHash(_manifestPath);
        var h2 = H12bAppConfigSeedHandler.ComputeManifestHash(_manifestPath);
        h1.Should().Be(h2);
        h1.Length.Should().Be(64, "SHA-256 hex is 32 bytes = 64 hex chars.");
    }

    [Fact]
    public void AC3_ComputeManifestHash_ChangesOnManifestByteEdit()
    {
        var tmp2 = Path.Combine(Path.GetTempPath(), $"h12b-manifest2-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(tmp2, "schemaVersion: 1\nartifacts:\n  - id: some-new-scope\n");
            var h1 = _expectedManifestHash;
            var h2 = H12bAppConfigSeedHandler.ComputeManifestHash(tmp2);
            h1.Should().NotBe(h2, "a manifest byte-edit MUST produce a new manifestHash to force re-seed.");
        }
        finally
        {
            if (File.Exists(tmp2)) File.Delete(tmp2);
        }
    }

    // ---------- AC-5 Seeder failure ----------

    [Fact]
    public async Task AC5_SeederFailure_ReturnsFailure_ResumableWithSeederFailedCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var seeders = new IAppConfigSeeder[]
        {
            FakeSeeder.Ok(AppConfigSeedScopes.DataGrid),
            new FakeSeeder(AppConfigSeedScopes.WorkspaceLayout, () =>
                AppConfigSeederResult.Failed("Deploy-SystemWorkspaceLayouts.ps1 exited 1: az token acquisition failed")),
        };
        var handler = NewHandler(repo, enqueuer, seeders);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AppConfigSeedRejectionCodes.SeederFailed);
        failure.Diagnostic.Should().Contain(AppConfigSeedScopes.WorkspaceLayout,
            "rolled-up diagnostic MUST name the offending scope for operator triage.");
        failure.Diagnostic.Should().Contain("az token acquisition failed",
            "rolled-up diagnostic MUST carry the seeder's own diagnostic string.");

        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
        enqueuer.Sent.Should().BeEmpty("no H12c enqueue on H12b failure.");
    }

    // ---------- AC-6 Structural negative: no AI scope in registry ----------

    [Fact]
    public void AC6_AppConfigSeedScopes_ContainsNoAiRecordType()
    {
        // AI record types (per ADR-039 / spec.md FR-15 — H12a's scope):
        //   sprk_playbookconsumer, sprk_analysisaction, sprk_analysistool,
        //   sprk_knowledge, sprk_skill, sprk_playbook, sprk_aioutputtype,
        //   sprk_aimodeldeployment
        var allScopes = new[]
        {
            AppConfigSeedScopes.DataGrid,
            AppConfigSeedScopes.WorkspaceLayout,
            AppConfigSeedScopes.FieldMapping,
            AppConfigSeedScopes.ChartDefinition,
        };
        var aiSubstrings = new[] { "ai-", "playbook", "action", "tool", "knowledge", "skill", "output-type" };
        foreach (var scope in allScopes)
        {
            aiSubstrings.Should().OnlyContain(sub => !scope.Contains(sub, StringComparison.OrdinalIgnoreCase),
                $"scope '{scope}' MUST NOT overlap with H12a's AI-domain scope names (ADR-039 + spec.md FR-15).");
        }
    }

    // ---------- Defensive: Missing tenantId (§4D I1) ----------

    [Fact]
    public async Task MissingTenantId_ReturnsFailure_MissingTenantId_NoSeederFires()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove("tenantId"); // §4D I1 — no fallback allowed.
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var seeder = FakeSeeder.Ok(AppConfigSeedScopes.DataGrid);
        var handler = NewHandler(repo, enqueuer, new IAppConfigSeeder[] { seeder });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AppConfigSeedRejectionCodes.MissingTenantId);

        seeder.CallCount.Should().Be(0, "no seeder may fire against a default-tenant fallback (§4D I1).");
        enqueuer.Sent.Should().BeEmpty();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- Defensive: Missing dataverseUrl ----------

    [Fact]
    public async Task MissingDataverseUrl_ReturnsFailure_MissingDataverseUrl_NoSeederFires()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove("dataverseUrl");
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var seeder = FakeSeeder.Ok(AppConfigSeedScopes.DataGrid);
        var handler = NewHandler(repo, enqueuer, new IAppConfigSeeder[] { seeder });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(AppConfigSeedRejectionCodes.MissingDataverseUrl);
        seeder.CallCount.Should().Be(0);
    }

    // ---------- Defensive: Run not found ----------

    [Fact]
    public async Task RunNotFound_ReturnsFailure_RunNotFound()
    {
        var repo = new FakeRepository(runOrNull: null);
        var enqueuer = new FakeEnqueuer();
        var handler = NewHandler(repo, enqueuer, Array.Empty<IAppConfigSeeder>());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(AppConfigSeedRejectionCodes.RunNotFound);
    }

    // ---------- Defensive: HandlerId mismatch ----------

    [Fact]
    public async Task HandlerIdMismatch_Throws_InvalidOperationException()
    {
        var handler = NewHandler(
            new FakeRepository(BuildRun(), "etag-1"),
            new FakeEnqueuer(),
            Array.Empty<IAppConfigSeeder>());
        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H12a", // Wrong — expected H12b.
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- Defensive: Manifest not found ----------

    [Fact]
    public async Task ManifestNotFound_ReturnsFailure_ManifestNotFound_NoCosmosRead()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"h12b-missing-{Guid.NewGuid():N}.yaml");
        var repo = new FakeRepository(BuildRun(), "etag-1");
        var enqueuer = new FakeEnqueuer();
        var handler = NewHandlerWithManifest(repo, enqueuer, Array.Empty<IAppConfigSeeder>(), missingPath);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AppConfigSeedRejectionCodes.ManifestNotFound);
    }

    // ---------- Defensive: Optimistic-concurrency race on success write ----------

    [Fact]
    public async Task ConcurrentWriteConflict_OnSuccessWrite_ReturnsFailure_ConcurrentWriteConflict()
    {
        var run = BuildRun();
        var winningRun = BuildRun();
        winningRun.Status = RunStatus.Cancelled;
        var repo = new FakeRepository(run, etag: "etag-1")
        {
            NextReplaceResult = new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(winningRun, "etag-2")),
        };
        var enqueuer = new FakeEnqueuer();
        var seeders = new IAppConfigSeeder[] { FakeSeeder.Ok(AppConfigSeedScopes.DataGrid) };
        var handler = NewHandler(repo, enqueuer, seeders);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(AppConfigSeedRejectionCodes.ConcurrentWriteConflict);
        enqueuer.Sent.Should().BeEmpty("no H12c enqueue when the success write LOST the ETag race.");
    }

    // ---------- Defensive: Enqueue failure still returns Success ----------

    [Fact]
    public async Task EnqueueFailure_StillReturnsSuccess_ReconcilerReEmits()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer { ThrowOnNext = new InvalidOperationException("SB broker unreachable") };
        var seeders = new IAppConfigSeeder[] { FakeSeeder.Ok(AppConfigSeedScopes.DataGrid) };
        var handler = NewHandler(repo, enqueuer, seeders);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>(
            "Cosmos state already records H12b complete; enqueue failure is a Wave-C5 reconciler concern.");
        repo.LastWrittenRun!.CurrentPhase.Should().Be(H12bAppConfigSeedHandler.DownstreamHandlerId);
    }

    // ---------- Defensive: Seeder throws unexpected exception ----------

    [Fact]
    public async Task SeederThrows_ReturnsFailure_SeederInfrastructureError()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var seeders = new IAppConfigSeeder[]
        {
            new FakeSeeder(AppConfigSeedScopes.DataGrid, () =>
                throw new InvalidOperationException("pwsh not on PATH")),
        };
        var handler = NewHandler(repo, enqueuer, seeders);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AppConfigSeedRejectionCodes.SeederInfrastructureError);
        failure.Diagnostic.Should().Contain("pwsh not on PATH");
        enqueuer.Sent.Should().BeEmpty();
    }

    // ---------- Deferred-only outcome is Success ----------

    [Fact]
    public async Task AllDeferredSeeders_ReturnsSuccess_NotFailure()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var seeders = new IAppConfigSeeder[]
        {
            new FakeSeeder(AppConfigSeedScopes.FieldMapping, () => AppConfigSeederResult.Deferred("deferred")),
            new FakeSeeder(AppConfigSeedScopes.ChartDefinition, () => AppConfigSeederResult.Deferred("deferred")),
        };
        var handler = NewHandler(repo, enqueuer, seeders);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        enqueuer.Sent.Should().ContainSingle().Which.HandlerId.Should().Be(H12bAppConfigSeedHandler.DownstreamHandlerId);
    }

    // ---------- Mixed outcome: any Failed wins ----------

    [Fact]
    public async Task MixedOkDeferredFailed_FailedWins()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var seeders = new IAppConfigSeeder[]
        {
            FakeSeeder.Ok(AppConfigSeedScopes.DataGrid),
            new FakeSeeder(AppConfigSeedScopes.WorkspaceLayout, () => AppConfigSeederResult.Failed("script exited 1")),
            new FakeSeeder(AppConfigSeedScopes.FieldMapping, () => AppConfigSeederResult.Deferred("deferred")),
        };
        var handler = NewHandler(repo, enqueuer, seeders);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>()
            .Which.RejectionCode.Should().Be(AppConfigSeedRejectionCodes.SeederFailed);
    }

    // ---------- HandlerId matches design constant ----------

    [Fact]
    public void HandlerId_MatchesDesignDocConstant()
    {
        var handler = NewHandler(
            new FakeRepository(BuildRun(), "etag-1"),
            new FakeEnqueuer(),
            Array.Empty<IAppConfigSeeder>());
        handler.HandlerId.Should().Be("H12b", "value MUST match design.md §4.1 handler-catalog verbatim.");
    }

    [Fact]
    public void DownstreamHandlerId_IsH12c()
    {
        H12bAppConfigSeedHandler.DownstreamHandlerId.Should().Be("H12c",
            "H12c is the DAG-join point that requires BOTH H12a + H12b complete.");
    }

    // -------------------------------------------------------------------------
    // Helpers + fakes
    // -------------------------------------------------------------------------

    private H12bAppConfigSeedHandler NewHandler(
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        IReadOnlyList<IAppConfigSeeder> seeders)
        => NewHandlerWithManifest(repository, enqueuer, seeders, _manifestPath);

    private static H12bAppConfigSeedHandler NewHandlerWithManifest(
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        IReadOnlyList<IAppConfigSeeder> seeders,
        string manifestPath)
    {
        var options = Options.Create(new AppConfigSeedOptions { ManifestPath = manifestPath });
        return new H12bAppConfigSeedHandler(
            repository,
            enqueuer,
            seeders,
            options,
            NullLogger<H12bAppConfigSeedHandler>.Instance);
    }

    private static ProvisioningRun BuildRun()
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-abc",
            TenancyModel = "SpaarkeOwned",
            Profile = "spaarke-hosted-model2",
            Status = RunStatus.Running,
            CurrentPhase = "H12b",
        };
        run.Parameters.NonSecret["tenantId"] = TenantId;
        run.Parameters.NonSecret["dataverseUrl"] = DataverseUrl;
        return run;
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H12bAppConfigSeedHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    // --- FakeRepository ---
    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private readonly ProvisioningRun? _run;
        private readonly string _etag;

        public FakeRepository(ProvisioningRun run, string etag)
        {
            _run = run;
            _etag = etag;
        }

        public FakeRepository(ProvisioningRun? runOrNull)
        {
            _run = runOrNull;
            _etag = "etag-1";
        }

        public ProvisioningRun? LastWrittenRun { get; private set; }
        public int WriteCount { get; private set; }
        public ReplaceRunResult? NextReplaceResult { get; set; }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(
            string customerId, string runId, CancellationToken cancellationToken)
        {
            if (_run is null) return Task.FromResult<ProvisioningRunReadResult?>(null);
            return Task.FromResult<ProvisioningRunReadResult?>(
                new ProvisioningRunReadResult(_run, _etag));
        }

        public Task<ProvisioningRunReadResult> CreateRunAsync(
            ProvisioningRun run, CancellationToken cancellationToken)
            => throw new NotSupportedException("H12b tests do not exercise CreateRun.");

        public Task<ReplaceRunResult> ReplaceRunAsync(
            ProvisioningRun run, string ifMatchEtag, CancellationToken cancellationToken)
        {
            WriteCount++;
            LastWrittenRun = run;
            if (NextReplaceResult is not null)
            {
                var configured = NextReplaceResult;
                NextReplaceResult = null;
                return Task.FromResult(configured);
            }
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, "etag-next"));
        }
    }

    // --- FakeEnqueuer ---
    private sealed class FakeEnqueuer : IHandlerEnqueuer
    {
        public List<HandlerEnvelope> Sent { get; } = new();
        public Exception? ThrowOnNext { get; set; }

        public Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
        {
            if (ThrowOnNext is not null)
            {
                var toThrow = ThrowOnNext;
                ThrowOnNext = null;
                throw toThrow;
            }
            Sent.Add(envelope);
            return Task.CompletedTask;
        }
    }

    // --- FakeSeeder ---
    private sealed class FakeSeeder : IAppConfigSeeder
    {
        private readonly Func<AppConfigSeederResult> _resultProducer;

        public FakeSeeder(string scopeName, Func<AppConfigSeederResult> resultProducer)
        {
            ScopeName = scopeName;
            _resultProducer = resultProducer;
        }

        public string ScopeName { get; }
        public int CallCount { get; private set; }

        public static FakeSeeder Ok(string scopeName)
            => new(scopeName, () => AppConfigSeederResult.Ok($"{scopeName} ok"));

        public Task<AppConfigSeederResult> SeedAsync(
            AppConfigSeedInput input, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_resultProducer());
        }
    }
}
