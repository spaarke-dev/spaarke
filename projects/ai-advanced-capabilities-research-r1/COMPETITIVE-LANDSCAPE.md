# Competitive Landscape: How Legal-AI Products Organize Surfaces, Work Types, and Tools

**Date:** 2026-07-28
**Author:** Spaarke researcher subagent
**Purpose:** Validate Spaarke's "work-type-specialized surfaces on a shared editor core, knowledge as a sub-axis, tools scoped per work-type" model against the current (2026) structure of leading legal-AI products, and extract UX patterns for a future "Legal Research" surface distinct from the Compose-based "Agreement Analysis" surface.

---

## Executive summary

The 2026 market strongly validates Spaarke's core thesis: every serious legal-AI platform now ships **distinct top-level product surfaces per work type** (a chat/assistant, a bulk multi-doc review grid, a research surface, and an agent/workflow builder), and treats **knowledge/grounding as a configurable sub-axis** (vaults/projects, playbooks, jurisdiction/source selectors) rather than as the primary organizing axis. The single most universal pattern is the split between (a) a **tabular multi-document review grid** (rows = documents, columns = questions/clauses, cells = cited answers) for contract/diligence review, and (b) a **query → cited-authorities → memo** research surface that looks and behaves nothing like the review grid. Redline/clause editing overwhelmingly lives in a **Word-add-in or in-editor comment/redline surface** separate from the grid. Spaarke's plan to build "Agreement Analysis" and "Legal Research" as separate surfaces sharing primitives is directly in line with how Harvey (Assistant/Vault/Knowledge/Workflows), Legora (Assistant/Tabular Review/Research/Workflows), CoCounsel, and LexisNexis Protégé are actually structured. Spaarke's main divergence is deliberate and defensible: a **single Compose editor as the review+revise surface** and **comments-first, user-driven redlines** rather than a batch review grid — a fit for the NDA advisory north-star, but the market signal is that a tabular multi-doc grid becomes necessary the moment the use case moves to *portfolio/data-room volume*.

## Sources + confidence

- **Confidence: HIGH** on the existence and naming of top-level surfaces for Harvey, Legora, CoCounsel, LexisNexis Protégé, Robin AI, Spellbook — corroborated by official product pages, vendor blogs/press, and multiple independent 2026 analyst/review sites.
- **Confidence: MEDIUM** on fine-grained UX mechanics (exact grid interactions, citation-drill behavior, memo layout) — drawn from analyst reviews and vendor marketing rather than hands-on use; vendor screenshots were not directly inspected. Treat specific interaction claims as directional.
- **Confidence: LOW / volatile** on anything dated within the last ~90 days (agent counts, "Deep Research" timings, renames). These products ship monthly; re-verify before citing in a spec.
- All claims are dated inline. Where a claim is marketing-sourced, it is flagged. This report did **not** rely on Microsoft Learn (not relevant to competitor product structure).

---

## Per-product findings

### 1. Harvey

**1. Top-level organizing concept.** As of mid-2026, Harvey organizes around **four named product surfaces** (per Harvey.ai platform pages + GC AI review, 2026): **Assistant** (chat, drafting, single/multi-doc analysis), **Vault** (bulk cross-document review at data-room volume), **Knowledge** (legal/regulatory research with citations), and **Workflow Agents** (multi-step no-code automation + a "Deep Research" agentic mode). An **Ecosystem** layer connects these to Word, Outlook, SharePoint, Box, and mobile. The user chooses *what to do* by picking a product surface, not by picking a "skill" from a menu — though within Workflows, Harvey now ships 500+ pre-built, practice-area-specific agents and an **Agent Builder** self-service tool (Law.com, 2026-05-05). So: top-level = **surfaces**; second-level = **agents/workflows**.

**2. Agreement/contract review UX.** Two distinct modes. For *single/few* documents, review happens in **Assistant** as a **chat + document split** (ask questions, get answers anchored to the doc). For *many* documents, **Vault** is the **tabular multi-doc review grid**: run one query set across a large document set and get consolidated/tabular cross-document answers, built for M&A due diligence and large regulatory sets. Clause-level tooling (markup analysis, counterparty-markup review, checklist comparison) is increasingly delivered via the pre-built agents rather than a fixed clause palette.

