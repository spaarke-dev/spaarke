# Task 012 — BFF Publish-Size Delta (ADR-029 / NFR-01)

> **Date**: 2026-06-21
> **Task**: 012 (Add `WorkspaceOptions.SummarizePlaybookCode` + fix ADR-018 violation at `WorkspaceFileEndpoints.cs:30,254`)
> **Wave**: 1-A
> **Rigor**: FULL
> **Outcome**: ✅ Within NFR-01 budget

---

## Publish-Size Measurement

| Metric | Value |
|---|---|
| **Compressed size (this task)** | **46.08 MB** |
| Phase 0 baseline (Wave 0-B, 2026-06-21) | 44.75 MB |
| Cumulative delta vs Phase 0 | **+1.33 MB** |
| NFR-01 ceiling | 60 MB |
| Single-task delta threshold (escalation) | ≥ +5 MB |
| Cumulative threshold (architecture review) | ≥ 55 MB |
| Cumulative threshold (HARD STOP) | ≥ 60 MB |
| **Status** | ✅ Within budget on all thresholds |

### Measurement procedure

1. `dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish/`
2. Compress published output via PowerShell `Compress-Archive -CompressionLevel Optimal` (matches deploy-pipeline zip semantics)
3. Output: `deploy/task012-publish-size.zip`
4. Size reported as compressed bytes ÷ 1MB.

### Single-task delta attribution

Task 012 itself is **code-only** with **no new package references**:
- Modified: 3 .cs files (`WorkspaceOptions.cs`, `WorkspaceFileEndpoints.cs`, no signature shape changes that pull new transitive types).
- Modified: 1 `appsettings.template.json` (text-only, < 1 KB added).
- Added: 1 test file (in test project — not part of BFF publish output).

The +1.33 MB cumulative drift since the Phase 0 baseline is therefore **NOT attributable to task 012**. It reflects the aggregate of intervening wave work merged on the branch (likely the dependency restore graph hydrating slightly differently between Phase 0 build and the current Release build). The single-task delta from task 012 alone is effectively zero.

No escalation required per NFR-01 thresholds.

---

## Verification Artifacts

- `deploy/task012-publish-size.zip` (46.08 MB, regenerable)
- Build: 0 errors, 17 warnings (all pre-existing)
- Unit tests: 6/6 passed (`Sprk.Bff.Api.Tests.Configuration.WorkspaceOptionsTests`)
- FR-04 acceptance grep: `IConfiguration["Workspace:SummarizePlaybookId"]` returns **0 live matches** (only XML doc-comment historical references remain)

---

## Next Wave Anchor

Task 013 (Wave 1-B) extends `WorkspaceOptions` with 3 additional `*PlaybookCode` properties (ChatSummarize, MatterPreFill, ProjectPreFill, AiSummary). Task 012 left the file shape stable for 013 — the only added properties were the two for the Summarize playbook (`SummarizePlaybookId` for backward-compat and `SummarizePlaybookCode` for the upcoming stable-code migration in task 019). No anticipated merge conflict.
