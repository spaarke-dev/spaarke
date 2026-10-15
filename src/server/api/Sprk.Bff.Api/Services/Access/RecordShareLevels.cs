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
/// carries — unified-access-control-r2 task 063, spec FR-29. The levels were set by the owner on 2026-09-15 and
/// re-set on 2026-09-30 (task 139, defect C4).
/// </summary>
/// <remarks>
/// <para><b>The levels (owner, 2026-09-30).</b> View Only = Read. Collaborate = Read, Write, Append, AppendTo and
/// Share — the working access a colleague receives at secure-project provisioning (whose constants point here),
/// plus the right to pass access on. Full Access = Collaborate + Delete. <b>Assign is at no level</b>: a person given
/// access through the "+ User" picker cannot take the record over.</para>
///
/// <para><b>Why Collaborate and Full Access carry Share.</b> The owner's rule: "if the user has write access, then
/// they can share (OOB functionality) and grant access to users/contacts/organizations"; only a View holder may not
/// pass access on. Every Spaarke grant route already admits a Write-holder (DelegationRuleFilter, B-14). Without
/// <c>ShareAccess</c> on the share row, the same colleague could not use Dataverse's own Share command in the
/// model-driven app, so the two surfaces disagreed. Dataverse's native sharing rule still stops an OOB sharer handing
/// out a right they do not hold, and every Spaarke grant route caps a grant at the grantor's own level. This
/// SUPERSEDES the 2026-09-15 decision recorded in notes/task-063-internal-user-share-endpoints.md §4 (see
/// notes/design-register.md, B-14a).</para>
///
/// <para><b>Legacy masks.</b> Shares written before 2026-09-30 store Collaborate as 23 and Full Access as 65559
/// (no Share). <see cref="LevelForMask"/> still reads them as their level, so an existing share keeps showing its
/// level; <c>scripts/Upgrade-LegacyRecordShareMasks.ps1</c> (operator-run, dry-run by default) upgrades them. The old
/// provisioning creator mask (Collaborate + Share = 262167) is exactly the new Collaborate mask and needs nothing.</para>
///
/// <para><b>The masks are Dataverse's numbers, not <see cref="Spaarke.Dataverse.AccessRights"/>.</b> That enum is
/// Spaarke's evaluation vocabulary with its own bit layout — its Delete is 4, which on a share row means Append, and
/// its Share is 64. A share row stores Dataverse's <c>AccessRights</c> values, read by reflection from
/// <c>Microsoft.Crm.Sdk.Proxy</c> 1.2.26: Read 1, Write 2, Append 4, AppendTo 16, Create 32, Delete 65536, Share
/// 262144, Assign 524288. The share endpoint confirms each write by comparing the stored mask with these numbers, so a
/// mask built from the other vocabulary would fail every confirmation — or pass one for the wrong rights.</para>
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
    private const int Share = 262144;

    private const int ViewOnlyMask = Read;
    private const int CollaborateMask = Read | Write | Append | AppendTo | Share;
    private const int FullAccessMask = CollaborateMask | Delete;

    // What Collaborate and Full Access stored before task 139 added Share (2026-09-30). READ-compatible only: nothing
    // writes these any more, and the operator backfill upgrades the rows that still carry them.
    private const int LegacyCollaborateMask = Read | Write | Append | AppendTo;
    private const int LegacyFullAccessMask = LegacyCollaborateMask | Delete;

    /// <summary>View Only: read the record.</summary>
    internal const string ViewOnlyRights = "ReadAccess";

    /// <summary>
    /// Collaborate: work the record and pass access on. Also the creator AND colleague share at secure-project
    /// provisioning (owner 2026-09-30: colleagues receive exactly the creator's rights).
    /// </summary>
    internal const string CollaborateRights = "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess";

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
    /// outside the three levels — a share made in the Dataverse UI with Create or Assign, say — has no level; naming
    /// one would under- or over-state what it grants.
    /// </summary>
    /// <remarks>
    /// The two LEGACY masks (23 and 65559, written before Share joined the levels on 2026-09-30) still read as
    /// Collaborate and Full Access. Without that, every share made before task 139 would show "no level" in
    /// <c>/user-shares</c> and the Manage Access dialog until the backfill ran. They are read-compatible only:
    /// <see cref="TryGetRights"/> never produces them, so a new write always carries the current mask.
    /// </remarks>
    internal static ExternalAccessLevel? LevelForMask(int accessRightsMask) => accessRightsMask switch
    {
        ViewOnlyMask => ExternalAccessLevel.ViewOnly,
        CollaborateMask or LegacyCollaborateMask => ExternalAccessLevel.Collaborate,
        FullAccessMask or LegacyFullAccessMask => ExternalAccessLevel.FullAccess,
        _ => null
    };

    /// <summary>
    /// The rights a level can carry, paired across the TWO vocabularies: the Dataverse <c>AccessMask</c> name and bit
    /// a POA row stores, and the <see cref="AccessRights"/> flag the evaluator reports for the same right.
    /// </summary>
    /// <remarks>
    /// This is the only place in the codebase where the two vocabularies meet. Everything else stays inside one of
    /// them, which is what keeps Dataverse's Delete (65536) from ever being read as Spaarke's Delete (4). The order is
    /// the canonical order the CSV is emitted in, so the same set of rights always produces the same literal — and it
    /// is the order the level constants above are written in (Share before Delete), so an intersection that keeps
    /// every right reproduces the level's own literal.
    /// <para><b>Share is a row here since task 139.</b> A right with no row is silently DROPPED by
    /// <see cref="Intersect"/>; adding Share to the levels without this row would have stripped it from every share,
    /// whatever the caller held. With the row, Share survives the intersection only when the caller holds
    /// <see cref="AccessRights.Share"/> (Spaarke 64 ↔ Dataverse 262144).</para>
    /// </remarks>
    private static readonly (string Name, int DataverseBit, AccessRights Flag)[] LevelRights =
    {
        ("ReadAccess", Read, AccessRights.Read),
        ("WriteAccess", Write, AccessRights.Write),
        ("AppendAccess", Append, AccessRights.Append),
        ("AppendToAccess", AppendTo, AccessRights.AppendTo),
        ("ShareAccess", Share, AccessRights.Share),
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

    /// <summary>Whether a stored share mask carries Read — whether its holder can see the record at all.</summary>
    internal static bool CanRead(int accessRightsMask) => (accessRightsMask & Read) == Read;

    /// <summary>
    /// Whether replacing <paramref name="currentMask"/> with <paramref name="newMask"/> would take away a right the
    /// holder has now — the "never silently lower" test (task 139). ModifyAccess REPLACES the rights, so any bit in
    /// the current mask that the new one lacks is lost.
    /// </summary>
    internal static bool WouldRemoveRights(int currentMask, int newMask) => (currentMask & ~newMask) != 0;

    // ── Child mirrors (unified-access-control-r2 task 149, C10 part 2) ──────────────────────────────────────────────

    /// <summary>
    /// The rights a share on a secure ROOT may carry onto one of its children: Read, Write, Append, AppendTo, Delete and
    /// Share. Never Assign, never Create. Each is carried only when the root share holds it (<see cref="ChildMirrorMask"/>).
    /// </summary>
    /// <remarks>
    /// <para><b>Share is carried since owner round 91 (2026-10-10, task 179)</b>, replacing round 11 item 4 ("ShareAccess is
    /// NOT mirrored onto children"). Managing access needs the Share privilege (round 89), and Dataverse reports
    /// <c>ShareAccess</c> on a record only where the caller's share or role gives it. A sharee of a secure root whose own
    /// share carries Share (Collaborate or Full Access) therefore lost Manage Access on every filed child, the sibling
    /// business unit case of SECURE-PROJECT-ENVIRONMENT-SETUP §6 included. The owner chose to mirror it. It is never wider
    /// than the root: a root share without Share mirrors none. The trade-off round 11 named is accepted: a child sharee can
    /// share that ONE child with someone the root is not shared with, and the reconcile then removes that share (children
    /// follow their roots); sharing a secure family is still done at the root, which fans out.</para>
    /// <para><b>No Assign</b>: no level carries it, and a child's owner is the Secure team's (task 146). <b>No Create</b>:
    /// it means nothing on a share of an existing row.</para>
    /// </remarks>
    internal const int ChildMirrorableMask = Read | Write | Append | AppendTo | Delete | Share;

    /// <summary>
    /// What a principal holding <paramref name="rootMask"/> on a secure root holds on each of its children: the root's
    /// rights restricted to <see cref="ChildMirrorableMask"/>. Never wider than the root, by construction.
    /// </summary>
    internal static int ChildMirrorMask(int rootMask) => rootMask & ChildMirrorableMask;

    /// <summary>
    /// The <c>AccessMask</c> literal for a mask built from <see cref="ChildMirrorableMask"/> bits, in the canonical
    /// order. A bit outside the mirrorable set is refused rather than silently dropped.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The mask carries a bit a child mirror never carries.</exception>
    internal static RecordShareRights ChildMirrorRights(int mask)
    {
        if ((mask & ~ChildMirrorableMask) != 0)
            throw new ArgumentOutOfRangeException(nameof(mask), mask, "A child mirror carries only Read, Write, Append, AppendTo, Delete and Share.");

        var names = LevelRights.Where(r => (mask & r.DataverseBit) != 0).Select(r => r.Name);
        return new RecordShareRights(string.Join(",", names), mask);
    }

    /// <summary>
    /// Every right a POA share row can carry, as the Web API <c>AccessMask</c> name and the bit Dataverse stores —
    /// the level rights plus the two no level carries (Create, Assign). Same numbers as the constants above.
    /// </summary>
    private static readonly (string Name, int DataverseBit)[] AllShareRights =
    {
        ("ReadAccess", Read),
        ("WriteAccess", Write),
        ("AppendAccess", Append),
        ("AppendToAccess", AppendTo),
        ("CreateAccess", 32),
        ("DeleteAccess", Delete),
        ("ShareAccess", Share),
        ("AssignAccess", 524288),
    };

    /// <summary>
    /// The mask Dataverse stores for an <c>AccessMask</c> literal — so a caller can confirm a write by comparing the
    /// stored mask with what it asked for, including a mask no level names (unified-access-control-r2 task 133: the
    /// provisioning creator share, and the compensation that restores a share read before the call).
    /// </summary>
    /// <exception cref="ArgumentException">A name in the literal is not a Dataverse access right. An unknown name
    /// never becomes a guessed bit.</exception>
    internal static int MaskForRightsCsv(string accessRightsCsv)
    {
        var mask = 0;
        foreach (var name in accessRightsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Array.FindIndex(AllShareRights, r => string.Equals(r.Name, name, StringComparison.Ordinal));
            if (match < 0)
                throw new ArgumentException($"'{name}' is not a Dataverse access right.", nameof(accessRightsCsv));

            mask |= AllShareRights[match].DataverseBit;
        }

        return mask;
    }

    /// <summary>
    /// The <c>AccessMask</c> literal that restores a share to exactly <paramref name="accessRightsMask"/> through
    /// ModifyAccess — the inverse of <see cref="MaskForRightsCsv"/> (task 133: compensation puts a creator's share
    /// back to the mask read before the call).
    /// </summary>
    /// <exception cref="ArgumentException">The mask carries a bit no access right names, or is zero (a zero mask is
    /// a revoke, not a modify).</exception>
    internal static string RightsCsvForMask(int accessRightsMask)
    {
        var names = new List<string>(AllShareRights.Length);
        var covered = 0;
        foreach (var (name, bit) in AllShareRights)
        {
            if ((accessRightsMask & bit) == 0)
                continue;

            names.Add(name);
            covered |= bit;
        }

        if (accessRightsMask == 0 || covered != accessRightsMask)
            throw new ArgumentException($"Mask {accessRightsMask} is not expressible as Dataverse access rights.", nameof(accessRightsMask));

        return string.Join(",", names);
    }
}
