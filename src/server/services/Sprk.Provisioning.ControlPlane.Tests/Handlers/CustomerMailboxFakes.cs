// -----------------------------------------------------------------------------
// CustomerMailboxFakes.cs
//
// Task 263: hand-written fakes for H14m's two seams (ICustomerMailboxClient, ICommunicationAccountStore), shared by
// the H14m, H14 (parent), CustomerMailboxVerifier and H13 tests. Each records every call so a test can assert
// "nothing was written" (ADR-038: fakes over mocks for seams with behaviour).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

internal sealed class FakeCustomerMailboxClient : ICustomerMailboxClient
{
    public static readonly IReadOnlyList<string> Roles = new[] { "Application Mail.Read", "Application Mail.ReadWrite", "Application Mail.Send" };

    public CustomerMailboxEnsureOutcome EnsureOutcome { get; set; } = Ensured(created: true);
    public CustomerMailboxReadOutcome ReadOutcome { get; set; } = InPlace();
    public List<CustomerMailboxRequest> EnsureCalls { get; } = new();
    public List<CustomerMailboxRequest> ReadCalls { get; } = new();

    public static CustomerMailboxEnsureOutcome Ensured(bool created, bool verified = true)
        => new CustomerMailboxEnsureOutcome.Ensured(created, verified, Roles.Select(r => new CustomerMailboxAuthorization(r, verified)).ToArray(), "ok");

    public static CustomerMailboxReadOutcome InPlace(params string[] conflicts)
        => new CustomerMailboxReadOutcome.Success(true, conflicts, Roles.Select(r => new CustomerMailboxAuthorization(r, true)).ToArray(), "found");

    public Task<CustomerMailboxEnsureOutcome> EnsureAsync(CustomerMailboxRequest request, CancellationToken cancellationToken)
    {
        EnsureCalls.Add(request);
        return Task.FromResult(EnsureOutcome);
    }

    public Task<CustomerMailboxReadOutcome> ReadAsync(CustomerMailboxRequest request, CancellationToken cancellationToken)
    {
        ReadCalls.Add(request);
        return Task.FromResult(ReadOutcome);
    }
}

internal sealed class FakeCommunicationAccountStore : ICommunicationAccountStore
{
    public static readonly Guid AccountId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public CommunicationAccountEnsureOutcome EnsureOutcome { get; set; } = new CommunicationAccountEnsureOutcome.Ready(AccountId, Written: true);
    public CommunicationAccountReadOutcome ReadOutcome { get; set; } = new CommunicationAccountReadOutcome.Found(new[] { VerifiedRow() });
    public List<(CommunicationAccountTarget Target, CommunicationAccountSpec Spec)> EnsureCalls { get; } = new();
    public List<(CommunicationAccountTarget Target, string Address)> ReadCalls { get; } = new();

    public static CommunicationAccountRow VerifiedRow(int state = 0, int? type = CommunicationAccountValues.SharedAccount, int? status = CommunicationAccountValues.Verified)
        => new(AccountId, "acme@contoso.com", state, type, status, true, true, "verified");

    public Task<CommunicationAccountEnsureOutcome> EnsureVerifiedAsync(CommunicationAccountTarget target, CommunicationAccountSpec spec, CancellationToken cancellationToken)
    {
        EnsureCalls.Add((target, spec));
        return Task.FromResult(EnsureOutcome);
    }

    public Task<CommunicationAccountReadOutcome> ReadAsync(CommunicationAccountTarget target, string emailAddress, CancellationToken cancellationToken)
    {
        ReadCalls.Add((target, emailAddress));
        return Task.FromResult(ReadOutcome);
    }
}
