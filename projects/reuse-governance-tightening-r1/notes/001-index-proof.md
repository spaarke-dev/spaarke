# Task 001 — capability index: verification and dead-path proof

> 2026-11-11 · `.claude/patterns/CAPABILITY-INDEX.md` · 32 data rows, six columns (Capability, Synonyms, Canonical component, Symbol, Governing ADR, Extension point).

## How the cells were verified

- Facts were gathered by a read-only `code-mapper` agent (sonnet), then re-checked in the main session. A scratch script extracted every backticked repo path in the file and checked `Test-Path` plus `git ls-files` for each: **126 paths, 0 missing**. It ran `git grep -w` over `src/` for 66 symbols named in the file: **0 not found**.
- Two of the mapper's claims were wrong and corrected before writing: `FileIndexingService` does not call `RagIndexingPipeline` (it writes through `IRagService`; row 15), and 21 BFF classes derive from `BackgroundService`, not 12 (Known overlaps).

## Checker runs (`pwsh -File scripts/quality/Test-InstructionPaths.ps1`)

The checker's corpus is `git ls-files`, so the new file was staged (`git add`) before each run; an untracked file is not scanned.

| Run | Content | Result line | Attributed to CAPABILITY-INDEX.md |
|---|---|---|---|
| 1 | as committed | `Instruction files scanned: 247. Missing paths: 25 (baseline 23, new 2).` | **0** |
| 2 | + one seeded line `` `src/server/api/Sprk.Bff.Api/Services/DoesNotExist/SeededDeadPath.cs` `` | `Missing paths: 26 (baseline 23, new 3).` | **1** — `::error file=.claude/patterns/CAPABILITY-INDEX.md::Instruction file names a path that does not exist: src/server/api/Sprk.Bff.Api/Services/DoesNotExist/SeededDeadPath.cs` |
| 3 | reverted (file restored from a copy, re-staged) | `Missing paths: 25 (baseline 23, new 2).` | **0**; `SeededDeadPath` not in the file |

The seeded dead path was reported exactly once, and the committed file contains no dead path.

### The two "new" findings in every run are pre-existing and not from this task

Both are on `origin/master` and both are checker false positives (the named paths exist), which make the checker exit 1 on master:

1. `.claude/adr/ADR-028-spaarke-auth-architecture.md -> src/server/shared/Spaarke.Core/Auth/CallerIdentity.cs:113,247-249`. The file exists; the suffix stripper handles `:N` and `:N-M` but not a comma list (added 2026-10-10, `209ee27e0c`).
2. `.claude/FAILURE-MODES.md -> .claude/worktrees` (line 1185). This is a runtime folder, untracked and not gitignored (added 2026-11-05, `fafbad73d7`).

Filed as ISS-002 (#2684) and reported to the owner.

## Seed rows dropped or changed materially (design.md §3.2)

| Seed | Change | Reason |
|---|---|---|
| 5 | Two components: client `PolymorphicResolverService.ts` + server `FieldMappingEndpoints.cs` | Client and server halves both exist; ADR-024 governs the client resolver |
| 8 | Two files: `DistributedCacheExtensions.cs` + `RequestCache.cs` | The pattern files point at two code files, one for each cache kind |
| 10 | ADR-001 → ADR-019 | `.claude/adr/ADR-019-problemdetails.md` is the ProblemDetails ADR |
| 11 | ADR "—"; component is `GraphHttpMessageHandler` + `CircuitBreakerRegistry` | No resilience ADR exists; the only `DelegatingHandler` is Graph-specific (noted in the row) |
| 2 | ADR-007 dropped | ADR-007 governs `SpeFileStore` (row 13), not Graph OBO |
| 15 | `FileIndexingService` (not a pattern-file target) | The seed's premise that the pattern file is missing was wrong — `.claude/patterns/ai/indexing-pipeline.md` exists (my first listing hid it with a case-insensitive filter). `RagIndexingPipeline` is a second path → Known overlaps |
| 16 | ADR-043 added; symbol `ExecutorType` | The enum was renamed `ActionType` → `ExecutorType` (`d2f768e8d5`, 2026-06-28) |
| 17 | ADR-032 added; "root §10" dropped | §10 is not an ADR; the Null peer is ADR-032 |
| 18 | ADR-033 added | The streaming ADR; no shared SSE writer exists (Known overlaps) |
| 22 | `DataGrid` (not `DatasetGrid`) | The `DatasetGrid` folder holds only `ViewSelector.tsx`; `DatasetGrid` kept as a synonym |
| 24 | ADR-012 → ADR-023 | ADR-023 is the choice-dialog ADR |
| 25 | ADR "—" | No ADR governs the Navigator; the architecture doc is linked |
| added | Row 31 infinite scroll (ADR-051), row 32 user-record membership (ADR-034) | Each has a pattern file and an ADR, and both get rebuilt under other names |

No seed capability lacked a canonical component (escalation threshold: > 5), and the checker does scan `.claude/patterns/*.md` (second escalation trigger), so neither trigger fired.

## Found in passing (out of scope; filed + reported)

- ISS-001 (#2683): `.claude/patterns/ai/node-executor-authoring.md` still names the `ActionType` enum and `SupportedActionTypes`. The code renamed them to `ExecutorType` / `SupportedExecutorTypes` on 2026-06-28 (`d2f768e8d5`); `NodeService.MapCanvasTypeToActionType` kept its name. An agent following the pointer looks for an enum that does not exist.
- ISS-002 (#2684): the two checker false positives above.

rework: none task 001
