// -----------------------------------------------------------------------------
// CosmosPartitionKeyInvariantProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for CosmosPartitionKeyInvariantProbe (task 174,
// Wave G-7 Batch G-7A1). Proves the REAL Azure.ResourceManager.CosmosDB SDK
// call path — not a hard-coded verdict — via a fake HttpClientTransport
// (parity with ArmSubscriptionReadinessProbeTests.cs task 121 +
// ArmKeyVaultRefProbeTests.cs task 123 — ADR-038 path #1).
//
// COVERAGE (maps to POML acceptance criteria):
//   - Every container has correct pk='/customerId'                → Passed
//   - One container has wrong pk path                              → Failed
//   - One container has NO partition key (paths null/empty)        → Failed
//   - Multiple containers, mixed correct + violations              → Failed (all listed)
//   - Cosmos endpoint invalid host shape                           → InfraFault
//   - Empty SubscriptionId                                         → InfraFault
//   - Empty CosmosEndpoint                                         → InfraFault
//   - Account not found under subscription                         → InfraFault
//   - Account list returns 403 RBAC                                → InfraFault
//   - Container list returns 403 RBAC                              → InfraFault
//   - Empty account (0 databases/containers)                       → InfraFault
//   - Kind property matches InvariantKind.I3CosmosPartitionKey     → structural
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.ResourceManager;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class CosmosPartitionKeyInvariantProbeTests
{
    private const string SubscriptionId = "11111111-2222-3333-4444-555555555555";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string AccountName = "cosmos-spaarke-acme-prod";
    private const string CosmosEndpoint = "https://cosmos-spaarke-acme-prod.documents.azure.com:443/";
    private const string CustomerId = "acme";
    private const string RunId = "run-abcdef";

    private static InvariantVerificationRequest NewRequest(
        string? subscriptionId = null,
        string? cosmosEndpoint = null) => new(
            CustomerId: CustomerId,
            RunId: RunId,
            TenantId: TenantId,
            SubscriptionId: subscriptionId ?? SubscriptionId,
            AiSearchEndpoint: "",
            CosmosEndpoint: cosmosEndpoint ?? CosmosEndpoint,
            BffApiUrl: "",
            ProvisioningScriptsDirectory: "");

    // ---------- Kind + Structural ----------

    [Fact]
    public void Kind_MatchesI3CosmosPartitionKey()
    {
        var probe = new CosmosPartitionKeyInvariantProbe(
            ArmSdkTestFakes.NewArmClient(ArmSdkTestFakes.NewHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            NullLogger<CosmosPartitionKeyInvariantProbe>.Instance);
        probe.Kind.Should().Be(InvariantKind.I3CosmosPartitionKey);
    }

    // ---------- Pre-flight guards ----------

    [Fact]
    public async Task ProbeAsync_EmptySubscriptionId_ReturnsInfraFault()
    {
        var probe = NewProbe(_ => throw new InvalidOperationException("HTTP should NOT be invoked when SubscriptionId is empty"));
        var result = await probe.ProbeAsync(NewRequest(subscriptionId: ""), CancellationToken.None);

        result.Should().BeOfType<InvariantVerificationOutcome.InfraFault>();
        ((InvariantVerificationOutcome.InfraFault)result).Diagnostic.Should().Contain("SubscriptionId is empty");
    }

    [Fact]
    public async Task ProbeAsync_EmptyCosmosEndpoint_ReturnsInfraFault()
    {
        var probe = NewProbe(_ => throw new InvalidOperationException("HTTP should NOT be invoked when CosmosEndpoint is empty"));
        var result = await probe.ProbeAsync(NewRequest(cosmosEndpoint: ""), CancellationToken.None);

        result.Should().BeOfType<InvariantVerificationOutcome.InfraFault>();
        ((InvariantVerificationOutcome.InfraFault)result).Diagnostic.Should().Contain("CosmosEndpoint is empty");
    }

    [Fact]
    public async Task ProbeAsync_MalformedCosmosEndpoint_ReturnsInfraFault()
    {
        var probe = NewProbe(_ => throw new InvalidOperationException("HTTP should NOT be invoked on malformed endpoint"));
        var result = await probe.ProbeAsync(
            NewRequest(cosmosEndpoint: "https://not-cosmos.example.com/"),
            CancellationToken.None);

        result.Should().BeOfType<InvariantVerificationOutcome.InfraFault>();
        ((InvariantVerificationOutcome.InfraFault)result).Diagnostic.Should().Contain("does not match the expected");
    }

    // ---------- Happy path ----------

    [Fact]
    public async Task ProbeAsync_SingleContainerCorrectPartitionKey_ReturnsPassedViaGenuineArmCalls()
    {
        var handler = ArmSdkTestFakes.NewHandler(request => RouteCosmosRequest(request,
            containers: new[] { (dbName: "spaarke-runtime", containerName: "runs", pkPaths: new[] { "/customerId" }) }));

        var probe = new CosmosPartitionKeyInvariantProbe(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<CosmosPartitionKeyInvariantProbe>.Instance);

        var result = await probe.ProbeAsync(NewRequest(), CancellationToken.None);
        result.Should().BeOfType<InvariantVerificationOutcome.Passed>();

        // Real-call assertion: probe MUST have hit ARM management-plane
        // endpoints (accounts list + databases list + containers list) —
        // proves this is not a hard-coded Passed.
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.Contains("Microsoft.DocumentDB/databaseAccounts"),
            "asserts the CosmosDB accounts enumeration was invoked over HTTP");
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.Contains("sqlDatabases"),
            "asserts the SQL databases enumeration was invoked");
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.Contains("containers"),
            "asserts the containers enumeration was invoked");
    }

    // ---------- Silent-fail-audit critical: wrong PK path ----------

    [Fact]
    public async Task ProbeAsync_ContainerWithWrongPartitionKeyPath_ReturnsFailed()
    {
        var handler = ArmSdkTestFakes.NewHandler(request => RouteCosmosRequest(request,
            containers: new[] { (dbName: "spaarke-runtime", containerName: "runs", pkPaths: new[] { "/tenantId" }) }));

        var probe = new CosmosPartitionKeyInvariantProbe(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<CosmosPartitionKeyInvariantProbe>.Instance);

        var result = await probe.ProbeAsync(NewRequest(), CancellationToken.None);
        result.Should().BeOfType<InvariantVerificationOutcome.Failed>();
        var failed = (InvariantVerificationOutcome.Failed)result;
        failed.Kind.Should().Be(InvariantKind.I3CosmosPartitionKey);
        failed.Diagnostic.Should().Contain("/tenantId");
        failed.Diagnostic.Should().Contain("/customerId");
        failed.Diagnostic.Should().Contain("spaarke-runtime/runs");
    }

    [Fact]
    public async Task ProbeAsync_ContainerWithNoPartitionKey_ReturnsFailed()
    {
        var handler = ArmSdkTestFakes.NewHandler(request => RouteCosmosRequest(request,
            containers: new[] { (dbName: "spaarke-runtime", containerName: "runs", pkPaths: Array.Empty<string>()) }));

        var probe = new CosmosPartitionKeyInvariantProbe(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<CosmosPartitionKeyInvariantProbe>.Instance);

        var result = await probe.ProbeAsync(NewRequest(), CancellationToken.None);
        result.Should().BeOfType<InvariantVerificationOutcome.Failed>();
        var failed = (InvariantVerificationOutcome.Failed)result;
        failed.Diagnostic.Should().Contain("NO partition-key definition");
        failed.Diagnostic.Should().Contain("CATASTROPHIC");
    }

    [Fact]
    public async Task ProbeAsync_MixedCorrectAndViolationContainers_FailedListsEveryOffender()
    {
        var handler = ArmSdkTestFakes.NewHandler(request => RouteCosmosRequest(request,
            containers: new[]
            {
                (dbName: "db1", containerName: "goodOne",  pkPaths: new[] { "/customerId" }),
                (dbName: "db1", containerName: "badPath",  pkPaths: new[] { "/tenantId" }),
                (dbName: "db1", containerName: "noPk",     pkPaths: Array.Empty<string>()),
            }));

        var probe = new CosmosPartitionKeyInvariantProbe(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<CosmosPartitionKeyInvariantProbe>.Instance);

        var result = await probe.ProbeAsync(NewRequest(), CancellationToken.None);
        result.Should().BeOfType<InvariantVerificationOutcome.Failed>();
        var failed = (InvariantVerificationOutcome.Failed)result;
        failed.Diagnostic.Should().Contain("badPath");
        failed.Diagnostic.Should().Contain("noPk");
        failed.Diagnostic.Should().NotContain("goodOne violate",
            "the passing container must NOT appear in the violation list");
        failed.Diagnostic.Should().Contain("2 of 3 container(s)",
            "aggregate summary shows partial-violation count");
    }

    // ---------- Infrastructure faults ----------

    [Fact]
    public async Task ProbeAsync_AccountNotFound_ReturnsInfraFault()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("Microsoft.DocumentDB/databaseAccounts"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """{"value":[]}""");
            }
            throw new InvalidOperationException("Unexpected request: " + request.RequestUri.AbsolutePath);
        });

        var probe = new CosmosPartitionKeyInvariantProbe(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<CosmosPartitionKeyInvariantProbe>.Instance);

        var result = await probe.ProbeAsync(NewRequest(), CancellationToken.None);
        result.Should().BeOfType<InvariantVerificationOutcome.InfraFault>();
        ((InvariantVerificationOutcome.InfraFault)result).Diagnostic.Should().Contain($"No Cosmos DB account named '{AccountName}'");
    }

    [Fact]
    public async Task ProbeAsync_AccountListReturns403_ReturnsInfraFault()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("Microsoft.DocumentDB/databaseAccounts"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                    ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "Missing Reader on subscription."));
            }
            throw new InvalidOperationException("Unexpected request: " + request.RequestUri.AbsolutePath);
        });

        var probe = new CosmosPartitionKeyInvariantProbe(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<CosmosPartitionKeyInvariantProbe>.Instance);

        var result = await probe.ProbeAsync(NewRequest(), CancellationToken.None);
        result.Should().BeOfType<InvariantVerificationOutcome.InfraFault>();
        ((InvariantVerificationOutcome.InfraFault)result).Diagnostic.Should().Contain("account enumeration failed");
    }

    [Fact]
    public async Task ProbeAsync_ContainerListReturns403_ReturnsInfraFault()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("Microsoft.DocumentDB/databaseAccounts") && !path.Contains("sqlDatabases"))
            {
                // account list
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    CosmosBodies.AccountsListBody(SubscriptionId, AccountName));
            }
            if (path.Contains("sqlDatabases") && !path.Contains("containers"))
            {
                // database list
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    CosmosBodies.DatabasesListBody(SubscriptionId, AccountName, "db1"));
            }
            if (path.Contains("containers"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                    ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "Missing container read RBAC."));
            }
            throw new InvalidOperationException("Unexpected: " + path);
        });

        var probe = new CosmosPartitionKeyInvariantProbe(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<CosmosPartitionKeyInvariantProbe>.Instance);

        var result = await probe.ProbeAsync(NewRequest(), CancellationToken.None);
        result.Should().BeOfType<InvariantVerificationOutcome.InfraFault>();
        ((InvariantVerificationOutcome.InfraFault)result).Diagnostic.Should().Contain("database/container enumeration failed");
    }

    [Fact]
    public async Task ProbeAsync_EmptyAccountZeroDatabases_ReturnsInfraFault()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("Microsoft.DocumentDB/databaseAccounts") && !path.Contains("sqlDatabases"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    CosmosBodies.AccountsListBody(SubscriptionId, AccountName));
            }
            if (path.Contains("sqlDatabases"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """{"value":[]}""");
            }
            throw new InvalidOperationException("Unexpected: " + path);
        });

        var probe = new CosmosPartitionKeyInvariantProbe(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<CosmosPartitionKeyInvariantProbe>.Instance);

        var result = await probe.ProbeAsync(NewRequest(), CancellationToken.None);
        result.Should().BeOfType<InvariantVerificationOutcome.InfraFault>();
        ((InvariantVerificationOutcome.InfraFault)result).Diagnostic.Should().Contain("0 SQL containers");
    }

    // -------------------------------------------------------------------------
    // Test infrastructure
    // -------------------------------------------------------------------------

    private static CosmosPartitionKeyInvariantProbe NewProbe(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = ArmSdkTestFakes.NewHandler(responder);
        var client = ArmSdkTestFakes.NewArmClient(handler);
        return new CosmosPartitionKeyInvariantProbe(client, NullLogger<CosmosPartitionKeyInvariantProbe>.Instance);
    }

    /// <summary>
    /// Routes a request to the appropriate CosmosDB management-plane fake
    /// response body based on URL shape:
    ///   .../Microsoft.DocumentDB/databaseAccounts                 → accounts list
    ///   .../databaseAccounts/{name}/sqlDatabases                  → databases list
    ///   .../sqlDatabases/{db}/containers                          → containers list
    /// Any unexpected URL throws — tests catch drift in the SDK URL contract.
    /// </summary>
    private static HttpResponseMessage RouteCosmosRequest(
        HttpRequestMessage request,
        (string dbName, string containerName, string[] pkPaths)[] containers)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.Contains("Microsoft.DocumentDB/databaseAccounts") && !path.Contains("sqlDatabases"))
        {
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                CosmosBodies.AccountsListBody(SubscriptionId, AccountName));
        }

        if (path.Contains("sqlDatabases") && !path.Contains("containers"))
        {
            var distinctDbs = containers.Select(c => c.dbName).Distinct().ToArray();
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                CosmosBodies.DatabasesListBody(SubscriptionId, AccountName, distinctDbs));
        }

        if (path.Contains("containers"))
        {
            // Extract db name from URL segment: .../sqlDatabases/{dbName}/containers
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var idx = Array.IndexOf(segments, "sqlDatabases");
            var dbName = (idx >= 0 && idx + 1 < segments.Length) ? segments[idx + 1] : "unknown";
            var forDb = containers.Where(c => c.dbName == dbName).ToArray();
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                CosmosBodies.ContainersListBody(SubscriptionId, AccountName, dbName, forDb));
        }

        throw new InvalidOperationException("Unexpected request URL: " + path);
    }

    /// <summary>
    /// Cosmos DB management-plane response body helpers. Response shapes are
    /// ground-truthed against the CosmosDB REST API 2024-05-15 (properties
    /// JSON envelope, partitionKey.paths[] shape).
    /// </summary>
    private static class CosmosBodies
    {
        public static string AccountsListBody(string subscriptionId, string accountName) =>
            $$"""
            {
              "value": [
                {
                  "id": "/subscriptions/{{subscriptionId}}/resourceGroups/rg-fake/providers/Microsoft.DocumentDB/databaseAccounts/{{accountName}}",
                  "name": "{{accountName}}",
                  "location": "eastus",
                  "type": "Microsoft.DocumentDB/databaseAccounts",
                  "kind": "GlobalDocumentDB",
                  "properties": {
                    "documentEndpoint": "https://{{accountName}}.documents.azure.com:443/",
                    "provisioningState": "Succeeded",
                    "databaseAccountOfferType": "Standard"
                  }
                }
              ]
            }
            """;

        public static string DatabasesListBody(string subscriptionId, string accountName, params string[] dbNames)
        {
            if (dbNames.Length == 0)
            {
                return """{"value":[]}""";
            }
            var sb = new StringBuilder();
            sb.Append("{\"value\":[");
            for (int i = 0; i < dbNames.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($$"""
                {
                  "id": "/subscriptions/{{subscriptionId}}/resourceGroups/rg-fake/providers/Microsoft.DocumentDB/databaseAccounts/{{accountName}}/sqlDatabases/{{dbNames[i]}}",
                  "name": "{{dbNames[i]}}",
                  "type": "Microsoft.DocumentDB/databaseAccounts/sqlDatabases",
                  "properties": { "resource": { "id": "{{dbNames[i]}}" } }
                }
                """);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static string ContainersListBody(
            string subscriptionId,
            string accountName,
            string dbName,
            (string dbName, string containerName, string[] pkPaths)[] containers)
        {
            if (containers.Length == 0)
            {
                return """{"value":[]}""";
            }
            var sb = new StringBuilder();
            sb.Append("{\"value\":[");
            for (int i = 0; i < containers.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var c = containers[i];
                sb.Append($$"""
                {
                  "id": "/subscriptions/{{subscriptionId}}/resourceGroups/rg-fake/providers/Microsoft.DocumentDB/databaseAccounts/{{accountName}}/sqlDatabases/{{dbName}}/containers/{{c.containerName}}",
                  "name": "{{c.containerName}}",
                  "type": "Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers",
                  "properties": {
                    "resource": {
                      "id": "{{c.containerName}}",
                      "partitionKey": {
                        "paths": [{{string.Join(",", c.pkPaths.Select(p => "\"" + p + "\""))}}],
                        "kind": "Hash"
                      }
                    }
                  }
                }
                """);
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
