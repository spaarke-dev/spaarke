using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// API endpoints for Work Assignment entity operations.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API pattern (no controllers).
/// Follows ADR-008: Endpoint filters for authorization.
/// Follows ADR-019: ProblemDetails for error responses.
/// </remarks>
public static class WorkAssignmentEndpoints
{
    /// <summary>
    /// Registers work assignment endpoints with the application.
    /// </summary>
    public static void MapWorkAssignmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/work-assignments")
            .WithTags("Work Assignments")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        // POST /api/v1/work-assignments - Create a new work assignment
        group.MapPost("/", CreateWorkAssignmentAsync)
            .WithName("CreateWorkAssignment")
            .WithSummary("Create a new work assignment")
            .WithDescription("Creates a new Work Assignment record in Dataverse. " +
                "Title and AssignedToUserId are required. " +
                "An in-app notification is sent to the assigned user on success. " +
                "Returns 201 Created with the new assignment ID.")
            .Produces<CreateWorkAssignmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// Creates a new work assignment and notifies the assigned user.
    /// </summary>
    private static async Task<IResult> CreateWorkAssignmentAsync(
        [FromBody] CreateWorkAssignmentRequest request,
        IGenericEntityService entityService,
        NotificationService notificationService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // ── Validate required fields ──────────────────────────────────────
        var validationErrors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Title))
            validationErrors["Title"] = ["Title is required."];

        if (request.AssignedToUserId == Guid.Empty)
            validationErrors["AssignedToUserId"] = ["Assigned user ID is required."];

        if (validationErrors.Count > 0)
            return Results.ValidationProblem(validationErrors);

        logger.LogInformation(
            "Creating work assignment. Title={Title}, AssignedTo={AssignedToUserId}, MatterId={MatterId}",
            request.Title, request.AssignedToUserId, request.MatterId);

        try
        {
            // ── Create the sprk_workassignment record ─────────────────────
            var entity = new Entity("sprk_workassignment");
            entity["sprk_name"] = request.Title;
            entity["ownerid"] = new EntityReference("systemuser", request.AssignedToUserId);

            if (!string.IsNullOrWhiteSpace(request.Description))
                entity["sprk_description"] = request.Description;

            if (request.MatterId.HasValue)
                entity["sprk_matterid"] = new EntityReference("sprk_matter", request.MatterId.Value);

            if (request.DueDate.HasValue)
                entity["sprk_duedate"] = request.DueDate.Value;

            var assignmentId = await entityService.CreateAsync(entity, ct);

            logger.LogInformation(
                "Work assignment created. AssignmentId={AssignmentId}, Title={Title}, AssignedTo={AssignedToUserId}",
                assignmentId, request.Title, request.AssignedToUserId);

            // ── Send inline notification to assigned user ─────────────────
            try
            {
                var notificationBody = !string.IsNullOrWhiteSpace(request.MatterName)
                    ? $"You have been assigned \"{request.Title}\" on matter {request.MatterName}."
                    : $"You have been assigned \"{request.Title}\".";

                var actionUrl = request.MatterId.HasValue
                    ? $"/main.aspx?etn=sprk_matter&id={request.MatterId.Value}&pagetype=entityrecord"
                    : null;

                await notificationService.CreateNotificationAsync(
                    userId: request.AssignedToUserId,
                    title: $"New Assignment: {request.Title}",
                    body: notificationBody,
                    category: "assignments",
                    actionUrl: actionUrl,
                    regardingId: assignmentId,
                    cancellationToken: ct);

                logger.LogInformation(
                    "Notification sent for work assignment {AssignmentId} to user {AssignedToUserId}",
                    assignmentId, request.AssignedToUserId);
            }
            catch (Exception ex)
            {
                // Notification failure should not fail the assignment creation
                logger.LogWarning(
                    ex,
                    "Failed to send notification for work assignment {AssignmentId} to user {AssignedToUserId}: {Error}",
                    assignmentId, request.AssignedToUserId, ex.Message);
            }

            return TypedResults.Created(
                $"/api/v1/work-assignments/{assignmentId}",
                new CreateWorkAssignmentResponse(assignmentId, request.Title));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creating work assignment. Title={Title}, AssignedTo={AssignedToUserId}",
                request.Title, request.AssignedToUserId);

            return Results.Problem(
                detail: "An error occurred while creating the work assignment",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }
}

/// <summary>
/// Request model for creating a new work assignment.
/// </summary>
/// <param name="Title">Assignment title/name (required, maps to sprk_name).</param>
/// <param name="AssignedToUserId">Dataverse systemuserid of the user to assign (required, sets ownerid).</param>
/// <param name="Description">Optional description of the assignment.</param>
/// <param name="MatterId">Optional related matter ID (sprk_matterid lookup).</param>
/// <param name="MatterName">Optional matter display name (used in notification body, not stored).</param>
/// <param name="DueDate">Optional due date for the assignment.</param>
public record CreateWorkAssignmentRequest(
    string Title,
    Guid AssignedToUserId,
    string? Description = null,
    Guid? MatterId = null,
    string? MatterName = null,
    DateTime? DueDate = null
);

/// <summary>
/// Response model for a successfully created work assignment.
/// </summary>
/// <param name="Id">The ID of the created work assignment record.</param>
/// <param name="Title">The title of the created assignment.</param>
public record CreateWorkAssignmentResponse(Guid Id, string Title);
