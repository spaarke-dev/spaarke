// -----------------------------------------------------------------------------
// GraphRestAppRoleGranter.cs
//
// Production IGraphAppRoleGranter — raw Microsoft Graph REST calls (NOT the
// Microsoft.Graph SDK; see H10DataverseAppUserGraphParityHandler.cs file
// header "NFR-09 IMPLEMENTATION NOTE" for the Path-C rationale) via
// HttpClient + DefaultAzureCredential. The calls live in GraphAppRoleRest:
//   GET    /v1.0/servicePrincipals?$filter=appId eq '{graphResourceAppId}'
//   GET    /v1.0/servicePrincipals/{uamiSpId}/appRoleAssignments
//   POST   /v1.0/servicePrincipals/{uamiSpId}/appRoleAssignments
//          { principalId, resourceId, appRoleId }
//   DELETE /v1.0/servicePrincipals/{uamiSpId}/appRoleAssignments/{id}   (task 261)
//
// Auth: DefaultAzureCredential with explicit TenantId (§4D I5 — never a
// default-tenant credential); scope "https://graph.microsoft.com/.default".
// The caller is the L2 Worker identity, which needs AppRoleAssignment.ReadWrite.All
// + Directory.Read.All (ControlPlaneGraphAppRoles).
//
// HTTP-testable through the internal constructor (credential factory); handler
// unit tests substitute a fake IGraphAppRoleGranter.
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <inheritdoc cref="IGraphAppRoleGranter"/>
public sealed class GraphRestAppRoleGranter : IGraphAppRoleGranter
{
    private readonly HttpClient _httpClient;
    private readonly IGraphAppRolesRegistry _registry;
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly ILogger<GraphRestAppRoleGranter> _logger;

