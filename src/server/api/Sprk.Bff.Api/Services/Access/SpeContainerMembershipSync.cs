using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// Keeps SharePoint Embedded container ROLES in line with Dataverse — unified-access-control-r2 task 171 (owner rounds 69
/// and 70). Two passes, one mechanism (<see cref="SpeContainerMembershipService"/>'s MARKED grants), run by
/// <see cref="SpeContainerMembershipSyncJob"/>:
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>Standing writers on environment / business-unit containers</b> (round 70 option (c)). Every enabled, internal
/// (<c>sprk_isexternal</c> not true — blank is internal, round 67 item 3) PERSON system user of a business unit whose
/// <c>sprk_containerid</c> names a container is kept as a writer on it. A user who is disabled, flagged external, no longer
/// a person, or moved to a business unit that does not map to that container loses the role — but ONLY a role this code
/// granted (a <see cref="SpeContainerMembershipService.StandingWriterMarkerPrefix"/> marker). Owner roles and hand-granted
/// roles are never touched: a user who already holds any role is never granted, so never recorded.</item>
/// <item><b>Just-in-time Office-edit writers on SECURE containers</b> (round 69 (2), round 70). Every grant
/// <c>OfficeEditAccessService</c> made (a <see cref="SpeContainerMembershipService.JitWriterMarkerPrefix"/> marker) on a
/// secure record's own container is removed once Dataverse POSITIVELY answers that the holder no longer has Write on the
/// record (<c>RetrievePrincipalAccess</c> as that user — unshare, No Access, a role change), or the holder is disabled, or
/// the record is Restricted and the holder is flagged external (round 67). An answer that could not be obtained removes
/// nothing and is counted, so a Dataverse outage never becomes a revocation loop.</item>
/// <item><b>Item grants on secure containers</b> (uac-r2 task 191g, owner Q2 = b). The same pass also reads every
/// <see cref="SpeContainerMembershipService.ItemWriterMarkerPrefix"/> marker (a flagged-external caller's write permission on
/// ONE file) and removes it on the same positive answers, plus "the document is no longer externally visible"
/// (<see cref="ExternalVisibility"/> through 191h's public seam <see cref="AppOnlyExternalVisibility"/>; Hidden, or an Undecided that is not
/// a failed read). An unknown answer keeps the grant this run and reports it. With
/// <c>ExternalAccess:FlaggedOfficeEdit:Enabled</c> on, it also removes ANY container role a flagged-external user holds on a
/// secure container (container-scoped, so their item grants stay).</item>
/// </list>
/// <para><b>Why one new component</b> (CLAUDE.md §11). Existing: the reconciliation jobs all keep DATAVERSE shares; none
/// touches SPE roles, and <see cref="SpeContainerMembershipService"/> had grant/revoke primitives with no caller.
/// Extension: this composes those primitives; the decision rules (who belongs, who lost Write) are Dataverse reads that
/// already exist (<see cref="InternalShareEndpoints.ClassifyEligibility"/>, <see cref="IDataverseRecordShareService.GetPrincipalRightsAsync"/>).
/// Cost of doing nothing: no internal user can open a business-unit container's files in Office unless someone hand-adds
/// them, and a just-in-time grant on a secure container would outlive the Write that justified it.</para>
/// </remarks>
public class SpeContainerMembershipSync
{
    /// <summary>Grants one pass may make (each costs two Graph writes): a first run on a large unit converges over runs.</summary>
    internal const int MaxGrantsPerRun = 200;

    /// <summary>Secure records one pass inspects; one more is reported TRUNCATED.</summary>
    internal const int MaxSecureContainers = 5000;

    private const int PageSize = 500;
    private const int MaxPages = 200;

    private static readonly string[] UserColumns =
    [
        "systemuserid", "domainname", "azureactivedirectoryobjectid", "isdisabled", "accessmode", "applicationid",
        "sprk_isexternal", "businessunitid",
    ];

    private readonly IGenericEntityService _dataverse;
    private readonly SpeContainerMembershipService _membership;
    private readonly ISecurableEntityRegistry _securableEntities;
    private readonly IDataverseRecordShareService _rights;
    private readonly AppOnlyExternalVisibility _visibility;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SpeContainerMembershipSync> _logger;

