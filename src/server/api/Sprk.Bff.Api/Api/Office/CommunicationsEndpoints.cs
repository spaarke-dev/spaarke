using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Office;

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
/// 🔴 <b>Dataverse access on these routes is DELEGATED (user-OBO), via
/// <see cref="IDataverseUserClient"/> — not app-only.</b> Changed 2026-09-29 by
/// unified-access-control-r2 task 127 (GitHub #1020). Every read here previously went through
/// <c>IGenericEntityService</c>, an app-only singleton that cannot carry per-request user context,
/// and every read was keyed SOLELY on a caller-supplied identifier. The caller's object id was
/// resolved on all three handlers and used exclusively as a log argument — so holding an
/// internetMessageId (which every participant in a thread holds) or a communication GUID was the
/// same thing as being entitled to what it pointed at.
/// </para>
/// <para>
/// The group's bare <c>.RequireAuthorization()</c> means "any authenticated caller" and nothing more:
/// there is no <c>DefaultPolicy</c> override, and the <c>FallbackPolicy</c> (UAC-r2 task 167) also asks only
/// for an authenticated user. Authorization
/// on these routes is therefore the DELEGATED QUERY ITSELF — there is no per-record filter because the
/// record is not known until the query resolves it. Denial is deliberately indistinguishable from
/// absence (a 404, never a 403): answering 403 would confirm the record exists, trading an IDOR for an
/// existence oracle.
/// </para>
/// <para>
/// ⚠️ Do not "restore" <c>IGenericEntityService</c> on these handlers to fix a permissions complaint.
/// If a caller cannot see a communication, that is the control working.
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

        // GET /api/office/communications/by-message-id/{internetMessageId}/suggestions
        // Task 042 (FR-B2) — the Outlook add-in "Save to Spaarke" picker + ribbon
        // quick-save use this to pre-select the Association Engine's predicted record.
        // Resolves the open email's internetMessageId → the captured sprk_communication,
        // then runs the SAME read-only evaluate path as
        // POST /api/communications/{id}/suggest-associations (CommunicationService.
        // ReconstructEnvelopeAsync + IncomingAssociationResolver.EvaluateAsync,
        // projected via SuggestAssociationsResponse.FromDecision) — no forked candidate
        // model (ADR-045 / §11). 404 when the email is not yet captured: the client then
        // opens the picker with NO pre-selection (FR-B2 no-prediction fallback).
        group.MapGet("/by-message-id/{internetMessageId}/suggestions", GetSuggestionsByMessageIdAsync)
            .WithName("GetCommunicationSuggestionsByMessageId")
            .WithDescription(
                "Resolve an email's RFC-5322 internetMessageId to its captured sprk_communication " +
                "and return the Association Engine's read-only regarding suggestions (predicted " +
                "record + alternates + confidence + provenance). 404 when the email is not yet captured.")
            .Produces<CommunicationSuggestionsResponse>(StatusCodes.Status200OK)
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
        IDataverseUserClient userClient,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var traceId = context.TraceIdentifier;
        var userId = CallerResolution.ResolveObjectId(context.User);

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
            var entity = await QueryCommunicationByMessageIdAsync(userClient, internetMessageId, ct);

            if (entity is null)
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

            var communicationId = ReadGuid(entity.Value, "sprk_communicationid");
            // Use sprk_subject if present, otherwise fall back to the empty string. The
            // client tolerates a missing subject (see communicationLookupService.ts L99).
            var subject = ReadString(entity.Value, "sprk_subject") ?? string.Empty;

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
    /// Query <c>sprk_communication</c> by <c>sprk_internetmessageid</c>, returning the single matching
    /// row (id + subject) or <c>null</c>. Shared by the by-message-id lookup and the suggestions handler
    /// so both resolve the email identically (task 042). Mirrors
    /// <c>DataverseServiceClientImpl.GetCommunicationByInternetMessageIdAsync</c> but selects the richer
    /// column set the callers need.
    /// </summary>
    /// <remarks>
    /// 🔴 DELEGATED (user-OBO) read — #1020 / task 127. Runs under the CALLER's Dataverse security
    /// context, so a communication the caller may not read simply does not come back and the handler
    /// returns its ordinary 404. That conflation is deliberate: answering 403 here would tell the
    /// caller the record EXISTS, trading an IDOR for an existence oracle — the separation task 022
    /// removed from bulk download and that <c>CallerRecordAccessProbe</c> refuses to reintroduce.
    /// <para>
    /// This previously used the app-only <c>IGenericEntityService</c>, so holding an internetMessageId
    /// — which every participant in an email thread holds — was the same thing as being allowed to
    /// read its Spaarke filing.
    /// </para>
    /// </remarks>
    private static async Task<JsonElement?> QueryCommunicationByMessageIdAsync(
        IDataverseUserClient userClient,
        string internetMessageId,
        CancellationToken ct)
    {
        // OData string literal: single-quotes doubled, then the whole value URL-encoded.
        var escaped = Uri.EscapeDataString(internetMessageId.Replace("'", "''"));
        var path =
            "sprk_communications"
            + "?$select=sprk_communicationid,sprk_subject,sprk_internetmessageid"
            + $"&$filter=sprk_internetmessageid eq '{escaped}'"
            + "&$top=1";

        var response = await userClient.GetAsync(path, ct);

        if (!response.IsSuccess)
        {
            // 401/403 means the delegated context itself is broken. Returning null would present that
            // as "this email was never captured", which the add-in shows as a normal no-preselection
            // state — a broken security context silently rendering as a working feature.
            if (response.StatusCode is 401 or 403)
            {
                throw new InvalidOperationException(
                    $"Communication lookup could not run under the caller's Dataverse security context "
                    + $"(HTTP {response.StatusCode}, {response.ErrorCode}).");
            }

            return null;
        }

        if (response.Body is not { } body || !body.TryGetProperty("value", out var rows))
        {
            return null;
        }

        foreach (var row in rows.EnumerateArray())
        {
            return row.Clone();
        }

        return null;
    }

    /// <summary>Reads a string property from a Dataverse OData row, or null when absent.</summary>
    private static string? ReadString(JsonElement row, string property)
        => row.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>Reads an integer (option-set) property from a Dataverse OData row; 0 when absent.</summary>
    private static int ReadInt(JsonElement row, string property)
        => row.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : 0;

    /// <summary>Reads a GUID property from a Dataverse OData row.</summary>
    private static Guid ReadGuid(JsonElement row, string property)
        => row.TryGetProperty(property, out var v)
           && v.ValueKind == JsonValueKind.String
           && Guid.TryParse(v.GetString(), out var g)
            ? g
            : Guid.Empty;

    /// <summary>
    /// Handler for <c>GET /api/office/communications/by-message-id/{internetMessageId}/suggestions</c>.
    /// Resolves the email to its captured <c>sprk_communication</c> then reuses the read-only Association
    /// Engine evaluate path (identical to <c>CommunicationEndpoints.SuggestAssociationsAsync</c>) — never
    /// writes the record, never forks the candidate model (ADR-045 / §11). 404 when the email is not yet
    /// captured so the client opens the picker with no pre-selection (FR-B2 fallback).
    /// </summary>
    private static async Task<IResult> GetSuggestionsByMessageIdAsync(
        string internetMessageId,
        IDataverseUserClient userClient,
        CommunicationService communicationService,
        IncomingAssociationResolver associationResolver,
        OfficeSearchService searchService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var traceId = context.TraceIdentifier;
        var userId = CallerResolution.ResolveObjectId(context.User);

        if (string.IsNullOrWhiteSpace(internetMessageId))
        {
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

        try
        {
            var entity = await QueryCommunicationByMessageIdAsync(userClient, internetMessageId, ct);
            if (entity is null)
            {
                // Not captured yet — the client opens the picker with NO pre-selection (FR-B2 fallback).
                logger.LogInformation(
                    "No sprk_communication for internetMessageId (suggestions), " +
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

            var communicationId = ReadGuid(entity.Value, "sprk_communicationid");
            var subject = ReadString(entity.Value, "sprk_subject") ?? string.Empty;

            // SAME read-only evaluate path as the Communication-group suggest endpoint — reuse, not fork — and the
            // SAME caller-scoped trimming (task 161): a candidate the caller cannot read is absent from the
            // decision itself, so neither its id nor a status computed with it reaches the response. The helper
            // also resolves each readable candidate's DISPLAY NAME (task 042 / FR-B2) — the persisted provenance
            // carries IDs, not names, and the shared candidate model (`derivePrimaryReview`) is designed to receive
            // `targetName` — from the same delegated read that decided it was readable.
            var (message, associationContext) = await communicationService.ReconstructEnvelopeAsync(communicationId, ct);
            var scoped = await SuggestionCandidateAccess.EvaluateForCallerAsync(
                communicationId, message, associationContext, associationResolver, userClient, logger, ct);
            var suggestions = scoped.Suggestions;
            var names = scoped.Names;

            // Task 084 (#1037): "pickable equals savable". The pane renders these candidates as selectable cards
            // and the ribbon quick-save files straight to the top one, so each named candidate also says whether
            // POST /api/office/save would accept it, from the save's own rights check (OfficeSearchService).
            var filingAccess = await ResolveCandidateFilingAccessAsync(
                searchService, suggestions.Candidates, names, TokenHelper.ExtractBearerTokenOrNull(context), ct);

            logger.LogInformation(
                "Returning engine suggestions for sprk_communication {CommunicationId} ({NameCount} names resolved, " +
                "{FilingCount} filing verdicts), UserId={UserId}, CorrelationId={CorrelationId}",
                communicationId, names.Count, filingAccess.Count, userId, traceId);

            return Results.Ok(new CommunicationSuggestionsResponse
            {
                CommunicationId = communicationId,
                Subject = subject,
                Suggestions = suggestions,
                Names = names,
                FilingAccess = filingAccess
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error building engine suggestions for internetMessageId, " +
                "UserId={UserId}, CorrelationId={CorrelationId}",
                userId, traceId);
            return Results.Problem(
                title: "Suggestions Failed",
                detail: "An unexpected error occurred while building association suggestions.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_INTERNAL",
                    ["correlationId"] = traceId
                });
        }
    }

    /// <summary>
    /// Whether the caller can file to each NAMED candidate (task 084), keyed by candidate <c>targetId</c>.
    /// </summary>
    /// <remarks>
    /// Only candidates that resolved a name are evaluated. Since task 161 a record the caller cannot read is not a
    /// candidate at all (<see cref="SuggestionCandidateAccess"/> removes it from the decision); a readable candidate
    /// with a blank name is one the client cannot label, so it is not offered for filing and asking whether it is
    /// fileable would cost a Dataverse call for nothing. The type is the candidate's LOGICAL name;
    /// <c>EntityAccessFilter.TryResolveEntitySet</c> maps it to the same collection as the friendly name the
    /// client sends to the save, so the verdict is the save's.
    /// </remarks>
    internal static async Task<IReadOnlyDictionary<string, bool>> ResolveCandidateFilingAccessAsync(
        OfficeSearchService searchService,
        IReadOnlyList<SuggestedCandidate> candidates,
        IReadOnlyDictionary<string, string> names,
        string? callerBearerToken,
        CancellationToken ct)
    {
        var targets = new List<(string EntityType, Guid RecordId)>();
        var targetIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            if (!names.ContainsKey(c.TargetId)) continue;
            if (!Guid.TryParse(c.TargetId, out var recordId)) continue;
            if (!seen.Add(c.TargetId)) continue;

            targets.Add((c.TargetEntity, recordId));
            targetIds.Add(c.TargetId);
        }

        var filingAccess = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 0)
            return filingAccess;

        var verdicts = await searchService.EvaluateFilingAccessAsync(targets, callerBearerToken, ct);
        for (var i = 0; i < targets.Count; i++)
        {
            // null = not checked (past the per-request cap): leave the key out, so the client treats it as today.
            if (verdicts[i] is { } canFile)
                filingAccess[targetIds[i]] = canFile;
        }

        return filingAccess;
    }

    /// <summary>
    /// Handler for <c>GET /api/office/communications/{commId:guid}/linked-todos</c>.
    /// </summary>
    private static async Task<IResult> GetLinkedTodosAsync(
        Guid commId,
        IDataverseUserClient userClient,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var traceId = context.TraceIdentifier;
        var userId = CallerResolution.ResolveObjectId(context.User);

        logger.LogInformation(
            "Listing linked todos for sprk_communication {CommunicationId}, " +
            "UserId={UserId}, CorrelationId={CorrelationId}",
            commId, userId, traceId);

        try
        {
            // 🔴 DELEGATED read (#1020 / task 127). This filtered on sprk_regardingcommunication
            // alone, app-only — so a caller holding any communication GUID received that email's
            // linked to-dos regardless of entitlement. Under the caller's own context Dataverse
            // returns only what they may see, and a communication they cannot see yields an empty
            // list — indistinguishable from one that simply has no to-dos, which is the intended
            // conflation.
            //
            // OData addresses a lookup column as _{name}_value.
            var path =
                "sprk_todos"
                + "?$select=sprk_todoid,sprk_name,statecode,statuscode"
                + $"&$filter=_sprk_regardingcommunication_value eq {commId}"
                + $"&$top={LinkedTodosTopCount}";

            var response = await userClient.GetAsync(path, ct);

            if (!response.IsSuccess)
            {
                if (response.StatusCode is 401 or 403)
                {
                    throw new InvalidOperationException(
                        $"Linked-todos lookup could not run under the caller's Dataverse security "
                        + $"context (HTTP {response.StatusCode}, {response.ErrorCode}).");
                }

                logger.LogWarning(
                    "Linked-todos lookup failed for {CommunicationId}: HTTP {StatusCode} {ErrorCode}",
                    commId, response.StatusCode, response.ErrorCode);
            }

            var todos = new List<LinkedTodoSummary>();
            if (response.IsSuccess
                && response.Body is { } todoBody
                && todoBody.TryGetProperty("value", out var todoRows))
            {
                foreach (var entity in todoRows.EnumerateArray())
                {
                    todos.Add(new LinkedTodoSummary
                    {
                        SprkTodoid = ReadGuid(entity, "sprk_todoid"),
                        SprkName = ReadString(entity, "sprk_name") ?? string.Empty,
                        Statecode = ReadInt(entity, "statecode"),
                        Statuscode = ReadInt(entity, "statuscode")
                    });
                }
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
/// Response DTO for <c>GET /api/office/communications/by-message-id/{internetMessageId}/suggestions</c>
/// (task 042 / FR-B2). Carries the resolved communication identity plus the read-only Association Engine
/// suggestion projection (<see cref="SuggestAssociationsResponse"/>) the add-in reconstructs into a
/// <c>ProvenanceDoc</c> and feeds to the shared <c>derivePrimaryReview</c> — the SAME candidate model the
/// code-page review surface uses (ADR-045; no fork). Default camelCase serialization yields
/// <c>communicationId</c>, <c>subject</c>, <c>suggestions</c> — matches the client.
/// </summary>
public sealed class CommunicationSuggestionsResponse
{
    /// <summary>The resolved sprk_communicationid GUID.</summary>
    public required Guid CommunicationId { get; init; }

    /// <summary>The sprk_subject of the resolved record (empty string if unset).</summary>
    public required string Subject { get; init; }

    /// <summary>The Association Engine's read-only regarding suggestions (candidates + signals + status).</summary>
    public required SuggestAssociationsResponse Suggestions { get; init; }

    /// <summary>
    /// Resolved display names keyed by candidate <c>targetId</c> (GUID string). The persisted provenance
    /// carries IDs, not names, so the client folds these into the candidate model's <c>targetName</c>
    /// (which is designed to be catalog-resolved). Absent keys fall back to the id (task 042).
    /// </summary>
    public IReadOnlyDictionary<string, string> Names { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Whether the caller can file a document to each named candidate, keyed by candidate <c>targetId</c>
    /// (task 084, #1037). <c>false</c> means <c>POST /api/office/save</c> would refuse it as the target: the pane
    /// shows it disabled with the reason, and the ribbon quick-save does not auto-file to it. An absent key means
    /// not checked; the client treats it as selectable and the save remains the enforcement.
    /// </summary>
    public IReadOnlyDictionary<string, bool> FilingAccess { get; init; } = new Dictionary<string, bool>();
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
