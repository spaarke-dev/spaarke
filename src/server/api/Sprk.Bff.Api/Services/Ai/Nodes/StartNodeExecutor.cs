// R4 spaarke-daily-update-service-r4 — First-class Start node executor.
//
// Purpose (UAT-blocking fix, post canonical-truth deploy 2026-06-25):
//   The 5 R4 playbooks were deployed with a "Start" sprk_playbooknode row carrying
//   nodeType=Control + canvasType=start + configJson with `inputContract` and `scope`
//   metadata but NO `__actionType` field (Deploy-Playbook.ps1 does not inject it the
//   way NodeService.BuildConfigJson does on the canvas-sync path). The orchestrator's
//   structural fallback (PlaybookOrchestrationService.cs:1117-1127) therefore mapped
//   NodeType.Control → ExecutorType.Condition → ConditionNodeExecutor, which then rejected
//   the Start node with "Condition expression is required; At least one branch
//   (trueBranch or falseBranch) must be specified".
//
//   Owner direction (R4 2026-06-25): "if start is a required step (probably should
//   be because could have some rules) then fix it to properly use start" — i.e., make
//   Start a first-class executable node with its own executor + (optional) input-contract
//   rules; do NOT bury it as inline orchestrator special-casing.
//
// Semantics (per docs/architecture/ai-architecture-playbook-runtime.md §6 + §9):
//   - NodeType: Control (no Action FK, no scope resolution required).
//   - ExecutorType: Start = 33 (already in INodeExecutor.cs enum; this executor is the
//     pairing piece that was missing).
//   - Execute: read the dispatch payload from one of three sources (in priority order)
//     and bind it as JsonElement to `context.Node.OutputVariable` (default "start").
//     Source priority:
//       1. configJson.payloadParameter (caller-provided key into Parameters dict)
//       2. Parameters["briefingPayload"] (R4 dispatch convention from DailyBriefingEndpoints)
//       3. Parameters[OutputVariable] (generic fallback — matches the variable name)
//       4. Empty object (no payload; downstream nodes referencing {{start.*}} get null)
//   - Validate: configJson is optional; if `inputContract` is present AND
//     `validateOnExecute=true`, top-level keys of the payload (parsed at execute time)
//     are checked against the contract's key set. Missing required keys ⇒ validation
//     error chunk. Mode `lenient` (default) logs warnings only.
//
// Why a dedicated executor, not orchestrator special-casing (Option A vs Option B):
//   - Per canonical-truth §9: NodeType (5 values) and ExecutorType (31 enum values) are
//     orthogonal; the dispatch axis is ExecutorType. canvasType is a UX concept that the
//     canvas-sync path maps into ExecutorType — coupling canvasType to the orchestrator
//     would re-create the same UX/runtime entanglement the canonical doc warns against.
//   - Per node-executor-authoring pattern: every dispatchable node-type has its own
//     INodeExecutor; the registry indexes by ExecutorType. Adding Start as the 19th
//     executor in this registry is the canonical shape.
//   - The brittle inline detection at PlaybookOrchestrationService.cs:1031-1046 (which
//     only fires for `__actionType=33` OR empty configJson OR Name=="Start" with
//     `__actionType=30`) is now superseded by the structural-fallback Control+Start
//     routing wired in alongside this executor.
//
// Pattern source: EntityNameValidatorNodeExecutor (sibling — simple ConfigJson read +
//   per-execution Validate + ILogger only, no Scoped deps, Singleton registration).
//
// Reference: spec FR-12 + R4 UAT failure 2026-06-25 (verbatim:
//   "Node 'Start' failed: Validation failed: Condition expression is required; At
//   least one branch (trueBranch or falseBranch) must be specified");
//   notes/canonical-truth/01-code-archaeology.md §6 (ExtractActionTypeFromConfig +
//   NodeType→ExecutorType default switch); docs/architecture/ai-architecture-playbook-runtime.md
//   §6 (Action lookup precedence); .claude/patterns/ai/node-executor-authoring.md.

