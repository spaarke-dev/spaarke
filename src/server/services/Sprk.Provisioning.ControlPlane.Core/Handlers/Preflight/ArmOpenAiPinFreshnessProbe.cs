// -----------------------------------------------------------------------------
// ArmOpenAiPinFreshnessProbe.cs
//
// HANDLER-03 (pre-dispatch audit 2026-08-27, Wave 2 remediation): F1
// verbatim — pinned Azure OpenAI model versions (ADR-020 catalog:
// gpt-4o=2024-08-06, gpt-4o-mini=2024-07-18, text-embedding-3-large=1)
// MUST be GA and NOT-Deprecating in the target region + subscription
// BEFORE H2a's ~20-30 min Bicep deploy tries to create the model
// deployment and fails with ServiceModelDeprecated.
//
// USER'S MEMORY.md quote: "Azure OpenAI pins deprecate ~4-6 months after
// GA; ALWAYS check before greenfield Bicep deploy or preflight rejects
// with ServiceModelDeprecated." This probe codifies that rule.
//
// SHAPE (mirrors ArmCognitiveServicesTpmProbe.cs verbatim per CLAUDE.md §11):
//   - Ctor: (ArmClient, ILogger). Production DI (HandlersModule) supplies the
//     shared UAMI-pinned ArmClient singleton (same one the TPM probe uses —
//     no second credential chain).
//   - CheckAsync:
//     (a) reads region + subscriptionId from PreflightProbeInput.NonSecretParameters
//     (b) SubscriptionResource.GetModelsAsync(location, ct) — returns
//         AsyncPageable<CognitiveServicesAccountModel> (SDK type verified via
//         Azure.ResourceManager.CognitiveServices 1.5.2 XML docs:
//         Format/Name/Version live under `.Model`, deprecation on
//         `.Deprecation.InferenceOn`, status on `.LifecycleStatus`).
//     (c) projects each model into PinnedModelStatusEntry (Name / Version /
//         inference-deprecation-timestamp / lifecycle-status-string).
//     (d) Evaluate() — pure logic — compares against
//         PinnedModelCatalog.Models. Fails if ANY pinned entry is:
//           - not reported at all
//           - lifecycle status contains "Deprecat" (Deprecating/Deprecated)
//           - Deprecation.InferenceOn <= now + freshnessThreshold
//
// EVALUATION SEMANTICS:
//   Pass = every pinned (name, version) has a reported entry AND is not
//          in a Deprecating status AND its inference-deprecation date is
//          either null (indefinite GA) or STRICTLY greater than
//          (now + freshness threshold, default = 90 days).
//   Fail = any of the above conditions fail — H0 blocks the run.
//
// RUN-PARAMETER OVERRIDE FOR THE THRESHOLD:
//   Non-secret parameter `openaiPinFreshnessMinDays` (integer, days). Absent
//   OR non-parseable → default 90. Lets an operator lower the bar for an
//   emergency deploy at their own risk.
//
// ADR-038 TEST BOUNDARY (parity with ArmCognitiveServicesTpmProbe.cs
// task 120 / task 121):
//   - Evaluate() boundary-case tests are pure C# (build PinnedModelStatusEntry
//     fixtures directly; no ArmClient, no fake transport).
//   - CheckAsync() SDK path relies on the same shared ArmSdkTestFakes helper
//     used by the sibling probes for full-flow coverage; the priority in this
//     wave is the Evaluate() coverage (the deterministic rule) since the
//     transport shim just materializes the SDK response into
//     PinnedModelStatusEntry records.
// -----------------------------------------------------------------------------

using System.Text.Json;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.CognitiveServices; // CognitiveServicesExtensions.GetModelsAsync (subscription-scope extension method)
using Azure.ResourceManager.CognitiveServices.Models;
using Azure.ResourceManager.Resources;
using Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// SDK-backed <see cref="IPreflightQuotaProbe"/> for Azure OpenAI pinned-
/// model freshness (ADR-020 catalog). Fails preflight if any pin is
/// Deprecating / already-Deprecated / not-reported in the target region.
/// </summary>
public sealed class ArmOpenAiPinFreshnessProbe : IPreflightQuotaProbe
{
    /// <summary>Run-parameter key for the target Azure region (parity with sibling probes).</summary>
    public const string RegionParameterKey = "region";

    /// <summary>Run-parameter key for the target Azure subscription id (parity with sibling probes).</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    /// <summary>
    /// Run-parameter key for an operator-supplied freshness threshold in
    /// DAYS. Absent OR non-parseable → default 90. Lets an operator lower
    /// the bar for an emergency deploy at their own risk.
    /// </summary>
    public const string FreshnessThresholdDaysParameterKey = "openaiPinFreshnessMinDays";

    /// <summary>Default freshness window (90 days) — matches Azure OpenAI's typical 4-6 month deprecation lead time (user MEMORY.md).</summary>
    internal static readonly TimeSpan DefaultFreshnessThreshold = TimeSpan.FromDays(90);

