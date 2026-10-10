// -----------------------------------------------------------------------------
// CustomerMailboxVerifier.cs
//
// H13 step 7c (task 263, #1562): the stamp's customer mailbox is in place — asserted from EFFECTS, read-only:
//   Exchange (sidecar POST /read-customer-mailbox): the shared mailbox sprk-{customerId}-mail exists at the intake
//     address, is a member of the customer's scope group (Spaarke-AppAccess-{customerId}) and of no other group, and
//     Test-ServicePrincipalAuthorization reports every stamp mail role in scope;
//   Dataverse (the stamp): exactly one ACTIVE sprk_communicationaccount row for that address, a Shared Account, Verified.
//
// VERDICTS: Passed | Failed (H13 quarantines: the mail model is not met, or something foreign sits where the
// customer's mailbox should be) | Inconclusive (H13 fails Resumable: a missing input or an unreachable sidecar or
// Dataverse — no verdict). Never writes: the verifier must not alter what it verifies.
//
// PLACEMENT (CLAUDE.md §11): Existing — the T4 probe checks the stamp identity's ROLE assignments, not the mailbox
// they must reach; Extension — reuses H14m's two seams (ICustomerMailboxClient, ICommunicationAccountStore) and its
// derived names, so H13 and H14m cannot disagree on what "the mailbox" is; Cost of doing nothing — a stamp could reach
// Ready with no mailbox record (the POML acceptance criterion "H13 fails if missing or unverified").
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>H13's customer-mailbox check (task 263).</summary>
public interface ICustomerMailboxVerifier
{
    /// <summary>Read-only. MUST NOT throw for a "could not verdict" state — returns Inconclusive.</summary>
    Task<CustomerMailboxVerificationOutcome> VerifyAsync(CustomerMailboxVerificationRequest request, CancellationToken cancellationToken);
}

/// <summary>What H13 knows about the run.</summary>
public sealed record CustomerMailboxVerificationRequest(
    string CustomerId,
    string RunId,
    string TenantId,
    string UamiClientId,
    string ScopeGroupId,
    string DisplayName,
    string MailboxAddress,
    string DataverseUrl,
    string BffAppRegId);

/// <summary>Passed | Failed | Inconclusive.</summary>
public abstract record CustomerMailboxVerificationOutcome
{
    private CustomerMailboxVerificationOutcome() { }

    /// <summary>The mailbox and its verified row are in place.</summary>
    public sealed record Passed(string Summary) : CustomerMailboxVerificationOutcome;

    /// <summary>The check ran and the mail model is not met — every problem listed.</summary>
    public sealed record Failed(IReadOnlyList<string> Problems) : CustomerMailboxVerificationOutcome;

    /// <summary>No verdict (missing input, unreachable sidecar or Dataverse).</summary>
    public sealed record Inconclusive(string Diagnostic) : CustomerMailboxVerificationOutcome;
}

/// <inheritdoc cref="ICustomerMailboxVerifier"/>
public sealed class CustomerMailboxVerifier : ICustomerMailboxVerifier
{
    private readonly ICustomerMailboxClient _mailboxClient;
    private readonly ICommunicationAccountStore _accountStore;
    private readonly IGraphAppRolesRegistry _roles;
    private readonly ILogger<CustomerMailboxVerifier> _logger;