using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for the playbook entry-point ("Start") node.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="INodeExecutor"/> for <see cref="ExecutorType.Start"/> (value
/// 33). Registered as a Singleton in <c>AnalysisServicesModule.AddNodeExecutors</c>
/// alongside the other 18 executors (no scope-factory needed — reads only ConfigJson
/// + Parameters; ILogger is the only dependency).
/// </para>
/// <para>
/// The Start node's responsibility is to bind the dispatching wrapper's payload
/// (passed via <c>PlaybookRunContext.Parameters</c>) into the playbook's variable
/// scope under the node's <see cref="PlaybookNodeDto.OutputVariable"/> name (default
/// <c>"start"</c>). Downstream nodes then reference fields via standard template
/// substitution: <c>{{start.channels}}</c>, <c>{{start.priorityItems}}</c>, etc.
/// </para>
/// <para>
/// Optional input-contract validation: when ConfigJson contains an
/// <c>inputContract</c> object and <c>validateOnExecute=true</c>, the executor
/// checks that every top-level key declared in the contract is present in the parsed
/// payload. Missing keys produce a validation error chunk. When
/// <c>validateOnExecute</c> is false (default) the contract is documentation only.
/// </para>
/// </remarks>
public sealed class StartNodeExecutor : INodeExecutor
{
    /// <summary>
    /// Fallback parameter key when the Start node's ConfigJson does not declare an
    /// explicit <c>payloadParameter</c>. Matches the R4 dispatch convention in
    /// <c>DailyBriefingEndpoints.NarrateAsync</c> (line 281) where the structured
    /// request is serialized into <c>parameters["briefingPayload"]</c>.
    /// </summary>
    public const string DefaultPayloadParameterKey = "briefingPayload";

    /// <summary>
    /// Default output-variable name used when <see cref="PlaybookNodeDto.OutputVariable"/>
    /// is unset. Matches the convention in every R4 playbook's Start node.
    /// </summary>
    public const string DefaultOutputVariable = "start";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<StartNodeExecutor> _logger;

    public StartNodeExecutor(ILogger<StartNodeExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.Start
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        // ConfigJson is OPTIONAL for Start nodes. Some authors keep it null (pure
        // anchor); the R4 playbooks populate it with `inputContract` + `scope` + a
        // description. Either is acceptable here — payload validation happens at
        // execute time when the payload is actually present.
        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
            return NodeValidationResult.Success();

        try
        {
            // Parse to confirm the JSON is well-formed; reject malformed early so
            // operators see a clear validation error rather than a runtime exception.
            using var _ = JsonDocument.Parse(context.Node.ConfigJson);
        }
        catch (JsonException ex)
        {
            return NodeValidationResult.Failure(
                $"Start node ConfigJson is not valid JSON: {ex.Message}");
        }

        // OutputVariable defaulting is handled at execute time — Validate accepts
        // empty (the executor falls back to DefaultOutputVariable).
        return NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var outputVariable = string.IsNullOrWhiteSpace(context.Node.OutputVariable)
            ? DefaultOutputVariable
            : context.Node.OutputVariable;

        _logger.LogDebug(
            "Executing Start node {NodeId} ({NodeName}) — binding payload to '{OutputVariable}'",
            context.Node.Id, context.Node.Name, outputVariable);

        StartNodeConfig? config = null;
        if (!string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            try
            {
                config = JsonSerializer.Deserialize<StartNodeConfig>(
                    context.Node.ConfigJson, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Start node {NodeId} ConfigJson failed to deserialize into StartNodeConfig — proceeding with defaults. Message: {Message}",
                    context.Node.Id, ex.Message);
            }
        }

        // Resolve the parameter key the wrapper used to ship the payload.
        // Priority:
        //   1. configJson.payloadParameter (explicit author choice)
        //   2. DefaultPayloadParameterKey ("briefingPayload" — R4 wrapper convention)
        //   3. outputVariable (generic fallback for non-briefing dispatchers)
        var payloadKey = !string.IsNullOrWhiteSpace(config?.PayloadParameter)
            ? config!.PayloadParameter!
            : DefaultPayloadParameterKey;

        var payloadString = TryGetParameter(context.Parameters, payloadKey)
            ?? TryGetParameter(context.Parameters, outputVariable)
            ?? string.Empty;

        // Parse the payload string as JSON. If the wrapper sent a non-JSON string OR
        // sent nothing at all, we bind an EMPTY object (downstream {{start.field}}
        // references resolve to null without throwing). Per the empty-payload contract
        // in ai-architecture-playbook-runtime.md §7.
        JsonElement payloadElement;
        if (string.IsNullOrWhiteSpace(payloadString))
        {
            payloadElement = JsonDocument.Parse("{}").RootElement.Clone();
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadString);
                payloadElement = doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                // Payload is non-JSON text. Bind it as a single-field object
                // {{ raw: "<payload>" }} so downstream nodes can still reference it
                // intentionally; emit a structured warning rather than failing the run.
                _logger.LogWarning(
                    ex,
                    "Start node {NodeId} payload at Parameters['{PayloadKey}'] is not valid JSON — binding as {{ raw: <text> }}. PayloadLength={PayloadLength}",
                    context.Node.Id, payloadKey, payloadString.Length);

                payloadElement = JsonSerializer
                    .SerializeToElement(new { raw = payloadString });
            }
        }

        // Optional input-contract validation. The contract is documentation-by-default;
        // gate enforcement behind `validateOnExecute=true` so existing playbooks (R4
        // deploy carries `inputContract` without the flag) remain non-blocking until
        // an author explicitly opts in.
        if (config?.InputContract is { Count: > 0 } && config.ValidateOnExecute == true)
        {
            var missing = new List<string>();
            foreach (var key in config.InputContract.Keys)
            {
                if (payloadElement.ValueKind != JsonValueKind.Object ||
                    !payloadElement.TryGetProperty(key, out _))
                {
                    missing.Add(key);
                }
            }

            if (missing.Count > 0)
            {
                var msg = $"Start node payload missing required input-contract keys: {string.Join(", ", missing)}";
                _logger.LogWarning(
                    "Start node {NodeId} input-contract validation failed: {Message}",
                    context.Node.Id, msg);

                return Task.FromResult(NodeOutput.Error(
                    context.Node.Id,
                    outputVariable,
                    msg,
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
            }
        }

        _logger.LogInformation(
            "Start node {NodeId} ({NodeName}) bound payload to scope '{OutputVariable}' (source='{PayloadKey}', kind={Kind})",
            context.Node.Id,
            context.Node.Name,
            outputVariable,
            payloadKey,
            payloadElement.ValueKind);

        return Task.FromResult(NodeOutput.Ok(
            context.Node.Id,
            outputVariable,
            data: payloadElement,
            textContent: null,
            confidence: null,
            metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
    }

    private static string? TryGetParameter(IReadOnlyDictionary<string, string> parameters, string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (parameters is null) return null;
        return parameters.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>
    /// ConfigJson contract for Start nodes. All fields are optional; defaults match the
    /// R4 daily-briefing playbooks. The <c>scope</c>, <c>description</c>, and
    /// <c>_comment</c> fields carried in deployed JSON are ignored here (documentation
    /// only).
    /// </summary>
    internal sealed record StartNodeConfig
    {
        /// <summary>
        /// Optional parameter-dictionary key that holds the dispatch payload. When
        /// unset, the executor falls back to <see cref="DefaultPayloadParameterKey"/>.
        /// </summary>
        [JsonPropertyName("payloadParameter")]
        public string? PayloadParameter { get; init; }

        /// <summary>
        /// Optional schema documentation for the expected payload. Keys are payload
        /// field names; values are TypeScript-style type hints (free-form strings).
        /// Enforcement is opt-in via <see cref="ValidateOnExecute"/>.
        /// </summary>
        [JsonPropertyName("inputContract")]
        public Dictionary<string, string>? InputContract { get; init; }

        /// <summary>
        /// When true and <see cref="InputContract"/> is non-empty, the executor checks
        /// that every contract key is present in the parsed payload at execute time.
        /// Missing keys produce a validation-error NodeOutput. Default false.
        /// </summary>
        [JsonPropertyName("validateOnExecute")]
        public bool? ValidateOnExecute { get; init; }
    }
}
