// -----------------------------------------------------------------------------
// H4SharedKvSecretsPopulationHandlerTests.cs
//
// Task 200 — unit tests over H4SharedKvSecretsPopulationHandler. xunit +
// FluentAssertions + hand-rolled fakes for every seam, following the H4
// (task 047) test exemplar's shape.
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live KV / ARM / Azure API. Fakes replace
//   the repository + all FOUR collaborator seams (manifest, accessor,
//   extractor, probe). Live-Azure coverage belongs in env-guarded smoke
//   tests (out of scope for CI here).
//
// COVERAGE (POML acceptance criteria mapping):
//   AC-1  6-source-type happy path — mocked extractor returns known values
//         for all 6 F19 entries; handler writes all 6 to KV; probe verifies;
//         returns Success + advances Cosmos state.
//   AC-2  Source drift rotation — KV has "old", extractor returns "new" → 1
//         WriteAsync call + audit-log entry.
//   AC-3  Existing-and-matching NO-OP — KV value == extractor value → 0
//         WriteAsync calls.
//   AC-4  Source-service unreachable Quarantine — mocked extractor throws
//         RequestFailedException(403) → Failure(QuarantineRequired,
//         "SourceServiceExtractionFailed", diagnostic naming the service).
//   AC-5  BINDING guard — synthetic manifest with Dataverse-ClientSecret as
//         from-shared-service → Failure(QuarantineRequired,
//         "BindingPreCheckViolation") BEFORE any extractor/writer call.
//   AC-6  Post-condition probe failure — probe returns Mismatch → Failure
//         (QuarantineRequired, "SharedSecretRefUnresolvable").
//   AC-7  Idempotency-key match — two runs same (env, secretsVer) → second
//         short-circuits Success no-op with 0 external calls.
//   AC-8  Cleartext-secret leak scan — assert 0 substrings of test-extractor
//         return values appear in any captured log line.
//   AC-9  Manifest v1 backwards compat — entries WITHOUT
//         value_source==FromSharedService are skipped, no error, no warning.
//   AC-10 Malformed service_ref → Failure(QuarantineRequired,
//         "InvalidServiceRef").
//   AC-11 Missing parameter guards — theory covering all 6 required params.
//   AC-12 HandlerId mismatch throws.
//   AC-13 Idempotency-key format determinism.
//   AC-14 Run not found → Resumable + RunNotFound.
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Azure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
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

public sealed class H4SharedKvSecretsPopulationHandlerTests
{
    private const string CustomerId = "shared-env";
    private const string RunId = "01j7q3zp-h4shared-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "sub-shared-prod";
    private const string SharedKeyVaultName = "sprk-prod-kv";
    private const string SourceResourceGroupName = "rg-spaarke-shared-prod";
    private const string EnvironmentName = "prod";
    private const string SecretsVer = "manifest-hash-abc123";
    private const string AppServiceResourceGroupName = "rg-spaarke-shared-prod";
    private const string AppServiceName = "sprk-prod-api";
    private const string StagingSlotName = "staging";
    private const string UamiResourceId =
        "/subscriptions/sub-shared-prod/resourceGroups/rg-spaarke-shared-prod/providers/Microsoft.ManagedIdentity/userAssignedIdentities/sprk-prod-uami";

    // Six F19 canonical secrets (matches Phase A manifest.yaml entries).
    private static readonly (string Canonical, string ServiceRef)[] SixF19Entries =
    {
        ("AiSearch--AdminKey",           "search:sprksharedprod-search"),
        ("AzureOpenAI-ApiKey",           "cognitiveservices:sprksharedprod-openai"),
        ("DocumentIntelligence-ApiKey",  "cognitiveservices:sprksharedprod-docintel"),
        ("Redis-ConnectionString",       "redis:sprksharedprod-redis"),
        ("ServiceBus-ConnectionString",  "servicebus:sprksharedprod-servicebus"),
        ("Storage-ConnectionString",     "storage:sprksharedprodsa"),
    };

    // ---------- AC-1 happy path (6-source-type) ----------

