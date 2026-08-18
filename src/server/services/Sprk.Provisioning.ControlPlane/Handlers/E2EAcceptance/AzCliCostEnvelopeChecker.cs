// -----------------------------------------------------------------------------
// AzCliCostEnvelopeChecker.cs
//
// Production <see cref="ICostEnvelopeChecker"/> impl — shells out to
// `az costmanagement query` to fetch the customer subscription's MTD cost,
// extrapolates a full-month total, then compares against the expected envelope
// per <see cref="H13AcceptanceOptions"/> + spec.md §15 #14.
//
// PLACEHOLDER NOTE (per POML mandatory pre-work #escalation):
//   Cost Management access requires cross-subscription Reader RBAC on the
//   customer subscription — the L2 App Service's UAMI must be granted this
//   role via the fleet-scoped provisioning role assignment (task 043 H1
//   subscription readiness pre-condition). Where the RBAC is not yet
//   configured OR the customer subscription is not reachable from the L2
//   host, this impl throws an <see cref="InvalidOperationException"/> which
//   the handler catches + classifies Resumable (per H13 handler §4C mapping).
//   Live-Azure coverage belongs in the Phase F acceptance suite (task 089).
//
// SHELL-OUT PATTERN PARITY:
//   Mirrors the shell-out shape of DeployBffApiScriptRunner.cs +
//   AzCliArmKeyVaultRefProbe.cs (Process.Start + async IO + timeout + JSON
//   stdout parse).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <inheritdoc cref="ICostEnvelopeChecker"/>
public sealed class AzCliCostEnvelopeChecker : ICostEnvelopeChecker
{
    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<AzCliCostEnvelopeChecker> _logger;

    public AzCliCostEnvelopeChecker(
        IOptions<H13AcceptanceOptions> options,
        ILogger<AzCliCostEnvelopeChecker> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CostEnvelopeReport> CheckAsync(
        CostEnvelopeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Compute the expected envelope from tenancy model.
        var expected = SelectExpectedEnvelope(request.TenancyModel);

        var psi = new ProcessStartInfo
        {
            FileName = _options.AzCliExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("costmanagement");
        psi.ArgumentList.Add("query");
        psi.ArgumentList.Add("--type");
        psi.ArgumentList.Add("ActualCost");
        psi.ArgumentList.Add("--timeframe");
        psi.ArgumentList.Add("MonthToDate");
        psi.ArgumentList.Add("--scope");
        psi.ArgumentList.Add($"/subscriptions/{request.SubscriptionId}");
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add("json");

        _logger.LogInformation(
            "H13 cost-envelope query: subscriptionId={SubscriptionId} customerId={CustomerId} runId={RunId} tenancyModel={TenancyModel} expectedUsd={ExpectedUsd}",
            request.SubscriptionId, request.CustomerId, request.RunId, request.TenancyModel, expected);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start az CLI executable '{_options.AzCliExecutable}'.");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.CostQueryTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new TimeoutException(
                $"az costmanagement query exceeded timeout {_options.CostQueryTimeout}.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"az costmanagement query exited {process.ExitCode}. stderr: {stderr.ToString().Trim()}. " +
                "Verify the L2 App Service UAMI has Reader RBAC on the customer subscription (task 043 H1 pre-condition).");
        }

        var mtdUsd = TryParseMtdUsd(stdout.ToString());
        var monthlyUsd = ExtrapolateMonthly(mtdUsd);
        var driftFraction = expected == 0m ? 0m : (monthlyUsd - expected) / expected;
        var exceeds = Math.Abs(driftFraction) > request.DriftAdvisoryThreshold;

        var summary = $"observedMonthlyUsd={monthlyUsd:F2} expectedMonthlyUsd={expected:F2} " +
                      $"driftFraction={driftFraction:P1} advisoryThreshold={request.DriftAdvisoryThreshold:P0} " +
                      $"exceeds={exceeds} tenancyModel={request.TenancyModel}";

        return new CostEnvelopeReport(monthlyUsd, expected, driftFraction, exceeds, summary);
    }

    private decimal SelectExpectedEnvelope(string tenancyModel) => tenancyModel switch
    {
        "Model2Dedicated" => _options.Model2EmptyEnvelopeUsd,
        "Model1Shared"    => _options.Model1MarginalEnvelopeUsd,
        _                 => _options.Model1SharedFloorEnvelopeUsd,
    };

    private static decimal TryParseMtdUsd(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0m;
        try
        {
            using var doc = JsonDocument.Parse(json);
            // Cost Management query response shape: rows contains arrays where the
            // first element is the cost value. Fall back to 0 if the shape does not
            // match (defensive parsing — az CLI JSON shape has been stable but
            // varies across API versions).
            if (doc.RootElement.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                decimal total = 0m;
                foreach (var row in rows.EnumerateArray())
                {
                    if (row.ValueKind == JsonValueKind.Array && row.GetArrayLength() > 0)
                    {
                        var cell = row[0];
                        if (cell.ValueKind == JsonValueKind.Number && cell.TryGetDecimal(out var v))
                        {
                            total += v;
                        }
                    }
                }
                return total;
            }
        }
        catch (JsonException)
        {
            // Ignore; return 0 to trigger advisory-warn on obvious drift.
        }
        return 0m;
    }

    private static decimal ExtrapolateMonthly(decimal mtdUsd)
    {
        var now = DateTime.UtcNow;
        var daysElapsed = Math.Max(1, now.Day); // at least one day so we don't div/0.
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        return mtdUsd / daysElapsed * daysInMonth;
    }
}
