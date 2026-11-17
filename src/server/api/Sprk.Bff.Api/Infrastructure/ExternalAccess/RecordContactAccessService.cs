// spaarke-SPA-external-access-platform-r4 task 014a — "which contacts have EFFECTIVE external access to this root record now".
//
// The record-centric mirror of the contact plane in AccessibleRecordSetService (which answers the other direction: "which
// records may this contact see"). Written to uac-r2's review of R4 task 014, Q2 (projects/spaarke-SPA-external-access-
// platform-r4/notes/task-014-notes.md, "uac-r2 review"): it is the ONE owner of this computation, beside the evaluator, so the
// assignable-people route (014b) and any later consumer never re-derive it route-locally.
//
// Every rule is answered by a reader that already exists; this class only orders them and folds their answers:
//   flags (Restricted / direct-only / inactive)  → ExternalParticipationService.GetEffectiveRootRecordFlagsAsync
//   active, unexpired grant rows                 → ExternalGrantLifecycle.QueryActiveRowsForRootAsync + ConfersAccessOn
//   a direct row naming a firm (ISS-026)          → ExternalGrantLifecycle.GrantFirmIsActiveAsync (the firm must be active)
//   organisation grant → its members             → ExternalParticipationService.FindWallMemberContactsAsync (superset),
//                                                   narrowed by the evaluator's CONFERRING membership set
//                                                   (RequestAccessMemo.ReadOrganizationMembershipsAsync — the evaluator's read)
//   No Access list (contact, its orgs, ancestors) → IAccessibleRecordSetService.CheckGranteeNoAccessAsync
//                                                   (the evaluator's ResolveDenyVetoAsync over INoAccessListReader)
//   inactive contact                              → ExternalParticipationService.ReadContactStateAsync
//   removed marker                                → IContactIdentityStore.ReadRemovedMarkerAsync
//
// FAIL CLOSED, NEVER PARTIAL. Any read that cannot be completed makes the whole answer Unknown — never a Known list with
// someone missing, and never an empty Known on a fault (the RecordGrantsEndpoint precedent: an empty list means "nobody").

using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// One contact with effective external access to a root record: its id and display name (Dataverse's formatted value, or
/// <c>fullname</c>). Never an email, UPN or any other identifier.
/// </summary>
/// <param name="ContactId">The contact's id.</param>
/// <param name="Name">The contact's display name; empty when Dataverse holds none.</param>
public sealed record RecordAccessContact(Guid ContactId, string Name);

/// <summary>Why <see cref="RecordContactAccessService"/> could not decide who has access (fixed values, safe to log).</summary>
public enum RecordContactAccessUnknownReason
{
    /// <summary>The record's effective flags (Secure, Access Permission, state, its filing) could not be read.</summary>
    FlagsUnreadable,

    /// <summary>The record's grant rows could not be read.</summary>
    GrantsUnreadable,

    /// <summary>The record has more active grant rows than one read takes, so the rows read are not all of them.</summary>
    GrantsOverBound,

    /// <summary>A grant row came back without its contact or organization column, so its grantee is unknown.</summary>
    GrantRowUnreadable,

    /// <summary>The members of an organization holding a grant could not be read.</summary>
    OrganizationMembersUnreadable,

    /// <summary>More contacts would be evaluated than one answer may cover.</summary>
    TooManyContacts,

    /// <summary>A candidate contact's state could not be read.</summary>
    ContactStateUnreadable,

    /// <summary>A candidate contact's removed marker could not be proven.</summary>
    RemovedMarkerUnreadable,

    /// <summary>A candidate contact's organization memberships could not be read.</summary>
    MembershipsUnreadable,

    /// <summary>The No Access check for a candidate contact could not be completed.</summary>
    NoAccessUnverifiable,

    /// <summary>The display names of the contacts with access could not be read.</summary>
    NamesUnreadable,

    /// <summary>A read about a candidate contact faulted in a way none of its readers reported as an answer.</summary>
    CandidateUnreadable,

    /// <summary>Whether the firm a candidate's direct grant names is active could not be read (ISS-026).</summary>
    FirmUnreadable,
}

