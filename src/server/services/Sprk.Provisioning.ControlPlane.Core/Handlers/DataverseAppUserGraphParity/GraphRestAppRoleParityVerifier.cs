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
    private readonly H10DataverseAppUserGraphParityOptions _options;
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
        _options = options.Value;
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

        // T186 audit: Microsoft Graph is eventually consistent — an assignment H10 POSTed seconds ago can be absent from
        // the next list — and answers 429 / 5xx under load. Look again (bounded) before judging either one; a real
        // mismatch is still reported as Partial once the looks are used, and a non-transient fault (403, 400, an
        // unreadable body) keeps its old outcome without a retry.
        var lookAttempts = Math.Max(1, _options.PropagationRetryAttempts);
        var transientAttempts = Math.Max(1, _options.TransientRetryAttempts);
        var looks = 0;
        var transientFaults = 0;
        while (true)
        {
            HashSet<string> currentIds;
            try
            {
                var (_, assignments) = await ReadAsync(uamiServicePrincipalObjectId, tenantId, cancellationToken).ConfigureAwait(false);
                currentIds = assignments.Select(a => a.AppRoleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (!H10Propagation.IsTransient(ex, cancellationToken))
                {
                    _logger.LogWarning(ex, "T3 verify: appRoleAssignments read failed for UAMI SP {UamiSpId}", uamiServicePrincipalObjectId);
                    return new GraphAppRoleParityResult.Partial(
                        expectedRoles.Select(r => r.Value).ToList(), GrantedCount: 0, ExpectedCount: expectedRoles.Count);
                }

                transientFaults++;
                if (transientFaults >= transientAttempts)
                {
                    _logger.LogWarning(ex, "T3 verify: appRoleAssignments read still failing after {Attempts} attempt(s) for UAMI SP {UamiSpId}",
                        transientFaults, uamiServicePrincipalObjectId);
                    return new GraphAppRoleParityResult.Unverified(
                        $"The appRoleAssignments read kept failing with a transient fault after {transientFaults} attempt(s): " +
                        $"{ex.GetType().Name}: {ex.Message}", expectedRoles.Count);
                }
                _logger.LogInformation(
                    "T3 verify: appRoleAssignments read attempt {Attempt}/{Max} hit a transient fault ({Fault}); retrying",
                    transientFaults, transientAttempts, ex.GetType().Name);
                await H10Propagation.DelayAsync(_options.TransientRetryDelay, transientFaults, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var missing = expectedRoles
                .Where(r => string.IsNullOrWhiteSpace(r.AppRoleId) || !currentIds.Contains(r.AppRoleId))
                .Select(r => r.Value)
                .ToList();

            if (missing.Count == 0)
            {
                return new GraphAppRoleParityResult.Verified(expectedRoles.Count);
            }

            looks++;
            if (looks >= lookAttempts || expectedRoles.Any(r => string.IsNullOrWhiteSpace(r.AppRoleId)))
            {
                // A null AppRoleId can never appear by waiting — no point retrying it.
                return new GraphAppRoleParityResult.Partial(
                    missing, GrantedCount: expectedRoles.Count - missing.Count, ExpectedCount: expectedRoles.Count);
            }
            _logger.LogInformation(
                "T3 verify: {Missing} of {Expected} expected role(s) not listed yet on UAMI SP {UamiSpId} (look {Look}/{Max}); " +
                "Graph may not have replicated the grant — looking again",
                missing.Count, expectedRoles.Count, uamiServicePrincipalObjectId, looks, lookAttempts);
            await H10Propagation.DelayAsync(_options.PropagationRetryDelay, looks, cancellationToken).ConfigureAwait(false);
        }
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
