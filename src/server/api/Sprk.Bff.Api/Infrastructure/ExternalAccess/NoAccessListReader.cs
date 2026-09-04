using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

// unified-access-control-r2 Task 038 (FR-23) — the deny-list STORE + READER only.
//
// design.md §5 / spec FR-23: a deny list vetoes access AFTER the additive max, keyed by
//     (contact | organization) subject  x  (organization | specific record) object
// A contact on the No Access List for organization X is denied on EVERY record referencing X —
// even holding an explicit Full Access grant. The same table serves per-child revocation: a
// subject x specific-record entry denies exactly that one record without touching its parent.
//
// Task 039 (NOT this task) wires the result into AccessibleRecordSetService.ApplyVetoPipeline
// Slot 1 — the deny-list slot that is currently a documented no-op. This file delivers the STORE
// (src/solutions/SpaarkeCore/entities/sprk_noaccessentry/entity-schema.md) and the READER only;
// AccessibleRecordSetService.cs is deliberately NOT touched here.

/// <summary>
/// One candidate record to evaluate against the deny list: its own id plus every organization id
/// the record itself references (matter/project organization lookups, etc.). Resolving WHICH
/// organizations a record references is the CALLER's responsibility (task 039 decides how the
/// evaluator supplies it) — this reader is deliberately agnostic to that resolution, per the
/// task's own notes: "keep the reader agnostic."
/// </summary>
/// <param name="EntityLogicalName">Retained for provenance/logging only. Matching against
/// <see cref="RecordId"/> does NOT additionally require this to equal the matched entry's object
/// record type — see <see cref="NoAccessListReader"/> remarks for why id-only matching is safe.</param>
/// <param name="RecordId">The candidate record's own id.</param>
/// <param name="ReferencedOrganizationIds">Every organization this record references, in any
/// lookup slot — the ethical-wall match is deliberately ANY-reference, not conferring-only
/// (over-matching a deny is the specified behavior; spec FR-23 / register B-10).</param>
public sealed record NoAccessCandidateRecord(
    string EntityLogicalName,
    Guid RecordId,
    IReadOnlyCollection<Guid> ReferencedOrganizationIds);

/// <summary>
/// The outcome of evaluating a candidate-record batch against the active deny list.
/// </summary>
public sealed class NoAccessListResult
{
    /// <summary>The subset of the queried candidate record ids that are denied.</summary>
    public required IReadOnlySet<Guid> DeniedRecordIds { get; init; }

    /// <summary>
    /// <c>recordId -&gt; the sprk_noaccessentryid(s) that matched</c> (usually one; more than one
    /// is possible — e.g. a contact-subject entry AND an organization-subject entry both deny the
    /// same record). Empty for every id when <see cref="FailedClosed"/> is <c>true</c>: no real
    /// entry matched, the denial is precautionary, not provable.
    /// </summary>
    public required IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> DenyingEntryIds { get; init; }

    /// <summary>
    /// <c>true</c> when EVERY id in <see cref="DeniedRecordIds"/> is denied because the deny-list
    /// read itself faulted (NFR-01), not because a real entry matched. Lets a caller or telemetry
    /// distinguish a provable deny from "the read failed and we could not prove this wasn't
    /// denied" — both are correctly DENIES, but they are not the same kind of fact.
    /// </summary>
    public bool FailedClosed { get; init; }

    /// <summary>No subject identity, or no candidates, to evaluate. Not a fail-closed outcome —
    /// a considered "nothing to check" answer, distinct from a faulted read.</summary>
    public static NoAccessListResult Empty { get; } = new()
    {
        DeniedRecordIds = new HashSet<Guid>(),
        DenyingEntryIds = new Dictionary<Guid, IReadOnlyList<Guid>>(),
        FailedClosed = false,
    };
}

