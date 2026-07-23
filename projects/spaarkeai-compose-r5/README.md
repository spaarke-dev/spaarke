# Spaarke Compose R5 — Editing Completeness (deferred scope)

> **Status**: 📋 PLANNED — deferred scope captured during R4 (`spaarkeai-compose-r4`), 2026-07-23.
> **Origin**: R4 (Shadow Document Architecture) delivered the engine + save-path foundation and ships
> **error-free with documented functional limits**. R5 implements those limits. The owner decision (2026-07-23):
> *"For R4 keep the two byte-authors separate and ship with no errors, albeit known functional limits; defer
> the limits to R5 and fully document them here."*
> **Not yet piped** — this is a scoping/requirements capture, the seed for a future `/project-pipeline` run.

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
| G6 | **Transient-mount projection unification** — route Browse-local-`.docx`, Assistant-upload, and Open-in-Compose transient mounts through the projection builder (extend `POST /api/compose/upload` to return a `ComposeServerProjection`; give Browse a projection path — Browse is client-only per ADR-040, so this is an architecture decision) so the high-fidelity projection is the sole mapper. Then remove the client `mammoth` fallback (`docxToTipTapHtml` in `docxBridge.ts` + the `ComposeEditor.tsx` fallback branch). | Fidelity / FR-12 completion | Client + `ComposeService`/upload endpoint | M | Blocks R4 task 060's mammoth removal (retained as a cited §6.5 Path-A exception in R4). Today transient Browse/upload docs render + save via the lossy mammoth→contentModel path — complex formatting can degrade on save in those secondary flows (pre-existing, not the R4 primary stored-doc workflow). See `../spaarkeai-compose-r4/notes/060-BLOCKED-projection-less-transient-mounts.md`. NOTE: `mammoth` also serves SprkChat + Notepad (out of scope). |

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

## Suggested R5 sequencing

1. **G3 alignment** (S, unblocks the most common edit-path error class) → **G3 heading/list** (M).
2. **G1 + G2** (REQ 1 — authored-doc clean lifecycle) — the highest user-visible value.
3. **G5 hyperlinks** (both paths).
4. **G4 tables** (L, hardest — schedule last / its own sub-effort).
5. Optional: **ET-2 tracked-change reconciliation** for imported redlined docs.

Each removes its R4 guard (re-enables the control) as its exit criterion, with a seam slice proving the construct
round-trips and the no-error/no-silent-loss invariant still holds.
