// -----------------------------------------------------------------------------
// ArmComputeVCpuProbe.cs
//
// Production <see cref="IPreflightQuotaProbe"/> implementation — SDK port of
// scripts/preflight/Test-SubscriptionVCpuQuota.ps1 (task 120, Wave G-2,
// Option D hybrid per DS-1b §1 H0 row). Replaces the `az vm list-usage
// --location <region>` shell-out with Azure.ResourceManager.Compute's
// SubscriptionResource.GetUsagesAsync (verified via reflection against the
// installed 1.16.0 package: returns AsyncPageable&lt;ComputeUsage&gt; where
// ComputeUsage exposes Name.Value (string), CurrentValue (int),
// Limit (long) — the exact three fields the PS script's own
// shape-verification block asserts are present).
//
// THRESHOLD LOGIC: ported verbatim from Test-SubscriptionVCpuQuota.ps1's
// per-family matching + headroom computation (see <see cref="Evaluate"/>):
//   - name.value matched by case-insensitive EXACT equality against the
//     requested SKU family name ("family names are unique in az vm
//     list-usage per region" per the PS script comment).
//   - No match -> observed/limit = "not-reported", fits = false.
//   - Match found -> observed = currentValue, limit = limit,
//     projected = observed + requested, fits = projected <= limit.
//   - H0 blocks the run if ANY requested family fails.
//
// DEFAULT REQUESTED vCPU: <c>{ "standardDv5Family": 8 }</c> — matches
// Test-SubscriptionVCpuQuota.ps1's -RequestedVCpuPerFamily default exactly
// (Standard-profile stamp: App Service Plan P1v3 = 2 vCPU + AI Search
// Standard = 2 vCPU + 4 vCPU elasticity headroom).
//
// ADR-038 TEST-BOUNDARY DESIGN: same fake-ArmClient-transport pattern as
// ArmCognitiveServicesTpmProbe.cs / task 121's ArmSubscriptionReadinessProbe
// (see that file's header for the full rationale) — <see cref="ArmClient"/>
// is injected directly; tests build one against a hand-rolled fake
// HttpMessageHandler so the SDK's own request marshaling runs unmodified.
//
// AUTH: reuses the shared platform <see cref="ArmClient"/> singleton
// (HandlersModule.cs, built from the CosmosModule <see cref="TokenCredential"/>
// per ADR-028) — targets the SPAARKE PLATFORM subscription.
// -----------------------------------------------------------------------------

using System.Text.Json;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Resources;

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// SDK-backed <see cref="IPreflightQuotaProbe"/> for subscription vCPU
/// headroom per SKU family. Reads <c>region</c> + <c>subscriptionId</c> from
/// <see cref="PreflightProbeInput.NonSecretParameters"/>.
/// </summary>
public sealed class ArmComputeVCpuProbe : IPreflightQuotaProbe
{
    /// <summary>Run-parameter key for the target Azure region.</summary>
    public const string RegionParameterKey = "region";

