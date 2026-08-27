// -----------------------------------------------------------------------------
// ArmDeploymentRunner.cs
//
// Production <see cref="IBicepDeployRunner"/> — task 123 (Wave G-2) SDK port
// of <c>ProvisionCustomerScriptBicepDeployRunner</c> (retired shell-out to
// the pwsh orchestrator script — see that file's retirement banner for the
// exact on-disk path). Deploys the CI-precompiled ARM JSON
// artifact task 117's workflow publishes via
// Azure.ResourceManager.Resources — NOT raw .bicep (the ARM SDK has no
// .bicep deploy primitive; bundling the bicep CLI into the L2 runtime was
// explicitly rejected per DS-1b §1 H2a row / Option D's zero-shell
// constraint).
//
// SCOPE (per DS-4 §2 H2a row / DS-1b §1 discount analysis — task 123 POML):
//   Ports ONLY the retired pwsh orchestrator's steps 1-3 (validate / RG-ensure /
//   bicep-deploy, script lines 318-500). Steps 4-10 (lines 501-1150,
//   including Step 4's Key Vault secret population) duplicate H4/H5/H6/H7/
//   H8's own handler logic and MUST NOT be re-ported here (CLAUDE.md §11 —
//   two sources of truth for the same provisioning step). DEVIATION FROM
//   POML PROSE (documented, not silent — CLAUDE.md §6.5 posture): the POML's
//   <prompt> paragraph also mentions "SecretClient for the 3 az keyvault
//   secret call sites" as in-scope for this collaborator. Code inspection of
//   the CURRENT retired-orchestrator script (grep `az keyvault`) found
//   exactly 3 call sites — at lines 556, 1158, and 1347 — and ALL THREE fall
//   within the lines-501-1150 "MUST NOT port" range (line 556 is inside Step
//   4's KV-secret-population loop, which is H4's exclusive territory per the
//   same POML's <constraints> block). Zero KV call sites exist within the
//   effective steps-1-3 scope (lines 318-500). The <constraints> section
//   (binding) is honored over the <prompt> prose (imprecise) — this runner
//   performs NO SecretClient / Key Vault writes. H4 (task 125) owns all KV
//   secret population, per H2a's own file-header rollback table and
//   Worker/Program.cs's H4 registration comment ("H4 also REUSES
//   IArmKeyVaultRefProbe from H2a... single source of truth for the T1 trap"
//   — H4 does NOT reuse a KV-writer from H2a, confirming H2a never owned KV
//   writes in the target-state DI graph).
//
// HISTORICAL — PRE-WAVE-G-2.5 STATE (2026-08-19 and earlier; resolved, kept
// for context only): at task 123 authoring time, infrastructure/bicep/
// customer.bicep (the template FileBicepTemplateInspector.ResolveTemplatePath
// selects for the Model2Dedicated branch) deployed ONLY Key Vault + Storage +
// Service Bus + Cosmos + membership-topic + optional ACS + optional SignalR.
// It did NOT deploy a UAMI, App Service, or Azure OpenAI resource —
// `userAssignedIdentityResourceId` was a pass-through parameter with no
// resource binding it. BicepDeployOutputs could not be populated for
// UserAssignedIdentityObjectId/ClientId, AppServiceName,
// AppServiceStagingSlotName, or OpenAiEndpoint from a real deploy. This
// runner mapped exactly what the template output at the time (leaving the
// rest as empty string) so H2aBicepInfraDeployHandler's AreOutputsComplete()
// check correctly reported BicepDeployOutputsIncomplete rather than silently
// fabricating values.
//
// CURRENT STATE (as of Batch 1 + tasks 127/128/128b/129, Wave G-2.5):
// customer.bicep now deploys `module uami` (modules/uami.bicep), `module
// bffApi` / `bffApiSlot` (modules/app-service.bicep), and `module openAi`
// (modules/openai.bicep) — see customer.bicep lines ~203, ~340, ~532 — and
// exposes `userAssignedIdentityResourceId`, `openAiEndpoint`, and the App
// Service outputs this runner consumes. Azure AI Search is deployed via the
// separate H2b handler path, not customer.bicep, so no AiSearchEndpoint gap
// remains here. If AreOutputsComplete() reports BicepDeployOutputsIncomplete
// today, treat it as a real signal (template drift or a genuinely partial
// deploy) — not as this historical gap resurfacing.
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
/// Deploys the customer Azure stamp via
/// <see cref="ArmDeploymentCollection.CreateOrUpdateAsync(WaitUntil, string, ArmDeploymentContent, CancellationToken)"/>
/// against the CI-precompiled ARM JSON artifact task 117 publishes. Ensures
/// the target resource group first via
/// <see cref="ResourceGroupCollection.CreateOrUpdateAsync(WaitUntil, string, ResourceGroupData, CancellationToken)"/>.
/// </summary>
public sealed class ArmDeploymentRunner : IBicepDeployRunner
{
    private readonly ArmClient _armClient;
    private readonly BlobContainerClient _artifactsContainer;
    private readonly BicepInfraDeployOptions _options;
    private readonly ILogger<ArmDeploymentRunner> _logger;

