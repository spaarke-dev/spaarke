# R6 → R7 Consolidated Defer Register (for R7 scope review)

> **Compiled**: 2026-08-13 by the `spaarkeai-compose-r6` session, at operator request, for review against
> [`../design.md`](../design.md) (Compose R7 — Editor UX).
> **Sources of truth** (this doc consolidates; the sources remain canonical):
> 1. UAT defect register **D1–D9** — `projects/spaarkeai-compose-r6/notes/phase1-deploy-uat.md`
> 2. Accumulated follow-ups ledger — `projects/spaarkeai-compose-r6/notes/020-canonical-hub-design.md` §16/§23
> 3. PDF-intake ledger — `projects/spaarkeai-compose-r6/notes/040-pdf-intake.md` + `notes/042-052-close.md`
> 4. Spec deferred-scope + Unresolved Questions — `projects/spaarkeai-compose-r6/spec.md`
>
> **Disposition legend**:
> `R7-DESIGN` = already covered by the existing R7 design.md use cases ·
> `R7-ADD?` = candidate to add to R7 scope (operator decision) ·
> `OTHER-OWNER` = belongs to a different project/surface ·
> `WAIT-SIGNAL` = deliberately parked until a telemetry/usage signal fires ·
> `FAST-FOLLOW` = explicitly deferred whole-phase scope (own project when scheduled)

---

## A. UAT defects (register D1–D9, operator-driven UAT 2026-08-06 → 2026-08-13)

| ID | Item | Scope of fix | Disposition |
|---|---|---|---|
| **D1** | Duplicate `sprk_document` rows per save session (6 records → same `sprk_graphitemid`). Root cause CONFIRMED: Save split-button invites the "Save New Document" fork by mistake; `forkNew` skips transientKey dedup (`ComposeService.cs:1030-1033`) AND reuses the unchanged filename (`ComposeWorkspace.tsx:1517`), so Graph PUT-by-path coalesces onto the EXISTING driveItem → new record, same file | (a) distinct save-mode UX; (b) fork MUST uniquify filename; (c) optional upsert guard on `sprk_graphitemid` | **R7-DESIGN** — (a)+(b) are exactly UC-2 (save dropdown) + UC-3 (name prompt on create). **(c) upsert guard is NOT in the design** → R7-ADD? (thin BFF change; closes the data-hygiene hole for ALL doors, not just the fork) |
| D1-hygiene | Delete the 5 duplicate `sprk_document` records on dev (keep newest) | one-time data cleanup | R7-ADD? (or just do it manually — 10 minutes) |
| **D2** | Curly quotes render as digit `2` in AI-SUGGESTED draft text (`2Affiliate2`). Editor/save path exonerated — corruption is in the AI-suggestion insertion pipeline (or LLM echo of mangled extraction) | suggestion pipeline (NOT `Services/Compose`) | **OTHER-OWNER** — Assistant/suggestion pipeline (assistant-enhancements-r3 or AI-platform surface). NOT in R7 design and R7 is explicitly "not an AI-capability project" |
| **D3** | "Suggested edit couldn't be placed — wording differs" | expected consequence of D2 (mangled text no longer matches document) | OTHER-OWNER — re-test after D2; no independent fix expected |
| **D4** | "Restore from Source" blanks the page and asks for a new upload | not root-caused; likely mount-state reset regression on the transient/upload path | **R7-ADD?** — Compose-surface repro+fix, small; natural fit alongside R7's save-path UX work |
| **D5** | Page borders overrun / page-break flow in Word output | known consequence of `section-break-flattened ×6` (hard-tier accept-flatten, warned loudly) | → folded into **C. fidelity wideners** (section-break survival) |
| **D6** | No saving-progress / saved indicator | small UX | **R7-ADD?** — trivially adjacent to UC-2/UC-4 (a save dropdown + autosave NEED a visible save-state indicator; arguably implied by the design but not written) |
| **D7** | No "Add Comment" toolbar affordance (comment round-trip machinery itself shipped + seam-proven in 024/026; missing UI entry point blocks live UAT of it) | toolbar affordance + wire to existing machinery | **R7-ADD?** — editor-toolbar surface, same fault line as UC-2/UC-5 |
| **D8** | Compose tab → "Blank page" mounts NON-editable; "Open template" editable. Both use the SAME `mountBornInEditor` path; only the seed differs (`'<p></p>'` vs heading+para). Empty-seed-specific; suspects at `ComposeEditor.tsx:2252`/`2155` (setContent no-op against identical creation content) | small client fix once reproduced | **R7-DESIGN-ADJACENT** — UC-1 re-points "Open template" to the Quick Start Templates tab, but "Blank page" REMAINS a direct mount → the defect still needs the fix. Make it explicit in R7 scope |
| **D9** | Assistant pane transcript viewport clipped (dead space + mid-row clip) in the "Open in Compose" Xrm-dialog host; fine on the full-page surface. Same `ThreePaneShell` both hosts → host-dependent broken flex height chain in the `ConversationPane`→`SprkChat` subtree | flex-chain fix (client-only) | **OTHER-OWNER** — handed to assistant-enhancements-r3 with a full diagnosis doc: `projects/spaarkeai-assistant-enhancements-r3/notes/assistant-viewport-clipping-open-in-compose-handoff.md`. Falls back to R7 only if r3 declines |

