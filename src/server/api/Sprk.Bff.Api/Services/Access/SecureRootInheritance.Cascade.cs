// unified-access-control-r2 task 175 (owner round 84, 2026-10-08): "Child access should always follow parent; if parent
// changes, then child changes. A protection is that if a child has a parent then the access cannot be changed manually."
// Follow-ups the same day: un-securing cascades ("Child follows"), replacing round 6 item 4; it applies to work assignments
// and projects ("all children").
//
// This file makes the STORED sprk_issecure and sprk_accesspermission of every filed work assignment and project equal to
// what its parents give it (task 174's effective rule: secure if any ancestor is; the most restrictive Access Permission),
// in both directions. Task 174 already makes ENFORCEMENT follow the parent whatever the stored values say, so a lagging
// stored value is never a hole; this brings the stored values (and so every reader of them, and the forms) into step.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — SecureRootInheritance secures a filed record (one direction); the unsecure endpoint un-secures ONE record
//       a caller names (F3); the 174 walk (ReadSecureParentsAsync) answers what a record's parents give it; task 173's
//       ChildAccessPermissionReconciler keeps the four CHILD tables' Access Permission in step (roots terminal).
//   (2) Extension — It IS an extension of SecureRootInheritance (a partial of the same class: the ONE owner of "a filed work
//       assignment or project follows its parents", called from the job, the parent's unsecure and the re-file paths —
//       CLAUDE.md §17 "one server-side owner per invariant"). The un-secure step is the unsecure endpoint's own steps
//       (UnsecureInheritedAsync, the ProvisionInheritedAsync precedent), never a second implementation. The decision is
//       the 174 walk, never a second filing reader. Not in task 173's reconciler: its walk follows the CHILD lineage map
//       (lookups only), and a root's filing includes the polymorphic pair, which only this class resolves.
//   (3) Cost of doing nothing — un-securing a matter leaves its filed work assignments secure (owned by the memberless
//       Secure team, own container) for good; a matter turned Standard leaves its children Restricted, and one turned
//       Restricted leaves them Standard for every reader of the stored column (#1478); the form shows values the server
//       does not apply.
//
// Placement (bff-extensions.md; ADR-052): BFF — the transition runs inside the parent's unsecure request and the re-file
// writers, the safety net in the existing SecureRootInheritanceJob (ADR-036: no new job, no timer). No package, endpoint,
// column, interface or DI registration.

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

    /// <summary>Filed under nothing: its own values stand and stay editable (round 84; F3 still applies to it).</summary>
    Parentless,

    /// <summary>
    /// What it is filed under (or its own flag) could not be read: nothing was loosened (a readably secure ancestor still
    /// secures it — the closed direction), and it is reported. Enforcement already treats it as secure and Restricted
    /// (task 174, fail closed).
    /// </summary>
    Undetermined,

    /// <summary>Its stored values already equal its parents'. Nothing was written.</summary>
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

    /// <summary>The secure step's result, when it ran (provisioning's own steps, <see cref="SecureRootInheritance.SecureIfFiledUnderSecureAsync"/>).</summary>
    public SecureRootInheritResult? Secured { get; init; }

    /// <summary><see cref="Unsecured"/>, <see cref="Refused"/> or <see cref="Failed"/> when the un-secure step ran; otherwise null.</summary>
    public string? UnsecureOutcome { get; init; }

    /// <summary>The stored Access Permission before, and the value written (null: not written).</summary>
    public int? PermissionFrom { get; init; }

    public int? PermissionTo { get; init; }

    /// <summary>Whether the record is (still) flagged secure after the call, as far as this call knows.</summary>
    public bool IsSecureAfter { get; init; }

    /// <summary>The DIRECT parents it follows (empty for a parentless or unread record).</summary>
    public IReadOnlyList<SecureFilingParent> Parents { get; init; } = Array.Empty<SecureFilingParent>();

    public string? ReasonCode { get; init; }

    public string? Detail { get; init; }

    /// <summary>Nothing is left to do for this record.</summary>
    public bool IsComplete => Outcome is FollowParentsOutcome.NotFound or FollowParentsOutcome.Parentless
        or FollowParentsOutcome.InStep or FollowParentsOutcome.Changed;

    /// <summary>The call changed something.</summary>
    public bool WroteAnything => PermissionTo is not null || UnsecureOutcome == Unsecured || Secured?.WroteAnything == true;
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
    public int Undetermined { get; init; }
    public int NotCompleted { get; init; }
    public int Deferred { get; init; }

    /// <summary>The last record the un-secure bound stopped after (the next run continues after it); null when none was deferred.</summary>
    public (string Table, Guid Id)? UnsecureResumeAfter { get; init; }

    /// <summary>Up to 50 "table:id: reason" lines for records not brought into step.</summary>
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

    /// <summary>Up to 200 changes: table, id, what changed.</summary>
    public IReadOnlyList<object> Changes { get; init; } = Array.Empty<object>();

    public bool IsComplete => Undetermined == 0 && NotCompleted == 0 && Deferred == 0;
}

