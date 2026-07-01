# Inbound feedback from R7 Wave 12 UAT

> **Captured**: 2026-07-01
>
> **Source**: R7 Wave 12 Daily Briefing operator UAT (spaarkedev1) — evening session 2026-06-30 through morning 2026-07-01
>
> **Feedback owner**: ralph.schroeder@hotmail.com
>
> **Audience**: R5 design.md author

## What this folder is

R7 Wave 12 shipped a large Daily Briefing operator-feedback batch (~9 items). Some items landed as R7 code (case fix, navigateTo binding, HighPriority mini-report, rotating emoji, Add-to-ToDo case fix, LLM prompt tightening via MCP). Others are structural, cross-cutting, or too speculative to land in R7 — they graduated to R5.

Each document in this folder is a **verbatim capture of the feedback**, plus **what R7 already did** (so R5 doesn't repeat work), plus **open questions the design.md author must resolve**. The documents are ordered by expected R5 priority.

## Reading order

Start with `01-llm-hallucinations-and-determinism.md` — it frames the biggest open question ("should Daily Briefing narrative bullets be LLM-generated at all?"). The other four documents are narrower and self-contained.

## Files

| # | Document | Priority | Scope |
|---|---|---|---|
| 1 | `01-llm-hallucinations-and-determinism.md` | HIGH | Architectural — LLM narrative vs deterministic rendering |
| 2 | `02-monitored-for-schema.md` | HIGH | Data model — Choice option set on 7 entities |
| 3 | `03-code-review-followups.md` | MEDIUM | Code quality — 5 items from R7 code-review |
| 4 | `04-latent-bugs.md` | LOW-MEDIUM | Latent bug — same pattern as R7 W12 fix |
| 5 | `05-deploy-safety-governance.md` | PROCESS | Coordination — not code |

## What R7 already delivered (do NOT re-scope in R5)

- **Case fix** on `sprk_highpriority` query (was `sprk_HighPriority` schema-name — Dataverse QueryExpression requires lowercase logical name)
- **`navigateTo` binding fix** — destructuring `xrm.Navigation.navigateTo` loses `this` context and throws `_clientApiExecutor undefined`
- **HighPrioritySection rewrite** as mini-report cards with description + action badge + reason chip
- **Rotating TL;DR emoji** (16-emoji pool, deterministic per `generatedAt`)
- **LLM prompt tightening via MCP** — `BRIEF-NARRATE-CHANNEL` + `BRIEF-NARRATE-TLDR` Actions updated with PAIRING RULE + GROUNDING CHECK + AGGREGATION PREFERENCE + STRUCTURAL PREFERENCE
- **`sprk_AssignedTo@odata.bind` PascalCase fix** — Add-to-ToDo was throwing OData 400

R5 inherits these as the starting baseline.
