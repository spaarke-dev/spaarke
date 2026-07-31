# Task 021 — G2 Clean-Apply (R5-D2 Candidate A) — Deviations & Notes

> **Status**: ✅ COMPLETE · 2026-07-29 · FULL rigor · opus/xhigh
> **Approach**: Candidate A (engine clean-apply branch over retained bytes) — operator-confirmed highest fidelity 2026-07-29.

## What shipped

**Engine** (`ComposeShadowPatchEngine.cs`): added `bool trackChanges = true` to `Apply(...)`, threaded to a per-call `PatchSession._trackChanges` (no singleton state — ADR-010). Clean-mode (`trackChanges:false`) behavior, resolving by `(paraId,runIndex,offset)` exactly as tracked (I-7, no text-search):
- **insertText / replaceRange** → plain `w:r` (via new `BuildInsertElement` helper), no `w:ins`.
- **deleteRange / replaceRange / deleteParagraph strikes** → physical `run.Remove()` (via `WrapRunAsDeleted` clean branch), no `w:del`.
- **split / insertParagraph** → node change applied; `MarkParagraphMark` no-ops the tracked para-mark → clean.
- **mergeParagraph** → physical merge (move source inline content onto target, remove source node).
- **deleteParagraph** → physical removal of the whole node (sectPr guard retained).
- **setBlockAttr (alignment/style/list)** → direct `w:jc`/`w:pStyle`/`w:numPr`, NO `w:pPrChange` (alignment branch + shared `RecordPPrChange` clean no-op → Style + List inherit).
- **table structural ops + accept/reject** → REFUSED with a typed `ComposePatchException` in clean mode (see Limitation below).

**Service** (`ComposeService.cs`): extracted `ReadPersistedOriginAsync` (shared by Load + Save). `SaveAsync` resolves `cleanApply` from the durable `sprk_composeorigin` marker (server-read, NOT inferred) on the op-log path, and passes `trackChanges: !cleanApply` to both the normal `Apply` and the stale re-anchor `Apply` (`ReanchorStaleSaveAsync` gained the flag). Best-effort: marker read failure → tracked (safe).

**Client** (`ComposeWorkspace.tsx`): re-routed the reopened-doc save — reopened AUTHORED docs now take the **op-log path** (server applies clean via the marker), NOT the contentModel/renderer path. Only an in-session **born-in-editor** re-save (`!state.docxBytes`, no retained baseline — task 039) still uses contentModel (authored ORIGINATION through the renderer; the two-byte-author split stands). Reverses task 020's interim `routeClean = bornInEditor || isAuthoredOrigin` → just `bornInEditor`.

**Tests** — `tests/integration/seam/Compose/ComposeCleanApplySeamTests.cs` (3 through-the-wire slices): authored-origin insert+delete applies clean (zero w:ins/w:del/pPrChange, plain-run insert, physical delete, untouched paras byte-identical, all parts byte-identical); imported-origin stays tracked; legacy-null → treated as imported (tracked). **3/3 green.**

## Verification
- Clean-apply seam **3/3**; full Compose suite **807/807** (was 739 baseline — +9 new, no regression); corpus byte-diff **24/24** (NFR-01); tracked byte-diff **16/16** (default path unchanged — backward compatible).
- **R4.5 non-regression:** numbering/citation/projection/reference-map seam tests all green in the 807 (operator requirement — R4.5 is on master).
- Publish **46.75 MB compressed** (+0.00 vs task 020; ≤60 ceiling); **zero new runtime package** (pure C# flag + branches).
- BFF build 0 errors. ArchTests: 3 failures PROVEN PRE-EXISTING (identical on clean pre-021 tree via stash) — zero new violations. Gates: adr-check clean, code-review PASS.

## Decisions / deviations
1. **Routing conflict (020 vs 021) — operator-resolved to Candidate A (highest fidelity).** See g2-clean-apply-decision.md operator-resolution addendum. Task 020's origin *marker* read/write is intact; only the client clean-payload *shape* for reopened authored docs changed (contentModel → op-log+clean). This is why the change touches ComposeWorkspace.tsx (a task-020 file) again — expected Phase-2 serialization.
2. **Origin resolution = server reads the durable marker** (not client-supplied). More robust than trusting a client flag (immune to client bugs); one extra Dataverse retrieve on the authored op-log path only, best-effort. Matches decision note "durable marker, not inferred."
3. **BOUNDED LIMITATION (surface to operator):** clean-mode **table structural** ops (insert/delete row/col, setProps) + **accept/reject-revision** REFUSE with a typed error rather than emit tracked markup on an authored doc — the decision note §2 sanctioned option. Clean-supported: text/mark, split/merge/insert/delete paragraph, alignment/style/list. **NFR-08 note:** a user doing a table row/col edit on an authored doc will hit a clean 422 (not silent tracked markup, not data loss). If clean table structural editing is wanted, it is a G2 follow-up (est. ~200 LOC of table-applier clean branches). Recommend a defer-issue if operator wants it before UAT.

## PR obligations
- **Placement Justification (§10):** clean-apply is a mode on the existing `ComposeShadowPatchEngine` byte-author + a marker read in `ComposeService` — no new component (§11 N/A). Engine stays `byte[]`-in/out; no AI/Graph types.
- `/conflict-check` before the BFF PR (overlaps compose-r1/r2/r3 + ai-architecture-redesign-r2); watch #266 OpenXml.
