// -----------------------------------------------------------------------------
// ArmCognitiveServicesTpmProbe.cs
//
// Production <see cref="IPreflightQuotaProbe"/> implementation — SDK port of
// scripts/preflight/Test-AzureOpenAiTpmHeadroom.ps1 (task 120, Wave G-2,
// Option D hybrid per DS-1b §1 H0 row). Replaces the `az cognitiveservices
// usage list --location <region>` shell-out with
// Azure.ResourceManager.CognitiveServices's SubscriptionResource.GetUsagesAsync
// (verified via reflection against the installed 1.5.2 package: returns
// AsyncPageable&lt;ServiceAccountUsage&gt; where ServiceAccountUsage exposes
// Name.Value (string), CurrentValue (double?), Limit (double?) — the exact
// same three fields (name.value / currentValue / limit) the PS script itself
// asserts are present per its shape-verification block).
//
// THRESHOLD LOGIC: ported verbatim from Test-AzureOpenAiTpmHeadroom.ps1's
// per-model matching + headroom computation (see <see cref="Evaluate"/>):
//   - name.value matched against "(?:^|[.\-/_ ])modelName$" (case-insensitive)
//     so a request for 'gpt-4o' does not also match 'gpt-4o-mini'.
//   - No match -> observed/limit = "not-reported", fits = false.
//   - Match(es) found -> observed = SUM(currentValue), limit = MAX(limit),
//     projected = observed + requested, fits = projected <= limit.
//   - H0 blocks the run if ANY requested model fails (no advisory results).
//
// DEFAULT REQUESTED TPM: NFR-12's 150+200+30+350 per-model sum (gpt-4o /
// gpt-4o-mini / text-embedding-3-large / text-embedding-3-small) — matches
// Test-AzureOpenAiTpmHeadroom.ps1's -RequestedTpmPerModel default exactly.
//
// ADR-038 TEST-BOUNDARY DESIGN: task 121 (Wave G-2, same wave —
// ArmSubscriptionReadinessProbe) established the codebase's precedent for
// testing Azure.ResourceManager SDK calls: construct a REAL <see cref="ArmClient"/>
// against a hand-rolled fake <see cref="HttpMessageHandler"/> wrapped in
// <see cref="Azure.Core.Pipeline.HttpClientTransport"/>, so the SDK's own
// request marshaling / URL building / STJ deserialization all run
// unmodified — only the HTTP socket is faked. This probe follows the SAME
// pattern (CLAUDE.md §11 — reuse, don't reinvent a parallel test
// philosophy in the same wave) rather than introducing a bespoke reader
// interface: <see cref="ArmClient"/> is injected directly, and tests build
// one via the shared fake-transport helper. This is NOT the banned
// Mock&lt;HttpMessageHandler&gt; pattern (a genuine hand-rolled fake, not a
// Moq mock of the transport) and NOT a Mock&lt;IServiceClient&gt;
// wrapper-mock — testing.md's own "fake HttpClient via test-double" guidance
// applied at the transport layer the SDK itself exposes for exactly this
// purpose.
//
// AUTH: reuses the shared <see cref="TokenCredential"/> singleton registered
// by CosmosModule (UAMI-pinned per ADR-028 MI-outbound) to construct the
// shared platform <see cref="ArmClient"/> singleton (HandlersModule.cs) —
// this probe targets the SPAARKE PLATFORM subscription (not a customer
// tenant), so no per-call TenantId override is needed (unlike the
// Graph/Dataverse REST collaborators elsewhere in Handlers/** that scope
// DefaultAzureCredential to the customer tenant via §4D I5).
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.CognitiveServices;
using Azure.ResourceManager.Resources;

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// SDK-backed <see cref="IPreflightQuotaProbe"/> for Azure OpenAI regional
/// TPM headroom. Reads <c>region</c> + <c>subscriptionId</c> from
/// <see cref="PreflightProbeInput.NonSecretParameters"/> (same keys the H0
/// test fixtures + <see cref="PowerShellPreflightProbe"/>'s predecessor
/// contract already establish).
/// </summary>
public sealed class ArmCognitiveServicesTpmProbe : IPreflightQuotaProbe
{
    /// <summary>Run-parameter key for the target Azure region.</summary>
    public const string RegionParameterKey = "region";