/// <summary>
/// The answer to "which contacts have effective external access to this record now": either the complete list
/// (<see cref="Known"/>) or <see cref="Unknown"/> — never a partial list.
/// </summary>
public abstract record RecordContactAccess
{
    private RecordContactAccess()
    {
    }

    /// <summary>Every contact with effective access, ordered by name. Empty means "nobody", never "could not tell".</summary>
    public sealed record Known(IReadOnlyList<RecordAccessContact> Contacts) : RecordContactAccess;

    /// <summary>A read could not be completed; nothing about who has access may be shown or acted on.</summary>
    public sealed record Unknown(RecordContactAccessUnknownReason Reason) : RecordContactAccess;
}

/// <summary>
/// Computes the contacts with EFFECTIVE external access to one root record (project, matter or work assignment) — uac-r2's
/// rules for R4 task 014 Q2, through the evaluator's own readers.
/// </summary>
/// <remarks>
/// <para><b>Rules, in order</b> (uac-r2 Q2):</para>
/// <list type="number">
/// <item>The record's EFFECTIVE flags first (its own and those of everything it is filed under — the evaluator's fold). Restricted,
/// or an inactive record, removes every contact-sourced contribution: <see cref="RecordContactAccess.Known"/> with nobody
/// (<see cref="RootRecordFlags.RemovesContactSourcedAccess"/>). Unreadable flags are a fault, not "Restricted".</item>
/// <item>The record's ACTIVE grant rows (the grant core's row read), each conferring only on the read path's UTC-today rule
/// (<see cref="ExternalParticipationService.ConfersAccessOn"/>) and only at a level that carries Read (owner 2026-09-30: no
/// level = not granted).</item>
/// <item>A contact-keyed row names its contact directly; when it also names a firm it confers only while that firm is active
/// (ISS-026, <see cref="ExternalGrantLifecycle.GrantFirmIsActiveAsync"/> — the read path's <c>GrantOrganizationConfers</c>
/// guard; each firm is read at most once per answer). An organization row (no contact) reaches the organization's members
/// whose membership CONFERS today (date-bounded, active organization — the evaluator's org-grant term reads exactly that set)
/// — EXCEPT on a direct-only record (Secure or Limited, <see cref="RootRecordFlags.IsDirectOnly"/>, the evaluator's ONE
/// suppression predicate), where only named direct contact grants count.</item>
/// <item>Each candidate is then removed when it is inactive, carries an administrator's removed marker, or is on the record's
/// No Access list — as itself, through any organization it belongs to (the wall set), or through a secure record the record
/// is filed under.</item>
/// </list>
/// <para><b>Deliberately left out: the workforce standing-grant and derived-member terms</b> (the evaluator's contact-plane
/// standing-grant membership and organization expansion — <see cref="AccessibleRecordSetService"/>, which runs them on the
/// ContactOnly plane only). Those terms only ADD access, so leaving them out can only under-list: the answer fails closed,
/// never over-lists. They cannot be included because this service cannot tell which plane a contact signs in on, and including
/// them would list contacts whose CIAM sign-in never receives that access (an over-list). Consequently a caller asking "am I on
/// this list" (<c>isMe</c>) is not found when its only access to the record comes from one of those terms.</para>
/// <para><b>Fail closed, never partial</b> (uac-r2 Q2.6). A thrown read, an unreadable answer, an over-bound read or an
/// unverifiable No Access check answers <see cref="RecordContactAccess.Unknown"/>. Only the caller's own cancellation
/// propagates.</para>
/// <para><b>Names only</b> (Q2.7). The answer carries a contact id and a display name; nothing else is read or returned.
/// This class's own logs carry fixed labels and counts — no record, contact or organization id, no tenant, oid, UPN or email.
/// (The evaluator's shared readers it calls log a failed read as the evaluator always has, with the contact id.)</para>
/// <para><b>Once per request.</b> The contact state, removed-marker and membership reads go through the request's
/// <see cref="RequestAccessMemo"/> through the SAME shared reads the evaluator uses (<see cref="ExternalParticipationService.ReadContactStateAsync"/>,
/// <see cref="RequestAccessMemo.ReadRemovedMarkerAsync"/>, <see cref="RequestAccessMemo.ReadOrganizationMembershipsAsync"/>),
/// so a request that also composes the caller shares them.</para>
/// <para><b>CLAUDE.md §11.</b> <i>Existing</i>: <see cref="AccessibleRecordSetService"/> composes access per CONTACT (contact →
/// records); <c>RecordGrantsEndpoint</c> lists raw grant rows, lapsed ones included. <i>Extension</i>: neither answers per
/// RECORD over effective access — the evaluator would have to be run once per candidate over that contact's whole grant set,
/// and its set cannot tell a fault from "no access" (a faulted read simply removes records), so the list could silently lose
/// someone; the grant listing ignores organization grants' members, the No Access list and contact state. <i>Cost of doing
/// nothing</i>: the assignable-people route (014b) would compute it route-locally from raw grant rows and offer a walled,
/// removed, inactive or expired contact as an assignee (uac-r2 Q2.8 asks for one owner here).</para>
/// </remarks>
public class RecordContactAccessService
{
    /// <summary>
    /// The most active grant rows one answer reads — the bound <c>SetRecordShareExpiryEndpoint.MaxSharesPerRecord</c> and
    /// <c>RecordGrantsEndpoint</c> use for the same rows. One more is asked for, so an over-bound record is
    /// <see cref="RecordContactAccessUnknownReason.GrantsOverBound"/>, never silently truncated
    /// (<see cref="DataverseWebApiClient.QueryAsync{T}"/> reads ONE page).
    /// </summary>
    internal const int MaxGrantRows = 1000;

