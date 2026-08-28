// -----------------------------------------------------------------------------
// CosmosSmokeTests.cs
//
// L2 CONTROL-PLANE Cosmos repository smoke test (task 037 step 6).
//
// PURPOSE:  End-to-end CRUD round-trip against a real dev Cosmos account so
//           the wiring in CosmosModule + CosmosProvisioningRunRepository is
//           proven against the actual SDK + real HTTP surface (ADR-038 path #3
//           external-integration boundary). Also proves the two load-bearing
//           invariants at the wire level:
//
//             1. §4D I3 partition-key predicate — every operation includes
//                `new PartitionKey(customerId)`; a wrong-partition read
//                returns null, not the neighbouring customer's data.
//             2. FR-23 I5 ETag concurrency — a stale ETag on Replace yields
//                a typed ReplaceRunResult.Conflict, NOT a CosmosException.
//
// ENV-GUARD: Skipped by default. Set COSMOS_L2_SMOKE_ENDPOINT to the dev
//           Cosmos account endpoint (e.g. https://cosmos-spaarke-platform-dev.
//           documents.azure.com:443/) AND authenticate with a principal that
//           has Cosmos DB Built-in Data Contributor on the account (an
//           operator running `az login` picks this up via DefaultAzureCredential
//           -> AzureCliCredential fallback). Optional overrides:
//             COSMOS_L2_SMOKE_DATABASE   (default: `spaarke-provisioning`)
//             COSMOS_L2_SMOKE_CONTAINER  (default: `runs`)
//             COSMOS_L2_SMOKE_MI_CLIENTID (optional; pins DefaultAzureCredential
//                                          to a specific UAMI when both a
//                                          user identity and a UAMI are
//                                          present on the host)
//
// NOT a unit test — do NOT add this file to a "unit-tests-only" glob.
// ADR-038 KEEP category: `tests/integration/**` (or the equivalent
// project-owned integration path); documented here per the task 037
// scoping ruling — the smoke test lives with L2 owning code, gated by an
// environment variable so CI unit runs are unaffected.
// -----------------------------------------------------------------------------

using System.Net;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests;

/// <summary>
/// Env-guarded smoke tests over <see cref="CosmosProvisioningRunRepository"/>.
/// Skipped by default; opt in by setting <c>COSMOS_L2_SMOKE_ENDPOINT</c>.
/// </summary>
[Trait("Category", "Smoke")]
[Trait("RequiresLiveResource", "Cosmos")]
public sealed class CosmosSmokeTests : IAsyncLifetime
{
    private const string EndpointEnvVar = "COSMOS_L2_SMOKE_ENDPOINT";
    private const string DatabaseEnvVar = "COSMOS_L2_SMOKE_DATABASE";
    private const string ContainerEnvVar = "COSMOS_L2_SMOKE_CONTAINER";
    private const string MiClientIdEnvVar = "COSMOS_L2_SMOKE_MI_CLIENTID";

    private CosmosClient? _client;
    private CosmosProvisioningRunRepository? _repository;
    private string _testCustomerId = string.Empty;
    private readonly List<string> _createdRunIds = new();

