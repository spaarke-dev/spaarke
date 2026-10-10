// -----------------------------------------------------------------------------
// H14mCustomerMailboxSubHandler.cs
//
// L2 CONTROL-PLANE H14m — the customer's Spaarke-tenant shared mailbox (task 263; owner decision 2026-10-10, #1562:
// one Spaarke-tenant shared mailbox per customer, created and verified by a provisioning step after H14a).
//
// PURPOSE (in this order; each step only after the previous succeeded):
//   1. Exchange, through the sidecar (ICustomerMailboxClient.EnsureAsync): the shared mailbox sprk-{customerId}-mail at
//      the intake communicationDefaultMailbox address, display name = intake displayName, a DIRECT member of the
//      customer's scope group (Spaarke-AppAccess-{customerId}) — so H14a's group-scoped Mail.* roles reach it — and
//      Exchange's own authorization test (Test-ServicePrincipalAuthorization) for every one of those roles.
//   2. The stamp's Dataverse (ICommunicationAccountStore): exactly one active, shared, Verified sprk_communicationaccount
//      row for that address — the record the BFF's Communication module reads and GraphSubscriptionManager subscribes.
//
// CLASSIFICATION (§4C):
//   Mailbox Drift (foreign / out-of-scope mailbox, another customer's group)  → QuarantineRequired, nothing written
//   Mailbox ensure Failure (sign-in, sidecar, PRQ-E-16 not applied)            → Resumable (get-before-set re-run)
//   Mailbox in place but a role not yet InScope                                 → Resumable, no Dataverse write
//   Row Conflict (inactive / other type / several rows)                          → QuarantineRequired, nothing written
//   Row VerificationFailed (the BFF's own Graph verify failed)                   → Resumable — re-verify in the app
//   Row Failure                                                                  → Resumable
//
// PURE EXECUTOR: like H14a, this class touches no Cosmos state; the H14 parent reads the run once, decides whether to
// dispatch (CompletedPhases scan against ExpectedIdempotencyKey) and writes once. Runs AFTER H14a in the same
// invocation and is not attempted when H14a fails — its authorization test needs H14a's assignments.
// Design: projects/customer-provisioning-orchestration-r1/notes/t263-customer-shared-mailbox.md.
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H14mCustomerMailboxSubHandler : IProvisioningHandler
{
    /// <summary>Handler identifier.</summary>
    public const string HandlerIdentifier = HandlerIds.H14m;

    /// <summary>Sub-step token used in the idempotency key format h14-{customerId}-{subStep}-{hash}.</summary>
    public const string SubStep = "mailbox";

    private readonly ICustomerMailboxClient _mailboxClient;
    private readonly ICommunicationAccountStore _accountStore;
    private readonly IGraphAppRolesRegistry _roles;
    private readonly ILogger<H14mCustomerMailboxSubHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    public H14mCustomerMailboxSubHandler(
        ICustomerMailboxClient mailboxClient,
        ICommunicationAccountStore accountStore,
        IGraphAppRolesRegistry roles,
        ILogger<H14mCustomerMailboxSubHandler> logger)
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
    public async Task<HandlerResult> HandleAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.CustomerId);
        if (!string.Equals(envelope.HandlerId, HandlerIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"H14mCustomerMailboxSubHandler invoked with mismatched HandlerId '{envelope.HandlerId}' (expected '{HandlerIdentifier}').");
        }

        Parameters? p;
        try
        {
            p = JsonSerializer.Deserialize<Parameters>(envelope.ParametersJson);
        }
        catch (JsonException ex)
        {
            return Fail(FailureClass.Resumable, H14mRejections.InvalidParameters, $"H14m ParametersJson deserialization failed: {ex.Message}");
        }
        var missing = p is null ? "all" : MissingParameter(p);
        if (missing is not null)
        {
            return Fail(FailureClass.Resumable, H14mRejections.InvalidParameters, $"H14m ParametersJson is missing '{missing}'.");
        }

        var name = CustomerMailboxNaming.MailboxName(envelope.CustomerId);
        var request = new CustomerMailboxRequest(
            p!.TenantId, p.UamiClientId, p.ScopeGroupId, CustomerMailboxNaming.ScopeGroupName(envelope.CustomerId),
            name, p.DisplayName, p.MailboxAddress, ExchangeRoles(), envelope.RunId);

        // (1) Exchange: the mailbox, its scope-group membership and Exchange's authorization answer.
        CustomerMailboxEnsureOutcome ensured;
        try
        {
            ensured = await _mailboxClient.EnsureAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            ensured = new CustomerMailboxEnsureOutcome.Failure($"The mailbox client threw {ex.GetType().Name}: {ex.Message} (its contract is to return Failure).");
        }

        switch (ensured)
        {
            case CustomerMailboxEnsureOutcome.Drift drift:
                return Fail(FailureClass.QuarantineRequired, H14mRejections.MailboxDrift,
                    $"The customer mailbox '{name}' <{p.MailboxAddress}> cannot be created or adopted: {string.Join(" ", drift.Conflicts)} " +
                    "Nothing was created or changed — inspect the recipient and the scope group in Exchange Online.");
            case CustomerMailboxEnsureOutcome.Failure failure:
                return Fail(FailureClass.Resumable, H14mRejections.MailboxEnsureFailed, $"Customer mailbox ensure failed: {failure.Diagnostic}");
            case CustomerMailboxEnsureOutcome.Ensured { Verified: false } unverified:
                return Fail(FailureClass.Resumable, H14mRejections.MailboxUnverified,
                    $"The customer mailbox '{name}' is in the scope group, but Exchange does not yet report every mail role of the stamp " +
                    $"identity in scope ({string.Join(", ", unverified.Authorization.Select(a => $"{a.Role}={a.InScope}"))}). No account row " +
                    "was written; a re-run writes nothing in Exchange and tests again. If it persists, check H14a's assignments (T4).");
            case CustomerMailboxEnsureOutcome.Ensured:
                break;
            default:
                throw new InvalidOperationException($"Unhandled {nameof(CustomerMailboxEnsureOutcome)} '{ensured.GetType().Name}'.");
        }
        var mailbox = (CustomerMailboxEnsureOutcome.Ensured)ensured;

        // (2) Dataverse: one verified sprk_communicationaccount row.
        var spec = new CommunicationAccountSpec(
            Name: p.DisplayName,
            EmailAddress: p.MailboxAddress,
            DisplayName: p.DisplayName,
            SecurityGroupId: p.ScopeGroupId,
            VerificationMessage:
                "Verified by provisioning (H14m): Exchange RBAC for Applications authorizes the stamp identity's " +
                $"{string.Join(", ", request.Roles)} on this shared mailbox (Test-ServicePrincipalAuthorization). Graph calls " +
                "can lag the grant by 30 min - 2 h; re-verify here after that to test send and read.");
        CommunicationAccountEnsureOutcome row;
        try
        {
            row = await _accountStore.EnsureVerifiedAsync(
                new CommunicationAccountTarget(p.DataverseUrl, p.TenantId, p.BffAppRegId), spec, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            row = new CommunicationAccountEnsureOutcome.Failure($"The account store threw {ex.GetType().Name}: {ex.Message} (its contract is to return Failure).");
        }

        switch (row)
        {
            case CommunicationAccountEnsureOutcome.Ready ready:
                _logger.LogInformation(
                    "H14m customer mailbox ready: customerId={CustomerId} mailbox={Mailbox} created={Created} accountId={AccountId} rowWritten={RowWritten}",
                    envelope.CustomerId, name, mailbox.Created, ready.AccountId, ready.Written);
                return new HandlerResult.Success(ExpectedIdempotencyKey(envelope.CustomerId, p.UamiClientId, p.ScopeGroupId, p.MailboxAddress, p.DataverseUrl));
            case CommunicationAccountEnsureOutcome.Conflict conflict:
                return Fail(FailureClass.QuarantineRequired, H14mRejections.AccountRowConflict,
                    $"The stamp's sprk_communicationaccount rows for '{p.MailboxAddress}' cannot be adopted: {conflict.Diagnostic}");
            case CommunicationAccountEnsureOutcome.VerificationFailed failed:
                return Fail(FailureClass.Resumable, H14mRejections.AccountVerificationFailed,
                    $"The sprk_communicationaccount row {failed.AccountId} for '{p.MailboxAddress}' says the BFF's own verification FAILED " +
                    $"('{failed.Message}'). Provisioning never overwrites that. Graph usually lags a new grant by 30 min - 2 h: verify the " +
                    "account again in the app (POST /api/communications/accounts/{id}/verify) once it passes, then resume.");
            case CommunicationAccountEnsureOutcome.Failure failure:
                return Fail(FailureClass.Resumable, H14mRejections.AccountRowFailed, $"Writing the sprk_communicationaccount row failed: {failure.Diagnostic}");
            default:
                throw new InvalidOperationException($"Unhandled {nameof(CommunicationAccountEnsureOutcome)} '{row.GetType().Name}'.");
        }
    }

    /// <summary>The Exchange application roles that must reach the mailbox — H14a's set, one source.</summary>
    internal IReadOnlyList<string> ExchangeRoles()
        => _roles.GetExchangeScoped().Select(r => IGraphAppRolesRegistry.ToExchangeApplicationRole(r.Value)).ToArray();

    /// <summary>
    /// <c>h14-{customerId}-mailbox-{hash}</c>; hash = SHA-256 over the mailbox name, address, scope group, stamp identity
    /// and Dataverse URL — a different one of these is different work.
    /// </summary>
    internal static string ExpectedIdempotencyKey(string customerId, string uamiClientId, string scopeGroupId, string mailboxAddress, string dataverseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        var payload = string.Join("|",
            CustomerMailboxNaming.MailboxName(customerId),
            mailboxAddress.Trim().ToLowerInvariant(),
            scopeGroupId.Trim().ToLowerInvariant(),
            uamiClientId.Trim().ToLowerInvariant(),
            dataverseUrl.Trim().TrimEnd('/').ToLowerInvariant());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"h14-{customerId}-{SubStep}-{hash}";
    }

    /// <summary>The opaque ParametersJson H14 (parent) embeds — one serialization contract for the parent and the tests.</summary>
    internal static string BuildParametersJson(
        string tenantId, string uamiClientId, string scopeGroupId, string displayName, string mailboxAddress, string dataverseUrl, string bffAppRegId)
        => JsonSerializer.Serialize(new Parameters(tenantId, uamiClientId, scopeGroupId, displayName, mailboxAddress, dataverseUrl, bffAppRegId));

    private static string? MissingParameter(Parameters p) =>
        string.IsNullOrWhiteSpace(p.TenantId) ? "tenantId"
        : string.IsNullOrWhiteSpace(p.UamiClientId) ? "uamiClientId"
        : string.IsNullOrWhiteSpace(p.ScopeGroupId) ? "scopeGroupId"
        : string.IsNullOrWhiteSpace(p.DisplayName) ? "displayName"
        : string.IsNullOrWhiteSpace(p.MailboxAddress) ? "mailboxAddress"
        : string.IsNullOrWhiteSpace(p.DataverseUrl) ? "dataverseUrl"
        : string.IsNullOrWhiteSpace(p.BffAppRegId) ? "bffAppRegId"
        : null;

    private HandlerResult Fail(FailureClass failureClass, string code, string diagnostic)
    {
        _logger.LogWarning("H14m failed [{Code}] ({Class}): {Diagnostic}", code, failureClass, diagnostic);
        return new HandlerResult.Failure(failureClass, code, diagnostic);
    }

    /// <summary>H14m's typed ParametersJson shape.</summary>
    public sealed record Parameters(
        [property: JsonPropertyName("tenantId")] string TenantId,
        [property: JsonPropertyName("uamiClientId")] string UamiClientId,
        [property: JsonPropertyName("scopeGroupId")] string ScopeGroupId,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("mailboxAddress")] string MailboxAddress,
        [property: JsonPropertyName("dataverseUrl")] string DataverseUrl,
        [property: JsonPropertyName("bffAppRegId")] string BffAppRegId);
}
