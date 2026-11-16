using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

// ---------------------------------------------------------------------------
// Collection response wrapper
// ---------------------------------------------------------------------------

/// <summary>
/// OData-style collection response envelope returned by all list endpoints.
/// The SPA reads the <c>value</c> array (camelCase) from collection responses.
/// </summary>
public sealed class ExternalCollectionResponse<T>
{
    [JsonPropertyName("value")]
    public IReadOnlyList<T> Value { get; init; } = [];

    /// <summary>
    /// <c>true</c> when <see cref="Value"/> is known to be INCOMPLETE: the read stopped at its row cap with more rows
    /// remaining, or a later page failed after earlier pages were read (unified-access-control-r2 task 105, NFR-03 —
    /// a cap is never silent). Omitted from the JSON when <c>false</c>, so a complete list is byte-for-byte what it was
    /// before the field existed; clients read a missing field as <c>false</c>.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Truncated { get; init; }

    /// <summary>
    /// Whether the caller may create a child of this list's kind (a to-do, an event, a message) under the record the list
    /// is scoped to: the answer that record's create route would give (its workforce ceiling, then its Create check,
    /// confirmed against Dataverse for a caller whose access Dataverse decides). First written by unified-access-control-r2
    /// task 036 fix round 1 (F4) for Dataverse-decided callers only; since spaarke-SPA-external-access-platform-r4 task 002
    /// (FR-06) the to-do, event and message lists of a project, matter or work assignment (and the message lists of an
    /// invoice and a service request) write it for EVERY caller, because <c>/me</c> carries no per-record right on a matter
    /// or work assignment. An unanswered Dataverse question is <c>false</c>. Omitted (<c>null</c>) on every other list.
    /// A UI affordance only: the route still decides every create.
    /// </summary>
    [JsonPropertyName("canCreate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CanCreate { get; init; }

    /// <summary>
    /// The event lists of a project, matter or work assignment only (spaarke-SPA-external-access-platform-r4 task 008 review
    /// round 1, FR-07): whether the create route would admit an event with <c>eventType: "task"</c> here. It is
    /// <see cref="CanCreate"/> AND, for a caller whose access Dataverse decides, AppendTo on the Task row of
    /// <c>sprk_eventtype_ref</c>, asked through the same check the create route makes (uac-r2 B1). For a contact it equals
    /// <see cref="CanCreate"/> (the closed set is the authorization). An unanswered Dataverse question is <c>false</c>.
    /// Omitted (<c>null</c>) on every other list. A UI affordance only: the route still decides every create.
    /// </summary>
    [JsonPropertyName("canCreateTask")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CanCreateTask { get; init; }

    /// <summary>
    /// The event lists of a project, matter or work assignment only (spaarke-SPA-external-access-platform-r4 task 014b, FR-35;
    /// owner Q-1, uac-r2 Q3): the event types the create route would accept from this caller here — every ACTIVE
    /// <c>sprk_eventtype_ref</c> row, narrowed by <c>ExternalAccess:EventTypes:Allowed</c> when set, and for a caller whose
    /// access Dataverse decides, only the rows Dataverse grants it AppendTo on. Empty when <see cref="CanCreate"/> is false or
    /// the types could not be read. Omitted (<c>null</c>) on every other list. A UI affordance only: the route still decides.
    /// </summary>
    [JsonPropertyName("creatableEventTypes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ExternalEventTypeOptionDto>? CreatableEventTypes { get; init; }
}

/// <summary>One event type the caller may pick on create (R4 task 014b, FR-35): the type row's id and name, and whether an
/// event of that type needs a due date (<c>sprk_requiredduedate</c>).</summary>
public sealed class ExternalEventTypeOptionDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("requiresDueDate")]
    public bool RequiresDueDate { get; init; }
}

/// <summary>
/// One person an event on a record may be assigned to (R4 task 014b, FR-37; uac-r2 Q2.7): a contact with effective external
/// access to the record now. Name and id only — never an email or a UPN. <c>GET /{root}/{id}/assignable-people</c> is the
/// ONLY external route that returns contact ids for this purpose.
/// </summary>
public sealed class ExternalAssignablePersonDto
{
    [JsonPropertyName("contactId")]
    public string ContactId { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The caller's own contact.</summary>
    [JsonPropertyName("isMe")]
    public bool IsMe { get; init; }
}

// ---------------------------------------------------------------------------
// Project DTO
// ---------------------------------------------------------------------------

public sealed class ExternalProjectDto
{
    [JsonPropertyName("sprk_projectid")]
    public string SprkProjectid { get; init; } = "";

    [JsonPropertyName("sprk_name")]
    public string SprkName { get; init; } = "";

    [JsonPropertyName("sprk_referencenumber")]
    public string? SprkReferencenumber { get; init; }

    [JsonPropertyName("sprk_description")]
    public string? SprkDescription { get; init; }

    [JsonPropertyName("sprk_issecure")]
    public bool? SprkIssecure { get; init; }

    [JsonPropertyName("sprk_status")]
    public int? SprkStatus { get; init; }

    [JsonPropertyName("createdon")]
    public string? Createdon { get; init; }

    [JsonPropertyName("modifiedon")]
    public string? Modifiedon { get; init; }
}

// ---------------------------------------------------------------------------
// Document list envelope
// ---------------------------------------------------------------------------

/// <summary>
/// The document list for one parent record: the same <c>value</c> / <c>truncated</c> members as
/// <see cref="ExternalCollectionResponse{T}"/> (same names, same order, same omission rule) plus <see cref="CanCreate"/>
/// (spaarke-SPA-external-access-platform-r3 task 054 review fix).
/// </summary>
public sealed class ExternalDocumentListResponse
{
    [JsonPropertyName("value")]
    public IReadOnlyList<ExternalDocumentDto> Value { get; init; } = [];

    /// <summary>See <see cref="ExternalCollectionResponse{T}.Truncated"/>.</summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Truncated { get; init; }

    /// <summary>
    /// <c>true</c> when the caller holds <c>Create</c> on this parent, i.e. the same right the upload route checks, so an
    /// Upload control can be offered only to a caller who can use it. Always written (a missing member would read as
    /// "cannot" to a client). The caller has just passed the Read check on this parent, so it discloses nothing new; the
    /// upload route still decides every upload.
    /// </summary>
    [JsonPropertyName("canCreate")]
    public bool CanCreate { get; init; }
}

// ---------------------------------------------------------------------------
// Document DTO
// ---------------------------------------------------------------------------

public sealed class ExternalDocumentDto
{
    [JsonPropertyName("sprk_documentid")]
    public string SprkDocumentid { get; init; } = "";

    [JsonPropertyName("sprk_name")]
    public string SprkName { get; init; } = "";

    [JsonPropertyName("sprk_documenttype")]
    public string? SprkDocumenttype { get; init; }

    [JsonPropertyName("sprk_summary")]
    public string? SprkSummary { get; init; }

    [JsonPropertyName("_sprk_projectid_value")]
    public string? SprkProjectidValue { get; init; }

    [JsonPropertyName("createdon")]
    public string? Createdon { get; init; }

    /// <summary>The systemuser creator stamp (<c>sprk_createdbyperson</c>, task 030). With
    /// <see cref="SprkCreatedbycontactValue"/> it lets the SPA offer Delete only on the caller's own rows (FR-26,
    /// task 054); the server re-checks every delete.</summary>
    [JsonPropertyName("_sprk_createdbyperson_value")]
    public string? SprkCreatedbypersonValue { get; init; }

    /// <summary>The contact creator stamp (<c>sprk_createdbycontact</c>, task 030). See
    /// <see cref="SprkCreatedbypersonValue"/>.</summary>
    [JsonPropertyName("_sprk_createdbycontact_value")]
    public string? SprkCreatedbycontactValue { get; init; }

    /// <summary>
    /// <c>true</c> when the document has a file the download and versions routes can serve; <c>false</c> when it has none
    /// (no usable SPE pointers), so the SPA lists the row without Download or Versions (R3 ISS-011, #1886, owner decision
    /// 2026-10-14). Always written. A boolean only: the pointers themselves never leave the BFF. The routes still refuse
    /// a no-file request on their own (409 <c>no_file_attached</c>).
    /// </summary>
    [JsonPropertyName("hasFile")]
    public bool HasFile { get; init; }

    /// <summary>
    /// ebilling-r1 task 130-0 fix round 1: the invoices this row links to (<c>InvoiceLinks</c>), so the route can hide a row whose
    /// invoice the caller may not see. Server-side only: internal and <see cref="JsonIgnoreAttribute"/>, never on the wire.
    /// Task 130-0 fix round 2 (K1): null means the links were not read, and the route hides the row (fail closed by type); a row with
    /// no invoice link carries an empty list.
    /// </summary>
    [JsonIgnore]
    internal IReadOnlyList<Guid>? InvoiceLinks { get; init; }
}

// ---------------------------------------------------------------------------
// To Do DTOs (replaces former Event+todoflag DTOs per smart-todo-decoupling-r3)
//
// Old contract (R2 and earlier): event records with a legacy to-do flag boolean.
// New contract (R3+): first-class sprk_todo entity with multi-entity regarding
// per ADR-024. For the external-access surface, todos are project-scoped, so
// the regarding lookup exposed here is _sprk_regardingproject_value plus the
// four resolver fields (sprk_regardingrecordtype, sprk_regardingrecordid,
// sprk_regardingrecordname, sprk_regardingrecordurl).
//
// Breaking change: see projects/smart-todo-decoupling-r3/notes/
// external-access-contract-change.md for the migration guide consumed by
// the external-spa update (task 008).
// ---------------------------------------------------------------------------

/// <summary>
/// Read-side projection of a <c>sprk_todo</c> for the external SPA.
/// Mirrors the <c>sprk_todo</c> shape defined in
/// <c>src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md</c>.
/// </summary>
/// <remarks>
/// Per ADR-024, the regarding context is exposed as the project-specific lookup
/// (<c>_sprk_regardingproject_value</c>) plus the four denormalized resolver
/// fields. NO polymorphic <c>regardingobjectid</c> lookup is exposed.
/// </remarks>
public sealed class ExternalTodoDto
{
    [JsonPropertyName("sprk_todoid")]
    public string SprkTodoid { get; init; } = "";

    [JsonPropertyName("sprk_name")]
    public string SprkName { get; init; } = "";

    [JsonPropertyName("sprk_notes")]
    public string? SprkNotes { get; init; }

    /// <summary>The to-do due date (<c>sprk_todo.sprk_duedate</c>) as Dataverse returns it: a calendar date, <c>yyyy-MM-dd</c>,
    /// once the environment's column is Date Only (task 106); a UTC timestamp before that (not normalised here — before
    /// the conversion its UTC date is not always the picked day). Read it with <c>parseDueDate</c>, never
    /// <c>new Date(value)</c>.</summary>
    [JsonPropertyName("sprk_duedate")]
    public string? SprkDuedate { get; init; }

    [JsonPropertyName("sprk_priorityscore")]
    public int? SprkPriorityscore { get; init; }

    [JsonPropertyName("sprk_effortscore")]
    public int? SprkEffortscore { get; init; }

    [JsonPropertyName("sprk_todocolumn")]
    public int? SprkTodocolumn { get; init; }

    [JsonPropertyName("sprk_todopinned")]
    public bool? SprkTodopinned { get; init; }

    /// <summary>Dataverse statecode (0 = Active, 1 = Inactive).</summary>
    [JsonPropertyName("statecode")]
    public int? Statecode { get; init; }

    /// <summary>
    /// Dataverse statuscode (1 = Open, 659490001 = In Progress, 2 = Completed,
    /// 659490002 = Dismissed — per smart-todo-decoupling-r3 FR-24).
    /// </summary>
    [JsonPropertyName("statuscode")]
    public int? Statuscode { get; init; }

    [JsonPropertyName("createdon")]
    public string? Createdon { get; init; }

    /// <summary>Project the to-do is regarding (<c>sprk_regardingproject</c> lookup value).</summary>
    [JsonPropertyName("_sprk_regardingproject_value")]
    public string? SprkRegardingprojectValue { get; init; }

    /// <summary>
    /// Resolver field — GUID of the regarding parent record (lower-case "D" format).
    /// Populated atomically alongside the specific regarding lookup per ADR-024.
    /// </summary>
    [JsonPropertyName("sprk_regardingrecordid")]
    public string? SprkRegardingrecordid { get; init; }

    /// <summary>Resolver field — display name of the regarding parent record.</summary>
    [JsonPropertyName("sprk_regardingrecordname")]
    public string? SprkRegardingrecordname { get; init; }

    /// <summary>Resolver field — clickable model-driven-app record URL (relative; host resolved at click time).</summary>
    [JsonPropertyName("sprk_regardingrecordurl")]
    public string? SprkRegardingrecordurl { get; init; }

    /// <summary>The systemuser creator stamp (<c>sprk_createdbyperson</c>, task 030): with
    /// <see cref="SprkCreatedbycontactValue"/>, what the SPA uses to offer Delete only on the caller's own to-dos
    /// (FR-26, task 054); the server re-checks every delete.</summary>
    [JsonPropertyName("_sprk_createdbyperson_value")]
    public string? SprkCreatedbypersonValue { get; init; }

    /// <summary>The contact creator stamp (<c>sprk_createdbycontact</c>, task 030).</summary>
    [JsonPropertyName("_sprk_createdbycontact_value")]
    public string? SprkCreatedbycontactValue { get; init; }

    /// <summary>
    /// ebilling-r1 task 130-0 fix round 1: the invoices this row links to (<c>InvoiceLinks</c>), so the route can hide a row whose
    /// invoice the caller may not see. Server-side only: internal and <see cref="JsonIgnoreAttribute"/>, never on the wire.
    /// Task 130-0 fix round 2 (K1): null means the links were not read, and the route hides the row (fail closed by type); a row with
    /// no invoice link carries an empty list.
    /// </summary>
    [JsonIgnore]
    internal IReadOnlyList<Guid>? InvoiceLinks { get; init; }
}

/// <summary>
/// Request body from the SPA for creating a new <c>sprk_todo</c> on a project.
/// The regarding context (<c>sprk_regardingproject</c> + four resolver fields)
/// is applied server-side using the project from the route, per ADR-024.
/// </summary>
public sealed class CreateExternalTodoRequest
{
    [JsonPropertyName("sprk_name")]
    public string SprkName { get; init; } = "";

    [JsonPropertyName("sprk_notes")]
    public string? SprkNotes { get; init; }

    /// <summary>The due date as a calendar date, <c>yyyy-MM-dd</c> (task 106: Dataverse Date Only refuses a timestamp). A
    /// timestamp <c>yyyy-MM-ddT…</c> from an earlier client build is read as its leading ten characters; anything else is 400.</summary>
    [JsonPropertyName("sprk_duedate")]
    public string? SprkDuedate { get; init; }

    [JsonPropertyName("sprk_priorityscore")]
    public int? SprkPriorityscore { get; init; }

    [JsonPropertyName("sprk_effortscore")]
    public int? SprkEffortscore { get; init; }

    [JsonPropertyName("sprk_todocolumn")]
    public int? SprkTodocolumn { get; init; }

    [JsonPropertyName("sprk_todopinned")]
    public bool? SprkTodopinned { get; init; }
}

/// <summary>
/// Request body from the SPA for updating an existing <c>sprk_todo</c> (PATCH semantics —
/// only provided fields are changed). Regarding context cannot be changed via this surface.
/// </summary>
public sealed class UpdateExternalTodoRequest
{
    [JsonPropertyName("sprk_name")]
    public string? SprkName { get; init; }

    [JsonPropertyName("sprk_notes")]
    public string? SprkNotes { get; init; }

    /// <summary>The due date as a calendar date, <c>yyyy-MM-dd</c> (task 106: Dataverse Date Only refuses a timestamp). A
    /// timestamp <c>yyyy-MM-ddT…</c> from an earlier client build is read as its leading ten characters; anything else is 400.</summary>
    [JsonPropertyName("sprk_duedate")]
    public string? SprkDuedate { get; init; }

    [JsonPropertyName("sprk_priorityscore")]
    public int? SprkPriorityscore { get; init; }

    [JsonPropertyName("sprk_effortscore")]
    public int? SprkEffortscore { get; init; }

    [JsonPropertyName("sprk_todocolumn")]
    public int? SprkTodocolumn { get; init; }

    [JsonPropertyName("sprk_todopinned")]
    public bool? SprkTodopinned { get; init; }

    /// <summary>
    /// Update the Dataverse statuscode (1 = Open, 659490001 = In Progress,
    /// 2 = Completed, 659490002 = Dismissed). The BFF writes the matching statecode with it (Completed/Dismissed →
    /// Inactive, Open/In Progress → Active): Dataverse does NOT move the state itself — a status reason outside the
    /// row's current state is HTTP 400 (verified live, task 106). Any other value is refused with 400.
    /// </summary>
    [JsonPropertyName("statuscode")]
    public int? Statuscode { get; init; }
}

// ---------------------------------------------------------------------------
// Document version DTOs
//
// Property names deliberately match the external SPA's existing DocumentVersion
// interface (versionId / versionLabel / createdAt / createdByName /
// fileSizeBytes) so the client needs no reshaping. This is NOT the internal
// VersionInfoDto projection — Graph pointers (driveId/itemId) are never
// surfaced to an external caller.
// ---------------------------------------------------------------------------

public sealed class ExternalDocumentVersionDto
{
    [JsonPropertyName("versionId")]
    public string VersionId { get; init; } = "";

    /// <summary>
    /// Human version label. For a SharePoint driveItemVersion the id IS the label ("1.0", "2.0"),
    /// so this carries the same value rather than a synthesised one.
    /// </summary>
    [JsonPropertyName("versionLabel")]
    public string VersionLabel { get; init; } = "";

    /// <summary>ISO-8601 ("o") timestamp of the version's last modification.</summary>
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = "";

    /// <summary>
    /// Always null on the external surface: an external participant is not shown who wrote a version (internal authors
    /// are a disclosure decision of their own). Kept in the contract because the client already types it optional;
    /// fabricating an author would be worse than omitting one. (<c>VersionInfoDto</c> carries the author since
    /// unified-access-control-r2 task 166 f1-v2 for the internal history route only.)
    /// </summary>
    [JsonPropertyName("createdByName")]
    public string? CreatedByName { get; init; }

    [JsonPropertyName("fileSizeBytes")]
    public long? FileSizeBytes { get; init; }
}

public sealed class ExternalDocumentVersionsResponse
{
    [JsonPropertyName("versions")]
    public IReadOnlyList<ExternalDocumentVersionDto> Versions { get; init; } = [];
}

/// <summary>
/// SPE pointers for a freshly uploaded file, passed from the upload endpoint to
/// <c>ExternalDataService.CreateDocumentAsync</c>.
/// </summary>
/// <remarks>
/// ⚠️ <b>Internal only — never returned to a client.</b> The external surface has a standing rule
/// that Graph pointers (driveId/itemId) are never surfaced; the download and versions routes both
/// state it. This type exists to move those pointers from the endpoint that produced them to the
/// Dataverse write that persists them, both server-side. It is deliberately NOT part of any response
/// DTO. If a future change puts it in one, that is the leak the rule exists to prevent.
/// </remarks>
public sealed record ExternalUploadedFilePointers(
    string DriveId,
    string ItemId,
    string FileName,
    long? FileSizeBytes,
    string? WebUrl);

// ---------------------------------------------------------------------------
// Event DTOs
//
// These are CALENDAR events (sprk_event), not to-dos. smart-todo-decoupling-r3
// FR-29 removed the legacy event-AS-todo model (sprk_event + sprk_todoflag used
// to represent a to-do) and replaced it with the first-class sprk_todo entity
// above — and the /events routes went with it. That retirement was correct, but
// it also removed the only way an external user could see genuine calendar
// events for their project, which the external SPA's EventsCalendar still
// renders. These DTOs restore that surface for real events ONLY.
//
// The distinction is load-bearing: sprk_todoflag is NOT carried here and must
// never be reintroduced. To-dos are served by ExternalTodoDto / the /todos
// routes; events are chronological calendar entries. Two entities, two
// surfaces, no overlap.
// ---------------------------------------------------------------------------

public sealed class ExternalEventDto
{
    [JsonPropertyName("sprk_eventid")]
    public string SprkEventid { get; init; } = "";

    /// <summary>The event name — read from Dataverse <c>sprk_eventname</c> (sprk_event has no <c>sprk_name</c>; the wire
    /// name is kept for the external SPA, task 097).</summary>
    [JsonPropertyName("sprk_name")]
    public string SprkName { get; init; } = "";

    /// <summary>The event due date (<c>sprk_event.sprk_duedate</c>) as a calendar date, <c>yyyy-MM-dd</c> (task 098: Dataverse
    /// Date Only). Parse it as a local calendar date (<c>parseDueDate</c>), never <c>new Date(value)</c>.</summary>
    [JsonPropertyName("sprk_duedate")]
    public string? SprkDuedate { get; init; }

    /// <summary>Dataverse <c>statuscode</c> — the event's status of record (live: Draft 1, Open 659490001, Completed
    /// 659490002, Closed 659490003, Cancelled 659490004, Transferred 659490005, On Hold 659490006, Reassigned 659490007,
    /// No Further Action 2; Spaarke.Dataverse.EventStatusCode). Wire name kept for the external SPA; sprk_event has no
    /// <c>sprk_status</c> column (task 097, review F2).</summary>
    [JsonPropertyName("sprk_status")]
    public int? SprkStatus { get; init; }

    [JsonPropertyName("createdon")]
    public string? Createdon { get; init; }

    /// <summary>
    /// The owning project lookup. sprk_event has no "sprk_projectid" attribute — its project
    /// lookup is sprk_regardingproject (metadata-verified, smart-todo-r5 task 002), so the
    /// OData value field is _sprk_regardingproject_value.
    /// </summary>
    [JsonPropertyName("_sprk_regardingproject_value")]
    public string? SprkRegardingprojectValue { get; init; }

    /// <summary>The systemuser creator stamp (<c>sprk_createdbyperson</c>, task 030): with
    /// <see cref="SprkCreatedbycontactValue"/>, what the SPA uses to offer Delete only on the caller's own events
    /// (FR-26, task 054); the server re-checks every delete.</summary>
    [JsonPropertyName("_sprk_createdbyperson_value")]
    public string? SprkCreatedbypersonValue { get; init; }

    /// <summary>The contact creator stamp (<c>sprk_createdbycontact</c>, task 030).</summary>
    [JsonPropertyName("_sprk_createdbycontact_value")]
    public string? SprkCreatedbycontactValue { get; init; }

    /// <summary>
    /// spaarke-SPA-external-access-platform-r4 task 008 (uac-r2 review 2026-11-13, condition C): <see langword="true"/> only
    /// when the event's <c>sprk_eventtype_ref</c> is the Task row (<c>Spaarke.Dataverse.EventTypeRef.Task</c>). The lookup is
    /// read and compared server-side; its id and name never reach the wire — only this boolean.
    /// </summary>
    [JsonPropertyName("isTask")]
    public bool IsTask { get; init; }

    // ── R4 task 014b (FR-35/FR-36; uac-r2 review of task 014, C and Q5; owner 2026-11-16 for the creator name) ─────────────
    // Names are Dataverse FormattedValues of the lookups, never a person's id or email. See ExternalDataService.EventReadColumns.

    /// <summary>The event's <c>sprk_eventtype_ref</c> row id (a type row, not a person); null when the event has no type.</summary>
    [JsonPropertyName("eventTypeId")]
    public string? EventTypeId { get; init; }

    /// <summary>The event type's name (the lookup's FormattedValue); null when the event has no type.</summary>
    [JsonPropertyName("eventTypeName")]
    public string? EventTypeName { get; init; }

    /// <summary><c>sprk_description</c> (plain text, up to 2,000 characters).</summary>
    [JsonPropertyName("sprk_description")]
    public string? SprkDescription { get; init; }

    /// <summary><c>sprk_priority</c>: Low 100000000, Normal 100000001, High 100000002, Urgent 100000003.</summary>
    [JsonPropertyName("sprk_priority")]
    public int? SprkPriority { get; init; }

    /// <summary>The assignee's name (<c>sprk_assignedto</c>'s FormattedValue). Never the contact id or email.</summary>
    [JsonPropertyName("assignedToName")]
    public string? AssignedToName { get; init; }

    /// <summary>The creator's name (the task-030 creator stamp's FormattedValue; owner-approved 2026-11-16, uac-r2 Q5).</summary>
    [JsonPropertyName("createdByName")]
    public string? CreatedByName { get; init; }

    /// <summary>Whether <c>PATCH /events/{id}</c> would admit this caller's edit (the SAME check, owner S-1, uac-r2 Q1). Set on
    /// the events lists and <c>GET /events/{id}</c>; omitted elsewhere. An unanswered Dataverse question is <c>false</c>.</summary>
    [JsonPropertyName("canEdit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CanEdit { get; set; }

    /// <summary>Whether the PATCH would admit a status change (the same right as <see cref="CanEdit"/>, uac-r2 Q1).</summary>
    [JsonPropertyName("canChangeStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CanChangeStatus { get; set; }

    /// <summary>The <c>statuscode</c> values the PATCH would accept here: every <c>sprk_event</c> status except the current one
    /// (owner S-3) when <see cref="CanChangeStatus"/>, else empty.</summary>
    [JsonPropertyName("allowedStatuses")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? AllowedStatuses { get; set; }

    /// <summary>The record the event is regarding (kind, id, name). Set by <c>GET /events/{id}</c> only.</summary>
    [JsonPropertyName("regarding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExternalEventRegardingDto? Regarding { get; set; }

    /// <summary>
    /// R4 task 014b: the roots the event is filed under (its three regarding lookups), with their names, so the route can apply
    /// the edit right on EVERY parent and name the regarding record. Server-side only, never on the wire.
    /// </summary>
    [JsonIgnore]
    internal IReadOnlyList<ExternalEventParent> Parents { get; init; } = Array.Empty<ExternalEventParent>();

    /// <summary>
    /// ebilling-r1 task 130-0 fix round 1: the invoices this row links to (<c>InvoiceLinks</c>), so the route can hide a row whose
    /// invoice the caller may not see. Server-side only: internal and <see cref="JsonIgnoreAttribute"/>, never on the wire.
    /// Task 130-0 fix round 2 (K1): null means the links were not read, and the route hides the row (fail closed by type); a row with
    /// no invoice link carries an empty list.
    /// </summary>
    [JsonIgnore]
    internal IReadOnlyList<Guid>? InvoiceLinks { get; init; }
}

/// <summary>A root an event is filed under (server-side only; R4 task 014b).</summary>
internal sealed record ExternalEventParent(
    Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDataService.TodoRootKind Kind, Guid Id, string? Name);

/// <summary>The record an event is regarding, on <c>GET /events/{id}</c> (R4 task 014b, owner Q-7).</summary>
public sealed class ExternalEventRegardingDto
{
    /// <summary><c>project</c>, <c>matter</c> or <c>workassignment</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed class CreateExternalEventRequest
{
    /// <summary>Written to Dataverse <c>sprk_eventname</c> (task 097).</summary>
    [JsonPropertyName("sprk_name")]
    public string SprkName { get; init; } = "";

    /// <summary>The due date as a calendar date, <c>yyyy-MM-dd</c> (task 098: Dataverse Date Only refuses a timestamp). A
    /// timestamp <c>yyyy-MM-ddT…</c> from an earlier client build is read as its leading ten characters; anything else is 400.</summary>
    [JsonPropertyName("sprk_duedate")]
    public string? SprkDuedate { get; init; }

    /// <summary>Written to Dataverse <c>statuscode</c> (task 097, review F2/F9): Draft (1) or Open (659490001) only;
    /// omitted ⇒ Open. Any other value is refused with 400.</summary>
    [JsonPropertyName("sprk_status")]
    public int? SprkStatus { get; init; }

    /// <summary>
    /// spaarke-SPA-external-access-platform-r4 task 008 (uac-r2 review 2026-11-13, condition B): the event's type, from a CLOSED
    /// set — only <c>"task"</c> (<see cref="Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDataService.TaskEventType"/>),
    /// which binds the Task row of <c>sprk_eventtype_ref</c>. Omitted (or null) creates the event with no type, exactly as
    /// before. Any other value is refused with 400 <c>sdap.event.type_not_allowed</c> and nothing is created.
    /// </summary>
    [JsonPropertyName("eventType")]
    public string? EventType { get; init; }

    // Each NEW-shape member records that it was SENT (an explicit JSON null included): see IsLegacyUntypedCreate.
    private Guid? _eventTypeId, _assignedTo;
    private int? _priority;
    private string? _description;
    private bool _newShapeSent;

    /// <summary>
    /// R4 task 014b (FR-35, owner Q-2): the event's type, REQUIRED in the new shape — an ACTIVE <c>sprk_eventtype_ref</c> row the
    /// caller may pick (<c>creatableEventTypes</c>). Missing from a new-shape body is 400 <c>sdap.event.type_required</c>; outside
    /// the set is 400 <c>sdap.event.type_not_allowed</c>. <see cref="EventType"/> = <c>"task"</c> is still accepted in its place,
    /// and a legacy body may still omit both (LEGACY-EVENT-CREATE-R4, see <see cref="IsLegacyUntypedCreate"/>).
    /// </summary>
    [JsonPropertyName("eventTypeId")]
    public Guid? EventTypeId { get => _eventTypeId; init { _eventTypeId = value; _newShapeSent = true; } }

    /// <summary>R4 task 014b (owner Q-4): Low 100000000, Normal 100000001 (the default), High 100000002, Urgent 100000003.</summary>
    [JsonPropertyName("sprk_priority")]
    public int? SprkPriority { get => _priority; init { _priority = value; _newShapeSent = true; } }

    /// <summary>R4 task 014b: plain text, up to 2,000 characters.</summary>
    [JsonPropertyName("sprk_description")]
    public string? SprkDescription { get => _description; init { _description = value; _newShapeSent = true; } }

    /// <summary>R4 task 014b (owner S-2, uac-r2 Q2): a contact from the record's assignable set
    /// (<c>GET /{root}/{id}/assignable-people</c>); anyone else is 400 <c>sdap.event.assignee_not_allowed</c>.</summary>
    [JsonPropertyName("assignedToContactId")]
    public Guid? AssignedToContactId { get => _assignedTo; init { _assignedTo = value; _newShapeSent = true; } }

    /// <summary>
    /// LEGACY-EVENT-CREATE-R4 — the pre-014b create shapes, kept for ONE release so the SPA deployed before task 015 keeps
    /// working against a master-built BFF. Both come out TOGETHER in R5, once task 015's SPA is live in every environment:
    /// (1) <c>eventType: "task"</c> mapped to the Task row (<c>ExternalDataService.EventTypeIdOf</c>), and (2) this legacy
    /// UNTYPED body. Remove every site tagged LEGACY-EVENT-CREATE-R4.
    /// <para>The rule: a body is the NEW shape when it SENDS any of <c>eventTypeId</c>, <c>sprk_priority</c>,
    /// <c>sprk_description</c> or <c>assignedToContactId</c> (an explicit JSON <c>null</c> counts as sent). A new-shape body must
    /// name its type (400 <c>sdap.event.type_required</c> otherwise). A body that sends none of the four AND no
    /// <c>eventType</c> is the legacy untyped create — the old SPA's "Event" (<c>sprk_name</c>, <c>sprk_duedate</c>,
    /// <c>sprk_status</c> only) — and creates an untyped event exactly as before 014b.</para>
    /// </summary>
    [JsonIgnore]
    public bool IsLegacyUntypedCreate => !_newShapeSent && EventType is null;
}

/// <summary>
/// Body of <c>PATCH /api/v1/external/events/{id}</c> (R4 task 014b, FR-36): partial — a member that is not sent is not
/// changed. Each setter records that its member was SENT (an explicit JSON <c>null</c> included), so "clear the due date"
/// and "leave the due date" are different requests. Any member this type does not declare is ignored by the binder and never
/// reaches Dataverse (the column allow-list is <c>ExternalDataService.BuildEventUpdatePayload</c>).
/// </summary>
public sealed class UpdateExternalEventRequest
{
    private string? _name, _dueDate, _description;
    private int? _priority, _status;
    private Guid? _eventTypeId, _assignedTo;

    [JsonPropertyName("sprk_name")]
    public string? SprkName { get => _name; init { _name = value; NameSent = true; } }

    /// <summary>A calendar date <c>yyyy-MM-dd</c>; an explicit null clears it (400 when the event's type requires one).</summary>
    [JsonPropertyName("sprk_duedate")]
    public string? SprkDuedate { get => _dueDate; init { _dueDate = value; DueDateSent = true; } }

    [JsonPropertyName("sprk_priority")]
    public int? SprkPriority { get => _priority; init { _priority = value; PrioritySent = true; } }

    /// <summary>Plain text up to 2,000 characters; an explicit null or empty string clears it.</summary>
    [JsonPropertyName("sprk_description")]
    public string? SprkDescription { get => _description; init { _description = value; DescriptionSent = true; } }

    /// <summary>Any <c>sprk_event</c> statuscode (owner S-3); the matching statecode is written with it.</summary>
    [JsonPropertyName("sprk_status")]
    public int? SprkStatus { get => _status; init { _status = value; StatusSent = true; } }

    /// <summary>Never accepted (owner S-4): any value is 400 <c>sdap.event.type_immutable</c>.</summary>
    [JsonPropertyName("eventTypeId")]
    public Guid? EventTypeId { get => _eventTypeId; init { _eventTypeId = value; EventTypeIdSent = true; } }

    /// <summary>Not accepted yet (the assignee write path is with the owner): any value is 400
    /// <c>sdap.event.assignee_update_unsupported</c> and nothing is written.</summary>
    [JsonPropertyName("assignedToContactId")]
    public Guid? AssignedToContactId { get => _assignedTo; init { _assignedTo = value; AssigneeSent = true; } }

    [JsonIgnore] public bool NameSent { get; private set; }
    [JsonIgnore] public bool DueDateSent { get; private set; }
    [JsonIgnore] public bool PrioritySent { get; private set; }
    [JsonIgnore] public bool DescriptionSent { get; private set; }
    [JsonIgnore] public bool StatusSent { get; private set; }
    [JsonIgnore] public bool EventTypeIdSent { get; private set; }
    [JsonIgnore] public bool AssigneeSent { get; private set; }
}

// ---------------------------------------------------------------------------
// Contact DTO
// ---------------------------------------------------------------------------

public sealed class ExternalContactDto
{
    [JsonPropertyName("contactid")]
    public string Contactid { get; init; } = "";

    [JsonPropertyName("fullname")]
    public string? Fullname { get; init; }

    [JsonPropertyName("firstname")]
    public string? Firstname { get; init; }

    [JsonPropertyName("lastname")]
    public string? Lastname { get; init; }

    [JsonPropertyName("emailaddress1")]
    public string? Emailaddress1 { get; init; }

    [JsonPropertyName("telephone1")]
    public string? Telephone1 { get; init; }

    [JsonPropertyName("jobtitle")]
    public string? Jobtitle { get; init; }

    [JsonPropertyName("_parentcustomerid_value")]
    public string? ParentcustomeridValue { get; init; }
}

// ---------------------------------------------------------------------------
// Organization (Account) DTO
// ---------------------------------------------------------------------------

public sealed class ExternalOrganizationDto
{
    [JsonPropertyName("accountid")]
    public string Accountid { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("websiteurl")]
    public string? Websiteurl { get; init; }

    [JsonPropertyName("telephone1")]
    public string? Telephone1 { get; init; }

    [JsonPropertyName("address1_city")]
    public string? Address1City { get; init; }

    [JsonPropertyName("address1_country")]
    public string? Address1Country { get; init; }
}
