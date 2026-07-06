using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Tunable platform settings for the agent-turn loop contract (FR-P2-01,
/// spaarke-ai-architecture-redesign-r1 task 030). Bound from the
/// <c>Ai:AgentTurn</c> configuration section (NFR-09 / ADR-016: the per-turn
/// tool-call cap is a platform setting, not a hardcoded constant).
/// </summary>
public sealed class AgentTurnOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ai:AgentTurn";

    /// <summary>
    /// Maximum number of tool calls the loop may EXECUTE in one turn
    /// (ADR-039: "MUST enforce a per-turn tool-call budget"). Default 8 per
    /// spec FR-P2-01 / NFR-09. Calls beyond the budget are denied with a
    /// grounded budget-exhausted message so the model concludes the turn
    /// from results already gathered.
    /// </summary>
    public int ToolCallBudget { get; set; } = 8;
}

/// <summary>
/// Per-agent, per-turn state of the agent-turn loop contract (FR-P2-01 /
/// ADR-039): tool-call budget accounting and the NFR-07-safe tool-call audit
/// trail that becomes the turn's <see cref="SessionToolChain"/> ledger entry
/// (ADR-040).
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime</b>: one instance per <see cref="SprkChatAgent"/> (same pattern
/// as <see cref="CitationContext"/>). <see cref="SprkChatAgent.SendMessageAsync"/>
/// calls <see cref="Reset"/> at the start of every message so budget and audit
/// state are scoped to a single turn.
/// </para>
/// <para>
/// <b>NFR-07 / ADR-015 BINDING</b>: recorded calls carry identifiers, filters,
/// counts, durations, and citation ids ONLY — never verbatim tool arguments,
/// result content, or user text. <see cref="SummarizeArguments"/> is the single
/// enforcement point: argument values are included only when they are
/// identifier-shaped (GUID / number / bool / short whitespace-free token);
/// everything else is redacted to a length marker.
/// </para>
/// <para>
/// <b>Thread safety</b>: tool calls may execute concurrently within one LLM
/// iteration; state uses <see cref="Interlocked"/> + a lock around the call list.
/// </para>
/// </remarks>
public sealed class AgentTurnContract
{
    private readonly object _gate = new();
    private readonly List<SessionToolCall> _calls = [];
    private int _executedCalls;
    private int _deniedCalls;
    private int _persistedCallCount;

    /// <summary>Creates a contract with the given per-turn tool-call budget.</summary>
    /// <param name="toolCallBudget">Per-turn executed-tool-call cap (default 8 per FR-P2-01).</param>
    public AgentTurnContract(int toolCallBudget)
    {
        if (toolCallBudget < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toolCallBudget), toolCallBudget, "Per-turn tool-call budget must be >= 1.");
        }

        ToolCallBudget = toolCallBudget;
    }

    /// <summary>The per-turn executed-tool-call cap (NFR-09; default 8).</summary>
    public int ToolCallBudget { get; }

    /// <summary>Tool calls executed this turn (the ADR-016 "budget spent" figure).</summary>
    public int BudgetSpent => Volatile.Read(ref _executedCalls);

    /// <summary>Tool calls denied this turn because the budget was exhausted.</summary>
    public int DeniedCalls => Volatile.Read(ref _deniedCalls);

    /// <summary>True once the budget is fully spent (subsequent calls are denied).</summary>
    public bool BudgetExhausted => BudgetSpent >= ToolCallBudget;

    /// <summary>
    /// Attempts to reserve one budget unit for a tool call. Returns <c>false</c>
    /// (and counts a denial) when the budget is exhausted — the caller returns the
    /// grounded budget-exhausted message to the model instead of executing.
    /// </summary>
    public bool TryBeginToolCall()
    {
        while (true)
        {
            var current = Volatile.Read(ref _executedCalls);
            if (current >= ToolCallBudget)
            {
                Interlocked.Increment(ref _deniedCalls);
                return false;
            }

            if (Interlocked.CompareExchange(ref _executedCalls, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    /// <summary>Records one executed tool call (identifiers/filters/counts only — NFR-07).</summary>
    public void RecordCall(SessionToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        lock (_gate)
        {
            _calls.Add(call);
        }
    }

    /// <summary>Ordered snapshot of every call recorded this turn.</summary>
    public IReadOnlyList<SessionToolCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    /// <summary>
    /// True when calls have been recorded that are not yet covered by a
    /// <see cref="DrainUnpersistedCalls"/> snapshot — i.e. a ledger write is due
    /// BEFORE the next rendered content (ADR-040 storage-precedes-rendering).
    /// </summary>
    public bool HasUnpersistedCalls
    {
        get
        {
            lock (_gate)
            {
                return _calls.Count > _persistedCallCount;
            }
        }
    }

    /// <summary>
    /// Snapshots the calls recorded since the last drain and marks them persisted.
    /// The caller writes the returned segment to the session ledger BEFORE
    /// rendering the content that follows it. Returns an empty list when nothing
    /// is pending (callers skip the ledger write).
    /// </summary>
    public IReadOnlyList<SessionToolCall> DrainUnpersistedCalls()
    {
        lock (_gate)
        {
            if (_calls.Count == _persistedCallCount)
            {
                return Array.Empty<SessionToolCall>();
            }

            var segment = _calls.Skip(_persistedCallCount).ToArray();
            _persistedCallCount = _calls.Count;
            return segment;
        }
    }

    /// <summary>
    /// Resets budget + audit state for a new turn. Called by
    /// <see cref="SprkChatAgent.SendMessageAsync"/> before each message (same
    /// per-message scoping as <see cref="CitationContext.Reset"/>).
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            _calls.Clear();
            _persistedCallCount = 0;
        }

        Interlocked.Exchange(ref _executedCalls, 0);
        Interlocked.Exchange(ref _deniedCalls, 0);
    }

    /// <summary>
    /// The deterministic, grounded message returned to the model when the
    /// per-turn budget is exhausted (FR-P2-01: budget-exceeded behavior ends the
    /// turn gracefully with a grounded message — never a raw exception).
    /// </summary>
    public string BuildBudgetExhaustedMessage() =>
        $"TOOL BUDGET EXHAUSTED: this turn's tool-call budget ({ToolCallBudget} calls) has been spent. " +
        "Do not request any more tools. Compose your final answer NOW using only the tool results " +
        "already gathered in this turn, citing them with their [N] markers where applicable. " +
        "If part of the request could not be completed within the budget, say so explicitly and " +
        "suggest the user narrow the request — do not guess or fabricate the missing part.";

    /// <summary>
    /// Builds the NFR-07-safe identifier/filter summary of an LLM tool-call
    /// argument payload (e.g. <c>"matterId=8f4…; top=5; query=&lt;redacted:42&gt;"</c>).
    /// Values are included verbatim ONLY when identifier-shaped: GUID, number,
    /// boolean, or a whitespace-free token of at most <see cref="MaxIdentifierValueLength"/>
    /// characters. Anything else — free text, document content, user prose — is
    /// redacted to <c>&lt;redacted:len&gt;</c>. Additionally, values under
    /// conventionally content-bearing argument NAMES (<see cref="AlwaysRedactedArgNames"/>)
    /// are redacted regardless of shape: a single-token <c>query="Rosebud"</c> is still
    /// verbatim user content (Step 9.5 review W2, 2026-07-06).
    /// </summary>
    public static string? SummarizeArguments(AIFunctionArguments? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var kvp in arguments.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }

            var value = AlwaysRedactedArgNames.Contains(kvp.Key)
                ? $"<redacted:{ValueLength(kvp.Value)}>"
                : SummarizeValue(kvp.Value);
            sb.Append(kvp.Key).Append('=').Append(value);

            if (sb.Length >= MaxSummaryLength)
            {
                sb.Append("; …");
                break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Argument names whose values are ALWAYS redacted regardless of shape (defense in
    /// depth for NFR-07): these keys conventionally carry user/document text, and even a
    /// single-token value is verbatim user content.
    /// </summary>
    internal static readonly HashSet<string> AlwaysRedactedArgNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "query", "text", "body", "message", "content", "prompt", "instruction", "question",
        // FR-P2-04 (task 033): the refusal tool's request label paraphrases the user's
        // utterance — redact by name so it never lands in the ToolChain audit (NFR-07).
        RefusalCapabilityTool.UnsupportedRequestArgName,
        // FR-P3-02 (task 041): email.draft's subject line is drafted from document/session
        // content — even a single-token subject is verbatim content; redact by name (NFR-07).
        "subject",
    };

    private static int ValueLength(object? value) => value switch
    {
        null => 0,
        string s => s.Length,
        JsonElement je when je.ValueKind == JsonValueKind.String => (je.GetString() ?? string.Empty).Length,
        JsonElement je => je.GetRawText().Length,
        _ => value.ToString()?.Length ?? 0,
    };

    /// <summary>Max identifier-shaped value length included verbatim in an args summary.</summary>
    internal const int MaxIdentifierValueLength = 64;

    /// <summary>Soft cap on the total args-summary length (ledger entries stay compact).</summary>
    internal const int MaxSummaryLength = 512;

    private static string SummarizeValue(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case bool b:
                return b ? "true" : "false";
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "?";
            case Guid g:
                return g.ToString("D");
            case string s:
                return SummarizeString(s);
            case JsonElement je:
                return SummarizeJsonElement(je);
            case System.Collections.IEnumerable list:
                var count = 0;
                foreach (var _ in list) count++;
                return $"<list:{count}>";
            default:
                return $"<{value.GetType().Name}>";
        }
    }

    private static string SummarizeString(string s)
    {
        if (Guid.TryParse(s, out var g))
        {
            return g.ToString("D");
        }

        // Identifier-shaped: short, whitespace-free token (ids, enum values, filters).
        if (s.Length <= MaxIdentifierValueLength && !s.Any(char.IsWhiteSpace))
        {
            return s;
        }

        // Free text (may echo user content / document text) — NFR-07: length marker only.
        return $"<redacted:{s.Length}>";
    }

    private static string SummarizeJsonElement(JsonElement je) => je.ValueKind switch
    {
        JsonValueKind.String => SummarizeString(je.GetString() ?? string.Empty),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => je.GetRawText(),
        JsonValueKind.Null or JsonValueKind.Undefined => "null",
        JsonValueKind.Array => $"<list:{je.GetArrayLength()}>",
        JsonValueKind.Object => $"<object:{je.GetRawText().Length}>",
        _ => "<json>",
    };
}
