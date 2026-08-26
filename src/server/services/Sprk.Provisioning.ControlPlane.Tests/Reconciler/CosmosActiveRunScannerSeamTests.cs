// -----------------------------------------------------------------------------
// CosmosActiveRunScannerSeamTests.cs
//
// L2 CONTROL-PLANE repository -> scanner integration seam test (task 106 /
// DS-5 §C4.5). THIS is the test that would have caught the original defect:
// a fully in-process unit test over CosmosActiveRunScanner's SQL text cannot
// observe how the Cosmos SDK's DEFAULT (Newtonsoft-based) serializer
// actually renders the `status` field on the wire — only a real write
// through IProvisioningRunRepository (via CosmosClient) followed by a real
// scan through CosmosActiveRunScanner (via the SAME CosmosClient) proves the
// two are talking the same serialized language.
//
// Pattern-matches CosmosSmokeTests.cs (task 037) — SAME env-guard variable
// (COSMOS_L2_SMOKE_ENDPOINT) and construction so both smoke suites share one
// dev-Cosmos opt-in; skipped by default so the CI unit run never depends on
// a live Cosmos endpoint.
//
// ADR-038 KEEP category: this project's INTEGRATION path (real Cosmos
// account, no mocks) — same categorization CosmosSmokeTests.cs documents
// as "ADR-038 §2 path #3 (external-integration boundary)" / "equivalent
// project-owned integration path" to `tests/integration/**`. NOTE:
// `tests/integration/seam/**` at the repo root is the ADR-043 AI-execution-
// spine vertical-slice category (dispatch -> ContextBinder -> ActionRunner
// -> OutputRouter -> SessionOutput), compiled into
// `tests/unit/Sprk.Bff.Api.Tests` — a different KEEP category for a
// different subsystem (BFF AI dispatch, not L2 Cosmos persistence). This
// file intentionally follows the L2 project's OWN established integration-
// test convention instead (this project-owned path is what CosmosSmokeTests
// already extended for the identical Cosmos-repository-boundary reason).
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Reconciler;

/// <summary>
/// Env-guarded seam test: writes a <see cref="RunStatus.Running"/> run via
/// <see cref="CosmosProvisioningRunRepository"/> (production write path) then
/// asserts <see cref="CosmosActiveRunScanner"/> (production cross-partition
/// scan path) returns it. Both components share the SAME
/// <see cref="CosmosClient"/> instance and container, exactly as
/// <c>CosmosModule</c> wires them in production — reproducing the real
/// serializer boundary end to end. Skipped by default; opt in by setting
/// <c>COSMOS_L2_SMOKE_ENDPOINT</c> (identical env-guard to
/// <see cref="CosmosSmokeTests"/>).
/// </summary>
[Trait("Category", "Smoke")]
[Trait("RequiresLiveResource", "Cosmos")]
public sealed class CosmosActiveRunScannerSeamTests : IAsyncLifetime
{
    private const string EndpointEnvVar = "COSMOS_L2_SMOKE_ENDPOINT";
    private const string DatabaseEnvVar = "COSMOS_L2_SMOKE_DATABASE";
    private const string ContainerEnvVar = "COSMOS_L2_SMOKE_CONTAINER";
    private const string MiClientIdEnvVar = "COSMOS_L2_SMOKE_MI_CLIENTID";

    private CosmosClient? _client;
    private CosmosProvisioningRunRepository? _repository;
    private CosmosActiveRunScanner? _scanner;
    private string _testCustomerId = string.Empty;
    private readonly List<string> _createdRunIds = new();

