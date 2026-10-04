using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// unified-access-control-r2 task 133 (C11, #1054), owner round 10 item 4 — the ONE snapshot-and-restore of the child
/// rows an <c>Assign</c> of a secure root re-owns as a side effect. Snapshot each child's OWN owner before the Assign;
/// after an Assign that must not leave them where the cascade put them (a compensating reverse move), put each one back.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> An owner move of a project or a matter cascades <c>Assign</c> to three 1:N relationships
/// (live metadata, spaarkedev1, 2026-10-01 and re-read 2026-10-03): <c>{root}_Teams</c> → <c>team</c>,
/// <c>{root}_SharePointDocumentLocations</c> → <c>sharepointdocumentlocation</c> and <c>{root}_SharePointDocuments</c> →
/// <c>sharepointdocument</c>, each on <c>regardingobjectid</c>. A work assignment cascades nothing. The owner accepted
/// that cascade for the FORWARD move into the secure owner team (round 4 item 3). A move BACK — provisioning's
/// compensation — cascades the same way, so without this every child would end with the ROOT's pre-call owner, not its
/// own: a child whose owner differed from the root's would silently change hands. The owner chose (round 10 item 4,
/// options (c) then (a)): snapshot and restore each re-owned child's own owner, built so task 148 reuses it.</para>
///
/// <para><b>The three tables, each from live metadata (2026-10-03).</b></para>
/// <list type="bullet">
/// <item><c>team</c> — <c>BusinessOwned</c>: it has no <c>ownerid</c>, <c>owninguser</c> or <c>owningteam</c>, so an
/// Assign cannot re-own a team and there is no owner to snapshot or restore. Listed (<see cref="CascadeChildRead.NoOwner"/>)
/// so the coverage of the three cascading relationships is explicit; never read.</item>
/// <item><c>sharepointdocumentlocation</c> — <c>UserOwned</c>, stored: read by <c>_regardingobjectid_value</c> and
/// restored by an <c>ownerid</c> bind.</item>
/// <item><c>sharepointdocument</c> — <c>UserOwned</c>, but not stored in Dataverse: its rows are the files SharePoint
/// lists under the record's document locations. Dataverse REFUSES a read of it with 400 (0x80071017 "SharePoint S2S
/// and MSTeams integration is not enabled for this org") wherever that integration is off — as it is in dev, where
/// Spaarke stores documents in SharePoint Embedded instead. So it is read ONLY when the root has at least one document
/// location (<see cref="CascadeChildRead.UnderDocumentLocations"/>): with no location there is no folder for a document
/// to come from. Under a location, a refused or failed read fails the snapshot — never "no documents".</item>
/// </list>
///
/// <para><b>Fail closed (ADR-003).</b> A snapshot either reads every owner-bearing child completely or fails, naming
/// the table: an unreadable child set is never "no children". A caller that cannot snapshot must not make the Assign
/// it would need to undo. A full page (<see cref="PageLimit"/> rows) counts as incomplete — <c>QueryAsync</c> reads one
/// page. A child read without an id or an owner cannot be put back, so it fails the snapshot too.</para>
///
/// <para><b>Restore is keyed on observed state.</b> <see cref="RestoreAsync"/> reads each snapshotted child: one that
/// reads as its snapshotted owner is left alone; one that does not is assigned back — its own operation, never folded
/// into another PATCH — and read back, exactly as the root's own owner moves are (<c>ProvisionProjectEndpoint</c>'s
/// bind-then-read-back). A child that no longer exists when it is read BEFORE the restore is
/// <see cref="CascadeChildRestoreOutcome.Gone"/>: nothing is left to put back. Only that read decides <c>Gone</c> — a child
/// that disappears after its PATCH is reported <see cref="CascadeChildRestoreOutcome.Refused"/> or
/// <see cref="CascadeChildRestoreOutcome.NotApplied"/>, a failure (fail closed). Every outcome other than
/// <c>AlreadyOwned</c>, <c>Restored</c> and <c>Gone</c> — including a child that could not be read before the restore
/// (<see cref="CascadeChildRestoreOutcome.Unreadable"/>) or read back after it
/// (<see cref="CascadeChildRestoreOutcome.Unverified"/>) — is a failure the caller reports, child by child, with the call
/// that puts it back (<see cref="CascadeChild.RestoreCall"/>).</para>
///
/// <para><b>For task 148 (secure child backfill and transitions)</b> — reuse, do not fork. Around ANY owner move of a
/// root whose cascade must not decide the children's owners: <see cref="SnapshotAsync"/> before the move (refuse the
/// move when it fails), then <see cref="RestoreAsync"/> after it. Where following the cascade IS intended (the forward
/// move into the secure owner team, accepted by the owner), take the snapshot anyway when the move may need undoing,
/// and simply do not restore on success. <see cref="CascadeChildSnapshot.NotOwnedBy"/> names the children a move to a
/// given owner would leave with the wrong owner. A new cascading relationship (a metadata change) is added to
/// <see cref="TablesFor"/> — here, once.</para>
///
/// <para><b>Placement</b> (CLAUDE.md §10/§11): a static helper beside <see cref="SecureRecordOwnerTeam"/>, not a
/// DI-registered service — reads and owner binds on the <see cref="DataverseWebApiClient"/> its callers already inject.
/// No new interface (ADR-010), no registration, no package.</para>
/// </remarks>
public static class AssignCascadeChildOwners
{
    /// <summary>
    /// The most rows one table's snapshot reads. <c>DataverseWebApiClient.QueryAsync</c> reads ONE page, and Dataverse's
    /// Web API pages at 5,000, so a full page may not be all of them: it is treated as incomplete (fail closed).
    /// </summary>
    internal const int PageLimit = 5000;

