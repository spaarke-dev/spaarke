# Spaarke Compose R5 — Editing Completeness (deferred scope)

> **Portfolio**: [Project #695](https://github.com/spaarke-dev/spaarke/issues/695) · Epic [#421 SPAARKE AI](https://github.com/spaarke-dev/spaarke/issues/421) · [Board #2](https://github.com/users/spaarke-dev/projects/2) — registered 2026-07-28 via `/devops-project-register`.

> **Status**: 📋 PLANNED — deferred scope captured during R4 (`spaarkeai-compose-r4`), 2026-07-23. **Revised 2026-07-28**: read/reference fidelity (incl. G6) carved out into `spaarkeai-compose-fidelity-r4.5` — see the ⚠️ note below. **R4 is now merged to master.**
> **Origin**: R4 (Shadow Document Architecture) delivered the engine + save-path foundation and ships
> **error-free with documented functional limits**. R5 implements those limits. The owner decision (2026-07-23):
> *"For R4 keep the two byte-authors separate and ship with no errors, albeit known functional limits; defer
> the limits to R5 and fully document them here."*
> **⚠️ R5 IS NOT YET A REAL PROJECT.** This is a **backlog capture only** — no `spec.md`, no tasks, no worktree, not
> piped. Everything R4/UAT "defers to R5" is *documented here but not scheduled or funded*. **This README is the
> authoritative, complete deferred-scope backlog (G1–G12 + REQ-1/2/3)** — when R5 is initiated it MUST pick up the full
> set. **To make R5 real:** run `/design-to-spec` → `/project-pipeline` on this doc **after R4.5 lands** (R5 depends on
> R4.5 — see the coordination note `notes/COORDINATION-with-r4.5.md`). Until then, treat any "→ R5" as *tracked, not
> in-flight*. **Completeness audit 2026-07-28:** all deferrals from R4 + the 2026-07-23/07-28 UAT rounds are captured as
> G1–G12; read-fidelity items moved to R4.5; Word-comment export issues route to the NDA/agreements feature (not R5).
>
> **⚠️ 2026-07-28 — READ/REFERENCE fidelity split into a priority interstitial project (`spaarkeai-compose-fidelity-r4.5`).**
> Dev UAT of a real NDA showed the **read/reference** fidelity gaps (mammoth on upload, no computed numbering, no
> `paraId→section-number` reference) are load-bearing for a legal tool, and were carved out of R5 into
> **[`../spaarkeai-compose-fidelity-r4.5/design.md`](../spaarkeai-compose-fidelity-r4.5/design.md)** (worktree
> `spaarke-wt-spaarkeai-compose-fidelity-r4.5`, branch `work/spaarkeai-compose-fidelity-r4.5`, based on latest master).
>
> **Moved OUT of R5 → into R4.5:**
> - **G6** (transient-mount projection unification / mammoth removal) → R4.5 **WS-1**. *(Row below kept as a stub for traceability.)*
> - **Read-fidelity work** that was flagged "new" in the Dev-UAT section (verbatim text + silent-drop fixes for `w:sym`/`w:cr`,
>   deterministic clause/section/heading/list numbering, `paraId→legal-number` citation layer, page/line-numbering spike) → R4.5 **WS-2/3/4/5**.
>
> **STAYS in R5 (this doc):** G1, G2, G3, G4, G5, G7, G8, G9, G10 — all **edit / lifecycle / UX**, not read/reference fidelity.
>
> **The boundary:** R4.5 = *reading* a legal doc with perfect fidelity + making it *referenceable*; R5 = *editing* it with full
> formatting fidelity. **Dependency:** G3 (edit-path heading/list/alignment numbering) and G7 (transient-doc versioning) both
> **build on R4.5** (the numbering engine and the transient-mount projection respectively) — do R4.5 first.

---

## Why R5 exists

R4 replaced the Compose translation/save layer with the Shadow Document Architecture: one `ComposeShadowPatchEngine`
applies `(paraId, runIndex, offset)`-anchored operations as native Word tracked changes, with **zero text-search
in the write path** (invariant I-7). That foundation is done and correct. But the closed 10-operation schema does
not yet cover every construct a user can create in the editor, and the two save paths (clean-authoring renderer for
new docs; tracked-change engine for imported docs) don't yet give authored documents a clean lifecycle across
sessions. R4 ships by **guarding** the gaps (disabling unsupported controls, informing on paste, never losing data);
R5 **implements** them.

## The R4/R5 boundary (what R4 guards vs. what R5 builds)

R4 guardrails (shipped in R4 — see `spaarkeai-compose-r4` task 038): unsupported edit-path controls are disabled on
loaded docs, hyperlinks disabled in both modes, formatted-paste surfaces a non-blocking notice, and the op-log is
preserved across a rejected save so no batch is ever lost. **Result: no errors, no silent data loss — just features
that are visibly not-yet-available.** R5 removes those guards by implementing the features.

---

## R5 requirements (owner-stated, 2026-07-23)

**REQ 1 — Authored-doc lifecycle stays CLEAN.** A user starts from a blank editor; first save creates the SPE file
(clean, no track-changes — already works). The user reopens that document later and keeps editing, and their edits
remain CLEAN (not tracked changes), because it is their own original document.

**REQ 2 — Imported-doc edits are TRACKED.** When the editor is launched from an existing/uploaded `.docx`,
track-changes mode is ON. — ✅ Already MET in R4 (baseline).

**REQ 3 — Both modes support headings, lists, tables, hyperlinks** (as authored content for new docs; as applied
tracked edits for imported docs).
> **Split (2026-07-28):** the **READ** side of REQ 3 — rendering headings/lists/**numbering** faithfully when a doc is
> loaded/uploaded — moved to **R4.5** (WS-2/3). R5 owns the **EDIT** side — applying heading/list/table/hyperlink
> changes as tracked edits (G3/G4/G5). R5's edit-side numbering (G3) reuses R4.5's numbering engine.

---

## Gap ledger (code-grounded, sized) — the R5 backlog

Evidence gathered during R4 (2026-07-23). Sizes: S ≈ ≤1d, M ≈ 2–4d, L ≈ 1–2wk.

| # | Gap | Requirement | Path affected | Size | Notes |
|---|---|---|---|---|---|
| G1 | **Cross-session authored-vs-imported routing** — durable origin flag (Dataverse field or SPE metadata) written on create-on-save + returned by `LoadAsync`; client sends the clean payload for a reopened authored doc instead of an op-log. Today the *only* discriminator is "has an SPE id yet," so a reopened authored doc flips to the tracked path. | REQ 1 | Client + `ComposeService` | S–M | `ComposeWorkspace.tsx` `isTransientCreate`; `ComposeService.LoadAsync` returns no origin marker today. |
| G2 | **Clean (non-tracked) apply mode** — either re-author authored docs from the content model each save (fidelity-bounded by projection round-trip), or add a clean-apply flag to `ComposeShadowPatchEngine` that emits plain runs instead of `w:ins`/`w:del`. | REQ 1 | `ComposeShadowPatchEngine` | M–L | The real cost of REQ 1. Engine has no non-tracked apply branch today (`ApplyInsertText`/`WrapRunAsDeleted` always emit tracked). |
| G3 | **`setBlockAttr` applier in the engine** — implement the `StructuralOpNotYetImplemented` seam for `Style` (heading level), `ListOrdered`, `ListLevel`, `Alignment` as tracked paragraph-property changes (`w:pPrChange`). Client already emits `Alignment`; add heading/list emission in `classifyStep` (currently `defer-structural`). | REQ 3 | Engine + client interceptor | M | `ComposeShadowPatchEngine.cs` throws for all `setBlockAttr`. Alignment-only is S (already captured client-side). |
| G4 | **Table op** — new op type in the closed set + client capture (table steps currently `defer-structural`) + engine applier emitting tracked table structure (`w:tblPrChange`, row/cell `w:ins` tracking). | REQ 3 | Op schema + client + engine | L | The single hardest piece — tracked table changes in OOXML. |
| G5 | **Hyperlink support** — add `href` to `ComposeInlineRun` + emit `w:hyperlink` in `ComposeDocumentRenderer.BuildRun` (authored path, S–M); add a hyperlink op + `link` to `ComposeMarkType` + engine applier (edit path, M). | REQ 3 | Content model + renderer + op schema + engine | M–L | Renderer-side is the only REQ-3 clean-authoring gap; the rest is edit-path. |
| ~~G6~~ | **➡️ MOVED to R4.5 (WS-1) — Transient-mount projection unification.** Route Browse-local-`.docx`, Assistant-upload, and Open-in-Compose transient mounts through the projection builder + remove the client `mammoth` fallback. Now the foundation of the R4.5 legal read-fidelity project (it also structurally fixes the Assistant-upload duplicate-record proliferation V1 + the "uploaded-file edits don't track" gap T1). Full scope in [`../spaarkeai-compose-fidelity-r4.5/design.md`](../spaarkeai-compose-fidelity-r4.5/design.md) §WS-1. | — | — | — | **No longer an R5 item.** Row retained for traceability; see R4.5 for the live spec. |
| G7 | **Save-Version vs Save-New-Document control** — explicit Save dropdown in the Compose editor: **"Save Version"** (update the existing `sprk_document` + SPE item — the default) vs **"Save New Document"** (deliberately fork a new record). Today every save on a *transient/uploaded* doc mints a new `sprk_document` (UAT produced 8 duplicate records). R4 task **039** fixes the born-in-editor case (subsequent saves stay on the same-item replace path); **R5 G7 adds the user-facing choice + covers the Assistant-upload/transient path** (whose stable identity comes from R4.5 WS-1). | Versioning UX | Client (toolbar Save button → split-button) + `ComposeService` create-vs-replace routing | S–M | R4 039 gives the sane default (update-in-place after first save for born-in-editor); G7 is the explicit dropdown + the upload-path coverage. **Depends on R4.5 WS-1** (transient-mount projection) for the transient/upload doc identity — sequence after R4.5. |
| G8 | **External-change refresh + remount banner** — detect when the SPE `.docx` changed outside Compose (edited via Open-in-Web / Open-in-Desktop) and **remount** the current projection, surfacing a non-blocking banner: *"Document updated from document management system version"* (or similar). After a lock releases (web/desktop closed), a refresh remounts the file. | Concurrency UX | Client (mount lifecycle + banner) + wire existing `POST /api/compose/document/{id}/check-changes` + `spe-doc-changed` webhook | M | The detection endpoints already exist (`check-changes`, the `spe-doc-changed` webhook) but are **not wired** to a remount + banner UX. UAT 2026-07-23: the 423 lock message already works ("can't edit while open in web/desktop") — the missing piece is the auto-refresh/remount + notification after the external edit or lock release. |
| G9 | **Comment pane scroll-sync** — the right-hand Comments pane opens/collapses and **scrolls its comments in line with** the redline/comment anchor positions in the document (position-linked, not just a flat list). | Comments UX | Client (`ComposeCommentThread*` + editor scroll coordination) | S–M | UAT 2026-07-23: comments render as highlighted areas + a pane, but the pane does not scroll-track the in-document anchor positions. |
| G10 | **Document Profile re-run on Compose save (+ on reload)** — when a Compose **save writes the edited document back to the `sprk_document`/SPE record**, the Dataverse **Document Profile must re-run** so downstream analysis/search reflects the new content. Fire the re-trigger on the Compose→Document save path (BFF hook / background process), plus on reload (onload event) and a manual **"Refresh Profile"** button. | Dataverse profiling + Compose save hook | `ComposeService` save path (fire profile re-trigger) + Dataverse form script/process | M | **IN R5 scope (owner, 2026-07-28):** the Compose save is exactly when the profile goes stale, so the re-trigger belongs with Compose — include unless it proves to add significant complexity. Today Compose changes are not consistently re-profiled (web/desktop changes are). Reuses **R4.5 WS-4** `paraId→legal-number` reference so profile citations are precise. |
| G11 | **Track-changes toggle keeps pre-existing redlines visible** — when the user toggles their own free-typed-edit overlay **off**, imported/AI redlines (first-class marks) should stay visibly rendered. The toggle is display-only and does NOT remove them (no data loss — UAT BUG-B), but hiding the overlay reads as "redlines lost." Small view tweak; no persistence change. | UX clarity | Client (`TrackChangesExtension.ts` + toolbar) | S | From UAT 2026-07-23 BUG-B (confirmed **not** data loss). Given a G-number 2026-07-28 so it isn't orphaned. |
| G12 | **Accept/reject imported tracked changes (ET-2 reconciliation)** — accepting a **pre-existing (imported Word) tracked change** and saving fails with `TrackedChangeReconciliationUnsupported` (422): the engine cannot edit/split a run wrapped in `w:ins`/`w:del` (the task-030 boundary). Add first-class **`acceptRevision`/`rejectRevision`** ops addressed by the revision **id** (not offset), with engine handlers that resolve the revision natively (accept-ins = strip the `w:ins` wrapper, keep the run; accept-del = remove the run; reject = inverse). Also fixes the imported-**deletion** end-of-paragraph re-anchoring sub-bug (`importedRevisions.ts` applyDeletion). | Editing (tracked-change reconciliation) | Op schema (`ComposeOperation.cs` / `compose-operations.ts`) + `stepOperationInterceptor.ts` + `ComposeShadowPatchEngine.cs` + `importedRevisions.ts` | M–L | **UAT 2026-07-28 — accept-then-save error.** R4 **guards** this (op-log preserved, clean 422, "reload & reapply — nothing overwritten" — **no data loss**); it is the deferred **ET-2** gap, now a concrete must-do (previously buried as "optional item 8"). Two triggers: *typing onto* a tracked change AND *accepting* one. Server logs the exact `ex.Kind` (`ComposeEndpoints.cs:1336`) to confirm per-occurrence. |

**Architecture note (do NOT re-litigate in R5 without cause):** none of G1–G5 requires merging the two byte-authors.
Keep the create/edit split (renderer authors clean new docs; engine applies tracked edits). Collapsing to a single
public byte-author (relocating the renderer behind the engine) is an *optional, cheap, cosmetic* refactor that
satisfies I-5 literally; forcing whole-document origination through the op log is expensive and arguably worse
(the op vocabulary is for deltas, not origination). Decided in R4 (2026-07-23). See
`../spaarkeai-compose-r4/notes/task-037-born-in-editor-unification-block.md` and `owner-decisions-036-037.md`.

## Known failure modes R4 guards (the "why" behind each R5 feature)

From the R4 zero-error audit (2026-07-23). Each is *guarded* (disabled/informed/loss-proof) in R4 and *resolved* by
the R5 gap noted:

- **ET-1 Alignment edit on a loaded doc** → engine throws `StructuralOpNotYetImplemented` → 422. Guarded by disabling
  alignment on loaded docs. Resolved by **G3**.
- **SDL-1/2/3 Heading / list / table change on a loaded doc** → `defer-structural`, silently dropped. Guarded by
  disabling those controls on loaded docs. Resolved by **G3** (heading/list) + **G4** (table).
- **SDL-4/5 Hyperlink (loaded or born-in-editor)** → `unrepresentable` / no content-model href → silently lost.
  Guarded by disabling the hyperlink control in both modes. Resolved by **G5**.
- **REQ 1 reopened authored doc shows tracked changes** → the authored-doc UX wart. Resolved by **G1 + G2**.
- **ET-2 Reconcile against a pre-existing tracked change** (two triggers: *typing onto* one, OR *accepting* one then
  saving) → `TrackedChangeReconciliationUnsupported` → 422. Guarded by the R4 op-log-preservation fix (no batch loss,
  clean 422, reload-and-reapply). **Now tracked as G12** (accept/reject-revision op pair) — promoted from "optional."

## Dev UAT findings (2026-07-23) — R4 fixes vs. R5 defers

Full raw feedback + triage: [`../spaarkeai-compose-r4/notes/uat-feedback-2026-07-23.md`](../spaarkeai-compose-r4/notes/uat-feedback-2026-07-23.md).
Two UAT rounds on the dev deploy. **Headline: no crashes/failed saves in the primary stored-doc flow; the one hard
error (born-in-editor 2nd save) is fixed in R4 task 039.**

### Fixed in R4 (task 039 — UAT remediation)
- **BUG A (hard save error) — born-in-editor 2nd in-session save failed** with *"Provide the retained-original
  'content' bytes, or … 'baselineVersionId' …"*. Root cause: a born-blank doc's 2nd save took the op-log/replace path
  with **no valid baseline** (`docxBytes` null; create-on-save returns the drive-**item** id, not a real version id —
  `ComposeService.cs:845`). **Fix (039)**: born-in-editor docs re-author via `{contentModel}` on every in-session save
  (server accepts `contentModel` on the replace endpoint `ComposeEndpoints.cs:~1141`; client `triggerSave` sends
  `contentModel` when born-in-editor). **Also fixes V1 duplicate-records for the born-blank case** (stays on the
  same-item replace path). REQ-1-correct (authored doc stays clean in-session; redlines resume on reopen).
- **UX polish (039)**: Track Changes → icon-only toggle; "Search for Document" → **"Open Document"**; Word menu →
  vertical + labels ("Open web" / "Open desktop"); confirm no dead "Push to Word" control (036 retired the endpoint).

### Not a bug (clarified in UAT)
- **BUG B — "redlines lost when Track Changes turned off"**: **visual only, NOT data loss.** The toggle is a
  display-only ProseMirror decoration flip (`ComposeEditor.tsx:1859-1865` → `TrackChangesExtension.ts:157-161`);
  imported/AI redlines are first-class schema marks the overlay never touches, and nothing is stripped on save. What
  disappears is the overlay of the *user's own free-typed edits*; toggling back on restores it. **R5 view
  tweak → now tracked as G11**: keep pre-existing redlines visibly rendered even when the toggle is off (clarity).
  No persistence fix needed.

### Deferred to R5 (this doc) — updated 2026-07-28
- **V1 full versioning UX** → **G7** (Save-Version / Save-New dropdown; the transient/upload identity it needs comes from **R4.5 WS-1**).
- **T1 uploaded-file edits don't track** (Assistant-upload = transient/renderer clean path) → **➡️ R4.5 WS-1** (transient-mount
  projection unification) — no longer R5. This is the same structural fix as the upload read-fidelity work.
- **External-change refresh + banner** → **G8**. **Comment scroll-sync** → **G9**. **Profile re-run** → **G10**.

## Suggested R5 sequencing

> **Prerequisite: R4.5 lands first** (transient-mount projection + numbering engine). G3 and G7 depend on it.

1. **G3 alignment** (S, unblocks the most common edit-path error class) → **G3 heading/list** (M) — reuses R4.5's numbering engine.
2. **G1 + G2** (REQ 1 — authored-doc clean lifecycle) — the highest user-visible value; pairs with **G7** (versioning UX).
   *(The transient-mount unification the UAT V1/T1 findings depend on is R4.5 WS-1, not R5.)*
3. **G8** (external-change refresh banner) — concurrency UX, detection endpoints already exist.
4. **G5 hyperlinks** (both paths).
5. **G9** (comment scroll-sync) + **G11** (track-changes-off redline visibility).
6. **G4 tables** (L, hardest — schedule last / its own sub-effort).
7. **G10** (Document Profile re-run on Compose save) — **in R5 per owner (2026-07-28)**: fire the profile re-trigger on the
   Compose→Document save path so analysis reflects edits; reuses R4.5 WS-4's `paraId→legal-number` reference for precise citations.
8. **G12** (accept/reject tracked-change reconciliation) — **no longer optional**: UAT 2026-07-28 hit it via accept-then-save.
   The op-schema change (new revision ops) pairs it with **G3/G4/G5** (also op-schema) — sequence in the same engine wave.

Each removes its R4 guard (re-enables the control) as its exit criterion, with a seam slice proving the construct
round-trips and the no-error/no-silent-loss invariant still holds.
