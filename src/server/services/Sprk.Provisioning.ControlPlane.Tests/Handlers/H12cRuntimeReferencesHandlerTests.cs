// -----------------------------------------------------------------------------
// H12cRuntimeReferencesHandlerTests.cs
//
// L2 CONTROL-PLANE H12c runtime references handler unit tests (task 072).
//
// SCOPE:
//   Pure unit tests with in-memory fakes — no live Cosmos, no live Service
//   Bus, no live Dataverse Web API. ADR-038 path #1 (pure C# unit test — no
//   external processes).
//
// COVERAGE (POML acceptance criteria mapping):
//   AC-1  H12cRuntimeReferencesHandler implements IProvisioningHandler + registers in L2 DI (module test not here — see RuntimeReferencesModule; HandlerId test covers the contract)
//   AC-2  Model2Dedicated happy path — writer invoked with customer's dedicated OpenAI endpoint from InterStepState
//   AC-3  Model1Shared happy path — writer invoked with shared platform endpoint + metering-attribution note
//   AC-4  Endpoint URIs reference the ADR-020 pinned model catalog (gpt-4o/gpt-4o-mini/text-embedding-3-large with pinned versions)
//   AC-5  Idempotency key = h12c-{customerId}-{tenancyModel}-{endpointHash}; second call is durable no-op
//   AC-6  Negative: unknown tenancyModel → Failed with clear diagnostic; writer NOT invoked
//   AC-7  dotnet build/test — validated by build gate, not tested here
//
// Plus defensive negative branches:
//   - Missing tenantId (§4D I1) → Failure(Resumable, MissingTenantId); writer NOT invoked
//   - Missing H12a and/or H12b in CompletedPhases (DAG-join guard) → Failure(Resumable, MissingUpstreamHandlers)
//   - Missing dataverseUrl → Failure(Resumable, MissingDataverseUrl)
//   - Model2 missing InterStepState.OpenAiEndpoint → Failure(Resumable, MissingOpenAiEndpoint)
//   - Model1 missing SharedPlatformOpenAiEndpoint config → Failure(Resumable, MissingSharedPlatformEndpointConfiguration)
//   - Run not found in Cosmos partition → Failure(Resumable, RunNotFound)
//   - HandlerId mismatch → throws InvalidOperationException
//   - Writer failure → Failure(Resumable, ModelDeploymentWriteFailed)
//   - Optimistic-concurrency race on success write → Failure(Resumable, ConcurrentWriteConflict); no H14 enqueue
//   - H14 enqueue failure → still returns Success (reconciler re-emits)
//   - Idempotency-key + endpoint-hash determinism
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H12cRuntimeReferencesHandlerTests
{
    private const string CustomerId = "acme-corp";
    private const string RunId = "01j7q3zp-runtimerefs-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string DataverseUrl = "https://acme.crm.dynamics.com";
    private const string DedicatedEndpoint = "https://openai-acme-prod.openai.azure.com/";
    private const string SharedEndpoint = "https://openai-spaarke-platform-shared.openai.azure.com/";

    // ---------- AC-2 Model2Dedicated happy path ----------

    [Fact]
    public async Task Model2Dedicated_HappyPath_WriterInvokedWithDedicatedEndpoint_AndEnqueuesH14()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated);
        run.InterStepState.OpenAiEndpoint = DedicatedEndpoint;
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, enqueuer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var expectedHash = H12cRuntimeReferencesHandler.ComputeEndpointHash(DedicatedEndpoint);
        var expectedKey = H12cRuntimeReferencesHandler.BuildIdempotencyKey(
            CustomerId, H12cRuntimeReferencesHandler.Model2Dedicated, expectedHash);
        result.Should().BeOfType<HandlerResult.Success>()
            .Which.IdempotencyKey.Should().Be(expectedKey);

        writer.LastRequest.Should().NotBeNull();
        writer.LastRequest!.DataverseEnvironmentUrl.Should().Be(DataverseUrl);
        writer.LastRequest.TenantId.Should().Be(TenantId);
        writer.LastRequest.Provider.Should().Be(ModelProvider.AzureOpenAI);
        writer.LastRequest.Deployments.Should().OnlyContain(d => d.EndpointUri == DedicatedEndpoint);

        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be(H12cRuntimeReferencesHandler.DownstreamHandlerId);
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle(cp => cp.Phase == H12cRuntimeReferencesHandler.HandlerIdentifier);
        repo.LastWrittenRun.GateStates.Should().ContainKey(RuntimeReferencesGates.RuntimeReferencesWritten);

        enqueuer.Sent.Should().ContainSingle();
        enqueuer.Sent[0].HandlerId.Should().Be("H14");
    }

    // ---------- AC-3 Model1Shared happy path ----------

    [Fact]
    public async Task Model1Shared_HappyPath_WriterInvokedWithSharedEndpoint_AndMeteringNote()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model1Shared);
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, enqueuer, sharedEndpoint: SharedEndpoint);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        writer.LastRequest.Should().NotBeNull();
        writer.LastRequest!.Deployments.Should().OnlyContain(d => d.EndpointUri == SharedEndpoint);
        writer.LastRequest.Deployments.Should().OnlyContain(d =>
            d.Description != null && d.Description.Contains(TenantId, StringComparison.Ordinal));
    }

    // ---------- AC-4 Pinned model catalog ----------

    [Fact]
    public void PinnedModelCatalog_MatchesADR020PinnedVersions()
    {
        PinnedModelCatalog.Models.Should().HaveCount(3);
        PinnedModelCatalog.Models.Should().ContainSingle(m => m.ModelId == "gpt-4o" && m.PinnedVersion == "2024-08-06" && m.Capability == ModelCapability.Chat);
        PinnedModelCatalog.Models.Should().ContainSingle(m => m.ModelId == "gpt-4o-mini" && m.PinnedVersion == "2024-07-18" && m.Capability == ModelCapability.Chat);
        PinnedModelCatalog.Models.Should().ContainSingle(m => m.ModelId == "text-embedding-3-large" && m.PinnedVersion == "1" && m.Capability == ModelCapability.Embedding);
    }

    [Fact]
    public async Task HappyPath_WriterRequestContainsAllThreePinnedModels()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated);
        run.InterStepState.OpenAiEndpoint = DedicatedEndpoint;
        var repo = new FakeRepository(run, etag: "etag-1");
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, new FakeEnqueuer());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        writer.LastRequest!.Deployments.Select(d => d.ModelId).Should().BeEquivalentTo(
            new[] { "gpt-4o", "gpt-4o-mini", "text-embedding-3-large" });
    }

    // ---------- AC-5 Idempotency ----------

    [Fact]
    public async Task Idempotency_SecondCallWithSameKey_IsDurableNoOp()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated);
        run.InterStepState.OpenAiEndpoint = DedicatedEndpoint;
        var expectedHash = H12cRuntimeReferencesHandler.ComputeEndpointHash(DedicatedEndpoint);
        var expectedKey = H12cRuntimeReferencesHandler.BuildIdempotencyKey(
            CustomerId, H12cRuntimeReferencesHandler.Model2Dedicated, expectedHash);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = H12cRuntimeReferencesHandler.HandlerIdentifier,
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-30),
            CompletedAt = DateTimeOffset.UtcNow.AddSeconds(-29),
            JobId = "prior-job",
        });
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, enqueuer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>()
            .Which.IdempotencyKey.Should().Be(expectedKey);
        writer.CallCount.Should().Be(0, "durable no-op MUST NOT invoke the writer.");
        repo.WriteCount.Should().Be(0, "durable no-op MUST NOT rewrite Cosmos.");
        enqueuer.Sent.Should().BeEmpty("durable no-op MUST NOT re-enqueue H14.");
    }

    [Fact]
    public void BuildIdempotencyKey_UsesExpectedFormat()
    {
        var key = H12cRuntimeReferencesHandler.BuildIdempotencyKey("cust-x", "Model2Dedicated", "ABCDEF");
        key.Should().Be("h12c-cust-x-Model2Dedicated-ABCDEF");
    }

    [Fact]
    public void ComputeEndpointHash_IsDeterministic_AndChangesOnEndpointEdit()
    {
        var h1 = H12cRuntimeReferencesHandler.ComputeEndpointHash(DedicatedEndpoint);
        var h2 = H12cRuntimeReferencesHandler.ComputeEndpointHash(DedicatedEndpoint);
        h1.Should().Be(h2);
        h1.Length.Should().Be(64, "SHA-256 hex is 32 bytes = 64 hex chars.");

        var h3 = H12cRuntimeReferencesHandler.ComputeEndpointHash(SharedEndpoint);
        h1.Should().NotBe(h3, "a different endpoint MUST produce a different hash to force a re-write.");
    }

    // ---------- AC-6 Negative: unknown tenancyModel ----------

    [Fact]
    public async Task UnknownTenancyModel_ReturnsFailure_UnknownTenancyModel_WriterNotInvoked()
    {
        var run = BuildRun("SomeUnknownTier");
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, enqueuer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(RuntimeReferencesRejectionCodes.UnknownTenancyModel);
        failure.Diagnostic.Should().Contain("SomeUnknownTier");

        writer.CallCount.Should().Be(0, "unknown tenancyModel MUST NOT upsert.");
        enqueuer.Sent.Should().BeEmpty();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- Defensive: Missing tenantId ----------

    [Fact]
    public async Task MissingTenantId_ReturnsFailure_MissingTenantId_WriterNotInvoked()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated);
        run.Parameters.NonSecret.Remove("tenantId");
        var repo = new FakeRepository(run, etag: "etag-1");
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, new FakeEnqueuer());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(RuntimeReferencesRejectionCodes.MissingTenantId);
        writer.CallCount.Should().Be(0);
    }

    // ---------- Defensive: DAG-join guard ----------

    [Fact]
    public async Task MissingH12aAndH12b_ReturnsFailure_MissingUpstreamHandlers_NamesBothMissing()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated, includeUpstreamCompletion: false);
        var repo = new FakeRepository(run, etag: "etag-1");
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, new FakeEnqueuer());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(RuntimeReferencesRejectionCodes.MissingUpstreamHandlers);
        failure.Diagnostic.Should().Contain("H12a");
        failure.Diagnostic.Should().Contain("H12b");
        writer.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task MissingOnlyH12b_ReturnsFailure_NamesOnlyH12b()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated, includeUpstreamCompletion: false);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H12a",
            IdempotencyKey = "h12a-acme-corp-somehash",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            JobId = "prior-job",
        });
        var repo = new FakeRepository(run, etag: "etag-1");
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, new FakeEnqueuer());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Diagnostic.Should().NotContain("H12a,");
        failure.Diagnostic.Should().Contain("H12b");
    }

    // ---------- Defensive: Missing dataverseUrl ----------

    [Fact]
    public async Task MissingDataverseUrl_ReturnsFailure_MissingDataverseUrl()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated);
        run.InterStepState.DataverseEnvUrl = null;
        var repo = new FakeRepository(run, etag: "etag-1");
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, new FakeEnqueuer());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(RuntimeReferencesRejectionCodes.MissingDataverseUrl);
        writer.CallCount.Should().Be(0);
    }

    // ---------- Defensive: Model2 missing OpenAiEndpoint ----------

    [Fact]
    public async Task Model2Dedicated_MissingOpenAiEndpoint_ReturnsFailure()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated);
        run.InterStepState.OpenAiEndpoint = null; // H2a hasn't run / populated yet.
        var repo = new FakeRepository(run, etag: "etag-1");
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, new FakeEnqueuer());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(RuntimeReferencesRejectionCodes.MissingOpenAiEndpoint);
        writer.CallCount.Should().Be(0);
    }

    // ---------- Defensive: Model1 missing shared-endpoint config ----------

    [Fact]
    public async Task Model1Shared_MissingSharedEndpointConfig_ReturnsFailure()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model1Shared);
        var repo = new FakeRepository(run, etag: "etag-1");
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, new FakeEnqueuer(), sharedEndpoint: null);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(RuntimeReferencesRejectionCodes.MissingSharedPlatformEndpointConfiguration);
        writer.CallCount.Should().Be(0);
    }

    // ---------- Defensive: Run not found ----------

    [Fact]
    public async Task RunNotFound_ReturnsFailure_RunNotFound()
    {
        var repo = new FakeRepository(runOrNull: null);
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, new FakeEnqueuer());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(RuntimeReferencesRejectionCodes.RunNotFound);
    }

    // ---------- Defensive: HandlerId mismatch ----------

    [Fact]
    public async Task HandlerIdMismatch_Throws_InvalidOperationException()
    {
        var handler = NewHandler(
            new FakeRepository(BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated), "etag-1"),
            new FakeWriter(),
            new FakeEnqueuer());
        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H12b", // Wrong — expected H12c.
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- Defensive: Writer failure ----------

    [Fact]
    public async Task WriterFailure_ReturnsFailure_ModelDeploymentWriteFailed()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated);
        run.InterStepState.OpenAiEndpoint = DedicatedEndpoint;
        var repo = new FakeRepository(run, etag: "etag-1");
        var writer = new FakeWriter { NextOutcome = new ModelDeploymentReferenceWriteOutcome.Failure("PATCH sprk_aimodeldeployments failed: 503") };
        var handler = NewHandler(repo, writer, new FakeEnqueuer());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(RuntimeReferencesRejectionCodes.ModelDeploymentWriteFailed);
        failure.Diagnostic.Should().Contain("503");
    }

    [Fact]
    public async Task WriterThrows_ReturnsFailure_ModelDeploymentWriteFailed()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated);
        run.InterStepState.OpenAiEndpoint = DedicatedEndpoint;
        var repo = new FakeRepository(run, etag: "etag-1");
        var writer = new FakeWriter { ThrowOnNext = new InvalidOperationException("token acquisition failed") };
        var handler = NewHandler(repo, writer, new FakeEnqueuer());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(RuntimeReferencesRejectionCodes.ModelDeploymentWriteFailed);
        failure.Diagnostic.Should().Contain("token acquisition failed");
    }

    // ---------- Defensive: Optimistic-concurrency race on success write ----------

    [Fact]
    public async Task ConcurrentWriteConflict_OnSuccessWrite_ReturnsFailure_NoH14Enqueue()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated);
        run.InterStepState.OpenAiEndpoint = DedicatedEndpoint;
        var winningRun = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated);
        winningRun.Status = RunStatus.Cancelled;
        var repo = new FakeRepository(run, etag: "etag-1")
        {
            NextReplaceResult = new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(winningRun, "etag-2")),
        };
        var enqueuer = new FakeEnqueuer();
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, enqueuer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(RuntimeReferencesRejectionCodes.ConcurrentWriteConflict);
        enqueuer.Sent.Should().BeEmpty("no H14 enqueue when the success write LOST the ETag race.");
    }

    // ---------- Defensive: H14 enqueue failure still returns Success ----------

    [Fact]
    public async Task EnqueueFailure_StillReturnsSuccess_ReconcilerReEmits()
    {
        var run = BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated);
        run.InterStepState.OpenAiEndpoint = DedicatedEndpoint;
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer { ThrowOnNext = new InvalidOperationException("SB broker unreachable") };
        var writer = new FakeWriter();
        var handler = NewHandler(repo, writer, enqueuer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>(
            "Cosmos state already records H12c complete; enqueue failure is a Wave-C5 reconciler concern.");
        repo.LastWrittenRun!.CurrentPhase.Should().Be("H14");
    }

    // ---------- HandlerId matches design constant ----------

    [Fact]
    public void HandlerId_MatchesDesignDocConstant()
    {
        var handler = NewHandler(
            new FakeRepository(BuildRun(H12cRuntimeReferencesHandler.Model2Dedicated), "etag-1"),
            new FakeWriter(),
            new FakeEnqueuer());
        handler.HandlerId.Should().Be("H12c", "value MUST match design.md §4.1 handler-catalog verbatim.");
    }

    // -------------------------------------------------------------------------
    // Helpers + fakes
    // -------------------------------------------------------------------------

    private static H12cRuntimeReferencesHandler NewHandler(
        IProvisioningRunRepository repository,
        IModelDeploymentReferenceWriter writer,
        IHandlerEnqueuer enqueuer,
        string? sharedEndpoint = SharedEndpoint)
    {
        var options = Options.Create(new RuntimeReferencesOptions { SharedPlatformOpenAiEndpoint = sharedEndpoint });
        return new H12cRuntimeReferencesHandler(
            repository,
            writer,
            enqueuer,
            options,
            NullLogger<H12cRuntimeReferencesHandler>.Instance);
    }

    private static ProvisioningRun BuildRun(string tenancyModel, bool includeUpstreamCompletion = true)
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-abc",
            TenancyModel = tenancyModel,
            Profile = "spaarke-hosted-model2",
            Status = RunStatus.Running,
            CurrentPhase = "H12c",
        };
        run.Parameters.NonSecret["tenantId"] = TenantId;
        run.InterStepState.DataverseEnvUrl = DataverseUrl;

        if (includeUpstreamCompletion)
        {
            run.CompletedPhases.Add(new CompletedPhase
            {
                Phase = "H12a",
                IdempotencyKey = "h12a-acme-corp-somehash",
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
                JobId = "prior-job-a",
            });
            run.CompletedPhases.Add(new CompletedPhase
            {
                Phase = "H12b",
                IdempotencyKey = "h12b-acme-corp-somehash",
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                JobId = "prior-job-b",
            });
        }

        return run;
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H12cRuntimeReferencesHandler.HandlerIdentifier,
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
            => throw new NotSupportedException("H12c tests do not exercise CreateRun.");

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

    // --- FakeWriter ---
    private sealed class FakeWriter : IModelDeploymentReferenceWriter
    {
        public int CallCount { get; private set; }
        public ModelDeploymentReferenceWriteRequest? LastRequest { get; private set; }
        public ModelDeploymentReferenceWriteOutcome? NextOutcome { get; set; }
        public Exception? ThrowOnNext { get; set; }

        public Task<ModelDeploymentReferenceWriteOutcome> UpsertAsync(
            ModelDeploymentReferenceWriteRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;

            if (ThrowOnNext is not null)
            {
                var toThrow = ThrowOnNext;
                ThrowOnNext = null;
                throw toThrow;
            }

            var outcome = NextOutcome ?? new ModelDeploymentReferenceWriteOutcome.Success(
                request.Deployments.Select(d => d.ModelId).ToList());
            return Task.FromResult(outcome);
        }
    }
}
