using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Api.Office;

/// <summary>
/// Office-add-in-scoped endpoints for sprk_communication lookups and linked-todo
/// queries. Backs Outlook taskpane flows from smart-todo-decoupling-r3 tasks 070
/// (Create To Do ribbon) and 071 (linked-todos banner).
/// </summary>
/// <remarks>
/// <para>
/// Routes (under <c>/api/office/communications</c>):
/// </para>
/// <list type="bullet">
///   <item><description><c>GET /by-message-id/{internetMessageId}</c> — lookup an existing
///     <c>sprk_communication</c> by Outlook's RFC-5322 <c>internetMessageId</c>. Used by
///     <c>communicationLookupService.findCommunicationByMessageId</c> (task 070). Returns
///     200 + minimal projection or 404 when no matching row exists.</description></item>
///   <item><description><c>GET /{commId}/linked-todos</c> — list active+inactive
///     <c>sprk_todo</c> records linked to a <c>sprk_communication</c> via the
///     <c>sprk_regardingcommunication</c> lookup. Used by
///     <c>useLinkedTodosForCommunication</c> hook (task 071). Returns 200 +
///     <c>{ count, todos }</c> (todos capped at 10 per spec FR-28); 404 only if the
///     communication itself does not exist (defensive — the client always supplies an
///     id returned from a prior save).</description></item>
/// </list>
/// <para>
/// Follows ADR-001 (Minimal API), ADR-008 (endpoint-filter authorization at group level
/// via <see cref="Microsoft.AspNetCore.Builder.AuthorizationEndpointConventionBuilderExtensions.RequireAuthorization(Microsoft.AspNetCore.Builder.IEndpointConventionBuilder, string[])"/>),
/// ADR-028 (auth via JWT bearer per the shared BFF pipeline).
/// </para>
/// <para>
/// Dataverse access uses <see cref="IGenericEntityService"/> per existing BFF
/// patterns (see <c>WorkAssignmentEndpoints</c>, <c>CommunicationEndpoints</c>). The
/// service is registered in <c>SharedServicesModule</c> and resolves to either
/// <c>DataverseServiceClientImpl</c> (canonical) or <c>DataverseWebApiService</c>
/// depending on configuration.
/// </para>
/// </remarks>
public static class OfficeCommunicationsEndpoints
{
    /// <summary>
    /// Maximum number of linked-todo projections returned in a single response.
    /// Aligns with the client's banner cap (spec.md FR-28 / NFR-09).
    /// </summary>
    private const int LinkedTodosTopCount = 10;

    /// <summary>
    /// Maps the Office-scoped communication endpoints to the application.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapOfficeCommunicationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/office/communications")
            .WithTags("Office")
            .RequireAuthorization();

        // GET /api/office/communications/by-message-id/{internetMessageId}
        // Task 070 — Outlook ribbon Create To Do button uses this to check whether
        // the current email has already been saved as a sprk_communication.
        // Route uses {**id} catch-all so RFC-5322 internet message ids that include
        // forward slashes (rare but possible per RFC) survive routing; the client
        // URL-encodes already.
        group.MapGet("/by-message-id/{internetMessageId}", FindByMessageIdAsync)
            .WithName("FindCommunicationByMessageId")
            .WithDescription(
                "Look up an existing sprk_communication by the email's RFC-5322 internetMessageId. " +
                "Returns minimal projection (communicationId, subject) or 404.")
            .Produces<CommunicationLookupResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/office/communications/{commId:guid}/linked-todos
        // Task 071 — Outlook taskpane banner uses this to display the count of
        // linked sprk_todo records and (capped) projections.
        group.MapGet("/{commId:guid}/linked-todos", GetLinkedTodosAsync)
            .WithName("GetLinkedTodosForCommunication")
            .WithDescription(
                "List sprk_todo records linked to a sprk_communication via " +
                "sprk_regardingcommunication. Returns { count, todos } (todos capped at " +
                $"{LinkedTodosTopCount}).")
            .Produces<LinkedTodosResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// Handler for <c>GET /api/office/communications/by-message-id/{internetMessageId}</c>.
    /// </summary>
    private static async Task<IResult> FindByMessageIdAsync(
        string internetMessageId,
        IGenericEntityService entityService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var traceId = context.TraceIdentifier;
        var userId = context.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("oid");

        // Defensive: empty / whitespace path segment should never reach us (the route
        // template requires a value), but guard anyway.
        if (string.IsNullOrWhiteSpace(internetMessageId))
        {
            logger.LogWarning(
                "FindCommunicationByMessageId rejected empty internetMessageId, " +
                "UserId={UserId}, CorrelationId={CorrelationId}",
                userId, traceId);
            return Results.Problem(
                title: "Invalid internetMessageId",
                detail: "internetMessageId is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_VALIDATION",
                    ["correlationId"] = traceId
                });
        }

