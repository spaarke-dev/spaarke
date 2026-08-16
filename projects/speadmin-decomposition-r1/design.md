# Design — SpeAdmin GraphService Decomposition (R1)

> **Status**: INITIALIZED (design only) · **Surface**: BFF · **Origin**: r3 RED-1 seed

## Hot-Path Declaration (CLAUDE.md §10)

```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- decomposes Infrastructure/Graph/SpeAdminGraphService.cs -->
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives> <!-- edits tests/Spaarke.ArchTests (waiver removal), not .claude/ -->
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

## Problem

`src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs` is **4,911 LOC / ~102 public
members / no `#region` structure** — the largest production file and #1 God-class. It concentrates
unrelated SharePoint-Embedded admin concerns behind one type, plus a KV-secret-resolution + Graph-client
build/cache cross-cut. Method-name census shows dominant concerns: containers/storage, container-types,
permissions, drive-items.

## Goals

1. Split the class into cohesive per-concern services **without changing behavior or the public contract**.
2. Drop it below the 2,000-line God-class ceiling and **remove its `GodClassGuardTests` waiver** (ratchets
   the floor down permanently).
3. Improve testability (per-concern seams) and reviewability.

## Non-goals

- No behavior change, no new capability, no new endpoint/package.
- Not touching the SPE *feature* surface — only the admin-Graph service internals.

## Approach (phased, behavior-preserving)

**Phase 1 — partial-class split (byte-neutral, near-zero risk).** Move method groups into
`SpeAdminGraphService.Containers.cs`, `.ContainerTypes.cs`, `.Permissions.cs`, `.Drives.cs`, `.GraphClient.cs`
(same class, `partial`). Each file drops below the ceiling immediately; the guard's waiver for the monolith
is replaced/removed as the files land. Zero behavior change.

**Phase 2 — extract true services where a consumer boundary exists.** Behind interfaces:
`SpeAdminContainerService`, `SpeAdminContainerTypeService`, `SpeAdminPermissionService`,
`SpeAdminDriveService`, and a shared `SpeAdminGraphClientFactory` (the KV-secret + Graph-client cross-cut,
extracted last, cache semantics identical per ADR-009). DI registrations updated 1:1 (ADR-010 minimalism).

## Placement Justification (CLAUDE.md §11)

New files are **extractions of existing code**, not new capability — net-negative on complexity (one
4,911-LOC class → several ≤2,000-LOC cohesive units). No new service *capability*, endpoint, package, or
Dataverse surface. The interfaces added in Phase 2 are testing/decomposition seams (ADR-010), justified by
"a 102-method class cannot be unit-tested or reviewed at the seam."

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| Live SPE admin path — a behavior change breaks container/permission ops | Phase 1 is byte-for-byte; verify route-dump + SpeAdmin integration tests (`Phase2IntegrationTests`, `ContainerTypeEndpointsTests`, `UpdateContainerTypeSettingsTests`) green after each split |
| KV-secret/Graph-client cross-cut is entangled | Extract the factory LAST; keep the Redis cache semantics identical (ADR-009) |
| God-class ratchet trips mid-refactor | Update `GodClassGuardTests` waiver as each file drops below 2,000 — remove the entry, don't re-baseline up |

## Acceptance criteria

- No resulting file > 2,000 LOC; `SpeAdminGraphService.cs` waiver removed from `GodClassGuardTests`.
- Public contract unchanged (route-dump identical; SpeAdmin integration tests green).
- `dotnet build -c Release` 0 errors under the analyzer gate; ArchTests 38/0; publish size neutral; no new NuGet.

## Dependencies / coordination

Standalone; BFF-hot-path (`/conflict-check` before each PR). No overlap with Compose/Communication worktrees.
Sequence after any in-flight SpeAdmin *feature* work. Uses the repo INITIALIZE-ONLY pattern — worktree +
task breakdown created at execution start (`/design-to-spec` → `/project-pipeline`, or `/task-create`).
