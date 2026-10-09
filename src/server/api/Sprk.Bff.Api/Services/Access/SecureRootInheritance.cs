// unified-access-control-r2 task 158 (owner round 6, 2026-10-02): "a work assignment (or project) filed under a SECURE
// matter or project is itself secure — yes". Such a record becomes a real secure root: sprk_issecure, the named-team owner,
// its own container and the creator share — through provisioning's own steps — and the parent's sharees can see it.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — ProvisionProjectEndpoint secures ONE record a caller names; SecureChildReconciler (task 148) re-owns the
//       CHILDREN of a record and deliberately never touches a root filed under it; RecordOwnershipResolver decides a child's
//       owner and refuses a root under a secure parent (146: "a root under a secure parent is task 158"); the synchronizer
//       (149) mirrors onto Secure-team-owned CHILD tables only; RecordContainerResolver (155) only reads a record's filing to
//       place its files. Nothing answers "this work assignment / project is filed under a secure record, so make it secure".
//   (2) Extension — Not inside ProvisionProjectEndpoint: the question "which roots are filed under which, and is the parent
//       secure" is asked by five writers, the transition and a job; the endpoint stays the ONE place that secures a record
//       (this class calls it — ProvisionInheritedAsync — never re-implements a step). Not inside the reconciler: its walk
//       and its "never move a root" rule are load-bearing for 148 (a child pass re-owns, a root needs a container and a
//       creator share). Not inside the resolver: it decides owners and holds no provisioning dependencies.
//   (3) Cost-of-doing-nothing — a work assignment filed under a secure matter stays readable by its whole business unit,
//       its files go to the matter's container (155 interpretation iii) while its access follows its own business unit,
//       and a contact granted on it gets the access FR-22 forbids on the secure matter (task 155 round f3 finding).
//
// Placement (bff-extensions.md; ADR-052): in the BFF. Create and re-file are BFF write paths (the caller is told what
// happened), the transition runs inside provisioning's request, and the safety net is an in-process IScheduledJob
// (SecureRootInheritanceJob, ADR-036). BFF identity, BFF domain code, low volume. No package, no column, no plugin
// (ADR-002), no new endpoint.

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>What became of one work assignment or project the inheritance looked at.</summary>
public enum SecureRootInheritOutcome
{
    /// <summary>The record does not exist (any more).</summary>
    NotFound,

    /// <summary>No matter or project it is filed under is secure: nothing to do.</summary>
    NotFiledUnderSecure,

    /// <summary>Filed under a secure record and already isolated (owned by the named team, with its own container).</summary>
    AlreadySecure,

    /// <summary>Filed under a secure record and made secure by this call (provisioning completed).</summary>
    Secured,

    /// <summary>Whether a record it is filed under is secure could not be determined (ADR-003): nothing was written.</summary>
    Unverifiable,

    /// <summary>Provisioning refused (a 4xx — e.g. no usable creator, the creator on the No Access list).</summary>
    Refused,

    /// <summary>Provisioning failed (a 5xx — a read or write failed; retried by the job).</summary>
    Failed,
}

/// <summary>
/// A secure matter or project a work assignment or project is filed under. Whether it is ISOLATED is not carried: every
/// consumer that depends on it (the mirror, the reverse rule) reads it from the synchronizer at the moment it acts.
/// </summary>
public sealed record SecureFilingParent(string Table, Guid Id, string? Name = null);

/// <summary>What the inheritance did to one record.</summary>
public sealed record SecureRootInheritResult(
    string Table,
    Guid Id,
    SecureRootInheritOutcome Outcome,
    string? ReasonCode,
    string? Detail,
    IReadOnlyList<SecureFilingParent> SecureParents,
    SecureChildShareSyncResult? Shares)
{
    /// <summary>Secure after the call.</summary>
    public bool IsSecure => Outcome is SecureRootInheritOutcome.AlreadySecure or SecureRootInheritOutcome.Secured;

    /// <summary>Its parents' sharees were all given to it (or there was nothing to give).</summary>
    public bool SharesComplete => Shares is null || Shares.IsComplete;

    /// <summary>Nothing is left to do for this record.</summary>
    public bool IsComplete =>
        Outcome is SecureRootInheritOutcome.NotFound or SecureRootInheritOutcome.NotFiledUnderSecure
        || (IsSecure && SharesComplete);

    /// <summary>The call changed something (secured the record, or gave it a sharee).</summary>
    public bool WroteAnything =>
        Outcome == SecureRootInheritOutcome.Secured || (Shares is { } s && s.SharesGranted + s.SharesChanged > 0);

    /// <summary>
    /// Task 158 r1 (owner round 31 item 2): a record CREATED into isolation whose creator could not be shared was deleted
    /// again (read back gone) — the create is refused, never left as a row nobody can open.
    /// </summary>
    public bool RowRemoved { get; init; }

    /// <summary>
    /// Task 158 r1: a record CREATED into isolation whose creator could not be shared AND which could not be deleted again:
    /// it exists, secure, and nobody but an administrator can open it until the job shares it to its creator (≤ 5 minutes).
    /// The writer says exactly that — never "shared to you".
    /// </summary>
    public bool RowStranded { get; init; }

    /// <summary>
    /// Task 158 r1c-v1 (verifier item 7): whether what is left undone finishes on its own — the secure-root inheritance
    /// job retries it every few minutes. <c>false</c> when provisioning REFUSED (a 4xx: the person it is secured for is on a
    /// No Access list — e.g. walled between the create's plan and its provisioning — or cannot be named, …): every run
    /// refuses again, so an administrator must act, and no writer may promise "automatically within a few minutes".
    /// </summary>
    public bool CompletesAutomatically => Outcome != SecureRootInheritOutcome.Refused;

    /// <summary>The outcome as the response names it.</summary>
    public string WireOutcome => !SharesComplete && IsSecure
        ? "failed"
        : Outcome switch
        {
            SecureRootInheritOutcome.Secured => "secured",
            SecureRootInheritOutcome.AlreadySecure => "already-secure",
            SecureRootInheritOutcome.NotFiledUnderSecure => "not-filed-under-secure",
            SecureRootInheritOutcome.NotFound => "not-found",
            SecureRootInheritOutcome.Unverifiable => "unverifiable",
            SecureRootInheritOutcome.Refused => "refused",
            _ => "failed",
        };
}

/// <summary>How a pass over the records filed under one matter or project ended.</summary>
public enum SecureFiledRootsStatus
{
    /// <summary>Every filed record is secure (or turned out not to be filed under it).</summary>
    Completed,

    /// <summary>At least one filed record is not secure yet; each is listed with its reason.</summary>
    Incomplete,

    /// <summary>Nothing is filed under this record for the rule (a work assignment), or it is not secure.</summary>
    NotApplicable,

    /// <summary>The filed records could not be read: nothing was decided.</summary>
    Failed,
}

/// <summary>A pass over the work assignments and projects filed under one secure matter or project.</summary>
public sealed record SecureFiledRootsPass(SecureFiledRootsStatus Status, IReadOnlyList<SecureRootInheritResult> Results, string? Detail)
{
    /// <summary>True when nothing filed under the record is left to secure.</summary>
    public bool IsComplete => Status is SecureFiledRootsStatus.Completed or SecureFiledRootsStatus.NotApplicable;

    /// <summary>Records this pass made secure.</summary>
    public int Secured => Results.Count(r => r.Outcome == SecureRootInheritOutcome.Secured);

    /// <summary>The pass changed something.</summary>
    public bool WroteAnything => Results.Any(r => r.WroteAnything);

    /// <summary>The response shape.</summary>
    public SecureFiledRecordsSummary Summary() => new(
        Status.ToString(),
        Examined: Results.Count,
        Secured: Secured,
        AlreadySecure: Results.Count(r => r.Outcome == SecureRootInheritOutcome.AlreadySecure),
        Remaining: Results.Count(r => !r.IsComplete),
        Records: Results
            .Select(r => new SecureFiledRecordOutcome(
                SecureRootInheritance.WireTokenFor(r.Table), r.Id, r.WireOutcome,
                r.IsComplete ? null : r.ReasonCode ?? SecureRootInheritance.ReasonSharesIncomplete,
                r.IsComplete ? null : r.Detail ?? r.Shares?.Detail))
            .ToList());

    internal static SecureFiledRootsPass NotApplicable(string detail) =>
        new(SecureFiledRootsStatus.NotApplicable, Array.Empty<SecureRootInheritResult>(), detail);
}

/// <summary>
/// The secure matters / projects a record is filed under that COULD be read, and — when some other record it is filed under
/// could not be (an unreadable or empty flag, an unresolvable pair) — why. Secure-if-any: one readable secure parent is
/// enough to make the record secure, whatever the others say; an unreadable one only means "not provably not secure" (it
/// refuses a write that would otherwise proceed, and it holds the sharee mirror, never the securing).
/// </summary>
public sealed record SecureParentsAnswer(IReadOnlyList<SecureFilingParent> SecureParents, string? Unverifiable)
{
    /// <summary>Every record it is filed under was read.</summary>
    public bool IsKnown => Unverifiable is null;

    /// <summary>At least one record it is filed under is (readably) secure — the record is secure whatever the rest say.</summary>
    public bool HasSecureParent => SecureParents.Count > 0;

    /// <summary>
    /// Task 174 (owner round 84): the most restrictive <c>sprk_accesspermission</c> among the records the climb read above
    /// this one (Restricted over Limited), and the record that carries it — <c>null</c> when none of them is Limited or
    /// Restricted. Read in the SAME parent read as the Secure flag (no extra query). Only as deep as the climb went: the
    /// sharee rule's one-level climb reports the direct parents' value, the walls' climb every ancestor's.
    /// </summary>
    public FilingPermission? StrictestPermission { get; init; }

    /// <summary>
    /// Task 174 (verifier F1-d): the records this one is filed under DIRECTLY, each with the effective Secure flag and
    /// Access Permission rank that arrive through it (its own values, folded with everything above it that the climb read).
    /// Display names only these: a direct parent is already visible on the record's own lookup, a grandparent is not.
    /// </summary>
    public IReadOnlyList<FilingParentState> DirectParents { get; init; } = Array.Empty<FilingParentState>();
}

/// <summary>Task 174: one DIRECT filing parent and what arrives through it — <see cref="EffectiveSecure"/> (it or anything
/// above it is secure) and <see cref="EffectiveRank"/> (<see cref="FilingPermission.Rank"/> of the strictest Access
/// Permission on it or above it).</summary>
public sealed record FilingParentState(SecureFilingParent Parent, bool EffectiveSecure, int EffectiveRank);

/// <summary>
/// Task 174 (owner round 84): an Access Permission a record inherits from a record it is filed under — the option value
/// (<c>ExternalParticipationService.AccessPermissionLimited</c> or <c>AccessPermissionRestricted</c>) and that record.
/// </summary>
public sealed record FilingPermission(int Value, SecureFilingParent From)
{
    /// <summary>Restricted (2) over Limited (1); any other value is Standard (0), as the flag read maps it.</summary>
    internal static int Rank(int? value) => value switch
    {
        Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalParticipationService.AccessPermissionRestricted => 2,
        Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalParticipationService.AccessPermissionLimited => 1,
        _ => 0,
    };

    /// <summary>The stricter of two (the first on a tie); <c>null</c> stands for Standard.</summary>
    internal static FilingPermission? Stricter(FilingPermission? a, FilingPermission? b) =>
        b is null || (a is not null && Rank(a.Value) >= Rank(b.Value)) ? a : b;
}

/// <summary>
/// Task 158 r1 (owner round 30): what the reverse fan-out of a secure parent's unshare did to the inherited shares it had
/// passed on — rows examined, shares removed (or put back to the mask they raised), rows ended with the share kept
/// (modified, direct, justified by another parent, the last reader), and rows not done (reported; the job completes them).
/// </summary>
/// <param name="NotRegiven">Task 158 r1c-v1 (verifier item 3): records whose share was removed while the secure records they
/// are filed under NOW still pass part of it on, where that part could not be given back at once (the job gives it).</param>
public sealed record InheritedUnsharePass(
    SecureFiledRootsStatus Status, int Rows, int Removed, int Kept, int NotDone, string? Detail, int NotRegiven = 0)
{
    /// <summary>Nothing left to end.</summary>
    public bool IsComplete => Status is SecureFiledRootsStatus.Completed or SecureFiledRootsStatus.NotApplicable;

    /// <summary>
    /// Task 158 r1c-v2 (round 39 item 1): the records (and principals) whose share was removed while their OTHER secure parents
    /// still pass part of it on — what <see cref="SecureRootInheritance.GiveBackAsync"/> gives back. The <c>/unshare-user</c>
    /// fan-out gives it back in the same pass; an unsecure gives it back once its flag is cleared.
    /// </summary>
    public IReadOnlyList<(string Table, Guid Id, DataversePrincipalRef Principal)> GiveBack { get; init; } =
        Array.Empty<(string, Guid, DataversePrincipalRef)>();

    internal static InheritedUnsharePass NotApplicable { get; } =
        new(SecureFiledRootsStatus.NotApplicable, 0, 0, 0, 0, null);
}

/// <summary>
/// Task 158 r1 (owner round 31 item 2): how a BFF create of a work assignment or project is made — refused (nothing written),
/// an ordinary create (filed under no secure record), or created INTO isolation (owned by the named team, flagged, for its
/// creator) and completed by <see cref="SecureRootInheritance.CompleteIsolatedCreateAsync"/>.
/// </summary>
public sealed record SecureRootCreatePlan(
    RecordOwnerResolution? Refusal,
    bool Isolated,
    Guid? SecureOwnerTeamId,
    IReadOnlyList<SecureFilingParent> SecureParents)
{
    /// <summary>Filed under no secure record: the writer's ordinary create.</summary>
    public static SecureRootCreatePlan Ordinary { get; } = new(null, false, null, Array.Empty<SecureFilingParent>());

    internal static SecureRootCreatePlan Refused(RecordOwnerResolution refusal) =>
        new(refusal, false, null, Array.Empty<SecureFilingParent>());
}

/// <summary>A work assignment or project filed under a given record.</summary>
/// <param name="Confirmed"><c>true</c> when it is PROVABLY filed under one of the records asked about: a typed lookup, or a
/// pair whose type was read and names that record's table. <c>false</c>: a pair naming the id whose type could not be read —
/// a candidate the per-record decision reports (unverifiable), never a record shown as "related".</param>
public sealed record FiledRootRef(string Table, Guid Id, string? Name, bool FlaggedSecure, bool Confirmed = true);

/// <summary>
/// Round 61 item 1: the work assignments and projects filed below a set of records at any depth
/// (<see cref="SecureRootInheritance.ListFiledRootsBelowAsync"/>), and whether the chain went past the depth bound — in which
/// case the caller cannot say it reached everything and fails closed.
/// </summary>
public sealed record FiledRootsWalk(IReadOnlyList<FiledRootRef> Roots, bool DepthBoundReached);

/// <summary>
/// unified-access-control-r2 task 158 — a <c>sprk_workassignment</c> or <c>sprk_project</c> FILED UNDER a secure
/// <c>sprk_matter</c> or <c>sprk_project</c> is itself secure (owner round 6), secured through
/// <see cref="ProvisionProjectEndpoint.ProvisionInheritedAsync"/> — provisioning's own steps, for the person who created it —
/// and given its parents' sharees by <see cref="SecureChildShareSynchronizer.SyncInheritedRootAsync"/> (task 149's mechanism).
/// </summary>
/// <remarks>
/// <para><b>"Filed under"</b> (the POML's goal, task 155's sweep): a work assignment's typed <c>sprk_regardingmatter</c> /
/// <c>sprk_regardingproject</c>, or the polymorphic pair (<c>sprk_regardingrecordid</c> + <c>sprk_regardingrecordtype</c>)
/// naming a matter or project; a project's pair (a project has no typed regarding lookup). Secure-if-ANY: one secure parent is
/// enough. A pair naming any other type (an invoice, a contact, a work assignment) is not this rule.</para>
/// <para><b>Fail closed (ADR-003).</b> A pair that cannot be resolved (an id that is not a GUID, no type, an unreadable type)
/// or a parent whose flag cannot be read — or reads EMPTY (owner round 17 item 3) — is never "not secure". When no OTHER
/// parent is readably secure the record is <see cref="SecureRootInheritOutcome.Unverifiable"/>: nothing is written, a BFF
/// create or re-file is refused (<see cref="CheckRefileAsync"/>), and the job reports it. When another parent IS readably
/// secure, the record is secure (secure-if-any) — but the person it would be secured for cannot be checked against the
/// unreadable parent's No Access list (owner round 31 item 1), so provisioning, a create and a re-file all refuse
/// (<c>sdap.provision.creator_no_access_unverifiable</c>) until it can be read, and the job reports it. A parent that does
/// not exist is not a secure parent (it confers nothing).</para>
/// <para><b>Both ways — task 175, owner round 84</b> ("if parent changes, then child changes"; REPLACES round 6 item 4's "never
/// auto-unsecure" and task 158's constraint of that name): a record re-filed away from its secure parent, or whose parent is
/// unsecured, FOLLOWS it out of isolation when no other ancestor is secure — through the unsecure endpoint's own steps
/// (<see cref="FollowParentsAsync"/>, SecureRootInheritance.Cascade.cs), from the parent's unsecure, the re-file writers
/// and the job — and its Access Permission follows too. A record WITH a parent cannot be un-secured (or its Access
/// Permission set) on its own (<see cref="AccessFollowsParent"/>); F3 applies only to a parentless record. Unsecuring a
/// parent still ends the unmodified shares it passed on first (<see cref="EndWhatAParentPassedOnAsync"/>, main-session
/// round 39 item 1), before its filed records follow it.</para>
/// <para><b>Triggers.</b> (1) create: the BFF writers call <see cref="PlanCreateAsync"/> before the write and create the
/// row INTO isolation (owner round 31 item 2), then <see cref="CompleteIsolatedCreateAsync"/>; (2) re-file: they call
/// <see cref="CheckRefileAsync"/> before the write and <see cref="SecureAfterWriteAsync"/> after it; (3) a parent becoming
/// secure: provisioning Step 8 calls <see cref="SecureFiledRootsUnderAsync"/>; (4) writes outside the BFF:
/// <see cref="SecureRootInheritanceJob"/> (≤ 5 min). A parent's sharees reach its secure filed records through
/// <see cref="PassSharesOnAsync"/> — from <c>/share-user</c>, and from a project that was just given sharees itself — and
/// through the job; a secure parent's UNSHARE ends the inherited shares it passed on through
/// <see cref="PassUnshareOnAsync"/> (<c>/unshare-user</c>) and the job, by the provenance recorded on task 142's
/// <c>sprk_assignedaccess</c> ledger (owner round 30) — and a parent's UNSECURE ends them all
/// (<see cref="EndWhatAParentPassedOnAsync"/>, round 39 item 1).</para>
/// </remarks>
public sealed partial class SecureRootInheritance
{
    internal const string WorkAssignment = "sprk_workassignment";
    internal const string Project = "sprk_project";
    internal const string Matter = "sprk_matter";

    internal const string PairIdColumn = "sprk_regardingrecordid";
    internal const string PairTypeColumn = "sprk_regardingrecordtype";
    private const string RecordTypeRefEntity = "sprk_recordtype_ref";
    private const string RecordTypeLogicalNameColumn = "sprk_recordlogicalname";
    private const string IsSecureColumn = "sprk_issecure";
    private const string AccessPermissionColumn = "sprk_accesspermission"; // task 174: read with the flag (round 84)
    private const string OwningTeamColumn = "owningteam";
    private const string ContainerColumn = "sprk_containerid";

    /// <summary>A pair, a type or another filing value could not be resolved to a record: nothing was written.</summary>
    internal const string ReasonParentUndetermined = "sdap.inherit.parent_undetermined";

    /// <summary>Whether a matter or project the record is filed under is secure could not be read (or read empty).</summary>
    internal const string ReasonParentUnverifiable = "sdap.inherit.parent_unverifiable";

    /// <summary>The record itself could not be read.</summary>
    internal const string ReasonRecordUnreadable = "sdap.inherit.record_unreadable";

    /// <summary>Records filed under records filed under … deeper than <see cref="MaxNestedProvisioning"/>: left to the job.</summary>
    internal const string ReasonTooDeep = "sdap.inherit.too_deep";

    /// <summary>The record is secure but its parents' sharees could not all be given to it.</summary>
    internal const string ReasonSharesIncomplete = "sdap.inherit.shares_incomplete";

    /// <summary>Provisioning answered an unexpected result.</summary>
    internal const string ReasonUnexpectedResult = "sdap.inherit.unexpected_result";

    /// <summary>
    /// A sharee-only pass (a parent's share changed) found a filed record that is not secure yet: securing it is not that
    /// request's act — the job (or the next provisioning of the parent) does it.
    /// </summary>
    internal const string ReasonNotYetSecure = "sdap.inherit.not_yet_secure";

    /// <summary>
    /// How many inherited passes may run inside one another (a project secured inside a matter's provisioning, whose own
    /// Step 8 secures the records filed under IT …; or a project's new sharees passed on to the records filed under it).
    /// Past it, the record is left to the job, which reaches it on its next run as a record filed under a now-secure parent.
    /// </summary>
    internal const int MaxNestedProvisioning = 3;

    /// <summary>Rows per page of a filed-records read; past one page the read fails rather than decide on part of it.</summary>
    internal const int FiledPageSize = 5000;

    /// <summary>Parent ids per IN condition.</summary>
    internal const int IdsPerQuery = 200;

    /// <summary>Parent ids per pair query (one LIKE condition each, OR-ed — task 158 r1).</summary>
    internal const int PairIdsPerQuery = 50;

