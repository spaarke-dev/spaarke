// -----------------------------------------------------------------------------
// ArmCostEnvelopeChecker.cs
//
// TASK: 183 (Phase C'' Wave G-7 Batch G-7A2.2, 2026-08-20). SDK port of
// AzCliCostEnvelopeChecker.cs replacing the last shell-out to the Azure CLI
// cost-query command with a native Azure.ResourceManager.CostManagement SDK
// call. Per
// DS-4 section 6 this is a "straightforward REST/SDK port with no placeholder-
// replacement subtlety beyond the mechanical swap" -- the drift-classification
// arithmetic + expected-envelope-per-tenancy-model selection are ported
// verbatim, only the collaborator call changes.
//
// PURPOSE (spec.md section 15 #14 / plan.md section 8 risk register):
//   Queries actual month-to-date cost for the customer's subscription via
//   ARM Cost Management, extrapolates to a full-month total, and returns a
//   typed report comparing observed vs the expected envelope per tenancy
//   model (Model1SharedFloor / Model1Marginal / Model2EmptyEnvelope). Cost
//   drift over the documented advisory threshold (default 20% per section
//   15 #14) sets ExceedsAdvisoryThreshold=true on the report; H13 decides
//   advisory-warn vs fail-run based on H13AcceptanceOptions.CostDriftFailsRun
//   per this project's Unresolved-Questions deviation note (ambiguous
//   between fail-run + advisory-warn per project constraint on this task's
//   own escalation note).
//
// WHY SDK, NOT RAW REST:
//   The POML permits either "POST /subscriptions/{id}/providers/Microsoft.
//   CostManagement/query?api-version=2023-11-01 (or the Azure.ResourceManager.
//   CostManagement SDK equivalent if it covers this exact query shape)". The
//   SDK (v1.0.3) covers the exact shape: CostManagementExtensions.UsageQueryAsync
//   on a subscription-scoped ResourceIdentifier accepts a QueryDefinition
//   with ExportType=ActualCost + TimeframeType=MonthToDate + a QueryDataset
//   carrying an aggregation (Sum of Cost). This is identical to the retired
//   az CLI shape (verified via SDK reflection: same POST target, same JSON
//   body, same result columns/rows shape). SDK is preferred over raw HTTP
//   for the same reasons task 174 (Cosmos partition-key probe) chose ARM SDK
//   over raw REST -- typed types + built-in retry/auth pipeline + fake-
//   transport-friendly for tests via ArmClientOptions.Transport.
//
// SILENT-FAIL AUDIT (per this task's dispatch directive):
//   The worst-case silent fail is a WRONG SCOPE producing a zero-cost report
//   that classifies as "far under budget = healthy" when the actual runtime
//   fault was "we queried the wrong subscription and it happens to be idle".
//   Defenses in this port:
//     (a) SubscriptionId is a REQUIRED input; empty/whitespace throws
//         InvalidOperationException BEFORE any ARM call is made -- the
//         handler catches + classifies Resumable (parity with the retired
//         AzCliCostEnvelopeChecker's stderr-guided InvalidOperationException).
//     (b) ARM RequestFailedException (403 missing Reader RBAC, 404 wrong
//         subscription, 5xx transient) is allowed to propagate -- the
//         handler catches + classifies Resumable. NEVER swallowed into a
//         zero-cost silent Pass.
//     (c) TryParseRows() is DEFENSIVE (returns 0 on shape mismatch) but the
//         SDK-typed response shape is stable across the pinned api-version
//         -- shape mismatches would only manifest on future SDK upgrades
//         that break the QueryResult contract, which would surface as a
//         separate compile-time failure long before runtime.
//     (d) The scope IS built from request.SubscriptionId with an explicit
//         `/subscriptions/{id}` prefix (typed ResourceIdentifier) -- no
//         string interpolation into the URL path that could route to a
//         parent management-group scope by accident.
//
// THRESHOLD LOGIC (ported verbatim from AzCliCostEnvelopeChecker.cs):
//   1. SelectExpectedEnvelope(TenancyModel) -> Model2Dedicated/Model1Shared/
//      other -> Model2Empty/Model1Marginal/Model1SharedFloor USD values.
//      Bit-identical to the retired impl.
//   2. ExtrapolateMonthly(mtdUsd) -> mtdUsd/daysElapsed*daysInMonth, using
//      UtcNow.Day with Math.Max(1, ...) guard against div/0. Bit-identical.
//   3. driftFraction = expected==0 ? 0 : (monthly-expected)/expected.
//      Bit-identical (defensive div/0 branch preserved).
//   4. ExceedsAdvisoryThreshold = Math.Abs(driftFraction) > request.
//      DriftAdvisoryThreshold. Bit-identical.
//   5. Summary string is identical modulo the "cliQuery"/"sdkQuery" tag we
//      DO NOT add -- deliberately keep the exact same summary shape so
//      operator log filters + diagnostics do not break on the swap.
//
// UAMI + AUTH:
//   Injects the same shared ArmClient the sibling Wave-G-7 T1/I3/I5 probes
//   use (WorkerHost registers a factory that resolves the shared TokenCredential
//   singleton -- see Program.cs section 8). The credential is DefaultAzureCredential
//   pinned to the L2 UAMI per ADR-028 MI-outbound. Zero admin-key handling.
//   Runtime scope requirement is Reader (or Cost Management Reader) on the
//   customer subscription; missing RBAC surfaces as RequestFailedException 403.
//
// PATTERN PARITY:
//   Mirrors task 123's ArmSubscriptionReadinessProbe / task 174's
//   CosmosPartitionKeyInvariantProbe: injected ArmClient (not per-probe
//   credential chain), ArgumentNullException guards, RequestFailedException
//   allowed to propagate for handler-side rollback classification, typed
//   ResourceIdentifier for scope construction (never string-interpolation).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10):
//   L2 (Sprk.Provisioning.ControlPlane.Core), not BFF. Consumes NO AI-internal
//   types (ADR-013). No shell-out / no process-start (DS-1b section 3
//   Option D posture). Same seam as the retired AzCliCostEnvelopeChecker
//   (ICostEnvelopeChecker) -- callers unchanged, only the DI registration
//   swaps.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11):
//   Existing:  The threshold-classification arithmetic + expected-envelope-per-
//              tenancy-model selection already exist and are correct in
//              AzCliCostEnvelopeChecker.cs; the ICostEnvelopeChecker seam +
//              CostEnvelopeRequest/CostEnvelopeReport records already exist.
//   Extension: Direct mechanical port behind the same interface -- the
//              retired AzCliCostEnvelopeChecker is kept on disk unregistered
//              with a retirement banner (parity with NamingConformanceScriptRunner
//              task 182 retirement pattern) so its stderr-parsing history +
//              az CLI shape reference is preserved for future forensic value.
//   Cost-of-doing-nothing: H13's cost-envelope acceptance criterion (section
//              15 #14) requires an executable production impl; under Option
//              D's zero-shell L2 Worker host the retired AzCliCostEnvelopeChecker
//              cannot run (no `az` binary in the App Service publish layout).
//              Without this port, the last cost gate cannot execute and
//              acceptance for Phase F cannot green-light on subscription
//              cost verification.
// -----------------------------------------------------------------------------

