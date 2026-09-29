using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;

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
    }

    private sealed class ProjectRow
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
    }

    private sealed class EventRow
    {
        [JsonPropertyName("sprk_eventid")] public string? SprkEventid { get; set; }
        [JsonPropertyName("sprk_name")] public string? SprkName { get; set; }
        [JsonPropertyName("sprk_duedate")] public string? SprkDuedate { get; set; }
        [JsonPropertyName("sprk_status")] public int? SprkStatus { get; set; }
        [JsonPropertyName("createdon")] public string? Createdon { get; set; }
        [JsonPropertyName("_sprk_regardingproject_value")] public string? SprkRegardingprojectValue { get; set; }
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
    public async Task<IReadOnlyList<ExternalProjectDto>> GetProjectsAsync(
        IEnumerable<Guid> projectIds, CancellationToken ct = default)
    {
        var ids = projectIds.ToList();
        if (ids.Count == 0) return [];

        var select = "sprk_projectid,sprk_projectname,sprk_projectnumber,sprk_projectdescription,sprk_issecure,statecode,createdon,modifiedon";
        var idFilter = string.Join(" or ", ids.Select(id => $"sprk_projectid eq {id}"));
        // H5 (task 022, 2026-08-24): $orderby said `sprk_name`, which does NOT exist on sprk_project
        // (live metadata: the display name is sprk_projectname — the $select above already had it right).
        // Dataverse answered 400, GetCollectionAsync caught it and returned an empty list, so the
        // external SPA rendered "you have no grants" for every caller WITH grants. Sixth instance of the
        // stale-column class in this project; the select/orderby split is why it survived review.
        var url = $"{GetApiUrl()}/sprk_projects?$filter={Uri.EscapeDataString(idFilter)}&$select={select}&$orderby=sprk_projectname asc";

        var rows = await GetCollectionAsync<ProjectRow>(url, ct);
        return rows.Select(MapProject).ToList();
    }

    /// <summary>Retrieves a single project by ID.</summary>
    public async Task<ExternalProjectDto?> GetProjectByIdAsync(Guid projectId, CancellationToken ct = default)
    {
        var select = "sprk_projectid,sprk_projectname,sprk_projectnumber,sprk_projectdescription,sprk_issecure,statecode,createdon,modifiedon";
        var url = $"{GetApiUrl()}/sprk_projects({projectId})?$select={select}";

        var row = await GetSingleAsync<ProjectRow>(url, ct);
        return row is null ? null : MapProject(row);
    }

    // ---------------------------------------------------------------------------
    // Document queries
    // ---------------------------------------------------------------------------

    /// <summary>Retrieves all documents belonging to the specified project.</summary>
    public async Task<IReadOnlyList<ExternalDocumentDto>> GetDocumentsAsync(Guid projectId, CancellationToken ct = default)
    {
        var select = "sprk_documentid,sprk_documentname,sprk_documenttype,sprk_filesummary,_sprk_project_value,createdon";
        var filter = Uri.EscapeDataString($"_sprk_project_value eq {projectId}");
        var url = $"{GetApiUrl()}/sprk_documents?$filter={filter}&$select={select}&$orderby=createdon desc&$top=200";

        var rows = await GetCollectionAsync<DocumentRow>(url, ct);
        return rows.Select(MapDocument).ToList();
    }

    /// <summary>
    /// Creates a <c>sprk_document</c> row for a file already uploaded to SPE, linked to the project.
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
    /// <para><c>sprk_Project@odata.bind</c> uses the PascalCase navigation property, the same
    /// case-sensitive form the event and to-do creates use. The read side of this service filters
    /// documents on <c>_sprk_project_value</c>, so this is the lookup that makes an uploaded document
    /// visible in the SPA's own list.</para>
    /// </remarks>
    public virtual async Task<ExternalDocumentDto> CreateDocumentAsync(
        Guid projectId, ExternalUploadedFilePointers pointers, CancellationToken ct = default)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project id must be a non-empty GUID.", nameof(projectId));
        ArgumentNullException.ThrowIfNull(pointers);

        var token = await GetAppOnlyTokenAsync(ct);

        var body = new Dictionary<string, object?>
        {
            ["sprk_documentname"] = pointers.FileName,
            ["sprk_filename"] = pointers.FileName,
            ["sprk_graphitemid"] = pointers.ItemId,
            ["sprk_graphdriveid"] = pointers.DriveId,
            ["sprk_Project@odata.bind"] = $"/sprk_projects({projectId})",
        };

        if (pointers.FileSizeBytes.HasValue)
            body["sprk_filesize"] = pointers.FileSizeBytes.Value;
        if (!string.IsNullOrWhiteSpace(pointers.WebUrl))
            body["sprk_filepath"] = pointers.WebUrl;

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
                "[EXT-DATA] Create document failed for project {ProjectId}: {Status} — {Body}",
                projectId, response.StatusCode, errorBody);
            throw new InvalidOperationException($"Failed to create document: {response.StatusCode}");
        }

        var row = await response.Content.ReadFromJsonAsync<DocumentRow>(ct);
        if (row is null)
            throw new InvalidOperationException("Dataverse returned no document data after create");

        return MapDocument(row);
    }

    /// <summary>
    /// Returns the document's parent project id and display name — used by the external download
    /// endpoint (task 027) for document→project authorization scoping BEFORE any SPE pointer
    /// resolution or Graph content read. App-only Dataverse read; returns (null, null) when the
    /// document does not exist. Does NOT expose any Graph pointer (driveId/itemId).
    /// </summary>
    public virtual async Task<(Guid? ProjectId, string? DocumentName)> GetDocumentProjectAndNameAsync(
        Guid documentId, CancellationToken ct = default)
    {
        var select = "sprk_documentname,_sprk_project_value";
        var url = $"{GetApiUrl()}/sprk_documents({documentId})?$select={select}";

        var row = await GetSingleAsync<DocumentRow>(url, ct);
        if (row is null) return (null, null);

        var projectId = Guid.TryParse(row.SprkProjectidValue, out var pid) ? pid : (Guid?)null;
        return (projectId, row.SprkName);
    }

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
    /// read. Deliberately mirrors <see cref="GetDocumentProjectAndNameAsync"/> (task 027), which
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
    public virtual async Task<IReadOnlyList<ExternalTodoDto>> GetTodosAsync(
        TodoRootKind rootKind, Guid rootId, CancellationToken ct = default)
    {
        var url = BuildTodoListUrl(GetApiUrl(), rootKind, rootId);

        var rows = await GetCollectionAsync<TodoRow>(url, ct);
        return rows.Select(MapTodo).ToList();
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
                     "sprk_regardingrecordid,sprk_regardingrecordname,sprk_regardingrecordurl";
        var filter = Uri.EscapeDataString($"{binding.LookupValueAttribute} eq {rootId}");

        return $"{apiUrl}/sprk_todos?$filter={filter}&$select={select}&$orderby=sprk_duedate asc&$top=200";
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
    public async Task<IReadOnlyList<ExternalEventDto>> GetEventsAsync(Guid projectId, CancellationToken ct = default)
    {
        var select = "sprk_eventid,sprk_name,sprk_duedate,sprk_status,createdon,_sprk_regardingproject_value";
        var filter = Uri.EscapeDataString($"_sprk_regardingproject_value eq {projectId}");
        var url = $"{GetApiUrl()}/sprk_events?$filter={filter}&$select={select}&$orderby=sprk_duedate asc&$top=200";

        var rows = await GetCollectionAsync<EventRow>(url, ct);
        return rows.Select(MapEvent).ToList();
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
    public async Task<ExternalEventDto> CreateEventAsync(
        Guid projectId, CreateExternalEventRequest request, CancellationToken ct = default)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project id must be a non-empty GUID.", nameof(projectId));

        var token = await GetAppOnlyTokenAsync(ct);

        var body = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(request.SprkName))
            body["sprk_name"] = request.SprkName;
        if (request.SprkDuedate is not null)
            body["sprk_duedate"] = request.SprkDuedate;
        if (request.SprkStatus.HasValue)
            body["sprk_status"] = request.SprkStatus.Value;

        // R5 002: PascalCase nav prop (metadata-verified) — same binding the to-do create uses.
        body["sprk_RegardingProject@odata.bind"] = $"/sprk_projects({projectId})";

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
    /// </remarks>
    public virtual async Task<ExternalTodoDto> CreateTodoAsync(
        TodoRootKind rootKind, Guid rootId, CreateExternalTodoRequest request, CancellationToken ct = default)
    {
        if (rootId == Guid.Empty)
            throw new ArgumentException("Root id must be a non-empty GUID.", nameof(rootId));

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
        var body = BuildTodoCreatePayload(request, binding, rootId, rootDisplayName, recordTypeRef?.Id);

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
        Guid? recordTypeRefId)
    {
        var body = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(request.SprkName))
            body["sprk_name"] = request.SprkName;
        if (!string.IsNullOrEmpty(request.SprkNotes))
            body["sprk_notes"] = request.SprkNotes;
        if (request.SprkDuedate is not null)
            body["sprk_duedate"] = request.SprkDuedate;
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

        var body = new Dictionary<string, object?>();
        if (request.SprkName is not null) body["sprk_name"] = request.SprkName;
        if (request.SprkNotes is not null) body["sprk_notes"] = request.SprkNotes;
        if (request.SprkDuedate is not null) body["sprk_duedate"] = request.SprkDuedate;
        if (request.SprkPriorityscore.HasValue) body["sprk_priorityscore"] = request.SprkPriorityscore.Value;
        if (request.SprkEffortscore.HasValue) body["sprk_effortscore"] = request.SprkEffortscore.Value;
        if (request.SprkTodocolumn.HasValue) body["sprk_todocolumn"] = request.SprkTodocolumn.Value;
        if (request.SprkTodopinned.HasValue) body["sprk_todopinned"] = request.SprkTodopinned.Value;
        if (request.Statuscode.HasValue) body["statuscode"] = request.Statuscode.Value;

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
    // Contact and organization queries
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Retrieves contacts with active access to the specified project.
    /// Queries sprk_externalrecordaccess to find contact IDs, then fetches contact details.
    /// </summary>
    public async Task<IReadOnlyList<ExternalContactDto>> GetContactsAsync(Guid projectId, CancellationToken ct = default)
    {
        // Step 1: Get contact IDs from the access junction table
        var contactIds = await GetProjectContactIdsAsync(projectId, ct);
        if (contactIds.Count == 0) return [];

        // Step 2: Fetch contact details for those IDs
        var select = "contactid,fullname,firstname,lastname,emailaddress1,telephone1,jobtitle,_parentcustomerid_value";
        var idFilter = string.Join(" or ", contactIds.Select(id => $"contactid eq {id}"));
        var url = $"{GetApiUrl()}/contacts?$filter={Uri.EscapeDataString(idFilter)}&$select={select}&$orderby=fullname asc";

        var rows = await GetCollectionAsync<ContactRow>(url, ct);
        return rows.Select(MapContact).ToList();
    }

    /// <summary>
    /// Retrieves organizations (accounts) linked to the project via project contacts.
    /// </summary>
    public async Task<IReadOnlyList<ExternalOrganizationDto>> GetOrganizationsAsync(Guid projectId, CancellationToken ct = default)
    {
        // Step 1: Get contacts for the project
        var contacts = await GetContactsAsync(projectId, ct);

        // Step 2: Collect unique account IDs from contacts
        var accountIds = contacts
            .Where(c => !string.IsNullOrEmpty(c.ParentcustomeridValue))
            .Select(c => c.ParentcustomeridValue!)
            .Distinct()
            .ToList();

        if (accountIds.Count == 0) return [];

        // Step 3: Fetch account details
        var select = "accountid,name,websiteurl,telephone1,address1_city,address1_country";
        var idFilter = string.Join(" or ", accountIds.Select(id => $"accountid eq {id}"));
        var url = $"{GetApiUrl()}/accounts?$filter={Uri.EscapeDataString(idFilter)}&$select={select}&$orderby=name asc";

        var rows = await GetCollectionAsync<AccountRow>(url, ct);
        return rows.Select(MapOrganization).ToList();
    }

    // ---------------------------------------------------------------------------
    // ADR-024 Resolver-field application (Web API path)
    //
    // Mirrors Sprk.Bff.Api.Services.Workspace.TodoRegardingBuilder.ApplyResolverFieldsAsync
    // but operates over the Dataverse Web API (no ServiceClient eager connect). When
    // sprk_recordtype_ref cannot be resolved, the type field is left unset and a warning
    // is logged — non-fatal, mirroring the SDK-path behaviour (correctness intact; only
    // the cross-entity-view icon is lost).
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

            var filter = Uri.EscapeDataString($"sprk_recordentitylogicalname eq '{entityLogicalName}'");
            var url = $"{GetApiUrl()}/sprk_recordtype_refs?$filter={filter}" +
                      "&$select=sprk_recordtype_refid,sprk_recorddisplayname&$top=1";

            var rows = await GetCollectionAsync<RecordTypeRefRow>(url, ct);
            var row = rows.FirstOrDefault();
            if (row?.Id is not null && Guid.TryParse(row.Id, out var refId))
            {
                var entry = (
                    Id: refId,
                    DisplayName: row.DisplayName ?? entityLogicalName
                );
                _recordTypeRefCache[entityLogicalName] = entry;
                return entry;
            }

            _recordTypeRefCache[entityLogicalName] = null;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[EXT-DATA] Failed to query sprk_recordtype_ref for '{Entity}'. Caching negative result.",
                entityLogicalName);
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
    /// </remarks>
    private async Task<IReadOnlyList<string>> GetProjectContactIdsAsync(Guid projectId, CancellationToken ct)
    {
        var expiry = ExternalParticipationService.ExpiryPredicate(DateOnly.FromDateTime(DateTime.UtcNow));
        var filter = Uri.EscapeDataString(
            $"_sprk_project_value eq {projectId} and statecode eq 0 and {expiry}");
        var url = $"{GetApiUrl()}/sprk_externalrecordaccesses?$filter={filter}&$select=_sprk_contact_value&$top=200";

        var rows = await GetCollectionAsync<AccessLinkRow>(url, ct);
        return rows
            .Where(r => !string.IsNullOrEmpty(r.ContactId))
            .Select(r => r.ContactId!)
            .Distinct()
            .ToList();
    }

    private async Task<IReadOnlyList<TRow>> GetCollectionAsync<TRow>(string url, CancellationToken ct)
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
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-DATA] GET collection failed: {Status} — {Url}", response.StatusCode, url);
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<ODataResult<TRow>>(ct);
            return result?.Value ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-DATA] Error fetching collection: {Url}", url);
            return [];
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

    private static ExternalProjectDto MapProject(ProjectRow r) => new()
    {
        SprkProjectid = r.SprkProjectid ?? "",
        SprkName = r.SprkName ?? "",
        SprkReferencenumber = r.SprkReferencenumber,
        SprkDescription = r.SprkDescription,
        SprkIssecure = r.SprkIssecure,
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
    };

    private static ExternalEventDto MapEvent(EventRow r) => new()
    {
        SprkEventid = r.SprkEventid ?? "",
        SprkName = r.SprkName ?? "",
        SprkDuedate = r.SprkDuedate,
        SprkStatus = r.SprkStatus,
        Createdon = r.Createdon,
        SprkRegardingprojectValue = r.SprkRegardingprojectValue,
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