## B. Engineering follow-ups ledger (accumulated across R6 tasks; all LOW/hardening)

| Item | Origin | Disposition |
|---|---|---|
| If-Match (ETag) on apply-template replace — TOCTOU vs a sibling tab saving concurrently | 032 review | R7-ADD? (thin server hardening; R7 touches the same apply path via UC-1) |
| ApiError-typed 404 branch in `handleApplyTemplate` (dead `response.ok` idiom) | 032 review | R7-ADD? (same file R7 rewires for UC-1 — cheap drive-by) |
| 051 version-history viewer: `window.open` popup-blocker fallback · 60s blob revoke · index-0 "Current" badge | 051 review | R7-ADD? (small UX polish batch on the version list — pairs with UC-2 "Save new version" mental model) |
| `nda-interrupted-clauses.docx` paraId regeneration (fixture carries spec-invalid paraIds ≥ 0x80000000) | 027/060 | R7-ADD? (test-fixture hygiene, tiny) |
| Corpus-manifest §3 consumer rows (document which suites consume which corpus rows) | 033/060 | R7-ADD? (docs-only) |
| #690 FidelityGate double-run dedup in CI (open PR overlap) | 061 | OTHER-OWNER (CI; coordinate with PR #690 owner) |
| Flaky `ComposeServiceCreateOnSaveTests` — FakeTimeProvider fix | 013/042 runs | R7-ADD? (test-only; the flake pollutes every Compose suite run) |
| Baseline set-`Contains` loses multiplicity at 033 seam `:306` (multiset diff would close a duplicate-paragraph mask; other oracles compensate) | 033 review | WAIT-SIGNAL (accepted; revisit only if a duplicate-paragraph regression escapes) |
| Graft helper `InsertAt(...,0)` would misplace `commentRangeStart` if the target paragraph ever gains a `pPr` | 033 review | WAIT-SIGNAL (accepted with comment in code) |
| LOW-9 — ~4 concurrent in-memory PDF copies on the intake leg (bounded by MaxFileSizeBytes) | 040 review | WAIT-SIGNAL (perf; revisit if large-PDF intake becomes hot) |
| LOW-10 — facade null-boundary cause-collapsing (circuit-open vs timeout vs corrupt → one message); discriminated facade result | 040 review | R7-ADD? (small; materially improves PDF-intake supportability) |
| A-LOW-2 — `pdf-intake-*` facts re-surface on repeated op-log saves (ACCEPTED-HONEST: each persisted artifact embodies the loss; banner dismissible) | 042 review | WAIT-SIGNAL (accepted by design; revisit only on user complaint) |
| Pre-existing failing jest suites (4× ComposeWorkspace "Element type is invalid" + `stepOperationInterceptor`) — proven pre-existing via stash bisect | 041/042 runs | R7-ADD? (owning-project fix candidate — R7 IS the next owning project for this surface) |

## C. Fidelity-widener backlog (render-on-save canonical model — all degrade LOUDLY today, per design principle)

Priority evidence from the REAL Corteva NDA UAT (2026-08-06 warning volumes):