    [Fact]
    public async Task AC1_SixSourceTypeHappyPath_WritesAllAndReturnsSuccess()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-1");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();  // NotFound for all → initial write for all
        var probe = FakeArmProbe.Match();
        var extractor = FakeExtractor.Static(new Dictionary<string, string>
        {
            ["search:sprksharedprod-search"] = "extracted-search-primary-key",
            ["cognitiveservices:sprksharedprod-openai"] = "extracted-openai-key1",
            ["cognitiveservices:sprksharedprod-docintel"] = "extracted-docintel-key1",
            ["redis:sprksharedprod-redis"] = "sprksharedprod-redis.redis.cache.windows.net:6380,password=REDIS-PRIMARY,ssl=True,abortConnect=False",
            ["servicebus:sprksharedprod-servicebus"] = "Endpoint=sb://sprksharedprod-servicebus.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SB-KEY",
            ["storage:sprksharedprodsa"] = "DefaultEndpointsProtocol=https;AccountName=sprksharedprodsa;AccountKey=STORAGE-KEY;EndpointSuffix=core.windows.net",
        });
        var handler = BuildHandler(repo, manifest, accessor, probe, extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(
            H4SharedKvSecretsPopulationHandler.BuildIdempotencyKey(EnvironmentName, SecretsVer));

        // All 6 extracted + all 6 written.
        extractor.CallCount.Should().Be(6);
        accessor.WriteCount.Should().Be(6);
        accessor.WrittenSecrets.Keys.Should().BeEquivalentTo(SixF19Entries.Select(e => e.Canonical));
        probe.CallCount.Should().Be(1);

        repo.LastWrittenRun!.CurrentPhase.Should().Be(HandlerIds.H4Shared);
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle()
            .Which.Phase.Should().Be(HandlerIds.H4Shared);
    }

    // ---------- AC-2 source drift rotation ----------

    [Fact]
    public async Task AC2_SourceDriftRotation_WritesOnlyDriftedEntry_AndAuditLogsHashes()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-2");
        var manifest = FakeManifest.Success(BuildSharedEntries());

        // Pre-populate accessor with 6 values matching the extractor for 5,
        // drift for 1 (AiSearch--AdminKey has old value, extractor returns new).
        var accessor = new FakeAccessor();
        accessor.SeedExisting("AiSearch--AdminKey", "OLD-search-key");
        accessor.SeedExisting("AzureOpenAI-ApiKey", "matching-openai-key");
        accessor.SeedExisting("DocumentIntelligence-ApiKey", "matching-docintel-key");
        accessor.SeedExisting("Redis-ConnectionString", "matching-redis");
        accessor.SeedExisting("ServiceBus-ConnectionString", "matching-servicebus");
        accessor.SeedExisting("Storage-ConnectionString", "matching-storage");

        var extractor = FakeExtractor.Static(new Dictionary<string, string>
        {
            ["search:sprksharedprod-search"] = "NEW-search-key",  // drift
            ["cognitiveservices:sprksharedprod-openai"] = "matching-openai-key",
            ["cognitiveservices:sprksharedprod-docintel"] = "matching-docintel-key",
            ["redis:sprksharedprod-redis"] = "matching-redis",
            ["servicebus:sprksharedprod-servicebus"] = "matching-servicebus",
            ["storage:sprksharedprodsa"] = "matching-storage",
        });
        var (handler, capturedLogs) = BuildHandlerWithLogCapture(repo, manifest, accessor, FakeArmProbe.Match(), extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        extractor.CallCount.Should().Be(6, "every entry is extracted for drift-check");
        accessor.WriteCount.Should().Be(1, "only the drifted entry is written");
        accessor.WrittenSecrets.Keys.Should().Contain("AiSearch--AdminKey");

        // Audit log carries BOTH old + new hashes for the rotation event.
        var oldHash = H4SharedKvSecretsPopulationHandler.HashForAudit("OLD-search-key");
        var newHash = H4SharedKvSecretsPopulationHandler.HashForAudit("NEW-search-key");
        var rotateLine = capturedLogs.Should()
            .Contain(l => l.Contains("drift-rotated") && l.Contains("AiSearch--AdminKey"))
            .Which;
        rotateLine.Should().Contain(oldHash);
        rotateLine.Should().Contain(newHash);
    }

    // ---------- AC-3 existing-and-matching NO-OP ----------

