using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// What an internal (POA) share of one level carries: the rights literal GrantAccess and ModifyAccess send, and the
/// <c>accessrightsmask</c> Dataverse stores for those rights.
/// </summary>
/// <param name="AccessRightsCsv">The Web API <c>AccessMask</c> literal, e.g. <c>"ReadAccess,WriteAccess"</c>.</param>
/// <param name="AccessRightsMask">The same rights as the bitmask Dataverse stores on the share row.</param>
internal readonly record struct RecordShareRights(string AccessRightsCsv, int AccessRightsMask);

/// <summary>
/// The ONE mapping from a share level to the Dataverse rights an internal system-user (POA) share of that level
/// carries — unified-access-control-r2 task 063, spec FR-29. The levels were set by the owner on 2026-09-15.
/// </summary>
/// <remarks>
/// <para><b>The levels.</b> View Only = Read. Collaborate = Read, Write, Append and AppendTo — the working access a
/// colleague already receives at secure-project provisioning, whose constant now points here. Full Access =
/// Collaborate + Delete. <b>Share and Assign are at no level</b> (owner, "No re-share at any level"): a person given
/// access through the "+ User" picker cannot pass it on, or take the record over.</para>
///
/// <para><b>The masks are Dataverse's numbers, not <see cref="Spaarke.Dataverse.AccessRights"/>.</b> That enum is
/// Spaarke's evaluation vocabulary with its own bit layout — its Delete is 4, which on a share row means Append. A
/// share row stores Dataverse's <c>AccessRights</c> values, read by reflection from <c>Microsoft.Crm.Sdk.Proxy</c>
/// 1.2.26: Read 1, Write 2, Append 4, AppendTo 16, Create 32, Delete 65536, Share 262144, Assign 524288. The share
/// endpoint confirms each write by comparing the stored mask with these numbers, so a mask built from the other
/// vocabulary would fail every confirmation — or pass one for the wrong rights.</para>
///
/// <para><b>Not <see cref="ExternalAccessLevels.ToAccessRights"/>.</b> That table decides what an EXTERNAL contact's
/// grant lets the evaluator admit, and it includes Create, which means nothing on a share of an existing record.
/// The two tables use the same three level names to answer different questions.</para>
///
/// <para><b>The levels NEST, and that is load-bearing</b> (Step 9.5 review, task 063): View Only ⊂ Collaborate ⊂
/// Full Access. The share endpoint's "no direct share → GrantAccess" branch is race-safe only because of it — two
/// concurrent creates union to the wider REQUESTED level, so neither caller is answered success over rights wider
/// than it asked for. A future NON-nested level (say a "Delete only") breaks that: two racing creates would union
/// to a mask wider than either request, both callers would get 500 "not confirmed", and the record would be left
/// elevated with no response claiming it. Add such a level only with the concurrency path revisited.</para>
/// </remarks>
internal static class RecordShareLevels
{
    // Dataverse AccessRights values (see the remarks). Private: a mask is built here and nowhere else.
    private const int Read = 1;
    private const int Write = 2;
    private const int Append = 4;
    private const int AppendTo = 16;
    private const int Delete = 65536;

    private const int ViewOnlyMask = Read;
    private const int CollaborateMask = Read | Write | Append | AppendTo;
    private const int FullAccessMask = CollaborateMask | Delete;

    /// <summary>View Only: read the record.</summary>
    internal const string ViewOnlyRights = "ReadAccess";

    /// <summary>Collaborate: work the record. Also the colleague share at secure-project provisioning.</summary>
    internal const string CollaborateRights = "ReadAccess,WriteAccess,AppendAccess,AppendToAccess";

    /// <summary>Full Access: Collaborate, plus delete the record.</summary>
    internal const string FullAccessRights = CollaborateRights + ",DeleteAccess";

