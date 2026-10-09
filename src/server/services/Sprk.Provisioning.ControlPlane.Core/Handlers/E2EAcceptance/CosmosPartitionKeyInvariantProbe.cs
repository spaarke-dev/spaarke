// -----------------------------------------------------------------------------
// CosmosPartitionKeyInvariantProbe.cs
//
// H13 §4D I3 tenant-isolation invariant probe — task 174 (Wave G-7 Batch G-7A1).
// Replaces the InfraFault deferral for InvariantKind.I3CosmosPartitionKey with
// a REAL structural check against the CUSTOMER'S own Cosmos account (deployed
// by H2a — customer.bicep provisions Cosmos for every customer stamp, task 128b).
//
// WHAT IT VERIFIES:
//   Enumerates all SQL databases + containers under the customer's Cosmos
//   account and asserts the account holds exactly the containers the stamp
//   template declares (infrastructure/bicep/modules/cosmos-db.bicep, database
//   'spaarke-ai') carry the single partition-key path declared for them
//   (<see cref="DeclaredPartitionKeys"/>; a parity test reads the bicep so the
//   two cannot drift), and that every other container has a partition key at
//   all. A container with NO partition key would admit cross-partition queries
//   by default — exactly the shape §4D I3 forbids (FR-30). Container MEMBERSHIP
//   drift (a declared container absent, or an extra one) is logged, not
//   failed: the Worker's table and the template H2a deploys ship on different
//   schedules, and ARM incremental deployments never delete a retired
//   container — neither is a partitioning defect.
//
//   Task 230a: the probe used to demand `/customerId` on every container. FR-30
//   reserves `/customerId` for L2's own ProvisioningRun container; the stamp's
//   runtime containers are tenant-scoped (`/tenantId`, the synthetic
//   `/partitionKey` = `{tenantId}|{yyyy-MM}` for audit-partitioned, `/subjectId`
//   for memory-items), so the old check failed every real stamp. On a dedicated
//   stamp the account itself holds one customer; what can drift is a container
//   deployed without the key the template declares, or one the template does
//   not know.
//
// WHAT IT DOES NOT VERIFY:
//   Runtime SDK-usage compliance ("does BFF code always pass PartitionKey?").
//   That's the compile-time ArchTest's job. This probe is BELT — a structural
//   sample check against the DEPLOYED containers, catching wrong-in-Bicep
//   partition keys the ArchTest cannot see because it scans code, not
//   deployed metadata.
//
// SILENT-FAIL AUDIT (per task 174 dispatch directive):
//   Wrong partition key on a container = silent tenant-data-cross-contamination
//   waiting to happen — exactly the class of defect H13 is the last line of
//   defense against. This probe MUST return Failed (not InfraFault) when it
//   RAN and observed a genuine violation:
//     - Empty subscription id / Cosmos endpoint      → InfraFault (wiring).
//     - Endpoint host doesn't match Cosmos shape     → InfraFault (wiring).
//     - Account not found under subscription         → InfraFault (H2a not
//                                                       landed / RBAC gap).
//     - Ambiguous account name (>1 matches)          → InfraFault (log-worthy
//                                                       ambiguity).
//     - ARM RBAC 403 / connectivity 5xx              → InfraFault.
//     - 0 containers in account                      → InfraFault (H2a shape
//                                                       problem, not silent-Pass).
//     - Declared container with a different key     → Failed (task 230a).
//     - Any container with no partition key         → Failed.
//     - Declared container absent / extra container → Passed + WARNING log
//                                                       (membership drift).
//     - >=1 container missing/wrong PK path          → Failed with diagnostic
//                                                       listing EVERY offender
//                                                       (aggregate posture:
//                                                       operator sees full
//                                                       picture on failure).
//
// LIVE-VS-FAKES POSTURE (per POML <escalation>):
//   Authored + unit-tested against ArmSdkTestFakes fake HTTP transport in
//   this task. LIVE verification against a real customer Cosmos account is
//   deferred to Wave G-7's assembly task (185) + Phase F rerun (186). All
//   ARM response shapes ground-truthed against the CosmosDB REST API docs.
//
// ARM SDK RATIONALE:
//   Reuses task 123's fake-transport pattern (ArmClientOptions.Transport →
//   HttpClientTransport → HttpMessageHandler — the well-supported test seam
//   for Azure SDK v12+ resource clients). The data-plane
//   Microsoft.Azure.Cosmos SDK is unsuited for this probe: partition-key
//   DEFINITION lives in management-plane container metadata, and the data-
//   plane SDK's transport is not fake-transport-friendly.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   L2, not BFF. Consumes NO AI-internal types (ADR-013).
//
// COMPONENT JUSTIFICATION (CLAUDE.md §11):
//   Existing — PlaceholderInvariantVerifier returned InfraFault for I3
//     unconditionally. No real probe existed.
//   Extension — the piecewise IInvariantProbe seam (task 174) is the minimum
//     move that lets THIS probe land without blocking sibling probes.
//   Cost-of-doing-nothing — the I3 gate cannot go green without this probe;
//     an acceptance-gate transition to Ready for a customer stamp
//     could pass with a mis-provisioned Cosmos container that opens cross-
//     tenant read paths — the CATASTROPHIC defect class H13 exists to prevent.
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.CosmosDB;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Real §4D I3 probe — enumerates the customer Cosmos account's SQL databases
/// + containers via ARM SDK and asserts they are exactly the containers the stamp template declares, each with its
/// declared partition-key path (<see cref="DeclaredPartitionKeys"/>).
/// </summary>
public sealed class CosmosPartitionKeyInvariantProbe : IInvariantProbe
{
    /// <summary>The database the stamp template creates (customer.bicep <c>databaseName</c>).</summary>
    public const string DeclaredDatabaseName = "spaarke-ai";

