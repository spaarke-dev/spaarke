# Coordination — agreements-r1 → analysis-hub-r1: ANSWERS to your 4 asks + 5 QUESTIONS back

> **From**: `ai-advanced-capabilities-agreements-r1` · **To**: `ai-advanced-capabilities-analysis-hub-r1`
> **Date**: 2026-07-31 · **Owner**: ralph.schroeder
> **Re**: your reverse coordination doc (`projects/ai-advanced-capabilities-analysis-hub-r1/notes/COORDINATION-hub-r1-TO-agreements-r1.md`, commit `0370f4dee`) — its "Open items / decisions needed FROM agreements-r1" section.
> **Basis**: a 7-agent code-verified review of your built state — `projects/ai-advanced-capabilities-agreements-r1/notes/HUB-R1-REVIEW-2026-07-30.md` (file:line evidence for everything asserted below).

---

## PART 1 — ANSWERS to your 4 open asks

**1. C.1 ownership (durable-recall re-route)** → ✅ **CONFIRMED OURS.** Now spec'd as agreements-r1 **FR-16**: Binding
disposition flip + a **findings branch** in the FR-04 materializer (via `placeAdvisoryComments`, preserving
riskLevel/sectionRef/standardRef) + DEF-09 document-session routing + apply-leg gating for findings-only outputs.
We verified your framing and note it's a 4-change set (not just the disposition flip) — details in our review doc §4.
Your Phase-2 remainder (wizard-finish auto-run + fileId bridge) is also accepted as our **FR-17**.

**2. `sprk_agreementtype` behavior columns** → ✅ **CONFIRMED OURS.** We own the VALUES of `sprk_knowledgepackref`,
`sprk_classificationcue`, `sprk_confidencethreshold` (filled via `update_record` as packs are authored). We will also
author the **code mirror** (TS type + infra seed JSON — today the table has zero repo references) and can load the
remaining 7 identity seed rows (see Q3).

**3. A1 picker filter on `sprk_isselectable=Yes`** → ✅ **CONFIRMED — and the blocker is CLEARED.** The owner updated
the 3 seed rows to `sprk_isselectable=Yes` on 2026-07-31 (verified live via MCP). Filter semantics: picker shows
`sprk_isselectable=Yes` (+ active statecode); the classifier may route to non-selectable rows but users can't pick them.

**4. A4 promote-vs-convenience-endpoint** → ✅ **Promote-after-loose-session FITS our classifier path** — no
convenience endpoint needed. Two conditions: (a) our classifier sessions will always carry a documentId (satisfying
promote's 400 guard); (b) the **silent-FK gap must be fixed** (see Q2 below).

---

## PART 2 — QUESTIONS back to hub-r1 (need answers; none block our interactive path)

**Q1 — Who builds the A1 wizard picker + A3 `subDomain` envelope param?** Your doc says "will build" / "finishing",
but your remaining tasks are 071 (env)/072 (e2e)/090 (wrap-up). **We are happy to take BOTH** (we own the registry the
picker reads; the param is small: `SpaarkeAiLaunchParams` + `buildLaunchUrl` + `main.tsx` parse — we've already scoped
the seam). Just need a yes/no so nobody double-builds. **Our default if no answer: we build both.**

**Q2 — Promote silent-FK gap: fix on your side or ours?** `PromoteSessionToAnalysisAsync` ignores
`BindSessionToAnalysisAsync`'s bool (`ChatSessionManager.cs:527` / `ChatDataverseRepository.cs:261-272`): if the
session's `sprk_aichatsummary` row never existed (Dataverse create is tolerated-failure), promote returns **201 with NO
durable FK** — the session is invisible to `GET /sessions/by-analysis` and the hub grid. For legal work product this
can't ship as-is. Small server fix (propagate the bool → retry-create the summary row or return a non-2xx/warning).
**Our default if no answer: we fix it inside FR-17.**

**Q3 — Remaining 7 seed rows** (`lease`, `asset-purchase`, `services`, `licensing`, `vendor`, `partnership`, `loan`) —
do you load them (identity columns, per your Part D table), or shall we as part of our seed-JSON/code-mirror task?
**Our default: we load them** (with `sprk_isselectable=Yes`, sortorder per your table).

**Q4 — `sprk_key` unique/alternate-key constraint** — your doc says unique/alt-key, but this is **unverifiable via MCP
describe**. Was it actually created? If not, please add (or confirm and we will). Our routing code will key on
`sprk_key`, so uniqueness must be enforced, not assumed.

**Q5 — Phase 1 owner-UAT status** — your handoff says Phase 1 (NDA Analysis card → wizard-as-modal → editable Compose)
is deployed but **awaiting owner UAT**. Our FR-17 builds directly on the wizard-finish seam
(`CreateAnalysisWizardWidget` finish → `widget_load` compose). Any UAT-driven changes to that seam ripple into us —
please flag when UAT closes (or if the seam changes).

---

## PART 3 — FYIs (no action needed; corrections for your docs)

- **Naming**: your doc says the lookup is `sprk_analysis.sprk_agreementtypeid` (3×) — the actual attribute logical name
  is **`sprk_agreementtype`** (OData `_sprk_agreementtype_value`; `sprk_agreementtypeid` is the reference table's PK).
  Worth correcting so nobody codes from the doc.
- **Attribution**: the refresh-durability effect you cite as "task 016 FR-04" is **spaarkeai-compose-r2's** task 016,
  not a hub task (`ChatEndpoints.cs:1266` marker). No functional impact.
- We noted (and will respect) that `sprk_chathistory` is **live with second semantics** (Insights ObservationMirror
  producer-context JSON) — not deletable schema; and that `AnalysisResponse.ChatHistory` is always-empty back-compat.
- Your `HANDOFF-2026-07-30` §4 still describes A2 as a planned `sprk_subdomain` column — superseded by the
  lookup-table resolution; consider a one-line correction to avoid confusion.

---

*Companion docs: our review (`notes/HUB-R1-REVIEW-2026-07-30.md`) · our original asks
(`notes/COORDINATION-with-analysis-hub-r1.md`, A1–A6) · your reverse doc (answers A1–A6). agreements-r1 spec now at
17 FRs incl. FR-16/FR-17 (your Part C / Phases 2–3).*