    public Task InitializeAsync()
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointEnvVar);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            // Signal skip via null fields — every test sees this and returns early.
            return Task.CompletedTask;
        }

        var database = Environment.GetEnvironmentVariable(DatabaseEnvVar) ?? "spaarke-provisioning";
        var container = Environment.GetEnvironmentVariable(ContainerEnvVar) ?? "runs";
        var miClientId = Environment.GetEnvironmentVariable(MiClientIdEnvVar);

        var options = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrWhiteSpace(miClientId))
        {
            options.ManagedIdentityClientId = miClientId;
        }
        TokenCredential credential = new DefaultAzureCredential(options);

        // Same CosmosClientOptions shape as CosmosModule.AddCosmosModule — the
        // point of this test is to prove the repository's write and the
        // scanner's read agree on the wire format under the SDK's DEFAULT
        // (Newtonsoft-based) serializer, so it must be configured identically
        // to production, not to a hand-picked "known good" serializer.
        _client = new CosmosClient(endpoint, credential, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
            },
            ConnectionMode = ConnectionMode.Direct,
        });

        _repository = new CosmosProvisioningRunRepository(
            _client,
            databaseName: database,
            containerName: container,
            logger: NullLogger<CosmosProvisioningRunRepository>.Instance);

        _scanner = new CosmosActiveRunScanner(
            _client,
            databaseName: database,
            containerName: container,
            logger: NullLogger<CosmosActiveRunScanner>.Instance);

        // Isolate from other partitions / other test runs.
        _testCustomerId = $"seam-{Environment.MachineName.ToLowerInvariant()}-{Guid.NewGuid():N}";
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_client is null || _repository is null)
        {
            return;
        }

        var container = _client.GetContainer(
            Environment.GetEnvironmentVariable(DatabaseEnvVar) ?? "spaarke-provisioning",
            Environment.GetEnvironmentVariable(ContainerEnvVar) ?? "runs");
        foreach (var runId in _createdRunIds)
        {
            try
            {
                await container.DeleteItemAsync<ProvisioningRun>(
                    id: runId,
                    partitionKey: new PartitionKey(_testCustomerId)).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup — a leaked test doc is a maintenance
                // nuisance, not a test-failure signal.
            }
        }

        _client.Dispose();
    }

    [Fact]
    public async Task ScanAsync_AfterRepositoryWritesRunningRun_ReturnsIt()
    {
        if (_repository is null || _scanner is null)
        {
            return; // env-guarded skip
        }

        // ARRANGE — write via the PRODUCTION repository write path (same
        // CosmosClient / serializer the scanner will read through).
        var run = NewRun(RunStatus.Running);
        await _repository.CreateRunAsync(run, CancellationToken.None);
        _createdRunIds.Add(run.RunId);

        // ACT — scan via the PRODUCTION cross-partition scanner. This is the
        // exact call StateReconcilerService (5s poll) and
        // CrashRecoveryStartupService (I6 boot scan) make.
        var activeRuns = await _scanner.QueryActiveRunsAsync(CancellationToken.None);

        // ASSERT — THE regression assertion: pre-fix, RunStatus's STJ-only
        // attribute meant Newtonsoft wrote `status` as an integer, so the
        // scanner's `WHERE c.status IN ('Running', 'WaitingOnGate')` query
        // matched ZERO rows — this run would be invisible forever.
        activeRuns.Should().Contain(r => r.RunId == run.RunId && r.CustomerId == run.CustomerId,
            "the reconciler + crash-recovery scanner must see a Running run written via the " +
            "production repository write path — a serializer mismatch between the write path " +
            "(Newtonsoft, via CosmosClient default) and the STJ-only enum attribute would make " +
            "this run permanently invisible to the string-comparison query (DS-5 §C4.5)");
    }

    [Fact]
    public async Task ScanAsync_AfterRepositoryWritesWaitingOnGateRun_ReturnsIt()
    {
        if (_repository is null || _scanner is null)
        {
            return; // env-guarded skip
        }

        var run = NewRun(RunStatus.WaitingOnGate);
        await _repository.CreateRunAsync(run, CancellationToken.None);
        _createdRunIds.Add(run.RunId);

        var activeRuns = await _scanner.QueryActiveRunsAsync(CancellationToken.None);

        activeRuns.Should().Contain(r => r.RunId == run.RunId && r.CustomerId == run.CustomerId,
            "WaitingOnGate is the other active-run status the reconciler polls for");
    }

    [Fact]
    public async Task ScanAsync_AfterRepositoryWritesCompletedRun_DoesNotReturnIt()
    {
        if (_repository is null || _scanner is null)
        {
            return; // env-guarded skip
        }

        // Negative control: a terminal-status run must NOT appear in the
        // active scan (proves the query's status filter is doing real work,
        // not just returning every document regardless of status).
        var run = NewRun(RunStatus.Completed);
        await _repository.CreateRunAsync(run, CancellationToken.None);
        _createdRunIds.Add(run.RunId);

        var activeRuns = await _scanner.QueryActiveRunsAsync(CancellationToken.None);

        activeRuns.Should().NotContain(r => r.RunId == run.RunId);
    }

    private ProvisioningRun NewRun(RunStatus status)
    {
        var runId = Guid.NewGuid().ToString("N");
        return new ProvisioningRun
        {
            RunId = runId,
            CustomerId = _testCustomerId,
            EnvironmentId = Guid.NewGuid().ToString("D"),
            TenancyModel = "Model2Dedicated",
            Status = status,
            Profile = "spaarke-hosted-model2",
        };
    }
}
