# Task 055 — anchor paraOffset fix + Open-Document modal + diagnostic — Deviation & Completion Note

> **Completed**: 2026-07-30 · Rigor FULL · opus/xhigh · SEV-1 (the 422 the operator hit repeatedly).
> Implementation delegated to two general-purpose subagents with precise specs from the confirmed diagnosis; main session reviewed the diffs + ran all gates on the combined tree.

## Confirmed root cause (App Insights + the failing doc)
Save 422 = `ComposePatchException` "run-local offset 45 is out of range for runIndex 3 on paraId '421E7EDC' (run editor length 9)". In `Test WORD Document 4.docx`, paragraph `421E7EDC` has **74 OOXML runs** (Word proofing/rsid split): run[3]="tincidunt"=9 chars, run[73]="Make changes to the document in Word for Web!"=45 chars. TipTap **merges** same-format runs, so the client tagged the edit editor-run-3 while the OOXML run is 73; the server resolved OOXML run 3 → offset 45 off the end → 422. The `(paraId,runIndex,offset)` anchor is **not stable across the TipTap↔OOXML boundary**. Independent of task 050 (replace path unchanged).

## Prong 2 — the fix (owner-approved, ADDITIVE) ✅
The engine already works in **absolute paragraph-offset space** internally (`ToAbsoluteOffset` → flatten runs → split at absolute offset). The only lossy step was the client→`(runIndex,offset)`→absolute round-trip. Fix: carry the client's paragraph-relative char offset `k` on the anchor and use it directly.
- **`Operations/ComposeOperation.cs`**: `ComposeRunPoint` gains optional `int? ParaOffset = null` (Range Start/End inherit). Nullable/defaulted → backward-compatible.
- **`ComposeShadowPatchEngine.cs`**: `ToAbsoluteOffset` — when `point.ParaOffset is int k`, resolve via new `ResolveAbsoluteFromParaOffset` (walks the paragraph's real OOXML editor-run flatten, reusing the existing `RunEditorLength` measure — the same "run editor length" in the 422 message — with the client's `k <= acc+len` boundary + at-end clamp). Falls back to `(RunIndex,Offset)` byte-identically when absent. **I-4** (only the target run splits) + **I-7** (pure numeric walk, never a text match) preserved.
- **Diagnostic**: optional `ILogger` ctor (keeps `new()` for tests; DI injects real logger); `LogAnchorRefusal` logs op paraId/runIndex/offset/paraOffset + the per-run editor-length breakdown at both refusal throw sites. Log-only (anchor metadata, no content).
- **Client**: `compose-operations.ts` `ComposeRunPoint` gains `paraOffset?`; `stepOperationInterceptor.ts` `runLocalPoint` emits `paraOffset: k` on every point (all branches incl. splitParagraph `at`).
- **Tests** (`ComposeParaOffsetAnchorSeamTests.cs`, banned-pattern clean): (1) many-run paragraph, insert at end of final run via paraOffset → lands correctly + persists; (2) same op as `(runIndex=3,offset=45)` no paraOffset → `OffsetOutOfRange` (proves the fix); (3) backward-compat legacy anchor resolves as before; (4) paraOffset overrides deliberately-wrong legacy indices. All PASS.

## Open-Document modal ✅ (reuse, not new)
Toolbar **"Open document"** button (threaded host→ComposeEditor→ComposeFormatToolbar like `onRefreshProfile`), gated on a promoted doc. Opens the shared **`RichFilePreviewDialog`** (`@spaarke/ui-components`) fed by the EXISTING BFF `GET /api/documents/{id}/preview-url` — the same mechanism `ConversationPane`'s "Open preview" uses. `@spaarke/auth` fetch (ADR-028), dark-mode (ADR-021). No new component, no new endpoint (§11 reuse / §10 BFF hygiene). +4 toolbar tests.

