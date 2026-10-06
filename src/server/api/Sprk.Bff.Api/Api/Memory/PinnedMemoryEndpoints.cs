using Sprk.Bff.Api.Infrastructure.Authentication;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json.Serialization;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Memory;
using Sprk.Bff.Api.Services.Ai.Memory;

namespace Sprk.Bff.Api.Api.Memory;

/// <summary>
/// Pinned-memory CRUD endpoint pair (R6 Pillar 7 / Q7 SCOPE EXPANSION / task 070 PART A —
/// FR-47 Q7 expansion). Exposes the user-direct write surface for
/// <see cref="PinnedContextItem"/> pins; complements the chat-side
/// <c>ManagePinnedContextHandler</c> (task 069) which exposes the voice-command-driven
/// "remember / forget / always" affordance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Routes</b> (4 endpoints):
/// <list type="bullet">
///   <item><c>GET /api/memory/pins?matterId={matterId?}</c> — list the caller's pinned items.</item>
///   <item><c>POST /api/memory/pins</c> — create a new pinned item (201).</item>
///   <item><c>PUT /api/memory/pins/{pinId}</c> — update an existing pinned item (200; ONE 404 for unknown AND not-owned).</item>
///   <item><c>DELETE /api/memory/pins/{pinId}</c> — delete a pinned item (204; ONE 404 for unknown AND not-owned).</item>
/// </list>
/// </para>
/// <para>
/// <b>Tenant isolation (NFR-16 / ADR-014)</b>: tenant is derived ONLY from the caller's
/// <c>tid</c> claim — the endpoint NEVER accepts a <c>tenantId</c> from the request body
/// or query string. Cross-tenant reads/writes are structurally impossible. A missing
/// <c>tid</c> claim returns 401 ProblemDetails (mirrors the <c>InsightEndpoints.Ask</c> precedent).
/// </para>
/// <para>
/// <b>Ownership (Q7 Pillar 7 requirement)</b>: PUT/DELETE callers MUST own the pin. The
/// endpoint loads the pin via <see cref="IPinnedContextRepository.GetByIdAsync"/>, then
/// compares the pin's <see cref="PinnedContextItem.UserId"/> to the caller's <c>oid</c>
/// claim. <b>A pin the caller does not own gets the SAME 404 as a pin that does not exist</b>
/// (unified-access-control-r2 task 166, owner round 12 item 4): the earlier 403-for-not-yours /
/// 404-for-unknown split told any caller which pin ids existed for other users — an existence oracle.
/// </para>
/// <para>
/// <b>The MATTER a pin names is authorized as the CALLER (task 166, findings S-43 / S-68).</b> A pin
/// carrying a <c>matterId</c> is not private to its author: <c>PinnedContextRepository.GetByMatterAsync</c>
/// returns EVERY user's pins for that matter, and <c>ContextBinder</c> injects them into the prompt of
/// every user who chats on the matter as "authoritative context". Writing one is therefore attaching
/// content TO the matter, so it costs the caller's own <see cref="AccessRights.AppendTo"/> on
/// <c>sprk_matters(matterId)</c> — operation key <see cref="PinMatterOperation"/> — asked through
/// <see cref="CallerRecordAccessProbe"/> (OBO <c>RetrievePrincipalAccess</c>) BEFORE the repository
/// write, for EVERY pinType that carries a matter id (not only <c>matter-fact</c>), and on PUT for the
/// NEW matter id in the body. A non-GUID matter id is a 400 with no probe. An unknown matter and a
/// forbidden one get the identical 403 (the probe answers <see cref="AccessRights.None"/> for both).
/// </para>
/// <para>
/// <b>Auth model</b> (ADR-008): group-level <c>RequireAuthorization()</c> gates the 401
/// path; per-handler tid+oid claim extraction gates the 401-missing-claim path. The matter check
/// lives in the handler because its subject is a BODY id no route-value filter can read.
/// </para>
/// <para>
/// <b>Rate limit</b> (ADR-016): <c>ai-context</c> sliding window — 60 req/min per caller.
/// Same policy class as the chat/analysis context resolvers + workspace state. Read-heavy
/// for GET, low-volume for POST/PUT/DELETE.
/// </para>
/// <para>
/// <b>Placement</b> (CLAUDE.md §10 / ADR-013): consumes <see cref="IPinnedContextRepository"/>
/// only — memory plumbing, NOT AI-internal types. Zero <c>IOpenAiClient</c> /
/// <c>IPlaybookService</c> injection. The repository is AI-internal plumbing per the
/// 2026-05-20 refined ADR-013 boundary; the BFF-side endpoint is the user-direct surface
/// the Q7 UI consumes.
/// </para>
/// <para>
/// <b>Wiring</b> (ADR-010): registered via
/// <c>Infrastructure/DI/EndpointMappingExtensions.MapDomainEndpoints</c> alongside the
/// workspace endpoints. Zero new <c>Program.cs</c> lines (the
/// <see cref="MapPinnedMemoryEndpoints"/> call IS endpoint mapping, NOT DI registration).
/// </para>
/// <para>
/// <b>ADR-015 BINDING (telemetry)</b>: logging emits deterministic identifiers ONLY —
/// tenantId, userId, pinId, pinType, decision. NEVER pin <see cref="PinnedContextItem.Title"/>
/// body. NEVER pin <see cref="PinnedContextItem.Content"/> body. NEVER request body raw text.
/// Reuses the <c>Sprk.Bff.Api.Memory</c> Meter pattern established by task 069's
/// <c>ManagePinnedContextHandler</c> — the <c>memory.pin_created</c> + <c>memory.pin_deleted</c>
/// Counters are PUBLISHED on the SAME Meter; this endpoint adds a third Counter
/// <c>memory.pin_updated</c> for the new PUT path.
/// </para>
/// <para>
/// <b>ZERO new top-level DI registrations</b>: <see cref="IPinnedContextRepository"/> is
/// already registered in <c>AnalysisServicesModule</c> (task 065). The endpoint group is
/// the only addition — added inside the existing module's endpoint-mapping pass.
/// </para>
/// </remarks>
public static class PinnedMemoryEndpoints
{
    // ─────────────────────────────────────────────────────────────────────────
    // Telemetry — extends the Sprk.Bff.Api.Memory Meter introduced by task 069.
    //
    // Counter names mirror task 069's pattern:
    //   memory.pin_created (task 069 + task 070 PART A — published by BOTH the
    //     chat handler and the UI endpoint).
    //   memory.pin_deleted (task 069 + task 070 PART A — same).
    //   memory.pin_updated (NEW in task 070 PART A — the PUT path has no chat
    //     analog; the voice tool only supports create/delete).
    //
    // ADR-015 BINDING: dimension set = deterministic ids + enum-like discriminators ONLY.
    //   tenantId, userId, pinId, pinType, decision.
    //   NEVER title body. NEVER content body. NEVER request text.
    // ─────────────────────────────────────────────────────────────────────────
    internal const string MeterName = "Sprk.Bff.Api.Memory";
    private static readonly Meter _meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> _pinCreatedCounter = _meter.CreateCounter<long>(
        name: "memory.pin_created",
        unit: "{pin}",
        description: "Number of pinned-context items created (via task 070 Q7 UI endpoint OR task 069 voice command).");
    private static readonly Counter<long> _pinUpdatedCounter = _meter.CreateCounter<long>(
        name: "memory.pin_updated",
        unit: "{pin}",
        description: "Number of pinned-context items updated via the task 070 Q7 UI endpoint (no voice analog).");
    private static readonly Counter<long> _pinDeletedCounter = _meter.CreateCounter<long>(
        name: "memory.pin_deleted",
        unit: "{pin}",
        description: "Number of pinned-context items deleted (via task 070 Q7 UI endpoint OR task 069 voice command).");

