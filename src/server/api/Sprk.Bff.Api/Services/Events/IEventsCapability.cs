using Sprk.Bff.Api.Api.Events.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.RecordCreation;

namespace Sprk.Bff.Api.Services.Events;

/// <summary>
/// The events capability (spaarkeai-mcp-bff-api-r2 task 006, spec FR-07 part 1): the ONE owner of the event list, get, create and
/// complete rules and their authorization, so the REST routes (<c>Api/Events/EventEndpoints.cs</c>) and the MCP tools (tasks 025
/// and 036) call one implementation instead of copying it. The IDecisionCapability pattern (r1 B-1): every method takes the
/// request's <see cref="HttpContext"/> and answers an outcome, never an <c>IResult</c>; the caller maps it to its own contract.
/// </summary>
/// <remarks>
/// <para><b>Fail closed (ADR-028).</b> A null context, a missing principal or a principal with no authenticated identity is
/// <see cref="EventCapabilityStatus.UserContextRequired"/> before any Dataverse call. Nothing falls back to an app-only read.</para>
/// <para><b>Authorization (ADR-008, one implementation).</b> The list runs its query impersonated AS the caller. Get and complete
/// ask <see cref="RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync"/> (no Read: <see cref="EventCapabilityStatus.NotFound"/>,
/// the uniform answer an absent event also gets; Read without the right: <see cref="EventCapabilityStatus.Forbidden"/>). Create
/// checks the request shape, then asks <see cref="RecordRouteAccessAuthorizationFilter.DecideDeclaredTargetAsync"/> for the Create
/// privilege on <c>sprk_event</c> and AppendTo on the regarding record. The REST routes keep their endpoint filters, which call the
/// SAME decision methods; their handlers call this capability's static cores, so a REST request asks Dataverse exactly what it
/// asked before.</para>
/// </remarks>
internal interface IEventsCapability
{
    /// <summary>The events the caller may read, narrowed to "my events" (owner, assigned contact, creator), one page and its count.</summary>
    Task<EventCapabilityResult<EventListResponse>> ListAsync(HttpContext? httpContext, EventListQuery query, CancellationToken ct);

    /// <summary>One event, when the caller may read it; otherwise the uniform not-found.</summary>
    Task<EventCapabilityResult<EventDto>> GetAsync(HttpContext? httpContext, Guid eventId, CancellationToken ct);

    /// <summary>A new event, created through <see cref="RecordCreationService"/> after the caller's Create and AppendTo checks.</summary>
    Task<EventCapabilityResult<CreateEventResponse>> CreateAsync(HttpContext? httpContext, CreateEventRequest? request, CancellationToken ct);

    /// <summary>Completes open work the caller may write.</summary>
    Task<EventCapabilityResult<EventActionResponse>> CompleteAsync(HttpContext? httpContext, Guid eventId, CancellationToken ct);
}

/// <summary>The list's filters and page, as <c>GET /api/v1/events</c> binds them from its query string.</summary>
internal sealed record EventListQuery
{
    public int? RegardingRecordType { get; init; }

    /// <summary>A GUID as text; anything else is <see cref="EventCapabilityStatus.Invalid"/> (no caller text reaches $filter).</summary>
    public string? RegardingRecordId { get; init; }

    public Guid? EventTypeId { get; init; }

    public int? StatusCode { get; init; }

    /// <summary>"open", "completed", "cancelled": wins over <see cref="StatusCode"/>.</summary>
    public string? Status { get; init; }

    public int? Priority { get; init; }

    public DateTime? DueDateFrom { get; init; }

