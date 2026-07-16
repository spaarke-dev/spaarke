using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Deterministic session context consumed by the tool-list pre-filter
/// (FR-P2-01 step 3). Every member is a structural FACT about the session —
/// never derived from utterance content (the pre-filter is filtering, not a
/// second intent-detection mechanism; ADR-039).
/// </summary>
/// <param name="Surface">
/// The session's placement surface token (§4.1 vocabulary). Chat sessions are
/// always the <c>assistant</c> surface.
/// </param>
/// <param name="HasSessionFiles">Whether the session carries uploaded files.</param>
/// <param name="HasActiveDocument">Whether an active document is bound to the session.</param>
/// <param name="HasAnalysisBinding">Whether the session is bound to a <c>sprk_analysisoutput</c> record.</param>
/// <param name="HasAttachedRecord">
/// FR-H1 grounding fact (task 044): whether the chat session is hosted on a valid attached/regarding
/// record (<c>ChatHostContext.IsValid()</c> — a genuine host entity, threaded from
/// <c>SprkChatAgentFactory</c>). Fed to the PreFilter's <c>requires-no-attached-record</c> predicate to
/// remove capabilities that only make sense with no record in context (e.g. "Create matter" inside a
/// matter). A structural session FACT — never derived from utterance content (ADR-039). Optional with a
/// <c>false</c> default so existing construction sites (no host record) are unchanged.
/// </param>
public sealed record AgentToolFilterContext(
    string Surface,
    bool HasSessionFiles,
    bool HasActiveDocument,
    bool HasAnalysisBinding,
    bool HasAttachedRecord = false)
{
    /// <summary>The assistant (chat) surface token.</summary>
    public const string AssistantSurface = "assistant";
}

/// <summary>
/// The agent-turn loop's tool-list finalizer (FR-P2-01 / ADR-039 / NFR-04):
/// deterministic pre-filter → deterministic ordering → budget wrap.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pre-filter is the ONLY permitted dispatch aid (ADR-039)</b>: it narrows
/// the tool list from session-context facts and catalog columns BEFORE the
/// model sees it. It is a pure predicate — no scoring, no classification, no
/// utterance inspection. Rules:
/// </para>
/// <list type="bullet">
///   <item>Projected capability tools (<see cref="BindingCapabilityTool"/>) pass
///   only when the Binding's <c>sprk_surfaces</c> catalog column is empty
///   (= all surfaces, per column dictionary) or contains the session's surface.</item>
///   <item>Handler tools (<c>sprk_analysistool</c> rows) are already narrowed
///   upstream by the catalog's <c>sprk_availableincontexts</c> +
///   <c>sprk_requiredcapability</c> columns; they pass through unchanged. When
///   future catalog columns declare structural context requirements (e.g.
///   requires-attached-record), they are applied HERE against
///   <see cref="AgentToolFilterContext"/> facts — never by tool-name lists.</item>
/// </list>
/// <para>
/// <b>Prompt-cache stability (NFR-04)</b>: the surviving tools are sorted by
/// function name with <see cref="StringComparer.Ordinal"/>, so the projected
/// tool block serializes identically across turns for the same session state.
/// <see cref="ComputeProjectionFingerprint"/> hashes the ordered
/// name/description/schema triples; the factory logs it per creation so
/// cache-stability is observable (same fingerprint ⇒ byte-identical tool block
/// ⇒ Azure OpenAI prompt-cache prefix hits).
/// </para>
/// </remarks>
public static class AgentToolProjection
{
    /// <summary>
    /// Applies the deterministic pre-filter, sorts the survivors ordinally by
    /// name (NFR-04), and wraps each in a <see cref="BudgetedAIFunction"/>
    /// enforcing the turn contract.
    /// </summary>
    public static IReadOnlyList<AIFunction> Finalize(
        IEnumerable<AIFunction> tools,
        AgentToolFilterContext filterContext,
        AgentTurnContract turnContract,
        CitationContext? citations,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(filterContext);
        ArgumentNullException.ThrowIfNull(turnContract);
        ArgumentNullException.ThrowIfNull(logger);

        return PreFilter(tools, filterContext)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(AIFunction (t) => new BudgetedAIFunction(t, turnContract, citations, logger))
            .ToArray();
    }

    /// <summary>
    /// The deterministic pre-filter (pure; same inputs ⇒ same outputs).
    /// Exposed for direct testability.
    /// </summary>
    public static IEnumerable<AIFunction> PreFilter(
        IEnumerable<AIFunction> tools,
        AgentToolFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var tool in tools)
        {
            // Binding-projected tools (generic capability + the FR-P2-04 refusal
            // capability) filter on the catalog's sprk_surfaces column — the same
            // pure predicate for both projections.
            var surfaces = tool switch
            {
                BindingCapabilityTool capability => capability.Binding.Surfaces,
                RefusalCapabilityTool refusal => refusal.Binding.Surfaces,
                _ => null,
            };

            if (surfaces is not null)
            {
                // Empty = offered on ALL surfaces (column dictionary rule).
                var surfaceMatch = surfaces.Count == 0
                    || surfaces.Contains(context.Surface, StringComparer.OrdinalIgnoreCase);
                if (!surfaceMatch)
                {
                    continue;
                }
            }

            // FR-H1 grounding predicate (task 044): a capability whose Binding declares
            // requires-no-attached-record is REMOVED when the session is hosted on an attached record
            // (e.g. hide "Create matter" when already inside a matter). A pure predicate over the
            // threaded HasAttachedRecord fact — no model call, no tool-name list (ADR-039 §3.2
            // removes-the-impossible). Scoped to BindingCapabilityTool: the RefusalCapabilityTool
            // (no-match handler) is NEVER grounding-filtered — it must survive to enforce honest refusal.
            if (tool is BindingCapabilityTool capabilityTool
                && capabilityTool.Binding.RequiresNoAttachedRecord
                && context.HasAttachedRecord)
            {
                continue;
            }

            yield return tool;
        }
    }

    /// <summary>
    /// SHA-256 fingerprint of the ordered projected tool block
    /// (name + description + schema per tool, in list order). Two turns with the
    /// same session state produce the same fingerprint — the NFR-04
    /// cache-stability evidence surface. Unwraps <see cref="BudgetedAIFunction"/>
    /// so wrapping does not perturb the fingerprint.
    /// </summary>
    public static string ComputeProjectionFingerprint(IEnumerable<AIFunction> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var sb = new StringBuilder();
        foreach (var tool in tools)
        {
            var t = tool is BudgetedAIFunction b ? b.Inner : tool;
            sb.Append(t.Name).Append('\u001F')
              .Append(t.Description).Append('\u001F')
              .Append(t.JsonSchema.GetRawText()).Append('\u001E');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
    }
}
