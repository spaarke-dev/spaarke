# Discussion notes — work-type model + Legal Research as a distinct surface

> **Date**: 2026-07-28 · **Source**: owner ↔ agent discussion during `ai-advanced-capabilities-nda-r1` UAT.
> **Purpose**: raw input to fold into this project's `design.md` when we formalize. NOT a design decision yet.
> **Companion**: `../COMPETITIVE-LANDSCAPE.md` (Harvey/Legora/CoCounsel/Robin/Protégé — market validation).

---

## 1. Where "Legal Research" sits in the model

The program is organizing around **work types** (product surfaces the user picks by intent), with
**knowledge** as a sub-axis and **UI affordance** as a third axis (see the analysis-hub project's
`design-discussion.md` §1 and nda-r1's `notes/contextual-ai-tool-library-design.md` §10):

- **Agreement Analysis** = the Compose-based advisory surface (NDA is instance #1). Built.
- **Legal Research** = *this project's* work type — a **genuinely DIFFERENT surface**, not the Compose editor
  with new tools. Owner's framing: *"'What is the current state of the Chevron doctrine?' is Legal Research —
  different UI/UX needs and different tools, in addition to different knowledge. Users look for 'Agreement
  Analysis' when they want to review an agreement; 'Legal Research' when they want to research."*

**Implication**: Legal Research needs its own widget + tool palette; it plugs into the shared **`workTypes`**
tool-library seam (`getToolsForSurface(surface, 'legal-research')`) and the **`sprk_analysis`** durable spine
(built in the analysis-hub project) — but its SURFACE is new.

## 2. Competitive input (from COMPETITIVE-LANDSCAPE.md — verify before spec)

- The market **validates** work-type surfaces + knowledge-as-sub-axis; every leader ships distinct surfaces
  (chat/assistant · tabular review grid · authorities research · agent/workflow builder).
- **Legal research UX = query → cited authorities → memo.** Recommended shape (from the report): a **3-pane
  layout** — query+scope · cited memo · inline authorities — **citation-first**, with a grounded /
  "could-not-verify" state, an async agentic "deep research" job, and a **memo → Compose hand-off** via the
  existing `surfaceLaunchRegistry`.
- **⚠️ Do NOT promise KeyCite/Shepard's "good law" treatment** — Spaarke has no authorities corpus. Scope
  research grounding to what we can actually cite (CourtListener API is a program candidate; the roadmap lists
  a "native CourtListener API client" as genuinely ABSENT).
- Confidence: HIGH on surface naming/structure, MEDIUM on fine UX mechanics, LOW on sub-90-day competitor
  claims — re-verify current product state before spec.

## 3. Program roadmap alignment (from research-r1 design.md background)

Net-new candidates the roadmap already flags that intersect research:
- **`sprk_analysis` durable results table** — built in the analysis-hub project; Legal Research analyses are
  `sprk_analysis` records too (work-type = `legal-research`).
- **Native CourtListener API client** — the authorities corpus for citation-first research.
- **Tabular doc×question grid** — a review-grid surface (more Agreement-Analysis-portfolio than research, but
  the grid primitive may be shared).
- EvaluatorGate / phase deny-tools — relevant if research runs as an agentic multi-step job.

## 4. Open questions to carry into design

1. Is `research-r1` the **horizontal capability** (research grounding + citation engine) or a **concrete
   Legal Research surface**, or both? (The design.md driver field is still TBD.)
2. Authorities corpus: CourtListener API vs a licensed provider vs internal precedent (`sprk_precedent`)?
3. How does a research memo hand off to Compose (drafting) — the `surfaceLaunchRegistry` path?
4. Async "deep research" job: reuse which orchestration (agent loop / durable scheduler — the roadmap lists a
   cron/durable scheduler as ABSENT)?
5. Shared vs distinct tool palette: which research tools (find-authority, summarize-holding, build-citation-
   table, jurisdiction-filter) are `legal-research`-scoped vs shared `['*']` primitives?