    /// <summary>
    /// The most candidate contacts one answer evaluates (per organization's members, and in total) — the organization sweep's
    /// bound (<see cref="ExternalOrganizationMembership.MaxMembersPerSweep"/>). Beyond it the answer is
    /// <see cref="RecordContactAccessUnknownReason.TooManyContacts"/>, never a truncated list. A guard rail: the largest live
    /// organization had one member (2026-08-26).
    /// </summary>
    internal const int MaxContacts = ExternalOrganizationMembership.MaxMembersPerSweep;

    /// <summary>Contact ids per name read, so the <c>$filter</c> stays well inside Dataverse's URL limit.</summary>
    internal const int NameReadChunkSize = 50;

    /// <summary>Candidates evaluated at once (each is a handful of independent reads).</summary>
    private const int MaxConcurrentContactChecks = 8;

    private readonly DataverseWebApiClient _dataverse;
    private readonly ExternalParticipationService _participations;
    private readonly IAccessibleRecordSetService _evaluator;
    private readonly IContactIdentityStore _identityStore;
    private readonly TimeProvider _timeProvider;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<RecordContactAccessService> _logger;

    /// <param name="dataverse">The app-only Web API client the grant core reads grant rows through.</param>
    /// <param name="participations">Flags, organization members and memberships, contact state.</param>
    /// <param name="evaluator">The evaluator's write-time No Access entry point (the read path's deny veto).</param>
    /// <param name="identityStore">The removed-marker read.</param>
    /// <param name="timeProvider">UTC today for the expiry rule.</param>
    /// <param name="httpContextAccessor">The request whose <see cref="RequestAccessMemo"/> the reads share; none outside a
    /// request (every read is live).</param>
    /// <param name="logger">Logger (fixed labels, no ids).</param>
    public RecordContactAccessService(
        DataverseWebApiClient dataverse,
        ExternalParticipationService participations,
        IAccessibleRecordSetService evaluator,
        IContactIdentityStore identityStore,
        TimeProvider timeProvider,
        IHttpContextAccessor? httpContextAccessor,
        ILogger<RecordContactAccessService> logger)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(participations);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(identityStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _dataverse = dataverse;
        _participations = participations;
        _evaluator = evaluator;
        _identityStore = identityStore;
        _timeProvider = timeProvider;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// The contacts with effective external access to <paramref name="rootId"/> of <paramref name="rootType"/> now, or
    /// <see cref="RecordContactAccess.Unknown"/> when any read could not be completed.
    /// </summary>
    /// <remarks>Never throws for a read fault; only the caller's cancellation propagates. An empty id is a caller bug.
    /// <c>virtual</c> per ADR-038 section 4 (substitution seam): the external event routes' contract tests (R4 task 014b) answer it
    /// at this module boundary; the rules themselves are pinned here by <c>RecordContactAccessServiceTests</c>.</remarks>
    public virtual async Task<RecordContactAccess> GetContactsWithAccessAsync(
        ExternalGrantRootType rootType, Guid rootId, CancellationToken ct)
    {
        if (rootId == Guid.Empty)
        {
            throw new ArgumentException("rootId must be a record id.", nameof(rootId));
        }

        var entityType = ExternalGrantRoot.LogicalNameFor(rootType);

        // ── Rule 3: the record's EFFECTIVE flags, first (a Restricted record reads no grants) ─────────────────────────
        RootRecordFlags flags;
        try
        {
            var read = await _participations
                .GetEffectiveRootRecordFlagsAsync(entityType, new[] { rootId }, ct).ConfigureAwait(false);
            flags = read.TryGetValue(rootId, out var f) ? f : RootRecordFlags.Unreadable;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Unknown(RecordContactAccessUnknownReason.FlagsUnreadable, rootType, ex);
        }

        // The flag reader answers every fault — the caller's own cancellation included — as Unreadable. A client abort is
        // not an unreadable record: let it propagate instead of logging FlagsUnreadable.
        ct.ThrowIfCancellationRequested();

        if (flags.IsUnreadable)
        {
            return Unknown(RecordContactAccessUnknownReason.FlagsUnreadable, rootType);
        }

        if (flags.RemovesContactSourcedAccess)
        {
            // Restricted (FR-21) or an inactive record (task 137): nothing contact-sourced survives — a decided "nobody".
            _logger.LogInformation(
                "[RECORD-CONTACTS] The {RootType} is Restricted or inactive: no contact has external access to it.", rootType);
            return new RecordContactAccess.Known(Array.Empty<RecordAccessContact>());
        }

        // ── Rule 1: the record's ACTIVE grant rows, conferring today ──────────────────────────────────────────────────
        List<ExternalGrantRow> rows;
        try
        {
            rows = await ExternalGrantLifecycle.QueryActiveRowsForRootAsync(
                _dataverse, rootType, rootId, top: MaxGrantRows + 1, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Unknown(RecordContactAccessUnknownReason.GrantsUnreadable, rootType, ex);
        }

        if (rows.Count > MaxGrantRows)
        {
            return Unknown(RecordContactAccessUnknownReason.GrantsOverBound, rootType);
        }

        var today = ExternalGrantLifecycle.TodayUtc(_timeProvider);
        var directNames = new Dictionary<Guid, string?>();
        var directGrants = new Dictionary<Guid, DirectGrants>();
        var grantingOrganizations = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (!row.IsActive
                || !ExternalParticipationService.ConfersAccessOn(row.ExpiresDate, today)
                || !ExternalAccessLevels.ToAccessRights((ExternalAccessLevel?)row.AccessLevel).HasFlag(AccessRights.Read))
            {
                continue; // inactive, lapsed or undated, or no level: confers nothing (the read path's own rules)
            }

            if (!(row.ContactRead && row.OrganizationRead))
            {
                // Whose grant this is cannot be told; skipping it could drop someone with access.
                return Unknown(RecordContactAccessUnknownReason.GrantRowUnreadable, rootType);
            }

            if (row.ContactId is { } contactId && contactId != Guid.Empty)
            {
                if (!directNames.TryGetValue(contactId, out var known) || string.IsNullOrEmpty(known))
                {
                    directNames[contactId] = row.ContactName;
                }

                if (!directGrants.TryGetValue(contactId, out var grants))
                {
                    directGrants[contactId] = grants = new DirectGrants();
                }

                if (row.OrganizationId is { } firm && firm != Guid.Empty)
                {
                    grants.Firms.Add(firm); // ISS-026: confers only while this firm is active (decided per candidate)
                }
                else
                {
                    grants.Unconditional = true;
                }
            }
            else if (row.OrganizationId is { } organizationId && organizationId != Guid.Empty && !flags.IsDirectOnly)
            {
                // Rule 2's exception: on a direct-only (Secure or Limited) record an organization grant confers nothing.
                grantingOrganizations.Add(organizationId);
            }
        }

        // ── Rule 2: the members an organization grant reaches (candidates; the conferring check is per contact) ────────
        var reachedThrough = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var organizationId in grantingOrganizations)
        {
            IReadOnlyList<Guid> members;
            bool truncated;
            try
            {
                (members, truncated) = await _participations
                    .FindWallMemberContactsAsync(organizationId, MaxContacts, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                return Unknown(RecordContactAccessUnknownReason.OrganizationMembersUnreadable, rootType, ex);
            }

            if (truncated)
            {
                return Unknown(RecordContactAccessUnknownReason.TooManyContacts, rootType);
            }

            foreach (var member in members.Where(id => id != Guid.Empty))
            {
                if (!reachedThrough.TryGetValue(member, out var orgs))
                {
                    reachedThrough[member] = orgs = new HashSet<Guid>();
                }

                orgs.Add(organizationId);
            }
        }

        var candidates = directNames.Keys.Union(reachedThrough.Keys).ToList();
        if (candidates.Count > MaxContacts)
        {
            return Unknown(RecordContactAccessUnknownReason.TooManyContacts, rootType);
        }

        // ── Rules 4 and 5, per candidate: state, removed marker, conferring membership, No Access ─────────────────────
        // Create the request's memo HERE, before the fan-out. RequestAccessMemo.For locks context.Items, but on a request whose
        // Items were never touched that dictionary (and its feature) is created lazily by the first accessor — concurrent first
        // callers can each lock a different object and corrupt it or lose a memo (seen in this class's tests). Afterwards the
        // candidates only find the memo.
        _ = RequestMemo;
        var firmStates = new FirmStateReads(_dataverse, ct);
        var outcomes = new ConcurrentDictionary<Guid, CandidateOutcome>();
        await Parallel.ForEachAsync(
                candidates,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentContactChecks, CancellationToken = ct },
                async (contactId, token) =>
                {
                    outcomes[contactId] = await EvaluateCandidateAsync(
                            entityType, rootId, contactId,
                            directGrants.GetValueOrDefault(contactId),
                            reachedThrough.GetValueOrDefault(contactId),
                            firmStates,
                            token)
                        .ConfigureAwait(false);
                })
            .ConfigureAwait(false);

        var fault = outcomes.Values.FirstOrDefault(o => o.Unknown is not null);
        if (fault.Unknown is { } reason)
        {
            return Unknown(reason, rootType);
        }

        var included = candidates.Where(id => outcomes[id].Included).ToList();

        // ── Rule 7: names only ────────────────────────────────────────────────────────────────────────────────────────
        var unnamed = included
            .Where(id => !directNames.TryGetValue(id, out var name) || string.IsNullOrEmpty(name))
            .ToList();
        IReadOnlyDictionary<Guid, string?> readNames;
        try
        {
            readNames = await ReadContactNamesAsync(unnamed, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Unknown(RecordContactAccessUnknownReason.NamesUnreadable, rootType, ex);
        }

        if (unnamed.Any(id => !readNames.ContainsKey(id)))
        {
            // Active a moment ago, absent now: the two reads disagree — not decided.
            return Unknown(RecordContactAccessUnknownReason.NamesUnreadable, rootType);
        }

        var contacts = included
            .Select(id => new RecordAccessContact(
                id,
                (directNames.TryGetValue(id, out var direct) && !string.IsNullOrEmpty(direct) ? direct : readNames[id])
                ?? string.Empty))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.ContactId)
            .ToList();

        _logger.LogInformation(
            "[RECORD-CONTACTS] {Count} contact(s) have external access to the {RootType} ({Candidates} candidate(s), {Organizations} " +
            "organization grant(s) expanded, direct-only: {DirectOnly}).",
            contacts.Count, rootType, candidates.Count, grantingOrganizations.Count, flags.IsDirectOnly);

        return new RecordContactAccess.Known(contacts);
    }

