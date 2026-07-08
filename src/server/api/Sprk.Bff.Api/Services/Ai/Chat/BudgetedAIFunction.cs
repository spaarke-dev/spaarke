using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// The loop-contract decorator for every tool projected into the agent turn
/// (FR-P2-01, spaarke-ai-architecture-redesign-r1 task 030). Wraps an inner
/// <see cref="AIFunction"/> and, per invocation:
/// </summary>
/// <remarks>
/// <list type="number">
///   <item><b>Budget enforcement (ADR-039 / NFR-09)</b> — reserves one unit of the
///   per-turn tool-call budget via <see cref="AgentTurnContract.TryBeginToolCall"/>.
///   When exhausted, the inner tool is NOT executed; the model receives the
///   deterministic grounded budget-exhausted message so the turn ends gracefully.</item>
///   <item><b>ToolChain audit recording (ADR-040 / NFR-07)</b> — records a
///   <see cref="SessionToolCall"/> (tool id, identifier/filter args summary,
///   citation ids delta, duration) on the turn contract; the chat endpoint
///   persists the accumulated chain to the session ledger BEFORE rendering.</item>
///   <item><b>Budget telemetry (ADR-016)</b> — logs <c>[ADR-016][agent-turn.*]</c>
///   structured events: spent/budget per call, and a dedicated
///   <c>budget_exceeded</c> event on the first denial. Identifiers + counts only
///   (ADR-015) — argument payloads and result content are never logged.</item>
/// </list>
/// <para>
/// The wrapper preserves the inner function's name / description / schema
/// verbatim so the projected tool block the model sees is byte-identical to the
/// unwrapped projection — a prerequisite for the NFR-04 prompt-cache-stable
/// serialization (the wrapper changes execution semantics, not declaration).
/// </para>
/// </remarks>
public sealed class BudgetedAIFunction : AIFunction
{
    private readonly AIFunction _inner;
    private readonly AgentTurnContract _turn;
    private readonly CitationContext? _citations;
    private readonly ILogger _logger;

    public BudgetedAIFunction(
        AIFunction inner,
        AgentTurnContract turn,
        CitationContext? citations,
        ILogger logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _turn = turn ?? throw new ArgumentNullException(nameof(turn));
        _citations = citations;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The wrapped inner function (exposed for projection fingerprinting + tests).</summary>
    public AIFunction Inner => _inner;

    /// <inheritdoc />
    public override string Name => _inner.Name;

    /// <inheritdoc />
    public override string Description => _inner.Description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => _inner.JsonSchema;

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        if (!_turn.TryBeginToolCall())
        {
            // ADR-016 budget telemetry: identifiers + counts only.
            _logger.LogWarning(
                "[ADR-016][agent-turn.budget_exceeded] tool call denied — tool={ToolName} " +
                "budget={Budget} spent={Spent} denied={Denied}",
                Name, _turn.ToolCallBudget, _turn.BudgetSpent, _turn.DeniedCalls);

            // Grounded graceful end-of-turn: the model is told to conclude from
            // gathered results — the inner tool is never executed past the budget.
            return _turn.BuildBudgetExhaustedMessage();
        }

        var citationsBefore = _citations?.Count ?? 0;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await _inner.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();

            // Citation ids registered by THIS call (delta over the per-turn
            // accumulator; ids only — never quotes/excerpts, NFR-07).
            //
            // SEQUENTIAL-INVOCATION PREREQUISITE (Step 9.5 review W1, 2026-07-06):
            // the before/after count delta attributes citations correctly ONLY because
            // the function-invocation client executes tool calls sequentially
            // (FunctionInvokingChatClient default AllowConcurrentInvocation=false —
            // see AiModule's UseFunctionInvocation). If concurrent invocation is ever
            // enabled, replace this with a per-call watermark on the citation id
            // counter (or per-call attribution inside CitationContext).
            IReadOnlyList<string>? citationIds = null;
            if (_citations is not null && _citations.Count > citationsBefore)
            {
                citationIds = _citations.GetCitations()
                    .Where(c => c.CitationId > citationsBefore)
                    .Select(c => c.ChunkId)
                    .ToArray();
            }

            _turn.RecordCall(new SessionToolCall
            {
                ToolId = Name,
                ArgsSummary = AgentTurnContract.SummarizeArguments(arguments),
                ResultCount = citationIds?.Count,
                Citations = citationIds,
                DurationMs = stopwatch.ElapsedMilliseconds,
            });

            _logger.LogInformation(
                "[ADR-016][agent-turn.budget] tool executed — tool={ToolName} spent={Spent}/{Budget} " +
                "durationMs={DurationMs} citations={CitationCount}",
                Name, _turn.BudgetSpent, _turn.ToolCallBudget,
                stopwatch.ElapsedMilliseconds, citationIds?.Count ?? 0);
        }
    }
}