    // ADR-015 decision discriminators (telemetry-safe enum-like values).
    internal const string DecisionCreated = "created";
    internal const string DecisionUpdated = "updated";
    internal const string DecisionDeleted = "deleted";

    /// <summary>Hard ceiling on the pin title length — mirrors <see cref="PinnedContextRepository.MaxTitleLength"/>.</summary>
    internal const int MaxTitleLength = 200;

    /// <summary>Hard ceiling on the pin content length — mirrors <see cref="PinnedContextRepository.MaxContentLength"/>.</summary>
    internal const int MaxContentLength = 1000;

    /// <summary>
    /// The <see cref="OperationAccessPolicy"/> key a pin's MATTER costs: AppendTo on <c>sprk_matters(matterId)</c>
    /// (unified-access-control-r2 task 166). A pin is injected into every other user's prompt for that matter,
    /// which is attaching content to the matter — the same right <c>entity.associate_document</c> demands for a
    /// document. A separate key so a deny log names the act it refused.
    /// </summary>
    internal const string PinMatterOperation = "memory.pin_matter";

    /// <summary>The Dataverse logical name whose entity set the pin's matter id is authorized against.</summary>
    private const string MatterLogicalName = "sprk_matter";

    /// <summary>The ONE detail every refused matter carries, whether it is unknown or merely forbidden.</summary>
    internal const string PinMatterDeniedDetail = "You do not have permission to pin content to this matter.";

