// unified-access-control-r2 task 175 — owner round 84 (2026-10-08), refined by ROUND 87 (2026-10-09, binding): "the parent
// sets a FLOOR; a child may be stricter". A filed work assignment or project inherits the Secure designation and Access
// Permission of what it is filed under (the most restrictive across its parents and the chain) and is never looser than
// that floor. A user may make it STRICTER by hand; that value is the record's OWN and stays when the parent later loosens —
// only INHERITED values follow the parent down. A re-file never loosens a child. Task 174 already makes ENFORCEMENT use
// max(own, ancestors) whatever the stored values say, so a lagging stored value is never a hole; this file keeps the STORED
// values (and every reader of them, and the forms) right.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — SecureRootInheritance secures a filed record (one direction); the unsecure endpoint un-secures ONE record
//       a caller names (F3); the 174 walk (ReadSecureParentsAsync) answers what a record's parents give it (the floor); task
//       173's ChildAccessPermissionReconciler keeps the four CHILD tables' display copy equal to their parents'. Nothing
//       records which part of a work assignment's / project's values is its own and which is inherited.
//   (2) Extension — It IS an extension of SecureRootInheritance (a partial of the same class: the ONE owner of "a filed work
//       assignment or project follows its parents", called from the job, the parent's unsecure and the re-file paths —
//       CLAUDE.md §17 "one server-side owner per invariant"). The un-secure step is the unsecure endpoint's own steps
//       (UnsecureInheritedAsync, the ProvisionInheritedAsync precedent). The floor is the 174 walk. The own/inherited record
//       is ONE server-owned column, sprk_accessinheritance (see AccessInheritance for why one column and not one per value).
//   (3) Cost of doing nothing — un-securing a matter leaves its filed work assignments isolated for good; a matter turned
//       Standard leaves its children Restricted and one turned Restricted leaves them Standard for every reader of the stored
//       column (#1478); a re-file would un-secure a record without F3 (verifier F1); and a value a user set on a child would
//       be lost when its parent loosens (round 87 item 2).
//
// Placement (bff-extensions.md; ADR-052): BFF — the transition runs inside the parent's unsecure request and the re-file
// writers, the safety net in the existing SecureRootInheritanceJob (ADR-036: no new job, no timer). No package, endpoint,
// interface or DI registration; one Dataverse column (scripts/Set-AccessInheritanceSchema.ps1).

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>What became of one work assignment or project brought into step with its parents (task 175).</summary>
public enum FollowParentsOutcome
{
    /// <summary>The record does not exist (any more).</summary>
    NotFound,

    /// <summary>Filed under nothing: its values are its own and stay editable (F3 still governs removing its Secure).</summary>
    Parentless,

    /// <summary>
    /// What it is filed under, its own flag or its access record could not be read: nothing was loosened, and it is reported.
    /// Enforcement already treats it as secure and Restricted (task 174, fail closed).
    /// </summary>
    Undetermined,

    /// <summary>Its stored values already equal max(own, floor). Nothing was written but, possibly, its access record.</summary>
    InStep,

    /// <summary>Brought into step by this call.</summary>
    Changed,

    /// <summary>
    /// A step did not complete: the record is left at the MORE restrictive state (still secure, Access Permission not
    /// loosened) and reported; the next call or job run completes it.
    /// </summary>
    Incomplete,
}

/// <summary>What <see cref="SecureRootInheritance.FollowParentsAsync"/> did to one record.</summary>
public sealed record FollowParentsResult(string Table, Guid Id, FollowParentsOutcome Outcome)
{
    /// <summary>The un-secure step ran and completed.</summary>
    public const string Unsecured = "unsecured";

    /// <summary>The un-secure step was refused (a 4xx from the endpoint's steps): it stays secure.</summary>
    public const string Refused = "refused";

    /// <summary>The un-secure step failed (a 5xx): it stays secure (or flagged secure), and the job retries it.</summary>
    public const string Failed = "failed";

    /// <summary><see cref="Unsecured"/>, <see cref="Refused"/> or <see cref="Failed"/> when the un-secure step ran; otherwise null.</summary>
    public string? UnsecureOutcome { get; init; }

    /// <summary>The stored Access Permission before, and the value written (null: not written).</summary>
    public int? PermissionFrom { get; init; }

    public int? PermissionTo { get; init; }

    /// <summary>Whether the record is (still) flagged secure after the call, as far as this call knows.</summary>
    public bool IsSecureAfter { get; init; }

    /// <summary>The record's access record (own vs inherited) was written by this call.</summary>
    public bool MarkerWritten { get; init; }

    /// <summary>The DIRECT parents it follows (empty for a parentless or unread record).</summary>
    public IReadOnlyList<SecureFilingParent> Parents { get; init; } = Array.Empty<SecureFilingParent>();

    public string? ReasonCode { get; init; }

    public string? Detail { get; init; }

    /// <summary>Nothing is left to do for this record.</summary>
    public bool IsComplete => Outcome is FollowParentsOutcome.NotFound or FollowParentsOutcome.Parentless
        or FollowParentsOutcome.InStep or FollowParentsOutcome.Changed;

    /// <summary>The call changed an effective value (what is filed below it may follow).</summary>
    public bool WroteAnything => PermissionTo is not null || UnsecureOutcome == Unsecured;
}

/// <summary>
/// A parent's un-secure carried down to everything filed below it (task 175): what was filed (top-down), what each record's
/// follow did, and how many were left to the job (the bound, or a chain deeper than <see cref="SecureRootInheritance.MaxFilingDepth"/>).
/// </summary>
public sealed record FiledCascadePass(
    bool Unreadable, IReadOnlyList<FiledRootRef> Filed, IReadOnlyList<FollowParentsResult> Results, int Deferred)
{
    /// <summary>The records filed below that are (still) flagged secure after the pass, provably filed (a confirmed filing).</summary>
    public IReadOnlyList<FiledRootRef> StillSecure => Filed
        .Where(f => f.Confirmed)
        .Where(f => Results.FirstOrDefault(r => r.Id == f.Id && string.Equals(r.Table, f.Table, StringComparison.OrdinalIgnoreCase))
            is { } r ? r.IsSecureAfter : f.FlaggedSecure)
        .ToList();
}