        logger.LogInformation(
            "Looking up sprk_communication by internetMessageId | " +
            "MessageIdLength={Length}, UserId={UserId}, CorrelationId={CorrelationId}",
            internetMessageId.Length, userId, traceId);

        try
        {
            // Query sprk_communication where sprk_internetmessageid = id. Mirrors
            // DataverseServiceClientImpl.GetCommunicationByInternetMessageIdAsync but
            // returns a richer column set (incl. sprk_subject).
            var query = new QueryExpression("sprk_communication")
            {
                ColumnSet = new ColumnSet(
                    "sprk_communicationid",
                    "sprk_subject",
                    "sprk_internetmessageid"),
                TopCount = 1
            };
            query.Criteria.AddCondition(
                "sprk_internetmessageid", ConditionOperator.Equal, internetMessageId);

            var results = await entityService.RetrieveMultipleAsync(query, ct);

            if (results.Entities.Count == 0)
            {
                logger.LogInformation(
                    "No sprk_communication found for internetMessageId, " +
                    "UserId={UserId}, CorrelationId={CorrelationId}",
                    userId, traceId);
                return Results.Problem(
                    title: "Communication Not Found",
                    detail: "No sprk_communication exists for the provided internetMessageId.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "OFFICE_COMM_NOT_FOUND",
                        ["correlationId"] = traceId
                    });
            }

            var entity = results.Entities[0];
            var communicationId = entity.GetAttributeValue<Guid>("sprk_communicationid");
            // Use sprk_subject if present, otherwise fall back to the empty string. The
            // client tolerates a missing subject (see communicationLookupService.ts L99).
            var subject = entity.GetAttributeValue<string>("sprk_subject") ?? string.Empty;

            logger.LogInformation(
                "Found sprk_communication {CommunicationId}, " +
                "UserId={UserId}, CorrelationId={CorrelationId}",
                communicationId, userId, traceId);

            return Results.Ok(new CommunicationLookupResponse
            {
                CommunicationId = communicationId,
                Subject = subject
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error looking up sprk_communication by internetMessageId, " +
                "UserId={UserId}, CorrelationId={CorrelationId}",
                userId, traceId);
            return Results.Problem(
                title: "Lookup Failed",
                detail: "An unexpected error occurred while looking up the communication.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_INTERNAL",
                    ["correlationId"] = traceId
                });
        }
    }

    /// <summary>
    /// Handler for <c>GET /api/office/communications/{commId:guid}/linked-todos</c>.
    /// </summary>
    private static async Task<IResult> GetLinkedTodosAsync(
        Guid commId,
        IGenericEntityService entityService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var traceId = context.TraceIdentifier;
        var userId = context.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("oid");

        logger.LogInformation(
            "Listing linked todos for sprk_communication {CommunicationId}, " +
            "UserId={UserId}, CorrelationId={CorrelationId}",
            commId, userId, traceId);

        try
        {
            // Query sprk_todo where sprk_regardingcommunication = commId. Selects only
            // the fields the client banner needs (FR-28 / NFR-09).
            var query = new QueryExpression("sprk_todo")
            {
                ColumnSet = new ColumnSet(
                    "sprk_todoid",
                    "sprk_name",
                    "statecode",
                    "statuscode"),
                TopCount = LinkedTodosTopCount
            };
            query.Criteria.AddCondition(
                "sprk_regardingcommunication", ConditionOperator.Equal, commId);

            var results = await entityService.RetrieveMultipleAsync(query, ct);

            var todos = new List<LinkedTodoSummary>(results.Entities.Count);
            foreach (var entity in results.Entities)
            {
                todos.Add(new LinkedTodoSummary
                {
                    SprkTodoid = entity.GetAttributeValue<Guid>("sprk_todoid"),
                    SprkName = entity.GetAttributeValue<string>("sprk_name") ?? string.Empty,
                    Statecode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0,
                    Statuscode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 0
                });
            }

            logger.LogInformation(
                "Returning {TodoCount} linked todos for sprk_communication {CommunicationId}, " +
                "UserId={UserId}, CorrelationId={CorrelationId}",
                todos.Count, commId, userId, traceId);

            // count reflects how many we have in-hand (capped by TopCount). Client tolerates
            // count == todos.Length per useLinkedTodosForCommunication.ts L168.
            return Results.Ok(new LinkedTodosResponse
            {
                Count = todos.Count,
                Todos = todos.ToArray()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error listing linked todos for sprk_communication {CommunicationId}, " +
                "UserId={UserId}, CorrelationId={CorrelationId}",
                commId, userId, traceId);
            return Results.Problem(
                title: "Linked Todos Lookup Failed",
                detail: "An unexpected error occurred while listing linked to-dos.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_INTERNAL",
                    ["correlationId"] = traceId
                });
        }
    }
}

