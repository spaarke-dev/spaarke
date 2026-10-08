namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

// unified-access-control-r2 task 064 (owner round 59 item 3) — the contract of GET /api/v1/records/{type}/{id}/no-access,
// frozen for its two consumers: task 153 (the form banner reads the two signals) and task 067 (Manage Access reads the
// entries). Contract note: projects/unified-access-control-r2/notes/phase4-access-report-contract.md.

/// <summary>The three states of one access signal. A signal that could not be established is <see cref="Unknown"/>,
/// never <see cref="DoesNotApply"/>.</summary>
public static class AccessSignalState
{
    /// <summary>The restriction applies to the record.</summary>
    public const string Applies = "applies";

    /// <summary>The restriction was read and does not apply.</summary>
    public const string DoesNotApply = "doesNotApply";

    /// <summary>Whether it applies could not be read. A client shows "access status unavailable", never "not restricted".</summary>
    public const string Unknown = "unknown";
}

/// <summary>Whether <see cref="RecordNoAccessStatus.Entries"/> carries the entry list, and how complete it is.</summary>
public static class NoAccessEntriesState
{
    /// <summary>The caller holds Read but not Write on the record, so the entries are not shown (owner O2). <c>entries</c> is null.</summary>
    public const string NotShown = "notShown";

    /// <summary>Every active entry covering the record is listed.</summary>
    public const string Complete = "complete";

    /// <summary>More entries cover the record than one read lists; the list is a prefix and says so.</summary>
    public const string Truncated = "truncated";

    /// <summary>The entries could not be read. <c>entries</c> is null; a client shows an error, never "no entries".</summary>
    public const string Unavailable = "unavailable";
}

/// <summary>Why a listed entry is not in force on the record (<see cref="RecordNoAccessEntry.NotInForceReason"/>).</summary>
public static class NoAccessEntryNotInForceReason
{
    /// <summary>The entry fails the well-formedness rule: it walls nobody off. <c>inForce</c> is false.</summary>
    public const string Malformed = "malformed";

    /// <summary>A USER wall on a record that is not Secure: user walls bind only Secure records (owner Q4). <c>inForce</c> is false.</summary>
    public const string UserWallOnNonSecureRecord = "userWallOnNonSecureRecord";

    /// <summary>A user wall on a record whose Secure flag could not be read: whether it binds is unknown. <c>inForce</c> is null.</summary>
    public const string SecureStateUnknown = "secureStateUnknown";
}

/// <summary>
/// The access status of ONE project, matter or work assignment, for a caller who holds Read on it.
/// </summary>
/// <param name="RecordType">The table logical name the route named (<c>sprk_project</c>, <c>sprk_matter</c>, <c>sprk_workassignment</c>).</param>
/// <param name="RecordId">The record, echoed so a client can check the answer is about the record it asked for.</param>
/// <param name="Secure">Whether the record is Secure (<c>sprk_issecure</c>): an <see cref="AccessSignalState"/> value.</param>
/// <param name="NoAccess">Whether any active No Access entry IN FORCE on the record covers it (its own entries, walls over
/// organizations it references, and the same for every secure record it is filed under): an <see cref="AccessSignalState"/>
/// value.</param>
/// <param name="EntriesState">A <see cref="NoAccessEntriesState"/> value.</param>
/// <param name="Entries">The covering entries, for a caller who also holds Write on the record; otherwise null. Never the
/// entry's Reason.</param>
public sealed record RecordNoAccessStatus(
    string RecordType,
    Guid RecordId,
    string Secure,
    string NoAccess,
    string EntriesState,
    IReadOnlyList<RecordNoAccessEntry>? Entries);

/// <summary>
/// One active No Access entry covering the record (shown to a caller with Write on the record only).
/// </summary>
/// <param name="EntryId">The <c>sprk_noaccessentry</c> id.</param>
/// <param name="Name">The entry's name (<c>sprk_name</c>).</param>
/// <param name="SubjectKind"><c>contact</c>, <c>organization</c> or <c>systemuser</c>; null when the entry names none or
/// several (it is then <paramref name="Malformed"/>).</param>
/// <param name="SubjectId">Who is walled off: the subject contact, organization or systemuser.</param>
/// <param name="SubjectName">The subject's display name, as Dataverse formats the lookup.</param>
/// <param name="ObjectKind"><c>record</c> (the entry names a record) or <c>organization</c> (a wall over an organization
/// the covered record references); null when malformed.</param>
/// <param name="ObjectOrganizationId">The walled organization, for an <c>organization</c> object.</param>
/// <param name="ObjectOrganizationName">Its display name.</param>
/// <param name="CoveredRecordType">The record the entry covers this one through: this record, or a secure record it is
/// filed under.</param>
/// <param name="CoveredRecordId">That record's id.</param>
/// <param name="ViaSecureParent">True when the entry covers this record because it covers a secure record this one is
/// filed under (round 61: a parent's list reaches every secure record filed below it).</param>
/// <param name="AlsoViaSecureParent">True when the entry reaches this record on its listed path AND (again) through a secure
/// record it is filed under, e.g. this record and its secure parent both reference the walled organization. A user wall
/// is then in force whatever this record's own Secure flag reads.</param>
/// <param name="Malformed">The entry fails the well-formedness rule (schema Business Rule 1, task 154's canonical id): it
/// walls nobody off and does not count toward <see cref="RecordNoAccessStatus.NoAccess"/>. Listed so it can be fixed.</param>
/// <param name="InForce">Whether the entry walls anyone off THIS record: true; false (see <paramref name="NotInForceReason"/>,
/// e.g. a user wall on a record that is not Secure); null when that could not be read. Only an entry in force makes
/// <see cref="RecordNoAccessStatus.NoAccess"/> apply.</param>
/// <param name="NotInForceReason">A <see cref="NoAccessEntryNotInForceReason"/> value when <paramref name="InForce"/> is not true.</param>
/// <param name="ModifiedById">The entry's last modifier (the author owner N5 checks).</param>
/// <param name="ModifiedByName">Their display name.</param>
/// <param name="ModifiedOn">When the entry last changed.</param>
public sealed record RecordNoAccessEntry(
    Guid EntryId,
    string? Name,
    string? SubjectKind,
    Guid? SubjectId,
    string? SubjectName,
    string? ObjectKind,
    Guid? ObjectOrganizationId,
    string? ObjectOrganizationName,
    string CoveredRecordType,
    Guid CoveredRecordId,
    bool ViaSecureParent,
    bool AlsoViaSecureParent,
    bool Malformed,
    bool? InForce,
    string? NotInForceReason,
    Guid? ModifiedById,
    string? ModifiedByName,
    DateTimeOffset? ModifiedOn);
