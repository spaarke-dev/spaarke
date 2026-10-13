using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// The body of <c>POST /api/v1/external/servicerequests</c> (spaarke-SPA-external-access-platform-r3 task 035, FR-05): one
/// Legal Front Door wizard submission. A CLOSED DTO — a requester, owner or creator named in the request does not bind, so
/// <c>sprk_requestedby</c> is always the resolved caller's contact.
/// </summary>
public sealed class SubmitServiceRequestRequest
{
    /// <summary>The <c>sprk_requesttype</c> option value: NDA Assessment 100000000, Invention Submission 100000001, Policy
    /// Question 100000002, Trademark Search 100000003. Anything else is a 400.</summary>
    [JsonPropertyName("requestType")]
    public int? RequestType { get; init; }

    /// <summary>The request's title, written to <c>sprk_name</c> (required, at most 850 characters).</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>The wizard's answers: a JSON object of at most 100 KB (UTF-8), stored as JSON in
    /// <c>sprk_intakedata</c>.</summary>
    [JsonPropertyName("answers")]
    public JsonElement? Answers { get; init; }
}

/// <summary>The 201 body of <c>POST /api/v1/external/servicerequests</c>: the created request's id.</summary>
public sealed class ExternalServiceRequestCreatedDto
{
    [JsonPropertyName("sprk_servicerequestid")]
    public required string SprkServicerequestid { get; init; }
}
