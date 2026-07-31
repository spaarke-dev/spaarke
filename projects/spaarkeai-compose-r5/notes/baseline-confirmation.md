# Task 001 — R4.5 Merge Baseline Confirmation

> **Task**: `001-confirm-r45-merge-baseline` (Phase 0 gate, confirm-only — no source files modified)
> **Date**: 2026-07-29
> **Branch**: `work/spaarkeai-compose-r5` (rebased onto master-with-R4.5)
> **Verdict**: 🟢 **GREEN BASELINE** — R5 implementation is cleared to proceed.

---

## 1. R4.5 outputs present on branch

| Output | Evidence | Status |
|---|---|---|
| `NumberingComputationEngine` | `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs:1357` — `internal sealed class NumberingComputationEngine` nested inside `public sealed class ComposeDocxProjectionBuilder` (declared `:65`). Confirmed nested (indented, not top-level) by grepping `^public|^internal` in the file — only one top-level match (`ComposeDocxProjectionBuilder` itself). | ✅ Present |
| `CitationResolver.cs` | `src/server/api/Sprk.Bff.Api/Services/Compose/CitationResolver.cs:43` — `public static class CitationResolver` with `Resolve(string?, IReadOnlyList<ParaIdMapEntry>)` (`:53`), `Resolve(string?, IReadOnlyList<ParaReferenceMapEntry>)` (`:60`), `ResolveCitation(string?, ...)` overloads (`:72`, `:76`). | ✅ Present |

Both confirmed per spec §Affected Areas discovery correction (b): the numbering engine is *not* a standalone file — it's nested `internal sealed` inside the projection builder, which is the reuse-mechanism decision task 005 (`numbering-engine-reuse-decision.md`, already authored in this Wave-0 pass) needs to resolve for G3.

## 2. docxBridge hazard state

`src/client/shared/Spaarke.Compose.Components/src/utils/docxBridge.ts`:
- `docxToTipTapHtml` (mammoth READ helper) — **absent**, per the file's own header comment (lines 8–13, 58–60): "has been DELETED... now EXPORT-side only."
- `buildContentModel` (`:230`) — **present**.
- `stampParaIds` (`:92`) — **present**, along with `captureParaIdSnapshot` / `buildBaselineParaIdMap` referenced in the header.

This matches the expected post-R4.5 state exactly: only the read function is gone; the write/save helpers G1/G2/G7 depend on remain intact. The file was **not** modified or deleted by this task.

## 3. Test results

### 3a. Full Compose-filtered suite
`dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter "FullyQualifiedName~Compose"`

```
Test Run Successful.
Total tests: 739
     Passed: 739
 Total time: 19.77 Seconds
```

Zero failures. (Transient `warn`-level Cosmos DB / Azure Identity log noise from `SessionPersistenceService` in the console output is pre-existing test-fixture behavior — those tests intentionally fall back gracefully when no real Cosmos/AAD credential is available in this environment; it does not fail any test.)

### 3b. Compose seam suite only (`tests/integration/seam/Compose/`, compiled into the same assembly via `LinkBase="SeamTests"`)
`dotnet test ... --filter "FullyQualifiedName~Sprk.Bff.Api.Tests.Seam.Compose"`

```
Test Run Successful.
Total tests: 208
     Passed: 208
```

Per-class breakdown:

| Seam test class | Count | Result |
|---|---|---|
| `ComposeCitationResolverSeamTests` | 32 | ✅ |
| `ComposeNoOpRoundTripByteDiffSeamTests` | 8 | ✅ |
| `ComposeNumberingRoundTripSeamTests` | 4 | ✅ |
| `ComposePatchEngineSaveSeamTests` | 49 | ✅ |
| `ComposePhase1IngestSeamTests` | 20 | ✅ |
| `ComposeProjectSeamTests` | 3 | ✅ |
| `ComposeReadFidelityHarnessSeamTests` | 47 | ✅ |
| `ComposeReferenceMapSessionLedgerSeamTests` | 2 | ✅ |
| `ComposeShadowPatchEngineByteDiffSeamTests` | 16 | ✅ |
| `ComposeSummaryPageSeamTests` | 18 | ✅ |
| `ComposeUploadProjectionSeamTests` | 2 | ✅ |
| `ConcurrencySaveSeamTests` | 4 | ✅ |
| `SpeSaveVersioningSeamTests` | 1 | ✅ |
| **Total** | **208** | **208/208** |

