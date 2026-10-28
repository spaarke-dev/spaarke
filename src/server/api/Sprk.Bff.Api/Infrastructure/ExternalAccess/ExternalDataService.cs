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
    /// The <c>Prefer</c> value every collection page carries: server-driven paging (<see cref="CollectionPageSize"/>) and the
    /// formatted-value annotations (task 064), so a choice column arrives with its label beside its number
    /// (<c>sprk_documenttype@OData.Community.Display.V1.FormattedValue</c>). Preferences are comma-separated (Microsoft
    /// Learn, "Formatted values"). A <c>nextLink</c> is followed with the same value.
    /// </summary>
    internal static readonly string CollectionPrefer =
        $"odata.maxpagesize={CollectionPageSize},odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"";

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
        // Task 064: sprk_documenttype is a CHOICE column (Entity.xml: picklist), so Dataverse sends its NUMBER. Typed string?
        // it failed the whole page ("could not be converted to System.String ... token type 'Number'"). The raw value is an
        // int?; the label comes from the formatted-value annotation the collection reads ask for (CollectionPrefer).
        [JsonPropertyName("sprk_documenttype")] public int? SprkDocumenttype { get; set; }
        [JsonPropertyName("sprk_documenttype@OData.Community.Display.V1.FormattedValue")] public string? SprkDocumenttypeLabel { get; set; }
        [JsonPropertyName("sprk_filesummary")] public string? SprkSummary { get; set; }
        [JsonPropertyName("_sprk_project_value")] public string? SprkProjectidValue { get; set; }
        // Fix round for task 039: the other root links, read so the list can withhold a document also filed to a root the
        // caller cannot read (ExternalMultiRootRule). The typed column and its related twin, per root, are the roots in
        // DocumentLinkFields (the one declaration; pinned by test). Never mapped onto the wire DTO.
        [JsonPropertyName("_sprk_relatedproject_value")] public string? SprkRelatedprojectValue { get; set; }
        [JsonPropertyName("_sprk_matter_value")] public string? SprkMatterValue { get; set; }
        [JsonPropertyName("_sprk_relatedmatter_value")] public string? SprkRelatedmatterValue { get; set; }
        [JsonPropertyName("_sprk_workassignment_value")] public string? SprkWorkassignmentValue { get; set; }
        [JsonPropertyName("_sprk_relatedworkassignment_value")] public string? SprkRelatedworkassignmentValue { get; set; }
        [JsonPropertyName("createdon")] public string? Createdon { get; set; }
        [JsonPropertyName(CreatedByPersonValueAttribute)] public string? CreatedByPersonValue { get; set; }
        [JsonPropertyName(CreatedByContactValueAttribute)] public string? CreatedByContactValue { get; set; }
        // R3 ISS-011 (#1886): the SPE pointers, read ONLY to derive the wire DTO's boolean hasFile (MapDocument). The values
        // themselves are never mapped onto the DTO: no Graph pointer reaches the client (ExternalModuleRegistry.PointerColumns).
        [JsonPropertyName("sprk_graphdriveid")] public string? SprkGraphdriveid { get; set; }
        [JsonPropertyName("sprk_graphitemid")] public string? SprkGraphitemid { get; set; }
        // Task 187: every other selected column, among them the row's live-parent lookups (ExternalMultiRootRule.LiveParentSelect),
        // read by name so the select and the rule share ONE list. Never mapped onto the wire DTO.
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
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
        // Task 187: every other selected column, among them the row's live-parent lookups (ExternalMultiRootRule.LiveParentSelect),
        // read by name so the select and the rule share ONE list. Never mapped onto the wire DTO.
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    // Task 097: sprk_event has NO sprk_name / sprk_status columns (live: HTTP 400 "Could not find a property named
    // 'sprk_name'" — so the external events list and create always failed). The live columns are sprk_eventname (the
    // primary name) and, for status, statuscode — the column the rest of the BFF (and POST /events/{id}/complete) writes.
    // D-28 (task 066): statuscode is the status
    // of record; the deprecated second status column is neither read nor written here. The SPA wire names (sprk_name / sprk_status on
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
        // Task 039: the other two root lookups, read so a list can withhold an event also filed to a record the caller
        // cannot read (see ReadableEvents). Never mapped onto the wire DTO.
        [JsonPropertyName("_sprk_regardingmatter_value")] public string? SprkRegardingmatterValue { get; set; }
        [JsonPropertyName("_sprk_regardingworkassignment_value")] public string? SprkRegardingworkassignmentValue { get; set; }
        [JsonPropertyName(CreatedByPersonValueAttribute)] public string? CreatedByPersonValue { get; set; }
        [JsonPropertyName(CreatedByContactValueAttribute)] public string? CreatedByContactValue { get; set; }
        // Task 187: every other selected column, among them the row's live-parent lookups (ExternalMultiRootRule.LiveParentSelect),
        // read by name so the select and the rule share ONE list. Never mapped onto the wire DTO.
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
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

    /// <summary>
    /// Task 039 section 12 (option B, effective status): the app-only reader the ONE filing walk
    /// (<see cref="EffectiveRootFlags.ReadAncestryAsync"/>) reads through, so <see cref="GetNonSecureRootsAsync"/> answers a
    /// root's EFFECTIVE secure status. Registered unconditionally (GraphModule), so the typed-client factory supplies it. A
    /// null reader (a test double that passes none) makes every work assignment or project read as secure (fail closed).
    /// </summary>
    private readonly IGenericEntityService? _filing;

    public ExternalDataService(
        HttpClient httpClient,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<ExternalDataService> logger,
        IGenericEntityService? filing = null,
        Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver? columns = null,
        Sprk.Bff.Api.Services.Identity.ISystemUserIdentityResolver? identity = null)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _credential = credential;
        _logger = logger;
        _filing = filing;
        _identity = identity;
        // Task 187: which lineage lookups a parent table has in this environment (the 6h-cached metadata probe the stamp path
        // uses), so the live-parent walk never selects a column a later schema adds. Without one (a test double), every
        // lookup of the lineage map is taken as present.
        _columnProbe = columns is not null
            ? columns.ProbeColumnsAsync
            : (table, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
                Sprk.Bff.Api.Services.Dataverse.ParentLineage.ChildFiling.TryGetValue(table, out var lookups)
                    ? lookups.Keys
                    : Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Task 188: who is an internal systemuser (the 065 rule's own exemption, <c>CommunicationThreadReadService</c>), so the
    /// document narrowing (<see cref="NarrowDocumentsAsync"/>) leaves them as the internal plane does. Registered as a
    /// singleton (NotificationsModule), so the typed-client factory supplies it. Without one (a test double), every caller is
    /// narrowed (fail closed).
    /// </summary>
    private readonly Sprk.Bff.Api.Services.Identity.ISystemUserIdentityResolver? _identity;

    /// <summary>Task 187: the column probe the live-parent walk reads parent tables through (see the constructor).</summary>
    private readonly Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver.EntityColumnProbe _columnProbe;

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
    /// <b>Invoice</b> (owner decision 2026-10-11, task 031 option A "inherit"): an invoice is not a root in the caller's
    /// accessible set, so its documents take the caller's rights on the invoice's PARENT records — the union over its
    /// <c>sprk_matter</c> and <c>sprk_project</c> (<see cref="GetInvoiceParentsAsync"/>; the endpoint's rights arm), the
    /// same "any accessible parent" rule the invoices module and the message routes apply.
    /// </remarks>
    public enum DocumentParentKind
    {
        Project = 1,
        Matter = 2,
        WorkAssignment = 3,
        ServiceRequest = 4,
        Invoice = 5,
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
            // Task 031 owner decision 2026-10-11 (option A): sprk_document.sprk_invoice (Entity.xml:3452), navigation
            // property sprk_Invoice (Other/Relationships/sprk_Invoice.xml, sprk_invoice_sprk_document).
            [DocumentParentKind.Invoice] = new(
                DocumentParentKind.Invoice, "sprk_invoice", "sprk_invoices", "sprk_Invoice", "sprk_invoice"),
        };

    /// <summary>The binding for a document parent kind. Throws for a value outside the table: a programming error.</summary>
    internal static DocumentParentBinding GetDocumentParentBinding(DocumentParentKind kind) =>
        DocumentParents.TryGetValue(kind, out var binding)
            ? binding
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a document parent kind.");

    /// <summary>Retrieves the documents linked to the specified parent record through that parent's lookup, minus any also filed to a root the caller cannot read.</summary>
    /// <remarks>
    /// Task 105: followed page by page up to <see cref="MaxCollectionRows"/> (it was a single <c>$top=200</c> read, so
    /// a project's 201st document was silently missing); a cut-short list says
    /// <see cref="ExternalCollectionResponse{T}.Truncated"/>. The primary key breaks <c>createdon</c> ties so pages
    /// never overlap. Task 031 generalized it from project to every <see cref="DocumentParentKind"/>; the project query
    /// is unchanged.
    /// <para>Fix round for task 039: a document carries typed links to a project, a matter and a work assignment (and a
    /// related twin of each), so one filed to a secure matter AND to a project the caller can read used to list on the
    /// project route (name and AI summary). It is now listed only if the caller holds Read on every SECURE root it carries
    /// (<see cref="ExternalMultiRootRule"/>, owner decision 2026-10-12, option B); the secure flags of the linked roots the
    /// caller cannot read are read once for the whole list. The caller has ALREADY been authorized for
    /// <paramref name="parentId"/>.</para>
    /// </remarks>
    public virtual async Task<ExternalCollectionResponse<ExternalDocumentDto>> GetDocumentsAsync(
        DocumentParentKind parentKind, Guid parentId, CallerPrincipal caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var url = BuildDocumentListUrl(GetApiUrl(), parentKind, parentId);

        var read = await GetCollectionAsync<DocumentRow>(url, ct);
        // Task 187: the document's own root links stay the primary input; its other parents (the related event,
        // communication, to-do, invoice ...) are read too, and their LIVE roots join the rule.
        var links = read.Rows.Select(DocumentLinksOf).ToList();
        var live = await ReadLiveParentRootsAsync(links.Select(l => l.LiveParents), ct);
        var roots = links.Select(l => l.RuleInput(live)).ToList();
        var flags = await ReadSecureRootFlagsAsync(caller, roots.Select(r => (IEnumerable<(ExternalGrantRootType, string?)>)r), ct);
        var readable = read.Rows.Where((_, i) => ExternalMultiRootRule.ChildRowReadable(caller, flags, roots[i])).ToList();
        if (readable.Count != read.Rows.Count)
        {
            _logger.LogInformation(
                "[EXT-DATA] documents: {Withheld} of {Total} document(s) of {ParentKind} {ParentId} withheld - also filed to a secure record the caller cannot read",
                read.Rows.Count - readable.Count, read.Rows.Count, parentKind, parentId);
        }

        // Task 188 (D7, #2126): THE document narrowing, after the root rule: own or ancestor Restricted, or an internal-only /
        // non-Open communication on the bounded parent walk, hides a document from a caller who is not an internal systemuser.
        var candidates = readable.Select(r => ExternalDocumentNarrowing.FromWebApi(DocumentIdOf(r), r.Extra)).ToList();
        var narrowing = await NarrowDocumentsAsync(caller, candidates, ct);
        var visible = readable.Where((_, i) => narrowing.IsVisible(candidates[i])).ToList();
        if (visible.Count != readable.Count)
        {
            _logger.LogInformation(
                "[EXT-DATA] documents: {Hidden} of {Total} document(s) of {ParentKind} {ParentId} hidden by the document narrowing ({Reasons})",
                readable.Count - visible.Count, readable.Count, parentKind, parentId, narrowing.HiddenSummary(candidates));
        }

        return new ExternalCollectionResponse<ExternalDocumentDto>
        {
            Value = visible.Select(MapDocument).ToList(),
            Truncated = read.Truncated
                        || RuleReadCutListShort(flags, live, read.Rows.Count - readable.Count)
                        || (narrowing.Faulted && visible.Count < readable.Count),
        };
    }

    /// <summary>A document row's id (<see cref="Guid.Empty"/> when it did not come back as a GUID: never visible).</summary>
    private static Guid DocumentIdOf(DocumentRow row) =>
        Guid.TryParse(row.SprkDocumentid, out var id) ? id : Guid.Empty;

    /// <summary>
    /// Review of 039 option B (K2): a list that withheld rows while the secure-flag read was INCOMPLETE (a failed chunk, a
    /// walk that could not decide, the lookup bound) may be missing rows only because of that, so it says
    /// <see cref="ExternalCollectionResponse{T}.Truncated"/>. A complete read that withheld a genuinely secure row does not.
    /// </summary>
    private static bool FlagReadCutListShort(SecureRootFlags flags, int withheld) => withheld > 0 && !flags.IsComplete;

    /// <summary>
    /// Task 187: <see cref="FlagReadCutListShort"/> for a list that also resolved its rows' live parents: a withheld row may be
    /// missing only because a flag read OR a live-parent read could not decide, so the list says <c>Truncated</c>.
    /// </summary>
    private static bool RuleReadCutListShort(SecureRootFlags flags, LiveParentRoots live, int withheld) =>
        withheld > 0 && (!flags.IsComplete || !live.IsComplete);

    private const string TodoTable = "sprk_todo";
    private const string EventTable = "sprk_event";
    private const string DocumentTable = "sprk_document";

    /// <summary>Task 187: <c>,col1,col2...</c>, a child table's live-parent lookups to append to a list <c>$select</c>.</summary>
    private static string LiveParentSelectSuffix(string childTable) =>
        string.Concat(ExternalMultiRootRule.LiveParentSelect(childTable).Select(c => "," + c));

    private static DocumentRootLinks DocumentLinksOf(DocumentRow row) =>
        new(
            row.SprkProjectidValue, row.SprkRelatedprojectValue,
            row.SprkMatterValue, row.SprkRelatedmatterValue,
            row.SprkWorkassignmentValue, row.SprkRelatedworkassignmentValue)
        {
            Row = row.Extra,
        };

    /// <summary>
    /// The six root links of a <c>sprk_document</c> (a typed lookup and its related twin, per root: project, matter, work
    /// assignment) as raw <c>_..._value</c> strings, and the one place the multi-root rule is applied to them
    /// (<see cref="ExternalMultiRootRule"/>). Shared by the document LIST (<see cref="GetDocumentsAsync"/>) and every
    /// single-document route (download, versions), so one document is not readable on one path and hidden on another.
    /// </summary>
    /// <remarks>
    /// The columns are the root subset of <see cref="DocumentLinkFields"/> (pinned by test). A value that is null or blank
    /// is "not filed to this root"; a value that is not a GUID reads as unreadable (fail closed).
    /// </remarks>
    public readonly record struct DocumentRootLinks(
        string? Project, string? RelatedProject,
        string? Matter, string? RelatedMatter,
        string? WorkAssignment, string? RelatedWorkAssignment)
    {
        /// <summary>The <c>_..._value</c> select columns of the six links, in the order of this record's members.</summary>
        internal static readonly IReadOnlyList<string> SelectColumns = new[]
        {
            "_sprk_project_value", "_sprk_relatedproject_value",
            "_sprk_matter_value", "_sprk_relatedmatter_value",
            "_sprk_workassignment_value", "_sprk_relatedworkassignment_value",
        };

        /// <summary>The six links as multi-root rule input.</summary>
        public (ExternalGrantRootType Root, string? LookupValue)[] Links => new[]
        {
            (ExternalGrantRootType.Project, Project),
            (ExternalGrantRootType.Project, RelatedProject),
            (ExternalGrantRootType.Matter, Matter),
            (ExternalGrantRootType.Matter, RelatedMatter),
            (ExternalGrantRootType.WorkAssignment, WorkAssignment),
            (ExternalGrantRootType.WorkAssignment, RelatedWorkAssignment),
        };

        /// <summary>
        /// Task 187: the document row as read. Its parent lookups that are NOT root links
        /// (<see cref="ExternalMultiRootRule.LiveParentLookupsOf"/> of <c>sprk_document</c>: the related event, communication,
        /// to-do, agreement, invoice, the parent and canonical document ...) join the rule with their LIVE roots; the rule
        /// derives them from the row itself (<see cref="ChildRowRoots"/>). <c>null</c>: no such column came back.
        /// </summary>
        public IReadOnlyDictionary<string, JsonElement>? Row { get; init; }

        /// <summary>The parents as live-parent read input (empty when none).</summary>
        internal IReadOnlyList<LiveParentLookup> LiveParents => ExternalMultiRootRule.LiveParentsOf(DocumentTable, Row);

        /// <summary>
        /// The rule input: the six root links ∪ the live roots of the row's parents. <paramref name="live"/> <c>null</c>
        /// resolves nothing, so a document naming a parent is withheld (fail closed).
        /// </summary>
        /// <remarks>
        /// Task 187, fix round 1, owner decision D4: on an INVOICE route the route invoice is walked like every other parent.
        /// The caller was authorized for the invoice through the union of its parents (option A), so an invoice whose other
        /// parents are ordinary changes nothing; one whose live roots include a SECURE root the caller cannot read withholds
        /// its documents, as the invoices list withholds the invoice (D2).
        /// </remarks>
        public ChildRowRoots RuleInput(LiveParentRoots? live) =>
            ChildRowRoots.Of(DocumentTable, Row, Links, live ?? LiveParentRoots.Empty);

        /// <summary>
        /// Whether <paramref name="caller"/> holds Read on every SECURE root this document is filed to, and (task 187) on every
        /// secure LIVE root of its other parents; <paramref name="flags"/> are the secure flags read for the request
        /// (<see cref="ReadSecureRootFlagsAsync"/>) over <see cref="RuleInput"/>.
        /// </summary>
        public bool SecureRootsReadableBy(CallerPrincipal caller, SecureRootFlags flags, LiveParentRoots? live = null) =>
            ExternalMultiRootRule.ChildRowReadable(caller, flags, RuleInput(live));

        /// <summary>The links a single-row read returned. A present value that is not a JSON string reads as malformed.</summary>
        internal static DocumentRootLinks From(IReadOnlyDictionary<string, JsonElement> row) => new(
            Raw(row, SelectColumns[0]), Raw(row, SelectColumns[1]),
            Raw(row, SelectColumns[2]), Raw(row, SelectColumns[3]),
            Raw(row, SelectColumns[4]), Raw(row, SelectColumns[5]))
        {
            Row = row,
        };

        private static string? Raw(IReadOnlyDictionary<string, JsonElement> row, string column)
        {
            if (!row.TryGetValue(column, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => value.GetString(),
                _ => "malformed", // not a GUID: the multi-root rule withholds it (fail closed)
            };
        }
    }

    /// <summary>
    /// What the single-document routes decide on: the document's parent through the ROUTE's lookup, its display name, and
    /// every root link it carries (<see cref="DocumentRootLinks"/>). Deconstructs to <c>(ParentId, DocumentName)</c>.
    /// </summary>
    public sealed record DocumentScopeRead(Guid? ParentId, string? DocumentName, DocumentRootLinks Roots)
    {
        /// <summary>The document does not exist, or could not be read.</summary>
        public static readonly DocumentScopeRead Absent = new(null, null, default);

        public void Deconstruct(out Guid? parentId, out string? documentName)
        {
            parentId = ParentId;
            documentName = DocumentName;
        }

        /// <summary>Whether the document is under <paramref name="routeParentId"/> through the route's lookup.</summary>
        public bool IsUnder(Guid routeParentId) => ParentId is { } parent && parent == routeParentId;

        /// <summary>
        /// Whether the document is under <paramref name="routeParentId"/> through the route's lookup AND the caller holds
        /// Read on every SECURE root it is filed to (<paramref name="flags"/>: the request's secure-flag read). False answers
        /// the route's one "not part of this record" response.
        /// </summary>
        public bool IsReadableUnder(Guid routeParentId, CallerPrincipal caller, SecureRootFlags flags, LiveParentRoots? live = null) =>
            IsUnder(routeParentId) && Roots.SecureRootsReadableBy(caller, flags, live);
    }

    /// <summary>The document list URL for one parent. Pure, so a test can read the filter (task 031).</summary>
    internal static string BuildDocumentListUrl(string apiUrl, DocumentParentKind parentKind, Guid parentId)
    {
        var binding = GetDocumentParentBinding(parentKind);
        // Task 032 (FR-26, review 031 K2): both creator stamps, so the SPA can offer Delete only on the caller's own rows.
        // Fix round for task 039: every root link (typed + related twin), so a document also filed to a root the caller
        // cannot read can be withheld (ExternalMultiRootRule).
        var select = "sprk_documentid,sprk_documentname,sprk_documenttype,sprk_filesummary,_sprk_project_value,"
                     + "_sprk_relatedproject_value,_sprk_matter_value,_sprk_relatedmatter_value,"
                     + "_sprk_workassignment_value,_sprk_relatedworkassignment_value,createdon,"
                     + "sprk_graphdriveid,sprk_graphitemid," // R3 ISS-011 (#1886): read only to derive hasFile; never put on the wire
                     + CreatorStampSelect
                     + LiveParentSelectSuffix(DocumentTable); // task 187: the parents whose live roots the rule takes in
        // Task 188: the columns the document narrowing reads (the two lookups are already lineage parents; the own Access
        // Permission is not), each once.
        select = string.Join(",", select.Split(',').Concat(ExternalDocumentNarrowing.SelectColumns).Distinct(StringComparer.Ordinal));
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
    internal static readonly string[] CreatorColumns =
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
    /// Returns the document's parent id THROUGH THE LOOKUP OF <paramref name="parentKind"/>, its display name, and every
    /// root link it carries. The external download and versions endpoints use it (task 027; generalized by task 031) for
    /// document→parent authorization scoping BEFORE any SPE pointer resolution or Graph content read.
    /// </summary>
    /// <remarks>
    /// <para>App-only Dataverse read. Returns <see cref="DocumentScopeRead.Absent"/> when the document does not exist or
    /// cannot be read, and a null parent when it is not linked through that lookup. Does NOT expose any Graph pointer
    /// (driveId/itemId).</para>
    /// <para>Fix round for task 039: the select also reads the six root links (<see cref="DocumentRootLinks"/>), so a
    /// route can apply the multi-root rule (<see cref="ExternalMultiRootRule"/>) to ONE document as the list does to many.
    /// Without it a reader of project P who knew the id of a document filed to P AND to a secure matter M downloaded it
    /// through P's route.</para>
    /// </remarks>
    public virtual async Task<DocumentScopeRead> GetDocumentParentAndNameAsync(
        DocumentParentKind parentKind, Guid documentId, CancellationToken ct = default)
    {
        var binding = GetDocumentParentBinding(parentKind);
        var columns = new[] { "sprk_documentname", binding.LookupValueAttribute }
            .Concat(DocumentRootLinks.SelectColumns)
            .Concat(ExternalMultiRootRule.LiveParentSelect(DocumentTable)) // task 187
            .Concat(ExternalDocumentNarrowing.SelectColumns) // task 188: what the document narrowing reads
            .Distinct(StringComparer.Ordinal);
        var url = $"{GetApiUrl()}/sprk_documents({documentId})?$select={string.Join(",", columns)}";

        var row = await GetSingleAsync<Dictionary<string, JsonElement>>(url, ct);
        if (row is null) return DocumentScopeRead.Absent;

        var name = row.TryGetValue("sprk_documentname", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()
            : null;
        return new DocumentScopeRead(GuidValue(row, binding.LookupValueAttribute), name, DocumentRootLinks.From(row));
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

    /// <summary>
    /// Whether a service request is still open. <c>sprk_servicerequest</c> has exactly two states (live describe,
    /// 2026-10-12): <c>statecode</c> Active (0) / Inactive (1), with <c>statuscode</c> Active (1) / Inactive (2). There is
    /// no separate "resolved" status, so Inactive IS closed. <see cref="Unknown"/> is the zero value on purpose: a default,
    /// an absent row and any other value fail closed (owner decision 2026-10-12).
    /// </summary>
    public enum ServiceRequestOpenState
    {
        /// <summary>Not readable as Active or Inactive (absent row, null, or a value outside 0 / 1). Treated as closed.</summary>
        Unknown = 0,

        /// <summary><c>statecode</c> = 0.</summary>
        Open = 1,

        /// <summary><c>statecode</c> = 1 (inactive / resolved).</summary>
        Closed = 2,
    }

    /// <summary>The Dataverse <c>statecode</c> → <see cref="ServiceRequestOpenState"/> rule. Pure; anything but 0 or 1 is Unknown.</summary>
    internal static ServiceRequestOpenState ServiceRequestStateFor(int? statecode) => statecode switch
    {
        0 => ServiceRequestOpenState.Open,
        1 => ServiceRequestOpenState.Closed,
        _ => ServiceRequestOpenState.Unknown,
    };

    /// <summary>A service request's requester and open / closed state, from ONE row read.</summary>
    /// <param name="RequesterId"><c>sprk_requestedby</c>; null when the request names no requester.</param>
    /// <param name="State"><c>statecode</c> as a <see cref="ServiceRequestOpenState"/>; <see cref="ServiceRequestOpenState.Unknown"/> when not a readable 0 / 1.</param>
    public readonly record struct ServiceRequestRead(Guid? RequesterId, ServiceRequestOpenState State);

    /// <summary>
    /// The requester and the open / closed state (<c>statecode</c>) of a service request in ONE app-only read, for the
    /// delete route, which needs both (the requester gate first, then the state). Null when the request does not exist
    /// (a genuine 404); a state that is not a readable 0 / 1 is <see cref="ServiceRequestOpenState.Unknown"/>; a read that
    /// FAILS throws, so the caller answers 503 and nothing is deleted (same rule as <see cref="GetServiceRequestRequesterAsync"/>).
    /// </summary>
    /// <exception cref="DataverseUnavailableException">The read could not be completed.</exception>
    public virtual async Task<ServiceRequestRead?> GetServiceRequestRequesterAndStateAsync(
        Guid serviceRequestId, CancellationToken ct = default)
    {
        var url = $"{GetApiUrl()}/sprk_servicerequests({serviceRequestId})?$select=_sprk_requestedby_value,statecode";

        var row = await GetSingleOrThrowAsync<Dictionary<string, JsonElement>>(url, ct);
        if (row is null) return null;

        var state = row.TryGetValue("statecode", out var stateElement)
                    && stateElement.ValueKind == JsonValueKind.Number
                    && stateElement.TryGetInt32(out var code)
            ? ServiceRequestStateFor(code)
            : ServiceRequestOpenState.Unknown;
        return new ServiceRequestRead(GuidValue(row, "_sprk_requestedby_value"), state);
    }

    // ---------------------------------------------------------------------------
    // Legal Front Door intake (spaarke-SPA-external-access-platform-r3 task 035, FR-05 / FR-06)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The <c>sprk_servicerequest.sprk_requesttype</c> options (local choice, task 020; <c>Entity.xml</c> optionset
    /// <c>sprk_servicerequest_sprk_requesttype</c>): one per intake wizard. Any other value is refused.
    /// </summary>
    internal static readonly IReadOnlyDictionary<int, string> ServiceRequestTypes = new Dictionary<int, string>
    {
        [100000000] = "NDA Assessment",
        [100000001] = "Invention Submission",
        [100000002] = "Policy Question",
        [100000003] = "Trademark Search",
    };

    /// <summary><c>sprk_servicerequest.sprk_direction</c> = Inbound (<c>Entity.xml</c> optionset
    /// <c>sprk_servicerequest_sprk_direction</c>: Inbound 100000000, Outbound 100000001). An intake is always inbound.</summary>
    internal const int ServiceRequestDirectionInbound = 100000000;

    /// <summary>The <c>sprk_requestedby</c> (→ contact) bind key. Navigation property <c>sprk_RequestedBy</c>
    /// (<c>Other/Relationships/Contact.xml</c>, relationship <c>sprk_servicerequest_RequestedBy_contact</c>).</summary>
    internal const string RequestedByBindKey = "sprk_RequestedBy@odata.bind";

    /// <summary>The standard <c>contact.department</c> column (String, 100): the requester's department.</summary>
    internal const string ContactDepartmentColumn = "department";

    /// <summary>
    /// The department on the requester's contact record, trimmed; null when the contact has none, is blank there, or does
    /// not exist. App-only Dataverse read, the same way as the other external reads (no OBO).
    /// </summary>
    /// <remarks>
    /// Owner decision 2026-10-12: the wizard does not ask for the requester's department; the submit route stores the
    /// contact's value as the system key <c>requesterDepartment</c>. The principal carries no contact attributes and the
    /// ownership resolver's contact read selects only the owning business unit and team, so there is no existing contact
    /// read to widen; this is the one added read, made after validation so a rejected submission costs nothing. A read that
    /// FAILS throws <see cref="DataverseUnavailableException"/> rather than returning null: "could not read" must not be
    /// stored as "no department".
    /// </remarks>
    /// <exception cref="DataverseUnavailableException">The read could not be completed.</exception>
    public virtual async Task<string?> GetContactDepartmentAsync(Guid contactId, CancellationToken ct = default)
    {
        if (contactId == Guid.Empty)
            throw new ArgumentException("A contact id is required.", nameof(contactId));

        var url = $"{GetApiUrl()}/contacts({contactId:D})?$select={ContactDepartmentColumn}";

        var row = await GetSingleOrThrowAsync<Dictionary<string, JsonElement>>(url, ct);
        return row is not null
               && row.TryGetValue(ContactDepartmentColumn, out var value)
               && value.ValueKind == JsonValueKind.String
               && value.GetString()?.Trim() is { Length: > 0 } department
            ? department
            : null;
    }

    /// <summary>
    /// The COMPLETE create payload for an intake-submitted <c>sprk_servicerequest</c>: the six columns the intake writes —
    /// the five of the request itself and the OWNER (task 035, owner decision 2026-10-11). Pure so a test can read it.
    /// </summary>
    /// <remarks>
    /// <para><b>The requester is the resolved caller's contact</b>, passed in by the route from the principal — never from
    /// the request (FR-25 / constraint "requester from the resolved principal"). The row carries no creator stamp:
    /// <c>sprk_servicerequest</c> has neither <c>sprk_createdbyperson</c> nor <c>sprk_createdbycontact</c>; the requester
    /// is its attribution and the Service Requests module's scope (<c>sprk_requestedby</c>).</para>
    /// <para><b>The owner is passed in, and its absence refuses</b> (<see cref="RequireOwner"/>, the same rule as the
    /// document, event and to-do creates). Owner decision 2026-10-11: an intake request is owned by the DEFAULT owner team
    /// of the business unit that owns the requester's contact, so users whose role has business-unit-level access can see
    /// it. The route resolves that team through <c>IRecordOwnershipResolver</c> (target = the requester's
    /// <c>contact</c>) BEFORE this builder runs and refuses the submission when it cannot — I-6 "refuse when unresolved"
    /// (<c>DATAVERSE-WRITE-PATH-ARCHITECTURE.md</c>). This create is app-only, so without <c>ownerid</c> Dataverse would make
    /// the BFF application user the owner, in the root business unit.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">An empty requester, a blank name, or blank intake data.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A request type outside <see cref="ServiceRequestTypes"/>.</exception>
    /// <exception cref="InvalidOperationException">An empty owning team (<see cref="RequireOwner"/>).</exception>
    internal static Dictionary<string, object?> BuildServiceRequestCreatePayload(
        Guid requesterContactId, Guid owningTeamId, int requestType, string name, string intakeDataJson)
    {
        if (requesterContactId == Guid.Empty)
            throw new ArgumentException("A service request needs the requester's contact id.", nameof(requesterContactId));
        if (!ServiceRequestTypes.ContainsKey(requestType))
            throw new ArgumentOutOfRangeException(nameof(requestType), requestType, "Not a sprk_requesttype option.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("sprk_name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(intakeDataJson))
            throw new ArgumentException("sprk_intakedata is required.", nameof(intakeDataJson));
        RequireOwner(owningTeamId, "sprk_servicerequest");

        return new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_requesttype"] = requestType,
            ["sprk_intakedata"] = intakeDataJson,
            ["sprk_direction"] = ServiceRequestDirectionInbound,
            [RequestedByBindKey] = $"/contacts({requesterContactId:D})",
            [OwnerBindKey] = $"/teams({owningTeamId})",
        };
    }

    /// <summary>
    /// Creates one <c>sprk_servicerequest</c> from a Legal Front Door submission, app-only (no OBO), and returns its id.
    /// The payload is <see cref="BuildServiceRequestCreatePayload"/>'s. A non-success response throws:
    /// <see cref="DataverseUnavailableException"/> for a transport failure, an HttpClient timeout, a 429 or a 5xx (nothing was
    /// created; retry), <see cref="InvalidOperationException"/> for any other failure.
    /// </summary>
    // `virtual` per ADR-038 §4 (substitution seam): the intake route tests record the payload and assert that a refused
    // submission never reached Dataverse.
    public virtual async Task<Guid> CreateServiceRequestAsync(
        Guid requesterContactId, Guid owningTeamId, int requestType, string name, string intakeDataJson,
        CancellationToken ct = default)
    {
        var body = BuildServiceRequestCreatePayload(requesterContactId, owningTeamId, requestType, name, intakeDataJson);

        var token = await GetAppOnlyTokenAsync(ct);

        // Echo only the id: sprk_intakedata is field-secured and the caller already holds the answers.
        var url = $"{GetApiUrl()}/sprk_servicerequests?$select=sprk_servicerequestid";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.Add("OData-MaxVersion", "4.0");
        httpRequest.Headers.Add("OData-Version", "4.0");
        httpRequest.Headers.Add("Prefer", "return=representation");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, ct);
        }
        catch (HttpRequestException ex)
        {
            // Transport failure: nothing was created. The caller reports a retryable 503 (task 035 review F2).
            throw new DataverseUnavailableException("Dataverse could not be reached to create the service request.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // The HttpClient timeout, not the caller walking away.
            throw new DataverseUnavailableException("Dataverse timed out creating the service request.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "[EXT-DATA] Create service request (type {RequestType}) failed for contact {ContactId}: {Status} — {Body}",
                requestType, requesterContactId, response.StatusCode, errorBody);

            // 429 and 5xx are Dataverse being unavailable or throttling: retryable, so a 503 upstream. Anything else
            // (a 4xx the payload earned) stays a plain failure and a 500.
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                throw new DataverseUnavailableException($"Dataverse answered {(int)response.StatusCode} creating the service request.");

            throw new InvalidOperationException($"Failed to create service request: {response.StatusCode}");
        }

        var row = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(ct);
        return row is not null && GuidValue(row, "sprk_servicerequestid") is { } id
            ? id
            : throw new InvalidOperationException("Dataverse returned no service request id after create");
    }

    /// <summary>
    /// The parent records of an invoice (<c>sprk_matter</c>, <c>sprk_project</c>), or null when the invoice does not exist
    /// (a genuine 404). App-only Dataverse read.
    /// </summary>
    /// <remarks>
    /// Task 031, owner decision 2026-10-11 (option A): an invoice's document rights are inherited from these parents, so
    /// this is the one fact that decision needs. A read that FAILS throws <see cref="DataverseUnavailableException"/>,
    /// never null — the same rule as <see cref="GetServiceRequestRequesterAsync"/>.
    /// </remarks>
    /// <exception cref="DataverseUnavailableException">The read could not be completed.</exception>
    public virtual async Task<(Guid? MatterId, Guid? ProjectId)?> GetInvoiceParentsAsync(
        Guid invoiceId, CancellationToken ct = default)
    {
        var url = $"{GetApiUrl()}/sprk_invoices({invoiceId})?$select=_sprk_matter_value,_sprk_project_value";

        var row = await GetSingleOrThrowAsync<Dictionary<string, JsonElement>>(url, ct);
        return row is null ? null : (GuidValue(row, "_sprk_matter_value"), GuidValue(row, "_sprk_project_value"));
    }

    /// <summary>
    /// An invoice's <paramref name="lookups"/> (logical names, e.g. <c>sprk_matter</c>, <c>sprk_regardingagreement</c>), read
    /// app-only, in the shape the record gate reads a record (<c>ExternalMessageEndpoints.GetRecordRightsDecisionAsync</c>):
    /// a set lookup is its id, an unset one is absent, and a value that is not a GUID is passed through as it came (the gate
    /// reads it as malformed and fails closed).
    /// </summary>
    /// <remarks>
    /// Task 187 fix round 2 (F1): the invoice document upload asks the gate the invoice thread uses, so an invoice behind a
    /// secure root's wall cannot take a file through an ordinary parent. A missing invoice throws
    /// <see cref="Sprk.Bff.Api.Services.Dataverse.RecordNotFoundException"/> (the gate's "no such record"); a read that FAILS throws
    /// <see cref="DataverseUnavailableException"/>, never an empty row.
    /// </remarks>
    /// <exception cref="DataverseUnavailableException">The read could not be completed.</exception>
    /// <exception cref="Sprk.Bff.Api.Services.Dataverse.RecordNotFoundException">There is no such invoice.</exception>
    public virtual async Task<IReadOnlyDictionary<string, object?>> GetInvoiceLookupsAsync(
        Guid invoiceId, IReadOnlyCollection<string> lookups, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lookups);
        var columns = lookups.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (columns.Count == 0 || columns.Any(c => !LookupNamePattern.IsMatch(c)))
            throw new ArgumentException("Lookup names must be logical names.", nameof(lookups));

        var url = $"{GetApiUrl()}/sprk_invoices({invoiceId})?$select={string.Join(",", columns.Select(c => $"_{c}_value"))}";
        var row = await GetSingleOrThrowAsync<Dictionary<string, JsonElement>>(url, ct)
                  ?? throw new Sprk.Bff.Api.Services.Dataverse.RecordNotFoundException("sprk_invoice", invoiceId);

        var lookupsRead = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            if (!row.TryGetValue($"_{column}_value", out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue; // not filed there
            lookupsRead[column] = value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id)
                ? id
                : value.ToString();
        }

        return lookupsRead;
    }

    /// <summary>A Dataverse logical name (what <see cref="GetInvoiceLookupsAsync"/> puts in a <c>$select</c>).</summary>
    private static readonly System.Text.RegularExpressions.Regex LookupNamePattern =
        new(@"^[a-z][a-z0-9_]*\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>This service's logger, for the endpoint-side rule reads that run over its readers (task 187 F1).</summary>
    internal ILogger Logger => _logger;

    // ---------------------------------------------------------------------------
    // In-portal notification feed (spaarke-SPA-external-access-platform-r3 task 037, FR-08)
    //
    // The feed is DERIVED from existing rows (owner 2026-10-08: no new table). These three reads supply its grant items,
    // the caller's own service requests (whose messages it reports) and the record titles. None of them decides access:
    // the feed endpoint gates every item against the caller principal's accessible set itself.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// One grant row the feed reports: the row id, its single root and when it was granted. <see cref="RenewedOn"/> is the
    /// row's <c>modifiedon</c> when the row was changed after it was created (by more than <see cref="RenewalMinGap"/>),
    /// else null. See <see cref="EventOn"/> and the renewal remarks on <see cref="GetRecentGrantsAsync"/>.
    /// </summary>
    public sealed record RecentGrant(
        Guid GrantId, ExternalGrantRootType RootType, Guid RootId, DateTimeOffset GrantedOn, DateTimeOffset? RenewedOn = null)
    {
        /// <summary>When the feed item happened: the renewal time when there is one, else the grant time.</summary>
        public DateTimeOffset EventOn => RenewedOn is { } renewed && renewed > GrantedOn ? renewed : GrantedOn;
    }

    /// <summary>
    /// How long after <c>createdon</c> a change to a grant row is still part of creating it (a take-over stamp, the
    /// Assigned-To adopt, a same-request follow-up write), so it is not announced as a renewal. A change later than this is
    /// treated as a renewal (owner decision 2026-10-12; <c>modifiedon</c> is the only renewal timestamp that exists).
    /// </summary>
    internal static readonly TimeSpan RenewalMinGap = TimeSpan.FromMinutes(2);

    /// <summary>The recent-grants read: the rows, and whether it was cut short or failed (never silent).</summary>
    public sealed record RecentGrantsRead(IReadOnlyList<RecentGrant> Grants, bool Truncated);

    /// <summary>One service request the caller requested: its id, name and requester.</summary>
    public sealed record RequestedServiceRequest(Guid Id, string? Name, Guid RequestedBy);

    /// <summary>The caller's service requests, and whether the read was cut short or failed.</summary>
    public sealed record RequestedServiceRequestsRead(IReadOnlyList<RequestedServiceRequest> Requests, bool Truncated);

    /// <summary>
    /// Record titles by id. <see cref="Complete"/> is true only when every chunk was read in full, so an id missing from
    /// <see cref="Names"/> then means the record was deleted. The read has no <c>statecode</c> filter, so a deactivated
    /// record is still returned (and kept); only a deleted one is dropped.
    /// </summary>
    public sealed record RecordNamesRead(IReadOnlyDictionary<Guid, string?> Names, bool Complete);

    /// <summary>
    /// The ACTIVE, unexpired grant rows naming <paramref name="contactId"/> as the grantee whose <c>sprk_granteddate</c>
    /// is on or after <paramref name="since"/>, app-only. Organization grants (no contact) are not included. A row whose
    /// root is not exactly one of project / matter / work assignment (e.g. its root was deleted, which empties the lookup)
    /// is skipped. <see cref="RecentGrant.GrantedOn"/> is the row's <c>createdon</c> — <c>sprk_granteddate</c> is written
    /// once, on create, as a date only — or that date at 00:00 UTC when <c>createdon</c> is unreadable.
    /// </summary>
    /// <remarks>Carries the enforcement paths' <see cref="ExternalParticipationService.ExpiryPredicate"/>, so a lapsed grant
    /// is never announced. A failed read is returned empty and flagged truncated (the collection read's honesty rule).
    /// <para><b>Renewals (owner decision 2026-10-12).</b> A grant renewed or restored IN PLACE (a lapsed one re-granted, a
    /// level raised, an expiry extended) keeps its row, its <c>createdon</c> and its <c>sprk_granteddate</c> (written once,
    /// on create), and the table has no renewed-on or expiry-changed-on column (live describe 2026-10-12). The only
    /// timestamp that moves is <c>modifiedon</c>, so when <paramref name="modifiedSince"/> is given the read also returns a
    /// row modified at or after it, and <see cref="RecentGrant.RenewedOn"/> carries <c>modifiedon</c> when it is later than
    /// <c>createdon</c> by more than <see cref="RenewalMinGap"/>. <b>Known false positive:</b> <c>modifiedon</c> also moves
    /// on a shortened expiry, a lowered level, a take-over stamp or an edit in the model-driven app; none can be told from
    /// a renewal with existing columns. The row must still be active and unexpired, so a lapsed grant is never announced.</para></remarks>
    public virtual async Task<RecentGrantsRead> GetRecentGrantsAsync(
        Guid contactId, DateOnly since, DateOnly today, CancellationToken ct = default,
        DateTimeOffset? modifiedSince = null)
    {
        if (contactId == Guid.Empty)
            return new RecentGrantsRead(Array.Empty<RecentGrant>(), false);

        var window = modifiedSince is { } modified
            ? $"(sprk_granteddate ge {since:yyyy-MM-dd} or modifiedon ge {modified.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ})"
            : $"sprk_granteddate ge {since:yyyy-MM-dd}";
        var filter = Uri.EscapeDataString(
            $"_sprk_contact_value eq {contactId} and statecode eq 0 and {window} and "
            + ExternalParticipationService.ExpiryPredicate(today));
        // When renewals are asked for, order by modifiedon: a renewed row keeps its old createdon, so a cap on the feed
        // would otherwise cut the renewals (the newest activity) and keep the oldest. modifiedon is >= createdon, so a new
        // grant still sorts by its creation.
        var orderBy = modifiedSince is null
            ? "createdon desc,sprk_externalrecordaccessid desc"
            : "modifiedon desc,sprk_externalrecordaccessid desc";
        var url = $"{GetApiUrl()}/sprk_externalrecordaccesses?$filter={filter}" +
                  "&$select=sprk_externalrecordaccessid,_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value," +
                  $"sprk_granteddate,createdon,modifiedon&$orderby={orderBy}";

        var read = await GetCollectionAsync<Dictionary<string, JsonElement>>(url, ct);
        var grants = new List<RecentGrant>();
        foreach (var row in read.Rows)
        {
            if (GuidValue(row, "sprk_externalrecordaccessid") is not { } grantId)
                continue;

            var roots = new List<(ExternalGrantRootType Type, Guid Id)>();
            if (GuidValue(row, "_sprk_project_value") is { } project) roots.Add((ExternalGrantRootType.Project, project));
            if (GuidValue(row, "_sprk_matter_value") is { } matter) roots.Add((ExternalGrantRootType.Matter, matter));
            if (GuidValue(row, "_sprk_workassignment_value") is { } wa) roots.Add((ExternalGrantRootType.WorkAssignment, wa));
            if (roots.Count != 1)
                continue;

            if (GrantedOnValue(row) is not { } grantedOn)
                continue;

            grants.Add(new RecentGrant(grantId, roots[0].Type, roots[0].Id, grantedOn, RenewedOnValue(row)));
        }

        return new RecentGrantsRead(grants, read.Truncated);
    }

    /// <summary>
    /// <c>modifiedon</c> when BOTH it and <c>createdon</c> read and the row was changed more than <see cref="RenewalMinGap"/>
    /// after it was created; else null. Needs a real <c>createdon</c> (the date fallback would make every row look renewed).
    /// </summary>
    private static DateTimeOffset? RenewedOnValue(IReadOnlyDictionary<string, JsonElement> row)
    {
        if (row.TryGetValue("createdon", out var created) && created.ValueKind == JsonValueKind.String
            && created.TryGetDateTimeOffset(out var createdOn)
            && row.TryGetValue("modifiedon", out var modified) && modified.ValueKind == JsonValueKind.String
            && modified.TryGetDateTimeOffset(out var modifiedOn)
            && modifiedOn - createdOn > RenewalMinGap)
            return modifiedOn;

        return null;
    }

    /// <summary><c>createdon</c>, else <c>sprk_granteddate</c> (either wire shape) at 00:00 UTC; null when neither reads.</summary>
    private static DateTimeOffset? GrantedOnValue(IReadOnlyDictionary<string, JsonElement> row)
    {
        if (row.TryGetValue("createdon", out var created) && created.ValueKind == JsonValueKind.String
            && created.TryGetDateTimeOffset(out var createdOn))
            return createdOn;

        if (row.TryGetValue("sprk_granteddate", out var granted) && granted.ValueKind == JsonValueKind.String
            && granted.GetString() is { Length: >= 10 } text
            && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return null;
    }

    /// <summary>
    /// The service requests whose <c>sprk_requestedby</c> is one of <paramref name="requesterIds"/>, app-only — the
    /// rows the Service Requests module would scope to the caller. The caller re-applies the module's own predicate.
    /// </summary>
    public virtual async Task<RequestedServiceRequestsRead> GetServiceRequestsRequestedByAsync(
        IReadOnlyCollection<Guid> requesterIds, CancellationToken ct = default)
    {
        var ids = requesterIds.Where(id => id != Guid.Empty).Distinct().Select(id => id.ToString("D")).ToList();
        if (ids.Count == 0)
            return new RequestedServiceRequestsRead(Array.Empty<RequestedServiceRequest>(), false);

        var read = await GetByIdsAsync<Dictionary<string, JsonElement>>(
            "sprk_servicerequests", "_sprk_requestedby_value", ids,
            "sprk_servicerequestid,sprk_name,_sprk_requestedby_value", "createdon desc", ct);

        var requests = read.Rows
            .Where(r => GuidValue(r, "sprk_servicerequestid") is not null && GuidValue(r, "_sprk_requestedby_value") is not null)
            .Select(r => new RequestedServiceRequest(
                GuidValue(r, "sprk_servicerequestid")!.Value,
                r.TryGetValue("sprk_name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null,
                GuidValue(r, "_sprk_requestedby_value")!.Value))
            .ToList();

        return new RequestedServiceRequestsRead(requests, read.Truncated);
    }

    /// <summary>
    /// The display names of the given project / matter / work-assignment records (their primary name columns, the same
    /// ones the to-do regarding stamp reads), app-only and chunked. An entity outside those three yields an empty,
    /// incomplete answer.
    /// </summary>
    public virtual async Task<RecordNamesRead> GetRecordNamesAsync(
        string entityLogicalName, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        var binding = RootBindings.Values.FirstOrDefault(
            b => string.Equals(b.EntityLogicalName, entityLogicalName, StringComparison.OrdinalIgnoreCase));
        var idList = ids.Where(id => id != Guid.Empty).Distinct().Select(id => id.ToString("D")).ToList();
        if (binding is null)
            return new RecordNamesRead(new Dictionary<Guid, string?>(), Complete: idList.Count == 0);
        if (idList.Count == 0)
            return new RecordNamesRead(new Dictionary<Guid, string?>(), Complete: true);

        var idColumn = binding.EntityLogicalName + "id";
        var read = await GetByIdsAsync<Dictionary<string, JsonElement>>(
            binding.EntitySet, idColumn, idList, $"{idColumn},{binding.DisplayNameAttribute}", $"{idColumn} asc", ct);

        var names = new Dictionary<Guid, string?>();
        foreach (var row in read.Rows)
        {
            if (GuidValue(row, idColumn) is { } id)
            {
                names[id] = row.TryGetValue(binding.DisplayNameAttribute, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
            }
        }

        return new RecordNamesRead(names, Complete: !read.Truncated);
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
        TodoRootKind rootKind, Guid rootId, CallerPrincipal caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var url = BuildTodoListUrl(GetApiUrl(), rootKind, rootId);

        // Task 105: paged to the cap, and a cut-short list says so (see GetDocumentsAsync).
        var read = await GetCollectionAsync<TodoRow>(url, ct);

        // Fix round for task 039: a to-do under an invoice of secure matter M and project P is stamped with BOTH roots, so
        // the project route used to list M's to-do to a reader of P alone. Listed only if the caller holds Read on every
        // SECURE root the to-do carries (ExternalMultiRootRule, owner option B); a single-root to-do lists exactly as before.
        // Task 187 (owner round 99, D-123): the stamp is a COPY, so the to-do's parents (the communication, document, event,
        // invoice ... it is filed under) are read too and their LIVE roots join the rule: a carrier to-do (its pair names
        // ordinary M, its sprk_regardingcommunication a communication under secure X) and a stale copy are withheld.
        var live = await ReadLiveParentRootsAsync(read.Rows.Select(r => ExternalMultiRootRule.LiveParentsOf(TodoTable, r.Extra)), ct);
        var roots = read.Rows
            .Select(r => ChildRowRoots.Of(
                TodoTable, r.Extra,
                ExternalMultiRootRule.Stamps(
                    r.SprkRegardingprojectValue, r.SprkRegardingmatterValue, r.SprkRegardingworkassignmentValue),
                live))
            .ToList();
        var flags = await ReadSecureRootFlagsAsync(caller, roots.Select(r => (IEnumerable<(ExternalGrantRootType, string?)>)r), ct);
        var readable = read.Rows.Where((_, i) => ExternalMultiRootRule.ChildRowReadable(caller, flags, roots[i])).ToList();
        if (readable.Count != read.Rows.Count)
        {
            _logger.LogInformation(
                "[EXT-DATA] todos: {Withheld} of {Total} to-do(s) regarding {RootKind} {RootId} withheld - also filed to a secure record the caller cannot read",
                read.Rows.Count - readable.Count, read.Rows.Count, rootKind, rootId);
        }

        return new ExternalCollectionResponse<ExternalTodoDto>
        {
            Value = readable.Select(MapTodo).ToList(),
            Truncated = read.Truncated || RuleReadCutListShort(flags, live, read.Rows.Count - readable.Count),
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
                     CreatorStampSelect + // task 032 (FR-26): the SPA offers Delete only on the caller's own to-dos
                     LiveParentSelectSuffix(TodoTable); // task 187: the parents whose live roots the rule takes in
        var filter = Uri.EscapeDataString($"{binding.LookupValueAttribute} eq {rootId}");

        // Task 105: no $top (it suppresses @odata.nextLink); the primary key breaks due-date ties so pages never overlap.
        return $"{apiUrl}/sprk_todos?$filter={filter}&$select={select}&$orderby=sprk_duedate asc,sprk_todoid asc";
    }

    /// <summary>
    /// Retrieves the <c>sprk_event</c> records regarding one accessible root (project, matter or work assignment), minus
    /// any also filed to a root the <paramref name="caller"/> cannot read.
    /// </summary>
    /// <remarks>
    /// <para>CALENDAR events, not to-dos. The filter column comes from <see cref="TodoRootBinding"/>, the table the
    /// to-do list and the delete-own check already share, so the three reads cannot name different columns for the
    /// same root. <c>sprk_event</c> carries the same three lookups as <c>sprk_todo</c> (<c>sprk_regardingproject</c>,
    /// <c>sprk_regardingmatter</c>, <c>sprk_regardingworkassignment</c>; live metadata, unified-access-control-r2 task
    /// 159 notes section 0.2).</para>
    /// <para>The legacy <c>sprk_todoflag</c> attribute is deliberately NOT selected. smart-todo-decoupling-r3 FR-29
    /// retired the event-as-todo model; to-dos live on <c>sprk_todo</c> and are served by <see cref="GetTodosAsync"/>.</para>
    /// <para><b>Task 039 (FR-03) generalized this from project-only.</b> The caller has ALREADY been authorized for
    /// <paramref name="rootId"/> (Read, from <c>RightsForRoot</c>); <paramref name="caller"/> is here only for the
    /// second half of the rule, <see cref="ReadableEvents"/>.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rootKind"/> is <c>None</c> or <c>Ambiguous</c>:
    /// deny states, not roots.</exception>
    // `virtual` per ADR-038 section 4 (substitution seam): the endpoint tests assert WHICH root was asked for and that a
    // denied list never reached Dataverse. BuildEventsQuery is tested directly for the $filter this seam would hide.
    public virtual async Task<ExternalCollectionResponse<ExternalEventDto>> GetEventsAsync(
        TodoRootKind rootKind, Guid rootId, CallerPrincipal caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var url = $"{GetApiUrl()}/{BuildEventsQuery(rootKind, rootId)}";

        // Task 105: paged to the cap, and a cut-short list says so (see GetDocumentsAsync).
        var read = await GetCollectionAsync<EventRow>(url, ct);
        var (readable, flags, live) = await ReadableEventsAsync(read.Rows, caller, ct);
        if (readable.Count != read.Rows.Count)
        {
            _logger.LogInformation(
                "[EXT-DATA] events: {Withheld} of {Total} event(s) regarding {RootKind} {RootId} withheld - also filed to a secure record the caller cannot read",
                read.Rows.Count - readable.Count, read.Rows.Count, rootKind, rootId);
        }

        return new ExternalCollectionResponse<ExternalEventDto>
        {
            Value = readable.Select(MapEvent).ToList(),
            Truncated = read.Truncated || RuleReadCutListShort(flags, live, read.Rows.Count - readable.Count),
        };
    }

    /// <summary>
    /// The events of <paramref name="rows"/> the caller may see: an event is listed only if the caller holds Read on every
    /// SECURE root it is filed to (<c>sprk_regardingproject</c>, <c>sprk_regardingmatter</c>,
    /// <c>sprk_regardingworkassignment</c>); owner decision 2026-10-12, option B.
    /// </summary>
    /// <remarks>
    /// <para>Without it an event filed to a secure matter AND to a project the caller can read is returned by the project
    /// route to a caller who holds nothing on the matter: the matter's isolation (its rights come only from a direct grant)
    /// would be bypassed by a second lookup. A row naming no other root is unaffected, so every single-parent event lists
    /// exactly as before. The rule itself (fail-closed) is <see cref="ExternalMultiRootRule"/>, shared with the to-do,
    /// document, message and feed lists. The delete-own rule (<c>AuthorizeDeleteOwnAsync</c>) stays "Read on EVERY parent"
    /// (FR-26).</para>
    /// </remarks>
    private async Task<(List<EventRow> Rows, SecureRootFlags Flags, LiveParentRoots Live)> ReadableEventsAsync(
        IReadOnlyList<EventRow> rows, CallerPrincipal caller, CancellationToken ct)
    {
        // Task 187: the stamped roots plus the live roots of the event's parents (see GetTodosAsync).
        var live = await ReadLiveParentRootsAsync(rows.Select(r => ExternalMultiRootRule.LiveParentsOf(EventTable, r.Extra)), ct);
        var roots = rows
            .Select(r => ChildRowRoots.Of(
                EventTable, r.Extra,
                ExternalMultiRootRule.Stamps(
                    r.SprkRegardingprojectValue, r.SprkRegardingmatterValue, r.SprkRegardingworkassignmentValue),
                live))
            .ToList();
        var flags = await ReadSecureRootFlagsAsync(caller, roots.Select(r => (IEnumerable<(ExternalGrantRootType, string?)>)r), ct);
        return (rows.Where((_, i) => ExternalMultiRootRule.ChildRowReadable(caller, flags, roots[i])).ToList(), flags, live);
    }

    /// <summary>
    /// The secure flags the multi-root rule needs for a set of rows (<see cref="ExternalMultiRootRule.ReadFlagsAsync"/> over
    /// <see cref="GetNonSecureRootsAsync"/>): one app-only read for the distinct linked roots the caller cannot read, none
    /// when there are none. Fails closed (see the rule).
    /// </summary>
    public Task<SecureRootFlags> ReadSecureRootFlagsAsync(
        CallerPrincipal caller,
        IEnumerable<IEnumerable<(ExternalGrantRootType Root, string? LookupValue)>> rowRoots,
        CancellationToken ct = default) =>
        ExternalMultiRootRule.ReadFlagsAsync(GetNonSecureRootsAsync, caller, rowRoots, _logger, ct);

    /// <summary>
    /// Task 187: the live roots of the distinct parents a set of rows carries
    /// (<see cref="ExternalMultiRootRule.ReadLiveParentsAsync"/> over <see cref="GetLiveParentRootsAsync"/>): one bounded,
    /// batched walk for the whole page, none when no row names a parent. Fails closed (see the rule).
    /// </summary>
    public Task<LiveParentRoots> ReadLiveParentRootsAsync(
        IEnumerable<IEnumerable<LiveParentLookup>> rowParents, CancellationToken ct = default) =>
        ExternalMultiRootRule.ReadLiveParentsAsync(GetLiveParentRootsAsync, rowParents, _logger, ct);

    /// <summary>
    /// Task 188 (D7, #2126): THE document narrowing (<see cref="ExternalDocumentNarrowing"/>) for document rows that passed
    /// the root rule, read app-only. An internal systemuser (CallerPrincipal.SystemUserId set and
    /// <see cref="Sprk.Bff.Api.Services.Identity.ISystemUserIdentityResolver.IsExternalAsync"/> false) is not narrowed, the
    /// 065 rule's own exemption; every other caller is. A failed read hides every row (<see cref="DocumentNarrowingResult.Faulted"/>).
    /// The one seam every external document surface narrows through (the list, the single-document routes and the documents
    /// module).
    /// </summary>
    internal async Task<DocumentNarrowingResult> NarrowDocumentsAsync(
        CallerPrincipal caller, IReadOnlyCollection<DocumentNarrowingRow> rows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            return DocumentNarrowingResult.NotNarrowed(rows);
        if (await IsInternalSystemUserAsync(caller, ct))
            return DocumentNarrowingResult.NotNarrowed(rows);
        var narrowing = await ExternalDocumentNarrowing.DecideOrFailClosedAsync(rows, QueryRowsOrThrowAsync, _logger, ct);
        LogNarrowingCost("external", rows.Count, rows.Count(row => !narrowing.IsVisible(row)), narrowing);
        return narrowing;
    }

    /// <summary>
    /// Task 188 (main-session decision 2026-10-20): the assistant's <c>dataverse.*</c> tools' document narrowing
    /// (<see cref="AppOnlyDocumentNarrowing"/>). The documents are re-read BY ID and decided APP-ONLY through the same reader as
    /// the external plane, so a column field security masks for the user can never read as "no link". The tool has already
    /// decided the caller is external. A failed read THROWS (the tool call fails with no data); a document that does not come
    /// back is not visible. Returns the decision only.
    /// <para>Task 188 round 3 slice B: <paramref name="plane"/> labels the cost line — <see cref="AssistantPlane"/> for
    /// <see cref="AppOnlyDocumentNarrowing"/> (the assistant's tools and the AI retrieval trim), <see cref="InternalRoutesPlane"/>
    /// for <see cref="InternalRouteDocumentNarrowing"/> (the internal plane's document routes).</para>
    /// </summary>
    internal async Task<DocumentNarrowingResult> ReadAndNarrowDocumentsAsync(
        IReadOnlyCollection<Guid> documentIds, CancellationToken ct, string plane = AssistantPlane)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        var narrowing = await ExternalDocumentNarrowing.ReadAndDecideAsync(documentIds, QueryRowsOrThrowAsync, ct);
        var distinct = documentIds.Distinct().ToList();
        LogNarrowingCost(plane, distinct.Count, distinct.Count(id => !narrowing.IsVisible(id)), narrowing);
        return narrowing;
    }

    /// <summary>The cost line's plane for <see cref="AppOnlyDocumentNarrowing"/>.</summary>
    internal const string AssistantPlane = "assistant";

    /// <summary>The cost line's plane for <see cref="InternalRouteDocumentNarrowing"/> (task 188 round 3 slice B).</summary>
    internal const string InternalRoutesPlane = "internal-routes";

    /// <summary>Reads above which a request's narrowing cost line is logged at Information (Debug otherwise).</summary>
    internal const int NarrowingCostInformationThreshold = 20;

    /// <summary>
    /// The ONE narrowing cost line per request (task 188): rows checked, rows hidden, hop levels read and requests per set.
    /// Counts only (ADR-015). Information when the reads exceed <see cref="NarrowingCostInformationThreshold"/>.
    /// </summary>
    private void LogNarrowingCost(string plane, int rows, int hidden, DocumentNarrowingResult narrowing)
    {
        var cost = narrowing.Cost;
        var level = cost.Reads > NarrowingCostInformationThreshold ? LogLevel.Information : LogLevel.Debug;
        if (!_logger.IsEnabled(level))
            return;
        _logger.Log(level,
            "[EXT-NARROWING] cost plane={Plane} rows={Rows} hidden={Hidden} faulted={Faulted} hopLevels={HopLevels} "
            + "documentReads={DocumentReads} communicationReads={CommunicationReads} threadReads={ThreadReads}",
            plane, rows, hidden, narrowing.Faulted, cost.HopLevels,
            cost.DocumentReads, cost.CommunicationReads, cost.ThreadReads);
    }

    /// <summary>
    /// Task 188: the single-document routes' narrowing (download, versions): whether <paramref name="document"/>, already
    /// known to be readable under the route parent, survives <see cref="NarrowDocumentsAsync"/>. A <c>false</c> answers the
    /// route's one "not part of this record" refusal, before any SPE pointer is resolved.
    /// </summary>
    public async Task<bool> IsDocumentVisibleAsync(
        CallerPrincipal caller, Guid documentId, DocumentScopeRead document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var row = ExternalDocumentNarrowing.FromWebApi(documentId, document.Roots.Row);
        var narrowing = await NarrowDocumentsAsync(caller, new[] { row }, ct);
        if (narrowing.IsVisible(row))
            return true;

        _logger.LogInformation(
            "[EXT-DATA] document {DocumentId} hidden by the document narrowing ({Reasons})",
            documentId, narrowing.HiddenSummary(new[] { row }));
        return false;
    }

    /// <summary>
    /// ebilling-r1 task 098: THE invoice (a′) predicate (<see cref="ExternalInvoiceVisibility"/>) for <paramref name="caller"/>,
    /// read app-only. An internal systemuser (the same test as <see cref="NarrowDocumentsAsync"/>) hides nothing; every other
    /// caller — a CIAM contact, a flagged systemuser, an unresolvable one, one whose lookup FAILED (unknown is never internal)
    /// — gets the predicate. A failed read hides every asked invoice (<see cref="InvoiceVisibilityResult.Faulted"/>). Bound to
    /// <see cref="ExternalInvoiceHiding"/> by <c>AddExternalAccess</c>, with the effective root flags it needs.
    /// </summary>
    internal async Task<InvoiceVisibilityResult> HideInvoicesForAsync(
        CallerPrincipal caller, IReadOnlyCollection<Guid> invoiceIds, EffectiveRootFlagReader rootFlags, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(invoiceIds);
        ArgumentNullException.ThrowIfNull(rootFlags);
        var asked = invoiceIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (asked.Count == 0 || await IsInternalSystemUserAsync(caller, ct))
            return InvoiceVisibilityResult.NotHidden(asked);
        try
        {
            var decision = await ExternalInvoiceVisibility.DecideAsync(asked, InvoiceVisibilityReadersOver(rootFlags), ct);
            LogInvoiceVisibilityCost("external", asked.Count, decision);
            return decision;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-INVOICE-VISIBILITY] The (a') read for {Count} invoice(s) failed. Failing CLOSED: none of them is shown.",
                asked.Count);
            return InvoiceVisibilityResult.ReadFailed(asked);
        }
    }

    /// <summary>
    /// ebilling-r1 task 098 review round 4b: THE decision of the external submission routes (list and single read) for
    /// <paramref name="caller"/>, read app-only (<see cref="ExternalInvoiceVisibility.DecideSubmissionsAsync"/>): with an
    /// invoice, the invoice predicate; without one, the submission's own source. Keyed by submission id. An internal systemuser
    /// (the same test as <see cref="HideInvoicesForAsync"/>) sees every one and causes no read; every other caller gets the
    /// decision. A failed read withholds every asked submission (<see cref="InvoiceVisibilityResult.Faulted"/>). Bound to
    /// <see cref="ExternalInvoiceSubmissionHiding"/> by <c>AddExternalAccess</c>, with the effective root flags it needs.
    /// </summary>
    internal async Task<InvoiceVisibilityResult> HideSubmissionsForAsync(
        CallerPrincipal caller, IReadOnlyCollection<Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView> submissions,
        EffectiveRootFlagReader rootFlags, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(submissions);
        ArgumentNullException.ThrowIfNull(rootFlags);
        var asked = submissions.Where(s => s is not null && s.SubmissionId != Guid.Empty).Select(s => s.SubmissionId).Distinct().ToList();
        if (asked.Count == 0 || await IsInternalSystemUserAsync(caller, ct))
            return InvoiceVisibilityResult.NotHidden(asked);
        try
        {
            var decision = await ExternalInvoiceVisibility.DecideSubmissionsAsync(submissions, InvoiceVisibilityReadersOver(rootFlags), ct);
            LogInvoiceVisibilityCost("external-submissions", asked.Count, decision);
            return decision;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-INVOICE-VISIBILITY] The (a') read for {Count} submission(s) failed. Failing CLOSED: none of them is shown.",
                asked.Count);
            return InvoiceVisibilityResult.ReadFailed(asked);
        }
    }

    /// <summary>
    /// ebilling-r1 task 098 surface (d): the assistant tools' taint closure (<see cref="AppOnlyInvoiceFamilyHiding"/>): of
    /// <paramref name="rowIds"/> (rows of an invoice-family table), the ids hidden because their invoice is. The caller is
    /// already known to be external. Read app-only; a failed read THROWS (the tool call fails with no data).
    /// </summary>
    internal async Task<IReadOnlySet<Guid>> HideInvoiceFamilyRowsAsync(
        string table, IReadOnlyCollection<Guid> rowIds, EffectiveRootFlagReader rootFlags, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rowIds);
        ArgumentNullException.ThrowIfNull(rootFlags);
        var hidden = await ExternalInvoiceVisibility.HiddenFamilyRowsAsync(table, rowIds, InvoiceVisibilityReadersOver(rootFlags), ct);
        _logger.LogDebug("[EXT-INVOICE-VISIBILITY] cost plane={Plane} table={Table} rows={Rows} hidden={Hidden}",
            AssistantPlane, table, rowIds.Count, hidden.Count);
        return hidden;
    }

    /// <summary>
    /// The predicate's readers, every one app-only: this service's OData reader, 188's decision with the marker-aware rule 2
    /// (the core decision is called only here and in <see cref="ExternalDocumentNarrowing"/>, the 188 guard's app-only rule),
    /// the live-parent walk and <paramref name="rootFlags"/>.
    /// </summary>
    private InvoiceVisibilityReaders InvoiceVisibilityReadersOver(EffectiveRootFlagReader rootFlags) => new(
        QueryRowsOrThrowAsync,
        (rows, ct) => ExternalDocumentNarrowing.DecideAsync(rows, QueryRowsOrThrowAsync, ct, honourSubmissionMarker: true),
        GetLiveParentRootsAsync,
        rootFlags,
        _logger);

    /// <summary>The ONE (a′) cost line per request: invoices checked and hidden, reads per level. Counts only (ADR-015).</summary>
    private void LogInvoiceVisibilityCost(string plane, int invoices, InvoiceVisibilityResult decision)
    {
        var cost = decision.Cost;
        var level = cost.Reads > NarrowingCostInformationThreshold ? LogLevel.Information : LogLevel.Debug;
        if (!_logger.IsEnabled(level))
            return;
        _logger.Log(level,
            "[EXT-INVOICE-VISIBILITY] cost plane={Plane} invoices={Invoices} hidden={Hidden} ({Reasons}) sources={Sources} "
            + "storedFileReads={StoredFileReads} submissionReads={SubmissionReads} sourceReads={SourceReads} "
            + "narrowingReads={NarrowingReads} rootFlagReads={RootFlagReads}",
            plane, invoices, decision.Hidden.Count, decision.HiddenSummary(), cost.Sources,
            cost.StoredFileReads, cost.SubmissionReads, cost.SourceReads, cost.NarrowingReads, cost.RootFlagReads);
    }

    /// <summary>
    /// Whether <paramref name="caller"/> is an internal systemuser. A contact, a systemuser flagged external, an unknown one
    /// and a lookup that fails are all NOT internal (the narrowing applies: under-deliver, never leak).
    /// </summary>
    private async Task<bool> IsInternalSystemUserAsync(CallerPrincipal caller, CancellationToken ct)
    {
        if (_identity is null || caller.SystemUserId is not { } systemUserId || systemUserId == Guid.Empty)
            return false;
        try
        {
            return !await _identity.IsExternalAsync(systemUserId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[EXT-DATA] The internal/external lookup for systemuser {SystemUserId} failed; the document narrowing applies.",
                systemUserId);
            return false;
        }
    }

    /// <summary>
    /// Task 188: the app-only OData reader the document narrowing (and the 065 rule inside it) reads through. A 404 or any
    /// other failure THROWS (<see cref="DataverseUnavailableException"/>), so a fault never reads as "no rows".
    /// </summary>
    private async Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryRowsOrThrowAsync(
        string entitySet, string odata, CancellationToken ct)
    {
        var page = await GetSingleOrThrowAsync<ODataResult<Dictionary<string, JsonElement>>>(
            $"{GetApiUrl()}/{entitySet}?{odata}", ct);
        return page?.Value ?? throw new DataverseUnavailableException($"The {entitySet} read returned no collection.");
    }

    /// <summary>
    /// The walk's query count above which one request's live-parent read is logged at Information (task 187 fix round 2,
    /// K1). A usual page costs one query per parent table; a full 5,000-row page of rows each under its own communication and
    /// thread costs 50; the hard ceiling is 250 (the notes' "Query cost").
    /// </summary>
    internal const int WalkQueriesWorthReporting = 50;

    /// <summary>One request's live-parent walk cost: Debug always, Information once it passes
    /// <see cref="WalkQueriesWorthReporting"/>. Counts only.</summary>
    private void LogWalkCost(int parents, int queries, int records)
    {
        if (queries > WalkQueriesWorthReporting)
        {
            _logger.LogInformation(
                "[EXT-LIVEPARENT] Walk cost: {Queries} filing queries for {Records} records ({Parents} parents) in one request.",
                queries, records, parents);
            return;
        }

        _logger.LogDebug(
            "[EXT-LIVEPARENT] Walk cost: {Queries} filing queries for {Records} records ({Parents} parents).",
            queries, records, parents);
    }

    /// <summary>
    /// Task 187 (owner round 99, D-123; #1857): of <paramref name="parents"/> (child-table records a row is filed under), the
    /// LIVE roots at the top of each one's filing, read APP-ONLY.
    /// </summary>
    /// <remarks>
    /// <para><b>The walk.</b> The ONE lineage map (<see cref="Sprk.Bff.Api.Services.Dataverse.ParentLineage.ChildFiling"/>, the
    /// lookups <c>SecureChildReconciler</c>'s secure-if-any follows), climbed by
    /// <see cref="Sprk.Bff.Api.Services.Dataverse.ParentLineageWalk.TopsOfManyAsync"/>: level by level, one query per parent
    /// table per level (<c>id IN (…)</c>, chunked by 200), selecting only that table's lineage lookups. A project, matter or
    /// work assignment ends the climb without a read: whether it is (effectively) secure is the secure-flag read's to decide
    /// (<see cref="GetNonSecureRootsAsync"/>), which runs once over the union.</para>
    /// <para><b>Bounded, fail closed.</b> At most <see cref="ExternalMultiRootRule.MaxLiveParentReads"/> records and
    /// <see cref="Sprk.Bff.Api.Services.Dataverse.ParentLineageWalk.MaxDepth"/> levels. A parent whose filing reaches a record
    /// that faulted, did not come back or lies past a bound is left out of the answer (unresolved), and every row filed under
    /// it is withheld. No filing reader means nothing is resolved.</para>
    /// <para><c>virtual</c> per ADR-038 section 4 (substitution seam); the tests drive the real walk over a fake reader.</para>
    /// </remarks>
    public virtual async Task<LiveParentRootsRead> GetLiveParentRootsAsync(
        IReadOnlyCollection<(string Table, Guid Id)> parents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parents);
        var empty = new Dictionary<(string Table, Guid Id), IReadOnlyList<(ExternalGrantRootType Root, Guid Id)>>();
        if (parents.Count == 0)
            return new LiveParentRootsRead(empty);
        if (_filing is null)
        {
            _logger.LogError(
                "[EXT-LIVEPARENT] No filing reader is configured; {Count} parent(s) cannot be resolved (fail closed).", parents.Count);
            return new LiveParentRootsRead(empty, Faulted: true);
        }

        var walk = new Sprk.Bff.Api.Services.Dataverse.ParentLineageWalk(_filing, _columnProbe, Array.Empty<string>());
        var (tops, faulted) = await walk
            .TopsOfManyAsync(parents, Sprk.Bff.Api.Services.Access.SecureChildLineage.IsRoot, ExternalMultiRootRule.MaxLiveParentReads, ct)
            .ConfigureAwait(false);
        LogWalkCost(parents.Count, walk.QueriesSent, walk.RecordsAsked);

        var resolved = new Dictionary<(string Table, Guid Id), IReadOnlyList<(ExternalGrantRootType Root, Guid Id)>>();
        var undetermined = 0;
        foreach (var (parent, answer) in tops)
        {
            if (answer.Status != Sprk.Bff.Api.Services.Dataverse.ParentTopsStatus.Found)
            {
                undetermined++;
                continue;
            }

            resolved[parent] = answer.Tops
                .Where(t => Sprk.Bff.Api.Services.Access.SecureChildLineage.IsRoot(t.Table))
                .Select(t => (RootTypeOf(t.Table), t.Id))
                .Distinct()
                .ToList();
        }

        if (undetermined > 0)
        {
            _logger.LogWarning(
                "[EXT-LIVEPARENT] {Undetermined} of {Count} parent(s) have a filing that could not be decided (faulted={Faulted}); "
                + "rows filed under them are withheld.", undetermined, tops.Count, faulted);
        }

        return new LiveParentRootsRead(resolved, faulted);

        static ExternalGrantRootType RootTypeOf(string table) =>
            Enum.GetValues<ExternalGrantRootType>()
                .First(t => string.Equals(ExternalGrantRoot.LogicalNameFor(t), table, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Task 187: the answer of <see cref="CheckLiveParentsAsync"/>.</summary>
    public enum LiveParentDecision
    {
        /// <summary>The zero value on purpose: an unset decision refuses.</summary>
        Withheld = 0,

        /// <summary>The caller holds Read on every SECURE root of the row's stamps and of its parents' live roots.</summary>
        Readable = 1,

        /// <summary>A read the decision needed failed: nothing may be written, and the route says "try again".</summary>
        Unavailable = 2,
    }

    /// <summary>
    /// Task 187: the single-row form of the live-parent rule, for the to-do PATCH and the to-do / event / document
    /// delete-own routes: reads the row's stamps and parent lookups, resolves the parents' live roots and the secure flags,
    /// and decides with <see cref="ExternalMultiRootRule.ChildRowReadable"/>. A row that is absent is
    /// <see cref="LiveParentDecision.Withheld"/> (the routes' own checks already refuse it); a read that failed is
    /// <see cref="LiveParentDecision.Unavailable"/>.
    /// </summary>
    /// <remarks>
    /// <c>virtual</c> per ADR-038 section 4 (substitution seam). A to-do's and an event's stamps are its three
    /// <c>sprk_regarding*</c> roots; a document's are its six root links (<see cref="DocumentRootLinks"/>, owner decision
    /// D3 of fix round 1), the same input the document list, download and versions decide over.
    /// </remarks>
    public virtual async Task<LiveParentDecision> CheckLiveParentsAsync(
        DeletableKind kind, Guid id, CallerPrincipal caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var binding = GetDeletableBinding(kind);
        if (kind is not (DeletableKind.Todo or DeletableKind.Event or DeletableKind.Document))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The live-parent check covers to-dos, events and documents.");

        Dictionary<string, JsonElement>? row;
        try
        {
            row = await GetSingleOrThrowAsync<Dictionary<string, JsonElement>>(BuildLiveParentCheckUrl(GetApiUrl(), kind, id), ct);
        }
        catch (DataverseUnavailableException)
        {
            return LiveParentDecision.Unavailable;
        }

        if (row is null)
            return LiveParentDecision.Withheld;

        var stamps = kind == DeletableKind.Document
            ? DocumentRootLinks.From(row).Links
            : ExternalMultiRootRule.Stamps(
                RawLookup(row, "_sprk_regardingproject_value"), RawLookup(row, "_sprk_regardingmatter_value"),
                RawLookup(row, "_sprk_regardingworkassignment_value"));
        var live = await ReadLiveParentRootsAsync(new[] { ExternalMultiRootRule.LiveParentsOf(binding.EntityLogicalName, row) }, ct);
        var roots = ChildRowRoots.Of(binding.EntityLogicalName, row, stamps, live);
        var flags = await ReadSecureRootFlagsAsync(caller, new[] { (IEnumerable<(ExternalGrantRootType, string?)>)roots }, ct);
        if (ExternalMultiRootRule.ChildRowReadable(caller, flags, roots))
            return LiveParentDecision.Readable;

        // Withheld for its roots, or only because a read could not decide: a write route answers the second as unavailable.
        return live.Faulted || !flags.IsComplete ? LiveParentDecision.Unavailable : LiveParentDecision.Withheld;

        static string? RawLookup(IReadOnlyDictionary<string, JsonElement> r, string column) =>
            !r.TryGetValue(column, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : v.ValueKind == JsonValueKind.String ? v.GetString() : ExternalMultiRootRule.UnresolvedParent;
    }

    /// <summary>
    /// The single-row read <see cref="CheckLiveParentsAsync"/> sends: the stamps (a to-do's or event's three regarding roots,
    /// a document's six root links) and every live-parent lookup. Pure.
    /// </summary>
    internal static string BuildLiveParentCheckUrl(string apiUrl, DeletableKind kind, Guid id)
    {
        var binding = GetDeletableBinding(kind);
        IEnumerable<string> stamps = kind == DeletableKind.Document
            ? DocumentRootLinks.SelectColumns
            : new[] { "_sprk_regardingproject_value", "_sprk_regardingmatter_value", "_sprk_regardingworkassignment_value" };
        var columns = stamps
            .Concat(ExternalMultiRootRule.LiveParentSelect(binding.EntityLogicalName))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return $"{apiUrl}/{binding.EntitySet}({id})?$select={string.Join(",", columns)}";
    }

    /// <summary>
    /// Of <paramref name="roots"/>, the ones whose EFFECTIVE secure status, read APP-ONLY, is not secure (owner decision
    /// 2026-10-12, option B: only a SECURE linked root the caller cannot read withholds a row; main-session decision: secure
    /// means effectively secure, task 174's rule).
    /// </summary>
    /// <remarks>
    /// <para><b>Own flag.</b> One query per root entity per <see cref="IdFilterChunkSize"/> ids
    /// (<see cref="GetByIdsAsync{TRow}"/>, an <c>or</c> filter on the ids), selecting only the id and <c>sprk_issecure</c>. All
    /// three roots carry the column (live metadata, <c>SecureRecordRoot</c>).</para>
    /// <para><b>Effective flag.</b> A work assignment or project whose own flag is <c>false</c> is still secure when anything it
    /// is filed under is effectively secure (a child whose own flag has not caught up). Those ids, per table, go through the
    /// ONE filing walk (<see cref="EffectiveRootFlags.ReadAncestryAsync"/>, batched level by level and bounded by
    /// <see cref="SecureRootInheritance.MaxFilingDepth"/>) and its fold (<see cref="EffectiveRootFlags.Fold(IReadOnlyDictionary{Guid, RootRecordFlags}, IReadOnlyDictionary{Guid, SecureParentsAnswer}?)"/>).
    /// A matter files under nothing; its own flag is its effective flag (no walk).</para>
    /// <para><b>Fail closed.</b> Only an explicit JSON <c>false</c> on a returned row whose id was asked for, and then an
    /// ancestry the walk decided with no secure ancestor, opens a root. A failed chunk, a record not returned (missing, or
    /// invisible to the app identity), an absent or null <c>sprk_issecure</c> (a masked field-secured value reads empty), any
    /// non-boolean value, an ancestor read that faults or is missing or empty, a chain past the bound and a missing filing
    /// reader all leave the root out of the answer, so it counts as secure. A named filing parent the walk is not given back
    /// is Unknown too (<c>absentParentIsUnknown</c>, the read path's opt-in; the write-time walk is unchanged). No cache: a
    /// staff change to the flag applies on the next request.</para>
    /// <para><b>Complete.</b> The answer says whether the read COMPLETED (<see cref="NonSecureRootsRead.Complete"/>): every
    /// flag chunk succeeded, every asked root came back with a row and a boolean flag, and every walk decided. When it did not, a root may count as secure only because it could not be
    /// read; the document, event and to-do lists that withheld a row then set <c>Truncated</c>.</para>
    /// <para><c>virtual</c> per ADR-038 section 4 (substitution seam); the tests drive the real read over a fake Web API.</para>
    /// </remarks>
    public virtual async Task<NonSecureRootsRead> GetNonSecureRootsAsync(
        IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)> roots, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var notSecure = new HashSet<(ExternalGrantRootType Root, Guid Id)>();
        var complete = true;
        foreach (var group in roots.Where(r => r.Id != Guid.Empty).Distinct().GroupBy(r => r.Root))
        {
            var idColumn = ExternalGrantRoot.LogicalNameFor(group.Key) + "id";
            var asked = group.Select(r => r.Id).ToHashSet();
            var read = await GetByIdsAsync<Dictionary<string, JsonElement>>(
                ExternalGrantRoot.BindFor(group.Key).EntitySet, idColumn,
                asked.Select(id => id.ToString("D")).ToList(),
                $"{idColumn},{SecureFlagColumn}", $"{idColumn} asc", ct);

            var ownOpen = new HashSet<Guid>();
            var resolved = new HashSet<Guid>(); // asked ids that came back with a row AND a boolean flag
            foreach (var row in read.Rows)
            {
                if (GuidValue(row, idColumn) is { } id && asked.Contains(id)
                    && row.TryGetValue(SecureFlagColumn, out var flag)
                    && flag.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    resolved.Add(id);
                    if (flag.ValueKind == JsonValueKind.False)
                    {
                        ownOpen.Add(id);
                    }
                }
            }

            // F4: a chunk can succeed and still leave an asked root unresolved (no row: missing or invisible to the app
            // identity; no sprk_issecure: field security; a non-boolean value). It still counts as secure, but the rows it
            // withholds must say Truncated, so the read is not complete.
            if (resolved.Count < asked.Count)
            {
                complete = false;
            }

            if (read.Outcome != CollectionReadOutcome.Complete)
            {
                _logger.LogWarning(
                    "[EXT-DATA] secure-flag read for {Count} {Root} id(s) was incomplete ({Outcome}); the unread ones count as secure.",
                    asked.Count, group.Key, read.Outcome);
                complete = false;
            }

            if (ownOpen.Count == 0)
            {
                continue;
            }

            // Effective status (task 174's rule): the ONE filing walk, batched for the own-open ids of this table; null for a
            // matter (files under nothing). The fold makes an id with no decided ancestry secure. Owner policy (review of
            // 039 option B): a named filing parent the walk cannot find is Unknown, not "files under nothing"
            // (absentParentIsUnknown), so it folds to secure on this read path; the write-time walk keeps its default.
            var ancestry = await EffectiveRootFlags.ReadAncestryAsync(
                _filing, _logger, ExternalGrantRoot.LogicalNameFor(group.Key), ownOpen, ct, absentParentIsUnknown: true);
            var effective = EffectiveRootFlags.Fold(ownOpen.ToDictionary(id => id, _ => RootRecordFlags.None), ancestry);
            foreach (var (id, flags) in effective)
            {
                if (flags.IsUnreadable)
                {
                    complete = false; // a walk fault, an unreadable or missing ancestor, an empty ancestor flag, past the bound
                }

                if (!flags.IsSecure)
                {
                    notSecure.Add((group.Key, id));
                }
            }
        }

        return new NonSecureRootsRead(notSecure, complete);
    }

    /// <summary>The root records' secure flag (all three roots carry it).</summary>
    private const string SecureFlagColumn = "sprk_issecure";

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
    /// The relative <c>sprk_events</c> query <see cref="GetEventsAsync"/> sends (task 097: live column names; task 039: any
    /// accessible root). Internal for tests.
    /// </summary>
    internal static string BuildEventsQuery(TodoRootKind rootKind, Guid rootId)
    {
        var binding = TryGetRootBinding(rootKind)
            ?? throw new ArgumentOutOfRangeException(nameof(rootKind), rootKind,
                "Events can only be listed for a project, matter or work assignment. None/Ambiguous are deny states - a "
                + "caller reaching here with one has skipped its authorization gate.");

        // Task 032 (FR-26): both creator stamps, so the SPA offers Delete only on the caller's own events.
        // Task 039: all three root lookups, so a row also filed to a root the caller cannot read can be withheld.
        var select = $"sprk_eventid,{EventNameColumn},sprk_duedate,{EventStatusColumn},createdon,"
                     + "_sprk_regardingproject_value,_sprk_regardingmatter_value,_sprk_regardingworkassignment_value,"
                     + CreatorStampSelect
                     + LiveParentSelectSuffix(EventTable); // task 187: the parents whose live roots the rule takes in
        var filter = Uri.EscapeDataString($"{binding.LookupValueAttribute} eq {rootId}");
        // Task 105: no $top (it suppresses @odata.nextLink); the primary key breaks due-date ties so pages never overlap.
        return $"sprk_events?$filter={filter}&$select={select}&$orderby=sprk_duedate asc,sprk_eventid asc";
    }

    /// <summary>The project form of <see cref="BuildEventsQuery(TodoRootKind, Guid)"/> (the shipped query, plus the two extra lookups).</summary>
    internal static string BuildEventsQuery(Guid projectId) => BuildEventsQuery(TodoRootKind.Project, projectId);

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
    /// <para><b>Documents</b> use the task-031 parent lookups (<see cref="DocumentParents"/>), the same ones the
    /// external list, download and upload routes scope by — including <c>sprk_invoice</c> since the owner's option A
    /// (2026-10-11), whose Read is inherited from the invoice's parents.</para>
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
            // Fix round for task 039: plus the related twin of each root (sprk_relatedproject / _relatedmatter /
            // _relatedworkassignment), the same six root links the list, download and versions apply the multi-root rule
            // to. The delete itself stays Read on EVERY parent (FR-26), not the secure-roots rule.
            [DeletableKind.Document] = new(DeletableKind.Document, "sprk_document", "sprk_documents",
                DocumentParents.Values.Select(b => new DeletableParentLookup(b.Kind, b.LookupValueAttribute))
                    .Concat(new[]
                    {
                        new DeletableParentLookup(DocumentParentKind.Project, "_sprk_relatedproject_value"),
                        new DeletableParentLookup(DocumentParentKind.Matter, "_sprk_relatedmatter_value"),
                        new DeletableParentLookup(DocumentParentKind.WorkAssignment, "_sprk_relatedworkassignment_value"),
                    })
                    .ToList()),
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
    /// <remarks>
    /// <paramref name="HasUnreadableParentLink"/> (fix round for task 039): a parent lookup carried a value that is not a
    /// GUID. The delete is refused (fail closed): the row names a parent the caller's rights cannot be checked against.
    /// </remarks>
    public sealed record DeleteTarget(
        Guid? CreatedByPerson, Guid? CreatedByContact, IReadOnlyList<DeleteTargetParent> Parents,
        bool HasUnreadableParentLink = false);

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
        var unreadable = false;
        foreach (var lookup in GetDeletableBinding(kind).Parents)
        {
            if (GuidValue(row, lookup.LookupValueAttribute) is { } parentId)
                parents.Add(new DeleteTargetParent(lookup.Parent, parentId));
            else if (row.TryGetValue(lookup.LookupValueAttribute, out var raw)
                     && raw.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                     && !(raw.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(raw.GetString())))
                unreadable = true; // present, but not a GUID
        }

        return new DeleteTarget(
            GuidValue(row, CreatedByPersonValueAttribute),
            GuidValue(row, CreatedByContactValueAttribute),
            parents,
            unreadable);
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
    /// <c>src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_ServiceRequest.xml</c>, 9 relationships, all
    /// <c>CascadeDelete=RemoveLink</c>): deleting the request would orphan every row listed here, so ANY row (active or not)
    /// blocks the delete. A relationship added to the solution must be added here; the pinned test
    /// <c>TheRelationshipList_IsExactlyTheSolutionsOneToManyRelationships</c> reads the solution XML and fails until it is.
    /// </summary>
    internal static readonly IReadOnlyList<ServiceRequestRelation> ServiceRequestRelations = new ServiceRequestRelation[]
    {
        new("sprk_analysis", "sprk_analysises", "sprk_regardingservicerequest"),
        new("sprk_communication", "sprk_communications", "sprk_regardingservicerequest"),
        new("sprk_communicationthread", "sprk_communicationthreads", "sprk_regardingservicerequest"),
        new("sprk_decisionrecord", "sprk_decisionrecords", "sprk_regardingservicerequest"),
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
    /// All nine probes run; a probe that FAILS (non-success status of any kind, transport error, token failure) makes the
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
    /// it. The one access decision made from it is the secure-flag read (<see cref="GetNonSecureRootsAsync"/>), and only in
    /// the closing direction: a row it did not read leaves its root counted as secure.</para>
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
            request.Headers.Add("Prefer", CollectionPrefer);
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
        // The choice LABEL when a value is set and Dataverse supplied its label; otherwise null — never the number.
        SprkDocumenttype = r.SprkDocumenttype is not null && !string.IsNullOrWhiteSpace(r.SprkDocumenttypeLabel)
            ? r.SprkDocumenttypeLabel
            : null,
        SprkSummary = r.SprkSummary,
        SprkProjectidValue = r.SprkProjectidValue,
        Createdon = r.Createdon,
        SprkCreatedbypersonValue = r.CreatedByPersonValue,
        SprkCreatedbycontactValue = r.CreatedByContactValue,
        // R3 ISS-011 (#1886): the same truth the download and versions routes use (DocumentStorageResolver.GetSpePointersAsync:
        // a usable drive id AND item id; sprk_hasfile is a hint that can be stale). Only the boolean leaves the BFF.
        HasFile = Sprk.Bff.Api.Infrastructure.Dataverse.DocumentStorageResolver.HasUsablePointers(r.SprkGraphdriveid, r.SprkGraphitemid),
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
