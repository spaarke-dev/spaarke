# Coordination — `ai-advanced-capabilities-analysis-hub-r1` → `ai-advanced-capabilities-agreements-r1`

> **From**: `analysis-hub-r1` (the Analysis **platform**: hub widget · wizard/modal · `sprk_analysis` spine · sessions · cross-surface communication)
> **To**: `agreements-r1` (the Agreement Analysis **review machine**: classifier · general Action · review-depth UX · memo/export)
> **Date**: 2026-07-30 · **Owner**: ralph.schroeder
> **Direction**: This is the **reverse** of `COORDINATION-with-analysis-hub-r1.md` (agreements-r1 → hub, 2026-07-29). That doc was written when the hub was "near-deploy" and treats A1–A6 as **open**. **They are now largely CLOSED in code** — this doc reports current built reality so agreements-r1 re-plans against facts, not assumptions.

---

## 0. Headline for agreements-r1 planning

When your coordination doc was authored (2026-07-29), hub-r1 had not completed. It now has: **tasks 001–070 shipped** (spine, session binding, fork, two-tier promotion, hub widget, wizard, entry matrix, record integration, legacy retirement, BFF deploy). **071 in progress** (client/ribbon deploy), **072/090 remaining** (e2e + wrap-up). Plus a **post-plan Phase 1** (NDA Analysis card → wizard-as-modal → open document in the **editable Compose** surface).

**Net effect on your plan**: your explicit-wizard path and durable classifier-started Analysis do **not** need to "degrade to classifier-only" — the substrate they depend on (A3/A4) is built. You can depend on it now.

---

## PART A — What hub-r1 has ALREADY BUILT (correct the stale picture)

| Capability agreements-r1 assumed open / near-deploy | Actual status | Where |
|---|---|---|
| `sprk_analysis` spine (`sprk_worktype`, regarding field-set, subgrids) | ✅ shipped | tasks 011/012/051 |
| File resolution via `sprk_documentid` → `sprk_document` SPE hop (no duplicate SPE pointers) | ✅ shipped | task 013 (ADR-007) |
| Session ↔ Analysis binding FK (`sprk_aichatsummary.sprk_analysis`) on `ChatEndpoints` | ✅ shipped | task 020 |
| **Fork-on-analysis** `POST /api/ai/analysis/fork` | ✅ shipped | task 021 |
| Archive durability (`sprk_isarchived` flip; Cosmos = store-of-record, never deleted) | ✅ shipped | task 022 |
| **Two-tier session model + explicit promotion** `POST /api/ai/analysis/promote` (bind a loose session after the fact) | ✅ shipped | task 023 |
| Add-file-to-running-chat (new-session vs add-to-current) | ✅ shipped | task 024 |
| Persistence hardening (stale→Cosmos, tab anchor, edit restore) | ✅ shipped | task 025 |
| Hub widget (grid + view-by-type) — **now a plain dataset grid; `+New` → tabbed Quick Start** | ✅ shipped | task 030 + Phase 1 |
| Reopen from grid → rehydrate session + review state + files | ✅ shipped | task 031 |
| Per-type **creation wizard** (3 steps; `CreateRecordWizard` + Field Mapping) — **now launches AS A MODAL** | ✅ shipped | task 040 + Phase 1 |
| `activeWorkType` host prop → `getToolsForSurface` tool-palette scoping | ✅ shipped | task 041 |
| Entry matrix 2a–2d host routing + context pre-set | ✅ shipped | task 050 |
| `openSpaarkeAi` extended (`analysisId`/`worktype`/`regarding`) + ribbon launcher; record-driven opens enforced via `openSpaarkeAi` (ADR-039), not `surfaceLaunchRegistry` | ✅ shipped | tasks 052/053 |
| Legacy retirement (`/continue`,`/resume`,`sprk_chathistory` read, `sprk_analysischatmessage`, `AnalysisWorkspace/` tree) | ✅ shipped | tasks 060–064 |
| Open analysis document in **editable Compose** (wizard-finish + analysis-open), work-type `agreement-analysis` seed | ✅ shipped (Phase 1) | Phase 1 |
| **Cross-surface communication** (PaneEventBus intents `open_quick_start`, `open_create_analysis_wizard`, `widget_load` compose; Assistant↔Workspace hand-off; launch envelope) | ✅ hub-owned, shipped | PaneEventTypes + WorkspacePane/ConversationPane |

