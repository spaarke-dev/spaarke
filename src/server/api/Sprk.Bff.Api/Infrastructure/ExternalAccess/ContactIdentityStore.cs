// unified-access-control-r2 task 141 — the Dataverse reads and writes behind the identity binding.
//
// ONE store, Web API, parameterized by environment URL, so the same code serves the BFF's own environment
// (Dataverse:ServiceUrl) AND RegistrationDataverseService's target environment (a demo/customer org that is
// not the default one — the systemuser it creates must get its contact link in the SAME environment).
//
// The interface is the testing seam (ADR-010): the binder, the job and the endpoints are exercised against an
// in-memory store; HTTP doubles are banned (ADR-038 ban B1), so the HTTP shapes are pinned through the PURE
// builders/parsers below instead.
//
// App-only, broker-only (ADR-028): every call uses the BFF's own TokenCredential (managed identity) — never
// the caller's token, no OBO, no Graph, no .WithClientSecret.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>A systemuser as the link decision needs it.</summary>
public sealed record SystemUserIdentityRow(
    Guid SystemUserId,
    Guid? Oid,
    string? InternalEmail,
    string? DomainName,
    Guid? PrimaryContactId,
    string? ETag,
    ContactBindingRow? LinkedContact = null,
    string? FirstName = null,
    string? LastName = null);

/// <summary>A systemuser lookup.</summary>
public sealed record SystemUserLookup(LookupStatus Status, SystemUserIdentityRow? Row)
{
    /// <summary>The systemuser could not be read.</summary>
    public static SystemUserLookup Failed { get; } = new(LookupStatus.Failed, null);
}

/// <summary>Contact details for a contact created keyed by an oid.</summary>
public sealed record NewContactDetails(string? FirstName, string LastName, string? Email);

/// <summary>How a store write ended.</summary>
public enum StoreWriteStatus
{
    /// <summary>Written.</summary>
    Written,

    /// <summary>The row changed since it was read (HTTP 412 on <c>If-Match</c>) or already exists (create-only).</summary>
    PreconditionFailed,

    /// <summary>The target row does not exist.</summary>
    NotFound,

    /// <summary>The create needs the <c>sprk_externalobjectid</c> alternate key and it is not defined.</summary>
    KeyMissing,

    /// <summary>Any other failure.</summary>
    Failed,
}

/// <summary>A store write's result; <see cref="ContactId"/> is set by a successful create.</summary>
public sealed record StoreWriteResult(StoreWriteStatus Status, Guid? ContactId = null, string? Error = null)
{
    /// <summary>A plain success.</summary>
    public static StoreWriteResult Written { get; } = new(StoreWriteStatus.Written);
}

/// <summary>Whether this identity can READ the binding column (field-level security).</summary>
public enum BindingReadability
{
    /// <summary>At least one marked row came back with its oid: readable.</summary>
    Readable,

    /// <summary>No row carries a plane marker yet, so masking cannot be distinguished — nothing to protect.</summary>
    NoMarkedRows,

    /// <summary>Every marked row came back with no oid: the column is masked. No bind, create or link is safe.</summary>
    Masked,

    /// <summary>The probe itself could not be read.</summary>
    Failed,
}

/// <summary>One page of a scan, and the continuation for the next page (null when done).</summary>
public sealed record StorePage<T>(LookupStatus Status, IReadOnlyList<T> Rows, string? Continuation, string? Error = null);

/// <summary>The identity-binding reads and writes. Testing seam (ADR-010).</summary>
public interface IContactIdentityStore
{
    /// <summary>Contacts carrying <paramref name="oid"/> — ANY statecode, two rows.</summary>
    Task<ContactLookup> FindContactsByOidAsync(Guid oid, CancellationToken ct);

    /// <summary>ACTIVE contacts whose <c>emailaddress1</c> is <paramref name="email"/>, two rows.</summary>
    Task<ContactLookup> FindActiveContactsByEmailAsync(string email, CancellationToken ct);

    /// <summary>One contact by id; a missing row reads as zero rows.</summary>
    Task<ContactLookup> GetContactAsync(Guid contactId, CancellationToken ct);