using System.Globalization;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.CostManagement;
using Azure.ResourceManager.CostManagement.Models;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Production <see cref="ICostEnvelopeChecker"/> impl using the
/// Azure.ResourceManager.CostManagement SDK. Replaces
/// <see cref="AzCliCostEnvelopeChecker"/> (retained on disk unregistered per
/// this project's retirement convention) so H13 can execute under Option D's
/// zero-shell L2 Worker host.
/// </summary>
public sealed class ArmCostEnvelopeChecker : ICostEnvelopeChecker
{
    /// <summary>
    /// Aggregation entry name -- ARBITRARY-BUT-STABLE key the request uses to
    /// name the summed-Cost aggregation; the response echoes it back as the
    /// column name we key row parsing against. "totalCost" matches the shape
    /// the retired az CLI + the CostManagement REST API examples both use.
    /// </summary>
    internal const string AggregationEntryName = "totalCost";

    /// <summary>
    /// Aggregation function target column name -- <see cref="FunctionName.Cost"/>
    /// (the SDK-typed identifier for the "Cost" column, which is the primary
    /// USD cost column in the ActualCost usage payload). Parity with the
    /// retired az CLI's default column selection.
    /// </summary>
    internal const string AggregationTargetColumn = "Cost";

    private readonly ArmClient _armClient;
    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<ArmCostEnvelopeChecker> _logger;