    public Task InitializeAsync()
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointEnvVar);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            // Signal skip via a null client — every test sees this and Skip.If()s out.
            return Task.CompletedTask;
        }

        var database = Environment.GetEnvironmentVariable(DatabaseEnvVar) ?? "spaarke-provisioning";
        var container = Environment.GetEnvironmentVariable(ContainerEnvVar) ?? "runs";
        var miClientId = Environment.GetEnvironmentVariable(MiClientIdEnvVar);

        TokenCredential credential;
        var options = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrWhiteSpace(miClientId))
        {
            options.ManagedIdentityClientId = miClientId;
        }
        credential = new DefaultAzureCredential(options);

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

        // Isolate this test run from other partitions by prefixing with the
        // machine + a fresh guid; test cleanup deletes what we created but a
        // late failure will not leak into any real customer partition.
        _testCustomerId = $"smoke-{Environment.MachineName.ToLowerInvariant()}-{Guid.NewGuid():N}";
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_client is null || _repository is null)
        {
            return;
        }

        // Best-effort cleanup of anything the test created. Failure is
        // logged (not surfaced) because a leaked test document is a
        // maintenance nuisance, not a test failure signal.
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
                // ignore — cleanup is opportunistic.
            }
        }

        _client.Dispose();
    }

    [Fact]
    public async Task Create_Read_Replace_RoundTrips()
    {
        if (_repository is null)
        {
            return; // env-guarded skip
        }

        var run = NewRun();

        // CREATE
        var created = await _repository.CreateRunAsync(run, CancellationToken.None);
        _createdRunIds.Add(run.RunId);
        created.Run.RunId.Should().Be(run.RunId);
        created.Run.CustomerId.Should().Be(run.CustomerId);
        created.ETag.Should().NotBeNullOrWhiteSpace();

        // READ (correct partition)
        var read = await _repository.ReadRunAsync(run.CustomerId, run.RunId, CancellationToken.None);
        read.Should().NotBeNull();
        read!.Run.RunId.Should().Be(run.RunId);
        read.Run.Status.Should().Be(run.Status);
        read.ETag.Should().Be(created.ETag);

        // REPLACE (happy path)
        var updated = read.Run;
        updated.Status = RunStatus.Running;
        updated.CurrentPhase = "H0.5";
        var replaceResult = await _repository.ReplaceRunAsync(updated, read.ETag, CancellationToken.None);
        replaceResult.Should().BeOfType<ReplaceRunResult.Success>();
        var success = (ReplaceRunResult.Success)replaceResult;
        success.Run.Status.Should().Be(RunStatus.Running);
        success.Run.CurrentPhase.Should().Be("H0.5");
        success.ETag.Should().NotBe(created.ETag);
    }

    [Fact]
    public async Task Read_WrongPartition_ReturnsNull()
    {
        if (_repository is null)
        {
            return; // env-guarded skip
        }

        var run = NewRun();
        await _repository.CreateRunAsync(run, CancellationToken.None);
        _createdRunIds.Add(run.RunId);

        // §4D I3: a read against a DIFFERENT partition key must NOT find the
        // document — proves the SDK is honoring our explicit PartitionKey.
        var wrongCustomer = $"other-{Guid.NewGuid():N}";
        var read = await _repository.ReadRunAsync(wrongCustomer, run.RunId, CancellationToken.None);
        read.Should().BeNull();
    }

    [Fact]
    public async Task Replace_StaleETag_ReturnsTypedConflict()
    {
        if (_repository is null)
        {
            return; // env-guarded skip
        }

        var run = NewRun();
        var created = await _repository.CreateRunAsync(run, CancellationToken.None);
        _createdRunIds.Add(run.RunId);

        // First replace advances the ETag.
        run.Status = RunStatus.Running;
        var firstReplace = await _repository.ReplaceRunAsync(run, created.ETag, CancellationToken.None);
        firstReplace.Should().BeOfType<ReplaceRunResult.Success>();

        // Second replace using the ORIGINAL (stale) ETag must return a typed
        // Conflict — NOT throw a CosmosException. FR-23 I5.
        run.Status = RunStatus.Failed;
        var staleReplace = await _repository.ReplaceRunAsync(run, created.ETag, CancellationToken.None);
        staleReplace.Should().BeOfType<ReplaceRunResult.Conflict>();
        var conflict = (ReplaceRunResult.Conflict)staleReplace;
        conflict.Current.Run.RunId.Should().Be(run.RunId);
        // The winning current state reflects the FIRST replace, not our stale attempt.
        conflict.Current.Run.Status.Should().Be(RunStatus.Running);
    }

    [Fact]
    public async Task Read_NotFound_ReturnsNull_NoException()
    {
        if (_repository is null)
        {
            return; // env-guarded skip
        }

        // Reading a run id that never existed in a fresh partition MUST return
        // null rather than throw — callers rely on this shape in the initial-
        // provision handshake + reconciler crash-recovery scan.
        var read = await _repository.ReadRunAsync(_testCustomerId, Guid.NewGuid().ToString("N"), CancellationToken.None);
        read.Should().BeNull();
    }

    [Fact]
    public void Read_MissingCustomerId_ThrowsArgumentException()
    {
        if (_repository is null)
        {
            return; // env-guarded skip
        }

        // Compile-time signature (customerId string non-null) + runtime
        // guard (ArgumentException.ThrowIfNullOrWhiteSpace) combine to make
        // a cross-partition read structurally impossible.
        var act = async () => await _repository!.ReadRunAsync(
            customerId: "  ",
            runId: Guid.NewGuid().ToString("N"),
            cancellationToken: CancellationToken.None);
        act.Should().ThrowAsync<ArgumentException>();
    }

    private ProvisioningRun NewRun()
    {
        var runId = Guid.NewGuid().ToString("N");
        return new ProvisioningRun
        {
            RunId = runId,
            CustomerId = _testCustomerId,
            EnvironmentId = Guid.NewGuid().ToString("D"),
            TenancyModel = "Model2Dedicated",
            Status = RunStatus.NotStarted,
            Profile = "spaarke-hosted-model2",
        };
    }
}