**3. Legal research UX.** **Knowledge** is a separate surface: complex cross-domain queries return **synthesized answers with citations** to authoritative primary sources. Users select a **Regional Knowledge Source** to constrain search + citations to a defined jurisdiction's legal/regulatory sites (Harvey help docs, 2026). Sources include US Case Law (9M+ opinions via CourtListener), EDGAR/SEC, EUR-Lex, French case law, tax guidance, and firm memoranda libraries. Notable UX detail: **case content is viewable inline alongside results** ("no separate window or tool required") so lawyers drill into the primary material without leaving the surface. This is a fundamentally different layout from Vault's grid — it is answer-plus-authorities, not rows-and-columns.

**4. Knowledge/grounding scoping.** Grounding is scoped by (a) **Vault projects** (the document set under review), (b) **Regional Knowledge Sources** (jurisdiction/source selectors in Knowledge), and (c) firm knowledge libraries (memoranda). Grounding is an *input selector to a surface*, orthogonal to which tools run.

**5. Tool/action palettes.** Tools differ per surface (Vault = review queries; Knowledge = research; Workflows = agents). The Agent Builder + 500 pre-built agents is effectively a **shared, contextually-surfaced tool/agent library** layered over the surfaces — the closest analogue to a "shared tool primitives" concept in the market.

*Sources:* harvey.ai/platform, harvey.ai/blog (The Brief, Apr/May 2026), help.harvey.ai (knowledge-sources-overview, release notes), Law.com 2026-05-05, GC AI review 2026. Confidence HIGH on surfaces, MEDIUM on grid/inline-citation mechanics.

---

### 2. Legora (formerly Leya)

**1. Top-level organizing concept.** Legora presents (per legora.com + reviews, 2026): a legal-specific **Assistant** (document analysis + drafting, incl. in-Word drafting), **Tabular Review** (flagship grid), **Research** (agentic legal research across DMS + legal databases + web from one interface), and **Agentic Workflows** (multi-step tasks chaining drafting/review/research/translation/DB queries into one consolidated output). Legora is explicitly branding 2026 as "the year of agents" (Legora blog, 2026). Choice of work = pick the surface / launch a workflow.

**2. Agreement/contract review UX.** **Tabular Review** is the canonical **rows = documents, columns = questions, cell = cited answer** grid, each cell linked to its source. Built for firm-level volume: data rooms, financing packages, lease portfolios, regulatory sets. **Playbooks** (codified clause checklists, negotiation guidelines, redlining preferences) are applied automatically or manually during drafting/review — so clause-level standards live in playbooks layered onto both the grid and the Word drafting surface.

**3. Legal research UX.** **Research** is agentic and *source-federated*: one interface searches across the firm's DMS, external legal databases, and the web, returning consolidated answers. Distinct from Tabular Review's grid; oriented to synthesis + citation across heterogeneous sources rather than doc-by-doc extraction.

**4. Knowledge/grounding scoping.** Scoped by connected sources (DMS, legal databases, web), the document set in a given Tabular Review, and **playbooks** (firm standards). Playbooks are the knowledge-as-sub-axis mechanism; the surfaces/tools are separate.

**5. Tool/action palettes.** Agentic Workflows are the shared-primitive story — a workflow can *compose* tabular review + drafting + research + translation + DB query as steps, customizable to firm templates/logic. So Legora treats review/research/draft as **composable primitives** that a workflow orchestrates, which is the strongest market precedent for "shared tool primitives scoped by work-type."

*Sources:* legora.com (product, /bar benchmark, 2026 blog), GC AI review 2026, Legaltech Hub vendor page, layer3labs Harvey-vs-Legora 2026. Confidence HIGH on Tabular Review + Workflows; MEDIUM on Research UX specifics.

---

### 3. CoCounsel (Thomson Reuters)

**1. Top-level organizing concept.** CoCounsel Legal was **re-architected in 2026 into a fully agentic model** ("CoCounsel Legal Reimagined," TR Institute, 2026), running on **Anthropic's Claude Agent SDK**. Instead of a menu of discrete "skills," the user **describes the need in plain language** and CoCounsel plans which skills/workflows to engage and executes them ("works like a colleague that plans and executes instead of a menu of tasks"). Under the hood it still has **skills** grouped as **Drafting, Review, Calculations, Research** (and Deep Research). So the *organizing concept shifted from an explicit skill menu (2024-25) to an agentic front door (2026)* — but the work types persist as skill families.