**Scope claim (confirmed with owner 2026-07-30)**: hub-r1 owns the **wizard/modal** AND the **communication paths between Spaarke AI surfaces** (Assistant / Workspace / Context). Agreements-r1 consumes these; it does not build cross-surface routing.

---

## PART B — Answers to your asks A1–A6

| Ask | Answer | Detail |
|---|---|---|
| **A1** — agreement-type/sub-domain picker in the wizard | **Done (built)** — `1e1a6579b` | The Create Analysis wizard renders an **Agreement Type** picker in the details step, reading `sprk_agreementtype` rows (`sprk_isselectable eq true`, ordered by name), defaulting to the launch hint (`defaultSubDomain` → `sprk_key`) else the `sprk_isfallback` row. On finish it **persists** the `sprk_agreementtype` lookup (`_sprk_agreementtype_value`) on `sprk_analysis` (nav-prop via `discoverNavProps`, PascalCase fallback `sprk_AgreementType`). **You own the row set** (the picker is data-driven off your registry table). |
| **A2** — persist sub-domain on `sprk_analysis` | **Done (built)** | Resolved as a **lookup**, not an option set (see Part D rationale). Created reference table **`sprk_agreementtype`** (the data-driven registry — new type = add a row, zero code); the `sprk_analysis.sprk_agreementtype` lookup (OData `_sprk_agreementtype_value`; note `sprk_agreementtypeid` is the *table's PK*, not the lookup attr) points to it. The **same table is reused by the new `sprk_agreement` entity**. Seed rows loaded. **The option set + integer↔key map are dropped.** |
| **A3** — launch envelope carries `activeWorkType` + `subDomain` | **Core built** — `bd64a69d4` | `activeWorkType` shipped (041/052). `subDomain` (= `sprk_key`) is now a first-class field on `ComposeLaunchContextValue` + the SpaarkeAi compose seed (all three door shapes), carried from the **wizard-finish** compose dispatch. **Deferred to land with your consumer** (so the shape matches your reader, not a guess): the cold-load URL/ribbon deep-link threading (`openSpaarkeAi` URL param → App/ThreePaneShell → `AnalysisLaunchContext`) and the **open-existing** `subDomain` derivation (expand `sprk_agreementtype.sprk_key` from the persisted lookup on reopen). Tell us the moment you wire a reader and we finish both. |
| **A4** — session↔Analysis binding callable from a **non-wizard (classifier)** trigger | **Confirmed YES** | `POST /api/ai/analysis/fork` (021) + `POST /api/ai/analysis/promote` (023, binds a *loose* session to an Analysis after the fact) + the binding FK (020) are all live and are **not wizard-coupled**. Your classifier path can start a review on a loose session, then call **promote** to bind it durably — no "transient-until-bound" fallback needed. |
| **A5** — `sprk_analysisoutput` shape for the memo | **Confirmed stable** | 1:N `sprk_analysis → sprk_analysisoutput` is on our KEEP list (never retired). `analysisId` is available at memo-generation time via the bound session's `HostContext.EntityId`. No conflicting write contract from hub (we don't write review results — see Part C). |
| **A6** — build only on KEEP surfaces | **Confirmed** | Retirement (062–064) is done. KEEP set preserved: `sprk_analysisoutput`, `/export`, `ChatEndpoints`, shared widgets `NdaReviewSummaryPanel`/`FindingsWidget`/`AnalysisEditorWidget`. No KEEP item's retirement status changed. |

---

## PART C — Items surfaced in hub-r1 that are AGREEMENTS-R1's to build (the durable-review machine)

