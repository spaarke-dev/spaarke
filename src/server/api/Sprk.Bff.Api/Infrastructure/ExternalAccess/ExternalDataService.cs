using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Queries Dataverse for project data on behalf of authenticated external users.
/// Uses managed identity (app-only) tokens to read sprk_projects, sprk_documents,
/// sprk_todos, contacts, and accounts from Dataverse.
///
/// All data access is gated by the ExternalCallerAuthorizationFilter — only records
/// belonging to projects in the caller's ExternalCallerContext.Participations are served.
///
/// ADR-009: Redis caching not applied here — participation is cached by ExternalParticipationService.
///          Individual data queries are short-lived; add caching only if profiling shows need.
///
/// ADR-024: To-do regarding context is applied via the four resolver fields
///          (sprk_regardingrecordtype, sprk_regardingrecordid, sprk_regardingrecordname,
///          sprk_regardingrecordurl) atomically with the specific sprk_regardingproject lookup.
///          Mirrors <see cref="Sprk.Bff.Api.Services.Workspace.TodoRegardingBuilder"/> semantics
///          but over the Dataverse Web API (no ServiceClient eager connection).
///
/// smart-todo-decoupling-r3 (FR-29): Replaces the legacy event-based to-do model
/// with first-class sprk_todo. Breaking change documented at
/// projects/smart-todo-decoupling-r3/notes/external-access-contract-change.md.
/// </summary>
public class ExternalDataService
{
    private static readonly TimeSpan TokenRefreshBuffer = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;
    private readonly ILogger<ExternalDataService> _logger;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    // Per-instance cache of sprk_recordtype_ref lookups (entity logical name → GUID + display name).
    // Mirrors TodoRegardingBuilder's _recordTypeRefCache.
    private readonly Dictionary<string, (Guid Id, string DisplayName)?> _recordTypeRefCache = new();
    private readonly SemaphoreSlim _recordTypeCacheSemaphore = new(1, 1);

    // ---------------------------------------------------------------------------
    // Private Dataverse deserialization row types
    // ---------------------------------------------------------------------------

