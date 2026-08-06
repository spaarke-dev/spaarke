# Current Task — Spaarke Compose R6

> **Last Updated**: 2026-08-06 (task-execute Step 2 — task 012 STARTED)
> **Recovery**: Read "Quick Recovery" first; full state below.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **012 — Retire ComposeShadowPatchEngine + ComposeBaselineParaIdStamper from the save path** (`tasks/012-retire-surgical-save-path.poml`) |
| **Rigor** | FULL · sonnet@high (running Fable main-session) · directional |
| **Status** | in-progress — ALL implementation COMMITTED: A=70be80006 · C=2fc8ff530 · P-2/FR-08=3e4a9f456 · CLIENT CUTOVER=09b79eaae (66 client tests green, tsc baseline-identical 28; server Compose 979/982 floor). Step 9 criteria verified (types retained, docxBridge exists, no deletions/new usings, NDA no-422 pinned). CVE: no new HIGH (only pre-existing Crypto.Xml). Step 9.5 IN FLIGHT: code-review + adr-check agents on 09b79eaae + clean-worktree publish |
| **Next Action** | On reviewer completion: triage findings → fix commit if needed → close-out (POML completed, TASK-INDEX ✅, current-task reset for 013, push) |

### Conflict-check (Step 0.5) — done 2026-08-06
No open PR touches Services/Compose (PR 690 = CI LFS only). Sibling branches: architecture-redesign-r2 =
merge-commit only (empty Compose diff); compose-r1 = stale Prettier on ComposeToolbar.tsx/index.ts (files
not touched by 012). SOFT-warn only → proceed. Re-run before the eventual BFF PR.

### 012 ARCHITECTURE (locked this session)

**Why the client cutover is IN 012**: without it, the current client's imported dirty saves still post
op-log → engine, so the POML goal ("no reachable path for a normal save invokes engine/count-gate")
cannot hold in practice. 010 was server-side only; the client shapes were verified unchanged.

**Mapper design (Design A — merge-with-loaded-model + diff)**:
- Server Load (and browse /project) responses gain ADDITIVE `contentModel` (from the 020
  `ComposeDocxProjectionBuilder.BuildContentModel`) — ADR-040 additive JSON.
- Client retains `loadedContentModel`; new `buildImportedContentModel(editor, loadedModel, baselineSnapshot)`:
  - Untouched paragraph (text+editable-props unchanged) → loaded block passed through VERBATIM
    (preserves ALL server-set facts trivially — the common case, exact fidelity).
  - Edited paragraph → runs rebuilt from editor (b/i/u/link/comment/ins-del marks → facts) + user edits
    redlined via `diffTokens(baselineText, currentText)` → Revision Inserted/Deleted facts (author=user);
    block-level server-set facts (numId, table facts, pageBreakBefore, propertiesChange, markRevision)
    merged from the loaded block; editable props (alignment, level) from the editor.
  - New paragraph → fresh block, runs+mark Revision Inserted. Deleted paragraph → loaded block retained
    with runs wrapped Deleted + markRevision Deleted (merge by paraId anchor order).
  - Authored docs (clean semantics REQ-1): NO diff-derived revision facts — needs origin known client-side
    at load (verify/add on load response).
- Save routing flip: imported dirty (transient + replace) → `{ contentModel, content?/baselineVersionId }`;
  drop op-log/paraIdMap/separate-comments on that shape. Comments fold INTO the model (024 Comments list +
  anchor marker runs) — replaces the server comments-bake.

**Server retirement (Phase C)**: remove the comments-baking `_patchEngine.Apply` (ComposeService ~:901-923,
the last ContentModel-reachable engine caller — 010 adr-check residual); replace with loud wire-visible
warning when request.Comments arrive WITH ContentModel. Op-log path stays as THE transitional path
(amendment §4) with a deprecation warning log on imported tracked op-log saves. Count-gate: unreachable
from all post-cutover shapes (proof by request-shape trace in notes).

**Phases**: A server load-model exposure → B client mapper + routing flip → C server retirement →
D warning-family separation (026-F5) → E tests (mapper unit + server seam/unit) → F docs (FR-08 note in
ADR-049/design notes; P-10 carve-out + P-2 preamble verify; notes §18; publish + CVE) → Step 9.5 → close.

### 012 OBLIGATIONS (accumulated — BINDING; all covered by phases above)
1. CLIENT CUTOVER preserving every server-set field (numId 021 · tables 022 · pageBreak 023 · comments+
   anchors 024 · revision/formatChange/markRevision/propertiesChange 025) + baseline source; born-in-editor
   branch keeps omitting baselineVersionId (drive-item id would 404).
2. Retire stamper+engine from save path + the comments-baking Apply at ~:901 (route comments through model).
3. Client warning-family separation (026-F5).
4. Record FR-08 imported-coverage change in ADR-049/design notes.
5. P-10 audit carve-out + P-2 preamble extraction verification.

### Standing items
- Operator principle: best fidelity on common cases; rare shapes degrade LOUDLY, never silently.
- Execution shape: implement → seam/unit slice → commit → Step 9.5 (TWO parallel background agents on the
  committed SHA) + clean-worktree publish (46.90 MB ±0.00 baseline) → fix commit → close-out.
- NEVER delete docxBridge.ts. Commit --no-verify + Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>.
- Suite floor: 3 pre-existing reds (surgical/read-path artifacts owned by 012/013/027).
- Before eventual PR: merge origin/master (~67 behind; Crypto.Xml HIGH patched there) + re-run /conflict-check.

## Steps completed this task
- [x] Step 0.5: rigor FULL declared; /conflict-check soft-pass
- [x] Step 1: POML + obligations loaded; ADR-049 amendment §4 re-read (transitional clean-apply = sole
      permitted engine caller); save-path code map (engine callers :837 op-log, :905 comments-bake,
      :1669/:1901 = re-anchor + best-effort, all inside the op-log family)
- [x] Step 2: architecture locked (Design A above); current-task.md initialized

## Files modified this task
(none yet)

## Decisions
- 2026-08-06: Client mapper = Design A (merge-with-loaded-model + diffTokens redlining). Reason: block-level
  server-set facts cannot round-trip through TipTap; verbatim pass-through of untouched blocks gives exact
  fidelity on the common case per the operator principle. Pure-editor rebuild (Design B) rejected — cannot
  represent numId/table facts/propertiesChange.
- 2026-08-06: Op-log path RETAINED whole as the transitional path (amendment §4 wording + pre-cutover
  client compatibility); reachability criterion satisfied by post-cutover request-shape trace, not deletion.
