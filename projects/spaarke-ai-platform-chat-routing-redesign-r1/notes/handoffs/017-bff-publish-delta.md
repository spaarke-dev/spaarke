# Task 017 — BFF Publish-Size Delta

> **Task**: 017 — Migrate `ProjectPreFillService` to stable-ID lookup (Pattern A, FR-05, NFR-07 binding)
> **Date**: 2026-06-22
> **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
> **NFR-01 ceiling**: ≤60 MB compressed (HARD STOP)

## Measurement

| Metric | Value |
|---|---|
| Build configuration | `Release` |
| Output path | `deploy/api-publish-017/` |
| Uncompressed bundle | 139.44 MB |
| Compressed (`tar czf`) | **44.74 MB** |
| Baseline (pre-task) | 44.75 MB (task 015) |
| **Delta** | **-0.01 MB (negligible)** |
| Threshold check (≥+5 MB) | ✅ Not exceeded |
| Cumulative check (≥55 MB) | ✅ Not exceeded |
| Ceiling check (≥60 MB) | ✅ Not exceeded |

## Verdict

✅ Within NFR-01 thresholds. No new packages added; only existing types (`IPlaybookLookupService`, `IOptions<WorkspaceOptions>`) injected into an existing service. Net DLL size unchanged.

## What changed

`src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs`:

- Removed `private static readonly Guid DefaultPreFillPlaybookId = Guid.Parse("fc343e9c-3460-f111-ab0b-7c1e521b425f");` constant.
- Added `IPlaybookLookupService _playbookLookup` field + constructor parameter.
- Replaced `Guid.TryParse` + hardcoded GUID fallback at the playbook-resolution call site with:
  - Fail-fast guard on empty `WorkspaceOptions.ProjectPreFillPlaybookId` → returns `ProjectPreFillResponse.Empty()` with `CONFIG_MISSING` log
  - `await _playbookLookup.GetByIdAsync(configuredPlaybookId, cancellationToken)` lookup via stable-ID alternate key (`sprk_playbookid`)
  - Try/catch around lookup → returns `ProjectPreFillResponse.Empty()` with `PLAYBOOK_LOOKUP_FAILED` log

## NFR-07 preservation evidence

| Invariant | Preserved? | Evidence |
|---|---|---|
| Public method signature `AnalyzeFilesAsync(IFormFileCollection, string, HttpContext, CancellationToken)` | ✅ | Unchanged |
| 45s timeout via `timeoutCts.CancelAfter(TimeSpan.FromSeconds(45))` | ✅ | Line preserved verbatim |
| `useAiPrefill` consumer contract | ✅ | Endpoint surface unchanged |
| `$choices` output envelope | ✅ | `PlaybookRunRequest`/`ProjectPreFillResponse` schema untouched |
| Service registration `services.AddScoped<ProjectPreFillService>()` | ✅ | `WorkspaceModule.cs` unchanged |

## Grep verification

```
grep -rn "fc343e9c" src/server/api/Sprk.Bff.Api/
src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs:39   (XML doc comment — migration history)
src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs:277  (XML doc comment — migration history)
```

✅ **0 hits in executable code**; 2 hits in migration-history comments (intended).

```
grep -rn "3f21cec1" src/server/api/Sprk.Bff.Api/
(no matches)
```

✅ Task 003 already removed the stale `3f21cec1-` comment — confirmed.

## Build + test results

| Step | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ 0 errors, 16 pre-existing warnings (none in modified file) |
| `dotnet test ... --filter "ProjectPreFill"` | ✅ 3/3 passed |
| `dotnet test ... --filter "PreFill\|Workspace\|WorkspaceOptions"` | ✅ 392/392 passed |

## Files modified

- `src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs` (constructor + hardcoded GUID removal + Pattern A lookup)

Total: **1 file modified**, 0 files added, 0 files deleted.
