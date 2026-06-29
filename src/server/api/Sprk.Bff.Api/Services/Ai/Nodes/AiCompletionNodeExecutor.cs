// R7 spaarke-ai-platform-unification-r7 — AiCompletionNodeExecutor (task 002 / FR-12).
// Implements ActionType.AiCompletion = 1 (enum value present since the original node
// system, but never wired to an executor — R4 /narrate could not ship as a result).
//
// Purpose (prompt-only structured LLM call per FR-12 / FR-13):
//   AiAnalysisNodeExecutor REQUIRES a Tool (handler dispatch) AND a Document (extracted
//   text). That contract is wrong for prompt-only generators like R4 /narrate, channel
//   TL;DR, and other workflows that just need {SystemPrompt + OutputSchema + Temperature}
//   passed straight to IOpenAiClient.GetStructuredCompletionRawAsync. AiCompletion is the
//   leaf executor for that case — it REQUIRES Action FK + SystemPrompt + OutputSchema and
//   PROHIBITS Tool (FR-13). Document is optional (NOT REQUIRED).
//
// Pattern source: EntityNameValidatorNodeExecutor (sibling canonical R4-era shape —
//   private static JsonSerializerOptions, ILogger + injected dep ctor, AiTelemetry.ActivitySource
//   span, structured try/catch with Cancelled / InternalError propagation, terminal
//   NodeOutput.Ok / .Error). The one addition vs EntityNameValidator: inject IOpenAiClient
//   (Singleton-safe). No IServiceScopeFactory — no Scoped deps required.
//
// SCAFFOLD STATUS (task 002):
//   - Class compiles and registers in DI.
//   - Validate() enforces FR-13 require/prohibit invariants (Action FK + SystemPrompt +
//     OutputSchema + OutputVariable required; Tool prohibited).
//   - ExecuteAsync() is a deliberate skeleton — returns InternalError pointing to the
//     implementing tasks. Tasks 003 + 004 add the PromptSchemaOverrideMerger plug-in,
//     IOpenAiClient.GetStructuredCompletionRawAsync call, and NodeOutput.StructuredData
//     binding. Task 005 fills in the full Validate() error catalog. Task 006 wires the
//     PlaybookOrchestrationService dispatch path (Wave 2 enum rename / dispatch refactor).
//
// Reference: projects/spaarke-ai-platform-unification-r7/spec.md FR-12, FR-13;
//            projects/spaarke-ai-platform-unification-r7/notes/spikes/aicompletion-pattern-decision.md
//            (task 001 pattern-decision doc, the contract this scaffold follows);
//            .claude/patterns/ai/node-executor-authoring.md;
//            ADR-010 DI Minimalism; ADR-013 BFF AI Architecture; ADR-029 BFF Publish Hygiene.