/// <summary>
/// Fail-closed reader for the FR-23 deny list (<c>sprk_noaccessentry</c>). Answers "which of these
/// candidate records are denied for this principal's identities" in bounded, batched queries
/// (NFR-02). Store + reader only — task 039 wires the result into
/// <c>AccessibleRecordSetService.ApplyVetoPipeline</c> Slot 1, which today removes nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fail direction is the mirror image of <see cref="ContactStandingGrantReader"/>, deliberately.</b>
/// That reader answers a single yes/no ADDITIVE question and fails closed toward <c>false</c> ("no
/// standing grant") — an unreadable term must contribute NOTHING, because it only ever widens
/// access. THIS reader answers a VETO question, so its fail-closed direction is the opposite: an
/// unreadable deny-list must contribute DENIAL, never "no denies" (spec NFR-01: "a faulted
/// deny-list read returns a DENY-ALL answer for the queried candidates... deny-list unreadable ⇒
/// cannot prove not-denied"). Both directions are correctly "fail closed" — which one is safe
/// depends on whether the term being read is additive or a veto. See
/// <see cref="NoAccessListResult.FailedClosed"/> for how that outcome is surfaced.
/// </para>
/// <para>
/// <b>A veto is never a level.</b> This reader returns denied RECORD IDS, never an
/// <c>AccessRights</c> value — there is no value in this codebase that means "denied"
/// (root CLAUDE.md §5 fact 5; <c>AccessibleRecordSet.Rights</c> doc comment). The caller (task 039)
/// is expected to REMOVE the denied ids from the composed rights map, never write a low value.
/// </para>
/// <para>
/// <b>No caching, deliberately</b> — unlike <c>ExternalParticipationService</c>'s 60s grant-set
/// cache. This mirrors <c>ExternalParticipationService.GetRootRecordFlagsAsync</c> (the Secure /
/// Restricted veto-flag reader, task 037), which is also read live per request: an ethical wall or
/// a per-child revocation is exactly the kind of change that should take effect on the NEXT
/// request, not after a TTL window during which the walled-off party could still see the record.
/// </para>
/// <para>
/// <b>Broker-only (ADR-010 / NFR-02).</b> Reads Dataverse app-only via its own typed
/// <see cref="HttpClient"/> + <see cref="TokenCredential"/> — the established style of every OTHER
/// QUERY-shaped reader in this module (<c>ExternalParticipationService</c>,
/// <c>ModuleEntitlementResolver</c>), as opposed to <see cref="ContactStandingGrantReader"/>'s
/// single retrieve-by-id via the shared <c>IDataverseService</c> broker (which has no
/// batched/filtered query capability — <c>IGenericEntityService.RetrieveMultipleAsync</c> exists,
/// but nothing in THIS module uses it; every filtered/batched read here is a hand-built OData
/// <c>$filter</c> over a typed <see cref="HttpClient"/>, matching
/// <c>ExternalParticipationService.QueryOrganizationGrantRowsAsync</c>'s or-joined id-filter shape,
/// the task's own cited pattern). No OBO.
/// </para>
/// <para>
/// <b>Matching is by record id alone</b> — a matched "object record" entry does NOT additionally
/// require <see cref="NoAccessCandidateRecord.EntityLogicalName"/> to equal the entry's
/// <c>sprk_objectrecordtype</c>. Dataverse record ids are random v4 GUIDs assigned per row; a
/// collision across tables is not a realistic adversarial concern in this system, so a second join
/// against <c>sprk_recordtype_ref</c> purely to re-confirm entity type would add a round trip
/// without closing a real gap. <c>sprk_objectrecordtype</c> is still read and stored for
/// provenance/audit legibility.
/// </para>
/// </remarks>
public interface INoAccessListReader
{
    /// <summary>
    /// Evaluates <paramref name="candidates"/> against the active deny list for the principal
    /// identified by <paramref name="contactId"/> (direct subject) and <paramref name="organizationIds"/>
    /// (organization-subject rows deny every active member — the caller supplies the contact's own
    /// active organization memberships; this reader does not resolve membership itself).
    /// </summary>
    /// <param name="contactId">The caller's own contact id, or <c>null</c> (or <see cref="Guid.Empty"/>,
    /// treated identically) for a principal with no linked contact.</param>
    /// <param name="organizationIds">Organizations the contact is an ACTIVE member of. An
    /// implausibly large set (see <see cref="NoAccessListReader"/> remarks) is itself treated as a
    /// fail-closed condition — it cannot be safely embedded in a bounded query.</param>
    /// <param name="candidates">The records to check, each carrying its own referenced-organization
    /// set (resolved by the caller).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="NoAccessListResult.Empty"/> when there is no subject identity or no candidates to
    /// evaluate; otherwise the denied subset with provenance, or a fail-closed deny-all-queried
    /// result (<see cref="NoAccessListResult.FailedClosed"/> = <c>true</c>) if the read could not be
    /// completed.
    /// </returns>
    Task<NoAccessListResult> GetDeniedRecordsAsync(
        Guid? contactId,
        IReadOnlyCollection<Guid> organizationIds,
        IReadOnlyCollection<NoAccessCandidateRecord> candidates,
        CancellationToken ct = default);
}

