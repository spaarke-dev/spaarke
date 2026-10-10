// -----------------------------------------------------------------------------
// ICommunicationAccountStore.cs
//
// Task 263 (#1562): the stamp's sprk_communicationaccount row for the customer's shared mailbox — the record the BFF's
// Communication module reads (CommunicationAccountService: send-enabled / receive-enabled accounts, the default
// sender) and GraphSubscriptionManager subscribes. Until T263 an operator created it by hand and no guide said so.
//
//   EnsureVerifiedAsync (H14m) — matched by sprk_emailaddress:
//     none                                   → POST one shared, send + receive, default-sender, Verified row;
//     one active shared row, Verified        → adopt, NOTHING written (a second run writes nothing);
//     one active shared row, empty / Pending → PATCH the three verification fields only;
//     one row whose status is Failed         → VerificationFailed (the BFF's own Graph test failed; never overwritten);
//     inactive / another type / more than one → Conflict, nothing written.
//   ReadAsync (H13) — every row with the address, read-only.
//
// "Verified" here = H14m's Exchange authorization test passed (configuration); the row's sprk_verificationmessage says
// so. The BFF's user-facing verify (Graph send/read) can re-run it any time (design note §1).
//
// SEAM JUSTIFICATION (ADR-010; CLAUDE.md §11): Existing — no L2 component writes sprk_communicationaccount;
// Extension — the HTTP + credential plumbing is H7b's (same identity, EnvVarValuesOptions, WorkerDataverseCredentialFactory),
// reused rather than copied into a third configuration; Cost of doing nothing — a provisioned stamp has no mailbox
// record, so the Communication module sends and receives nothing until someone hand-creates it (#1562).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>Ensures (H14m) and reads (H13) the stamp's <c>sprk_communicationaccount</c> row for the customer mailbox.</summary>
public interface ICommunicationAccountStore
{
    /// <summary>Get-before-set ensure of one verified row. MUST NOT throw for HTTP, auth or Dataverse errors.</summary>
    Task<CommunicationAccountEnsureOutcome> EnsureVerifiedAsync(
        CommunicationAccountTarget target, CommunicationAccountSpec spec, CancellationToken cancellationToken);

    /// <summary>Read-only: every row carrying <paramref name="emailAddress"/>. MUST NOT throw for HTTP, auth or Dataverse errors.</summary>
    Task<CommunicationAccountReadOutcome> ReadAsync(
        CommunicationAccountTarget target, string emailAddress, CancellationToken cancellationToken);
}

/// <summary>The stamp's Dataverse and the identity that signs in (the BFF app registration — same as H7/H7b).</summary>
public sealed record CommunicationAccountTarget(string DataverseUrl, string TenantId, string ClientId);

/// <summary>The row H14m expects.</summary>
/// <param name="Name">sprk_name (primary name; required column) — the customer's display name.</param>
/// <param name="EmailAddress">sprk_emailaddress — the shared mailbox's primary SMTP address.</param>
/// <param name="DisplayName">sprk_displayname.</param>
/// <param name="SecurityGroupId">sprk_securitygroupid — the Exchange scope group (documents the RBAC scope).</param>
/// <param name="VerificationMessage">sprk_verificationmessage written with Verified.</param>
public sealed record CommunicationAccountSpec(
    string Name, string EmailAddress, string DisplayName, string SecurityGroupId, string VerificationMessage);

/// <summary>One row as read.</summary>
public sealed record CommunicationAccountRow(
    Guid Id, string EmailAddress, int StateCode, int? AccountType, int? VerificationStatus, bool SendEnabled, bool ReceiveEnabled,
    string? VerificationMessage);

/// <summary>The Dataverse choice values the store and H13 use (sprk_communicationaccount; BFF Models/*.cs).</summary>
public static class CommunicationAccountValues
{
    /// <summary>sprk_accounttype = Shared Account.</summary>
    public const int SharedAccount = 100000000;

    /// <summary>sprk_authmethod = App-Only (client credentials).</summary>
    public const int AppOnly = 100000000;

    /// <summary>sprk_verificationstatus = Verified.</summary>
    public const int Verified = 100000000;

    /// <summary>sprk_verificationstatus = Failed.</summary>
    public const int Failed = 100000001;

    /// <summary>sprk_verificationstatus = Pending.</summary>
    public const int Pending = 100000002;
}

/// <summary>Outcome of <see cref="ICommunicationAccountStore.EnsureVerifiedAsync"/>.</summary>
public abstract record CommunicationAccountEnsureOutcome
{
    private CommunicationAccountEnsureOutcome() { }

    /// <summary>One active, shared, Verified row exists.</summary>
    /// <param name="Written">Whether this call wrote (POST or verification PATCH); false = nothing written.</param>
    public sealed record Ready(Guid AccountId, bool Written) : CommunicationAccountEnsureOutcome;

    /// <summary>The row exists and the BFF's own Graph verification FAILED — not overwritten.</summary>
    public sealed record VerificationFailed(Guid AccountId, string? Message) : CommunicationAccountEnsureOutcome;

    /// <summary>Inactive row, another account type, or more than one row — nothing written.</summary>
    public sealed record Conflict(string Diagnostic) : CommunicationAccountEnsureOutcome;

    /// <summary>No conclusive result (auth, HTTP, Dataverse).</summary>
    public sealed record Failure(string Diagnostic) : CommunicationAccountEnsureOutcome;
}

/// <summary>Outcome of <see cref="ICommunicationAccountStore.ReadAsync"/>.</summary>
public abstract record CommunicationAccountReadOutcome
{
    private CommunicationAccountReadOutcome() { }

    /// <summary>The read reached Dataverse.</summary>
    public sealed record Found(IReadOnlyList<CommunicationAccountRow> Rows) : CommunicationAccountReadOutcome;

    /// <summary>No conclusive answer.</summary>
    public sealed record Failure(string Diagnostic) : CommunicationAccountReadOutcome;
}