    private readonly ArmClient _armClient;
    private readonly IReadOnlyList<PinnedModel> _pinnedModels;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ArmOpenAiPinFreshnessProbe> _logger;

    /// <inheritdoc/>
    public string CheckName => PreflightCheckNames.OpenAiPinFreshness;

    /// <summary>
    /// Constructs the probe. Production DI wires the shared platform
    /// <see cref="ArmClient"/> singleton + the canonical ADR-020
    /// <see cref="PinnedModelCatalog.Models"/> list + the ambient
    /// <see cref="TimeProvider"/>.
    /// </summary>
    public ArmOpenAiPinFreshnessProbe(
        ArmClient armClient,
        IReadOnlyList<PinnedModel> pinnedModels,
        TimeProvider timeProvider,
        ILogger<ArmOpenAiPinFreshnessProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(pinnedModels);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _pinnedModels = pinnedModels;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PreflightCheckResult> CheckAsync(PreflightProbeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.NonSecretParameters.TryGetValue(RegionParameterKey, out var region) || string.IsNullOrWhiteSpace(region))
        {
            return ConfigError($"Run parameter '{RegionParameterKey}' is required by {CheckName} (parity with {PreflightCheckNames.AzureOpenAiTpmHeadroom}).");
        }
        if (!input.NonSecretParameters.TryGetValue(SubscriptionIdParameterKey, out var subscriptionId) || string.IsNullOrWhiteSpace(subscriptionId))
        {
            return ConfigError($"Run parameter '{SubscriptionIdParameterKey}' is required by {CheckName}.");
        }

        var threshold = ResolveFreshnessThreshold(input.NonSecretParameters);

        _logger.LogInformation(
            "{CheckName} querying Azure.ResourceManager.CognitiveServices models: region={Region} subscriptionId={SubscriptionId} freshnessThresholdDays={Days}",
            CheckName, region, subscriptionId, (int)threshold.TotalDays);

        var subscription = _armClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(subscriptionId));
        var location = new AzureLocation(region);

        // SubscriptionResource.GetModelsAsync returns AsyncPageable<CognitiveServicesModel>;
        // the SDK's data shape nests the account-model surface under `.Model` (verified
        // via Azure.ResourceManager.CognitiveServices 1.5.2 XML docs: CognitiveServicesModel
        // holds a CognitiveServicesAccountModel on `.Model`; Name/Version/Format,
        // `Deprecation.InferenceOn`, and `LifecycleStatus` all live on the inner model).
        var reported = new List<PinnedModelStatusEntry>();
        await foreach (var m in subscription.GetModelsAsync(location, cancellationToken).ConfigureAwait(false))
        {
            var inner = m.Model;
            var name = inner?.Name ?? string.Empty;
            var version = inner?.Version ?? string.Empty;
            var format = inner?.Format ?? string.Empty;
            reported.Add(new PinnedModelStatusEntry(
                Name: name,
                Version: version,
                Format: format,
                InferenceDeprecation: inner?.Deprecation?.InferenceOn,
                LifecycleStatus: inner?.LifecycleStatus?.ToString()));
        }

