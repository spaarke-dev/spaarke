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
/// <param name="OpenTabContextTypes">
/// FR-12 tool-economy fact (task 030): the closed-vocabulary context-types of the workspace tabs
/// currently OPEN in the session — derived deterministically from the LIVE <c>session.Tabs</c>
/// (the source-of-record that task 011 re-pointed the awareness block to) by mapping each tab's
/// <c>widgetType</c> / typed <c>WidgetData.Kind</c> through <see cref="WidgetContextTypeResolver"/>
/// (the C# mirror of the client widget-registry <c>WidgetMetadata.contextType</c> map, FR-08 / task 022).
/// Values are the <c>WidgetContextType</c> vocabulary (<c>email</c>, <c>document</c>, <c>compose-doc</c>,
/// <c>matter-grid</c>, <c>dashboard</c>, <c>calendar</c>). Fed to the PreFilter's tab-economy predicate so a
/// parity capability (one whose Binding declares <c>sprk_contexttypetags</c>) mounts ONLY while a matching
/// tab is open. A structural session FACT — reads only tab identity/category, never item content (ADR-015
/// Path A). <c>null</c> (the default) = the caller supplied NO open-tab awareness, so the tab-economy
/// predicate stays INERT (every pre-task-030 construction site is byte-identical); an EMPTY (non-null)
/// set means "tabs known, none open" and DOES scope tab-gated tools out.
/// </param>
/// <param name="AdvisoryToolAllowList">
/// spaarkeai-assistant-enhancements-r4 FR-02 (task 011): the closed grounded-tool allow-list of an
/// ADVISORY capability whose dispatch runs a NESTED bounded agent turn (design note Option A —
/// <c>notes/011-012-advisory-nested-turn-design.md</c>). It is the resolved Action's own catalog DATA
/// (task 010 <c>sprk_groundedtoolallowlist</c>, a list of <c>sprk_toolid</c> values such as
/// <c>spaarke.grid_overview</c>), applied AFTER the Action was already selected by binding id — so it is
/// the ADR-039-sanctioned deterministic context-scoping pre-filter, NEVER a classifier or a second dispatch
/// decider. When non-null, <see cref="AgentToolProjection.PreFilter"/> keeps ONLY the allow-listed grounded
/// READ handler tools and DROPS every capability-selecting tool (<see cref="BindingCapabilityTool"/> +
/// <see cref="RefusalCapabilityTool"/>) so the nested turn structurally cannot dispatch a second capability
/// (the one probabilistic DISPATCH decider remains the top-level Text-path turn). Match is by the projected
/// LLM-facing function name (<see cref="ToolHandlerToAIFunctionAdapter.SanitiseToolName"/>-normalized), so
/// <c>sprk_toolid</c>-authored allow-list entries line up with the projected handler names deterministically.
/// <c>null</c> (the default) = NOT an advisory turn → this narrowing stays INERT and the projection is
/// byte-identical to every pre-task-011 turn; an EMPTY (non-null) set is fail-closed (an advisory turn with
/// no grounded tools mounts none — only ever constructed by the task-012 runner, which requires a non-empty
/// list before routing to the nested turn).
/// </param>
public sealed record AgentToolFilterContext(
    string Surface,
    bool HasSessionFiles,
    bool HasActiveDocument,
    bool HasAnalysisBinding,
    bool HasAttachedRecord = false,
    IReadOnlyCollection<string>? OpenTabContextTypes = null,
    IReadOnlyCollection<string>? AdvisoryToolAllowList = null)
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
///   <item>FR-12 tool economy (task 030): a parity capability whose Binding declares
///   <c>sprk_contexttypetags</c> mounts only while a tab of a matching context-type is
///   open — a pure set-membership test against
///   <see cref="AgentToolFilterContext.OpenTabContextTypes"/>, the deterministic open-tab
///   context-type set (context scoping, the ONLY ADR-039-sanctioned dispatch aid; never a
///   classifier, ranker, or second dispatch surface). Empty tags = relevant to ANY context
///   (unaffected).</item>
///   <item>FR-02 advisory narrowing (task 011): when
///   <see cref="AgentToolFilterContext.AdvisoryToolAllowList"/> is non-null the turn is an ADVISORY
///   capability's NESTED bounded turn — the pre-filter keeps ONLY the allow-listed grounded READ handler
///   tools and drops EVERY <see cref="BindingCapabilityTool"/> + <see cref="RefusalCapabilityTool"/> so the
///   nested turn cannot dispatch a second capability. The allow-list is the resolved Action's catalog data
///   applied after the Action was selected by binding id (deterministic context scoping, ADR-039 — the ONE
///   probabilistic dispatch decider stays the top-level Text-path turn). Null = inert (byte-identical).</item>
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
            // FR-02 advisory nested-turn narrowing (R4 task 011; design note Option A). When
            // AdvisoryToolAllowList is non-null the projection is for an ADVISORY capability's NESTED
            // bounded turn: keep ONLY the allow-listed GROUNDED READ handler tools and DROP every
            // capability-selecting tool (BindingCapabilityTool + RefusalCapabilityTool), so the nested
            // turn structurally cannot dispatch a second capability (ADR-039: exactly one probabilistic
            // DISPATCH decider — the top-level Text-path turn — is preserved). The allow-list is the
            // resolved Action's own catalog DATA (task 010 sprk_groundedtoolallowlist), applied AFTER the
            // Action was already selected by binding id → deterministic context scoping, never intent
            // detection / a classifier / a routing surface. A grounded (handler) tool survives ONLY when
            // its projected function name is in the allow-list (sanitisation-normalized both sides so
            // sprk_toolid-authored entries like "spaarke.grid_overview" match the projected name). NULL
            // (every non-advisory turn) leaves this branch inert and the predicates below unchanged —
            // byte-identical projection. Empty (non-null) is fail-closed (zero grounded tools).
            if (context.AdvisoryToolAllowList is { } advisoryAllowList)
            {
                if (tool is BindingCapabilityTool or RefusalCapabilityTool)
                {
                    continue;
                }
                if (!AdvisoryAllowListContains(advisoryAllowList, tool.Name))
                {
                    continue;
                }
                yield return tool;
                continue;
            }

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

            // FR-12 tool-economy predicate (task 030): a parity capability whose Binding declares
            // sprk_contexttypetags (tab-scoped relevance) is REMOVED unless at least one currently-open
            // tab's context-type is in that tag set. Empty tags = relevant to ANY context (never scoped
            // out — the always-available default). A pure set-membership test over the deterministic
            // open-tab context-type set (threaded from live session.Tabs) ∩ the Binding's tags — no model
            // call, no scoring, no tool-name list (ADR-039: deterministic context-scoping pre-filter, the
            // ONLY sanctioned dispatch aid). Scoped to BindingCapabilityTool — the RefusalCapabilityTool
            // (no-match handler) is NEVER tab-scoped: it must survive to enforce honest refusal. Inert when
            // OpenTabContextTypes is null (caller supplied no tab awareness) — preserving every
            // pre-task-030 construction site byte-for-byte.
            if (tool is BindingCapabilityTool tabScopedCapability
                && context.OpenTabContextTypes is { } openTabContextTypes
                && tabScopedCapability.Binding.ContextTypeTags.Count > 0
                && !tabScopedCapability.Binding.ContextTypeTags.Any(
                    tag => openTabContextTypes.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return tool;
        }
    }

    /// <summary>
    /// Deterministic membership test for the FR-02 advisory allow-list (task 011). An allow-list entry
    /// (an <c>sprk_toolid</c> value authored on the Action's <c>sprk_groundedtoolallowlist</c>) matches a
    /// projected tool when its <see cref="ToolHandlerToAIFunctionAdapter.SanitiseToolName"/>-normalized form
    /// equals the tool's already-sanitised LLM-facing <paramref name="toolName"/>. Normalizing BOTH sides
    /// makes the match robust whether the maker authored the raw tool id or a display-shaped value — no
    /// hardcoded tool-name list; the allow-list is catalog data (ADR-039 §3.2).
    /// </summary>
    private static bool AdvisoryAllowListContains(IReadOnlyCollection<string> allowList, string toolName)
    {
        foreach (var entry in allowList)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }
            if (string.Equals(
                    ToolHandlerToAIFunctionAdapter.SanitiseToolName(entry),
                    toolName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
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
