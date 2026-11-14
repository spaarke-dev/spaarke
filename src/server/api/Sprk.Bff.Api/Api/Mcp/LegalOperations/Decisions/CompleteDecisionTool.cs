using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;

/// <summary>
/// <c>spaarke_complete_decision</c> (spaarkeai-mcp-bff-api-r1 task 024; spec FR-08, FR-15 server half, NFR-06; design §7.1, §10;
/// ontology condition 2): the ONE write tool of the Decide lane. App-only: the decision wizard sends it from its explicit Confirm step and
/// the model never sees it (O-14: no model-callable complete tool in v1). Input: the Signal id and a <see cref="DecisionReview"/>, bound
/// with the same JSON options as <c>POST /api/v1/signals/{signalId}/decision</c> (<see cref="RestJson"/>).
/// </summary>
/// <remarks>
/// <para><b>A thin adapter, nothing else (ADR-002).</b> The tool calls <see cref="IDecisionCapability.CommitAsync"/> with exactly
/// <c>IHttpContextAccessor.HttpContext</c> (FR-10) and returns what comes back: <see cref="DecisionCommitService"/> stays the single owner
/// of completion, so the explicit Confirm, the plan-read stamp, the review-id idempotency key, the invoice-review refusal, the caller's
/// rights and the commit order all run there. The tool adds no check, validation, retry or write of its own and relies on no host
/// confirmation prompt: the widget-initiated write shows none (Phase 0 T3), so the server-side checks are the only gate (NFR-06).</para>
/// <para><b>No caller, no commit.</b> A null context or an unauthenticated caller is the capability's own guard
/// (<see cref="DecisionCapability.UserContextRequiredCommitResult"/>, before the service is called, nothing read, locked or written);
/// the tool maps it, like every other result, through <see cref="DecisionResultMapper.ToToolError(DecisionCommitResult)"/>, so it is an
/// <c>isError</c> result with <c>DATAVERSE_USER_CONTEXT_REQUIRED</c> and nothing falls back to an app-only client.</para>
/// <para><b>Result.</b> A recorded decision is returned as <c>structuredContent</c> UNCHANGED: the <see cref="DecisionCommitResult"/> the
/// REST route puts in its 200 body, serialized the way ASP.NET does, so <c>warnings[]</c> and each step's <c>warning</c> (D-216, e.g.
/// <c>inquiry_email_not_archived</c>) reach the widget. Every other outcome is an <c>isError</c> result from
/// <see cref="McpToolResults.FromError"/> (ADR-019: a domain failure is never a transport error); a failed commit carries the D-17
/// <c>partial</c> facts (<c>landed</c>, <c>possiblyLanded</c>, <c>resumable</c>). Nothing the tool returns or logs carries record text
/// (ADR-015): the text content is a fixed sentence.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
public sealed class CompleteDecisionTool : McpToolDefinition
{
    /// <summary>The tool's name (naming note §1: <c>spaarke_{verb}_{object}</c>).</summary>
    public const string ToolName = "spaarke_complete_decision";

    /// <summary>
    /// The JSON options of the REST route: ASP.NET's web defaults (camelCase, case-insensitive, numbers from strings). The route binds a
    /// <see cref="DecisionReview"/> and writes a <see cref="DecisionCommitResult"/> with them, and so does this tool, so the two hosts
    /// share one wire contract.
    /// </summary>
    private static readonly JsonSerializerOptions RestJson = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(
            CompleteAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Complete decision",
                Description =
                    "Use for: recording one decision on a Work Item after the person pressed Confirm in the decision wizard (sent by the "
                    + "wizard only; never call it yourself). It writes one Decision Record and closes the Work Item; sending the same review "
                    + "id again returns the first outcome and writes nothing. Not for: listing or searching Work Items, reading a decision "
                    + "plan, previewing a decision, or deciding an invoice or invoice line (those are decided in Invoice Review).",
                ReadOnly = false,
                Destructive = false,
                Idempotent = true,
                OpenWorld = false,
                SerializerOptions = RestJson,
                // MCP Apps visibility: listed to the widget (app), never to the model.
                Meta = new JsonObject { ["ui"] = new JsonObject { ["visibility"] = new JsonArray(JsonValue.Create("app")) } },
            });

    /// <summary>
    /// One call. <paramref name="decisions"/> and <paramref name="accessor"/> are resolved from the request's own service provider on every
    /// call (the tool holds no state); <paramref name="review"/> is bound exactly as the REST route binds its body.
    /// </summary>
    internal static async Task<CallToolResult> CompleteAsync(
        Guid signalId,
        DecisionReview review,
        IHttpContextAccessor accessor,
        IDecisionCapability decisions,
        CancellationToken ct)
    {
        var result = await decisions.CommitAsync(accessor.HttpContext, signalId, review, ct);

        var error = DecisionResultMapper.ToToolError(result);
        if (error != null)
        {
            return McpToolResults.FromError(error);
        }

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = result.Replayed ? "Decision already recorded; returning the first outcome." : "Decision recorded." }],
            StructuredContent = JsonSerializer.SerializeToElement(result, RestJson),
        };
    }
}
