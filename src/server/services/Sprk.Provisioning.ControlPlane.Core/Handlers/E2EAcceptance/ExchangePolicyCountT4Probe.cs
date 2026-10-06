// -----------------------------------------------------------------------------
// ExchangePolicyCountT4Probe.cs
//
// H13 T4 probe (task 180; rewritten by task 251 for RBAC for Applications, owner D26).
//
// WHAT IT VERIFIES: H14a's effect in Exchange — the stamp's managed identity holds every
// Exchange-scoped mailbox role (IGraphAppRolesRegistry.GetExchangeScoped(), e.g.
// "Application Mail.Send") through an assignment limited to the customer's scope group, and holds
// none of those roles OUTSIDE that group. Read through the sidecar's read-only
// POST /read-mailbox-access route; never mutates (H13: assert effects, not intentions).
//
// VERDICTS:
//   Passed       — every role present in scope; nothing out of scope.
//   Failed       — the identity is not registered in Exchange, a role is missing, or a role is
//                  granted outside the group (the stamp would reach other customers' mailboxes —
//                  on Model 1 every stamp lives in Spaarke's tenant).
//   InfraFault   — missing input, or the read could not reach a conclusive answer (Resumable).
//
// Checks CONFIGURATION, not effect: Exchange applies a new grant within 30 min – 2 h, so a fresh
// stamp can pass T4 before its Graph mail calls succeed (documented by Microsoft).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>H13's T4 probe: the stamp identity's group-scoped Exchange mailbox roles.</summary>
public sealed class ExchangePolicyCountT4Probe : ITrapProbe
{
    /// <inheritdoc/>
    public TrapKind Kind => TrapKind.T4ExchangePolicyCount;

    private readonly IExchangePolicyReadClient _readClient;
    private readonly IGraphAppRolesRegistry _roles;
    private readonly ILogger<ExchangePolicyCountT4Probe> _logger;

    public ExchangePolicyCountT4Probe(
        IExchangePolicyReadClient readClient,
        IGraphAppRolesRegistry roles,
        ILogger<ExchangePolicyCountT4Probe> logger)
    {
        ArgumentNullException.ThrowIfNull(readClient);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(logger);
        _readClient = readClient;
        _roles = roles;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TrapVerificationOutcome> ProbeAsync(TrapVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind, "T4 probe: request.TenantId is empty (§4D I1 — never an ambient tenant).");
        }
        if (string.IsNullOrWhiteSpace(request.UamiClientId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind, "T4 probe: request.UamiClientId is empty — H2a must populate InterStepState.miClientId.");
        }
        if (string.IsNullOrWhiteSpace(request.ExchangeScopeGroupId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T4 probe: request.ExchangeScopeGroupId is empty — the run's 'exchangePolicyScopeGroupId' intake value is required to verify the scope.");
        }

        var roles = _roles.GetExchangeScoped().Select(r => IGraphAppRolesRegistry.ToExchangeApplicationRole(r.Value)).ToArray();
        ExchangePolicyReadOutcome read;
        try
        {
            read = await _readClient.ReadAsync(
                new ExchangePolicyReadRequest(request.TenantId, request.UamiClientId, request.ExchangeScopeGroupId, roles, request.RunId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "T4 probe: the read client threw (customerId={CustomerId})", request.CustomerId);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T4 verdict deferred: the Exchange read client threw {ex.GetType().Name}: {ex.Message} (its contract is to return Failure).");
        }

        if (read is ExchangePolicyReadOutcome.Failure failure)
        {
            return new TrapVerificationOutcome.InfraFault(Kind, $"T4 verdict deferred: the Exchange read did not complete. {failure.Diagnostic}");
        }

        var success = (ExchangePolicyReadOutcome.Success)read;
        var problems = Classify(roles, success);
        if (problems.Count == 0)
        {
            _logger.LogInformation("T4 probe PASSED: {RoleCount} group-scoped Exchange roles on app {AppId} (tenant {TenantId})",
                roles.Length, request.UamiClientId, request.TenantId);
            return new TrapVerificationOutcome.Passed(Kind);
        }

        var diagnostic =
            $"T4 VIOLATED for the stamp identity (app {request.UamiClientId}, tenant {request.TenantId}, customer {request.CustomerId}, " +
            $"scope group {request.ExchangeScopeGroupId}): {string.Join(" ", problems)} Re-run H14a, or inspect with " +
            "Get-ManagementRoleAssignment in Exchange Online (spec.md FR-33 T4).";
        _logger.LogWarning("T4 probe FAILED: {Diagnostic}", diagnostic);
        return new TrapVerificationOutcome.Failed(Kind, diagnostic);
    }

    /// <summary>The problems found, empty when T4 holds. Internal so tests can pin the rules.</summary>
    internal static IReadOnlyList<string> Classify(IReadOnlyList<string> expectedRoles, ExchangePolicyReadOutcome.Success read)
    {
        if (!read.ServicePrincipalRegistered)
        {
            return new[] { "The identity is not registered in Exchange Online (New-ServicePrincipal never ran) — it holds no mailbox roles." };
        }
        var problems = new List<string>();
        var missing = expectedRoles
            .Where(role => !read.Assignments.Any(a => a.InExpectedScope && string.Equals(a.Role, role, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (missing.Length > 0)
        {
            problems.Add($"Missing in scope: {string.Join(", ", missing)}.");
        }
        var outOfScope = read.Assignments.Where(a => !a.InExpectedScope).ToArray();
        if (outOfScope.Length > 0)
        {
            problems.Add("Granted OUTSIDE the customer's group (reaches other mailboxes): " +
                string.Join(", ", outOfScope.Select(a => $"{a.Name} ({a.Role}, scope '{(a.Scope.Length == 0 ? "organization-wide" : a.Scope)}')")) + ".");
        }
        return problems;
    }
}