    /// <summary>The ONE detail a pin the caller cannot reach carries, whether it is unknown or someone else's.</summary>
    internal const string PinNotFoundDetail = "The pin was not found.";

    /// <summary>Supported pinType wire strings (kebab-case, mirrors <see cref="PinType"/> JSON discriminators).</summary>
    internal static readonly HashSet<string> SupportedPinTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "user-preference",
        "system-rule",
        "matter-fact"
    };

    /// <summary>
    /// Registers the four pinned-memory endpoints on the supplied builder. Called from
    /// <c>EndpointMappingExtensions.MapDomainEndpoints</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapPinnedMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/memory")
            .RequireAuthorization()               // ADR-008: group-level auth filter
            .RequireRateLimiting("ai-context")    // ADR-016: read-heavy + low-volume mutate
            .WithTags("Pinned Memory");

        group.MapGet("/pins", ListPinsAsync)
            .WithName("ListPinnedMemoryItems")
            .WithSummary("List the caller's pinned memory items (R6 Pillar 7 / FR-47 Q7).")
            .WithDescription(
                "Returns all pinned items owned by the caller within their tenant. " +
                "Optional 'matterId' query parameter narrows the list to matter-fact pins for the " +
                "supplied matter (other pinTypes ignore matterId). Tenant scope is derived from the " +
                "caller's 'tid' claim; user scope from the caller's 'oid' claim. The response shape " +
                "matches the Q7 UI consumption contract (PART B task).")
            .Produces<PinListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/pins", CreatePinAsync)
            .WithName("CreatePinnedMemoryItem")
            .WithSummary("Create a new pinned memory item (R6 Pillar 7 / FR-47 Q7).")
            .WithDescription(
                "Persists a new pinned-context item under the caller's tenant + user. The pin will " +
                "appear in subsequent chat-session memory composition (task 067).")
            .Produces<PinResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPut("/pins/{pinId}", UpdatePinAsync)
            .WithName("UpdatePinnedMemoryItem")
            .WithSummary("Update an existing pinned memory item (R6 Pillar 7 / FR-47 Q7).")
            .WithDescription(
                "Caller MUST own the pin (UserId match against the caller's 'oid' claim). An unknown pin and " +
                "another user's pin get the SAME 404. A matterId in the body costs the caller's AppendTo on that " +
                "matter (403 otherwise).")
            .Produces<PinResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapDelete("/pins/{pinId}", DeletePinAsync)
            .WithName("DeletePinnedMemoryItem")
            .WithSummary("Delete a pinned memory item (R6 Pillar 7 / FR-47 Q7).")
            .WithDescription(
                "Caller MUST own the pin (UserId match against the caller's 'oid' claim). An unknown pin and " +
                "another user's pin get the SAME 404 (owner round 12 item 4 — no existence oracle).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    // ==========================================================================
    // GET /api/memory/pins?matterId={matterId?}
    // ==========================================================================
    private static async Task<IResult> ListPinsAsync(
        HttpContext httpContext,
        IPinnedContextRepository repository,
        ILogger<PinListResponse> logger,
        CancellationToken ct,
        string? matterId = null)
    {
        if (!TryGetTenantAndUser(httpContext, out var tenantId, out var userId, out var claimProblem))
        {
            return claimProblem!;
        }

        try
        {
            var userPins = await repository.GetByUserAsync(tenantId, userId, ct);

            IReadOnlyList<PinnedContextItem> result = userPins;
            if (!string.IsNullOrWhiteSpace(matterId))
            {
                // For matter-fact pins, narrow to the supplied matter. Other pin types are
                // returned unchanged (matterId filter is additive — system-rule + user-preference
                // pins remain visible because they apply broadly).
                result = userPins
                    .Where(p => p.PinType != PinType.MatterFact || string.Equals(p.MatterId, matterId, StringComparison.Ordinal))
                    .ToList();
            }

            // ADR-015 BINDING: log deterministic identifiers + counts ONLY.
            logger.LogDebug(
                "[PINNED-MEMORY] list tenant={TenantId} user={UserId} matterFilter={MatterFilter} count={Count}",
                tenantId, userId, matterId is not null, result.Count);

            var response = new PinListResponse(
                Items: result.Select(ToDto).ToList(),
                Count: result.Count);
            return Results.Ok(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // ADR-015 BINDING: log exception TYPE only, never repository inner-exception text
            // that could include user-authored content.
            logger.LogError(ex,
                "[PINNED-MEMORY] list failed tenant={TenantId} user={UserId} errorType={ErrorType}",
                tenantId, userId, ex.GetType().Name);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to list pinned memory items. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "PINNED_MEMORY_LIST_INTERNAL_ERROR",
                    ["correlationId"] = httpContext.TraceIdentifier,
                });
        }
    }

    // ==========================================================================
    // POST /api/memory/pins
    // ==========================================================================
    private static async Task<IResult> CreatePinAsync(
        HttpContext httpContext,
        IPinnedContextRepository repository,
        CallerRecordAccessProbe probe,
        TimeProvider timeProvider,
        ILogger<PinResponse> logger,
        CreatePinRequest? request,
        CancellationToken ct)
    {
        if (!TryGetTenantAndUser(httpContext, out var tenantId, out var userId, out var claimProblem))
        {
            return claimProblem!;
        }

        if (!TryValidateCreateRequest(request, out var validationProblem, out var pinType))
        {
            return validationProblem!;
        }

        // Task 166 (S-43): the matter a pin names is authorized AS THE CALLER, before the repository write,
        // whatever the pinType. A request with no matterId is a private pin and is unchanged.
        var matterDenied = await AuthorizePinMatterAsync(request!.MatterId, probe, httpContext, logger, ct);
        if (matterDenied is not null)
        {
            return matterDenied;
        }

        // request is non-null after TryValidateCreateRequest succeeds.
        var pinId = Guid.NewGuid().ToString("N");
        var now = timeProvider.GetUtcNow();
        var pin = new PinnedContextItem
        {
            Id = PinnedContextRepository.BuildDocumentId(tenantId, pinId),
            DocumentType = PinnedContextRepository.DocumentTypeValue,
            TenantId = tenantId,
            UserId = userId,
            PinType = pinType,
            Title = request!.Title!,
            Content = request.Content!,
            MatterId = string.IsNullOrWhiteSpace(request.MatterId) ? null : request.MatterId,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId,
        };

        try
        {
            await repository.CreateAsync(pin, ct);

            _pinCreatedCounter.Add(1,
                new KeyValuePair<string, object?>("tenantId", tenantId),
                new KeyValuePair<string, object?>("userId", userId),
                new KeyValuePair<string, object?>("pinId", pinId),
                new KeyValuePair<string, object?>("pinType", PinTypeToWire(pinType)),
                new KeyValuePair<string, object?>("decision", DecisionCreated));

            // ADR-015 BINDING: log deterministic identifiers ONLY. Title length + content length
            // are length-class indicators (numeric); the strings themselves NEVER hit the sink.
            logger.LogInformation(
                "[PINNED-MEMORY] created tenant={TenantId} user={UserId} pinId={PinId} pinType={PinType} titleLen={TitleLen} contentLen={ContentLen}",
                tenantId, userId, pinId, PinTypeToWire(pinType), pin.Title.Length, pin.Content.Length);

            return Results.Created($"/api/memory/pins/{pinId}", new PinResponse(ToDto(pin)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            // Repository length-cap or null-arg violations — surface as 400.
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: ex.Message,
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PINNED-MEMORY] create failed tenant={TenantId} user={UserId} errorType={ErrorType}",
                tenantId, userId, ex.GetType().Name);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to create pinned memory item. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "PINNED_MEMORY_CREATE_INTERNAL_ERROR",
                    ["correlationId"] = httpContext.TraceIdentifier,
                });
        }
    }

    // ==========================================================================
    // PUT /api/memory/pins/{pinId}
    // ==========================================================================
    private static async Task<IResult> UpdatePinAsync(
        HttpContext httpContext,
        IPinnedContextRepository repository,
        CallerRecordAccessProbe probe,
        TimeProvider timeProvider,
        ILogger<PinResponse> logger,
        string pinId,
        UpdatePinRequest? request,
        CancellationToken ct)
    {
        if (!TryGetTenantAndUser(httpContext, out var tenantId, out var userId, out var claimProblem))
        {
            return claimProblem!;
        }

        if (string.IsNullOrWhiteSpace(pinId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "'pinId' route parameter is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        if (!TryValidateUpdateRequest(request, out var validationProblem, out var pinType))
        {
            return validationProblem!;
        }

        try
        {
            var existing = await repository.GetByIdAsync(tenantId, pinId, ct);
            if (existing is null)
            {
                return PinNotFound();
            }

            // Pillar 7 ownership invariant — caller's oid MUST match the pin's UserId. Not-yours is answered
            // EXACTLY as not-found (task 166, owner round 12 item 4): a 403 here confirmed that another user's
            // pin existed under that id. No matter probe is made for a pin the caller does not own.
            if (!string.Equals(existing.UserId, userId, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "[PINNED-MEMORY] update refused_not_owned tenant={TenantId} caller={CallerUserId} owner={OwnerUserId} pinId={PinId} (answered 404)",
                    tenantId, userId, existing.UserId, pinId);
                return PinNotFound();
            }

            // Task 166 (S-68): the NEW matter in the body is authorized as the caller on every update that
            // carries one — re-pointing an owned pin at a matter is the same attach as creating one there.
            var matterDenied = await AuthorizePinMatterAsync(request!.MatterId, probe, httpContext, logger, ct);
            if (matterDenied is not null)
            {
                return matterDenied;
            }

            var now = timeProvider.GetUtcNow();
            var updated = new PinnedContextItem
            {
                Id = existing.Id,
                DocumentType = existing.DocumentType,
                TenantId = existing.TenantId,
                UserId = existing.UserId,
                PinType = pinType,
                Title = request!.Title!,
                Content = request.Content!,
                MatterId = string.IsNullOrWhiteSpace(request.MatterId) ? null : request.MatterId,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = now,
                CreatedBy = existing.CreatedBy,
                ETag = existing.ETag,
            };

            await repository.UpdateAsync(updated, ct);

            _pinUpdatedCounter.Add(1,
                new KeyValuePair<string, object?>("tenantId", tenantId),
                new KeyValuePair<string, object?>("userId", userId),
                new KeyValuePair<string, object?>("pinId", pinId),
                new KeyValuePair<string, object?>("pinType", PinTypeToWire(pinType)),
                new KeyValuePair<string, object?>("decision", DecisionUpdated));

            logger.LogInformation(
                "[PINNED-MEMORY] updated tenant={TenantId} user={UserId} pinId={PinId} pinType={PinType} titleLen={TitleLen} contentLen={ContentLen}",
                tenantId, userId, pinId, PinTypeToWire(pinType), updated.Title.Length, updated.Content.Length);

            return Results.Ok(new PinResponse(ToDto(updated)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: ex.Message,
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PINNED-MEMORY] update failed tenant={TenantId} user={UserId} pinId={PinId} errorType={ErrorType}",
                tenantId, userId, pinId, ex.GetType().Name);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to update pinned memory item. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "PINNED_MEMORY_UPDATE_INTERNAL_ERROR",
                    ["correlationId"] = httpContext.TraceIdentifier,
                });
        }
    }

    // ==========================================================================
    // DELETE /api/memory/pins/{pinId}
    // ==========================================================================
    private static async Task<IResult> DeletePinAsync(
        HttpContext httpContext,
        IPinnedContextRepository repository,
        ILogger<PinResponse> logger,
        string pinId,
        CancellationToken ct)
    {
        if (!TryGetTenantAndUser(httpContext, out var tenantId, out var userId, out var claimProblem))
        {
            return claimProblem!;
        }

        if (string.IsNullOrWhiteSpace(pinId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "'pinId' route parameter is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        try
        {
            var existing = await repository.GetByIdAsync(tenantId, pinId, ct);
            if (existing is null)
            {
                return PinNotFound();
            }

            // Owner round 12 item 4 (task 166): another user's pin is answered EXACTLY as an unknown one. The
            // former 404/403 split was a pin-existence oracle across users.
            if (!string.Equals(existing.UserId, userId, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "[PINNED-MEMORY] delete refused_not_owned tenant={TenantId} caller={CallerUserId} owner={OwnerUserId} pinId={PinId} (answered 404)",
                    tenantId, userId, existing.UserId, pinId);
                return PinNotFound();
            }

            await repository.DeleteAsync(tenantId, pinId, ct);

            _pinDeletedCounter.Add(1,
                new KeyValuePair<string, object?>("tenantId", tenantId),
                new KeyValuePair<string, object?>("userId", userId),
                new KeyValuePair<string, object?>("pinId", pinId),
                new KeyValuePair<string, object?>("pinType", PinTypeToWire(existing.PinType)),
                new KeyValuePair<string, object?>("decision", DecisionDeleted));

            logger.LogInformation(
                "[PINNED-MEMORY] deleted tenant={TenantId} user={UserId} pinId={PinId} pinType={PinType}",
                tenantId, userId, pinId, PinTypeToWire(existing.PinType));

            return Results.NoContent();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PINNED-MEMORY] delete failed tenant={TenantId} user={UserId} pinId={PinId} errorType={ErrorType}",
                tenantId, userId, pinId, ex.GetType().Name);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to delete pinned memory item. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "PINNED_MEMORY_DELETE_INTERNAL_ERROR",
                    ["correlationId"] = httpContext.TraceIdentifier,
                });
        }
    }

    // ==========================================================================
    // Helpers — claim extraction, validation, mapping
    // ==========================================================================

    private static bool TryGetTenantAndUser(
        HttpContext httpContext,
        out string tenantId,
        out string userId,
        out IResult? problem)
    {
        tenantId = string.Empty;
        userId = string.Empty;
        problem = null;

        var tid = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        if (string.IsNullOrWhiteSpace(tid))
        {
            problem = Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Tenant identity ('tid' claim) not found in authentication token.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
            return false;
        }

        var oid = CallerResolution.ResolveObjectId(httpContext.User);
        if (string.IsNullOrWhiteSpace(oid))
        {
            problem = Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity ('oid' claim) not found in authentication token.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
            return false;
        }

        tenantId = tid;
        userId = oid;
        return true;
    }

    private static bool TryValidateCreateRequest(
        CreatePinRequest? request,
        out IResult? problem,
        out PinType pinType)
    {
        pinType = PinType.UserPreference;
        problem = null;

        if (request is null)
        {
            problem = Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Request body is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            problem = ValidationProblem("'title' is required.");
            return false;
        }
        if (request.Title.Length > MaxTitleLength)
        {
            problem = ValidationProblem($"'title' length {request.Title.Length} exceeds the maximum {MaxTitleLength}.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            problem = ValidationProblem("'content' is required.");
            return false;
        }
        if (request.Content.Length > MaxContentLength)
        {
            problem = ValidationProblem($"'content' length {request.Content.Length} exceeds the maximum {MaxContentLength}.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.PinType) || !SupportedPinTypes.Contains(request.PinType))
        {
            problem = ValidationProblem(
                $"'pinType' must be one of: {string.Join(", ", SupportedPinTypes)}. Received: '{request.PinType}'.");
            return false;
        }
        pinType = WireToPinType(request.PinType);

        // Matter-fact pins MUST carry a matterId.
        if (pinType == PinType.MatterFact && string.IsNullOrWhiteSpace(request.MatterId))
        {
            problem = ValidationProblem("'matterId' is required when pinType = 'matter-fact'.");
            return false;
        }

        // Task 166: a matterId — on ANY pinType — must be a GUID, because it is authorized against
        // sprk_matters(matterId) as the caller before anything is written. Refused here, before the pin
        // lookup and before any probe, so a malformed id costs nothing and discloses nothing.
        if (!string.IsNullOrWhiteSpace(request.MatterId) && !Guid.TryParse(request.MatterId, out _))
        {
            problem = Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["matterId"] = ["'matterId' must be the GUID of a matter."],
                },
                title: "Bad Request",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Task 166 (S-43 / S-68): when a pin request names a matter, the CALLER must hold
    /// <see cref="AccessRights.AppendTo"/> on <c>sprk_matters(matterId)</c> (operation
    /// <see cref="PinMatterOperation"/>). Returns <see langword="null"/> to proceed, or the 403 to return.
    /// </summary>
    /// <remarks>
    /// <para><b>No matter, no check.</b> A pin without a matter id is private to its author and is
    /// unchanged by this task.</para>
    /// <para><b>One answer.</b> An unknown matter, a matter the caller cannot see, a caller holding Read and
    /// Write but not AppendTo, a missing bearer token and a probe fault all return the SAME 403 — the probe
    /// already collapses "could not answer" to <see cref="AccessRights.None"/>, and a throw is folded in here
    /// rather than reaching the handler's 500. The id is never echoed.</para>
    /// <para>The stored matter id stays the string as sent (validated as a GUID upstream), so existing pins
    /// keep matching <c>PinnedContextRepository.GetByMatterAsync</c>'s ordinal equality.</para>
    /// </remarks>
    internal static async Task<IResult?> AuthorizePinMatterAsync(
        string? matterIdText,
        CallerRecordAccessProbe probe,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(matterIdText))
        {
            return null;
        }

        // Validated as a GUID by TryValidateCreateRequest; parsed again here so this helper cannot be the
        // weak link if a future caller skips validation — an unparseable id DENIES.
        if (!Guid.TryParse(matterIdText, out var matterId) || matterId == Guid.Empty
            || !EntityAccessFilter.TryResolveEntitySet(MatterLogicalName, out var entitySet))
        {
            return PinMatterDenied(httpContext);
        }

        AccessRights rights;
        try
        {
            rights = await probe.GetCallerRightsAsync(
                TokenHelper.ExtractBearerTokenOrNull(httpContext), entitySet, matterId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The DECISION only — the repository write is never reached on this path.
            logger.LogError(ex,
                "[PINNED-MEMORY] matter rights probe threw for {EntitySet}({MatterId}); denying. CorrelationId={CorrelationId}",
                entitySet, matterId, httpContext.TraceIdentifier);
            return PinMatterDenied(httpContext);
        }

        if (!OperationAccessPolicy.HasRequiredRights(rights, PinMatterOperation))
        {
            logger.LogWarning(
                "[PINNED-MEMORY] pin refused: caller lacks AppendTo on {EntitySet}({MatterId}); holds {Rights}. CorrelationId={CorrelationId}",
                entitySet, matterId, rights, httpContext.TraceIdentifier);
            return PinMatterDenied(httpContext);
        }

        return null;
    }

    private static IResult PinMatterDenied(HttpContext httpContext)
        => ProblemDetailsHelper.Forbidden("insufficient_rights", PinMatterDeniedDetail, httpContext.TraceIdentifier);

    private static IResult PinNotFound()
        => Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: PinNotFoundDetail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");

    private static bool TryValidateUpdateRequest(
        UpdatePinRequest? request,
        out IResult? problem,
        out PinType pinType)
    {
        // Update shares the same shape requirements as Create — delegate.
        var createShape = new CreatePinRequest
        {
            Title = request?.Title,
            Content = request?.Content,
            PinType = request?.PinType,
            MatterId = request?.MatterId,
        };
        return TryValidateCreateRequest(createShape, out problem, out pinType);
    }

    private static IResult ValidationProblem(string detail)
        => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");

    internal static PinType WireToPinType(string wire) => wire.ToLowerInvariant() switch
    {
        "user-preference" => PinType.UserPreference,
        "system-rule" => PinType.SystemRule,
        "matter-fact" => PinType.MatterFact,
        _ => throw new ArgumentException($"Unrecognised pinType wire value '{wire}'.", nameof(wire))
    };

    internal static string PinTypeToWire(PinType pinType) => pinType switch
    {
        PinType.UserPreference => "user-preference",
        PinType.SystemRule => "system-rule",
        PinType.MatterFact => "matter-fact",
        _ => throw new ArgumentException($"Unrecognised PinType '{pinType}'.", nameof(pinType))
    };

    internal static PinDto ToDto(PinnedContextItem pin)
    {
        // The wire id is the {pinId} portion of the Cosmos document id, NOT the full doc id.
        // Recovered via prefix stripping; mirrors ManagePinnedContextHandler.ExtractPinIdFromDocumentId.
        var prefix = $"{PinnedContextRepository.IdPrefix}_{pin.TenantId}_";
        var wireId = pin.Id.StartsWith(prefix, StringComparison.Ordinal)
            ? pin.Id.Substring(prefix.Length)
            : pin.Id;

        return new PinDto(
            PinId: wireId,
            PinType: PinTypeToWire(pin.PinType),
            Title: pin.Title,
            Content: pin.Content,
            MatterId: pin.MatterId,
            CreatedAt: pin.CreatedAt,
            UpdatedAt: pin.UpdatedAt,
            CreatedBy: pin.CreatedBy);
    }
}

// ==========================================================================
// DTOs — wire contracts consumed by the Q7 UI (PART B)
// ==========================================================================

/// <summary>Request body for <c>POST /api/memory/pins</c>.</summary>
public sealed class CreatePinRequest
{
    /// <summary>Short user-facing label (≤200 chars).</summary>
    [Required]
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>User-authored body content (≤1000 chars).</summary>
    [Required]
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>Kebab-case pin classification: 'user-preference' | 'system-rule' | 'matter-fact'.</summary>
    [Required]
    [JsonPropertyName("pinType")]
    public string? PinType { get; init; }

    /// <summary>Required when pinType = 'matter-fact'; ignored otherwise.</summary>
    [JsonPropertyName("matterId")]
    public string? MatterId { get; init; }
}

/// <summary>Request body for <c>PUT /api/memory/pins/{pinId}</c>. Same shape as create.</summary>
public sealed class UpdatePinRequest
{
    [Required]
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [Required]
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [Required]
    [JsonPropertyName("pinType")]
    public string? PinType { get; init; }

    [JsonPropertyName("matterId")]
    public string? MatterId { get; init; }
}

/// <summary>Single-pin response wrapper (POST 201 + PUT 200).</summary>
public sealed record PinResponse(
    [property: JsonPropertyName("item")] PinDto Item);

/// <summary>List response wrapper (GET 200).</summary>
public sealed record PinListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<PinDto> Items,
    [property: JsonPropertyName("count")] int Count);

/// <summary>
/// User-facing pin DTO. The <see cref="PinId"/> is the stable identifier (the <c>{pinId}</c>
/// portion of the Cosmos document id) — NOT the full document id. Mirrors the
/// <c>ManagePinnedContextHandler</c> chat-tool payload's <c>pinId</c> field.
/// </summary>
public sealed record PinDto(
    [property: JsonPropertyName("pinId")] string PinId,
    [property: JsonPropertyName("pinType")] string PinType,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("matterId")] string? MatterId,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("createdBy")] string CreatedBy);