    /// <summary>Systemusers (any state) whose <c>sprk_primarycontact</c> is one of <paramref name="contactIds"/>.</summary>
    Task<ReferenceLookup> FindSystemUsersLinkingAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct);

    /// <summary>One systemuser with its link.</summary>
    Task<SystemUserLookup> GetSystemUserAsync(Guid systemUserId, CancellationToken ct);

    /// <summary>The field-level-security masking probe.</summary>
    Task<BindingReadability> ProbeBindingReadabilityAsync(CancellationToken ct);

    /// <summary>Writes the oid (D format) and plane onto a contact, conditional on <paramref name="etag"/>.</summary>
    Task<StoreWriteResult> BindOidAsync(Guid contactId, string? etag, Guid oid, IdentityPlaneMarker plane, CancellationToken ct);

    /// <summary>Creates a contact keyed by <paramref name="oid"/> — create-only through the alternate key.</summary>
    Task<StoreWriteResult> CreateContactForOidAsync(Guid oid, IdentityPlaneMarker plane, NewContactDetails details, CancellationToken ct);

    /// <summary>Sets <c>systemuser.sprk_primarycontact</c>, conditional on <paramref name="etag"/>.</summary>
    Task<StoreWriteResult> SetPrimaryContactAsync(Guid systemUserId, string? etag, Guid contactId, CancellationToken ct);

    /// <summary>
    /// Writes a collision flag (every party) onto a contact, conditional on <paramref name="etag"/> so a party
    /// another writer appended meanwhile is not overwritten (412 → the caller re-reads and appends again).
    /// </summary>
    Task<StoreWriteResult> WriteCollisionFlagAsync(Guid contactId, CollisionFlag flag, string? etag, CancellationToken ct);

    /// <summary>
    /// Clears a contact's collision flag, conditional on <paramref name="etag"/> — the version the clearing
    /// decision was made on. Without a version nothing is cleared: a party appended after that read must survive.
    /// </summary>
    Task<StoreWriteResult> ClearCollisionFlagAsync(Guid contactId, string? etag, CancellationToken ct);

    /// <summary>One page of enabled interactive systemusers with their linked contact.</summary>
    Task<StorePage<SystemUserIdentityRow>> ScanInteractiveSystemUsersAsync(string? continuation, CancellationToken ct);

    /// <summary>One page of contacts carrying an open collision flag.</summary>
    Task<StorePage<ContactBindingRow>> ScanFlaggedContactsAsync(string? continuation, CancellationToken ct);
}

/// <summary>
/// The Web API implementation of <see cref="IContactIdentityStore"/> for one Dataverse environment.
/// </summary>
public sealed class DataverseContactIdentityStore : IContactIdentityStore
{
    /// <summary>The named HttpClient (pooled by <see cref="IHttpClientFactory"/>, safe in a singleton).</summary>
    public const string HttpClientName = "ContactIdentityStore";

    /// <summary>Rows per scan page (<c>Prefer: odata.maxpagesize</c>).</summary>
    public const int ScanPageSize = 500;

    // Column names. The flag columns are this task's schema (scripts/Set-ContactIdentityBindingSchema.ps1).
    public const string FlagOnColumn = "sprk_identitycollisionon";
    public const string FlagOidColumn = "sprk_identitycollisionoid";
    public const string FlagPlaneColumn = "sprk_identitycollisionplane";
    public const string FlagReasonColumn = "sprk_identitycollisionreason";

    /// <summary>Every recorded party of the flag, as JSON (multi-line text). The four columns above are the first.</summary>
    public const string FlagPartiesColumn = "sprk_identitycollisionparties";

    /// <summary>The contact columns every binding read selects.</summary>
    public static readonly string ContactSelect = string.Join(",",
        "contactid", "statecode", "emailaddress1",
        ContactBindingDecision.ExternalObjectIdColumn, ContactBindingDecision.IdentityPlaneColumn,
        FlagOnColumn, FlagOidColumn, FlagPlaneColumn, FlagReasonColumn, FlagPartiesColumn);

    /// <summary>The systemuser columns the link decision selects.</summary>
    public const string SystemUserSelect =
        "systemuserid,firstname,lastname,azureactivedirectoryobjectid,internalemailaddress,domainname,_sprk_primarycontact_value";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<CancellationToken, Task<string>> _getToken;
    private readonly string _apiUrl;
    private readonly ILogger _logger;

