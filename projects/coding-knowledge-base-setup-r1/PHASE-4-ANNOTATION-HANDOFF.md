# Phase 4 Handoff — Senior-Engineer NOTES.md Annotation Pass

> **Audience**: Ralph Schroeder (or designated senior engineer)
> **Estimated effort**: 1–2 days of focused work, spreadable over a week
> **Goal**: Convert 11 stub `knowledge/<topic>/NOTES.md` files from scaffolds to substantive Spaarke-specific commentary

---

## Why this is the highest-leverage step

The 6 knowledge-base skills are wired and pointing at the right files. The samples are curated with provenance. **What separates "agent reads our knowledge base" from "agent produces Spaarke-quality code" is the substance in `NOTES.md`.**

A stub `NOTES.md` with honest TODO placeholders is still useful — it scaffolds the headings and tells the agent which Spaarke patterns matter. But the *judgment* lives in your head: where Spaarke diverges from canonical samples, what gotchas you hit in production, which patterns are battle-tested vs experimental, what the cross-references to existing Spaarke code are.

Phase 4 is where that judgment gets externalized.

---

## How to annotate (per topic)

Each `knowledge/<topic>/NOTES.md` currently has:
- A required first-line banner: `> ⚠️ STUB — senior engineer review pending`
- Headings drawn from the directive's "NOTES.md guidance" for that topic
- `_TODO: <hint>_` placeholders under each heading

**For each heading**:

1. Replace the TODO placeholder with substantive content. Lead with **what this pattern teaches in practice** — not what it is (the samples show that), but what an engineer actually needs to know.
2. Add **where Spaarke's existing code follows or modifies the pattern** — cite specific files (e.g., `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs`). The agent will Read those references.
3. Add **specific gotchas, preview limitations, or platform constraints** you've hit. The samples don't show production constraints.
4. Add **cross-references to ADRs and Spaarke decisions** that intersect — the agent already has ADR loading via `adr-aware`, but explicit links here help.
5. When done, **remove the `⚠️ STUB` banner** from line 1. That's the signal Phase 4 is complete for that topic.

**Honest stub policy stays in force**: if you don't have substantive content for a section, leave it as a TODO marked clearly. A NOTES.md mixing real content with honest TODOs is fine. A NOTES.md pretending to have insight it doesn't is worse than the stub.

---

## Recommended annotation order

Front-load the topics that affect the most ongoing work. Suggested sequence:

| Order | Topic | Why early |
|---|---|---|
| 1 | `sharepoint-embedded` | Touched by nearly every Spaarke document-handling feature; ADR-007 facade pattern is daily-relevant |
| 2 | `mcp-apps` | Powers Spaarke's planned widget surface (Redline, TabularReview, Compare); skill→widget mapping is opinionated and you own that opinion |
| 3 | `declarative-agents` | The user-facing AI surface; your DA composition (SPE + Dataverse MCP + Spaarke MCP + Work IQ + Foundry IQ) is unique and not derivable from canonical samples |
| 4 | `foundry-agent-service` | Captures the multi-day legal workflow framing; runtime-choice rationale is essential |
| 5 | `dataverse-mcp` | The three-flavor migration heuristic (tables → keep / procedural → Business Skills / verbs → App MCP / docs → Spaarke MCP) is a major design decision — only you can write it authoritatively |
| 6 | `m365-copilot` | Foundation layer; usually annotated in pieces as the other topics reference it |
| 7 | `agent-framework` | Lower priority — in-process .NET pattern is well-defined by Microsoft samples |
| 8 | `foundry-iq` | Lower priority until the team actually wires a KB; preview surface |
| 9 | `azure-ai-search` | Already has substantial coverage via `docs/architecture/RAG-ARCHITECTURE.md` + `RAG-CONFIGURATION.md` — annotation here mostly cross-references |
| 10 | `github-mcp` | Tool-discipline notes (when to use / when NOT to use) are the main value here; the rest is Microsoft's catalog |
| 11 | `work-iq` | Preview surface; annotation can wait until naming/positioning stabilizes (Agent 365 vs Work IQ confusion in the wild) |

---

## Quality bar (per topic)

When a topic's `NOTES.md` is "done" (banner removed):

- [ ] Every section heading has substantive content OR is honestly marked TODO
- [ ] At least 2 cross-references to specific Spaarke files (e.g., `src/...`)
- [ ] At least 1 cross-reference to a Spaarke ADR
- [ ] At least 1 production gotcha or constraint not visible from canonical samples
- [ ] No fabricated content (if you don't know, mark TODO)
- [ ] Banner line 1 removed

A "good" `NOTES.md` is ~3–8 KB of dense, opinionated text. Don't pad — agents read better than humans do, but density still matters.

---

## What does the agent do with annotated NOTES.md?

The 6 skills are wired to load `NOTES.md` *first* (before samples or docs) when the agent starts work on a relevant task. The agent reads top-to-bottom, builds context, then generates code. Substantive `NOTES.md` is how you steer hundreds of future agent tasks without being in the loop.

Example: today, if a developer asks the agent to "implement an MCP tool that summarizes a document," the agent reads stub `mcp-apps/NOTES.md` → sees the heading scaffold → falls back to general Microsoft MCP knowledge for the body. After annotation, the agent reads your battle-tested `IAiToolHandler` guidance, the specific approval-mode policy for destructive ops, and the cross-reference to existing tool handlers in `Sprk.Bff.Api/Services/Ai/`. The output reflects Spaarke, not generic MCP advice.

---

## When you finish

1. Update each annotated file: remove `⚠️ STUB` banner from line 1.
2. Append to `knowledge/REFRESH-LOG.md`:
   ```markdown
   ## YYYY-MM-DD — Phase 4 annotation pass (initial)
   Topics annotated: <list>
   Topics deferred: <list with reasons>
   ```
3. Update `projects/coding-knowledge-base-setup-r1/TASK-INDEX.md` Phase 4 row: 🔲 → ✅.
4. Commit per topic (one commit per annotated topic is cleanest) using message format: `docs(knowledge): annotate <topic>/NOTES.md (Phase 4)`.

---

## If the work outgrows 1–2 days

It's OK to phase-4-by-phase-4 — annotate the top 3–4 topics first, ship that, then annotate more as you encounter them in normal work. The skills will still load partial annotation correctly; topics that remain stubs continue to work as scaffolds.

The monthly refresh ritual (`knowledge/REFRESH-PROCEDURE.md`) is an ongoing opportunity to deepen annotations as the platform evolves and Spaarke's patterns mature.
