using System.Text;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Result of evaluating one completed agent turn against the citation clause of
/// the loop contract (FR-P2-01 step 4 / ADR-039 grounded-execution invariant:
/// "tool-composed answers carry citations").
/// </summary>
/// <param name="ReadResultsConsumed">Read-tool results (citations) were registered this turn.</param>
/// <param name="CitationsAvailable">Citations registered by tools during the turn.</param>
/// <param name="CitationMarkersInText">Distinct <c>[N]</c> markers present in the assistant text.</param>
/// <param name="Violation">
/// True when the turn consumed read-tool results but the rendered text cites
/// none of them — an uncited read-derived answer, which fails the turn contract.
/// </param>
public sealed record CitationEnforcementResult(
    bool ReadResultsConsumed,
    int CitationsAvailable,
    int CitationMarkersInText,
    bool Violation);

/// <summary>
/// Citation enforcement on reads (FR-P2-01 / ADR-039): every read-tool result
/// surfaced in the loop's output MUST carry citations; ungrounded free-form
/// output over tool reads is forbidden.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mechanism</b>: tools register citations on the per-turn
/// <see cref="CitationContext"/> as they execute (the existing Wave-7b
/// accumulation seam). After the turn's text completes,
/// <see cref="Evaluate"/> checks that a turn which consumed read results
/// (citations registered &gt; 0) rendered at least one <c>[N]</c> citation
/// marker. A violating turn is repaired by appending the deterministic
/// <see cref="BuildRepairSuffix"/> sources block — the rendered turn ALWAYS
/// carries its grounding — and the violation is telemetered
/// (<c>[agent-turn.citation_violation]</c>) so eval/regression suites can
/// assert on it (uncited read-derived claims fail the turn contract).
/// </para>
/// <para>
/// <b>What this is not</b>: no LLM re-ask, no semantic claim-checking — the
/// enforcement is deterministic and cheap. The NFR-13 CitationSafetyCheck
/// middleware (semantic safety) is unchanged and runs regardless.
/// </para>
/// </remarks>
public static partial class AgentTurnCitationEnforcer
{
    [GeneratedRegex(@"\[(\d{1,3})\]")]
    private static partial Regex CitationMarkerRegex();

    /// <summary>
    /// Evaluates a completed turn's text against the citations registered during
    /// the turn. Pure and deterministic.
    /// </summary>
    public static CitationEnforcementResult Evaluate(string? assistantText, CitationContext? citations)
    {
        var available = citations?.Count ?? 0;
        if (available == 0)
        {
            // No read results consumed — nothing to cite (prompt-controlled or
            // conversational turn; grounding classes (a)/(c)/(d) apply upstream).
            return new CitationEnforcementResult(false, 0, 0, false);
        }

        var markers = string.IsNullOrEmpty(assistantText)
            ? 0
            : CitationMarkerRegex().Matches(assistantText)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .Count();

        return new CitationEnforcementResult(
            ReadResultsConsumed: true,
            CitationsAvailable: available,
            CitationMarkersInText: markers,
            Violation: markers == 0);
    }

    /// <summary>
    /// Deterministic sources block appended to a violating turn so the rendered
    /// output carries its grounding (identifiers + source names only — excerpts
    /// stay in the <c>citations</c> SSE event where the ADR-015 cap applies).
    /// </summary>
    public static string BuildRepairSuffix(CitationContext citations)
    {
        ArgumentNullException.ThrowIfNull(citations);

        var sb = new StringBuilder();
        sb.Append("\n\nSources: ");
        var first = true;
        foreach (var c in citations.GetCitations())
        {
            if (!first)
            {
                sb.Append(" · ");
            }

            sb.Append('[').Append(c.CitationId).Append("] ").Append(c.SourceName);
            first = false;
        }

        return sb.ToString();
    }
}