    public GraphRestAppRoleGranter(
        HttpClient httpClient,
        IGraphAppRolesRegistry registry,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<GraphRestAppRoleGranter> logger)
        : this(httpClient, registry, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>Test seam: a credential factory instead of DefaultAzureCredential.</summary>
    internal GraphRestAppRoleGranter(
        HttpClient httpClient,
        IGraphAppRolesRegistry registry,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<GraphRestAppRoleGranter> logger,
        Func<string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _registry = registry;
        _logger = logger;
        _credentialFactory = credentialFactory;
        _httpClient.Timeout = options.Value.GraphRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<GraphAppRoleGrantOutcome> GrantRolesAsync(
        string uamiServicePrincipalObjectId,
        string tenantId,
        IReadOnlyList<GraphAppRoleEntry> expectedRoles,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uamiServicePrincipalObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(expectedRoles);

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GraphAppRoleGrantOutcome.Failure(
                $"Graph token acquisition failed: {ex.GetType().Name}: {ex.Message}",
                FailedRoleValues: Array.Empty<string>());
        }

        GraphResourcePrincipal graph;
        try
        {
            graph = await GraphAppRoleRest.ResolveGraphResourceAsync(
                _httpClient, token, _registry.GraphResourceAppId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GraphAppRoleGrantOutcome.Failure(
                $"Failed to resolve Microsoft Graph resource service principal: {ex.Message}",
                FailedRoleValues: Array.Empty<string>());
        }

        HashSet<string> currentAppRoleIds;
        try
        {
            var assignments = await GraphAppRoleRest.ReadAssignmentsAsync(
                _httpClient, token, uamiServicePrincipalObjectId, graph.Id, cancellationToken).ConfigureAwait(false);
            currentAppRoleIds = assignments.Select(a => a.AppRoleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GraphAppRoleGrantOutcome.Failure(
                $"Failed to read current UAMI app-role assignments: {ex.Message}",
                FailedRoleValues: Array.Empty<string>());
        }

        var failedRoles = new List<string>();
        var grantedCount = 0;
        foreach (var role in expectedRoles)
        {
            if (string.IsNullOrWhiteSpace(role.AppRoleId))
            {
                // Escalation gate (handler-level, ran BEFORE this call) already
                // refuses on any null AppRoleId — defensive skip here in case
                // this seam is ever invoked directly (e.g. from a future
                // reconciler path) without the gate re-run.
                failedRoles.Add(role.Value);
                continue;
            }

            if (currentAppRoleIds.Contains(role.AppRoleId))
            {
                grantedCount++;
                continue;
            }

            try
            {
                await GraphAppRoleRest.PostGrantAsync(
                    _httpClient, token, uamiServicePrincipalObjectId, graph.Id, role.AppRoleId, cancellationToken)
                    .ConfigureAwait(false);
                grantedCount++;
                _logger.LogInformation(
                    "H10 granted Graph app role {RoleValue} ({AppRoleId}) to stamp identity SP {UamiSpId}",
                    role.Value, role.AppRoleId, uamiServicePrincipalObjectId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Grant FAILED for role {RoleValue} ({AppRoleId}) on UAMI SP {UamiSpId}",
                    role.Value, role.AppRoleId, uamiServicePrincipalObjectId);
                failedRoles.Add(role.Value);
                // Per Grant-GraphAppRoles.ps1: NEVER silent-skip — record + continue
                // attempting the remaining roles so one bad grant doesn't mask
                // diagnostics for the others.
            }
        }

        if (failedRoles.Count > 0)
        {
            return new GraphAppRoleGrantOutcome.Failure(
                $"{failedRoles.Count} of {expectedRoles.Count} role grant(s) failed: {string.Join(", ", failedRoles)}.",
                failedRoles);
        }

        return new GraphAppRoleGrantOutcome.Success(grantedCount);
    }

    /// <inheritdoc/>
    public async Task<GraphAppRoleRemovalOutcome> RemoveUnexpectedRolesAsync(
        string uamiServicePrincipalObjectId,
        string uamiClientId,
        string tenantId,
        IReadOnlyList<GraphAppRoleEntry> allowedRoles,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uamiServicePrincipalObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(uamiClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(allowedRoles);

        var none = Array.Empty<string>();
        if (allowedRoles.Count == 0 || allowedRoles.Any(r => string.IsNullOrWhiteSpace(r.AppRoleId)))
        {
            // Fail closed: with an empty or partly-null allowed set every assignment would look "unexpected".
            return new GraphAppRoleRemovalOutcome.Failure(
                "Refusing to remove Graph app roles: the allowed set is empty or has a null AppRoleId. Nothing was removed.",
                none, none);
        }
        var allowedIds = allowedRoles.Select(r => r.AppRoleId!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        AccessToken token;
        GraphResourcePrincipal graph;
        IReadOnlyList<GraphAppRoleAssignment> assignments;
        try
        {
            token = await AcquireTokenAsync(tenantId, cancellationToken).ConfigureAwait(false);

            // The target must BE the stamp's managed identity — removal is destructive, and an object id that drifted
            // to another principal would otherwise lose that principal's roles.
            var (appId, type) = await GraphAppRoleRest.ReadPrincipalAsync(
                _httpClient, token, uamiServicePrincipalObjectId, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(appId, uamiClientId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(type, "ManagedIdentity", StringComparison.OrdinalIgnoreCase))
            {
                return new GraphAppRoleRemovalOutcome.Failure(
                    $"Refusing to remove Graph app roles from SP {uamiServicePrincipalObjectId}: its appId is '{appId ?? "(none)"}' " +
                    $"and type '{type ?? "(none)"}', expected the stamp managed identity (appId '{uamiClientId}', type " +
                    "'ManagedIdentity'). InterStepState.miObjectId and miClientId disagree. Nothing was removed.",
                    none, none);
            }

            graph = await GraphAppRoleRest.ResolveGraphResourceAsync(
                _httpClient, token, _registry.GraphResourceAppId, cancellationToken).ConfigureAwait(false);
            assignments = await GraphAppRoleRest.ReadAssignmentsAsync(
                _httpClient, token, uamiServicePrincipalObjectId, graph.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GraphAppRoleRemovalOutcome.Failure(
                $"Could not read the stamp identity's Graph app-role assignments: {ex.GetType().Name}: {ex.Message}. Nothing was removed.",
                none, none);
        }

        var removed = new List<string>();
        var failed = new List<string>();
        foreach (var extra in assignments.Where(a => !allowedIds.Contains(a.AppRoleId)))
        {
            var name = graph.NameOf(extra.AppRoleId);
            if (string.IsNullOrWhiteSpace(extra.AssignmentId))
            {
                failed.Add(name);
                continue;
            }
            try
            {
                await GraphAppRoleRest.DeleteAssignmentAsync(
                    _httpClient, token, uamiServicePrincipalObjectId, extra.AssignmentId, cancellationToken).ConfigureAwait(false);
                removed.Add(name);
                // Drift record (provisioning.md: drift-detection handlers audit-log the drift they repair).
                _logger.LogWarning(
                    "H10 REMOVED Graph app role {RoleValue} ({AppRoleId}, assignment {AssignmentId}) from stamp identity SP {UamiSpId} — " +
                    "not in the stamp's evidence-backed role set (task 261)",
                    name, extra.AppRoleId, extra.AssignmentId, uamiServicePrincipalObjectId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Removal FAILED for Graph app role {RoleValue} ({AppRoleId}, assignment {AssignmentId}) on stamp identity SP {UamiSpId}",
                    name, extra.AppRoleId, extra.AssignmentId, uamiServicePrincipalObjectId);
                failed.Add(name);
            }
        }

        if (failed.Count > 0)
        {
            return new GraphAppRoleRemovalOutcome.Failure(
                $"{failed.Count} Graph app role(s) outside the stamp set could not be removed: {string.Join(", ", failed)}." +
                (removed.Count > 0 ? $" Removed: {string.Join(", ", removed)}." : string.Empty),
                removed, failed);
        }

        return new GraphAppRoleRemovalOutcome.Success(removed);
    }

    private async Task<AccessToken> AcquireTokenAsync(string tenantId, CancellationToken ct)
        => await _credentialFactory(tenantId)
            .GetTokenAsync(new TokenRequestContext(GraphAppRoleRest.GraphScope), ct).ConfigureAwait(false);
}
