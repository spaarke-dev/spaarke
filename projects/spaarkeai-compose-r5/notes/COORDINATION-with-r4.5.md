# R5 ↔ R4.5 Coordination Note

> **Authored**: 2026-07-28 (from a code-grounded collision analysis).
> **For**: whoever pipes / executes R5. Read this BEFORE opening any Compose PR.
> **Bottom line**: R4.5 and R5 are **aligned in direction, no fundamental design conflict**, but they **contend on two files** and R5 has **hard sequencing dependencies on R4.5**. **R4.5 must land first.**

## 1. Sequencing (binding)

**R4.5 lands before R5 starts coding.** R5 gaps that rebase onto R4.5 outputs:
- **G7** (versioning) → **R4.5 WS-1** (transient-mount projection gives the stable doc identity). HARD.
- **G1** (cross-session routing) → **R4.5 WS-1** — *the R5 README under-stated this*: G1's `isTransientCreate` discriminator is the exact block WS-1 rewrites. G1 must re-base on the post-WS-1 world.
- **G3** (setBlockAttr numbering) → **R4.5 WS-3** (reuse the numbering engine; do NOT fork the numbering algorithm). Reconciled in R4.5 spec FR-14 (R4.5 = read-time number; G3 = edit-time renumber, shared model).
- **G2** (clean apply) → **R4.5 WS-2/WS-3** — *missed by the R5 README*: G2's "re-author from content model" is fidelity-bounded by the projection round-trip WS-2/3 harden.
- **G8 / G9** → **R4.5 WS-1** mount-surface rewrite (they hook the `ComposeEditor.tsx`/`ComposeWorkspace.tsx` mount effects WS-1 restructures).
- **G10** (profile re-run) → **R4.5 WS-4** (soft; only makes citations precise). Note: the save-path profile re-trigger already exists (`ComposeService.cs:843-855`, compose-r2 fire-and-forget); G10's real gap is reload/manual-button.

## 2. File contention (the two merge-conflict hot spots)

| File | R4.5 | R5 | Rule |
|---|---|---|---|
| **`ComposeService.cs`** | WS-1 upload projection + WS-4 persist `paraId→number` (in `LoadAsync`/`SaveAsync`) | G1 origin marker (`LoadAsync`), G7 create-vs-replace (`~:737-836`), G10 profile trigger (`:843`) | **R4.5-owned-FIRST.** R5 rebases G1/G7/G10 onto post-R4.5 `LoadAsync`/`SaveAsync`. |
| **`ComposeWorkspace.tsx`** | WS-1 upload/browse hydrate `projection` (`:685-740`, `:1891-1983`) | G1 transient/save routing (same `isTransientCreate` region `:940-1205`), G7 toolbar, G8 mount | **R4.5-owned-FIRST.** |

Soft-contended (mergeable with care, mostly different regions): `ComposeEditor.tsx`, `ComposeEndpoints.cs`, `docxBridge.ts`.

## 3. The docxBridge hazard (⚠️ read before WS-1 executes)

`docxBridge.ts` exports **both** `docxToTipTapHtml` (mammoth READ — WS-1 deletes) **and** `buildContentModel` + `stampParaIds` + paraId helpers (WRITE/save — **G1/G2/G7 depend on these**). **WS-1 must delete ONLY `docxToTipTapHtml`, NOT the file.** A task that "removes the mammoth module" or "deletes docxBridge.ts" would break R5's clean-authoring/versioning path. (Born-in-editor rides `initialHtml`/`setContent`, not mammoth — untouched by WS-1.)

## 4. No design contradictions found

R4.5 does NOT touch `ComposeShadowPatchEngine.cs` / byte-authoring (its ADR-tension T-3 = comply with the R4 two-author split); R5's G2/G3/G4/G5 own the engine. WS-1's mammoth removal is the SAME structural fix R5 already moved out (T1/G6) — aligned, not contradictory. Number-atom immutability (WS-3) vs live-renumber (G3) is pre-reconciled in R4.5 FR-14.

## 5. Deploy coordination

Both surfaces deploy to the shared **`sprk_spaarkeai`** web resource + `spaarke-bff-dev`. R4 already hit deploy contention here (another project's master-based bundle overwrote R4's). Coordinate deploy timing; whoever deploys last wins. Prefer: land R4.5 to master, then R5 builds/deploys from master-with-R4.5.

## 6. How this note should propagate

- This note lives in R5's folder; a **reciprocal note should be dropped in `projects/spaarkeai-compose-fidelity-r4.5/notes/`** (that project runs in a separate session/worktree — its owner/agent should mirror §2/§3 there so WS-1's executor sees the docxBridge hazard).
- Both projects should be rows in **`projects/INDEX.md`** (registered 2026-07-28) so `/conflict-check` surfaces the `ComposeService.cs`/`ComposeWorkspace.tsx` overlap at PR time.
- `/conflict-check` will catch the **file-level** overlap once both open PRs, but will NOT catch the **region-level** contention or the **logical** dependencies in §1 — those live here.
