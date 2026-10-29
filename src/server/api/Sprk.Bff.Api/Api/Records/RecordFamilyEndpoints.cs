using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using FamilyKind = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.FamilyKind;

namespace Sprk.Bff.Api.Api.Records;

/// <summary>
/// field-mapping-server-write-path-r1 task 072 (FR-31; design §12.3 C, §12.12) —
/// <c>GET /api/v1/records/{table}/{id}/family</c>: one page of a record's Filing family, read AS THE CALLER by
/// <see cref="RecordFamilyReader"/>. A thin endpoint: the walk, the pruning (UAC F2) and the continuation live in the reader;
/// this file owns the request contract, the caller, and the existence-equalised gate.
/// </summary>
/// <remarks>
/// <para><b>The /api/v1/records group, in its own file.</b> The same group conventions as <see cref="Sprk.Bff.Api.Api.ExternalAccess.RecordNoAccessEndpoint"/>
/// (<c>dataverse-query</c> rate limit, <c>RequireAuthorization</c>), but that file keeps its one No Access responsibility
/// (CLAUDE.md §11.5): this route is not added there.</para>
///
/// <para><b>The gate is in the handler, not a route filter.</b> <c>RecordRouteAccessAuthorizationFilter</c>'s fixed-entity-set
/// form cannot take a table supplied by the route, and its declared-target form denies with a 403, the wrong shape. So the
/// handler asks the caller's Read on the anchor itself, through <see cref="CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync"/>
/// (#2137: an unknown id and a denied one take the same work), before the reader reads anything.</para>
///
/// <para><b>Order</b> (each step is fixed so that no answer depends on whether a record exists): (1) the query is parsed — a
/// bad <c>kind</c> or <c>direction</c> is a 400 before any probe or Dataverse call; (2) the caller is resolved (Entra oid from
/// the token, Dataverse systemuserid by WhoAmI on the caller's own token, the /push pattern); (3) a table that is not a family
/// table is the uniform 404 — table names are public, so that branch needs no probe; (4) the equalised Read probe on the anchor;
/// (5) the read. Every refusal of 3 and 4, and an anchor that vanishes after the probe, is
/// <see cref="ProblemDetailsHelper.UniformRecordNotFound"/>, byte-identical apart from its correlation id; the detail (table,
/// id, decision) goes to the log, never the body (ADR-019). A continuation that is malformed, tampered, re-bound or stale is
/// ONE 400 body.</para>
///
/// <para><b>The response</b> is what <c>recordFamilyService.ts</c> (<c>parseFamilyResponse</c>) parses: <c>anchor</c>, an
/// optional <c>root</c> (the read's top on the full view, omitted on <c>down</c>; nothing says whether a record exists above it),
/// <c>truncated</c>, <c>continuation</c> and <c>members[]</c>. No access metadata and no per-table counts.</para>
/// </remarks>
public static class RecordFamilyEndpoints
{
    /// <summary>The route below <c>/api/v1/records</c>.</summary>
    internal const string FamilyRoute = "/{table}/{id:guid}/family";

    /// <summary>The longest continuation accepted. A real token is a few hundred characters; anything longer is malformed.</summary>
    internal const int MaxContinuationLength = 4096;

    private const string KindQuery = "kind";
    private const string DirectionQuery = "direction";
    private const string ContinuationQuery = "continuation";
    private const string LogMarker = "[RECORD-FAMILY-ENDPOINT]";