/// <inheritdoc cref="INoAccessListReader" />
public class NoAccessListReader : INoAccessListReader
{
    // ── Query shape (NFR-02: bounded, batched — no per-record round trip) ───────────────────────

    /// <summary>
    /// Ids per OBJECT-side chunk (referenced-organization ids, or candidate record ids). Matches
    /// the <c>FlagQueryChunkSize</c> precedent in <c>ExternalParticipationService.GetRootRecordFlagsAsync</c>
    /// — the same class of query (a bounded OR-filter of ids embedded in a GET URL).
    /// </summary>
    internal const int ObjectIdChunkSize = 50;

    /// <summary>
    /// Defensive ceiling on the SUBJECT-side organization-id set. Chosen so the worst-case combined
    /// clause count in one request (subject clauses + one object chunk's 50 clauses) stays within
    /// the same order of magnitude as the proven single-dimension 50-clause precedent above, rather
    /// than doubling it. In practice a contact belongs to a small handful of organizations
    /// (register C-5) — this ceiling exists to make an implausible case fail SAFE, not because it
    /// is expected to be hit.
    /// </summary>
    internal const int MaxSubjectOrganizationIds = 25;

    /// <summary>Columns needed to classify a row's subject/object shape and identify it for provenance.</summary>
    internal const string RowSelect =
        "sprk_noaccessentryid,_sprk_subjectcontact_value,_sprk_subjectorganization_value," +
        "_sprk_objectorganization_value,_sprk_objectrecordtype_value,sprk_objectrecordid";

    /// <summary>
    /// The subject <c>$filter</c> fragment: this contact, OR any organization the contact is an
    /// active member of. Always wrapped in parens so it composes safely with an <c>and</c>-joined
    /// object fragment. Extracted as a PURE member (task 007 / A-5 precedent in
    /// <c>ExternalParticipationService</c>) so the predicate is directly assertable in tests without
    /// intercepting HTTP transport.
    /// </summary>
    internal static string BuildSubjectFilter(Guid? contactId, IReadOnlyCollection<Guid> organizationIds)
    {
        var parts = new List<string>();
        if (contactId is Guid cid && cid != Guid.Empty)
        {
            parts.Add($"sprk_subjectcontact eq {cid}");
        }

        if (organizationIds.Count > 0)
        {
            parts.Add("(" + string.Join(" or ", organizationIds.Select(id => $"sprk_subjectorganization eq {id}")) + ")");
        }

        // Caller (GetDeniedRecordsAsync) guarantees at least one part before calling this.
        return "(" + string.Join(" or ", parts) + ")";
    }

    /// <summary>The object <c>$filter</c> fragment for one chunk of referenced-organization ids (ethical wall).</summary>
    internal static string BuildOrganizationObjectFilter(IEnumerable<Guid> organizationIds)
        => "(" + string.Join(" or ", organizationIds.Select(id => $"sprk_objectorganization eq {id}")) + ")";

    /// <summary>
    /// The object <c>$filter</c> fragment for one chunk of candidate record ids (per-child
    /// revocation). <c>sprk_objectrecordid</c> is a text field (ADR-024 resolver pair), so values
    /// are quoted OData string literals — the ids are always well-formed <see cref="Guid"/>
    /// <see cref="Guid.ToString()"/> output (hex + hyphens only), never caller-supplied free text,
    /// so no quote-escaping is needed (contrast
    /// <c>ExternalParticipationService.ResolveContactByOidAsync</c>'s escaping of a genuinely
    /// external string).
    /// </summary>
    internal static string BuildRecordObjectFilter(IEnumerable<Guid> recordIds)
        => "(" + string.Join(" or ", recordIds.Select(id => $"sprk_objectrecordid eq '{id}'")) + ")";