    public CustomerMailboxVerifier(
        ICustomerMailboxClient mailboxClient,
        ICommunicationAccountStore accountStore,
        IGraphAppRolesRegistry roles,
        ILogger<CustomerMailboxVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(mailboxClient);
        ArgumentNullException.ThrowIfNull(accountStore);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(logger);
        _mailboxClient = mailboxClient;
        _accountStore = accountStore;
        _roles = roles;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CustomerMailboxVerificationOutcome> VerifyAsync(CustomerMailboxVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var inputs = new (string Name, string Value)[]
        {
            ("tenantId", request.TenantId), ("miClientId", request.UamiClientId),
            ("exchangePolicyScopeGroupId", request.ScopeGroupId), ("displayName", request.DisplayName),
            ("communicationDefaultMailbox", request.MailboxAddress), ("dataverseEnvUrl", request.DataverseUrl),
            ("bffAppRegId", request.BffAppRegId),
        };
        var missing = inputs.Where(x => string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Name).ToArray();
        if (missing.Length > 0)
        {
            return new CustomerMailboxVerificationOutcome.Inconclusive(
                $"Customer mailbox check has no {string.Join(", ", missing)} — cannot judge the mailbox.");
        }

        var name = CustomerMailboxNaming.MailboxName(request.CustomerId);
        var roles = _roles.GetExchangeScoped().Select(r => IGraphAppRolesRegistry.ToExchangeApplicationRole(r.Value)).ToArray();
        var problems = new List<string>();

        // (1) Exchange.
        CustomerMailboxReadOutcome exchange;
        try
        {
            exchange = await _mailboxClient.ReadAsync(
                new CustomerMailboxRequest(request.TenantId, request.UamiClientId, request.ScopeGroupId,
                    CustomerMailboxNaming.ScopeGroupName(request.CustomerId), name, request.DisplayName, request.MailboxAddress,
                    roles, request.RunId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            exchange = new CustomerMailboxReadOutcome.Failure($"The mailbox client threw {ex.GetType().Name}: {ex.Message}.");
        }
        if (exchange is CustomerMailboxReadOutcome.Failure exchangeFailure)
        {
            return new CustomerMailboxVerificationOutcome.Inconclusive($"The Exchange mailbox read did not complete: {exchangeFailure.Diagnostic}");
        }
        problems.AddRange(ClassifyExchange(name, request.MailboxAddress, roles, (CustomerMailboxReadOutcome.Success)exchange));

        // (2) Dataverse.
        CommunicationAccountReadOutcome rows;
        try
        {
            rows = await _accountStore.ReadAsync(
                new CommunicationAccountTarget(request.DataverseUrl, request.TenantId, request.BffAppRegId),
                request.MailboxAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            rows = new CommunicationAccountReadOutcome.Failure($"The account store threw {ex.GetType().Name}: {ex.Message}.");
        }
        if (rows is CommunicationAccountReadOutcome.Failure rowsFailure)
        {
            return new CustomerMailboxVerificationOutcome.Inconclusive($"The sprk_communicationaccount read did not complete: {rowsFailure.Diagnostic}");
        }
        problems.AddRange(ClassifyRows(request.MailboxAddress, ((CommunicationAccountReadOutcome.Found)rows).Rows));

        if (problems.Count == 0)
        {
            _logger.LogInformation("H13 customer mailbox PASSED: {Mailbox} <{Address}> customer={CustomerId}", name, request.MailboxAddress, request.CustomerId);
            return new CustomerMailboxVerificationOutcome.Passed($"{name} <{request.MailboxAddress}> in scope; one verified account row.");
        }
        _logger.LogWarning("H13 customer mailbox FAILED: customer={CustomerId} problems={Problems}", request.CustomerId, string.Join(" | ", problems));
        return new CustomerMailboxVerificationOutcome.Failed(problems);
    }

    /// <summary>Exchange problems, empty when the mailbox is right. Internal so tests pin the rules.</summary>
    internal static IReadOnlyList<string> ClassifyExchange(
        string name, string address, IReadOnlyList<string> roles, CustomerMailboxReadOutcome.Success read)
    {
        var problems = new List<string>(read.Conflicts);
        if (!read.Exists)
        {
            problems.Add($"The shared mailbox '{name}' <{address}> does not exist in Exchange Online (H14m never created it).");
            return problems;
        }
        var notInScope = roles
            .Where(role => !read.Authorization.Any(a => a.InScope && string.Equals(a.Role, role, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (notInScope.Length > 0)
        {
            problems.Add($"Exchange does not authorize the stamp identity's {string.Join(", ", notInScope)} on '{name}' " +
                "(Test-ServicePrincipalAuthorization) — check H14a's assignments (T4) and the mailbox's scope-group membership.");
        }
        return problems;
    }

    /// <summary>Dataverse problems, empty when exactly one active, shared, Verified row exists. Internal so tests pin the rules.</summary>
    internal static IReadOnlyList<string> ClassifyRows(string address, IReadOnlyList<CommunicationAccountRow> rows)
    {
        var active = rows.Where(r => r.StateCode == 0).ToArray();
        if (active.Length == 0)
        {
            return new[] { $"The stamp has no active sprk_communicationaccount row for '{address}' (H14m writes it)." };
        }
        if (active.Length > 1)
        {
            return new[] { $"The stamp has {active.Length} active sprk_communicationaccount rows for '{address}' — exactly one is expected." };
        }
        var row = active[0];
        var problems = new List<string>();
        if (row.AccountType != CommunicationAccountValues.SharedAccount)
        {
            problems.Add($"The sprk_communicationaccount row {row.Id} is not a Shared Account (sprk_accounttype {row.AccountType?.ToString() ?? "empty"}).");
        }
        if (row.VerificationStatus != CommunicationAccountValues.Verified)
        {
            problems.Add($"The sprk_communicationaccount row {row.Id} is not Verified (sprk_verificationstatus " +
                $"{row.VerificationStatus?.ToString() ?? "empty"}{(string.IsNullOrEmpty(row.VerificationMessage) ? string.Empty : $": {row.VerificationMessage}")}).");
        }
        return problems;
    }
}