    private sealed class ODataResult<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Collection paging (unified-access-control-r2 task 105, ISS-002 / #963, NFR-03)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The page size every collection read asks for, through <c>Prefer: odata.maxpagesize</c>.
    /// </summary>
    /// <remarks>
    /// Dataverse returns <c>@odata.nextLink</c> ONLY under server-driven paging. A <c>$top</c> query returns no
    /// <c>nextLink</c> at all (Microsoft Learn, "Use OData to query data — Limit the number of rows": "Don't use
    /// <c>$top</c> when you request pages of data"), and <c>$top</c> combined with <c>maxpagesize</c> silently returns
    /// the SMALLER of the two with no <c>nextLink</c> (live, spaarkedev1, 2026-10-08: <c>$top=3</c> +
    /// <c>maxpagesize=2</c> → 2 rows, no <c>nextLink</c>). So no collection read here carries <c>$top</c>;
    /// <see cref="GetCollectionAsync{TRow}"/> refuses one.
    /// </remarks>
    internal const int CollectionPageSize = 200;

    /// <summary>
    /// How many pages one collection read follows before it stops and reports itself TRUNCATED — a row cap of
    /// <see cref="MaxCollectionRows"/>, the same 5,000 as the evaluator's NFR-03 ceiling. A detector, not a target:
    /// hitting it with a <c>nextLink</c> still present is reported, never returned as a complete list.
    /// </summary>
    internal const int MaxCollectionPages = 25;

    /// <summary>The row cap <see cref="MaxCollectionPages"/> × <see cref="CollectionPageSize"/> produces.</summary>
    internal const int MaxCollectionRows = CollectionPageSize * MaxCollectionPages;

    /// <summary>
    /// How many ids one <c>id eq … or id eq …</c> filter carries. A GET URL is limited to 32,768 characters (Microsoft
    /// Learn, "URL length limitations"); one escaped clause is ~60, so 100 ids is ~6 KB. Without chunking, the contact
    /// list's detail read would fail (400 → empty list) once a project had a few hundred participants.
    /// </summary>
    internal const int IdFilterChunkSize = 100;

    /// <summary>How a collection read ended.</summary>
    internal enum CollectionReadOutcome
    {
        /// <summary>Followed to the last page — the rows are the whole set.</summary>
        Complete = 0,

        /// <summary>Stopped at <see cref="MaxCollectionPages"/> with more rows remaining — TRUNCATED.</summary>
        CapReached = 1,

        /// <summary>A page after the first failed; the rows are a prefix of the set — TRUNCATED.</summary>
        LaterPageFailed = 2,

        /// <summary>
        /// The first page failed: NOTHING was read — TRUNCATED, never "an empty list". Before task 105 this came back as
        /// an empty, unflagged list, so a Dataverse fault showed the user "no documents" (round of 2026-10-08: fixed in
        /// the same PR, the same honesty class). A create-payload reader must fail its request instead
        /// (<see cref="ResolveRecordTypeRefAsync"/>).
        /// </summary>
        FirstPageFailed = 3,
    }

    /// <summary>The rows a collection read returned and how it ended.</summary>
    internal sealed record CollectionRead<TRow>(IReadOnlyList<TRow> Rows, CollectionReadOutcome Outcome)
    {
        /// <summary>The rows are known to be INCOMPLETE (possibly empty) — never to be shown as a complete list.</summary>
        public bool Truncated => Outcome != CollectionReadOutcome.Complete;
    }

    /// <summary>The project columns the external SPA reads (internal so <see cref="MapProject"/> is testable).</summary>
    internal sealed class ProjectRow
    {
        [JsonPropertyName("sprk_projectid")] public string? SprkProjectid { get; set; }
        [JsonPropertyName("sprk_projectname")] public string? SprkName { get; set; }
        [JsonPropertyName("sprk_projectnumber")] public string? SprkReferencenumber { get; set; }
        [JsonPropertyName("sprk_projectdescription")] public string? SprkDescription { get; set; }
        [JsonPropertyName("sprk_issecure")] public bool? SprkIssecure { get; set; }
        [JsonPropertyName("statecode")] public int? SprkStatus { get; set; }
        [JsonPropertyName("createdon")] public string? Createdon { get; set; }
        [JsonPropertyName("modifiedon")] public string? Modifiedon { get; set; }
    }

    private sealed class DocumentRow
    {
        [JsonPropertyName("sprk_documentid")] public string? SprkDocumentid { get; set; }
        [JsonPropertyName("sprk_documentname")] public string? SprkName { get; set; }
        [JsonPropertyName("sprk_documenttype")] public string? SprkDocumenttype { get; set; }
        [JsonPropertyName("sprk_filesummary")] public string? SprkSummary { get; set; }
        [JsonPropertyName("_sprk_project_value")] public string? SprkProjectidValue { get; set; }
        [JsonPropertyName("createdon")] public string? Createdon { get; set; }
        [JsonPropertyName(CreatedByPersonValueAttribute)] public string? CreatedByPersonValue { get; set; }
        [JsonPropertyName(CreatedByContactValueAttribute)] public string? CreatedByContactValue { get; set; }
    }

    /// <summary>
    /// Dataverse row for <c>sprk_todo</c>. Replaces legacy <c>EventRow</c>.
    /// </summary>
    private sealed class TodoRow
    {
        [JsonPropertyName("sprk_todoid")] public string? SprkTodoid { get; set; }
        [JsonPropertyName("sprk_name")] public string? SprkName { get; set; }
        [JsonPropertyName("sprk_notes")] public string? SprkNotes { get; set; }
        [JsonPropertyName("sprk_duedate")] public string? SprkDuedate { get; set; }
        [JsonPropertyName("sprk_priorityscore")] public int? SprkPriorityscore { get; set; }
        [JsonPropertyName("sprk_effortscore")] public int? SprkEffortscore { get; set; }
        [JsonPropertyName("sprk_todocolumn")] public int? SprkTodocolumn { get; set; }
        [JsonPropertyName("sprk_todopinned")] public bool? SprkTodopinned { get; set; }
        [JsonPropertyName("statecode")] public int? Statecode { get; set; }
        [JsonPropertyName("statuscode")] public int? Statuscode { get; set; }
        [JsonPropertyName("createdon")] public string? Createdon { get; set; }
        [JsonPropertyName("_sprk_regardingproject_value")] public string? SprkRegardingprojectValue { get; set; }
        [JsonPropertyName("_sprk_regardingmatter_value")] public string? SprkRegardingmatterValue { get; set; }
        [JsonPropertyName("_sprk_regardingworkassignment_value")] public string? SprkRegardingworkassignmentValue { get; set; }
        [JsonPropertyName("sprk_regardingrecordid")] public string? SprkRegardingrecordid { get; set; }
        [JsonPropertyName("sprk_regardingrecordname")] public string? SprkRegardingrecordname { get; set; }
        [JsonPropertyName("sprk_regardingrecordurl")] public string? SprkRegardingrecordurl { get; set; }
        [JsonPropertyName(CreatedByPersonValueAttribute)] public string? CreatedByPersonValue { get; set; }
        [JsonPropertyName(CreatedByContactValueAttribute)] public string? CreatedByContactValue { get; set; }
    }

    // Task 097: sprk_event has NO sprk_name / sprk_status columns (live: HTTP 400 "Could not find a property named
    // 'sprk_name'" — so the external events list and create always failed). The live columns are sprk_eventname (the
    // primary name) and, for status, statuscode — the column the rest of the BFF (and POST /events/{id}/complete) writes.
    // Review F2 (coordinator interim decision, pending the owner's two-status-columns decision): statuscode is the status
    // of record; sprk_eventstatus is neither read nor written here. The SPA wire names (sprk_name / sprk_status on
    // ExternalEventDto) are unchanged; only the Dataverse side is mapped here.
    internal const string EventNameColumn = "sprk_eventname";
    internal const string EventStatusColumn = "statuscode";

    /// <summary>
    /// The statuses an external caller may create an event in (review F9): Draft or Open — both Active. Any other value
    /// is refused with 400. When omitted the event is created Open, matching POST /api/v1/events.
    /// </summary>
    internal static readonly IReadOnlySet<int> ExternalCreatableStatuses =
        new HashSet<int> { EventStatusCode.Draft, EventStatusCode.Open };

    private sealed class EventRow
    {
        [JsonPropertyName("sprk_eventid")] public string? SprkEventid { get; set; }
        [JsonPropertyName(EventNameColumn)] public string? SprkName { get; set; }
        [JsonPropertyName("sprk_duedate")] public string? SprkDuedate { get; set; }
        [JsonPropertyName(EventStatusColumn)] public int? SprkStatus { get; set; }
        [JsonPropertyName("createdon")] public string? Createdon { get; set; }
        [JsonPropertyName("_sprk_regardingproject_value")] public string? SprkRegardingprojectValue { get; set; }
        [JsonPropertyName(CreatedByPersonValueAttribute)] public string? CreatedByPersonValue { get; set; }
        [JsonPropertyName(CreatedByContactValueAttribute)] public string? CreatedByContactValue { get; set; }
    }

    private sealed class ContactRow
    {
        [JsonPropertyName("contactid")] public string? Contactid { get; set; }
        [JsonPropertyName("fullname")] public string? Fullname { get; set; }
        [JsonPropertyName("firstname")] public string? Firstname { get; set; }
        [JsonPropertyName("lastname")] public string? Lastname { get; set; }
        [JsonPropertyName("emailaddress1")] public string? Emailaddress1 { get; set; }
        [JsonPropertyName("telephone1")] public string? Telephone1 { get; set; }
        [JsonPropertyName("jobtitle")] public string? Jobtitle { get; set; }
        [JsonPropertyName("_parentcustomerid_value")] public string? ParentcustomeridValue { get; set; }
    }

    private sealed class AccountRow
    {
        [JsonPropertyName("accountid")] public string? Accountid { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("websiteurl")] public string? Websiteurl { get; set; }
        [JsonPropertyName("telephone1")] public string? Telephone1 { get; set; }
        [JsonPropertyName("address1_city")] public string? Address1City { get; set; }
        [JsonPropertyName("address1_country")] public string? Address1Country { get; set; }
    }

    private sealed class AccessLinkRow
    {
        [JsonPropertyName("_sprk_contact_value")] public string? ContactId { get; set; }
    }

    /// <summary>
    /// Dataverse row for <c>sprk_recordtype_ref</c>. Used to resolve the regarding-record-type
    /// lookup target when applying resolver fields per ADR-024.
    /// </summary>
    private sealed class RecordTypeRefRow
    {
        [JsonPropertyName("sprk_recordtype_refid")] public string? Id { get; set; }
        [JsonPropertyName("sprk_recorddisplayname")] public string? DisplayName { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------------------

    public ExternalDataService(
        HttpClient httpClient,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<ExternalDataService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _credential = credential;
        _logger = logger;
    }

    // ---------------------------------------------------------------------------
    // Project queries
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Retrieves multiple projects by their IDs.
    /// Used by the workspace home page to list the user's accessible projects.
    /// </summary>
    /// <remarks>
    /// Task 105: the ids are read in chunks of <see cref="IdFilterChunkSize"/> (one OR-filter for every id overran the
    /// 32 KB URL limit past a few hundred projects, and the 400 came back as "no projects"), and the response says
    /// <see cref="ExternalCollectionResponse{T}.Truncated"/> when a chunk or page was lost.
    /// </remarks>
    public virtual async Task<ExternalCollectionResponse<ExternalProjectDto>> GetProjectsAsync(
        IEnumerable<Guid> projectIds, CancellationToken ct = default)
    {
        var ids = projectIds.Select(id => id.ToString("D")).Distinct().ToList();
        if (ids.Count == 0) return new ExternalCollectionResponse<ExternalProjectDto>();

        var select = "sprk_projectid,sprk_projectname,sprk_projectnumber,sprk_projectdescription,sprk_issecure,statecode,createdon,modifiedon";
        // H5 (task 022, 2026-08-24): $orderby said `sprk_name`, which does NOT exist on sprk_project
        // (live metadata: the display name is sprk_projectname — the $select above already had it right).
        // Dataverse answered 400, GetCollectionAsync caught it and returned an empty list, so the
        // external SPA rendered "you have no grants" for every caller WITH grants. Sixth instance of the
        // stale-column class in this project; the select/orderby split is why it survived review.
        var read = await GetByIdsAsync<ProjectRow>(
            "sprk_projects", "sprk_projectid", ids, select, "sprk_projectname asc,sprk_projectid asc", ct);

        var rows = read.Rows;
        if (ids.Count > IdFilterChunkSize)
            rows = rows.OrderBy(r => r.SprkName, StringComparer.CurrentCultureIgnoreCase).ToList();

        WarnOnEmptySecureFlag(rows);
        return new ExternalCollectionResponse<ExternalProjectDto>
        {
            Value = rows.Select(MapProject).ToList(),
            Truncated = read.Truncated,
        };
    }

    /// <summary>Retrieves a single project by ID.</summary>
    public virtual async Task<ExternalProjectDto?> GetProjectByIdAsync(Guid projectId, CancellationToken ct = default)
    {
        var select = "sprk_projectid,sprk_projectname,sprk_projectnumber,sprk_projectdescription,sprk_issecure,statecode,createdon,modifiedon";
        var url = $"{GetApiUrl()}/sprk_projects({projectId})?$select={select}";

        var row = await GetSingleAsync<ProjectRow>(url, ct);
        if (row is null) return null;
        WarnOnEmptySecureFlag(new[] { row });
        return MapProject(row);
    }

    // ---------------------------------------------------------------------------
    // Document queries
    // ---------------------------------------------------------------------------

    // ---------------------------------------------------------------------------
    // Document parents (spaarke-SPA-external-access-platform-r3 task 031, FR-24)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The record types an external caller can list, download and upload documents on (FR-24). Each is a
    /// <c>sprk_document</c> parent through its own lookup (<see cref="DocumentParentBinding"/>).
    /// </summary>
    /// <remarks>
    /// Invoice is NOT here: invoices are not in the caller's accessible set as roots, so there is no Tier-2 answer for
    /// an invoice id. Task 031 escalated it (POML escalation trigger 2). Adding it is one row in
    /// <see cref="DocumentParents"/> plus its rights arm in the endpoint, once the owner decides how an invoice is
    /// authorized.
    /// </remarks>
    public enum DocumentParentKind
    {
        Project = 1,
        Matter = 2,
        WorkAssignment = 3,
        ServiceRequest = 4,
    }

    /// <summary>
    /// Everything the Web API path needs to read or write a document against one parent: the parent entity, its entity
    /// set, the <c>sprk_document</c> navigation property that binds it, and the lookup attribute (whose <c>_..._value</c>
    /// form filters and scopes).
    /// </summary>
    /// <remarks>
    /// Names from the solution source (<c>Entities/sprk_Document/Entity.xml</c>, <c>Other/Relationships/*.xml</c>;
    /// task 031). The navigation property differs in case from the attribute, and the service-request lookup is
    /// <c>sprk_relatedservicerequest</c>, not <c>sprk_servicerequest</c>.
    /// </remarks>
    internal sealed record DocumentParentBinding(
        DocumentParentKind Kind,
        string EntityLogicalName,
        string EntitySet,
        string NavigationProperty,
        string LookupAttribute)
    {
        /// <summary>The <c>_..._value</c> form used in <c>$filter</c> and <c>$select</c>.</summary>
        public string LookupValueAttribute => $"_{LookupAttribute}_value";

        /// <summary>The full <c>@odata.bind</c> key, e.g. <c>sprk_Project@odata.bind</c>.</summary>
        public string BindKey => $"{NavigationProperty}@odata.bind";
    }

    private static readonly IReadOnlyDictionary<DocumentParentKind, DocumentParentBinding> DocumentParents =
        new Dictionary<DocumentParentKind, DocumentParentBinding>
        {
            [DocumentParentKind.Project] = new(
                DocumentParentKind.Project, "sprk_project", "sprk_projects", "sprk_Project", "sprk_project"),
            [DocumentParentKind.Matter] = new(
                DocumentParentKind.Matter, "sprk_matter", "sprk_matters", "sprk_Matter", "sprk_matter"),
            [DocumentParentKind.WorkAssignment] = new(
                DocumentParentKind.WorkAssignment, "sprk_workassignment", "sprk_workassignments",
                "sprk_WorkAssignment", "sprk_workassignment"),
            [DocumentParentKind.ServiceRequest] = new(
                DocumentParentKind.ServiceRequest, "sprk_servicerequest", "sprk_servicerequests",
                "sprk_RelatedServiceRequest", "sprk_relatedservicerequest"),
        };

    /// <summary>The binding for a document parent kind. Throws for a value outside the table: a programming error.</summary>
    internal static DocumentParentBinding GetDocumentParentBinding(DocumentParentKind kind) =>
        DocumentParents.TryGetValue(kind, out var binding)
            ? binding
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a document parent kind.");

    /// <summary>Retrieves all documents linked to the specified parent record through that parent's lookup.</summary>
    /// <remarks>
    /// Task 105: followed page by page up to <see cref="MaxCollectionRows"/> (it was a single <c>$top=200</c> read, so
    /// a project's 201st document was silently missing); a cut-short list says
    /// <see cref="ExternalCollectionResponse{T}.Truncated"/>. The primary key breaks <c>createdon</c> ties so pages
    /// never overlap. Task 031 generalized it from project to every <see cref="DocumentParentKind"/>; the project query
    /// is unchanged.
    /// </remarks>
    public virtual async Task<ExternalCollectionResponse<ExternalDocumentDto>> GetDocumentsAsync(
        DocumentParentKind parentKind, Guid parentId, CancellationToken ct = default)
    {
        var url = BuildDocumentListUrl(GetApiUrl(), parentKind, parentId);

        var read = await GetCollectionAsync<DocumentRow>(url, ct);
        return new ExternalCollectionResponse<ExternalDocumentDto>
        {
            Value = read.Rows.Select(MapDocument).ToList(),
            Truncated = read.Truncated,
        };
    }

    /// <summary>The document list URL for one parent. Pure, so a test can read the filter (task 031).</summary>
    internal static string BuildDocumentListUrl(string apiUrl, DocumentParentKind parentKind, Guid parentId)
    {
        var binding = GetDocumentParentBinding(parentKind);
        // Task 032 (FR-26, review 031 K2): both creator stamps, so the SPA can offer Delete only on the caller's own rows.
        var select = "sprk_documentid,sprk_documentname,sprk_documenttype,sprk_filesummary,_sprk_project_value,createdon,"
                     + CreatorStampSelect;
        var filter = Uri.EscapeDataString($"{binding.LookupValueAttribute} eq {parentId}");
        return $"{apiUrl}/sprk_documents?$filter={filter}&$select={select}&$orderby=createdon desc,sprk_documentid asc";
    }

    /// <summary>
    /// Creates a <c>sprk_document</c> row for a file already uploaded to SPE, linked to its parent record.
    /// </summary>
    /// <remarks>
    /// <para>Called ONLY after the bytes are in SPE — the pointers in <paramref name="pointers"/> are
    /// produced by the upload endpoint from a server-derived container, never from client input.</para>
    ///
    /// <para><b>Field choices mirror the canonical wizard create</b>
    /// (<c>Spaarke.UI.Components/services/document-upload/DocumentRecordService.ts</c>) rather than
    /// inventing a second convention: <c>sprk_graphdriveid</c> is the canonical Document container
    /// field and <c>sprk_containerid</c> stays NULL on <c>sprk_document</c> (the Phase F backfill audit
    /// depends on that, and the wizard has a regression test asserting it). <c>sprk_documentname</c> is
    /// the display name; <c>sprk_filename</c> / <c>sprk_filesize</c> / <c>sprk_graphitemid</c> /
    /// <c>sprk_filepath</c> carry the file identity.</para>
    ///
    /// <para>The parent bind (<c>sprk_Project@odata.bind</c>, <c>sprk_Matter@odata.bind</c>, ...) uses the PascalCase
    /// navigation property from <see cref="DocumentParentBinding"/>, the same case-sensitive form the event and to-do
    /// creates use. The read side of this service filters documents on the same lookup, so this is the bind that makes
    /// an uploaded document visible in the SPA's own list (task 031).</para>
    /// </remarks>
    public virtual async Task<ExternalDocumentDto> CreateDocumentAsync(
        DocumentParentKind parentKind, Guid parentId, ExternalUploadedFilePointers pointers, Guid owningTeamId,
        CallerPrincipal creator, CancellationToken ct = default)
    {
        var body = BuildDocumentCreatePayload(parentKind, parentId, pointers, owningTeamId);
        StampCreator(body, creator); // FR-25 (task 030)

        var token = await GetAppOnlyTokenAsync(ct);

        var url = $"{GetApiUrl()}/sprk_documents";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.Add("OData-MaxVersion", "4.0");
        httpRequest.Headers.Add("OData-Version", "4.0");
        httpRequest.Headers.Add("Prefer", "return=representation");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "[EXT-DATA] Create document failed for {ParentKind} {ParentId}: {Status} — {Body}",
                parentKind, parentId, response.StatusCode, errorBody);
            throw new InvalidOperationException($"Failed to create document: {response.StatusCode}");
        }

        var row = await response.Content.ReadFromJsonAsync<DocumentRow>(ct);
        if (row is null)
            throw new InvalidOperationException("Dataverse returned no document data after create");

        return MapDocument(row);
    }

    /// <summary>
    /// The COMPLETE create payload for an external upload's <c>sprk_document</c>: the file identity, the parent
    /// bind (task 031: any <see cref="DocumentParentKind"/>), and the OWNER (unified-access-control-r2 task 146).
    /// </summary>
    /// <remarks>
    /// <para><b>The owner is passed in, and its absence refuses</b> (the <c>OfficeDocumentPersistence</c> pattern,
    /// PR #1045 F4). The route resolves it from the project through <c>IRecordOwnershipResolver</c> BEFORE any SPE
    /// write: a document filed to a secure project is owned by the named Secure team, any other by the project's
    /// business-unit default team. This create is app-only, so without <c>ownerid</c> Dataverse makes the BFF
    /// application user the owner — in the root business unit, readable by every root-BU user with ordinary depth,
    /// which is how a secure project's documents were never isolated (C10).</para>
    /// <para>Pure so a test can read the payload; <see cref="CreateDocumentAsync"/> is a substitution seam.</para>
    /// </remarks>
    internal static Dictionary<string, object?> BuildDocumentCreatePayload(
        DocumentParentKind parentKind, Guid parentId, ExternalUploadedFilePointers pointers, Guid owningTeamId)
    {
        var parent = GetDocumentParentBinding(parentKind);
        if (parentId == Guid.Empty)
            throw new ArgumentException("Parent id must be a non-empty GUID.", nameof(parentId));
        ArgumentNullException.ThrowIfNull(pointers);
        RequireOwner(owningTeamId, "sprk_document");

        var body = new Dictionary<string, object?>
        {
            ["sprk_documentname"] = pointers.FileName,
            ["sprk_filename"] = pointers.FileName,
            ["sprk_graphitemid"] = pointers.ItemId,
            [Spaarke.Dataverse.DocumentPointerBinding.BoundItemIdColumn] = pointers.ItemId, // Task 171 round 72 (F4): the field-secured copy the pointer check compares — same write, same value.
            ["sprk_graphdriveid"] = pointers.DriveId,
            [parent.BindKey] = $"/{parent.EntitySet}({parentId})",
            [OwnerBindKey] = $"/teams({owningTeamId})",
        };

        if (pointers.FileSizeBytes.HasValue)
            body["sprk_filesize"] = pointers.FileSizeBytes.Value;
        if (!string.IsNullOrWhiteSpace(pointers.WebUrl))
            body["sprk_filepath"] = pointers.WebUrl;

        return body;
    }

    /// <summary>The <c>ownerid</c> bind key every create on this surface carries (task 146).</summary>
    internal const string OwnerBindKey = "ownerid@odata.bind";

    /// <summary>
    /// Refuses a create that arrives with no owner — never an app-owned row (task 146, ADR-003). Every route
    /// resolves the owner first, so this fires only on a programming error.
    /// </summary>
    private static void RequireOwner(Guid owningTeamId, string entityLogicalName)
    {
        if (owningTeamId == Guid.Empty)
            throw new InvalidOperationException(
                $"A {entityLogicalName} create on the external surface must carry its owning team, resolved through "
                + "IRecordOwnershipResolver from the record it is filed to; refusing to create it app-owned (task 146).");
    }

    // ---------------------------------------------------------------------------
    // FR-25 creator stamp (spaarke-SPA-external-access-platform-r3 task 030)
    // ---------------------------------------------------------------------------

    /// <summary>The <c>sprk_createdbyperson</c> (→ systemuser) bind key: the internal plane's column (task 146 c1-r1),
    /// spelled once in <see cref="RecordCreatorPersonColumn"/>.</summary>
    internal const string CreatedByPersonBindKey = RecordCreatorPersonColumn.NavigationProperty + "@odata.bind";

    /// <summary>The <c>sprk_createdbycontact</c> (→ contact) bind key. Navigation property <c>sprk_CreatedByContact</c>, as the
    /// task 020 schema source declares it (<c>Other/Relationships/Contact.xml</c>, <c>sprk_contact_*_createdbycontact</c>).</summary>
    internal const string CreatedByContactBindKey = "sprk_CreatedByContact@odata.bind";

    /// <summary>Columns that name who created or requested a row. The server alone writes them (FR-11, FR-25).</summary>
    private static readonly string[] CreatorColumns =
    {
        RecordCreatorPersonColumn.LogicalName, "sprk_createdbycontact", "sprk_requestedby", "createdby", "createdonbehalfby",
    };

    /// <summary>
    /// THE creator stamp for every create on <c>/api/v1/external/**</c> (FR-25; closes auth record L-5 for the external
    /// plane). Writes exactly one of <c>sprk_createdbyperson</c> (a systemuser principal) or <c>sprk_createdbycontact</c> (a
    /// contact principal: a workforce contact or a CIAM partner) onto <paramref name="body"/>, from the RESOLVED principal.
    /// </summary>
    /// <remarks>
    /// <para><b>The kind decides, not the plane.</b> A principal carrying <see cref="CallerPrincipal.SystemUserId"/> is a
    /// systemuser, even when it also carries a linked contact (task 141's user↔contact link) — the same rule as
    /// <see cref="CallerPrincipal.IsContactPrincipal"/>. Every create is app-only, so Dataverse <c>createdby</c> is the
    /// BFF application user; this stamp is the only record of who created the row, and the delete-own check (FR-26,
    /// task 032) reads it.</para>
    /// <para><b>Client input never lands.</b> Any key in the payload that names a creator column (any casing, its
    /// <c>@odata.bind</c> or <c>_x_value</c> form) is removed before the stamp is written. The request DTOs are closed, so
    /// nothing should be there; this keeps it so if a DTO or builder ever passes fields through.</para>
    /// <para><b>Fails closed.</b> A principal that is neither kind (or carries an empty systemuser id) throws before
    /// anything is posted: the surface never writes an unattributed row.</para>
    /// <para>Sender stamps on message send (task 034) and the service-request requester (task 035) call this too.</para>
    /// </remarks>
    internal static void StampCreator(IDictionary<string, object?> body, CallerPrincipal creator)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(creator);

        // Decide first, so a refusal leaves the payload untouched.
        EnsureCanStampCreator(creator);
        Guid? systemUserId = creator.SystemUserId;
        Guid? contactId = systemUserId is null ? creator.ContactId : null;

        foreach (var key in body.Keys.Where(NamesCreatorColumn).ToList())
            body.Remove(key);

        if (systemUserId is { } person)
            RecordCreatorPersonColumn.BindIfKnown(body, person);
        else
            body[CreatedByContactBindKey] = $"/contacts({contactId!.Value:D})";
    }