    /// <summary>
    /// Combines a subject fragment and an object fragment into the full <c>$filter</c>, always
    /// ANDing in <c>statecode eq 0</c> — the acceptance criterion "a deactivated entry denies
    /// nothing" is enforced HERE, server-side, not by client-side post-filtering. Extracted as a
    /// PURE member (same reasoning as the two builders above) specifically so this is directly
    /// assertable: <see cref="QueryChunkAsync"/> is a test seam that a unit test overrides
    /// wholesale, which means the literal <c>statecode eq 0</c> clause inside it would otherwise
    /// be unobservable from a test that never reaches real HTTP.
    /// </summary>
    internal static string CombineFilter(string subjectFilter, string objectFilter)
        => $"{subjectFilter} and {objectFilter} and statecode eq 0";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;
    private readonly ILogger<NoAccessListReader> _logger;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public NoAccessListReader(
        HttpClient httpClient,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<NoAccessListReader> logger)
    {
        // No ArgumentNullException guards — matching ExternalParticipationService's and
        // ModuleEntitlementResolver's exact constructor shape (the established style of every
        // OTHER typed-HttpClient reader in this module). Deliberate, not an oversight: it is what
        // lets a test double (this task's FakeNoAccessListReader, mirroring
        // FakeParticipationService/ThrowingFlagParticipationService) pass `configuration: null!` /
        // `credential: null!` for a subclass that overrides QueryChunkAsync and never touches
        // either field — and testing.md B4 bans a dedicated constructor-null-argument test anyway.
        _httpClient = httpClient;
        _configuration = configuration;
        _credential = credential;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NoAccessListResult> GetDeniedRecordsAsync(
        Guid? contactId,
        IReadOnlyCollection<Guid> organizationIds,
        IReadOnlyCollection<NoAccessCandidateRecord> candidates,
        CancellationToken ct = default)
    {
        organizationIds ??= Array.Empty<Guid>();
        candidates ??= Array.Empty<NoAccessCandidateRecord>();

        var hasContact = contactId is Guid cid0 && cid0 != Guid.Empty;
        if (!hasContact && organizationIds.Count == 0)
        {
            // No subject identity to evaluate. A considered "nothing to check" answer — not a
            // fail-closed condition (there is no missing DATA here, just no question to ask).
            return NoAccessListResult.Empty;
        }

        if (candidates.Count == 0)
        {
            return NoAccessListResult.Empty;
        }

        if (organizationIds.Count > MaxSubjectOrganizationIds)
        {
            // Cannot safely embed this many ids in one bounded $filter (NFR-02), and there is no
            // sound way to split the SUBJECT side across multiple requests and still trust any
            // single response alone. The safe response to "cannot be safely evaluated" is the same
            // one NFR-01 prescribes for an unreadable read: deny everything queried.
            _logger.LogError(
                "[NO-ACCESS] FAIL-CLOSED: {Count} organization ids exceeds the safe query bound " +
                "({Max}) for a single subject evaluation. Denying all {CandidateCount} queried " +
                "candidates — deny-list evaluation for this subject cannot be safely performed.",
                organizationIds.Count, MaxSubjectOrganizationIds, candidates.Count);
            return FailClosed(candidates);
        }

        var subjectFilter = BuildSubjectFilter(contactId, organizationIds);
        var denied = new Dictionary<Guid, List<Guid>>();

        try
        {
            // Loop A — ethical wall: chunk the DISTINCT referenced-organization ids across every
            // candidate (not per-candidate — one chunk can answer for many candidates at once).
            var referencedOrgIds = candidates
                .SelectMany(c => c.ReferencedOrganizationIds ?? Array.Empty<Guid>())
                .Distinct()
                .ToList();

            foreach (var chunk in referencedOrgIds.Chunk(ObjectIdChunkSize))
            {
                var rows = await QueryChunkAsync(subjectFilter, BuildOrganizationObjectFilter(chunk), ct).ConfigureAwait(false);
                if (rows is null)
                {
                    // QueryChunkAsync already logged the distinct fail-closed signal.
                    return FailClosed(candidates);
                }

                ProcessRows(rows, candidates, denied);
            }

            // Loop B — per-child revocation: chunk the DISTINCT candidate record ids themselves.
            var candidateRecordIds = candidates.Select(c => c.RecordId).Distinct().ToList();

            foreach (var chunk in candidateRecordIds.Chunk(ObjectIdChunkSize))
            {
                var rows = await QueryChunkAsync(subjectFilter, BuildRecordObjectFilter(chunk), ct).ConfigureAwait(false);
                if (rows is null)
                {
                    return FailClosed(candidates);
                }

                ProcessRows(rows, candidates, denied);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[NO-ACCESS] FAIL-CLOSED: deny-list read threw for {CandidateCount} candidates. " +
                "Denying all queried candidates (NFR-01).",
                candidates.Count);
            return FailClosed(candidates);
        }

        return new NoAccessListResult
        {
            DeniedRecordIds = denied.Keys.ToHashSet(),
            DenyingEntryIds = denied.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Guid>)kv.Value),
            FailedClosed = false,
        };
    }

    /// <summary>Every id in <paramref name="candidates"/>, denied, with no provenance (NFR-01).</summary>
    private static NoAccessListResult FailClosed(IReadOnlyCollection<NoAccessCandidateRecord> candidates) => new()
    {
        DeniedRecordIds = candidates.Select(c => c.RecordId).ToHashSet(),
        DenyingEntryIds = candidates.ToDictionary(c => c.RecordId, _ => (IReadOnlyList<Guid>)Array.Empty<Guid>()),
        FailedClosed = true,
    };

    /// <summary>
    /// Matches fetched rows against <paramref name="candidates"/>, accumulating denials into
    /// <paramref name="denied"/>. A row whose object shape is ambiguous (neither or both of
    /// {object organization} / {object record type+id} populated — see entity-schema.md Business
    /// Rule 1) is logged at WARNING and excluded: a malformed row must never silently deny an
    /// unbounded set, and must never be silently dropped without a trace either.
    /// </summary>
    private void ProcessRows(
        IReadOnlyList<NoAccessEntryRow> rows,
        IReadOnlyCollection<NoAccessCandidateRecord> candidates,
        Dictionary<Guid, List<Guid>> denied)
    {
        foreach (var row in rows)
        {
            if (row.sprk_noaccessentryid is not Guid entryId)
            {
                continue; // Defensive — the primary key is always present on a real row.
            }

            var objOrgPopulated = row._sprk_objectorganization_value.HasValue;
            var objRecordTypePopulated = row._sprk_objectrecordtype_value.HasValue;
            var objRecordIdPopulated = !string.IsNullOrEmpty(row.sprk_objectrecordid);

            var isOrgObject = objOrgPopulated && !objRecordTypePopulated && !objRecordIdPopulated;
            var isRecordObject = !objOrgPopulated && objRecordTypePopulated && objRecordIdPopulated;

            if (!isOrgObject && !isRecordObject)
            {
                _logger.LogWarning(
                    "[NO-ACCESS] Entry {EntryId} has an ambiguous object shape (object-organization " +
                    "populated: {HasOrg}, object-record-type populated: {HasType}, object-record-id " +
                    "populated: {HasId}) — exactly one of {{object organization}} / {{object record}} " +
                    "is required. Excluding this entry from matching (denies nothing).",
                    entryId, objOrgPopulated, objRecordTypePopulated, objRecordIdPopulated);
                continue;
            }

            if (isOrgObject)
            {
                var orgId = row._sprk_objectorganization_value!.Value;
                foreach (var candidate in candidates)
                {
                    if (candidate.ReferencedOrganizationIds?.Contains(orgId) == true)
                    {
                        AddDenial(denied, candidate.RecordId, entryId);
                    }
                }
            }
            else if (Guid.TryParse(row.sprk_objectrecordid, out var deniedRecordId))
            {
                foreach (var candidate in candidates)
                {
                    if (candidate.RecordId == deniedRecordId)
                    {
                        AddDenial(denied, candidate.RecordId, entryId);
                    }
                }
            }
            else
            {
                _logger.LogWarning(
                    "[NO-ACCESS] Entry {EntryId} has sprk_objectrecordtype populated but " +
                    "sprk_objectrecordid ('{RawValue}') is not a parseable GUID. Excluding this " +
                    "entry from matching.",
                    entryId, row.sprk_objectrecordid);
            }
        }
    }

    private static void AddDenial(Dictionary<Guid, List<Guid>> denied, Guid recordId, Guid entryId)
    {
        if (!denied.TryGetValue(recordId, out var entryIds))
        {
            entryIds = new List<Guid>();
            denied[recordId] = entryIds;
        }

        if (!entryIds.Contains(entryId))
        {
            entryIds.Add(entryId);
        }
    }

    /// <summary>
    /// Issues one chunk's Dataverse query. Returns <c>null</c> (having already logged the distinct
    /// fail-closed signal) on any non-success response; the caller propagates that into a
    /// deny-all-queried <see cref="NoAccessListResult"/>.
    /// </summary>
    /// <remarks>
    /// <c>internal virtual</c> — the ADR-010 testing seam (<c>InternalsVisibleTo("Sprk.Bff.Api.Tests")</c>,
    /// the convention already used across this assembly). A test subclass overrides JUST this
    /// wire-level fetch, returning canned <see cref="NoAccessEntryRow"/> lists or throwing, while
    /// the REAL chunking/matching/fail-closed orchestration in <see cref="GetDeniedRecordsAsync"/>
    /// runs unmocked — mirroring the five task-037 test doubles that subclass
    /// <c>ExternalParticipationService</c> rather than mocking <see cref="HttpMessageHandler"/>
    /// (banned, testing.md B1).
    /// </remarks>
    internal virtual async Task<List<NoAccessEntryRow>?> QueryChunkAsync(
        string subjectFilter, string objectFilter, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct).ConfigureAwait(false);
            var apiUrl = GetDataverseApiUrl();

            var filter = CombineFilter(subjectFilter, objectFilter);
            var query = $"{apiUrl}/sprk_noaccessentries?$filter={filter}&$select={RowSelect}";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[NO-ACCESS] Deny-list query FAILED: {Status}. Failing CLOSED — this chunk's " +
                    "candidates will be denied (NFR-01).",
                    response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<NoAccessEntryRow>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return result?.Value ?? new List<NoAccessEntryRow>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[NO-ACCESS] Deny-list query THREW. Failing CLOSED — this chunk's candidates will " +
                "be denied (NFR-01).");
            return null;
        }
    }

    private async Task<string> GetAppOnlyTokenAsync(CancellationToken ct)
    {
        if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _currentToken.Value.Token;
        }

        if (!await _tokenSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
        {
            throw new TimeoutException("Timed out waiting for Dataverse token");
        }

        try
        {
            if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return _currentToken.Value.Token;
            }

            var dataverseUrl = _configuration["Dataverse:ServiceUrl"]
                ?? throw new InvalidOperationException("Dataverse:ServiceUrl is required");

            var scope = $"{dataverseUrl.TrimEnd('/')}/.default";
            _currentToken = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct);
            return _currentToken.Value.Token;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    private string GetDataverseApiUrl()
    {
        var dataverseUrl = _configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl is required");
        return $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";
    }

    private sealed class DataverseQueryResult<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }
    }
}

/// <summary>
/// Projection of the <c>sprk_noaccessentry</c> columns <see cref="NoAccessListReader.RowSelect"/>
/// requests. <c>internal</c> (not <c>private</c>) so a test subclass overriding
/// <see cref="NoAccessListReader.QueryChunkAsync"/> can construct canned rows.
/// </summary>
internal sealed class NoAccessEntryRow
{
    [JsonPropertyName("sprk_noaccessentryid")]
    public Guid? sprk_noaccessentryid { get; set; }

    [JsonPropertyName("_sprk_subjectcontact_value")]
    public Guid? _sprk_subjectcontact_value { get; set; }

    [JsonPropertyName("_sprk_subjectorganization_value")]
    public Guid? _sprk_subjectorganization_value { get; set; }

    [JsonPropertyName("_sprk_objectorganization_value")]
    public Guid? _sprk_objectorganization_value { get; set; }

    [JsonPropertyName("_sprk_objectrecordtype_value")]
    public Guid? _sprk_objectrecordtype_value { get; set; }

    [JsonPropertyName("sprk_objectrecordid")]
    public string? sprk_objectrecordid { get; set; }
}
