---
name: legal-ai-competitive-landscape-2026-07
description: 2026 product-surface/work-type/tool structure of Harvey, Legora, CoCounsel, Spellbook, Robin AI, LexisNexis Protégé (+ Ironclad/Luminance/Definely) — validates Spaarke's work-type-specialized-surface model
metadata:
  type: reference
---

## 2026-07-28: Legal-AI competitive landscape (surfaces / work types / tools)

**Question**: How do leading legal-AI products organize product surfaces / work types / tools, to validate Spaarke's "work-type-specialized surfaces on a shared editor core, knowledge as a sub-axis, shared tool primitives scoped by work-type" model?

**Findings**: The market STRONGLY validates all three axes. Universal quartet of top-level surfaces: (a) chat/assistant, (b) bulk multi-doc review GRID (rows=docs, cols=questions, cells=cited answers), (c) authorities research (query→cited memo, distinct layout), (d) agent/workflow builder. Knowledge/grounding is consistently a configurable SUB-AXIS (playbooks / vaults-projects / jurisdiction+source selectors / content-provider corpora), never the organizing axis — nobody makes "NDA vs MSA" a surface. Redline/clause editing lives in a Word-add-in or in-editor comment surface, separate from the grid. 2026 agentic wave is converging on COMPOSABLE PRIMITIVES (Legora Workflows chain review+draft+research+translate; CoCounsel hides skills behind agentic planner; Harvey Agent Builder + 500 agents).

Per-product: **Harvey** = Assistant / Vault (grid) / Knowledge (research, inline case view, Regional Knowledge Sources) / Workflow Agents. **Legora** = Assistant / Tabular Review (flagship grid) / Research (federated DMS+DB+web) / Agentic Workflows; Playbooks = knowledge axis. **CoCounsel (TR)** = agentic planner over Draft/Review/Calc/Research skills, Claude Agent SDK; Deep Research → cited memo 3-8min 80-85% usable; KeyCite treatment inline = "good law" signal. **Spellbook** = Word add-in sidebar (playbook redlines) + Associate agent for multi-doc; NOT a research tool. **Robin AI** = Query/Reports/Review/Draft families (cleanest work-type-as-tabs); Query = search over YOUR contracts not case law. **Protégé (Lexis)** = Feb-2026 rename of Lexis+AI; single assistant over Draft/Research/Analyze; Shepard's + citation-existence verification (hallucination guard); Protégé Work launched May-2026.

**Spaarke fit**: Agreement Analysis ≈ review surface, Legal Research ≈ research surface = exactly the market's primary cleavage. Deliberate divergences: (1) Spaarke uses ONE Compose editor + comments-first user-driven redlines (like Spellbook/Definely in-editor) vs market's tabular grid for VOLUME — fine for single-NDA advisory north-star, but a portfolio/data-room use case will need a SECOND tabular Agreement surface. (2) Spaarke has no Westlaw/Lexis authorities corpus — a Legal Research surface grounded only in RAG references is a firm-knowledge research tool (like Harvey Knowledge over memoranda), NOT a good-law/Shepard's competitor; must NOT promise citation treatment.

Legal Research widget rec: 3-pane (query+scope | cited memo | inline authorities panel), scope selectors over RAG indexes, citation-first with grounded/could-not-verify state, async agentic deep-research job (reuse notification spine), memo→Compose hand-off via surfaceLaunchRegistry (shared-primitive bridge), distinct tool palette (no Compose clause tools).

**Sources**: Report at `projects/ai-advanced-capabilities-research-r1/COMPETITIVE-LANDSCAPE.md`. Primary: harvey.ai/platform + help.harvey.ai + The Brief; legora.com; legal.thomsonreuters.com/products/cocounsel-legal + TR Institute "Reimagined"; spellbook.com; lexisnexis.com/products/lexis-plus-protege + LawNext 2026-02 + Artificial Lawyer 2026-05-07. Corroborated by GC AI / Lawyerist / Vaquill / layer3labs reviews 2026. HIGH confidence on surface naming; MEDIUM on fine UX mechanics (analyst/marketing-sourced, no hands-on); LOW on <90-day claims (agent counts, timings).

**Open questions**: Will Spaarke Agreement Analysis need portfolio/data-room volume (→ tabular grid surface)? What is the definitive research grounding corpus? Real-screen fidelity pass on Harvey Knowledge + CoCounsel Deep Research not yet done.