During hub-r1 UAT + design we traced the full NDA/agreement execution machinery. These items are **agreements-r1's chartered scope** (they hold the classifier/schema/memo/compose-fidelity build context — hub must NOT build them context-stripped). Recorded here so nothing is lost in the seam.

1. **The durable-recall gap for `nda-review` output is a DISPOSITION problem, not a persistence one.**
   - The `nda-review` dispatch result `{overallRisk, flaggedSections[]}` **is already written durably** to the Cosmos session ledger as a `SessionOutput` (ADR-040, store-of-record, never deleted on archive).
   - BUT it is written with **`informational`** disposition, and (a) `GET /sessions/{id}/compose-outputs` filters to **`compose`** disposition only → it's skipped; (b) `/restore` doesn't return `Outputs`; (c) the advisory gutter comments are a **client-side ephemeral projection** (`useNdaReviewAdvisoryCommentsBridge`) recomputed from the live terminal chunk and **lost on reload**.
   - **Consequence**: today, reopening re-derives comments only by **re-dispatching** (LLM cost + latency + non-determinism) — unacceptable for a legal work product.
   - **The robust fix (yours)**: route the general `agreement-review` findings through the **already-durable `compose`-disposition anchored-comment (+ redline where applicable) path** that ComposeWorkspace's **refresh-durability effect (task 016 FR-04 / `redline-from-ledger`)** already re-materializes on load. Then reopen restores comments **deterministically, no re-run**. This aligns with your FR-05 (schema split) + FR-06 (general Action) + FR-13 (memo).

2. **DEF-01 clause-anchoring correctness** — already your **FR-04**. The fidelity-of-placement concern (a target matching >1 location or below confidence must be `ambiguous`/`not_found`, never silently placed) is the crux of durable comment/redline reopen. Re-enable `ComposeEditor.advisoryComments.test.tsx` with its original assertion.

3. **Anchoring via `paraId` + `CitationResolver`** — your **FR-03**. Comments/redlines anchor to stable `paraId` + `ComputedNumber` (from the one server projection), not text-search. This is what makes placement survive edits across the two stores (SPE = document bytes; Cosmos = the anchored AI layer).

