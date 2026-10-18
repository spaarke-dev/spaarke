using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// unified-access-control-r2 task 036 (FR-20): <see cref="CallerRecordAccessProbe"/> substituted at its module boundary —
/// the caller's own Dataverse rights per (entity set, record id), as the write routes ask for them. Unanswered pairs are
/// <see cref="AccessRights.None"/>, the probe's own fail-closed answer. Every call is recorded, so a test can assert that a
/// caller whose access Dataverse did NOT decide is never asked about (flag off is byte-equivalent).
/// </summary>
/// <remarks>Not a transport mock (ADR-038 §7 B1): it overrides the probe's virtual question, never its HTTP.</remarks>
public sealed class CallerRecordAccessProbeDouble : CallerRecordAccessProbe
{
    public CallerRecordAccessProbeDouble()
        : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
    }

    /// <summary>Dataverse's answer per (entity set, record id).</summary>
    public ConcurrentDictionary<(string EntitySet, Guid RecordId), AccessRights> Answers { get; } = new();

    /// <summary>When set, the question throws (an unexpected fault; the real probe answers None instead).</summary>
    public Exception? Fault { get; set; }

    /// <summary>Every question asked, in order.</summary>
    public ConcurrentQueue<(string? Token, string EntitySet, Guid RecordId)> Calls { get; } = new();

    public CallerRecordAccessProbeDouble Answer(string entitySet, Guid recordId, AccessRights rights)
    {
        Answers[(entitySet, recordId)] = rights;
        return this;
    }

    /// <summary>Task 036 fix round 1 (F1): the table privileges the caller holds. Any other name is not held, the real
    /// probe's fail-closed answer.</summary>
    public ConcurrentDictionary<string, bool> HeldPrivileges { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, the privilege question throws (an unexpected fault; the real probe answers false instead).</summary>
    public Exception? PrivilegeFault { get; set; }

    /// <summary>Every privilege asked about, in order.</summary>
    public ConcurrentQueue<string> PrivilegeCalls { get; } = new();

    public CallerRecordAccessProbeDouble Holds(params string[] privileges)
    {
        foreach (var privilege in privileges)
        {
            HeldPrivileges[privilege] = true;
        }

        return this;
    }

    // ── Task 036 owner decision (a): the app-only questions naming a WorkforceHome-linked systemuser. Kept apart from the
    //    OBO answers on purpose: a test that seeds only these proves the route asked app-only, because the OBO path (whose
    //    real exchange cannot succeed for a customer-directory token) finds nothing and answers None.

    /// <summary>Dataverse's app-only answer per (named systemuser, entity set, record id).</summary>
    public ConcurrentDictionary<(Guid SystemUserId, string EntitySet, Guid RecordId), AccessRights> AppOnlyAnswers { get; } = new();

    /// <summary>The privileges each named systemuser holds, asked app-only. Any other pair is not held.</summary>
    public ConcurrentDictionary<(Guid SystemUserId, string Privilege), bool> AppOnlyHeldPrivileges { get; } = new();

    /// <summary>When set, the app-only rights question throws (an unexpected fault; the real probe answers None).</summary>
    public Exception? AppOnlyFault { get; set; }

    /// <summary>When set, the app-only privilege question throws (the real probe answers false).</summary>
    public Exception? AppOnlyPrivilegeFault { get; set; }

    /// <summary>Every app-only rights question asked, in order.</summary>
    public ConcurrentQueue<(Guid SystemUserId, string EntitySet, Guid RecordId)> AppOnlyCalls { get; } = new();

    /// <summary>Every app-only privilege question asked, in order.</summary>
    public ConcurrentQueue<(Guid SystemUserId, string Privilege)> AppOnlyPrivilegeCalls { get; } = new();

    public CallerRecordAccessProbeDouble AnswerAppOnly(Guid systemUserId, string entitySet, Guid recordId, AccessRights rights)
    {
        AppOnlyAnswers[(systemUserId, entitySet, recordId)] = rights;
        return this;
    }

    public CallerRecordAccessProbeDouble HoldsAppOnly(Guid systemUserId, params string[] privileges)
    {
        foreach (var privilege in privileges)
        {
            AppOnlyHeldPrivileges[(systemUserId, privilege)] = true;
        }

        return this;
    }

    public void Reset()
    {
        Answers.Clear();
        Fault = null;
        Calls.Clear();
        HeldPrivileges.Clear();
        PrivilegeFault = null;
        PrivilegeCalls.Clear();
        AppOnlyAnswers.Clear();
        AppOnlyHeldPrivileges.Clear();
        AppOnlyFault = null;
        AppOnlyPrivilegeFault = null;
        AppOnlyCalls.Clear();
        AppOnlyPrivilegeCalls.Clear();
    }

    public override Task<AccessRights> GetPrincipalRightsAppOnlyAsync(
        Guid principalSystemUserId, string entitySet, Guid recordId, CancellationToken ct = default)
    {
        AppOnlyCalls.Enqueue((principalSystemUserId, entitySet, recordId));
        if (AppOnlyFault is not null)
        {
            throw AppOnlyFault;
        }

        return Task.FromResult(AppOnlyAnswers.TryGetValue((principalSystemUserId, entitySet, recordId), out var rights)
            ? rights
            : AccessRights.None);
    }

    public override Task<bool> PrincipalHoldsPrivilegeAppOnlyAsync(
        Guid principalSystemUserId, string privilegeName, CancellationToken ct = default)
    {
        AppOnlyPrivilegeCalls.Enqueue((principalSystemUserId, privilegeName));
        if (AppOnlyPrivilegeFault is not null)
        {
            throw AppOnlyPrivilegeFault;
        }

        return Task.FromResult(AppOnlyHeldPrivileges.TryGetValue((principalSystemUserId, privilegeName), out var held) && held);
    }

    public override Task<bool> CallerHoldsPrivilegeAsync(
        string? callerBearerToken, string privilegeName, CancellationToken ct = default)
    {
        PrivilegeCalls.Enqueue(privilegeName);
        if (PrivilegeFault is not null)
        {
            throw PrivilegeFault;
        }

        return Task.FromResult(HeldPrivileges.TryGetValue(privilegeName, out var held) && held);
    }

    public override Task<AccessRights> GetCallerRightsAsync(
        string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
    {
        Calls.Enqueue((callerBearerToken, entitySet, recordId));
        if (Fault is not null)
        {
            throw Fault;
        }

        return Task.FromResult(Answers.TryGetValue((entitySet, recordId), out var rights) ? rights : AccessRights.None);
    }
}