### 3c. Corpus byte-diff harness (the specific NFR-01 gate)
`ComposeShadowPatchEngineByteDiffSeamTests` = 16 (2 theory methods × 8 corpus docs: `NoOpApply_OnEveryCorpusDoc_ReturnsRetainedBytesByteIdentical` + `InteriorInsert_OnEveryCorpusDoc_LeavesUntouchedPartsAndSubtreesByteIdentical`), plus `ComposeNoOpRoundTripByteDiffSeamTests` = 8 (1 theory method × 8 corpus docs) — **24/24 byte-diff-specific cases, all green**, driving the production `ComposeShadowPatchEngine` (not a mock) over every fixture in `tests/fixtures/compose-corpus/`.

**Note on the "28/28" figure in the task prompt/spec**: the task's `<goal>` cited "28/28" as the expected byte-diff count from prior R4 baselines. The actual current corpus byte-diff composition on this branch is **24/24** (8 corpus docs × 3 theory methods across the two byte-diff-focused seam classes) — all passing. The discrepancy is a stale headline number from an earlier corpus size, not a regression: ADR-049 F-1 independently confirms "8/8 corpus docs char-exact" as the current corpus size. The byte-diff harness is unambiguously **GREEN** regardless of which historical count is used as reference.

## 4. Publish-size baseline (NFR-01/NFR-04)

`dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` — build succeeded, zero errors (pre-existing warnings only, same set as before this task; not introduced by this task since no source was touched).

| Measure | Value |
|---|---|
| Compressed size, incl. PDBs | **47.53 MB** |
| Compressed size, excl. PDBs | **46.70 MB** |
| Entry count | 248 files |
| PDB convention | `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, matching the repo's established measurement method (`.claude/constraints/azure-deployment.md`) |
| Ceiling (NFR-01/NFR-04) | ≤ 60 MB compressed — **well under**, ~13 MB headroom incl. PDBs |
| Spec reference baseline | ~46.11 MB (spec NFR-04, post-R4.5) — **46.70 MB excl. PDBs matches closely** (Δ +0.59 MB, within normal build-to-build variance / warning-driven noise; not attributable to this confirm-only task, which touched zero source files) |

This is the **baseline** every subsequent R5 BFF-touching task reports its delta against, per NFR-04 / root CLAUDE.md §10 item 4.

## 5. Acceptance criteria verification

| # | Criterion | Result |
|---|---|---|
| 1 | `NumberingComputationEngine` + `CitationResolver.cs` present with file:line evidence | ✅ §1 |
| 2 | Corpus byte-diff harness + existing Compose seam tests all pass | ✅ §3 (208/208 seam, 24/24 byte-diff-specific) |
| 3 | Release publish compressed size recorded, ≤60 MB | ✅ §4 (47.53 MB incl / 46.70 MB excl PDBs) |
| 4 | `docxBridge.ts` write helpers present, `docxToTipTapHtml` absent, file NOT modified | ✅ §2 |
| 5 | No source file under `src/` modified by this task | ✅ Confirm-only; only ran `dotnet test`/`dotnet publish` and wrote this note + `deploy/api-publish/` build artifacts (git-ignored build output, not source) |

## 6. Escalation check

Per the task's `<escalation><trigger>`: the corpus byte-diff harness is **GREEN**, not red. **No escalation required.** R5 implementation waves are cleared to proceed on this baseline.

## 7. Deviations

- Task prompt cited byte-diff target "28/28"; actual current count is 24/24 (all green) — see §3c for reconciliation. No functional deviation; documented per task step 8.
- `TASK-INDEX.md` status update is intentionally **not** performed by this task run — the orchestrating session owns that update per explicit dispatch instruction (parallel Wave-0 execution).
