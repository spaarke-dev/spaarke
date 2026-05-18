using System.Runtime.Caching;
using System.Text;
using System.Text.Json;
using Sprk.Bff.Api.Services.Ai.Capabilities;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Production singleton implementation of <see cref="IOrchestratorPromptBuilder"/>.
///
/// Constructs a two-layer system prompt for the orchestrator LLM:
///
///   Layer 1 — Stable prefix (target ~2000 tokens, cached by manifest hash):
///     Persona section, capability index (name + 1-line description for each enabled
///     capability), standing instructions (tool usage rules, citation requirements,
///     safety reminders, matter-isolation notice), and optional entity enrichment.
///
///   Layer 2 — Per-turn suffix (target 0–3000 tokens, never cached):
///     JSON block containing the full schema definitions of the 6–8 tools selected
///     by the capability router for the current turn. The chat client reads
///     <see cref="OrchestratorPrompt.ToolSchemaNames"/> to activate the correct
///     function-calling definitions.
///
/// Token budget enforcement (total 9000 tokens, chars / 4 heuristic):
///   - Capability index:  max 500 tokens
///   - Active tool schemas: max 3000 tokens (limits to MaxToolsPerTurn tools)
///   - Persona + standing instructions: max 1500 tokens
///   - Residual (history + response headroom): ~4000 tokens reserved by caller
///
/// When prefix + suffix would exceed the budget the builder trims capability index
/// descriptions (names only, no descriptions) and reduces <see cref="MaxToolsPerTurn"/>
/// by 2, then logs a warning.
///
/// Prefix caching:
///   The prefix is keyed by <c>{LastRefreshedUtc.Ticks}:{capabilityCount}</c>.
///   Cached for 20 minutes using <see cref="MemoryCache"/> (in-process, ADR-009 exception).
///   Cache hit → prefix reused without recomputation (byte-identical string = Azure OpenAI
///   prompt cache hit on the service side).
///
/// ADR-009 exception: prefix cache is in-process (MemoryCache), not Redis.
///   The prefix is structural metadata computed from the manifest and context;
///   sharing it across instances would not reduce LLM cost because Azure OpenAI
///   caching is per-connection, not cross-instance.
///
/// Thread-safety: all mutable state is in <see cref="MemoryCache"/> (thread-safe).
///   No instance fields are mutated after construction.
/// </summary>
public sealed class OrchestratorPromptBuilder : IOrchestratorPromptBuilder
{
    // ── Token budget constants ────────────────────────────────────────────────

    /// <summary>Total token budget for prefix + suffix.</summary>
    internal const int TotalTokenBudget = 9_000;

    /// <summary>Maximum tokens for the capability index section.</summary>
    internal const int MaxCapabilityIndexTokens = 500;

    /// <summary>Maximum tokens for the active tool schemas section.</summary>
    internal const int MaxToolSchemasTokens = 3_000;

    /// <summary>Maximum tokens for persona + standing instructions.</summary>
    internal const int MaxPersonaTokens = 1_500;

    /// <summary>Default maximum number of tool schemas injected per turn.</summary>
    internal const int MaxToolsPerTurn = 8;

    /// <summary>Reduced tool cap applied when the budget would be exceeded.</summary>
    internal const int ReducedToolsPerTurn = MaxToolsPerTurn - 2;

    /// <summary>Prefix cache lifetime.</summary>
    private static readonly TimeSpan PrefixCacheExpiry = TimeSpan.FromMinutes(20);

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ICapabilityManifest _manifest;
    private readonly ILogger<OrchestratorPromptBuilder> _logger;

    // In-process prefix cache (ADR-009 exception: structural metadata, not business data).
    private readonly MemoryCache _prefixCache = new("OrchestratorPromptBuilder.PrefixCache");

    public OrchestratorPromptBuilder(
        ICapabilityManifest manifest,
        ILogger<OrchestratorPromptBuilder> logger)
    {
        _manifest = manifest;
        _logger = logger;
    }

    // ── IOrchestratorPromptBuilder ────────────────────────────────────────────

