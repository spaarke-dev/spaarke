using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

// unified-access-control-r2 task 143 (GitHub #1066) — the Dataverse reads the No Access ENFORCER needs that no
// existing reader answers: one entry by id, the active entries, a record type's logical name, systemusers by oid and
// their person state, a user's team memberships, a record's owning team, and a principal's rights on a record.

/// <summary>One <c>sprk_noaccessentry</c> row, as the enforcer reads it (task 143).</summary>
/// <param name="Row">The subject/object columns, in the reader's own projection — so the malformed-row rules are the
/// reader's (<see cref="NoAccessListReader.SubjectKindOf"/>).</param>
/// <param name="StateCode">0 Active, 1 Inactive.</param>
/// <param name="ModifiedBy">The entry's last modifier (<c>modifiedby</c>) — the author owner N5 checks.</param>
internal sealed record NoAccessEntrySnapshot(NoAccessEntryRow Row, int? StateCode, Guid? ModifiedBy)
{
    /// <summary>Only an active entry is enforced.</summary>
    public bool IsActive => StateCode == 0;
}

/// <summary>The person state of a systemuser the enforcer must reason about.</summary>
public sealed record EnforcementSystemUser(Guid SystemUserId, bool? IsDisabled, Guid? ApplicationId)
{
    /// <summary>An enabled person: not disabled (an unread value is disabled) and not an application user.</summary>
    public bool IsEnabledPerson => IsDisabled is false && ApplicationId is null;
}

/// <summary>
/// App-only Dataverse reads for the No Access enforcer (task 143). Every method THROWS on a fault — the enforcer
/// records a failed run; nothing here turns a failed read into "none".
/// </summary>
/// <remarks>
/// <para><b>Why a store and not <see cref="DataverseWebApiClient"/>.</b> The enforcer needs one call that client cannot
/// make — <c>RetrievePrincipalAccess</c> for a principal OTHER than the caller (owner N5: does the entry's author hold
/// Write on the record; criterion 7: what access does a walled user still hold after the direct share is gone). The
/// function is bound to the principal, so an app-only token answers for THAT principal, not for the application.
/// <see cref="CallerRecordAccessProbe"/> deliberately has no app-only path (it answers for the caller, over OBO).</para>
/// <para><b>Test seam (ADR-010 / ADR-038).</b> <c>internal virtual</c> per read, the <see cref="NoAccessListReader"/>
/// convention: a test subclass overrides the wire reads, the enforcer's real orchestration runs; never
/// <c>Mock&lt;HttpMessageHandler&gt;</c>.</para>
/// </remarks>
public class NoAccessEnforcementStore
{
    /// <summary>Ids per OR-filter chunk — the module's bounded-URL precedent.</summary>
    internal const int IdChunkSize = 50;

    /// <summary>The entry columns: the reader's projection plus state and author.</summary>
    internal const string EntrySelect = NoAccessListReader.RowSelect + ",statecode,_modifiedby_value";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;
    private readonly ILogger<NoAccessEnforcementStore> _logger;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    public NoAccessEnforcementStore(
        HttpClient httpClient,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<NoAccessEnforcementStore> logger)
    {
        // No null guards — the NoAccessListReader constructor shape, so a test subclass that overrides every read can
        // pass null configuration and credential (and ADR-038 bans a constructor-null test anyway).
        _httpClient = httpClient;
        _configuration = configuration;
        _credential = credential;
        _logger = logger;
    }