| Widener | Evidence / origin | Disposition |
|---|---|---|
| **Indentation survival** (`indentation-dropped ×84`) | UAT volume — front of queue | R7-ADD? or FAST-FOLLOW fidelity project |
| **Paragraph-style survival** (`paragraph-style-flattened ×85`) | UAT volume — front of queue | same |
| Section-break survival (`section-break-flattened ×6`) — **is D5** (page borders/page-break flow) | UAT + D5 | same |
| Tab survival (`tab-flattened ×5`) · line-break (×5) · heading-direct-numbering (×4) · drawing/embedded (×5) | UAT volumes | same (lower) |
| Custom-style-linked numbering | 020 review (tagged "020-R7" in the ledger) | same |
| Localized heading ids (non-English style ids) | 011-P8 | same |
| `hMerge`/`tblLayout` typed carry | 022-F2 | same |
| Bookmarks + internal links | 024 | same |
| Typed move-revisions + table-revision carry | 025 | same |
| `pageBreakBefore` tri-state | 023-F2 | same |
| Field-result box text + SmartArt doc note | 026-F4 | same |

> **Review question for R7**: the R7 design is deliberately UX-only. If the fidelity wideners are NOT
> pulled into R7, they need a named home (a "compose-fidelity-wideners-r1" fast-follow) or they will rot
> as ledger entries. The ×84/×85 volumes on a routine NDA argue for scheduling soon.

## D. Deliberately deferred SCOPE (spec-level; each is its own project when scheduled)

| Item | Contract | Disposition |
|---|---|---|
| **PDF export** (docx → PDF out) via **headless-LibreOffice sidecar** — separate process, NEVER linked into the BFF (NFR-02) | Blocked on 2 human licensing sign-offs: (a) AGPL-3.0 "as a separate service" ambiguity; (b) Syncfusion "Community License" free-but-not-permissive. Graph `format=pdf` render spike is the noted alternative | FAST-FOLLOW (own project; not R7 — R7 design explicitly excludes it) |
| **Version restore & branch-from** (R6 shipped read-only open of prior versions) | new write semantics on the version surface | FAST-FOLLOW. NOTE: R7's UC-2 "Save new version" language should stay consistent with this future surface |
| **Page/line pagination** (WS-5, R4.5-deferred) — no page/line claim ships | honest page/line requires a layout engine | FAST-FOLLOW (tied to the PDF-export/LibreOffice decision — same engine could serve both) |
| **Owner worst-offender corpus rows 4–8** (live redline doc; table-heavy nested/merged; OOXML fields/content-controls; real multi-level numbering; multi-section headers/footers) | strengthens the FR-08 fidelity gate | OPEN — operator to supply documents (no code) |
| **Corteva NDA → corpus row 4** | real signed agreement currently UNTRACKED at `projects/spaarkeai-compose-r6/notes/` (worktree `info/exclude`); PERFECT worst-offender candidate | OPEN — needs operator **confidentiality sign-off** before it can be committed (R6 090 decision point; carries to R7 if unresolved) |

## E. Architecture cleanups on a telemetry trigger (do NOT schedule — watch)

| Item | Trigger | Action when fired |
|---|---|---|
| Delete the transitional op-log save path + `ComposeShadowPatchEngine` + `ComposeBaselineParaIdStamper` count-gate | the `TRANSITIONAL op-log save shape` Warning (ComposeService.SaveAsync, ContentModel-null path) decays to zero in dashboards | removal task (the ADR-049 amendment already anticipates it). **NEVER delete `docxBridge.ts`** regardless |
| Save latency on very large documents | post-save re-projection (`BuildContentModel` on persisted bytes) runs inside the save request — watch p95 | move re-projection out-of-band if it becomes material |
| Old-client-on-new-server comment drops (`comments-ignored`) | only within a future deploy window | expected + bounded by the atomic BFF+`sprk_spaarkeai` window; no action |

---

## Suggested R7 review pass (operator)

1. Confirm the **R7-DESIGN** rows are truly covered by design.md as written (D1a/D1b ↔ UC-2/UC-3; D8 ↔ UC-1 — note D8 needs the *Blank page* fix spelled out, not just the template re-point).
2. Accept/reject each **R7-ADD?** row — the natural adds by cohesion: D1c upsert guard, D4, D6, D7, the apply-template + version-viewer polish batch, FakeTimeProvider flake, pre-existing jest suites, LOW-10.
3. Decide the **fidelity-wideners home**: in R7 (breaks its UX-only framing) vs a named fast-follow project.
4. Sign off or defer the two OPEN operator items: Corteva corpus row-4 confidentiality; worst-offender corpus supply.
5. Leave **WAIT-SIGNAL** and **E** rows alone — they are deliberately parked with named triggers.