    /// <summary>The typed regarding lookups that file each inheriting table under a parent (live metadata, task 155 sweep).</summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Column, string ParentTable)>> TypedFilingColumns =
        new Dictionary<string, IReadOnlyList<(string, string)>>(StringComparer.OrdinalIgnoreCase)
        {
            [WorkAssignment] = new[] { ("sprk_regardingmatter", Matter), ("sprk_regardingproject", Project) },
            [Project] = Array.Empty<(string, string)>(),
        };

    private static readonly AsyncLocal<int> NestedProvisioning = new();

    private readonly IGenericEntityService _dataverse;
    private readonly DataverseWebApiClient _webApi;
    private readonly SpeFileStore _speFileStore;
    private readonly IDataverseRecordShareService _recordShare;
    private readonly SecureChildReconciler _secureChildren;
    private readonly SecureChildShareSynchronizer _synchronizer;
    private readonly SecureShareNoAccessGuard _noAccessGuard;
    private readonly AssignedAccessStore _ledger;
    private readonly AssignedAccessMaterializer _assignedAccess;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecureRootInheritance> _logger;

    /// <param name="ledger">Task 158 r1 (owner round 30): task 142's <c>sprk_assignedaccess</c> ledger, which records where
    /// each share passed on to a filed secure root came from (no new table — CLAUDE.md §11). Registered unconditionally in
    /// the same module (ExternalAccessModule), so this service gains no asymmetric dependency (§10 F.1).</param>
    /// <param name="assignedAccess">Task 158 r1c-v2 (main-session round 47 item 1 (2)): task 142's invariant owner, run at
    /// once for a filed record when this rule removes a share that 142's Assigned-To row had found covering its target — on a
    /// secure record the assignee is then SUGGESTED (owner A3), never silently dropped. Registered unconditionally in the same
    /// module; it does not depend on this class (its sharee-only pass reaches this class through a scope — round 47 item 1
    /// (3)), so there is no cycle.</param>
    private readonly Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator _accessCacheInvalidator;
    private readonly Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator? _fileRelocator;
    private readonly IRecordOwnershipResolver _ownership;

