// -----------------------------------------------------------------------------
// CustomerOwnerTeamsVerifier.cs
//
// H13 step 7d (#2429): the customer owner teams H7c set up are in place — read-only, the script's -Verify
// (scripts/Set-CustomerOwnerTeams.ps1): in the customer's top business unit each declared team exists exactly once, is an
// Owner team, not a default team, has no member and holds no other role; it holds that unit's copy of its role; the role
// (and every copy) is assigned to nothing else; and the role holds Read at Basic on exactly its file's tables.
//
// VERDICTS: Passed | Failed (H13 quarantines: drift, or the environment refused — the customer unit is the root, Secure
// Record, not a direct child of the root …) | Inconclusive (H13 fails Resumable: a missing input or an unreachable
// Dataverse — no verdict). Never writes: the verifier must not alter what it verifies.
//
// PLACEMENT (CLAUDE.md §11): Existing — H13's secure-isolation census (task 260) grades the Secure Record topology through
// the BFF, not these teams, and the customer-mailbox check (task 263) is the pattern copied here; Extension — reuses H7c's
// procedure (its VerifyAsync) and H7b's seam, so H13 and H7c cannot disagree on what "the teams" are; Cost of doing nothing
// — a stamp could reach Ready with a team someone added a member to, and every hard-deny write the BFF makes would refuse.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.CustomerOwnerTeams;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>H13's customer owner teams check (#2429).</summary>
public interface ICustomerOwnerTeamsVerifier
{
    /// <summary>Read-only. MUST NOT throw for a "could not verdict" state — returns Inconclusive.</summary>
    Task<CustomerOwnerTeamsVerificationOutcome> VerifyAsync(CustomerOwnerTeamsVerificationRequest request, CancellationToken cancellationToken);
}

/// <summary>What H13 knows about the run.</summary>
public sealed record CustomerOwnerTeamsVerificationRequest(
    string TenantId,
    string DataverseUrl,
    string BffAppRegId,
    string CustomerBusinessUnitId);

/// <summary>Passed | Failed | Inconclusive.</summary>
public abstract record CustomerOwnerTeamsVerificationOutcome
{
    private CustomerOwnerTeamsVerificationOutcome() { }

    /// <summary>Both teams are in the declared state.</summary>
    public sealed record Passed(string Summary) : CustomerOwnerTeamsVerificationOutcome;

    /// <summary>The check ran and found drift — every finding listed.</summary>
    public sealed record Failed(IReadOnlyList<string> Problems) : CustomerOwnerTeamsVerificationOutcome;

    /// <summary>No verdict (missing input, unreachable Dataverse).</summary>
    public sealed record Inconclusive(string Diagnostic) : CustomerOwnerTeamsVerificationOutcome;
}

/// <inheritdoc cref="ICustomerOwnerTeamsVerifier"/>
public sealed class CustomerOwnerTeamsVerifier : ICustomerOwnerTeamsVerifier
{
    private readonly ISecureRecordSetupDataverse _dataverse;
    private readonly ILogger<CustomerOwnerTeamsVerifier> _logger;
    private readonly Func<CustomerOwnerRoleSet> _roleSet;
    private readonly Func<SecureRecordOwnerRoleSet> _secureRoleSet;

    /// <summary>Production: the embedded declared files.</summary>
    public CustomerOwnerTeamsVerifier(ISecureRecordSetupDataverse dataverse, ILogger<CustomerOwnerTeamsVerifier> logger)
        : this(dataverse, logger, () => CustomerOwnerRoleSet.Embedded, () => SecureRecordOwnerRoleSet.Embedded)
    {
    }

    /// <summary>Test seam.</summary>
    internal CustomerOwnerTeamsVerifier(
        ISecureRecordSetupDataverse dataverse,
        ILogger<CustomerOwnerTeamsVerifier> logger,
        Func<CustomerOwnerRoleSet> roleSet,
        Func<SecureRecordOwnerRoleSet> secureRoleSet)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(roleSet);
        ArgumentNullException.ThrowIfNull(secureRoleSet);
        _dataverse = dataverse;
        _logger = logger;
        _roleSet = roleSet;
        _secureRoleSet = secureRoleSet;
    }

    /// <inheritdoc/>
    public async Task<CustomerOwnerTeamsVerificationOutcome> VerifyAsync(
        CustomerOwnerTeamsVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var inputs = new (string Name, string Value)[]
        {
            ("tenantId", request.TenantId), ("dataverseEnvUrl", request.DataverseUrl), ("bffAppRegId", request.BffAppRegId),
            ("customerBusinessUnitId", request.CustomerBusinessUnitId),
        };
        var missing = inputs.Where(x => string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Name).ToArray();
        if (missing.Length > 0)
        {
            return new CustomerOwnerTeamsVerificationOutcome.Inconclusive(
                $"Customer owner teams check has no {string.Join(", ", missing)} — cannot judge the teams.");
        }
        if (!Guid.TryParse(request.CustomerBusinessUnitId, out var customerUnitId) || customerUnitId == Guid.Empty)
        {
            return new CustomerOwnerTeamsVerificationOutcome.Inconclusive(
                $"customerBusinessUnitId '{request.CustomerBusinessUnitId}' is not a GUID — cannot judge the teams.");
        }

        CustomerOwnerTeamsVerification verification;
        try
        {
            var procedureRequest = new CustomerOwnerTeamsRequest(
                new SecureRecordSetupTarget(request.DataverseUrl, request.TenantId, request.BffAppRegId),
                _roleSet(), _secureRoleSet().BusinessUnitName, customerUnitId);
            verification = await new CustomerOwnerTeamsProcedure(_dataverse, _logger)
                .VerifyAsync(procedureRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A read that fails is no verdict — never a pass.
            return new CustomerOwnerTeamsVerificationOutcome.Inconclusive(
                $"The customer owner teams read did not complete: {ex.GetType().Name}: {ex.Message}");
        }

        switch (verification)
        {
            case CustomerOwnerTeamsVerification.Passed passed:
                _logger.LogInformation("H13 customer owner teams PASSED: unit={Unit} teams={Teams}",
                    customerUnitId, string.Join(", ", passed.Teams.Select(x => x.TeamName)));
                return new CustomerOwnerTeamsVerificationOutcome.Passed(
                    string.Join("; ", passed.Teams.Select(x => $"'{x.TeamName}' ({x.TeamId}) holds '{x.RoleName}' ({x.RoleCopyId}), {x.PrivilegeCount} privilege(s)")));
            case CustomerOwnerTeamsVerification.Drifted drifted:
                _logger.LogWarning("H13 customer owner teams FAILED: unit={Unit} problems={Problems}",
                    customerUnitId, string.Join(" | ", drifted.Findings));
                return new CustomerOwnerTeamsVerificationOutcome.Failed(drifted.Findings);
            default:
                return new CustomerOwnerTeamsVerificationOutcome.Inconclusive(
                    $"The customer owner teams check returned an unhandled verdict '{verification.GetType().Name}'.");
        }
    }
}