/// <summary>One run of the job's follow-parents pass (task 175).</summary>
public sealed record FollowParentsPass
{
    public int Listed { get; init; }
    public int Parentless { get; init; }
    public int InStep { get; init; }
    public int Unsecured { get; init; }
    public int PermissionsChanged { get; init; }
    public int MarkersWritten { get; init; }
    public int Undetermined { get; init; }
    public int NotCompleted { get; init; }
    public int Deferred { get; init; }

    /// <summary>The last record the un-secure bound stopped after (the next run continues after it); null when none was deferred.</summary>
    public (string Table, Guid Id)? UnsecureResumeAfter { get; init; }

    /// <summary>Up to 50 "table:id: reason" lines for records not brought into step.</summary>
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

    /// <summary>Up to 200 changes: table, id, what changed.</summary>
    public IReadOnlyList<object> Changes { get; init; } = Array.Empty<object>();

    /// <summary>
    /// Nothing is left that a run can do. Undetermined records are reported, not counted here (task 173's precedent): a record
    /// whose filing cannot be decided is left at its current state, which enforcement already treats as secure and Restricted
    /// (task 174, fail closed); counting it would keep the job failed on every run for one bad row.
    /// </summary>
    public bool IsComplete => NotCompleted == 0 && Deferred == 0;
}

/// <summary>
/// Task 175 (rounds 84 / 87): the refusal for a change that would make a work assignment or project LOOSER than the floor its
/// parents set — removing a Secure designation that comes from a secure parent, or an Access Permission below the parents'.
/// Tightening is never refused. One code for every surface that refuses it: <c>/unsecure-project</c> and a BFF write of
/// <c>sprk_accesspermission</c> / <c>sprk_issecure</c> (<see cref="SecureRootInheritance.CheckRefileAsync"/>).
/// </summary>
public static class AccessFollowsParent
{
    /// <summary>The named 409 reason code.</summary>
    public const string ReasonCode = "sdap.access.access_follows_parent";

    /// <summary>The columns whose floor a record's parents set (round 87).</summary>
    internal static readonly IReadOnlySet<string> LockedColumns =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_accesspermission", "sprk_issecure" };

    /// <summary>"the matter 'X'" (or "the project 'Y'"), with "and N other(s)" when it has several parents.</summary>
    internal static string Describe(IReadOnlyList<SecureFilingParent> parents)
    {
        if (parents.Count == 0)
            return "the record it is filed under";
        var first = parents[0];
        var noun = SecureRootInheritance.WireTokenFor(first.Table) == "matter" ? "matter" : "project";
        var text = $"the {noun} '{first.Name ?? first.Id.ToString()}'";
        return parents.Count == 1 ? text : $"{text} and {parents.Count - 1} other record(s)";
    }

    /// <summary>The 409 ProblemDetails for removing a Secure designation the parents give, naming the first such parent.</summary>
    internal static IResult SecureFloorProblem(string recordLabel, IReadOnlyList<SecureFilingParent> secureParents, string traceId)
    {
        var first = secureParents[0];
        var label = recordLabel.ToLowerInvariant();
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Conflict",
            detail: $"This {label} is filed under {Describe(secureParents)}, which is secure, so its secure designation comes " +
                    $"from there and cannot be removed on the {label}. Remove it there, or file the {label} somewhere else. " +
                    "Nothing was changed.",
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = traceId,
                ["reasonCode"] = ReasonCode,
                ["parentRecordType"] = SecureRootInheritance.WireTokenFor(first.Table),
                ["parentRecordId"] = first.Id,
                ["parentName"] = first.Name,
            });
    }
}

/// <summary>
/// Task 175 (owner round 87): what a filed work assignment's or project's STORED Secure flag and Access Permission were last
/// derived from — the column <see cref="Column"/>, written only by the BFF (never on a form).
/// </summary>
/// <remarks>
/// <para><b>Why one record, and these five facts.</b> Round 87 needs, per record, which part of each value is the record's OWN
/// (set on it by a user — it stays when the parent loosens) and which is INHERITED (it follows the parent down). A per-value
/// "own" marker alone cannot tell, from the stored values, a user's edit from a parent's change (a stored Restricted above a
/// Standard floor is either "the parent just loosened" or "a user just tightened it"), nor a re-file from a parent loosening
/// (verifier F1's class). So the record also keeps the floor the stored values were derived from and the parents it came
/// from: a stored value that differs from max(own, last floor) is a user's edit; a parent set that differs is a re-file (which
/// never loosens); a floor that moved with the same parents is the parent's change (inherited values follow it, both ways).
/// One versioned JSON column (the relocation ledger's precedent, <c>sprk_relocationpending</c>) instead of five columns.</para>
/// <para><b>No record (existing rows): the backfill rule</b> — a value equal to the current floor is inherited; a value
/// stricter than it is set on the record. Applied the first time the job (or an inline follow) sees the row, and written.</para>
/// </remarks>
internal sealed record AccessInheritance(
    IReadOnlyList<string> Parents, bool FloorSecure, int FloorPermission, bool OwnSecure, int? OwnPermission)
{
    /// <summary>The column (Multiple lines of text) on <c>sprk_workassignment</c> and <c>sprk_project</c>.</summary>
    internal const string Column = "sprk_accessinheritance";

    private const int Version = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private sealed record Stored(
        int V, IReadOnlyList<string>? Parents, bool FloorSecure, int FloorPermission, bool OwnSecure, int? OwnPermission);

    /// <summary>The column's text.</summary>
    internal string Serialize() =>
        JsonSerializer.Serialize(new Stored(Version, Parents, FloorSecure, FloorPermission, OwnSecure, OwnPermission), Json);

    /// <summary>The record in <paramref name="text"/>: <c>(null, false)</c> when there is none yet, <c>(null, true)</c> when it
    /// cannot be read (never guessed at — the caller loosens nothing).</summary>
    internal static (AccessInheritance? Value, bool Unreadable) Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, false);
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(text, Json);
            if (stored is null || stored.V != Version || stored.Parents is null
                || SecureRootInheritance.RankOf(stored.FloorPermission) < 0
                || (stored.OwnPermission is { } own && SecureRootInheritance.RankOf(own) < 0))
                return (null, true);
            return (new AccessInheritance(stored.Parents, stored.FloorSecure, stored.FloorPermission, stored.OwnSecure,
                stored.OwnPermission), false);
        }
        catch (JsonException)
        {
            return (null, true);
        }
    }

    /// <summary>Same facts (the parent list compared as a set).</summary>
    internal bool SameAs(AccessInheritance? other) =>
        other is not null && FloorSecure == other.FloorSecure && FloorPermission == other.FloorPermission
        && OwnSecure == other.OwnSecure && OwnPermission == other.OwnPermission
        && new HashSet<string>(Parents, StringComparer.OrdinalIgnoreCase).SetEquals(other.Parents);
}