4. **The session/fileId impedance the wizard introduces.** The wizard produces a **durable `sprk_document`** (`sprk_graphitemid`/`sprk_graphdriveid`), whereas the chip/dispatch path runs against **session-uploaded file ids**. Whoever wires auto-run must bridge these (register the durable doc as the session's file context before dispatch). Flagged for your classifier/orientation + review-run wiring.

5. **Review-RESULTS recall = yours (memo → `sprk_analysisoutput`, FR-13); conversation-HISTORY recall = ours** (session binding + restore). The reopen experience is the composition of both legs.

6. **The `sprk_analysisoutput` naming footgun**: the literal string `"sprk_analysisoutput"` is **overloaded** — it's both the real Dataverse result table AND a `HostContext.EntityType` sentinel whose `EntityId` actually points at an **`sprk_analysis`** GUID (`ChatDataverseRepository.cs:36`). Any code near the binding must not conflate them.

---

## PART D — Contract shapes

**Naming reconciliation**: your spec says "sub-domain"; the hub table is **`sprk_agreementtype`** — **same concept**. Everywhere your spec says `subDomain`, the value is the row's **`sprk_key`** (the stable slug). No second concept.

**C1′ — The sub-domain registry IS the `sprk_agreementtype` table (single source of truth).** Resolved as a **Dataverse lookup**, not an option set: new agreement type = **add a row** (zero code), matching your C1 "data-driven, zero-code" goal, and collapsing persistence + picker + registry into ONE store (no parallel list). Ownership split:
- **Hub owns** the table + identity rows: `sprk_key` (unique/alt-key), `sprk_name`, `sprk_isfallback`, `sprk_isselectable`, `sprk_sortorder`.
- **Agreements-r1 owns** the behavior values per row: `sprk_knowledgepackref` (grounding + taxonomy/rubric), `sprk_classificationcue`, `sprk_confidencethreshold` (per-type override of the ≥0.85 baseline; null → global).

Seed rows (identity loaded by hub; behavior columns filled by agreements-r1 as packs are authored):

| `sprk_key` | `sprk_name` | `sprk_isfallback` |
|---|---|---|
| `general` | General / Unclassified | **Yes** |
| `nda` | NDA / Confidentiality | No |
| `employment` | Employment | No |
| `lease` | Lease / Real Property | No |
| `asset-purchase` | Asset Purchase | No |
| `services` | Services / MSA | No |
| `licensing` | Licensing / IP | No |
| `vendor` | Vendor / Procurement | No |
| `partnership` | Partnership / JV | No |
| `loan` | Loan / Financing | No |

> The classifier routes an unmatched/low-confidence doc to the single `sprk_isfallback=Yes` row (`general`) — no magic-string key. "Both = multiple packs" (composite docs, your FR-08) is a routing-time 1-to-many over rows, not a schema concern.

**C2 — Launch / hand-off envelope (hub → machine), with `subDomain` added:**
```
openSpaarkeAi({
  ...existing,
  activeWorkType: 'agreement-analysis',   // shipped (041/052)
  subDomain: 'nda' | 'lease' | ... ,       // = sprk_agreementtype.sprk_key; user-selected (wizard) or absent (chat-upload → your classifier infers)
  analysisId, regarding, speDriveItemId/documentId
})
```
The hub resolves `subDomain` (`sprk_key`) → the `sprk_agreementtype` lookup when writing `sprk_analysis`. **No integer↔key map** — the lookup deletes the need.
Worktype reference: `SprkAnalysisWorkType.AgreementAnalysis = 100000000` (the "NDA Analysis" card maps here today).

**C3 — Persistence**: `sprk_analysis.sprk_worktype = agreement-analysis` (hub) · **`sprk_analysis.sprk_agreementtype`** lookup (`_sprk_agreementtype_value`) → `sprk_agreementtype` table (hub, A2 — reused by `sprk_agreement`) · `sprk_analysisoutput` (hub KEEP, your memo target, A5).

---

## PART E — Stale operational constraint to drop

Hub-r1's own `CLAUDE.md`/`TASK-INDEX.md` and your spec's ADR-013 line both reference **`Services/Ai/` as "sole-owned by `spaarke-ai-architecture-redesign-r2`, consume PublicContracts, no fork, `/conflict-check` every PR."**

**That project is CLOSED and merged to master (2026-07).** `Services/Ai/` is now ordinary master code:
- **Still binding**: ADR-013 (CRUD→AI via `Services/Ai/PublicContracts/` facade; don't inject AI-internal types into CRUD). That's an architecture rule, not an ownership lock.
- **No longer applies**: the sole-owner lock, the "no fork," and the per-PR `/conflict-check`-vs-redesign-r2 gate. Agreements-r1 may modify `Services/Ai/` internals (e.g., the disposition routing in Part C.1) as normal master code, honoring ADR-013.

---

## Open items / decisions needed FROM agreements-r1

1. **Confirm ownership of Part C.1** (route `agreement-review` output to `compose` disposition + anchored comments) — hub is NOT building it; confirm it's on your FR-05/06/13 path.
2. **Take ownership of the behavior columns on `sprk_agreementtype`** (`sprk_knowledgepackref`, `sprk_classificationcue`, `sprk_confidencethreshold`) — hub seeded identity rows (Part D); you fill routing/grounding as packs are authored. Propose any new type = new row (send hub the `sprk_key`/`sprk_name` or just add it — the table is the registry).
3. **A1 timing** — the wizard picker reads `sprk_agreementtype` rows directly (governed + data-driven), so it's not blocked on any separate registry shipping. Confirm the picker should filter on `sprk_isselectable=Yes` (and/or active statecode).
4. **A4** — confirm the promote-after-loose-session flow fits your classifier path, or whether you want a single "start-review-and-bind" convenience endpoint (hub can add if it's genuinely better positioned on our side).

*Companion to `notes/COORDINATION-with-analysis-hub-r1.md` (their → hub). Reflects hub-r1 built state as of 2026-07-30 (tasks 001–070 shipped; 071 in progress).*