    /// <summary>The <c>sprk_createdbycontact</c> (→ contact) lookup's logical name: what an SDK create payload sets.</summary>
    internal const string CreatedByContactColumn = "sprk_createdbycontact";

    /// <summary>
    /// <see cref="StampCreator(IDictionary{string, object?}, CallerPrincipal)"/> for an SDK create payload — the external
    /// message post (task 034, FR-11) creates its <c>sprk_communication</c> through <c>CommunicationService</c>'s SDK
    /// writer. The same decision, the same strip of every creator column already on the row, and exactly one stamp:
    /// <c>sprk_createdbyperson</c> (a systemuser principal) or <c>sprk_createdbycontact</c> (a contact principal).
    /// </summary>
    /// <exception cref="InvalidOperationException">The principal is not attributable; the row is left untouched.</exception>
    internal static void StampCreator(Microsoft.Xrm.Sdk.Entity row, CallerPrincipal creator)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(creator);

        // Decide first, so a refusal leaves the row untouched.
        EnsureCanStampCreator(creator);
        Guid? systemUserId = creator.SystemUserId;
        Guid? contactId = systemUserId is null ? creator.ContactId : null;

        foreach (var key in row.Attributes.Keys.Where(NamesCreatorColumn).ToList())
            row.Attributes.Remove(key);

