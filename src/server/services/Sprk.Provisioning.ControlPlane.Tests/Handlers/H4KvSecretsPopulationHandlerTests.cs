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
//   AC-19 Missing InterStepState.KeyVaultName → Resumable + kvsecrets-missing-kv-name.
//   AC-20 Missing InterStepState.ResourceGroupName → Resumable + kvsecrets-missing-resource-group.
//   AC-21 Missing InterStepState.AppServiceName → Resumable + kvsecrets-missing-app-service-name.
//   AC-22 Missing InterStepState.MiResourceId → Resumable + kvsecrets-missing-uami-resource-id.
//         (AC-19..22: task 245a / G25 — these are H2a outputs, read from InterStepState,
//         never from run parameters; a same-named run parameter is ignored. The H2a
//         staging slot flows to T1/T5 with a "staging" fallback when blank; the KV
//         resource id is always derived — the keyVaultResourceId override is gone.)
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
    // Task 245b: secretsVer is the manifest's content version (KvSecretManifestReadResult.Success.ContentVersion).
    private const string SecretsVer = "manifest-hash-abc123";
    private const string L2PrincipalObjectId = "7d1f0c3e-2b6a-4c55-9e1d-3a8b5c6d7e8f";
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
        manifest.CallCount.Should().Be(1, "task 245b: the manifest's content version IS the key's secretsVer — read, nothing written");
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

    // AC-19..AC-22 (task 245a, G25): the customer vault name, resource group, App Service
    // name and UAMI resource id are H2a's outputs on InterStepState. A missing value keeps
    // the SAME rejection code it had when it was (wrongly) read from run parameters.
    [Theory]
    [InlineData(nameof(InterStepState.KeyVaultName),
                KvSecretsPopulationRejectionCodes.MissingKeyVaultName)]
    [InlineData(nameof(InterStepState.ResourceGroupName),
                KvSecretsPopulationRejectionCodes.MissingResourceGroupName)]
    [InlineData(nameof(InterStepState.AppServiceName),
                KvSecretsPopulationRejectionCodes.MissingAppServiceName)]
    [InlineData(nameof(InterStepState.MiResourceId),
                KvSecretsPopulationRejectionCodes.MissingUamiResourceId)]
    public async Task AC19to22_MissingH2aInterStepStateValue_FailsResumable_NoWriterCall(
        string interStepStateProperty, string expectedRejectionCode)
    {
        var run = BuildRun();
        ClearInterStepStateValue(run.InterStepState, interStepStateProperty);
        var repo = new FakeRepository(run, etag: "etag-guard-iss");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(expectedRejectionCode);
        failure.Diagnostic.Should().Contain("H2a");
        writer.CallCount.Should().Be(0);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    [Fact]
    public async Task AC19to22_LegacyRunParametersAreIgnored_InterStepStateIsTheOnlySource()
    {
        // A run parameter can never stand in for an H2a output: with the InterStepState
        // value absent, the same-named NonSecret key does not satisfy the guard.
        var run = BuildRun();
        run.InterStepState.KeyVaultName = null;
        run.Parameters.NonSecret["keyVaultName"] = KeyVaultName;
        var repo = new FakeRepository(run, etag: "etag-legacy-param");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.MissingKeyVaultName);
        writer.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task H2aOutputs_FlowToTheT1PatchT1ProbeAndT5Grant()
    {
        const string customSlot = "blue";
        var run = BuildRun();
        run.InterStepState.AppServiceStagingSlotName = customSlot;
        var repo = new FakeRepository(run, etag: "etag-h2a-outputs");
        var patcher = FakeIdentityPatcher.Success();
        var probe = FakeArmProbe.Match();
        var granter = FakeSlotGranter.NoSystemAssigned();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), patcher, probe, granter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        patcher.LastInput!.ResourceGroupName.Should().Be(ResourceGroupName);
        patcher.LastInput.AppServiceName.Should().Be(AppServiceName);
        patcher.LastInput.StagingSlotName.Should().Be(customSlot);
        patcher.LastInput.UserAssignedIdentityResourceId.Should().Be(UamiResourceId);
        probe.LastInput!.StagingSlotName.Should().Be(customSlot);
        probe.LastInput.ExpectedUserAssignedIdentityResourceId.Should().Be(UamiResourceId);
        granter.LastInput!.StagingSlotName.Should().Be(customSlot);
        granter.LastInput.AppServiceName.Should().Be(AppServiceName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task H2aStagingSlotBlank_FallsBackToDefaultStagingSlot(string? slot)
    {
        var run = BuildRun();
        run.InterStepState.AppServiceStagingSlotName = slot;
        var repo = new FakeRepository(run, etag: "etag-slot-default");
        var patcher = FakeIdentityPatcher.Success();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), patcher, FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        patcher.LastInput!.StagingSlotName.Should().Be("staging",
            "the app-service.bicep default slot name applies when H2a reports none");
    }

    [Fact]
    public async Task T5GrantScope_IsDerivedFromSubscriptionAndH2aOutputs()
    {
        // Task 245a: the former keyVaultResourceId run-parameter override is gone — the T5 grant
        // scope is always BuildKvResourceId(subscriptionId, InterStepState.ResourceGroupName,
        // InterStepState.KeyVaultName).
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-kv-rid");
        var granter = FakeSlotGranter.NoSystemAssigned();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(), granter);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        granter.LastInput!.VaultResourceId.Should().Be(
            H4KvSecretsPopulationHandler.BuildKvResourceId(SubscriptionId, ResourceGroupName, KeyVaultName));
    }

    private static void ClearInterStepStateValue(InterStepState state, string property)
    {
        switch (property)
        {
            case nameof(InterStepState.KeyVaultName): state.KeyVaultName = null; break;
            case nameof(InterStepState.ResourceGroupName): state.ResourceGroupName = null; break;
            case nameof(InterStepState.AppServiceName): state.AppServiceName = null; break;
            case nameof(InterStepState.MiResourceId): state.MiResourceId = null; break;
            default: throw new ArgumentOutOfRangeException(nameof(property), property, "Not an H4-read H2a output.");
        }
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

    // ---------- AC-28b leak guard vs real H2a outputs (task 245a) ----------
    // customer.bicep names for an 8-character customerId in the longest environment ('staging'):
    // the host names alone are 40+ characters of the token alphabet, which tripped the guard before.

    [Theory]
    [InlineData("https://spaarke-abcdefgh-staging-cosmos.documents.azure.com:443/")]
    [InlineData("https://sprk-abcdefgh-staging-openai.openai.azure.com/")]
    [InlineData("https://sprk-abcdefgh-staging-search.search.windows.net")]
    [InlineData("https://sprk-abcdefgh-staging-kv.vault.azure.net/")]
    [InlineData("spaarke-abcdefgh-staging-sbus.servicebus.windows.net")]
    [InlineData("/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg-spaarke-abcdefgh-staging/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-spaarke-abcdefgh-staging")]
    public void AC28b_IsCleartextSecretPattern_RealH2aOutputs_DoNotTrip(string value)
        => H4KvSecretsPopulationHandler.IsCleartextSecretPattern(value).Should().BeFalse();

    [Theory]
    [InlineData("https://sprk-acme-prod-search.search.windows.net/?api-key=Nx8Q~aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789")]
    [InlineData("https://sprk-acme-prod-kv.vault.azure.net/Nx8Q~aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789")]
    [InlineData("eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIn0")]
    public void AC28c_IsCleartextSecretPattern_SecretInsideAUriOrDotted_StillTrips(string value)
        => H4KvSecretsPopulationHandler.IsCleartextSecretPattern(value).Should().BeTrue(
            "only a lowercase DNS host is exempt — a URI's path / query and dotted tokens are still scanned");

    [Fact]
    public async Task AC28d_RealShapedH2aOutputs_OnARun_DoNotQuarantine()
    {
        var run = BuildRun();
        run.InterStepState.CosmosEndpoint = "https://spaarke-abcdefgh-staging-cosmos.documents.azure.com:443/";
        run.InterStepState.OpenAiEndpoint = "https://sprk-abcdefgh-staging-openai.openai.azure.com/";
        run.InterStepState.AiSearchEndpoint = "https://sprk-abcdefgh-staging-search.search.windows.net";
        run.InterStepState.KeyVaultUri = "https://sprk-abcdefgh-staging-kv.vault.azure.net/";
        run.InterStepState.ServiceBusFullyQualifiedNamespace = "spaarke-abcdefgh-staging-sbus.servicebus.windows.net";
        var repo = new FakeRepository(run, etag: "etag-28d");
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
    }

    [Fact]
    public async Task AC28e_LeakInAnInterStepStatePropertyAddedLater_IsStillCaught()
    {
        // The guard enumerates every string property; it used to check a hand-kept list of eleven
        // that predated the seven H2a outputs task 245a added.
        var run = BuildRun();
        run.InterStepState.KeyVaultUri = "https://sprk-acme-prod-kv.vault.azure.net/?sig=Nx8Q~aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789";
        var repo = new FakeRepository(run, etag: "etag-28e");
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(KvSecretsPopulationRejectionCodes.CleartextSecretLeak);
        failure.Diagnostic.Should().Contain(nameof(InterStepState.KeyVaultUri));
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
    // T226 — from-topology-constants (SPE-ContainerTypeId) projection
    // =========================================================================

    // The value reaches the run from operator-maintained spaarke-constants.yaml (via the
    // /provision-environment intake) — surrounding whitespace would otherwise be written
    // into the vault as part of the id.
    [Theory]
    [InlineData("ct-guid")]
    [InlineData("  ct-guid\n")]
    public void BuildTopologyConstantValues_MapsContainerTypeIdRunParameterToCanonicalName(string raw)
    {
        var values = H4KvSecretsPopulationHandler.BuildIntakeValues(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["containerTypeId"] = raw });

        values.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, string>("SPE-ContainerTypeId", "ct-guid"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildTopologyConstantValues_MissingOrBlankParameter_IsLeftOut(string? raw)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (raw is not null)
        {
            parameters["containerTypeId"] = raw;
        }

        H4KvSecretsPopulationHandler.BuildIntakeValues(parameters).Should().BeEmpty(
            "an absent value must surface as a resolver failure against the canonical name, never a blank secret");
    }

    [Fact]
    public async Task HandleAsync_RunWithContainerTypeId_PassesItToTheWriterAsTopologyConstant()
    {
        var run = BuildRun();
        run.Parameters.NonSecret["containerTypeId"] = "ct-guid";
        var repo = new FakeRepository(run, etag: "etag-t226-topology");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        writer.LastRequest!.IntakeValues.Should().Contain("SPE-ContainerTypeId", "ct-guid");
    }

    [Fact]
    public async Task HandleAsync_PassesTheIntakeTenantIdToTheWriter_ForTheTenantIdSecret()
    {
        // Task 245a: TenantId is value_source from-intake-parameter — the run's own tenantId,
        // with no RunParameters.Secrets reference (nothing ever supplied one).
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-245a-tenant");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        writer.LastRequest!.IntakeValues.Should().Contain("TenantId", TenantId);
    }

    [Fact]
    public async Task HandleAsync_SkipsEntriesWrittenByH3_TheyNeverReachTheWriter()
    {
        // Task 245a: H3 runs after H4 and commits BFF-API-ClientId / BFF-API-Audience itself.
        // H4 waiting for them was the deadlock; H4 must leave them alone.
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-245a-h3");
        var writer = FakeWriter.AllWrote();
        var entries = BuildCanonicalEntries()
            .Append(new KvSecretEntry("BFF-API-ClientId", KvSecretOperation.Upsert, KvSecretValueSource.WrittenByEntraAppReg))
            .Append(new KvSecretEntry("BFF-API-Audience", KvSecretOperation.Upsert, KvSecretValueSource.WrittenByEntraAppReg))
            .ToList();
        var handler = BuildHandler(repo, FakeManifest.Success(entries), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        writer.LastRequest!.Entries.Select(e => e.CanonicalName)
            .Should().NotContain(new[] { "BFF-API-ClientId", "BFF-API-Audience" });
        writer.LastRequest.Entries.Should().HaveCount(entries.Count - 2);
    }

    // =========================================================================
    // Row A38a (task 205a, 2026-08-25) — secret-free omit via the task-126
    // FR-39 OmitCanonicalNames seam + positive migration marker
    // =========================================================================

    // T226: ServiceBus-ConnectionString + AiSearch--AdminKey were removed from the catalog
    // for every stamp, leaving BFF-API-ClientSecret; task 225b (G21) added Dataverse-ClientSecret —
    // the BINDING rule: neither credential secret is ever created in a secret-free environment.
    private static readonly string[] A38aOmitTargets =
    {
        "BFF-API-ClientSecret",
        "Dataverse-ClientSecret",
    };

    [Fact]
    public async Task A38a1_SecretFreeTrue_UnionsBothCredentialSecretsIntoExistingOmitSeam()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a38a1");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned(),
            options: ValidOptions(requireSecretFreeIdentity: true));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        // The omit flows through the SAME KvSecretWriteRequest.OmitCanonicalNames
        // seam task 126 landed — no parallel mechanism.
        writer.LastRequest!.OmitCanonicalNames.Should().BeEquivalentTo(A38aOmitTargets,
            "task 225b: both credential secrets are omitted (never a sentinel) on secret-free environments");
    }

    [Fact]
    public async Task A38a2_DefaultOptions_AreSecretFree_OmitBothCredentialSecrets()
    {
        // The production default (nothing set — the L2 principal lives in ControlPlaneIdentityOptions since
        // task 249): task 225b / G21 made RequireSecretFreeIdentity default to true.
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a38a2");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()), writer,
            FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned(),
            options: new KvSecretsPopulationOptions());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        writer.LastRequest!.OmitCanonicalNames.Should().BeEquivalentTo(A38aOmitTargets,
            "every new stamp runs MI-FIC by default — H4 never creates either credential secret");
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
            options: ValidOptions(requireSecretFreeIdentity: true));

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        writer.LastRequest!.OmitCanonicalNames.Should().Contain("Some-Operator-Chosen-Secret");
        writer.LastRequest.OmitCanonicalNames.Should().Contain("Another-One");
        writer.LastRequest.OmitCanonicalNames.Should().Contain(A38aOmitTargets);
        writer.LastRequest.OmitCanonicalNames.Should().HaveCount(4,
            "2 operator-chosen names + the 2 A38a targets (BFF-API-ClientSecret + Dataverse-ClientSecret, task 225b)");
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
            options: ValidOptions(requireSecretFreeIdentity: true, secretFreeIdentityRollback: true));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        writer.LastRequest!.OmitCanonicalNames.Should().BeEmpty(
            "Q3 Path A rollback re-includes both A38a targets (regression path)");
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
            options: ValidOptions(requireSecretFreeIdentity: true));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        marker.CallCount.Should().Be(1);
        marker.LastRequest!.KeyVaultName.Should().Be(KeyVaultName);
        marker.LastRequest.TenantId.Should().Be(TenantId);
        marker.LastRequest.SubscriptionId.Should().Be(SubscriptionId);
        marker.LastRequest.ResourceGroupName.Should().Be(ResourceGroupName);
    }

    [Fact]
    public async Task A38a6_SecretFreeFalse_MarkerNotApplied_OmitSetStaysEmpty()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a38a6");
        var marker = FakeMarkerApplier.Success();
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
            writer, FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
            FakeSlotGranter.NoSystemAssigned(), markerApplier: marker,
            options: ValidOptions(requireSecretFreeIdentity: false));

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        marker.CallCount.Should().Be(0);
        writer.LastRequest!.OmitCanonicalNames.Should().BeEmpty(
            "the explicit legacy client-secret path (RequireSecretFreeIdentity=false) omits nothing");
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
            options: ValidOptions(requireSecretFreeIdentity: true));

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
            options: ValidOptions(requireSecretFreeIdentity: true));

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
            options: ValidOptions(requireSecretFreeIdentity: true));

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
            run.InterStepState.KeyVaultName = vaultName;
            var repo = new FakeRepository(run, etag: $"etag-a38a9-{customerId}");
            var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries()),
                FakeWriter.AllWrote(), FakeIdentityPatcher.Success(), FakeArmProbe.Match(),
                FakeSlotGranter.NoSystemAssigned(), markerApplier: marker,
                options: ValidOptions(requireSecretFreeIdentity: true));

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

    // ---------- Task 245b: computed secretsVer + L2-owned configuration ----------

    [Fact]
    public async Task ChangedManifestVersion_IsNotAnIdempotentNoOp()
    {
        var run = BuildRun();
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = H4KvSecretsPopulationHandler.HandlerIdentifier,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-9),
            IdempotencyKey = H4KvSecretsPopulationHandler.BuildIdempotencyKey(CustomerId, SecretsVer),
            JobId = RunId,
        });
        var repo = new FakeRepository(run, etag: "etag-245b-a");
        var writer = FakeWriter.AllWrote();
        var handler = BuildHandler(repo, FakeManifest.Success(BuildCanonicalEntries(), contentVersion: "edited-manifest"),
            writer, FakeIdentityPatcher.Success(), FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>().Which.IdempotencyKey
            .Should().Be(H4KvSecretsPopulationHandler.BuildIdempotencyKey(CustomerId, "edited-manifest"));
        writer.CallCount.Should().Be(1, "an edited manifest is re-applied, not skipped");
    }

    [Fact]
    public async Task KvRbacBootstrap_GrantsTheConfiguredL2Principal_NeverTheStampUami()
    {
        var run = BuildRun();
        run.InterStepState.MiObjectId = "0f0e0d0c-0b0a-0908-0706-050403020100";   // the stamp's BFF UAMI
        var repo = new FakeRepository(run, etag: "etag-245b-b");
        var writer = FakeWriter.AllWrote();
        var bootstrapper = new StubOperatorKvRbacBootstrapper(new OperatorKvRbacBootstrapOutcome.Success(WasFreshlyGranted: true));
        var handler = new H4KvSecretsPopulationHandler(
            repo, FakeManifest.Success(BuildCanonicalEntries()), writer, FakeIdentityPatcher.Success(),
            FakeArmProbe.Match(), FakeSlotGranter.NoSystemAssigned(), FakeMarkerApplier.Success(), bootstrapper,
            Options.Create(new KvSecretsPopulationOptions()),
            Options.Create(new ControlPlaneIdentityOptions { PrincipalObjectId = L2PrincipalObjectId }),
            NullLogger<H4KvSecretsPopulationHandler>.Instance);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        bootstrapper.LastRequest!.PrincipalObjectId.Should().Be(L2PrincipalObjectId);
        bootstrapper.LastRequest.PrincipalObjectId.Should().NotBe(run.InterStepState.MiObjectId);
        bootstrapper.LastRequest.RoleDefinitionId.Should().Be(KvBuiltInRoleIds.SecretsOfficer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void IdentityOptions_Validate_RejectsMissingL2Principal(string principal)
    {
        var options = new ControlPlaneIdentityOptions { PrincipalObjectId = principal };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ControlPlaneIdentity:PrincipalObjectId*");
    }

    [Fact]
    public void IdentityOptions_Validate_AcceptsAPrincipalGuid()
    {
        var options = new ControlPlaneIdentityOptions { PrincipalObjectId = L2PrincipalObjectId };

        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

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
            Options.Create(ValidOptions()),
            Options.Create(new ControlPlaneIdentityOptions { PrincipalObjectId = L2PrincipalObjectId }),
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
        // HANDLER-09 (Wave 2 pre-dispatch remediation 2026-08-27; live impl
        // Wave 2.5): default to a Success-returning IOperatorKvRbacBootstrapper
        // stub so existing tests are unaffected by the scaffold-to-live
        // transition. Non-HANDLER-09 tests exercise the OTHER seams; the
        // bootstrap step is a no-op success gate. The live-Azure path is
        // proven by ArmOperatorKvRbacBootstrapperTests.cs (fake-transport
        // ArmClient) and by the H4 HANDLER-09 tests here that inject
        // an explicit StubOperatorKvRbacBootstrapper.
        return new H4KvSecretsPopulationHandler(
            repo, manifest, writer, patcher, probe, granter,
            markerApplier ?? FakeMarkerApplier.Success(),
            new StubOperatorKvRbacBootstrapper(new OperatorKvRbacBootstrapOutcome.Success(WasFreshlyGranted: false)),
            Options.Create(options ?? ValidOptions()),
            Options.Create(new ControlPlaneIdentityOptions { PrincipalObjectId = L2PrincipalObjectId }),
            NullLogger<H4KvSecretsPopulationHandler>.Instance);
    }

    /// <summary>
    /// Options in the shape Worker startup guarantees (KvSecretsPopulationOptions.Validate() runs under
    /// ValidateOnStart, task 245b) — the handler relies on it and does not re-validate.
    /// <paramref name="requireSecretFreeIdentity"/> defaults to the LEGACY client-secret path (false),
    /// set explicitly: the production default became true in task 225b (G21), and the tests that build
    /// on this helper exercise the write path for every canonical entry; the A38a tests opt in.
    /// </summary>
    private static KvSecretsPopulationOptions ValidOptions(
        bool requireSecretFreeIdentity = false, bool secretFreeIdentityRollback = false) => new()
    {
        RequireSecretFreeIdentity = requireSecretFreeIdentity,
        SecretFreeIdentityRollback = secretFreeIdentityRollback,
    };

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
            TenancyModel = "Model2",
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model2",
        };
        // Intake values (run parameters).
        run.Parameters.NonSecret[H4KvSecretsPopulationHandler.TenantIdParameterKey] = TenantId;
        run.Parameters.NonSecret[H4KvSecretsPopulationHandler.SubscriptionIdParameterKey] = SubscriptionId;
        // H2a outputs (task 245a, G25) — InterStepState, never run parameters.
        run.InterStepState.KeyVaultName = KeyVaultName;
        run.InterStepState.ResourceGroupName = ResourceGroupName;
        run.InterStepState.AppServiceName = AppServiceName;
        run.InterStepState.AppServiceStagingSlotName = StagingSlotName;
        run.InterStepState.MiResourceId = UamiResourceId;
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
        public static FakeManifest Success(IReadOnlyList<KvSecretEntry> entries, string contentVersion = SecretsVer)
            => new(new KvSecretManifestReadResult.Success(entries, contentVersion));
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