    /// <inheritdoc />
    public OrchestratorPrompt BuildSystemPrompt(
        CapabilityRoutingResult routing,
        OrchestratorPromptContext context)
    {
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(context);

        // 1. Build (or retrieve cached) stable prefix.
        var (prefix, cacheHit) = GetOrBuildPrefix(context);

        // 2. Resolve tool names for this turn.
        var toolNames = ResolveToolNames(routing);

        // 3. Build per-turn suffix.
        var (suffix, activeTools) = BuildPerTurnSuffix(toolNames);

        // 4. Budget check: trim if prefix + suffix exceeds total budget.
        var prefixTokens = EstimateTokens(prefix);
        var suffixTokens = EstimateTokens(suffix);
        var total = prefixTokens + suffixTokens;

        if (total > TotalTokenBudget)
        {
            _logger.LogWarning(
                "OrchestratorPromptBuilder: token budget exceeded ({Total} > {Budget}). " +
                "Trimming capability index to names-only and reducing MaxToolsPerTurn to {ReducedCap}.",
                total, TotalTokenBudget, ReducedToolsPerTurn);

            // Re-build prefix with compact (names-only) capability index.
            prefix = BuildPrefixInternal(context, compactCapabilityIndex: true);
            // Evict stale cache entry so next call also gets the trimmed version.
            // (Budget overflow is rare; not worth a separate cache key.)
            _prefixCache.Remove(ManifestHash());

            // Re-build suffix with reduced tool cap.
            var reducedTools = toolNames.Take(ReducedToolsPerTurn).ToList();
            (suffix, activeTools) = BuildPerTurnSuffix(reducedTools);

            prefixTokens = EstimateTokens(prefix);
            suffixTokens = EstimateTokens(suffix);
            total = prefixTokens + suffixTokens;
        }

        var residualBudget = TotalTokenBudget - total;
        _logger.LogDebug(
            "OrchestratorPromptBuilder: built prompt. PrefixTokens={PrefixTokens}, " +
            "SuffixTokens={SuffixTokens}, Total={Total}, ResidualBudget={ResidualBudget}, " +
            "CacheHit={CacheHit}, Tools={ToolCount}",
            prefixTokens, suffixTokens, total, residualBudget, cacheHit, activeTools.Count);

        _logger.LogDebug(
            "OrchestratorPromptBuilder: budget utilisation — {Percentage}% of {Budget} tokens consumed, " +
            "{Residual} tokens remaining for history + user message",
            (int)(total * 100.0 / TotalTokenBudget), TotalTokenBudget, residualBudget);

        return new OrchestratorPrompt(
            SystemPromptPrefix: prefix,
            PerTurnSuffix: suffix,
            ToolSchemaNames: activeTools,
            EstimatedTokens: total,
            PrefixCacheHit: cacheHit);
    }

    // ── Prefix: caching ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the stable prefix from cache when the manifest hash matches,
    /// or builds and caches a fresh one.
    /// </summary>
    private (string Prefix, bool CacheHit) GetOrBuildPrefix(OrchestratorPromptContext context)
    {
        var cacheKey = ManifestHash();

        if (_prefixCache.Get(cacheKey) is string cached)
            return (cached, true);

        var prefix = BuildPrefixInternal(context, compactCapabilityIndex: false);

        _prefixCache.Set(
            cacheKey,
            prefix,
            new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.UtcNow.Add(PrefixCacheExpiry) });