/// <summary>
/// The pure decision for one record (task 175, round 87): its targets and its new access record. See
/// <see cref="SecureRootInheritance.Decide"/>.
/// </summary>
internal sealed record FollowDecision
{
    /// <summary>Parentless / Undetermined: no value is changed (a parentless record may still have its record written).</summary>
    public FollowParentsOutcome? Stop { get; init; }

    public string? ReasonCode { get; init; }
    public string? Detail { get; init; }

    public bool TargetSecure { get; init; }
    public int TargetPermission { get; init; }

    /// <summary>The stored Access Permission is below the target: written FIRST.</summary>
    public bool Tighten { get; init; }

    /// <summary>The stored flag is secure and the target is not: the un-secure step.</summary>
    public bool Unsecure { get; init; }

    /// <summary>The stored Access Permission is above the target: written only after an un-secure completed.</summary>
    public bool Loosen { get; init; }

    public AccessInheritance? Marker { get; init; }

    /// <summary>The access record differs from the stored one (or there is none yet).</summary>
    public bool MarkerChanged { get; init; }

    public IReadOnlyList<FilingParentState> Parents { get; init; } = Array.Empty<FilingParentState>();

    /// <summary>Any write would be made.</summary>
    public bool NeedsWrite => Tighten || Unsecure || Loosen || MarkerChanged;
}

public sealed partial class SecureRootInheritance
{
    /// <summary>Records the parent's unsecure carries down in its own request; the rest are the job's (≤ 5 minutes).</summary>
    internal const int MaxInlineCascade = 50;

    private const int FiledListPageSize = 5000;
    private const int FiledListMaxPages = 20;
    private const int MaxProblemLines = 50;
    private const int MaxChangeLines = 200;

    /// <summary>The access record could not be read (task 175): nothing is loosened until it can be.</summary>
    internal const string ReasonMarkerUnreadable = "sdap.inherit.access_record_unreadable";

    /// <summary>The record holds an Access Permission that is not Standard, Limited or Restricted (task 175): left as it is.</summary>
    internal const string ReasonPermissionUnknown = "sdap.inherit.permission_unknown";

    /// <summary>The Access Permission could not be written (task 175): left as it was, reported, retried.</summary>
    internal const string ReasonPermissionNotWritten = "sdap.inherit.permission_not_written";

    /// <summary>The record's own <c>sprk_issecure</c> is EMPTY (task 175): neither secured nor un-secured on a guess.</summary>
    internal const string ReasonOwnFlagEmpty = "sdap.inherit.own_flag_empty";

    /// <summary>No owner could be resolved for the un-secured record (task 175): it stays secure.</summary>
    internal const string ReasonCascadeOwnerUnresolved = "sdap.inherit.unsecure_owner_unresolved";

    /// <summary>
    /// What <paramref name="table"/> <paramref name="recordId"/> is filed under DIRECTLY, with the effective Secure flag and
    /// Access Permission arriving through each (the floor, <see cref="SecureParentsAnswer.DirectParents"/>), at every level
    /// (<see cref="MaxFilingDepth"/>). A matter files under nothing.
    /// </summary>
    public Task<SecureParentsAnswer> FindFilingParentsAsync(string table, Guid recordId, CancellationToken ct) =>
        ReadSecureParentsAsync(_dataverse, _logger, table, recordId, ct, _recordTypes, MaxFilingDepth);

    /// <summary>
    /// <see cref="FindSecureParentsAsync(string, Guid, CancellationToken)"/> up to <paramref name="maxDepth"/> levels
    /// (<see cref="MaxFilingDepth"/>: every ancestor, task 174's effective rule — a record is secure if ANY ancestor is).
    /// </summary>
    public Task<SecureParentsAnswer> FindSecureParentsAsync(string table, Guid recordId, CancellationToken ct, int maxDepth) =>
        ReadSecureParentsAsync(_dataverse, _logger, table, recordId, ct, _recordTypes, maxDepth);

    /// <summary>
    /// #1478: whether <paramref name="table"/> <paramref name="recordId"/> is Restricted through what it is filed under
    /// (<see cref="EffectiveRootFlags.RestrictedThroughFilingAsync"/> over this class's reader); <c>null</c> when that could
    /// not be read.
    /// </summary>
    public Task<bool?> IsRestrictedThroughFilingAsync(string table, Guid recordId, CancellationToken ct) =>
        EffectiveRootFlags.RestrictedThroughFilingAsync(_dataverse, _logger, table, recordId, ct);

    /// <summary>The Access Permission option for a rank (Restricted 2 / Limited 1 / Standard 0).</summary>
    internal static int PermissionOf(int rank) => rank switch
    {
        2 => InheritedAccessPermission.Restricted,
        1 => InheritedAccessPermission.Limited,
        _ => InheritedAccessPermission.Standard,
    };

    /// <summary>An Access Permission's rank; null is Standard; an unknown option is -1 (never ranked by guess).</summary>
    internal static int RankOf(int? value) => value switch
    {
        null or InheritedAccessPermission.Standard => 0,
        InheritedAccessPermission.Limited => 1,
        InheritedAccessPermission.Restricted => 2,
        _ => -1,
    };

    /// <summary>The floor a record's parents set: secure if any ancestor is, and the most restrictive rank through them.</summary>
    internal static (bool Secure, int Rank) FloorOf(SecureParentsAnswer ancestry) =>
        (ancestry.HasSecureParent, ancestry.DirectParents.Select(p => p.EffectiveRank).DefaultIfEmpty(0).Max());