    /// <summary>The store for an environment, authenticated by <paramref name="getToken"/>.</summary>
    public DataverseContactIdentityStore(
        IHttpClientFactory httpClientFactory,
        Func<CancellationToken, Task<string>> getToken,
        string dataverseBaseUrl,
        ILogger logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _getToken = getToken ?? throw new ArgumentNullException(nameof(getToken));
        ArgumentException.ThrowIfNullOrWhiteSpace(dataverseBaseUrl);
        _apiUrl = $"{dataverseBaseUrl.TrimEnd('/')}/api/data/v9.2";
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The store for the BFF's own environment (<c>Dataverse:ServiceUrl</c>) under the BFF's managed identity.
    /// </summary>
    public static DataverseContactIdentityStore ForDefaultEnvironment(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        IConfiguration configuration,
        ILogger<DataverseContactIdentityStore> logger)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(configuration);
        var baseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl is required for the identity binding store.");
        var tokens = new CachedTokenSource(credential, $"{baseUrl.TrimEnd('/')}/.default");
        return new DataverseContactIdentityStore(httpClientFactory, tokens.GetAsync, baseUrl, logger);
    }

    // ── Pure builders (asserted directly; ADR-038 bans HTTP doubles) ──────────────────────────────────

    /// <summary>OData string literal: single quotes doubled, then URL-encoded.</summary>
    public static string Literal(string value) => Uri.EscapeDataString(value.Replace("'", "''"));

    /// <summary>Contacts carrying the oid — any state, two rows (an ambiguous oid must be visible).</summary>
    public static string BuildOidLookupPath(Guid oid)
        => $"contacts?$select={ContactSelect}"
           + $"&$filter={ContactBindingDecision.ExternalObjectIdColumn} eq '{oid:D}'&$top=2";

    /// <summary>ACTIVE contacts by email, two rows (TopCount 1 made ambiguity invisible — task 013).</summary>
    public static string BuildEmailLookupPath(string email)
        => $"contacts?$select={ContactSelect}"
           + $"&$filter=emailaddress1 eq '{Literal(email)}' and statecode eq 0&$top=2";

    /// <summary>Systemusers linking any of the contacts — any state, so a disabled user's link still counts.</summary>
    public static string BuildReferenceLookupPath(IReadOnlyCollection<Guid> contactIds)
        => $"systemusers?$select=systemuserid,azureactivedirectoryobjectid,_sprk_primarycontact_value"
           + $"&$filter=({string.Join(" or ", contactIds.Select(id => $"_sprk_primarycontact_value eq {id:D}"))})"
           + "&$top=50";

    /// <summary>The masking probe: rows that carry a plane marker.</summary>
    public static string BuildProbePath()
        => $"contacts?$select=contactid,{ContactBindingDecision.ExternalObjectIdColumn},{ContactBindingDecision.IdentityPlaneColumn}"
           + $"&$filter={ContactBindingDecision.IdentityPlaneColumn} ne null&$top=50";

    /// <summary>
    /// The interactive-systemuser scan: enabled, not an application user, and a human access mode —
    /// Read-Write (0), Administrative (1) or Read (2). Support User (3), Non-interactive (4) and Delegated
    /// Admin (5) are excluded. The linked contact is expanded so a verified user costs no further query.
    /// </summary>
    public static string BuildSystemUserScanPath()
        => $"systemusers?$select={SystemUserSelect}"
           + "&$filter=isdisabled eq false and applicationid eq null and (accessmode eq 0 or accessmode eq 1 or accessmode eq 2)"
           + $"&$expand=sprk_PrimaryContact($select={ContactSelect})"
           + "&$orderby=systemuserid";

    /// <summary>Contacts with an open collision flag.</summary>
    public static string BuildFlaggedContactScanPath()
        => $"contacts?$select={ContactSelect}&$filter={FlagOnColumn} ne null&$orderby=contactid";

    /// <summary>The create-only path: the alternate key in the URL.</summary>
    public static string BuildCreateByKeyPath(Guid oid)
        => $"contacts({ContactBindingDecision.ExternalObjectIdColumn}='{oid:D}')";

    /// <summary>The bind payload: the oid in "D" format and the plane marker, always together.</summary>
    public static Dictionary<string, object?> BindPayload(Guid oid, IdentityPlaneMarker plane) => new()
    {
        [ContactBindingDecision.ExternalObjectIdColumn] = oid.ToString("D"),
        [ContactBindingDecision.IdentityPlaneColumn] = (int)plane,
    };