    /// <summary>The entry, or <c>null</c> when no entry has the id.</summary>
    internal virtual async Task<NoAccessEntrySnapshot?> ReadEntryAsync(Guid entryId, CancellationToken ct)
    {
        using var doc = await GetAsync(
            $"sprk_noaccessentries?$filter=sprk_noaccessentryid eq {entryId}&$select={EntrySelect}&$top=1", ct);
        foreach (var row in Values(doc))
        {
            if (GuidOf(row, "sprk_noaccessentryid") != entryId)
            {
                continue;
            }

            return new NoAccessEntrySnapshot(
                new NoAccessEntryRow
                {
                    sprk_noaccessentryid = entryId,
                    _sprk_subjectcontact_value = GuidOf(row, "_sprk_subjectcontact_value"),
                    _sprk_subjectorganization_value = GuidOf(row, "_sprk_subjectorganization_value"),
                    _sprk_subjectsystemuser_value = GuidOf(row, "_sprk_subjectsystemuser_value"),
                    _sprk_objectorganization_value = GuidOf(row, "_sprk_objectorganization_value"),
                    _sprk_objectrecordtype_value = GuidOf(row, "_sprk_objectrecordtype_value"),
                    sprk_objectrecordid = row.TryGetProperty("sprk_objectrecordid", out var rid) &&
                                          rid.ValueKind == JsonValueKind.String ? rid.GetString() : null,
                },
                row.TryGetProperty("statecode", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetInt32() : null,
                GuidOf(row, "_modifiedby_value"));
        }

        return null;
    }

    /// <summary>Rows per page of the active-entry scan (<c>Prefer: odata.maxpagesize</c>).</summary>
    internal const int ScanPageSize = 500;

    /// <summary>
    /// EVERY active entry id, up to <paramref name="max"/>; one more reports <c>Truncated</c> (task 143 r1). Follows
    /// <c>@odata.nextLink</c> page by page — before r1 this read one page of the newest 500, so an entry past it was never
    /// enforced by the job at all.
    /// </summary>
    internal virtual async Task<(IReadOnlyList<Guid> Ids, bool Truncated)> ReadActiveEntryIdsAsync(int max, CancellationToken ct)
    {
        var ids = new List<Guid>();
        string? next = ActiveEntryScanPath;
        while (next is not null)
        {
            var (page, nextLink) = await ReadActiveEntryIdPageAsync(next, ct).ConfigureAwait(false);
            ids.AddRange(page);
            if (ids.Count > max)
            {
                return (ids.Take(max).ToList(), true);
            }

            next = nextLink;
        }

        return (ids, false);
    }

    /// <summary>The first page of the active-entry scan.</summary>
    internal const string ActiveEntryScanPath =
        "sprk_noaccessentries?$filter=statecode eq 0&$select=sprk_noaccessentryid&$orderby=sprk_noaccessentryid";

    /// <summary>One page of the active-entry scan and its <c>@odata.nextLink</c> (the wire seam).</summary>
    internal virtual async Task<(IReadOnlyList<Guid> Ids, string? NextLink)> ReadActiveEntryIdPageAsync(
        string pathOrNextLink, CancellationToken ct)
    {
        using var doc = await GetAsync(pathOrNextLink, ct, prefer: $"odata.maxpagesize={ScanPageSize}").ConfigureAwait(false);
        var ids = Values(doc).Select(r => GuidOf(r, "sprk_noaccessentryid")).OfType<Guid>().ToList();
        var next = doc.RootElement.TryGetProperty("@odata.nextLink", out var link) && link.ValueKind == JsonValueKind.String
            ? link.GetString()
            : null;
        return (ids, next);
    }

    /// <summary>
    /// The ACTIVE entries whose object is this record or one of these organizations (task 143: re-apply No Access for
    /// ONE record — the task-142 "Update Access" command). One more than <paramref name="max"/> reports <c>Truncated</c>.
    /// </summary>
    /// <remarks>
    /// EVERY organization is asked about (task 143 r1): the organizations go out in OR-chunks of
    /// <see cref="IdChunkSize"/>, the record clause rides in the first chunk, and the answers are unioned. Before r1 the
    /// organization list was cut at the first <see cref="IdChunkSize"/> with no signal — an entry on a referenced
    /// organization past the 50th was silently missed, against this module's "never a silent prefix" rule.
    /// </remarks>
    internal virtual async Task<(IReadOnlyList<Guid> Ids, bool Truncated)> ReadActiveEntryIdsCoveringAsync(
        Guid recordId, IReadOnlyCollection<Guid> organizationIds, int max, CancellationToken ct)
    {
        var found = new List<Guid>();
        foreach (var objectFilter in CoveringObjectFilters(recordId, organizationIds))
        {
            foreach (var id in await QueryActiveEntryIdsAsync(objectFilter, max + 1, ct).ConfigureAwait(false))
            {
                if (!found.Contains(id))
                {
                    found.Add(id);
                }
            }

            if (found.Count > max)
            {
                return (found.Take(max).ToList(), true);
            }
        }

        return (found, false);
    }

    /// <summary>
    /// The object filters one record-scoped re-apply sends: the record clause plus the FIRST organization chunk, then
    /// one filter per further chunk of <see cref="IdChunkSize"/> organizations. Every distinct organization appears in
    /// exactly one filter.
    /// </summary>
    internal static IReadOnlyList<string> CoveringObjectFilters(Guid recordId, IReadOnlyCollection<Guid> organizationIds)
    {
        var chunks = organizationIds.Where(o => o != Guid.Empty).Distinct().Chunk(IdChunkSize).ToList();
        var filters = new List<string>();
        var recordClause = $"sprk_objectrecordid eq '{recordId}'";
        if (chunks.Count == 0)
        {
            filters.Add(recordClause);
            return filters;
        }

        for (var i = 0; i < chunks.Count; i++)
        {
            var clauses = chunks[i].Select(o => $"_sprk_objectorganization_value eq {o}");
            filters.Add(string.Join(" or ", i == 0 ? clauses.Prepend(recordClause) : clauses));
        }

        return filters;
    }

    /// <summary>One active-entry query: the ids whose object matches <paramref name="objectFilter"/> (the wire seam).</summary>
    internal virtual async Task<IReadOnlyList<Guid>> QueryActiveEntryIdsAsync(string objectFilter, int top, CancellationToken ct)
    {
        using var doc = await GetAsync(
            $"sprk_noaccessentries?$filter=statecode eq 0 and ({objectFilter})&$select=sprk_noaccessentryid&$top={top}", ct);
        return Values(doc).Select(r => GuidOf(r, "sprk_noaccessentryid")).OfType<Guid>().ToList();
    }

    /// <summary>The entity logical name a <c>sprk_recordtype_ref</c> row stands for, or <c>null</c> when it has none.</summary>
    internal virtual async Task<string?> ReadRecordTypeLogicalNameAsync(Guid typeRefId, CancellationToken ct)
    {
        using var doc = await GetAsync(
            $"sprk_recordtype_refs?$filter=sprk_recordtype_refid eq {typeRefId}&$select=sprk_recordlogicalname&$top=1", ct);
        return Values(doc)
            .Select(r => r.TryGetProperty("sprk_recordlogicalname", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()
                : null)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
    }

    /// <summary>The systemusers whose Entra oid is one of <paramref name="oids"/>.</summary>
    internal virtual async Task<IReadOnlyList<Guid>> FindSystemUsersByOidsAsync(IReadOnlyCollection<Guid> oids, CancellationToken ct)
    {
        var found = new List<Guid>();
        foreach (var chunk in oids.Where(o => o != Guid.Empty).Distinct().Chunk(IdChunkSize))
        {
            var filter = string.Join(" or ", chunk.Select(o => $"azureactivedirectoryobjectid eq {o}"));
            using var doc = await GetAsync($"systemusers?$filter={filter}&$select=systemuserid", ct);
            found.AddRange(Values(doc).Select(r => GuidOf(r, "systemuserid")).OfType<Guid>());
        }

        return found.Distinct().ToList();
    }

    /// <summary>The person state of each id that exists.</summary>
    internal virtual async Task<IReadOnlyDictionary<Guid, EnforcementSystemUser>> ReadSystemUsersAsync(
        IReadOnlyCollection<Guid> systemUserIds, CancellationToken ct)
    {
        var users = new Dictionary<Guid, EnforcementSystemUser>();
        foreach (var chunk in systemUserIds.Where(i => i != Guid.Empty).Distinct().Chunk(IdChunkSize))
        {
            var filter = string.Join(" or ", chunk.Select(id => $"systemuserid eq {id}"));
            using var doc = await GetAsync($"systemusers?$filter={filter}&$select=systemuserid,isdisabled,applicationid", ct);
            foreach (var row in Values(doc))
            {
                if (GuidOf(row, "systemuserid") is not { } id || !chunk.Contains(id))
                {
                    continue;
                }

                bool? disabled = row.TryGetProperty("isdisabled", out var d) && d.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? d.GetBoolean()
                    : null;
                users[id] = new EnforcementSystemUser(id, disabled, GuidOf(row, "applicationid"));
            }
        }

        return users;
    }

    /// <summary>Which of <paramref name="teamIds"/> the user is a member of (<c>teammembership</c>).</summary>
    internal virtual async Task<IReadOnlySet<Guid>> ReadTeamMembershipsAsync(
        Guid systemUserId, IReadOnlyCollection<Guid> teamIds, CancellationToken ct)
    {
        var member = new HashSet<Guid>();
        foreach (var chunk in teamIds.Where(t => t != Guid.Empty).Distinct().Chunk(IdChunkSize))
        {
            var teams = string.Join(" or ", chunk.Select(t => $"teamid eq {t}"));
            using var doc = await GetAsync(
                $"teammemberships?$filter=systemuserid eq {systemUserId} and ({teams})&$select=teamid", ct);
            member.UnionWith(Values(doc).Select(r => GuidOf(r, "teamid")).OfType<Guid>());
        }

        return member;
    }

    /// <summary>The record's owning team, or <c>null</c> when a user owns it.</summary>
    internal virtual async Task<Guid?> ReadOwningTeamAsync(string entitySet, string idColumn, Guid recordId, CancellationToken ct)
    {
        using var doc = await GetAsync($"{entitySet}?$filter={idColumn} eq {recordId}&$select=_owningteam_value&$top=1", ct);
        return Values(doc).Select(r => GuidOf(r, "_owningteam_value")).FirstOrDefault();
    }

    /// <summary>
    /// The rights <paramref name="systemUserId"/> holds on the record, as Dataverse's <c>RetrievePrincipalAccess</c>
    /// reports them — evaluated for THAT principal (the function is bound to it), read with the app token.
    /// </summary>
    internal virtual async Task<AccessRights> GetPrincipalRightsAsync(
        Guid systemUserId, string entitySet, Guid recordId, CancellationToken ct)
    {
        var target = $"{{\"@odata.id\":\"{entitySet}({recordId})\"}}";
        using var doc = await GetAsync(
            $"systemusers({systemUserId})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)" +
            $"?@p1={Uri.EscapeDataString(target)}", ct);

        var rights = doc.RootElement.TryGetProperty("AccessRights", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString()
            : null;
        return DataverseAccessRightsMapper.FromAccessRightsString(rights);
    }

    // ── Wire ──────────────────────────────────────────────────────────────────────────────────────

    /// <param name="relativeUrl">A path under the Web API root, or an <c>@odata.nextLink</c> the same root returned (an
    /// absolute URL is followed ONLY when it is under that root — the app token is never sent anywhere else).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="prefer">An optional <c>Prefer</c> header (the scan's page size).</param>
    private async Task<JsonDocument> GetAsync(string relativeUrl, CancellationToken ct, string? prefer = null)
    {
        var url = ResolveRequestUrl(GetDataverseApiUrl(), relativeUrl);
        var token = await GetAppOnlyTokenAsync(ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");
        if (prefer is not null)
        {
            request.Headers.Add("Prefer", prefer);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("[NO-ACCESS-ENFORCE] Dataverse read FAILED ({Status}): {Url}", response.StatusCode,
                relativeUrl.Split('?')[0]);
            throw new HttpRequestException($"Dataverse read failed: {(int)response.StatusCode}", null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The URL one read is sent to (task 143 r2: extracted so the origin rule is tested on its own). A relative path goes
    /// under <paramref name="apiUrl"/>. An absolute URL — an <c>@odata.nextLink</c> — is followed only when it is under
    /// <paramref name="apiUrl"/> itself (same scheme, host and Web API version, then a <c>/</c>): the app token is never
    /// sent anywhere else. Anything else THROWS, so the read fails closed; for the active-entry scan that fails the whole
    /// run (the job records <c>error</c> and names it), never a silent prefix of the entries.
    /// </summary>
    /// <param name="apiUrl">The Web API root, e.g. <c>https://org.crm.dynamics.com/api/data/v9.2</c>.</param>
    /// <param name="relativeOrNextLink">A path under the root, or an absolute next-page link.</param>
    internal static string ResolveRequestUrl(string apiUrl, string relativeOrNextLink)
    {
        if (!Uri.TryCreate(relativeOrNextLink, UriKind.Absolute, out _))
        {
            return $"{apiUrl}/{relativeOrNextLink}";
        }

        if (!relativeOrNextLink.StartsWith(apiUrl + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A Dataverse next-page link pointed outside the Web API root; not followed.");
        }

        return relativeOrNextLink;
    }

    private static IEnumerable<JsonElement> Values(JsonDocument doc)
        => doc.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static Guid? GuidOf(JsonElement row, string column)
        => row.TryGetProperty(column, out var cell) && cell.ValueKind == JsonValueKind.String &&
           Guid.TryParse(cell.GetString(), out var id) && id != Guid.Empty
            ? id
            : null;

    private async Task<string> GetAppOnlyTokenAsync(CancellationToken ct)
    {
        if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _currentToken.Value.Token;
        }

        if (!await _tokenSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false))
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
            _currentToken = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct).ConfigureAwait(false);
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
}