    /// <summary>"table:id" for the access record's parent list.</summary>
    private static string ParentKey(SecureFilingParent parent) => $"{parent.Table.Trim().ToLowerInvariant()}:{parent.Id:D}";

    /// <summary>
    /// The round-87 decision for ONE record, pure: its stored values, its access record and what its parents give it.
    /// </summary>
    /// <remarks>
    /// <para><b>Own and inherited.</b> With an access record: a stored value that differs from max(own, last floor) is a
    /// user's edit (a flag set by Make Secure, or cleared by an F3 unsecure; an Access Permission chosen on the form) — it
    /// becomes the record's own (an Access Permission at or below the last floor is "none set"; one below the floor is put
    /// back). A flag that turned secure while the floor is secure is the floor's (the securing loop), not an own act. Without
    /// one (existing rows): the backfill rule — a value stricter than the current floor is own, one equal to it inherited.</para>
    /// <para><b>A re-file never loosens</b> (round 87 item 5, verifier F1): when the parents differ from the record's, whatever
    /// the stored values hold beyond the NEW floor becomes the record's own.</para>
    /// <para><b>Targets:</b> Secure = own OR floor; Access Permission = max(own, floor). So when the parents loosen (same
    /// parents, a lower floor) only the inherited part follows them down; an own value stays.</para>
    /// </remarks>
    /// <param name="parentWasSecure">The caller has just un-secured a parent of this record (Mode A, <c>/unsecure-project</c>):
    /// a flag that came from that parent is the floor's, not the record's own (the backfill rule applied as of a moment ago).</param>
    /// <param name="ownSecureSetNow">The caller has just made this record secure by hand (Make Secure): its secure is its own.</param>
    internal static FollowDecision Decide(
        bool? storedSecure, int? storedPermission, string? markerText, SecureParentsAnswer ancestry,
        bool parentWasSecure = false, bool ownSecureSetNow = false)
    {
        var parentStates = ancestry.DirectParents;
        if (!ancestry.IsKnown)
            return new FollowDecision { Stop = FollowParentsOutcome.Undetermined, ReasonCode = ReasonParentUnverifiable, Detail = ancestry.Unverifiable };

        var (marker, unreadable) = AccessInheritance.Parse(markerText);
        if (unreadable)
        {
            return new FollowDecision
            {
                Stop = FollowParentsOutcome.Undetermined, ReasonCode = ReasonMarkerUnreadable,
                Detail = $"its {AccessInheritance.Column} could not be read", Parents = parentStates,
            };
        }

        var storedRank = RankOf(storedPermission);
        if (storedRank < 0)
        {
            return new FollowDecision
            {
                Stop = FollowParentsOutcome.Undetermined, ReasonCode = ReasonPermissionUnknown,
                Detail = "it holds an Access Permission that is not Standard, Limited or Restricted", Parents = parentStates,
            };
        }

        var parentsNow = parentStates.Select(p => ParentKey(p.Parent)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        var (floorSecure, floorRank) = FloorOf(ancestry);
        var floorExplainsSecure = floorSecure || parentWasSecure;

        bool ownSecure;
        int? ownPermission;
        if (marker is null)
        {
            // The backfill rule (existing rows): stricter than the floor = set on the record; equal = inherited.
            ownSecure = storedSecure == true && !floorExplainsSecure;
            ownPermission = storedRank > floorRank ? storedPermission : null;
        }
        else
        {
            ownSecure = marker.OwnSecure;
            ownPermission = marker.OwnPermission;
            var lastFloorRank = RankOf(marker.FloorPermission);

            // A user's edit since the record was written.
            var expectedSecure = ownSecure || marker.FloorSecure;
            if (storedSecure == true && !expectedSecure && !floorExplainsSecure)
                ownSecure = true; // Make Secure (or another own act)
            else if (storedSecure == false && expectedSecure)
                ownSecure = false; // an F3 unsecure (or a flag cleared outside the BFF)

            if (storedRank != Math.Max(RankOf(ownPermission), lastFloorRank))
            {
                if (storedRank > lastFloorRank)
                    ownPermission = storedPermission;
                else if (storedRank == lastFloorRank)
                    ownPermission = null;
                // below the last floor: not the record's own; put back below
            }

            // A re-file: what the stored values hold beyond the NEW floor becomes the record's own (round 87 item 5).
            if (!new HashSet<string>(marker.Parents, StringComparer.OrdinalIgnoreCase).SetEquals(parentsNow))
            {
                if (storedSecure == true && !floorSecure)
                    ownSecure = true;
                if (storedRank > Math.Max(RankOf(ownPermission), floorRank))
                    ownPermission = storedPermission;
            }
        }

        if (ownSecureSetNow && storedSecure == true)
            ownSecure = true;

        var newMarker = new AccessInheritance(parentsNow, floorSecure, PermissionOf(floorRank), ownSecure, ownPermission);
        var markerChanged = !newMarker.SameAs(marker);

        if (parentStates.Count == 0)
        {
            // Parentless: its values are its own (recorded, so a later filing knows them); nothing is changed.
            var parentless = new AccessInheritance(Array.Empty<string>(), false, InheritedAccessPermission.Standard,
                storedSecure == true, storedRank > 0 ? storedPermission : null);
            return new FollowDecision
            {
                Stop = FollowParentsOutcome.Parentless, Marker = parentless, MarkerChanged = !parentless.SameAs(marker),
                TargetSecure = storedSecure == true, TargetPermission = PermissionOf(storedRank),
            };
        }

        var targetSecure = ownSecure || floorSecure;
        var targetRank = Math.Max(RankOf(ownPermission), floorRank);
        if (storedSecure is null && !targetSecure)
        {
            return new FollowDecision
            {
                Stop = FollowParentsOutcome.Undetermined, ReasonCode = ReasonOwnFlagEmpty,
                Detail = "its own secure flag is empty (empty is never read as either value)", Parents = parentStates,
            };
        }

        return new FollowDecision
        {
            TargetSecure = targetSecure,
            TargetPermission = PermissionOf(targetRank),
            Tighten = targetRank > storedRank,
            Unsecure = storedSecure == true && !targetSecure,
            Loosen = targetRank < storedRank,
            Marker = newMarker,
            MarkerChanged = markerChanged,
            Parents = parentStates,
        };
    }

    /// <summary>
    /// Brings ONE work assignment or project's stored <c>sprk_issecure</c> and <c>sprk_accesspermission</c> into step with
    /// its parents (owner round 87): never looser than the floor they set, an own (hand-set) value kept, an inherited value
    /// following them both ways (<see cref="Decide"/>). Writes only what differs, its access record last; never throws a read
    /// fault.
    /// </summary>
    /// <remarks>
    /// <para><b>Order — fail closed.</b> (a) A STRICTER Access Permission is written first. (b) Un-secure: the unsecure
    /// endpoint's own steps (<see cref="UnsecureProjectEndpoint.UnsecureInheritedAsync"/> — ownership to its parents' business
    /// unit's team and read back, related records out of isolation, shares revoked, the flag cleared LAST). (c) Only then a
    /// LOOSER Access Permission. (d) The access record. A step that does not complete stops the call: the record stays secure
    /// and its Access Permission is not loosened; the access record then still says "secure was applied", so the next run
    /// retries instead of reading the record's secure as its own.</para>
    /// <para><b>The securing direction is not here.</b> Making a record secure stays task 158's
    /// (<see cref="SecureIfFiledUnderSecureAsync"/>: provisioning's own steps, bounded by its callers).</para>
    /// </remarks>
    public async Task<FollowParentsResult> FollowParentsAsync(
        string table, Guid recordId, string traceId, CancellationToken ct, bool parentWasSecure = false, bool ownSecureSetNow = false)
    {
        if (!Inherits(table))
            throw new ArgumentOutOfRangeException(nameof(table), table, "Only work assignments and projects follow a parent.");

        var logical = table.Trim().ToLowerInvariant();
        OwnAccess? own;
        try
        {
            own = await ReadOwnAccessAsync(logical, recordId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[FOLLOW-PARENT] {Table} {RecordId} could not be read; nothing was changed.", logical, recordId);
            return new FollowParentsResult(logical, recordId, FollowParentsOutcome.Undetermined)
            {
                ReasonCode = ReasonRecordUnreadable, Detail = "the record could not be read", IsSecureAfter = true,
            };
        }

        if (own is null)
            return new FollowParentsResult(logical, recordId, FollowParentsOutcome.NotFound);

        var ancestry = await ReadSecureParentsAsync(_dataverse, _logger, logical, recordId, ct, _recordTypes, MaxFilingDepth)
            .ConfigureAwait(false);
        var d = Decide(own.Secure, own.Permission, own.Marker, ancestry, parentWasSecure, ownSecureSetNow);
        var result = new FollowParentsResult(logical, recordId, d.Stop ?? FollowParentsOutcome.InStep)
        {
            PermissionFrom = own.Permission,
            IsSecureAfter = own.Secure != false,
            Parents = d.Parents.Select(p => p.Parent).ToList(),
            ReasonCode = d.ReasonCode,
            Detail = d.Detail,
        };

        if (d.Stop is { } stop)
        {
            if (stop == FollowParentsOutcome.Undetermined)
            {
                _logger.LogWarning("[FOLLOW-PARENT] {Table} {RecordId}: {Detail} ({Code}); nothing was loosened.",
                    logical, recordId, d.Detail, d.ReasonCode);
            }

            if (d.MarkerChanged && d.Marker is { } parentlessMarker)
                result = result with { MarkerWritten = await WriteMarkerAsync(logical, recordId, parentlessMarker, ct).ConfigureAwait(false) };
            return result;
        }

        var parents = result.Parents;
        var wrote = false;

        // (a) A stricter Access Permission first.
        if (d.Tighten)
        {
            if (!await WritePermissionAsync(logical, recordId, own.Permission, d.TargetPermission, parents, ct).ConfigureAwait(false))
                return result with { Outcome = FollowParentsOutcome.Incomplete, ReasonCode = ReasonPermissionNotWritten, Detail = "its Access Permission could not be written" };
            result = result with { PermissionTo = d.TargetPermission };
            wrote = true;
        }

        // (b) Un-secure: an inherited Secure whose parents are no longer secure (round 84, refined by round 87).
        if (d.Unsecure)
        {
            var unsecure = await UnsecureFollowingParentsAsync(logical, recordId, d.Parents, traceId, ct).ConfigureAwait(false);
            result = result with
            {
                UnsecureOutcome = unsecure.Outcome,
                ReasonCode = unsecure.Code,
                Detail = unsecure.Detail,
                IsSecureAfter = unsecure.Outcome != FollowParentsResult.Unsecured,
            };
            if (unsecure.Outcome != FollowParentsResult.Unsecured)
            {
                // The record says what is APPLIED: still secure (and the Access Permission as it now stands), so the next run
                // retries the un-secure rather than reading the flag as the record's own.
                var applied = d.Marker! with
                {
                    FloorSecure = true,
                    FloorPermission = d.Loosen ? own.Permission ?? InheritedAccessPermission.Standard : d.Marker!.FloorPermission,
                };
                await WriteMarkerAsync(logical, recordId, applied, ct).ConfigureAwait(false);
                return result with { Outcome = FollowParentsOutcome.Incomplete };
            }

            wrote = true;
        }

        // (c) Only now a looser Access Permission (only its inherited part ever goes down: the target keeps an own value).
        if (d.Loosen)
        {
            if (!await WritePermissionAsync(logical, recordId, own.Permission, d.TargetPermission, parents, ct).ConfigureAwait(false))
                return result with { Outcome = FollowParentsOutcome.Incomplete, ReasonCode = ReasonPermissionNotWritten, Detail = "its Access Permission could not be written" };
            result = result with { PermissionTo = d.TargetPermission };
            wrote = true;
        }

        // (d) The access record, last.
        if (d.MarkerChanged)
            result = result with { MarkerWritten = await WriteMarkerAsync(logical, recordId, d.Marker!, ct).ConfigureAwait(false) };

        return result with { Outcome = wrote ? FollowParentsOutcome.Changed : FollowParentsOutcome.InStep, ReasonCode = null, Detail = null };
    }

    /// <summary>
    /// A parent's un-secure (or a re-file) carried down to every work assignment and project filed below
    /// <paramref name="parentTable"/> <paramref name="parentId"/>, top-down (each level after the one above it), at most
    /// <see cref="MaxInlineCascade"/> records; the rest — and anything deeper than <see cref="MaxFilingDepth"/> — are left to
    /// <see cref="SecureRootInheritanceJob"/>. A listing that cannot be read is <see cref="FiledCascadePass.Unreadable"/>.
    /// Never throws.
    /// </summary>
    /// <param name="parentWasSecure">The parent has just been un-secured (Mode A): a flag below it that came from it is
    /// inherited (see <see cref="Decide"/>).</param>
    public async Task<FiledCascadePass> CascadeBelowAsync(
        string parentTable, Guid parentId, string traceId, CancellationToken ct, bool parentWasSecure = false)
    {
        if (!IsParent(parentTable))
            return new FiledCascadePass(false, Array.Empty<FiledRootRef>(), Array.Empty<FollowParentsResult>(), 0);

        FiledRootsWalk walk;
        try
        {
            walk = await ListFiledRootsBelowAsync(_dataverse, _logger, new[] { (parentTable.Trim().ToLowerInvariant(), parentId) }, ct, _recordTypes)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[FOLLOW-PARENT] The records filed below {Table} {Id} could not be listed; the job brings them into step.",
                parentTable, parentId);
            return new FiledCascadePass(true, Array.Empty<FiledRootRef>(), Array.Empty<FollowParentsResult>(), 0);
        }

        var results = new List<FollowParentsResult>();
        var deferred = walk.DepthBoundReached ? 1 : 0;
        foreach (var root in walk.Roots)
        {
            if (!root.Confirmed)
                continue; // a pair whose type could not be read: not provably filed here — the job decides (and reports) it
            if (results.Count >= MaxInlineCascade)
            {
                deferred++;
                continue;
            }

            results.Add(await FollowParentsAsync(root.Table, root.Id, traceId, ct, parentWasSecure).ConfigureAwait(false));
        }

        _logger.LogInformation(
            "[FOLLOW-PARENT] Below {Table} {Id}: filed={Filed} examined={Examined} unsecured={Unsecured} permissions={Permissions} " +
            "incomplete={Incomplete} deferred={Deferred}. TraceId={TraceId}", parentTable, parentId, walk.Roots.Count, results.Count,
            results.Count(r => r.UnsecureOutcome == FollowParentsResult.Unsecured), results.Count(r => r.PermissionTo is not null),
            results.Count(r => !r.IsComplete), deferred, traceId);
        return new FiledCascadePass(false, walk.Roots, results, deferred);
    }

    /// <summary>
    /// The job's pass (task 175 goal 3): every work assignment and project is decided over the ONE batched walk
    /// (<see cref="Decide"/>), and only those with something to write are followed (<see cref="FollowParentsAsync"/>, a fresh
    /// read each). A project whose effective values changed here sends the records filed under it round again in the SAME run
    /// (up to <see cref="MaxFilingDepth"/> rounds). Bounded: at most <paramref name="maxUnsecures"/> un-secures (in a fixed
    /// order resumed after <paramref name="unsecureResumeAfter"/>) and <paramref name="maxRecordWrites"/> other followed records
    /// (an Access Permission or an access record, one column each); the rest are deferred to the next run.
    /// </summary>
    /// <exception cref="InvalidOperationException">The listing could not be completed (ADR-036 A1 rule 4: a failed run).</exception>
    public async Task<FollowParentsPass> FollowParentsPassAsync(
        string traceId, (string Table, Guid Id)? unsecureResumeAfter, int maxUnsecures, int maxRecordWrites, CancellationToken ct)
    {
        var listed = await ListRecordsAsync(ct).ConfigureAwait(false);

        int parentless = 0, inStep = 0, unsecured = 0, permissions = 0, markers = 0, undetermined = 0, notCompleted = 0, deferred = 0;
        int unsecuresTried = 0, writesTried = 0;
        var problems = new List<string>();
        var changes = new List<object>();
        (string Table, Guid Id)? lastUnsecureTried = null;
        var seen = new HashSet<(string, Guid)>();

        // Round 1: the batched decision over every record; follow only those with something to write.
        var needs = new List<(ListedRecord Row, bool Unsecure)>();
        foreach (var group in listed.GroupBy(r => r.Table))
        {
            var answers = await ReadSecureParentsOfManyAsync(_dataverse, _logger, group.Key, group.Select(r => r.Id).ToList(), ct,
                _recordTypes, MaxFilingDepth).ConfigureAwait(false);
            foreach (var row in group)
            {
                seen.Add((row.Table, row.Id));
                var d = Decide(row.Secure, row.Permission, row.Marker, answers[row.Id]);
                if (d.NeedsWrite)
                {
                    needs.Add((row, d.Unsecure));
                    continue;
                }

                switch (d.Stop)
                {
                    case FollowParentsOutcome.Parentless:
                        parentless++;
                        break;
                    case FollowParentsOutcome.Undetermined:
                        undetermined++;
                        Problem($"{row.Table}:{row.Id:D}: {d.Detail} ({d.ReasonCode})");
                        break;
                    default:
                        inStep++; // a not-yet-secure record under a secure parent is the job's securing loop's, not this pass's
                        break;
                }
            }
        }

        // The un-secures in a fixed order resumed after the cursor; then the rest (projects before work assignments).
        var unsecureOrder = needs.Where(n => n.Unsecure).Select(n => n.Row)
            .OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
        var start = unsecureResumeAfter is { } after ? unsecureOrder.FindIndex(r => CompareKeys((r.Table, r.Id), after) > 0) : 0;
        if (start < 0)
            start = 0;
        var ordered = unsecureOrder.Skip(start).Concat(unsecureOrder.Take(start)).Select(r => (Row: r, Unsecure: true))
            .Concat(needs.Where(n => !n.Unsecure).Select(n => (n.Row, Unsecure: false))
                .OrderBy(n => n.Row.Table, StringComparer.Ordinal).ThenBy(n => n.Row.Id))
            .ToList();

        var changedParents = new List<(string Table, Guid Id)>();
        foreach (var (row, isUnsecure) in ordered)
        {
            if ((isUnsecure && unsecuresTried >= maxUnsecures) || (!isUnsecure && writesTried >= maxRecordWrites))
            {
                deferred++;
                continue;
            }

            if (isUnsecure)
            {
                unsecuresTried++;
                lastUnsecureTried = (row.Table, row.Id);
            }
            else
            {
                writesTried++;
            }

            Tally(await FollowParentsAsync(row.Table, row.Id, traceId, ct).ConfigureAwait(false), counted: false);
        }

        // Later rounds: what is filed under a project changed in this run, decided again now (its values just moved).
        for (var round = 2; changedParents.Count > 0 && round <= MaxFilingDepth + 1; round++)
        {
            IReadOnlyList<FiledRootRef> below;
            try
            {
                below = await ListFiledRootsAsync(_dataverse, _logger, changedParents.ToList(), ct, _recordTypes).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[FOLLOW-PARENT] The records filed under {Count} changed project(s) could not be listed; the next run decides them.",
                    changedParents.Count);
                notCompleted++;
                Problem("the records filed under a project changed in this run could not be listed");
                break;
            }

            changedParents = new List<(string Table, Guid Id)>();
            foreach (var root in below.Where(r => r.Confirmed))
            {
                if (unsecuresTried >= maxUnsecures || writesTried >= maxRecordWrites)
                {
                    deferred++;
                    continue;
                }

                writesTried++;
                // A record round 1 already counted is counted again only for what this round did (a write or a problem).
                Tally(await FollowParentsAsync(root.Table, root.Id, traceId, ct).ConfigureAwait(false), counted: !seen.Add((root.Table, root.Id)));
            }
        }

        return new FollowParentsPass
        {
            Listed = listed.Count,
            Parentless = parentless,
            InStep = inStep,
            Unsecured = unsecured,
            PermissionsChanged = permissions,
            MarkersWritten = markers,
            Undetermined = undetermined,
            NotCompleted = notCompleted,
            Deferred = deferred,
            UnsecureResumeAfter = deferred > 0 ? lastUnsecureTried : null,
            Problems = problems,
            Changes = changes,
        };

        void Problem(string line)
        {
            if (problems.Count < MaxProblemLines)
                problems.Add(line);
        }

        // counted: the record was already counted (round 1's decision) — only what this follow DID is added.
        void Tally(FollowParentsResult result, bool counted)
        {
            if (result.UnsecureOutcome == FollowParentsResult.Unsecured)
                unsecured++;
            if (result.PermissionTo is not null)
                permissions++;
            if (result.MarkerWritten)
                markers++;
            switch (result.Outcome)
            {
                case FollowParentsOutcome.Undetermined:
                    undetermined++;
                    Problem($"{result.Table}:{result.Id:D}: {result.Detail} ({result.ReasonCode})");
                    break;
                case FollowParentsOutcome.Incomplete:
                    notCompleted++;
                    Problem($"{result.Table}:{result.Id:D}: {result.Detail} ({result.ReasonCode})");
                    break;
                case FollowParentsOutcome.Parentless when !counted:
                    parentless++;
                    break;
                case FollowParentsOutcome.InStep when !counted:
                    inStep++;
                    break;
            }

            if (result.WroteAnything)
            {
                if (changes.Count < MaxChangeLines)
                {
                    changes.Add(new
                    {
                        table = result.Table,
                        id = result.Id,
                        unsecure = result.UnsecureOutcome,
                        permissionFrom = result.PermissionFrom,
                        permissionTo = result.PermissionTo,
                    });
                }

                if (IsParent(result.Table) && !changedParents.Contains((result.Table, result.Id)))
                    changedParents.Add((result.Table, result.Id));
            }
        }
    }

