// -----------------------------------------------------------------------------
// CosmosPartitionKeyInvariantProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for CosmosPartitionKeyInvariantProbe (task 174,
// Wave G-7 Batch G-7A1). Proves the REAL Azure.ResourceManager.CosmosDB SDK
// call path — not a hard-coded verdict — via a fake HttpClientTransport
// (parity with ArmSubscriptionReadinessProbeTests.cs task 121 +
// ArmKeyVaultRefProbeTests.cs task 123 — ADR-038 path #1).
//
// COVERAGE (task 230a: the stamp's containers carry the keys cosmos-db.bicep declares):
//   - Exactly the declared containers, each with its declared key  → Passed
//   - A declared container with another key / hierarchical key     → Failed
//   - A declared container with NO partition key                   → Failed
//   - A declared container absent / an undeclared keyed container  → Passed (membership drift logged)
//   - An undeclared container with NO partition key                → Failed
//   - Several violations                                           → Failed (all listed)
//   - The probe's table equals cosmos-db.bicep (+ customer.bicep's database name)
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
using System.Text.RegularExpressions;
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
            BffApiUrl: "");

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

    private static (string dbName, string containerName, string[] pkPaths)[] StampContainers(
        Func<string, string[]?>? overrideKey = null, string? omit = null, (string, string, string[])? extra = null)
    {
        var list = CosmosPartitionKeyInvariantProbe.DeclaredPartitionKeys
            .Where(kv => kv.Key != omit)
            .Select(kv => (dbName: CosmosPartitionKeyInvariantProbe.DeclaredDatabaseName, containerName: kv.Key,
                pkPaths: overrideKey?.Invoke(kv.Key) ?? new[] { kv.Value }))
            .ToList();
        if (extra is { } e)
        {
            list.Add(e);
        }
        return list.ToArray();
    }

    private static async Task<InvariantVerificationOutcome> ProbeStamp((string, string, string[])[] containers)
    {
        var handler = ArmSdkTestFakes.NewHandler(request => RouteCosmosRequest(request, containers));
        var probe = new CosmosPartitionKeyInvariantProbe(
            ArmSdkTestFakes.NewArmClient(handler), NullLogger<CosmosPartitionKeyInvariantProbe>.Instance);
        return await probe.ProbeAsync(NewRequest(), CancellationToken.None);
    }

    [Fact]
    public async Task ProbeAsync_TheStampTemplatesContainersWithTheirDeclaredKeys_ReturnsPassedViaGenuineArmCalls()
    {
        var handler = ArmSdkTestFakes.NewHandler(request => RouteCosmosRequest(request, StampContainers()));

        var probe = new CosmosPartitionKeyInvariantProbe(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<CosmosPartitionKeyInvariantProbe>.Instance);

        var result = await probe.ProbeAsync(NewRequest(), CancellationToken.None);
        result.Should().BeOfType<InvariantVerificationOutcome.Passed>(
            "a correctly deployed stamp uses /tenantId, /partitionKey and /subjectId — not /customerId (task 230a)");

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

    // ---------- Silent-fail-audit critical: the deployed partitioning differs from the template ----------

    [Theory]
    [InlineData("/customerId")]           // the L2 ProvisioningRun convention, wrong for a stamp container
    [InlineData("/tenantId", "/userId")]  // hierarchical
    public async Task ProbeAsync_ADeclaredContainerWithAnotherKey_ReturnsFailed(params string[] paths)
    {
        var result = await ProbeStamp(StampContainers(overrideKey: name => name == "sessions" ? paths : null));

        var failed = result.Should().BeOfType<InvariantVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(InvariantKind.I3CosmosPartitionKey);
        failed.Diagnostic.Should().Contain("spaarke-ai/sessions").And.Contain(paths[0]).And.Contain("declares '/tenantId'");
    }

    [Fact]
    public async Task ProbeAsync_ADeclaredContainerWithNoPartitionKey_ReturnsFailed()
    {
        var result = await ProbeStamp(StampContainers(overrideKey: name => name == "memory-items" ? Array.Empty<string>() : null));

        var failed = result.Should().BeOfType<InvariantVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain("memory-items").And.Contain("NO partition-key definition").And.Contain("CATASTROPHIC");
    }

    [Fact]
    public async Task ProbeAsync_ADeclaredContainerIsAbsent_IsMembershipDrift_NotAFailure()
    {
        // The Worker's table and the template H2a deploys ship on different schedules (task 230a review F3).
        var result = await ProbeStamp(StampContainers(omit: "feedback"));

        result.Should().BeOfType<InvariantVerificationOutcome.Passed>();
    }

    [Theory]
    [InlineData("spaarke-ai", "scratch")]       // a container the template does not declare (e.g. retired — ARM never deletes it)
    [InlineData("spaarke-runtime", "sessions")]  // a declared name in another database
    public async Task ProbeAsync_AnUndeclaredContainerWithAKey_IsMembershipDrift_NotAFailure(string dbName, string containerName)
    {
        var result = await ProbeStamp(StampContainers(extra: (dbName, containerName, new[] { "/tenantId" })));

        result.Should().BeOfType<InvariantVerificationOutcome.Passed>();
    }

    [Fact]
    public async Task ProbeAsync_AnUndeclaredContainerWithNoPartitionKey_ReturnsFailed()
    {
        var result = await ProbeStamp(StampContainers(extra: ("spaarke-ai", "scratch", Array.Empty<string>())));

        result.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("spaarke-ai/scratch").And.Contain("NO partition-key definition");
    }

    [Fact]
    public async Task ProbeAsync_SeveralViolations_FailedListsEveryOffender()
    {
        var result = await ProbeStamp(StampContainers(
            overrideKey: name => name switch { "prompts" => new[] { "/customerId" }, "audit" => Array.Empty<string>(), _ => null },
            extra: ("spaarke-ai", "scratch", Array.Empty<string>())));

        var failed = result.Should().BeOfType<InvariantVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain("spaarke-ai/prompts").And.Contain("spaarke-ai/audit").And.Contain("spaarke-ai/scratch");
        failed.Diagnostic.Should().Contain("3 violation(s)");
        failed.Diagnostic.Should().NotContain("spaarke-ai/feedback", "a correctly keyed container is not an offender");
    }

    // ---------- The probe's table is the stamp template's (task 230a) ----------

    [Fact]
    public void DeclaredPartitionKeys_EqualCosmosDbBicep_AndTheDatabaseIsCustomerBiceps()
    {
        var root = RepoRoot();
        var bicep = File.ReadAllText(Path.Combine(root, "infrastructure", "bicep", "modules", "cosmos-db.bicep"));
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in Regex.Split(bicep, @"(?=^resource\s)", RegexOptions.Multiline)
                     .Where(b => b.Contains("'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@", StringComparison.Ordinal)))
        {
            var name = Regex.Match(block, @"^\s*name:\s*'([^']+)'", RegexOptions.Multiline).Groups[1].Value;
            var paths = Regex.Match(block, @"paths:\s*\[([^\]]*)\]").Groups[1].Value;
            declared[name] = string.Join(",", Regex.Matches(paths, @"'([^']+)'").Select(m => m.Groups[1].Value));
        }

        declared.Should().NotBeEmpty("the parser must find the container resources");
        declared.Keys.Should().NotContain(string.Empty,
            "every container resource must have a literal name the parser can read (a loop or variable name needs the table updated by hand)");
        CosmosPartitionKeyInvariantProbe.DeclaredPartitionKeys.Should().BeEquivalentTo(declared,
            "I3 compares the deployed account with exactly what the stamp template declares");

        File.ReadAllText(Path.Combine(root, "infrastructure", "bicep", "customer.bicep"))
            .Should().Contain($"databaseName: '{CosmosPartitionKeyInvariantProbe.DeclaredDatabaseName}'");
    }

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory (parity with RunContextContractTests).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var gitMarker = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker)) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate the repo root walking up from '{AppContext.BaseDirectory}'.");
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