        return (prefix, false);
    }

    /// <summary>
    /// Computes the manifest hash key used for prefix caching.
    /// Format: <c>{LastRefreshedUtc.Ticks}:{capabilityCount}</c>.
    /// Changes whenever the manifest is refreshed (new timestamp) or the capability
    /// count changes (added/removed capability).
    /// </summary>
    private string ManifestHash()
    {
        var all = _manifest.GetAll();
        return $"{_manifest.LastRefreshedUtc.Ticks}:{all.Count}";
    }

    // ── Prefix: construction ──────────────────────────────────────────────────

    /// <summary>
    /// Builds the full stable prefix string.
    /// </summary>
    /// <param name="context">Session context for persona personalisation.</param>
    /// <param name="compactCapabilityIndex">
    /// When <c>true</c>, the capability index lists names only (no descriptions)
    /// to save tokens after a budget overflow.
    /// </param>
    private string BuildPrefixInternal(OrchestratorPromptContext context, bool compactCapabilityIndex)
    {
        var sb = new StringBuilder(4096);

        // ── Section 1: Persona ────────────────────────────────────────────────
        var beforePersona = sb.Length;
        AppendPersona(sb, context);
        var personaTokens = EstimateTokens(sb.ToString().Substring(beforePersona));
        _logger.LogDebug(
            "OrchestratorPromptBuilder: component budget — Persona={PersonaTokens} tokens",
            personaTokens);

        // ── Section 2: Capability index ───────────────────────────────────────
        var beforeCapIndex = sb.Length;
        AppendCapabilityIndex(sb, compactCapabilityIndex);
        var capIndexTokens = EstimateTokens(sb.ToString().Substring(beforeCapIndex));
        _logger.LogDebug(
            "OrchestratorPromptBuilder: component budget — CapabilityIndex={CapIndexTokens} tokens (compact={Compact})",
            capIndexTokens, compactCapabilityIndex);

        // ── Section 3: Standing instructions ─────────────────────────────────
        var beforeStanding = sb.Length;
        AppendStandingInstructions(sb, context);
        var standingTokens = EstimateTokens(sb.ToString().Substring(beforeStanding));
        _logger.LogDebug(
            "OrchestratorPromptBuilder: component budget — StandingInstructions={StandingTokens} tokens",
            standingTokens);

        // ── Section 4: Entity enrichment (optional) ───────────────────────────
        var beforeEnrichment = sb.Length;
        AppendEntityEnrichment(sb, context);
        var enrichmentTokens = EstimateTokens(sb.ToString().Substring(beforeEnrichment));
        if (enrichmentTokens > 0)
        {
            _logger.LogDebug(
                "OrchestratorPromptBuilder: component budget — EntityEnrichment={EnrichmentTokens} tokens",
                enrichmentTokens);
        }

        var totalPrefixTokens = EstimateTokens(sb.ToString());
        _logger.LogDebug(
            "OrchestratorPromptBuilder: prefix total — Persona={PersonaTokens} + CapIndex={CapIndexTokens} + " +
            "Standing={StandingTokens} + Enrichment={EnrichmentTokens} = {TotalPrefixTokens} tokens",
            personaTokens, capIndexTokens, standingTokens, enrichmentTokens, totalPrefixTokens);

        return sb.ToString();
    }

    private static void AppendPersona(StringBuilder sb, OrchestratorPromptContext context)
    {
        sb.AppendLine("## Identity");

        if (!string.IsNullOrWhiteSpace(context.ActivePlaybookName))
        {
            sb.AppendLine(
                $"You are Spaarke AI, a legal technology assistant operating in the " +
                $"'{context.ActivePlaybookName}' workflow. " +
                $"You help legal professionals with document analysis, matter management, " +
                $"legal research, and AI-assisted drafting.");
        }
        else
        {
            sb.AppendLine(
                "You are Spaarke AI, a legal technology assistant embedded in the Spaarke " +
                "legal workspace platform. You help legal professionals with document analysis, " +
                "matter management, legal research, financial intelligence, and AI-assisted drafting.");
        }

        // First-turn orientation paragraph.
        if (context.ConversationTurnCount == 0)
        {
            sb.AppendLine();
            sb.AppendLine(
                "You have access to a set of tools listed below. Always select the most relevant " +
                "tool for the user's request. When in doubt, use search tools to ground your " +
                "response in the user's actual data before answering.");
        }
    }

    private void AppendCapabilityIndex(StringBuilder sb, bool compact)
    {
        var capabilities = _manifest.GetAll();
        if (capabilities.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Available Capabilities");
        sb.AppendLine("The following capabilities are active in this session:");
        sb.AppendLine();

        var indexTokens = 0;
        var i = 1;

        foreach (var cap in capabilities)
        {
            string line;
            if (compact)
            {
                line = $"{i}. {cap.CapabilityName}";
            }
            else
            {
                // CapabilityManifestEntry.Description is ≤120 chars by contract.
                line = $"{i}. **{cap.CapabilityName}** — {cap.Description}";
            }

            var lineTokens = EstimateTokens(line);
            if (indexTokens + lineTokens > MaxCapabilityIndexTokens)
            {
                _logger.LogDebug(
                    "OrchestratorPromptBuilder: capability index capped at {Count} of {Total} entries " +
                    "({Tokens} tokens limit).",
                    i - 1, capabilities.Count, MaxCapabilityIndexTokens);
                break;
            }

            sb.AppendLine(line);
            indexTokens += lineTokens;
            i++;
        }
    }

    private static void AppendStandingInstructions(StringBuilder sb, OrchestratorPromptContext context)
    {
        sb.AppendLine();
        sb.AppendLine("## Standing Instructions");
        sb.AppendLine("""
            - **Tool use**: Select tools based on the user's request. Prefer specific tools over broad ones.
            - **Single call per turn**: You will receive one set of tool schemas per turn; do not hallucinate tools that are not listed.
            - **Citations**: Always cite the source document, section, or record when quoting or paraphrasing retrieved content.
            - **Safety**: Do not generate advice that constitutes the practice of law. Summarise and highlight; do not opine on legal strategy.
            - **Accuracy**: If retrieved context is insufficient to answer confidently, say so and suggest what additional information would help.
            - **Format**: Use clear Markdown with headings, bullet points, and tables where appropriate. Keep responses concise unless the user asks for detail.
            """);

        // Matter isolation notice — always include when a tenant is known.
        if (!string.IsNullOrWhiteSpace(context.TenantId))
        {
            sb.AppendLine();
            sb.AppendLine(
                $"**Data isolation**: You operate exclusively within tenant " +
                $"`{context.TenantId}`. Never reference or retrieve data from other tenants. " +
                $"If a request would require cross-tenant access, decline and explain why.");
        }
    }

    private static void AppendEntityEnrichment(StringBuilder sb, OrchestratorPromptContext context)
    {
        if (string.IsNullOrWhiteSpace(context.MatterName))
            return;

        var enrichment =
            $"**Active matter**: You are currently assisting with the matter " +
            $"'{context.MatterName}'. Prioritise documents and records related to this matter " +
            $"when searching or retrieving content.";

        sb.AppendLine();
        sb.AppendLine(enrichment);
    }

    // ── Per-turn suffix ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the ordered list of tool names to inject this turn.
    ///
    /// When the routing result is confident, the tool names are derived from the
    /// selected capabilities' <see cref="CapabilityManifestEntry.ToolNames"/>.
    /// In broad mode (no confident selection), all registered tools are used up to
    /// <see cref="MaxToolsPerTurn"/>.
    /// The list is de-duplicated and capped at <see cref="MaxToolsPerTurn"/>.
    /// </summary>
    private IReadOnlyList<string> ResolveToolNames(CapabilityRoutingResult routing)
    {
        var allCapabilities = _manifest.GetAll();

        IEnumerable<string> rawToolNames;

        if (routing.IsConfident && routing.SelectedCapabilities.Length > 0)
        {
            // Confident routing: expand capability names → tool names.
            rawToolNames = routing.SelectedCapabilities
                .SelectMany(capName =>
                {
                    if (_manifest.TryGet(capName, out var entry) && entry is not null)
                        return entry.ToolNames;

                    _logger.LogDebug(
                        "OrchestratorPromptBuilder: selected capability '{CapabilityName}' " +
                        "not found in manifest; skipping.",
                        capName);
                    return Enumerable.Empty<string>();
                });
        }
        else
        {
            // Broad / fallback mode: include all registered tool names.
            rawToolNames = allCapabilities.SelectMany(c => c.ToolNames);
        }

        return rawToolNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxToolsPerTurn)
            .ToList();
    }

    /// <summary>
    /// Serialises tool schemas into the per-turn suffix JSON block.
    ///
    /// Each tool schema is represented as a minimal JSON object:
    /// <code>
    /// { "name": "SearchDocuments", "description": "...", "parameters": {} }
    /// </code>
    ///
    /// The actual function schema definitions (OpenAPI-style) are registered
    /// separately with the chat client via <see cref="OrchestratorPrompt.ToolSchemaNames"/>.
    /// This section exists so the LLM sees the tool list in its system prompt context
    /// (improves reliability for models without function-calling metadata in the context).
    ///
    /// Token budget: capped at <see cref="MaxToolSchemasTokens"/>.
    /// </summary>
    private (string Suffix, IReadOnlyList<string> ActiveTools) BuildPerTurnSuffix(
        IReadOnlyList<string> toolNames)
    {
        if (toolNames.Count == 0)
            return (string.Empty, Array.Empty<string>());

        var sb = new StringBuilder(1024);
        sb.AppendLine("## Active Tools This Turn");
        sb.AppendLine("The following tools are available for function-calling in this turn:");
        sb.AppendLine();
        sb.AppendLine("```json");

        var activeTools = new List<string>(toolNames.Count);
        var tokenCount = EstimateTokens(sb.ToString());

        foreach (var toolName in toolNames)
        {
            // Minimal schema stub — actual schema is wired by the chat client.
            var schema = JsonSerializer.Serialize(
                new { name = toolName, description = $"Invoke the {toolName} tool.", parameters = new { } },
                new JsonSerializerOptions { WriteIndented = true });

            var lineTokens = EstimateTokens(schema + "\n");

            if (tokenCount + lineTokens > MaxToolSchemasTokens)
            {
                _logger.LogDebug(
                    "OrchestratorPromptBuilder: tool schema section capped at {Count} tools " +
                    "({Tokens} token limit).",
                    activeTools.Count, MaxToolSchemasTokens);
                break;
            }

            sb.AppendLine(schema);
            tokenCount += lineTokens;
            activeTools.Add(toolName);
        }

        sb.AppendLine("```");

        var totalSuffixTokens = EstimateTokens(sb.ToString());
        _logger.LogDebug(
            "OrchestratorPromptBuilder: suffix total — {ToolCount} tool schemas, {SuffixTokens} tokens",
            activeTools.Count, totalSuffixTokens);

        return (sb.ToString(), activeTools);
    }

    // ── Token estimation ──────────────────────────────────────────────────────

    /// <summary>
    /// Estimates token count using the chars/4 heuristic.
    /// Good enough for budget enforcement; not a precise tokeniser count.
    /// </summary>
    internal static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(text.Length / 4.0);
    }
}