    /// <summary>Registers the family route on the <c>/api/v1/records</c> group.</summary>
    public static void MapRecordFamilyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/records")
            .WithTags("Record Family")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        group.MapGet(FamilyRoute, GetFamilyAsync)
            .WithName("GetRecordFamily")
            .WithSummary("One page of a record's family (the records filed with it), as the caller may read them")
            .WithDescription(
                "The anchor, the highest ancestor the caller can read (root, full view only) and the members below it, each with " +
                "its parent and depth. Read as the caller: a record the caller cannot read is never listed, and nothing hints at it. " +
                "Query: kind (Filing, the default), direction (full, the default, or down), continuation (opaque, from the previous page). " +
                "A caller without Read on the anchor, an anchor that does not exist and a table that is not a family table get the same 404.")
            .Produces<RecordFamilyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    internal static async Task<IResult> GetFamilyAsync(
        string table,
        Guid id,
        HttpContext httpContext,
        RecordFamilyReader reader,
        CallerRecordAccessProbe probe,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // (1) The query, before any probe or Dataverse call. Fixed texts: the input is never echoed.
        var query = httpContext.Request.Query;
        if (query.TryGetValue(KindQuery, out var kinds)
            && (kinds.Count != 1 || !string.Equals(kinds[0], nameof(FamilyKind.Filing), StringComparison.OrdinalIgnoreCase)))
        {
            return ProblemDetailsHelper.ValidationError("The family kind is not supported.");
        }

        var direction = RecordFamilyDirection.Full;
        if (query.TryGetValue(DirectionQuery, out var directions))
        {
            if (directions.Count == 1 && string.Equals(directions[0], "full", StringComparison.OrdinalIgnoreCase))
                direction = RecordFamilyDirection.Full;
            else if (directions.Count == 1 && string.Equals(directions[0], "down", StringComparison.OrdinalIgnoreCase))
                direction = RecordFamilyDirection.Down;
            else
                return ProblemDetailsHelper.ValidationError("The family direction is not supported.");
        }

        string? continuation = null;
        if (query.TryGetValue(ContinuationQuery, out var continuations))
        {
            if (continuations.Count != 1 || continuations[0]!.Length > MaxContinuationLength)
                return InvalidContinuation();
            continuation = string.IsNullOrEmpty(continuations[0]) ? null : continuations[0];
        }

        // (2) The caller: the Entra object id binds the continuation; the Dataverse systemuserid is impersonated by every read.
        // The oid is CallerResolution's (never a hand-rolled claim read); an unidentifiable caller is a 401, not a 404.
        if (CallerResolution.ResolveObjectIdGuid(httpContext.User) is not { } objectId || objectId == Guid.Empty)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity not found.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        var token = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        var systemUserId = await ResolveSystemUserIdAsync(probe, token, logger, ct).ConfigureAwait(false);
        if (systemUserId is null)
        {
            logger.LogWarning(
                "{Marker} {Table} {RecordId}: the caller's Dataverse systemuserid could not be resolved, so nothing can be read as " +
                "the caller. Answered 404 (no app-only fallback).", LogMarker, Truncate(table), id);
            return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
        }

        // (3) The table gate. Which tables are family tables is public (the registry is in the repository), so refusing one
        // reveals nothing about any record and needs no probe. An empty id names nothing.
        var anchorTable = table.Trim().ToLowerInvariant();
        if (id == Guid.Empty || !RecordFamilyReader.TryGetFamilyEntitySet(anchorTable, FamilyKind.Filing, out var entitySet))
        {
            logger.LogInformation(
                "{Marker} {Table} {RecordId}: not a family table (or an empty id). Answered 404.", LogMarker, Truncate(table), id);
            return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
        }

        // (4) The caller's Read on the anchor, on the equalised schedule: an unknown id and one the caller may not read take the
        // same work, and anything but Read is the same 404. The entity set is the reader's own (every family table has one),
        // not EntityAccessFilter's smaller map, which would refuse every anchor but a core, an event or a todo.
        AccessRights rights;
        try
        {
            rights = await probe.GetCallerRightsForAuthorizationGateAsync(token, entitySet, id, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Marker} {Table} {RecordId}: the caller-rights probe threw. Answered 404.", LogMarker, anchorTable, id);
            return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
        }

        if (!OperationAccessPolicy.HasRequiredRights(rights, MappingSourceReadProbe.ReadOperation))
        {
            logger.LogInformation(
                "{Marker} {Table} {RecordId}: caller holds {Rights}; Read required. Answered 404 (unknown and unreadable are one answer).",
                LogMarker, anchorTable, id, rights);
            return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
        }

        // (5) The read. A Dataverse fault propagates to the ProblemDetails middleware; nothing is retried app-only.
        var read = await reader.ReadAsync(
            new RecordFamilyCaller(objectId, systemUserId.Value), FamilyKind.Filing, anchorTable, id, direction, continuation, ct)
            .ConfigureAwait(false);

        switch (read.Outcome)
        {
            case RecordFamilyReadOutcome.Read when read.Anchor is { } anchor && read.Top is { } top:
                return TypedResults.Ok(new RecordFamilyResponse(
                    ToRef(anchor),
                    direction == RecordFamilyDirection.Full ? ToRef(top) : null,
                    read.Truncated,
                    read.Continuation,
                    read.Members.Select(ToMember).ToList()));

            case RecordFamilyReadOutcome.ContinuationInvalid:
                logger.LogInformation("{Marker} {Table} {RecordId}: the continuation was refused. Answered 400.", LogMarker, anchorTable, id);
                return InvalidContinuation();

            case RecordFamilyReadOutcome.KindNotSupported:
                // Cannot happen after step 1; answered as a bad kind all the same.
                return ProblemDetailsHelper.ValidationError("The family kind is not supported.");

            default:
                // AnchorNotFound: the record vanished (or became unreadable) between the probe and the read. One answer.
                logger.LogInformation(
                    "{Marker} {Table} {RecordId}: the anchor could not be read after the probe ({Outcome}). Answered 404.",
                    LogMarker, anchorTable, id, read.Outcome);
                return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
        }
    }