    /// <summary>One candidate's answer: included, excluded, or the reason it could not be decided.</summary>
    private readonly record struct CandidateOutcome(bool Included, RecordContactAccessUnknownReason? Unknown)
    {
        internal static CandidateOutcome In { get; } = new(true, null);
        internal static CandidateOutcome Out { get; } = new(false, null);
        internal static CandidateOutcome Undecided(RecordContactAccessUnknownReason reason) => new(false, reason);
    }

    /// <summary>A candidate's conferring direct grant rows on the record: one with no firm confers outright
    /// (<see cref="Unconditional"/>); one naming a firm confers only while that firm is active (ISS-026).</summary>
    private sealed class DirectGrants
    {
        internal bool Unconditional { get; set; }

        internal HashSet<Guid> Firms { get; } = new();
    }

    /// <summary>
    /// One answer's firm state reads (ISS-026), shared by its candidates: each firm a direct grant names is point-read at most
    /// once per <see cref="GetContactsWithAccessAsync"/> call, however many candidates' rows name it. Safe under the
    /// candidates' fan-out (<see cref="Lazy{T}"/> runs each read once).
    /// </summary>
    /// <remarks>
    /// Only the READ is shared. Whether a firm is one of the candidate's own CONFERRING organizations (active by
    /// construction — <see cref="ExternalGrantLifecycle.GrantFirmIsActiveAsync"/>'s first rule) is per contact, so it is
    /// answered before this cache and the shared definition is asked with no conferring set, which makes it read. A faulted
    /// read stays a faulted task: every candidate awaiting it sees the same exception (FirmUnreadable) — a fault is never
    /// remembered as active or inactive.
    /// </remarks>
    private sealed class FirmStateReads
    {
        private readonly ConcurrentDictionary<Guid, Lazy<Task<bool>>> _reads = new();
        private readonly DataverseWebApiClient _dataverse;
        private readonly CancellationToken _ct;