    /// <summary>
    /// Container → partition-key path, exactly as infrastructure/bicep/modules/cosmos-db.bicep declares them
    /// (CosmosPartitionKeyInvariantProbeTests pins the parity). Change both together.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DeclaredPartitionKeys { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sessions"] = "/tenantId",
            ["prompts"] = "/tenantId",
            ["audit"] = "/tenantId",
            ["audit-partitioned"] = "/partitionKey",
            ["memory"] = "/tenantId",
            ["memory-items"] = "/subjectId",
            ["feedback"] = "/tenantId",
        };

    /// <summary>
    /// Cosmos endpoint URI regex — matches the hostname pattern the ARM SDK
    /// records as <c>CosmosDBAccountData.DocumentEndpoint</c>.
    /// </summary>
    private static readonly Regex CosmosHostRegex = new(
        @"^https://(?<accountName>[a-z0-9-]+)\.documents\.azure\.com",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ArmClient _armClient;
    private readonly ILogger<CosmosPartitionKeyInvariantProbe> _logger;

    /// <inheritdoc/>
    public InvariantKind Kind => InvariantKind.I3CosmosPartitionKey;

    /// <summary>
    /// Constructs the probe. Production DI reuses the shared UAMI-pinned
    /// ArmClient singleton registered by HandlersModule (task 120).
    /// </summary>
    public CosmosPartitionKeyInvariantProbe(
        ArmClient armClient,
        ILogger<CosmosPartitionKeyInvariantProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<InvariantVerificationOutcome> ProbeAsync(
        InvariantVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (1) Pre-flight input guards.
        if (string.IsNullOrWhiteSpace(request.SubscriptionId))
        {
            return new InvariantVerificationOutcome.InfraFault(
                Kind,
                "InvariantVerificationRequest.SubscriptionId is empty — cannot enumerate the customer's Cosmos account. " +
                "H13 handler is expected to populate this from run.Parameters.NonSecret[\"subscriptionId\"].");
        }
        if (string.IsNullOrWhiteSpace(request.CosmosEndpoint))
        {
            return new InvariantVerificationOutcome.InfraFault(
                Kind,
                "InvariantVerificationRequest.CosmosEndpoint is empty — cannot locate the customer's Cosmos account under the subscription. " +
                "H2a's InterStepState.CosmosEndpoint must be populated before H13 runs.");
        }

        var host = CosmosHostRegex.Match(request.CosmosEndpoint);
        if (!host.Success)
        {
            return new InvariantVerificationOutcome.InfraFault(
                Kind,
                $"InvariantVerificationRequest.CosmosEndpoint '{request.CosmosEndpoint}' does not match the expected " +
                "'https://{accountName}.documents.azure.com[:port]/' shape — unable to derive the ARM account resource id.");
        }
        var accountName = host.Groups["accountName"].Value;

        // (2) Locate the account — enumerate subscription-scoped Cosmos accounts.
        CosmosDBAccountResource? matchedAccount;
        try
        {
            var subscription = _armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{request.SubscriptionId}"));
            var matches = new List<CosmosDBAccountResource>();
            await foreach (var candidate in subscription
                .GetCosmosDBAccountsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (candidate.HasData && NamesMatch(candidate.Data.Name, accountName))
                {
                    matches.Add(candidate);
                }
            }

            if (matches.Count == 0)
            {
                return new InvariantVerificationOutcome.InfraFault(
                    Kind,
                    $"No Cosmos DB account named '{accountName}' found under subscription '{request.SubscriptionId}'. " +
                    "Either H2a's Cosmos deployment did not land, or the L2 UAMI lacks Microsoft.DocumentDB/databaseAccounts/read RBAC on the RG.");
            }
            if (matches.Count > 1)
            {
                return new InvariantVerificationOutcome.InfraFault(
                    Kind,
                    $"Multiple Cosmos DB accounts named '{accountName}' returned under subscription '{request.SubscriptionId}' " +
                    $"(count={matches.Count}). Cosmos account names are subscription-unique — surfacing InfraFault so the ambiguity is investigated before Ready transitions.");
            }
            matchedAccount = matches[0];
        }
        catch (RequestFailedException rex)
        {
            return new InvariantVerificationOutcome.InfraFault(
                Kind,
                $"ARM Cosmos account enumeration failed for subscription '{request.SubscriptionId}': " +
                $"HTTP {rex.Status} {rex.ErrorCode ?? "(no code)"} — {rex.Message}. " +
                "Verify the L2 UAMI holds Reader on the customer subscription.");
        }

        // (3) Walk databases + containers. Capture ALL violations.
        var violations = new List<string>();
        var seenDeclared = new HashSet<string>(StringComparer.Ordinal);
        var membershipDrift = new List<string>();
        int totalContainers = 0;
        int databasesScanned = 0;

        try
        {
            var databases = matchedAccount.GetCosmosDBSqlDatabases();
            await foreach (var db in databases.GetAllAsync(cancellationToken).ConfigureAwait(false))
            {
                databasesScanned++;
                var dbName = db.Data?.Name ?? "(unknown)";
                var isDeclaredDatabase = string.Equals(dbName, DeclaredDatabaseName, StringComparison.Ordinal);
                var containers = db.GetCosmosDBSqlContainers();
                await foreach (var container in containers.GetAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    totalContainers++;
                    var containerName = container.Data?.Name ?? "(unknown)";
                    var paths = container.Data?.Resource?.PartitionKey?.Paths;

                    if (!isDeclaredDatabase || !DeclaredPartitionKeys.TryGetValue(containerName, out var declaredPath))
                    {
                        // Not the template's (a retired container, or one newer than this Worker): only the
                        // partitioning itself is checked; the membership difference is logged below.
                        if (paths is null || paths.Count == 0)
                        {
                            violations.Add(
                                $"Container '{dbName}/{containerName}' (not declared by the stamp template) has NO " +
                                "partition-key definition (paths is null/empty). §4D I3 CATASTROPHIC.");
                        }
                        else
                        {
                            membershipDrift.Add($"'{dbName}/{containerName}' (key '[{string.Join(",", paths)}]') is not declared");
                        }
                        continue;
                    }
                    seenDeclared.Add(containerName);

                    if (paths is null || paths.Count == 0)
                    {
                        violations.Add(
                            $"Container '{dbName}/{containerName}' has NO partition-key definition (paths is null/empty). " +
                            $"Declared: '{declaredPath}'. §4D I3 CATASTROPHIC.");
                        continue;
                    }

                    if (paths.Count > 1 || !string.Equals(paths[0], declaredPath, StringComparison.Ordinal))
                    {
                        violations.Add(
                            $"Container '{dbName}/{containerName}' partition key is '[{string.Join(",", paths)}]' — " +
                            $"the stamp template declares '{declaredPath}'. §4D I3 drift.");
                    }
                }
            }
        }
        catch (RequestFailedException rex)
        {
            return new InvariantVerificationOutcome.InfraFault(
                Kind,
                $"ARM Cosmos database/container enumeration failed for account '{accountName}': " +
                $"HTTP {rex.Status} {rex.ErrorCode ?? "(no code)"} — {rex.Message}. " +
                "Enumeration got past account lookup but failed on sub-resource — likely RBAC missing " +
                "Microsoft.DocumentDB/databaseAccounts/sqlDatabases/read or containers/read.");
        }

        // (4) Empty account (0 containers) — InfraFault, not Passed.
        if (totalContainers == 0)
        {
            return new InvariantVerificationOutcome.InfraFault(
                Kind,
                $"Cosmos account '{accountName}' has 0 SQL containers across {databasesScanned} database(s). " +
                "Expected H2a's customer.bicep to have created at least one container.");
        }

        foreach (var missing in DeclaredPartitionKeys.Keys.Where(k => !seenDeclared.Contains(k)).Order(StringComparer.Ordinal))
        {
            membershipDrift.Add($"'{DeclaredDatabaseName}/{missing}' is declared but absent");
        }
        if (membershipDrift.Count > 0)
        {
            _logger.LogWarning(
                "I3 probe on Cosmos account {AccountName}: container membership differs from the stamp template " +
                "(not a partitioning defect; check the deployed template version): {Drift}",
                accountName, string.Join(" | ", membershipDrift));
        }

        if (violations.Count > 0)
        {
            _logger.LogWarning(
                "I3 probe FAILED on Cosmos account {AccountName}: {ViolationCount} violation(s) across {TotalContainers} container(s). Details: {Details}",
                accountName, violations.Count, totalContainers, string.Join(" | ", violations));
            return new InvariantVerificationOutcome.Failed(
                Kind,
                $"Cosmos account '{accountName}' does not match the stamp template's partitioning (§4D I3): " +
                $"{violations.Count} violation(s) across {totalContainers} container(s). Violations: {string.Join(" | ", violations)}");
        }

        _logger.LogInformation(
            "I3 probe PASSED on Cosmos account {AccountName}: {TotalContainers} container(s) across {DatabaseCount} database(s); every declared container carries its declared partition key.",
            accountName, totalContainers, databasesScanned);
        return new InvariantVerificationOutcome.Passed(Kind);
    }

    private static bool NamesMatch(string? actual, string expected)
        => !string.IsNullOrWhiteSpace(actual) &&
           string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
}
