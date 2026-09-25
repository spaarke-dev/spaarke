// spaarkeai-word-add-in-r1 task 080 — record ownership assignment.
//
// Owner decision 2026-09-22: "the records should be owned by the acting user's BU default owner team.
// So if testuser1 is in spaarke business unit 1 team/business unit, then the record they create is
// assigned to that team (not the user)."
//
// REFINED 2026-09-25, same owner, after they asked whether a BFF-created record even HAS an accurate
// "user BU": the source is the TARGET record's business unit first, the acting user's only as a fallback.
// Two reasons, both verified — some BFF creates have no acting user at all (EmailAttachmentProcessor,
// inbound email), and where there is one their BU is often the wrong scope (a root-BU user filing to a
// child-BU matter would put the document where the matter's own team cannot see it). This matches
// RecordContainerResolver's already-sanctioned order for SPE containers. Nothing about the team-ownership
// convention changed; only where the business unit is read from.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — nothing resolves "business unit → default owner team". The two neighbours both do
//       something else: UserOrgContextReader (Services/Ai/Context/) reads BU and team NAMES to bind AI
//       prompt context, and CommunicationEnrichmentService.ResolveTeamIdByNameAsync (:738) resolves a team
//       by NAME from an email-category routing gate. Neither answers "which team owns what this caller
//       creates".
//   (2) Extension — No. UserOrgContextReader is ADR-013 in-zone AI code; having CRUD create paths inject it
//       would be precisely the CRUD→AI dependency root CLAUDE.md §10 bullet 3 forbids. The Communication
//       resolver keys off an email category, not the caller's business unit, so it cannot be generalized
//       without changing what it means.
//   (3) Cost-of-doing-nothing — every record the BFF creates app-only defaults to the calling application
//       user, which lives in the ROOT business unit: measured 2026-09-22, ALL 512 sprk_document rows sit in
//       root. Users sit in child BUs and Dataverse Deep depth traverses DOWNWARD, so they reach none of
//       them. Concretely: task 063's Run Index and task 064's create-To-Do both return 403 for every
//       ordinary user, and every future per-record gate on a BFF-created entity fails the same way.
//
// Placement Justification (bff-extensions.md): lives in Services/Dataverse/ beside CoreAncestorResolver —
// it is a Dataverse lookup with no AI concern. It deliberately does NOT live in Spaarke.Dataverse: that
// shared library must not depend on BFF services, so create paths there receive an already-resolved team id
// as a parameter (mirroring RecordCreationRequest.OwnerSystemUserId, the shipped precedent).

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Resolves the team that should own a newly created record: the <b>default owner team</b> of the relevant
/// business unit — the TARGET record's BU where the record is being filed against something, otherwise the
/// acting user's. See <see cref="RecordOwnershipContext"/> for why that order and not the reverse.
/// </summary>
/// <remarks>
/// <para>
/// Setting <c>ownerid</c> to that team makes <c>owningbusinessunit</c> <b>derive</b> from it —
/// <c>owningbusinessunit</c> is never assigned directly. This is the shape <c>sprk_matter</c> rows already
/// have in every environment checked, which is why it is the target rather than an invention.
/// </para>
/// <para>
/// <b>Fail-closed.</b> An unresolvable team returns <c>null</c> and callers MUST refuse the create. Falling
/// back to app-only ownership is exactly the defect this type exists to remove, and a silent fallback would
/// reintroduce it invisibly — a record that looks created but is unreachable by the person who created it.
/// </para>
/// </remarks>
public interface IRecordOwnershipResolver
{
    /// <summary>
    /// Resolves the team that should own a new record, applying the priority order in ONE place so no call
    /// site can get it wrong: <b>the target record's business unit first, the acting user's second</b>.
    /// Returns <c>null</c> when neither resolves — callers MUST then refuse.
    /// </summary>
    Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct);
}

/// <summary>
/// What is known about a record being created, in the order that decides its owner.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why record-first rather than creator-first.</b> The acting user's business unit is the wrong answer more
/// often than it is the right one:
/// </para>
/// <list type="number">
/// <item><b>Some creates have no acting user at all.</b> <c>EmailAttachmentProcessor</c> creates documents from
/// inbound email — no human initiated it, so "the acting user's BU" is undefined. Creator-first would have to
/// refuse and would break inbound attachment processing outright.</item>
/// <item><b>Even with an acting user, their BU is often the wrong scope.</b> A root-BU paralegal filing a
/// document to a child-BU matter would, creator-first, put the document in ROOT — where the very team that
/// owns the matter cannot see it. The document belongs with its matter, not with whoever uploaded it.</item>
/// </list>
/// <para>
/// This mirrors <c>RecordContainerResolver</c> (task 076, owner-sanctioned), which derives an SPE container the
/// same way — <c>ResolveForRecordAsync</c> from the target record, <c>ResolveForActingUserAsync</c> only when
/// there is no record. Same question, same answer shape; reusing the established order rather than inventing a
/// second one. It is also the same instinct as FR-26's <c>CoreAncestorResolver</c>, which exists precisely
/// because access for a server-created child has to be inherited from its core ancestor.
/// </para>
/// </remarks>
public sealed record RecordOwnershipContext
{
    /// <summary>Logical name of the record this one is being filed against (e.g. <c>sprk_matter</c>). Null when unfiled.</summary>
    public string? TargetEntityLogicalName { get; init; }