using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for prompt-only structured LLM completions (R7 FR-12 / FR-13).
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="INodeExecutor"/> for <see cref="ActionType.AiCompletion"/>
/// (value 1). Registered as a Singleton in
/// <c>AnalysisServicesModule.AddNodeExecutors</c> (UNCONDITIONAL per CLAUDE.md §F.1
/// asymmetric-registration governance). The injected <see cref="IOpenAiClient"/> is also
/// Singleton, so the executor is Singleton-safe with no <c>IServiceScopeFactory</c>
/// indirection.
/// </para>
/// <para>
/// Contract delta vs <see cref="AiAnalysisNodeExecutor"/> (FR-13):
/// </para>
/// <list type="bullet">
/// <item><description>REQUIRES <c>context.Action</c> (FK), <c>context.Action.SystemPrompt</c>,
///   and <c>context.Action.OutputSchemaJson</c>.</description></item>
/// <item><description>PROHIBITS <c>context.Tool</c> — AiCompletion is a prompt-only
///   call; presence of a Tool implies an AiAnalysis node was mis-typed.</description></item>
/// <item><description>Does NOT require <c>context.Document</c> — payload-driven flows like
///   R4 <c>/narrate</c> have no document context.</description></item>
/// </list>
/// <para>
/// SCAFFOLD STATUS: task 002 delivers the class shape, ctor, Validate() enforcement of the
/// FR-13 require/prohibit invariants, and an ExecuteAsync() skeleton. Full
/// <c>IOpenAiClient.GetStructuredCompletionRawAsync</c> call + <c>PromptSchemaOverrideMerger</c>
/// plug-in + <see cref="NodeOutput.StructuredData"/> binding land in tasks 003 + 004. Task 005
/// expands the Validate() error catalog. Unit tests follow in tasks 007–009.
/// </para>
/// </remarks>
public sealed class AiCompletionNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<AiCompletionNodeExecutor> _logger;

    public AiCompletionNodeExecutor(
        IOpenAiClient openAiClient,
        ILogger<AiCompletionNodeExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(openAiClient);
        ArgumentNullException.ThrowIfNull(logger);
        _openAiClient = openAiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.AiCompletion
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        // SCAFFOLD: full error catalog (per-field messages, prompt-format guidance) lands
        // in task 005. This block establishes the FR-13 require/prohibit invariants so the
        // dispatch path can be wired in task 006 against a Validate() that already enforces
        // the canonical shape. TODO(task 005): expand to the full diagnostic message set per
        // spec FR-13 acceptance criteria.

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Node.OutputVariable))
        {
            errors.Add("AiCompletion node requires OutputVariable to be set on the node");
        }

        // FR-06: prompt-driven executors REQUIRE Action FK (Action carries SystemPrompt,
        // OutputSchema, and Temperature for the LLM call).
        if (context.Action is null)
        {
            errors.Add("AiCompletion node requires an Action (FK on node) — Action carries SystemPrompt, OutputSchema, and Temperature");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        if (string.IsNullOrWhiteSpace(context.Action.SystemPrompt))
        {
            errors.Add("AiCompletion node Action has empty SystemPrompt — required for LLM call");
        }

        if (string.IsNullOrWhiteSpace(context.Action.OutputSchemaJson))
        {
            errors.Add("AiCompletion node Action has empty OutputSchemaJson — required for IOpenAiClient.GetStructuredCompletionRawAsync constrained-decoding schema arg");
        }

        // FR-13 inversion vs AiAnalysis: AiCompletion does NOT require Tool/Document and
        // EXPLICITLY rejects Tool presence (a Tool means the playbook author chose the
        // wrong ActionType — should be AiAnalysis).
        if (context.Tool is not null)
        {
            errors.Add($"AiCompletion node MUST NOT have a Tool configured (found Tool '{context.Tool.Name}'); use ActionType.AiAnalysis for tool-driven nodes");
        }

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        using var activity = AiTelemetry.ActivitySource.StartActivity(
            "ai.completion.node_execute", ActivityKind.Internal);
        activity?.SetTag("node.id", context.Node.Id.ToString());
        activity?.SetTag("node.name", context.Node.Name);
        activity?.SetTag("action_type", (int)ActionType.AiCompletion);

        _logger.LogDebug(
            "Executing AiCompletion node {NodeId} ({NodeName})",
            context.Node.Id, context.Node.Name);

        try
        {
            var validation = Validate(context);
            if (!validation.IsValid)
            {
                activity?.SetTag("node.outcome", "validation_failed");
                return Task.FromResult(NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", validation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
            }

            // ─────────────────────────────────────────────────────────────────────────
            // SCAFFOLD GAP — tasks 003 + 004 implement the body below.
            //
            // Task 003 (PromptSchemaOverrideMerger plug-in):
            //   - Read context.Action.SystemPrompt as basePrompt.
            //   - If basePrompt is JPS format and context.Node.ConfigJson carries a
            //     promptSchemaOverride section, call PromptSchemaOverrideMerger.Merge
            //     immediately before the LLM call (see notes/spikes/aicompletion-pattern-decision.md
            //     §Q3 for the verbatim plug-in shape — mirrors AiAnalysisNodeExecutor's
            //     private ApplyPromptSchemaOverride helper; task 003 may refactor it to a
            //     shared internal helper).
            //   - Render the (possibly merged) JPS prompt to the actual string passed to the LLM.
            //
            // Task 004 (IOpenAiClient.GetStructuredCompletionRawAsync call + binding):
            //   var rawJson = await _openAiClient.GetStructuredCompletionRawAsync(
            //       prompt:          renderedPrompt,
            //       jsonSchema:      BinaryData.FromString(context.Action.OutputSchemaJson),
            //       schemaName:      deriveSchemaName(context.Action),
            //       model:           context.ModelDeploymentId?.ToString(),
            //       maxOutputTokens: context.MaxTokens,
            //       temperature:     (float?)context.Action.Temperature,
            //       cancellationToken: cancellationToken);
            //   using var doc      = JsonDocument.Parse(rawJson);
            //   var structuredData = doc.RootElement.Clone();
            //   return NodeOutput.Ok(
            //       nodeId:         context.Node.Id,
            //       outputVariable: context.Node.OutputVariable,
            //       data:           structuredData,
            //       textContent:    rawJson,
            //       metrics:        NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            //
            // JsonOptions (declared above) is reserved for ConfigJson reads in task 003.
            // ─────────────────────────────────────────────────────────────────────────

            activity?.SetTag("node.outcome", "not_implemented");
            return Task.FromResult(NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                "AiCompletionNodeExecutor.ExecuteAsync is a scaffold — body lands in R7 tasks 003 (PromptSchemaOverrideMerger plug-in) + 004 (LLM call + structured-output binding). See notes/spikes/aicompletion-pattern-decision.md.",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "AiCompletion node {NodeId} was cancelled",
                context.Node.Id);

            activity?.SetTag("node.outcome", "cancelled");
            return Task.FromResult(NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                "Node execution was cancelled",
                NodeErrorCodes.Cancelled,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AiCompletion node {NodeId} failed: {ErrorMessage}",
                context.Node.Id, ex.Message);

            activity?.SetTag("node.outcome", "error");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Task.FromResult(NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"AiCompletion execution failed: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
        }
    }
}
