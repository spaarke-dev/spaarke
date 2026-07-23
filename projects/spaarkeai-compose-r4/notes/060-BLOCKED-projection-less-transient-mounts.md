# Task 060 — BLOCKED: residual `mammoth` removal gated on projection-less transient mounts

> **Status**: 🛑 BLOCKED (escalation per root CLAUDE.md §6) — no deletion performed.
> **Date**: 2026-07-23
> **Task**: `tasks/060-hard-replace-cutover.poml` (FR-12 hard-replace cutover completion)
> **Rigor**: FULL · prescriptive step mode · opus/high
> **Gate state at entry**: 006 Phase-0 gate 🟢 GREEN · 032 ✅ · 036 ✅ · 038 committed (a5368d5b5). All prerequisites GREEN — the block is NOT a gate/prereq failure.

## Verdict

Task 060 Step 1 ("confirm NO projection-less mount remains — the projection builder must be the
only mapper") **FAILS**. Multiple reachable Compose editor mounts still supply editable `docxBytes`
with `projection: null` and therefore render **only** via the client `mammoth` fallback
(`docxBridge.docxToTipTapHtml`). Removing `mammoth` would leave those load paths with **no mapper**
(blank/broken editor for any transient `.docx`). This is the exact BLOCKED condition named by:

- POML `<escalation><trigger>`: *"If ANY projection-less mount still exists, mammoth removal is
  BLOCKED — do NOT delete mammoth; surface the conflict per root §6."*
- POML acceptance-criterion (negative): *"if any projection-less mount still exists, mammoth removal
  is NOT performed — the task escalates as BLOCKED per root §6."*
- Spec **FR-12**: *"Residual `mammoth` is removed once no projection-less mount remains."*

Firing this trigger is the **prescribed, correct** outcome — not a failure. No `mammoth` / `docxBridge`
/ `ComposeEditor` deletion was performed. Steps 2–6 were not executed.

## Evidence (file:line ground truth)

The server-side projection is the sole mapper **only for stored-document Loads**. Transient mounts
(Browse / Assistant-upload / "Open in Compose") have **no server projection** and fall back to mammoth:

| Mount source | Dispatch | projection | Mapper | Reachable? |
|---|---|---|---|---|
| Stored-document Load | `loadSucceeded` (`ComposeWorkspace.types.ts:265`) | server projection (null only on older BFF) | projection.html | yes |
| **Browse local `.docx`** | `mountTransient` (`ComposeWorkspace.tsx:1645`, callback `handleBrowseFileSelected`) | `projection: null` (`ComposeWorkspace.types.ts:337`) | **mammoth** | **yes — FR-01/task 010, wired file picker** |
| **Assistant-uploaded file** | `mountTransient` (`ComposeWorkspace.tsx:1937`) | `projection: null` | **mammoth** | **yes — `POST /api/compose/upload` returns `{content,fileName,size}` only, no projection (`ComposeWorkspace.tsx:1918`)** |
| AI-draft / blank seed | `mountDraftHtml` (`ComposeWorkspace.tsx:1688`) | n/a — uses `seedHtml`→`initialHtml` | setContent(html), no mammoth | yes (already mammoth-free) |

- **`ComposeEditor.tsx:1667`** — the mount effect: `if (projection) { … projection.html … return; }` else
  falls through to **`ComposeEditor.tsx:1718` `docxToTipTapHtml(docxBytes)`** (the mammoth fallback).
- **`ComposeEditor.tsx:1665`** (comment, current behavior): *"A transient/browse mount (no projection)
  still uses the mammoth fallback below."*
- **`ComposeWorkspace.types.ts:336` / `:360`** — `mountTransient` / `mountDraftHtml` hardcode
  `projection: null` with the note *"No server round-trip → no projection; the editor falls back to
  the client mammoth convert."*

The `POST /api/compose/upload` endpoint returns bytes with **no projection** field, so even the
server-mediated upload path cannot supply a projection to the transient mount. Browse is purely
client-side (ADR-040, no BFF round-trip) and can never have a server projection under the current
design.

## Why this is not "hack-around-able" within task 060

Task 060 is a pure **deletion** task ("no new surface, no `<justification>`"). Making transient mounts
projection-backed requires **new wiring** (e.g. extend `/api/compose/upload` to return a
`ComposeServerProjection`, and route Browse/Open-in-Compose bytes through the projection builder before
mount) — that is net-new code, out of scope for a deletion task, and would itself be a BFF-touching
change requiring its own Placement Justification + seam slice. Forcing the mammoth deletion now would
break FR-01 Browse and the Assistant-upload → revise flow.

## Recommended resolution (for owner / main session)

Add a predecessor task (Phase 6, before 060) that makes the projection builder the sole mapper for
**all** editable docx mounts:

1. Extend `POST /api/compose/upload` (and the Browse/Open-in-Compose client paths) to produce a
   `ComposeServerProjection` for transient bytes — same `ComposeDocxProjectionBuilder` used by Load.
   (Browse is client-only today; it would need a lightweight projection round-trip or a client-side
   projection path — an architecture decision for the owner.)
2. Thread that projection through `mountTransient` (drop the hardcoded `projection: null`).
3. Re-run 060 Step 1: with every editable mount projection-backed, the mammoth fallback becomes dead
   code and `docxToTipTapHtml` + the `mammoth` dependency can be removed safely.

Alternatively, the owner may consciously **retain** the mammoth fallback for transient/Browse mounts
as a documented R4 exception (Browse-local-file has no server to project it), in which case FR-12's
"mammoth is gone" success criterion needs an amendment to "mammoth removed from the stored-Load path;
retained solely as the client transient/Browse mapper." That is a §6.5 Path-A/B decision for the owner.

## Scope note — unrelated `mammoth` usages (NOT in 060 scope regardless)

`mammoth` also serves two unrelated subsystems that task 060 never touches:
- `Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts` — chat attachment text
  extraction (CHAT-ATTACHMENT-POLICY.md).
- `src/solutions/Notepad/**` (`EntityCreationService`).

These are legitimate live dependencies outside the Compose write path; a "repo-wide zero mammoth"
reading must be scoped to the Compose surface.

## Legacy grep-audit (informational — the retired-writer half is already clean)

The retired-writer half of the audit (adjustment #2: live-refs must be zero; retrospective comments
are acceptable) is already satisfied by tasks 032/036:
- `DocxAnnotationWriter`, `ComposeParagraphRedlineSynthesizer`, `LocateTarget`, `collectEditedParagraphs`
  — **zero live-code references**; only retrospective `//`-comments / doc-strings / test-narrative
  mentions remain (acceptable per adjustment #2). Full audit deferred until the mammoth block is resolved
  and 060 resumes.
