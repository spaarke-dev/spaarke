# Spaarke Compose R5 — Editing Completeness (deferred scope)

> **Status**: 📋 PLANNED — deferred scope captured during R4 (`spaarkeai-compose-r4`), 2026-07-23.
> **Origin**: R4 (Shadow Document Architecture) delivered the engine + save-path foundation and ships
> **error-free with documented functional limits**. R5 implements those limits. The owner decision (2026-07-23):
> *"For R4 keep the two byte-authors separate and ship with no errors, albeit known functional limits; defer
> the limits to R5 and fully document them here."*
> **Not yet piped** — this is a scoping/requirements capture, the seed for a future `/project-pipeline` run.
>
> **⚠️ 2026-07-28 — G6 + numbering pulled into a priority interstitial project.** Dev UAT of a real NDA showed
> the **read/reference** fidelity gaps (mammoth on upload, no computed numbering, no `paraId→section-number`
> reference) are load-bearing for a legal tool. These were split out into **`spaarkeai-compose-fidelity-r4.5`**
> ([`../spaarkeai-compose-fidelity-r4.5/design.md`](../spaarkeai-compose-fidelity-r4.5/design.md)), which
> **absorbs G6** (transient-mount projection unification / mammoth removal) plus new read-fidelity work
> (verbatim text + silent-drop fixes, deterministic clause/section/heading numbering, a `paraId→legal-number`
> citation layer, and a page/line-numbering spike). The **remaining** R5 items below (G1/G2/G3/G4/G5/G7/G8/G9/G10)
> stay here — they are **edit / lifecycle / UX**, not read/reference fidelity. Note G3 (edit-path
> heading/list/alignment numbering) will build on R4.5's numbering engine — coupling flagged in the R4.5 design.

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
| G6 | **Transient-mount projection unification** — route Browse-local-`.docx`, Assistant-upload, and Open-in-Compose transient mounts through the projection builder (extend `POST /api/compose/upload` to return a `ComposeServerProjection`; give Browse a projection path — Browse is client-only per ADR-040, so this is an architecture decision) so the high-fidelity projection is the sole mapper. Then remove the client `mammoth` fallback (`docxToTipTapHtml` in `docxBridge.ts` + the `ComposeEditor.tsx` fallback branch). | Fidelity / FR-12 completion | Client + `ComposeService`/upload endpoint | M | Blocks R4 task 060's mammoth removal (retained as a cited §6.5 Path-A exception in R4). Today transient Browse/upload docs render + save via the lossy mammoth→contentModel path — complex formatting can degrade on save in those secondary flows (pre-existing, not the R4 primary stored-doc workflow). **UAT 2026-07-23**: this is also the STRUCTURAL fix for the Assistant-upload duplicate-record proliferation (V1) AND the "uploaded-file edits don't track" gap (T1) — routing the upload path through the projection/stored-doc model gives it a stable doc identity (updates instead of create-new) and a baseline (tracked edits). See `../spaarkeai-compose-r4/notes/060-BLOCKED-projection-less-transient-mounts.md`. NOTE: `mammoth` also serves SprkChat + Notepad (out of scope). |
| G7 | **Save-Version vs Save-New-Document control** — explicit Save dropdown in the Compose editor: **"Save Version"** (update the existing `sprk_document` + SPE item — the default) vs **"Save New Document"** (deliberately fork a new record). Today every save on a *transient/uploaded* doc mints a new `sprk_document` (UAT produced 8 duplicate records). R4 task **039** fixes the born-in-editor case (subsequent saves stay on the same-item replace path); **R5 G7 adds the user-facing choice + covers the Assistant-upload/transient path** (fully resolved with G6). | Versioning UX | Client (toolbar Save button → split-button) + `ComposeService` create-vs-replace routing | S–M | R4 039 gives the sane default (update-in-place after first save for born-in-editor); G7 is the explicit dropdown + the upload-path coverage. Depends on G6 for the transient/upload identity. |
| G8 | **External-change refresh + remount banner** — detect when the SPE `.docx` changed outside Compose (edited via Open-in-Web / Open-in-Desktop) and **remount** the current projection, surfacing a non-blocking banner: *"Document updated from document management system version"* (or similar). After a lock releases (web/desktop closed), a refresh remounts the file. | Concurrency UX | Client (mount lifecycle + banner) + wire existing `POST /api/compose/document/{id}/check-changes` + `spe-doc-changed` webhook | M | The detection endpoints already exist (`check-changes`, the `spe-doc-changed` webhook) but are **not wired** to a remount + banner UX. UAT 2026-07-23: the 423 lock message already works ("can't edit while open in web/desktop") — the missing piece is the auto-refresh/remount + notification after the external edit or lock release. |
| G9 | **Comment pane scroll-sync** — the right-hand Comments pane opens/collapses and **scrolls its comments in line with** the redline/comment anchor positions in the document (position-linked, not just a flat list). | Comments UX | Client (`ComposeCommentThread*` + editor scroll coordination) | S–M | UAT 2026-07-23: comments render as highlighted areas + a pane, but the pane does not scroll-track the in-document anchor positions. |
| G10 | **Document Profile re-run on (re)load** — ensure the Dataverse Document Profile re-runs when a document is loaded/updated (via a background process, a `.js` onload event, and/or a **"Refresh Profile"** button on the Document record). | Dataverse profiling integration | Dataverse (form script / process) + possibly BFF hook — **arguably a separate subsystem, not Compose core** | M | UAT 2026-07-23: the profile runs, but web/desktop changes are reflected on the record while Compose changes are not consistently re-profiled. Likely belongs to the document-profiling pipeline project rather than Compose R5 — flag for triage. |

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
- **ET-2 Typing onto a pre-existing tracked change in an imported doc** → `TrackedChangeReconciliationUnsupported`
  → 422. Guarded by the R4 op-log-preservation fix (no batch loss); full reconciliation is a separate R5 candidate.

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
  disappears is the overlay of the *user's own free-typed edits*; toggling back on restores it. **Optional R5 view
  tweak**: keep pre-existing redlines visibly rendered even when the toggle is off (clarity), tracked under G9-adjacent
  Comments/redline UX. No persistence fix needed.

### Deferred to R5 (this doc)
- **V1 full versioning UX** → **G7** (Save-Version / Save-New dropdown + Assistant-upload/transient coverage).
- **T1 uploaded-file edits don't track** (Assistant-upload = transient/renderer clean path) → **G6** (structural fix)
  + REQ-1/REQ-2.
- **External-change refresh + banner** → **G8**. **Comment scroll-sync** → **G9**. **Profile re-run** → **G10**.

## Suggested R5 sequencing

1. **G3 alignment** (S, unblocks the most common edit-path error class) → **G3 heading/list** (M).
2. **G1 + G2** (REQ 1 — authored-doc clean lifecycle) — the highest user-visible value; pairs with **G6 + G7**
   (transient-mount unification + versioning UX — together they resolve the UAT V1/T1 findings structurally).
3. **G8** (external-change refresh banner) — concurrency UX, detection endpoints already exist.
4. **G5 hyperlinks** (both paths).
5. **G9** (comment scroll-sync) + the optional track-changes-off redline-visibility tweak.
6. **G4 tables** (L, hardest — schedule last / its own sub-effort).
7. **G10** (Document Profile re-run) — triage first: likely belongs to the document-profiling pipeline, not Compose.
8. Optional: **ET-2 tracked-change reconciliation** for imported redlined docs.

Each removes its R4 guard (re-enables the control) as its exit criterion, with a seam slice proving the construct
round-trips and the no-error/no-silent-loss invariant still holds.
