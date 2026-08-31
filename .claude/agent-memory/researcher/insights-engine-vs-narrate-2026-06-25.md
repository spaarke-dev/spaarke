---
name: insights-engine-vs-narrate-2026-06-25
description: Full picture of Spaarke Insights Engine (r1/r2/r3/widgets-r1) and its relationship to Daily Briefing /narrate endpoint. Why /narrate is the right home for short briefing summarization in 2026-06-25 — Insights Engine is for evidence-grounded matter context, not 100-notification summarization.
metadata:
  type: project
---

## 2026-06-25: Insights Engine vs Daily Briefing /narrate endpoint

**Question**: Should Daily Briefing's TL;DR + per-channel bullets be powered by the Insights Engine? User said "this seems like 'Insights Engine' territory."

**Findings**:

1. **Insights Engine purpose**: Context-production service for **matter-level evidence-grounded claims** from documents/Dataverse — produces 4 typed artifacts (Fact / Observation / Precedent / Inference) with provenance, confidence, and structured Decline. Anchored on `spaarke-insights-index` AI Search index (Observations + Precedents). r1 ships end-to-end Phase 1 (synthesis = `predict-matter-cost@v1` only). r2 (Phase 1.5) generalized to multi-entity + RAG path + intent classifier + Spaarke Assistant tool-call contract v1.1. r3 (Phase 2) in design. Widgets-r1 shipped the first surface (Matter Health single-mode card) — a per-record matter card, not a daily list.

2. **Daily Briefing today uses `IBriefingAi`, NOT `IInsightsAi`**. `BriefingAi.cs` is a thin facade over `IOpenAiClient.GetCompletionAsync` — one prompt in, one string out, no grounding, no RAG, no playbook. Lives in `Services/Ai/PublicContracts/BriefingAi.cs`. Comments explicitly call out "daily-briefing summarization" + "matter-summary" as the use cases.

3. **Why Insights Engine is NOT a fit for daily TL;DR**:
   - `IInsightsAi.AnswerQuestionAsync` is **playbook-bound** — needs `playbookId@version`, expects subject like `matter:GUID`. There's no playbook for "summarize today's 60 notifications across 12 matters."
   - `IInsightsAi.SearchAsync` is RAG over `spaarke-insights-index` (Observations + Precedents). The data the daily briefing summarizes lives in **`appnotification` rows** — NOT in the insights index.
   - Hallucination protections (D-P9 GroundingVerifier, D-P10 confidence thresholds, structured Decline) are designed for **document quote verification**. They cannot enforce "use the actual firm name from row X" when the source is structured rows, not free-form documents.

4. **The /narrate firm-name hallucination is fundamentally a prompt + payload problem**:
   - `BuildNarrateTldrPrompt` (DailyBriefingEndpoints.cs:452) already includes channel item details with `RegardingName`. The TL;DR prompt has them.
   - But `BuildChannelNarrationPrompt` (line 526) emits per-item `regardingId=` and the endpoint already has **`ValidateBulletPrimaryEntityIds`** (line 635) that nulls out hallucinated GUIDs in **channel bullets**. There is NO equivalent validation on the **TL;DR free-text `summary` field** (it's just a string).
   - Firm names hallucinated INTO `tldr.summary` text cannot be caught by GUID validation — only by entity-name allow-list scrubbing or by anchoring summary text to verbatim quotes.

5. **Architectural delineation between BriefingAi and InsightsAi**:
   - `IBriefingAi.GenerateNarrativeAsync` — single-prompt completions for **short, deterministic narrative enhancement** (3-4 sentence summaries). Existing consumers: workspace briefing card, matter AI summary SSE, daily briefing. NO grounding, NO playbook overhead.
   - `IInsightsAi` (5 methods) — Insights playbook + RAG + ingest + embedding + assistant routing. Heavyweight; subject-scoped (matter/project/invoice); evidence-grounded. NOT for 100-notification daily summarization.

**Sources**:
- `projects/ai-spaarke-insights-engine-r1/README.md` — Phase 1 scope (4 artifact types, spaarke-insights-index)
- `projects/ai-spaarke-insights-engine-r2/README.md` — Phase 1.5 (hybrid playbook+RAG, multi-entity, Assistant contract v1.1)
- `projects/ai-spaarke-insights-engine-r3/README.md` — Phase 2 design phase
- `projects/ai-spaarke-insights-engine-widgets-r1/README.md` — Matter Health single-mode card (first widget surface, per-record)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInsightsAi.cs` + `IBriefingAi.cs` + `BriefingAi.cs`
- `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs` — current /summarize + /narrate code (using IBriefingAi)
- `docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`

**Open questions**:
- Should daily-briefing widget eventually use InsightsAi *for matter-specific bullets* (e.g., "Acme Corp has 3 overdue tasks and the engagement letter is 2 days overdue — last similar matter took 6 weeks")? That's a Phase 3+ enhancement, not the right place to start for fixing hallucination.
- The matter-summary SSE endpoint also uses BriefingAi — does it have the same hallucination class of bug?