    /// <summary>The provisioning order (table ordinal, then id) — the job's own cursor order.</summary>
    private static int CompareKeys((string Table, Guid Id) a, (string Table, Guid Id) b)
    {
        var byTable = string.CompareOrdinal(a.Table, b.Table);
        return byTable != 0 ? byTable : a.Id.CompareTo(b.Id);
    }

    /// <summary>
    /// The un-secure step for one record: the owner the ownership rule gives a record filed under its (now ordinary) parents —
    /// their business unit's team (D-11, record-first) — and then the unsecure endpoint's own steps. Never the Secure Record
    /// Owners team (a parent still isolated would hand it to a memberless team with its shares gone — reachable by nobody):
    /// that, an unresolvable owner, or any refusal leaves it secure.
    /// </summary>
    private async Task<(string Outcome, string? Code, string? Detail)> UnsecureFollowingParentsAsync(
        string logical, Guid recordId, IReadOnlyList<FilingParentState> parents, string traceId, CancellationToken ct)
    {
        RecordOwnerResolution owner;
        try
        {
            owner = await _ownership.ResolveOwnerAsync(
                RecordOwnershipContext.ForParents(parents.Select(p => (RecordOwnershipParent?)new RecordOwnershipParent(p.Parent.Table, p.Parent.Id))),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[FOLLOW-PARENT] The owner for un-securing {Table} {RecordId} could not be resolved; it stays secure.",
                logical, recordId);
            return (FollowParentsResult.Failed, ReasonCascadeOwnerUnresolved, "the business-unit team it would be owned by could not be read");
        }

        if (!owner.IsOwned || owner.IsSecureOwner || owner.OwningTeamId is not { } team)
        {
            _logger.LogWarning(
                "[FOLLOW-PARENT] {Table} {RecordId}: no ordinary owner for it ({Code}: {Reason}; secure owner={Secure}); it stays secure.",
                logical, recordId, owner.RefusalCode, owner.Reason, owner.IsSecureOwner);
            return (FollowParentsResult.Failed, owner.RefusalCode ?? ReasonCascadeOwnerUnresolved,
                owner.IsSecureOwner
                    ? "a record it is filed under is still owned by the Secure Record Owners team"
                    : owner.Reason ?? "no business-unit team could be resolved for it");
        }

        var secureTeam = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(_dataverse, _configuration, ct).ConfigureAwait(false);
        if (secureTeam.TeamId is not { } isolating || isolating == team)
        {
            return (FollowParentsResult.Failed, ReasonCascadeOwnerUnresolved, secureTeam.TeamId is null
                ? "which team isolates secure records could not be established"
                : "the owner it would get is the Secure Record Owners team");
        }

        var root = SecureRecordRoot.For(logical == Project ? ExternalGrantRootType.Project : ExternalGrantRootType.WorkAssignment);
        IResult result;
        try
        {
            result = await UnsecureProjectEndpoint.UnsecureInheritedAsync(
                root, recordId, DataversePrincipalRef.Team(team), traceId, _webApi, _recordShare, _secureChildren, this,
                _configuration, _accessCacheInvalidator, _logger, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[FOLLOW-PARENT] Un-securing {Table} {RecordId} threw; it is left as it is (the job retries).", logical, recordId);
            return (FollowParentsResult.Failed, ReasonUnexpectedResult, "un-securing it failed");
        }

        switch (result)
        {
            case Ok<UnsecureProjectResponse>:
                _logger.LogInformation(
                    "[FOLLOW-PARENT] {Table} {RecordId} followed its parents out of secure (owner team {Team}). TraceId={TraceId}",
                    logical, recordId, team, traceId);
                return (FollowParentsResult.Unsecured, null, null);

            case ProblemHttpResult problem:
                var code = problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var value) ? value?.ToString() : null;
                _logger.LogWarning(
                    "[FOLLOW-PARENT] Un-securing {Table} {RecordId} answered {Status} {Code}: {Detail}. It stays at the more restrictive " +
                    "state. TraceId={TraceId}", logical, recordId, problem.StatusCode, code, problem.ProblemDetails.Detail, traceId);
                return (problem.StatusCode is >= 400 and < 500 ? FollowParentsResult.Refused : FollowParentsResult.Failed,
                    code ?? ReasonUnexpectedResult, problem.ProblemDetails.Detail);

            default:
                return (FollowParentsResult.Failed, ReasonUnexpectedResult, "un-securing it answered an unexpected result");
        }
    }

