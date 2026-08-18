// -----------------------------------------------------------------------------
// H2bAiSearchIndexHandlerTests.cs
//
// Unit tests over H2bAiSearchIndexHandler (task 045 — wave C4).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live AI Search / az CLI / REST /
//   pwsh. Fakes replace the repository + all four collaborator seams
//   (catalog, provisioner, verifier, tenant-filter template provisioner)
//   so the handler orchestration logic is exercised in isolation. Live-
//   Azure coverage belongs in env-guarded smoke tests (H2b's real
//   provisioner runs Deploy-AllIndexes.ps1 against a real AI Search
//   service — not exercised at CI time by design).
//
// COVERAGE (18 tests):
//   T1  Model 2 happy path: catalog + provisioner + verifier called →
//       Success + Cosmos state advances.
//   T2  Model 1 happy path: catalog + verifier + template provisioner
//       called → Success (provisioner NOT called — verify-only for shared).
//   T3  Idempotent no-op: run already has H2b CompletedPhase with matching
//       key → Success (no seam called, no state mutation).
//   T4  Missing tenantId (§4D I1): Failure(Resumable, missing-tenant-id) +
//       no seam call + Cosmos marked Failed.
//   T5  Missing indexVer: Failure(Resumable, missing-index-version).
//   T6  Run not found: Failure(Resumable, run-not-found).
//   T7  Retired index (spaarke-playbook-embeddings) in requested catalog:
//       Failure(QuarantineRequired, retired-index-provisioning-forbidden)
//       + Cosmos marked Quarantined (both branches).
//   T8  Retired index (spaarke-knowledge-index-v2 lineage) — same.
//   T9  Model 2 provisioner returns Failure: Failure(QuarantineRequired,
//       index-provisioning-failed).
//   T10 Model 2 verifier reports InvariantViolation: Failure(QuarantineRequired,
//       index-invariant-violation) + diagnostic cites failing index.field.
//   T11 Model 2 verifier reports Missing (post-provisioner drift):
//       Failure(QuarantineRequired, index-provisioning-failed).
//   T12 Model 1 verifier reports Missing (shared under-provisioned):
//       Failure(QuarantineRequired, shared-index-missing).
//   T13 Model 1 verifier reports InvariantViolation: Failure(QuarantineRequired,
//       index-invariant-violation).
//   T14 Model 1 template provisioner returns Failure: Failure(Resumable,
//       tenant-filter-template-provision-failed).
//   T15 Model 2 missing AiSearchEndpoint (H2a InterStepState blank):
//       Failure(Resumable, missing-search-endpoint).
//   T16 Model 1 missing SharedPlatformSearchEndpoint (config blank):
//       Failure(Resumable, missing-search-endpoint).
//   T17 HandlerId mismatch: throws InvalidOperationException.
//   T18 Idempotency key format determinism: aisearch-{customerId}-{indexVer}.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    private const string IndexVer = "manifest-abc123";
    private const string Model2Endpoint = "https://sprk-acme-search.search.windows.net/";
    private const string SharedPlatformEndpoint = "https://spaarke-search-prod.search.windows.net/";

    // ---------- T1 Model 2 happy path ----------

    [Fact]
    public async Task HappyPath_Model2Dedicated_ProvisionerAndVerifierCalled_Success()
    {
        var run = BuildRun(tenancyModel: "Model2Dedicated");
        var repo = new FakeRepository(run, etag: "etag-1");
        var catalog = new FakeCanonicalIndexCatalog();
        var provisioner = FakeAiSearchIndexProvisioner.Success();
        var verifier = FakeAiSearchIndexVerifier.Ok();
        var template = new FakeAiSearchTenantFilterTemplateProvisioner();
        var handler = BuildHandler(repo, catalog, provisioner, verifier, template);

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
        template.CallCount.Should().Be(0, "Model 2 does NOT provision tenant-filter template");
        provisioner.LastRequest!.SearchEndpoint.Should().Be(Model2Endpoint);
        provisioner.LastRequest.TenantId.Should().Be(TenantId, "§4D I1 — tenant flows through explicitly");
    }

    // ---------- T2 Model 1 happy path ----------

    [Fact]
    public async Task HappyPath_Model1Shared_VerifierAndTemplateProvisionerCalled_Success()
    {
        var run = BuildRun(tenancyModel: "Model1Shared");
        var repo = new FakeRepository(run, etag: "etag-2");
        var catalog = new FakeCanonicalIndexCatalog();
        var provisioner = FakeAiSearchIndexProvisioner.Success();
        var verifier = FakeAiSearchIndexVerifier.Ok();
        var template = new FakeAiSearchTenantFilterTemplateProvisioner();
        var handler = BuildHandler(repo, catalog, provisioner, verifier, template,
            sharedPlatformEndpoint: SharedPlatformEndpoint);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        provisioner.CallCount.Should().Be(0, "Model 1 does NOT re-create indexes on shared platform");
        verifier.CallCount.Should().Be(1, "verifier confirms presence on shared platform");
        template.CallCount.Should().Be(1, "Model 1 provisions per-tenant filter template");

        // Tenant-filter template MUST carry the tenant id verbatim (§4D I2 / FR-29).
        template.LastRequest!.TenantId.Should().Be(TenantId);
        template.LastRequest.CustomerId.Should().Be(CustomerId);
        template.LastRequest.SearchEndpoint.Should().Be(SharedPlatformEndpoint);
        template.LastRequest.IndexNames.Should().Equal(catalog.CanonicalIndexNames);
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
        var template = new FakeAiSearchTenantFilterTemplateProvisioner();
        var handler = BuildHandler(repo, catalog, provisioner, verifier, template);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        provisioner.CallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
        template.CallCount.Should().Be(0);
    }

    // ---------- T4 missing tenantId (§4D I1) ----------

    [Fact]
    public async Task MissingTenantId_FailsResumable_NoSeamCall()
    {
        var run = BuildRun(includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-4");
        var provisioner = FakeAiSearchIndexProvisioner.Success();
        var verifier = FakeAiSearchIndexVerifier.Ok();
        var template = new FakeAiSearchTenantFilterTemplateProvisioner();
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            provisioner, verifier, template);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.MissingTenantId);
        failure.Diagnostic.Should().Contain("§4D I1");
        provisioner.CallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
        template.CallCount.Should().Be(0);
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- T5 missing indexVer ----------

    [Fact]
    public async Task MissingIndexVersion_FailsResumable()
    {
        var run = BuildRun(includeIndexVer: false);
        var repo = new FakeRepository(run, etag: "etag-5");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            FakeAiSearchIndexVerifier.Ok(),
            new FakeAiSearchTenantFilterTemplateProvisioner());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.MissingIndexVersion);
    }

    // ---------- T6 run not found ----------

    [Fact]
    public async Task RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            FakeAiSearchIndexVerifier.Ok(),
            new FakeAiSearchTenantFilterTemplateProvisioner());

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
            FakeAiSearchIndexVerifier.Ok(),
            new FakeAiSearchTenantFilterTemplateProvisioner());

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
            FakeAiSearchIndexVerifier.Ok(),
            new FakeAiSearchTenantFilterTemplateProvisioner());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.RetiredIndexProvisioningForbidden);
        failure.Diagnostic.Should().Contain("spaarke-knowledge-index-v2");
    }

    // ---------- T9 Model 2 provisioner failure ----------

    [Fact]
    public async Task Model2_ProvisionerReturnsFailure_FailsQuarantineRequired()
    {
        var run = BuildRun(tenancyModel: "Model2Dedicated");
        var repo = new FakeRepository(run, etag: "etag-9");
        var provisioner = FakeAiSearchIndexProvisioner.Failure(
            "Deploy-AllIndexes.ps1 exit 7: PUT spaarke-files-index HTTP 400: unknown field");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            provisioner,
            FakeAiSearchIndexVerifier.Ok(),
            new FakeAiSearchTenantFilterTemplateProvisioner());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.IndexProvisioningFailed);
        failure.Diagnostic.Should().Contain("exit 7");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- T10 Model 2 verifier reports InvariantViolation ----------

    [Fact]
    public async Task Model2_VerifierInvariantViolation_FailsQuarantineRequired()
    {
        var run = BuildRun(tenancyModel: "Model2Dedicated");
        var repo = new FakeRepository(run, etag: "etag-10");
        var verifier = FakeAiSearchIndexVerifier.InvariantViolation(
            new IndexInvariantIssue(
                "spaarke-files-index",
                "tenantId",
                "required filterable field 'tenantId' MISSING"));
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            verifier,
            new FakeAiSearchTenantFilterTemplateProvisioner());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.IndexInvariantViolation);
        failure.Diagnostic.Should().Contain("spaarke-files-index");
        failure.Diagnostic.Should().Contain("tenantId");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- T11 Model 2 verifier reports Missing (post-provisioner drift) ----------

    [Fact]
    public async Task Model2_VerifierMissing_FailsQuarantineRequired_AsProvisioningFailed()
    {
        var run = BuildRun(tenancyModel: "Model2Dedicated");
        var repo = new FakeRepository(run, etag: "etag-11");
        var verifier = FakeAiSearchIndexVerifier.Missing("spaarke-records-index");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            verifier,
            new FakeAiSearchTenantFilterTemplateProvisioner());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.IndexProvisioningFailed);
        failure.Diagnostic.Should().Contain("spaarke-records-index");
        failure.Diagnostic.Should().Contain("drift");
    }

    // ---------- T12 Model 1 verifier reports Missing (shared under-provisioned) ----------

    [Fact]
    public async Task Model1_VerifierMissing_FailsQuarantineRequired_AsSharedIndexMissing()
    {
        var run = BuildRun(tenancyModel: "Model1Shared");
        var repo = new FakeRepository(run, etag: "etag-12");
        var verifier = FakeAiSearchIndexVerifier.Missing("spaarke-invoices-index");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            verifier,
            new FakeAiSearchTenantFilterTemplateProvisioner(),
            sharedPlatformEndpoint: SharedPlatformEndpoint);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.SharedIndexMissing);
        failure.Diagnostic.Should().Contain("spaarke-invoices-index");
        failure.Diagnostic.Should().Contain("Deploy-AllIndexes.ps1");
    }

    // ---------- T13 Model 1 verifier reports InvariantViolation ----------

    [Fact]
    public async Task Model1_VerifierInvariantViolation_FailsQuarantineRequired()
    {
        var run = BuildRun(tenancyModel: "Model1Shared");
        var repo = new FakeRepository(run, etag: "etag-13");
        var verifier = FakeAiSearchIndexVerifier.InvariantViolation(
            new IndexInvariantIssue(
                "spaarke-rag-references",
                "domain",
                "FORBIDDEN field 'domain' is present (renamed to 'documentType' per FR-17)"));
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            verifier,
            new FakeAiSearchTenantFilterTemplateProvisioner(),
            sharedPlatformEndpoint: SharedPlatformEndpoint);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.IndexInvariantViolation);
        failure.Diagnostic.Should().Contain("spaarke-rag-references");
        failure.Diagnostic.Should().Contain("domain");
    }

    // ---------- T14 Model 1 template provisioner failure ----------

    [Fact]
    public async Task Model1_TemplateProvisionerFailure_FailsResumable()
    {
        var run = BuildRun(tenancyModel: "Model1Shared");
        var repo = new FakeRepository(run, etag: "etag-14");
        var template = FakeAiSearchTenantFilterTemplateProvisioner.Failure(
            "PUT template store 429 Too Many Requests");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            FakeAiSearchIndexVerifier.Ok(),
            template,
            sharedPlatformEndpoint: SharedPlatformEndpoint);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.TenantFilterTemplateProvisionFailed);
        failure.Diagnostic.Should().Contain("429");
    }

    // ---------- T15 Model 2 missing AiSearchEndpoint (H2a InterStepState blank) ----------

    [Fact]
    public async Task Model2_MissingAiSearchEndpoint_FailsResumable()
    {
        var run = BuildRun(tenancyModel: "Model2Dedicated");
        run.InterStepState.AiSearchEndpoint = null; // H2a didn't populate
        var repo = new FakeRepository(run, etag: "etag-15");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            FakeAiSearchIndexVerifier.Ok(),
            new FakeAiSearchTenantFilterTemplateProvisioner());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.MissingSearchEndpoint);
        failure.Diagnostic.Should().Contain("H2a");
    }

    // ---------- T16 Model 1 missing SharedPlatformSearchEndpoint config ----------

    [Fact]
    public async Task Model1_MissingSharedPlatformSearchEndpointConfig_FailsResumable()
    {
        var run = BuildRun(tenancyModel: "Model1Shared");
        var repo = new FakeRepository(run, etag: "etag-16");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            FakeAiSearchIndexVerifier.Ok(),
            new FakeAiSearchTenantFilterTemplateProvisioner(),
            sharedPlatformEndpoint: null);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(AiSearchIndexRejectionCodes.MissingSearchEndpoint);
        failure.Diagnostic.Should().Contain("AiSearchIndex:SharedPlatformSearchEndpoint");
    }

    // ---------- T17 handler-id mismatch ----------

    [Fact]
    public async Task HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-17");
        var handler = BuildHandler(repo, new FakeCanonicalIndexCatalog(),
            FakeAiSearchIndexProvisioner.Success(),
            FakeAiSearchIndexVerifier.Ok(),
            new FakeAiSearchTenantFilterTemplateProvisioner());

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
        IAiSearchIndexVerifier verifier,
        IAiSearchTenantFilterTemplateProvisioner template,
        string? sharedPlatformEndpoint = null)
    {
        var options = Options.Create(new AiSearchIndexOptions
        {
            SharedPlatformSearchEndpoint = sharedPlatformEndpoint,
        });
        return new H2bAiSearchIndexHandler(
            repo, catalog, provisioner, verifier, template, options,
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
        bool includeIndexVer = true,
        string tenancyModel = "Model2Dedicated")
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-guid",
            TenancyModel = tenancyModel,
            Status = RunStatus.Running,
            Profile = tenancyModel == "Model1Shared" ? "spaarke-hosted-model1-trial" : "spaarke-hosted-model2",
        };
        if (includeTenantId)
        {
            run.Parameters.NonSecret[H2bAiSearchIndexHandler.TenantIdParameterKey] = TenantId;
        }
        if (includeIndexVer)
        {
            run.Parameters.NonSecret[H2bAiSearchIndexHandler.IndexVersionParameterKey] = IndexVer;
        }
        // Model 2 requires AiSearchEndpoint populated by H2a; Model 1 does not.
        if (string.Equals(tenancyModel, "Model2Dedicated", StringComparison.Ordinal))
        {
            run.InterStepState.AiSearchEndpoint = Model2Endpoint;
        }
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

    /// <summary>Tenant-filter template provisioner fake — records call + last request.</summary>
    private sealed class FakeAiSearchTenantFilterTemplateProvisioner : IAiSearchTenantFilterTemplateProvisioner
    {
        private readonly AiSearchTenantFilterTemplateOutcome _outcome;
        public int CallCount { get; private set; }
        public AiSearchTenantFilterTemplateRequest? LastRequest { get; private set; }

        public FakeAiSearchTenantFilterTemplateProvisioner()
            : this(new AiSearchTenantFilterTemplateOutcome.Success()) { }

        private FakeAiSearchTenantFilterTemplateProvisioner(AiSearchTenantFilterTemplateOutcome outcome)
            => _outcome = outcome;

        public static FakeAiSearchTenantFilterTemplateProvisioner Failure(string diagnostic)
            => new(new AiSearchTenantFilterTemplateOutcome.Failure(diagnostic));

        public Task<AiSearchTenantFilterTemplateOutcome> ProvisionAsync(
            AiSearchTenantFilterTemplateRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }
}
