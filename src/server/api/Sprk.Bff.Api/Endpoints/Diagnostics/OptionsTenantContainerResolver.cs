using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Endpoints.Diagnostics;

/// <summary>
/// <see cref="ITenantContainerResolver"/> backed by the deployment's boot-time-bound
/// IOptions bags (<c>resolverSource = "options"</c>):
///
/// <list type="bullet">
/// <item><b>Tenant scope</b> — <see cref="GraphOptions.TenantId"/> (<c>Graph:TenantId</c>).
/// Each customer BFF stamp is single-tenant (dedicated Model-2 stamp or the shared-trial
/// stamp's own tenant); this is the tenant the app's Graph SDK operates in — i.e., the
/// exact scope the production upload path uses.</item>
/// <item><b>Container id</b> — <see cref="SharePointEmbeddedOptions.StagingContainerId"/>
/// (<c>SharePointEmbedded:StagingContainerId</c>): the deployment-level SPE container bound
/// at boot from App Service settings / KV references. Provisioning (H8) writes the customer's
/// root container id to KV + the Dataverse env-var <c>sprk_SharePointEmbeddedContainerId</c>;
/// the customer stamp's app settings bind it into this section.</item>
/// </list>
///
/// <para>
/// I4 discipline (spec.md FR-31 / design.md §4D I4): every failure mode FAILS LOUDLY —
/// there is no fallback container id, no ambient-tenant resolution, and
/// <c>resolvedFromLiteral</c> is false by construction (the only value ever returned is
/// the config-bound one). Per-document container ids (resolved from Dataverse per record
/// by <c>IDocumentStorageResolver</c>) are a separate, already-tenant-scoped path; this
/// resolver attests the deployment-level binding the probe verifies.
/// </para>
/// </summary>
public sealed class OptionsTenantContainerResolver : ITenantContainerResolver
{
    /// <summary><c>resolverSource</c> value reported by this implementation.</summary>
    public const string Source = "options";

    /// <summary>Tenant-scope pseudo-values that are NOT a concrete tenant pin.</summary>
    private static readonly HashSet<string> UnpinnedTenantScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "common", "organizations", "consumers",
    };

    private readonly IOptionsMonitor<GraphOptions> _graphOptions;
    private readonly IOptionsMonitor<SharePointEmbeddedOptions> _speOptions;
    private readonly ILogger<OptionsTenantContainerResolver> _logger;

    /// <summary>Constructs the resolver over the boot-time-bound options bags.</summary>
    public OptionsTenantContainerResolver(
        IOptionsMonitor<GraphOptions> graphOptions,
        IOptionsMonitor<SharePointEmbeddedOptions> speOptions,
        ILogger<OptionsTenantContainerResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(graphOptions);
        ArgumentNullException.ThrowIfNull(speOptions);
        ArgumentNullException.ThrowIfNull(logger);
        _graphOptions = graphOptions;
        _speOptions = speOptions;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<TenantContainerResolutionResult> ResolveAsync(
        string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var configuredTenantId = _graphOptions.CurrentValue.TenantId;

        // (1) This deployment's own tenant scope must be pinned to a concrete GUID.
        //     "common"/"organizations"/blank cannot attest tenant-scoped resolution.
        if (string.IsNullOrWhiteSpace(configuredTenantId)
            || UnpinnedTenantScopes.Contains(configuredTenantId.Trim()))
        {
            _logger.LogWarning(
                "Tenant-container resolution failed: Graph:TenantId is not pinned to a concrete " +
                "tenant GUID (value kind: {Kind}).",
                string.IsNullOrWhiteSpace(configuredTenantId) ? "blank" : "multi-tenant pseudo-scope");
            return Task.FromResult(TenantContainerResolutionResult.Failure(
                TenantContainerResolutionFailureCode.TenantScopeNotPinned,
                "This deployment's Graph:TenantId is not pinned to a concrete tenant GUID " +
                "(blank or a multi-tenant pseudo-scope such as 'common'). Tenant-scoped SPE " +
                "container resolution cannot be attested — fix the deployment's Graph:TenantId " +
                "app setting (provisioning H9/H12c bind this from the customer's tenant)."));
        }

        // (2) The requested tenant must BE the tenant this stamp serves. Returning the
        //     configured container for any other tenant would be the exact cross-tenant
        //     leak §4D I4 exists to catch — refuse instead. GUID compare is
        //     case-insensitive (casing is not semantic).
        if (!string.Equals(configuredTenantId.Trim(), tenantId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Tenant-container resolution refused: requested tenant {RequestedTenantId} is not " +
                "the tenant this deployment serves.",
                tenantId);
            return Task.FromResult(TenantContainerResolutionResult.Failure(
                TenantContainerResolutionFailureCode.TenantNotServed,
                $"Requested tenantId '{tenantId}' is not the tenant this BFF deployment is scoped " +
                "to. The resolver refuses cross-tenant container resolution (§4D I4 — returning " +
                "another tenant's container id would be the cross-tenant SPE leak this invariant catches)."));
        }

        // (3) The container id must be bound in configuration. NO fallback default —
        //     substituting one is the silent-fail I4 forbids.
        var containerId = _speOptions.CurrentValue.StagingContainerId;
        if (string.IsNullOrWhiteSpace(containerId))
        {
            _logger.LogWarning(
                "Tenant-container resolution failed: SharePointEmbedded:StagingContainerId is not " +
                "configured for tenant {TenantId}. No fallback default is substituted (§4D I4).",
                tenantId);
            return Task.FromResult(TenantContainerResolutionResult.Failure(
                TenantContainerResolutionFailureCode.ContainerNotConfigured,
                "SharePointEmbedded:StagingContainerId is not bound in this deployment's " +
                "configuration. The resolver fails rather than substituting a default container id " +
                "(§4D I4 — a fallback default silently routes uploads to the wrong customer's " +
                "container). Provisioning H8 writes the customer container id to KV / the Dataverse " +
                "env-var sprk_SharePointEmbeddedContainerId; verify the app-setting binding."));
        }

        // Echo the caller's tenantId VERBATIM (see TenantContainerResolution.TenantId docs —
        // the L2 probe compares ordinally against its own request value; the case-insensitive
        // scope match above already established it is the same tenant).
        return Task.FromResult(TenantContainerResolutionResult.Success(
            new TenantContainerResolution(
                TenantId: tenantId,
                ContainerId: containerId.Trim(),
                ResolverSource: Source,
                ResolvedFromLiteral: false)));
    }
}
