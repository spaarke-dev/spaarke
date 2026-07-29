# Task 010 — Upload path returns a projection (FR-01 / WS-1) — implementation notes

**Status**: implementation complete, build + seam tests green. This note is written by the
executing subagent, which is NOT permitted to edit `tasks/TASK-INDEX.md` or `current-task.md` —
the orchestrator/human should flip task 010 to ✅ there.

## Anchor drift found during re-grep (Step 1)

The design/POML line citations (`ComposeEndpoints.cs:913-920` + record `:1797-1804`) matched
almost exactly (`:834` Upload handler start, `:1788`/`:1797` request/response records) — no
meaningful drift. `IComposeService.cs:356-362` (the cited Load-path projection shape) turned out to
be doc-comment context around the `Projection` property, not a code line range — the actual shape is
`ComposeDocxProjection` (service-level, `Services/Compose/ComposeDocxProjection.cs`) and
`ComposeProjectionResponse` (wire-level DTO, `ComposeEndpoints.cs`), NOT a type literally named
`ComposeServerProjection` in C# (that name is the **client-side** TypeScript interface in
`compose-contracts.ts` the POML/spec use as shorthand for "the projection shape"). No escalation
needed — both C# types are exactly what Load already returns; the client TS type already existed
and was already used for the Load path.

## What "the same shape" meant in practice

`POST /api/compose/upload` (the assistant-upload / "open in Compose" transient-mount door) reads
retained bytes from `ITenantCache` — it does NOT go through `ComposeService.LoadAsync` (that's the
stored-document/SPE door). So it could not simply "call LoadAsync's projection" — a genuinely new
thin seam was needed. Added `IComposeService.ProjectDocument(ReadOnlyMemory<byte>, CancellationToken)
: ComposeDocxProjection`, a one-line wrapper around the SAME `_projectionBuilder` instance
`LoadAsync` already uses. This is an EXTENSION of the existing `IComposeService` facade (root
CLAUDE.md §11), not a new service/abstraction — no escalation needed; the shape was reused as-is.

`ComposeUploadResponse` gained a `Projection: ComposeProjectionResponse` field ADDITIVE to the
existing `Content: byte[]` field (NOT a replacement — `state.docxBytes` is still required for a
later create-on-save baseline; removing it would have broken Save). This matches what Load itself
does — Load returns `Content` AND `Projection` side by side; Upload now does too.

Extracted the wire-shape mapping (`ComposeDocxProjection` → `ComposeProjectionResponse`) from the
Load handler into a shared private `MapProjectionResponse` helper, reused by both Load and Upload —
avoids forking the mapping logic (root CLAUDE.md §11).

## Client wiring

`mountTransient` (the reducer action BOTH the assistant-upload door and the Browse-direct-upload
door dispatch) gained an optional `projection?: ComposeServerProjection | null` field. The reducer
now sets `projection: action.projection ?? null` instead of a hardcoded `null`. The assistant-upload
effect (`ComposeWorkspace.tsx`) now parses+normalizes `payload.projection` (mirroring the existing
Load effect's normalization) and passes it through. The Browse-direct-upload door (task 011, not yet
wired) omits `action.projection`, which still normalizes to `null` — unchanged mammoth-fallback
behavior for that door until 011 lands. No changes needed in `ComposeEditor.tsx` — its mount branch
already keys off `state.projection` generically for every door.

## No escalation fired

The Load-path `ComposeDocxProjection`/`ComposeProjectionResponse` shape was reused AS-IS for the
upload door — no divergence, no fork. The POML's escalation trigger ("cannot reuse the Load-path
shape as-is") did not apply.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors (23 pre-existing warnings, unrelated).
- `dotnet test --filter "FullyQualifiedName~Compose"` → 619 passed, 1 skipped (pre-existing,
  numbering harness pending WS-3), 0 failed.
- New seam test: `tests/integration/seam/Compose/ComposeUploadProjectionSeamTests.cs` (2 tests) —
  (1) proves upload→projection end-to-end AND that the SAME source bytes produce byte-identical
  projection HTML through the Load door too (the "one reader, not a forked shape" proof), (2) proves
  fail-closed behavior on unreadable retained bytes (never a 500).
- `dotnet publish -c Release` compressed (tar.gz) measured ~46.1 MB — no `.csproj` changes (`git
  diff --stat` on the csproj is empty), so **0 MB package delta** as expected for WS-1 (pure OOXML
  wiring on the existing `DocumentFormat.OpenXml` dependency, no new NuGet package). Absolute
  compression-tool differs from the ~49.63 MB zip-based baseline cited in root CLAUDE.md (this
  measurement used `tar czf`, not the Azure zip pipeline) — the delta-from-this-task claim (0 MB) is
  the load-bearing number; the absolute figure is not apples-to-apples across compression tools.
- TypeScript: no local `tsc` available pre-install; ran `npm install --legacy-peer-deps` then `npx
  tsc --noEmit` — 24 pre-existing errors (workspace-package resolution: `@spaarke/auth`,
  `@spaarke/ui-components`, `@spaarke/ai-widgets`, `@spaarke/document-operations` not linked in a
  standalone install; plus a few pre-existing implicit-any/unknown errors in untouched code).
  Confirmed via stash/pop A-B comparison that the error SET is byte-identical before and after this
  task's client edits (only a harmless line-number shift from the ~25 added lines) — zero new type
  errors introduced.
- `/conflict-check`: no open PR touches `ComposeEndpoints.cs`, `Services/Compose/**`,
  `ComposeWorkspace.tsx`, `ComposeWorkspace.types.ts`, or `ComposeEditor.tsx`. Sibling
  `spaarkeai-compose-r1/r2/r3/r4` worktrees (flagged in project CLAUDE.md as `Services/Compose/`
  co-owners) have no OPEN PR right now to diff against — pre-existing coordination note, not
  resolvable via PR-file-diff; unchanged risk carried forward.

## Placement Justification (root CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

All new surface area stays inside `Services/Compose/` (existing facade `IComposeService`/
`ComposeService`, one new method) and `Api/ComposeEndpoints.cs` (one new DI param + one new private
mapping helper on the existing static endpoint class). No new endpoint, no new DI registration
class, no new package. `Services/Compose/` remains pure: no AI-internal type touched (ADR-013), no
`Microsoft.Graph` above `SpeFileStore` (ADR-007) — `ProjectDocument` is `byte[]`-in/projection-out,
synchronous, no I/O.
