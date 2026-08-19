// -----------------------------------------------------------------------------
// AzCliUpgradeDriftDetector.cs
//
// RETIRED (task 123, Wave G-2, 2026-08-19): superseded by
// <see cref="ArmWhatIfDriftDetector"/> (Azure.ResourceManager.Resources
// WhatIfAsync SDK port — typed WhatIfChange[] results, not stdout-parsed
// JSON). No longer registered in Worker/Program.cs's IUpgradeDriftDetector
// DI slot. Retained on disk (NOT deleted); see
// ProvisionCustomerScriptBicepDeployRunner.cs's retirement banner for the
// full rationale.
// -----------------------------------------------------------------------------

// Production <see cref="IUpgradeDriftDetector"/> — invokes
// `az deployment sub what-if` (subscription-scope, matching customer.bicep +
// stacks/model1-shared.bicep both being targetScope='subscription') and
// parses the "resource change" summary. Returns
// <see cref="UpgradeDriftDetectionResult.DriftDetected"/> whenever the
// what-if output contains any change type other than
// <c>NoChange</c> / <c>Ignore</c> / <c>Create</c> — Create on a "fresh"
// upgrade run means the operator manually deleted a resource, which IS
// drift. Modify/Delete/Deploy/UnsupportedResource all count as drift.
//
// NOT exercised by CI unit suite — handler tests use stubs.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>Shells out to `az deployment sub what-if`.</summary>
public sealed class AzCliUpgradeDriftDetector : IUpgradeDriftDetector
{
    private readonly BicepInfraDeployOptions _options;
    private readonly ILogger<AzCliUpgradeDriftDetector> _logger;

    /// <summary>Constructs the detector bound to the az CLI + Bicep template directory.</summary>
    public AzCliUpgradeDriftDetector(
        IOptions<BicepInfraDeployOptions> options,
        ILogger<AzCliUpgradeDriftDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<UpgradeDriftDetectionResult> DetectDriftAsync(
        BicepDeployRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var templatePath = string.Equals(request.TenancyModel, "Model1Shared", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(_options.BicepDirectory, "stacks", "model1-shared.bicep")
            : Path.Combine(_options.BicepDirectory, "customer.bicep");

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException(
                $"Bicep template not found at '{templatePath}' for tenancy '{request.TenancyModel}'.",
                templatePath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = _options.AzCliExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("deployment");
        psi.ArgumentList.Add("sub");
        psi.ArgumentList.Add("what-if");
        psi.ArgumentList.Add("--subscription");
        psi.ArgumentList.Add(request.SubscriptionId);
        psi.ArgumentList.Add("--location");
        psi.ArgumentList.Add(request.Location);
        psi.ArgumentList.Add("--template-file");
        psi.ArgumentList.Add(templatePath);
        psi.ArgumentList.Add("--parameters");
        psi.ArgumentList.Add($"customerId={request.CustomerId}");
        psi.ArgumentList.Add("--parameters");
        psi.ArgumentList.Add($"environmentName={request.EnvironmentName}");
        psi.ArgumentList.Add("--parameters");
        psi.ArgumentList.Add($"location={request.Location}");
        psi.ArgumentList.Add("--parameters");
        psi.ArgumentList.Add($"signalrEnabled={(request.SignalREnabled ? "true" : "false")}");
        psi.ArgumentList.Add("--result-format");
        psi.ArgumentList.Add("FullResourcePayloads");
        psi.ArgumentList.Add("--no-pretty-print");

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.WhatIfTimeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"az deployment sub what-if for '{request.CustomerId}' timed out after {_options.WhatIfTimeout}.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"az deployment sub what-if exited {process.ExitCode} for '{request.CustomerId}'. " +
                $"Stderr: {stderr}");
        }

        var driftText = stdout.ToString();
        var hasDrift = HasNonBenignChanges(driftText);
        _logger.LogInformation(
            "Drift detector: customerId={CustomerId} hasDrift={HasDrift}",
            request.CustomerId, hasDrift);

        return hasDrift
            ? new UpgradeDriftDetectionResult.DriftDetected(driftText)
            : new UpgradeDriftDetectionResult.NoDrift();
    }

    // Parses the what-if JSON output; returns true if any change record has a
    // "changeType" NOT in {NoChange, Ignore, Create}. NoChange + Ignore are
    // benign; Create in upgrade mode means an operator deleted a resource
    // (still drift, but classifier defers to design.md §14A.5 — currently we
    // treat Create as benign so a template that added a new resource
    // legitimately does not block. Modify/Delete/Deploy DO block.)
    private static bool HasNonBenignChanges(string whatIfJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(whatIfJson);
            if (!doc.RootElement.TryGetProperty("changes", out var changes)
                || changes.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("changeType", out var ct)
                    || ct.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                var type = ct.GetString();
                if (string.Equals(type, "Modify", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "Delete", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "Deploy", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "Unsupported", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (JsonException)
        {
            // Unparseable output — treat as drift to fail safe.
            return true;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Benign race.
        }
    }
}