    /// <summary>The create payload. The key column itself comes from the URL.</summary>
    public static Dictionary<string, object?> CreatePayload(IdentityPlaneMarker plane, NewContactDetails details)
    {
        var payload = new Dictionary<string, object?>
        {
            ["lastname"] = details.LastName,
            [ContactBindingDecision.IdentityPlaneColumn] = (int)plane,
        };
        if (!string.IsNullOrWhiteSpace(details.FirstName)) payload["firstname"] = details.FirstName;
        if (!string.IsNullOrWhiteSpace(details.Email)) payload["emailaddress1"] = details.Email;
        return payload;
    }

    /// <summary>The flag payload: the four summary columns (the first party) and every party as JSON.</summary>
    public static Dictionary<string, object?> FlagPayload(CollisionFlag flag) => new()
    {
        [FlagOnColumn] = flag.FlaggedOn.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        [FlagOidColumn] = flag.CollidingOid?.ToString("D"),
        [FlagPlaneColumn] = flag.CollidingPlane is { } p ? (int)p : null,
        [FlagReasonColumn] = flag.Reason is { } r ? (int)r : null,
        [FlagPartiesColumn] = SerializeParties(flag.Parties),
    };

    /// <summary>The clear payload (all five flag columns null).</summary>
    public static Dictionary<string, object?> ClearFlagPayload() => new()
    {
        [FlagOnColumn] = null,
        [FlagOidColumn] = null,
        [FlagPlaneColumn] = null,
        [FlagReasonColumn] = null,
        [FlagPartiesColumn] = null,
    };

    /// <summary>
    /// The parties column's JSON: <c>[{"oid":"…","plane":100000001,"reason":100000000,"on":"…Z"}]</c>. Oids in "D"
    /// format, option values as integers, times in UTC. Pure.
    /// </summary>
    public static string SerializeParties(IReadOnlyList<CollisionParty> parties)
    {
        ArgumentNullException.ThrowIfNull(parties);
        var rows = parties.Select(p => new Dictionary<string, object?>
        {
            ["oid"] = p.Oid?.ToString("D"),
            ["plane"] = p.Plane is { } pl ? (int)pl : null,
            ["reason"] = p.Reason is { } r ? (int)r : null,
            ["on"] = p.FlaggedOn.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        });
        return JsonSerializer.Serialize(rows);
    }

    /// <summary>
    /// Parses the parties column. Null when the text is not the shape <see cref="SerializeParties"/> writes —
    /// the caller then treats the flag as carrying parties it cannot read (never cleared, never overwritten).
    /// Pure.
    /// </summary>
    public static IReadOnlyList<CollisionParty>? ParseParties(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var doc = TryParseJson(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        var parties = new List<CollisionParty>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return null;
            if (!item.TryGetProperty("on", out var onEl) || onEl.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(onEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var on))
            {
                return null;
            }

            Guid? oid = null;
            if (item.TryGetProperty("oid", out var oidEl) && oidEl.ValueKind != JsonValueKind.Null)
            {
                if (oidEl.ValueKind != JsonValueKind.String || !Guid.TryParse(oidEl.GetString(), out var g) || g == Guid.Empty)
                {
                    return null;
                }

                oid = g;
            }

            parties.Add(new CollisionParty(oid, PlaneOf(IntOf(item, "plane")), ReasonOf(IntOf(item, "reason")), on));
        }

        return parties.Count == 0 ? null : parties;
    }

    /// <summary>Parses one contact row from Web API JSON.</summary>
    public static ContactBindingRow? ParseContactRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        var id = GuidOf(row, "contactid");
        if (id is null) return null;

        CollisionFlag? flag = null;
        if (row.TryGetProperty(FlagOnColumn, out var on) && on.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(on.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var flaggedOn))
        {
            flag = new CollisionFlag(
                flaggedOn,
                GuidOf(row, FlagOidColumn),
                PlaneOf(IntOf(row, FlagPlaneColumn)),
                ReasonOf(IntOf(row, FlagReasonColumn)));

            // The summary columns are the first party; the parties column is every party. A parties column that
            // does not parse — or does not start with the summary's party — is a flag whose parties we cannot read:
            // it is kept and never overwritten (ContactBindingDecision.ShouldWriteFlag / ReconcileFlag).
            var rawParties = StringOf(row, FlagPartiesColumn);
            if (!string.IsNullOrWhiteSpace(rawParties))
            {
                var parties = ParseParties(rawParties);
                flag = parties is not null && parties[0].IsSameCollision(flag.Primary)
                    ? flag with { OtherParties = parties.Skip(1).ToList() }
                    : flag with { HasUnreadableParties = true };
            }
        }