/// <summary>
/// Task 175 (owner round 84): the refusal for a manual change to the access of a work assignment or project that has a
/// parent — "if a child has a parent then the access cannot be changed manually". One code for every surface that refuses
/// it: Make Secure / provisioning, <c>/unsecure-project</c>, and a BFF write of <c>sprk_accesspermission</c> or
/// <c>sprk_issecure</c> (<see cref="SecureRootInheritance.CheckRefileAsync"/>).
/// </summary>
public static class AccessFollowsParent
{
    /// <summary>The named 409 reason code.</summary>
    public const string ReasonCode = "sdap.access.access_follows_parent";

    /// <summary>The columns a record with a parent takes from it, and which nobody sets by hand (round 84).</summary>
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

    /// <summary>The 409 ProblemDetails, naming the first parent (type, id, name) in its extensions.</summary>
    internal static IResult Problem(
        string recordLabel, IReadOnlyList<FilingParentState> parents, string action, string traceId)
    {
        var named = parents.Select(p => p.Parent).ToList();
        var first = named[0];
        var label = recordLabel.ToLowerInvariant();
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Conflict",
            detail: $"This {label} is filed under {Describe(named)}, and its access follows it: its Secure designation and " +
                    $"Access Permission come from there and cannot be changed on the {label}. To {action}, change it on " +
                    $"{(named.Count == 1 ? "that record" : "those records")}, or file the {label} somewhere else. Nothing was changed.",
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

public sealed partial class SecureRootInheritance
{
    /// <summary>Records the parent's unsecure carries down in its own request; the rest are the job's (≤ 5 minutes).</summary>
    internal const int MaxInlineCascade = 50;

    private const int FiledListPageSize = 5000;
    private const int FiledListMaxPages = 20;
    private const int MaxProblemLines = 50;
    private const int MaxChangeLines = 200;

    /// <summary>
    /// What <paramref name="table"/> <paramref name="recordId"/> is filed under DIRECTLY (<see cref="SecureParentsAnswer.DirectParents"/>),
    /// and why it could not be read — the round-84 lock's question ("does this record have a parent?"). One level, the 174
    /// walk. A matter files under nothing.
    /// </summary>
    public Task<SecureParentsAnswer> FindFilingParentsAsync(string table, Guid recordId, CancellationToken ct) =>
        ReadSecureParentsAsync(_dataverse, _logger, table, recordId, ct, _recordTypes, maxDepth: 1);

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

    /// <summary>The Access Permission option for a rank (<see cref="FilingPermission.Rank"/>).</summary>
    internal static int PermissionOf(int rank) => rank switch
    {
        2 => InheritedAccessPermission.Restricted,
        1 => InheritedAccessPermission.Limited,
        _ => InheritedAccessPermission.Standard,
    };

    /// <summary>A stored value's rank for ORDERING the writes: an unknown option ranks highest (it is never loosened first).</summary>
    private static int StoredRank(int? value) => value switch
    {
        null or InheritedAccessPermission.Standard => 0,
        InheritedAccessPermission.Limited => 1,
        _ => 2,
    };

    /// <summary>
    /// Brings ONE work assignment or project's stored <c>sprk_issecure</c> and <c>sprk_accesspermission</c> into step with
    /// its parents (owner round 84): secure if any ancestor is (task 174's effective rule), and the most restrictive Access
    /// Permission that arrives through its DIRECT parents. A parentless record keeps its own values. Idempotent; writes only
    /// what differs; never throws a read fault.
    /// </summary>
    /// <remarks>
    /// <para><b>Order — fail closed (goal 5).</b> (a) A STRICTER Access Permission is written first. (b) Secure: provisioning's own
    /// steps (<see cref="SecureIfFiledUnderSecureAsync"/>). (c) Un-secure: the unsecure endpoint's own steps
    /// (<see cref="UnsecureProjectEndpoint.UnsecureInheritedAsync"/> — ownership to its parents' business unit's team and read
    /// back, related records out of isolation, shares revoked, the flag cleared LAST). (d) Only then a LOOSER Access Permission.
    /// A step that does not complete stops the call: the record stays secure and its Access Permission is not loosened.</para>
    /// <para><b>Undecidable.</b> A filing, parent or flag that cannot be read loosens nothing (a readably secure ancestor still
    /// secures it — the closed direction); an EMPTY own flag is never "secure, so un-secure it".</para>
    /// </remarks>
    public async Task<FollowParentsResult> FollowParentsAsync(string table, Guid recordId, string traceId, CancellationToken ct)
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
                ReasonCode = ReasonRecordUnreadable,
                Detail = "the record could not be read",
                IsSecureAfter = true,
            };
        }