    /// <summary>Id of the record this one is being filed against. Null when unfiled.</summary>
    public Guid? TargetRecordId { get; init; }

    /// <summary>The acting user's Dataverse systemuserid, when known (HTTP paths that already resolved it).</summary>
    public Guid? CallerSystemUserId { get; init; }

    /// <summary>
    /// The acting user's Entra object id, when that is all the path has. Background paths have only this:
    /// <c>UploadFinalizationWorker</c> runs from a queue message carrying <c>payload.UserId</c> with no
    /// <see cref="System.Security.Claims.ClaimsPrincipal"/> to hand to <c>ICallerSystemUserResolver</c>. The
    /// cross-reference is <c>systemuser.azureactivedirectoryobjectid</c> (ADR-028) — the same key that
    /// resolver uses.
    /// </summary>
    public Guid? CallerObjectId { get; init; }

    /// <summary>True when a target record was supplied — i.e. the preferred source is available.</summary>
    public bool HasTarget =>
        !string.IsNullOrWhiteSpace(TargetEntityLogicalName)
        && TargetRecordId is { } id && id != Guid.Empty;
}

/// <inheritdoc cref="IRecordOwnershipResolver" />
public sealed class RecordOwnershipResolver : IRecordOwnershipResolver
{
    private const string SystemUserEntity = "systemuser";
    private const string TeamEntity = "team";
    private const string BusinessUnitColumn = "businessunitid";
    private const string OwningBusinessUnitColumn = "owningbusinessunit";

    /// <summary>
    /// Owner-team teamtype. Dataverse defines 0 = Owner, 1 = Access. BOTH this and <c>isdefault</c> are
    /// required: dev contains non-default Owner teams (auto-created, GUID-shaped names) AND Access teams,
    /// so filtering on either predicate alone selects the wrong team.
    /// </summary>
    private const int OwnerTeamType = 0;

    private readonly IDataverseService _dataverse;
    private readonly ILogger<RecordOwnershipResolver> _logger;

    public RecordOwnershipResolver(IDataverseService dataverse, ILogger<RecordOwnershipResolver> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        // ── 1. PREFERRED: the target record's business unit ────────────────────────────────────────
        // A filed record belongs with what it is filed against, not with whoever uploaded it. This is also
        // the only source available when there is no acting user at all (inbound email).
        if (context.HasTarget)
        {
            var fromTarget = await ResolveFromTargetRecordAsync(
                context.TargetEntityLogicalName!, context.TargetRecordId!.Value, ct).ConfigureAwait(false);

            if (fromTarget is not null)
            {
                return fromTarget;
            }

            // ⛔ REFUSE — do NOT fall back to the acting user when a target WAS named but could not be
            // resolved. This branch used to fall through, which was wrong, and the reason is the secure-record
            // case that RecordContainerResolver already documents (task 076,
            // notes/secure-project-workflow-review-2026-08-24.md §A): users sit in the Operations subtree
            // while SECURE records are owned in `Secure Projects`. Falling back to the acting user's business
            // unit would assign a secure record's child to the general Operations team — the precise isolation
            // failure that resolver refuses to make for containers, and it fails the same way here.
            //
            // Record-first already handles secure targets correctly when the read SUCCEEDS, because a secure
            // record's own owningbusinessunit IS the Secure Project BU. The danger was only ever this
            // fallback. Per 076: an indeterminate answer read as "not secure" is the same isolation failure
            // with an extra step, so indeterminate must refuse.
            _logger.LogWarning(
                "Refusing to resolve an owning team: target {TargetEntity} {TargetId} was named but its "
                + "business unit could not be read. NOT falling back to the acting user — for a secure record "
                + "that would assign it to the caller's general business unit and defeat its isolation "
                + "(task 076 / task 080).",
                context.TargetEntityLogicalName, context.TargetRecordId);
            return null;
        }

        // ── 2. FALLBACK: the acting user's business unit ───────────────────────────────────────────
        if (context.CallerSystemUserId is { } systemUserId && systemUserId != Guid.Empty)
        {
            return await ResolveFromUserAsync("systemuserid", systemUserId, ct).ConfigureAwait(false);
        }

        if (context.CallerObjectId is { } objectId && objectId != Guid.Empty)
        {
            return await ResolveFromUserAsync("azureactivedirectoryobjectid", objectId, ct).ConfigureAwait(false);
        }

        // ── 3. Neither. Refuse upstream. ───────────────────────────────────────────────────────────
        _logger.LogWarning(
            "Cannot resolve an owning team: no target record and no acting-user identity were supplied. The "
            + "record must be refused rather than created app-owned (task 080).");
        return null;
    }