    public SecureRootInheritance(
        IGenericEntityService dataverse,
        DataverseWebApiClient webApi,
        SpeFileStore speFileStore,
        IDataverseRecordShareService recordShare,
        SecureChildReconciler secureChildren,
        SecureChildShareSynchronizer synchronizer,
        SecureShareNoAccessGuard noAccessGuard,
        AssignedAccessStore ledger,
        AssignedAccessMaterializer assignedAccess,
        IConfiguration configuration,
        ILogger<SecureRootInheritance> logger,
        // Batch-4 integration: what provisioning's own steps need beyond 158's set — the owner-change eviction (task 132)
        // and the Make Secure file relocation (round 26 item 3). Optional so this service's test compositions keep
        // compiling; the host registers both (IMembershipCacheInvalidator unconditionally, with its Null-Object).
        Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator? accessCacheInvalidator = null,
        Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator? fileRelocator = null,
        // Task 175: the ONE ownership rule, for the owner a cascaded un-secure gives a record (its parents' business unit's
        // team). Optional for the same reason; without it the rule is constructed over this class's own reader.
        IRecordOwnershipResolver? ownership = null)
    {
        _ownership = ownership ?? new RecordOwnershipResolver(dataverse, configuration,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RecordOwnershipResolver>.Instance);
        _accessCacheInvalidator = accessCacheInvalidator ?? new Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator>.Instance);
        _fileRelocator = fileRelocator;
        _dataverse = dataverse;
        _webApi = webApi;
        _speFileStore = speFileStore;
        _recordShare = recordShare;
        _secureChildren = secureChildren;
        _synchronizer = synchronizer;
        _noAccessGuard = noAccessGuard;
        _ledger = ledger;
        _assignedAccess = assignedAccess;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>True for a table whose rows inherit security from what they are filed under.</summary>
    public static bool Inherits(string? table) =>
        string.Equals(table?.Trim(), WorkAssignment, StringComparison.OrdinalIgnoreCase)
        || string.Equals(table?.Trim(), Project, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for a table whose rows pass security on to what is filed under them.</summary>
    public static bool IsParent(string? table) =>
        string.Equals(table?.Trim(), Matter, StringComparison.OrdinalIgnoreCase)
        || string.Equals(table?.Trim(), Project, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every column that decides what a row of <paramref name="table"/> is filed under.</summary>
    internal static IReadOnlySet<string> FilingColumnsOf(string table) =>
        new HashSet<string>(
            (TypedFilingColumns.TryGetValue(table, out var typed) ? typed.Select(t => t.Column) : Enumerable.Empty<string>())
                .Append(PairIdColumn).Append(PairTypeColumn),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The wire token for an inheriting table.</summary>
    internal static string WireTokenFor(string table) =>
        string.Equals(table, Project, StringComparison.OrdinalIgnoreCase) ? "project"
        : string.Equals(table, Matter, StringComparison.OrdinalIgnoreCase) ? "matter"
        : "workassignment";

    // ── (1) + (2): create and re-file ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// BEFORE a BFF create or re-file of a work assignment or project: when the write sets or clears what the row is filed
    /// under, the records it will be filed under AFTER the write are resolved and their flags read. If one cannot be read
    /// and none is readably secure, the write is refused — <see cref="RecordOwnerRefusal.ParentUndetermined"/>, the code
    /// every writer already refuses in its own contract — and nothing is written (owner: "if the parent's flag cannot be
    /// read, refuse"). A readably secure parent lets the write proceed: the record is secured after it.
    /// <c>null</c>: proceed (the write then calls <see cref="SecureAfterWriteAsync"/>).
    /// </summary>
    /// <param name="table">The row's table.</param>
    /// <param name="recordId">The row, or <c>null</c> for a create.</param>
    /// <param name="writes">The columns the write sets, by any spelling a BFF writer uses: the logical name, or the
    /// navigation property with <c>@odata.bind</c>; values as <see cref="EntityReference"/>, <see cref="Guid"/>, a bind path
    /// (<c>/sprk_matters(…)</c>), a GUID string, or <c>null</c> for a clear.</param>
    public async Task<RecordOwnerResolution?> CheckRefileAsync(
        string table, Guid? recordId, IEnumerable<KeyValuePair<string, object?>> writes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (!Inherits(table))
            return null;

        var materialized = writes as IReadOnlyCollection<KeyValuePair<string, object?>> ?? writes.ToList();
        List<FilingWrite> filingWrites;
        try
        {
            filingWrites = FilingWritesIn(table, materialized);
        }
        catch (FilingValueException ex)
        {
            // A value no writer spelling resolves to a record (neither a reference, an id nor a bind path) is never read as
            // "filed under nothing" (ADR-003): refused, nothing written.
            return Refused(table, ex.Message);
        }

        // Task 175 (owner round 84): an UPDATE that sets sprk_accesspermission or sprk_issecure on a record that will have a
        // parent after the write is refused — "if a child has a parent then the access cannot be changed manually". A create
        // is not refused: the cascade sets its values from its parents (the job, ≤ 5 minutes; enforcement already follows the
        // parent, task 174).
        var setsLocked = recordId is not null
                         && materialized.Any(w => AccessFollowsParent.LockedColumns.Contains(NormalizeColumn(w.Key)));
        if (filingWrites.Count == 0 && !setsLocked)
            return null;

        FilingFacts after;
        try
        {
            var before = recordId is { } id ? await ReadFactsAsync(table, id, ct).ConfigureAwait(false) : null;
            after = Overlay(table, before, filingWrites);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[SECURE-INHERIT] {Table} {RecordId} could not be read before a re-file; refusing.", table, recordId);
            return Refused(table, "the record could not be read, so what it would be filed under cannot be checked");
        }

        var (answer, filedUnder) = await DecideParentsCoreAsync(_dataverse, _logger, after, _recordTypes, ct).ConfigureAwait(false);
        if (setsLocked)
        {
            if (filedUnder.Count > 0)
            {
                var noun = table.Trim().Equals(Project, StringComparison.OrdinalIgnoreCase) ? "project" : "work assignment";
                return Refusal(AccessFollowsParent.ReasonCode,
                    $"this {noun} is filed under {AccessFollowsParent.Describe(filedUnder.Select(p => new SecureFilingParent(p.Table, p.Id, p.Name)).ToList())}, " +
                    $"and its Secure designation and Access Permission follow it (owner round 84); they cannot be set on the {noun}, " +
                    "so it was not written");
            }

            if (!answer.IsKnown)
                return Refused(table, answer.Unverifiable!); // whether it has a parent is unknown: never "parentless" on a guess

            if (filingWrites.Count == 0)
                return null; // a parentless record keeps and edits its own values
        }

        // Secure-if-any: a readable secure parent means the record is secured after the write whatever another parent says
        // (securing is the closed direction). Only when no parent is readably secure does an unreadable one refuse.
        if (!answer.HasSecureParent && !answer.IsKnown)
            return Refused(table, answer.Unverifiable!);

        if (answer.HasSecureParent)
        {
            // Owner round 31 item 1 (task 158 r1): the person the record will be secured for — its recorded creator — must
            // not be walled off it, nor off ANY secure record it would be filed under; checked HERE, before the caller's
            // write, never left to the provisioning after it (which would leave a re-filed, unsecured record behind).
            if (recordId is { } existing && await CreatorWallRefusalAsync(table, existing, answer, ct).ConfigureAwait(false) is { } walled)
                return walled;

            _logger.LogInformation(
                "[SECURE-INHERIT] {Table} {RecordId} is being filed under secure record(s) {Parents}; it is secured after the write.",
                table, recordId, string.Join(", ", answer.SecureParents.Select(p => $"{p.Table}:{p.Id:D}")));
        }

        return null;
    }

    /// <summary>
    /// Task 158 r1 (owner round 31 item 1): the re-file refusal when the record's recorded creator (the person an inherited
    /// provisioning secures it for) is walled off the record or any secure parent it would be filed under, or when that
    /// cannot be decided — a parent that cannot be read, a creator that cannot be named, a guard that cannot answer.
    /// <c>null</c>: not walled anywhere.
    /// </summary>
    private async Task<RecordOwnerResolution?> CreatorWallRefusalAsync(
        string table, Guid recordId, SecureParentsAnswer answer, CancellationToken ct)
    {
        var logical = table.Trim().ToLowerInvariant();
        var noun = logical == Project ? "project" : "work assignment";
        if (!answer.IsKnown)
        {
            return Refusal(ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable,
                $"whether the person who created this {noun} may access every secure record it would be filed under could " +
                $"not be checked ({answer.Unverifiable}), so it was not written");
        }

        var root = SecureRecordRoot.For(logical == Project ? ExternalGrantRootType.Project : ExternalGrantRootType.WorkAssignment);
        var creator = await ProvisionProjectEndpoint.ResolveRecordedCreatorAsync(
            _webApi, root, recordId, _logger, $"refile:{recordId:N}", ct).ConfigureAwait(false);
        if (creator.CreatorId is not { } creatorId)
        {
            return Refusal(creator.RefusalCode ?? ProvisionProjectEndpoint.ReasonResumeCreatorUnavailable,
                $"it would be filed under a secure record, which makes it secure for the person who created it, and that " +
                $"person cannot be determined ({creator.Detail}), so it was not written");
        }

        // The record's own list and every secure parent's (the filing as the write will leave it) — the guard's ONE entry
        // point (round 39 item 2), never a second copy of the per-parent loop.
        var refusing = await _noAccessGuard.CheckRecordAndSecureParentsAsync(
            logical, recordId, creatorId, SecureWallRecordScope.BeingSecured, answer, ct).ConfigureAwait(false);
        if (!refusing.RefusesShare)
            return null;

        var walled = refusing.Outcome == SecureShareWallOutcome.Walled;
        _logger.LogWarning(
            "[SECURE-INHERIT] Refusing the re-file of {Table} {RecordId}: its creator {CreatorId} is {State} the No Access list " +
            "of {Where} ({Detail}). Nothing was written.", logical, recordId, creatorId, walled ? "on" : "not provably off",
            refusing.ParentTable is { } p ? $"{p}:{refusing.ParentId:D}" : $"{logical}:{recordId:D}",
            walled ? string.Join(",", refusing.EntryIds) : refusing.Fault);
        var list = refusing.ParentTable is { } parentTable
            ? $"the No Access list of the secure {WireTokenFor(parentTable)} it would be filed under"
            : $"this {noun}'s No Access list";
        return walled
            ? Refusal(ProvisionProjectEndpoint.ReasonCreatorNoAccess,
                $"the person who created this {noun} is on {list}, so it cannot be made a secure record shared to them; " +
                "it was not written")
            : Refusal(ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable,
                $"whether the person who created this {noun} is on {list} could not be checked, so it was not written");
    }

    private RecordOwnerResolution Refusal(string code, string reason)
    {
        _logger.LogWarning("[SECURE-INHERIT] Refused ({Code}): {Reason}.", code, reason);
        return RecordOwnerResolution.Refused(code, reason);
    }

    /// <summary>
    /// AFTER a BFF create or re-file: when <paramref name="writtenColumns"/> (the create's or the update's own columns)
    /// touched what the row is filed under — or <paramref name="writtenColumns"/> is <c>null</c>, "unknown, check" — secures
    /// it if it is now filed under a secure record. A write that files nothing costs no read. Never
    /// throws: a failure is logged, returned, and left to <see cref="SecureRootInheritanceJob"/> (≤ 5 minutes).
    /// </summary>
    public async Task<SecureRootInheritResult?> SecureAfterWriteAsync(
        string table, Guid recordId, IEnumerable<string>? writtenColumns, string? traceId, CancellationToken ct)
    {
        if (!Inherits(table) || recordId == Guid.Empty)
            return null;

        if (writtenColumns is not null)
        {
            var filing = FilingColumnsOf(table);
            if (!writtenColumns.Select(NormalizeColumn).Any(filing.Contains))
                return null;
        }

        try
        {
            var trace = traceId ?? Guid.NewGuid().ToString("N");
            var result = await SecureIfFiledUnderSecureAsync(table, recordId, trace, ct).ConfigureAwait(false);
            if (!result.IsComplete)
            {
                _logger.LogError(
                    "[SECURE-INHERIT] {Table} {RecordId} was written but is not secured yet ({Outcome}, {Code}: {Detail}); the " +
                    "secure-root inheritance job retries it.", table, recordId, result.Outcome, result.ReasonCode, result.Detail);
            }

            // Task 175 (owner round 84, goal 2): a re-file recomputes its stored values both ways — re-filed away from its
            // secure parent it follows its new parents out of isolation (the unsecure endpoint's own steps), and its Access
            // Permission follows them. A record left with no parent keeps its values (and becomes editable). Never throws;
            // what does not complete is left at the more restrictive state for the job.
            if (result.Outcome is not (SecureRootInheritOutcome.NotFound or SecureRootInheritOutcome.Unverifiable))
            {
                var follow = await FollowParentsAsync(table, recordId, trace, ct).ConfigureAwait(false);
                if (!follow.IsComplete)
                {
                    _logger.LogWarning(
                        "[FOLLOW-PARENT] {Table} {RecordId} was re-filed but is not in step with its parents yet ({Outcome}, {Code}: " +
                        "{Detail}); the secure-root inheritance job completes it.", table, recordId, follow.Outcome, follow.ReasonCode,
                        follow.Detail);
                }

                // A project that changed carries the change down to what is filed under it (bounded; the job does the rest).
                if (IsParent(table) && follow.WroteAnything)
                    await CascadeBelowAsync(table, recordId, trace, ct).ConfigureAwait(false);
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[SECURE-INHERIT] Securing {Table} {RecordId} after its write failed; the secure-root inheritance job retries it.",
                table, recordId);
            return new SecureRootInheritResult(
                table, recordId, SecureRootInheritOutcome.Failed, ReasonUnexpectedResult, "the securing step threw",
                Array.Empty<SecureFilingParent>(), null);
        }
    }

    // ── (1') A create INTO isolation (owner round 31 item 2, task 158 r1) ───────────────────────────────────────────

    /// <summary>
    /// BEFORE a BFF create of a work assignment or project: decides, with nothing written, whether the row will be filed
    /// under a secure matter or project — and if so, that it is CREATED INTO ISOLATION (owner round 31 item 2: no
    /// business-unit-visible window): owned by the named Secure Record Owners team, flagged in the create, for
    /// <paramref name="creatorSystemUserId"/>. Refuses (nothing written) when a parent's flag cannot be read and none is
    /// readably secure (<see cref="RecordOwnerRefusal.ParentUndetermined"/>), when the creator is walled off — or cannot be
    /// checked against — the No Access list of any secure parent or of the record itself (round 31 item 1: the existing
    /// <c>sdap.provision.creator_no_access*</c> codes), or when the named team cannot be resolved. The writer then makes
    /// its own as-caller pre-check (G5), creates the row with <see cref="SecureRootCreatePlan.SecureOwnerTeamId"/> and the
    /// flag, and calls <see cref="CompleteIsolatedCreateAsync"/>.
    /// </summary>
    /// <param name="writes">The create's columns, as <see cref="CheckRefileAsync"/> reads them.</param>
    /// <param name="creatorSystemUserId">The person the create is made for — the caller (their <c>sprk_createdbyperson</c>).</param>
    /// <param name="callerMayFileUnder">The writer's own AS-THE-CALLER check (G5) on the secure parents the row would be filed
    /// under — asked once they are known and BEFORE the No Access checks, so a caller who may not file under a secure record
    /// learns nothing more about it (not even its No Access state). Its refusal is returned as the plan's. <c>null</c>: the
    /// writer made its own check already.</param>
    public async Task<SecureRootCreatePlan> PlanCreateAsync(
        string table, IEnumerable<KeyValuePair<string, object?>> writes, Guid creatorSystemUserId, CancellationToken ct,
        Func<IReadOnlyList<SecureFilingParent>, CancellationToken, Task<RecordOwnerResolution?>>? callerMayFileUnder = null)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (!Inherits(table))
            return SecureRootCreatePlan.Ordinary;

        var logical = table.Trim().ToLowerInvariant();
        var noun = logical == Project ? "project" : "work assignment";
        var materialized = writes.ToArray();
        List<FilingWrite> filingWrites;
        try
        {
            filingWrites = FilingWritesIn(logical, materialized);
        }
        catch (FilingValueException ex)
        {
            return SecureRootCreatePlan.Refused(Refused(logical, ex.Message));
        }

        if (filingWrites.Count == 0)
            return SecureRootCreatePlan.Ordinary;

        var answer = await DecideParentsAsync(Overlay(logical, null, filingWrites), ct).ConfigureAwait(false);
        if (!answer.HasSecureParent)
        {
            return answer.IsKnown
                ? SecureRootCreatePlan.Ordinary
                : SecureRootCreatePlan.Refused(Refused(logical, answer.Unverifiable!));
        }

        if (creatorSystemUserId == Guid.Empty)
        {
            return SecureRootCreatePlan.Refused(Refusal(ProvisionProjectEndpoint.ReasonCreatorUnresolved,
                $"a {noun} filed under a secure record is created secure for the person who creates it, and that person could " +
                "not be identified, so it was not created"));
        }

        // G5 on the secure parents (the writer's as-the-caller check) — before anything about them is said.
        if (callerMayFileUnder is not null
            && await callerMayFileUnder(answer.SecureParents, ct).ConfigureAwait(false) is { } callerRefusal)
        {
            return SecureRootCreatePlan.Refused(callerRefusal);
        }

        // Round 31 item 1: the creator honours every secure parent's No Access list AND the record's own — before the write.
        if (!answer.IsKnown)
        {
            return SecureRootCreatePlan.Refused(Refusal(ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable,
                $"whether you may access every secure record this {noun} would be filed under could not be checked " +
                $"({answer.Unverifiable}), so it was not created"));
        }

        IReadOnlyCollection<Guid> organizations;
        try
        {
            organizations = OrganizationsIn(logical, materialized);
        }
        catch (FilingValueException ex)
        {
            return SecureRootCreatePlan.Refused(Refusal(ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable,
                $"whether you are on this {noun}'s No Access list could not be checked ({ex.Message}), so it was not created"));
        }

        // The record's own (prospective) list and every secure parent's — the guard's ONE entry point (round 39 item 2).
        var wall = await _noAccessGuard.CheckRecordAndSecureParentsAsync(
                logical, Guid.NewGuid(), creatorSystemUserId, SecureWallRecordScope.Prospective, answer, ct, organizations)
            .ConfigureAwait(false);
        var wallParent = wall.ParentTable is { } wallTable && wall.ParentId is { } wallId
            ? answer.SecureParents.FirstOrDefault(p => p.Table == wallTable && p.Id == wallId)
            : null;
        if (CreateWallRefusal(wall,
                wallParent is null
                    ? $"this {noun}'s No Access list"
                    : $"the No Access list of the secure {WireTokenFor(wallParent.Table)} it would be filed under",
                noun, logical, wallParent) is { } refusal)
            return SecureRootCreatePlan.Refused(refusal);

        var topology = await Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.ResolveAsync(_webApi, _configuration, ct).ConfigureAwait(false);
        if (!topology.IsResolved || topology.OwnerTeamId is not { } team)
        {
            return SecureRootCreatePlan.Refused(Refusal(RecordOwnerRefusal.SecureOwnerTeamUnresolved,
                $"a {noun} filed under a secure record is created owned by the Secure Record Owners team, which could not be " +
                "resolved in this environment, so it was not created"));
        }

        _logger.LogInformation(
            "[SECURE-INHERIT] A new {Table} is filed under secure record(s) {Parents}: it is created INTO isolation (owner team " +
            "{Team}, flagged) for {Creator}.", logical, string.Join(", ", answer.SecureParents.Select(p => $"{p.Table}:{p.Id:D}")),
            team, creatorSystemUserId);
        return new SecureRootCreatePlan(null, Isolated: true, team, answer.SecureParents);
    }

    private RecordOwnerResolution? CreateWallRefusal(
        SecureShareWallDecision decision, string list, string noun, string logical, SecureFilingParent? parent)
    {
        if (!decision.RefusesShare)
            return null;

        var walled = decision.Outcome == SecureShareWallOutcome.Walled;
        _logger.LogWarning(
            "[SECURE-INHERIT] Refusing the create of a {Table}: its creator is {State} {List} ({Where}; {Detail}). Nothing was " +
            "written.", logical, walled ? "on" : "not provably off", list, parent is null ? "the record" : $"{parent.Table}:{parent.Id:D}",
            walled ? string.Join(",", decision.EntryIds) : decision.Fault);
        return walled
            ? RecordOwnerResolution.Refused(ProvisionProjectEndpoint.ReasonCreatorNoAccess,
                $"you are on {list}, so it cannot be made a secure record shared to you; nothing was created")
            : RecordOwnerResolution.Refused(ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable,
                $"whether you are on {list} could not be checked, so nothing was created");
    }

    /// <summary>
    /// The organizations a create payload references through the table's org-typed lookups — the No Access list of a record
    /// that has no id yet. A value no writer spelling resolves to an id throws (never read as "no organization").
    /// </summary>
    private static IReadOnlyCollection<Guid> OrganizationsIn(string table, IEnumerable<KeyValuePair<string, object?>> writes)
    {
        var columns = new HashSet<string>(ExternalParticipationService.OrganizationLookupAttributesOf(table), StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<Guid>();
        foreach (var (key, written) in writes)
        {
            if (!columns.Contains(NormalizeColumn(key)))
                continue;

            var value = written is System.Text.Json.JsonElement json
                ? json.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined ? null
                    : json.ValueKind == System.Text.Json.JsonValueKind.String ? json.GetString() : (object)json
                : written;
            if (value is null or DBNull)
                continue;

            var id = value switch
            {
                EntityReference reference => reference.Id,
                Guid g => g,
                string s when TryParseBindOrGuid(s, out var parsed) => parsed,
                _ => throw new FilingValueException($"the value written to {NormalizeColumn(key)} could not be interpreted"),
            };
            if (id != Guid.Empty)
                found.Add(id);
        }

        return found;
    }

    /// <summary>
    /// AFTER a create made INTO isolation (<see cref="PlanCreateAsync"/>): completes it through provisioning's own steps —
    /// the existing re-entry branch of a record owned by the named team with no container: its creator (the stamped
    /// <c>sprk_createdbyperson</c>) checked against every No Access list again and shared, READ BACK (133's share-first
    /// rule); then its own container; then its secure parents' sharees. When the creator's share is NOT in place at the
    /// end — a refusal or a failure before it — the just-created row is DELETED and read back gone (owner round 31 item 2:
    /// never a row nobody can open), and the result says so (<see cref="SecureRootInheritResult.RowRemoved"/>). A later
    /// step that does not complete (the container, the sharees) leaves a PROVISIONED-but-incomplete record, which the job
    /// (≤ 5 minutes) completes through the same re-entry branch. Never throws.
    /// </summary>
    public async Task<SecureRootInheritResult> CompleteIsolatedCreateAsync(
        string table, Guid recordId, Guid creatorSystemUserId, string traceId, CancellationToken ct)
    {
        if (!Inherits(table))
            throw new ArgumentOutOfRangeException(nameof(table), table, "Only work assignments and projects inherit security.");

        var logical = table.Trim().ToLowerInvariant();
        SecureRootInheritResult provisioned;
        try
        {
            provisioned = await ProvisionAsync(logical, recordId, traceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-INHERIT] Completing the isolated create of {Table} {RecordId} threw.", logical, recordId);
            provisioned = Result(logical, recordId, SecureRootInheritOutcome.Failed, ReasonUnexpectedResult, "the securing step threw");
        }

        if (provisioned.Outcome is SecureRootInheritOutcome.Secured or SecureRootInheritOutcome.AlreadySecure)
        {
            var answer = await FindSecureParentsAsync(logical, recordId, ct).ConfigureAwait(false);
            var shares = answer.HasSecureParent
                ? await GiveParentShareesAsync(logical, recordId, answer, ct).ConfigureAwait(false)
                : null;
            return provisioned with
            {
                SecureParents = answer.SecureParents,
                Shares = shares,
                ReasonCode = shares is null || shares.IsComplete ? null : answer.IsKnown ? ReasonSharesIncomplete : ReasonParentUnverifiable,
                Detail = shares is null || shares.IsComplete ? null : shares.Detail,
            };
        }

        // Provisioning did not finish. A record its creator can open is a provisioned-but-incomplete record (the re-entry
        // branch completes it); one nobody can open is removed, read back — never left behind.
        if (await CreatorHoldsShareAsync(logical, recordId, creatorSystemUserId, ct).ConfigureAwait(false))
        {
            _logger.LogWarning(
                "[SECURE-INHERIT] {Table} {RecordId} was created into isolation and shared to its creator, but its provisioning did " +
                "not complete ({Code}: {Detail}); the secure-root inheritance job completes it.", logical, recordId,
                provisioned.ReasonCode, provisioned.Detail);
            return provisioned;
        }

        var removed = false;
        try
        {
            await _dataverse.DeleteAsync(logical, recordId, ct).ConfigureAwait(false);
            removed = await ReadFactsAsync(logical, recordId, ct).ConfigureAwait(false) is null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-INHERIT] Removing {Table} {RecordId} after its creator could not be shared failed.", logical, recordId);
        }

        if (removed)
        {
            _logger.LogWarning(
                "[SECURE-INHERIT] {Table} {RecordId} was created into isolation, but its creator could not be shared ({Code}: " +
                "{Detail}); the row was deleted (read back gone) and the create is refused.", logical, recordId,
                provisioned.ReasonCode, provisioned.Detail);
        }
        else if (provisioned.CompletesAutomatically)
        {
            _logger.LogCritical(
                "[SECURE-INHERIT] {Table} {RecordId} was created into isolation, its creator could not be shared ({Code}), AND the " +
                "row could not be removed: it is owned by the memberless Secure Record Owners team and only an administrator can " +
                "open it until the secure-root inheritance job shares it to its creator (it retries every 5 minutes) or an " +
                "administrator deletes it.", logical, recordId, provisioned.ReasonCode);
        }
        else
        {
            // Provisioning REFUSED its creator (e.g. walled off after the create's plan): every job run refuses again, so
            // nothing shares it on its own — an administrator must review it (verifier item 7: no self-heal is promised).
            _logger.LogCritical(
                "[SECURE-INHERIT] {Table} {RecordId} was created into isolation, its creator was REFUSED ({Code}: {Detail}), AND the " +
                "row could not be removed: it is owned by the memberless Secure Record Owners team, the secure-root inheritance " +
                "job refuses it on every run, and only an administrator can open it — an administrator must review and remove " +
                "it.", logical, recordId, provisioned.ReasonCode, provisioned.Detail);
        }

        return provisioned with { RowRemoved = removed, RowStranded = !removed };
    }

    /// <summary>Whether <paramref name="systemUserId"/> holds the creator's share on the record (strict read; a fault is "no").</summary>
    private async Task<bool> CreatorHoldsShareAsync(string table, Guid recordId, Guid systemUserId, CancellationToken ct)
    {
        try
        {
            var shares = await _recordShare.GetPrincipalAccessOrThrowAsync(table, recordId, ct).ConfigureAwait(false);
            var mask = shares.Where(s => s.Principal == DataversePrincipalRef.User(systemUserId))
                .Aggregate(0, (m, s) => m | s.AccessRightsMask);
            return mask == ProvisionProjectEndpoint.CreatorAccessMask;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[SECURE-INHERIT] The shares on {Table} {RecordId} could not be read.", table, recordId);
            return false;
        }
    }

    // ── The one decision ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Secures ONE work assignment or project when it is filed under a secure matter or project, and gives it its secure
    /// parents' sharees. Idempotent: an isolated record is only given any sharee it is missing.
    /// </summary>
    public Task<SecureRootInheritResult> SecureIfFiledUnderSecureAsync(
        string table, Guid recordId, string traceId, CancellationToken ct) =>
        SecureIfFiledUnderSecureCoreAsync(table, recordId, traceId, allowProvisioning: true, ct);

    /// <summary>
    /// Task 158 r1c-v2 (main-session round 47 item 1 (3)): the SHAREE-ONLY pass for one work assignment or project — its
    /// secure parents' sharees given (with their provenance), never a provisioning (<see cref="ReasonNotYetSecure"/> for a
    /// record not secure yet). Called by task 142's materializer after an assignment ended on the record, so access the
    /// assignment had covered is passed on again at once. A record filed under no secure parent: nothing.
    /// </summary>
    public Task<SecureRootInheritResult> PassShareesToFiledRecordAsync(
        string table, Guid recordId, string traceId, CancellationToken ct) =>
        SecureIfFiledUnderSecureCoreAsync(table, recordId, traceId, allowProvisioning: false, ct);

    private async Task<SecureRootInheritResult> SecureIfFiledUnderSecureCoreAsync(
        string table, Guid recordId, string traceId, bool allowProvisioning, CancellationToken ct)
    {
        if (!Inherits(table))
            throw new ArgumentOutOfRangeException(nameof(table), table, "Only work assignments and projects inherit security.");

        var logical = table.Trim().ToLowerInvariant();
        FilingFacts? facts;
        try
        {
            facts = await ReadFactsAsync(logical, recordId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[SECURE-INHERIT] {Table} {RecordId} could not be read.", logical, recordId);
            return Result(logical, recordId, SecureRootInheritOutcome.Unverifiable, ReasonRecordUnreadable, "the record could not be read");
        }

        if (facts is null)
            return Result(logical, recordId, SecureRootInheritOutcome.NotFound, null, null);

        var answer = await DecideParentsAsync(facts, ct).ConfigureAwait(false);
        if (!answer.HasSecureParent)
        {
            if (answer.IsKnown)
                return Result(logical, recordId, SecureRootInheritOutcome.NotFiledUnderSecure, null, null);

            _logger.LogWarning(
                "[SECURE-INHERIT] {Table} {RecordId}: whether a record it is filed under is secure could not be determined ({Why}); " +
                "nothing was written.", logical, recordId, answer.Unverifiable);
            return Result(logical, recordId, SecureRootInheritOutcome.Unverifiable, ReasonParentUnverifiable, answer.Unverifiable);
        }

        var isolated = await IsIsolatedAsync(facts, ct).ConfigureAwait(false);
        SecureRootInheritOutcome outcome;
        if (isolated)
        {
            outcome = SecureRootInheritOutcome.AlreadySecure;
        }
        else if (!allowProvisioning)
        {
            return Result(logical, recordId, SecureRootInheritOutcome.Failed, ReasonNotYetSecure,
                "it is not secure yet; the secure-root inheritance job secures it", answer.SecureParents);
        }
        else if (NestedProvisioning.Value >= MaxNestedProvisioning)
        {
            _logger.LogWarning(
                "[SECURE-INHERIT] {Table} {RecordId} is filed under a secure record, {Depth} inherited provisionings deep; left to " +
                "the secure-root inheritance job.", logical, recordId, NestedProvisioning.Value);
            return Result(logical, recordId, SecureRootInheritOutcome.Failed, ReasonTooDeep,
                "it is filed under records that were themselves being secured, more levels deep than one call secures",
                answer.SecureParents);
        }
        else
        {
            var provisioned = await ProvisionAsync(logical, recordId, traceId, ct).ConfigureAwait(false);
            if (provisioned.Outcome is not (SecureRootInheritOutcome.Secured or SecureRootInheritOutcome.AlreadySecure))
                return provisioned with { SecureParents = answer.SecureParents };
            outcome = provisioned.Outcome;
        }

        // The parents' sharees (task 149's mechanism) — every isolated secure parent; a flagged-but-not-isolated one holds.
        // When another record it is filed under could not be read, NONE is given: a record under two parents gets only
        // the principals shared on both (the intersection rule), and the unread one may be secure (fail closed — held, the
        // job retries; the record itself is secure either way).
        var shares = await GiveParentShareesAsync(logical, recordId, answer, ct).ConfigureAwait(false);

        // A project that was just GIVEN sharees passes them on to the secure records filed under IT (which may have been
        // secured, inside its own provisioning, before it had them): a sharee-only pass, never a provisioning. Bounded like
        // the provisionings; an incomplete pass is logged and completed by the job.
        if (IsParent(logical) && shares.SharesGranted + shares.SharesChanged > 0)
            await PassSharesOnAsync(logical, recordId, traceId, ct).ConfigureAwait(false);

        return new SecureRootInheritResult(
            logical, recordId, outcome,
            shares.IsComplete ? null : answer.IsKnown ? ReasonSharesIncomplete : ReasonParentUnverifiable,
            shares.IsComplete ? null : shares.Detail ?? "its parents' sharees could not all be given to it",
            answer.SecureParents, shares);
    }

    /// <summary>
    /// A matter's or project's sharees changed (a <c>/share-user</c> on it, or it was just given its own parents'):
    /// every SECURE work assignment / project filed under it is given its secure parents' sharees NOW (add-only), rather
    /// than at the job's next run (owner R3/R4: immediate on save, the job only a safety net). A filed record that is not
    /// secure yet is left to the job (<see cref="ReasonNotYetSecure"/>): a share is not the act that secures it. Never
    /// throws.
    /// </summary>
    public async Task<SecureFiledRootsPass> PassSharesOnAsync(string parentTable, Guid parentId, string traceId, CancellationToken ct)
    {
        if (!IsParent(parentTable))
            return SecureFiledRootsPass.NotApplicable($"nothing is filed under a {parentTable} for this rule");
        if (NestedProvisioning.Value >= MaxNestedProvisioning)
        {
            _logger.LogWarning(
                "[SECURE-INHERIT] Passing {Parent} {ParentId}'s sharees on is {Depth} passes deep; left to the secure-root " +
                "inheritance job.", parentTable, parentId, NestedProvisioning.Value);
            return new SecureFiledRootsPass(SecureFiledRootsStatus.Incomplete, Array.Empty<SecureRootInheritResult>(),
                "more levels deep than one call passes sharees on");
        }

        NestedProvisioning.Value++;
        try
        {
            var pass = await FiledRootsPassAsync(parentTable.Trim().ToLowerInvariant(), parentId, traceId, allowProvisioning: false, ct)
                .ConfigureAwait(false);
            if (!pass.IsComplete)
            {
                _logger.LogWarning(
                    "[SECURE-INHERIT] {Parent} {ParentId}'s sharees were not passed on to every secure record filed under it " +
                    "({Detail}); the secure-root inheritance job completes it.", parentTable, parentId, pass.Detail);
            }

            return pass;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-INHERIT] Passing {Parent} {ParentId}'s sharees on failed; the job completes it.",
                parentTable, parentId);
            return new SecureFiledRootsPass(SecureFiledRootsStatus.Failed, Array.Empty<SecureRootInheritResult>(),
                "passing the sharees on failed");
        }
        finally
        {
            NestedProvisioning.Value--;
        }
    }

    // ── Inherited-share provenance (owner round 30, task 158 r1; write-ahead since r1c-v1) ─────────────────────────────

    /// <summary>
    /// Gives a secure filed record its secure parents' sharees (task 149's mirror, add-only) and records WHERE each came
    /// from on task 142's <c>sprk_assignedaccess</c> ledger (owner round 30, option (c)): one row per (record, parent,
    /// principal), source <c>inherited:{parentTable}:{parentId}</c>, carrying the mask written.
    /// <list type="number">
    /// <item>(a) The record's shares against its rows. A row recorded ahead of its share (<see cref="AssignedAccessReason.SharePending"/>)
    /// whose share is in place is the rule's share like any other (confirmed in (d)); one whose share never landed is left
    /// for the mirror to write again (never read as a removal). A share the rule passed on that was removed or narrowed since is <see cref="AssignedAccessState.Declined"/>
    /// — an operator's removal (<c>/unshare-user</c> on this record marks it so too) — unless the person is on the record's
    /// No Access list: then task 143's enforcer removed it, a known cause, and the row waits
    /// (<see cref="AssignedAccessState.Skipped"/>, <c>removed-by-no-access</c>) until the wall is lifted and the share is
    /// passed on again (task 142's criterion 9). A list that cannot be checked decides nothing this pass.</item>
    /// <item>(b) Sources that ended: an inherited share whose (isolated) parent no longer shares the principal — an unshare
    /// the <c>/unshare-user</c> fan-out did not see — is ended by the reverse rule (<see cref="EndInheritedSourceAsync"/>).</item>
    /// <item>(c) The mirror, never re-adding a declined principal, with WRITE-AHEAD provenance (task 158 r1c-v1, verifier
    /// item 1): each share's row is written BEFORE the share, so no fault and no concurrent pass can leave a share this rule
    /// wrote without the record that says so. A share whose row cannot be written is not written.</item>
    /// <item>(d) Each share written (and read back) has its row confirmed; a principal that already held the mirror is
    /// recorded — inherited when a live row of another parent says that share was passed on, otherwise
    /// <see cref="AssignedAccessState.CoveredByExisting"/> (direct access). The rows are re-read AFTER the record's shares
    /// were read, so the row of any share this pass saw that a concurrent pass wrote is in that read.</item>
    /// </list>
    /// Fail closed: a provenance that cannot be read gives nobody anything; one that cannot be written writes no share and
    /// makes the result incomplete (retried).
    /// </summary>
    private async Task<SecureChildShareSyncResult> GiveParentShareesAsync(
        string logical, Guid recordId, SecureParentsAnswer answer, CancellationToken ct)
    {
        if (!answer.IsKnown)
        {
            return new SecureChildShareSyncResult(
                SecureChildShareSyncStatus.Incomplete, 1, 0, 0, 0, 1, 0, 0, 0, 0,
                $"its parents' sharees are held: {answer.Unverifiable}");
        }

        var rootType = RootTypeOf(logical);
        List<AssignedAccessLedgerRow> ledger;
        Dictionary<DataversePrincipalRef, int> current;
        try
        {
            ledger = (await _ledger.ReadInheritedLedgerAsync(rootType, recordId, ct).ConfigureAwait(false)).ToList();
            current = MasksOf(await _recordShare.GetPrincipalAccessOrThrowAsync(logical, recordId, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[SECURE-INHERIT] Where the shares on {Table} {RecordId} came from could not be read; none of its parents' " +
                "sharees is given until it can be.", logical, recordId);
            return SecureChildShareSyncResult.Failed(
                "where its shares came from (the inherited-share provenance) could not be read, so none was given");
        }

        var notDone = new List<string>();

        // The rows that are declined (an operator's removal on THIS record), by id — apart from the row objects, whose
        // in-memory state a failed ledger write would leave stale.
        var declinedRows = new HashSet<Guid>();

        // Task 114 (owner round 67): on a RESTRICTED record a user flagged external is never passed on, and the Restricted
        // remover takes away a share such a user already held — a KNOWN cause. Asked once, for the users the ledger names.
        // A read that fails leaves their removals undecided (below) — never an operator's.
        RestrictedPrincipalsAnswer? restricted;
        try
        {
            restricted = await _synchronizer.RestrictedExternalPrincipalsOrThrowAsync(
                logical, recordId,
                ledger.Where(r => r.State != AssignedAccessState.Revoked)
                    .Select(AssignedAccessStore.InheritedPrincipalOf).OfType<DataversePrincipalRef>(),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[SECURE-INHERIT] Whether {Table} {RecordId} is Restricted, or who its sharees flagged external are, could not " +
                "be read; a removed inherited share of a user is decided on the next pass.", logical, recordId);
            restricted = null;
        }

        // (a) The record's own shares against what the ledger says was passed on.
        foreach (var row in ledger.Where(r => r.State != AssignedAccessState.Revoked))
        {
            if (AssignedAccessStore.InheritedPrincipalOf(row) is not { } principal)
                continue;

            if (row.State == AssignedAccessState.Declined)
            {
                declinedRows.Add(row.Id);
                continue;
            }

            if (row.State != AssignedAccessState.Shared || row.GrantedLevel is not { } written || written == 0)
                continue;

            var held = current.TryGetValue(principal, out var h) ? h : 0;
            if ((held & written) == written)
                continue; // still the share the rule passed on (one recorded ahead of its write is confirmed in (d))

            if (IsPending(row) && held == PriorMaskOf(row.Reason))
                continue; // recorded, but the share never landed: the mirror below writes it again — never a removal

            if (principal.Kind == DataversePrincipalKind.SystemUser && restricted is null)
            {
                // Task 114: the Restricted remover, or an operator? Undecided: nothing re-added this pass, nothing recorded.
                declinedRows.Add(row.Id);
                notDone.Add($"why {principal}'s inherited share was removed could not be decided (whether the record is " +
                            "Restricted could not be read)");
                continue;
            }

            if (restricted?.Barred.Contains(principal) == true)
            {
                // Task 114: the record is Restricted and the user is flagged external — the Restricted remover's removal, a
                // known cause. Skipped(restricted), never Declined, so the share is passed on again once the record is not.
                try
                {
                    await UpdateDecidedRowAsync(row,
                        new AssignedAccessLedgerWrite(AssignedAccessState.Skipped, AssignedAccessReason.Restricted), ct).ConfigureAwait(false);
                    _logger.LogInformation(
                        "[SECURE-INHERIT] {Principal}'s inherited share on Restricted {Table} {RecordId} was removed (the user is " +
                        "flagged external): it is passed on again once the record is not Restricted.", principal, logical, recordId);
                }
                catch (LedgerRowChangedException)
                {
                    notDone.Add($"{principal}'s inherited share record changed while it was being decided; it is decided on the next pass");
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "[SECURE-INHERIT] Recording why {Principal}'s inherited share on {Table} {RecordId} went failed.",
                        principal, logical, recordId);
                    notDone.Add($"{principal} could not be recorded as removed because the record is Restricted");
                }

                continue;
            }

            // Removed or narrowed since it was passed on (or, unconfirmed, changed by someone else). By whom? Task 158 final
            // round (main-session round 58 item 1): task 143's enforcer also removes a share here when the person is on a
            // secure PARENT's No Access list, so the record's own list AND its parents' are asked — through the guard's one
            // entry point, over the filing this pass already read — or that removal would read as an operator's (Declined)
            // and never be passed on again once the wall is lifted.
            var wall = principal.Kind == DataversePrincipalKind.SystemUser
                ? await _noAccessGuard.CheckRecordAndSecureParentsAsync(
                    logical, recordId, principal.Id, SecureWallRecordScope.AsFlagged, answer, ct).ConfigureAwait(false)
                : null; // a team is never on a No Access list
            if (wall?.Outcome == SecureShareWallOutcome.Unverifiable)
            {
                // Task 143's enforcer, or an operator? Undecided: nothing re-added this pass, nothing recorded (ADR-003).
                declinedRows.Add(row.Id);
                notDone.Add($"why {principal}'s inherited share was removed could not be decided (the No Access list could not be checked)");
                continue;
            }

            var walled = wall?.Outcome == SecureShareWallOutcome.Walled;
            var marker = walled
                ? new AssignedAccessLedgerWrite(AssignedAccessState.Skipped, AssignedAccessReason.RemovedByNoAccess)
                : new AssignedAccessLedgerWrite(AssignedAccessState.Declined, AssignedAccessReason.RemovedOutOfBand);
            if (!walled)
                declinedRows.Add(row.Id); // Declined whether or not the ledger write lands: never re-added by this pass (fail closed)
            try
            {
                await UpdateDecidedRowAsync(row, marker, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    walled
                        ? "[SECURE-INHERIT] {Principal}'s inherited share on {Table} {RecordId} was removed while they are on its No " +
                          "Access list (task 143's enforcer): it is passed on again once the wall is lifted."
                        : "[SECURE-INHERIT] {Principal}'s inherited share on {Table} {RecordId} was removed or narrowed outside the " +
                          "BFF: recorded Declined — it is not given again while the parent share persists.", principal, logical, recordId);
            }
            catch (LedgerRowChangedException)
            {
                // Round 47 item 2: the row changed after this pass read it (an operator's marker, another pass). Nothing is
                // recorded over it. The next pass decides on the row as it is now.
                notDone.Add($"{principal}'s inherited share record changed while it was being decided; it is decided on the next pass");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[SECURE-INHERIT] Recording why {Principal}'s inherited share on {Table} {RecordId} went failed.",
                    principal, logical, recordId);
                notDone.Add($"{principal} could not be recorded as {(walled ? "removed by the No Access list" : "declined")}");
            }
        }

        // (b) Sources that ended: an isolated parent that no longer shares the principal (owner round 30: "on the parent's
        // unshare, remove only the inherited share that is still UNMODIFIED"), reached here when the unshare was made
        // outside the BFF. A Declined row ends too — the operator's removal stands only "while the parent share persists",
        // so once it does not, a later share on the parent is passed on again. Task 158 r1c-v2 (round 39 item 1 —
        // interpretation xiii reversed): a parent that reads NOT secure (unsecured) or no longer exists passes nothing on, so
        // every row it passed on ends here too (the unsecure ends them itself; this is the net for anything it left). A parent
        // flagged secure but not isolated (mid-provisioning, mid-unsecure) ends nothing — held; one whose state cannot be
        // read is reported, never read as "not isolated".
        var mirrors = new Dictionary<(string, Guid), ParentMirrorAnswer>();
        foreach (var row in ledger.Where(r => r.State != AssignedAccessState.Revoked).ToList())
        {
            if (AssignedAccessStore.InheritedPrincipalOf(row) is not { } principal
                || AssignedAccessStore.InheritedSourceOf(row.SourceField) is not { } parent)
                continue;

            if (!mirrors.TryGetValue(parent, out var mirror))
            {
                mirror = await _synchronizer.IsolatedParentMirrorAsync(parent.Table, parent.Id, ct).ConfigureAwait(false);
                mirrors[parent] = mirror;
            }

            if (mirror.Unreadable)
            {
                notDone.Add($"whether {parent.Table} {parent.Id:D} still shares {principal} could not be read");
                continue;
            }

            if (!mirror.PassesNothingOn && (mirror.Mirror is null || mirror.Mirror.ContainsKey(principal)))
                continue;

            var ended = await EndInheritedSourceAsync(logical, recordId, row, ct).ConfigureAwait(false);
            if (!ended.Done)
            {
                notDone.Add($"{principal}'s share from {parent.Table} {parent.Id:D}: {ended.Detail}");
                continue;
            }

            if (!ended.RowEnded)
                continue; // kept as the record's last reader (S5): still the share the rule passed on, tried again next pass

            // Ended: a decline from this parent no longer holds. Taken out of the declined set NOW, so a principal that
            // another secure record the row is filed under passes on (it was re-filed since) is given in this same pass.
            row.StateValue = (int)AssignedAccessState.Revoked;
            declinedRows.Remove(row.Id);
        }

        // The principals still declined: a Declined row whose parent's share persists.
        var declined = ledger
            .Where(r => declinedRows.Contains(r.Id))
            .Select(AssignedAccessStore.InheritedPrincipalOf)
            .OfType<DataversePrincipalRef>()
            .ToHashSet();

        // (c) The add-only mirror — never re-adding a declined principal, each share's row written before the share.
        var parents = answer.SecureParents.Select(p => (p.Table, p.Id)).ToArray();
        var sync = await _synchronizer.SyncInheritedRootAsync(
            logical, recordId, parents, ct, declined,
            (intent, from, token) => RecordIntentAsync(rootType, recordId, intent, from, ledger, notDone, token)).ConfigureAwait(false);

        // (d) Each share written is confirmed; a principal that already held the mirror is recorded.
        if (sync.Inherited is { Count: > 0 } outcomes && sync.InheritedFrom is { Count: > 0 } from
            && outcomes.Any(o => o.Action is InheritedShareAction.Granted or InheritedShareAction.Raised or InheritedShareAction.AlreadyCovered))
        {
            try
            {
                var rows = (await _ledger.ReadInheritedLedgerAsync(rootType, recordId, ct).ConfigureAwait(false)).ToList();
                foreach (var outcome in outcomes.Where(o =>
                             o.Action is InheritedShareAction.Granted or InheritedShareAction.Raised or InheritedShareAction.AlreadyCovered))
                {
                    foreach (var parent in from)
                        await RecordProvenanceAsync(rootType, recordId, parent, outcome, rows, notDone, ct).ConfigureAwait(false);
                }

                // (e) Task 158 r1c-v2: each share this pass WROTE is decided again once it has landed.
                var wrote = outcomes.Where(o => o.Action is InheritedShareAction.Granted or InheritedShareAction.Raised).ToList();
                if (wrote.Count > 0)
                    await RecheckWrittenSharesAsync(logical, recordId, rootType, wrote, from, notDone, ct).ConfigureAwait(false);
            }
            catch (LedgerRowChangedException changed)
            {
                // Round 47 item 2: a row changed between this pass's read and its write. It is not written over.
                _logger.LogInformation(
                    "[SECURE-INHERIT] {Table} {RecordId}: inherited share record {RowId} changed while it was being recorded; " +
                    "the next pass decides on it.", logical, recordId, changed.RowId);
                notDone.Add("an inherited share record changed while it was being recorded; it is decided on the next pass");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex,
                    "[SECURE-INHERIT] Where {Table} {RecordId}'s inherited shares came from could not all be recorded; each share " +
                    "written this pass is already on record (written ahead of it), and the next pass confirms it.", logical, recordId);
                notDone.Add("where its inherited shares came from could not all be recorded");
            }
        }

