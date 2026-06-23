---
name: stateful-chat-memory-2026-06
description: Research on stateful chat memory architecture for Spaarke's legal-AI chat agent — per-turn injection vs JIT recall, file/workspace state, token budget, competitive patterns (Cursor, Harvey, Claude Artifacts, Linear, Glean, Hebbia, Copilot)
metadata:
  type: project
---

# Stateful chat memory research — 2026-06-19

**Question**: How should Spaarke's legal-AI chat carry full file + workspace context across turns without blowing the 8K system-prompt budget?

**Headline findings**:
1. The 2026 industry consensus is *just-in-time (JIT) retrieval over upfront stuffing*. Anthropic formalized this in Sep 2025 ("set of strategies for curating … optimal set of tokens") and Harvey, Copilot, Glean all converged on it.
2. Cursor's empirical evidence: stuffing files dilutes attention; selective injection wins. "Lost in the middle" U-shaped attention curve confirms this (Stanford 2024 TACL; GPT-4o 128K has ~8K *effective* tokens).
3. Harvey's drafting agent is the closest reference design: in-memory document representation that the agent reads/edits via tools (`get_diff`, scoped read/edit). The doc state lives in the SERVER's memory, not the prompt.
4. Prompt caching changes the math: Azure OpenAI caches the longest stable prefix ≥1024 tokens at 50% off + 80% faster TTFT. **Order the prompt: static prefix → dynamic suffix.**
5. The right pattern for Spaarke: tiered injection (metadata always; full text on demand via tool), with the system prompt structured for prefix caching.

**Recommended budget split (8K total)**: persona 800 / tool defs 1500 / static workspace summary 800 / static file summaries 1200 / chat history 2500 / scratch 1200.

**Recommended new tool**: `recall_session_file(file_id, mode: "summary" | "section" | "full", section_query?: string)`. Reuses `spaarke-session-files` AI Search index for sub-document recall.

**Recommended workspace pattern**: lightweight summary (tab list + types + state digest) always; full state retrieved via existing Pillar 6b tools — but those need Layer 1/2 router fix.

**Sources consulted**:
- Anthropic Effective Context Engineering for AI Agents (Sep 2025)
- Anthropic Prompting Long Context (place docs at top, query at bottom — up to 30% accuracy lift)
- Harvey AI: Building an Agent for Complex Document Drafting (in-memory doc model + get_diff tool)
- Harvey Memory blog
- Cursor: Morph context window analysis 2026
- GitHub blog: June 17 2026 Copilot improvements (tool_search defers schema load → 18% token savings)
- Azure OpenAI prompt caching docs (auto-cache ≥1024 tokens; 50% discount; 80% faster TTFT)
- Stanford "Lost in the Middle" TACL 2024
- OpenAI Assistants file_search (20 chunks max, 800-token chunks, 400 overlap)
- Linear Agent Session pattern
- Foundry memory patterns (this knowledge folder, 2026-05)

**Open questions** (carried into findings):
- What's the right LLM model for the upload-time summary call (gpt-4o-mini vs full)?
- Does Spaarke need per-user "always show" workspace state (Pillar 9), or session-default?
- How to handle the "no manifest table" Pillar 6b breakage — orthogonal to this design but blocking write-back tools.