    /// <summary>
    /// Constructs the runner. Production DI (Worker/Program.cs) builds
    /// <paramref name="armClient"/> from the shared UAMI-pinned
    /// <c>TokenCredential</c> singleton (ADR-028 MI-outbound; parity with
    /// <c>ArmSubscriptionReadinessProbe</c> task 121) and
    /// <paramref name="artifactsContainer"/> against the same credential —
    /// no second credential chain, no shared <c>ArmClient</c>/<c>BlobServiceClient</c>
    /// DI singleton registration (keeps this self-contained against sibling
    /// Wave-G-2 handler ports per the H1 precedent comment in Worker/Program.cs).
    /// </summary>
    public ArmDeploymentRunner(
        ArmClient armClient,
        BlobContainerClient artifactsContainer,
        IOptions<BicepInfraDeployOptions> options,
        ILogger<ArmDeploymentRunner> logger)
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
    public async Task<BicepDeployOutcome> DeployAsync(
        BicepDeployRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (1) Resolve the versioned ARM JSON artifact via the manifest task
        //     117's workflow publishes. A missing/misconfigured artifact is
        //     an infra-configuration fault (not a per-customer domain
        //     failure) — this MAY throw per IBicepDeployRunner's contract;
        //     the handler's existing try/catch around DeployAsync classifies
        //     it QuarantineRequired.
        var templateJson = await ResolveArmTemplateJsonAsync(
                _artifactsContainer, _options, request.TenancyModel, cancellationToken)
            .ConfigureAwait(false);

        // (2) RG-ensure. Idempotent — Azure treats an existing RG with
        //     matching location as a no-op update. Naming matches
        //     customer.bicep's own `resourceGroupName` variable convention
        //     (customer.bicep also declares the RG as an embedded resource —
        //     this pre-step is a fast-fail permissions/naming check before
        //     the ~10-20 min deploy starts, per POML step 1).
        var subscriptionResource = _armClient.GetSubscriptionResource(
            SubscriptionResource.CreateResourceIdentifier(request.SubscriptionId));
        var resourceGroupName = $"rg-spaarke-{request.CustomerId}-{request.EnvironmentName}";

        try
        {
            await subscriptionResource.GetResourceGroups()
                .CreateOrUpdateAsync(
                    WaitUntil.Completed,
                    resourceGroupName,
                    new ResourceGroupData(new AzureLocation(request.Location)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "ArmDeploymentRunner: resource-group ensure failed for '{ResourceGroupName}' " +
                "(status={Status} errorCode={ErrorCode})",
                resourceGroupName, ex.Status, ex.ErrorCode);
            return new BicepDeployOutcome.Failure(
                $"Resource-group ensure failed for '{resourceGroupName}' (HTTP {ex.Status}, " +
                $"{ex.ErrorCode ?? "no-error-code"}): {ex.Message}. Remediation: verify the L2 " +
                "control-plane UAMI has Contributor RBAC at the customer subscription scope.");
        }

        // (3) Deploy the ARM JSON. Incremental mode — parity with `az
        //     deployment sub create`'s default mode (the retired script
        //     never passed --mode Complete).
        var parameters = BuildParametersPayload(request);
        var properties = new ArmDeploymentProperties(ArmDeploymentMode.Incremental)
        {
            Template = BinaryData.FromString(templateJson),
            Parameters = parameters,
        };
        var content = new ArmDeploymentContent(properties);
        var deploymentName = $"customer-{request.CustomerId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        ArmOperation<ArmDeploymentResource> deployOperation;
        try
        {
            _logger.LogInformation(
                "ArmDeploymentRunner: starting deployment '{DeploymentName}' customerId={CustomerId} " +
                "tenancyModel={TenancyModel} bicepVer={BicepVersion}",
                deploymentName, request.CustomerId, request.TenancyModel, request.BicepVersion);

            // HANDLER-06 (Wave 2 pre-dispatch remediation 2026-08-27) — F11:
            // wrap the deploy call in RetryOnCogSvcRequestConflictAsync so a
            // transient CogSvc soft-lock (RequestConflict) does not immediately
            // fail the whole 20 min deploy. Retries 3 times with [30s, 90s, 180s]
            // backoffs (default schedule); on exhaustion returns Failure with
            // the CogSvc-soft-lock-persistent diagnostic prefix so H2a maps to
            // Resumable + CogSvcSoftLockPersistent (not Quarantine).
            deployOperation = await RetryOnCogSvcRequestConflictAsync(
                (attempt, ct) => subscriptionResource.GetArmDeployments()
                    .CreateOrUpdateAsync(WaitUntil.Completed, deploymentName, content, ct),
                DefaultCogSvcRetryBackoffs,
                Task.Delay,
                _logger,
                cancellationToken).ConfigureAwait(false);
        }
        catch (CogSvcSoftLockPersistentException softLock)
        {
            _logger.LogWarning(
                "ArmDeploymentRunner: CogSvc soft-lock persisted after {Attempts} attempts on deployment " +
                "'{DeploymentName}' customerId={CustomerId}: {Message}",
                softLock.AttemptsMade, deploymentName, request.CustomerId, softLock.Message);
            return new BicepDeployOutcome.Failure(
                CogSvcSoftLockDiagnosticPrefix + $" ARM deployment '{deploymentName}' for customerId " +
                $"'{request.CustomerId}' returned HTTP 409 RequestConflict on the Cognitive Services scope " +
                $"after {softLock.AttemptsMade} attempts across the [30s, 90s, 180s] backoff schedule. " +
                $"Last message: {softLock.Message}. The soft-lock did not clear within the retry window — " +
                "operator escalation required (retry later once the concurrent CogSvc operation completes).");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "ArmDeploymentRunner: deployment '{DeploymentName}' failed for customerId={CustomerId} " +
                "(status={Status} errorCode={ErrorCode})",
                deploymentName, request.CustomerId, ex.Status, ex.ErrorCode);
            return new BicepDeployOutcome.Failure(
                $"ARM deployment '{deploymentName}' failed for customerId '{request.CustomerId}' " +
                $"(HTTP {ex.Status}, {ex.ErrorCode ?? "no-error-code"}): {ex.Message}. Partial resource " +
                "state may exist — the handler classifies this Quarantine-required per §4C.");
        }

        var outputsJson = deployOperation.Value.Data.Properties.Outputs;
        var outputs = MapOutputs(outputsJson, resourceGroupName);

        _logger.LogInformation(
            "ArmDeploymentRunner: deployment '{DeploymentName}' succeeded for customerId={CustomerId}",
            deploymentName, request.CustomerId);

        return new BicepDeployOutcome.Success(outputs);
    }