    public DateTime? DueDateTo { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

/// <summary>How an events capability call ended.</summary>
internal enum EventCapabilityStatus
{
    /// <summary>Answered: <see cref="EventCapabilityResult{T}.Value"/> holds the result (created, completed or read).</summary>
    Ok,

    /// <summary>No signed-in caller: nothing was read or written (ADR-028).</summary>
    UserContextRequired,

    /// <summary>The request's shape: <see cref="EventCapabilityResult{T}.Field"/> and <see cref="EventCapabilityResult{T}.Message"/>.</summary>
    Invalid,

    /// <summary>List only: the caller does not resolve to a Dataverse user, so no caller-scoped answer can be given.</summary>
    CallerUnresolved,

    /// <summary>No Read on the event, or no such event: the same answer for both.</summary>
    NotFound,

    /// <summary>Read without the operation's rights, or (create) no Read + AppendTo on the regarding record.</summary>
    Forbidden,

    /// <summary>Create only: the caller does not hold the Create privilege on <c>sprk_event</c>.</summary>
    InsufficientPrivilege,

    /// <summary>Complete only: the event is not open work (<see cref="EventCapabilityResult{T}.Message"/> says which states are).</summary>
    InvalidTransition,

    /// <summary>Create only: <see cref="RecordCreationService"/> refused (<see cref="EventCapabilityResult{T}.Refusal"/>); nothing written.</summary>
    Refused,

    /// <summary>A fault; the message names no record.</summary>
    Failed,
}

/// <summary>The outcome of an events capability call.</summary>
/// <typeparam name="T">The answer on <see cref="EventCapabilityStatus.Ok"/>.</typeparam>
internal sealed record EventCapabilityResult<T>(EventCapabilityStatus Status, T? Value = null)
    where T : class
{
    /// <summary><see cref="EventCapabilityStatus.Invalid"/>: the request member at fault, as REST names it in its 400.</summary>
    public string? Field { get; init; }

    /// <summary><see cref="EventCapabilityStatus.Invalid"/> or <see cref="EventCapabilityStatus.InvalidTransition"/>: the text REST shows.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// <see cref="EventCapabilityStatus.Refused"/>: the service's refusal, for diagnostics and logs only. A tool must NOT
    /// render its <c>Detail</c> or <c>RefusedColumn</c> to the caller: REST collapses every refusal it does not classify into
    /// a generic 500 and never names the refused lookup (which would tell a caller which record was "not found or denied").
    /// Report <see cref="ReasonCode"/> instead, as REST does.
    /// </summary>
    public RecordCreationRefusal? Refusal { get; init; }

    /// <summary><see cref="EventCapabilityStatus.Refused"/>: the service's step records (which step refused).</summary>
    public IReadOnlyList<RecordCreationStepRecord> Steps { get; init; } = [];

    /// <summary>
    /// The stable reason code REST carries for the same outcome (<c>reasonCode</c> / <c>errorCode</c>), so a tool reports the same
    /// reason as the route: <c>null</c> for an outcome REST gives no code (a 400, a 500, a success).
    /// </summary>
    public string? ReasonCode => Status switch
    {
        EventCapabilityStatus.UserContextRequired => DataverseUserClientErrorCodes.UserContextRequired,
        EventCapabilityStatus.CallerUnresolved => EventsCapability.CallerUnresolvedReasonCode,
        EventCapabilityStatus.NotFound => ProblemDetailsHelper.RecordUnavailableReasonCode,
        EventCapabilityStatus.Forbidden => RecordRouteAccessAuthorizationFilter.InsufficientRightsReasonCode,
        EventCapabilityStatus.InsufficientPrivilege => RecordRouteAccessAuthorizationFilter.InsufficientPrivilegeReasonCode,
        EventCapabilityStatus.Refused when Refusal is not null => EventsCapability.RefusalReasonCode(Refusal, Steps),
        _ => null,
    };

    internal static EventCapabilityResult<T> Ok(T value) => new(EventCapabilityStatus.Ok, value);

    internal static EventCapabilityResult<T> Of(EventCapabilityStatus status) => new(status);

    internal static EventCapabilityResult<T> InvalidField(string field, string message) =>
        new(EventCapabilityStatus.Invalid) { Field = field, Message = message };
}
