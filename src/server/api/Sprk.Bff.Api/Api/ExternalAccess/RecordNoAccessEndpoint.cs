using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// <c>GET /api/v1/records/{sprk_project|sprk_matter|sprk_workassignment}/{recordId}/no-access</c> — the ONE per-record
/// No Access read (unified-access-control-r2 task 064, narrowed by owner round 59 item 3). It answers, for one record:
/// is it Secure, does a No Access restriction cover it, and — for a caller who can manage its access — which entries.
/// </summary>
/// <remarks>
/// <para><b>Who reads what</b> (owner O2, 2026-10-01). Every caller who holds Read on the record gets the two signals,
/// <c>secure</c> and <c>noAccess</c>, each <c>applies</c> / <c>doesNotApply</c> / <c>unknown</c> (task 153's banner). A
/// caller who ALSO holds Write gets the covering entries (task 067's read-only list in Manage Access): the BFF reads the
/// table on their behalf, because under O2 only the access-administrator role reads it directly. Nobody gets an entry's
/// Reason (task 143: a refusal never reveals it).</para>
///
/// <para><b>The gate.</b> <see cref="RecordRouteAccessAuthorizationFilter"/>'s fixed-entity-set form with the existing
/// <c>read</c> operation key: the caller's rights on the record, asked of Dataverse AS THE CALLER over OBO
/// (<see cref="CallerRecordAccessProbe"/>), before this handler runs. No Read — including no bearer token, a record that
/// does not exist and any probe fault — is the uniform 404, so the route is not an existence oracle. The Write decision
/// reuses the rights that same probe call returned (published by the filter); no second probe and no app-only read stands
/// in for the caller's gate. Not on the <c>/api/v1/external-access</c> group: its <see cref="DelegationRuleFilter"/>
/// demands Write for every route, and the banner must answer Read-only callers.</para>
///
/// <para><b>What "covers" means</b> is <see cref="NoAccessShareEnforcer.ReadCoverageAsync"/>, the lookup the enforcer's
/// "Update Access" path uses — one source of truth: an active entry whose object is the record, or an organization the
/// record references in ANY org-typed lookup (B-10), and the same for every secure record it is filed under (round 61).
/// An entry counts toward <c>noAccess</c> only when it is in force on this record (<see cref="InForce"/>): well-formed
/// (<see cref="NoAccessShareEnforcer.TryClassify"/>, the enforcer's rule), and — for a USER wall — on a Secure record or
/// through a secure parent (owner Q4: a user wall binds only Secure records). The Write tier lists every active entry,
/// with <c>inForce</c> and the reason when it is not, so a malformed or inert entry can be seen and fixed.</para>
///
/// <para><b>Fails closed, visibly</b> (NFR-01, ADR-003). A signal that cannot be read is <c>unknown</c>, never
/// <c>doesNotApply</c>: an unreadable or masked <c>sprk_issecure</c> (<see cref="RootRecordFlags.IsUnreadable"/>); an
/// unreadable referenced-organization set, filing or entry. The entries are then <c>unavailable</c> (null), never an
/// empty list. More entries than one read lists is <c>truncated</c>, never a silent prefix.</para>
///
/// <para><b>Computed per request</b>; nothing is stored or cached here.</para>
/// </remarks>
public static class RecordNoAccessEndpoint
{
    /// <summary>The <see cref="Spaarke.Core.Auth.OperationAccessPolicy"/> key the gate asks: Read (reused, not added).</summary>
    internal const string ReadOperation = "read";

    /// <summary>The route value carrying the record id.</summary>
    internal const string RecordIdRouteKey = "recordId";

    internal const string ProjectRoute = "/sprk_project/{recordId:guid}/no-access";
    internal const string MatterRoute = "/sprk_matter/{recordId:guid}/no-access";
    internal const string WorkAssignmentRoute = "/sprk_workassignment/{recordId:guid}/no-access";

    private const string Project = "sprk_project";
    private const string Matter = "sprk_matter";
    private const string WorkAssignment = "sprk_workassignment";
    private const string ProjectSet = "sprk_projects";
    private const string MatterSet = "sprk_matters";
    private const string WorkAssignmentSet = "sprk_workassignments";

