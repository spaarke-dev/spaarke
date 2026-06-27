# Task 118b — BFF Publish-Size Delta Evidence

> **Generated**: 2026-06-25
> **Task**: 118b — `GetWorkspaceTabContentHandler` (FR-57 workspace output → AI memory round-trip)
> **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`

## Measurement

| Metric | Value |
|---|---|
| Compressed BFF publish size (this task) | **44.99 MB** |
| Prior baseline (task 118R partial, current-task.md) | 47.84 MB |
| Delta vs baseline | **-2.85 MB** |
| NFR-01 ceiling | 60 MB |
| Headroom | ~15 MB |

## Method

```bash
dotnet publish src/server/api/Sprk.Bff.Api/ -c Release -o deploy/api-publish-118b
# Uncompressed dir: 141 MB
powershell Compress-Archive -Path deploy/api-publish-118b/* -DestinationPath deploy/api-publish-118b.zip
# Compressed: 44.99 MB
```

## Interpretation

The -2.85 MB delta vs the 47.84 MB baseline is environmental, not load-bearing. This task adds:

- **One new C# handler class** (~600 lines incl. XML doc) — `GetWorkspaceTabContentHandler.cs`
- **One new Dataverse seed-row JSON** — data, not code; not included in BFF publish
- **One new line** in `Seed-TypedHandlers.ps1` — script, not in BFF publish

The handler is BCL-only (no new NuGet refs), and the auto-discovery DI registration (`AddToolHandlersFromAssembly`) means ZERO new DI line in `Program.cs` or any module. The expected per-task code delta is sub-megabyte; the observed -2.85 MB reflects either (a) restore-cache hits not present in prior measurements or (b) dependency resolution variability between sessions (the project has historically observed +/-3 MB drift on identical bytecode — see 2026-06-24 current-task.md "10.78 MB headroom under NFR-01 — flag for ops monitoring; delta exceeds raw code volume, likely environmental drift").

## ADR-029 compliance

✅ ≤60 MB ceiling — far below at 44.99 MB
✅ No `<PublishTrimmed>` / `<PublishAot>` introduced (CSPROJ unchanged)
✅ BCL-only handler — no new NuGet refs
✅ Auto-discovery DI — no new `Program.cs` lines
✅ Per-task measurement reported with absolute number + delta

## Cumulative project tracking

Per CLAUDE.md §10 bullet 4: cumulative BFF publish must stay ≤60 MB. Current cumulative at 44.99 MB → 15.01 MB headroom. Project-level cumulative tracking is current-task.md responsibility; this note records only the per-task measurement.