/// <summary>
/// Response DTO for <c>GET /api/office/communications/by-message-id/{internetMessageId}</c>.
/// Mirrors the <c>BffLookupResponse</c> interface in
/// <c>src/client/office-addins/shared/taskpane/services/communicationLookupService.ts</c>.
/// Default ASP.NET Core camelCase serialization gives us <c>communicationId</c> and
/// <c>subject</c> — exactly what the client expects.
/// </summary>
public sealed class CommunicationLookupResponse
{
    /// <summary>The sprk_communicationid GUID of the matching record.</summary>
    public required Guid CommunicationId { get; init; }

    /// <summary>The sprk_subject of the matching record (empty string if unset).</summary>
    public required string Subject { get; init; }
}

/// <summary>
/// Single linked-todo projection. Mirrors the <c>LinkedTodo</c> interface in
/// <c>src/client/office-addins/shared/taskpane/hooks/useLinkedTodosForCommunication.ts</c>.
/// Wire-level field names are snake_case (<c>sprk_todoid</c> etc.) because the client
/// reads Dataverse field names verbatim — see the TS interface. JsonPropertyName
/// attributes override the BFF default camelCase to preserve this contract.
/// </summary>
public sealed class LinkedTodoSummary
{
    /// <summary>The sprk_todoid GUID of the linked to-do.</summary>
    [JsonPropertyName("sprk_todoid")]
    public required Guid SprkTodoid { get; init; }

    /// <summary>The sprk_name (display name) of the linked to-do.</summary>
    [JsonPropertyName("sprk_name")]
    public required string SprkName { get; init; }

    /// <summary>Statecode (0 = Active, 1 = Inactive) of the linked to-do.</summary>
    [JsonPropertyName("statecode")]
    public required int Statecode { get; init; }

    /// <summary>Statuscode (status reason) of the linked to-do.</summary>
    [JsonPropertyName("statuscode")]
    public required int Statuscode { get; init; }
}

/// <summary>
/// Response DTO for <c>GET /api/office/communications/{commId}/linked-todos</c>.
/// Mirrors the <c>LinkedTodosResponse</c> interface in
/// <c>src/client/office-addins/shared/taskpane/hooks/useLinkedTodosForCommunication.ts</c>.
/// Default camelCase serialization yields <c>count</c> and <c>todos</c> — matches client.
/// </summary>
public sealed class LinkedTodosResponse
{
    /// <summary>Total count of linked to-dos returned (≤ 10).</summary>
    public required int Count { get; init; }

    /// <summary>The linked to-do projections (≤ 10 per spec FR-28).</summary>
    public required LinkedTodoSummary[] Todos { get; init; }
}
