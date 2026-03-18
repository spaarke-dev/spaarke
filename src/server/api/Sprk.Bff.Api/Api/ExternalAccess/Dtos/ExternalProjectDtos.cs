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
// Event DTOs
// ---------------------------------------------------------------------------

public sealed class ExternalEventDto
{
    [JsonPropertyName("sprk_eventid")]
    public string SprkEventid { get; init; } = "";

    [JsonPropertyName("sprk_name")]
    public string SprkName { get; init; } = "";

    [JsonPropertyName("sprk_duedate")]
    public string? SprkDuedate { get; init; }

    [JsonPropertyName("sprk_status")]
    public int? SprkStatus { get; init; }

    [JsonPropertyName("sprk_todoflag")]
    public bool? SprkTodoflag { get; init; }

    [JsonPropertyName("createdon")]
    public string? Createdon { get; init; }

    [JsonPropertyName("_sprk_projectid_value")]
    public string? SprkProjectidValue { get; init; }
}

/// <summary>Request body from the SPA for creating a new event.</summary>
public sealed class CreateExternalEventRequest
{
    [JsonPropertyName("sprk_name")]
    public string SprkName { get; init; } = "";

    [JsonPropertyName("sprk_duedate")]
    public string? SprkDuedate { get; init; }

    [JsonPropertyName("sprk_status")]
    public int? SprkStatus { get; init; }

    [JsonPropertyName("sprk_todoflag")]
    public bool? SprkTodoflag { get; init; }
}

/// <summary>Request body from the SPA for updating an existing event (PATCH semantics).</summary>
public sealed class UpdateExternalEventRequest
{
    [JsonPropertyName("sprk_name")]
    public string? SprkName { get; init; }

    [JsonPropertyName("sprk_duedate")]
    public string? SprkDuedate { get; init; }

    [JsonPropertyName("sprk_status")]
    public int? SprkStatus { get; init; }

    [JsonPropertyName("sprk_todoflag")]
    public bool? SprkTodoflag { get; init; }
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
