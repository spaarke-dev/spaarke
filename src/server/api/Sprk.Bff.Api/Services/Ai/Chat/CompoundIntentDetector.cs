using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Detects compound intent in an LLM response and produces a <see cref="PendingPlan"/>.
///
/// Compound intent is declared when the AI model determines that fulfilling the user's request
/// requires any of the following:
///   - 2 or more tool calls (multi-step operation)
///   - Any write-back tool (tools that modify Dataverse entities, e.g., EditWorkingDocument, AppendSection)
///   - Any external action tool (e.g., sending email, calling external services)
///
/// When compound intent is detected, the BFF MUST:
///   1. Create a <see cref="PendingPlan"/> from the tool call list.
///   2. Store the plan in Redis via <see cref="PendingPlanManager"/>.
///   3. Emit a <c>plan_preview</c> SSE event.
///   4. Halt tool execution until the user approves via POST /plan/approve.
///
/// This satisfies spec constraint FR-11: "No write-back executes without user Proceed confirmation."
///
/// Detection approach: Keyword-based heuristic on tool names (task 071 scope).
/// Full ML-based intent classification is out of scope for this iteration.
///
/// Lifetime: Transient (instantiated per detection call; no state). Can be made a singleton
/// since it is stateless — registered as transient per ADR-010 (no unnecessary DI registrations;
/// factory instantiates it directly without DI registration to avoid overhead).
///
/// Not registered in DI — instantiated directly by <see cref="SprkChatAgentFactory"/> and
/// <see cref="ChatEndpoints"/> as needed. (ADR-010: 0 additional DI registrations.)
/// </summary>
public sealed class CompoundIntentDetector
{
    /// <summary>
    /// Tool names that constitute a "write-back" operation.
    /// Any tool in this set triggers compound intent even if it is the only tool in the plan.
    /// This ensures ALL write-back operations are plan-preview-gated (spec FR-11).
    ///
    /// Task 073: Added "WriteBackToWorkingDocument" — the primary Dataverse write-back tool
    /// that persists AI-generated content to sprk_analysisoutput.sprk_workingdocument.
    /// </summary>
    private static readonly HashSet<string> WriteBackToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "EditWorkingDocument",
        "AppendSection",
        "WriteBackToWorkingDocument",
        "SaveWorkingDocument",
        "UpdateAnalysisOutput",
        "WriteToDataverse"
    };

    /// <summary>
    /// Tool names that constitute an "external action" (beyond document analysis).
    /// Any tool in this set triggers compound intent even if it is the only tool in the plan.
    /// </summary>
    private static readonly HashSet<string> ExternalActionToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SendEmail",
        "CreateTask",
        "PostToTeams",
        "CreateCalendarEvent"
    };

    private readonly ILogger _logger;

    public CompoundIntentDetector(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines whether the given tool call list represents compound intent.
    ///
    /// Compound intent is declared when ANY of the following is true:
    ///   - 2 or more tool calls in the list
    ///   - Any tool is a write-back tool (see <see cref="WriteBackToolNames"/>)
    ///   - Any tool is an external action (see <see cref="ExternalActionToolNames"/>)
    /// </summary>
    /// <param name="toolCalls">The list of tool calls proposed by the AI model.</param>
    /// <returns>True when compound intent is detected; false when single non-write tool.</returns>
    public bool IsCompoundIntent(IReadOnlyList<FunctionCallContent> toolCalls)
    {
        if (toolCalls.Count == 0)
            return false;

        // Multiple tool calls → compound intent
        if (toolCalls.Count >= 2)
        {
            _logger.LogDebug(
                "Compound intent detected: {ToolCount} tool calls proposed",
                toolCalls.Count);
            return true;
        }

        // Single tool call: check if it's a write-back or external action
        var toolName = toolCalls[0].CallId is not null ? toolCalls[0].Name : string.Empty;
        toolName = toolCalls[0].Name ?? string.Empty;

        if (WriteBackToolNames.Contains(toolName))
        {
            _logger.LogDebug(
                "Compound intent detected: write-back tool '{ToolName}' proposed",
                toolName);
            return true;
        }

        if (ExternalActionToolNames.Contains(toolName))
        {
            _logger.LogDebug(
                "Compound intent detected: external action tool '{ToolName}' proposed",
                toolName);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a <see cref="PendingPlan"/> from the given tool call list and session context.
    ///
    /// The plan title is a human-readable summary of the proposed operation.
    /// Each step maps one-to-one to a tool call, with an auto-generated description based on
    /// the tool name and parameters.
    ///
    /// For write-back steps, the <see cref="PendingPlan.WriteBackTarget"/> and
    /// <see cref="PendingPlan.AnalysisId"/> are populated from the chat context.
    /// </summary>
    /// <param name="toolCalls">The tool calls proposed by the AI model.</param>
    /// <param name="sessionId">Session ID for the plan.</param>
    /// <param name="tenantId">Tenant ID for Redis key isolation (ADR-014).</param>
    /// <param name="context">
    /// The chat context (contains AnalysisMetadata and KnowledgeScope for write-back target resolution).
    /// </param>
    /// <returns>A fully constructed <see cref="PendingPlan"/> ready for storage and SSE emission.</returns>
    public PendingPlan BuildPlan(
        IReadOnlyList<FunctionCallContent> toolCalls,
        string sessionId,
        string tenantId,
        ChatContext context)
    {
        var planId = Guid.NewGuid().ToString("N");
        var steps = new PendingPlanStep[toolCalls.Count];

        string? analysisId = null;
        string? writeBackTarget = null;

        for (var i = 0; i < toolCalls.Count; i++)
        {
            var call = toolCalls[i];
            var toolName = call.Name ?? "UnknownTool";
            var parametersJson = SerializeArguments(call.Arguments);
            var description = BuildStepDescription(toolName, call.Arguments);

            steps[i] = new PendingPlanStep(
                Id: $"step-{i + 1}",
                Description: description,
                ToolName: toolName,
                ParametersJson: parametersJson);

            // Detect write-back metadata for plan-level fields
            if (WriteBackToolNames.Contains(toolName))
            {
                analysisId = context.AnalysisMetadata?.GetValueOrDefault("analysisId");
                writeBackTarget = "sprk_analysisoutput.sprk_workingdocument";

                _logger.LogInformation(
                    "BuildPlan: write-back tool detected — tool={ToolName}, " +
                    "analysisMetadataNull={AnalysisMetadataNull}, " +
                    "analysisMetadataKeys={AnalysisMetadataKeys}, " +
                    "resolvedAnalysisId={AnalysisId}",
                    toolName,
                    context.AnalysisMetadata is null,
                    context.AnalysisMetadata is not null
                        ? string.Join(", ", context.AnalysisMetadata.Keys)
                        : "(null)",
                    analysisId ?? "(null)");
            }
        }

        var planTitle = BuildPlanTitle(toolCalls);

        _logger.LogInformation(
            "PendingPlan built — planId={PlanId}, session={SessionId}, steps={StepCount}, " +
            "hasWriteBack={HasWriteBack}, analysisId={AnalysisId}, writeBackTarget={WriteBackTarget}",
            planId, sessionId, steps.Length, writeBackTarget != null,
            analysisId ?? "(null)", writeBackTarget ?? "(null)");

        return new PendingPlan(
            PlanId: planId,
            SessionId: sessionId,
            TenantId: tenantId,
            PlanTitle: planTitle,
            Steps: steps,
            AnalysisId: analysisId,
            WriteBackTarget: writeBackTarget,
            CreatedAt: DateTimeOffset.UtcNow);
    }

    // === Private helpers ===

    /// <summary>
    /// Serializes the tool call arguments dictionary to a JSON string.
    /// Used to preserve parameters for re-execution on approval.
    /// </summary>
    private static string SerializeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return "{}";

        try
        {
            return System.Text.Json.JsonSerializer.Serialize(arguments);
        }
        catch
        {
            return "{}";
        }
    }

    /// <summary>
    /// Generates a human-readable description for a plan step based on the tool name and arguments.
    /// These descriptions appear in the PlanPreviewCard step list shown to the user.
    /// </summary>
    private static string BuildStepDescription(string toolName, IDictionary<string, object?>? args)
    {
        return toolName switch
        {
            "EditWorkingDocument" => BuildEditDescription(args),
            "AppendSection" => BuildAppendDescription(args),
            "WriteBackToWorkingDocument" => "Write AI-generated content back to analysis working document",
            "RunAnalysis" or "RerunAnalysis" => "Run document analysis",
            "SearchDocuments" => "Search document knowledge base",
            "SearchKnowledgeBase" => "Search knowledge base",
            "GetAnalysisResult" => "Retrieve analysis results",
            "SendEmail" => "Send email notification",
            "CreateTask" => "Create a task",
            _ => $"Execute {toolName}"
        };
    }

    private static string BuildEditDescription(IDictionary<string, object?>? args)
    {
        if (args?.TryGetValue("instruction", out var instr) == true && instr?.ToString() is { Length: > 0 } instrStr)
        {
            var truncated = instrStr.Length > 80 ? instrStr[..80] + "..." : instrStr;
            return $"Edit working document: {truncated}";
        }
        return "Edit working document";
    }

    private static string BuildAppendDescription(IDictionary<string, object?>? args)
    {
        if (args?.TryGetValue("sectionTitle", out var title) == true && title?.ToString() is { Length: > 0 } titleStr)
        {
            return $"Append section \"{titleStr}\" to working document";
        }
        return "Append section to working document";
    }

    /// <summary>
    /// Builds a human-readable plan title summarizing all proposed tool calls.
    /// Displayed at the top of the PlanPreviewCard.
    /// </summary>
    private static string BuildPlanTitle(IReadOnlyList<FunctionCallContent> toolCalls)
    {
        if (toolCalls.Count == 1)
        {
            return BuildStepDescription(toolCalls[0].Name ?? "UnknownTool", toolCalls[0].Arguments);
        }

        var toolNames = toolCalls.Select(t => t.Name ?? "UnknownTool").Distinct().ToArray();

        if (toolNames.Length <= 2)
        {
            return string.Join(" and ", toolNames.Select(FormatToolNameForTitle));
        }

        return $"Multi-step operation ({toolCalls.Count} steps)";
    }

    private static string FormatToolNameForTitle(string toolName) => toolName switch
    {
        "EditWorkingDocument" => "edit working document",
        "AppendSection" => "append section",
        "WriteBackToWorkingDocument" => "write back to working document",
        "RunAnalysis" or "RerunAnalysis" => "run analysis",
        "SearchDocuments" => "search documents",
        "SendEmail" => "send email",
        _ => toolName
    };
}
