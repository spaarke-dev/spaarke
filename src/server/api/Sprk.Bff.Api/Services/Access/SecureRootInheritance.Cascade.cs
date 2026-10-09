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

    /// <summary>Records a user changed between this run's read and its write: nothing was written; the next run decides them.</summary>
    public int Conflicts { get; init; }
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
    /// Task 175 fix round 2: why no access record could be trusted in this run (the column is not field-secured, K2; or the
    /// BFF's read of it is not proven while records read empty, F1) — the run fails; null when they were trusted.
    /// </summary>
    public string? Untrusted { get; init; }

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
/// <c>sprk_accesspermission</c> (<see cref="SecureRootInheritance.CheckRefileAsync"/>). A generic write of <c>sprk_issecure</c>
/// is refused outright (<see cref="SecureFlagReasonCode"/>, fix round 2).
/// </summary>
public static class AccessFollowsParent
{
    /// <summary>The named 409 reason code.</summary>
    public const string ReasonCode = "sdap.access.access_follows_parent";

    /// <summary>
    /// Task 175 fix round 2 (K3): a caller-supplied write of <c>sprk_issecure</c> on a work assignment or project through a
    /// generic BFF writer (the chat / playbook update and create tools, the output orchestrator, the field-mapping push, the
    /// Office create) is refused: only the transition endpoints (<c>/provision-project</c>, <c>/unsecure-project</c>, with
    /// F3 and their isolation steps) and the cascade set it.
    /// </summary>
    public const string SecureFlagReasonCode = "sdap.access.secure_flag_transition_only";

    /// <summary>The secure flag's column.</summary>
    internal const string SecureFlagColumn = "sprk_issecure";