    /// <summary>Registers the three routes (one per root type; the entity set is a constant of each route).</summary>
    public static void MapRecordNoAccessEndpoint(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/records")
            .WithTags("Record Access")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        group.MapGet(ProjectRoute, GetProjectAsync)
            .AddRecordRouteAccessAuthorizationFilter(ReadOperation, ProjectSet, RecordIdRouteKey)
            .WithName("GetProjectNoAccessStatus")
            .Described("project");
        group.MapGet(MatterRoute, GetMatterAsync)
            .AddRecordRouteAccessAuthorizationFilter(ReadOperation, MatterSet, RecordIdRouteKey)
            .WithName("GetMatterNoAccessStatus")
            .Described("matter");
        group.MapGet(WorkAssignmentRoute, GetWorkAssignmentAsync)
            .AddRecordRouteAccessAuthorizationFilter(ReadOperation, WorkAssignmentSet, RecordIdRouteKey)
            .WithName("GetWorkAssignmentNoAccessStatus")
            .Described("work assignment");
    }

    private static RouteHandlerBuilder Described(this RouteHandlerBuilder route, string type) => route
        .WithSummary($"Whether this {type} is Secure and under a No Access restriction, and (with Write) which entries")
        .WithDescription(
            $"For a caller who holds Read on the {type}: 'secure' and 'noAccess', each applies / doesNotApply / unknown " +
            "(unknown when it could not be read, never doesNotApply). For a caller who also holds Write: the active No Access " +
            "entries covering it (its own, walls over organizations it references, and those of secure records it is filed " +
            "under), never their Reason. A caller without Read, and a record that does not exist, get the same 404.")
        .Produces<RecordNoAccessStatus>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

    internal static Task<IResult> GetProjectAsync(
        Guid recordId, NoAccessShareEnforcer enforcer, NoAccessEnforcementStore store,
        ExternalParticipationService participations, HttpContext httpContext, ILogger<Program> logger, CancellationToken ct)
        => HandleAsync(Project, ProjectSet, recordId, enforcer, store, participations, httpContext, logger, ct);

    internal static Task<IResult> GetMatterAsync(
        Guid recordId, NoAccessShareEnforcer enforcer, NoAccessEnforcementStore store,
        ExternalParticipationService participations, HttpContext httpContext, ILogger<Program> logger, CancellationToken ct)
        => HandleAsync(Matter, MatterSet, recordId, enforcer, store, participations, httpContext, logger, ct);

    internal static Task<IResult> GetWorkAssignmentAsync(
        Guid recordId, NoAccessShareEnforcer enforcer, NoAccessEnforcementStore store,
        ExternalParticipationService participations, HttpContext httpContext, ILogger<Program> logger, CancellationToken ct)
        => HandleAsync(WorkAssignment, WorkAssignmentSet, recordId, enforcer, store, participations, httpContext, logger, ct);

    /// <summary>The shared handler. Runs only after the filter allowed Read on (<paramref name="entitySet"/>, <paramref name="recordId"/>).</summary>
    internal static async Task<IResult> HandleAsync(
        string logicalName,
        string entitySet,
        Guid recordId,
        NoAccessShareEnforcer enforcer,
        NoAccessEnforcementStore store,
        ExternalParticipationService participations,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        // The filter's own answer for THIS record. Absent (the filter did not run for it) is treated as no rights — the
        // same uniform 404 as a caller without Read — never as an app-only read in the caller's place.
        if (!RecordRouteAccessAuthorizationFilter.TryGetAuthorizedRights(httpContext, entitySet, recordId, out var rights)
            || (rights & AccessRights.Read) != AccessRights.Read)
        {
            logger.LogWarning(
                "[NO-ACCESS-READ] {Type} {RecordId}: no caller-rights decision for this record reached the handler; refused.",
                logicalName, recordId);
            return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
        }

        var canSeeEntries = (rights & AccessRights.Write) == AccessRights.Write;
        var secure = await ReadSecureAsync(logicalName, recordId, participations, logger, ct).ConfigureAwait(false);
        var (noAccess, entriesState, entries) =
            await ReadNoAccessAsync(logicalName, recordId, secure, canSeeEntries, enforcer, store, logger, ct).ConfigureAwait(false);

        logger.LogInformation(
            "[NO-ACCESS-READ] {Type} {RecordId}: secure {Secure}, no access {NoAccess}, entries {EntriesState} ({Count}).",
            logicalName, recordId, secure, noAccess, entriesState, entries?.Count ?? 0);

        return TypedResults.Ok(new RecordNoAccessStatus(logicalName, recordId, secure, noAccess, entriesState, entries));
    }

