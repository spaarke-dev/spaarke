// -----------------------------------------------------------------------------
// H4KvSecretsPopulationHandlerTests.cs
//
// Unit tests over H4KvSecretsPopulationHandler (task 047 — wave C4 Batch 3D).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live KV / az CLI / ARM / Azure API. Fakes
//   replace the repository + all FIVE collaborator seams (manifest, writer,
//   T1 patcher, T1 probe, T5 granter) so the handler orchestration logic is
//   exercised in isolation. Live-Azure coverage belongs in env-guarded smoke
//   tests (H4 is not exercised end-to-end at CI time by design — a real KV
//   population + T1 PATCH requires an actual customer stamp).
//
// COVERAGE (POML acceptance criteria mapping):
//   AC-1  Fresh populate happy path — Model 2 dedicated, upgrade=false, all
//         5 collaborators green, T5 Granted → Success + CompletedPhase(H4).
//   AC-2  T1 verify success — patcher + probe both Match → Success.
//   AC-3  T1 patcher FAILURE → QuarantineRequired + kvsecrets-trap-T1-patch-failed.
//   AC-4  T1 verify MISMATCH after PATCH → QuarantineRequired + kvsecrets-trap-T1-verification-mismatch.
//   AC-5  T5 both-slot grant — Granted result → Success (both principals cited).
//   AC-6  T5 NoSlotSystemAssignedIdentity (post-Phase-C steady state) → Success.
//   AC-7  T5 Failure → Resumable + kvsecrets-trap-T5-grant-failed (not fatal).
//   AC-8  Upgrade mode rotation-safe — writer returns SkippedRotationSafe → Success.
//   AC-9  Upgrade mode rotate=true — writer returns Wrote → Success.
//   AC-10 BINDING pre-check — manifest contains Delete for Dataverse-ClientSecret
//         → QuarantineRequired + BindingPreCheckViolation + NO writer/patcher call.
//   AC-11 BINDING pre-check — manifest contains Delete for BFF-API-ClientSecret
//         → QuarantineRequired + BindingPreCheckViolation.
//   AC-12 KV write PARTIAL failure — writer returns Success with some Failed
//         entries → QuarantineRequired + kvsecrets-kv-write-partial-failure.
//   AC-13 KV writer whole-Failure (no partial state) → Resumable +
//         kvsecrets-kv-write-failed-no-partial-state.
//   AC-14 Cleartext-secret leak on interStepState → QuarantineRequired +
//         kvsecrets-cleartext-secret-leak.
//   AC-15 Idempotency — two invocations for same (customerId, secretsVer) — second
//         call short-circuits Success no-op (no writer/patcher/probe/granter calls).
//   AC-16 Manifest read failure → Resumable + kvsecrets-manifest-read-failed.
//
// Plus defensive negative branches (parameter guards + control-flow):
//   AC-17 Missing tenantId (§4D I1) → Resumable + kvsecrets-missing-tenant-id.
//   AC-18 Missing subscriptionId → Resumable + kvsecrets-missing-subscription-id.
//   AC-19 Missing keyVaultName → Resumable + kvsecrets-missing-kv-name.
//   AC-20 Missing resourceGroupName → Resumable + kvsecrets-missing-resource-group.
//   AC-21 Missing appServiceName → Resumable + kvsecrets-missing-app-service-name.
//   AC-22 Missing userAssignedIdentityResourceId → Resumable + kvsecrets-missing-uami-resource-id.
//   AC-23 Missing secretsVer → Resumable + kvsecrets-missing-secrets-version.
//   AC-24 HandlerId mismatch → throws InvalidOperationException.
//   AC-25 Idempotency-key format determinism.
//   AC-26 Run not found → Resumable + kvsecrets-run-not-found.
//   AC-27 BINDING never-delete guard grep-zero: sanity check the BindingNeverDeleteSecrets set.
//   AC-28 IsCleartextSecretPattern short-circuits on KV URI ref prefix.
//   AC-29 KvResourceId builder produces expected shape.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H4KvSecretsPopulationHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h4-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "sub-cus-acme-prod";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string AppServiceName = "sprk-acme-prod-api";
    private const string StagingSlotName = "staging";
    private const string SecretsVer = "manifest-hash-abc123";
    private const string UamiResourceId = "/subscriptions/sub-cus-acme-prod/resourceGroups/rg-spaarke-acme-prod/providers/Microsoft.ManagedIdentity/userAssignedIdentities/sprk-acme-prod-uami";

    // ---------- AC-1 fresh populate happy path (all seams green) ----------

    [Fact]
    public async Task AC1_FreshPopulate_HappyPath_SucceedsAndAdvancesState()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var manifest = FakeManifest.Success(BuildCanonicalEntries());
        var writer = FakeWriter.AllWrote();
        var patcher = FakeIdentityPatcher.Success();
        var probe = FakeArmProbe.Match();
        var granter = FakeSlotGranter.Granted("prod-principal", "staging-principal");
        var handler = BuildHandler(repo, manifest, writer, patcher, probe, granter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(
            H4KvSecretsPopulationHandler.BuildIdempotencyKey(CustomerId, SecretsVer));

        // Cosmos state advanced.
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H4");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle()
            .Which.Phase.Should().Be("H4");

        // Each seam called exactly once.
        manifest.CallCount.Should().Be(1);
        writer.CallCount.Should().Be(1);
        patcher.CallCount.Should().Be(1);
        probe.CallCount.Should().Be(1);
        granter.CallCount.Should().Be(1);
        writer.LastRequest.Should().NotBeNull();
        writer.LastRequest!.RotateExisting.Should().BeFalse("default rotation-safe");
        writer.LastRequest.UpgradeMode.Should().BeFalse("no provisionedOn param");
    }

    // ---------- AC-2 T1 verify success ----------

    [Fact]
    public async Task AC2_T1_VerifySuccess_PatcherAndProbeMatch_ReturnsSuccess()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-2");
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
    }

    // ---------- AC-3 T1 patcher FAILURE ----------

    [Fact]
    public async Task AC3_T1PatcherFailure_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-3");
        var patcher = FakeIdentityPatcher.Failure(
            "prod slot: InvalidOperationException: az webapp update exit 1: (ResourceNotFound)");
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), patcher, FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.TrapT1PatchFailed);
        failure.Diagnostic.Should().Contain("ResourceNotFound");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.Quarantine.Should().NotBeNull();
    }

    // ---------- AC-4 T1 verify MISMATCH ----------

    [Fact]
    public async Task AC4_T1VerifyMismatch_AfterPatchClaimsSuccess_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-4");
        var probe = FakeArmProbe.Mismatch(observedProd: null, observedStaging: "wrong-uami-rid");
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), probe, FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.TrapT1VerificationMismatch);
        failure.Diagnostic.Should().Contain("wrong-uami-rid");
        failure.Diagnostic.Should().Contain("(null)");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- AC-5 T5 Granted both slots ----------

    [Fact]
    public async Task AC5_T5_GrantedBothSlots_ReturnsSuccess()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-5");
        var granter = FakeSlotGranter.Granted("prod-mi-oid", "staging-mi-oid");
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(), granter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        granter.CallCount.Should().Be(1);
        granter.LastInput.Should().NotBeNull();
        granter.LastInput!.VaultResourceId.Should().Contain(KeyVaultName);
        granter.LastInput.RoleDefinitionId.Should().Be("4633458b-17de-408a-b874-0445c86b69e6",
            "public-cloud KV Secrets User role id");
    }

    // ---------- AC-6 T5 NoSlotSystemAssignedIdentity (post-Phase-C steady state) ----------

    [Fact]
    public async Task AC6_T5_NoSlotSystemAssigned_StructuralNoop_ReturnsSuccess()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-6");
        var granter = FakeSlotGranter.NoSystemAssigned();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(), granter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running,
            "post-Phase-C UAMI-only is desired steady state — treated as SUCCESS");
    }

    // ---------- AC-7 T5 Failure → Resumable ----------

    [Fact]
    public async Task AC7_T5_Failure_ReturnsResumableFailure()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-7");
        var granter = FakeSlotGranter.Failure(
            "prod slot (mi-oid-1): az role assignment create exit 1: AuthorizationFailed");
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(), granter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.TrapT5GrantFailed);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed,
            "T5 is INTERIM — Resumable not Quarantined");
    }

    // ---------- AC-8 upgrade mode rotation-safe ----------

    [Fact]
    public async Task AC8_UpgradeMode_RotateFalse_WriterReturnsSkippedRotationSafe_Success()
    {
        var run = BuildRun(provisionedOn: "2026-01-01T00:00:00Z");
        var repo = new FakeRepository(run, etag: "etag-8");
        // Writer skips all entries.
        var writer = FakeWriter.AllSkippedRotationSafe();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        writer.LastRequest!.UpgradeMode.Should().BeTrue("provisionedOn param present");
        writer.LastRequest.RotateExisting.Should().BeFalse("rotate param absent");
    }

    // ---------- AC-9 upgrade mode explicit rotate=true ----------

    [Fact]
    public async Task AC9_UpgradeMode_RotateTrue_WriterFlagPropagated_Success()
    {
        var run = BuildRun(provisionedOn: "2026-01-01T00:00:00Z", rotate: true);
        var repo = new FakeRepository(run, etag: "etag-9");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        writer.LastRequest!.UpgradeMode.Should().BeTrue();
        writer.LastRequest.RotateExisting.Should().BeTrue();
    }

    // ---------- AC-10 BINDING pre-check violation — Dataverse-ClientSecret Delete ----------

    [Fact]
    public async Task AC10_BindingPreCheck_DeleteDataverseClientSecret_FailsQuarantineNoWrites()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-10");
        var evilManifest = FakeManifest.Success(new List<KvSecretEntry>
        {
            new("Dataverse-ClientSecret", KvSecretOperation.Delete, KvSecretValueSource.FromExistingKvSecret),
            new("AiSearch--AdminKey", KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),
        });
        var writer = FakeWriter.AllWrote();
        var patcher = FakeIdentityPatcher.Success();
        var handler = BuildHandler(repo, evilManifest, writer, patcher, FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.BindingPreCheckViolation);
        failure.Diagnostic.Should().Contain("Dataverse-ClientSecret");
        failure.Diagnostic.Should().Contain("MUST rule");

        // Critical: writer + patcher NEVER called — BINDING pre-check trips
        // BEFORE any external side effect.
        writer.CallCount.Should().Be(0);
        patcher.CallCount.Should().Be(0);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- AC-11 BINDING pre-check violation — BFF-API-ClientSecret Delete ----------

    [Fact]
    public async Task AC11_BindingPreCheck_DeleteBffApiClientSecret_FailsQuarantineNoWrites()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-11");
        var evilManifest = FakeManifest.Success(new List<KvSecretEntry>
        {
            new("BFF-API-ClientSecret", KvSecretOperation.Delete, KvSecretValueSource.FromExistingKvSecret),
        });
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, evilManifest, writer, FakeIdentityPatcher.Success(),
            FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.BindingPreCheckViolation);
        failure.Diagnostic.Should().Contain("BFF-API-ClientSecret");
        writer.CallCount.Should().Be(0);
    }

    // ---------- AC-12 KV write PARTIAL failure ----------

    [Fact]
    public async Task AC12_KvWritePartialFailure_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-12");
        // Two entries wrote OK, one failed.
        var writer = new FakeWriter(new KvSecretsWriteOutcome.Success(new[]
        {
            new KvSecretWriteResult("Dataverse-ClientSecret", KvSecretWriteAction.Wrote, null),
            new KvSecretWriteResult("BFF-API-ClientSecret", KvSecretWriteAction.Wrote, null),
            new KvSecretWriteResult("AiSearch--AdminKey", KvSecretWriteAction.Failed, "throttle: 429"),
        }));
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.KvWritePartialFailure);
        failure.Diagnostic.Should().Contain("AiSearch--AdminKey");
        failure.Diagnostic.Should().Contain("throttle: 429");
    }

    // ---------- AC-13 KV writer whole-Failure (no partial state) ----------

    [Fact]
    public async Task AC13_KvWriterWholeFailure_NoPartialState_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-13");
        var writer = new FakeWriter(new KvSecretsWriteOutcome.Failure(
            "az CLI probe failed BEFORE any KV write attempted"));
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.KvWriteFailedNoPartialState);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- AC-14 cleartext-secret leak on interStepState ----------

    [Fact]
    public async Task AC14_CleartextSecretLeakOnInterStepState_FailsQuarantineRequired()
    {
        var run = BuildRun();
        // Simulate upstream regression writing a secret-shaped literal into an interstep field.
        run.InterStepState.ContainerTypeId = "Nx8Q~aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789.-_";
        var repo = new FakeRepository(run, etag: "etag-14");
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.CleartextSecretLeak);
        failure.Diagnostic.Should().Contain("ContainerTypeId");
        failure.Diagnostic.Should().Contain("ADR-028");
    }

    // ---------- AC-15 idempotency ----------

    [Fact]
    public async Task AC15_Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun();
        var expectedKey = H4KvSecretsPopulationHandler.BuildIdempotencyKey(CustomerId, SecretsVer);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H4",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-15");
        var manifest = FakeManifest.Success(BuildCanonicalEntries());
        var writer = FakeWriter.AllWrote();
        var patcher = FakeIdentityPatcher.Success();
        var probe = FakeArmProbe.Match();
        var granter = FakeSlotGranter.NoSystemAssigned();
        var handler = BuildHandler(repo, manifest, writer, patcher, probe, granter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        manifest.CallCount.Should().Be(0);
        writer.CallCount.Should().Be(0);
        patcher.CallCount.Should().Be(0);
        probe.CallCount.Should().Be(0);
        granter.CallCount.Should().Be(0);
    }

    // ---------- AC-16 manifest read failure ----------

    [Fact]
    public async Task AC16_ManifestReadFailure_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-16");
        var manifest = FakeManifest.Failure("YAML parse error at line 42");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, manifest, writer, FakeIdentityPatcher.Success(),
            FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.ManifestReadFailed);
        failure.Diagnostic.Should().Contain("YAML parse error");
        writer.CallCount.Should().Be(0);
    }

    // ---------- AC-17..AC-23 parameter guards ----------

    [Theory]
    [InlineData(H4KvSecretsPopulationHandler.TenantIdParameterKey,
                KvSecretsPopulationRejectionCodes.MissingTenantId)]
    [InlineData(H4KvSecretsPopulationHandler.SubscriptionIdParameterKey,
                KvSecretsPopulationRejectionCodes.MissingSubscriptionId)]
    [InlineData(H4KvSecretsPopulationHandler.KeyVaultNameParameterKey,
                KvSecretsPopulationRejectionCodes.MissingKeyVaultName)]
    [InlineData(H4KvSecretsPopulationHandler.ResourceGroupNameParameterKey,
                KvSecretsPopulationRejectionCodes.MissingResourceGroupName)]
    [InlineData(H4KvSecretsPopulationHandler.AppServiceNameParameterKey,
                KvSecretsPopulationRejectionCodes.MissingAppServiceName)]
    [InlineData(H4KvSecretsPopulationHandler.UamiResourceIdParameterKey,
                KvSecretsPopulationRejectionCodes.MissingUamiResourceId)]
    [InlineData(H4KvSecretsPopulationHandler.SecretsVersionParameterKey,
                KvSecretsPopulationRejectionCodes.MissingSecretsVersion)]
    public async Task AC17to23_MissingRequiredParameter_FailsResumable_NoWriterCall(
        string parameterKey, string expectedRejectionCode)
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(parameterKey);
        var repo = new FakeRepository(run, etag: "etag-guard");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(expectedRejectionCode);
        writer.CallCount.Should().Be(0);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- AC-24 handler-id mismatch ----------

    [Fact]
    public async Task AC24_HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-24");
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned());

        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H3",
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- AC-25 idempotency-key format determinism ----------

    [Fact]
    public void AC25_IdempotencyKey_IsDeterministicByCustomerAndSecretsVer()
    {
        var k1 = H4KvSecretsPopulationHandler.BuildIdempotencyKey("acme", "hash-v1");
        var k2 = H4KvSecretsPopulationHandler.BuildIdempotencyKey("acme", "hash-v1");
        k1.Should().Be(k2);
        k1.Should().Be("kv-acme-hash-v1");

        // Different secretsVer → different key (upgrade path invalidates key).
        H4KvSecretsPopulationHandler.BuildIdempotencyKey("acme", "hash-v2").Should().NotBe(k1);

        // Different customer → different key.
        H4KvSecretsPopulationHandler.BuildIdempotencyKey("other", "hash-v1").Should().NotBe(k1);
    }

    // ---------- AC-26 run not found ----------

    [Fact]
    public async Task AC26_RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.RunNotFound);
    }

    // ---------- AC-27 BINDING never-delete grep-zero sanity ----------

    [Fact]
    public void AC27_BindingNeverDeleteSet_ContainsExactlyDataverseAndBffApiClientSecret()
    {
        H4KvSecretsPopulationHandler.BindingNeverDeleteSecrets.Should().HaveCount(2);
        H4KvSecretsPopulationHandler.BindingNeverDeleteSecrets.Should().Contain("Dataverse-ClientSecret");
        H4KvSecretsPopulationHandler.BindingNeverDeleteSecrets.Should().Contain("BFF-API-ClientSecret");
    }

    // ---------- AC-28 IsCleartextSecretPattern KV URI ref short-circuit ----------

    [Fact]
    public void AC28_IsCleartextSecretPattern_ShortCircuitsOnKvUriRef()
    {
        // Legit KV URI ref — MUST NOT trip.
        H4KvSecretsPopulationHandler.IsCleartextSecretPattern(
            "@Microsoft.KeyVault(SecretUri=https://sprk-acme-prod-kv.vault.azure.net/secrets/Dataverse-ClientSecret/)")
            .Should().BeFalse();

        // Cleartext-shape — MUST trip.
        H4KvSecretsPopulationHandler.IsCleartextSecretPattern(
            "Nx8Q~aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789.-_")
            .Should().BeTrue();

        // Short + blank — safe.
        H4KvSecretsPopulationHandler.IsCleartextSecretPattern("").Should().BeFalse();
        H4KvSecretsPopulationHandler.IsCleartextSecretPattern(null).Should().BeFalse();
        H4KvSecretsPopulationHandler.IsCleartextSecretPattern("guid-like").Should().BeFalse();
    }

    // ---------- AC-29 KvResourceId builder ----------

    [Fact]
    public void AC29_BuildKvResourceId_ProducesCanonicalShape()
    {
        var rid = H4KvSecretsPopulationHandler.BuildKvResourceId(
            SubscriptionId, ResourceGroupName, KeyVaultName);
        rid.Should().Be(
            $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroupName}/providers/Microsoft.KeyVault/vaults/{KeyVaultName}");
    }

    // =========================================================================
    // Row A38a (task 205a, 2026-08-25) — secret-free omit via the task-126
    // FR-39 OmitCanonicalNames seam + positive migration marker
    // =========================================================================

    private static readonly string[] A38aOmitTargets =
    {
        "BFF-API-ClientSecret",
        "ServiceBus-ConnectionString",
        "AiSearch--AdminKey",
    };

    [Fact]
    public async Task A38a1_SecretFreeTrue_UnionsThreeTargetsIntoExistingOmitSeam()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a38a1");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned(),
            options: new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        // The omit flows through the SAME KvSecretWriteRequest.OmitCanonicalNames
        // seam task 126 landed — no parallel mechanism.
        writer.LastRequest!.OmitCanonicalNames.Should().Contain(A38aOmitTargets);
        writer.LastRequest.OmitCanonicalNames.Should().NotContain("Dataverse-ClientSecret",
            "Q3 Path A rollback copy is never omitted (§6.5 record 2026-08-25)");
    }

    [Fact]
    public async Task A38a2_DefaultOptions_OmitSetStaysEmpty_NoRegression()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a38a2");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        writer.LastRequest!.OmitCanonicalNames.Should().BeEmpty(
            "client-secret environments are unchanged by A38a (default RequireSecretFreeIdentity=false)");
    }

    [Fact]
    public async Task A38a3_OperatorFicOmitParameter_StillWorks_AndUnionsWithSecretFreeTargets()
    {
        // Existing FR-39 operator path (task 126): ficOmitSecretNames run
        // parameter. A38a UNIONS into it — both sources coexist.
        var run = BuildRun();
        run.Parameters.NonSecret[H4KvSecretsPopulationHandler.FicOmitSecretNamesParameterKey] =
            "Some-Operator-Chosen-Secret, Another-One";
        var repo = new FakeRepository(run, etag: "etag-a38a3");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned(),
            options: new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true });

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        writer.LastRequest!.OmitCanonicalNames.Should().Contain("Some-Operator-Chosen-Secret");
        writer.LastRequest.OmitCanonicalNames.Should().Contain("Another-One");
        writer.LastRequest.OmitCanonicalNames.Should().Contain(A38aOmitTargets);
        writer.LastRequest.OmitCanonicalNames.Should().HaveCount(5);
    }

    [Fact]
    public async Task A38a4_Q3PathARollback_TargetsNotOmitted_MarkerNotApplied()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a38a4");
        var writer = FakeWriter.AllWrote();
        var marker = FakeMarkerApplier.Success();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned(),
            markerApplier: marker,
            options: new KvSecretsPopulationOptions
            {
                RequireSecretFreeIdentity = true,
                SecretFreeIdentityRollback = true,
            });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        writer.LastRequest!.OmitCanonicalNames.Should().BeEmpty(
            "Q3 Path A rollback re-includes the three targets (regression path)");
        marker.CallCount.Should().Be(0,
            "a rolled-back environment is not secret-free — the positive marker MUST NOT be applied");
    }

    [Fact]
    public async Task A38a5_SecretFreeTrue_MarkerAppliedOnceWithVaultAndTenant()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a38a5");
        var marker = FakeMarkerApplier.Success();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned(), markerApplier: marker,
            options: new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        marker.CallCount.Should().Be(1);
        marker.LastRequest!.KeyVaultName.Should().Be(KeyVaultName);
        marker.LastRequest.TenantId.Should().Be(TenantId);
        marker.LastRequest.SubscriptionId.Should().Be(SubscriptionId);
        marker.LastRequest.ResourceGroupName.Should().Be(ResourceGroupName);
    }

    [Fact]
    public async Task A38a6_SecretFreeFalse_MarkerNotApplied()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a38a6");
        var marker = FakeMarkerApplier.Success();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned(), markerApplier: marker);

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        marker.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task A38a7_MarkerFailure_FailsResumable_WithMarkerRejectionCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a38a7");
        var marker = FakeMarkerApplier.Failure(
            "A38a marker: no sprk_dataverseenvironment registry row found for tenantId");
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned(), markerApplier: marker,
            options: new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable,
            "marker application is idempotent — operator fixes cause + resumes (FAIL-LOUD, never silent)");
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.SecretFreeMarkerApplyFailed);
        failure.Diagnostic.Should().Contain("registry row");
    }

    [Fact]
    public async Task A38a8_Idempotency_SecondInvocation_NoSecondMarkerApply()
    {
        // Run-level idempotency: a matching CompletedPhase short-circuits the
        // ENTIRE handler (including marker application) — the "2nd invocation
        // is a no-op" contract at handler level. Applier-level idempotency
        // (tag check-then-apply) is covered by
        // ArmSecretFreeMarkerApplier.IsVaultTagAlreadyApplied tests.
        var run = BuildRun();
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H4",
            IdempotencyKey = H4KvSecretsPopulationHandler.BuildIdempotencyKey(CustomerId, SecretsVer),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-a38a8");
        var marker = FakeMarkerApplier.Success();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned(), markerApplier: marker,
            options: new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        marker.CallCount.Should().Be(0, "Level-3 idempotency short-circuits before any external work");
    }

    [Fact]
    public async Task A38a8b_MarkerAlreadyApplied_SecondRunOutcome_StillSuccess()
    {
        // Applier reports the tag was already present (idempotent re-apply on
        // a resumed run) — the handler treats Applied(true) as Success.
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a38a8b");
        var marker = FakeMarkerApplier.AlreadyApplied();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned(), markerApplier: marker,
            options: new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        marker.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task A38a9_Model2FanOut_ThreeCustomerVaults_MarkerAppliedOncePerVault_Uniform()
    {
        // Model 2 fan-out: the per-customer DISPATCH iteration invokes H4
        // once per customer vault — there is deliberately no N-vault loop
        // inside a single run. Simulate the fan-out as three H4 invocations
        // sharing one marker applier and assert once-per-vault uniformity
        // (the property the §5.3 fleet-consistency detector guards).
        var marker = FakeMarkerApplier.Success();
        var customers = new[] { ("acme", "kv-acme-v1"), ("globex", "kv-globex-v1"), ("initech", "kv-initech-v1") };

        foreach (var (customerId, vaultName) in customers)
        {
            var run = BuildRun();
            run.CustomerId = customerId;
            run.Parameters.NonSecret[H4KvSecretsPopulationHandler.KeyVaultNameParameterKey] = vaultName;
            var repo = new FakeRepository(run, etag: $"etag-a38a9-{customerId}");
            var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
                FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
                FakeSlotGranter.NoSystemAssigned(), markerApplier: marker,
                options: new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true });

            var envelope = new HandlerEnvelope
            {
                HandlerId = H4KvSecretsPopulationHandler.HandlerIdentifier,
                RunId = RunId,
                CustomerId = customerId,
                ParametersJson = "{}",
                EnqueuedAt = DateTimeOffset.UtcNow,
            };

            var result = await handler.HandleAsync(envelope, CancellationToken.None);
            result.Should().BeOfType<HandlerResult.Success>();
        }

        marker.CallCount.Should().Be(3, "one application per customer vault — no vault skipped, none doubled");
        marker.AppliedVaults.Should().BeEquivalentTo(new[] { "kv-acme-v1", "kv-globex-v1", "kv-initech-v1" },
            "all N per-customer vaults receive the marker uniformly (remediation plan §5.3)");
    }

    // ---------- AC-30 T1 patcher throws (infrastructure fault) ----------

    [Fact]
    public async Task AC30_T1PatcherThrowsUnexpectedException_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-30");
        var patcher = FakeIdentityPatcher.Throws(
            new InvalidOperationException("az CLI not on PATH"));
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), patcher, FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired,
            "T1 infrastructure fault after successful writes is QuarantineRequired — App Service still un-patched");
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.TrapT1PatchFailed);
        failure.Diagnostic.Should().Contain("InvalidOperationException");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- AC-31 MarkComplete optimistic-concurrency Conflict ----------

    [Fact]
    public async Task AC31_MarkComplete_ConcurrencyConflictOnFinalWrite_ReturnsConcurrentWriteConflict()
    {
        var run = BuildRun();
        // FakeRepository variant that succeeds on the FIRST write (never happens
        // in H4 — no intermediate writes) but returns Conflict on the FINAL
        // MarkComplete write. Since H4's happy path only writes once, we use a
        // Conflict-only fake here.
        var repo = new ConflictOnFinalWriteRepository(run, etag: "etag-31");
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.ConcurrentWriteConflict);
        failure.Diagnostic.Should().Contain("Concurrent write");
    }

    // ---------- AC-32 MarkComplete NotFound (row deleted mid-flight) ----------

    [Fact]
    public async Task AC32_MarkComplete_RunDeletedMidFlight_ReturnsRunDeletedDuringPopulation()
    {
        var run = BuildRun();
        var repo = new NotFoundOnFinalWriteRepository(run, etag: "etag-32");
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.RunDeletedDuringPopulation);
    }

    // ---------- HANDLER-09 operator KV RBAC bootstrap (Wave 2 pre-dispatch remediation 2026-08-27) ----------

    private sealed class StubOperatorKvRbacBootstrapper : IOperatorKvRbacBootstrapper
    {
        private readonly OperatorKvRbacBootstrapOutcome _outcome;
        public int CallCount { get; private set; }
        public OperatorKvRbacBootstrapRequest? LastRequest { get; private set; }
        public StubOperatorKvRbacBootstrapper(OperatorKvRbacBootstrapOutcome outcome) => _outcome = outcome;
        public Task<OperatorKvRbacBootstrapOutcome> EnsureGrantedAsync(
            OperatorKvRbacBootstrapRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }

    [Fact]
    public async Task Handler09_OperatorKvRbacBootstrap_Failure_FailsResumable_NoWriterCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-h09");
        var manifest = FakeManifest.Success(Array.Empty<KvSecretEntry>());
        var writer = FakeWriter.AllWrote();
        var patcher = FakeIdentityPatcher.Success();
        var probe = FakeArmProbe.Match();
        var granter = FakeSlotGranter.NoSystemAssigned();
        var failingBootstrapper = new StubOperatorKvRbacBootstrapper(
            new OperatorKvRbacBootstrapOutcome.Failure("Insufficient permission — could not PUT role assignment."));

        var handler = new H4KvSecretsPopulationHandler(
            repo, manifest, writer, patcher, probe, granter,
            FakeMarkerApplier.Success(),
            failingBootstrapper,
            Options.Create(new KvSecretsPopulationOptions()),
            NullLogger<H4KvSecretsPopulationHandler>.Instance);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.OperatorKvRbacBootstrapFailed);
        failure.Diagnostic.Should().Contain("Insufficient permission");
        writer.CallCount.Should().Be(0, "writer MUST NOT fire when bootstrap fails");
        failingBootstrapper.CallCount.Should().Be(1);
        failingBootstrapper.LastRequest!.RoleDefinitionId.Should().Be(KvBuiltInRoleIds.SecretsOfficer);
        failingBootstrapper.LastRequest.KeyVaultName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Handler09_KvBuiltInRoleIds_SecretsOfficer_MatchesF15bVerbatim()
    {
        // F15b verbatim: role definition id b86a8fe4-44ce-4948-aee5-eccb2c155cd7
        KvBuiltInRoleIds.SecretsOfficer.Should().Be("b86a8fe4-44ce-4948-aee5-eccb2c155cd7");
    }

    // ---------- helpers ----------

    private static H4KvSecretsPopulationHandler BuildHandler(
        IProvisioningRunRepository repo,
        IKvSecretManifest manifest,
        IKvSecretsWriter writer,
        IAppServiceIdentityPatcher patcher,
        IArmKeyVaultRefProbe probe,
        ISlotIdentityRoleGranter granter,
        FakeMarkerApplier? markerApplier = null,
        KvSecretsPopulationOptions? options = null)
    {
        // HANDLER-09 (Wave 2 pre-dispatch remediation 2026-08-27): default
        // to the production scaffold IOperatorKvRbacBootstrapper — returns
        // Success unconditionally so existing tests remain unaffected.
        return new H4KvSecretsPopulationHandler(
            repo, manifest, writer, patcher, probe, granter,
            markerApplier ?? FakeMarkerApplier.Success(),
            new ArmOperatorKvRbacBootstrapper(NullLogger<ArmOperatorKvRbacBootstrapper>.Instance),
            Options.Create(options ?? new KvSecretsPopulationOptions()),
            NullLogger<H4KvSecretsPopulationHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H4KvSecretsPopulationHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(
        string? provisionedOn = null,
        bool rotate = false)
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
        run.Parameters.NonSecret[H4KvSecretsPopulationHandler.TenantIdParameterKey] = TenantId;
        run.Parameters.NonSecret[H4KvSecretsPopulationHandler.SubscriptionIdParameterKey] = SubscriptionId;
        run.Parameters.NonSecret[H4KvSecretsPopulationHandler.KeyVaultNameParameterKey] = KeyVaultName;
        run.Parameters.NonSecret[H4KvSecretsPopulationHandler.ResourceGroupNameParameterKey] = ResourceGroupName;
        run.Parameters.NonSecret[H4KvSecretsPopulationHandler.AppServiceNameParameterKey] = AppServiceName;
        run.Parameters.NonSecret[H4KvSecretsPopulationHandler.UamiResourceIdParameterKey] = UamiResourceId;
        run.Parameters.NonSecret[H4KvSecretsPopulationHandler.SecretsVersionParameterKey] = SecretsVer;
        if (provisionedOn is not null)
        {
            run.Parameters.NonSecret[H4KvSecretsPopulationHandler.ProvisionedOnParameterKey] = provisionedOn;
        }
        if (rotate)
        {
            run.Parameters.NonSecret[H4KvSecretsPopulationHandler.RotateExistingParameterKey] = "true";
        }
        return run;
    }

    private static IReadOnlyList<KvSecretEntry> BuildCanonicalEntries() => new List<KvSecretEntry>
    {
        new("Dataverse-ClientSecret", KvSecretOperation.Upsert, KvSecretValueSource.FromExistingKvSecret),
        new("BFF-API-ClientSecret",   KvSecretOperation.Upsert, KvSecretValueSource.FromExistingKvSecret),
        new("AiSearch--AdminKey",     KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),
    };

    // ---------- fakes ----------

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

    private sealed class FakeManifest : IKvSecretManifest
    {
        private readonly KvSecretManifestReadResult _result;
        public int CallCount { get; private set; }
        private FakeManifest(KvSecretManifestReadResult result) => _result = result;
        public static FakeManifest Success(IReadOnlyList<KvSecretEntry> entries)
            => new(new KvSecretManifestReadResult.Success(entries));
        public static FakeManifest Failure(string diagnostic)
            => new(new KvSecretManifestReadResult.Failure(diagnostic));
        public Task<KvSecretManifestReadResult> ReadAsync(CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeWriter : IKvSecretsWriter
    {
        private readonly KvSecretsWriteOutcome _outcome;
        public int CallCount { get; private set; }
        public KvSecretWriteRequest? LastRequest { get; private set; }

        public FakeWriter(KvSecretsWriteOutcome outcome) => _outcome = outcome;

        public static FakeWriter AllWrote() => new(new KvSecretsWriteOutcome.Success(new[]
        {
            new KvSecretWriteResult("Dataverse-ClientSecret", KvSecretWriteAction.Wrote, null),
            new KvSecretWriteResult("BFF-API-ClientSecret",   KvSecretWriteAction.Wrote, null),
            new KvSecretWriteResult("AiSearch--AdminKey",     KvSecretWriteAction.Wrote, null),
        }));

        public static FakeWriter AllSkippedRotationSafe() => new(new KvSecretsWriteOutcome.Success(new[]
        {
            new KvSecretWriteResult("Dataverse-ClientSecret", KvSecretWriteAction.SkippedRotationSafe, null),
            new KvSecretWriteResult("BFF-API-ClientSecret",   KvSecretWriteAction.SkippedRotationSafe, null),
            new KvSecretWriteResult("AiSearch--AdminKey",     KvSecretWriteAction.SkippedRotationSafe, null),
        }));

        public Task<KvSecretsWriteOutcome> WriteAsync(KvSecretWriteRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }

    private sealed class FakeIdentityPatcher : IAppServiceIdentityPatcher
    {
        private readonly AppServiceIdentityPatchResult? _result;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }
        public AppServiceIdentityPatchInput? LastInput { get; private set; }
        private FakeIdentityPatcher(AppServiceIdentityPatchResult? result, Exception? throwOnCall)
        {
            _result = result;
            _throwOnCall = throwOnCall;
        }
        public static FakeIdentityPatcher Success() => new(new AppServiceIdentityPatchResult.Success(), null);
        public static FakeIdentityPatcher Failure(string diagnostic)
            => new(new AppServiceIdentityPatchResult.Failure(diagnostic), null);
        public static FakeIdentityPatcher Throws(Exception ex) => new(null, ex);
        public Task<AppServiceIdentityPatchResult> PatchKeyVaultReferenceIdentityAsync(
            AppServiceIdentityPatchInput input, CancellationToken ct)
        {
            CallCount++;
            LastInput = input;
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_result!);
        }
    }

    /// <summary>Repository variant — first Read succeeds, first Write returns Conflict.</summary>
    private sealed class ConflictOnFinalWriteRepository : IProvisioningRunRepository
    {
        private readonly ProvisioningRun _run;
        private readonly string _etag;
        public ConflictOnFinalWriteRepository(ProvisioningRun run, string etag)
        {
            _run = run;
            _etag = etag;
        }
        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult<ProvisioningRunReadResult?>(new ProvisioningRunReadResult(_run, _etag));
        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
        {
            // Simulate a concurrent write that advanced the run's status.
            var winning = new ProvisioningRun
            {
                RunId = run.RunId,
                CustomerId = run.CustomerId,
                EnvironmentId = run.EnvironmentId,
                TenancyModel = run.TenancyModel,
                Profile = run.Profile,
                Status = RunStatus.Cancelled, // Some winning state.
            };
            var current = new ProvisioningRunReadResult(winning, ifMatchEtag + "-winning");
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Conflict(current));
        }
    }

    /// <summary>Repository variant — first Read succeeds, first Write returns NotFound (row deleted mid-flight).</summary>
    private sealed class NotFoundOnFinalWriteRepository : IProvisioningRunRepository
    {
        private readonly ProvisioningRun _run;
        private readonly string _etag;
        public NotFoundOnFinalWriteRepository(ProvisioningRun run, string etag)
        {
            _run = run;
            _etag = etag;
        }
        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult<ProvisioningRunReadResult?>(new ProvisioningRunReadResult(_run, _etag));
        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
            => Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.NotFound());
    }

    private sealed class FakeArmProbe : IArmKeyVaultRefProbe
    {
        private readonly ArmKeyVaultRefProbeResult _result;
        public int CallCount { get; private set; }
        public ArmKeyVaultRefProbeInput? LastInput { get; private set; }
        private FakeArmProbe(ArmKeyVaultRefProbeResult result) => _result = result;
        public static FakeArmProbe Match() => new(new ArmKeyVaultRefProbeResult.Match());
        public static FakeArmProbe Mismatch(string? observedProd, string? observedStaging)
            => new(new ArmKeyVaultRefProbeResult.Mismatch(observedProd, observedStaging));
        public Task<ArmKeyVaultRefProbeResult> VerifyKeyVaultReferenceIdentityAsync(
            ArmKeyVaultRefProbeInput input, CancellationToken ct)
        {
            CallCount++;
            LastInput = input;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeSlotGranter : ISlotIdentityRoleGranter
    {
        private readonly SlotIdentityRoleGrantResult _result;
        public int CallCount { get; private set; }
        public SlotIdentityRoleGrantInput? LastInput { get; private set; }
        private FakeSlotGranter(SlotIdentityRoleGrantResult result) => _result = result;
        public static FakeSlotGranter Granted(string prodPrincipal, string stagingPrincipal)
            => new(new SlotIdentityRoleGrantResult.Granted(prodPrincipal, stagingPrincipal));
        public static FakeSlotGranter NoSystemAssigned()
            => new(new SlotIdentityRoleGrantResult.NoSlotSystemAssignedIdentity(
                "No System-Assigned MI on prod or staging (post-Phase-C steady state)."));
        public static FakeSlotGranter Failure(string diagnostic)
            => new(new SlotIdentityRoleGrantResult.Failure(diagnostic));
        public Task<SlotIdentityRoleGrantResult> GrantAsync(
            SlotIdentityRoleGrantInput input, CancellationToken ct)
        {
            CallCount++;
            LastInput = input;
            return Task.FromResult(_result);
        }
    }

    /// <summary>Row A38a — stub ISecretFreeMarkerApplier recording every application (vault list for fan-out uniformity assertions).</summary>
    private sealed class FakeMarkerApplier : ISecretFreeMarkerApplier
    {
        private readonly SecretFreeMarkerApplyOutcome _outcome;
        public int CallCount { get; private set; }
        public SecretFreeMarkerApplyRequest? LastRequest { get; private set; }
        public List<string> AppliedVaults { get; } = new();

        private FakeMarkerApplier(SecretFreeMarkerApplyOutcome outcome) => _outcome = outcome;

        public static FakeMarkerApplier Success()
            => new(new SecretFreeMarkerApplyOutcome.Applied(VaultTagWasAlreadyPresent: false));

        public static FakeMarkerApplier AlreadyApplied()
            => new(new SecretFreeMarkerApplyOutcome.Applied(VaultTagWasAlreadyPresent: true));

        public static FakeMarkerApplier Failure(string diagnostic)
            => new(new SecretFreeMarkerApplyOutcome.Failure(diagnostic));

        public Task<SecretFreeMarkerApplyOutcome> ApplyAsync(
            SecretFreeMarkerApplyRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            AppliedVaults.Add(request.KeyVaultName);
            return Task.FromResult(_outcome);
        }
    }
}