        if (own is null)
            return new FollowParentsResult(logical, recordId, FollowParentsOutcome.NotFound);

        var ancestry = await ReadSecureParentsAsync(_dataverse, _logger, logical, recordId, ct, _recordTypes, MaxFilingDepth)
            .ConfigureAwait(false);
        var parents = ancestry.DirectParents.Select(p => p.Parent).ToList();
        var result = new FollowParentsResult(logical, recordId, FollowParentsOutcome.InStep)
        {
            PermissionFrom = own.Permission,
            IsSecureAfter = own.Secure != false,
            Parents = parents,
        };

        if (ancestry.IsKnown && ancestry.DirectParents.Count == 0)
            return result with { Outcome = FollowParentsOutcome.Parentless };

        if (!ancestry.IsKnown)
        {
            // The closed direction only: a readably secure ancestor still secures it (secure-if-any, as task 158's rule).
            SecureRootInheritResult? secured = null;
            if (ancestry.HasSecureParent && own.Secure != true)
                secured = await SecureIfFiledUnderSecureAsync(logical, recordId, traceId, ct).ConfigureAwait(false);

            _logger.LogWarning(
                "[FOLLOW-PARENT] {Table} {RecordId}: what it is filed under could not be read ({Why}); nothing was loosened.",
                logical, recordId, ancestry.Unverifiable);
            return result with
            {
                Outcome = FollowParentsOutcome.Undetermined,
                Secured = secured,
                IsSecureAfter = own.Secure != false || secured?.IsSecure == true,
                ReasonCode = ReasonParentUnverifiable,
                Detail = ancestry.Unverifiable,
            };
        }

        var targetSecure = ancestry.HasSecureParent;
        var target = PermissionOf(ancestry.DirectParents.Max(p => p.EffectiveRank));
        var stored = own.Permission ?? InheritedAccessPermission.Standard;
        var wrote = false;

        // (a) A stricter Access Permission first.
        if (stored != target && FilingPermission.Rank(target) > StoredRank(own.Permission))
        {
            if (!await WritePermissionAsync(logical, recordId, own.Permission, target, parents, ct).ConfigureAwait(false))
                return result with { Outcome = FollowParentsOutcome.Incomplete, ReasonCode = ReasonPermissionNotWritten, Detail = "its Access Permission could not be written" };
            result = result with { PermissionTo = target };
            stored = target;
            wrote = true;
        }

        // (b) Secure.
        if (targetSecure && own.Secure != true)
        {
            var secured = await SecureIfFiledUnderSecureAsync(logical, recordId, traceId, ct).ConfigureAwait(false);
            result = result with { Secured = secured, IsSecureAfter = secured.IsSecure || own.Secure != false };
            if (!secured.IsComplete)
            {
                return result with
                {
                    Outcome = FollowParentsOutcome.Incomplete,
                    ReasonCode = secured.ReasonCode ?? ReasonSharesIncomplete,
                    Detail = secured.Detail ?? "it is not secure yet",
                };
            }

            wrote |= secured.WroteAnything;
        }

        // (c) Un-secure: its only secure sources are no longer secure (round 84, replacing round 6 item 4).
        else if (!targetSecure && own.Secure == true)
        {
            var unsecure = await UnsecureFollowingParentsAsync(logical, recordId, ancestry.DirectParents, traceId, ct).ConfigureAwait(false);
            result = result with
            {
                UnsecureOutcome = unsecure.Outcome,
                ReasonCode = unsecure.Code,
                Detail = unsecure.Detail,
                IsSecureAfter = unsecure.Outcome != FollowParentsResult.Unsecured,
            };
            if (unsecure.Outcome != FollowParentsResult.Unsecured)
                return result with { Outcome = FollowParentsOutcome.Incomplete };
            wrote = true;
        }
        else if (!targetSecure && own.Secure is null)
        {
            // Owner round 17 item 3: an EMPTY flag is never "not secure" — and never "secure, so un-secure it" either.
            return result with
            {
                Outcome = FollowParentsOutcome.Undetermined,
                ReasonCode = ReasonOwnFlagEmpty,
                Detail = "its own secure flag is empty (empty is never read as either value)",
            };
        }