        if (notDone.Count == 0)
            return sync;

        return sync with
        {
            Status = SecureChildShareSyncStatus.Incomplete,
            ChildrenNotUpdated = Math.Max(sync.ChildrenNotUpdated, 1),
            Detail = string.Join("; ", new[] { sync.Detail }.Concat(notDone).Where(d => !string.IsNullOrWhiteSpace(d))),
        };
    }

    /// <summary>
    /// (c) Write-ahead (task 158 r1c-v1, verifier item 1): records — BEFORE the mirror grants or raises
    /// <paramref name="intent"/>'s share — one unconfirmed <see cref="AssignedAccessState.Shared"/> row per isolated parent
    /// it is passed on from (<see cref="AssignedAccessReason.SharePending"/>; <c>sprk_grantedlevel</c> = the mask about to be
    /// written; the level it raises from, which the parent's unshare puts back). An existing row is updated in place; an
    /// <see cref="AssignedAccessState.Adopted"/> row (an operator shared the person on this record) is recorded too, the
    /// operator's level as the level raised from, so the parent's unshare takes back only what the rule added. <c>false</c>
    /// — the share is NOT written — when a row cannot be written, or a concurrent pass created it first (its decision was
    /// made on a read that did not see it; the next pass decides on the row as it is).
    /// </summary>
    private async Task<bool> RecordIntentAsync(
        ExternalGrantRootType rootType, Guid recordId, InheritedShareOutcome intent, IReadOnlyList<(string Table, Guid Id)> from,
        List<AssignedAccessLedgerRow> rows, List<string> notDone, CancellationToken ct)
    {
        foreach (var parent in from)
        {
            var source = AssignedAccessStore.InheritedSourceField(parent.Table, parent.Id);
            // (A Declined row never gets here: its principal is in the mirror's declined set, so nothing is written for it.)
            var mine = rows.FirstOrDefault(r =>
                string.Equals(r.SourceField?.Trim(), source, StringComparison.OrdinalIgnoreCase)
                && AssignedAccessStore.InheritedPrincipalOf(r) == intent.Principal);
            var inheritedElsewhere = rows.FirstOrDefault(r =>
                r.State == AssignedAccessState.Shared && !ReferenceEquals(r, mine)
                && AssignedAccessStore.InheritedPrincipalOf(r) == intent.Principal);
            var prior = mine is { State: AssignedAccessState.Shared } ? PriorMaskOf(mine.Reason)   // a re-raise keeps what it raised from
                : inheritedElsewhere is not null ? PriorMaskOf(inheritedElsewhere.Reason)
                : intent.MaskBefore;
            var write = new AssignedAccessLedgerWrite(AssignedAccessState.Shared, PendingReason(prior), GrantedLevel: intent.MaskAfter);

            if (mine is null)
            {
                Guid? id;
                try
                {
                    id = await _ledger.CreateInheritedLedgerAsync(rootType, recordId, parent.Table, parent.Id, intent.Principal, write, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "[SECURE-INHERIT] Recording {Principal}'s share from {Parent} on {RecordId} ahead of it failed; " +
                        "the share is not written.", intent.Principal, $"{parent.Table}:{parent.Id:D}", recordId);
                    notDone.Add($"where {intent.Principal}'s share would come from could not be recorded, so it was not given");
                    return false;
                }

                if (id is not { } created)
                {
                    notDone.Add($"{intent.Principal}'s share from {parent.Table} {parent.Id:D} was being recorded concurrently; " +
                                "it is decided on the next pass");
                    return false;
                }

                rows.Add(new AssignedAccessLedgerRow
                {
                    Id = created, SourceField = source, StateValue = (int)write.State, Reason = write.Reason, GrantedLevel = write.GrantedLevel,
                    SystemUserId = intent.Principal.Kind == DataversePrincipalKind.SystemUser ? intent.Principal.Id : null,
                    SubjectTeamId = intent.Principal.Kind == DataversePrincipalKind.Team ? intent.Principal.Id : null,
                });
                continue;
            }

            try
            {
                // Round 47 item 2: If-Match from the read this decision was made on. A row that changed meanwhile (an
                // operator's Declined marker landing between this pass's read and this write) is not written over, and
                // the share is NOT given.
                if (!await _ledger.UpdateLedgerIfUnchangedAsync(mine, write, ct).ConfigureAwait(false))
                {
                    notDone.Add($"{intent.Principal}'s share from {parent.Table} {parent.Id:D} was not given: its record changed " +
                                "while it was being decided; it is decided on the next pass");
                    return false;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[SECURE-INHERIT] Recording {Principal}'s share from {Parent} on {RecordId} ahead of it failed; " +
                    "the share is not written.", intent.Principal, $"{parent.Table}:{parent.Id:D}", recordId);
                notDone.Add($"where {intent.Principal}'s share would come from could not be recorded, so it was not given");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// (d) For ONE (parent, principal) after the mirror. A share this pass wrote (Granted / Raised, read back): its row,
    /// written ahead of it, is confirmed. A principal that already held the mirror (AlreadyCovered): a live row of this
    /// parent already records it (an unconfirmed one whose share is in place, or a kept last reader the parent shares again,
    /// is confirmed); otherwise the share is inherited when a live row of another parent records it as passed on (and it is
    /// in place), and DIRECT (<see cref="AssignedAccessState.CoveredByExisting"/>) only when no live row says it was passed
    /// on — every share this rule writes has its row before it exists, so "no row" is never one of the rule's own shares.
    /// Declined and Adopted rows are never overwritten here. A row a concurrent pass created first is left as it is
    /// (reported; the next pass decides). Throws on a failed write (the caller reports it).
    /// </summary>
    private async Task RecordProvenanceAsync(
        ExternalGrantRootType rootType, Guid recordId, (string Table, Guid Id) parent, InheritedShareOutcome outcome,
        List<AssignedAccessLedgerRow> rows, List<string> notDone, CancellationToken ct)
    {
        var source = AssignedAccessStore.InheritedSourceField(parent.Table, parent.Id);
        var mine = rows.FirstOrDefault(r =>
            string.Equals(r.SourceField?.Trim(), source, StringComparison.OrdinalIgnoreCase)
            && AssignedAccessStore.InheritedPrincipalOf(r) == outcome.Principal);
        var held = outcome.Action == InheritedShareAction.AlreadyCovered ? outcome.MaskBefore : outcome.MaskAfter;

        if (mine is { State: AssignedAccessState.Shared })
        {
            if ((IsPending(mine) || mine.Reason == AssignedAccessReason.KeptLastReader)
                && mine.GrantedLevel is { } level && (held & level) == level)
            {
                await ConfirmAsync(mine, ct).ConfigureAwait(false);
            }

            return;
        }

        if (outcome.Action != InheritedShareAction.AlreadyCovered
            || mine is { State: AssignedAccessState.Declined or AssignedAccessState.Adopted or AssignedAccessState.CoveredByExisting })
        {
            return; // a written share whose row changed meanwhile (the next pass decides), an operator's decision, or recorded
        }

        var inheritedElsewhere = rows.FirstOrDefault(r =>
            r.State == AssignedAccessState.Shared && !ReferenceEquals(r, mine)
            && AssignedAccessStore.InheritedPrincipalOf(r) == outcome.Principal
            && r.GrantedLevel is { } other && (held & other) == other);
        var write = inheritedElsewhere is not null
            ? new AssignedAccessLedgerWrite(AssignedAccessState.Shared, PriorReason(PriorMaskOf(inheritedElsewhere.Reason)),
                GrantedLevel: inheritedElsewhere.GrantedLevel)
            : new AssignedAccessLedgerWrite(AssignedAccessState.CoveredByExisting, AssignedAccessReason.CoveredByExistingShare,
                GrantedLevel: held);

        if (mine is null)
        {
            var id = await _ledger.CreateInheritedLedgerAsync(rootType, recordId, parent.Table, parent.Id, outcome.Principal, write, ct)
                .ConfigureAwait(false);
            if (id is not { } created)
            {
                notDone.Add($"{outcome.Principal}'s share from {parent.Table} {parent.Id:D} was recorded concurrently; it is decided " +
                            "on the next pass");
                return;
            }

            rows.Add(new AssignedAccessLedgerRow
            {
                Id = created, SourceField = source, StateValue = (int)write.State, Reason = write.Reason, GrantedLevel = write.GrantedLevel,
                SystemUserId = outcome.Principal.Kind == DataversePrincipalKind.SystemUser ? outcome.Principal.Id : null,
                SubjectTeamId = outcome.Principal.Kind == DataversePrincipalKind.Team ? outcome.Principal.Id : null,
            });
            return;
        }

        await UpdateDecidedRowAsync(mine, write, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// (e) Task 158 r1c-v2: the shares this pass WROTE, decided again on what holds once they landed. The mirror decided on
    /// reads taken before its writes; meanwhile a parent may have been UNSECURED (it passes nothing on — round 39 item 1),
    /// re-owned out of isolation (an unsecure in progress), or may have unshared the principal — and that change's reverse
    /// pass may have read this record's provenance before this share's row existed, or ended the row before this share
    /// landed (it then removed nothing, for there was nothing yet). Left alone, the share would stay on the record with no
    /// source, and a later pass would record it as direct access. So, from fresh reads: a row a concurrent pass ended while
    /// this share is in place is put back on record as the share this pass wrote; and a share whose parent no longer passes
    /// it on is ended by the reverse rule now (<see cref="EndInheritedSourceAsync"/>, exactly as step (b) of the next pass
    /// would). A parent that cannot be read is reported. Throws on a failed read or write (the caller reports it).
    /// </summary>
    private async Task RecheckWrittenSharesAsync(
        string logical, Guid recordId, ExternalGrantRootType rootType, IReadOnlyList<InheritedShareOutcome> written,
        IReadOnlyList<(string Table, Guid Id)> from, List<string> notDone, CancellationToken ct)
    {
        var rows = (await _ledger.ReadInheritedLedgerAsync(rootType, recordId, ct).ConfigureAwait(false)).ToList();
        var held = MasksOf(await _recordShare.GetPrincipalAccessOrThrowAsync(logical, recordId, ct).ConfigureAwait(false));
        var parentsNow = new Dictionary<(string, Guid), ParentMirrorAnswer>();
        foreach (var outcome in written)
        {
            foreach (var parent in from)
            {
                var source = AssignedAccessStore.InheritedSourceField(parent.Table, parent.Id);
                var row = rows.FirstOrDefault(r =>
                    string.Equals(r.SourceField?.Trim(), source, StringComparison.OrdinalIgnoreCase)
                    && AssignedAccessStore.InheritedPrincipalOf(r) == outcome.Principal);
                if (row is null || row.State == AssignedAccessState.Revoked)
                {
                    // Ended by a concurrent reverse pass as this share landed. When it also removed the share, nothing of this
                    // pass's is left; when the share is in place, it is this pass's — on record again before it is decided.
                    var inPlace = held.TryGetValue(outcome.Principal, out var mask) && (mask & outcome.MaskAfter) == outcome.MaskAfter;
                    if (!inPlace)
                        continue;

                    row = await ReopenAsync(rootType, recordId, parent, outcome, row, rows, ct).ConfigureAwait(false);
                    if (row is null)
                    {
                        notDone.Add($"{outcome.Principal}'s share from {parent.Table} {parent.Id:D} was being recorded concurrently; " +
                                    "it is decided on the next pass");
                        continue;
                    }
                }

                if (!parentsNow.TryGetValue(parent, out var mirror))
                {
                    mirror = await _synchronizer.IsolatedParentMirrorAsync(parent.Table, parent.Id, ct).ConfigureAwait(false);
                    parentsNow[parent] = mirror;
                }

                if (mirror.Unreadable)
                {
                    notDone.Add($"whether {parent.Table} {parent.Id:D} still passes on the share just given to {outcome.Principal} " +
                                "could not be read");
                    continue;
                }

                if (!mirror.PassesNothingOn && mirror.Mirror is { } sharees && sharees.ContainsKey(outcome.Principal))
                    continue; // still passed on: the share stands

                _logger.LogWarning(
                    "[SECURE-INHERIT] {Principal}'s share on {Table} {RecordId} was written from {Parent}, which no longer passes it on " +
                    "(it changed while the share was being written); it is ended now.", outcome.Principal, logical, recordId,
                    $"{parent.Table}:{parent.Id:D}");
                var ended = await EndInheritedSourceAsync(logical, recordId, row, ct).ConfigureAwait(false);
                if (!ended.Done)
                    notDone.Add($"{outcome.Principal}'s share from {parent.Table} {parent.Id:D}: {ended.Detail}");
            }
        }
    }

    /// <summary>
    /// (e) Puts a row a concurrent pass ended back on record as the share this pass wrote — <see cref="AssignedAccessState.Shared"/>,
    /// confirmed (the share is in place), the mask written, the level it raised from. <c>null</c> when a concurrent pass
    /// created the row first (the store wrote nothing over it).
    /// </summary>
    private async Task<AssignedAccessLedgerRow?> ReopenAsync(
        ExternalGrantRootType rootType, Guid recordId, (string Table, Guid Id) parent, InheritedShareOutcome outcome,
        AssignedAccessLedgerRow? row, List<AssignedAccessLedgerRow> rows, CancellationToken ct)
    {
        var write = new AssignedAccessLedgerWrite(AssignedAccessState.Shared, PriorReason(outcome.MaskBefore), GrantedLevel: outcome.MaskAfter);
        if (row is not null)
        {
            // Round 47 item 2: reopened only if the row is still the ended one this pass read.
            return await _ledger.UpdateLedgerIfUnchangedAsync(row, write, ct).ConfigureAwait(false) ? row : null;
        }

        var created = await _ledger.CreateInheritedLedgerAsync(rootType, recordId, parent.Table, parent.Id, outcome.Principal, write, ct)
            .ConfigureAwait(false);
        if (created is not { } id)
            return null;

        var reopened = new AssignedAccessLedgerRow
        {
            Id = id, SourceField = AssignedAccessStore.InheritedSourceField(parent.Table, parent.Id), StateValue = (int)write.State,
            Reason = write.Reason, GrantedLevel = write.GrantedLevel,
            SystemUserId = outcome.Principal.Kind == DataversePrincipalKind.SystemUser ? outcome.Principal.Id : null,
            SubjectTeamId = outcome.Principal.Kind == DataversePrincipalKind.Team ? outcome.Principal.Id : null,
        };
        rows.Add(reopened);
        return reopened;
    }

    /// <summary>Confirms a Shared row (drops the write-ahead / kept-last-reader marker, keeps the level it raised from). Throws on a failed write.</summary>
    private async Task ConfirmAsync(AssignedAccessLedgerRow row, CancellationToken ct)
    {
        var confirmed = PriorReason(PriorMaskOf(row.Reason));
        await UpdateDecidedRowAsync(row,
            new AssignedAccessLedgerWrite(AssignedAccessState.Shared, confirmed, GrantedLevel: row.GrantedLevel), ct).ConfigureAwait(false);
    }

    /// <summary>What ending one inherited share did.</summary>
    /// <param name="Done">Nothing is left to do for the row.</param>
    /// <param name="Removed">The share was removed (or put back to the level it raised from).</param>
    /// <param name="Detail">Why not done.</param>
    /// <param name="RowEnded"><c>false</c> when the row stays live (the record's last reader, kept — S5).</param>
    /// <param name="StillCarried">After a removal: what the record's current secure parents (those read) still pass the
    /// principal on at — non-zero when they still give part of it, which is given back at once (verifier item 3).</param>
    private readonly record struct EndOutcome(bool Done, bool Removed, string? Detail, bool RowEnded = true, int StillCarried = 0);

    /// <summary>
    /// The reverse rule for ONE inherited-share row whose parent no longer shares the principal (owner round 30, under
    /// ADR-034 Amendment A4's rules): a <see cref="AssignedAccessState.Declined"/> row ends (the operator's removal stands);
    /// an <see cref="AssignedAccessState.Adopted"/> or <see cref="AssignedAccessState.CoveredByExisting"/> share is direct
    /// and stays; a <see cref="AssignedAccessState.Shared"/> one is removed ONLY when it is still UNMODIFIED (the mask
    /// written), no other provenance justifies it (an independent Assigned-To row, or the record's current secure parents,
    /// whose intersection still carries all it added), and removing it leaves someone who can open the record (S5) — put
    /// back to the mask it raised, or revoked, read back, and the record's own children brought into line. A row recorded
    /// ahead of a share that never landed ends with nothing removed. Never lowers access below what the record held before
    /// the share was passed on. The record's LAST reader is kept with its row live (r1c-v1): the share is still the one the
    /// rule passed on, so every later pass tries again and removes it once someone else can open the record.
    /// </summary>
    /// <param name="endingParent">Task 158 r1c-v2 (round 39 item 1): a parent whose sharing is ending as a whole — the
    /// matter or project being UNSECURED — which therefore justifies nothing any more, though its flag still reads secure
    /// until the unsecure's last step. <c>null</c> for every other caller.</param>
    private async Task<EndOutcome> EndInheritedSourceAsync(
        string logical, Guid recordId, AssignedAccessLedgerRow row, CancellationToken ct,
        (string Table, Guid Id)? endingParent = null)
    {
        if (AssignedAccessStore.InheritedPrincipalOf(row) is not { } principal)
            return new EndOutcome(true, false, null);

        try
        {
            switch (row.State)
            {
                case AssignedAccessState.Revoked:
                    return new EndOutcome(true, false, null);
                case AssignedAccessState.Adopted:
                    await EndRowAsync(row, AssignedAccessReason.KeptAdopted, ct).ConfigureAwait(false);
                    return new EndOutcome(true, false, null);
                case AssignedAccessState.CoveredByExisting:
                    await EndRowAsync(row, AssignedAccessReason.KeptDirectShare, ct).ConfigureAwait(false);
                    return new EndOutcome(true, false, null);
                case AssignedAccessState.Shared:
                    break;
                default: // Declined (the operator's removal stands), Skipped (removed by the No Access list), or anything unexpected
                    await EndRowAsync(row, AssignedAccessReason.AssignmentEnded, ct).ConfigureAwait(false);
                    return new EndOutcome(true, false, null);
            }

            var rootType = RootTypeOf(logical);
            var written = row.GrantedLevel ?? 0;

            // What the parent's share ADDED (a raise keeps the level it raised from): the record's current secure parents
            // justify the share when their intersection still carries at least that.
            var prior = PriorMaskOf(row.Reason);
            var added = written & ~prior;
            var (justified, carried) = await StillJustifiedByParentsAsync(
                logical, recordId, principal, added != 0 ? added : written, endingParent, ct).ConfigureAwait(false);
            if (justified is null)
            {
                return new EndOutcome(false, false,
                    "every secure record it is filed under that could be read still shares it, but another could not be read");
            }

            if (justified == true)
            {
                await EndRowAsync(row, AssignedAccessReason.KeptOtherSource, ct).ConfigureAwait(false);
                return new EndOutcome(true, false, null);
            }

            // Direct as well: an independent (Assigned-To) ledger row naming the same user — but ONLY one where task 142 WROTE
            // the access (Shared) or an operator ADOPTED it (Adopted). Task 158 r1c-v2 (round 47 item 1 (1), E-158-v1-1): a
            // CoveredByExisting row is 142's OBSERVATION that a share already covered its target — very possibly this rule's
            // share — so it justifies nothing (counting it kept the parent's level after both sources had ended).
            IReadOnlyList<AssignedAccessLedgerRow> assigned = Array.Empty<AssignedAccessLedgerRow>();
            if (principal.Kind == DataversePrincipalKind.SystemUser)
            {
                assigned = (await _ledger.ReadLedgerAsync(rootType, recordId, ct).ConfigureAwait(false))
                    .Where(r => AssignedAccessStore.InheritedSourceOf(r.SourceField) is null && r.SystemUserId == principal.Id)
                    .ToList();
                if (assigned.Any(r => r.State is AssignedAccessState.Shared or AssignedAccessState.Adopted))
                {
                    await EndRowAsync(row, AssignedAccessReason.KeptOtherField, ct).ConfigureAwait(false);
                    return new EndOutcome(true, false, null);
                }
            }

            var shares = MasksOf(await _recordShare.GetPrincipalAccessOrThrowAsync(logical, recordId, ct).ConfigureAwait(false));
            var mask = shares.TryGetValue(principal, out var m) ? m : 0;
            if (IsPending(row) && (mask & written) != written)
            {
                // Recorded ahead of its write, and the write is not in place: nothing of the rule's is on the record. The row
                // ends with nothing removed; a share someone changed meanwhile is theirs (A4: "modified" is kept).
                await EndRowAsync(row, mask == prior ? AssignedAccessReason.AssignmentEnded : AssignedAccessReason.KeptModified, ct)
                    .ConfigureAwait(false);
                return new EndOutcome(true, false, null);
            }

            if (mask != written)
            {
                // Raised or narrowed since it was passed on: no longer the share this rule made (A4: "modified" is kept).
                await EndRowAsync(row, AssignedAccessReason.KeptModified, ct).ConfigureAwait(false);
                return new EndOutcome(true, false, null);
            }

            if (prior == 0 && !shares.Any(s => s.Key != principal && RecordShareLevels.CanRead(s.Value)))
            {
                // S5: the record's last reader is never removed by this rule. Its row stays live — the share is still the one
                // the rule passed on (an ended row would let a later pass read it as direct access) — and every later pass
                // tries again, removing it once someone else can open the record.
                if (row.Reason != AssignedAccessReason.KeptLastReader)
                {
                    await UpdateDecidedRowAsync(row,
                        new AssignedAccessLedgerWrite(AssignedAccessState.Shared, AssignedAccessReason.KeptLastReader, GrantedLevel: written),
                        ct).ConfigureAwait(false);
                }

                _logger.LogWarning(
                    "[SECURE-INHERIT] {Principal}'s inherited share on {Table} {RecordId} is the last share that opens it; kept (S5) " +
                    "until someone else can open it.", principal, logical, recordId);
                return new EndOutcome(true, false, null, RowEnded: false);
            }

            // Round 47 item 1 (2): a 142 CoveredByExisting row naming the person observed THIS share covering its target; the
            // share is about to be removed (or put back to what it raised), a KNOWN cause, not an operator's removal — so the
            // row is Skipped (covering-share-ended), written AHEAD of the removal (the write-ahead shape of round 47 item 2): a
            // 142 pass between the removal and the marker can never read it as removed-out-of-band (Declined). The materializer
            // then decides it afresh (below): covered again by what is left, or — on a secure record — the assignee suggested.
            // A removal that fails leaves a Skipped row over the share as it was: 142's next pass records it covered again
            // (Skipped is re-evaluated every pass).
            var uncovered = assigned.Where(r => r.State == AssignedAccessState.CoveredByExisting).ToList();
            foreach (var covering in uncovered)
            {
                await UpdateDecidedRowAsync(covering,
                    new AssignedAccessLedgerWrite(AssignedAccessState.Skipped, AssignedAccessReason.CoveringShareEnded,
                        SystemUserId: principal.Id), ct).ConfigureAwait(false);
            }

            var entitySet = SecureDesignationRemoval.EntitySetFor(logical);
            if (prior != 0)
                await _recordShare.ModifyAccessAsync(entitySet, recordId, principal, RecordShareLevels.RightsCsvForMask(prior), ct).ConfigureAwait(false);
            else
                await _recordShare.RevokeAccessAsync(entitySet, recordId, principal, ct).ConfigureAwait(false);

            var after = MasksOf(await _recordShare.GetPrincipalAccessOrThrowAsync(logical, recordId, ct).ConfigureAwait(false));
            if ((after.TryGetValue(principal, out var left) ? left : 0) != prior)
                return new EndOutcome(false, false, "the share did not read back as removed");

            await EndRowAsync(row, prior != 0 ? AssignedAccessReason.PriorLevelRestored : AssignedAccessReason.AccessRemoved, ct)
                .ConfigureAwait(false);
            foreach (var other in (await _ledger.ReadInheritedLedgerAsync(rootType, recordId, ct).ConfigureAwait(false))
                         .Where(r => r.Id != row.Id && r.State == AssignedAccessState.Shared && AssignedAccessStore.InheritedPrincipalOf(r) == principal))
            {
                await EndRowAsync(other, AssignedAccessReason.AccessRemoved, ct).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "[SECURE-INHERIT] {Principal}'s inherited share on {Table} {RecordId} was {Action} (its parent no longer shares them).",
                principal, logical, recordId, prior != 0 ? $"put back to mask {prior}" : "removed");

            // Round 47 item 1 (2): task 142's materializer runs at once for the assignment whose covering share just ended — on
            // a secure record the assignee is SUGGESTED (owner A3), never silently dropped. Its outcome is 142's: one that does
            // not complete leaves the row Skipped, which 142's own job re-evaluates (<= 5 minutes).
            if (uncovered.Count > 0)
            {
                var suggestion = await _assignedAccess.MaterializeAsync(
                    new AssignedAccessRequest(rootType, recordId, AssignedAccessTrigger.Inline, GrantorOid: null, RevokeOnChange: true,
                        _assignedAccess.DeploymentTenants()), ct).ConfigureAwait(false);
                if (!suggestion.Complete)
                {
                    _logger.LogWarning(
                        "[SECURE-INHERIT] {Principal}'s Assigned-To access on {Table} {RecordId} was covered by the share just removed; " +
                        "its materialization ended {Status} — the Assigned-To job completes it.", principal, logical, recordId,
                        suggestion.Status);
                }
            }

            // The record's own children carry its shares.
            var cascade = await _synchronizer.SyncRootAsync(logical, recordId, ct).ConfigureAwait(false);
            return cascade.IsComplete
                ? new EndOutcome(true, true, null, StillCarried: carried)
                : new EndOutcome(false, true, $"its own related records were not all brought into line ({cascade.Status})", StillCarried: carried);
        }
        catch (LedgerRowChangedException changed)
        {
            // Round 47 item 2: a record this decision was made on changed between the read and the write (an operator's
            // marker, another pass's end, a task 142 write). Nothing is written over it. The next pass decides on it as it is.
            _logger.LogInformation(
                "[SECURE-INHERIT] Ending {Principal}'s inherited share on {Table} {RecordId}: record {RowId} changed while it " +
                "was being decided; the next pass decides on it.", principal, logical, recordId, changed.RowId);
            return new EndOutcome(false, false, "its share record changed while it was being decided; it is decided on the next pass");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-INHERIT] Ending {Principal}'s inherited share on {Table} {RecordId} failed.", principal, logical, recordId);
            return new EndOutcome(false, false, "removing it failed");
        }
    }

    /// <summary>
    /// Whether the record's CURRENT secure parents still pass <paramref name="principal"/> on at <paramref name="needed"/>
    /// — the intersection rule (owner round 11 item 4) the mirror gave it by, with task 149's rule for parents that cannot
    /// be trusted ("a principal its known roots do not share is revoked; held children are only narrowed"):
    /// <c>false</c> when a parent that was read (isolated) does not carry all of it, or no parent could be read at all — the
    /// intersection does not carry it, so the share is the ended source's; <c>true</c> when every secure parent was read and
    /// carries it; <c>null</c> (undecided, reported) when every parent that was read carries it but another could not be
    /// read or is flagged secure without being isolated. <c>Carried</c>: the intersection of what the parents that were read
    /// pass the principal on at (0 when none was read) — what a removal gives back at once.
    /// </summary>
    private async Task<(bool? Justified, int Carried)> StillJustifiedByParentsAsync(
        string logical, Guid recordId, DataversePrincipalRef principal, int needed, (string Table, Guid Id)? endingParent,
        CancellationToken ct)
    {
        var answer = await FindSecureParentsAsync(logical, recordId, ct).ConfigureAwait(false);
        var (known, unknown, notCarried, carried) = (0, !answer.IsKnown, false, ~0);
        foreach (var parent in answer.SecureParents)
        {
            // Round 39 item 1: the parent being unsecured justifies nothing (its flag still reads secure until the last step).
            if (endingParent is { } ending && string.Equals(parent.Table, ending.Table, StringComparison.OrdinalIgnoreCase)
                && parent.Id == ending.Id)
                continue;

            var mirror = await _synchronizer.IsolatedParentMirrorAsync(parent.Table, parent.Id, ct).ConfigureAwait(false);
            if (mirror.Unreadable || mirror.Mirror is null)
            {
                unknown = true; // unreadable, or flagged secure but not isolated: its mirror cannot be trusted
                continue;
            }

            var passedOn = mirror.Mirror.TryGetValue(principal, out var c) ? c : 0;
            carried &= passedOn;
            known++;
            if ((passedOn & needed) != needed)
                notCarried = true;
        }

        if (known == 0)
            return (false, 0);
        return notCarried ? (false, carried) : (unknown ? null : true, carried);
    }

    private Task EndRowAsync(AssignedAccessLedgerRow row, string reason, CancellationToken ct) =>
        UpdateDecidedRowAsync(row, new AssignedAccessLedgerWrite(AssignedAccessState.Revoked, reason), ct);

    /// <summary>
    /// Batch-4 integration (round 47 item 2): every ledger update an inheritance pass makes is conditional on the row still
    /// holding what the pass decided on (<see cref="AssignedAccessStore.UpdateLedgerIfUnchangedAsync"/>, which sends
    /// <c>If-Match</c> through task 140's client support, re-reads on a mismatch, and writes only when the facts are
    /// unchanged). Throws <see cref="LedgerRowChangedException"/> when the row changed. Each pass step reports it as
    /// "decided on the next pass", never as a write.
    /// </summary>
    private async Task UpdateDecidedRowAsync(AssignedAccessLedgerRow row, AssignedAccessLedgerWrite write, CancellationToken ct)
    {
        if (!await _ledger.UpdateLedgerIfUnchangedAsync(row, write, ct).ConfigureAwait(false))
            throw new LedgerRowChangedException(row.Id);
    }

    /// <summary>A ledger row changed between the read a pass decided on and its write; nothing was written (round 47 item 2).</summary>
    private sealed class LedgerRowChangedException(Guid rowId)
        : Exception($"Ledger row {rowId:D} changed since it was read; nothing was written.")
    {
        public Guid RowId { get; } = rowId;
    }

    /// <summary>A Shared row recorded ahead of its share and not confirmed yet (<see cref="AssignedAccessReason.SharePending"/>).</summary>
    private static bool IsPending(AssignedAccessLedgerRow row) =>
        row.State == AssignedAccessState.Shared
        && row.Reason?.StartsWith(AssignedAccessReason.SharePending, StringComparison.Ordinal) == true;

    /// <summary>The reason of a row recorded ahead of its share: the marker, then the level it raises from (if any).</summary>
    private static string PendingReason(int prior) =>
        prior == 0
            ? AssignedAccessReason.SharePending
            : AssignedAccessReason.SharePending + ";" + PriorReason(prior);

    /// <summary>The reason of a confirmed row: <c>raised-from-mask:N</c>, or none when it raised nothing.</summary>
    private static string? PriorReason(int prior) =>
        prior == 0 ? null : AssignedAccessReason.RaisedFromMaskPrefix + prior.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The mask a raise recorded in its reason (<c>raised-from-mask:N</c>, after any marker), or 0 when it raised nothing.</summary>
    private static int PriorMaskOf(string? reason)
    {
        var at = reason?.IndexOf(AssignedAccessReason.RaisedFromMaskPrefix, StringComparison.Ordinal) ?? -1;
        return at >= 0
               && int.TryParse(reason![(at + AssignedAccessReason.RaisedFromMaskPrefix.Length)..], System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out var prior)
            ? prior
            : 0;
    }

    private static Dictionary<DataversePrincipalRef, int> MasksOf(IReadOnlyList<DataversePrincipalAccess> shares) =>
        shares.GroupBy(s => s.Principal).ToDictionary(g => g.Key, g => g.Aggregate(0, (m, s) => m | s.AccessRightsMask));

    private static ExternalGrantRootType RootTypeOf(string logical) =>
        string.Equals(logical, Project, StringComparison.OrdinalIgnoreCase) ? ExternalGrantRootType.Project
        : string.Equals(logical, Matter, StringComparison.OrdinalIgnoreCase) ? ExternalGrantRootType.Matter
        : ExternalGrantRootType.WorkAssignment;

    /// <summary>
    /// A secure matter's or project's sharee was REMOVED (<c>/unshare-user</c>): every secure work assignment / project the
    /// parent's sharing gave them is ended by the reverse rule NOW (owner round 30 — "fan-out happens in the same place 158
    /// already calls PassSharesOnAsync, and in the reverse direction on unshare"), from the ledger rows sourced from that
    /// parent. A row whose share is no longer the unmodified inherited one, is also direct, or is justified by another
    /// parent is ended without touching the share. Never throws; an incomplete pass is reported (children_incomplete) and
    /// completed by the job.
    /// </summary>
    public async Task<InheritedUnsharePass> PassUnshareOnAsync(
        string parentTable, Guid parentId, DataversePrincipalRef principal, string traceId, CancellationToken ct)
    {
        if (!IsParent(parentTable))
            return InheritedUnsharePass.NotApplicable;

        var parent = parentTable.Trim().ToLowerInvariant();
        IReadOnlyList<AssignedAccessLedgerRow> rows;
        bool truncated;
        try
        {
            (rows, truncated) = await _ledger.ReadInheritedLedgerByParentAsync(parent, parentId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[SECURE-INHERIT] What {Parent} {ParentId} passed on to the records filed under it could not be read; {Principal} " +
                "keeps it there until the secure-root inheritance job ends it.", parent, parentId, principal);
            return new InheritedUnsharePass(SecureFiledRootsStatus.Failed, 0, 0, 0, 0,
                "what the record passed on to the work assignments and projects filed under it could not be read");
        }

        var mine = rows.Where(r => r.State != AssignedAccessState.Revoked && AssignedAccessStore.InheritedPrincipalOf(r) == principal).ToList();
        if (mine.Count > 0)
        {
            // A SECURE (isolated) parent's unshare ends what it passed on (owner round 30) — the job's rule too — and so does a
            // parent that reads NOT secure any more (task 158 r1c-v2, round 39 item 1: unsecuring a parent ends what it passed
            // on; its unsecure ends the rows itself, this ends any it left). One that still shares the principal (a second
            // share) has nothing to end; one flagged secure but not isolated (mid-provisioning, mid-unsecure) ends nothing —
            // held; one whose state cannot be read is reported (children_incomplete), never read as "not secure".
            var parentMirror = await _synchronizer.IsolatedParentMirrorAsync(parent, parentId, ct).ConfigureAwait(false);
            if (parentMirror.Unreadable)
            {
                _logger.LogError(
                    "[SECURE-INHERIT] Whether {Parent} {ParentId} is still secure could not be read; what it passed on to " +
                    "{Principal} is ended by the secure-root inheritance job.", parent, parentId, principal);
                return new InheritedUnsharePass(SecureFiledRootsStatus.Failed, mine.Count, 0, 0, mine.Count,
                    "whether the record is still secure could not be read, so what it passed on was not ended");
            }

            if (!parentMirror.PassesNothingOn && (parentMirror.Mirror is null || parentMirror.Mirror.ContainsKey(principal)))
            {
                _logger.LogInformation(
                    "[SECURE-INHERIT] {Parent} {ParentId} is {State}: the {Count} inherited share(s) it passed on to {Principal} " +
                    "stay.", parent, parentId, parentMirror.Mirror is null ? "flagged secure but not isolated" : "still sharing them",
                    mine.Count, principal);
                return InheritedUnsharePass.NotApplicable;
            }
        }

        var ended = await EndPassedOnAsync(mine, truncated, endingParent: null, ct).ConfigureAwait(false);

        // Task 158 r1c-v1 (verifier item 3): a record whose share was removed while the secure records it is filed under NOW
        // still pass part of it on (a parent sharing the person at a lower level) is given that part back in the same call —
        // its parents' sharees, the mirror's own pass (never a provisioning) — not left to the job's next run.
        var (notRegiven, regiveDetails) = await GiveBackAsync(ended.GiveBack, traceId, ct).ConfigureAwait(false);
        var details = new List<string>();
        if (ended.Detail is not null)
            details.Add(ended.Detail);
        details.AddRange(regiveDetails);

        var complete = ended.IsComplete && notRegiven == 0;
        _logger.LogInformation(
            "[SECURE-INHERIT] {Principal} was unshared from {Parent} {ParentId}: inherited rows={Rows} removed={Removed} kept={Kept} " +
            "notDone={NotDone} regiven={Regiven} notRegiven={NotRegiven} truncated={Truncated}. TraceId={TraceId}", principal, parent,
            parentId, mine.Count, ended.Removed, ended.Kept, ended.NotDone, ended.GiveBack.Select(g => (g.Table, g.Id)).Distinct().Count() - notRegiven,
            notRegiven, truncated, traceId);
        return new InheritedUnsharePass(
            complete ? SecureFiledRootsStatus.Completed : SecureFiledRootsStatus.Incomplete,
            mine.Count, ended.Removed, ended.Kept, ended.NotDone, details.Count == 0 ? null : string.Join("; ", details),
            notRegiven);
    }

    /// <summary>
    /// Task 158 r1c-v2 (main-session round 39 item 1 — interpretation xiii reversed): a matter or project is being UNSECURED.
    /// Its unsecure revoked every explicit share on it (Step 4), so for each sharee it revoked, round 30's reverse rule runs
    /// on the secure records filed under it: every live inherited-share row the parent passed on is ended by
    /// <see cref="EndInheritedSourceAsync"/>, the parent counting as justifying NOTHING (its flag still reads secure until the
    /// unsecure's last step). Ended: only the UNMODIFIED inherited share. Kept: a direct share, a raised mask (put back to
    /// what it raised), a share another secure parent still justifies, the record's last reader (S5). The rows are read from
    /// the parent's own provenance, not from the share list Step 4 revoked, so a repeat call (after a failure, the flag still
    /// set) ends exactly what is left. Records whose removed share their OTHER secure parents still pass part of are returned
    /// in <see cref="InheritedUnsharePass.GiveBack"/>: the unsecure gives that part back once its flag is cleared
    /// (<see cref="GiveBackAsync"/> — a flagged parent that is no longer isolated holds every mirror until then). Never throws;
    /// a pass that is not complete is reported through <c>children_incomplete</c>, and the same call completes it.
    /// </summary>
    public async Task<InheritedUnsharePass> EndWhatAParentPassedOnAsync(
        string parentTable, Guid parentId, string traceId, CancellationToken ct)
    {
        if (!IsParent(parentTable))
            return InheritedUnsharePass.NotApplicable;

        var parent = parentTable.Trim().ToLowerInvariant();
        IReadOnlyList<AssignedAccessLedgerRow> rows;
        bool truncated;
        try
        {
            (rows, truncated) = await _ledger.ReadInheritedLedgerByParentAsync(parent, parentId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[SECURE-INHERIT] What {Parent} {ParentId} passed on to the records filed under it could not be read; it is not " +
                "ended yet. TraceId={TraceId}", parent, parentId, traceId);
            return new InheritedUnsharePass(SecureFiledRootsStatus.Failed, 0, 0, 0, 1,
                "what it passed on to the work assignments and projects filed under it could not be read");
        }

        var live = rows.Where(r => r.State != AssignedAccessState.Revoked && AssignedAccessStore.InheritedPrincipalOf(r) is not null)
            .ToList();
        var ended = await EndPassedOnAsync(live, truncated, endingParent: (parent, parentId), ct).ConfigureAwait(false);
        _logger.LogInformation(
            "[SECURE-INHERIT] {Parent} {ParentId} is being unsecured: inherited rows={Rows} removed={Removed} kept={Kept} " +
            "notDone={NotDone} toGiveBack={GiveBack} truncated={Truncated}. TraceId={TraceId}", parent, parentId, live.Count,
            ended.Removed, ended.Kept, ended.NotDone, ended.GiveBack.Count, truncated, traceId);
        return ended;
    }

    /// <summary>
    /// The reverse rule over rows passed on from ONE parent (the <c>/unshare-user</c> fan-out and the unsecure's): each ended
    /// by <see cref="EndInheritedSourceAsync"/>; what the record's other secure parents still pass part of is collected for
    /// the give-back. <paramref name="endingParent"/>: the parent whose sharing ends as a whole (an unsecure).
    /// </summary>
    private async Task<InheritedUnsharePass> EndPassedOnAsync(
        IReadOnlyList<AssignedAccessLedgerRow> rows, bool truncated, (string Table, Guid Id)? endingParent, CancellationToken ct)
    {
        var (removed, kept, notDone) = (0, 0, 0);
        var details = new List<string>();
        var giveBack = new List<(string Table, Guid Id, DataversePrincipalRef Principal)>();
        foreach (var row in rows)
        {
            if (AssignedAccessStore.RootOf(row) is not { } root || AssignedAccessStore.InheritedPrincipalOf(row) is not { } principal)
                continue;

            var logical = ExternalGrantRoot.LogicalNameFor(root.RootType);
            var ended = await EndInheritedSourceAsync(logical, root.RootId, row, ct, endingParent).ConfigureAwait(false);
            if (!ended.Done)
            {
                notDone++;
                details.Add($"{logical} {root.RootId:D}: {principal}: {ended.Detail}");
            }
            else if (ended.Removed)
            {
                removed++;
            }
            else
            {
                kept++;
            }

            // One row per (record, parent, principal), and every row here is from ONE parent: no entry repeats.
            if (ended.Removed && ended.StillCarried != 0)
                giveBack.Add((logical, root.RootId, principal));
        }

        if (truncated)
            details.Add("more inherited shares than one pass reads; the secure-root inheritance job ends the rest");

        return new InheritedUnsharePass(
            notDone == 0 && !truncated ? SecureFiledRootsStatus.Completed : SecureFiledRootsStatus.Incomplete,
            rows.Count, removed, kept, notDone + (truncated ? 1 : 0), details.Count == 0 ? null : string.Join("; ", details))
        {
            GiveBack = giveBack,
        };
    }

    /// <summary>
    /// Gives back, in the same call, what the secure records a record is filed under NOW still pass on to a principal whose
    /// share was just removed (task 158 r1c-v1, verifier item 3; the unsecure's too since r1c-v2) — the mirror's own pass for
    /// each record, never a provisioning. A record counts as NOT given back when the pass could not give it (its write failed,
    /// or the No Access list could not be checked — Held) or could not decide at all (it answered for nobody and is
    /// incomplete: a parent unreadable or untrusted). Given back, or rightly not (walled, declined, nothing passed on any
    /// more), it is done. Never throws.
    /// </summary>
    public async Task<(int NotRegiven, IReadOnlyList<string> Details)> GiveBackAsync(
        IReadOnlyList<(string Table, Guid Id, DataversePrincipalRef Principal)> giveBack, string traceId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(giveBack);
        var notRegiven = 0;
        var details = new List<string>();
        foreach (var record in giveBack.GroupBy(g => (g.Table, g.Id)))
        {
            var (table, id) = record.Key;
            var regiven = await SecureIfFiledUnderSecureCoreAsync(table, id, traceId, allowProvisioning: false, ct).ConfigureAwait(false);
            var missing = record
                .Select(g => (g.Principal, Given: regiven.Shares?.Inherited?.FirstOrDefault(o => o.Principal == g.Principal)))
                .Where(g => g.Given?.Action is InheritedShareAction.Failed or InheritedShareAction.Held
                            || (g.Given is null && !regiven.IsComplete))
                .ToList();
            if (missing.Count == 0)
                continue;

            notRegiven++;
            details.Add($"{table} {id:D}: what the secure records it is filed under still give " +
                        $"{string.Join(", ", missing.Select(m => m.Principal))} could not be given back yet " +
                        $"({regiven.ReasonCode ?? missing[0].Given?.Action.ToString()})");
        }

        return (notRegiven, details);
    }

    /// <summary>
    /// A work assignment or project is being UNSECURED (task 158 r1, owner round 30): the unsecure endpoint revokes every
    /// share on it, so every share its secure parents passed on ends with it — each live inherited-share row on it is ended
    /// (<see cref="AssignedAccessReason.RecordUnsecured"/>) BEFORE the shares are revoked. A row left <c>Shared</c> would
    /// otherwise read, once the record is secured again, as an operator's removal (Declined), and its parents' sharees would
    /// never be passed on again. <c>null</c>: done (or nothing to end); otherwise why not — the unsecure stops before
    /// revoking anything and the same call completes it.
    /// </summary>
    public async Task<string?> EndProvenanceForUnsecureAsync(string table, Guid recordId, CancellationToken ct)
    {
        if (!Inherits(table))
            return null;

        var logical = table.Trim().ToLowerInvariant();
        try
        {
            var rows = await _ledger.ReadInheritedLedgerAsync(RootTypeOf(logical), recordId, ct).ConfigureAwait(false);
            var live = rows.Where(r => r.State != AssignedAccessState.Revoked).ToList();
            var end = new AssignedAccessLedgerWrite(AssignedAccessState.Revoked, AssignedAccessReason.RecordUnsecured);
            foreach (var row in live)
            {
                if (await _ledger.UpdateLedgerIfUnchangedAsync(row, end, ct).ConfigureAwait(false))
                    continue;

                // Round 47 item 2: the row changed since it was read. The unsecure ends EVERY live row whatever its state,
                // so the decision is made again on the row as it is now: still live, so it is ended at its new version.
                var now = await _ledger.ReadLedgerRowAsync(row.Id, ct).ConfigureAwait(false);
                if (now is not null && now.State != AssignedAccessState.Revoked
                    && !await _ledger.UpdateLedgerIfUnchangedAsync(now, end, ct).ConfigureAwait(false))
                {
                    return "the record of where its shares came from changed while it was being updated; the same call completes it";
                }
            }

            if (live.Count > 0)
            {
                _logger.LogInformation(
                    "[SECURE-INHERIT] {Table} {RecordId} is being unsecured: {Count} inherited share record(s) ended.",
                    logical, recordId, live.Count);
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[SECURE-INHERIT] Where the shares on {Table} {RecordId} came from could not be updated before its unsecure.",
                logical, recordId);
            return "the record of where its shares came from (shares passed on from the secure records it is filed under) " +
                   "could not be updated";
        }
    }

    /// <summary>
    /// The job's provenance reconcile for records it did NOT visit this run (task 158 r1, owner round 30: "the L4 job also
    /// reconciles provenance"): a record re-filed AWAY from a secure parent keeps what that parent passed on (re-filing is
    /// not an unshare), so a later unshare of the parent made OUTSIDE the BFF (the model-driven app's Share dialog) is
    /// reached only through the parent's own ledger rows. For each secure parent, its live inherited rows on records not in
    /// <paramref name="visited"/> whose principal the parent no longer shares are ended by the reverse rule
    /// (<see cref="EndInheritedSourceAsync"/>). A parent that is not isolated ends nothing here: the job lists only parents
    /// flagged secure, so one not isolated is mid-provisioning or mid-unsecure (the unsecure ends what it passed on itself —
    /// round 39 item 1 — and its records filed under it are reported by their own pass); one that cannot be read, a read that
    /// cannot complete, or a row that is not ended is reported (the run is not a success). Never throws.
    /// </summary>
    public async Task<InheritedUnsharePass> ReconcileUnvisitedProvenanceAsync(
        IReadOnlyCollection<(string Table, Guid Id)> secureParents, IReadOnlySet<(string Table, Guid Id)> visited,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(secureParents);
        ArgumentNullException.ThrowIfNull(visited);
        var (rowsSeen, removed, kept, notDone) = (0, 0, 0, 0);
        var details = new List<string>();

        foreach (var (table, id) in secureParents.Where(p => IsParent(p.Table)).Distinct())
        {
            var parent = table.Trim().ToLowerInvariant();
            List<AssignedAccessLedgerRow> unvisited;
            try
            {
                var (rows, truncated) = await _ledger.ReadInheritedLedgerByParentAsync(parent, id, ct).ConfigureAwait(false);
                if (truncated)
                {
                    notDone++;
                    details.Add($"{parent} {id:D}: more inherited shares than one pass reads");
                }

                unvisited = rows.Where(r => r.State != AssignedAccessState.Revoked
                                            && AssignedAccessStore.InheritedPrincipalOf(r) is not null
                                            && AssignedAccessStore.RootOf(r) is { } root
                                            && !visited.Contains((ExternalGrantRoot.LogicalNameFor(root.RootType), root.RootId)))
                    .ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[SECURE-INHERIT] What {Parent} {ParentId} passed on could not be read.", parent, id);
                notDone++;
                details.Add($"{parent} {id:D}: what it passed on could not be read");
                continue;
            }

            if (unvisited.Count == 0)
                continue;

            var mirror = await _synchronizer.IsolatedParentMirrorAsync(parent, id, ct).ConfigureAwait(false);
            if (mirror.Unreadable)
            {
                notDone += unvisited.Count;
                details.Add($"{parent} {id:D}: whether it still shares what it passed on could not be read");
                continue;
            }

            if (mirror.Mirror is null)
                continue;

            foreach (var row in unvisited)
            {
                var principal = AssignedAccessStore.InheritedPrincipalOf(row)!.Value;
                if (mirror.Mirror.ContainsKey(principal))
                    continue;

                rowsSeen++;
                var root = AssignedAccessStore.RootOf(row)!.Value;
                var logical = ExternalGrantRoot.LogicalNameFor(root.RootType);
                var ended = await EndInheritedSourceAsync(logical, root.RootId, row, ct).ConfigureAwait(false);
                if (!ended.Done)
                {
                    notDone++;
                    details.Add($"{logical} {root.RootId:D}: {principal}'s share from {parent} {id:D}: {ended.Detail}");
                }
                else if (ended.Removed)
                {
                    removed++;
                }
                else
                {
                    kept++;
                }
            }
        }

        _logger.LogInformation(
            "[SECURE-INHERIT] Provenance of records not filed under their source any more: ended={Rows} removed={Removed} " +
            "kept={Kept} notDone={NotDone}.", rowsSeen, removed, kept, notDone);
        return new InheritedUnsharePass(
            notDone == 0 ? SecureFiledRootsStatus.Completed : SecureFiledRootsStatus.Incomplete,
            rowsSeen, removed, kept, notDone, details.Count == 0 ? null : string.Join("; ", details.Take(20)));
    }

    /// <summary>
    /// (3) A matter or project has become (or is) secure: every work assignment and project FILED UNDER it is secured and
    /// given its sharees. Called by provisioning's Step 8 after the record's own children. A work assignment, or a record
    /// that is not flagged secure, has nothing to pass on (<see cref="SecureFiledRootsStatus.NotApplicable"/>).
    /// </summary>
    public Task<SecureFiledRootsPass> SecureFiledRootsUnderAsync(
        string parentTable, Guid parentId, string traceId, CancellationToken ct) =>
        IsParent(parentTable)
            ? FiledRootsPassAsync(parentTable.Trim().ToLowerInvariant(), parentId, traceId, allowProvisioning: true, ct)
            : Task.FromResult(SecureFiledRootsPass.NotApplicable($"nothing is filed under a {parentTable} for this rule"));

    private async Task<SecureFiledRootsPass> FiledRootsPassAsync(
        string parent, Guid parentId, string traceId, bool allowProvisioning, CancellationToken ct)
    {
        IReadOnlyList<FiledRootRef> filed;
        try
        {
            var flag = await ReadParentAsync(_dataverse, parent, parentId, ct).ConfigureAwait(false);

            // A record that is not (or no longer) there, or reads NOT secure, passes nothing on — and its filed records are
            // not read at all (a share on an ordinary matter costs no read of what is filed under it). An EMPTY flag is never
            // "not secure" (owner round 17 item 3): its filed records are listed and each is decided — unverifiable, nothing
            // written, reported — never skipped as "nothing to pass on".
            if (flag is null || flag.Value.Flag == false)
                return SecureFiledRootsPass.NotApplicable($"{parent} {parentId:D} is not secure");

            filed = await ListFiledRootsAsync(new[] { (parent, parentId) }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-INHERIT] The records filed under {Parent} {ParentId} could not be read.", parent, parentId);
            return new SecureFiledRootsPass(SecureFiledRootsStatus.Failed, Array.Empty<SecureRootInheritResult>(),
                "the work assignments and projects filed under it could not be read");
        }

        var results = new List<SecureRootInheritResult>();
        foreach (var root in filed)
        {
            if (root.Table == parent && root.Id == parentId)
                continue; // a project whose pair names itself is not filed under anything new

            results.Add(await SecureIfFiledUnderSecureCoreAsync(root.Table, root.Id, traceId, allowProvisioning, ct)
                .ConfigureAwait(false));
        }

        var complete = results.All(r => r.IsComplete);
        _logger.LogInformation(
            "[SECURE-INHERIT] Records filed under {Parent} {ParentId}: examined={Examined} secured={Secured} complete={Complete}.",
            parent, parentId, results.Count, results.Count(r => r.Outcome == SecureRootInheritOutcome.Secured), complete);

        return new SecureFiledRootsPass(
            complete ? SecureFiledRootsStatus.Completed : SecureFiledRootsStatus.Incomplete, results,
            complete ? null : $"{results.Count(r => !r.IsComplete)} of {results.Count} filed record(s) are not secure yet");
    }

    /// <summary>
    /// The secure matters / projects <paramref name="table"/> <paramref name="recordId"/> is filed under now — the unsecure
    /// endpoint's question ("a related record whose parent is STILL secure cannot be unsecured", owner round 6). A record
    /// that is not a work assignment or project, or that does not exist, has none.
    /// </summary>
    public Task<SecureParentsAnswer> FindSecureParentsAsync(string table, Guid recordId, CancellationToken ct) =>
        ReadSecureParentsAsync(_dataverse, _logger, table, recordId, ct, _recordTypes);

    /// <summary>
    /// The ONE parent walk (task 158 r1c-v2, round 39 item 2: "never a second copy of the parent walk"): the secure matters /
    /// projects a work assignment or project is filed under NOW, and why any other could not be read — the decision
    /// <see cref="FindSecureParentsAsync"/> answers, callable without this scoped service by
    /// <see cref="SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync(string, Guid, Guid, SecureWallRecordScope, CancellationToken)"/>
    /// (which this class depends on, so it cannot depend back). A record that is not a work assignment or project, or that
    /// does not exist, has none. Never throws a read fault: an unreadable record, filing or parent is
    /// <see cref="SecureParentsAnswer.Unverifiable"/>.
    /// </summary>
    /// <param name="maxDepth">Round 61 item 1: how many levels of filing to climb. <c>1</c> (the default) is the record's
    /// DIRECT secure parents — the sharee rule (owner round 11 item 4's intersection is over the records it is filed under,
    /// never their ancestors). The No Access walls ask for <see cref="MaxFilingDepth"/>: a person walled off a secure matter
    /// is walled off every secure record filed below it, at any depth (a work assignment under a project under the matter).
    /// The climb goes level by level through every work assignment or project the record is filed under — secure or not, a
    /// filing is a filing — with a visited set (a cycle ends the climb), and a chain still rising past the bound is
    /// <see cref="SecureParentsAnswer.Unverifiable"/> (fail closed), never "no more parents".</param>
    internal static async Task<SecureParentsAnswer> ReadSecureParentsAsync(
        IGenericEntityService dataverse, ILogger logger, string table, Guid recordId, CancellationToken ct,
        ConcurrentDictionary<Guid, string?>? recordTypes = null, int maxDepth = 1)
    {
        if (!Inherits(table))
            return new SecureParentsAnswer(Array.Empty<SecureFilingParent>(), null);

        // One record: every row and every parent is read on its own (one query each, as before #1410), so a fault on one
        // parent never hides a readable secure sibling (secure-if-any).
        var answers = await ClimbAsync(dataverse, logger, table.Trim().ToLowerInvariant(), new[] { recordId }, batched: false,
            recordTypes ?? new ConcurrentDictionary<Guid, string?>(), maxDepth, ct).ConfigureAwait(false);
        return answers[recordId];
    }

    /// <summary>
    /// GitHub #1410: the ONE parent walk (<see cref="ReadSecureParentsAsync"/>) asked about MANY records of one table at
    /// once — for the Teams/SPA read-time No Access veto, which composes every record a user can reach and must not climb
    /// each one with its own round trips. The same climb and the same decision per record; the reads are batched, and
    /// their faults are CHUNK-granular and fail closed: each level's rows are read per table in chunks of
    /// <see cref="IdsPerQuery"/>, and the flags of the records they are filed under likewise, so a chunk read that faults
    /// leaves every record that needed it <see cref="SecureParentsAnswer.Unverifiable"/>. Every asked id has an answer; a
    /// table whose rows file under nothing (a matter) answers "no parents" for each.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, SecureParentsAnswer>> ReadSecureParentsOfManyAsync(
        IGenericEntityService dataverse, ILogger logger, string table, IReadOnlyCollection<Guid> recordIds,
        CancellationToken ct, ConcurrentDictionary<Guid, string?>? recordTypes = null, int maxDepth = 1)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        var ids = recordIds.Distinct().ToList();
        if (!Inherits(table) || ids.Count == 0)
            return ids.ToDictionary(id => id, _ => new SecureParentsAnswer(Array.Empty<SecureFilingParent>(), null));

        return await ClimbAsync(dataverse, logger, table.Trim().ToLowerInvariant(), ids, batched: true,
            recordTypes ?? new ConcurrentDictionary<Guid, string?>(), maxDepth, ct).ConfigureAwait(false);
    }

    /// <summary>One record's climb: what it has visited, what it reads next, and what it has found.</summary>
    private sealed class ParentClimb((string Table, Guid Id) start)
    {
        public (string Table, Guid Id) Start { get; } = start;

        public HashSet<(string, Guid)> Visited { get; } = new() { start };

        public List<(string Table, Guid Id)> Frontier { get; set; } = new() { start };

        public List<SecureFilingParent> Secure { get; } = new();

        /// <summary>Task 174: the strictest Access Permission met on the way up (null = Standard).</summary>
        public FilingPermission? Strictest { get; set; }

        public string? Unknown { get; set; }
    }

    /// <summary>A row's filing as read: the facts (<c>null</c> = the row does not exist), or a fault.</summary>
    private readonly record struct FactsRead(FilingFacts? Facts, bool Faulted);

    /// <summary>A matter's or project's flag, name and Access Permission (task 174) as read: the row (<c>null</c> = it does
    /// not exist), or a fault.</summary>
    private readonly record struct ParentRead(ParentRow? Row, bool Faulted);

    /// <summary>One matter or project as the parent read returns it.</summary>
    private readonly record struct ParentRow(bool? Flag, string? Name, int? Permission);

    /// <summary>Task 174: a record a row is filed under that exists, with its own flag and Access Permission as read.</summary>
    private readonly record struct FiledParent(string Table, Guid Id, string? Name, bool? Flag, int? Permission);

    /// <summary>
    /// The climb behind <see cref="ReadSecureParentsAsync"/> and <see cref="ReadSecureParentsOfManyAsync"/> (round 61 item
    /// 1): level by level, each record's frontier is read, decided and climbed with its own visited set (a cycle ends the
    /// climb); a row past <paramref name="maxDepth"/> that is still filed under something ends that record's climb
    /// Unverifiable. A row reached by several records' climbs is read and decided once.
    /// </summary>
    /// <param name="batched"><c>false</c>: every row and every parent is read on its own (the one-record shape).
    /// <c>true</c>: read in chunks (#1410).</param>
    private static async Task<Dictionary<Guid, SecureParentsAnswer>> ClimbAsync(
        IGenericEntityService dataverse, ILogger logger, string table, IReadOnlyList<Guid> starts, bool batched,
        ConcurrentDictionary<Guid, string?> recordTypes, int maxDepth, CancellationToken ct)
    {
        var climbs = starts.Select(id => new ParentClimb((table, id))).ToList();
        var facts = new Dictionary<(string Table, Guid Id), FactsRead>();
        var decisions = new Dictionary<(string Table, Guid Id), (SecureParentsAnswer Answer, IReadOnlyList<FiledParent> FiledUnder)>();
        var faultedTypes = new HashSet<Guid>(); // #1410 F3: a pair type that could not be read is not read again in this call
        for (var level = 1; climbs.Any(c => c.Frontier.Count > 0); level++)
        {
            var rows = climbs.SelectMany(c => c.Frontier).Distinct().ToList();
            await ReadFactsIntoAsync(dataverse, logger, rows.Where(r => !facts.ContainsKey(r)).ToList(), facts, batched, ct)
                .ConfigureAwait(false);
            if (level <= maxDepth)
            {
                var undecided = rows
                    .Where(r => !decisions.ContainsKey(r) && facts[r].Facts is not null)
                    .Select(r => facts[r].Facts!)
                    .ToList();
                await DecideParentsIntoAsync(dataverse, logger, undecided, recordTypes, faultedTypes, decisions, batched, ct)
                    .ConfigureAwait(false);
            }

            foreach (var climb in climbs)
            {
                var next = new List<(string Table, Guid Id)>();
                foreach (var row in climb.Frontier)
                {
                    var read = facts[row];
                    if (read.Faulted)
                    {
                        climb.Unknown ??= level == 1
                            ? "the record could not be read"
                            : $"what the {row.Table} it is filed under is itself filed under could not be read";
                        continue;
                    }

                    if (read.Facts is not { } rowFacts)
                        continue; // a record that does not exist confers nothing

                    if (level > maxDepth)
                    {
                        // Past the bound: a row that is still filed under something ends the climb UNDECIDED (fail closed).
                        if (rowFacts.Typed.Values.Any(v => v is not null) || !string.IsNullOrWhiteSpace(rowFacts.PairId))
                            climb.Unknown ??= $"it is filed under a chain of more than {maxDepth} matters or projects, which is not followed further";
                        continue;
                    }

                    var (answer, filedUnder) = decisions[row];
                    climb.Unknown ??= answer.Unverifiable;
                    climb.Strictest = FilingPermission.Stricter(climb.Strictest, answer.StrictestPermission);
                    foreach (var parent in answer.SecureParents)
                    {
                        if (!climb.Secure.Any(s => string.Equals(s.Table, parent.Table, StringComparison.OrdinalIgnoreCase) && s.Id == parent.Id))
                            climb.Secure.Add(parent);
                    }

                    // The direct question (maxDepth 1, the sharee rule) stops here. The walls climb on: only a work assignment or
                    // project is itself filed under something; a matter ends the chain.
                    foreach (var parent in maxDepth > 1 ? filedUnder : Array.Empty<FiledParent>())
                    {
                        if (Inherits(parent.Table) && climb.Visited.Add((parent.Table, parent.Id)))
                            next.Add((parent.Table, parent.Id));
                    }
                }

                climb.Frontier = next;
            }
        }

        // Task 174 (verifier F1-a): a work assignment or project above the record that is not flagged secure itself but sits
        // below a secure ancestor IS secure (round 84), so its No Access list binds what is filed below it too. Decided over
        // the decisions already read (no extra query; a plain reachability walk per question, no memo); only the walls'
        // climb (maxDepth > 1) adds them — the one-level questions (inheritance, the sharee rule) are unchanged.
        var effective = new EffectiveOverDecisions(decisions);
        foreach (var climb in climbs)
        {
            if (maxDepth > 1)
            {
                foreach (var node in effective.Ancestors(climb.Start, climb.Visited))
                {
                    if (node.Flag != true && Inherits(node.Table) && (node.Table, node.Id) != climb.Start && effective.IsSecure(node)
                        && !climb.Secure.Any(x => string.Equals(x.Table, node.Table, StringComparison.OrdinalIgnoreCase) && x.Id == node.Id))
                        climb.Secure.Add(new SecureFilingParent(node.Table, node.Id, node.Name));
                }
            }
        }

        return climbs.ToDictionary(
            c => c.Start.Id,
            c => new SecureParentsAnswer(c.Secure, c.Unknown)
            {
                StrictestPermission = c.Strictest,
                DirectParents = decisions.TryGetValue(c.Start, out var own)
                    ? own.FiledUnder.Select(p => new FilingParentState(
                        new SecureFilingParent(p.Table, p.Id, p.Name), effective.IsSecure(p), effective.Rank(p))).ToList()
                    : Array.Empty<FilingParentState>(),
            });
    }

    /// <summary>
    /// Task 174: the effective Secure flag and Access Permission rank of each record the climb read, over its decisions —
    /// a record's own values folded with every record above it that was decided. A plain reachability walk per question
    /// (no memo, so a filing cycle can never cache a partial "not secure"); the graph above one record is a handful of rows.
    /// A record not decided (a matter, or past the bound — which already makes the answer unverifiable) has its own values.
    /// </summary>
    private sealed class EffectiveOverDecisions(
        IReadOnlyDictionary<(string Table, Guid Id), (SecureParentsAnswer Answer, IReadOnlyList<FiledParent> FiledUnder)> decisions)
    {
        private IReadOnlyList<FiledParent> Above((string Table, Guid Id) key) =>
            Inherits(key.Table) && decisions.TryGetValue(key, out var d) ? d.FiledUnder : Array.Empty<FiledParent>();

        /// <summary><paramref name="node"/> and every record above it, each once.</summary>
        private IEnumerable<FiledParent> SelfAndAbove(FiledParent node)
        {
            var seen = new HashSet<(string, Guid)> { (node.Table, node.Id) };
            var stack = new Stack<FiledParent>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                yield return current;
                foreach (var parent in Above((current.Table, current.Id)))
                {
                    if (seen.Add((parent.Table, parent.Id)))
                        stack.Push(parent);
                }
            }
        }

        public bool IsSecure(FiledParent node) => SelfAndAbove(node).Any(n => n.Flag == true);

        public int Rank(FiledParent node) => SelfAndAbove(node).Max(n => FilingPermission.Rank(n.Permission));

        /// <summary>Every record above <paramref name="start"/> that the climb visited and read (its FiledUnder entries).</summary>
        public IEnumerable<FiledParent> Ancestors((string Table, Guid Id) start, IReadOnlySet<(string, Guid)> visited)
        {
            var seen = new HashSet<(string, Guid)>();
            var queue = new Queue<(string Table, Guid Id)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var row = queue.Dequeue();
                if (!decisions.TryGetValue(row, out var d))
                    continue;
                foreach (var parent in d.FiledUnder)
                {
                    if (!seen.Add((parent.Table, parent.Id)))
                        continue;
                    yield return parent;
                    if (visited.Contains((parent.Table, parent.Id)))
                        queue.Enqueue((parent.Table, parent.Id));
                }
            }
        }
    }

    /// <summary>Reads the filing of every row in <paramref name="rows"/> into <paramref name="into"/>. Never throws a read
    /// fault: a row (or, batched, a chunk) that cannot be read is recorded as faulted.</summary>
    private static async Task ReadFactsIntoAsync(
        IGenericEntityService dataverse, ILogger logger, IReadOnlyList<(string Table, Guid Id)> rows,
        Dictionary<(string Table, Guid Id), FactsRead> into, bool batched, CancellationToken ct)
    {
        foreach (var group in rows.GroupBy(r => r.Table))
        {
            foreach (var chunk in batched ? group.Chunk(IdsPerQuery) : group.Select(r => new[] { r }))
            {
                if (chunk.Length == 1)
                {
                    // One row: the one-record query, as before #1410.
                    var (rowTable, rowId) = chunk[0];
                    try
                    {
                        into[chunk[0]] = new FactsRead(await ReadFactsAsync(dataverse, rowTable, rowId, ct).ConfigureAwait(false), false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "[SECURE-INHERIT] {Table} {RecordId} could not be read.", rowTable, rowId);
                        into[chunk[0]] = new FactsRead(null, true);
                    }

                    continue;
                }

                try
                {
                    var found = await ReadFactsOfManyAsync(dataverse, group.Key, chunk.Select(r => r.Id).ToArray(), ct)
                        .ConfigureAwait(false);
                    foreach (var row in chunk)
                        into[row] = new FactsRead(found.TryGetValue(row.Id, out var f) ? f : null, false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "[SECURE-INHERIT] {Count} {Table} rows could not be read.", chunk.Length, group.Key);
                    foreach (var row in chunk)
                        into[row] = new FactsRead(null, true);
                }
            }
        }
    }

    /// <summary>Decides what each of <paramref name="rows"/> is filed under, into <paramref name="into"/>: the one-record
    /// decision (<see cref="DecideParentsCoreAsync"/>) per row, or — batched — the same decision
    /// (<see cref="DecideNamedParents"/>) over parent flags read in chunks. Never throws a read fault.</summary>
    /// <param name="faultedTypes">Batched only (#1410 F3): pair types whose read faulted earlier in this call; a later row
    /// naming one is "could not be read" without another read (a throttle or fault never becomes N sequential reads).</param>
    private static async Task DecideParentsIntoAsync(
        IGenericEntityService dataverse, ILogger logger, IReadOnlyList<FilingFacts> rows,
        ConcurrentDictionary<Guid, string?> recordTypes, HashSet<Guid> faultedTypes,
        Dictionary<(string Table, Guid Id), (SecureParentsAnswer Answer, IReadOnlyList<FiledParent> FiledUnder)> into,
        bool batched, CancellationToken ct)
    {
        if (!batched)
        {
            foreach (var row in rows)
                into[(row.Table, row.Id)] = await DecideParentsCoreAsync(dataverse, logger, row, recordTypes, ct).ConfigureAwait(false);
            return;
        }

        var named = new List<(FilingFacts Row, IReadOnlyList<(string Table, Guid Id)> Parents, string? Unknown)>();
        foreach (var row in rows)
        {
            var (parents, unknown) = await NameParentsAsync(dataverse, logger, row, recordTypes, ct, faultedTypes).ConfigureAwait(false);
            named.Add((row, parents, unknown));
        }

        var reads = new Dictionary<(string Table, Guid Id), ParentRead>();
        foreach (var group in named.SelectMany(n => n.Parents).Distinct().GroupBy(p => p.Table))
        {
            foreach (var chunk in group.Chunk(IdsPerQuery))
            {
                try
                {
                    var found = await ReadParentsOfManyAsync(dataverse, group.Key, chunk.Select(p => p.Id).ToArray(), ct)
                        .ConfigureAwait(false);
                    foreach (var parent in chunk)
                        reads[parent] = new ParentRead(found.TryGetValue(parent.Id, out var p) ? p : (ParentRow?)null, false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "[SECURE-INHERIT] {Count} {Table} rows could not be read.", chunk.Length, group.Key);
                    foreach (var parent in chunk)
                        reads[parent] = new ParentRead(null, true);
                }
            }
        }

        foreach (var (row, parents, unknown) in named)
            into[(row.Table, row.Id)] = DecideNamedParents(parents, unknown, reads);
    }

    /// <summary>
    /// Round 61 item 1: how far the No Access walls climb (<see cref="ReadSecureParentsAsync"/>) and reach down
    /// (<see cref="ListFiledRootsBelowAsync"/>) through records filed under one another. Small, and enough for every chain
    /// Spaarke creates (a work assignment under a project under a matter is two levels); a longer one fails closed.
    /// </summary>
    internal const int MaxFilingDepth = 4;

    /// <summary>
    /// The work assignments and projects filed under any of <paramref name="parents"/> (typed lookups, and the pair), with
    /// their flag. A pair match counts only when its TYPE names the matched record's table (a pair naming the id of a matter
    /// with an invoice type is not filed under the matter); one whose type cannot be read is returned as an unconfirmed
    /// candidate (<see cref="FiledRootRef.Confirmed"/> false). One page per query; a read that cannot complete THROWS
    /// (nothing is decided on part of it).
    /// </summary>
    public Task<IReadOnlyList<FiledRootRef>> ListFiledRootsAsync(
        IReadOnlyCollection<(string Table, Guid Id)> parents, CancellationToken ct) =>
        ListFiledRootsAsync(_dataverse, _logger, parents, ct, _recordTypes);

    /// <summary>
    /// The ONE child-direction walk (task 158, final round — main-session round 58 item 1): what
    /// <see cref="ListFiledRootsAsync(IReadOnlyCollection{ValueTuple{string, Guid}}, CancellationToken)"/> answers, callable
    /// without this scoped service by task 143's <see cref="NoAccessShareEnforcer"/> (which this class's dependencies reach,
    /// so it cannot depend on this class) — the counterpart of <see cref="ReadSecureParentsAsync"/>. Never a second copy.
    /// </summary>
    /// <summary>
    /// Round 61 item 1, the downward reach: every work assignment and project filed below <paramref name="parents"/>, at any
    /// depth up to <paramref name="maxDepth"/> — <see cref="ListFiledRootsAsync(IGenericEntityService, ILogger, IReadOnlyCollection{ValueTuple{string, Guid}}, CancellationToken, ConcurrentDictionary{Guid, string}?)"/>
    /// (the ONE child-direction listing) asked level by level, each confirmed project found becoming the next level's parent
    /// (a work assignment files nothing under it). A visited set ends a cycle; a level past the bound that still finds a
    /// record not yet reached sets <see cref="FiledRootsWalk.DepthBoundReached"/> (the caller fails closed). A read that
    /// cannot complete throws, as the one-level listing does.
    /// </summary>
    internal static async Task<FiledRootsWalk> ListFiledRootsBelowAsync(
        IGenericEntityService dataverse, ILogger logger, IReadOnlyCollection<(string Table, Guid Id)> parents,
        CancellationToken ct, ConcurrentDictionary<Guid, string?>? recordTypes = null, int maxDepth = MaxFilingDepth)
    {
        ArgumentNullException.ThrowIfNull(parents);
        recordTypes ??= new ConcurrentDictionary<Guid, string?>();
        var visited = parents.Select(p => (p.Table.ToLowerInvariant(), p.Id)).ToHashSet();
        var found = new List<FiledRootRef>();
        IReadOnlyCollection<(string Table, Guid Id)> frontier = parents;
        for (var level = 1; frontier.Count > 0; level++)
        {
            var listed = await ListFiledRootsAsync(dataverse, logger, frontier, ct, recordTypes).ConfigureAwait(false);
            var fresh = listed.Where(r => !visited.Contains((r.Table, r.Id))).ToList();
            if (level > maxDepth)
                return new FiledRootsWalk(found, DepthBoundReached: fresh.Count > 0);

            var next = new List<(string Table, Guid Id)>();
            foreach (var root in fresh)
            {
                visited.Add((root.Table, root.Id));
                found.Add(root);
                if (root.Confirmed && IsParent(root.Table))
                    next.Add((root.Table, root.Id));
            }

            frontier = next;
        }

        return new FiledRootsWalk(found, DepthBoundReached: false);
    }

    internal static async Task<IReadOnlyList<FiledRootRef>> ListFiledRootsAsync(
        IGenericEntityService dataverse, ILogger logger, IReadOnlyCollection<(string Table, Guid Id)> parents,
        CancellationToken ct, ConcurrentDictionary<Guid, string?>? recordTypes = null)
    {
        ArgumentNullException.ThrowIfNull(parents);
        recordTypes ??= new ConcurrentDictionary<Guid, string?>();
        var found = new Dictionary<(string, Guid), FiledRootRef>();

        foreach (var table in new[] { Project, WorkAssignment })
        {
            var nameColumn = table == Project ? "sprk_projectname" : "sprk_name";
            var columns = new[] { nameColumn, IsSecureColumn };

            // Typed lookups.
            foreach (var (column, parentTable) in TypedFilingColumns[table])
            {
                var ids = parents.Where(p => string.Equals(p.Table, parentTable, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Id).Distinct().ToArray();
                foreach (var chunk in ids.Chunk(IdsPerQuery))
                {
                    var query = Query(table, columns);
                    query.Criteria.AddCondition(column, ConditionOperator.In, chunk.Cast<object>().ToArray());
                    foreach (var row in await ReadOnePageAsync(dataverse, query, ct).ConfigureAwait(false))
                        found[(table, row.Id)] = Ref(table, row, nameColumn);
                }
            }

            // The pair: its id is TEXT. Every spelling the decision side accepts (Guid.TryParse of the trimmed text — "D",
            // "B", "P", "N", "X", padded, any case) must be found (task 158 r1), so the query matches the one fragment every
            // spelling carries verbatim — the first 8 hex digits — and each row is then parsed exactly as the decision side
            // parses it and kept only when it names an asked-for id. Dataverse compares text case-insensitively; a GUID has
            // no LIKE wildcard character. Its TYPE decides whether the row is filed under the record with that id.
            var pairColumns = columns.Append(PairIdColumn).Append(PairTypeColumn).ToArray();
            var askedIds = parents.Select(p => p.Id).Distinct().ToArray();
            foreach (var chunk in askedIds.Chunk(PairIdsPerQuery))
            {
                var query = Query(table, pairColumns);
                var spellings = new FilterExpression(LogicalOperator.Or);
                foreach (var id in chunk)
                    spellings.AddCondition(PairIdColumn, ConditionOperator.Like, $"%{id.ToString("N")[..8]}%");
                query.Criteria.AddFilter(spellings);
                foreach (var row in await ReadOnePageAsync(dataverse, query, ct).ConfigureAwait(false))
                {
                    if (found.TryGetValue((table, row.Id), out var typed) && typed.Confirmed)
                        continue; // already filed by a typed lookup

                    var pairId = Guid.TryParse(row.GetAttributeValue<string>(PairIdColumn)?.Trim(), out var parsed) ? parsed : Guid.Empty;
                    if (pairId == Guid.Empty || !chunk.Contains(pairId))
                        continue; // another record's id that shares the fragment, or text that names no record

                    var pairTable = await PairTableOfAsync(dataverse, logger, recordTypes, row, ct).ConfigureAwait(false);
                    if (pairTable is null)
                    {
                        found[(table, row.Id)] = Ref(table, row, nameColumn) with { Confirmed = false };
                    }
                    else if (parents.Any(p => p.Id == pairId && string.Equals(p.Table, pairTable, StringComparison.OrdinalIgnoreCase)))
                    {
                        found[(table, row.Id)] = Ref(table, row, nameColumn);
                    }
                }
            }
        }

        return found.Values.OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();

        static FiledRootRef Ref(string table, Entity row, string nameColumn) =>
            new(table, row.Id, row.GetAttributeValue<string>(nameColumn), row.GetAttributeValue<bool?>(IsSecureColumn) == true);
    }

    /// <summary>The table a listed row's pair type names, or <c>null</c> when it has none or it cannot be read.</summary>
    private static async Task<string?> PairTableOfAsync(
        IGenericEntityService dataverse, ILogger logger, ConcurrentDictionary<Guid, string?> recordTypes, Entity row,
        CancellationToken ct)
    {
        if (row.GetAttributeValue<EntityReference>(PairTypeColumn)?.Id is not { } typeRef || typeRef == Guid.Empty)
            return null;

        try
        {
            return await ReadRecordTypeAsync(dataverse, recordTypes, typeRef, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[SECURE-INHERIT] The regarding type {TypeRef} of {Table} {RecordId} could not be read.",
                typeRef, row.LogicalName, row.Id);
            return null;
        }
    }

    // ── Provisioning (the endpoint's own steps) ──────────────────────────────────────────────────────────────────────

    private async Task<SecureRootInheritResult> ProvisionAsync(string table, Guid recordId, string traceId, CancellationToken ct)
    {
        var root = SecureRecordRoot.For(table == Project ? ExternalGrantRootType.Project : ExternalGrantRootType.WorkAssignment);
        IResult result;
        NestedProvisioning.Value++;
        try
        {
            result = await ProvisionProjectEndpoint.ProvisionInheritedAsync(
                root, recordId, traceId, _webApi, _speFileStore, _recordShare, _secureChildren, this, _configuration,
                _noAccessGuard, _accessCacheInvalidator, _fileRelocator, _logger, ct).ConfigureAwait(false);
        }
        finally
        {
            NestedProvisioning.Value--;
        }

        switch (result)
        {
            case Ok<ProvisionProjectResponse>:
                _logger.LogInformation(
                    "[SECURE-INHERIT] {Table} {RecordId} is filed under a secure record and is now secure (provisioned for its " +
                    "creator). TraceId={TraceId}", table, recordId, traceId);
                return Result(table, recordId, SecureRootInheritOutcome.Secured, null, null);

            case ProblemHttpResult problem:
                var code = problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var value) ? value?.ToString() : null;
                if (problem.StatusCode == StatusCodes.Status409Conflict
                    && string.Equals(code, ProvisionProjectEndpoint.ReasonAlreadyProvisioned, StringComparison.Ordinal))
                {
                    return Result(table, recordId, SecureRootInheritOutcome.AlreadySecure, null, null);
                }

                var outcome = problem.StatusCode is >= 400 and < 500
                    ? SecureRootInheritOutcome.Refused
                    : SecureRootInheritOutcome.Failed;
                _logger.LogWarning(
                    "[SECURE-INHERIT] {Table} {RecordId} is filed under a secure record, but provisioning it answered {Status} " +
                    "{Code}: {Detail}. TraceId={TraceId}", table, recordId, problem.StatusCode, code, problem.ProblemDetails.Detail,
                    traceId);
                return Result(table, recordId, outcome, code ?? ReasonUnexpectedResult, problem.ProblemDetails.Detail);

            default:
                _logger.LogError(
                    "[SECURE-INHERIT] Provisioning {Table} {RecordId} answered an unexpected result ({Type}). TraceId={TraceId}",
                    table, recordId, result.GetType().Name, traceId);
                return Result(table, recordId, SecureRootInheritOutcome.Failed, ReasonUnexpectedResult,
                    "provisioning answered an unexpected result");
        }
    }

    private async Task<bool> IsIsolatedAsync(FilingFacts facts, CancellationToken ct)
    {
        if (facts.IsSecure != true || facts.OwningTeam is null || string.IsNullOrWhiteSpace(facts.ContainerId))
            return false;

        var team = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(_dataverse, _configuration, ct).ConfigureAwait(false);
        return team.TeamId is { } secureTeam && facts.OwningTeam == secureTeam;
    }

    // ── Reads ────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>What a row says it is filed under, and its own isolation facts.</summary>
    private sealed record FilingFacts(
        string Table,
        Guid Id,
        bool? IsSecure,
        Guid? OwningTeam,
        string? ContainerId,
        IReadOnlyDictionary<string, Guid?> Typed,
        string? PairId,
        Guid? PairType);

    /// <summary>The row's filing, or <c>null</c> when it does not exist. A fault propagates.</summary>
    private Task<FilingFacts?> ReadFactsAsync(string table, Guid id, CancellationToken ct) => ReadFactsAsync(_dataverse, table, id, ct);

    private static async Task<FilingFacts?> ReadFactsAsync(IGenericEntityService dataverse, string table, Guid id, CancellationToken ct)
    {
        var typed = TypedFilingColumns[table];
        var query = Query(table,
            typed.Select(t => t.Column).Concat(new[] { IsSecureColumn, OwningTeamColumn, ContainerColumn, PairIdColumn, PairTypeColumn })
                .ToArray());
        query.TopCount = 1;
        query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
        var row = (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        return row is null ? null : ToFacts(table, id, row);
    }

    /// <summary>#1410: the filing of every row of <paramref name="table"/> in <paramref name="ids"/> (at most
    /// <see cref="IdsPerQuery"/>) in one query; a row that does not exist is absent. A fault propagates.</summary>
    private static async Task<IReadOnlyDictionary<Guid, FilingFacts>> ReadFactsOfManyAsync(
        IGenericEntityService dataverse, string table, Guid[] ids, CancellationToken ct)
    {
        var query = Query(table,
            TypedFilingColumns[table].Select(t => t.Column)
                .Concat(new[] { IsSecureColumn, OwningTeamColumn, ContainerColumn, PairIdColumn, PairTypeColumn })
                .ToArray());
        query.Criteria.AddCondition(table + "id", ConditionOperator.In, ids.Cast<object>().ToArray());
        var found = new Dictionary<Guid, FilingFacts>();
        foreach (var row in await ReadOnePageAsync(dataverse, query, ct).ConfigureAwait(false))
        {
            if (ids.Contains(row.Id))
                found[row.Id] = ToFacts(table, row.Id, row);
        }

        return found;
    }

    private static FilingFacts ToFacts(string table, Guid id, Entity row)
    {
        var typed = TypedFilingColumns[table];
        return new FilingFacts(
            table,
            id,
            row.GetAttributeValue<bool?>(IsSecureColumn),
            row.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } team && team != Guid.Empty ? team : null,
            row.GetAttributeValue<string>(ContainerColumn),
            typed.ToDictionary(
                t => t.Column,
                t => row.GetAttributeValue<EntityReference>(t.Column)?.Id is { } p && p != Guid.Empty ? p : (Guid?)null,
                StringComparer.OrdinalIgnoreCase),
            row.GetAttributeValue<string>(PairIdColumn),
            row.GetAttributeValue<EntityReference>(PairTypeColumn)?.Id is { } type && type != Guid.Empty ? type : null);
    }

    /// <summary>
    /// The secure matters / projects <paramref name="facts"/> names, and why any other could not be read. Typed lookups
    /// first; then the pair (agreement by identity with a typed lookup adds nothing; otherwise its type decides — a matter or
    /// project is a parent, any other type is not this rule). Every named record is read even after one fails, so a readable
    /// secure parent is never hidden behind an unreadable sibling (secure-if-any).
    /// </summary>
    private Task<SecureParentsAnswer> DecideParentsAsync(FilingFacts facts, CancellationToken ct) =>
        DecideParentsAsync(_dataverse, _logger, facts, _recordTypes, ct);

    private static async Task<SecureParentsAnswer> DecideParentsAsync(
        IGenericEntityService dataverse, ILogger logger, FilingFacts facts, ConcurrentDictionary<Guid, string?> recordTypes,
        CancellationToken ct) =>
        (await DecideParentsCoreAsync(dataverse, logger, facts, recordTypes, ct).ConfigureAwait(false)).Answer;

    /// <summary>The direct decision, and every record the row is filed under that exists (secure or not) — what the
    /// level-by-level climb (round 61 item 1) continues from.</summary>
    private static async Task<(SecureParentsAnswer Answer, IReadOnlyList<FiledParent> FiledUnder)> DecideParentsCoreAsync(
        IGenericEntityService dataverse, ILogger logger, FilingFacts facts, ConcurrentDictionary<Guid, string?> recordTypes,
        CancellationToken ct)
    {
        var (named, unknown) = await NameParentsAsync(dataverse, logger, facts, recordTypes, ct).ConfigureAwait(false);

        // Every named record is read on its own, even after one fails (secure-if-any).
        var reads = new Dictionary<(string Table, Guid Id), ParentRead>();
        foreach (var (table, id) in named.Distinct())
        {
            try
            {
                reads[(table, id)] = new ParentRead(await ReadParentAsync(dataverse, table, id, ct).ConfigureAwait(false), false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "[SECURE-INHERIT] {Table} {Id} could not be read.", table, id);
                reads[(table, id)] = new ParentRead(null, true);
            }
        }

        return DecideNamedParents(named, unknown, reads);
    }

    /// <summary>The records <paramref name="facts"/> names as what it is filed under (typed lookups, then the pair), and why
    /// any could not be named. Reads only the pair's TYPE (cached in <paramref name="recordTypes"/>).</summary>
    /// <param name="faultedTypes">When given (the batched walk, #1410 F3), types whose read already faulted in this call are
    /// answered "could not be read" without reading again, and a new fault is added. <c>null</c>: every row reads (the
    /// one-record shape, as before).</param>
    private static async Task<(IReadOnlyList<(string Table, Guid Id)> Named, string? Unknown)> NameParentsAsync(
        IGenericEntityService dataverse, ILogger logger, FilingFacts facts, ConcurrentDictionary<Guid, string?> recordTypes,
        CancellationToken ct, HashSet<Guid>? faultedTypes = null)
    {
        var named = new List<(string Table, Guid Id)>();
        string? unknown = null;
        foreach (var (column, parentTable) in TypedFilingColumns[facts.Table])
        {
            if (facts.Typed.TryGetValue(column, out var id) && id is { } parentId)
                named.Add((parentTable, parentId));
        }

        if (!string.IsNullOrWhiteSpace(facts.PairId))
        {
            if (!Guid.TryParse(facts.PairId.Trim(), out var pairId) || pairId == Guid.Empty)
            {
                unknown ??= $"its {PairIdColumn} does not hold a record identifier";
            }
            else if (!named.Any(n => n.Id == pairId))
            {
                if (facts.PairType is not { } typeRef)
                {
                    unknown ??= $"it names a record in {PairIdColumn} without that record's type";
                }
                else if (faultedTypes is not null && faultedTypes.Contains(typeRef))
                {
                    unknown ??= "the type of the record it is filed under could not be read";
                }
                else
                {
                    try
                    {
                        var pairTable = await ReadRecordTypeAsync(dataverse, recordTypes, typeRef, ct).ConfigureAwait(false);
                        if (pairTable is null)
                            unknown ??= "the type of the record it is filed under could not be determined";
                        else if (IsParent(pairTable))
                            named.Add((pairTable, pairId));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "[SECURE-INHERIT] The regarding type {TypeRef} could not be read.", typeRef);
                        faultedTypes?.Add(typeRef);
                        unknown ??= "the type of the record it is filed under could not be read";
                    }
                }
            }
        }

        return (named, unknown);
    }

    /// <summary>The direct decision over <paramref name="named"/> as <paramref name="reads"/> read them (in order; the first
    /// reason wins): a faulted read or an EMPTY flag is unverifiable, a missing record confers nothing, a true flag is a
    /// secure parent. Also every named record that exists (secure or not) — what the climb continues from.</summary>
    private static (SecureParentsAnswer Answer, IReadOnlyList<FiledParent> FiledUnder) DecideNamedParents(
        IReadOnlyList<(string Table, Guid Id)> named, string? unknown,
        IReadOnlyDictionary<(string Table, Guid Id), ParentRead> reads)
    {
        var secure = new List<SecureFilingParent>();
        var existing = new List<FiledParent>();
        FilingPermission? strictest = null;
        foreach (var (table, id) in named.Distinct())
        {
            var read = reads[(table, id)];
            if (read.Faulted)
            {
                unknown ??= $"whether the {table} it is filed under is secure could not be read";
                continue;
            }

            if (read.Row is not { } parent)
                continue; // a record that does not exist confers nothing
            existing.Add(new FiledParent(table, id, parent.Name, parent.Flag, parent.Permission));

            // Task 174 (owner round 84): the parent's Access Permission, from the same row (a null value is Standard).
            if (FilingPermission.Rank(parent.Permission) > 0)
            {
                strictest = FilingPermission.Stricter(strictest,
                    new FilingPermission(parent.Permission!.Value, new SecureFilingParent(table, id, parent.Name)));
            }

            if (parent.Flag is null)
            {
                unknown ??= $"the {table} it is filed under has no secure flag value (empty is never read as not secure)";
                continue;
            }

            if (parent.Flag == true)
                secure.Add(new SecureFilingParent(table, id, parent.Name));
        }

        return (new SecureParentsAnswer(secure, unknown) { StrictestPermission = strictest }, existing);
    }

    /// <summary>A matter's or project's flag, name and Access Permission, or <c>null</c> when it does not exist.</summary>
    private static async Task<ParentRow?> ReadParentAsync(
        IGenericEntityService dataverse, string table, Guid id, CancellationToken ct)
    {
        var nameColumn = NameColumnOf(table);
        var query = Query(table, new[] { IsSecureColumn, nameColumn, AccessPermissionColumn });
        query.TopCount = 1;
        query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
        var row = (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        return row is null ? null : ToParentRow(row, nameColumn);
    }

    /// <summary>#1410: the flag, name and Access Permission of every row of <paramref name="table"/> in <paramref name="ids"/>
    /// (at most <see cref="IdsPerQuery"/>) in one query — one id is the one-record query; a row that does not exist is absent.
    /// A fault propagates.</summary>
    private static async Task<IReadOnlyDictionary<Guid, ParentRow>> ReadParentsOfManyAsync(
        IGenericEntityService dataverse, string table, Guid[] ids, CancellationToken ct)
    {
        var found = new Dictionary<Guid, ParentRow>();
        if (ids.Length == 1)
        {
            if (await ReadParentAsync(dataverse, table, ids[0], ct).ConfigureAwait(false) is { } one)
                found[ids[0]] = one;
            return found;
        }

        var nameColumn = NameColumnOf(table);
        var query = Query(table, new[] { IsSecureColumn, nameColumn, AccessPermissionColumn });
        query.Criteria.AddCondition(table + "id", ConditionOperator.In, ids.Cast<object>().ToArray());
        foreach (var row in await ReadOnePageAsync(dataverse, query, ct).ConfigureAwait(false))
        {
            if (ids.Contains(row.Id))
                found[row.Id] = ToParentRow(row, nameColumn);
        }

        return found;
    }

    private static string NameColumnOf(string table) =>
        string.Equals(table, Matter, StringComparison.OrdinalIgnoreCase) ? "sprk_mattername" : "sprk_projectname";

    /// <summary>Task 174: <c>sprk_accesspermission</c> is a choice column; the SDK returns an <see cref="OptionSetValue"/>.
    /// Absent or empty is Standard (the flag read's rule).</summary>
    private static ParentRow ToParentRow(Entity row, string nameColumn) => new(
        row.GetAttributeValue<bool?>(IsSecureColumn),
        row.GetAttributeValue<string>(nameColumn),
        row.Attributes.TryGetValue(AccessPermissionColumn, out var permission)
            ? permission switch { OptionSetValue o => o.Value, int i => i, _ => null }
            : null);

    private readonly ConcurrentDictionary<Guid, string?> _recordTypes = new();

    /// <summary>The logical name a <c>sprk_recordtype_ref</c> row stands for, or <c>null</c> when it has none / is gone.</summary>
    private static async Task<string?> ReadRecordTypeAsync(
        IGenericEntityService dataverse, ConcurrentDictionary<Guid, string?> recordTypes, Guid typeRef, CancellationToken ct)
    {
        if (recordTypes.TryGetValue(typeRef, out var cached))
            return cached;

        var query = Query(RecordTypeRefEntity, new[] { RecordTypeLogicalNameColumn });
        query.TopCount = 1;
        query.Criteria.AddCondition(RecordTypeRefEntity + "id", ConditionOperator.Equal, typeRef);
        var row = (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        var logical = row?.GetAttributeValue<string>(RecordTypeLogicalNameColumn)?.Trim().ToLowerInvariant();
        var answer = string.IsNullOrWhiteSpace(logical) ? null : logical;
        recordTypes[typeRef] = answer;
        return answer;
    }

    private static async Task<IReadOnlyList<Entity>> ReadOnePageAsync(
        IGenericEntityService dataverse, QueryExpression query, CancellationToken ct)
    {
        query.PageInfo = new PagingInfo { Count = FiledPageSize, PageNumber = 1 };
        var result = await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (result.MoreRecords)
        {
            throw new InvalidOperationException(
                $"More than {FiledPageSize} {query.EntityName} rows are filed under the records asked about; nothing is decided " +
                "on part of them.");
        }

        return result.Entities;
    }

    private static QueryExpression Query(string table, string[] columns) =>
        new(table) { ColumnSet = new ColumnSet(columns), NoLock = true };

    // ── Writes as the writers spell them ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A write's value for one filing column, normalized: a parent id, a clear, or (the pair) a text id / type.</summary>
    private sealed record FilingWrite(string Column, Guid? Id, string? Text, bool IsClear);

    private sealed class FilingValueException(string message) : Exception(message);

    /// <summary>The writes that touch what a row of <paramref name="table"/> is filed under.</summary>
    private static List<FilingWrite> FilingWritesIn(string table, IEnumerable<KeyValuePair<string, object?>> writes)
    {
        var filing = FilingColumnsOf(table);
        var found = new List<FilingWrite>();
        foreach (var (key, written) in writes)
        {
            var column = NormalizeColumn(key);
            if (!filing.Contains(column))
                continue;

            var value = written;
            if (value is System.Text.Json.JsonElement json)
            {
                // A JSON value (a payload read back from the Web API): a string is read like a string, null is a clear.
                value = json.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null,
                    System.Text.Json.JsonValueKind.String => json.GetString(),
                    _ => json,
                };
            }

            if (value is null or DBNull)
            {
                found.Add(new FilingWrite(column, null, null, IsClear: true));
                continue;
            }

            if (string.Equals(column, PairIdColumn, StringComparison.OrdinalIgnoreCase))
            {
                var text = value switch
                {
                    string s => s,
                    Guid g => g.ToString("D"),
                    _ => throw new FilingValueException($"the value written to {PairIdColumn} could not be interpreted"),
                };
                found.Add(string.IsNullOrWhiteSpace(text)
                    ? new FilingWrite(column, null, null, IsClear: true)
                    : new FilingWrite(column, null, text, IsClear: false));
                continue;
            }

            var id = value switch
            {
                EntityReference reference => reference.Id,
                Guid g => g,
                string s when TryParseBindOrGuid(s, out var parsed) => parsed,
                _ => throw new FilingValueException($"the value written to {column} could not be interpreted"),
            };
            found.Add(id == Guid.Empty
                ? new FilingWrite(column, null, null, IsClear: true)
                : new FilingWrite(column, id, null, IsClear: false));
        }

        return found;
    }

    /// <summary>The row as the write will leave it: <paramref name="before"/> (or an empty row for a create) with the writes.</summary>
    private static FilingFacts Overlay(string table, FilingFacts? before, IReadOnlyList<FilingWrite> writes)
    {
        var logical = table.Trim().ToLowerInvariant();
        var typed = new Dictionary<string, Guid?>(
            before?.Typed ?? TypedFilingColumns[logical].ToDictionary(t => t.Column, _ => (Guid?)null),
            StringComparer.OrdinalIgnoreCase);
        var pairId = before?.PairId;
        var pairType = before?.PairType;

        foreach (var write in writes)
        {
            if (string.Equals(write.Column, PairIdColumn, StringComparison.OrdinalIgnoreCase))
                pairId = write.IsClear ? null : write.Text;
            else if (string.Equals(write.Column, PairTypeColumn, StringComparison.OrdinalIgnoreCase))
                pairType = write.IsClear ? null : write.Id;
            else
                typed[write.Column] = write.IsClear ? null : write.Id;
        }

        return new FilingFacts(logical, before?.Id ?? Guid.Empty, before?.IsSecure, before?.OwningTeam, before?.ContainerId,
            typed, pairId, pairType);
    }

    /// <summary>
    /// A write key's logical column: <c>sprk_RegardingMatter@odata.bind</c> → <c>sprk_regardingmatter</c>,
    /// <c>_sprk_regardingmatter_value</c> → <c>sprk_regardingmatter</c>, any case → lower.
    /// </summary>
    internal static string NormalizeColumn(string key)
    {
        var name = (key ?? string.Empty).Trim();
        var at = name.IndexOf('@');
        if (at >= 0)
            name = name[..at];
        if (name.StartsWith('_') && name.EndsWith("_value", StringComparison.OrdinalIgnoreCase))
            name = name[1..^"_value".Length];
        return name.ToLowerInvariant();
    }

    /// <summary>A bind path (<c>/sprk_matters(…)</c> or <c>sprk_matters(…)</c>) or a bare GUID.</summary>
    private static bool TryParseBindOrGuid(string value, out Guid id)
    {
        var text = value.Trim();
        var open = text.LastIndexOf('(');
        var close = text.LastIndexOf(')');
        if (open >= 0 && close > open)
            text = text[(open + 1)..close];
        return Guid.TryParse(text, out id);
    }

    private RecordOwnerResolution Refused(string table, string why)
    {
        _logger.LogWarning("[SECURE-INHERIT] Refusing a create / re-file of a {Table}: {Why}. Nothing was written.", table, why);
        return RecordOwnerResolution.Refused(
            RecordOwnerRefusal.ParentUndetermined,
            $"whether the matter or project this {(table == Project ? "project" : "work assignment")} would be filed under is " +
            $"secure could not be determined — {why} — so it was not written (a record filed under a secure record must be " +
            "secured itself)");
    }

    private static SecureRootInheritResult Result(
        string table, Guid id, SecureRootInheritOutcome outcome, string? code, string? detail,
        IReadOnlyList<SecureFilingParent>? parents = null) =>
        new(table, id, outcome, code, detail, parents ?? Array.Empty<SecureFilingParent>(), null);
}