    private const string RegardingColumn = "_regardingobjectid_value";
    private const string OwningUserColumn = "_owninguser_value";
    private const string OwningTeamColumn = "_owningteam_value";

    private static readonly CascadeChildTable Team =
        new("team", "teams", "teamid", CascadeChildRead.NoOwner);

    private static readonly CascadeChildTable DocumentLocation =
        new("sharepointdocumentlocation", "sharepointdocumentlocations", "sharepointdocumentlocationid", CascadeChildRead.Always);

    private static readonly CascadeChildTable Document =
        new("sharepointdocument", "sharepointdocuments", "sharepointdocumentid", CascadeChildRead.UnderDocumentLocations);

    private static readonly IReadOnlyList<CascadeChildTable> ProjectAndMatterTables = new[] { Team, DocumentLocation, Document };

    /// <summary>
    /// The tables an Assign of a <paramref name="rootLogicalName"/> row cascades to (live metadata 2026-10-03). Project
    /// and matter: <c>team</c>, <c>sharepointdocumentlocation</c>, <c>sharepointdocument</c>; work assignment: none. Any
    /// other table is not a secure root and is refused rather than guessed (an empty list would read as "no cascade").
    /// </summary>
    public static IReadOnlyList<CascadeChildTable> TablesFor(string rootLogicalName) => rootLogicalName switch
    {
        "sprk_project" or "sprk_matter" => ProjectAndMatterTables,
        "sprk_workassignment" => Array.Empty<CascadeChildTable>(),
        _ => throw new ArgumentOutOfRangeException(
            nameof(rootLogicalName), rootLogicalName, "Not a secure root: its Assign cascade is not described.")
    };

    /// <summary>
    /// Every owner-bearing table an Assign of ANY secure root re-owns as a side effect — the union of
    /// <see cref="TablesFor"/> over the roots, <see cref="CascadeChildRead.NoOwner"/> tables excluded (an Assign cannot
    /// re-own them).
    /// </summary>
    private static readonly IReadOnlyList<CascadeChildTable> ReownedByCascadeTables =
        new[] { "sprk_project", "sprk_matter", "sprk_workassignment" }
            .SelectMany(TablesFor)
            .Where(t => t.Read != CascadeChildRead.NoOwner)
            .Distinct()
            .ToArray();