    /// <summary>The caller's Dataverse systemuserid (WhoAmI on their own token), or null when it cannot be established.</summary>
    private static async Task<Guid?> ResolveSystemUserIdAsync(
        CallerRecordAccessProbe probe, string? token, ILogger logger, CancellationToken ct)
    {
        try
        {
            var systemUserId = await probe.GetCallerSystemUserIdAsync(token, ct).ConfigureAwait(false);
            return systemUserId is { } id && id != Guid.Empty ? id : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Marker} The caller's systemuserid lookup faulted.", LogMarker);
            return null;
        }
    }

    /// <summary>ONE fixed 400 for every continuation that is malformed, oversize, tampered, re-bound or stale.</summary>
    private static IResult InvalidContinuation() => ProblemDetailsHelper.ValidationError("The continuation is not valid.");

    private static string Truncate(string value) => value.Length <= 64 ? value : value[..64];

    private static RecordFamilyRef ToRef(RecordFamilyMember member) => new(member.Id, member.Table, member.Name);

    private static RecordFamilyMemberDto ToMember(RecordFamilyMember member) => new(
        member.Id,
        member.Table,
        member.Name,
        member.RecordNumber,
        // The display name of the member's TABLE ("Matter"), never a per-record value. No existing source covers the family
        // tables without a Dataverse metadata call per request (MetadataService projects no table label; the static maps list
        // eight tables), so it is null until one does (task 072 notes, open item).
        RecordType: null,
        member.Status,
        member.ModifiedOn,
        member.Kind.ToString(),
        member.EdgeKind?.ToString(),
        member.Parent?.Id,
        member.Depth);
}

/// <summary>A record named by id and table, with its display name when the caller may read it.</summary>
internal sealed record RecordFamilyRef(Guid Id, string Table, string? Name);

/// <summary>One family member as the wire carries it (camelCase; <c>recordFamilyService.ts</c> <c>IFamilyMember</c>). No access metadata.</summary>
internal sealed record RecordFamilyMemberDto(
    Guid Id,
    string Table,
    string? Name,
    string? RecordNumber,
    string? RecordType,
    string? Status,
    DateTimeOffset? ModifiedOn,
    string Kind,
    string? EdgeKind,
    Guid? ParentId,
    int Depth);

/// <summary>The FR-31 response body (<c>recordFamilyService.ts</c> <c>IFamilyResponse</c>).</summary>
internal sealed record RecordFamilyResponse(
    RecordFamilyRef Anchor,
    RecordFamilyRef? Root,
    bool Truncated,
    string? Continuation,
    IReadOnlyList<RecordFamilyMemberDto> Members);
