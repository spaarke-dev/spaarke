// -----------------------------------------------------------------------------
// ArmWhatIfDriftDetector.cs
//
// Production <see cref="IUpgradeDriftDetector"/> — task 123 (Wave G-2) SDK
// port of the retired <c>AzCliUpgradeDriftDetector</c> (shelled out to
// `az deployment sub what-if`). Calls
// <see cref="ArmDeploymentResource.WhatIfAsync(WaitUntil, Azure.ResourceManager.Resources.Models.ArmDeploymentWhatIfContent, CancellationToken)"/>
// against a not-yet-materialized subscription-scope deployment resource
// reference — the ARM SDK's public what-if surface is an INSTANCE method on
// <see cref="ArmDeploymentResource"/> (there is no standalone
// "WhatIfAtSubscriptionScopeAsync" public method in Azure.ResourceManager.Resources
// 1.11.2 — that REST operation name only exists as an INTERNAL
// DeploymentsRestOperations method the public SDK surface wraps; ground-
// truthed via reflection against the installed package per task 123 POML's
// effort directive, not guessed from the POML's title text).
//
// Returns typed <c>WhatIfOperationResult.Changes</c>
// (<c>IReadOnlyList&lt;WhatIfChange&gt;</c>) — a strict upgrade over the
// retired detector's stdout-parsed JSON, matching the POML's stated goal.
// -----------------------------------------------------------------------------

using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Runs an ARM what-if preview at subscription scope via
/// <see cref="ArmDeploymentResource.WhatIfAsync(WaitUntil, ArmDeploymentWhatIfContent, CancellationToken)"/>
/// and classifies the typed <see cref="WhatIfChange"/> results. Constructed
/// with an <see cref="ArmClient"/> + <see cref="BlobContainerClient"/> so
/// tests can inject both against a fake HTTP transport (parity with
/// <see cref="ArmDeploymentRunner"/>).
/// </summary>
public sealed class ArmWhatIfDriftDetector : IUpgradeDriftDetector
{
    private readonly ArmClient _armClient;
    private readonly BlobContainerClient _artifactsContainer;
    private readonly BicepInfraDeployOptions _options;
    private readonly ILogger<ArmWhatIfDriftDetector> _logger;

    /// <summary>Constructs the detector. Production DI reuses the shared UAMI-pinned ArmClient + artifacts-container factory pattern.</summary>
    public ArmWhatIfDriftDetector(
        ArmClient armClient,
        BlobContainerClient artifactsContainer,
        IOptions<BicepInfraDeployOptions> options,
        ILogger<ArmWhatIfDriftDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(artifactsContainer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _armClient = armClient;
        _artifactsContainer = artifactsContainer;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<UpgradeDriftDetectionResult> DetectDriftAsync(
        BicepDeployRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var templateJson = await ArmDeploymentRunner.ResolveArmTemplateJsonAsync(
                _artifactsContainer, _options, request.TenancyModel, cancellationToken)
            .ConfigureAwait(false);
        var parameters = ArmDeploymentRunner.BuildParametersPayload(request);

        var whatIfProperties = new ArmDeploymentWhatIfProperties(ArmDeploymentMode.Incremental)
        {
            Template = BinaryData.FromString(templateJson),
            Parameters = parameters,
            WhatIfResultFormat = WhatIfResultFormat.FullResourcePayloads,
        };
        var whatIfContent = new ArmDeploymentWhatIfContent(whatIfProperties);

        // Subscription-scope deployment resource reference. Does not need to
        // pre-exist — the ARM what-if REST operation previews the deployment
        // as if it were applied, whether or not a deployment by this name has
        // run before (parity with `az deployment sub what-if`, which likewise
        // does not require a prior deployment).
        var deploymentName = $"customer-{request.CustomerId}-whatif";
        var deploymentResourceId = ArmDeploymentResource.CreateResourceIdentifier(
            $"/subscriptions/{request.SubscriptionId}", deploymentName);
        var deploymentResource = _armClient.GetArmDeploymentResource(deploymentResourceId);

        // RequestFailedException is intentionally NOT caught here — it is a
        // genuine infra fault (per IUpgradeDriftDetector's contract: "infra
        // faults... throw so the handler can classify per §4C") and
        // H2aBicepInfraDeployHandler's own catch around this detector call
        // already logs with fuller context (runId + customerId); a
        // catch-log-rethrow here would just duplicate that log entry at a
        // lower information level (parity with ArmKeyVaultRefProbe, task 123).
        var operation = await deploymentResource
            .WhatIfAsync(WaitUntil.Completed, whatIfContent, cancellationToken)
            .ConfigureAwait(false);
        var result = operation.Value;

        var driftChanges = (result.Changes ?? Array.Empty<WhatIfChange>())
            .Where(IsNonBenignChange)
            .ToList();

        _logger.LogInformation(
            "ArmWhatIfDriftDetector: customerId={CustomerId} hasDrift={HasDrift} changeCount={ChangeCount}",
            request.CustomerId, driftChanges.Count > 0, result.Changes?.Count ?? 0);

        if (driftChanges.Count == 0)
        {
            return new UpgradeDriftDetectionResult.NoDrift();
        }

        var report = BuildDriftReport(request.CustomerId, result, driftChanges);
        return new UpgradeDriftDetectionResult.DriftDetected(report);
    }

    /// <summary>
    /// NoChange / Ignore / Create are benign (parity with the retired
    /// az-CLI detector's classification — Create on a "fresh" upgrade run
    /// means a legitimately-added template resource, not drift). Modify /
    /// Delete / Deploy / Unsupported all count as drift.
    /// </summary>
    private static bool IsNonBenignChange(WhatIfChange change)
        => change.ChangeType is WhatIfChangeType.Modify
            or WhatIfChangeType.Delete
            or WhatIfChangeType.Deploy
            or WhatIfChangeType.Unsupported;

    private static string BuildDriftReport(
        string customerId, WhatIfOperationResult result, IReadOnlyList<WhatIfChange> driftChanges)
    {
        var summary = driftChanges.Select(c => new
        {
            resourceId = c.ResourceId,
            changeType = c.ChangeType.ToString(),
            unsupportedReason = c.UnsupportedReason,
        });
        var payload = new
        {
            customerId,
            status = result.Status,
            driftChangeCount = driftChanges.Count,
            totalChangeCount = result.Changes?.Count ?? 0,
            driftChanges = summary,
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}