        internal FirmStateReads(DataverseWebApiClient dataverse, CancellationToken ct)
        {
            _dataverse = dataverse;
            _ct = ct;
        }

        /// <summary>Whether <paramref name="firm"/> is active (a firm that no longer exists is not). Throws on a fault.</summary>
        internal Task<bool> IsActiveAsync(Guid firm)
            => _reads.GetOrAdd(firm, f => new Lazy<Task<bool>>(() => ExternalGrantLifecycle
                    .GrantFirmIsActiveAsync(f, Array.Empty<Guid>(), _dataverse, _ct)))
                .Value;
    }

    /// <param name="direct">The contact's own conferring grant rows, or <c>null</c> when it has none.</param>
    /// <param name="throughOrganizations">The organizations whose grant reached this contact, or <c>null</c> when none did.</param>
    /// <param name="firmStates">This answer's shared firm state reads.</param>
    private async Task<CandidateOutcome> EvaluateCandidateAsync(
        string entityType, Guid rootId, Guid contactId, DirectGrants? direct, IReadOnlyCollection<Guid>? throughOrganizations,
        FirmStateReads firmStates, CancellationToken ct)
    {
        try
        {
            // Rule 5: an inactive contact confers nothing (read live, memoized per request by the reader itself).
            var state = await _participations.ReadContactStateAsync(contactId, ct).ConfigureAwait(false);
            if (state == ContactRecordState.Unreadable)
            {
                return CandidateOutcome.Undecided(RecordContactAccessUnknownReason.ContactStateUnreadable);
            }

            if (state != ContactRecordState.Active)
            {
                return CandidateOutcome.Out;
            }

            // Rule 5: an administrator's removal confers nothing, whatever the contact's state.
            var marker = await RequestAccessMemo
                .ReadRemovedMarkerAsync(RequestMemo, _identityStore, _logger, contactId, ct).ConfigureAwait(false);
            if (marker == RemovedMarkerRead.Removed)
            {
                return CandidateOutcome.Out;
            }

            if (marker != RemovedMarkerRead.NotRemoved)
            {
                return CandidateOutcome.Undecided(RecordContactAccessUnknownReason.RemovedMarkerUnreadable);
            }

            // A direct row with no firm confers outright. Otherwise the contact's CONFERRING memberships decide: a direct row's
            // firm (ISS-026: it must be active — a conferring firm is by construction) and an organization grant (rule 2: it
            // reaches only a member whose membership confers today, the org-grant term's own set).
            var confers = direct?.Unconditional == true;
            if (!confers)
            {
                var memberships = await RequestAccessMemo
                    .ReadOrganizationMembershipsAsync(RequestMemo, _participations, _logger, contactId, ct)
                    .ConfigureAwait(false);
                if (memberships.Unreadable)
                {
                    return CandidateOutcome.Undecided(RecordContactAccessUnknownReason.MembershipsUnreadable);
                }

                foreach (var firm in direct?.Firms ?? Enumerable.Empty<Guid>())
                {
                    bool active;
                    try
                    {
                        active = memberships.ConferringOrganizationIds.Contains(firm)
                            || await firmStates.IsActiveAsync(firm).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "[RECORD-CONTACTS] The state of a firm a direct grant names could not be read.");
                        return CandidateOutcome.Undecided(RecordContactAccessUnknownReason.FirmUnreadable);
                    }

                    if (active)
                    {
                        confers = true;
                        break;
                    }
                }

                confers = confers
                    || (throughOrganizations is not null
                        && memberships.ConferringOrganizationIds.Any(throughOrganizations.Contains));
            }

            if (!confers)
            {
                return CandidateOutcome.Out;
            }

            // Rule 4: the evaluator's deny veto — the contact, its organizations (wall set), the record and every secure
            // record it is filed under. The contact's own memberships are read inside, from the same memo key.
            var noAccess = await _evaluator
                .CheckGranteeNoAccessAsync(entityType, rootId, contactId, Array.Empty<Guid>(), ct).ConfigureAwait(false);
            return noAccess switch
            {
                NoAccessCheckAnswer.Allowed => CandidateOutcome.In,
                NoAccessCheckAnswer.Denied => CandidateOutcome.Out,
                _ => CandidateOutcome.Undecided(RecordContactAccessUnknownReason.NoAccessUnverifiable),
            };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[RECORD-CONTACTS] A candidate contact's access could not be decided; the answer is Unknown.");
            return CandidateOutcome.Undecided(RecordContactAccessUnknownReason.CandidateUnreadable);
        }
    }

    /// <summary>The request's memo the per-contact reads share with the evaluator (the same keys and remember rules — the
    /// shared reads on <see cref="RequestAccessMemo"/>); none outside a request.</summary>
    private RequestAccessMemo? RequestMemo => RequestAccessMemo.For(_httpContextAccessor?.HttpContext);

    /// <summary>
    /// The display names (<c>fullname</c>) of <paramref name="contactIds"/>, batched. Selects the id and the name ONLY. Throws on
    /// a fault; a contact the read did not return is absent from the answer.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string?>> ReadContactNamesAsync(
        IReadOnlyList<Guid> contactIds, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        for (var i = 0; i < contactIds.Count; i += NameReadChunkSize)
        {
            var chunk = contactIds.Skip(i).Take(NameReadChunkSize).ToList();
            var rows = await _dataverse.QueryAsync<ContactNameRow>(
                "contacts",
                filter: string.Join(" or ", chunk.Select(id => $"contactid eq {id}")),
                select: "contactid,fullname",
                top: chunk.Count + 1,
                cancellationToken: ct).ConfigureAwait(false);

            foreach (var row in rows)
            {
                if (row.ContactId is { } id && chunk.Contains(id))
                {
                    names[id] = row.FullName;
                }
            }
        }

        return names;
    }

    private RecordContactAccess Unknown(RecordContactAccessUnknownReason reason, ExternalGrantRootType rootType, Exception? ex = null)
    {
        _logger.LogWarning(ex,
            "[RECORD-CONTACTS] The contacts with external access to the {RootType} could not be decided ({Reason}); answering " +
            "Unknown (fail closed — never a partial list).", rootType, reason);
        return new RecordContactAccess.Unknown(reason);
    }

    private sealed class ContactNameRow
    {
        [JsonPropertyName("contactid")]
        public Guid? ContactId { get; set; }

        [JsonPropertyName("fullname")]
        public string? FullName { get; set; }
    }
}
