// -----------------------------------------------------------------------------
// ArmOpenAiDeploymentSetRecomposer.cs
//
// HANDLER-13 (Wave 2 pre-dispatch remediation 2026-08-27) — F5 verbatim.
// LIVE production <see cref="IOpenAiDeploymentSetRecomposer"/>. Queries the
// Azure.ResourceManager.CognitiveServices per-region usage endpoint via
// <see cref="SubscriptionResource.GetUsagesAsync"/>, evaluates the
// auto-granted TPM per pinned model, and DROPS zero-TPM models from the
// requested deployment set with an operator-visible note.
//
// SUPERSEDES: Wave-2 scaffold that returned the full set unchanged with a
// log line. That scaffold shipped in commit 74197c02e (2026-08-27 punchlist
// HANDLER-13). This file is the follow-on live implementation the scaffold
// commit's "Wave 2 scope + deviation" paragraph explicitly anticipated.
//
// EVALUATION LOGIC — parity with <see cref="Preflight.ArmCognitiveServicesTpmProbe.Evaluate"/>:
//   - <c>name.value</c> matched against <c>(?:^|[.\-/_ ])modelName$</c>
//     (case-insensitive, anchored end) so a request for <c>gpt-4o</c> does not
//     also match <c>gpt-4o-mini</c>.
//   - Model NOT reported in usage → treated as zero auto-granted quota →
//     dropped.
//   - Model reported with Limit &lt;= 0 → auto-granted TPM is zero → dropped.
//   - Model reported with Limit &gt; 0 → preserved (the run-time deploy will
//     still fail if <c>current + requested &gt; limit</c>, but that is H0's
//     pre-flight boundary — the recomposer's role per F5 is strictly to drop
//     the frontier tiers that fresh subs never auto-grant at all).
//
// AUTH: reuses the shared platform <see cref="ArmClient"/> singleton
// registered in Worker/Program.cs from the CosmosModule <see cref="TokenCredential"/>
// (UAMI-pinned per ADR-028 MI-outbound) — no second credential chain, parity
// with <see cref="Preflight.ArmCognitiveServicesTpmProbe"/> +
// <see cref="ArmResourceNameAvailabilityProbe"/>.
//
// TEST BOUNDARY (ADR-038 path #1): unit tests build a REAL ArmClient against
// a hand-rolled fake <see cref="System.Net.Http.HttpMessageHandler"/> wrapped
// in <see cref="Azure.Core.Pipeline.HttpClientTransport"/> via the shared
// <c>ArmSdkTestFakes</c> helper, so the SDK's own URL construction + STJ
// deserialization run unmodified — only the HTTP socket is faked. This is
// NOT the banned <c>Mock&lt;HttpMessageHandler&gt;</c> pattern; it is the
// same pattern <see cref="Preflight.ArmCognitiveServicesTpmProbe"/> uses.
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.CognitiveServices;
using Azure.ResourceManager.Resources;
using Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// LIVE production <see cref="IOpenAiDeploymentSetRecomposer"/> — reads
/// Azure.ResourceManager.CognitiveServices regional usage + drops zero-TPM
/// pinned models from the requested deploy set.
/// </summary>
public sealed class ArmOpenAiDeploymentSetRecomposer : IOpenAiDeploymentSetRecomposer
{
    private readonly ArmClient _armClient;
    private readonly ILogger<ArmOpenAiDeploymentSetRecomposer> _logger;

