// -----------------------------------------------------------------------------
// SpeOwnerCredentialProbe.cs
//
// H0's SPE readiness check (task 248, G28 — owner decision D16). REPLACES
// KeyVaultCertBootstrapProbe, which checked that a base64-PFX owning-app
// certificate existed in a Spaarke platform Key Vault and was at least 24 h old.
// No such certificate ever existed, so that check rejected every run; and L2 no
// longer uses a certificate at all — it signs in as the owning app with the
// Worker UAMI's federated identity credential (SpeConfidentialClientGraphFactory).
//
// WHAT IT CHECKS — exactly what H8 will need, in the order H8 needs it:
//   (a) the run's containerTypeId has a SpeContainerOptions:ContainerTypeOwners
//       entry (the owning app L2 acts as)          → else spe-owner-not-configured
//   (b) L2 can obtain an owning-app token through the federated credential, and
//       Graph accepts it                            → else spe-owner-token-failed
//   (c) the container type is registered in the tenant: app-only
//       GET /v1.0/storage/fileStorage/containerTypeRegistrations/{id}
//       (FileStorageContainerTypeReg.Selected)     → 404: spe-container-type-not-registered
// All three are Resumable (H0 classifies every preflight failure Resumable): the
// operator fixes the Worker configuration / the owning app's FIC or consent /
// registers the container type, then resumes. No time gate — the 24 h "cert
// replication" wait is retired with the certificate. Unexpected faults (5xx,
// transport, timeout) THROW and surface as H0's probe-infrastructure-error.
//
// TEST BOUNDARY (ADR-038): the Graph call runs through the real Graph SDK
// against a fake HttpMessageHandler, and the credential is a fake TokenCredential
// — both injected through SpeConfidentialClientGraphFactory's internal seam.
// -----------------------------------------------------------------------------

using System.Text.Json;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// H0 probe: the run's SPE container type has an owner entry, L2 can sign in as that owning app (Worker UAMI
/// federated credential) and the container type is registered in the tenant. Three distinct rejection codes.
/// </summary>
public sealed class SpeOwnerCredentialProbe : IPreflightQuotaProbe
{
    /// <summary>No owner entry can be selected: <c>containerTypeId</c> missing, or not in <c>ContainerTypeOwners</c>.</summary>
    public const string OwnerNotConfiguredRejectionCode = "spe-owner-not-configured";

    /// <summary>The owning-app token could not be obtained through the Worker UAMI's federated credential, or Graph refused it.</summary>
    public const string OwnerTokenFailedRejectionCode = "spe-owner-token-failed";

    /// <summary>The container type is not registered in the run's tenant (registration GET returned 404).</summary>
    public const string ContainerTypeNotRegisteredRejectionCode = "spe-container-type-not-registered";

    private readonly SpeConfidentialClientGraphFactory _graphFactory;
    private readonly SpeContainerOptions _speOptions;
    private readonly ILogger<SpeOwnerCredentialProbe> _logger;

    /// <inheritdoc/>
    public string CheckName => PreflightCheckNames.SpeOwnerCredential;

