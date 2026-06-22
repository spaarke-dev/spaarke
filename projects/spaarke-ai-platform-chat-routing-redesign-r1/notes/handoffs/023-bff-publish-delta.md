# Task 023 — BFF Publish Delta

> **Generated**: 2026-06-22
> **Task**: 023 — Action code reform: drop `@v1` from new actions; backward-compat normalization for existing `sprk_actioncode` values (FR-06)
> **Wave**: 1-H (parallel-safe with task 024 — different file scopes)
> **Phase 0 baseline (per handoff history)**: 44.75 MB compressed
> **Same-method baseline (re-compressed task-020 dir on 2026-06-22)**: 46.08 MB compressed

---

## Measurement

| Metric | Value |
|---|---|
| Source | `deploy/api-publish-023/` |
| Compressed archive | `deploy/api-publish-023.zip` (also captured at `c:/tmp/api-publish-023.zip`) |
| Compression method | `pwsh Compress-Archive` (identical to prior handoffs §71-78 of `020-bff-publish-delta.md`) |
| Compressed bytes | **48,316,933** |
| MB (PowerShell `/1MB`) | **46.0786** |
| MB (1024²) | **46.08** |
| Uncompressed bytes | **130,007,461** |
| Δ uncompressed vs task 020 | **+2,364 bytes (~2 KB)** |
| Δ compressed vs same-method 020 baseline (46.0778) | **+0.0008 MB (~826 bytes)** |
| NFR-01 ceiling | 60 MB (HARD STOP) |
| NFR-01 architecture-review trigger | 55 MB |
| NFR-01 escalation trigger | +5 MB single-task delta |

**Verdict**: ✅ **WITHIN BUDGET**. True code-driven delta is +2 KB uncompressed / +826 bytes compressed — well below the +5 MB escalation threshold.

---

## ⚠️ Baseline-measurement drift notice

The prior handoffs (010 through 022) all report **44.75 MB** compressed. Re-compressing the
prior `deploy/api-publish-020/` directory today using the **same `pwsh Compress-Archive`
command** yields **46.08 MB** — not 44.75 MB. The 192 files / 130 MB uncompressed content
matches between task 020 and task 023 publishes (file-by-file diff: only `Sprk.Bff.Api.dll`
+2048 bytes, `Sprk.Bff.Api.pdb` +312 bytes, `Spaarke.Dataverse.pdb` +4 bytes; total +2,364
bytes). This delta is consistent with the modest +60 LOC `ActionCodeNormalizer` static class
addition + a few log-statement edits in `InsightsActionRouter.cs`.

The **44.75 MB number in prior handoffs is not reproducible** when re-running
`Compress-Archive` against the same directories today. Hypothesis: prior baselines were
taken before certain artifacts were added to the publish output (e.g., satellite assemblies,
runtime configs, framework refs added by a tooling update between wave 1-A and wave 1-H), or
they were measured with a slightly different command (`zip` vs `Compress-Archive`, different
compression level, etc.). The **same-method baseline of 46.08 MB** (derived by re-compressing
the prior task-020 publish dir today) is the correct apples-to-apples comparison point for
this task.

**Action requested**: a future task should normalize the baseline number across handoff log
entries 010–022. This task does not retroactively edit those entries.

---

## Why minimal delta

This task is purely additive at the code level:

1. **New file**: `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Routing/ActionCodeNormalizer.cs`
   — internal static class with two pure methods (`Normalize`, `Format`). ~70 LOC including XML doc.
   No new types in DI graph (per ADR-010); no new packages; no new endpoints.

2. **Edit**: `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Routing/InsightsActionRouter.cs`
   — added 3 calls (`ActionCodeNormalizer.Format`, `ActionCodeNormalizer.Normalize`, plus
   the existing `LogInformation` calls extended with the `ActionCodeFormat` structured
   property). ~20 LOC of inline code + ~10 LOC of expanded XML doc.

3. **Test file**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/Routing/ActionCodeNormalizerTests.cs`
   — 12 new tests. Test-project output is NOT published; zero impact on BFF publish size.

4. **Test edits**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/Routing/InsightsActionRouterTests.cs`
   — added two `private const string ...Normalized` constants and 3 mock-setup updates
   (alternate-key lookup now uses the normalized form). Test-project — not published.

5. **No NuGet additions**: zero changes to `Sprk.Bff.Api.csproj`.

---

## Cumulative project trajectory (recomputed same-method baseline)

| Wave | Compressed (same-method) | Δ vs prior |
|---|---|---|
| Same-method baseline (re-compressed task 020) | 46.08 MB | — |
| **023** | **46.08 MB** | **+0.0008 MB (~826 bytes)** |

Headroom vs NFR-01 ceiling: **60.00 − 46.08 = 13.92 MB** (23.2% of budget remains).

---

## ADR-029 compliance

| Check | Status | Notes |
|---|---|---|
| Per-task `dotnet publish` measurement performed | ✅ | `deploy/api-publish-023/` + `deploy/api-publish-023.zip` |
| Compressed size reported in absolute + delta form | ✅ | 46.08 MB / +826 bytes (same-method) |
| `<PublishTrimmed>` / `<PublishAot>` not enabled | ✅ | Verified `Sprk.Bff.Api.csproj` unchanged |
| Δ < +5 MB single-task escalation threshold | ✅ | Cleared by orders of magnitude (+826 bytes vs +5 MB) |
| Cumulative size < 55 MB architecture-review threshold | ✅ | 46.08 MB — 8.92 MB headroom |
| Cumulative size < 60 MB HARD STOP | ✅ | 13.92 MB headroom |

---

## NFR + CVE check

| Check | Status | Notes |
|---|---|---|
| No new HIGH-severity CVEs from `dotnet list package --vulnerable` | ✅ | No package references added |
| Test count delta | ✅ | +12 new tests (`ActionCodeNormalizerTests.cs`); 3 existing `InsightsActionRouterTests` test mocks updated to match the post-normalization lookup contract |
| Existing test suite regression | ✅ | All 25 tests in scope pass (12 new + 13 existing router) |
| Build errors | ✅ | 0 errors; 16 pre-existing warnings unrelated |