    /// <summary>
    /// Reads the target record's <c>owningbusinessunit</c> and returns that business unit's default owner
    /// team. Works whether the target is itself user-owned or team-owned, because the BU is derived either way.
    /// </summary>
    private async Task<Guid?> ResolveFromTargetRecordAsync(
        string entityLogicalName, Guid recordId, CancellationToken ct)
    {
        try
        {
            var query = new QueryExpression(entityLogicalName)
            {
                ColumnSet = new ColumnSet(OwningBusinessUnitColumn),
                TopCount = 1,
                NoLock = true
            };
            query.Criteria.AddCondition($"{entityLogicalName}id", ConditionOperator.Equal, recordId);

            var results = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            var businessUnitId = results.Entities.FirstOrDefault()
                ?.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn)?.Id;

            if (businessUnitId is null || businessUnitId == Guid.Empty)
            {
                return null;
            }

            return await ResolveDefaultOwnerTeamAsync(businessUnitId.Value, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Not fatal — the caller falls back to the acting user. Logged so a systematically unreadable
            // target (a wrong logical name, a missing primary-key column) is visible rather than silent.
            _logger.LogWarning(ex,
                "Could not read owningbusinessunit from target {EntityLogicalName} {RecordId}.",
                entityLogicalName, recordId);
            return null;
        }
    }

    /// <summary>
    /// Resolves the acting user's business unit, then that BU's default owner team.
    /// <paramref name="userKeyColumn"/> selects which identity the caller is known by.
    /// </summary>
    private async Task<Guid?> ResolveFromUserAsync(string userKeyColumn, Guid userKey, CancellationToken ct)
    {
        try
        {
            var userQuery = new QueryExpression(SystemUserEntity)
            {
                ColumnSet = new ColumnSet(BusinessUnitColumn),
                TopCount = 1,
                NoLock = true
            };
            userQuery.Criteria.AddCondition(userKeyColumn, ConditionOperator.Equal, userKey);

            var users = await _dataverse.RetrieveMultipleAsync(userQuery, ct).ConfigureAwait(false);
            var businessUnitId = users.Entities.FirstOrDefault()
                ?.GetAttributeValue<EntityReference>(BusinessUnitColumn)?.Id;

            if (businessUnitId is null || businessUnitId == Guid.Empty)
            {
                _logger.LogWarning(
                    "Cannot resolve an owning team: no systemuser with {UserKeyColumn}={UserKey}, or that user "
                    + "has no business unit.",
                    userKeyColumn, userKey);
                return null;
            }

            return await ResolveDefaultOwnerTeamAsync(businessUnitId.Value, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail-closed, never fail-open-to-app-ownership: an unresolved team is a refusal upstream.
            _logger.LogWarning(ex,
                "Owning-team lookup failed for caller {UserKeyColumn}={UserKey}; the record must be refused "
                + "rather than created app-owned.",
                userKeyColumn, userKey);
            return null;
        }
    }

    /// <summary>
    /// A business unit's DEFAULT OWNER team. Both predicates are load-bearing — see <see cref="OwnerTeamType"/>.
    /// </summary>
    private async Task<Guid?> ResolveDefaultOwnerTeamAsync(Guid businessUnitId, CancellationToken ct)
    {
        var teamQuery = new QueryExpression(TeamEntity)
        {
            ColumnSet = new ColumnSet("teamid"),
            TopCount = 1,
            NoLock = true
        };
        teamQuery.Criteria.AddCondition(BusinessUnitColumn, ConditionOperator.Equal, businessUnitId);
        teamQuery.Criteria.AddCondition("isdefault", ConditionOperator.Equal, true);
        teamQuery.Criteria.AddCondition("teamtype", ConditionOperator.Equal, OwnerTeamType);

        var teams = await _dataverse.RetrieveMultipleAsync(teamQuery, ct).ConfigureAwait(false);
        var teamId = teams.Entities.FirstOrDefault()?.Id;

        if (teamId is null || teamId == Guid.Empty)
        {
            _logger.LogWarning(
                "Business unit {BusinessUnitId} has no default owner team "
                + "(isdefault = true AND teamtype = {OwnerTeamType}).",
                businessUnitId, OwnerTeamType);
            return null;
        }

        _logger.LogDebug(
            "Resolved owning team {TeamId} for business unit {BusinessUnitId}.", teamId, businessUnitId);
        return teamId;
    }
}