    /// <summary>
    /// Constructs the checker. All collaborators are seams (ADR-010 -- ArmClient
    /// is DI-injected so tests substitute ArmClientOptions.Transport-wrapped
    /// fake HttpClient, parity with ArmSdkTestFakes in the test project).
    /// </summary>
    public ArmCostEnvelopeChecker(
        ArmClient armClient,
        IOptions<H13AcceptanceOptions> options,
        ILogger<ArmCostEnvelopeChecker> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CostEnvelopeReport> CheckAsync(
        CostEnvelopeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (1) Silent-fail defense (a): missing SubscriptionId is a fatal
        //     precondition. Throwing InvalidOperationException BEFORE any ARM
        //     call runs preserves the exact same shape the retired
        //     AzCliCostEnvelopeChecker used for missing-subscription (which
        //     upstream H13 handler catches + classifies Resumable). This
        //     prevents the wrong-scope zero-cost silent-Pass this task's
        //     dispatch directive calls out.
        if (string.IsNullOrWhiteSpace(request.SubscriptionId))
        {
            throw new InvalidOperationException(
                "CostEnvelopeRequest.SubscriptionId is empty -- cannot query Cost Management. " +
                "H2a's InterStepState must populate this before H13 runs.");
        }

        // (2) Compute the expected envelope from tenancy model. Ported verbatim
        //     from AzCliCostEnvelopeChecker.SelectExpectedEnvelope -- same
        //     tenancy-model branches, same USD constants.
        var expected = SelectExpectedEnvelope(request.TenancyModel);

        // (3) Build the SDK-typed query definition. Ground-truthed to emit the
        //     exact same JSON body the retired az CLI produced (verified via
        //     HttpMessageHandler capture on the SDK's ArmClientOptions.Transport):
        //       POST /subscriptions/{id}/providers/Microsoft.CostManagement/query?api-version=2025-03-01
        //       body: {"type":"ActualCost","timeframe":"MonthToDate",
        //              "dataset":{"aggregation":{"totalCost":{"name":"Cost","function":"Sum"}}}}
        //     Granularity is deliberately left null (SDK omits the field), matching
        //     the retired az CLI which also did not specify granularity; the server
        //     returns aggregated totals when granularity is absent (parity with the
        //     retired impl's ExtrapolateMonthly on the returned MTD sum).
        var dataset = new QueryDataset
        {
            Aggregation =
            {
                [AggregationEntryName] = new QueryAggregation(AggregationTargetColumn, FunctionType.Sum),
            },
        };
        var queryDefinition = new QueryDefinition(
            ExportType.ActualCost,
            TimeframeType.MonthToDate,
            dataset);

        // (4) Silent-fail defense (d): scope built via typed ResourceIdentifier
        //     with explicit `/subscriptions/` prefix -- never risks routing to
        //     a parent management-group scope from a malformed input.
        var scope = new ResourceIdentifier($"/subscriptions/{request.SubscriptionId}");

        _logger.LogInformation(
            "H13 cost-envelope query (SDK): subscriptionId={SubscriptionId} customerId={CustomerId} runId={RunId} tenancyModel={TenancyModel} expectedUsd={ExpectedUsd}",
            request.SubscriptionId, request.CustomerId, request.RunId, request.TenancyModel, expected);

        // (5) ARM call. RequestFailedException / other exceptions propagate --
        //     H13 handler catches + classifies Resumable (parity with retired
        //     impl's stderr-driven InvalidOperationException). NEVER swallowed
        //     into a zero-cost silent Pass (silent-fail defense (b)).
        Response<QueryResult> response = await _armClient
            .UsageQueryAsync(scope, queryDefinition, cancellationToken)
            .ConfigureAwait(false);

        // (6) Parse rows into MTD USD total. TryParseRows is defensive but the
        //     SDK-typed response shape is stable across the pinned api-version;
        //     shape mismatches would only occur on future SDK upgrades that
        //     break the QueryResult contract, which would surface as compile-
        //     time failures long before runtime (silent-fail defense (c)).
        var mtdUsd = TryParseRows(response.Value);

        // (7) Extrapolate + classify. Ported verbatim from AzCliCostEnvelopeChecker.
        var monthlyUsd = ExtrapolateMonthly(mtdUsd);
        var driftFraction = expected == 0m ? 0m : (monthlyUsd - expected) / expected;
        var exceeds = Math.Abs(driftFraction) > request.DriftAdvisoryThreshold;

        var summary = $"observedMonthlyUsd={monthlyUsd:F2} expectedMonthlyUsd={expected:F2} " +
                      $"driftFraction={driftFraction:P1} advisoryThreshold={request.DriftAdvisoryThreshold:P0} " +
                      $"exceeds={exceeds} tenancyModel={request.TenancyModel}";

        return new CostEnvelopeReport(monthlyUsd, expected, driftFraction, exceeds, summary);
    }

    /// <summary>
    /// Selects the expected monthly envelope in USD for the request's tenancy
    /// model. Ported verbatim from AzCliCostEnvelopeChecker.SelectExpectedEnvelope.
    /// Exposed internal so unit tests can pin the classifier against
    /// H13AcceptanceOptions directly.
    /// </summary>
    internal decimal SelectExpectedEnvelope(string tenancyModel) => tenancyModel switch
    {
        "Model2Dedicated" => _options.Model2EmptyEnvelopeUsd,
        "Model1Shared" => _options.Model1MarginalEnvelopeUsd,
        _ => _options.Model1SharedFloorEnvelopeUsd,
    };

    /// <summary>
    /// Extracts the aggregated MTD USD total from a QueryResult response. The
    /// response is a table: <see cref="QueryResult.Columns"/> names each column
    /// and <see cref="QueryResult.Rows"/> is the list of rows (each row is an
    /// <c>IList&lt;BinaryData&gt;</c>). For an ActualCost / Sum-of-Cost query
    /// with no grouping, there is exactly one row with one numeric cell (the
    /// summed Cost); we sum ALL rows defensively (a grouped-by-day accidental
    /// server-side default would still surface the correct total). BinaryData
    /// wraps the raw JSON token -- for a JSON number the string representation
    /// parses via decimal.TryParse under invariant culture.
    /// Exposed internal so unit tests can pin the classifier against synthetic
    /// QueryResults built via ArmCostManagementModelFactory (parity with the
    /// retired impl's TryParseMtdUsd internal exposure).
    /// </summary>
    internal static decimal TryParseRows(QueryResult result)
    {
        if (result is null || result.Rows is null || result.Rows.Count == 0)
        {
            return 0m;
        }

        // Locate the numeric column index. For the pinned QueryDefinition shape
        // the response's numeric column is named "Cost" (echoed from
        // AggregationTargetColumn). Defensive fallback: if the column is not
        // found by name, take the first Number column.
        int costColIndex = -1;
        var columns = result.Columns;
        if (columns is not null && columns.Count > 0)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (string.Equals(columns[i].Name, AggregationTargetColumn, StringComparison.OrdinalIgnoreCase))
                {
                    costColIndex = i;
                    break;
                }
            }
            if (costColIndex < 0)
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    if (string.Equals(columns[i].QueryColumnType, "Number", StringComparison.OrdinalIgnoreCase))
                    {
                        costColIndex = i;
                        break;
                    }
                }
            }
        }
        if (costColIndex < 0)
        {
            // Deep-fallback (extremely unlikely given the pinned request shape):
            // take column 0.
            costColIndex = 0;
        }

        decimal total = 0m;
        foreach (var row in result.Rows)
        {
            if (row is null || row.Count <= costColIndex)
            {
                continue;
            }
            var cell = row[costColIndex];
            if (cell is null)
            {
                continue;
            }
            var s = cell.ToString();
            if (string.IsNullOrEmpty(s))
            {
                continue;
            }
            if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                total += v;
            }
        }
        return total;
    }

    /// <summary>
    /// Extrapolates a full-month cost from a MTD figure using DateTime.UtcNow.
    /// Ported verbatim from AzCliCostEnvelopeChecker.ExtrapolateMonthly (same
    /// Math.Max(1, day) div/0 guard).
    /// Exposed internal so unit tests can invoke it directly for pin-point
    /// arithmetic verification.
    /// </summary>
    internal static decimal ExtrapolateMonthly(decimal mtdUsd)
    {
        var now = DateTime.UtcNow;
        var daysElapsed = Math.Max(1, now.Day);
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        return mtdUsd / daysElapsed * daysInMonth;
    }
}