    /// <summary>
    /// 🔴 unified-access-control-r2 task 132 (integration residual): whether <paramref name="entityLogicalName"/> is a
    /// table whose rows Dataverse re-owns as a SIDE EFFECT of an Assign of a secure root (<c>sharepointdocumentlocation</c>,
    /// <c>sharepointdocument</c>). <b>No access cache stores anything keyed by such a table</b> — the membership resolver,
    /// the impersonated root-set source and the record-access snapshot decorator all read it live — because the
    /// cascade's owner changes happen with no BFF write to evict after (the forward Assign into the secure owner team
    /// re-owns them silently). Consequence, and the reason the rule lives HERE beside <see cref="TablesFor"/>: a child
    /// restore needs no eviction (the eviction hook builds no pattern for these tables, so it scans nothing), and a new
    /// cascading relationship added to <see cref="TablesFor"/> is uncached the moment it is listed.
    /// </summary>
    internal static bool IsReownedByCascade(string entityLogicalName) =>
        ReownedByCascadeTables.Any(t => string.Equals(t.LogicalName, entityLogicalName?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The entity-SET form of <see cref="IsReownedByCascade"/> (the record-access snapshot key carries the set).</summary>
    internal static bool IsReownedByCascadeEntitySet(string entitySetName) =>
        ReownedByCascadeTables.Any(t => string.Equals(t.EntitySet, entitySetName?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads every owner-bearing child an Assign of the root re-owns, with its own owner. Read-only. Either the whole
    /// set or a failure naming the table that could not be read completely — never a partial snapshot.
    /// </summary>
    public static async Task<CascadeSnapshotResult> SnapshotAsync(
        DataverseWebApiClient dataverseClient,
        string rootLogicalName,
        Guid rootId,
        CancellationToken ct)
    {
        var children = new List<CascadeChild>();
        var locationCount = 0;

        foreach (var table in TablesFor(rootLogicalName))
        {
            if (table.Read == CascadeChildRead.NoOwner)
                continue;

            if (table.Read == CascadeChildRead.UnderDocumentLocations && locationCount == 0)
                continue;

            List<ChildOwnerRow> rows;
            try
            {
                rows = await dataverseClient.QueryAsync<ChildOwnerRow>(
                    table.EntitySet,
                    filter: $"{RegardingColumn} eq {rootId}",
                    select: $"{table.IdColumn},{OwningUserColumn},{OwningTeamColumn}",
                    top: PageLimit,
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                return CascadeSnapshotResult.Failed(table, FailureOf(ex), ex);
            }

            if (rows.Count >= PageLimit)
                return CascadeSnapshotResult.Failed(table, CascadeReadFailure.Refused, fault: null);

            foreach (var row in rows)
            {
                if (row.IdFrom(table.IdColumn) is not { } childId || row.Owner is not { } owner)
                    return CascadeSnapshotResult.Failed(table, CascadeReadFailure.Refused, fault: null);

                children.Add(new CascadeChild(table.LogicalName, table.EntitySet, table.IdColumn, childId, owner));
            }

            if (table == DocumentLocation)
                locationCount = rows.Count;
        }

        return CascadeSnapshotResult.Read(new CascadeChildSnapshot(rootLogicalName, rootId, children));
    }

    /// <summary>
    /// Puts every snapshotted child back on its snapshotted owner — each one read first, assigned back only when it does
    /// not read as that owner, and read back. Never throws for one child: each outcome is reported.
    /// </summary>
    public static async Task<CascadeRestoreReport> RestoreAsync(
        DataverseWebApiClient dataverseClient,
        CascadeChildSnapshot snapshot,
        ILogger logger,
        CancellationToken ct)
    {
        var results = new List<CascadeChildRestore>(snapshot.Children.Count);
        foreach (var child in snapshot.Children)
            results.Add(await RestoreOneAsync(dataverseClient, child, logger, ct));

        return new CascadeRestoreReport(results);
    }

    private static async Task<CascadeChildRestore> RestoreOneAsync(
        DataverseWebApiClient dataverseClient, CascadeChild child, ILogger logger, CancellationToken ct)
    {
        ChildOwnerRead found;
        try
        {
            found = await ReadOwnerAsync(dataverseClient, child, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[ASSIGN-CASCADE] Could not read the owner of {Table} {ChildId} to put it back on {OwnerKind} {OwnerId}.",
                child.LogicalName, child.Id, child.Owner.Kind, child.Owner.Id);
            return new CascadeChildRestore(child, CascadeChildRestoreOutcome.Unreadable, FoundOwner: null);
        }

        if (!found.Exists)
            return new CascadeChildRestore(child, CascadeChildRestoreOutcome.Gone, FoundOwner: null);

        if (found.Owner == child.Owner)
            return new CascadeChildRestore(child, CascadeChildRestoreOutcome.AlreadyOwned, found.Owner);

        var patchRefused = false;
        try
        {
            await dataverseClient.UpdateAsync(
                child.EntitySet,
                child.Id,
                new Dictionary<string, object?> { ["ownerid@odata.bind"] = child.OwnerBind },
                ct);
        }
        catch (Exception ex)
        {
            // The read below still decides: a PATCH that reports failure can have committed.
            patchRefused = true;
            logger.LogError(ex,
                "[ASSIGN-CASCADE] Dataverse refused putting {Table} {ChildId} back on {OwnerKind} {OwnerId}.",
                child.LogicalName, child.Id, child.Owner.Kind, child.Owner.Id);
        }

        ChildOwnerRead after;
        try
        {
            after = await ReadOwnerAsync(dataverseClient, child, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[ASSIGN-CASCADE] Could not read {Table} {ChildId} back after putting it on {OwnerKind} {OwnerId}.",
                child.LogicalName, child.Id, child.Owner.Kind, child.Owner.Id);
            return new CascadeChildRestore(child, CascadeChildRestoreOutcome.Unverified, found.Owner);
        }

        if (after.Exists && after.Owner == child.Owner)
            return new CascadeChildRestore(child, CascadeChildRestoreOutcome.Restored, found.Owner);

        logger.LogError(
            "[ASSIGN-CASCADE] {Table} {ChildId} reads back as {Found}, not {OwnerKind} {OwnerId}.",
            child.LogicalName, child.Id, after.Owner?.ToString() ?? "(no row)", child.Owner.Kind, child.Owner.Id);
        return new CascadeChildRestore(
            child,
            patchRefused ? CascadeChildRestoreOutcome.Refused : CascadeChildRestoreOutcome.NotApplied,
            after.Owner ?? found.Owner);
    }

    private static async Task<ChildOwnerRead> ReadOwnerAsync(
        DataverseWebApiClient dataverseClient, CascadeChild child, CancellationToken ct)
    {
        var rows = await dataverseClient.QueryAsync<ChildOwnerRow>(
            child.EntitySet,
            filter: $"{child.IdColumn} eq {child.Id}",
            select: $"{child.IdColumn},{OwningUserColumn},{OwningTeamColumn}",
            top: 1,
            cancellationToken: ct);

        var row = rows.FirstOrDefault();
        return row is null ? new ChildOwnerRead(false, null) : new ChildOwnerRead(true, row.Owner);
    }

    /// <summary>
    /// True when a failed Dataverse read is DETERMINISTIC — the same query is refused again until an administrator acts:
    /// a 400 (Dataverse REFUSING the read; for <c>sharepointdocument</c>, the integration is off), and a 401 or 403 (the
    /// service's own sign-in, or its Read privilege on the table, refused — e.g. a BFF application user without Read on
    /// <c>sharepointdocumentlocation</c>; <c>DataverseWebApiClient</c> renews its token five minutes before expiry, so a
    /// 401 is not a stale token). Anything else (a 5xx, a 429, a timeout, a non-HTTP fault) may pass on the next call.
    /// <c>DataverseWebApiClient</c> surfaces only the status (<c>EnsureSuccessStatusCode</c>).
    /// </summary>
    /// <remarks>
    /// Task 133 c1-r2 (verifier item 4): a 401/403 had been <c>Unreadable</c>, so the caller was told to retry a read that
    /// fails every time. Owner round 14 item 3 (task 133 c1-r4): provisioning's OTHER read refusals —
    /// <c>container_ownership_unreadable</c> and <c>resume_creator_unavailable</c> — classify by this same rule, so it is
    /// the one place the rule is written.
    /// </remarks>
    internal static bool IsRefusedRead(Exception ex) =>
        ex is HttpRequestException
        {
            StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        };

    private static CascadeReadFailure FailureOf(Exception ex) =>
        IsRefusedRead(ex) ? CascadeReadFailure.Refused : CascadeReadFailure.Unreadable;

    private readonly record struct ChildOwnerRead(bool Exists, DataversePrincipalRef? Owner);

    /// <summary>One child row as read: its id (from its own id column) and its owning user or team.</summary>
    private sealed class ChildOwnerRow
    {
        [JsonPropertyName(OwningUserColumn)]
        public Guid? OwningUser { get; set; }

        [JsonPropertyName(OwningTeamColumn)]
        public Guid? OwningTeam { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }

        public DataversePrincipalRef? Owner =>
            OwningUser is { } user && user != Guid.Empty ? DataversePrincipalRef.User(user)
            : OwningTeam is { } team && team != Guid.Empty ? DataversePrincipalRef.Team(team)
            : null;

        public Guid? IdFrom(string idColumn) =>
            Extra is not null
            && Extra.TryGetValue(idColumn, out var value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out var id)
                ? id
                : null;
    }
}

/// <summary>How a cascade child table is read by <see cref="AssignCascadeChildOwners.SnapshotAsync"/>.</summary>
public enum CascadeChildRead
{
    /// <summary>The table carries no owner (<c>team</c> is business-owned): an Assign cannot re-own it. Never read.</summary>
    NoOwner,

    /// <summary>Stored, owner-bearing: always read.</summary>
    Always,

    /// <summary>
    /// Owner-bearing but listed from SharePoint through the root's document locations (<c>sharepointdocument</c>): read
    /// only when the root has at least one.
    /// </summary>
    UnderDocumentLocations
}

/// <summary>One table an Assign of a secure root cascades to.</summary>
public sealed record CascadeChildTable(string LogicalName, string EntitySet, string IdColumn, CascadeChildRead Read);

/// <summary>One child row, with the owner it had when the snapshot was taken.</summary>
public sealed record CascadeChild(string LogicalName, string EntitySet, string IdColumn, Guid Id, DataversePrincipalRef Owner)
{
    /// <summary>The <c>ownerid@odata.bind</c> value that puts this child back on its snapshotted owner.</summary>
    public string OwnerBind => $"/{Owner.Kind.ToEntitySet()}({Owner.Id})";

    /// <summary>
    /// The Web API call an administrator makes to put this child back by hand — what a failed restore names (owner round
    /// 10 item 4: "with the next call to make"). Equivalent to Assign on the row in the model-driven app.
    /// </summary>
    public string RestoreCall =>
        $"PATCH /api/data/v9.2/{EntitySet}({Id}) {{\"ownerid@odata.bind\":\"{OwnerBind}\"}}";
}

/// <summary>Why a snapshot could not be taken.</summary>
public enum CascadeReadFailure
{
    /// <summary>The snapshot was read.</summary>
    None,

    /// <summary>A read failed in a way the next call may not repeat (anything but a 400, 401 or 403).</summary>
    Unreadable,

    /// <summary>
    /// Deterministic: Dataverse refused the read (400), or refused the service's sign-in or Read privilege for it (401,
    /// 403); a full page was returned; or a row came back without an id or an owner. Calling again repeats it.
    /// </summary>
    Refused
}

/// <summary>The children an Assign of one root re-owns, each with its own owner before the Assign.</summary>
public sealed record CascadeChildSnapshot(string RootLogicalName, Guid RootId, IReadOnlyList<CascadeChild> Children)
{
    /// <summary>
    /// The children whose own owner is NOT <paramref name="owner"/> — the ones a cascading move to that owner leaves with
    /// an owner that is not theirs.
    /// </summary>
    public IReadOnlyList<CascadeChild> NotOwnedBy(DataversePrincipalRef owner) =>
        Children.Where(c => c.Owner != owner).ToList();
}

/// <summary>A snapshot, or the table that could not be read completely and why.</summary>
public sealed record CascadeSnapshotResult(
    CascadeChildSnapshot? Snapshot, CascadeChildTable? FailedTable, CascadeReadFailure Failure, Exception? Fault)
{
    public static CascadeSnapshotResult Read(CascadeChildSnapshot snapshot) =>
        new(snapshot, null, CascadeReadFailure.None, null);

    public static CascadeSnapshotResult Failed(CascadeChildTable table, CascadeReadFailure failure, Exception? fault) =>
        new(null, table, failure, fault);
}

/// <summary>What happened to one snapshotted child when it was put back.</summary>
public enum CascadeChildRestoreOutcome
{
    /// <summary>It already read as its snapshotted owner: nothing was written.</summary>
    AlreadyOwned,

    /// <summary>Assigned back and read back as its snapshotted owner.</summary>
    Restored,

    /// <summary>
    /// The row did not exist when read before the restore: there is nothing to put back. Decided only by that read — a
    /// row that disappears after its PATCH is <see cref="Refused"/> or <see cref="NotApplied"/>.
    /// </summary>
    Gone,

    /// <summary>
    /// Dataverse refused the assignment, and the row does not read back as its snapshotted owner (or no longer reads).
    /// </summary>
    Refused,

    /// <summary>
    /// The assignment was accepted, and the row still does not read back as its snapshotted owner (or no longer reads).
    /// </summary>
    NotApplied,

    /// <summary>The assignment was sent and the row could not be read back: unknown.</summary>
    Unverified,

    /// <summary>The row could not be read before putting it back: nothing was written.</summary>
    Unreadable
}

/// <summary>One child's restore: the child, what happened, and the owner it was found with (when read).</summary>
public sealed record CascadeChildRestore(CascadeChild Child, CascadeChildRestoreOutcome Outcome, DataversePrincipalRef? FoundOwner)
{
    /// <summary>
    /// True when the child ends on its snapshotted owner (read), or did not exist when read before the restore. An
    /// <c>Unreadable</c> or <c>Unverified</c> child is NOT back: whether it is on its owner is unknown.
    /// </summary>
    public bool IsBack => Outcome is CascadeChildRestoreOutcome.AlreadyOwned
        or CascadeChildRestoreOutcome.Restored
        or CascadeChildRestoreOutcome.Gone;
}

/// <summary>Every snapshotted child's restore.</summary>
public sealed record CascadeRestoreReport(IReadOnlyList<CascadeChildRestore> Children)
{
    /// <summary>The children that are NOT back on their snapshotted owner — each one a failure to report.</summary>
    public IReadOnlyList<CascadeChildRestore> NotRestored => Children.Where(c => !c.IsBack).ToList();

    /// <summary>True when every snapshotted child is back on its own owner (or gone).</summary>
    public bool AllRestored => Children.All(c => c.IsBack);
}
