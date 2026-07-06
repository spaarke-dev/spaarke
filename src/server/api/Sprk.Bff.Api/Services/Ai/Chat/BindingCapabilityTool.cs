using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Projects one catalog Binding row (<c>sprk_playbookconsumer</c>) into the
/// agent-turn loop's tool list as a capability tool (FR-P2-01 /
/// ADR-039 loop-as-dispatcher). The function's name is derived deterministically
/// from the Binding's consumer type; its description is the maker-authored
/// <see cref="Binding.ToolDescription"/> (the §6.2 intent surface); its parameter
/// schema is the target Action's <see cref="Binding.InputSchemaJson"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>ONE dispatch protocol (ADR-039)</b>: invocation delegates to the SAME
/// executor stack as the Click path — <see cref="SessionDispatchOrchestrator"/>
/// resolves the Binding BY ID and executes via ActionRunner with the universal
/// ledger write (<see cref="IOutputRouter"/>) BEFORE the terminal chunk
/// (ADR-040 storage-precedes-rendering). The loop choosing this tool IS the
/// text-path dispatch decision; no second intent mechanism exists here — the
/// model picks from the closed, projected catalog and function-calling
/// validation rejects anything unlisted.
/// </para>
/// <para>
/// <b>Catalog-column gating (ADR-039)</b>: which Bindings project is decided by
/// catalog columns only — <c>sprk_tooldescription</c> non-empty (the maker's
/// explicit opt-in to text-path invocation) and <c>sprk_surfaces</c> ∋ the
/// session's surface (applied by <see cref="AgentToolProjection"/>). No
/// tool-name lists anywhere.
/// </para>
/// <para>
/// <b>Scope discipline (ADR-010)</b>: the tool captures the ROOT
/// <see cref="IServiceProvider"/> and opens a fresh scope per invocation —
/// the factory's creation scope is disposed long before the LLM invokes tools.
/// </para>
/// <para>
/// <b>Gate seam note (W-P2-B integration)</b>: side-effecting capabilities are
/// not executed here ungated — the P1/P2 dispatch envelope rejects non-prompted
/// kinds / non-informational dispositions pre-run, and FR-P2-02 (task 031)
/// generalizes the pending store so write-shaped dispatch suspends through the
/// ONE confirmation gate keyed on <c>side_effect_class</c>. This tool codes
/// against the orchestrator seam; the gate slots in behind it without changing
/// this projection.
/// </para>
/// </remarks>
public sealed class BindingCapabilityTool : AIFunction
{
    /// <summary>Deterministic function-name prefix for projected capability tools.</summary>
    public const string NamePrefix = "capability_";

    /// <summary>Cap on the capability output text handed back to the model as the tool result.</summary>
    internal const int MaxResultChars = 8_000;

    private static readonly JsonElement DefaultSchema = JsonDocument.Parse(
        """
        {"type":"object","properties":{"fileIds":{"type":"array","items":{"type":"string"},"description":"Optional subset of session file ids the capability should operate on. Omit to use all session files."}},"additionalProperties":true}
        """).RootElement.Clone();

    private readonly Binding _binding;
    private readonly IServiceProvider _rootServices;
    private readonly string _tenantId;
    private readonly string _sessionId;
    private readonly ILogger _logger;
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _schema;

    public BindingCapabilityTool(
        Binding binding,
        IServiceProvider rootServices,
        string tenantId,
        string sessionId,
        ILogger logger)
    {
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
        _tenantId = !string.IsNullOrWhiteSpace(tenantId) ? tenantId : throw new ArgumentException("tenantId required", nameof(tenantId));
        _sessionId = !string.IsNullOrWhiteSpace(sessionId) ? sessionId : throw new ArgumentException("sessionId required", nameof(sessionId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(binding.ToolDescription))
        {
            throw new ArgumentException(
                $"Binding {binding.BindingId} has no sprk_tooldescription — not text-projectable (ADR-039 catalog opt-in).",
                nameof(binding));
        }

        _name = BuildFunctionName(binding.ConsumerType);
        _description = binding.ToolDescription!;
        _schema = ParseSchema(binding.InputSchemaJson);
    }

    /// <summary>The projected Binding (exposed for pre-filter + tests).</summary>
    public Binding Binding => _binding;

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string Description => _description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => _schema;

    /// <summary>
    /// Deterministic capability-tool name for a consumer type:
    /// <c>capability_{consumerType}</c> with non-identifier characters mapped to
    /// <c>_</c> (LLM function-name charset: <c>[a-zA-Z0-9_-]</c>). Pure — same
    /// input always yields the same name (NFR-04 cache stability).
    /// </summary>
    public static string BuildFunctionName(string consumerType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerType);
        var sanitized = new string(consumerType
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_')
            .ToArray());
        return NamePrefix + sanitized;
    }

    private static JsonElement ParseSchema(string? inputSchemaJson)
    {
        if (string.IsNullOrWhiteSpace(inputSchemaJson))
        {
            return DefaultSchema;
        }

        try
        {
            using var doc = JsonDocument.Parse(inputSchemaJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return DefaultSchema;
            }

            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Maker-authored schema is malformed — degrade to the permissive default
            // (routing/projection never throws on maker data; same tolerance as
            // ConsumerRoutingService JSON parsing).
            return DefaultSchema;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // Fresh scope per invocation: the orchestrator + its dependencies are Scoped,
        // and the agent-creation scope is long gone by the time the LLM calls tools.
        await using var scope = _rootServices.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetService<SessionDispatchOrchestrator>();
        if (orchestrator is null)
        {
            return "This capability is not available in the current environment (dispatch service disabled).";
        }

        JsonElement? args = null;
        if (arguments is { Count: > 0 })
        {
            args = JsonSerializer.SerializeToElement(
                arguments.ToDictionary(a => a.Key, a => a.Value));
        }

        var request = new SessionDispatchRequest(_tenantId, _sessionId, _binding.BindingId, args);

        try
        {
            string? summary = null;
            string? error = null;
            await foreach (var chunk in orchestrator.DispatchAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (!chunk.Done)
                {
                    continue;
                }

                error = chunk.Error;
                summary = chunk.Summary ?? chunk.Content;
            }

            if (error is not null)
            {
                _logger.LogWarning(
                    "[agent-turn.capability] dispatch error — binding={BindingId} consumerType={ConsumerType} session={SessionId}",
                    _binding.BindingId, _binding.ConsumerType, _sessionId);
                return $"The '{_binding.ConsumerType}' capability failed: {error}. Tell the user honestly; do not fabricate its output.";
            }

            var text = summary ?? string.Empty;
            if (text.Length > MaxResultChars)
            {
                text = text[..MaxResultChars] + "…[truncated]";
            }

            // The capability output was ledger-written by the dispatch stack BEFORE this
            // return (ADR-040) — the model composes over an already-grounded output.
            return $"Capability '{_binding.ConsumerType}' completed. Output (already stored to the session ledger):\n{text}";
        }
        catch (DispatchRejectedException ex)
        {
            // Clean catalog-boundary refusal (unsupported kind/disposition at this phase,
            // disabled row) — relay honestly, never fabricate (ADR-039 grounded outputs).
            _logger.LogInformation(
                "[agent-turn.capability] dispatch rejected — binding={BindingId} errorCode={ErrorCode}",
                _binding.BindingId, ex.ErrorCode);
            return $"The '{_binding.ConsumerType}' capability cannot run right now ({ex.ErrorCode}). Tell the user honestly and suggest an alternative.";
        }
    }
}
