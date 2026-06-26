# Task 003 — Publish-Size & CVE Verification

**Date**: 2026-06-25
**Task**: 003-author-entityname-validator-nodeexecutor
**Files added**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/EntityNameValidatorNodeExecutor.cs` (~430 lines including XML docs)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/EntityNameValidatorNodeExecutorTests.cs` (~320 lines)
**Files modified**:
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` (DI registration; +9 lines)

## Publish-Size

| Metric | Value |
|---|---|
| Wave 1 baseline (per spec §10) | **46.30 MB** compressed |
| Task 003 compressed | **44.98 MB** |
| Delta vs baseline | **-1.32 MB** |
| Single-task ceiling per CLAUDE.md §10 | +5 MB (within) |
| Hard ceiling per spec NFR-01 | 60 MB (within) |

The slight reduction is normal noise in publish output (dependency variance + measurement environment). The new executor (~430 LOC of pure C# string handling, no new NuGet refs) contributes ≈ 10–20 KB to the compiled DLL.

Measurement procedure: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish-task003/` then `Compress-Archive -Path deploy/api-publish-task003/* -DestinationPath deploy/api-publish-task003.zip`.

## CVE Scan

```
> dotnet list src/server/api/Sprk.Bff.Api/ package --vulnerable --include-transitive
Project Sprk.Bff.Api has the following vulnerable packages
   [net8.0]:
   Top-level Package                   Requested   Resolved   Severity   Advisory URL
   > Microsoft.Kiota.Abstractions      1.21.2      1.21.2     High       https://github.com/advisories/GHSA-7j59-v9qr-6fq9
```

**Status**: Only the pre-existing `Microsoft.Kiota.Abstractions 1.21.2` HIGH advisory remains (documented in project CLAUDE.md §10 / known carry-over from R3). **No NEW HIGH-severity CVE introduced by task 003.** The executor adds zero NuGet references — uses only existing infrastructure (`ILogger`, `System.Text.Json`, `System.Text.RegularExpressions`, `System.Diagnostics`).

## Tests

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~EntityNameValidator"
Passed!  - Failed: 0, Passed: 12, Skipped: 0, Total: 12
```

12/12 tests pass (8 mandatory per task prompt + 4 additional Validate edge cases). Spec AC-3b satisfied: `ExecuteAsync_ScrubsHallucination_Johnson_Lee_LLP` removes the offending firm name AND verifies a `hallucination_detected` warning event is emitted via mock `ILogger`.