    /// <summary>
    /// Constructs the recomposer. In production <paramref name="armClient"/>
    /// is the shared platform ArmClient built from the CosmosModule
    /// <see cref="TokenCredential"/> singleton (Worker/Program.cs). Tests
    /// inject one built against the shared <c>ArmSdkTestFakes</c> fake
    /// transport helper.
    /// </summary>
    public ArmOpenAiDeploymentSetRecomposer(
        ArmClient armClient,
        ILogger<ArmOpenAiDeploymentSetRecomposer> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<OpenAiDeploymentSetRecomposeResult> RecomposeAsync(
        OpenAiDeploymentSetRecomposeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Region);

        _logger.LogInformation(
            "HANDLER-13 querying Azure.ResourceManager.CognitiveServices usage: " +
            "region={Region} subscriptionId={SubscriptionId} pinnedModels={Count}",
            request.Region, request.SubscriptionId, request.FullPinnedSet.Count);

        var subscription = _armClient.GetSubscriptionResource(
            SubscriptionResource.CreateResourceIdentifier(request.SubscriptionId));
        var location = new AzureLocation(request.Region);

        var usage = new List<OpenAiRegionalUsageEntry>();
        await foreach (var u in subscription
            .GetUsagesAsync(location, filter: null, cancellationToken)
            .ConfigureAwait(false))
        {
            usage.Add(new OpenAiRegionalUsageEntry(
                NameValue: u.Name?.Value ?? string.Empty,
                Limit: u.Limit ?? 0));
        }

        return Evaluate(request.Region, request.FullPinnedSet, usage);
    }

    /// <summary>
    /// Pure filter logic — exposed <c>internal</c> so unit tests exercise the
    /// exact evaluation function the production path calls, without needing to
    /// round-trip through the ARM SDK for boundary-case coverage.
    /// </summary>
    internal static OpenAiDeploymentSetRecomposeResult Evaluate(
        string region,
        IReadOnlyList<PinnedModel> fullPinnedSet,
        IReadOnlyList<OpenAiRegionalUsageEntry> usage)
    {
        var preserved = new List<PinnedModel>();
        var dropped = new List<string>();
        var dropReasons = new List<string>();

        foreach (var model in fullPinnedSet)
        {
            // Anchored-suffix match, case-insensitive — parity with
            // ArmCognitiveServicesTpmProbe.Evaluate (H0 preflight sibling)
            // so 'gpt-4o' does not conflate with 'gpt-4o-mini'.
            var pattern = $@"(?:^|[.\-/_ ]){Regex.Escape(model.ModelId)}$";
            var matched = usage
                .Where(u => Regex.IsMatch(u.NameValue, pattern, RegexOptions.IgnoreCase))
                .ToList();

            if (matched.Count == 0)
            {
                dropped.Add(model.ModelId);
                dropReasons.Add(
                    $"'{model.ModelId}' NOT REPORTED by Azure.ResourceManager.CognitiveServices " +
                    $"usage in region '{region}' (no auto-granted quota)");
                continue;
            }

            var maxLimit = matched.Max(m => m.Limit);
            if (maxLimit <= 0)
            {
                dropped.Add(model.ModelId);
                dropReasons.Add(
                    $"'{model.ModelId}' auto-granted TPM = 0 in region '{region}' " +
                    $"(reported by ARM usage endpoint but Limit=0)");
                continue;
            }

            preserved.Add(model);
        }

        if (dropped.Count == 0)
        {
            return new OpenAiDeploymentSetRecomposeResult(
                PreservedSet: preserved,
                DroppedModelIds: Array.Empty<string>(),
                OperatorNote: string.Empty);
        }

        var note =
            $"HANDLER-13 (F5) OpenAI deployment-set auto-recomposed for region '{region}': " +
            $"dropped {dropped.Count} of {fullPinnedSet.Count} pinned model(s) with zero " +
            $"auto-granted TPM on this subscription. File a Microsoft support ticket to grant " +
            $"frontier-tier TPM in this region and re-run with " +
            $"OpenAiDeploymentSetPolicy=Strict once quota is provisioned. " +
            $"Details: {string.Join("; ", dropReasons)}.";

        return new OpenAiDeploymentSetRecomposeResult(
            PreservedSet: preserved,
            DroppedModelIds: dropped,
            OperatorNote: note);
    }
}

/// <summary>
/// Flattened projection of <c>ServiceAccountUsage</c> (Name.Value + Limit) —
/// the only two fields the recomposer needs to decide keep-vs-drop. Kept
/// separate from <see cref="Preflight.ArmCognitiveServicesTpmProbe"/>'s
/// <c>CognitiveServicesUsageEntry</c> so each seam owns a self-contained
/// evaluation type (parity refactor would tangle their independent
/// evolution).
/// </summary>
internal sealed record OpenAiRegionalUsageEntry(string NameValue, double Limit);
