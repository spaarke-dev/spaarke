using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

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
    private readonly ILogger<SpeContainerMembershipSync> _logger;

    public SpeContainerMembershipSync(
        IGenericEntityService dataverse,
        SpeContainerMembershipService membership,
        ISecurableEntityRegistry securableEntities,
        IDataverseRecordShareService rights,
        ILogger<SpeContainerMembershipSync> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _membership = membership ?? throw new ArgumentNullException(nameof(membership));
        _securableEntities = securableEntities ?? throw new ArgumentNullException(nameof(securableEntities));
        _rights = rights ?? throw new ArgumentNullException(nameof(rights));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>What one standing-writer pass did.</summary>
    public sealed record StandingResult(
        int Containers, int Granted, int AlreadyHeld, int Removed, int MarkersCleared, int Unknown, int Failed, bool Capped,
        IReadOnlyList<string> Problems);

    /// <summary>What one just-in-time removal pass did.</summary>
    public sealed record JitResult(
        int SecureContainers, int GrantsSeen, int Removed, int MarkersCleared, int Kept, int Unknown, int Failed, bool Truncated,
        IReadOnlyList<string> Problems);

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

        int containers = 0, seen = 0, removed = 0, cleared = 0, kept = 0, unknown = 0, failed = 0;
        var problems = new List<string>();
        var truncated = false;

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

                try
                {
                    var markers = await _membership.ReadMarkersAsync(container, ct).ConfigureAwait(false);
                    var jit = markers is null
                        ? new Dictionary<Guid, string>()
                        : ParseMarkers(markers, SpeContainerMembershipService.JitWriterMarkerPrefix);
                    if (jit.Count == 0)
                        continue;

                    var access = await _membership.ReadAccessAsync(container, ct).ConfigureAwait(false);
                    if (access is null)
                        continue;

                    var restricted = await IsRestrictedAsync(entity, record.Id, ct).ConfigureAwait(false);
                    foreach (var (systemUserId, permissionId) in jit)
                    {
                        seen++;
                        var verdict = await JitVerdictAsync(systemUserId, entitySet, record.Id, restricted, ct).ConfigureAwait(false);
                        if (verdict == Verdict.Keep) { kept++; continue; }
                        if (verdict == Verdict.Unknown) { unknown++; continue; }

                        var key = SpeContainerMembershipService.MarkerKey(SpeContainerMembershipService.JitWriterMarkerPrefix, systemUserId);
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

        return new JitResult(containers, seen, removed, cleared, kept, unknown, failed, truncated, problems);
    }

    /// <summary>
    /// Keep, remove, or "could not tell". Remove ONLY on a positive answer: the holder is disabled; the record is
    /// Restricted and the holder is flagged external (round 67); or Dataverse answers that the holder lacks Write.
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
        if (user.GetAttributeValue<bool?>("isdisabled") == true)
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

    /// <summary>Is the record Restricted? <see langword="null"/> when it could not be read (the Restricted rule then does not fire).</summary>
    private async Task<bool?> IsRestrictedAsync(string entity, Guid recordId, CancellationToken ct)
    {
        try
        {
            var row = await _dataverse.RetrieveAsync(entity, recordId, ["sprk_accesspermission"], ct).ConfigureAwait(false);
            return row?.GetAttributeValue<OptionSetValue>("sprk_accesspermission")?.Value == ExternalParticipationService.AccessPermissionRestricted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "[SPE-MEMBERSHIP-SYNC] {Entity} {RecordId}'s access permission could not be read.", entity, recordId);
            return null;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private enum Verdict { Keep, Remove, Unknown }

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