    /// <summary>Run-parameter key for the target Azure subscription id.</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    /// <summary>
    /// NFR-12 default per-model TPM (thousands) — ported verbatim from
    /// Test-AzureOpenAiTpmHeadroom.ps1's <c>$RequestedTpmPerModel</c> default.
    /// Not run-parameter-overridable (NFR-12 is a fixed contractual sum);
    /// mirrors the PS script's own documented default exactly.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, int> DefaultRequestedTpmPerModel =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4o"] = 150,
            ["gpt-4o-mini"] = 200,
            ["text-embedding-3-large"] = 30,
            ["text-embedding-3-small"] = 350,
        };

    private readonly ArmClient _armClient;
    private readonly ILogger<ArmCognitiveServicesTpmProbe> _logger;

    /// <inheritdoc/>
    public string CheckName => PreflightCheckNames.AzureOpenAiTpmHeadroom;

    /// <summary>
    /// Constructs the probe. In production <paramref name="armClient"/> is the
    /// shared platform <see cref="ArmClient"/> singleton (HandlersModule.cs,
    /// built from the CosmosModule <see cref="TokenCredential"/>); tests inject
    /// one built against a fake transport (see ArmCognitiveServicesTpmProbeTests.cs).
    /// </summary>
    public ArmCognitiveServicesTpmProbe(ArmClient armClient, ILogger<ArmCognitiveServicesTpmProbe> logger)
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
            "{CheckName} querying Azure.ResourceManager.CognitiveServices usage: region={Region} subscriptionId={SubscriptionId}",
            CheckName, region, subscriptionId);

        var subscription = _armClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(subscriptionId));
        var location = new AzureLocation(region);

        var usage = new List<CognitiveServicesUsageEntry>();
        await foreach (var u in subscription.GetUsagesAsync(location, filter: null, cancellationToken).ConfigureAwait(false))
        {
            usage.Add(new CognitiveServicesUsageEntry(u.Name?.Value ?? string.Empty, u.CurrentValue ?? 0, u.Limit ?? 0));
        }

        return Evaluate(region, DefaultRequestedTpmPerModel, usage);
    }

    /// <summary>
    /// Pure threshold-comparison logic — ported from Test-AzureOpenAiTpmHeadroom.ps1's
    /// per-model matching block. Exposed internal so unit tests exercise the exact
    /// evaluation function the production path calls.
    /// </summary>
    internal static PreflightCheckResult Evaluate(
        string region,
        IReadOnlyDictionary<string, int> requestedTpmPerModel,
        IReadOnlyList<CognitiveServicesUsageEntry> usage)
    {
        var perModelReport = new Dictionary<string, object?>();
        var failedModels = new List<string>();

        foreach (var (modelName, requested) in requestedTpmPerModel)
        {
            // Separators recognized: `.` `-` `/` `_` ` ` (space). Case-insensitive.
            // Anchored at end so 'gpt-4o' does not also match 'gpt-4o-mini'.
            var pattern = $@"(?:^|[.\-/_ ]){Regex.Escape(modelName)}$";
            var matched = usage.Where(u => Regex.IsMatch(u.NameValue, pattern, RegexOptions.IgnoreCase)).ToList();

            if (matched.Count == 0)
            {
                perModelReport[modelName] = new Dictionary<string, object?>
                {
                    ["observed"] = "not-reported",
                    ["limit"] = "not-reported",
                    ["requested"] = requested,
                    ["projected_after"] = "unknown",
                    ["fits"] = false,
                };
                failedModels.Add(modelName);
                continue;
            }

            var observed = matched.Sum(m => m.CurrentValue);
            var limit = matched.Max(m => m.Limit);
            var projected = observed + requested;
            var fits = projected <= limit;

            perModelReport[modelName] = new Dictionary<string, object?>
            {
                ["observed"] = observed,
                ["limit"] = limit,
                ["requested"] = requested,
                ["projected_after"] = projected,
                ["fits"] = fits,
            };
            if (!fits)
            {
                failedModels.Add(modelName);
            }
        }

        var headroom = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["region"] = region,
            ["perModel"] = perModelReport,
        });

        if (failedModels.Count == 0)
        {
            return new PreflightCheckResult(
                CheckName: PreflightCheckNames.AzureOpenAiTpmHeadroom,
                Passed: true,
                Headroom: headroom,
                Diagnostic: $"OpenAI regional TPM headroom OK in '{region}' for all {requestedTpmPerModel.Count} model deployments.");
        }

        var lines = new List<string>
        {
            $"OpenAI regional TPM headroom INSUFFICIENT in region '{region}' for {failedModels.Count} model(s).",
        };
        foreach (var m in failedModels)
        {
            var p = (Dictionary<string, object?>)perModelReport[m]!;
            if (Equals(p["observed"], "not-reported"))
            {
                lines.Add(
                    $"  - Model '{m}': NOT REPORTED by Azure.ResourceManager.CognitiveServices usage for region '{region}' " +
                    $"(requested {p["requested"]} TPM). Verify model name matches Azure's naming (e.g. 'gpt-4o', not 'GPT-4') " +
                    "and that model is available in region. File quota-bump request if expected.");
            }
            else
            {
                var shortfall = (double)p["projected_after"]! - (double)p["limit"]!;
                lines.Add(
                    $"  - Model '{m}': observed usage {p["observed"]} + requested {p["requested"]} = projected {p["projected_after"]}, " +
                    $"regional quota = {p["limit"]}. SHORTFALL: {shortfall}. File quota-bump request per External Dependencies (1-3 day lead time).");
            }
        }

        return new PreflightCheckResult(
            CheckName: PreflightCheckNames.AzureOpenAiTpmHeadroom,
            Passed: false,
            Headroom: headroom,
            Diagnostic: string.Join("\n", lines));
    }

    private static PreflightCheckResult ConfigError(string diagnostic) => new(
        CheckName: PreflightCheckNames.AzureOpenAiTpmHeadroom,
        Passed: false,
        Headroom: JsonDocument.Parse("{}").RootElement.Clone(),
        Diagnostic: diagnostic);
}

/// <summary>
/// Flattened projection of <c>ServiceAccountUsage</c> (Name.Value /
/// CurrentValue / Limit) — the exact three fields the PS script's own
/// shape-verification block asserts are present. Decouples <see cref="ArmCognitiveServicesTpmProbe.Evaluate"/>
/// from the SDK model type (which has no public constructor) so unit tests
/// can build arbitrary boundary-case inputs directly.
/// </summary>
internal sealed record CognitiveServicesUsageEntry(string NameValue, double CurrentValue, double Limit);