    /// <summary>
    /// HANDLER-06 diagnostic prefix. H2aBicepInfraDeployHandler pattern-
    /// matches on this to route CogSvc-soft-lock failures to
    /// <see cref="BicepDeployRejectionCodes.CogSvcSoftLockPersistent"/> +
    /// <see cref="Handlers.FailureClass.Resumable"/> instead of the default
    /// <see cref="BicepDeployRejectionCodes.BicepDeployFailed"/> +
    /// <see cref="Handlers.FailureClass.QuarantineRequired"/>.
    /// </summary>
    internal const string CogSvcSoftLockDiagnosticPrefix = "CogSvc-soft-lock-persistent:";

    /// <summary>
    /// F11 verbatim retry schedule per punchlist: 3 retries with
    /// [30s, 90s, 180s] backoffs (initial attempt + 3 retries = 4 total
    /// attempts). Exposed <c>internal</c> so unit tests share the exact
    /// schedule and validate exhaustion behavior at attempt 4.
    /// </summary>
    internal static readonly IReadOnlyList<TimeSpan> DefaultCogSvcRetryBackoffs = new[]
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromSeconds(180),
    };

    /// <summary>
    /// HANDLER-06 (Wave 2 pre-dispatch remediation 2026-08-27) — F11 verbatim.
    /// Wraps a deploy invocation with retry-on-<c>RequestConflict</c>
    /// (CogSvc soft-lock) semantics: retries up to <c>backoffs.Count</c>
    /// times, waiting the corresponding backoff duration between attempts.
    /// Non-<c>RequestConflict</c> failures propagate immediately. After
    /// exhaustion, throws <see cref="CogSvcSoftLockPersistentException"/>
    /// carrying the last-attempt error message + total attempts made.
    ///
    /// The delay is injected as <paramref name="delay"/> so unit tests can
    /// substitute a no-op / capture without waiting real wall-clock time.
    /// </summary>
    internal static async Task<T> RetryOnCogSvcRequestConflictAsync<T>(
        Func<int, CancellationToken, Task<T>> action,
        IReadOnlyList<TimeSpan> backoffs,
        Func<TimeSpan, CancellationToken, Task> delay,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(backoffs);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(logger);

        var maxAttempts = 1 + backoffs.Count; // initial + N retries
        RequestFailedException? lastConflict = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (IsCogSvcRequestConflict(ex))
            {
                lastConflict = ex;
                if (attempt == maxAttempts)
                {
                    // Exhausted — propagate the specific typed exception.
                    break;
                }
                var backoff = backoffs[attempt - 1];
                logger.LogWarning(
                    ex,
                    "ArmDeploymentRunner: CogSvc RequestConflict (attempt {Attempt}/{Max}) — waiting {BackoffSeconds}s before retry (errorCode={ErrorCode})",
                    attempt, maxAttempts, (int)backoff.TotalSeconds, ex.ErrorCode);
                await delay(backoff, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new CogSvcSoftLockPersistentException(
            attemptsMade: maxAttempts,
            message: lastConflict?.Message ?? "unknown",
            innerException: lastConflict);
    }

    /// <summary>
    /// HANDLER-06: detects the CogSvc soft-lock signature —
    /// <c>HTTP 409</c> with error code containing "RequestConflict".
    /// Exposed <c>internal</c> for direct test coverage of the boundary rule.
    /// </summary>
    internal static bool IsCogSvcRequestConflict(RequestFailedException ex)
        => ex.Status == 409
        && !string.IsNullOrEmpty(ex.ErrorCode)
        && ex.ErrorCode.Contains("RequestConflict", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Downloads <c>ArmManifestBlobName</c> (the mutable "latest" pointer),
    /// resolves the tenancy-model-appropriate ARM JSON blob name, then
    /// downloads + returns that blob's content. <c>internal static</c> (not
    /// an instance member) so <see cref="ArmWhatIfDriftDetector"/> (task 123,
    /// same collaborator family) reuses the identical artifact-resolution
    /// logic without depending on an <see cref="ArmDeploymentRunner"/>
    /// instance (CLAUDE.md §11 — extend/share, don't duplicate the manifest
    /// + blob-download plumbing across both collaborators).
    /// </summary>
    internal static async Task<string> ResolveArmTemplateJsonAsync(
        BlobContainerClient artifactsContainer,
        BicepInfraDeployOptions options,
        string tenancyModel,
        CancellationToken cancellationToken)
    {
        var manifestBlob = artifactsContainer.GetBlobClient(options.ArmManifestBlobName);
        var manifestResponse = await manifestBlob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        var manifestJson = manifestResponse.Value.Content.ToString();

        using var manifestDoc = JsonDocument.Parse(manifestJson);
        var templateKey = string.Equals(tenancyModel, "Model1Shared", StringComparison.OrdinalIgnoreCase)
            ? "model1-shared"
            : "customer";

        if (!manifestDoc.RootElement.TryGetProperty("templates", out var templates)
            || !templates.TryGetProperty(templateKey, out var templateEntry)
            || !templateEntry.TryGetProperty("armJsonBlobName", out var blobNameElement)
            || blobNameElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Manifest blob '{options.ArmManifestBlobName}' is missing templates.{templateKey}.armJsonBlobName. " +
                "Verify task 117's publish-provisioning-arm-artifacts.yml workflow published a well-formed manifest.");
        }

        var armJsonBlobName = blobNameElement.GetString()!;
        var templateBlob = artifactsContainer.GetBlobClient(armJsonBlobName);
        var templateResponse = await templateBlob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        return templateResponse.Value.Content.ToString();
    }

    /// <summary>
    /// Builds the ARM deployment parameters payload
    /// (<c>{ "paramName": { "value": ... } }</c> shape). Only parameters this
    /// handler's effective steps-1-3 scope owns — customer.bicep's
    /// remaining parameters (platformKeyVaultName, storageSku,
    /// keyVaultSku, ...) keep their Bicep-declared defaults; adding them here
    /// would be scope creep beyond the run parameters
    /// <see cref="BicepDeployRequest"/> actually carries (CLAUDE.md §11).
    /// <c>internal</c> so <see cref="ArmWhatIfDriftDetector"/> builds an
    /// IDENTICAL parameters payload for its preview call (the what-if MUST
    /// compare against the same inputs the real deploy would use).
    /// </summary>
    internal static BinaryData BuildParametersPayload(BicepDeployRequest request)
    {
        var payload = new Dictionary<string, object>
        {
            ["customerId"] = new { value = request.CustomerId },
            ["environmentName"] = new { value = request.EnvironmentName },
            ["location"] = new { value = request.Location },
            ["signalrEnabled"] = new { value = request.SignalREnabled },
            ["requireSecretFreeIdentity"] = new { value = request.RequireSecretFreeIdentity },
        };
        return BinaryData.FromObjectAsJson(payload);
    }

    /// <summary>
    /// Maps the ARM deployment's raw <c>outputs</c> BinaryData (ARM output
    /// shape: <c>{ "key": { "type": "...", "value": ... } }</c>) onto
    /// <see cref="BicepDeployOutputs"/>. Fields customer.bicep does not
    /// currently produce (see file-header "BLOCKING DISCOVERY" note) map to
    /// <see cref="string.Empty"/> — <see cref="H2aBicepInfraDeployHandler.AreOutputsComplete"/>
    /// already treats blank as incomplete, so this is an honest signal, not
    /// a fabricated value.
    /// </summary>
    private static BicepDeployOutputs MapOutputs(BinaryData? outputsJson, string fallbackResourceGroupName)
    {
        string ReadString(JsonElement root, string key)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(key, out var entry)
                && entry.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
            return string.Empty;
        }

        bool ReadBool(JsonElement root, string key)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(key, out var entry)
                && entry.TryGetProperty("value", out var value)
                && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                return value.GetBoolean();
            }
            return false;
        }

        var root = default(JsonElement);
        var hasRoot = false;
        if (outputsJson is not null)
        {
            using var doc = JsonDocument.Parse(outputsJson);
            root = doc.RootElement.Clone();
            hasRoot = true;
        }

        var rg = hasRoot ? ReadString(root, "resourceGroupName") : string.Empty;

        return new BicepDeployOutputs
        {
            ResourceGroupName = string.IsNullOrWhiteSpace(rg) ? fallbackResourceGroupName : rg,
            UserAssignedIdentityResourceId = hasRoot ? ReadString(root, "userAssignedIdentityResourceId") : string.Empty,
            UserAssignedIdentityObjectId = hasRoot ? ReadString(root, "userAssignedIdentityObjectId") : string.Empty,
            UserAssignedIdentityClientId = hasRoot ? ReadString(root, "userAssignedIdentityClientId") : string.Empty,
            AppServiceName = hasRoot ? ReadString(root, "appServiceName") : string.Empty,
            AppServiceStagingSlotName = hasRoot ? ReadString(root, "appServiceStagingSlotName") : string.Empty,
            OpenAiEndpoint = hasRoot ? ReadString(root, "openAiEndpoint") : string.Empty,
            AiSearchEndpoint = hasRoot ? ReadString(root, "aiSearchEndpoint") : string.Empty,
            CosmosEndpoint = hasRoot ? ReadString(root, "cosmosAccountEndpoint") : string.Empty,
            SignalRDeployed = hasRoot && ReadBool(root, "signalrEnabled"),
        };
    }
}

/// <summary>
/// HANDLER-06 (Wave 2 pre-dispatch remediation 2026-08-27) — F11 verbatim.
/// Thrown by <see cref="ArmDeploymentRunner.RetryOnCogSvcRequestConflictAsync{T}"/>
/// after the retry budget for a CogSvc soft-lock (HTTP 409 RequestConflict)
/// is exhausted. <see cref="ArmDeploymentRunner.DeployAsync"/> catches this
/// and returns a <see cref="BicepDeployOutcome.Failure"/> whose diagnostic
/// begins with <see cref="ArmDeploymentRunner.CogSvcSoftLockDiagnosticPrefix"/>
/// so H2aBicepInfraDeployHandler maps it to a Resumable
/// <c>cogsvc-soft-lock-persistent</c> rejection code (not the default
/// Quarantine-required <c>bicep-deploy-failed</c>).
/// </summary>
internal sealed class CogSvcSoftLockPersistentException : Exception
{
    public int AttemptsMade { get; }

    public CogSvcSoftLockPersistentException(int attemptsMade, string message, Exception? innerException)
        : base(message, innerException)
    {
        AttemptsMade = attemptsMade;
    }
}
