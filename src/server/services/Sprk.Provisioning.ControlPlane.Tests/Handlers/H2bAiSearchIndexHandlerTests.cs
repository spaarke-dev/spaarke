// -----------------------------------------------------------------------------
// H2bAiSearchIndexHandlerTests.cs
//
// Unit tests over H2bAiSearchIndexHandler (task 045 — wave C4).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live AI Search / az CLI / REST /
//   pwsh. Fakes replace the repository + the three collaborator seams
//   (catalog, provisioner, verifier) so the handler orchestration logic is
//   exercised in isolation. Live-Azure coverage belongs in env-guarded smoke
//   tests.
//
// COVERAGE:
//   T1  Happy path, both tenancy models (task 225b — one path): provisioner +
//       verifier called against the stamp's own endpoint (H2a output) →
//       Success + Cosmos state advances.
//   T3  Idempotent no-op: run already has H2b CompletedPhase with matching
//       key → Success (no seam called, no state mutation).
//   T4  Missing tenantId (§4D I1): Failure(Resumable, missing-tenant-id) +
//       no seam call + Cosmos marked Failed.
//   T5  Requested index with no embedded schema (task 245b — indexVer is the
//       schema set's content version): Failure(Resumable, index-schema-unavailable).
//   T6  Run not found: Failure(Resumable, run-not-found).
//   T7  Retired index (spaarke-playbook-embeddings) in requested catalog:
//       Failure(QuarantineRequired, retired-index-provisioning-forbidden)
//       + Cosmos marked Quarantined.
//   T8  Retired index (spaarke-knowledge-index-v2 lineage) — same.
//   T9  Provisioner returns Failure: Failure(QuarantineRequired,
//       index-provisioning-failed).
//   T9b Provisioner returns Failure with AccessDenied (HTTP 401/403 — Search roles
//       not yet applied, task 244): Failure(Resumable, search-access-denied).
//   T10 Verifier reports InvariantViolation: Failure(QuarantineRequired,
//       index-invariant-violation) + diagnostic cites failing index.field.
//   T11 Verifier reports Missing (post-provisioner drift):
//       Failure(QuarantineRequired, index-provisioning-failed).
//   T15 Missing AiSearchEndpoint (H2a InterStepState blank), both tenancy
//       models: Failure(Resumable, missing-search-endpoint).
//   T17 HandlerId mismatch: throws InvalidOperationException.
//   T18 Idempotency key format determinism: aisearch-{customerId}-{indexVer}.
//   (T2 / T12 / T13 / T14 / T16 covered the retired Model 1 shared-platform
//   branch — deleted with it by task 225b.)
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H2bAiSearchIndexHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h2b-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    // Task 245b: H2b's idempotency version is the content version of the embedded schemas it applies.
    private static readonly string IndexVer = ComputeIndexVer(new FakeCanonicalIndexCatalog().CanonicalIndexNames);

    private static string ComputeIndexVer(IEnumerable<string> names)
    {
        IndexSchemaSet.TryComputeVersion(names, out var version, out _).Should().BeTrue();
        return version;
    }
    private const string StampEndpoint = "https://sprk-acme-prod-search.search.windows.net/";

    // ---------- T1 happy path (both tenancy models — task 225b) ----------

    [Theory]
    [InlineData("Model1")]
    [InlineData("Model2")]
    public async Task HappyPath_ProvisionerAndVerifierCalledOnStampEndpoint_Success(string tenancyModel)
    {
        var run = BuildRun(tenancyModel: tenancyModel);
        var repo = new FakeRepository(run, etag: "etag-1");
        var catalog = new FakeCanonicalIndexCatalog();
        var provisioner = FakeAiSearchIndexProvisioner.Success();
        var verifier = FakeAiSearchIndexVerifier.Ok();
        var handler = BuildHandler(repo, catalog, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H2bAiSearchIndexHandler.BuildIdempotencyKey(CustomerId, IndexVer));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H2b");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle()
            .Which.Phase.Should().Be("H2b");

        provisioner.CallCount.Should().Be(1);
        verifier.CallCount.Should().Be(1, "post-deploy verifier runs after provisioner");
        provisioner.LastRequest!.SearchEndpoint.Should().Be(StampEndpoint, "every stamp indexes its own AI Search service");
        provisioner.LastRequest.TenantId.Should().Be(TenantId, "§4D I1 — tenant flows through explicitly");
    }

    // ---------- T3 idempotency ----------

    [Fact]
    public async Task Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun();
        var expectedKey = H2bAiSearchIndexHandler.BuildIdempotencyKey(CustomerId, IndexVer);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H2b",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-3");
        var catalog = new FakeCanonicalIndexCatalog();
        var provisioner = FakeAiSearchIndexProvisioner.Success();
        var verifier = FakeAiSearchIndexVerifier.Ok();
        var handler = BuildHandler(repo, catalog, provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        provisioner.CallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
    }

    // ---------- T4 missing tenantId (§4D I1) ----------

    [Fact]
    public async Task MissingTenantId_FailsResumable_NoSeamCall()
    {
        var run = BuildRun(includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-4");
        var provisioner = FakeAiSearchIndexProvisioner.Success();
        var verifier = FakeAiSearchIndexVerifier.Ok();
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            provisioner, verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.MissingTenantId);
        failure.Diagnostic.Should().Contain("§4D I1");
        provisioner.CallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- T5 index-schema version (task 245b) ----------

    [Fact]
    public async Task RequestedIndexWithoutEmbeddedSchema_FailsResumable_NothingApplied()
    {
        var run = BuildRun();
        run.Parameters.NonSecret[H2bAiSearchIndexHandler.RequestedIndexesParameterKey] = "spaarke-files-index,spaarke-nonexistent-index";
        var repo = new FakeRepository(run, etag: "etag-5");
        var provisioner = FakeAiSearchIndexProvisioner.Success();
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            provisioner,
            FakeAiSearchIndexVerifier.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.IndexSchemaUnavailable);
        failure.Diagnostic.Should().Contain("spaarke-nonexistent-index");
        provisioner.CallCount.Should().Be(0);
    }

    [Fact]
    public void SchemaSetVersion_SameBodiesSameVersion_ChangedBodyNewVersion_OrderIndependent()
    {
        var a = IndexSchemaSet.ComputeVersion([("spaarke-files-index", "{\"name\":\"a\"}"), ("spaarke-records-index", "{\"name\":\"b\"}")]);
        var reordered = IndexSchemaSet.ComputeVersion([("spaarke-records-index", "{\"name\":\"b\"}"), ("spaarke-files-index", "{\"name\":\"a\"}")]);
        var changed = IndexSchemaSet.ComputeVersion([("spaarke-files-index", "{\"name\":\"a\",\"x\":1}"), ("spaarke-records-index", "{\"name\":\"b\"}")]);

        reordered.Should().Be(a);
        changed.Should().NotBe(a);
        IndexVer.Should().MatchRegex("^[0-9a-f]{64}$", "the version of the real embedded schema set");
    }

    // ---------- T6 run not found ----------

    [Fact]
    public async Task RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            FakeAiSearchIndexVerifier.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.RunNotFound);
    }

    // ---------- T7 retired index — spaarke-playbook-embeddings (ADR-039) ----------

    [Fact]
    public async Task RetiredIndex_PlaybookEmbeddings_FailsQuarantineRequired()
    {
        var run = BuildRun();
        run.Parameters.NonSecret[H2bAiSearchIndexHandler.RequestedIndexesParameterKey]
            = "spaarke-files-index,spaarke-playbook-embeddings,spaarke-records-index";
        var repo = new FakeRepository(run, etag: "etag-7");
        var provisioner = FakeAiSearchIndexProvisioner.Success();
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            provisioner,
            FakeAiSearchIndexVerifier.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.RetiredIndexProvisioningForbidden);
        failure.Diagnostic.Should().Contain("spaarke-playbook-embeddings");
        failure.Diagnostic.Should().Contain("ADR-039");
        provisioner.CallCount.Should().Be(0, "retired-index guard fires BEFORE any seam call");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.Quarantine.Should().NotBeNull();
    }

    // ---------- T8 retired index — spaarke-knowledge-index-v2 lineage ----------

    [Fact]
    public async Task RetiredIndex_KnowledgeIndexLineage_FailsQuarantineRequired()
    {
        var run = BuildRun();
        run.Parameters.NonSecret[H2bAiSearchIndexHandler.RequestedIndexesParameterKey]
            = "spaarke-files-index,spaarke-knowledge-index-v2";
        var repo = new FakeRepository(run, etag: "etag-8");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            FakeAiSearchIndexVerifier.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.RetiredIndexProvisioningForbidden);
        failure.Diagnostic.Should().Contain("spaarke-knowledge-index-v2");
    }

    // ---------- T9 provisioner failure ----------

    [Fact]
    public async Task ProvisionerReturnsFailure_FailsQuarantineRequired()
    {
        var run = BuildRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run, etag: "etag-9");
        var provisioner = FakeAiSearchIndexProvisioner.Failure(
            "Deploy-AllIndexes.ps1 exit 7: PUT spaarke-files-index HTTP 400: unknown field");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            provisioner,
            FakeAiSearchIndexVerifier.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.IndexProvisioningFailed);
        failure.Diagnostic.Should().Contain("exit 7");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    [Fact]
    public async Task ProvisionerAccessDenied_FailsResumable_NotQuarantined()
    {
        var run = BuildRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run, etag: "etag-9b");
        var provisioner = FakeAiSearchIndexProvisioner.AccessDenied(
            "PUT index 'spaarke-files-index' returned HTTP 403: Forbidden");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(), provisioner, FakeAiSearchIndexVerifier.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.SearchAccessDenied);
        failure.Diagnostic.Should().Contain("403").And.Contain("resume");
        repo.LastWrittenRun!.Status.Should().NotBe(RunStatus.Quarantined);
    }

    // ---------- T10 verifier reports InvariantViolation ----------

    [Fact]
    public async Task VerifierInvariantViolation_FailsQuarantineRequired()
    {
        var run = BuildRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run, etag: "etag-10");
        var verifier = FakeAiSearchIndexVerifier.InvariantViolation(
            new IndexInvariantIssue(
                "spaarke-files-index",
                "tenantId",
                "required filterable field 'tenantId' MISSING"));
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.IndexInvariantViolation);
        failure.Diagnostic.Should().Contain("spaarke-files-index");
        failure.Diagnostic.Should().Contain("tenantId");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- T11 verifier reports Missing (post-provisioner drift) ----------

    [Fact]
    public async Task VerifierMissing_FailsQuarantineRequired_AsProvisioningFailed()
    {
        var run = BuildRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run, etag: "etag-11");
        var verifier = FakeAiSearchIndexVerifier.Missing("spaarke-records-index");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            verifier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.IndexProvisioningFailed);
        failure.Diagnostic.Should().Contain("spaarke-records-index");
        failure.Diagnostic.Should().Contain("drift");
    }

    // ---------- T15 missing AiSearchEndpoint (H2a InterStepState blank), both models ----------

    [Theory]
    [InlineData("Model1")]
    [InlineData("Model2")]
    public async Task MissingAiSearchEndpoint_FailsResumable(string tenancyModel)
    {
        var run = BuildRun(tenancyModel: tenancyModel);
        run.InterStepState.AiSearchEndpoint = null; // H2a didn't populate
        var repo = new FakeRepository(run, etag: "etag-15");
        var provisioner = FakeAiSearchIndexProvisioner.Success();
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            provisioner,
            FakeAiSearchIndexVerifier.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.MissingSearchEndpoint);
        failure.Diagnostic.Should().Contain("H2a");
        provisioner.CallCount.Should().Be(0);
    }

    // ---------- T17 handler-id mismatch ----------

    [Fact]
    public async Task HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-17");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            FakeAiSearchIndexVerifier.Ok());

        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H2a",
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- T18 idempotency key format ----------

    [Fact]
    public void IdempotencyKey_IsDeterministicByCustomerAndIndexVer()
    {
        var k1 = H2bAiSearchIndexHandler.BuildIdempotencyKey("acme", "manifest-abc");
        var k2 = H2bAiSearchIndexHandler.BuildIdempotencyKey("acme", "manifest-abc");
        k1.Should().Be(k2);
        k1.Should().Be("aisearch-acme-manifest-abc");
    }

    // ---------- helpers ----------

    private static H2bAiSearchIndexHandler BuildHandler(
        FakeRepository repo,
        ICanonicalIndexCatalog catalog,
        IAiSearchIndexProvisioner provisioner,
        IAiSearchIndexVerifier verifier)
    {
        return new H2bAiSearchIndexHandler(
            repo, catalog, provisioner, verifier,
            NullLogger<H2bAiSearchIndexHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H2bAiSearchIndexHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(
        bool includeTenantId = true,
        string tenancyModel = "Model2")
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-guid",
            TenancyModel = tenancyModel,
            Status = RunStatus.Running,
            Profile = tenancyModel == "Model1" ? "spaarke-hosted-model2" : "customer-owned-model2",
        };
        if (includeTenantId)
        {
            run.Parameters.NonSecret[H2bAiSearchIndexHandler.TenantIdParameterKey] = TenantId;
        }
        // H2a populates the stamp's AI Search endpoint for every tenancy model (task 225b).
        run.InterStepState.AiSearchEndpoint = StampEndpoint;
        return run;
    }

    /// <summary>
    /// Repository fake — records last written run. Parity with H2a's
    /// FakeRepository pattern.
    /// </summary>
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

    /// <summary>Catalog fake — mirrors production for canonical + retired sets so
    /// the handler's retired-name guard exercises real name matching.</summary>
    private sealed class FakeCanonicalIndexCatalog : ICanonicalIndexCatalog
    {
        public ImmutableArray<string> CanonicalIndexNames { get; }
            = ImmutableArray.Create(
                "spaarke-files-index",
                "spaarke-discovery-index",
                "spaarke-records-index",
                "spaarke-rag-references",
                "spaarke-insights-index",
                "spaarke-session-files",
                "spaarke-invoices-index");

        public ImmutableHashSet<string> RetiredIndexNames { get; }
            = ImmutableHashSet.Create(
                StringComparer.OrdinalIgnoreCase,
                "spaarke-playbook-embeddings",
                "spaarke-knowledge-index",
                "spaarke-knowledge-index-v2",
                "spaarke-knowledge-shared");

        public bool IsRetired(string indexName) => !string.IsNullOrWhiteSpace(indexName)
            && RetiredIndexNames.Contains(indexName.Trim());
    }

    /// <summary>Provisioner fake — records last request + returns canned outcomes.</summary>
    private sealed class FakeAiSearchIndexProvisioner : IAiSearchIndexProvisioner
    {
        private readonly AiSearchIndexProvisionOutcome _outcome;
        public int CallCount { get; private set; }
        public AiSearchIndexProvisionRequest? LastRequest { get; private set; }

        private FakeAiSearchIndexProvisioner(AiSearchIndexProvisionOutcome outcome) => _outcome = outcome;

        public static FakeAiSearchIndexProvisioner Success()
            => new(new AiSearchIndexProvisionOutcome.Success(ImmutableArray<string>.Empty));

        public static FakeAiSearchIndexProvisioner Failure(string diagnostic)
            => new(new AiSearchIndexProvisionOutcome.Failure(diagnostic));

        public static FakeAiSearchIndexProvisioner AccessDenied(string diagnostic)
            => new(new AiSearchIndexProvisionOutcome.Failure(diagnostic, AccessDenied: true));

        public Task<AiSearchIndexProvisionOutcome> ProvisionAsync(
            AiSearchIndexProvisionRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }

    /// <summary>Verifier fake — records call count + returns canned outcomes.</summary>
    private sealed class FakeAiSearchIndexVerifier : IAiSearchIndexVerifier
    {
        private readonly AiSearchIndexVerifyResult _result;
        public int CallCount { get; private set; }

        private FakeAiSearchIndexVerifier(AiSearchIndexVerifyResult result) => _result = result;

        public static FakeAiSearchIndexVerifier Ok() => new(new AiSearchIndexVerifyResult.Ok());

        public static FakeAiSearchIndexVerifier Missing(params string[] names)
            => new(new AiSearchIndexVerifyResult.Missing(names.ToImmutableArray()));

        public static FakeAiSearchIndexVerifier InvariantViolation(params IndexInvariantIssue[] issues)
            => new(new AiSearchIndexVerifyResult.InvariantViolation(issues.ToImmutableArray()));

        public Task<AiSearchIndexVerifyResult> VerifyAsync(
            AiSearchIndexVerifyRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }
}