    public SpeContainerMembershipSync(
        IGenericEntityService dataverse,
        SpeContainerMembershipService membership,
        ISecurableEntityRegistry securableEntities,
        IDataverseRecordShareService rights,
        AppOnlyExternalVisibility visibility,
        IConfiguration configuration,
        ILogger<SpeContainerMembershipSync> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _membership = membership ?? throw new ArgumentNullException(nameof(membership));
        _securableEntities = securableEntities ?? throw new ArgumentNullException(nameof(securableEntities));
        _rights = rights ?? throw new ArgumentNullException(nameof(rights));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>What one standing-writer pass did.</summary>
    public sealed record StandingResult(
        int Containers, int Granted, int AlreadyHeld, int Removed, int MarkersCleared, int Unknown, int Failed, bool Capped,
        IReadOnlyList<string> Problems);

    /// <summary>What one just-in-time removal pass did.</summary>
    /// <param name="Unresolvable">Secure containers whose id Graph does not resolve in this tenant (#1830): skipped and
    /// named in <paramref name="Problems"/>, not counted in <paramref name="Failed"/>.</param>
    /// <param name="ItemGrantsSeen">Task 191g: item-level grants (<see cref="SpeContainerMembershipService.ItemWriterMarkerPrefix"/>)
    /// inspected; their removals, keeps, unknowns and failures are counted in the shared counters.</param>
    /// <param name="FlaggedRoleSweep">Task 191g: whether the removal of flagged users' container roles ran
    /// (<see cref="Sprk.Bff.Api.Services.Documents.OfficeEditAccessService.FlaggedOfficeEditEnabledConfigKey"/>).</param>
    /// <param name="FlaggedRolesRemoved">Task 191g: container roles of flagged-external users removed from secure containers.</param>
    public sealed record JitResult(
        int SecureContainers, int GrantsSeen, int Removed, int MarkersCleared, int Kept, int Unknown, int Failed,
        int Unresolvable, bool Truncated, IReadOnlyList<string> Problems,
        int ItemGrantsSeen = 0, bool FlaggedRoleSweep = false, int FlaggedRolesRemoved = 0);

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Pass 1 — standing writers (round 70)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Brings every business-unit container's STANDING writer grants in line with its units' internal users. Throws only
    /// when the business-unit list itself cannot be read (no progress is possible — ADR-036 A1 rule 4).
    /// </summary>
    public virtual async Task<StandingResult> SyncStandingWritersAsync(CancellationToken ct)
    {
        var query = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("businessunitid", "sprk_containerid") };
        query.Criteria.AddCondition("sprk_containerid", ConditionOperator.NotNull);
        var units = await ReadAllAsync(query, ct).ConfigureAwait(false);

        var unitsByContainer = units
            .Select(u => (Unit: u.Id, Container: u.GetAttributeValue<string>("sprk_containerid")?.Trim()))
            .Where(u => !string.IsNullOrEmpty(u.Container))
            .GroupBy(u => u.Container!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(u => u.Unit).ToHashSet(), StringComparer.Ordinal);

        int granted = 0, alreadyHeld = 0, removed = 0, cleared = 0, unknown = 0, failed = 0;
        var problems = new List<string>();
        var capped = false;

        foreach (var (container, unitIds) in unitsByContainer)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var users = await ReadUsersInUnitsAsync(unitIds, ct).ConfigureAwait(false);
                var access = await _membership.ReadAccessAsync(container, ct).ConfigureAwait(false);
                if (access is null)
                {
                    problems.Add($"container {container} (stamped on {unitIds.Count} business unit(s)) does not exist");
                    failed++;
                    continue;
                }

                if (!access.RolesComplete)
                {
                    // Nothing can be concluded from an absence in a partial list — neither "add" nor "already gone".
                    problems.Add($"container {container}: its roles could not be fully enumerated; skipped this run");
                    failed++;
                    continue;
                }

                var markers = MarkersOf(access, SpeContainerMembershipService.StandingWriterMarkerPrefix);

                // Adds: an eligible user with no role at all. A user holding ANY role is left alone (never recorded).
                foreach (var user in users.Where(IsStandingEligible))
                {
                    if (markers.TryGetValue(user.Id, out var markedPermission))
                    {
                        if (access.Roles.Any(r => string.Equals(r.PermissionId, markedPermission, StringComparison.Ordinal)))
                            continue;

                        // STALE marker: the grant it records is gone (an admin removed it, or Graph replaced it). Left
                        // alone it would block this eligible user from ever being granted again — clear it (the role list is
                        // complete, so RemoveMarkedGrantAsync only clears) and fall through to the add.
                        var staleKey = SpeContainerMembershipService.MarkerKey(SpeContainerMembershipService.StandingWriterMarkerPrefix, user.Id);
                        if (await _membership.RemoveMarkedGrantAsync(container, staleKey, markedPermission, access, ct).ConfigureAwait(false)
                            != SpeContainerMembershipService.MarkedRemovalOutcome.MarkerCleared)
                        {
                            failed++;
                            continue;
                        }

                        cleared++;
                    }

                    var upn = user.GetAttributeValue<string>("domainname");
                    var oid = user.GetAttributeValue<Guid?>("azureactivedirectoryobjectid"); // a systemuser row
                    if (access.Roles.Any(r => r.IsFor(upn, oid)))
                        continue;

                    if (granted + alreadyHeld >= MaxGrantsPerRun)
                    {
                        capped = true;
                        break;
                    }

                    switch (await _membership.GrantMarkedWriterAsync(
                                container, SpeContainerMembershipService.StandingWriterMarkerPrefix, user.Id, upn!, ct)
                                .ConfigureAwait(false))
                    {
                        case SpeContainerMembershipService.MarkedGrantOutcome.Granted: granted++; break;
                        case SpeContainerMembershipService.MarkedGrantOutcome.AlreadyHeld: alreadyHeld++; break;
                        default: failed++; break;
                    }
                }

                // Removals: ONLY grants this code recorded, and only on a positive Dataverse fact.
                var usersById = users.ToDictionary(u => u.Id);
                foreach (var (systemUserId, permissionId) in markers)
                {
                    var verdict = await StandingVerdictAsync(systemUserId, usersById, unitIds, ct).ConfigureAwait(false);
                    if (verdict == Verdict.Keep)
                        continue;
                    if (verdict == Verdict.Unknown)
                    {
                        unknown++;
                        continue;
                    }

                    var key = SpeContainerMembershipService.MarkerKey(SpeContainerMembershipService.StandingWriterMarkerPrefix, systemUserId);
                    switch (await _membership.RemoveMarkedGrantAsync(container, key, permissionId, access, ct).ConfigureAwait(false))
                    {
                        case SpeContainerMembershipService.MarkedRemovalOutcome.Removed: removed++; break;
                        case SpeContainerMembershipService.MarkedRemovalOutcome.MarkerCleared: cleared++; break;
                        default: failed++; break;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (SdapProblemException ex) when (ex.Code == SpeContainerOwnershipGuard.NotOwnedErrorCode)
            {
                // A business unit names a container this stamp neither configured nor created (task 227d) — a data or
                // configuration fault an operator must correct; no standing grant can be made there.
                _logger.LogError(
                    "[SPE-MEMBERSHIP-SYNC] Container {ContainerId} (stamped on {UnitCount} business unit(s)) is not one of this "
                    + "stamp's containers; its standing writers cannot be synced.", container, unitIds.Count);
                problems.Add($"container {container} (stamped on {unitIds.Count} business unit(s)) is not one of this stamp's containers");
                failed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SPE-MEMBERSHIP-SYNC] Standing writers on container {ContainerId} could not be synced.", container);
                problems.Add($"container {container}: {ex.GetType().Name}");
                failed++;
            }
        }

        return new StandingResult(unitsByContainer.Count, granted, alreadyHeld, removed, cleared, unknown, failed, capped, problems);
    }

