using System.Collections.Concurrent;
using Sprk.Bff.Api.Services.Dataverse.Privileges;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// unified-access-control-r2 task 099b review round 2: a module-boundary double for the OWNER-TEAM question of
/// <see cref="IDataversePrivilegeChecker"/> (<see cref="IDataversePrivilegeChecker.TeamHasReadPrivilegeAsync"/>) — whether a
/// team's roles hold Read on a table, which the grant routes and the secure-transition child pass ask before a grant row is
/// created for, or assigned to, a team. Every team holds Read unless a test says otherwise; it records every question.
/// </summary>
/// <remarks>
/// The user questions (<see cref="IDataversePrivilegeChecker.HasReadPrivilegeAsync"/>,
/// <see cref="IDataversePrivilegeChecker.GetReadableEntitiesAsync"/>) and the task 186 Workforce-role ceiling
/// (<see cref="IDataversePrivilegeChecker.GetWorkforceRoleCeilingAsync"/>) are not this double's: they throw, so a test that
/// reaches them through it fails loudly instead of being answered.
/// </remarks>
public sealed class TeamPrivilegeCheckerDouble : IDataversePrivilegeChecker
{
    /// <summary>Teams whose roles do NOT hold Read on any table (the Secure Record Owner role before the prerequisite).</summary>
    public HashSet<Guid> TeamsLackingRead { get; } = new();

    /// <summary>
    /// Review round 5 (K2): teams with a YES the real checker remembered (its per-team cache), seeded by a test. A question
    /// that is not fresh is answered YES from it, as <c>UserPrivilegeChecker</c> answers from its cache; a fresh question
    /// reads the truth, and a NO drops the remembered YES (the checker's eviction).
    /// </summary>
    public HashSet<Guid> RememberedYes { get; } = new();

    /// <summary>When set, every team question throws it (the privileges could not be read).</summary>
    public Exception? Fault { get; set; }

    /// <summary>Every team question, in order, with whether it was asked fresh (review round 3, K2).</summary>
    public ConcurrentQueue<(Guid TeamId, string Table, bool Fresh)> TeamQuestions { get; } = new();

    /// <summary>A double in which <paramref name="teamId"/> lacks Read.</summary>
    public static TeamPrivilegeCheckerDouble Lacking(Guid teamId)
    {
        var double_ = new TeamPrivilegeCheckerDouble();
        double_.TeamsLackingRead.Add(teamId);
        return double_;
    }

    public Task<bool> TeamHasReadPrivilegeAsync(Guid teamId, string entityLogicalName, bool fresh, CancellationToken ct)
    {
        TeamQuestions.Enqueue((teamId, entityLogicalName, fresh));
        if (!fresh && RememberedYes.Contains(teamId))
            return Task.FromResult(true);
        if (Fault is not null)
            return Task.FromException<bool>(Fault);
        var holds = !TeamsLackingRead.Contains(teamId);
        if (!holds)
            RememberedYes.Remove(teamId);
        return Task.FromResult(holds);
    }

    public Task<bool> HasReadPrivilegeAsync(Guid userOid, string entityLogicalName, CancellationToken ct) =>
        throw new NotSupportedException("TeamPrivilegeCheckerDouble answers owner-team questions only.");

    public Task<IReadOnlySet<string>> GetReadableEntitiesAsync(Guid userOid, CancellationToken ct) =>
        throw new NotSupportedException("TeamPrivilegeCheckerDouble answers owner-team questions only.");

    public Task<WorkforceRoleCeiling> GetWorkforceRoleCeilingAsync(CancellationToken ct) =>
        throw new NotSupportedException("TeamPrivilegeCheckerDouble answers owner-team questions only.");
}
