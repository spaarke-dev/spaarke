# W1 Baseline — BFF Publish Size (2026-06-24)

> **Purpose**: Anchor measurement for Phase 7 task 144 ("BFF publish-size net reduction") comparison. Captured before any Phase 5R / Phase 7 code work begins so the delta from WP4 retirement is measurable.

## Measurement

| Metric | Value |
|---|---|
| **Compressed publish size** | **46.28 MB** (48,526,324 bytes) |
| Uncompressed publish size | 139.97 MB (146,773,570 bytes) |
| DLL count | 197 |
| Build configuration | `Release` |
| Output path | `deploy/api-publish/` |
| Source revision | `5eca2402f` (Phase 5R + Phase 1R spec/index/POMLs committed; no source code changes since `8579d6536` master merge) |

## NFR-01 ceiling context

Per spec NFR-01 and `.claude/constraints/azure-deployment.md`:

| Threshold | Limit | Current state | Headroom |
|---|---|---|---|
| Single-task delta requires justification | ≥ +5 MB | n/a (baseline) | — |
| Architecture review trigger | ≥ 55 MB | 46.28 MB | 8.72 MB |
| HARD STOP | ≥ 60 MB | 46.28 MB | 13.72 MB |

## Comparison to prior baselines

| Date | Source revision | Compressed | Delta from prior | Reason |
|---|---|---|---|---|
| Post-Phase-5-MVP (per spec.md §10) | (prior) | 45.65 MB | — | MVP scope cut |
| Phase 4 exit gate (task 105 evidence) | (mid-Phase 4) | 48.89 MB | +3.24 MB | Phase 4 MVP cumulative additions |
| **W1 baseline (this measurement)** | `5eca2402f` | **46.28 MB** | **−2.61 MB vs Phase 4 exit gate** | Net effect of subsequent task work + R6 hotfixes; no Phase 5R source code yet |

## Expected delta direction for Phase 7 task 141 (CapabilityRouter retirement)

Per spec WP4 + NFR-01 commentary: WP4 deletion is **expected to reduce** publish size (10 deletion-targeted files in `Services/Ai/Capabilities/` + supporting infrastructure). The net post-retirement number should be ≤ 46.28 MB; net reduction is the Phase 7 task 144 acceptance signal.

## Expected delta direction for Phase 5R additions (new code WILL add size)

| Phase 5R component | Expected delta | Note |
|---|---|---|
| 111R `IIntentRerankerService` + structured-output schema | ~+0.2 MB | Small service + JSON schema generator |
| 113R `IPlaybookCandidateSelector` | ~+0.05 MB | Pure logic, no dependencies |
| 114R `NodeType.DeliverComposite` engine extension | ~+0.3 MB | New executor case + DI |
| 114a Per-section SSE streaming | ~+0.1 MB | Event types + emitter |
| 117a `playbook_options` SSE event contract | ~+0.05 MB | Event type only |
| 118b `GetWorkspaceTabContentHandler` | ~+0.1 MB | Reuses Pillar 6b plumbing |
| **Subtotal (Phase 5R cumulative add)** | **~+0.8 MB** | Estimate; verify per-task |
| Phase 5R **net** after WP4 retirement | ~46.28 MB ±2 MB | Depends on size of `Services/Ai/Capabilities/` deletion |

## Phase 1R expected delta

| Phase 1R component | Expected delta |
|---|---|
| 028a `IConsumerRoutingService` + impl | ~+0.15 MB |
| Phase 1R **net** | ~+0.15 MB (does not retire any files) |

## Cumulative project ceiling check (forward-looking)

Estimated worst-case cumulative position at end of project:
```
46.28 MB current
 +0.80 MB Phase 5R additions
 +0.15 MB Phase 1R additions
 -X.XX MB WP4 retirement (Phase 7 task 141 — unknown until measured)
= ~47.23 MB - X.XX (likely 44-48 MB range)
```

All scenarios remain well below the **architecture-review trigger of 55 MB** and far below the **HARD STOP of 60 MB**.

## Method / reproducibility

```powershell
# Clean
Remove-Item -Recurse -Force src/server/api/Sprk.Bff.Api/publish -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force deploy/api-publish -ErrorAction SilentlyContinue

# Publish Release
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/

# Compress + measure
Compress-Archive -Path "deploy/api-publish/*" -DestinationPath "deploy/api-publish-baseline.zip" -Force
(Get-Item "deploy/api-publish-baseline.zip").Length / 1MB
```

## Reuse instructions

Phase 7 task 144 ("BFF publish-size net reduction") executors:
1. Re-run the measurement using the same method above (clean publish, Release config, compressed zip)
2. Compare the new compressed-MB against **46.28 MB** baseline
3. Expected: post-WP4-retirement value < 46.28 MB (net reduction)
4. If post-WP4-retirement value > 46.28 MB: investigate; Phase 5R additions may have exceeded estimate; reference the per-task publish-size measurements (each FULL-rigor task in Phase 5R has a `notes/handoffs/NNN-bff-publish-delta.md` deliverable per the POML output specification)

---

*Baseline captured 2026-06-24 by main session as part of W1 parallel work (no-risk measurement-only task). Filed at `notes/handoffs/` per task-evidence convention.*