        return Evaluate(region, _pinnedModels, reported, _timeProvider.GetUtcNow(), threshold);
    }

    /// <summary>
    /// Pure evaluation function — comparing pinned-model catalog against
    /// reported model status. Exposed <c>internal</c> so unit tests exercise
    /// the exact rule the production path calls (parity with
    /// <see cref="ArmCognitiveServicesTpmProbe.Evaluate"/>).
    /// </summary>
    internal static PreflightCheckResult Evaluate(
        string region,
        IReadOnlyList<PinnedModel> requestedPins,
        IReadOnlyList<PinnedModelStatusEntry> reportedModels,
        DateTimeOffset now,
        TimeSpan freshnessThreshold)
    {
        var perPinReport = new Dictionary<string, object?>();
        var failedPins = new List<string>();

        foreach (var pin in requestedPins)
        {
            var pinKey = $"{pin.ModelId}@{pin.PinnedVersion}";
            var match = reportedModels.FirstOrDefault(r =>
                string.Equals(r.Name, pin.ModelId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Version, pin.PinnedVersion, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                perPinReport[pinKey] = new Dictionary<string, object?>
                {
                    ["reported"] = false,
                    ["fits"] = false,
                    ["reason"] = "not-reported",
                };
                failedPins.Add(pinKey);
                continue;
            }

            // Lifecycle status contains "Deprecat" (Deprecating / Deprecated).
            var statusIsDeprecating = !string.IsNullOrEmpty(match.LifecycleStatus)
                && match.LifecycleStatus.Contains("Deprecat", StringComparison.OrdinalIgnoreCase);

            // Inference-deprecation date <= now + freshness threshold →
            // window has expired or too close for comfort.
            var inferenceDate = match.InferenceDeprecation;
            var windowExpired = inferenceDate is { } d && d <= now + freshnessThreshold;

            var fits = !statusIsDeprecating && !windowExpired;

            perPinReport[pinKey] = new Dictionary<string, object?>
            {
                ["reported"] = true,
                ["fits"] = fits,
                ["lifecycleStatus"] = match.LifecycleStatus ?? "(unknown)",
                ["inferenceDeprecationOn"] = inferenceDate?.ToString("O"),
                ["reason"] = fits
                    ? "ga"
                    : statusIsDeprecating
                        ? "deprecating-status"
                        : "window-expired",
            };
            if (!fits)
            {
                failedPins.Add(pinKey);
            }
        }

        var headroom = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["region"] = region,
            ["freshnessThresholdDays"] = (int)freshnessThreshold.TotalDays,
            ["evaluatedAt"] = now.ToString("O"),
            ["perPin"] = perPinReport,
        });

        if (failedPins.Count == 0)
        {
            return new PreflightCheckResult(
                CheckName: PreflightCheckNames.OpenAiPinFreshness,
                Passed: true,
                Headroom: headroom,
                Diagnostic:
                    $"All {requestedPins.Count} ADR-020 pinned OpenAI model versions are GA and outside the " +
                    $"{(int)freshnessThreshold.TotalDays}-day deprecation window in region '{region}'.");
        }

        var lines = new List<string>
        {
            $"ADR-020 pinned OpenAI model freshness check FAILED for {failedPins.Count} of " +
            $"{requestedPins.Count} pinned deployments in region '{region}' " +
            $"(freshness threshold: {(int)freshnessThreshold.TotalDays} days).",
        };
        foreach (var key in failedPins)
        {
            var entry = (Dictionary<string, object?>)perPinReport[key]!;
            var reason = entry["reason"] as string ?? "unknown";
            switch (reason)
            {
                case "not-reported":
                    lines.Add(
                        $"  - '{key}': NOT REPORTED by Azure.ResourceManager.CognitiveServices models list for region '{region}'. " +
                        "Verify the pinned model+version is available in region + your subscription has been enabled for it. " +
                        "If Azure has retired this version entirely, ADR-020 pin bump required.");
                    break;
                case "deprecating-status":
                    var status = entry["lifecycleStatus"] as string ?? "(unknown)";
                    var dep = entry["inferenceDeprecationOn"] as string ?? "(none)";
                    lines.Add(
                        $"  - '{key}': lifecycleStatus='{status}' inferenceDeprecationOn={dep}. Azure has scheduled deprecation — " +
                        "ADR-020 pin bump required BEFORE next provisioning run (H2a would otherwise fail with ServiceModelDeprecated).");
                    break;
                case "window-expired":
                    var dep2 = entry["inferenceDeprecationOn"] as string ?? "(unknown)";
                    lines.Add(
                        $"  - '{key}': inferenceDeprecationOn={dep2} is within the {(int)freshnessThreshold.TotalDays}-day freshness window. " +
                        "ADR-020 pin bump recommended to avoid provisioning failures as the deprecation date approaches.");
                    break;
                default:
                    lines.Add($"  - '{key}': freshness check failed ({reason}).");
                    break;
            }
        }

        return new PreflightCheckResult(
            CheckName: PreflightCheckNames.OpenAiPinFreshness,
            Passed: false,
            Headroom: headroom,
            Diagnostic: string.Join("\n", lines));
    }

    private static TimeSpan ResolveFreshnessThreshold(IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.TryGetValue(FreshnessThresholdDaysParameterKey, out var raw)
            && int.TryParse(raw, out var days) && days > 0)
        {
            return TimeSpan.FromDays(days);
        }
        return DefaultFreshnessThreshold;
    }

    private static PreflightCheckResult ConfigError(string diagnostic) => new(
        CheckName: PreflightCheckNames.OpenAiPinFreshness,
        Passed: false,
        Headroom: JsonDocument.Parse("{}").RootElement.Clone(),
        Diagnostic: diagnostic);
}

/// <summary>
/// Flattened projection of one Cognitive Services model as returned by
/// <see cref="SubscriptionResource"/>'s GetModelsAsync — mirrors the fields
/// the pin-freshness rule reads (Name / Version / Format live under
/// <c>.Model</c>; deprecation on <c>.Deprecation.InferenceOn</c>; status on
/// <c>.LifecycleStatus</c>). Decouples <see cref="ArmOpenAiPinFreshnessProbe.Evaluate"/>
/// from the SDK model types (no public constructor) so unit tests build
/// arbitrary boundary-case inputs directly (parity with
/// <see cref="CognitiveServicesUsageEntry"/> in the sibling TPM probe).
/// </summary>
internal sealed record PinnedModelStatusEntry(
    string Name,
    string Version,
    string Format,
    DateTimeOffset? InferenceDeprecation,
    string? LifecycleStatus);