    /// <summary>True when a write key names <c>sprk_issecure</c> (any spelling a BFF writer uses).</summary>
    internal static bool NamesSecureFlag(IEnumerable<KeyValuePair<string, object?>> writes) =>
        writes.Any(w => string.Equals(SecureRootInheritance.NormalizeColumn(w.Key), SecureFlagColumn, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>
    /// Task 175 fix round (verifier F1-1): a caller-supplied write that names <see cref="Column"/> — the record of what is
    /// the record's own and what is inherited — is refused by every BFF writer (<see cref="SecureRootInheritance.CheckRefileAsync"/>,
    /// <see cref="SecureRootInheritance.PlanCreateAsync"/>). Only the cascade writes it; Dataverse field-level security keeps
    /// users from writing it directly (<c>scripts/Set-AccessInheritanceSchema.ps1</c>).
    /// </summary>
    internal const string ServerOnlyReasonCode = "sdap.access.access_record_server_only";

    /// <summary>True when a write key names the column (any spelling a BFF writer uses).</summary>
    internal static bool IsNamedIn(IEnumerable<KeyValuePair<string, object?>> writes) =>
        writes.Any(w => string.Equals(SecureRootInheritance.NormalizeColumn(w.Key), Column, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>The access record could not be written (task 175): reported; the next run reads the previous one.</summary>
    internal const string ReasonMarkerNotWritten = "sdap.inherit.access_record_not_written";

    /// <summary>
    /// The column changed between the read this decision was made on and the write (task 175, verifier c): a user's edit is
    /// never overwritten; nothing is written and the next run decides again.
    /// </summary>
    internal const string ReasonChangedConcurrently = "sdap.inherit.changed_concurrently";

    /// <summary>
    /// The access record was written but reads back EMPTY (task 175 fix round, verifier F1-1): field-level security hides
    /// <c>sprk_accessinheritance</c> from the BFF (it can write it but not read it). Nothing is loosened — every record then
    /// reads as "no record yet", and the backfill rule only ever raises — and the run fails naming it, so the profile is
    /// fixed (<c>scripts/Set-AccessInheritanceSchema.ps1 -Verify</c>); the rest of the pass writes no further access record.
    /// </summary>
    internal const string ReasonMarkerHidden = "sdap.inherit.access_record_hidden";

    /// <summary>
    /// <c>sprk_accessinheritance</c> is NOT field-secured (or its metadata cannot be read) — task 175 fix round 2, K2: a user
    /// could have written any record, so none is trusted; every record is undetermined (nothing loosened, nothing written) and
    /// the run fails until <c>scripts/Set-AccessInheritanceSchema.ps1 -Apply</c> secures it.
    /// </summary>
    internal const string ReasonAccessRecordUnsecured = "sdap.inherit.access_record_not_secured";

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
    /// <para><b>An empty record never loosens anything</b> (task 175 fix round, verifier F1-1): the backfill rule keeps every
    /// stored value beyond the CURRENT floor as the record's own, so a record that is not written yet — or one the BFF cannot
    /// read (field-level security lost) — is only ever raised, never lowered. Records secured by inheritance get their record
    /// when they are secured (<see cref="ProvisionAsync"/>), and the job backfills every other one on first sight.</para>
    /// </remarks>
    /// <param name="ownSecureSetNow">The caller has just made this record secure by hand (Make Secure): its secure is its own.</param>
    internal static FollowDecision Decide(
        bool? storedSecure, int? storedPermission, string? markerText, SecureParentsAnswer ancestry,
        bool ownSecureSetNow = false)
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

        bool ownSecure;
        int? ownPermission;
        if (marker is null)
        {
            // The backfill rule (existing rows): stricter than the floor = set on the record; equal = inherited.
            ownSecure = storedSecure == true && !floorSecure;
            ownPermission = storedRank > floorRank ? storedPermission : null;
        }
        else
        {
            ownSecure = marker.OwnSecure;
            ownPermission = marker.OwnPermission;
            var lastFloorRank = RankOf(marker.FloorPermission);

            // A user's edit since the record was written.
            var expectedSecure = ownSecure || marker.FloorSecure;
            if (storedSecure == true && !expectedSecure && !floorSecure)
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
        string table, Guid recordId, string traceId, CancellationToken ct, bool ownSecureSetNow = false)
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

        // Task 175 fix round 2 (F1, K2): a record is decided only on an access record that can be trusted — the column is
        // field-secured, and an EMPTY one is read as "not written yet" only once the BFF's read of the column is proven.
        // Otherwise nothing is written: an unreadable record must never be overwritten by the backfill rule's guess.
        if (await AccessRecordDistrustAsync(own.Marker, ct).ConfigureAwait(false) is { } distrust)
        {
            _logger.LogWarning("[FOLLOW-PARENT] {Table} {RecordId}: {Detail} ({Code}); nothing was written.",
                logical, recordId, distrust.Detail, distrust.Code);
            return new FollowParentsResult(logical, recordId, FollowParentsOutcome.Undetermined)
            {
                ReasonCode = distrust.Code, Detail = distrust.Detail, PermissionFrom = own.Permission, IsSecureAfter = own.Secure != false,
            };
        }

        var ancestry = await ReadSecureParentsAsync(_dataverse, _logger, logical, recordId, ct, _recordTypes, MaxFilingDepth)
            .ConfigureAwait(false);
        var d = Decide(own.Secure, own.Permission, own.Marker, ancestry, ownSecureSetNow);
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
            {
                var written = await WriteMarkerAsync(logical, recordId, own.Marker, parentlessMarker, ct).ConfigureAwait(false);
                if (written != ColumnWrite.Written)
                    return Unwritten(result, written, "its access record");
                result = result with { MarkerWritten = true };
            }

            return result;
        }

        var parents = result.Parents;
        var wrote = false;

        // (a) A stricter Access Permission first.
        if (d.Tighten)
        {
            var tightened = await WritePermissionAsync(logical, recordId, own.Permission, d.TargetPermission, parents, ct).ConfigureAwait(false);
            if (tightened != ColumnWrite.Written)
                return Unwritten(result, tightened, "its Access Permission");
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
                await WriteMarkerAsync(logical, recordId, own.Marker, applied, ct).ConfigureAwait(false);
                return result with { Outcome = FollowParentsOutcome.Incomplete };
            }

            wrote = true;
        }

        // (c) Only now a looser Access Permission (only its inherited part ever goes down: the target keeps an own value).
        if (d.Loosen)
        {
            var loosened = await WritePermissionAsync(logical, recordId, own.Permission, d.TargetPermission, parents, ct).ConfigureAwait(false);
            if (loosened != ColumnWrite.Written)
                return Unwritten(result, loosened, "its Access Permission");
            result = result with { PermissionTo = d.TargetPermission };
            wrote = true;
        }

        // (d) The access record, last.
        if (d.MarkerChanged)
        {
            var marked = await WriteMarkerAsync(logical, recordId, own.Marker, d.Marker!, ct).ConfigureAwait(false);
            if (marked != ColumnWrite.Written)
                return Unwritten(result with { Outcome = wrote ? FollowParentsOutcome.Changed : FollowParentsOutcome.InStep }, marked, "its access record");
            result = result with { MarkerWritten = true };
        }

        return result with { Outcome = wrote ? FollowParentsOutcome.Changed : FollowParentsOutcome.InStep, ReasonCode = null, Detail = null };
    }

    /// <summary>
    /// A parent's un-secure (or a re-file) carried down to every work assignment and project filed below
    /// <paramref name="parentTable"/> <paramref name="parentId"/>, top-down (each level after the one above it), at most
    /// <see cref="MaxInlineCascade"/> records; the rest — and anything deeper than <see cref="MaxFilingDepth"/> — are left to
    /// <see cref="SecureRootInheritanceJob"/>. A listing that cannot be read is <see cref="FiledCascadePass.Unreadable"/>.
    /// Never throws.
    /// </summary>
    public async Task<FiledCascadePass> CascadeBelowAsync(
        string parentTable, Guid parentId, string traceId, CancellationToken ct)
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

            results.Add(await FollowParentsAsync(root.Table, root.Id, traceId, ct).ConfigureAwait(false));
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

        // Task 175 fix round 2 (K2): no access record is trusted while the column is not field-secured.
        if (!await AccessRecordColumnSecuredAsync(ct).ConfigureAwait(false))
        {
            _logger.LogError("[FOLLOW-PARENT] {Column} is not field-secured (or its metadata could not be read): no access record is " +
                "trusted, nothing is followed this run ({Code}).", AccessInheritance.Column, ReasonAccessRecordUnsecured);
            return new FollowParentsPass
            {
                Listed = listed.Count,
                Undetermined = listed.Count,
                Untrusted = ReasonAccessRecordUnsecured,
                Problems = new[]
                {
                    $"{AccessInheritance.Column} is not field-secured, so no access record is trusted and nothing was followed " +
                    $"({ReasonAccessRecordUnsecured}; run scripts/Set-AccessInheritanceSchema.ps1 -Apply, then -Verify)",
                },
            };
        }

        // F1: an EMPTY record is read as "not written yet" only once the BFF's read of the column is proven.
        if (listed.Any(r => !string.IsNullOrEmpty(r.Marker)))
            _accessRecordReadable = true; // a non-empty record read proves the BFF reads the column
        var readable = !listed.Any(r => string.IsNullOrEmpty(r.Marker)) || await AccessRecordReadableAsync(ct).ConfigureAwait(false);
        var unproven = 0;

        int parentless = 0, inStep = 0, unsecured = 0, permissions = 0, markers = 0, undetermined = 0, notCompleted = 0, deferred = 0, conflicts = 0;
        int unsecuresTried = 0, writesTried = 0;
        var markerHidden = false;
        var problems = new List<string>();
        var changes = new List<object>();
        (string Table, Guid Id)? lastUnsecureTried = null;
        var seen = new HashSet<(string, Guid)>();

        // Round 1: the batched decision over every record; follow only those with something to write.
        var needs = new List<(ListedRecord Row, bool Unsecure, bool RecordOnly, bool InheritedSecureFirst)>();
        foreach (var group in listed.GroupBy(r => r.Table))
        {
            var answers = await ReadSecureParentsOfManyAsync(_dataverse, _logger, group.Key, group.Select(r => r.Id).ToList(), ct,
                _recordTypes, MaxFilingDepth).ConfigureAwait(false);
            foreach (var row in group)
            {
                seen.Add((row.Table, row.Id));
                if (!readable && string.IsNullOrEmpty(row.Marker))
                {
                    unproven++; // never decided on an empty read the BFF cannot vouch for (F1): nothing written
                    continue;
                }

                var d = Decide(row.Secure, row.Permission, row.Marker, answers[row.Id]);
                if (d.NeedsWrite)
                {
                    // K1: a record about to be recorded as INHERITED secure (no record yet, secure, under a secure floor) is
                    // written before any other record-only write, so that a later un-secure of its parent finds it recorded.
                    needs.Add((row, d.Unsecure, !d.Tighten && !d.Unsecure && !d.Loosen,
                        string.IsNullOrEmpty(row.Marker) && row.Secure == true && answers[row.Id].HasSecureParent));
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
        if (unproven > 0)
        {
            undetermined += unproven;
            Problem($"{unproven} record(s) read an EMPTY {AccessInheritance.Column} and the BFF's read of the column could not be " +
                $"proven, so nothing was written to them ({ReasonMarkerHidden}; run scripts/Set-AccessInheritanceSchema.ps1 -Verify)");
        }

        var unsecureOrder = needs.Where(n => n.Unsecure).Select(n => n.Row)
            .OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
        var start = unsecureResumeAfter is { } after ? unsecureOrder.FindIndex(r => CompareKeys((r.Table, r.Id), after) > 0) : 0;
        if (start < 0)
            start = 0;
        var ordered = unsecureOrder.Skip(start).Concat(unsecureOrder.Take(start)).Select(r => (Row: r, Unsecure: true))
            .Concat(needs.Where(n => !n.Unsecure)
                .OrderByDescending(n => n.InheritedSecureFirst).ThenBy(n => n.Row.Table, StringComparer.Ordinal).ThenBy(n => n.Row.Id)
                .Select(n => (Row: n.Row, Unsecure: false)))
            .ToList();

        var changedParents = new List<(string Table, Guid Id)>();
        var hidden = 0;
        foreach (var (row, isUnsecure) in ordered)
        {
            if ((isUnsecure && unsecuresTried >= maxUnsecures) || (!isUnsecure && writesTried >= maxRecordWrites))
            {
                deferred++;
                continue;
            }

            // Field security hides the access record from the BFF (a write read back empty): no record read in this run can be
            // trusted any more, so nothing further is followed (fix round 2: every write, not just record-only ones).
            if (markerHidden)
            {
                hidden++;
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
                if (markerHidden)
                {
                    hidden++; // fix round 2: round 2 stops too once a record read back empty
                    continue;
                }

                // Any of them may un-secure, so the un-secure bound is checked before each (verifier d) and counted after.
                if (unsecuresTried >= maxUnsecures || writesTried >= maxRecordWrites)
                {
                    deferred++;
                    continue;
                }

                writesTried++;
                var follow = await FollowParentsAsync(root.Table, root.Id, traceId, ct).ConfigureAwait(false);
                if (follow.UnsecureOutcome is not null)
                    unsecuresTried++;
                // A record round 1 already counted is counted again only for what this round did (a write or a problem).
                Tally(follow, counted: !seen.Add((root.Table, root.Id)));
            }
        }

        if (hidden > 0)
        {
            notCompleted += hidden;
            Problem($"{hidden} more record(s) were not followed: {AccessInheritance.Column} reads back empty to the BFF " +
                $"({ReasonMarkerHidden}; run scripts/Set-AccessInheritanceSchema.ps1 -Verify)");
        }

        return new FollowParentsPass
        {
            Untrusted = markerHidden || unproven > 0 ? ReasonMarkerHidden : null,
            Listed = listed.Count,
            Parentless = parentless,
            InStep = inStep,
            Unsecured = unsecured,
            PermissionsChanged = permissions,
            MarkersWritten = markers,
            Conflicts = conflicts,
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
                case FollowParentsOutcome.Incomplete when result.ReasonCode == ReasonChangedConcurrently:
                    conflicts++; // a user's edit landed between the read and the write: nothing was written; the next run decides
                    Problem($"{result.Table}:{result.Id:D}: {result.Detail} ({result.ReasonCode})");
                    break;
                case FollowParentsOutcome.Incomplete:
                    notCompleted++;
                    markerHidden |= result.ReasonCode == ReasonMarkerHidden;
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

    /// <summary>How a one-column write ended.</summary>
    private enum ColumnWrite { Written, ChangedConcurrently, Failed, ReadsBackEmpty }

    /// <summary>The result for a column that was not written: a concurrent edit (nothing written, next run) or a failure.</summary>
    private static FollowParentsResult Unwritten(FollowParentsResult result, ColumnWrite outcome, string what) =>
        outcome switch
        {
            ColumnWrite.ChangedConcurrently => result with
            {
                Outcome = FollowParentsOutcome.Incomplete, ReasonCode = ReasonChangedConcurrently,
                Detail = $"{what} changed while it was being decided; nothing was written (the next run decides again)",
            },
            ColumnWrite.ReadsBackEmpty => result with
            {
                Outcome = FollowParentsOutcome.Incomplete, ReasonCode = ReasonMarkerHidden,
                Detail = $"{what} was written but reads back empty: field-level security hides {AccessInheritance.Column} from the BFF",
            },
            _ => result with
            {
                Outcome = FollowParentsOutcome.Incomplete,
                ReasonCode = what.Contains("record", StringComparison.Ordinal) ? ReasonMarkerNotWritten : ReasonPermissionNotWritten,
                Detail = $"{what} could not be written",
            },
        };

    /// <summary>
    /// Writes one column, <c>sprk_accesspermission</c> — only when it still holds <paramref name="expected"/> (verifier c:
    /// re-read and compare just before the write, so a user's concurrent edit is never overwritten; the app-only reader has no
    /// row-version write).
    /// </summary>
    private async Task<ColumnWrite> WritePermissionAsync(
        string logical, Guid recordId, int? expected, int to, IReadOnlyList<SecureFilingParent> parents, CancellationToken ct)
    {
        try
        {
            var now = await ReadOwnAccessAsync(logical, recordId, ct).ConfigureAwait(false);
            if (now is null || now.Permission != expected)
            {
                _logger.LogInformation(
                    "[FOLLOW-PARENT] {Table} {RecordId}: its Access Permission changed while it was being decided ({Expected} -> {Now}); " +
                    "not written.", logical, recordId, expected?.ToString() ?? "null", now?.Permission?.ToString() ?? "gone");
                return ColumnWrite.ChangedConcurrently;
            }

            await _dataverse.UpdateAsync(logical, recordId,
                new Dictionary<string, object> { [AccessPermissionColumn] = new OptionSetValue(to) }, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "[FOLLOW-PARENT] {Table} {RecordId}: Access Permission {From} -> {To} (follows {Parents}).",
                logical, recordId, expected?.ToString() ?? "null", to, string.Join(", ", parents.Select(p => $"{p.Table}:{p.Id:D}")));
            return ColumnWrite.Written;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[FOLLOW-PARENT] {Table} {RecordId}: writing Access Permission {To} failed; retried.", logical, recordId, to);
            return ColumnWrite.Failed;
        }
    }

    /// <summary>
    /// Writes one column, the access record — only when it still holds <paramref name="expected"/> (verifier c). A failure is
    /// reported by the caller (verifier e); the next run reads the previous record.
    /// </summary>
    private async Task<ColumnWrite> WriteMarkerAsync(
        string logical, Guid recordId, string? expected, AccessInheritance marker, CancellationToken ct)
    {
        try
        {
            var now = await ReadOwnAccessAsync(logical, recordId, ct).ConfigureAwait(false);
            if (now is null || !string.Equals(now.Marker ?? string.Empty, expected ?? string.Empty, StringComparison.Ordinal))
            {
                _logger.LogInformation("[FOLLOW-PARENT] {Table} {RecordId}: its {Column} changed while it was being decided; not written.",
                    logical, recordId, AccessInheritance.Column);
                return ColumnWrite.ChangedConcurrently;
            }

            await _dataverse.UpdateAsync(logical, recordId,
                new Dictionary<string, object> { [AccessInheritance.Column] = marker.Serialize() }, ct).ConfigureAwait(false);

            // Read it back: a secured column the BFF can write but not read answers EMPTY - indistinguishable, on a read,
            // from "no record yet". Here the two ARE told apart (it was just written), and that is reported, not trusted.
            var back = await ReadOwnAccessAsync(logical, recordId, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(back?.Marker))
            {
                _accessRecordReadable = false;
                _logger.LogError("[FOLLOW-PARENT] {Table} {RecordId}: its {Column} was written but reads back empty - field-level " +
                    "security hides it from the BFF. Nothing is loosened until it can be read.", logical, recordId, AccessInheritance.Column);
                return ColumnWrite.ReadsBackEmpty;
            }

            return ColumnWrite.Written;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[FOLLOW-PARENT] {Table} {RecordId}: writing its {Column} failed; the next run reads the previous one.",
                logical, recordId, AccessInheritance.Column);
            return ColumnWrite.Failed;
        }
    }

    /// <summary>
    /// Task 175 fix round 2 (K1, owner round 87 item 1 for records secured before this deploy): BEFORE a matter or project's
    /// flag is cleared, every work assignment and project filed below it that has NO access record yet gets one by the backfill
    /// rule, decided while the parent is still secure — so a Secure it holds through that parent is recorded as INHERITED and
    /// follows the parent out in the cascade after. At most <see cref="MaxInlineCascade"/> records; best effort (never throws):
    /// a record not reached stays secure (the backfill rule never loosens) and is reported.
    /// </summary>
    public async Task<int> RecordBelowBeforeUnsecureAsync(string parentTable, Guid parentId, string traceId, CancellationToken ct)
    {
        if (!IsParent(parentTable))
            return 0;

        var recorded = 0;
        try
        {
            var walk = await ListFiledRootsBelowAsync(_dataverse, _logger, new[] { (parentTable.Trim().ToLowerInvariant(), parentId) }, ct, _recordTypes)
                .ConfigureAwait(false);
            var examined = 0;
            foreach (var root in walk.Roots.Where(r => r.Confirmed))
            {
                if (examined++ >= MaxInlineCascade)
                    break;
                var own = await ReadOwnAccessAsync(root.Table, root.Id, ct).ConfigureAwait(false);
                if (own is null || !string.IsNullOrEmpty(own.Marker))
                    continue;
                var follow = await FollowParentsAsync(root.Table, root.Id, traceId, ct).ConfigureAwait(false);
                if (follow.MarkerWritten)
                    recorded++;
                else if (!follow.IsComplete || follow.Outcome == FollowParentsOutcome.Undetermined)
                    _logger.LogWarning("[FOLLOW-PARENT] {Table} {RecordId}: its access record was not written before {ParentTable} {ParentId} " +
                        "was un-secured ({Code}); it stays secure (the backfill rule never loosens).", root.Table, root.Id, parentTable, parentId,
                        follow.ReasonCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[FOLLOW-PARENT] The records below {Table} {Id} could not all be recorded before its un-secure; any " +
                "not recorded stays secure.", parentTable, parentId);
        }

        _logger.LogInformation("[FOLLOW-PARENT] Below {Table} {Id}: {Recorded} access record(s) written before its un-secure. TraceId={TraceId}",
            parentTable, parentId, recorded, traceId);
        return recorded;
    }

    // ── Task 175 fix round 2: whether access records can be trusted (cached for this scope: one job pass, one request) ──

    private bool? _accessRecordColumnSecured;
    private bool? _accessRecordReadable;

    /// <summary>Why no access record (or this EMPTY one) can be trusted, or null when it can.</summary>
    private async Task<(string Code, string Detail)?> AccessRecordDistrustAsync(string? marker, CancellationToken ct)
    {
        if (!await AccessRecordColumnSecuredAsync(ct).ConfigureAwait(false))
            return (ReasonAccessRecordUnsecured, $"{AccessInheritance.Column} is not field-secured, so no access record is trusted");
        if (string.IsNullOrEmpty(marker) && !await AccessRecordReadableAsync(ct).ConfigureAwait(false))
            return (ReasonMarkerHidden, $"its {AccessInheritance.Column} reads empty and the BFF's read of that column could not be proven");
        return null;
    }

    /// <summary>
    /// K2: the column's metadata says <c>IsSecured</c> on both tables. Anything else — not secured, missing, unreadable — is
    /// false (fail closed). Asked once per scope.
    /// </summary>
    private async Task<bool> AccessRecordColumnSecuredAsync(CancellationToken ct)
    {
        if (_accessRecordColumnSecured is { } known)
            return known;
        try
        {
            foreach (var table in new[] { WorkAssignment, Project })
            {
                var rows = await _webApi.QueryAsync<AttributeSecurityRow>(
                    $"EntityDefinitions(LogicalName='{table}')/Attributes", $"LogicalName eq '{AccessInheritance.Column}'", "IsSecured",
                    cancellationToken: ct).ConfigureAwait(false);
                if (rows is not { Count: 1 } || rows[0].IsSecured != true)
                {
                    _logger.LogError("[FOLLOW-PARENT] {Table}.{Column} is not field-secured ({Found} definition(s)).", table,
                        AccessInheritance.Column, rows?.Count ?? 0);
                    return (_accessRecordColumnSecured = false).Value;
                }
            }

            return (_accessRecordColumnSecured = true).Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[FOLLOW-PARENT] Whether {Column} is field-secured could not be read; no access record is trusted.",
                AccessInheritance.Column);
            return (_accessRecordColumnSecured = false).Value;
        }
    }

    /// <summary>
    /// F1: the BFF's own read of the column is PROVEN — a non-empty record read back in this scope, or the BFF application user
    /// holds the System Administrator role, or a field security profile it is a member of grants Read on the column on both
    /// tables. Anything else, a fault included, is unproven (fail closed). Asked at most once per scope.
    /// </summary>
    private async Task<bool> AccessRecordReadableAsync(CancellationToken ct)
    {
        if (_accessRecordReadable is { } known)
            return known;
        try
        {
            var me = (await _webApi.QueryAsync<SystemUserIdRow>("systemusers", "Microsoft.Dynamics.CRM.EqualUserId(PropertyName='systemuserid')",
                "systemuserid", cancellationToken: ct).ConfigureAwait(false)).SingleOrDefault()?.SystemUserId;
            if (me is not { } user || user == Guid.Empty)
                return Unproven("the BFF's own user could not be read");

            var admin = await _webApi.QueryAsync<RoleIdRow>($"systemusers({user})/systemuserroles_association",
                "name eq 'System Administrator'", "roleid", cancellationToken: ct).ConfigureAwait(false);
            if (admin.Count > 0)
                return (_accessRecordReadable = true).Value;

            var profiles = (await _webApi.QueryAsync<ProfileIdRow>($"systemusers({user})/systemuserprofiles_association", null,
                "fieldsecurityprofileid", cancellationToken: ct).ConfigureAwait(false))
                .Select(p => p.FieldSecurityProfileId).Where(id => id != Guid.Empty).Distinct().ToList();
            if (profiles.Count == 0)
                return Unproven("the BFF's user is in no field security profile");

            var grants = await _webApi.QueryAsync<FieldPermissionRow>("fieldpermissions",
                $"attributelogicalname eq '{AccessInheritance.Column}' and canread eq 4 and (" +
                string.Join(" or ", profiles.Select(p => $"_fieldsecurityprofileid_value eq {p}")) + ")",
                "entityname", cancellationToken: ct).ConfigureAwait(false);
            var tables = grants.Select(g => g.EntityName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return tables.Contains(WorkAssignment) && tables.Contains(Project)
                ? (_accessRecordReadable = true).Value
                : Unproven("no field security profile of the BFF's user grants Read on the column on both tables");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[FOLLOW-PARENT] Whether the BFF can read {Column} could not be established.", AccessInheritance.Column);
            return (_accessRecordReadable = false).Value;
        }

        bool Unproven(string why)
        {
            _logger.LogError("[FOLLOW-PARENT] The BFF's read of {Column} is not proven: {Why}. No EMPTY access record is overwritten.",
                AccessInheritance.Column, why);
            return (_accessRecordReadable = false).Value;
        }
    }

    private sealed class AttributeSecurityRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("IsSecured")]
        public bool? IsSecured { get; set; }
    }

    private sealed class SystemUserIdRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("systemuserid")]
        public Guid SystemUserId { get; set; }
    }

    private sealed class RoleIdRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("roleid")]
        public Guid RoleId { get; set; }
    }

    private sealed class ProfileIdRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("fieldsecurityprofileid")]
        public Guid FieldSecurityProfileId { get; set; }
    }

    private sealed class FieldPermissionRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("entityname")]
        public string? EntityName { get; set; }
    }

    /// <summary>A record's own Secure flag, Access Permission and access record (null: it does not exist). A fault propagates.</summary>
    private sealed record OwnAccess(bool? Secure, int? Permission, string? Marker);

    private async Task<OwnAccess?> ReadOwnAccessAsync(string logical, Guid recordId, CancellationToken ct)
    {
        var query = Query(logical, new[] { IsSecureColumn, AccessPermissionColumn, AccessInheritance.Column });
        query.TopCount = 1;
        query.Criteria.AddCondition(logical + "id", ConditionOperator.Equal, recordId);
        var row = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        if (row is null)
            return null;
        var marker = row.GetAttributeValue<string>(AccessInheritance.Column);
        if (!string.IsNullOrEmpty(marker))
            _accessRecordReadable = true; // a non-empty record read back proves the BFF reads the column (fix round 2, F1)
        return new OwnAccess(row.GetAttributeValue<bool?>(IsSecureColumn), PermissionOfRow(row), marker);
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