    /// <summary>Run-parameter key for the target Azure subscription id.</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    /// <summary>
    /// Default requested vCPU per SKU family — ported verbatim from
    /// Test-SubscriptionVCpuQuota.ps1's <c>$RequestedVCpuPerFamily</c> default.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, int> DefaultRequestedVCpuPerFamily =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["standardDv5Family"] = 8,
        };

    private readonly ArmClient _armClient;
    private readonly ILogger<ArmComputeVCpuProbe> _logger;

    /// <inheritdoc/>
    public string CheckName => PreflightCheckNames.SubscriptionVCpuQuota;

    /// <summary>
    /// Constructs the probe. In production <paramref name="armClient"/> is the
    /// shared platform <see cref="ArmClient"/> singleton (HandlersModule.cs);
    /// tests inject one built against a fake transport (see
    /// ArmComputeVCpuProbeTests.cs).
    /// </summary>
    public ArmComputeVCpuProbe(ArmClient armClient, ILogger<ArmComputeVCpuProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PreflightCheckResult> CheckAsync(PreflightProbeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.NonSecretParameters.TryGetValue(RegionParameterKey, out var region) || string.IsNullOrWhiteSpace(region))
        {
            return ConfigError($"Run parameter '{RegionParameterKey}' is required by {CheckName} (no az CLI region default under Option D).");
        }
        if (!input.NonSecretParameters.TryGetValue(SubscriptionIdParameterKey, out var subscriptionId) || string.IsNullOrWhiteSpace(subscriptionId))
        {
            return ConfigError($"Run parameter '{SubscriptionIdParameterKey}' is required by {CheckName} (no 'currently selected az account' fallback under Option D).");
        }

        _logger.LogInformation(
            "{CheckName} querying Azure.ResourceManager.Compute usage: region={Region} subscriptionId={SubscriptionId}",
            CheckName, region, subscriptionId);

        var subscription = _armClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(subscriptionId));
        var location = new AzureLocation(region);

        var usage = new List<ComputeUsageEntry>();
        await foreach (var u in subscription.GetUsagesAsync(location, cancellationToken).ConfigureAwait(false))
        {
            usage.Add(new ComputeUsageEntry(u.Name?.Value ?? string.Empty, u.CurrentValue, u.Limit));
        }

        return Evaluate(region, DefaultRequestedVCpuPerFamily, usage);
    }

    /// <summary>
    /// Pure threshold-comparison logic — ported from Test-SubscriptionVCpuQuota.ps1's
    /// per-family matching block. Exposed internal so unit tests exercise the exact
    /// evaluation function the production path calls.
    /// </summary>
    internal static PreflightCheckResult Evaluate(
        string region,
        IReadOnlyDictionary<string, int> requestedVCpuPerFamily,
        IReadOnlyList<ComputeUsageEntry> usage)
    {
        var perFamilyReport = new Dictionary<string, object?>();
        var failedFamilies = new List<string>();

        foreach (var (family, requested) in requestedVCpuPerFamily)
        {
            var matched = usage.FirstOrDefault(u => string.Equals(u.NameValue, family, StringComparison.OrdinalIgnoreCase));

            if (matched is null)
            {
                perFamilyReport[family] = new Dictionary<string, object?>
                {
                    ["observed"] = "not-reported",
                    ["limit"] = "not-reported",
                    ["requested"] = requested,
                    ["projected_after"] = "unknown",
                    ["fits"] = false,
                };
                failedFamilies.Add(family);
                continue;
            }

            var observed = matched.CurrentValue;
            var limit = matched.Limit;
            var projected = observed + requested;
            var fits = projected <= limit;

            perFamilyReport[family] = new Dictionary<string, object?>
            {
                ["observed"] = observed,
                ["limit"] = limit,
                ["requested"] = requested,
                ["projected_after"] = projected,
                ["fits"] = fits,
            };
            if (!fits)
            {
                failedFamilies.Add(family);
            }
        }

        var headroom = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["region"] = region,
            ["perFamily"] = perFamilyReport,
        });

        if (failedFamilies.Count == 0)
        {
            return new PreflightCheckResult(
                CheckName: PreflightCheckNames.SubscriptionVCpuQuota,
                Passed: true,
                Headroom: headroom,
                Diagnostic: $"Subscription vCPU headroom OK in '{region}' for all {requestedVCpuPerFamily.Count} SKU family(ies).");
        }

        var lines = new List<string>
        {
            $"Subscription vCPU headroom INSUFFICIENT in region '{region}' for {failedFamilies.Count} SKU family(ies).",
        };
        foreach (var f in failedFamilies)
        {
            var p = (Dictionary<string, object?>)perFamilyReport[f]!;
            if (Equals(p["observed"], "not-reported"))
            {
                lines.Add(
                    $"  - Family '{f}': NOT REPORTED by Azure.ResourceManager.Compute usage for region '{region}' " +
                    $"(requested {p["requested"]} vCPU). Possible causes: family name mismatch, SKU not offered in region, " +
                    "or subscription lacks Compute usage access. File Azure vCPU quota-bump support ticket if family is expected (1-3 day lead time).");
            }
            else
            {
                var shortfall = (long)p["projected_after"]! - (long)p["limit"]!;
                lines.Add(
                    $"  - Family '{f}': observed {p["observed"]} vCPU + requested {p["requested"]} vCPU = projected {p["projected_after"]}, " +
                    $"regional quota = {p["limit"]}. SHORTFALL: {shortfall} vCPU. File Azure vCPU quota-bump support ticket (1-3 day lead time per External Dependencies).");
            }
        }

        return new PreflightCheckResult(
            CheckName: PreflightCheckNames.SubscriptionVCpuQuota,
            Passed: false,
            Headroom: headroom,
            Diagnostic: string.Join("\n", lines));
    }

    private static PreflightCheckResult ConfigError(string diagnostic) => new(
        CheckName: PreflightCheckNames.SubscriptionVCpuQuota,
        Passed: false,
        Headroom: JsonDocument.Parse("{}").RootElement.Clone(),
        Diagnostic: diagnostic);
}

/// <summary>
/// Flattened projection of <c>ComputeUsage</c> (Name.Value / CurrentValue /
/// Limit) — the exact three fields the PS script's own shape-verification
/// block asserts are present. Decouples <see cref="ArmComputeVCpuProbe.Evaluate"/>
/// from the SDK model type so unit tests can build arbitrary boundary-case
/// inputs directly.
/// </summary>
internal sealed record ComputeUsageEntry(string NameValue, long CurrentValue, long Limit);