    /// <summary>The round-70 population: an enabled, internal person (blank <c>sprk_isexternal</c> is internal).</summary>
    /// <remarks>A container holds many records, so a user flagged external is NEVER a standing writer (owner round 70),
    /// whatever any one record's Restricted state: the eligibility rule is asked as for a Restricted record
    /// (<c>rootIsRestricted: true</c>), which bars exactly a stored <c>sprk_isexternal = true</c>.</remarks>
    internal static bool IsStandingEligible(Entity user)
        => !string.IsNullOrWhiteSpace(user.GetAttributeValue<string>("domainname"))
           && InternalShareEndpoints.ClassifyEligibility(
                  user.GetAttributeValue<bool?>("isdisabled"),
                  user.GetAttributeValue<OptionSetValue>("accessmode")?.Value,
                  user.GetAttributeValue<Guid?>("applicationid"),
                  user.GetAttributeValue<bool?>("sprk_isexternal") ?? false,
                  rootIsRestricted: true)
              == InternalShareEndpoints.ShareEligibility.Eligible;

    private async Task<Verdict> StandingVerdictAsync(
        Guid systemUserId, IReadOnlyDictionary<Guid, Entity> usersInUnits, IReadOnlySet<Guid> unitIds, CancellationToken ct)
    {
        if (usersInUnits.TryGetValue(systemUserId, out var inUnit))
            return IsStandingEligible(inUnit) ? Verdict.Keep : Verdict.Remove;

        // Not in any of this container's units any more — confirm by reading the user (moved, rather than unreadable).
        Entity? user;
        try
        {
            user = await _dataverse.RetrieveAsync("systemuser", systemUserId, UserColumns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "[SPE-MEMBERSHIP-SYNC] User {SystemUserId} holding a standing grant could not be read; the grant is kept this run.",
                systemUserId);
            return Verdict.Unknown;
        }

        if (user is null)
            return Verdict.Unknown;

        var unit = user.GetAttributeValue<EntityReference>("businessunitid")?.Id;
        if (unit is { } u && unitIds.Contains(u))
            return IsStandingEligible(user) ? Verdict.Keep : Verdict.Remove;

        return unit is null ? Verdict.Unknown : Verdict.Remove;
    }