    /// <summary>Writes one column, <c>sprk_accesspermission</c>; false (logged) when the write failed.</summary>
    private async Task<bool> WritePermissionAsync(
        string logical, Guid recordId, int? from, int to, IReadOnlyList<SecureFilingParent> parents, CancellationToken ct)
    {
        try
        {
            await _dataverse.UpdateAsync(logical, recordId,
                new Dictionary<string, object> { [AccessPermissionColumn] = new OptionSetValue(to) }, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "[FOLLOW-PARENT] {Table} {RecordId}: Access Permission {From} -> {To} (follows {Parents}).",
                logical, recordId, from?.ToString() ?? "null", to, string.Join(", ", parents.Select(p => $"{p.Table}:{p.Id:D}")));
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[FOLLOW-PARENT] {Table} {RecordId}: writing Access Permission {To} failed; retried.", logical, recordId, to);
            return false;
        }
    }

    /// <summary>Writes one column, the access record; false (logged) when the write failed — the next run reads the old one.</summary>
    private async Task<bool> WriteMarkerAsync(string logical, Guid recordId, AccessInheritance marker, CancellationToken ct)
    {
        try
        {
            await _dataverse.UpdateAsync(logical, recordId,
                new Dictionary<string, object> { [AccessInheritance.Column] = marker.Serialize() }, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[FOLLOW-PARENT] {Table} {RecordId}: writing its {Column} failed; the next run reads the previous one.",
                logical, recordId, AccessInheritance.Column);
            return false;
        }
    }

