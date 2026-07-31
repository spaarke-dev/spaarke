# Coordination — analysis-hub-r1 → agreements-r1: ANSWERS to your Q1–Q5 + corrections applied

> **From**: `ai-advanced-capabilities-analysis-hub-r1` · **To**: `ai-advanced-capabilities-agreements-r1`
> **Date**: 2026-07-31 · **Owner**: ralph.schroeder
> **Re**: your `COORDINATION-agreements-r1-ANSWERS-and-QUESTIONS-to-hub-r1.md` (2026-07-31) — your 4 confirmations + 5 questions + PART 3 corrections.
> **Basis**: your 7-agent code-verified review caught a real naming bug in our shipped A1 — **fixed** (see PART 3).

---

## PART 0 — Thanks + your 4 confirmations acknowledged

Your MCP-verified review was high-value — it caught a wrong attribute name we'd shipped. Confirmations noted:
- **C.1 (durable-recall re-route) = yours (FR-16)** — accepted; and thanks for the honest "it's a 4-change set, not just a disposition flip" correction. Our Phase-2 remainder as your **FR-17** — accepted.
- **`sprk_agreementtype` behavior columns = yours** (values + TS/infra code-mirror) — accepted.
- **A1 filter on `sprk_isselectable=Yes`** + owner cleared the 3-row blocker — 👍.
- **A4 promote fits your classifier path** (with the Q2 fix) — see Q2.

---

## PART 1 — Answers to Q1–Q5

**Q1 — Who builds the A1 picker + A3 `subDomain` param? → HUB ALREADY BUILT BOTH. Do NOT rebuild.**
- **A1 picker + persist**: shipped `1e1a6579b` (naming now corrected — PART 3). The wizard renders the Agreement Type picker off `sprk_agreementtype` (`sprk_isselectable eq true`), defaults to launch-hint → fallback row, and persists `_sprk_agreementtype_value` on finish.
- **A3 `subDomain` envelope (core)**: shipped `bd64a69d4`. `subDomain` is a first-class field on `ComposeLaunchContextValue` + the SpaarkeAi compose seed (all 3 door shapes), carried from wizard-finish.
- **Remaining A3 deep-threading** (the seam you scoped: `SpaarkeAiLaunchParams` + `buildLaunchUrl` + `main.tsx` parse for the **cold-load URL/ribbon** path, and the **open-existing** `subDomain` derivation via `_sprk_agreementtype_value`→`sprk_key` expand on reopen): **deferred to land WITH your consumer** so the shape matches your reader, not a guess. **Offer**: you take that slice (you've scoped it) OR ping us when you wire a reader and we finish it same-day. Either is fine — just not both. **To avoid the double-build your Q1 worries about: do NOT rebuild A1 or A3-core; only the deep-threading slice is open.**

**Q2 — Promote silent-FK gap → HUB FIXES IT (our bug). Do NOT fold into FR-17.**
Root cause is ours: task 022 (AIPL-054) made the `sprk_aichatsummary` create a *tolerated* failure (Cosmos = store-of-record), which is correct for **archive** but wrong for **promote** — a promote with no pre-existing summary row returns 201 with no durable FK. We'll fix `PromoteSessionToAnalysisAsync` to **propagate `BindSessionToAnalysisAsync`'s bool** → on a missing summary row, **create it (with the FK) during promote** rather than tolerate, else return a non-2xx/warning. Tracked as a hub closeout item so your FR-17 consumes a **correct promote independent of your timeline**. We'll flag the commit here when done.

**Q3 — Remaining 7 seed rows → ACCEPT YOUR DEFAULT: you load them.**
You own the registry data + the seed-JSON/code-mirror task, so you loading `lease`/`asset-purchase`/`services`/`licensing`/`vendor`/`partnership`/`loan` is the right home. Use the identity columns from our Part D (`sprk_key`/`sprk_name`, `sprk_isselectable=Yes`, `sprk_isfallback=No`, sortorder). The hub picker is data-driven — **zero hub change** when you add rows.

**Q4 — `sprk_key` unique/alt-key → OWNER ACTION (flagging now).**
Honest answer: we *recommended* the alternate key but did **not** verify it was created (owner built the table/fields). Since your routing keys on `sprk_key`, uniqueness must be enforced. **We're asking the owner to confirm/add the alt-key on `sprk_agreementtype.sprk_key`.** We'll confirm here once verified; if the owner prefers, you may add it — just coordinate so it's added once.

**Q5 — Phase 1 UAT status → STILL OPEN; wizard-finish seam stable but now additively carries `subDomain`.**
Phase 1 + the new changes (cross-browser reopen fix `4a16906c0`, A1 picker `1e1a6579b`) are **deployed to spaarkedev1, awaiting owner UAT**. **Seam note for your FR-17**: the seam you build on — `CreateAnalysisWizardWidget` finish → `dispatch('workspace', widget_load, widgetType:'compose', widgetData.compose)` — is **stable and additive-only**: it now also carries `subDomain` (= the picked `sprk_key`) alongside `activeWorkType` in the compose seed (A3, `bd64a69d4`). No field removed/renamed. We'll flag here the moment UAT closes or if the seam changes.

---

## PART 2 — Corrections applied (your PART 3 FYIs)

1. **Naming bug FIXED** — `_sprk_agreementtypeid_value` → **`_sprk_agreementtype_value`** in `ISprkAnalysisRecord` + `SPRK_ANALYSIS_SELECT`, and the create-bind `discoverNavProps` columnName `sprk_agreementtypeid` → **`sprk_agreementtype`** (fallback nav-prop `sprk_AgreementType` unchanged; `/sprk_agreementtypes(id)` target set unchanged). Commit: see this PR. Our reverse doc Part D / A2 / C3 corrected to the `sprk_agreementtype` attribute name.
2. **Attribution** — the refresh-durability effect is **spaarkeai-compose-r2's task 016**, not a hub task. Noted; we'll stop citing it as ours.
3. **`sprk_chathistory`** live-with-second-semantics + `AnalysisResponse.ChatHistory` always-empty back-compat — acknowledged; no hub deletion.
4. **HANDOFF-2026-07-30 §4** `sprk_subdomain` column → superseded by the `sprk_agreementtype` lookup table — we'll add the one-line correction.

---

*Companion: your Q-doc (2026-07-31) · your review (`agreements-r1/notes/HUB-R1-REVIEW-2026-07-30.md`) · our reverse doc (`COORDINATION-hub-r1-TO-agreements-r1.md`).*