    /// <summary>
    /// The rights for <paramref name="level"/>. <c>false</c> for <c>null</c> or any value outside the three levels —
    /// an unrecognised level never becomes a share.
    /// </summary>
    internal static bool TryGetRights(ExternalAccessLevel? level, out RecordShareRights rights)
    {
        switch (level)
        {
            case ExternalAccessLevel.ViewOnly:
                rights = new RecordShareRights(ViewOnlyRights, ViewOnlyMask);
                return true;
            case ExternalAccessLevel.Collaborate:
                rights = new RecordShareRights(CollaborateRights, CollaborateMask);
                return true;
            case ExternalAccessLevel.FullAccess:
                rights = new RecordShareRights(FullAccessRights, FullAccessMask);
                return true;
            default:
                rights = default;
                return false;
        }
    }

    /// <summary>
    /// The level whose rights are EXACTLY <paramref name="accessRightsMask"/>, or <c>null</c>. A share holding rights
    /// outside the three levels — the provisioning creator's Share right, a share made in the Dataverse UI — has no
    /// level; naming one would under- or over-state what it grants.
    /// </summary>
    internal static ExternalAccessLevel? LevelForMask(int accessRightsMask) => accessRightsMask switch
    {
        ViewOnlyMask => ExternalAccessLevel.ViewOnly,
        CollaborateMask => ExternalAccessLevel.Collaborate,
        FullAccessMask => ExternalAccessLevel.FullAccess,
        _ => null
    };

    /// <summary>
    /// The rights a level can carry, paired across the TWO vocabularies: the Dataverse <c>AccessMask</c> name and bit
    /// a POA row stores, and the <see cref="AccessRights"/> flag the evaluator reports for the same right.
    /// </summary>
    /// <remarks>
    /// This is the only place in the codebase where the two vocabularies meet. Everything else stays inside one of
    /// them, which is what keeps Dataverse's Delete (65536) from ever being read as Spaarke's Delete (4). The order is
    /// the canonical order the CSV is emitted in, so the same set of rights always produces the same literal.
    /// </remarks>
    private static readonly (string Name, int DataverseBit, AccessRights Flag)[] LevelRights =
    {
        ("ReadAccess", Read, AccessRights.Read),
        ("WriteAccess", Write, AccessRights.Write),
        ("AppendAccess", Append, AccessRights.Append),
        ("AppendToAccess", AppendTo, AccessRights.AppendTo),
        ("DeleteAccess", Delete, AccessRights.Delete),
    };

    /// <summary>
    /// The rights of <paramref name="requested"/> that <paramref name="callerRights"/> also holds — <b>you may grant
    /// only what you hold</b> (owner decision 2026-09-16).
    /// </summary>
    /// <remarks>
    /// <para>Dataverse applies this rule natively when a USER shares a record, and cannot apply it here: the POA write
    /// is app-only, so the platform sees the application's rights rather than the caller's. Intersecting restores the
    /// rule the platform would have enforced — a caller who cannot delete a record cannot hand Delete to anyone else,
    /// themselves included, which is the escalation path this closes.</para>
    /// <para>A result with no Read grants nothing readable; <see cref="IsGrantable"/> is that check, and the share
    /// endpoint refuses on it. <c>CallerRecordAccessProbe</c> answers <see cref="AccessRights.None"/> both for "no
    /// rights" and for "could not answer" — deliberately indistinguishable — so an unanswerable probe lands in the
    /// same refusal, which is the fail-closed direction.</para>
    /// </remarks>
    internal static RecordShareRights Intersect(RecordShareRights requested, AccessRights callerRights)
    {
        var names = new List<string>(LevelRights.Length);
        var mask = 0;

        foreach (var (name, dataverseBit, flag) in LevelRights)
        {
            if ((requested.AccessRightsMask & dataverseBit) == 0)
                continue;

            if ((callerRights & flag) != flag)
                continue;

            names.Add(name);
            mask |= dataverseBit;
        }

        return new RecordShareRights(string.Join(",", names), mask);
    }

    /// <summary>Whether these rights are worth writing as a share at all: without Read, a share grants nothing readable.</summary>
    internal static bool IsGrantable(RecordShareRights rights) => (rights.AccessRightsMask & Read) == Read;
}