    /// <summary>Constructs the probe over the owning-app Graph client factory and the SPE owner configuration.</summary>
    public SpeOwnerCredentialProbe(
        SpeConfidentialClientGraphFactory graphFactory,
        IOptions<SpeContainerOptions> speOptions,
        ILogger<SpeOwnerCredentialProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(graphFactory);
        ArgumentNullException.ThrowIfNull(speOptions);
        ArgumentNullException.ThrowIfNull(logger);
        _graphFactory = graphFactory;
        _speOptions = speOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PreflightCheckResult> CheckAsync(PreflightProbeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        // (a) The owning app L2 will act as.
        input.NonSecretParameters.TryGetValue(IntakeParameterCatalog.ContainerTypeId, out var containerTypeId);
        if (string.IsNullOrWhiteSpace(containerTypeId))
        {
            return Fail(OwnerNotConfiguredRejectionCode, containerTypeId: null, ownerAppId: null,
                $"Run parameter '{IntakeParameterCatalog.ContainerTypeId}' is required by {CheckName} — it selects the SPE " +
                "container type H8 creates the customer's container in (spaarke-constants.yaml per_env_constants).");
        }
        if (!_speOptions.TryGetOwner(containerTypeId, out var owner))
        {
            return Fail(OwnerNotConfiguredRejectionCode, containerTypeId.Trim(), ownerAppId: null,
                $"No SpeContainerOptions:ContainerTypeOwners entry for container type '{containerTypeId.Trim()}' — this L2 " +
                "deployment does not know the container type's owning app, so H8 could not create the customer's container. " +
                "Add {containerTypeId, ownerAppId} to the Worker (controlplane-worker-app-service.bicep speContainerTypeOwners) " +
                "after the topology runbook (SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md) has created the container type and its " +
                "owning app, then resume.");
        }

        _logger.LogInformation(
            "{CheckName}: containerType={ContainerType} ownerApp={OwnerApp} tenant={TenantId} — registration GET as the owning app",
            CheckName, owner.ContainerTypeId, owner.OwnerAppId, input.TenantId);

        // (b) + (c) One app-only call as the owning app: acquiring its token exercises the federated
        // credential; the registration GET proves Graph accepts the token AND the type is registered.
        using var graph = _graphFactory.CreateGraphClient(input.TenantId, owner.OwnerAppId);
        FileStorageContainerTypeRegistration? registration;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_speOptions.GraphRequestTimeout);
            registration = await graph.Storage.FileStorage.ContainerTypeRegistrations[owner.ContainerTypeId]
                .GetAsync(cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (AuthenticationFailedException ex)
        {
            return Fail(OwnerTokenFailedRejectionCode, owner.ContainerTypeId, owner.OwnerAppId,
                $"L2 could not obtain a token as the SPE owning app '{owner.OwnerAppId}' in tenant '{input.TenantId}' through " +
                $"the Worker managed identity's federated credential: {ex.Message} — check that the owning app has a federated " +
                "identity credential whose subject is the Worker UAMI's principal id (issuer = the UAMI's own tenant, " +
                "https://login.microsoftonline.com/{uami-tenant-id}/v2.0; audience api://AzureADTokenExchange) and that " +
                "ManagedIdentity__ClientId on the Worker names that UAMI, then resume. A transient Entra or managed-identity " +
                "fault surfaces the same way — if the configuration is right, resume to retry.");
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 401 or 403)
        {
            return Fail(OwnerTokenFailedRejectionCode, owner.ContainerTypeId, owner.OwnerAppId,
                $"Graph refused the SPE owning app '{owner.OwnerAppId}' token (HTTP {ex.ResponseStatusCode} " +
                $"{ex.Error?.Code} {ex.Error?.Message ?? ex.Message}) on the container-type registration GET — check that the " +
                "owning app has the Application permission FileStorageContainerTypeReg.Selected with admin consent and that " +
                $"it owns container type '{owner.ContainerTypeId}', then resume.");
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            return Fail(ContainerTypeNotRegisteredRejectionCode, owner.ContainerTypeId, owner.OwnerAppId,
                $"SPE container type '{owner.ContainerTypeId}' is not registered in tenant '{input.TenantId}' (registration " +
                $"GET returned 404: {ex.Error?.Code} {ex.Error?.Message ?? ex.Message}). Register it as the owning app — Graph " +
                "v1.0 PUT /storage/fileStorage/containerTypeRegistrations/{id} (SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md) — then resume.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{CheckName}: the container-type registration GET exceeded {_speOptions.GraphRequestTimeout}.");
        }

        if (registration is null)
        {
            return Fail(ContainerTypeNotRegisteredRejectionCode, owner.ContainerTypeId, owner.OwnerAppId,
                $"The registration GET for SPE container type '{owner.ContainerTypeId}' returned no registration. Register it " +
                "as the owning app (SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md), then resume.");
        }

        var headroom = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["containerTypeId"] = owner.ContainerTypeId,
            ["ownerAppId"] = owner.OwnerAppId,
            ["registered"] = true,
            ["billingClassification"] = registration.BillingClassification?.ToString(),
            ["billingStatus"] = registration.BillingStatus?.ToString(),
        });
        return new PreflightCheckResult(
            CheckName: CheckName,
            Passed: true,
            Headroom: headroom,
            Diagnostic:
                $"SPE owner OK: signed in as owning app '{owner.OwnerAppId}' through the Worker UAMI's federated credential; " +
                $"container type '{owner.ContainerTypeId}' is registered in tenant '{input.TenantId}'.");
    }

    private PreflightCheckResult Fail(string rejectionCode, string? containerTypeId, string? ownerAppId, string diagnostic)
    {
        var headroom = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["containerTypeId"] = containerTypeId,
            ["ownerAppId"] = ownerAppId,
        });
        return new PreflightCheckResult(CheckName, Passed: false, headroom, diagnostic, rejectionCode);
    }
}