        // (d) Only now a looser (or otherwise different) Access Permission.
        if (stored != target)
        {
            if (!await WritePermissionAsync(logical, recordId, own.Permission, target, parents, ct).ConfigureAwait(false))
                return result with { Outcome = FollowParentsOutcome.Incomplete, ReasonCode = ReasonPermissionNotWritten, Detail = "its Access Permission could not be written" };
            result = result with { PermissionTo = target };
            wrote = true;
        }

        return result with { Outcome = wrote ? FollowParentsOutcome.Changed : FollowParentsOutcome.InStep, ReasonCode = null, Detail = null };
    }

    /// <summary>The Access Permission could not be written (task 175): left as it was, reported, retried.</summary>
    internal const string ReasonPermissionNotWritten = "sdap.inherit.permission_not_written";

    /// <summary>The record's own <c>sprk_issecure</c> is EMPTY (task 175): neither secured nor un-secured on a guess.</summary>
    internal const string ReasonOwnFlagEmpty = "sdap.inherit.own_flag_empty";

    /// <summary>No owner could be resolved for the un-secured record (task 175): it stays secure.</summary>
    internal const string ReasonCascadeOwnerUnresolved = "sdap.inherit.unsecure_owner_unresolved";

    /// <summary>
    /// A parent's un-secure (or a re-file) carried down to every work assignment and project filed below
    /// <paramref name="parentTable"/> <paramref name="parentId"/>, top-down (each level after the one above it, so a project
    /// un-secured here is ordinary before what is filed under it is decided), at most <see cref="MaxInlineCascade"/> records;
    /// the rest — and anything deeper than <see cref="MaxFilingDepth"/> — are left to <see cref="SecureRootInheritanceJob"/>.
    /// A listing that cannot be read is <see cref="FiledCascadePass.Unreadable"/> (nothing done, the job does it). Never throws.
    /// </summary>
    public async Task<FiledCascadePass> CascadeBelowAsync(string parentTable, Guid parentId, string traceId, CancellationToken ct)
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
    /// The job's pass (task 175 goal 3): every work assignment and project that is filed under something is decided over the
    /// ONE batched walk, and only those whose stored values differ from their parents' are brought into step
    /// (<see cref="FollowParentsAsync"/>, a fresh read each). A project changed here sends the records filed under it round
    /// again in the SAME run (up to <see cref="MaxFilingDepth"/> rounds), so a chain converges in one run. Bounded: at most
    /// <paramref name="maxUnsecures"/> un-secures (each moves ownership and revokes shares; in a fixed order resumed after
    /// <paramref name="unsecureResumeAfter"/>, so one that keeps failing never starves the rest) and
    /// <paramref name="maxPermissionWrites"/> Access Permission writes; the rest are deferred to the next run.
    /// </summary>
    /// <exception cref="InvalidOperationException">The listing could not be completed (ADR-036 A1 rule 4: a failed run).</exception>
    public async Task<FollowParentsPass> FollowParentsPassAsync(
        string traceId, (string Table, Guid Id)? unsecureResumeAfter, int maxUnsecures, int maxPermissionWrites, CancellationToken ct)
    {
        var listed = await ListFiledRecordsAsync(ct).ConfigureAwait(false);

        int parentless = 0, inStep = 0, unsecured = 0, permissions = 0, undetermined = 0, notCompleted = 0, deferred = 0;
        var problems = new List<string>();
        var changes = new List<object>();
        (string Table, Guid Id)? lastUnsecureTried = null;
        var unsecuresTried = 0;
        var seen = new HashSet<(string, Guid)>();

        // Round 1: the batched decision over every filed record; act only where a stored value differs.
        var needs = new List<(FiledRecordRow Row, bool Unsecure)>();
        foreach (var group in listed.GroupBy(r => r.Table))
        {
            var answers = await ReadSecureParentsOfManyAsync(_dataverse, _logger, group.Key, group.Select(r => r.Id).ToList(), ct,
                _recordTypes, MaxFilingDepth).ConfigureAwait(false);
            foreach (var row in group)
            {
                seen.Add((row.Table, row.Id));
                var answer = answers[row.Id];
                if (answer.IsKnown && answer.DirectParents.Count == 0)
                {
                    parentless++;
                    continue;
                }

                if (!answer.IsKnown)
                {
                    undetermined++;
                    Problem($"{row.Table}:{row.Id:D}: {answer.Unverifiable}");
                    continue;
                }

                var target = PermissionOf(answer.DirectParents.Max(p => p.EffectiveRank));
                var unsecure = row.Secure == true && !answer.HasSecureParent;
                var permissionDiffers = (row.Permission ?? InheritedAccessPermission.Standard) != target;
                if (row.Secure is null && !answer.HasSecureParent)
                {
                    undetermined++;
                    Problem($"{row.Table}:{row.Id:D}: its own secure flag is empty");
                }
                else if (unsecure || permissionDiffers)
                {
                    needs.Add((row, unsecure));
                }
                else
                {
                    inStep++; // a not-yet-secure record under a secure parent is the job's securing loop's, not this pass's
                }
            }
        }

        // The un-secures in a fixed order resumed after the cursor; then the Access-Permission-only records.
        var unsecureOrder = needs.Where(n => n.Unsecure).Select(n => n.Row)
            .OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
        var start = unsecureResumeAfter is { } after
            ? unsecureOrder.FindIndex(r => CompareKeys((r.Table, r.Id), after) > 0)
            : 0;
        if (start < 0)
            start = 0;
        var ordered = unsecureOrder.Skip(start).Concat(unsecureOrder.Take(start)).Select(r => (Row: r, Unsecure: true))
            .Concat(needs.Where(n => !n.Unsecure).Select(n => (n.Row, Unsecure: false))
                .OrderBy(n => n.Row.Table, StringComparer.Ordinal).ThenBy(n => n.Row.Id))
            .ToList();

        var changedParents = new List<(string Table, Guid Id)>();
        foreach (var (row, isUnsecure) in ordered)
        {
            if ((isUnsecure && unsecuresTried >= maxUnsecures) || (!isUnsecure && permissions >= maxPermissionWrites))
            {
                deferred++;
                continue;
            }

            if (isUnsecure)
            {
                unsecuresTried++;
                lastUnsecureTried = (row.Table, row.Id);
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
                if (unsecuresTried >= maxUnsecures && permissions >= maxPermissionWrites)
                {
                    deferred++;
                    continue;
                }

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

    /// <summary>A record's own Secure flag and Access Permission (null: it does not exist). A fault propagates.</summary>
    private sealed record OwnAccess(bool? Secure, int? Permission);

    private async Task<OwnAccess?> ReadOwnAccessAsync(string logical, Guid recordId, CancellationToken ct)
    {
        var query = Query(logical, new[] { IsSecureColumn, AccessPermissionColumn });
        query.TopCount = 1;
        query.Criteria.AddCondition(logical + "id", ConditionOperator.Equal, recordId);
        var row = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        return row is null ? null : new OwnAccess(row.GetAttributeValue<bool?>(IsSecureColumn), PermissionOfRow(row));
    }

    private static int? PermissionOfRow(Entity row) =>
        row.Attributes.TryGetValue(AccessPermissionColumn, out var value)
            ? value switch { OptionSetValue o => o.Value, int i => i, _ => null }
            : null;

    /// <summary>One filed record as the job's listing reads it.</summary>
    private sealed record FiledRecordRow(string Table, Guid Id, bool? Secure, int? Permission);

    /// <summary>
    /// Every work assignment and project filed under SOMETHING (a typed regarding lookup, or the pair), with its own flag and
    /// Access Permission — paged; past the page ceiling it throws rather than decide on part of them.
    /// </summary>
    private async Task<IReadOnlyList<FiledRecordRow>> ListFiledRecordsAsync(CancellationToken ct)
    {
        var rows = new List<FiledRecordRow>();
        foreach (var table in new[] { Project, WorkAssignment })
        {
            var query = new QueryExpression(table)
            {
                ColumnSet = new ColumnSet(table + "id", IsSecureColumn, AccessPermissionColumn),
                NoLock = true,
                PageInfo = new PagingInfo { Count = FiledListPageSize, PageNumber = 1 },
            };
            var filed = new FilterExpression(LogicalOperator.Or);
            foreach (var (column, _) in TypedFilingColumns[table])
                filed.AddCondition(column, ConditionOperator.NotNull);
            filed.AddCondition(PairIdColumn, ConditionOperator.NotNull);
            query.Criteria.AddFilter(filed);

            for (var page = 1; ; page++)
            {
                if (page > FiledListMaxPages)
                    throw new InvalidOperationException(
                        $"{table} still had filed records to list after {FiledListMaxPages} pages; nothing is decided on part of them.");

                var result = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                rows.AddRange(result.Entities.Select(e =>
                    new FiledRecordRow(table, e.Id, e.GetAttributeValue<bool?>(IsSecureColumn), PermissionOfRow(e))));
                if (!result.MoreRecords)
                    break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = result.PagingCookie;
            }
        }

        return rows;
    }
}