    /// <summary>A record's own Secure flag, Access Permission and access record (null: it does not exist). A fault propagates.</summary>
    private sealed record OwnAccess(bool? Secure, int? Permission, string? Marker);

    private async Task<OwnAccess?> ReadOwnAccessAsync(string logical, Guid recordId, CancellationToken ct)
    {
        var query = Query(logical, new[] { IsSecureColumn, AccessPermissionColumn, AccessInheritance.Column });
        query.TopCount = 1;
        query.Criteria.AddCondition(logical + "id", ConditionOperator.Equal, recordId);
        var row = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        return row is null
            ? null
            : new OwnAccess(row.GetAttributeValue<bool?>(IsSecureColumn), PermissionOfRow(row), row.GetAttributeValue<string>(AccessInheritance.Column));
    }

    private static int? PermissionOfRow(Entity row) =>
        row.Attributes.TryGetValue(AccessPermissionColumn, out var value)
            ? value switch { OptionSetValue o => o.Value, int i => i, _ => null }
            : null;

    /// <summary>One work assignment or project as the job's listing reads it.</summary>
    private sealed record ListedRecord(string Table, Guid Id, bool? Secure, int? Permission, string? Marker);

    /// <summary>
    /// Every work assignment and project, with its own flag, Access Permission and access record — paged; past the page
    /// ceiling it throws rather than decide on part of them. Parentless records are listed too: their record keeps what is
    /// their own, so a later filing never mistakes it for something inherited.
    /// </summary>
    private async Task<IReadOnlyList<ListedRecord>> ListRecordsAsync(CancellationToken ct)
    {
        var rows = new List<ListedRecord>();
        foreach (var table in new[] { Project, WorkAssignment })
        {
            var query = new QueryExpression(table)
            {
                ColumnSet = new ColumnSet(table + "id", IsSecureColumn, AccessPermissionColumn, AccessInheritance.Column),
                NoLock = true,
                PageInfo = new PagingInfo { Count = FiledListPageSize, PageNumber = 1 },
            };

            for (var page = 1; ; page++)
            {
                if (page > FiledListMaxPages)
                    throw new InvalidOperationException(
                        $"{table} still had records to list after {FiledListMaxPages} pages; nothing is decided on part of them.");

                var result = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                rows.AddRange(result.Entities.Select(e => new ListedRecord(
                    table, e.Id, e.GetAttributeValue<bool?>(IsSecureColumn), PermissionOfRow(e),
                    e.GetAttributeValue<string>(AccessInheritance.Column))));
                if (!result.MoreRecords)
                    break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = result.PagingCookie;
            }
        }

        return rows;
    }
}
