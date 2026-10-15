// -----------------------------------------------------------------------------
// GraphRestAppRoleParityVerifier.cs
//
// Production IGraphAppRoleParityVerifier — the T3 silent-fail trap post-
// condition check (spec.md FR-33). Raw Microsoft Graph REST calls (same
// Path-C rationale as GraphRestAppRoleGranter.cs; shared plumbing in
// GraphAppRoleRest) — independently re-reads
// GET /v1.0/servicePrincipals/{uamiSpId}/appRoleAssignments (Graph-resource-
// scoped) and asserts every expected AppRoleId is present and, since task 261,
// that no other Graph app role is.
//
// HTTP-testable through the internal constructor (credential factory); handler
// unit tests substitute a fake IGraphAppRoleParityVerifier.
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <inheritdoc cref="IGraphAppRoleParityVerifier"/>
public sealed class GraphRestAppRoleParityVerifier : IGraphAppRoleParityVerifier
{
    private readonly HttpClient _httpClient;
    private readonly IGraphAppRolesRegistry _registry;
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly ILogger<GraphRestAppRoleParityVerifier> _logger;

    public GraphRestAppRoleParityVerifier(
        HttpClient httpClient,
        IGraphAppRolesRegistry registry,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<GraphRestAppRoleParityVerifier> logger)
        : this(httpClient, registry, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>Test seam: a credential factory instead of DefaultAzureCredential.</summary>
    internal GraphRestAppRoleParityVerifier(
        HttpClient httpClient,
        IGraphAppRolesRegistry registry,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<GraphRestAppRoleParityVerifier> logger,
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
    public async Task<GraphAppRoleParityResult> VerifyAsync(
        string uamiServicePrincipalObjectId,
        string tenantId,
        IReadOnlyList<GraphAppRoleEntry> expectedRoles,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uamiServicePrincipalObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(expectedRoles);

        HashSet<string> currentIds;
        try
        {
            var (_, assignments) = await ReadAsync(uamiServicePrincipalObjectId, tenantId, cancellationToken).ConfigureAwait(false);
            currentIds = assignments.Select(a => a.AppRoleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "T3 verify: appRoleAssignments read failed for UAMI SP {UamiSpId}", uamiServicePrincipalObjectId);
            return new GraphAppRoleParityResult.Partial(
                expectedRoles.Select(r => r.Value).ToList(), GrantedCount: 0, ExpectedCount: expectedRoles.Count);
        }

        var missing = expectedRoles
            .Where(r => string.IsNullOrWhiteSpace(r.AppRoleId) || !currentIds.Contains(r.AppRoleId))
            .Select(r => r.Value)
            .ToList();

        if (missing.Count == 0)
        {
            return new GraphAppRoleParityResult.Verified(expectedRoles.Count);
        }

        return new GraphAppRoleParityResult.Partial(
            missing, GrantedCount: expectedRoles.Count - missing.Count, ExpectedCount: expectedRoles.Count);
    }

    /// <inheritdoc/>
    public async Task<GraphAppRoleExtrasResult> FindUnexpectedRolesAsync(
        string uamiServicePrincipalObjectId,
        string tenantId,
        IReadOnlyList<GraphAppRoleEntry> allowedRoles,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uamiServicePrincipalObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(allowedRoles);

        if (allowedRoles.Count == 0 || allowedRoles.Any(r => string.IsNullOrWhiteSpace(r.AppRoleId)))
        {
            return new GraphAppRoleExtrasResult.Unknown("The allowed role set is empty or has a null AppRoleId — no verdict.");
        }
        var allowedIds = allowedRoles.Select(r => r.AppRoleId!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            var (graph, assignments) = await ReadAsync(uamiServicePrincipalObjectId, tenantId, cancellationToken).ConfigureAwait(false);
            var extras = assignments
                .Where(a => !allowedIds.Contains(a.AppRoleId))
                .Select(a => graph.NameOf(a.AppRoleId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return extras.Count == 0
                ? new GraphAppRoleExtrasResult.None()
                : new GraphAppRoleExtrasResult.Found(extras);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "T3 extras check: appRoleAssignments read failed for UAMI SP {UamiSpId}", uamiServicePrincipalObjectId);
            return new GraphAppRoleExtrasResult.Unknown(
                $"Could not read the identity's Graph app-role assignments: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<(GraphResourcePrincipal Graph, IReadOnlyList<GraphAppRoleAssignment> Assignments)> ReadAsync(
        string uamiSpId, string tenantId, CancellationToken ct)
    {
        var token = await _credentialFactory(tenantId)
            .GetTokenAsync(new TokenRequestContext(GraphAppRoleRest.GraphScope), ct).ConfigureAwait(false);
        var graph = await GraphAppRoleRest.ResolveGraphResourceAsync(_httpClient, token, _registry.GraphResourceAppId, ct)
            .ConfigureAwait(false);
        var assignments = await GraphAppRoleRest.ReadAssignmentsAsync(_httpClient, token, uamiSpId, graph.Id, ct)
            .ConfigureAwait(false);
        return (graph, assignments);
    }
}