        return new ContactBindingRow(
            id.Value,
            IntOf(row, "statecode"),
            StringOf(row, ContactBindingDecision.ExternalObjectIdColumn),
            IntOf(row, ContactBindingDecision.IdentityPlaneColumn),
            StringOf(row, "@odata.etag"),
            StringOf(row, "emailaddress1"),
            flag);
    }

    /// <summary>Parses one systemuser row (with an optional expanded linked contact) from Web API JSON.</summary>
    public static SystemUserIdentityRow? ParseSystemUserRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        var id = GuidOf(row, "systemuserid");
        if (id is null) return null;

        ContactBindingRow? linked = null;
        if (row.TryGetProperty("sprk_PrimaryContact", out var expanded) && expanded.ValueKind == JsonValueKind.Object)
        {
            linked = ParseContactRow(expanded);
        }

        return new SystemUserIdentityRow(
            id.Value,
            GuidOf(row, "azureactivedirectoryobjectid"),
            StringOf(row, "internalemailaddress"),
            StringOf(row, "domainname"),
            GuidOf(row, "_sprk_primarycontact_value"),
            StringOf(row, "@odata.etag"),
            linked,
            StringOf(row, "firstname"),
            StringOf(row, "lastname"));
    }

    /// <summary>
    /// Classifies a Dataverse error body: a column this environment lacks, an alternate key it lacks, or
    /// anything else. Both missing-schema shapes come back as 400 with code <c>0x80060888</c> (verified live
    /// 2026-09-30), told apart by message.
    /// </summary>
    public static LookupStatus ClassifyError(string? body)
        => body is not null && body.Contains("Could not find a property named", StringComparison.OrdinalIgnoreCase)
            ? LookupStatus.ColumnMissing
            : LookupStatus.Failed;

    /// <summary>True when a Dataverse error body says the alternate key in the URL is not defined.</summary>
    public static bool IsKeyMissing(string? body)
        => body is not null && body.Contains("key in the request URI is not valid", StringComparison.OrdinalIgnoreCase);

    // ── Reads ──────────────────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<ContactLookup> FindContactsByOidAsync(Guid oid, CancellationToken ct)
        => ReadContactsAsync(BuildOidLookupPath(oid), "oid lookup", ct);

    /// <inheritdoc />
    public Task<ContactLookup> FindActiveContactsByEmailAsync(string email, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return ReadContactsAsync(BuildEmailLookupPath(email.Trim()), "email lookup", ct);
    }

    /// <inheritdoc />
    public async Task<ContactLookup> GetContactAsync(Guid contactId, CancellationToken ct)
    {
        var (status, code, body, _) = await GetAsync($"contacts({contactId:D})?$select={ContactSelect}", ct)
            .ConfigureAwait(false);
        if (status == LookupStatus.Read && code == HttpStatusCode.NotFound)
        {
            return ContactLookup.Of();
        }

        if (status != LookupStatus.Read || body is null)
        {
            return status == LookupStatus.ColumnMissing ? ContactLookup.ColumnMissing : ContactLookup.Failed;
        }

        using var doc = TryParseJson(body);
        var row = doc is null ? null : ParseContactRow(doc.RootElement);
        return row is null ? ContactLookup.Failed : ContactLookup.Of(row);
    }

    /// <inheritdoc />
    public async Task<ReferenceLookup> FindSystemUsersLinkingAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct)
    {
        var ids = contactIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return ReferenceLookup.Of();

        var (status, _, body, _) = await GetAsync(BuildReferenceLookupPath(ids), ct).ConfigureAwait(false);
        if (status != LookupStatus.Read || body is null)
        {
            return ReferenceLookup.Failed;
        }

        using var doc = TryParseJson(body);
        if (doc is null)
        {
            return ReferenceLookup.Failed;
        }

        var refs = new List<SystemUserReference>();
        foreach (var row in Values(doc.RootElement))
        {
            var suid = GuidOf(row, "systemuserid");
            var linked = GuidOf(row, "_sprk_primarycontact_value");
            if (suid is { } s && linked is { } l)
            {
                refs.Add(new SystemUserReference(s, GuidOf(row, "azureactivedirectoryobjectid"), l));
            }
        }

        return new ReferenceLookup(LookupStatus.Read, refs);
    }

    /// <inheritdoc />
    public async Task<SystemUserLookup> GetSystemUserAsync(Guid systemUserId, CancellationToken ct)
    {
        var (status, code, body, _) = await GetAsync(
                $"systemusers({systemUserId:D})?$select={SystemUserSelect}&$expand=sprk_PrimaryContact($select={ContactSelect})",
                ct)
            .ConfigureAwait(false);
        if (status != LookupStatus.Read || body is null || code == HttpStatusCode.NotFound)
        {
            return new SystemUserLookup(status == LookupStatus.Read ? LookupStatus.Failed : status, null);
        }

        using var doc = TryParseJson(body);
        var row = doc is null ? null : ParseSystemUserRow(doc.RootElement);
        return row is null ? SystemUserLookup.Failed : new SystemUserLookup(LookupStatus.Read, row);
    }

    /// <inheritdoc />
    public async Task<BindingReadability> ProbeBindingReadabilityAsync(CancellationToken ct)
    {
        var lookup = await ReadContactsAsync(BuildProbePath(), "masking probe", ct).ConfigureAwait(false);
        return ClassifyProbe(lookup);
    }

    /// <summary>The masking verdict over the probe's rows. Pure.</summary>
    public static BindingReadability ClassifyProbe(ContactLookup probe)
    {
        if (probe.Status != LookupStatus.Read) return BindingReadability.Failed;
        if (probe.Rows.Count == 0) return BindingReadability.NoMarkedRows;

        // Every BFF write sets oid and marker together. If EVERY marked row comes back with no oid, the column
        // is being masked from this identity (FLS without Read) — a few orphaned markers cannot cause this.
        return probe.Rows.All(r => string.IsNullOrWhiteSpace(r.RawOid))
            ? BindingReadability.Masked
            : BindingReadability.Readable;
    }

    // ── Writes ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>If-Match</c> value for a bind or a link: the row version read with the row, or NULL when there is
    /// none. Never <c>*</c> — "the row exists" is not "the row is as I read it", and an unconditional bind or link
    /// could overwrite one a concurrent writer just made (a binding someone else owns; a link that must never be
    /// re-pointed). A caller without a version re-reads the row first. Pure.
    /// </summary>
    public static string? RowVersionPrecondition(string? etag)
        => string.IsNullOrWhiteSpace(etag) || etag.Trim() == "*" ? null : etag;

    /// <inheritdoc />
    public Task<StoreWriteResult> BindOidAsync(Guid contactId, string? etag, Guid oid, IdentityPlaneMarker plane, CancellationToken ct)
        => RowVersionPrecondition(etag) is { } version
            ? WriteAsync(HttpMethod.Patch, $"contacts({contactId:D})", BindPayload(oid, plane), ("If-Match", version), ct)
            : Task.FromResult(NoRowVersion("bind", contactId));

    /// <inheritdoc />
    public async Task<StoreWriteResult> CreateContactForOidAsync(
        Guid oid, IdentityPlaneMarker plane, NewContactDetails details, CancellationToken ct)
    {
        // PATCH on the alternate key + If-None-Match: * is CREATE-ONLY: Dataverse answers 412 when a contact
        // with that oid already exists. With the key's unique index this is exactly one contact per oid even
        // when two first sign-ins race — the loser gets 412 and re-reads.
        var result = await WriteAsync(HttpMethod.Patch, BuildCreateByKeyPath(oid), CreatePayload(plane, details),
            ("If-None-Match", "*"), ct).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public Task<StoreWriteResult> SetPrimaryContactAsync(Guid systemUserId, string? etag, Guid contactId, CancellationToken ct)
        => RowVersionPrecondition(etag) is { } version
            ? WriteAsync(
                HttpMethod.Patch,
                $"systemusers({systemUserId:D})",
                new Dictionary<string, object?> { ["sprk_PrimaryContact@odata.bind"] = $"/contacts({contactId:D})" },
                ("If-Match", version),
                ct)
            : Task.FromResult(NoRowVersion("link", systemUserId));

    private StoreWriteResult NoRowVersion(string what, Guid rowId)
    {
        _logger.LogWarning(
            "[ID-BIND] {What} of row {RowId} refused: no row version to make it conditional (never an unconditional write)",
            what, rowId);
        return new StoreWriteResult(StoreWriteStatus.Failed, Error: $"no row version for the {what}");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Conditional on the row version so a concurrent append is never overwritten. A row read without a version
    /// falls back to <c>*</c>: the worst case is a lost party (re-recorded on that identity's next collision),
    /// never a binding or a link — those stay strictly version-conditional (<see cref="RowVersionPrecondition"/>).
    /// </remarks>
    public Task<StoreWriteResult> WriteCollisionFlagAsync(Guid contactId, CollisionFlag flag, string? etag, CancellationToken ct)
        => WriteAsync(HttpMethod.Patch, $"contacts({contactId:D})", FlagPayload(flag),
            ("If-Match", RowVersionPrecondition(etag) ?? "*"), ct);

    /// <inheritdoc />
    public Task<StoreWriteResult> ClearCollisionFlagAsync(Guid contactId, string? etag, CancellationToken ct)
        => RowVersionPrecondition(etag) is { } version
            ? WriteAsync(HttpMethod.Patch, $"contacts({contactId:D})", ClearFlagPayload(), ("If-Match", version), ct)
            : Task.FromResult(NoRowVersion("flag clear", contactId));

    // ── Scans ──────────────────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<StorePage<SystemUserIdentityRow>> ScanInteractiveSystemUsersAsync(string? continuation, CancellationToken ct)
        => ScanAsync(continuation ?? BuildSystemUserScanPath(), ParseSystemUserRow, ct);

    /// <inheritdoc />
    public Task<StorePage<ContactBindingRow>> ScanFlaggedContactsAsync(string? continuation, CancellationToken ct)
        => ScanAsync(continuation ?? BuildFlaggedContactScanPath(), ParseContactRow, ct);

    // ── Plumbing ───────────────────────────────────────────────────────────────────────────────────────

    private async Task<ContactLookup> ReadContactsAsync(string path, string what, CancellationToken ct)
    {
        var (status, _, body, _) = await GetAsync(path, ct).ConfigureAwait(false);
        if (status != LookupStatus.Read || body is null)
        {
            if (status == LookupStatus.ColumnMissing)
            {
                _logger.LogError(
                    "[ID-BIND] {What} failed: a binding column is not provisioned in this environment ({DenyCode}). "
                    + "Apply scripts/Set-ContactIdentityBindingSchema.ps1 before this BFF build.",
                    what, ContactBindingDecision.DenyBindingColumnMissing);
                return ContactLookup.ColumnMissing;
            }

            return ContactLookup.Failed;
        }

        using var doc = TryParseJson(body);
        if (doc is null)
        {
            return ContactLookup.Failed;
        }

        var rows = Values(doc.RootElement).Select(ParseContactRow).Where(r => r is not null).Select(r => r!).ToList();
        return new ContactLookup(LookupStatus.Read, rows);
    }

    private async Task<StorePage<T>> ScanAsync<T>(string pathOrNextLink, Func<JsonElement, T?> parse, CancellationToken ct)
        where T : class
    {
        var (status, _, body, error) = await GetAsync(pathOrNextLink, ct,
            prefer: $"odata.maxpagesize={ScanPageSize}").ConfigureAwait(false);
        if (status != LookupStatus.Read || body is null)
        {
            return new StorePage<T>(status == LookupStatus.Read ? LookupStatus.Failed : status, Array.Empty<T>(), null, error);
        }

        using var doc = TryParseJson(body);
        if (doc is null)
        {
            return new StorePage<T>(LookupStatus.Failed, Array.Empty<T>(), null, "the response body is not JSON");
        }

        var rows = Values(doc.RootElement).Select(parse).Where(r => r is not null).Select(r => r!).ToList();
        var next = doc.RootElement.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;
        return new StorePage<T>(LookupStatus.Read, rows, next);
    }

    private async Task<StoreWriteResult> WriteAsync(
        HttpMethod method, string path, Dictionary<string, object?> payload, (string Name, string Value) precondition,
        CancellationToken ct)
    {
        try
        {
            var token = await _getToken(ct).ConfigureAwait(false);
            using var request = new HttpRequestMessage(method, Absolute(path))
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.TryAddWithoutValidation(precondition.Name, precondition.Value);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                Guid? created = null;
                if (response.Headers.TryGetValues("OData-EntityId", out var ids))
                {
                    var raw = ids.FirstOrDefault();
                    var open = raw?.LastIndexOf('(') ?? -1;
                    if (raw is not null && open >= 0 && Guid.TryParse(raw[(open + 1)..].TrimEnd(')'), out var g))
                    {
                        created = g;
                    }
                }

                return new StoreWriteResult(StoreWriteStatus.Written, created);
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return new StoreWriteResult(StoreWriteStatus.PreconditionFailed, Error: Trim(body));
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new StoreWriteResult(StoreWriteStatus.NotFound, Error: Trim(body));
            }

            if (IsKeyMissing(body))
            {
                return new StoreWriteResult(StoreWriteStatus.KeyMissing, Error: Trim(body));
            }

            _logger.LogWarning("[ID-BIND] {Method} {Path} failed: {Status} {Body}", method, path, (int)response.StatusCode, Trim(body));
            return new StoreWriteResult(StoreWriteStatus.Failed, Error: $"{(int)response.StatusCode}: {Trim(body)}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A timeout surfaces as TaskCanceledException without ct being cancelled — a failure, not a cancel.
            _logger.LogWarning(ex, "[ID-BIND] {Method} {Path} threw", method, path);
            return new StoreWriteResult(StoreWriteStatus.Failed, Error: ex.Message);
        }
    }

    private async Task<(LookupStatus Status, HttpStatusCode Code, string? Body, string? Error)> GetAsync(
        string path, CancellationToken ct, string? prefer = null)
    {
        try
        {
            var token = await _getToken(ct).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, Absolute(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (prefer is not null) request.Headers.Add("Prefer", prefer);

            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return (LookupStatus.Read, response.StatusCode, body, null);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // A retrieve-by-id of a row that does not exist. The caller decides what absence means.
                return (LookupStatus.Read, HttpStatusCode.NotFound, null, null);
            }

            var classified = ClassifyError(body);
            _logger.LogWarning("[ID-BIND] GET {Path} failed: {Status} {Body}", path, (int)response.StatusCode, Trim(body));
            return (classified, response.StatusCode, null, $"{(int)response.StatusCode}: {Trim(body)}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ID-BIND] GET {Path} threw — treated as could-not-read (deny)", path);
            return (LookupStatus.Failed, default, null, ex.Message);
        }
    }

    private string Absolute(string pathOrNextLink)
        => pathOrNextLink.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? pathOrNextLink
            : $"{_apiUrl}/{pathOrNextLink}";

    /// <summary>
    /// Parses a response body, or null when it is not JSON. A body that cannot be parsed is a could-not-read —
    /// the store's three-state contract (Read / Failed / ColumnMissing) holds even for a malformed response, so a
    /// parse failure denies through the decision instead of escaping as an exception. Pure.
    /// </summary>
    public static JsonDocument? TryParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<JsonElement> Values(JsonElement root)
        => root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().ToList()
            : Enumerable.Empty<JsonElement>();

    private static string? StringOf(JsonElement row, string name)
        => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? IntOf(JsonElement row, string name)
        => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static Guid? GuidOf(JsonElement row, string name)
        => StringOf(row, name) is { } s && Guid.TryParse(s, out var g) && g != Guid.Empty ? g : null;

    private static IdentityPlaneMarker? PlaneOf(int? value)
        => value is { } v && Enum.IsDefined(typeof(IdentityPlaneMarker), v) ? (IdentityPlaneMarker)v : null;

    private static IdentityCollisionReason? ReasonOf(int? value)
        => value is { } v && Enum.IsDefined(typeof(IdentityCollisionReason), v) ? (IdentityCollisionReason)v : null;

    private static string Trim(string? body) => body is null ? string.Empty : body.Length <= 400 ? body : body[..400];

    /// <summary>A double-checked token cache over a <see cref="TokenCredential"/> (singleton-safe).</summary>
    private sealed class CachedTokenSource(TokenCredential credential, string scope)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private AccessToken? _token;

        public async Task<string> GetAsync(CancellationToken ct)
        {
            if (_token is { } t && t.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5)) return t.Token;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_token is { } again && again.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5)) return again.Token;
                _token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct).ConfigureAwait(false);
                return _token.Value.Token;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
