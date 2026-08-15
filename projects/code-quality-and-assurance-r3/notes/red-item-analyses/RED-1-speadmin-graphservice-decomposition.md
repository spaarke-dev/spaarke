# RED-1 — Decompose `SpeAdminGraphService` (God-class, 4,911 LOC)

> **Type**: remediation-project seed · **Origin**: code-quality-and-assurance-r3 post-program review (2026-08-15)
> **Surface**: BFF (`Sprk.Bff.Api`) · **Effort**: L · **Value**: High (unblocks the God-class ratchet floor)

## Summary

`src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs` is the single largest
production file in the codebase at **4,911 lines / ~102 public methods / 0 `#region` structure**. It is
the #1 God-class and the reason `GodClassGuardTests` cannot ratchet its ceiling below ~2,700 (it is the
top waiver entry). It concentrates unrelated SharePoint-Embedded admin concerns behind one type.

## Evidence

- LOC: 4,911 (measured 2026-08-15; frozen as a `GodClassGuardTests` waiver).
- ~102 public members; dominant concern tokens by frequency: **Storage/quota (99), Containers (85+57+41),
  Permissions (83), ContainerTypes (61+52), DriveItems (37)**.
- No internal partition (`0` regions, one flat class). Depends on KV secret resolution + Graph client
  build/cache + Dataverse config reads (per the r3 config-deployment assessment D1 finding).

## Why it matters (concrete failure modes without it)

1. Any change to one concern (e.g. container permissions) forces a reviewer to reason about 4,900 lines
   of unrelated surface — review quality degrades, regressions slip.
2. The God-class ratchet (`GodClassGuardTests`) is stuck: the ceiling can't drop toward best-practice
   (~500-800 LOC) while this file anchors the floor at 4,911.
3. Testability: a 102-method class is near-impossible to unit-test at the seam; it forces integration-only
   coverage.

## Proposed approach (behavior-preserving decomposition)

Split by the concern boundaries the method census already reveals, keeping `IDataverseService`-style
seams and the existing public contract:

| New service | Concern | Rough method share |
|---|---|---|
| `SpeAdminContainerService` | container CRUD + list + storage/quota | ~40% |
| `SpeAdminContainerTypeService` | container-type config + registration + settings | ~25% |
| `SpeAdminPermissionService` | permission grants/reads | ~20% |
| `SpeAdminDriveService` | drive-item operations | ~10% |
| `SpeAdminGraphClientFactory` (shared) | KV-secret resolution + Graph client build/cache | ~5% (extract the cross-cut) |

Options for the transition: (a) **partial classes first** (`SpeAdminGraphService.Containers.cs`, etc.) as
a zero-risk mechanical split that immediately drops each file below the ceiling, then (b) extract true
services behind interfaces where a consumer boundary exists. Recommend (a) as phase 1 (fast, safe,
ratchets the guard) then (b) opportunistically.

## Risks & mitigations

- **Risk**: it's a live SPE admin path; a behavior change breaks container/permission ops. **Mitigation**:
  partial-class split is byte-for-byte behavior-preserving; verify route-dump + the SpeAdmin integration
  tests (`Phase2IntegrationTests`, `ContainerTypeEndpointsTests`) stay green.
- **Risk**: the KV-secret/Graph-client cross-cut is entangled. **Mitigation**: extract the factory last;
  keep the cache semantics identical (ADR-009).

## Acceptance criteria

- No single resulting file > 2,700 LOC (remove the `SpeAdminGraphService.cs` waiver from `GodClassGuardTests`).
- Public contract unchanged (route-dump identical; SpeAdmin integration tests green).
- Publish size neutral; no new NuGet.

## Dependencies / coordination

Standalone; BFF-hot-path (`/conflict-check` before PR). No overlap with the Compose or Communication
worktrees. Sequence AFTER any in-flight SpeAdmin feature work.
