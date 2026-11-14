using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;

/// <summary>
/// What the decision-plan and lookup tools (spaarkeai-mcp-bff-api-r1 task 023, FR-06, FR-07) share: the no-caller guard, the app-only
/// tool metadata, and the success result. Kept in one place so the two tools cannot disagree on any of them.
/// </summary>
/// <remarks>
/// <para><b>Success bodies are the REST JSON.</b> A success carries the same members, in the same case and with the same nulls, as the
/// REST route's answer (<c>JsonSerializerDefaults.Web</c>, which is what <c>Results.Ok</c> writes in this BFF), so the widget's
/// <c>ResponseFetchFn</c> shim can wrap a member of <c>structuredContent</c> in a <c>Response</c> unchanged (task 023 notes). Failures
/// are not built here: they go through <see cref="McpToolResults.FromError"/> (ADR-019).</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="McpToolResults"/> (failures only) and the
/// capability's own caller guard. Extension: neither can build a success result or the app-only metadata, and the caller guard must
/// run in the tool before any capability call (FR-10, ADR-002 WP-6); this class adds only those three. Cost of doing nothing: each
/// tool would carry its own copy of the guard and the metadata, and one could drift to a caller-less read.</para>
/// </remarks>
internal static class PlanAndLookupToolSupport
{
    /// <summary>The REST routes' JSON: web defaults, nulls written (a candidate's <c>email</c> is <c>null</c>, not absent).</summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// <c>_meta.ui.visibility = ["app"]</c> (MCP Apps): the host lists the tool for the widget only and keeps it out of the model's tool
    /// set (design §7.1). A new node per tool, because a <see cref="JsonNode"/> has one parent.
    /// </summary>
    internal static JsonObject AppOnlyMeta() =>
        new() { ["ui"] = new JsonObject { ["visibility"] = new JsonArray("app") } };

    /// <summary>
    /// True when the tool can go on: the request's context exists and carries a signed-in caller (the same test the capability applies
    /// and <c>RequireAuthorization</c>'s default policy makes). False is <c>UserContextRequired</c>: nothing is read, and nothing falls
    /// back to an app-only client (FR-10 as amended 2026-11-07, option C).
    /// </summary>
    internal static bool HasCaller(HttpContext? http) => http is not null && DecisionCapability.HasSignedInCaller(http.User);

    /// <summary>A success: a one-line summary (counts only, never record text, ADR-015) and the body as <c>structuredContent</c>.</summary>
    internal static CallToolResult Success(string summary, object body) =>
        new()
        {
            Content = [new TextContentBlock { Text = summary }],
            StructuredContent = JsonSerializer.SerializeToElement(body, body.GetType(), Json),
        };

    /// <summary>The <c>{ reasonCode, message }</c> of a part of the plan result that failed after the plan itself was returned.</summary>
    internal sealed record PartError(string ReasonCode, string Message)
    {
        internal static PartError From(DecisionToolError error) => new(error.ReasonCode, error.Message);
    }

    /// <summary>
    /// <c>structuredContent</c> of <c>spaarke_get_decision_plan</c>. <see cref="Plan"/> is the REST decision-plan body and
    /// <see cref="Suppression"/> the REST suppression body, each exactly as its route writes it. <see cref="Row"/> is the item's full
    /// worklist row as the REST worklist writes it; null when the item is no longer in the caller's Decide or Do lane.
    /// <see cref="SuppressionError"/> and <see cref="RowError"/> are set (and the member is null) when that part could not be read; the
    /// plan, and the plan-read stamp, stand regardless.
    /// </summary>
    internal sealed record PlanBody(
        DecisionPlanResponse Plan,
        SuppressionResponse? Suppression,
        WorklistItemRow? Row,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PartError? SuppressionError = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PartError? RowError = null);
}