    private async Task<IReadOnlyList<Entity>> ReadUsersInUnitsAsync(IReadOnlySet<Guid> unitIds, CancellationToken ct)
    {
        var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet(UserColumns) };
        query.Criteria.AddCondition("businessunitid", ConditionOperator.In, unitIds.Cast<object>().ToArray());
        return await ReadAllAsync(query, ct).ConfigureAwait(false);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Pass 2 — just-in-time grants on secure containers (rounds 69 / 70)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Removes every just-in-time writer grant whose holder no longer has Write on the secure record that owns the
    /// container. Throws only when the securable-entity catalog cannot be read (no progress possible).
    /// </summary>
    public virtual async Task<JitResult> RemoveRevokedJitGrantsAsync(CancellationToken ct)
    {
        var securable = await _securableEntities.GetSecurableEntitiesAsync(ct).ConfigureAwait(false);

        int containers = 0, seen = 0, removed = 0, cleared = 0, kept = 0, unknown = 0, failed = 0, unresolvable = 0;
        int itemSeen = 0, flaggedRolesRemoved = 0;
        var problems = new List<string>();
        var truncated = false;

        // Task 191g: the flagged users whose container roles on secure containers are removed — only with the setting on.
        var sweep = Sprk.Bff.Api.Services.Documents.OfficeEditAccessService.FlaggedOfficeEditEnabled(_configuration);
        IReadOnlyList<Entity>? flaggedUsers = null;
        if (sweep)
        {
            try
            {
                var query = new QueryExpression("systemuser")
                {
                    ColumnSet = new ColumnSet("systemuserid", "domainname", "azureactivedirectoryobjectid", "sprk_isexternal"),
                };
                query.Criteria.AddCondition("sprk_isexternal", ConditionOperator.Equal, true);
                // The flag is re-checked on every row the query returns (review 191g-B2): a sweep removes roles, so a query
                // that came back wider than asked must never make an internal user's role removable.
                flaggedUsers = (await ReadAllAsync(query, ct).ConfigureAwait(false))
                    .Where(u => u.GetAttributeValue<bool?>("sprk_isexternal") == true)
                    .ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[SPE-MEMBERSHIP-SYNC] Flagged-external users could not be listed; their container roles are not swept this run.");
                problems.Add("flagged-external users could not be listed; their container roles were not swept");
                failed++;
            }
        }

        var flaggedIds = flaggedUsers?.Select(u => u.Id).ToHashSet() ?? [];

        foreach (var entity in securable.OrderBy(e => e, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<Entity> records;
            string entitySet;
            try
            {
                entitySet = await _dataverse.GetEntitySetNameAsync(entity, ct).ConfigureAwait(false);
                var query = new QueryExpression(entity) { ColumnSet = new ColumnSet("sprk_containerid") };
                query.Criteria.AddCondition("sprk_issecure", ConditionOperator.Equal, true);
                query.Criteria.AddCondition("sprk_containerid", ConditionOperator.NotNull);
                records = await ReadAllAsync(query, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[SPE-MEMBERSHIP-SYNC] Secure {Entity} records could not be listed; their JIT grants are kept this run.", entity);
                problems.Add($"{entity}: secure records could not be listed");
                failed++;
                continue;
            }

            foreach (var record in records)
            {
                if (containers >= MaxSecureContainers)
                {
                    truncated = true;
                    break;
                }

                var container = record.GetAttributeValue<string>("sprk_containerid")?.Trim();
                if (string.IsNullOrEmpty(container))
                    continue;
                containers++;

                // Set once the first read answers: from then on the id is proven to resolve, so a later 400 invalidRequest
                // (the permission collection or one of its nextLink pages) is a fault, not an unresolvable container.
                var resolved = false;
                try
                {
                    var markers = await _membership.ReadMarkersAsync(container, ct).ConfigureAwait(false);
                    resolved = true;
                    if (markers is null)
                        continue;
                    var jit = ParseMarkers(markers, SpeContainerMembershipService.JitWriterMarkerPrefix);
                    var items = ParseItemMarkers(markers, problems, entity, record.Id);
                    var sweepHere = flaggedUsers is { Count: > 0 };
                    if (jit.Count == 0 && items.Count == 0 && !sweepHere)
                        continue;

                    var restricted = jit.Count > 0 || (items.Count > 0 && sweep)
                        ? await IsRestrictedAsync(entity, record.Id, ct).ConfigureAwait(false)
                        : null;

                    // Task 191g: item grants (flagged-external holders, one file each). Same rules as the container grants,
                    // plus "the document is no longer externally visible". The container roles are not read for them.
                    // With the setting OFF the feature is off: every item grant goes (the edit-open reports view only, so a
                    // grant left standing would be write access the API denies — turning the setting off is the kill switch).
                    if (items.Count > 0)
                    {
                        var visibility = sweep
                            ? await DecideDocumentsAsync(items.Select(i => i.DocumentId).Distinct().ToList(), ct).ConfigureAwait(false)
                            : null;
                        foreach (var item in items)
                        {
                            itemSeen++;
                            var verdict = sweep
                                ? Combine(
                                    await JitVerdictAsync(item.SystemUserId, entitySet, record.Id, restricted, ct).ConfigureAwait(false),
                                    DocumentVerdict(visibility, item.DocumentId))
                                : Verdict.Remove;
                            if (verdict == Verdict.Keep) { kept++; continue; }
                            if (verdict == Verdict.Unknown)
                            {
                                unknown++;
                                problems.Add($"{entity} {record.Id}: item grant for user {item.SystemUserId} on document {item.DocumentId} could not be decided; kept this run");
                                continue;
                            }

                            if (await HolderObjectIdAsync(item.SystemUserId, ct).ConfigureAwait(false) is not { } holderObjectId)
                            {
                                failed++;
                                problems.Add($"{entity} {record.Id}: item grant for user {item.SystemUserId} on document {item.DocumentId} is due for removal but the holder's Entra object id could not be read; kept, retried next pass");
                                continue;
                            }

                            switch (await _membership.RemoveMarkedItemGrantAsync(container, item.MarkerKey, item.ItemId, holderObjectId, ct).ConfigureAwait(false))
                            {
                                case SpeContainerMembershipService.MarkedRemovalOutcome.Removed: removed++; break;
                                case SpeContainerMembershipService.MarkedRemovalOutcome.MarkerCleared: cleared++; break;
                                default: failed++; break;
                            }
                        }
                    }

                    if (jit.Count == 0 && !sweepHere)
                        continue;

                    var access = await _membership.ReadAccessAsync(container, ct).ConfigureAwait(false);
                    if (access is null)
                        continue;

                    // Permissions the JIT loop already removed, or failed on (retried next run): the sweep leaves them alone.
                    var handledPermissions = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var (systemUserId, permissionId) in jit)
                    {
                        seen++;
                        // Task 191g: with the sweep on, a flagged holder's container grant goes whatever their Write.
                        var verdict = sweepHere && flaggedIds.Contains(systemUserId)
                            ? Verdict.Remove
                            : await JitVerdictAsync(systemUserId, entitySet, record.Id, restricted, ct).ConfigureAwait(false);
                        if (verdict == Verdict.Keep) { kept++; continue; }
                        if (verdict == Verdict.Unknown) { unknown++; continue; }

                        var key = SpeContainerMembershipService.MarkerKey(SpeContainerMembershipService.JitWriterMarkerPrefix, systemUserId);
                        switch (await _membership.RemoveMarkedGrantAsync(container, key, permissionId, access, ct).ConfigureAwait(false))
                        {
                            case SpeContainerMembershipService.MarkedRemovalOutcome.Removed:
                                removed++;
                                handledPermissions.Add(permissionId);
                                break;
                            case SpeContainerMembershipService.MarkedRemovalOutcome.MarkerCleared: cleared++; break;
                            default:
                                failed++;
                                handledPermissions.Add(permissionId);
                                break;
                        }
                    }

                    // Task 191g (owner Q2 = b): a flagged-external user holds NO role on a secure container, whoever granted it
                    // (a hand-granted role, an owner role, a pre-191g grant whose role was changed). Removed with the
                    // container-scoped Prefer, so the user's item grants stay. Only a role the read SHOWS is acted on.
                    if (sweepHere)
                    {
                        foreach (var role in access.Roles)
                        {
                            if (handledPermissions.Contains(role.PermissionId))
                                continue;
                            var holder = flaggedUsers!.FirstOrDefault(u => role.IsFor(
                                u.GetAttributeValue<string>("domainname"), u.GetAttributeValue<Guid?>("azureactivedirectoryobjectid")));
                            if (holder is null)
                                continue;

                            if (await _membership.RemoveFlaggedUserContainerRoleAsync(container, role.PermissionId, ct).ConfigureAwait(false))
                            {
                                flaggedRolesRemoved++;
                                _logger.LogWarning(
                                    "[SPE-MEMBERSHIP-SYNC] Removed flagged-external user {SystemUserId}'s container role on secure {Entity} {RecordId}.",
                                    holder.Id, entity, record.Id);
                            }
                            else
                            {
                                failed++;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (SdapProblemException ex) when (ex.Code == SpeContainerOwnershipGuard.NotOwnedErrorCode)
                {
                    // Not one of this stamp's containers (deleted, or a pointer this stamp never created). A JIT grant is
                    // only ever made through the ownership guard, so none can stand there: nothing to remove, not a
                    // failure — the same outcome as a container that no longer exists (task 227d).
                    _logger.LogWarning(
                        "[SPE-MEMBERSHIP-SYNC] Secure {Entity} {RecordId} points at container {ContainerId}, which is not one of "
                        + "this stamp's containers; skipped.", entity, record.Id, container);
                    problems.Add($"{entity} {record.Id}: container {container} is not one of this stamp's containers; skipped");
                }
                catch (Exception ex) when (!resolved && SpeContainerMembershipService.IsUnresolvableContainer(ex))
                {
                    // The FIRST read says Graph cannot resolve the id in this tenancy (400 invalidRequest — "Invalid hostname
                    // for this tenancy" once the container's SharePoint site is gone; #1830, task 192). Retrying will not
                    // change the answer and no one reaches files through it, so it is reported and counted unresolvable, not
                    // failed — the same outcome as the guard's refusal above, which is what an id NOT on the stamp's
                    // configured list gets for the same 400. Fail closed all the same: nothing is read, removed or cleared
                    // there, and the next run reports it again. The record's container pointer (or the stamp's configured
                    // id) is a data fault an operator must fix.
                    unresolvable++;
                    _logger.LogWarning(
                        "[SPE-MEMBERSHIP-SYNC] Secure {Entity} {RecordId} points at container {ContainerId}, which does not "
                        + "resolve in this tenant (Graph 400 invalidRequest); its JIT grants cannot be checked — skipped.",
                        entity, record.Id, container);
                    problems.Add($"{entity} {record.Id}: container {container} does not resolve in this tenant; skipped");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[SPE-MEMBERSHIP-SYNC] JIT grants on secure {Entity} {RecordId}'s container {ContainerId} could not be checked.",
                        entity, record.Id, container);
                    problems.Add($"{entity} {record.Id}: {ex.GetType().Name}");
                    failed++;
                }
            }

            if (truncated)
                break;
        }

        return new JitResult(containers, seen, removed, cleared, kept, unknown, failed, unresolvable, truncated, problems,
            itemSeen, sweep, flaggedRolesRemoved);
    }

    /// <summary>One item-grant marker (task 191g), parsed.</summary>
    private sealed record ItemGrant(string MarkerKey, Guid SystemUserId, Guid DocumentId, string ItemId);

    /// <summary>The item-grant markers on a container. A marker whose value is malformed, or whose key is not the key its ids
    /// derive (hand-edited, or another code's), is reported and left alone.</summary>
    private static List<ItemGrant> ParseItemMarkers(
        IReadOnlyDictionary<string, string> markers, List<string> problems, string entity, Guid recordId)
    {
        var items = new List<ItemGrant>();
        foreach (var (key, value) in markers)
        {
            if (!key.StartsWith(SpeContainerMembershipService.ItemWriterMarkerPrefix, StringComparison.Ordinal))
                continue;
            if (!SpeContainerMembershipService.TryParseItemMarker(key, value, out var systemUserId, out var documentId, out var itemId))
            {
                problems.Add($"{entity} {recordId}: item-grant marker {key} is malformed or does not match its key; left alone");
                continue;
            }

            items.Add(new ItemGrant(key, systemUserId, documentId, itemId));
        }

        return items;
    }

    /// <summary>The Entra object id of an item grant's holder — removal deletes only that identity's own plain write
    /// (task 191g); <see langword="null"/> when the row or the id cannot be read (the grant is then kept and reported).</summary>
    private async Task<Guid?> HolderObjectIdAsync(Guid systemUserId, CancellationToken ct)
    {
        try
        {
            var user = await _dataverse.RetrieveAsync("systemuser", systemUserId, ["azureactivedirectoryobjectid"], ct).ConfigureAwait(false);
            return user?.GetAttributeValue<Guid?>("azureactivedirectoryobjectid") is { } oid && oid != Guid.Empty ? oid : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[SPE-MEMBERSHIP-SYNC] Item-grant holder {SystemUserId} could not be read; the grant is kept this run.", systemUserId);
            return null;
        }
    }

    /// <summary>
    /// The external visibility of the documents item grants were made for (one batched decision), or <see langword="null"/>
    /// when the decision itself threw (every document is then unknown, never removed).
    /// </summary>
    private async Task<ExternalRowVisibility?> DecideDocumentsAsync(IReadOnlyList<Guid> documentIds, CancellationToken ct)
    {
        try
        {
            return await _visibility(documentIds.Select(id => ("sprk_document", id)).ToList(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[SPE-MEMBERSHIP-SYNC] The external visibility of {Count} item-granted document(s) could not be decided.",
                documentIds.Count);
            return null;
        }
    }

    /// <summary>
    /// Task 191g: what the document's external visibility says about its item grant. Hidden → remove. Undecided for a reason
    /// a later read may answer (a faulted decision, or 191c's transient reasons: a failed read, a row not found this time, an
    /// undecided parent, ...) → unknown: kept this run and reported, so an outage never becomes a revocation. Any other
    /// Undecided is structural (no root, filed too deep, ...) → remove. Visible → keep.
    /// </summary>
    internal static Verdict DocumentVerdict(ExternalRowVisibility? visibility, Guid documentId)
    {
        if (visibility is null)
            return Verdict.Unknown;
        var visible = visibility.IsVisible("sprk_document", documentId);
        if (!visible && !visibility.IsUndecided("sprk_document", documentId))
            return Verdict.Remove;                       // Hidden: a positive answer, even inside a faulted decision (the 065 rule)
        if (visible && !visibility.Faulted)
            return Verdict.Keep;                         // Visible counts only from a decision that did not fault
        return visibility.MayAnswerLater("sprk_document", documentId) ? Verdict.Unknown : Verdict.Remove;
    }

    /// <summary>Remove on ANY positive answer; otherwise unknown if either is unknown; keep only when both keep.</summary>
    internal static Verdict Combine(Verdict holder, Verdict document)
        => holder == Verdict.Remove || document == Verdict.Remove ? Verdict.Remove
           : holder == Verdict.Unknown || document == Verdict.Unknown ? Verdict.Unknown
           : Verdict.Keep;

    /// <summary>
    /// Keep, remove, or "could not tell". Remove ONLY on a positive answer: the holder is disabled (or its row carries no
    /// <c>isdisabled</c> value, which is not proof of an active holder — issue #1576); the record is Restricted and the holder
    /// is flagged external (round 67); or Dataverse answers that the holder lacks Write.
    /// </summary>
    private async Task<Verdict> JitVerdictAsync(
        Guid systemUserId, string entitySet, Guid recordId, bool? restricted, CancellationToken ct)
    {
        Entity? user;
        try
        {
            user = await _dataverse.RetrieveAsync("systemuser", systemUserId, ["isdisabled", "sprk_isexternal"], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[SPE-MEMBERSHIP-SYNC] JIT holder {SystemUserId} could not be read; the grant is kept this run.", systemUserId);
            return Verdict.Unknown;
        }

        if (user is null)
            return Verdict.Unknown;
        // Issue #1576 (fail closed): only an explicit isdisabled = false is an active holder. A row that comes back without the
        // value is not proof the holder is active, so the grant is removed rather than kept.
        if (user.GetAttributeValue<bool?>("isdisabled") != false)
            return Verdict.Remove;
        if (restricted == true && user.GetAttributeValue<bool?>("sprk_isexternal") == true)
            return Verdict.Remove;

        try
        {
            // Task 171 (adversarial finding 3): the STRICT read — only an answer about the holder's access may revoke. A
            // request-level 403 (the application user missing prvActOnBehalfOfAnotherUser, which would 403 EVERY check)
            // is UNKNOWN, so a misconfiguration keeps grants instead of revoking them all each pass.
            var rights = await _rights.GetPrincipalRightsOrUnknownAsync(systemUserId, entitySet, recordId, ct).ConfigureAwait(false);
            if (rights is null)
            {
                _logger.LogWarning(
                    "[SPE-MEMBERSHIP-SYNC] Dataverse gave no access answer for JIT holder {SystemUserId} on {EntitySet}({RecordId}); "
                    + "kept this run.", systemUserId, entitySet, recordId);
                return Verdict.Unknown;
            }

            return rights.Value.HasFlag(AccessRights.Write) ? Verdict.Keep : Verdict.Remove;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "[SPE-MEMBERSHIP-SYNC] Write of JIT holder {SystemUserId} on {EntitySet}({RecordId}) could not be checked; kept this run.",
                systemUserId, entitySet, recordId);
            return Verdict.Unknown;
        }
    }

    /// <summary>
    /// Is the record Restricted — its own value, or (#1478, task 175) through what it is filed under (task 174's effective
    /// rule)? <see langword="null"/> when it could not be read (the Restricted rule then does not fire, as before).
    /// </summary>
    private async Task<bool?> IsRestrictedAsync(string entity, Guid recordId, CancellationToken ct)
    {
        try
        {
            var row = await _dataverse.RetrieveAsync(entity, recordId, ["sprk_accesspermission"], ct).ConfigureAwait(false);
            if (row?.GetAttributeValue<OptionSetValue>("sprk_accesspermission")?.Value == ExternalParticipationService.AccessPermissionRestricted)
                return true;
            return row is null
                ? false
                : await Sprk.Bff.Api.Infrastructure.ExternalAccess.EffectiveRootFlags
                    .RestrictedThroughFilingAsync(_dataverse, _logger, entity, recordId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "[SPE-MEMBERSHIP-SYNC] {Entity} {RecordId}'s access permission could not be read.", entity, recordId);
            return null;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    internal enum Verdict { Keep, Remove, Unknown }

    private static IReadOnlyDictionary<Guid, string> MarkersOf(SpeContainerMembershipService.ContainerAccess access, string prefix)
        => ParseMarkers(access.Markers, prefix);

    private static Dictionary<Guid, string> ParseMarkers(IReadOnlyDictionary<string, string> markers, string prefix)
    {
        var parsed = new Dictionary<Guid, string>();
        foreach (var (key, permissionId) in markers)
        {
            if (!string.IsNullOrWhiteSpace(permissionId)
                && SpeContainerMembershipService.TryParseMarkerKey(key, prefix, out var systemUserId))
            {
                parsed[systemUserId] = permissionId.Trim();
            }
        }

        return parsed;
    }

    private async Task<IReadOnlyList<Entity>> ReadAllAsync(QueryExpression query, CancellationToken ct)
    {
        query.PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 };
        var rows = new List<Entity>();
        for (var page = 1; ; page++)
        {
            if (page > MaxPages)
                throw new InvalidOperationException($"{query.EntityName} still had rows after {MaxPages} pages.");
            var result = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(result.Entities);
            if (!result.MoreRecords)
                return rows;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
    }
}