**2. Agreement/contract review UX.** Review is a **skill family** (consistency checks, compliance validation, conflict identification) invoked agentically; document analysis handles docs up to ~300 pages. CoCounsel is less known for a firm-scale *tabular* review grid than Legora/Harvey Vault; its strength is the agentic assistant + Westlaw-grounded research. (Confidence MEDIUM — TR markets outcomes, not grid mechanics.)

**3. Legal research UX.** This is CoCounsel's crown jewel via Westlaw grounding. **Deep Research** produces **structured memos with proper citations**, 3-8 min/query, ~80-85% usable as-is (analyst reviews, 2026). **KeyCite integration flags overruled/questioned authority directly in the workflow** — i.e., "is this still good law" is surfaced inline as a citation-treatment signal, not a separate check. This is the reference implementation for the research surface: query → agentic plan → cited memo → inline citation-validation.

**4. Knowledge/grounding scoping.** Grounded in Westlaw/TR authoritative content by default; org documents when enabled. Scoping is largely *content-provider-anchored* (TR's trusted corpus) plus practice-area/jurisdiction context — less of a user-facing "vault selector" than Harvey/Legora.

**5. Tool/action palettes.** Skills (Draft/Review/Calc/Research) are the tool families; the 2026 move is to **hide the palette behind an agentic planner** rather than expose a fixed toolbar. Expert-created prompts + end-to-end agentic workflows are the packaged "tools."

*Sources:* legal.thomsonreuters.com/products/cocounsel-legal + blog (2026), TR Institute "Reimagined"/"Rebuilding for the Agent Era" 2026, Lawyerist + Vaquill reviews 2026, ZiefBrief Westlaw/Lexis update 2026-02. Confidence HIGH on agentic reframe + research/KeyCite; MEDIUM on review-grid absence.

---

### 4. Spellbook

**1. Top-level organizing concept.** Spellbook's front door is a **Microsoft Word add-in / sidebar** — the *editor is the surface*. The organizing concept is not "pick a surface" but "the assistant lives in the document you're drafting." A second surface, **Spellbook Associate** (AI agent), handles **multi-document transactional work** (data-room materials, financing docs, disclosure schedules, employment packages). So: **in-editor assistant** (primary) + **agent for multi-doc** (secondary).

**2. Agreement/contract review UX.** Classic **Word-add-in in-editor** review: the sidebar reads the open contract in real time, **flags missing clauses, suggests playbook-based language, identifies risks, offers alternative provisions**. Clause-level tools are inline suggestions and redlines against a **playbook**. This is the archetype of the "in-editor redline/comment surface" — closest in spirit to Spaarke's Compose model, though Spellbook batch-suggests where Spaarke's NDA north-star is comments-first + user-driven.

**3. Legal research UX.** Spellbook is not primarily a legal-research (case-law) tool; it is drafting/review/negotiation-centric. It can answer questions about an agreement and compare terms to market standards, but there is no Westlaw/Lexis-class authorities-and-citations research surface. (Confidence HIGH that research is not a first-class surface.)

**4. Knowledge/grounding scoping.** **Playbooks** are the knowledge axis (firm standards, market-standard comparisons). Grounding is the open document + the firm playbook; no jurisdiction/authority selector.

**5. Tool/action palettes.** Tools are inline drafting/review actions (draft clause, review to playbook, suggest alternative, ask about agreement). Spellbook Associate adds cross-document coordination. Minimal per-work-type palette differentiation — the palette is drafting-centric.

*Sources:* spellbook.com (contract-playbook), Lawyerist + GC AI + AI Vortex reviews 2026. Confidence HIGH.

---

### 5. Robin AI

**1. Top-level organizing concept.** Robin AI organizes its product into **four named feature families: Query, Reports, Review, Draft** (analyst syntheses, 2026) — "from first look to post-signature tracking." This is the most explicit **work-type-as-top-level-tab** structure in the set. The user chooses a family.

**2. Agreement/contract review UX.** **Review** = AI-assisted contract review with **redlining and risk flagging**, redlines suggested against the **company playbook**; clause-level analysis, suggested redlines, and obligation tracking. **Reports** handles contract analysis and structured **data extraction** (the tabular/extraction cousin). So Robin splits *interactive redline review* (Review) from *bulk extraction/reporting* (Reports) — two different surfaces for two different review intents.

**3. Legal research UX.** **Query** is an AI-powered **deep search across your contracts** — i.e., research *over your own contract corpus*, not case-law research. Robin is contract-lifecycle-centric, not an authorities-research platform. (Confidence HIGH.)

**4. Knowledge/grounding scoping.** Grounding = your contract portfolio + **company playbook** (for redline standards) + obligation data. Knowledge is corpus + playbook, scoped per feature family.

**5. Tool/action palettes.** Tools are clearly partitioned by family: Query (search), Reports (extract), Review (redline/flag), Draft (generate from template). This is a clean example of **tools differing per work type** with a shared underlying contract corpus.

*Sources:* layer3labs Robin guide 2026, G2 + techsuggest reviews 2026, agenticcontractreview.com 2026. Confidence MEDIUM-HIGH (analyst-sourced; Robin's own docs not directly read).

---

### 6. LexisNexis Protégé

**1. Top-level organizing concept.** In **February 2026**, Lexis+ AI was renamed **Lexis+ with Protégé** and repositioned as an **end-to-end workflow platform** (LawSites/LawNext, 2026-02; LexisNexis press). In **May 2026**, Lexis launched **Protégé Work**, expanding the offering (Artificial Lawyer, 2026-05-07). The organizing concept is a **single Protégé AI assistant** that spans **drafting, research, and analysis workflows**, grounded in Lexis content. Work types are framed as **workflows** the assistant executes rather than separate app tiles.

**2. Agreement/contract review UX.** Drafting/analysis via the assistant, handling up to ~1M characters (complex contracts/filings). Review is assistant-driven + document-grounded; personalization by practice area, jurisdiction, and writing style. Less a tabular grid, more an assistant-over-document model. (Confidence MEDIUM.)

**3. Legal research UX.** The core strength: grounded in Lexis authoritative content with **legal citations on every AI response**, **Shepard's citation tools** integrated, plus Practical Guidance. **Citation validation**: identifies citations in AI-generated *and* attorney-drafted content, checks them against Lexis authoritative sources, and **flags citations that cannot be verified as existing** (hallucination guard). Shepard's provides the "is this still good law" treatment signal — the Lexis analogue to CoCounsel's KeyCite. Research is a distinctly authorities-centric surface.

**4. Knowledge/grounding scoping.** Grounded by default in Lexis content + Shepard's + Practical Guidance; org documents when enabled (integrates with **iManage and SharePoint**). Scoping = content provider corpus + practice area/jurisdiction + optional firm DMS.

**5. Tool/action palettes.** Draft/research/analyze workflows under one assistant; Shepard's + citation-verification are research-surface tools; personalization settings act as a cross-cutting knowledge layer.

*Sources:* lexisnexis.com/products/lexis-plus-protege + /protege + AI solutions pages, LexisNexis pressroom 2026, LawNext 2026-02, Artificial Lawyer 2026-05-07, legal.io 2026. Confidence HIGH on research/Shepard's; MEDIUM on review UX.

---

### 7. Notable others (brief)

- **Ironclad AI (Rivet/AI Assist):** CLM-native. Contract review + redlining live **inside the CLM workflow and inside Word**, scoped by the company's **playbook and clause library**. Work-type surfaces map to CLM stages (intake → review → negotiate → track). Knowledge = the clause library + playbook. (Confidence MEDIUM, from general 2025-26 positioning.)
- **Luminance:** Positions around an **agentic "Ask Lumi" assistant** plus **automated review/negotiation** and a **panoramic tabular/portfolio analysis** view over document sets; strong on due-diligence-scale document understanding. Confirms the grid-for-volume pattern. (Confidence MEDIUM.)
- **Definely:** In-editor Word tooling focused on **drafting quality** (Define/Vault/Draft) — navigate defined terms, cross-references, and inconsistencies inside the document. Reinforces the "in-editor clause/term surface" as a distinct primitive from review grids and research. (Confidence MEDIUM.)

---

## Synthesis

### Does the market validate Spaarke's model?

**Yes, on all three axes — with one nuance.**

1. **Work-type-specialized surfaces (VALIDATED, strongly).** Every platform ships distinct top-level surfaces per work type. The near-universal quartet is: **(a) chat/assistant**, **(b) bulk multi-doc review grid**, **(c) authorities research**, **(d) agent/workflow builder**. Robin AI (Query/Reports/Review/Draft) and Harvey (Assistant/Vault/Knowledge/Workflows) are the cleanest confirmations. Spaarke's "Agreement Analysis" ≈ the review surface and "Legal Research" ≈ the research surface is exactly the market's primary cleavage.

2. **Knowledge as a sub-axis (VALIDATED).** Grounding is consistently a *configurable input to a surface*, not the organizing axis: **playbooks** (Legora, Spellbook, Robin, Ironclad), **vaults/projects** (Harvey Vault), **jurisdiction/source selectors** (Harvey Regional Knowledge Sources), **content-provider corpora + practice-area/jurisdiction** (CoCounsel/Westlaw, Protégé/Lexis + Shepard's). Nobody makes "NDA vs MSA vs employment" a *surface* — they make it a **playbook/knowledge selection inside the review surface**, which is precisely Spaarke's "same UI+tools, knowledge varies" thesis for Agreement Analysis.

3. **Shared tool primitives scoped by work-type (VALIDATED, emerging).** The 2026 agentic wave is *converging on composable primitives*: Legora Workflows explicitly chain review + draft + research + translate + DB-query as steps; CoCounsel hides Draft/Review/Calc/Research skills behind an agentic planner; Harvey's Agent Builder + 500 agents is a shared library surfaced contextually. So "shared tool library, surfaced per work-type" is not just viable — it is where the leaders are heading.

**The nuance / where Spaarke diverges (deliberately):**

- **Spaarke uses ONE Compose editor as the review+revise surface with comments-first, user-driven redlines.** The market's dominant *review* pattern for volume is a **tabular grid (rows=docs, cols=questions)**, and the dominant *redline* pattern is **batch AI suggestions in Word**. Spaarke's single-doc, comment-first, human-drives-each-edit model is closest to **Spellbook/Definely (in-editor)** and is well-suited to the **NDA advisory north-star** (Claude/ChatGPT-level advisory reasoning, human-verified, per the project north-star memo 2026-07-24). This is a legitimate divergence, not a gap — *for the single-document advisory use case*. **The signal to heed:** the moment Agreement Analysis needs to span a *portfolio or data room* (many NDAs at once), the market says you will want a **tabular multi-doc review surface** as a *second* Agreement-family surface, not a stretch of the single Compose editor. Flag this as a future surface, not an r1 need.
- **Spaarke has no authorities corpus (Westlaw/Lexis/CourtListener).** The market's research surfaces are defined by their grounding corpus + citation-treatment tooling (KeyCite/Shepard's). A Spaarke "Legal Research" surface grounded only in firm/RAG references will be a *different animal* — closer to Harvey Knowledge over firm memoranda than to CoCounsel over Westlaw. Set expectations accordingly (see recommendations).

### Concrete UX recommendations for a future Spaarke "Legal Research" widget

Treat this as a **separate surface from Compose**, not a Compose mode. The research work type has a genuinely different information shape (query → many authorities → synthesized memo) that does not map onto a document-editing canvas. Recommended pattern, drawn from Harvey Knowledge + CoCounsel Deep Research + Protégé:

1. **Three-pane research layout** (not an editor): **(left) query + scope selectors**, **(center) synthesized answer/memo with inline citation chips**, **(right) authorities panel** that shows the cited source *inline* when a chip is clicked (Harvey's "no separate window" pattern — the highest-value, most-repeated research UX detail). Do **not** reuse the Compose two-pane doc+comments layout.
2. **Scope selectors as the knowledge sub-axis**, mirroring Regional Knowledge Sources / practice-area+jurisdiction: let the user constrain grounding to a **reference set / practice area / jurisdiction** before querying. In Spaarke terms, this is a selector over the RAG reference indexes (`spaarke-rag-references`) and any future authority sources — the research analogue of the NDA playbook selector.
3. **Citation-first output with a verification/treatment signal.** Every assertion carries a citation chip to a retrieved source; render an explicit **"grounded / could not verify"** state per citation (the Protégé hallucination-guard pattern). Since Spaarke lacks KeyCite/Shepard's, do **not** promise "is this still good law" treatment — instead promise **"grounded in your reference set with verifiable pinpoint citations,"** and pin each finding to an exact passage (GC AI "Exact Quote" pattern). This is honest given the corpus and still differentiating.
4. **Deep-research as an async agentic run, not a chat turn.** CoCounsel Deep Research (3-8 min → structured memo, 80-85% usable) sets the expectation: a research memo is a *job* with a progress state and a structured, citation-dense deliverable — reuse Spaarke's existing async/agentic execution + notification spine rather than forcing it into a synchronous chat bubble.
5. **Memo → Compose hand-off, not merge.** Let a completed research memo **launch into a Compose document** (via the existing `surfaceLaunchRegistry` / `handleSurfaceLaunch` mechanism) so drafting continues on the shared editor core. This is how Spaarke gets "shared primitives" leverage: Research produces content; Compose edits it. Keep the two surfaces distinct but connected — exactly the Legora Workflows "research step feeds draft step" model.
6. **Distinct tool palette.** Research tools = {search/retrieve, synthesize-with-citations, verify-citation, generate-memo, export-to-Compose}. Do **not** surface Compose's clause tools (Draft Alternative, Compare-to-Playbook) here; do surface "send to Compose." This keeps palettes work-type-scoped per the validated market pattern.

### Cross-product summary table

| Product | Top-level surfaces (work types) | Contract-review UX | Legal-research UX | Signature tools | Knowledge-scoping |
|---|---|---|---|---|---|
| **Harvey** | Assistant · Vault · Knowledge · Workflow Agents | Vault = tabular multi-doc grid (rows=docs, cols=Qs); Assistant = chat+doc split | Knowledge = Q → synthesized cited answer, inline case view; Regional Knowledge Sources | 500+ pre-built agents, Agent Builder, Vault queries | Vault projects · Regional Knowledge Sources · firm memoranda |
| **Legora** | Assistant · Tabular Review · Research · Agentic Workflows | Tabular Review = flagship grid, cell-linked citations | Research = agentic, federated over DMS + legal DBs + web | Workflows chaining review/draft/research/translate/DB | Playbooks · connected sources · review doc-set |
| **CoCounsel (TR)** | Agentic assistant over skill families: Draft · Review · Calc · Research (+ Deep Research) | Review skill, docs ≤300pp; agentic (no prominent grid) | Deep Research = cited memo (3-8min, 80-85%); KeyCite treatment inline | Skills behind agentic planner (Claude Agent SDK), Deep Research | Westlaw/TR corpus (default) · org docs when enabled |
| **Spellbook** | Word add-in assistant (primary) · Spellbook Associate (multi-doc agent) | In-editor sidebar: flag missing clauses, playbook suggestions, risk, alternatives | Not a first-class surface (drafting/review-centric) | Draft clause, review-to-playbook, suggest alternative; Associate for data rooms | Playbooks · open document · market-standard comparison |
| **Robin AI** | Query · Reports · Review · Draft | Review = redline + risk flag vs playbook; Reports = extraction | Query = deep search over *your contracts* (not case law) | Redlines vs company playbook, obligation tracking, extraction | Contract portfolio · company playbook · obligation data |
| **LexisNexis Protégé** | Single Protégé assistant over Draft · Research · Analyze workflows (+ Protégé Work, May 2026) | Assistant-over-document, ≤1M chars; practice-area personalization | Cited responses + Shepard's treatment + citation-existence verification | Shepard's, citation validator, Practical Guidance | Lexis corpus + Shepard's (default) · iManage/SharePoint org docs |
| **(others)** | Ironclad (CLM stages) · Luminance (Ask Lumi + portfolio grid) · Definely (in-editor terms/refs) | Grid-for-volume (Luminance) / in-editor (Definely, Ironclad) | Mostly N/A (corpus/CLM-centric) | Clause library, defined-terms navigation, agentic review | Playbook + clause library (Ironclad/Luminance) |

---

## Recommended follow-ups

1. **Verify the "portfolio review" trigger for Spaarke.** Confirm whether Agreement Analysis will ever need many-NDAs-at-once. If yes, scope a *second* Agreement-family surface (tabular grid) rather than overloading Compose — the market is unanimous that volume review wants a grid.
2. **Decide the research grounding corpus honestly.** Spaarke Legal Research over `spaarke-rag-references` is a firm-knowledge research surface, not a Westlaw/Lexis competitor. Confirm this positioning before UX work so citations/verification claims stay truthful (no "good law" promise without a treatment corpus).
3. **Re-verify volatile facts before spec.** Harvey agent count, CoCounsel "Reimagined" internals, Protégé Work scope, and any renames are <90-day claims — re-check at spec time.
4. **Inspect real screens.** This report is analyst/marketing-sourced on UX mechanics. Before committing to the three-pane research layout, capture actual screenshots of Harvey Knowledge and CoCounsel Deep Research for a fidelity pass.
5. **Model the "send research memo → Compose" hand-off** against the existing `surfaceLaunchRegistry` contract (see `docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`) to confirm the shared-primitive bridge is code-cheap.