## Prong 1 (keep-edits recovery) — ✅ DONE (2026-07-31 fast-follow)
Implemented as **service-level best-effort per-paragraph recovery** in `ComposeService.SaveAsync` (NOT engine surgery — preserves the engine's tested all-or-nothing purity + byte-diff determinism). On an OP-LEVEL `ComposePatchException` from the loaded-doc apply block, `ApplyBestEffortByParagraph` partitions the op-log into the **largest units provably safe under the engine's intra-paragraph sequential rebasing** and applies each via the SAME `ComposeShadowPatchEngine.Apply` onto cumulative bytes:
- **Inline ops (text/mark/setBlockAttr/single-revision) grouped by `paraId`** — the paragraph is the atomic unit (dropping one op would leave later same-paragraph ops mis-anchored), first-seen order; each paragraph's anchored comments ride its unit (preserves the engine's comments-first-per-Apply EDGE-1 ordering per paragraph).
- **Structural / All-revision ops = ONE all-or-nothing unit applied LAST** (mirrors the engine's structural-last pass; keeps minted-paraId lineage intact).
- **`AppliedCount == 0` → re-throw the original refusal** (a wholly-unanchorable batch has no partial success to preserve → stays a hard 422/409, no no-op version — preserves the pre-prong-1 `Save_UnknownParaId` contract).
- **BATCH-level kinds** (`MalformedDocument` / `UnsupportedSchemaVersion`) are filtered OUT by the `when (!IsBatchLevelPatchRefusal)` guard → still fail hard.

**Surface (never silently applied, never silently dropped):** new `PartialApplySummary` (+ `UnresolvedComposeOp`) on `SaveComposeDocumentResult` + `SaveComposeDocumentResponse` (`partialApply`) — Total/AppliedCount/UnresolvedCount + per-op {paraId, opType, kind, reason}. Client: `partialApply` state (set on `saveSucceeded`, cleared on save-start/load) → honest warning banner in `ComposeBannerStack` ("Saved N of M edits; K couldn't be placed — please redo them"), replacing the plain Saved ✓ bar; dismissable; Fluent v9 dark-mode.

**Files:** `IComposeService.cs`, `ComposeService.cs` (`ApplyBestEffortByParagraph`/`TryApplyPatchUnit`/`IsBatchLevelPatchRefusal`/`IsStructuralOrGlobalOp`), `ComposeEndpoints.cs`; client `ComposeWorkspace.types.ts`/`ComposeWorkspace.tsx`/`ComposeBannerStack.tsx`.

**Tests:** NEW `ComposePartialApplyRecoverySeamTests.cs` (3, through-the-wire): mixed batch (para A applied + absent-paraId surfaced), intra-paragraph all-or-nothing (mixed: resolvable para applied + same-para pair both surfaced, `[SECOND]` never applied), batch-level schema refusal stays hard 409. `ComposeBannerStack.test.tsx` +5 (banner shows/suppresses-success/hidden-when-clean/dismiss/dark-mode). Compose C# **830/0** (incl byte-diff **25/25**); full BFF **9581/0/101**; banner **12/12**. Publish **46.86 MB** (≤60), zero new package.

**Step 9.5:** ADR-049 (additive, I-4/I-7, byte-diff held) · ADR-013/007 (no AI/Graph) · ADR-010 (no new interface) · ADR-038 (seam DoD, no banned mocks — existing fixture boundaries only) · ADR-021 (dark mode) · §10/§11 (no new endpoint/service/component/package; reuse). No violations.

## Combined gate results (both agents' changes on one tree)
- BFF build **0 errors**; Compose C# suite **827/0** (incl. byte-diff 24/24 + the 4 new anchor tests); client typecheck **no new errors** (touched files clean); toolbar tests **51/51**.
- Publish **50.80 MB** compressed (≤60 ceiling; code delta is one int field + a resolution method — the vs-050 variance is measurement noise); no new runtime package.

## Step 9.5
ADR-049 (additive, I-4/I-7, byte-diff 24/24) · ADR-013/007 (no AI/Graph added; modal reuses existing endpoint) · ADR-038 (new engine seam test, no banned patterns) · ADR-028/021 (modal auth + dark mode) · §10/§11 (no new endpoint/component/package; reuse). No violations.
