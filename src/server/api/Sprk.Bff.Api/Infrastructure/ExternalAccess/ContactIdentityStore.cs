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

    /// <summary>
    /// The create needs the alternate key on the uniqueness mirror (<c>sprk_externalobjectidkey</c>) and it is not
    /// defined in this environment.
    /// </summary>
    KeyMissing,

    /// <summary>Any other failure.</summary>
    Failed,

    /// <summary>
    /// The write would put the oid into the uniqueness mirror while ANOTHER contact already holds it — Dataverse's
    /// duplicate-key fault on the alternate key's unique index (<c>0x80060892</c>, HTTP 412). Not a version race:
    /// re-reading and retrying changes nothing, so the binder denies and flags the holder (owner round 4 item 4, B2).
    /// </summary>
    KeyConflict,
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

    /// <summary>
    /// Contacts whose uniqueness MIRROR (<c>sprk_externalobjectidkey</c>) carries <paramref name="oid"/> — any
    /// statecode, two rows. DIAGNOSTIC ONLY: it names the holder of a key conflict so the holder can be flagged.
    /// Nothing resolves or binds by it; who a contact IS is read from the binding column alone.
    /// </summary>
    Task<ContactLookup> FindContactsByKeyMirrorAsync(Guid oid, CancellationToken ct);

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

    /// <summary>
    /// Writes the oid (D format) into the binding AND the uniqueness mirror, with the plane, in ONE request,
    /// conditional on <paramref name="etag"/>. Another contact holding the oid in its mirror makes the platform
    /// refuse the write (<see cref="StoreWriteStatus.KeyConflict"/>).
    /// </summary>
    Task<StoreWriteResult> BindOidAsync(Guid contactId, string? etag, Guid oid, IdentityPlaneMarker plane, CancellationToken ct);

    /// <summary>
    /// Creates a contact for <paramref name="oid"/> — one POST carrying the binding, the uniqueness mirror and the
    /// plane. Another contact holding the oid in its mirror makes the key's unique index refuse the create
    /// (<see cref="StoreWriteStatus.KeyConflict"/>).
    /// </summary>
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

    /// <summary>
    /// The contact columns every binding read selects. The uniqueness mirror is selected so a squatted or
    /// half-cleared mirror can be told apart (<see cref="ContactBindingDecision.MirrorHeldWithoutBinding"/>); it is
    /// never what a contact resolves by.
    /// </summary>
    public static readonly string ContactSelect = string.Join(",",
        "contactid", "statecode", "emailaddress1",
        ContactBindingDecision.ExternalObjectIdColumn, ContactBindingDecision.IdentityPlaneColumn,
        ContactBindingDecision.KeyMirrorColumn,
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

    /// <summary>
    /// Contacts whose uniqueness mirror carries the oid — any state, two rows. Diagnostic: names a key conflict's
    /// holder (<see cref="IContactIdentityStore.FindContactsByKeyMirrorAsync"/>).
    /// </summary>
    public static string BuildKeyMirrorLookupPath(Guid oid)
        => $"contacts?$select={ContactSelect}"
           + $"&$filter={ContactBindingDecision.KeyMirrorColumn} eq '{oid:D}'&$top=2";

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

    /// <summary>
    /// The create request: a plain <c>POST contacts</c> whose body carries the binding, the UNSECURED uniqueness
    /// mirror and the plane (owner round 4 item 4, B2: Dataverse refuses an alternate key on the field-secured binding,
    /// so the key lives on the mirror). The key's unique index refuses a second contact for the same oid with HTTP 412
    /// <c>0x80060892</c> (<see cref="IsDuplicateKey"/> → <see cref="StoreWriteStatus.KeyConflict"/>), so two first
    /// sign-ins that race still produce exactly one contact.
    /// </summary>
    /// <remarks>
    /// Not <c>PATCH contacts(sprk_externalobjectidkey='…')</c> with <c>If-None-Match: *</c>: Dataverse answers that
    /// create-only upsert on an alternate key with HTTP 404 <c>0x80060891</c> ("A record with the specified key values
    /// does not exist in contact entity") and creates nothing. Verified live on spaarkedev1 2026-10-02 (task 141 gate
    /// G-6: all seven creates of the first write run failed that way; a POST probe created, and a second POST with the
    /// same mirror value got 412 <c>0x80060892</c>).
    /// </remarks>
    public static (HttpMethod Method, string Path, Dictionary<string, object?> Payload) BuildCreateRequest(
        Guid oid, IdentityPlaneMarker plane, NewContactDetails details)
        => (HttpMethod.Post, "contacts", CreatePayload(oid, plane, details));

    /// <summary>
    /// The bind payload: the oid in "D" format into the binding AND the uniqueness mirror, and the plane marker —
    /// always together, in one request, so the unique index guards every bind as well as every create.
    /// </summary>
    public static Dictionary<string, object?> BindPayload(Guid oid, IdentityPlaneMarker plane) => new()
    {
        [ContactBindingDecision.ExternalObjectIdColumn] = oid.ToString("D"),
        [ContactBindingDecision.KeyMirrorColumn] = oid.ToString("D"),
        [ContactBindingDecision.IdentityPlaneColumn] = (int)plane,
    };

    /// <summary>
    /// The create payload: the binding (the field-secured column the BFF's writer profile may create), the uniqueness
    /// mirror (the key column) and the plane — the same oid in both columns, in the one create request
    /// (<see cref="BuildCreateRequest"/>).
    /// </summary>
    public static Dictionary<string, object?> CreatePayload(Guid oid, IdentityPlaneMarker plane, NewContactDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var payload = new Dictionary<string, object?>
        {
            ["lastname"] = details.LastName,
            [ContactBindingDecision.ExternalObjectIdColumn] = oid.ToString("D"),
            [ContactBindingDecision.KeyMirrorColumn] = oid.ToString("D"),
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
            flag,
            StringOf(row, ContactBindingDecision.KeyMirrorColumn));
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

    /// <summary>
    /// True when a Dataverse error body is the alternate key's DUPLICATE fault — <c>0x80060892</c>, "Entity Key …
    /// violated", returned as HTTP 412 (captured live 2026-08-06 on <c>sprk_communication</c>,
    /// email-communication-intelligence-r2 task 020). Exact code, never a range. Distinct from the 412 a stale
    /// <c>If-Match</c> version produces, which a re-read can fix.
    /// </summary>
    public static bool IsDuplicateKey(string? body)
        => body is not null && body.Contains("0x80060892", StringComparison.OrdinalIgnoreCase);

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
    public Task<ContactLookup> FindContactsByKeyMirrorAsync(Guid oid, CancellationToken ct)
        => ReadContactsAsync(BuildKeyMirrorLookupPath(oid), "key-mirror lookup", ct);

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
        // A POST carrying the mirror: the key's unique index answers 412 0x80060892 (KeyConflict) when a contact
        // already holds that oid in the mirror. That is exactly one contact per oid even when two first sign-ins
        // race — the loser re-reads by the binding. A conflict the re-read cannot explain (no contact BOUND to the
        // oid) is a squatted mirror: the binder denies and flags. See BuildCreateRequest for why not a keyed PATCH.
        var (method, path, payload) = BuildCreateRequest(oid, plane, details);
        var result = await WriteAsync(method, path, payload, precondition: null, ct).ConfigureAwait(false);
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
        HttpMethod method, string path, Dictionary<string, object?> payload, (string Name, string Value)? precondition,
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
            if (precondition is { } header)
            {
                request.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
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

            // Checked BEFORE the generic 412: the duplicate-key fault is also a 412, but a re-read cannot fix it.
            if (IsDuplicateKey(body))
            {
                return new StoreWriteResult(StoreWriteStatus.KeyConflict, Error: Trim(body));
            }

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