    /// <summary>
    /// The Secure signal from the one batched flag read every veto uses. An unreadable, unreturned or EMPTY
    /// <c>sprk_issecure</c> comes back <see cref="RootRecordFlags.IsUnreadable"/> (task 150: an empty value means the
    /// field-level Read was lost and a true value may be masked), which is <c>unknown</c> here — never "not secure".
    /// </summary>
    private static async Task<string> ReadSecureAsync(
        string logicalName, Guid recordId, ExternalParticipationService participations, ILogger logger, CancellationToken ct)
    {
        try
        {
            var flags = await participations.GetRootRecordFlagsAsync(logicalName, new[] { recordId }, ct).ConfigureAwait(false);
            if (!flags.TryGetValue(recordId, out var f) || f.IsUnreadable)
            {
                return AccessSignalState.Unknown;
            }

            return f.IsSecure ? AccessSignalState.Applies : AccessSignalState.DoesNotApply;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "[NO-ACCESS-READ] Whether {Type} {RecordId} is secure could not be read.", logicalName, recordId);
            return AccessSignalState.Unknown;
        }
    }

    /// <summary>
    /// The No Access signal and (for a Write caller) the entries. Every covering entry is read, for every caller: whether
    /// the restriction applies depends on each entry being well-formed, still active and in force on this record
    /// (<see cref="InForce"/>), so a Read-only caller's answer is computed from the same rows a Write caller is shown.
    /// </summary>
    private static async Task<(string NoAccess, string EntriesState, IReadOnlyList<RecordNoAccessEntry>? Entries)> ReadNoAccessAsync(
        string logicalName,
        Guid recordId,
        string secure,
        bool canSeeEntries,
        NoAccessShareEnforcer enforcer,
        NoAccessEnforcementStore store,
        ILogger logger,
        CancellationToken ct)
    {
        var unavailable = (AccessSignalState.Unknown, canSeeEntries ? NoAccessEntriesState.Unavailable : NoAccessEntriesState.NotShown,
            (IReadOnlyList<RecordNoAccessEntry>?)null);

        var coverage = await enforcer.ReadCoverageAsync(logicalName, recordId, ct).ConfigureAwait(false);
        if (!coverage.IsReadable)
        {
            return unavailable;
        }

        // One cap for the whole answer: the record's own entries and its secure parents' together.
        var covering = coverage.Entries.Take(NoAccessShareEnforcer.MaxEntriesPerRecord).ToList();
        var truncated = coverage.Truncated || coverage.Entries.Count > covering.Count;

        var rows = new List<RecordNoAccessEntry>();
        var anyInForce = false;
        var anyUndecided = false;
        foreach (var cover in covering)
        {
            NoAccessEntrySnapshot? entry;
            try
            {
                entry = await store.ReadEntryAsync(cover.EntryId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "[NO-ACCESS-READ] Entry {EntryId} covering {Type} {RecordId} could not be read.",
                    cover.EntryId, logicalName, recordId);
                return unavailable;
            }

            // Deleted or deactivated since the covering query ran: it no longer walls anyone off.
            if (entry is null || !entry.IsActive)
            {
                continue;
            }

            var wellFormed = NoAccessShareEnforcer.TryClassify(entry.Row, out var subjectKind, out var isOrgObject);
            var viaSecureParent = !IsThisRecord(cover, logicalName, recordId);
            // ANY path through a secure parent binds a user wall (round 61), including an entry also found directly.
            var (inForce, notInForceReason) =
                InForce(wellFormed, subjectKind, viaSecureParent || cover.AlsoViaSecureParent, secure);
            anyInForce |= inForce == true;
            anyUndecided |= inForce is null;
            if (canSeeEntries)
            {
                rows.Add(ToView(entry, cover, viaSecureParent, wellFormed, subjectKind, isOrgObject, inForce, notInForceReason));
            }
        }

        // An entry whose force hangs on an unread Secure flag, or a prefix with nothing in force, cannot prove the
        // record is clear.
        var noAccess = anyInForce ? AccessSignalState.Applies
            : anyUndecided || truncated ? AccessSignalState.Unknown
            : AccessSignalState.DoesNotApply;

        if (!canSeeEntries)
        {
            return (noAccess, NoAccessEntriesState.NotShown, null);
        }

        return (noAccess, truncated ? NoAccessEntriesState.Truncated : NoAccessEntriesState.Complete, rows);
    }

    private static bool IsThisRecord(NoAccessCoveringEntry cover, string logicalName, Guid recordId)
        => cover.CoveredRecordId == recordId && string.Equals(cover.CoveredRecordType, logicalName, StringComparison.Ordinal);

    /// <summary>
    /// Whether one active entry walls anyone off THIS record: <c>true</c>, <c>false</c> with the reason, or <c>null</c>
    /// when that cannot be known.
    /// </summary>
    /// <remarks>
    /// <para>A malformed entry walls nobody off (the reader's rule).</para>
    /// <para><b>A user wall binds only a SECURE record</b> (owner Q4): on a non-secure record a systemuser-subject entry
    /// removes nothing at read time (<c>AccessibleRecordSetService.ResolveSystemUserDenyVetoAsync</c>) and the enforcer
    /// acts only on secure records. So on a record whose Secure flag reads false it is listed but not in force; when the
    /// flag could not be read it is undecided (fail closed: the signal becomes unknown, never "does not apply"). An entry
    /// reached through a secure PARENT is in force whatever this record's flag says: the parent is secure, and its list
    /// reaches every secure record filed below it (round 61).</para>
    /// <para>A contact or organization entry is in force on any record (the contact plane, and owner N3 on non-secure
    /// records).</para>
    /// </remarks>
    internal static (bool? InForce, string? NotInForceReason) InForce(
        bool wellFormed, NoAccessSubjectKinds subjectKind, bool viaSecureParent, string secure)
    {
        if (!wellFormed)
        {
            return (false, NoAccessEntryNotInForceReason.Malformed);
        }

        if (subjectKind != NoAccessSubjectKinds.SystemUser || viaSecureParent)
        {
            return (true, null);
        }

        return secure switch
        {
            AccessSignalState.Applies => (true, null),
            AccessSignalState.DoesNotApply => (false, NoAccessEntryNotInForceReason.UserWallOnNonSecureRecord),
            _ => (null, NoAccessEntryNotInForceReason.SecureStateUnknown),
        };
    }

    private static RecordNoAccessEntry ToView(
        NoAccessEntrySnapshot entry, NoAccessCoveringEntry cover, bool viaSecureParent,
        bool wellFormed, NoAccessSubjectKinds subjectKind, bool isOrgObject, bool? inForce, string? notInForceReason)
    {
        var row = entry.Row;
        var subjectId = row._sprk_subjectcontact_value ?? row._sprk_subjectorganization_value ?? row._sprk_subjectsystemuser_value;
        string? kind = wellFormed
            ? subjectKind switch
            {
                NoAccessSubjectKinds.Contact => "contact",
                NoAccessSubjectKinds.Organization => "organization",
                NoAccessSubjectKinds.SystemUser => "systemuser",
                _ => null,
            }
            : null;

        return new RecordNoAccessEntry(
            EntryId: row.sprk_noaccessentryid ?? cover.EntryId,
            Name: entry.Display?.Name,
            SubjectKind: kind,
            SubjectId: subjectId,
            SubjectName: entry.Display?.SubjectName,
            ObjectKind: wellFormed ? (isOrgObject ? "organization" : "record") : null,
            ObjectOrganizationId: row._sprk_objectorganization_value,
            ObjectOrganizationName: entry.Display?.ObjectOrganizationName,
            CoveredRecordType: cover.CoveredRecordType,
            CoveredRecordId: cover.CoveredRecordId,
            ViaSecureParent: viaSecureParent,
            AlsoViaSecureParent: cover.AlsoViaSecureParent,
            Malformed: !wellFormed,
            InForce: inForce,
            NotInForceReason: notInForceReason,
            ModifiedById: entry.ModifiedBy,
            ModifiedByName: entry.Display?.ModifiedByName,
            ModifiedOn: entry.Display?.ModifiedOn);
    }
}