        if (systemUserId is { } person)
            Sprk.Bff.Api.Services.Dataverse.RecordCreatorPerson.Stamp(row, person);
        else
            row[CreatedByContactColumn] = new Microsoft.Xrm.Sdk.EntityReference("contact", contactId!.Value);
    }

    /// <summary>Whether <paramref name="creator"/> is attributable: a systemuser (non-empty id) or a contact principal.
    /// The one rule <see cref="StampCreator"/> enforces; routes call it to refuse an unattributable caller BEFORE any
    /// side effect (the upload route's SPE write) rather than after.</summary>
    internal static bool CanStampCreator(CallerPrincipal creator)
    {
        ArgumentNullException.ThrowIfNull(creator);
        return creator.SystemUserId is { } su ? su != Guid.Empty : creator.IsContactPrincipal;
    }

    /// <summary>Throws (the <see cref="StampCreator"/> refusal) when <paramref name="creator"/> is not attributable.
    /// Called at the top of each create seam so the decision precedes every token, lookup and request.</summary>
    internal static void EnsureCanStampCreator(CallerPrincipal creator)
    {
        ArgumentNullException.ThrowIfNull(creator);
        if (creator.SystemUserId == Guid.Empty)
            throw new InvalidOperationException(
                "The caller principal carries an empty systemuser id; refusing to create an unattributed row (FR-25).");
        if (!CanStampCreator(creator))
            throw new InvalidOperationException(
                "The caller principal resolves to neither a systemuser nor a contact; refusing to create an unattributed "
                + "row (FR-25).");
    }

    /// <summary>Whether <paramref name="key"/> names a creator column (<see cref="CreatorColumns"/>): the logical name, its
    /// <c>_x_value</c> read form, or any <c>x@…</c> annotation such as <c>@odata.bind</c>. Case-insensitive.</summary>
    internal static bool NamesCreatorColumn(string? key)
    {
        var trimmed = key?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return false;

        foreach (var column in CreatorColumns)
        {
            if (string.Equals(trimmed, column, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, $"_{column}_value", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(column + "@", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the document's parent id THROUGH THE LOOKUP OF <paramref name="parentKind"/>, and its display name. The
    /// external download and versions endpoints use it (task 027; generalized by task 031) for document→parent
    /// authorization scoping BEFORE any SPE pointer resolution or Graph content read.
    /// </summary>
    /// <remarks>
    /// App-only Dataverse read. Returns (null, null) when the document does not exist or cannot be read, and
    /// (null, name) when it is not linked through that lookup. Does NOT expose any Graph pointer (driveId/itemId).
    /// </remarks>
    public virtual async Task<(Guid? ParentId, string? DocumentName)> GetDocumentParentAndNameAsync(
        DocumentParentKind parentKind, Guid documentId, CancellationToken ct = default)
    {
        var binding = GetDocumentParentBinding(parentKind);
        var url = $"{GetApiUrl()}/sprk_documents({documentId})?$select=sprk_documentname,{binding.LookupValueAttribute}";

        var row = await GetSingleAsync<Dictionary<string, JsonElement>>(url, ct);
        if (row is null) return (null, null);

        var name = row.TryGetValue("sprk_documentname", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()
            : null;
        return (GuidValue(row, binding.LookupValueAttribute), name);
    }

    /// <summary>
    /// The requester (<c>sprk_requestedby</c>, a contact) of a service request. Null when the request does not exist
    /// (a genuine 404) or names no requester. App-only Dataverse read.
    /// </summary>
    /// <remarks>
    /// Task 031 (FR-24): a service request's document rights come from the submitter scope (the requester is the
    /// caller), so this is the one fact that decision needs. A read that FAILS (non-404 status, transport error, token
    /// failure) throws <see cref="DataverseUnavailableException"/> rather than returning null: "could not tell" must not
    /// be reported as "no such request", or a throttle would 403 the real requester.
    /// </remarks>
    /// <exception cref="DataverseUnavailableException">The read could not be completed.</exception>
    public virtual async Task<Guid?> GetServiceRequestRequesterAsync(Guid serviceRequestId, CancellationToken ct = default)
    {
        var url = $"{GetApiUrl()}/sprk_servicerequests({serviceRequestId})?$select=_sprk_requestedby_value";

        var row = await GetSingleOrThrowAsync<Dictionary<string, JsonElement>>(url, ct);
        return row is null ? null : GuidValue(row, "_sprk_requestedby_value");
    }

    /// <summary>An app-only Dataverse read could not be completed (not a 404): the caller reports it as a 503.</summary>
    public sealed class DataverseUnavailableException : Exception
    {
        public DataverseUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>A GUID-valued column of a dictionary row, or null when it is absent, null or not a GUID.</summary>
    private static Guid? GuidValue(IReadOnlyDictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value)
        && value.ValueKind == JsonValueKind.String
        && Guid.TryParse(value.GetString(), out var id)
            ? id
            : null;

    // ---------------------------------------------------------------------------
    // To Do queries and mutations (smart-todo-decoupling-r3 FR-29)
    //
    // Replaces the legacy event-based to-do surface. To-dos are scoped to one of
    // the THREE A-9 accessible roots — project, matter or work assignment — via the
    // matching sprk_regarding* lookup. When a create writes that association, the
    // four resolver fields are applied atomically per ADR-024.
    //
    // ⚠️ This count DECAYS — treat it as a measurement, not a fact. It read "11" until
    // task 009 measured 13 live (2026-08-24); it is 14 as of 2026-09-09 (task 029),
    // because record-header-and-notepad-r2 added sprk_regardingagreement on 2026-08-25.
    // Only the first correction was an error; the second is a number that simply aged,
    // which is the same hazard CLAUDE.md §10 documents for the publish-size baseline.
    // ALWAYS state the as-of date beside the number, and re-measure before relying on it.
    // Live 2026-09-09, sprk_todo:
    //   agreement · analysis · budget · communication · contact · document · event ·
    //   invoice · matter · organization · project · reportcard · servicerequest ·
    //   workassignment          (+ sprk_regardingrecordtype, the resolver's type lookup)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Which of the three A-9 accessible-root types a to-do is parented to, as resolved by
    /// <see cref="ExternalDataService.GetTodoRootAsync"/>.
    /// </summary>
    /// <remarks>
    /// <c>None</c> covers three distinct situations that all deny identically: the to-do is absent,
    /// the to-do could not be read, or it is parented to one of the eleven regarding types that have no
    /// accessible set. <c>Ambiguous</c> means more than one root lookup was populated — also a deny.
    /// </remarks>
    public enum TodoRootKind
    {
        None = 0,
        Project = 1,
        Matter = 2,
        WorkAssignment = 3,
        Ambiguous = 4,
    }

    /// <summary>
    /// Everything the Web API path needs to read or write a to-do against one accessible root:
    /// the entity, its entity set, the <c>@odata.bind</c> navigation property, the lookup attribute
    /// and its <c>_value</c> filter form, and the column that holds the root's DISPLAY name.
    /// </summary>
    /// <remarks>
    /// <para><b>Every field is live-metadata verified (2026-09-09, task 029) — none is derivable.</b>
    /// Three separate conventions break across these three roots:</para>
    /// <list type="bullet">
    ///   <item>the navigation property is PascalCase and differs from the attribute name
    ///     (<c>sprk_regardingmatter</c> binds as <c>sprk_RegardingMatter</c>);</item>
    ///   <item>the DISPLAY-name column is a different name on each root — <c>sprk_projectname</c>,
    ///     <c>sprk_mattername</c>, <c>sprk_name</c>. <c>sprk_matter</c> has no <c>sprk_name</c> and
    ///     <c>sprk_workassignment</c> has no <c>sprk_workassignmentname</c>, so a name built by
    ///     convention fails on two of the three;</item>
    ///   <item>the display name is NOT the <c>PrimaryNameAttribute</c> for project or matter (those
    ///     are <c>sprk_projectnumber</c> / <c>sprk_matternumber</c>) — reaching for the primary name
    ///     would stamp a case number into <c>sprk_regardingrecordname</c>.</item>
    /// </list>
    ///
    /// <para><b>Why this table and not <see cref="Sprk.Bff.Api.Services.Workspace.TodoRegardingBuilder"/>'s
    /// (CLAUDE.md §11).</b> That map is keyed by entity logical name and carries only the lookup
    /// attribute, because the SDK path binds through an <c>EntityReference</c> and never needs a
    /// navigation property or an entity-set name. This is not a second copy of it: these names were
    /// previously inline string literals in <see cref="CreateTodoAsync"/> and
    /// <see cref="GetTodosAsync"/>, and collecting them here makes the count of places they live go
    /// DOWN. If a fourth root is ever added (service request — task 028), adding a row here is the
    /// whole data-layer change.</para>
    /// </remarks>
    internal sealed record TodoRootBinding(
        TodoRootKind Kind,
        string EntityLogicalName,
        string EntitySet,
        string NavigationProperty,
        string LookupAttribute,
        string DisplayNameAttribute)
    {
        /// <summary>The <c>_..._value</c> form used in <c>$filter</c> and <c>$select</c>.</summary>
        public string LookupValueAttribute => $"_{LookupAttribute}_value";

        /// <summary>The full <c>@odata.bind</c> key, e.g. <c>sprk_RegardingMatter@odata.bind</c>.</summary>
        public string BindKey => $"{NavigationProperty}@odata.bind";
    }

    private static readonly IReadOnlyDictionary<TodoRootKind, TodoRootBinding> RootBindings =
        new Dictionary<TodoRootKind, TodoRootBinding>
        {
            [TodoRootKind.Project] = new(
                TodoRootKind.Project, "sprk_project", "sprk_projects",
                "sprk_RegardingProject", "sprk_regardingproject", "sprk_projectname"),
            [TodoRootKind.Matter] = new(
                TodoRootKind.Matter, "sprk_matter", "sprk_matters",
                "sprk_RegardingMatter", "sprk_regardingmatter", "sprk_mattername"),
            [TodoRootKind.WorkAssignment] = new(
                TodoRootKind.WorkAssignment, "sprk_workassignment", "sprk_workassignments",
                "sprk_RegardingWorkAssignment", "sprk_regardingworkassignment", "sprk_name"),
        };

    /// <summary>
    /// The binding for a scopeable root kind, or <c>null</c> for <see cref="TodoRootKind.None"/> /
    /// <see cref="TodoRootKind.Ambiguous"/> — which are deny states, not addressable roots.
    /// </summary>
    internal static TodoRootBinding? TryGetRootBinding(TodoRootKind kind) =>
        RootBindings.TryGetValue(kind, out var binding) ? binding : null;

    /// <summary>
    /// Returns the to-do's regarding-ROOT (project, matter, or work assignment), its id, and its
    /// display name — used by the external PATCH endpoint (task 009 / FR-08 / finding A-7) to scope
    /// a to-do write to the caller's accessible root set BEFORE any mutation. App-only Dataverse
    /// read. Deliberately mirrors <see cref="GetDocumentParentAndNameAsync"/> (task 027), which
    /// solves the same child-record-to-root authorization problem for documents.
    /// </summary>
    /// <remarks>
    /// <para><b>ADR-003 fail-closed contract.</b> <c>GetSingleAsync</c> collapses HTTP 404,
    /// any non-success status, AND thrown exceptions all to <c>null</c>. A <c>None</c> kind with a
    /// null name therefore means "absent OR unreadable" — the two are not distinguishable here. The
    /// caller MUST treat both as DENY, never as "no restriction applies". That is the required
    /// behaviour for an authorization pre-check; the cost is that a transient Dataverse fault
    /// surfaces to the client as not-found rather than as a server error.</para>
    ///
    /// <para><b>Which of the 14 parents are scopeable.</b> <c>sprk_todo</c> carries 14
    /// regarding-parent lookups (live metadata 2026-09-09: agreement, analysis, budget,
    /// communication, contact, document, event, invoice, matter, organization, project, reportcard,
    /// servicerequest, workassignment). Task 009's count of 13 was CORRECT when it was measured on
    /// 2026-08-24 — <c>sprk_regardingagreement</c> was live-verified by
    /// <c>record-header-and-notepad-r2</c> (FR-24) the very next day. The number aged; nobody erred.
    /// Exactly THREE have a corresponding accessible set on
    /// <see cref="CallerPrincipal"/> — project, matter, work assignment (the A-9 root sets) — and all
    /// three are projected here per the 2026-08-24 owner decision that matter and work assignment get
    /// the same functionality as project. The other eleven resolve to <see cref="TodoRootKind.None"/>
    /// and are denied.</para>
    ///
    /// <para><b>Ambiguity is denied.</b> ADR-024 says a to-do has ONE parent, but the lookups are
    /// physically independent columns and nothing in Dataverse enforces that. If more than one root
    /// lookup is populated the result is <see cref="TodoRootKind.Ambiguous"/> and the caller must
    /// deny: honouring whichever root the caller happens to hold would let them write a record that
    /// is also parented somewhere they do not.</para>
    /// </remarks>
    public virtual async Task<(TodoRootKind Kind, Guid? RootId, string? TodoName)> GetTodoRootAsync(
        Guid todoId, CancellationToken ct = default)
    {
        // Columns verified against live Dataverse metadata 2026-08-24 (sprk_todo):
        // sprk_todoid GUID · sprk_name NVARCHAR(200) NOT NULL · sprk_regardingproject,
        // sprk_regardingmatter, sprk_regardingworkassignment all LOOKUP.
        var select = "sprk_todoid,sprk_name,_sprk_regardingproject_value," +
                     "_sprk_regardingmatter_value,_sprk_regardingworkassignment_value";
        var url = $"{GetApiUrl()}/sprk_todos({todoId})?$select={select}";

        var row = await GetSingleAsync<TodoRow>(url, ct);
        if (row is null) return (TodoRootKind.None, null, null);

        static Guid? Parse(string? v) => Guid.TryParse(v, out var g) ? g : (Guid?)null;

        var project = Parse(row.SprkRegardingprojectValue);
        var matter = Parse(row.SprkRegardingmatterValue);
        var workAssignment = Parse(row.SprkRegardingworkassignmentValue);

        // sprk_name is NOT NULL in Dataverse, so a non-null name is a reliable existence signal.
        var name = row.SprkName;

        var populated = new (TodoRootKind Kind, Guid? Id)[]
        {
            (TodoRootKind.Project, project),
            (TodoRootKind.Matter, matter),
            (TodoRootKind.WorkAssignment, workAssignment),
        }.Where(x => x.Id is not null).ToArray();

        return populated.Length switch
        {
            0 => (TodoRootKind.None, null, name),
            1 => (populated[0].Kind, populated[0].Id, name),
            _ => (TodoRootKind.Ambiguous, null, name),
        };
    }

    /// <summary>
    /// Retrieves all <c>sprk_todo</c> records parented to the supplied accessible ROOT — a project,
    /// a matter, or a work assignment — through that root's entity-specific regarding lookup.
    /// </summary>
    /// <remarks>
    /// <para>NOT a polymorphic regarding lookup per ADR-024: each root has its own physical column,
    /// and this filters on exactly the one that matches <paramref name="rootKind"/>.</para>
    ///
    /// <para><b>Task 029 widened this from project-only.</b> Task 009 widened
    /// <c>PATCH /todos/{id}</c> to all three roots and left this method (and
    /// <see cref="CreateTodoAsync"/>) on project alone, so the plane's write surface was WIDER than
    /// its read surface: a caller could PATCH a matter-parented to-do the same plane would not list.
    /// The <c>$filter</c> attribute now comes from <see cref="TodoRootBinding"/> rather than a
    /// literal, so read and write cannot name different columns for the same root.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="rootKind"/> is <c>None</c> or <c>Ambiguous</c> — deny states, not roots. A
    /// caller reaching here with one has skipped its authorization gate.
    /// </exception>
    /// <remarks>
    /// <c>virtual</c> per ADR-038 §4 (substitution seam), added by task 029 so the endpoint tests can
    /// assert WHICH root the handler asked for — and that a denied list never reached Dataverse at
    /// all. ⚠️ Substituting this method hides the <c>$filter</c> it would have built, which is the
    /// task-017 trap; <see cref="BuildTodoListUrl"/> is tested directly for exactly that reason.
    /// </remarks>
    public virtual async Task<ExternalCollectionResponse<ExternalTodoDto>> GetTodosAsync(
        TodoRootKind rootKind, Guid rootId, CancellationToken ct = default)
    {
        var url = BuildTodoListUrl(GetApiUrl(), rootKind, rootId);

        // Task 105: paged to the cap, and a cut-short list says so (see GetDocumentsAsync).
        var read = await GetCollectionAsync<TodoRow>(url, ct);
        return new ExternalCollectionResponse<ExternalTodoDto>
        {
            Value = read.Rows.Select(MapTodo).ToList(),
            Truncated = read.Truncated,
        };
    }

    /// <summary>
    /// Builds the to-do list query URL for one accessible root. Extracted as a pure function so the
    /// emitted OData can be asserted directly.
    /// </summary>
    /// <remarks>
    /// The task-017 lesson, restated by this task's review constraint: <b>mocking at a seam proves
    /// the caller, never the callee.</b> A test that substitutes <see cref="GetTodosAsync"/> cannot
    /// see which column it filtered on, so widening the filter inside a substituted method would
    /// ship untested. This function is where the widening actually lives, and it is directly
    /// callable.
    /// </remarks>
    internal static string BuildTodoListUrl(string apiUrl, TodoRootKind rootKind, Guid rootId)
    {
        var binding = TryGetRootBinding(rootKind)
            ?? throw new ArgumentOutOfRangeException(nameof(rootKind), rootKind,
                "To-dos can only be listed for a project, matter or work assignment. None/Ambiguous "
                + "are deny states — a caller reaching here with one has skipped its authorization gate.");

        // Every root's own lookup is projected (not just the queried one) so a client can tell what a
        // to-do is parented to, and so an ambiguous row is visible rather than silently attributed to
        // whichever root was asked for.
        var select = "sprk_todoid,sprk_name,sprk_notes,sprk_duedate,sprk_priorityscore,sprk_effortscore," +
                     "sprk_todocolumn,sprk_todopinned,statecode,statuscode,createdon," +
                     "_sprk_regardingproject_value,_sprk_regardingmatter_value," +
                     "_sprk_regardingworkassignment_value," +
                     "sprk_regardingrecordid,sprk_regardingrecordname,sprk_regardingrecordurl," +
                     CreatorStampSelect; // task 032 (FR-26): the SPA offers Delete only on the caller's own to-dos
        var filter = Uri.EscapeDataString($"{binding.LookupValueAttribute} eq {rootId}");

        // Task 105: no $top (it suppresses @odata.nextLink); the primary key breaks due-date ties so pages never overlap.
        return $"{apiUrl}/sprk_todos?$filter={filter}&$select={select}&$orderby=sprk_duedate asc,sprk_todoid asc";
    }

    /// <summary>
    /// Retrieves all <c>sprk_event</c> records whose regarding-project equals the supplied project id.
    /// </summary>
    /// <remarks>
    /// CALENDAR events, not to-dos. <c>sprk_event</c> has no <c>sprk_projectid</c> attribute — its
    /// project lookup is <c>sprk_regardingproject</c> (metadata-verified, smart-todo-r5 task 002),
    /// so the filter uses <c>_sprk_regardingproject_value</c>.
    ///
    /// The legacy <c>sprk_todoflag</c> attribute is deliberately NOT selected. smart-todo-decoupling-r3
    /// FR-29 retired the event-as-todo model; to-dos live on <c>sprk_todo</c> and are served by
    /// <see cref="GetTodosAsync"/>. Reintroducing that flag here would resurrect the model FR-29 removed.
    /// </remarks>
    public virtual async Task<ExternalCollectionResponse<ExternalEventDto>> GetEventsAsync(Guid projectId, CancellationToken ct = default)
    {
        var url = $"{GetApiUrl()}/{BuildEventsQuery(projectId)}";

        // Task 105: paged to the cap, and a cut-short list says so (see GetDocumentsAsync).
        var read = await GetCollectionAsync<EventRow>(url, ct);
        return new ExternalCollectionResponse<ExternalEventDto>
        {
            Value = read.Rows.Select(MapEvent).ToList(),
            Truncated = read.Truncated,
        };
    }

    /// <summary>
    /// Creates a new <c>sprk_event</c> record associated with the supplied project via
    /// <c>sprk_regardingproject</c>.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="CreateTodoAsync"/> this does NOT apply the four ADR-024 resolver fields:
    /// those are a <c>sprk_todo</c> construct (the 11-entity regarding model). <c>sprk_event</c>
    /// carries a direct project lookup only.
    /// </remarks>
    // `virtual` per ADR-038 §4 (substitution seam), added by task 146 so the endpoint tests can assert the owner the
    // route resolved reached the create — and that a refused owner never reached Dataverse at all.
    public virtual async Task<ExternalEventDto> CreateEventAsync(
        Guid projectId, CreateExternalEventRequest request, Guid owningTeamId, CallerPrincipal creator,
        CancellationToken ct = default)
    {
        var body = BuildEventCreatePayload(projectId, request, owningTeamId);
        StampCreator(body, creator); // FR-25 (task 030)

        var token = await GetAppOnlyTokenAsync(ct);

        var url = $"{GetApiUrl()}/sprk_events";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.Add("OData-MaxVersion", "4.0");
        httpRequest.Headers.Add("OData-Version", "4.0");
        httpRequest.Headers.Add("Prefer", "return=representation");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[EXT-DATA] Create event failed for project {ProjectId}: {Status} — {Body}",
                projectId, response.StatusCode, errorBody);
            throw new InvalidOperationException($"Failed to create event: {response.StatusCode}");
        }

        var row = await response.Content.ReadFromJsonAsync<EventRow>(ct);
        if (row is null)
            throw new InvalidOperationException("Dataverse returned no event data after create");

        return MapEvent(row);
    }

    /// <summary>
    /// The relative <c>sprk_events</c> query <see cref="GetEventsAsync"/> sends (task 097: live column names).
    /// Internal for tests.
    /// </summary>
    internal static string BuildEventsQuery(Guid projectId)
    {
        // Task 032 (FR-26): both creator stamps, so the SPA offers Delete only on the caller's own events.
        var select = $"sprk_eventid,{EventNameColumn},sprk_duedate,{EventStatusColumn},createdon,_sprk_regardingproject_value,"
                     + CreatorStampSelect;
        var filter = Uri.EscapeDataString($"_sprk_regardingproject_value eq {projectId}");
        // Task 105: no $top (it suppresses @odata.nextLink); the primary key breaks due-date ties so pages never overlap.
        return $"sprk_events?$filter={filter}&$select={select}&$orderby=sprk_duedate asc,sprk_eventid asc";
    }

    /// <summary>
    /// The COMPLETE create payload for an external calendar event: the caller's fields, the project bind, and the
    /// OWNER the route resolved from the project (task 146 — the named Secure team for a secure project). Pure so a
    /// test can read it. Task 097: the DTO's sprk_name / sprk_status are written to the LIVE sprk_eventname / statuscode
    /// (with the paired statecode) — sprk_event has neither sprk_name nor sprk_status, so the former body was a 400.
    /// <exception cref="ArgumentOutOfRangeException">sprk_status is not in <see cref="ExternalCreatableStatuses"/>.</exception>
    /// </summary>
    internal static Dictionary<string, object?> BuildEventCreatePayload(
        Guid projectId, CreateExternalEventRequest request, Guid owningTeamId)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project id must be a non-empty GUID.", nameof(projectId));
        ArgumentNullException.ThrowIfNull(request);
        RequireOwner(owningTeamId, "sprk_event");

        var status = request.SprkStatus ?? EventStatusCode.Open;
        if (!ExternalCreatableStatuses.Contains(status))
            throw new ArgumentOutOfRangeException(nameof(request), status,
                "sprk_status must be Draft (1) or Open (659490001).");

        var body = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(request.SprkName))
            body[EventNameColumn] = request.SprkName;
        // Task 098: sprk_duedate is a Date Only column — the Web API accepts only "yyyy-MM-dd" and answers 400 for a
        // timestamp. Earlier SPA builds sent toISOString() of the picked day's UTC midnight; its leading ten characters
        // are that day. The endpoint refuses anything DataverseDateOnly cannot read before this point.
        if (request.SprkDuedate is not null)
            body["sprk_duedate"] = DataverseDateOnly.Format(DataverseDateOnly.Parse(request.SprkDuedate));
        body[EventStatusColumn] = status;
        body["statecode"] = EventStatusCode.GetStateCode(status);

        // R5 002: PascalCase nav prop (metadata-verified) — same binding the to-do create uses.
        body["sprk_RegardingProject@odata.bind"] = $"/sprk_projects({projectId})";
        body[OwnerBindKey] = $"/teams({owningTeamId})";
        return body;
    }

    /// <summary>
    /// Creates a new <c>sprk_todo</c> record in Dataverse, associated with the supplied accessible
    /// ROOT — project, matter or work assignment — via that root's entity-specific regarding lookup.
    /// The four resolver fields are populated atomically per ADR-024.
    /// </summary>
    /// <remarks>
    /// <para>Returns the created to-do (Dataverse returns the created entity via
    /// <c>Prefer: return=representation</c>). Mirrors the regarding-application semantics of
    /// <see cref="Sprk.Bff.Api.Services.Workspace.TodoRegardingBuilder"/> over the Web API path.</para>
    ///
    /// <para><b>Task 029 widened this from project-only</b>, closing the read/create half of the
    /// asymmetry task 009 opened and flagged. The owner's stated intent — "the parent flows from the
    /// creation context" — is honoured by the ROUTE the caller used: a to-do created from a matter
    /// route is parented to that matter and its resolver fields carry <c>sprk_matter</c>.</para>
    ///
    /// <para><b>A wrong navigation property fails LOUDLY here</b> (unlike task 021's provisioning
    /// PATCH, which swallowed its 400): every non-success response throws below. Do not add a
    /// defensive swallow — that would manufacture 021's bug in a file that does not have it.</para>
    ///
    /// <para><c>virtual</c> per ADR-038 §4 (substitution seam), added by task 029 for the same reason
    /// <c>UpdateTodoAsync</c> is virtual: the deny tests must assert the create NEVER REACHED
    /// Dataverse, and a status-code assertion alone would pass even if it had.</para>
    /// <para><b>Assigned To (unified-access-control-r2 task 152, #1044 split).</b> The create is app-only, so Created
    /// By is the BFF application user and cannot say who the to-do is for. The CALLING contact — the triggering
    /// person — is written to <c>sprk_assignedto</c> (the closed request DTO carries no assignee, so nothing is ever
    /// overwritten). An unlinked workforce caller (<paramref name="callerContactId"/> null) leaves it blank with a
    /// <c>todo_unassigned</c> warning; the to-do is still reachable through its root. Never a team, never an email
    /// match. Escalation (d) checked: no grant path treats <c>sprk_todo.sprk_assignedto</c> as grant-bearing today
    /// (child-entity registry entries confer nothing; task 142's recommendation (f) keeps it that way) — reconcile
    /// with 142 if that changes.</para>
    /// </remarks>
    public virtual async Task<ExternalTodoDto> CreateTodoAsync(
        TodoRootKind rootKind,
        Guid rootId,
        CreateExternalTodoRequest request,
        Guid owningTeamId,
        Guid? callerContactId,
        CallerPrincipal creator,
        CancellationToken ct = default)
    {
        if (rootId == Guid.Empty)
            throw new ArgumentException("Root id must be a non-empty GUID.", nameof(rootId));
        ArgumentNullException.ThrowIfNull(creator);
        EnsureCanStampCreator(creator); // FR-25: decide the stamp kind before any token / lookup / request

        var binding = TryGetRootBinding(rootKind)
            ?? throw new ArgumentOutOfRangeException(nameof(rootKind), rootKind,
                "To-dos can only be created against a project, matter or work assignment. "
                + "None/Ambiguous are deny states — a caller reaching here has skipped its gate.");

        var token = await GetAppOnlyTokenAsync(ct);

        // Look up the ROOT's display name first — needed for sprk_regardingrecordname. Absent root
        // yields an empty name rather than a throw, matching the prior project-only behaviour.
        // ⚠️ The column differs per root (sprk_projectname / sprk_mattername / sprk_name) and is NOT
        // the PrimaryNameAttribute for project or matter — see TodoRootBinding's remarks.
        var rootDisplayName = await GetRootDisplayNameAsync(binding, rootId, ct);

        // The one genuinely async part of the payload: the sprk_recordtype_ref row for the PARENT's
        // entity. Resolved here (cached per instance) and handed to the pure builder, so everything
        // that DECIDES anything about the payload is synchronous and directly testable.
        var recordTypeRef = await ResolveRecordTypeRefAsync(binding.EntityLogicalName, ct);
        if (recordTypeRef is null)
        {
            _logger.LogWarning(
                "[EXT-DATA] sprk_recordtype_ref not found for entity '{Entity}'. Resolver type field left unset.",
                binding.EntityLogicalName);
        }

        // ADR-024: the specific regarding lookup + the 4 resolver fields are written ATOMICALLY in
        // this one request — never in a follow-up PATCH.
        var body = BuildTodoCreatePayload(
            request, binding, rootId, rootDisplayName, recordTypeRef?.Id, owningTeamId, callerContactId);
        StampCreator(body, creator); // FR-25 (task 030)
        if (!body.ContainsKey(AssignedToBindKey))
        {
            _logger.LogWarning(
                "todo_unassigned: entity=sprk_todo parentEntity={ParentEntity} parentId={ParentId} reason={Reason} — the "
                + "calling principal has no linked contact; sprk_assignedto left blank (never a team, never an email match)",
                binding.EntityLogicalName, rootId, "caller_has_no_linked_contact");
        }

        var url = $"{GetApiUrl()}/sprk_todos";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.Add("OData-MaxVersion", "4.0");
        httpRequest.Headers.Add("OData-Version", "4.0");
        httpRequest.Headers.Add("Prefer", "return=representation");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[EXT-DATA] Create to-do failed for {RootKind} {RootId}: {Status} — {Body}",
                rootKind, rootId, response.StatusCode, errorBody);
            throw new InvalidOperationException($"Failed to create to-do: {response.StatusCode}");
        }

        var row = await response.Content.ReadFromJsonAsync<TodoRow>(ct);
        if (row is null)
            throw new InvalidOperationException("Dataverse returned no to-do data after create");

        return MapTodo(row);
    }

    /// <summary>The <c>sprk_todo.sprk_assignedto</c> (contact) bind key on the Web API create payload (task 152).</summary>
    internal const string AssignedToBindKey = "sprk_AssignedTo@odata.bind";

    /// <summary>
    /// Builds the COMPLETE create payload for a to-do: the caller-supplied fields, exactly one
    /// regarding lookup bind, and all four ADR-024 resolver fields — then asserts the one-parent
    /// rule over the finished body.
    /// </summary>
    /// <remarks>
    /// <para><b>Pure by design, and that is the point.</b> Every decision this payload embodies —
    /// which navigation property is bound, which entity the resolver fields name, whether more than
    /// one parent slipped in — is made here, synchronously, where a test can read it. The only async
    /// input (<paramref name="recordTypeRefId"/>) is resolved by the caller and passed in.</para>
    ///
    /// <para>The alternative shape — deciding these inside the async method — is the task-017 trap
    /// this project keeps re-learning: <c>CreateTodoAsync</c> is a substitution seam, so anything
    /// decided inside it is invisible to every test that substitutes it. Perturbing the resolver's
    /// entity name or the one-parent guard would then fail ZERO tests.</para>
    /// </remarks>
    internal static Dictionary<string, object?> BuildTodoCreatePayload(
        CreateExternalTodoRequest request,
        TodoRootBinding binding,
        Guid rootId,
        string rootDisplayName,
        Guid? recordTypeRefId,
        Guid owningTeamId,
        Guid? assignedToContactId = null)
    {
        // Task 146: the owner the route resolved from the ROOT (the named Secure team for a secure root, else the
        // root's business-unit default team). Checked first, so a payload without one is never built.
        RequireOwner(owningTeamId, "sprk_todo");

        var body = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(request.SprkName))
            body["sprk_name"] = request.SprkName;
        if (!string.IsNullOrEmpty(request.SprkNotes))
            body["sprk_notes"] = request.SprkNotes;
        // Task 106: sprk_todo.sprk_duedate is a Date Only column — the Web API accepts only "yyyy-MM-dd" and answers 400
        // for a timestamp. Earlier SPA builds sent toISOString() of the picked day's NOON UTC; its leading ten characters
        // are that day. The endpoint refuses anything DataverseDateOnly cannot read before this point.
        if (request.SprkDuedate is not null)
            body["sprk_duedate"] = DataverseDateOnly.Format(DataverseDateOnly.Parse(request.SprkDuedate));
        if (request.SprkPriorityscore.HasValue)
            body["sprk_priorityscore"] = request.SprkPriorityscore.Value;
        if (request.SprkEffortscore.HasValue)
            body["sprk_effortscore"] = request.SprkEffortscore.Value;
        if (request.SprkTodocolumn.HasValue)
            body["sprk_todocolumn"] = request.SprkTodocolumn.Value;
        if (request.SprkTodopinned.HasValue)
            body["sprk_todopinned"] = request.SprkTodopinned.Value;

        // ONE root, ONE bind. The key and the entity set both come from the same binding row, so a
        // navigation property can never be paired with another root's entity set.
        body[binding.BindKey] = $"/{binding.EntitySet}({rootId})";

        // The four ADR-024 resolver fields, carrying the PARENT's own entity — not sprk_project.
        // A matter-parented to-do whose resolver said "sprk_project" would render a broken
        // cross-entity link and mis-report the parent type to every consumer of the resolver.
        var cleanId = rootId.ToString("D").ToLowerInvariant();
        body["sprk_regardingrecordid"] = cleanId;
        body["sprk_regardingrecordname"] = rootDisplayName ?? string.Empty;
        body["sprk_regardingrecordurl"] = BuildRecordUrl(binding.EntityLogicalName, cleanId);

        if (recordTypeRefId.HasValue)
        {
            // R5 002: PascalCase nav prop (metadata-verified; re-confirmed live 2026-09-09).
            body["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({recordTypeRefId.Value})";
        }

        // Non-fatal when absent, mirroring the SDK path: correctness is intact, only the
        // cross-entity-view icon is lost. The caller logs the warning (it owns the lookup).

        body[OwnerBindKey] = $"/teams({owningTeamId})";

        // Task 152: the person this to-do is FOR — the calling contact (the triggering person). PascalCase navigation
        // property, the same bind every client to-do writer uses (CreateTodoWizard/todoService.ts).
        if (assignedToContactId is { } assignee && assignee != Guid.Empty)
        {
            body[AssignedToBindKey] = $"/contacts({assignee:D})";
        }

        AssertSingleRegardingLookup(body);

        return body;
    }

    /// <summary>
    /// ADR-024 one-parent-at-a-time, enforced on the Web API path.
    /// </summary>
    /// <remarks>
    /// <para><c>TodoRegardingBuilder</c> enforces this on the SDK path; nothing enforced it here.
    /// The stake is not cosmetic: a two-parent row is classified <c>Ambiguous</c> by
    /// <see cref="GetTodoRootAsync"/> and then DENIED FOREVER to every caller, on every route — this
    /// surface could otherwise mint records it can never read or update again.</para>
    ///
    /// <para><c>sprk_RegardingRecordType</c> is excluded: it is the resolver's type reference, not a
    /// parent. It is the only <c>sprk_Regarding*</c> bind that legitimately coexists with a parent.</para>
    ///
    /// <para>🔴 <b>The match is deliberately case-INSENSITIVE.</b> Dataverse requires the
    /// PascalCase navigation property, but a wrongly-cased bind key is a real and observed mistake
    /// in this codebase — <c>spaarke-daily-update-service-r5</c>'s bind audit found a lowercase
    /// <c>sprk_regardingproject@odata.bind</c> in THIS FILE, and lowercase keys still appear in
    /// client code today. A case-SENSITIVE guard would wave through the second parent whose casing
    /// was wrong, i.e. it would miss precisely the buggy write it exists to catch. Detect broadly;
    /// let Dataverse reject the casing.</para>
    /// </remarks>
    internal static void AssertSingleRegardingLookup(IDictionary<string, object?> body)
    {
        var parentBinds = body.Keys
            .Where(k => k.StartsWith("sprk_Regarding", StringComparison.OrdinalIgnoreCase)
                     && k.EndsWith("@odata.bind", StringComparison.OrdinalIgnoreCase)
                     && !k.StartsWith("sprk_RegardingRecordType", StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        if (parentBinds.Length != 1)
            throw new InvalidOperationException(
                $"ADR-024 violation: a to-do create must set EXACTLY ONE regarding parent lookup, "
                + $"found {parentBinds.Length} ({string.Join(", ", parentBinds)}). A multi-parent row "
                + "would be classified Ambiguous by GetTodoRootAsync and denied to every caller forever.");
    }

    /// <summary>
    /// Reads one accessible root's DISPLAY name for <c>sprk_regardingrecordname</c>. Returns empty
    /// when the root is absent or unreadable — matching the prior project-only behaviour, which
    /// surfaced a missing root as an empty resolver name rather than a failed create.
    /// </summary>
    private async Task<string> GetRootDisplayNameAsync(
        TodoRootBinding binding, Guid rootId, CancellationToken ct)
    {
        var url = $"{GetApiUrl()}/{binding.EntitySet}({rootId})?$select={binding.DisplayNameAttribute}";

        var row = await GetSingleAsync<Dictionary<string, JsonElement>>(url, ct);
        if (row is null || !row.TryGetValue(binding.DisplayNameAttribute, out var value))
            return string.Empty;

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// The PATCH body of <see cref="UpdateTodoAsync"/>: only the fields the request carries. Hoisted out of the async
    /// method by task 106 so the body is testable without a transport (the create side's
    /// <see cref="BuildTodoCreatePayload"/> pattern).
    /// </summary>
    internal static Dictionary<string, object?> BuildTodoUpdatePayload(UpdateExternalTodoRequest request)
    {
        var body = new Dictionary<string, object?>();
        if (request.SprkName is not null) body["sprk_name"] = request.SprkName;
        if (request.SprkNotes is not null) body["sprk_notes"] = request.SprkNotes;
        // Task 106: a calendar date, "yyyy-MM-dd" — Dataverse refuses a timestamp for the Date Only column (see
        // BuildTodoCreatePayload); the route refuses what DataverseDateOnly cannot read before this point.
        if (request.SprkDuedate is not null) body["sprk_duedate"] = DataverseDateOnly.Format(DataverseDateOnly.Parse(request.SprkDuedate));
        if (request.SprkPriorityscore.HasValue) body["sprk_priorityscore"] = request.SprkPriorityscore.Value;
        if (request.SprkEffortscore.HasValue) body["sprk_effortscore"] = request.SprkEffortscore.Value;
        if (request.SprkTodocolumn.HasValue) body["sprk_todocolumn"] = request.SprkTodocolumn.Value;
        if (request.SprkTodopinned.HasValue) body["sprk_todopinned"] = request.SprkTodopinned.Value;
        if (request.Statuscode is { } status)
        {
            // Task 106 (found by the live complete leg): Dataverse refuses a status reason outside the row's current
            // state ("2 is not a valid status code for state code sprk_TodoState.Active", HTTP 400) — it does NOT move
            // the state for us — so the state the status reason belongs to is written with it. The route refuses any
            // other status reason before this point.
            body["statuscode"] = status;
            if (TodoStateCodeFor(status) is { } state)
                body["statecode"] = state;
        }
        return body;
    }

    /// <summary>
    /// The <c>statecode</c> a <c>sprk_todo</c> status reason belongs to (live option set, task 097): Open and In Progress
    /// are Active (0); Completed and Dismissed are Inactive (1). Null for any other value — not a to-do status reason.
    /// </summary>
    internal static int? TodoStateCodeFor(int statuscode) => statuscode switch
    {
        Services.Workspace.TodoGenerationService.StatusCodeOpen or Services.Workspace.TodoGenerationService.StatusCodeInProgress => 0,
        Services.Workspace.TodoGenerationService.StatusCodeCompleted or Services.Workspace.TodoGenerationService.StatusCodeDismissed => 1,
        _ => null,
    };

    /// <summary>
    /// Updates an existing <c>sprk_todo</c> record (PATCH semantics — only provided fields are changed).
    /// </summary>
    /// <remarks>
    /// Regarding context cannot be changed via this surface — to re-parent a to-do, use the
    /// internal model-driven-app form which applies the resolver fields atomically per ADR-024.
    /// </remarks>
    // `virtual` per ADR-038 §4 (substitution seam), added by task 009: the FR-08 scope check is only
    // meaningfully verifiable if a test can assert the write DID NOT HAPPEN on a deny. Asserting the
    // 403/404 status alone would pass even if the PATCH were still issued before the check.
    public virtual async Task UpdateTodoAsync(
        Guid todoId, UpdateExternalTodoRequest request, CancellationToken ct = default)
    {
        var token = await GetAppOnlyTokenAsync(ct);

        var body = BuildTodoUpdatePayload(request);

        if (body.Count == 0)
        {
            _logger.LogDebug("[EXT-DATA] UpdateTodo called with no fields to update for to-do {TodoId}", todoId);
            return;
        }

        var url = $"{GetApiUrl()}/sprk_todos({todoId})";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Patch, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.Add("OData-MaxVersion", "4.0");
        httpRequest.Headers.Add("OData-Version", "4.0");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[EXT-DATA] Update to-do failed for to-do {TodoId}: {Status} — {Body}",
                todoId, response.StatusCode, errorBody);
            throw new InvalidOperationException($"Failed to update to-do: {response.StatusCode}");
        }
    }

    // ---------------------------------------------------------------------------
    // Delete-own (spaarke-SPA-external-access-platform-r3 task 032, FR-26)
    //
    // An external caller may hard-delete a document, to-do or event it CREATED (the task-030 stamp), while it still
    // holds Read on the record's parent. The endpoint (ExternalProjectDataEndpoints.AuthorizeDeleteOwnAsync) composes
    // the two halves; this service supplies the facts (GetDeleteTargetAsync), the stamp rule (IsCreatedByCaller) and the
    // app-only row delete for to-dos and events (DeleteRecordAsync). A document is deleted through
    // DocumentCheckoutService so its SPE file goes too. A service request (owner decision 2026-10-11, option A) is
    // deleted by its REQUESTER only, and only while NO row is related to it (HasRelatedRecordsAsync).
    // ---------------------------------------------------------------------------

    /// <summary>The record types an external caller can delete-own (FR-26).</summary>
    public enum DeletableKind
    {
        Document = 1,
        Todo = 2,
        Event = 3,

        /// <summary>
        /// A service request the caller REQUESTED (owner decision 2026-10-11, option A + recall). Its "creator" fact is the
        /// requester (<c>sprk_requestedby</c>), decided by the requester scope, not by <see cref="GetDeleteTargetAsync"/>;
        /// it is refused when ANY related row exists (<see cref="HasRelatedRecordsAsync"/>).
        /// </summary>
        ServiceRequest = 4,
    }

    /// <summary>One parent lookup of a deletable row: the parent type and the row's <c>_..._value</c> attribute for it.</summary>
    internal sealed record DeletableParentLookup(DocumentParentKind Parent, string LookupValueAttribute);

    /// <summary>
    /// What the delete-own read needs for one deletable type: its entity set and the parent lookups the external plane
    /// authorizes it through.
    /// </summary>
    /// <remarks>
    /// <para><b>Documents</b> use the four task-031 parent lookups (<see cref="DocumentParents"/>), the same ones the
    /// external list, download and upload routes scope by.</para>
    /// <para><b>To-dos and events</b> use the three root lookups of <see cref="RootBindings"/>
    /// (<c>sprk_regardingproject</c> / <c>sprk_regardingmatter</c> / <c>sprk_regardingworkassignment</c>; the same column
    /// names on <c>sprk_event</c>, solution source <c>Entities/sprk_Event/Entity.xml</c>), the roots
    /// <see cref="GetTodoRootAsync"/> scopes a to-do by. <see cref="DocumentParentKind"/> names the parent type because it
    /// already names all four record types a child can be filed to on this plane.</para>
    /// </remarks>
    internal sealed record DeletableBinding(
        DeletableKind Kind, string EntityLogicalName, string EntitySet, IReadOnlyList<DeletableParentLookup> Parents);

    /// <summary>The <c>sprk_createdbyperson</c> (→ systemuser) read form: the task-030 stamp of a systemuser creator.</summary>
    internal const string CreatedByPersonValueAttribute = "_" + RecordCreatorPersonColumn.LogicalName + "_value";

    /// <summary>The <c>sprk_createdbycontact</c> (→ contact) read form: the task-030 stamp of a contact creator.</summary>
    internal const string CreatedByContactValueAttribute = "_" + CreatedByContactColumn + "_value";

    /// <summary>Both creator stamps in <c>$select</c> form: every external document, to-do and event read returns them
    /// (task 032, review 031 K2) so the SPA can offer Delete only on the caller's own rows (task 054). Requires task 020's
    /// <c>sprk_createdbycontact</c> in the environment: without it Dataverse answers 400 and the list is flagged
    /// truncated (deploy order, task 038).</summary>
    internal const string CreatorStampSelect = CreatedByPersonValueAttribute + "," + CreatedByContactValueAttribute;

    private static DocumentParentKind ParentKindOf(TodoRootKind kind) => kind switch
    {
        TodoRootKind.Project => DocumentParentKind.Project,
        TodoRootKind.Matter => DocumentParentKind.Matter,
        TodoRootKind.WorkAssignment => DocumentParentKind.WorkAssignment,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a root kind."),
    };

    private static readonly IReadOnlyList<DeletableParentLookup> RootParentLookups =
        RootBindings.Values.Select(b => new DeletableParentLookup(ParentKindOf(b.Kind), b.LookupValueAttribute)).ToList();

    private static readonly IReadOnlyDictionary<DeletableKind, DeletableBinding> Deletables =
        new Dictionary<DeletableKind, DeletableBinding>
        {
            [DeletableKind.Document] = new(DeletableKind.Document, "sprk_document", "sprk_documents",
                DocumentParents.Values.Select(b => new DeletableParentLookup(b.Kind, b.LookupValueAttribute)).ToList()),
            [DeletableKind.Todo] = new(DeletableKind.Todo, "sprk_todo", "sprk_todos", RootParentLookups),
            [DeletableKind.Event] = new(DeletableKind.Event, "sprk_event", "sprk_events", RootParentLookups),
            // No parent and no creator stamp: the requester scope decides (ExternalProjectDataEndpoints.DeleteOwnServiceRequest).
            [DeletableKind.ServiceRequest] = new(DeletableKind.ServiceRequest, "sprk_servicerequest", "sprk_servicerequests",
                Array.Empty<DeletableParentLookup>()),
        };

    /// <summary>The binding for a deletable kind. Throws for a value outside the table: a programming error.</summary>
    internal static DeletableBinding GetDeletableBinding(DeletableKind kind) =>
        Deletables.TryGetValue(kind, out var binding)
            ? binding
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a deletable kind.");

    /// <summary>A parent the row is filed to through one of its lookups.</summary>
    public readonly record struct DeleteTargetParent(DocumentParentKind Kind, Guid Id);

    /// <summary>
    /// The facts the delete-own check decides on: the row's two creator stamps (null when absent) and every parent its
    /// lookups name (empty when none is set).
    /// </summary>
    public sealed record DeleteTarget(Guid? CreatedByPerson, Guid? CreatedByContact, IReadOnlyList<DeleteTargetParent> Parents);

    /// <summary>
    /// Reads a deletable row's creator stamps and parent lookups, app-only (FR-26). Null when the row does not exist
    /// (a genuine 404), so absent and someone else's deny the same way.
    /// </summary>
    /// <remarks>
    /// A read that FAILS (non-404 status, transport error, token failure) throws <see cref="DataverseUnavailableException"/>:
    /// "could not tell" is never reported as "not yours" (the task-031 review rule for the requester read). Until task
    /// 020's <c>sprk_createdbycontact</c> column exists in the environment, Dataverse answers 400 to this read, so every
    /// delete there is a 503: fail closed.
    /// </remarks>
    /// <exception cref="DataverseUnavailableException">The read could not be completed.</exception>
    public virtual async Task<DeleteTarget?> GetDeleteTargetAsync(DeletableKind kind, Guid id, CancellationToken ct = default)
    {
        if (kind == DeletableKind.ServiceRequest)
            throw new ArgumentOutOfRangeException(nameof(kind), kind,
                "A service request is authorized through its requester (GetServiceRequestRequesterAsync), not the creator stamps.");

        var row = await GetSingleOrThrowAsync<Dictionary<string, JsonElement>>(
            BuildDeleteTargetUrl(GetApiUrl(), kind, id), ct);
        return row is null ? null : ParseDeleteTarget(kind, row);
    }

    /// <summary>The single-row read <see cref="GetDeleteTargetAsync"/> sends: both stamps and every parent lookup. Pure.</summary>
    internal static string BuildDeleteTargetUrl(string apiUrl, DeletableKind kind, Guid id)
    {
        var binding = GetDeletableBinding(kind);
        var columns = new[] { CreatedByPersonValueAttribute, CreatedByContactValueAttribute }
            .Concat(binding.Parents.Select(p => p.LookupValueAttribute));
        return $"{apiUrl}/{binding.EntitySet}({id})?$select={string.Join(",", columns)}";
    }

    /// <summary>The <see cref="DeleteTarget"/> a row's columns describe. Pure.</summary>
    internal static DeleteTarget ParseDeleteTarget(DeletableKind kind, IReadOnlyDictionary<string, JsonElement> row)
    {
        var parents = new List<DeleteTargetParent>();
        foreach (var lookup in GetDeletableBinding(kind).Parents)
        {
            if (GuidValue(row, lookup.LookupValueAttribute) is { } parentId)
                parents.Add(new DeleteTargetParent(lookup.Parent, parentId));
        }

        return new DeleteTarget(
            GuidValue(row, CreatedByPersonValueAttribute),
            GuidValue(row, CreatedByContactValueAttribute),
            parents);
    }

    /// <summary>
    /// THE stamp half of the delete-own check (FR-26): did <paramref name="caller"/> create this row?
    /// </summary>
    /// <remarks>
    /// <para><b>Owner decision 2026-10-11, option (b) widened</b> (<c>notes/decisions.md</c>):</para>
    /// <list type="bullet">
    /// <item>a CONTACT principal matches <c>sprk_createdbycontact</c> = its contact;</item>
    /// <item>a SYSTEMUSER principal matches <c>sprk_createdbyperson</c> = its systemuser, OR <c>sprk_createdbycontact</c>
    /// = its linked contact (<see cref="CallerPrincipal.ContactId"/>, resolved by the server from the user↔contact link,
    /// never from the request) — the same person who created the row before a licence was added.</item>
    /// </list>
    /// <para>The principal KIND decides, as it does for the stamp (<see cref="StampCreator(IDictionary{string, object?}, CallerPrincipal)"/>):
    /// a contact principal never matches a person stamp, and a systemuser matches a contact stamp only through its own
    /// linked contact. A row with neither stamp (created before task 030, or by staff in the model-driven app) matches
    /// nobody. An empty id never matches.</para>
    /// </remarks>
    internal static bool IsCreatedByCaller(DeleteTarget target, CallerPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.SystemUserId is { } systemUserId)
        {
            if (systemUserId == Guid.Empty)
                return false;
            if (target.CreatedByPerson == systemUserId)
                return true;
            return caller.ContactId != Guid.Empty && target.CreatedByContact == caller.ContactId;
        }

        return caller.IsContactPrincipal && target.CreatedByContact == caller.ContactId;
    }

    /// <summary>
    /// Hard-deletes a to-do or event row, app-only (FR-26). <see langword="true"/> when deleted, <see langword="false"/>
    /// when the row was already gone (404). The caller has authorized the delete first
    /// (<c>ExternalProjectDataEndpoints.AuthorizeDeleteOwnAsync</c>).
    /// </summary>
    /// <remarks>
    /// A document is refused here: it is deleted through <c>DocumentCheckoutService</c>, so its SPE file goes with it.
    /// <c>virtual</c> per ADR-038 §4 (substitution seam): a deny test must be able to assert no delete was sent.
    /// </remarks>
    /// <exception cref="DataverseUnavailableException">Dataverse refused or could not complete the delete.</exception>
    public virtual async Task<bool> DeleteRecordAsync(DeletableKind kind, Guid id, CancellationToken ct = default)
    {
        if (kind == DeletableKind.Document)
            throw new ArgumentOutOfRangeException(nameof(kind), kind,
                "A document is deleted through DocumentCheckoutService, so its SPE file is deleted too.");

        var binding = GetDeletableBinding(kind);
        var url = $"{GetApiUrl()}/{binding.EntitySet}({id})";

        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[EXT-DATA] Delete {Entity} {Id} failed: {Status} — {Body}",
                    binding.EntityLogicalName, id, response.StatusCode, errorBody);
                throw new DataverseUnavailableException($"Dataverse answered {(int)response.StatusCode} to the delete.");
            }

            return true;
        }
        catch (DataverseUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-DATA] Error deleting {Entity} {Id}", binding.EntityLogicalName, id);
            throw new DataverseUnavailableException("Dataverse could not complete the delete.", ex);
        }
    }

    /// <summary>
    /// One 1:N relationship from <c>sprk_servicerequest</c>: the child entity, its entity set and the lookup that points at
    /// the request.
    /// </summary>
    internal sealed record ServiceRequestRelation(string EntityLogicalName, string EntitySet, string LookupAttribute);

    /// <summary>
    /// EVERY 1:N relationship from <c>sprk_servicerequest</c> (solution source
    /// <c>src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_ServiceRequest.xml</c>, 8 relationships, all
    /// <c>CascadeDelete=RemoveLink</c>): deleting the request would orphan every row listed here, so ANY row (active or not)
    /// blocks the delete. A relationship added to the solution must be added here; the pinned test
    /// <c>TheRelationshipList_IsExactlyTheSolutionsOneToManyRelationships</c> reads the solution XML and fails until it is.
    /// </summary>
    internal static readonly IReadOnlyList<ServiceRequestRelation> ServiceRequestRelations = new ServiceRequestRelation[]
    {
        new("sprk_analysis", "sprk_analysises", "sprk_regardingservicerequest"),
        new("sprk_communication", "sprk_communications", "sprk_regardingservicerequest"),
        new("sprk_communicationthread", "sprk_communicationthreads", "sprk_regardingservicerequest"),
        new("sprk_document", "sprk_documents", "sprk_relatedservicerequest"),
        new("sprk_event", "sprk_events", "sprk_regardingservicerequest"),
        new("sprk_memo", "sprk_memos", "sprk_regardingservicerequest"),
        new("sprk_signal", "sprk_signals", "sprk_regardingservicerequest"),
        new("sprk_todo", "sprk_todos", "sprk_regardingservicerequest"),
    };

    /// <summary>The one-row existence probe for one relationship: no <c>statecode</c> filter, so inactive rows count. Pure.</summary>
    internal static string BuildRelatedRowProbeUrl(string apiUrl, ServiceRequestRelation relation, Guid serviceRequestId)
    {
        var value = $"_{relation.LookupAttribute}_value";
        return $"{apiUrl}/{relation.EntitySet}?$select={value}&$filter={value} eq {serviceRequestId}&$top=1";
    }

    /// <summary>
    /// Whether ANY row is related to the service request, across every 1:N relationship (<see cref="ServiceRequestRelations"/>),
    /// active or inactive, app-only (owner decision 2026-10-11, option A: a request with activity is not deletable).
    /// </summary>
    /// <remarks>
    /// All eight probes run; a probe that FAILS (non-success status of any kind, transport error, token failure) makes the
    /// whole answer unknown and throws <see cref="DataverseUnavailableException"/>, even when another probe found a row:
    /// "could not tell" is never reported as "none". Nothing is deleted on an exception (the caller answers 503).
    /// <c>virtual</c> per ADR-038 §4 (substitution seam).
    /// </remarks>
    /// <exception cref="DataverseUnavailableException">A relationship could not be read.</exception>
    public virtual async Task<bool> HasRelatedRecordsAsync(Guid serviceRequestId, CancellationToken ct = default)
    {
        try
        {
            var apiUrl = GetApiUrl();
            var probes = ServiceRequestRelations
                .Select(r => AnyRowAsync(BuildRelatedRowProbeUrl(apiUrl, r, serviceRequestId), ct))
                .ToArray();
            var found = await Task.WhenAll(probes);
            return found.Any(f => f);
        }
        catch (DataverseUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-DATA] Error probing the rows related to service request {Id}", serviceRequestId);
            throw new DataverseUnavailableException("Dataverse could not be read.", ex);
        }
    }

    /// <summary>A one-row collection probe: true when the page holds a row. Any non-success (a 404 included: an unknown
    /// entity set is a broken check, not "no rows") throws <see cref="DataverseUnavailableException"/>.</summary>
    private async Task<bool> AnyRowAsync(string url, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-DATA] Related-row probe failed: {Status} — {Url}", response.StatusCode, url);
                throw new DataverseUnavailableException($"Dataverse answered {(int)response.StatusCode}.");
            }

            var page = await response.Content.ReadFromJsonAsync<ODataResult<Dictionary<string, JsonElement>>>(ct)
                ?? throw new DataverseUnavailableException("Dataverse returned an empty body.");
            // A 200 with no "value" array is not "no rows": it is a body this check cannot read (fail closed).
            if (page.Value is null)
                throw new DataverseUnavailableException("Dataverse returned a body with no value array.");
            return page.Value.Count > 0;
        }
        catch (DataverseUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-DATA] Error probing related rows: {Url}", url);
            throw new DataverseUnavailableException("Dataverse could not be read.", ex);
        }
    }

    // ---------------------------------------------------------------------------
    // Contact and organization queries
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Retrieves contacts with active access to the specified project.
    /// Queries sprk_externalrecordaccess to find contact IDs, then fetches contact details.
    /// </summary>
    /// <remarks>
    /// Task 105: both reads are complete or flagged. The grant-row read is paged to the cap (it was <c>$top=200</c>, so
    /// the 201st participant silently vanished), and the detail read is chunked (see <see cref="IdFilterChunkSize"/>).
    /// The list is <see cref="ExternalCollectionResponse{T}.Truncated"/> when either was cut short.
    /// </remarks>
    public virtual async Task<ExternalCollectionResponse<ExternalContactDto>> GetContactsAsync(Guid projectId, CancellationToken ct = default)
    {
        // Step 1: Get contact IDs from the access junction table
        var (contactIds, idsTruncated) = await GetProjectContactIdsAsync(projectId, ct);
        if (contactIds.Count == 0)
            return new ExternalCollectionResponse<ExternalContactDto> { Truncated = idsTruncated };

        // Step 2: Fetch contact details for those IDs
        var select = "contactid,fullname,firstname,lastname,emailaddress1,telephone1,jobtitle,_parentcustomerid_value";
        var read = await GetByIdsAsync<ContactRow>(
            "contacts", "contactid", contactIds, select, "fullname asc,contactid asc", ct);

        var rows = read.Rows;
        if (contactIds.Count > IdFilterChunkSize)
            rows = rows.OrderBy(r => r.Fullname, StringComparer.CurrentCultureIgnoreCase).ToList();

        return new ExternalCollectionResponse<ExternalContactDto>
        {
            Value = rows.Select(MapContact).ToList(),
            Truncated = idsTruncated || read.Truncated,
        };
    }

    /// <summary>
    /// Retrieves organizations (accounts) linked to the project via project contacts.
    /// </summary>
    /// <remarks>
    /// Task 105: derived from <see cref="GetContactsAsync"/>, so an incomplete contact list makes this list incomplete
    /// too, and says so; the account read is chunked like the contact read.
    /// </remarks>
    public virtual async Task<ExternalCollectionResponse<ExternalOrganizationDto>> GetOrganizationsAsync(Guid projectId, CancellationToken ct = default)
    {
        // Step 1: Get contacts for the project
        var contacts = await GetContactsAsync(projectId, ct);

        // Step 2: Collect unique account IDs from contacts
        var accountIds = contacts.Value
            .Where(c => !string.IsNullOrEmpty(c.ParentcustomeridValue))
            .Select(c => c.ParentcustomeridValue!)
            .Distinct()
            .ToList();

        if (accountIds.Count == 0)
            return new ExternalCollectionResponse<ExternalOrganizationDto> { Truncated = contacts.Truncated };

        // Step 3: Fetch account details
        var select = "accountid,name,websiteurl,telephone1,address1_city,address1_country";
        var read = await GetByIdsAsync<AccountRow>("accounts", "accountid", accountIds, select, "name asc,accountid asc", ct);

        var rows = read.Rows;
        if (accountIds.Count > IdFilterChunkSize)
            rows = rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        return new ExternalCollectionResponse<ExternalOrganizationDto>
        {
            Value = rows.Select(MapOrganization).ToList(),
            Truncated = contacts.Truncated || read.Truncated,
        };
    }

    // ---------------------------------------------------------------------------
    // ADR-024 Resolver-field application (Web API path)
    //
    // Mirrors Sprk.Bff.Api.Services.Workspace.TodoRegardingBuilder.ApplyResolverFieldsAsync
    // but operates over the Dataverse Web API (no ServiceClient eager connect). The type
    // field (sprk_regardingrecordtype) has two different "no answer" cases (task 105):
    //   - ABSENT: a complete read found no active sprk_recordtype_ref row for the entity.
    //     The to-do is created without the type field and a warning is logged.
    //   - UNREADABLE: the read failed. The create is refused with a 503 problem
    //     (todo_record_type_unreadable) and nothing is written; the failure is not cached.
    // The field is not cosmetic: SecureRootInheritance reads the polymorphic pair
    // (sprk_regardingrecordid + sprk_regardingrecordtype, its PairTypeColumn) to find a
    // record's root, so a to-do written without it loses that path.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Apply the four resolver fields to a to-do request body, alongside the specific regarding lookup.
    /// </summary>
    // ⚠️ `ApplyResolverFieldsAsync` lived here until task 029. It was already entity-generic and was
    // NOT the problem — the problem was that it ran INSIDE CreateTodoAsync, a substitution seam, so
    // "which entity do the resolver fields name?" was a question no test could ask. Its three
    // synchronous fields moved into the pure BuildTodoCreatePayload; its one async step
    // (ResolveRecordTypeRefAsync) is now called by CreateTodoAsync and passed in as a value. Same
    // payload, same atomicity, but every decision is now on a testable path.

    /// <summary>
    /// Build a Dataverse model-driven-app record URL for the resolver.
    /// </summary>
    /// <remarks>
    /// Returns a RELATIVE URL — the host origin is resolved by the model-driven app at click
    /// time. No org URL or tenant id is hard-coded here (product portability).
    /// </remarks>
    internal static string BuildRecordUrl(string entityLogicalName, string recordId)
    {
        return $"/main.aspx?pagetype=entityrecord&etn={entityLogicalName}&id={recordId}";
    }

    /// <summary>
    /// Resolve the <c>sprk_recordtype_ref</c> GUID + display name for an entity logical name.
    /// Cached per service instance.
    /// </summary>
    /// <remarks>
    /// <para><b>Absent and unreadable are different answers (task 105).</b> A COMPLETE read with no ACTIVE row means the
    /// type is not configured: <c>null</c>, cached, and the create proceeds without the type bind (the caller logs it — the
    /// pre-existing, non-fatal design). A read that FAILED proves nothing, so it is never cached and never treated as
    /// absent: the create is refused with a 503 problem, because writing it would silently drop the fourth ADR-024
    /// resolver field. Before task 105 a failure (and any exception) was cached as "absent" for the instance's life.</para>
    /// </remarks>
    /// <exception cref="SdapProblemException">503 <c>todo_record_type_unreadable</c> — the lookup failed; nothing was
    /// created.</exception>
    private async Task<(Guid Id, string DisplayName)?> ResolveRecordTypeRefAsync(
        string entityLogicalName, CancellationToken ct)
    {
        if (_recordTypeRefCache.TryGetValue(entityLogicalName, out var cached))
            return cached;

        await _recordTypeCacheSemaphore.WaitAsync(ct);
        try
        {
            // Re-check after acquiring lock
            if (_recordTypeRefCache.TryGetValue(entityLogicalName, out cached))
                return cached;

            // Task 105 (found in passing): the column is sprk_recordlogicalname. The former `sprk_recordentitylogicalname`
            // does not exist (live 2026-10-08: 400 0x80060888), so every external to-do create logged "not found" and
            // left sprk_RegardingRecordType unset — three of ADR-024's four resolver fields, not four.
            // Active rows only, like every sibling lookup of this table (PolymorphicResolverService.ts, FieldMappingHandler.ts):
            // a deactivated type row is not a type to bind.
            var filter = Uri.EscapeDataString($"sprk_recordlogicalname eq '{entityLogicalName}' and statecode eq 0");
            // Task 105: no $top=1 — GetCollectionAsync refuses $top (it suppresses @odata.nextLink). The filter names one
            // entity, so this is one row on one page; the first is taken, as before.
            var url = $"{GetApiUrl()}/sprk_recordtype_refs?$filter={filter}" +
                      "&$select=sprk_recordtype_refid,sprk_recorddisplayname&$orderby=sprk_recordtype_refid asc";

            var read = await GetCollectionAsync<RecordTypeRefRow>(url, ct);
            if (read.Outcome != CollectionReadOutcome.Complete)
            {
                _logger.LogError(
                    "[EXT-DATA] sprk_recordtype_ref for '{Entity}' could not be read ({Outcome}); the to-do create is "
                    + "refused rather than written without its record-type bind. Not cached.",
                    entityLogicalName, read.Outcome);
                throw new SdapProblemException(
                    "todo_record_type_unreadable",
                    "The to-do could not be created",
                    "Its record type could not be read just now, so nothing was created. Please try again.",
                    StatusCodes.Status503ServiceUnavailable);
            }

            var row = read.Rows.FirstOrDefault();
            if (row?.Id is not null && Guid.TryParse(row.Id, out var refId))
            {
                var entry = (
                    Id: refId,
                    DisplayName: row.DisplayName ?? entityLogicalName
                );
                _recordTypeRefCache[entityLogicalName] = entry;
                return entry;
            }

            // A complete read with no row: the type is not configured — the one answer worth caching as absent.
            _recordTypeRefCache[entityLogicalName] = null;
            return null;
        }
        finally
        {
            _recordTypeCacheSemaphore.Release();
        }
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The Contacts holding an ACTIVE, UNEXPIRED grant on a project.
    /// </summary>
    /// <remarks>
    /// Carries the same expiry predicate as the enforcement paths (task 007 / FR-06, finding A-5),
    /// sharing <see cref="ExternalParticipationService.ExpiryPredicate"/> so the two cannot drift.
    ///
    /// <para>This one is a DISPLAY path, not an enforcement path — it answers "who is on this project",
    /// and <see cref="ExternalParticipationService"/> is what actually gates access. It is filtered
    /// anyway because the method's contract says "active access": listing someone whose grant lapsed
    /// last month tells an operator they still have access when they do not, which is how a revocation
    /// gets skipped. A participant list that disagrees with the enforcement path is its own hazard.</para>
    /// <para><b>Task 105:</b> paged to the cap (it was <c>$top=200</c>); the flag says the id list is incomplete.</para>
    /// </remarks>
    private async Task<(IReadOnlyList<string> ContactIds, bool Truncated)> GetProjectContactIdsAsync(
        Guid projectId, CancellationToken ct)
    {
        var expiry = ExternalParticipationService.ExpiryPredicate(DateOnly.FromDateTime(DateTime.UtcNow));
        var filter = Uri.EscapeDataString(
            $"_sprk_project_value eq {projectId} and statecode eq 0 and {expiry}");
        var url = $"{GetApiUrl()}/sprk_externalrecordaccesses?$filter={filter}&$select=_sprk_contact_value" +
                  "&$orderby=sprk_externalrecordaccessid asc";

        var read = await GetCollectionAsync<AccessLinkRow>(url, ct);
        var ids = read.Rows
            .Where(r => !string.IsNullOrEmpty(r.ContactId))
            .Select(r => r.ContactId!)
            .Distinct()
            .ToList();
        return (ids, read.Truncated);
    }

    /// <summary>
    /// Reads the rows whose <paramref name="idColumn"/> is one of <paramref name="ids"/>, in chunks of
    /// <see cref="IdFilterChunkSize"/> ids per request (task 105 — one OR-filter over every id overruns the URL limit).
    /// </summary>
    /// <remarks>
    /// Rows come back in chunk order; a caller with more than one chunk re-sorts. Truncated when any chunk was
    /// truncated or failed (a failed chunk is a first-page failure, which is itself truncated); when EVERY chunk fails
    /// the outcome is <see cref="CollectionReadOutcome.FirstPageFailed"/> — nothing was read.
    /// </remarks>
    private async Task<CollectionRead<TRow>> GetByIdsAsync<TRow>(
        string entitySet, string idColumn, IReadOnlyList<string> ids, string select, string orderBy, CancellationToken ct)
    {
        var rows = new List<TRow>();
        if (ids.Count == 0)
            return new CollectionRead<TRow>(rows, CollectionReadOutcome.Complete);

        var truncated = false;
        var failedChunks = 0;
        var chunks = ids.Chunk(IdFilterChunkSize).ToList();

        foreach (var chunk in chunks)
        {
            var idFilter = string.Join(" or ", chunk.Select(id => $"{idColumn} eq {id}"));
            var url = $"{GetApiUrl()}/{entitySet}?$filter={Uri.EscapeDataString(idFilter)}&$select={select}" +
                      $"&$orderby={orderBy}";

            var read = await GetCollectionAsync<TRow>(url, ct);
            rows.AddRange(read.Rows);
            truncated |= read.Truncated;
            if (read.Outcome == CollectionReadOutcome.FirstPageFailed) failedChunks++;
        }

        if (failedChunks == chunks.Count)
            return new CollectionRead<TRow>(rows, CollectionReadOutcome.FirstPageFailed);

        if (failedChunks > 0)
        {
            _logger.LogWarning(
                "[EXT-DATA] collection_truncated: {Failed} of {Chunks} id chunks of {EntitySet} failed; returning {Count} "
                + "rows for {Ids} ids flagged truncated.",
                failedChunks, chunks.Count, entitySet, rows.Count, ids.Count);
        }

        return new CollectionRead<TRow>(
            rows, truncated ? CollectionReadOutcome.LaterPageFailed : CollectionReadOutcome.Complete);
    }

    /// <summary>
    /// Reads a Dataverse collection, FOLLOWING <c>@odata.nextLink</c> up to <see cref="MaxCollectionPages"/> pages, and
    /// says how the read ended (task 105, ISS-002 / #963).
    /// </summary>
    /// <remarks>
    /// <para><b>The defect this replaces.</b> One request, <c>@odata.nextLink</c> never read, and four callers pinned to
    /// <c>$top=200</c> — which suppresses the <c>nextLink</c> outright. A root with 250 children returned exactly 200,
    /// indistinguishable from a complete list (NFR-03: a cap is never silent).</para>
    /// <para><b>The honesty rule</b> (task 024, <c>SpeContainerMembershipService.ReadPermissionsAsync</c>): rows already
    /// read when the cap is hit, or when a later page fails, are returned as <see cref="CollectionRead{TRow}.Truncated"/>
    /// — never as a complete list — and the cut is logged with the url and the row count. A plain loop, deliberately not
    /// the iterator shape of <c>PrivilegeGroupResolver</c> that double-counts page 1 (ISS-001 / #962): each response's
    /// rows are added exactly once.</para>
    /// <para><b>A first-page failure</b> is truncated too: logged, and returned EMPTY AND FLAGGED, never as an empty list
    /// (before task 105 it was an unflagged empty list, so a Dataverse fault read as "nothing here"). The display lists
    /// surface the flag; the one create-payload reader (<see cref="ResolveRecordTypeRefAsync"/>) fails its request on
    /// it. No reader makes an access decision from this method.</para>
    /// <para><b>A <c>nextLink</c> is used unchanged</b> and with the same <c>Prefer</c> value (Microsoft Learn, "Page
    /// results"), and only when it points at this service's own Web API base: the bearer token is never sent to another
    /// host. One that does not is treated as a failed later page.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">The url carries <c>$top</c> — a programming error (see <see cref="CollectionPageSize"/>).</exception>
    private async Task<CollectionRead<TRow>> GetCollectionAsync<TRow>(string url, CancellationToken ct)
    {
        if (url.Contains("$top=", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "A paged collection read must not carry $top: Dataverse then returns no @odata.nextLink, and $top with "
                + "odata.maxpagesize silently returns the smaller of the two (task 105).", nameof(url));

        var apiBase = GetApiUrl() + "/";
        var rows = new List<TRow>();
        var pageUrl = url;

        for (var page = 1; ; page++)
        {
            var result = await GetCollectionPageAsync<TRow>(pageUrl, ct);
            if (result is null)
            {
                if (page == 1)
                {
                    _logger.LogError(
                        "[EXT-DATA] collection_truncated: page 1 failed; nothing was read — {Url}. Returned empty and "
                        + "flagged truncated, never as an empty list.",
                        url);
                    return new CollectionRead<TRow>(rows, CollectionReadOutcome.FirstPageFailed);
                }

                _logger.LogError(
                    "[EXT-DATA] collection_truncated: page {Page} failed after {Count} rows were read — {Url}. The list "
                    + "is INCOMPLETE and is returned flagged truncated, never as a complete list.",
                    page, rows.Count, url);
                return new CollectionRead<TRow>(rows, CollectionReadOutcome.LaterPageFailed);
            }

            if (result.Value is { } pageRows)
                rows.AddRange(pageRows);

            if (string.IsNullOrEmpty(result.NextLink))
                return new CollectionRead<TRow>(rows, CollectionReadOutcome.Complete);

            if (page >= MaxCollectionPages)
            {
                _logger.LogWarning(
                    "[EXT-DATA] collection_truncated: the {Cap}-row cap ({Pages} pages) was reached with more rows "
                    + "remaining — {Url}. Returning {Count} rows flagged truncated.",
                    MaxCollectionRows, MaxCollectionPages, url, rows.Count);
                return new CollectionRead<TRow>(rows, CollectionReadOutcome.CapReached);
            }

            if (!result.NextLink.StartsWith(apiBase, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "[EXT-DATA] collection_truncated: page {Page} returned a nextLink outside the Dataverse Web API base; "
                    + "not followed (the token is never sent elsewhere). {Count} rows read — {Url}. Flagged truncated.",
                    page, rows.Count, url);
                return new CollectionRead<TRow>(rows, CollectionReadOutcome.LaterPageFailed);
            }

            pageUrl = result.NextLink;
        }
    }

    /// <summary>One page of a collection read, or <c>null</c> when it failed (logged). Cancellation by the caller is rethrown.</summary>
    private async Task<ODataResult<TRow>?> GetCollectionPageAsync<TRow>(string url, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Add("Prefer", $"odata.maxpagesize={CollectionPageSize}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-DATA] GET collection failed: {Status} — {Url}", response.StatusCode, url);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ODataResult<TRow>>(ct)
                ?? throw new InvalidOperationException("Dataverse returned an empty collection body.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller went away. That is not a failed page and nothing is reported as truncated to anyone. Rethrown, so
            // an aborted chunked read stops at once instead of logging an error for every remaining chunk.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-DATA] Error fetching collection: {Url}", url);
            return null;
        }
    }

    private async Task<TRow?> GetSingleAsync<TRow>(string url, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return default;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-DATA] GET single failed: {Status} — {Url}", response.StatusCode, url);
                return default;
            }

            return await response.Content.ReadFromJsonAsync<TRow>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-DATA] Error fetching single record: {Url}", url);
            return default;
        }
    }

    /// <summary>
    /// <see cref="GetSingleAsync{TRow}"/> for a caller that must tell "not there" from "could not read": a 404 is
    /// <c>default</c>; any other non-success, transport error or token failure throws
    /// <see cref="DataverseUnavailableException"/>. A cancelled caller still sees <see cref="OperationCanceledException"/>.
    /// </summary>
    private async Task<TRow?> GetSingleOrThrowAsync<TRow>(string url, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return default;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-DATA] GET single failed: {Status} — {Url}", response.StatusCode, url);
                throw new DataverseUnavailableException($"Dataverse answered {(int)response.StatusCode}.");
            }

            return await response.Content.ReadFromJsonAsync<TRow>(ct);
        }
        catch (DataverseUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-DATA] Error fetching single record: {Url}", url);
            throw new DataverseUnavailableException("Dataverse could not be read.", ex);
        }
    }

    private async Task<string> GetAppOnlyTokenAsync(CancellationToken ct)
    {
        if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.Add(TokenRefreshBuffer))
            return _currentToken.Value.Token;

        if (!await _tokenSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
            throw new TimeoutException("Timed out waiting for Dataverse token");

        try
        {
            // Double-check inside the lock
            if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.Add(TokenRefreshBuffer))
                return _currentToken.Value.Token;

            var dataverseUrl = _configuration["Dataverse:ServiceUrl"]
                ?? throw new InvalidOperationException("Dataverse:ServiceUrl is required");

            var scope = $"{dataverseUrl.TrimEnd('/')}/.default";
            _currentToken = await _credential.GetTokenAsync(new TokenRequestContext([scope]), ct);

            _logger.LogDebug("[EXT-DATA] Acquired new Dataverse token, expires {ExpiresOn}", _currentToken.Value.ExpiresOn);
            return _currentToken.Value.Token;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    private string GetApiUrl()
    {
        var dataverseUrl = _configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl is required");
        return $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";
    }

    // ---------------------------------------------------------------------------
    // Row → DTO mappers
    // ---------------------------------------------------------------------------

    /// <summary>Logs every project row whose <c>sprk_issecure</c> came back EMPTY (shown as secure).</summary>
    private void WarnOnEmptySecureFlag(IEnumerable<ProjectRow> rows)
    {
        var empty = rows.Where(r => r.SprkIssecure is null).Select(r => r.SprkProjectid ?? "(no id)").ToList();
        if (empty.Count == 0) return;

        _logger.LogError(
            "[EXT-DATA] sprk_issecure came back EMPTY on {Count} project(s) ({ProjectIds}); shown as SECURE (fail closed). "
            + "This service has likely lost its field-level-security Read on the column "
            + "(scripts/Set-SecureFlagFieldSecurity.ps1 -Verify), or the row predates the backfill "
            + "(scripts/Repair-SecureFlagNulls.ps1).",
            empty.Count, string.Join(", ", empty));
    }

    /// <remarks>
    /// <b>An EMPTY <c>sprk_issecure</c> maps to <c>true</c></b> (task 150, round 17 item 3: fail closed, never "not
    /// secure"). Every row holds true or false since the task 150 backfill, and the column is field-secured, so an
    /// empty value means this app identity's field-level Read was lost and the real value was masked. The external SPA
    /// uses the value only to label the project; labelling a possibly-secure project as secure is the safe direction,
    /// and <see cref="WarnOnEmptySecureFlag"/> names the cause in the log.
    /// </remarks>
    internal static ExternalProjectDto MapProject(ProjectRow r) => new()
    {
        SprkProjectid = r.SprkProjectid ?? "",
        SprkName = r.SprkName ?? "",
        SprkReferencenumber = r.SprkReferencenumber,
        SprkDescription = r.SprkDescription,
        SprkIssecure = r.SprkIssecure ?? true,
        SprkStatus = r.SprkStatus,
        Createdon = r.Createdon,
        Modifiedon = r.Modifiedon,
    };

    private static ExternalDocumentDto MapDocument(DocumentRow r) => new()
    {
        SprkDocumentid = r.SprkDocumentid ?? "",
        SprkName = r.SprkName ?? "",
        SprkDocumenttype = r.SprkDocumenttype,
        SprkSummary = r.SprkSummary,
        SprkProjectidValue = r.SprkProjectidValue,
        Createdon = r.Createdon,
        SprkCreatedbypersonValue = r.CreatedByPersonValue,
        SprkCreatedbycontactValue = r.CreatedByContactValue,
    };

    private static ExternalTodoDto MapTodo(TodoRow r) => new()
    {
        SprkTodoid = r.SprkTodoid ?? "",
        SprkName = r.SprkName ?? "",
        SprkNotes = r.SprkNotes,
        SprkDuedate = r.SprkDuedate,
        SprkPriorityscore = r.SprkPriorityscore,
        SprkEffortscore = r.SprkEffortscore,
        SprkTodocolumn = r.SprkTodocolumn,
        SprkTodopinned = r.SprkTodopinned,
        Statecode = r.Statecode,
        Statuscode = r.Statuscode,
        Createdon = r.Createdon,
        SprkRegardingprojectValue = r.SprkRegardingprojectValue,
        SprkRegardingrecordid = r.SprkRegardingrecordid,
        SprkRegardingrecordname = r.SprkRegardingrecordname,
        SprkRegardingrecordurl = r.SprkRegardingrecordurl,
        SprkCreatedbypersonValue = r.CreatedByPersonValue,
        SprkCreatedbycontactValue = r.CreatedByContactValue,
    };

    private static ExternalEventDto MapEvent(EventRow r) => new()
    {
        SprkEventid = r.SprkEventid ?? "",
        SprkName = r.SprkName ?? "",
        // Task 098: always the calendar date "yyyy-MM-dd" — also from an environment whose column is not yet converted
        // (a timestamp there reads as its UTC date, the day the conversion keeps).
        SprkDuedate = DataverseDateOnly.TryParse(r.SprkDuedate, out var due) ? DataverseDateOnly.Format(due) : r.SprkDuedate,
        SprkStatus = r.SprkStatus,
        Createdon = r.Createdon,
        SprkRegardingprojectValue = r.SprkRegardingprojectValue,
        SprkCreatedbypersonValue = r.CreatedByPersonValue,
        SprkCreatedbycontactValue = r.CreatedByContactValue,
    };

    private static ExternalContactDto MapContact(ContactRow r) => new()
    {
        Contactid = r.Contactid ?? "",
        Fullname = r.Fullname,
        Firstname = r.Firstname,
        Lastname = r.Lastname,
        Emailaddress1 = r.Emailaddress1,
        Telephone1 = r.Telephone1,
        Jobtitle = r.Jobtitle,
        ParentcustomeridValue = r.ParentcustomeridValue,
    };

    private static ExternalOrganizationDto MapOrganization(AccountRow r) => new()
    {
        Accountid = r.Accountid ?? "",
        Name = r.Name ?? "",
        Websiteurl = r.Websiteurl,
        Telephone1 = r.Telephone1,
        Address1City = r.Address1City,
        Address1Country = r.Address1Country,
    };
}
