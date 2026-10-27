using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Signals;

/// <summary>
/// <c>GET /api/v1/signals/worklist</c> (ontology platform R1 task 038; spec FR-24, FR-54; D-15): the ONE route the worklist
/// reads Signals through. Each lane's visible Signals grouped by core record, with the display columns a row needs.
/// </summary>
/// <remarks>
/// <para><b>Authorization (NFR-10, D-29).</b> <c>RequireAuthorization</c> on the group (no token: 401). The record-level
/// decision is <see cref="SignalReadService.ReadAsync"/>: the Signals, each row's core record and each Do-lane subject are read
/// AS THE CALLER through the caller-identity Dataverse client, and a row the caller cannot read (or, in the Do lane, that is not
/// their own work) is dropped without trace. A caller who cannot be resolved gets #1312's single 403; a fault is 503, never a
/// shorter list.</para>
/// <para><b>Placement justification (CLAUDE.md section 10; .claude/constraints/bff-extensions.md).</b> FR-24 makes the BFF the
/// only reader of <c>sprk_signal</c> for the Console: Signal read depth is Parent: Child BU, so a browser read (Xrm.WebApi or the
/// DataGrid) cannot apply the core-record check (D-15, #17). No AI-internal type is used beyond the identity service the
/// decision commit route already takes; the route and its service are registered unconditionally (SignalsModule), so there is no
/// asymmetric registration.</para>
/// </remarks>
public static class SignalReadEndpoints
{
    internal const string WorklistUnavailableReasonCode = "ontology.signal.worklist_unavailable";

    public static void MapSignalReadEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/signals")
            .WithTags("Signals")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        group.MapGet("/worklist", GetWorklistAsync)
            .WithName("GetSignalWorklist")
            .WithSummary("Get the caller's worklist: visible Signals per lane, grouped by core record and ordered")
            .WithDescription("set = lanes (default; Decide and Do, ordered severity, high priority, oldest, record number), closedToday, "
                + "suppressed, closedEarlier or decided (the Decision Record tab, task 045). Membership comes from the worklist grid configuration; a Signal whose core record the "
                + "caller cannot read is absent, and a Do-lane Signal is returned only when its subject is the caller's own work. "
                + "Each row carries its sentence; a Do-lane row also carries its subject reference; Not filed is the Do lane's last group. "
                + "403 when the caller cannot be resolved; 503 when the list cannot be read.")
            .Produces<WorklistResponse>(200)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    internal static async Task<IResult> GetWorklistAsync(
        HttpContext httpContext,
        SignalReadService signalRead,
        CancellationToken ct,
        string? set = null)
    {
        if (!TryParseSet(set, out var worklistSet))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["set"] = ["set must be one of: lanes, closedToday, suppressed, closedEarlier, decided."],
            });
        }

        var result = await signalRead.ReadAsync(worklistSet, httpContext.User, ct);
        return result.Outcome switch
        {
            SignalReadOutcome.Ok when result.Response is { } body => Results.Ok(body),
            SignalReadOutcome.CallerUnresolved => Results.Problem(
                title: "Forbidden",
                detail: "The caller could not be resolved to a Dataverse user.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = DecisionPlanEndpoints.CallerUnresolvedReasonCode,
                    ["correlationId"] = httpContext.TraceIdentifier,
                }),
            _ => Results.Problem(
                title: "Service Unavailable",
                detail: "The worklist could not be read. Try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = WorklistUnavailableReasonCode,
                    ["correlationId"] = httpContext.TraceIdentifier,
                }),
        };
    }

    internal static bool TryParseSet(string? value, out WorklistSet set)
    {
        set = WorklistSet.Lanes;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        foreach (var candidate in Enum.GetValues<WorklistSet>())
        {
            if (string.Equals(SignalReadService.SetName(candidate), value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                set = candidate;
                return true;
            }
        }

        return false;
    }
}