    [Fact]
    public async Task AC3_ExistingAndMatching_NoWriteCallsMade()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-3");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var canned = new Dictionary<string, string>();
        foreach (var (canonical, serviceRef) in SixF19Entries)
        {
            var v = $"matching-value-for-{canonical}";
            canned[serviceRef] = v;
            accessor.SeedExisting(canonical, v);
        }
        var extractor = FakeExtractor.Static(canned);
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        accessor.WriteCount.Should().Be(0, "all 6 matched — no writes");
        extractor.CallCount.Should().Be(6);
    }

    // ---------- AC-4 source-service unreachable Quarantine ----------

    [Fact]
    public async Task AC4_SourceServiceUnreachable_ReturnsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-4");
        var manifest = FakeManifest.Success(BuildSharedEntries().Take(1).ToList());  // Just one entry to simplify
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Throwing(new RequestFailedException(
            status: 403, message: "Forbidden — no RBAC on source service"));
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SharedKvSecretsPopulationRejectionCodes.SourceServiceExtractionFailed);
        failure.Diagnostic.Should().Contain("AiSearch--AdminKey");
        failure.Diagnostic.Should().Contain("search:sprksharedprod-search");
        failure.Diagnostic.Should().Contain("HTTP 403");
        accessor.WriteCount.Should().Be(0);
    }

    // ---------- AC-5 BINDING guard ----------

    [Fact]
    public async Task AC5_BindingPreCheck_FailsQuarantineBeforeExtractorOrWriter()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-5");
        // Synthetic evil manifest listing Dataverse-ClientSecret as from-shared-service.
        var evilManifest = FakeManifest.Success(new List<KvSecretEntry>
        {
            new("Dataverse-ClientSecret", KvSecretOperation.Upsert, KvSecretValueSource.FromSharedService, "search:evil"),
            new("AiSearch--AdminKey", KvSecretOperation.Upsert, KvSecretValueSource.FromSharedService, "search:sprksharedprod-search"),
        });
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(new Dictionary<string, string>());
        var handler = BuildHandler(repo, evilManifest, accessor, FakeArmProbe.Match(), extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SharedKvSecretsPopulationRejectionCodes.BindingPreCheckViolation);
        failure.Diagnostic.Should().Contain("Dataverse-ClientSecret");
        failure.Diagnostic.Should().Contain("MUST");

        extractor.CallCount.Should().Be(0, "BINDING pre-check fires BEFORE extractor");
        accessor.WriteCount.Should().Be(0);
    }

    // ---------- AC-6 post-condition probe failure ----------

    [Fact]
    public async Task AC6_PostConditionProbeMismatch_ReturnsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-6");
        var manifest = FakeManifest.Success(BuildSharedEntries().Take(1).ToList());
        var accessor = new FakeAccessor();
        var probe = FakeArmProbe.Mismatch(observedProd: null, observedStaging: "wrong-uami-rid");
        var extractor = FakeExtractor.Static(new Dictionary<string, string>
        {
            ["search:sprksharedprod-search"] = "some-key",
        });
        var handler = BuildHandler(repo, manifest, accessor, probe, extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SharedKvSecretsPopulationRejectionCodes.SharedSecretRefUnresolvable);
        failure.Diagnostic.Should().Contain("wrong-uami-rid");
    }

    // ---------- AC-7 idempotency-key match ----------

    [Fact]
    public async Task AC7_IdempotencyKeyMatch_ShortCircuitsSuccess_NoExternalCalls()
    {
        var run = BuildRun();
        var expectedKey = H4SharedKvSecretsPopulationHandler.BuildIdempotencyKey(EnvironmentName, SecretsVer);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIds.H4Shared,
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, "etag-7");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(new Dictionary<string, string>());
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(expectedKey);

        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        manifest.CallCount.Should().Be(0);
        extractor.CallCount.Should().Be(0);
        accessor.WriteCount.Should().Be(0);
    }

    // ---------- AC-8 cleartext-secret leak scan ----------

    [Fact]
    public async Task AC8_CleartextSecretLeakScan_ZeroValueSubstringsInLogOutput()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-8");
        // Values with distinctive prefix so grep is unambiguous.
        var canned = SixF19Entries.ToDictionary(
            e => e.ServiceRef,
            e => $"SECRETXX-{e.Canonical}-DO-NOT-LEAK");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(canned);
        var (handler, capturedLogs) = BuildHandlerWithLogCapture(repo, manifest, accessor, FakeArmProbe.Match(), extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        foreach (var value in canned.Values)
        {
            capturedLogs.Should().NotContain(l => l.Contains(value),
                $"cleartext value '{value}' MUST NEVER appear in log output (ADR-028)");
        }
    }

    // ---------- AC-9 manifest v1 backwards compat ----------

    [Fact]
    public async Task AC9_ManifestV1BackwardsCompat_NonSharedEntriesAreSkipped()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-9");
        // Mixed manifest — some shared, some non-shared. Non-shared MUST be skipped.
        var mixed = new List<KvSecretEntry>
        {
            new("Dataverse-ClientSecret", KvSecretOperation.Upsert, KvSecretValueSource.FromExistingKvSecret),
            new("BFF-API-ClientSecret", KvSecretOperation.Upsert, KvSecretValueSource.FromExistingKvSecret),
            new("Communication-Webhook-SigningKey", KvSecretOperation.Upsert, KvSecretValueSource.Generated),
            new("AiSearch--AdminKey", KvSecretOperation.Upsert, KvSecretValueSource.FromSharedService, "search:sprksharedprod-search"),
        };
        var manifest = FakeManifest.Success(mixed);
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(new Dictionary<string, string>
        {
            ["search:sprksharedprod-search"] = "search-primary-key",
        });
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        extractor.CallCount.Should().Be(1, "only the ONE FromSharedService entry was extracted");
        accessor.WriteCount.Should().Be(1);
        accessor.WrittenSecrets.Keys.Should().BeEquivalentTo(new[] { "AiSearch--AdminKey" });
    }

    // ---------- AC-10 malformed service_ref ----------

    [Fact]
    public async Task AC10_MalformedServiceRef_FailsQuarantineWithInvalidServiceRef()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-10");
        var badManifest = FakeManifest.Success(new List<KvSecretEntry>
        {
            new("AiSearch--AdminKey", KvSecretOperation.Upsert, KvSecretValueSource.FromSharedService, "not-a-valid-ref"),
        });
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(new Dictionary<string, string>());
        var handler = BuildHandler(repo, badManifest, accessor, FakeArmProbe.Match(), extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(SharedKvSecretsPopulationRejectionCodes.InvalidServiceRef);
        failure.Diagnostic.Should().Contain("not-a-valid-ref");
        extractor.CallCount.Should().Be(0);
    }

    // ---------- AC-11 missing parameter guards ----------

    [Theory]
    [InlineData(H4SharedKvSecretsPopulationHandler.TenantIdParameterKey,
                SharedKvSecretsPopulationRejectionCodes.MissingTenantId)]
    [InlineData(H4SharedKvSecretsPopulationHandler.SubscriptionIdParameterKey,
                SharedKvSecretsPopulationRejectionCodes.MissingSubscriptionId)]
    [InlineData(H4SharedKvSecretsPopulationHandler.SharedKeyVaultNameParameterKey,
                SharedKvSecretsPopulationRejectionCodes.MissingSharedKeyVaultName)]
    [InlineData(H4SharedKvSecretsPopulationHandler.SourceResourceGroupNameParameterKey,
                SharedKvSecretsPopulationRejectionCodes.MissingSourceResourceGroupName)]
    [InlineData(H4SharedKvSecretsPopulationHandler.EnvironmentNameParameterKey,
                SharedKvSecretsPopulationRejectionCodes.MissingEnvironmentName)]
    [InlineData(H4SharedKvSecretsPopulationHandler.SecretsVersionParameterKey,
                SharedKvSecretsPopulationRejectionCodes.MissingSecretsVersion)]
    public async Task AC11_MissingRequiredParameter_FailsResumable_NoExternalCalls(
        string parameterKey, string expectedRejectionCode)
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(parameterKey);
        var repo = new FakeRepository(run, "etag-guard");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(new Dictionary<string, string>());
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(expectedRejectionCode);
        extractor.CallCount.Should().Be(0);
        accessor.WriteCount.Should().Be(0);
    }

    // ---------- AC-12 handler-id mismatch ----------

    [Fact]
    public async Task AC12_HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-12");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(new Dictionary<string, string>());
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor);

        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H4",  // wrong id — H4-shared expects HandlerIds.H4Shared
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- AC-13 idempotency-key format determinism ----------

    [Fact]
    public void AC13_IdempotencyKey_IsDeterministicByEnvironmentAndSecretsVer()
    {
        var k1 = H4SharedKvSecretsPopulationHandler.BuildIdempotencyKey("prod", "hash-v1");
        var k2 = H4SharedKvSecretsPopulationHandler.BuildIdempotencyKey("prod", "hash-v1");
        k1.Should().Be(k2);
        k1.Should().Be("kv-shared-prod-hash-v1");
        H4SharedKvSecretsPopulationHandler.BuildIdempotencyKey("prod", "hash-v2").Should().NotBe(k1);
        H4SharedKvSecretsPopulationHandler.BuildIdempotencyKey("dev", "hash-v1").Should().NotBe(k1);
    }

    // ---------- AC-14 run not found ----------

    [Fact]
    public async Task AC14_RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(new Dictionary<string, string>());
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SharedKvSecretsPopulationRejectionCodes.RunNotFound);
    }

    // =========================================================================
    // Row A38a (task 205a, 2026-08-25) — from-shared-service omit + marker
    // =========================================================================

    [Fact]
    public async Task A38aS1_SecretFreeTrue_SbConnAndAdminKey_NeitherExtractedNorWritten_OthersStillWritten()
    {
        // The from-shared-service half of the A38a omit: SB-conn + admin-key
        // travel through H4-shared per manifest.yaml (:255/:433). With a
        // manifest that (like an emergency StaticKvSecretManifest revert)
        // still serves them, the handler-level omit MUST hold on its own.
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-a38as1");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(new Dictionary<string, string>
        {
            ["cognitiveservices:sprksharedprod-openai"] = "extracted-openai-key1",
            ["cognitiveservices:sprksharedprod-docintel"] = "extracted-docintel-key1",
            ["redis:sprksharedprod-redis"] = "extracted-redis-conn",
            ["storage:sprksharedprodsa"] = "extracted-storage-conn",
            // Deliberately NO canned values for search / servicebus — an
            // extraction attempt for either would throw in FakeExtractor,
            // proving the omit short-circuits BEFORE extraction.
        });
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor,
            options: new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        extractor.CallCount.Should().Be(4, "the two omitted entries are never extracted");
        accessor.WrittenSecrets.Keys.Should().BeEquivalentTo(new[]
        {
            "AzureOpenAI-ApiKey", "DocumentIntelligence-ApiKey",
            "Redis-ConnectionString", "Storage-ConnectionString",
        });
        accessor.WrittenSecrets.Keys.Should().NotContain("ServiceBus-ConnectionString");
        accessor.WrittenSecrets.Keys.Should().NotContain("AiSearch--AdminKey");
    }

    [Fact]
    public async Task A38aS2_Q3PathARollback_AllSixWrittenAgain()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-a38as2");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(SixF19Entries.ToDictionary(
            e => e.ServiceRef, e => $"value-{e.Canonical}"));
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor,
            options: new KvSecretsPopulationOptions
            {
                RequireSecretFreeIdentity = true,
                SecretFreeIdentityRollback = true,
            });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        accessor.WriteCount.Should().Be(6, "Q3 Path A rollback re-includes the omitted entries");
    }

    [Fact]
    public async Task A38aS3_OperatorFicOmitParameter_HonoredForSharedEntries()
    {
        // FR-39 parity with H4: the SAME ficOmitSecretNames run parameter
        // drives the shared flow (task 125 "no special-casing").
        var run = BuildRun();
        run.Parameters.NonSecret[H4SharedKvSecretsPopulationHandler.FicOmitSecretNamesParameterKey] =
            "Redis-ConnectionString";
        var repo = new FakeRepository(run, "etag-a38as3");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(SixF19Entries
            .Where(e => e.Canonical != "Redis-ConnectionString")
            .ToDictionary(e => e.ServiceRef, e => $"value-{e.Canonical}"));
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        extractor.CallCount.Should().Be(5);
        accessor.WrittenSecrets.Keys.Should().NotContain("Redis-ConnectionString");
    }

    [Fact]
    public async Task A38aS4_SecretFreeTrue_MarkerAppliedToSharedVault()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-a38as4");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(SixF19Entries
            .Where(e => e.Canonical is not ("ServiceBus-ConnectionString" or "AiSearch--AdminKey"))
            .ToDictionary(e => e.ServiceRef, e => $"value-{e.Canonical}"));
        var marker = FakeMarkerApplier.Success();
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor,
            markerApplier: marker,
            options: new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        marker.CallCount.Should().Be(1);
        marker.LastRequest!.KeyVaultName.Should().Be(SharedKeyVaultName);
        marker.LastRequest.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public async Task A38aS5_MarkerFailure_FailsResumable_WithSharedMarkerRejectionCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-a38as5");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(SixF19Entries
            .Where(e => e.Canonical is not ("ServiceBus-ConnectionString" or "AiSearch--AdminKey"))
            .ToDictionary(e => e.ServiceRef, e => $"value-{e.Canonical}"));
        var marker = FakeMarkerApplier.Failure("vault tag read/apply failed (HTTP 403)");
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor,
            markerApplier: marker,
            options: new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SharedKvSecretsPopulationRejectionCodes.SecretFreeMarkerApplyFailed);
    }

    [Fact]
    public async Task A38aS6_SecretFreeFalse_MarkerNotApplied_AllSixWritten_NoRegression()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, "etag-a38as6");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var extractor = FakeExtractor.Static(SixF19Entries.ToDictionary(
            e => e.ServiceRef, e => $"value-{e.Canonical}"));
        var marker = FakeMarkerApplier.Success();
        var handler = BuildHandler(repo, manifest, accessor, FakeArmProbe.Match(), extractor,
            markerApplier: marker);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        marker.CallCount.Should().Be(0);
        accessor.WriteCount.Should().Be(6);
    }

    // ---------- HANDLER-09 operator KV RBAC bootstrap (Wave 2 pre-dispatch remediation 2026-08-27; live impl Wave 2.5) ----------

    [Fact]
    public async Task Handler09_SharedOperatorKvRbacBootstrap_Failure_FailsResumable_NoAccessorWriteCall()
    {
        // Parity with the H4 HANDLER-09 test — H4-shared MUST fail-fast when
        // the operator KV RBAC bootstrap fails on the SHARED vault, and the
        // per-entry pipeline (extractor / accessor.Read / accessor.Write) MUST
        // NOT fire. F18 verbatim: shared KV alongside per-tenant KV both need
        // bootstrap; a bootstrap failure on the shared vault means every
        // subsequent SecretClient.SetSecretAsync on it will 403.
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-h09-shared");
        var manifest = FakeManifest.Success(BuildSharedEntries());
        var accessor = new FakeAccessor();
        var probe = FakeArmProbe.Match();
        var extractor = FakeExtractor.Static(new Dictionary<string, string>());
        var failingBootstrapper = new StubSharedOperatorKvRbacBootstrapper(
            new OperatorKvRbacBootstrapOutcome.Failure(
                "Insufficient permission — could not PUT role assignment on shared vault."));

        var handler = new H4SharedKvSecretsPopulationHandler(
            repo, manifest, accessor, probe, extractor,
            FakeMarkerApplier.Success(),
            failingBootstrapper,
            Options.Create(new KvSecretsPopulationOptions()),
            NullLogger<H4SharedKvSecretsPopulationHandler>.Instance);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SharedKvSecretsPopulationRejectionCodes.OperatorKvRbacBootstrapFailed);
        failure.Diagnostic.Should().Contain("Insufficient permission");
        extractor.CallCount.Should().Be(0, "extractor MUST NOT fire when the shared-vault bootstrap fails");
        accessor.WriteCount.Should().Be(0, "shared-KV writer MUST NOT fire when the shared-vault bootstrap fails");
        failingBootstrapper.CallCount.Should().Be(1);
        failingBootstrapper.LastRequest!.RoleDefinitionId.Should().Be(KvBuiltInRoleIds.SecretsOfficer);
        failingBootstrapper.LastRequest.KeyVaultName.Should().Be(SharedKeyVaultName,
            "bootstrap request must target the SHARED KV name, not the per-tenant KV");
    }

    // ---------- helpers ----------

    private static H4SharedKvSecretsPopulationHandler BuildHandler(
        IProvisioningRunRepository repo,
        IKvSecretManifest manifest,
        ISharedKvSecretAccessor accessor,
        IArmKeyVaultRefProbe probe,
        ISourceServiceKeyExtractor extractor,
        FakeMarkerApplier? markerApplier = null,
        KvSecretsPopulationOptions? options = null)
    {
        // HANDLER-09 (Wave 2 pre-dispatch remediation 2026-08-27; live impl
        // Wave 2.5): default to a Success-returning IOperatorKvRbacBootstrapper
        // stub so existing tests are unaffected by the scaffold-to-live
        // transition. The live-Azure path is proven by
        // ArmOperatorKvRbacBootstrapperTests.cs (fake-transport ArmClient).
        return new H4SharedKvSecretsPopulationHandler(
            repo, manifest, accessor, probe, extractor,
            markerApplier ?? FakeMarkerApplier.Success(),
            new StubSharedOperatorKvRbacBootstrapper(new OperatorKvRbacBootstrapOutcome.Success(WasFreshlyGranted: false)),
            Options.Create(options ?? new KvSecretsPopulationOptions()),
            NullLogger<H4SharedKvSecretsPopulationHandler>.Instance);
    }

    private static (H4SharedKvSecretsPopulationHandler handler, List<string> logs) BuildHandlerWithLogCapture(
        IProvisioningRunRepository repo,
        IKvSecretManifest manifest,
        ISharedKvSecretAccessor accessor,
        IArmKeyVaultRefProbe probe,
        ISourceServiceKeyExtractor extractor)
    {
        var logs = new List<string>();
        var logger = new CapturingLogger<H4SharedKvSecretsPopulationHandler>(logs);
        var h = new H4SharedKvSecretsPopulationHandler(
            repo, manifest, accessor, probe, extractor,
            FakeMarkerApplier.Success(),
            new StubSharedOperatorKvRbacBootstrapper(new OperatorKvRbacBootstrapOutcome.Success(WasFreshlyGranted: false)),
            Options.Create(new KvSecretsPopulationOptions()),
            logger);
        return (h, logs);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = HandlerIds.H4Shared,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun()
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-guid",
            TenancyModel = "Model1SharedTrial",
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model1",
        };
        run.Parameters.NonSecret[H4SharedKvSecretsPopulationHandler.TenantIdParameterKey] = TenantId;
        run.Parameters.NonSecret[H4SharedKvSecretsPopulationHandler.SubscriptionIdParameterKey] = SubscriptionId;
        run.Parameters.NonSecret[H4SharedKvSecretsPopulationHandler.SharedKeyVaultNameParameterKey] = SharedKeyVaultName;
        run.Parameters.NonSecret[H4SharedKvSecretsPopulationHandler.SourceResourceGroupNameParameterKey] = SourceResourceGroupName;
        run.Parameters.NonSecret[H4SharedKvSecretsPopulationHandler.EnvironmentNameParameterKey] = EnvironmentName;
        run.Parameters.NonSecret[H4SharedKvSecretsPopulationHandler.SecretsVersionParameterKey] = SecretsVer;
        run.Parameters.NonSecret[H4SharedKvSecretsPopulationHandler.AppServiceResourceGroupNameParameterKey] = AppServiceResourceGroupName;
        run.Parameters.NonSecret[H4SharedKvSecretsPopulationHandler.AppServiceNameParameterKey] = AppServiceName;
        run.Parameters.NonSecret[H4SharedKvSecretsPopulationHandler.UamiResourceIdParameterKey] = UamiResourceId;
        return run;
    }

    private static IReadOnlyList<KvSecretEntry> BuildSharedEntries() =>
        SixF19Entries.Select(e => new KvSecretEntry(
            e.Canonical, KvSecretOperation.Upsert, KvSecretValueSource.FromSharedService, e.ServiceRef))
            .ToList();

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

    private sealed class FakeAccessor : ISharedKvSecretAccessor
    {
        private readonly ConcurrentDictionary<string, string> _store = new(StringComparer.Ordinal);
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public IDictionary<string, string> WrittenSecrets { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public void SeedExisting(string secretName, string value) => _store[secretName] = value;

        public Task<SharedKvSecretReadResult> ReadAsync(string vaultName, string secretName, CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult<SharedKvSecretReadResult>(
                _store.TryGetValue(secretName, out var v)
                    ? new SharedKvSecretReadResult.Success(v)
                    : new SharedKvSecretReadResult.NotFound());
        }

        public Task<SharedKvSecretWriteResult> WriteAsync(string vaultName, string secretName, string value, CancellationToken ct)
        {
            WriteCount++;
            WrittenSecrets[secretName] = value;
            _store[secretName] = value;
            return Task.FromResult<SharedKvSecretWriteResult>(new SharedKvSecretWriteResult.Success());
        }
    }

    private sealed class FakeExtractor : ISourceServiceKeyExtractor
    {
        private readonly IReadOnlyDictionary<string, string>? _canned;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }

        private FakeExtractor(IReadOnlyDictionary<string, string>? canned, Exception? throwOnCall)
        {
            _canned = canned;
            _throwOnCall = throwOnCall;
        }

        public static FakeExtractor Static(IReadOnlyDictionary<string, string> canned)
            => new(canned, null);

        public static FakeExtractor Throwing(Exception ex) => new(null, ex);

        public Task<string> ExtractAsync(SharedKvSecretSource source, string subscriptionId, string resourceGroupName, CancellationToken ct)
        {
            CallCount++;
            if (_throwOnCall is not null) throw _throwOnCall;
            if (_canned is not null && _canned.TryGetValue(source.RawServiceRef, out var v))
            {
                return Task.FromResult(v);
            }
            throw new InvalidOperationException(
                $"FakeExtractor has no canned value for service_ref '{source.RawServiceRef}'.");
        }
    }

    private sealed class FakeArmProbe : IArmKeyVaultRefProbe
    {
        private readonly ArmKeyVaultRefProbeResult _result;
        public int CallCount { get; private set; }
        private FakeArmProbe(ArmKeyVaultRefProbeResult result) => _result = result;
        public static FakeArmProbe Match() => new(new ArmKeyVaultRefProbeResult.Match());
        public static FakeArmProbe Mismatch(string? observedProd, string? observedStaging)
            => new(new ArmKeyVaultRefProbeResult.Mismatch(observedProd, observedStaging));
        public Task<ArmKeyVaultRefProbeResult> VerifyKeyVaultReferenceIdentityAsync(
            ArmKeyVaultRefProbeInput input, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    /// <summary>Row A38a — stub ISecretFreeMarkerApplier (per-file copy, matching this file's private-fakes convention).</summary>
    private sealed class FakeMarkerApplier : ISecretFreeMarkerApplier
    {
        private readonly SecretFreeMarkerApplyOutcome _outcome;
        public int CallCount { get; private set; }
        public SecretFreeMarkerApplyRequest? LastRequest { get; private set; }

        private FakeMarkerApplier(SecretFreeMarkerApplyOutcome outcome) => _outcome = outcome;

        public static FakeMarkerApplier Success()
            => new(new SecretFreeMarkerApplyOutcome.Applied(VaultTagWasAlreadyPresent: false));

        public static FakeMarkerApplier Failure(string diagnostic)
            => new(new SecretFreeMarkerApplyOutcome.Failure(diagnostic));

        public Task<SecretFreeMarkerApplyOutcome> ApplyAsync(
            SecretFreeMarkerApplyRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }

    /// <summary>
    /// HANDLER-09 (Wave 2 pre-dispatch remediation 2026-08-27; live impl Wave
    /// 2.5) — stub <see cref="IOperatorKvRbacBootstrapper"/> for the shared
    /// handler tests (per-file copy, matching this file's private-fakes
    /// convention). The Wave-2 scaffold-default previously constructed a real
    /// <see cref="ArmOperatorKvRbacBootstrapper"/> with just an ILogger; the
    /// live impl now requires an ArmClient (parity with sibling H4 collaborators
    /// task 121/123/125), so BuildHandler / BuildHandlerWithLogCapture pass
    /// this stub with Success(WasFreshlyGranted=false) instead. The live-Azure
    /// path is covered by <c>ArmOperatorKvRbacBootstrapperTests.cs</c>.
    /// </summary>
    private sealed class StubSharedOperatorKvRbacBootstrapper : IOperatorKvRbacBootstrapper
    {
        private readonly OperatorKvRbacBootstrapOutcome _outcome;
        public int CallCount { get; private set; }
        public OperatorKvRbacBootstrapRequest? LastRequest { get; private set; }
        public StubSharedOperatorKvRbacBootstrapper(OperatorKvRbacBootstrapOutcome outcome) => _outcome = outcome;
        public Task<OperatorKvRbacBootstrapOutcome> EnsureGrantedAsync(
            OperatorKvRbacBootstrapRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }

    /// <summary>Captures every formatted log message to a shared List so tests can grep for secret substrings + audit-log content.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _sink;
        public CapturingLogger(List<string> sink) => _sink = sink;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _sink.Add(formatter(state, exception));
        }
    }
}
