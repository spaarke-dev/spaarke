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
    /// 2 = Completed, 659490002 = Dismissed). Setting Completed/Dismissed
    /// also moves the record to Inactive statecode (Dataverse handles the
    /// statecode transition automatically based on statuscode option values).
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
    /// Always null today: <c>VersionInfoDto</c> does not carry <c>lastModifiedBy</c>. Kept in the
    /// contract because the client already types it optional; fabricating an author would be worse
    /// than omitting one.
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

    [JsonPropertyName("sprk_name")]
    public string SprkName { get; init; } = "";

    [JsonPropertyName("sprk_duedate")]
    public string? SprkDuedate { get; init; }

    /// <summary>sprk_eventstatus choice value.</summary>
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
}

public sealed class CreateExternalEventRequest
{
    [JsonPropertyName("sprk_name")]
    public string SprkName { get; init; } = "";

    [JsonPropertyName("sprk_duedate")]
    public string? SprkDuedate { get; init; }

    [JsonPropertyName("sprk_status")]
    public int? SprkStatus { get; init; }
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
