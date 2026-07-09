# Deferrals + Issues — spaarkeai-compose-r2

> Track deferred work + newly-discovered issues in BOTH this file (source of truth) AND GitHub Issues (visibility), kept in sync via `/project-defer-issue-tracking` (alias `/defer`). Every entry MUST name a concrete failure mode (CLAUDE.md §11). `push-to-github` blocks push on entries lacking a GitHub URL.

## Deferrals filed during execution

| ID | Title | GitHub Issue | Root cause | Recommended action | Cost-of-doing-nothing |
|---|---|---|---|---|---|
| DEF-01 | `ComposeEditor` `docxBytes` effect resets `dirty=false` after a transient mount | [#601](https://github.com/spaarke-dev/spaarke/issues/601) | Pre-existing latent mechanism (shared with merged FR-03/task-012 upload path); surfaced in task 010 review. `ComposeEditor.tsx`'s `docxBytes` effect unconditionally calls `onDirtyChange(false)` after mammoth conversion, including for transient (Browse/Upload) mounts. Tests mock `ComposeEditor` so the reset never fires in test. | Patch `ComposeEditor` to skip the dirty-reset when the mount is transient (e.g. `documentRef.speDriveItemId` empty); verify live in a deployed env. | A Browse/Upload transient draft can report `isDirty=false` in production → first Save does not trigger create-on-save (FR-05/task 013) → the draft silently fails to persist. |
| DEF-02 | `searchResolvedDriveId` has no reset path on `loadFailed`→retry-Search within one mount | _(filed at next push)_ | Task 011 review (non-blocking robustness note, not an ADR violation). The new `searchResolvedDriveId` override (FR-02 Search load) is reset on Browse/Upload transient mounts but not on a load-failure→retry-Search sequence in the same mount instance. | When a later task composes multiple entry-path transitions in one mount (013 create-on-save era), add a reset on load-failure/retry. | Today no live consumer exercises the combination (013 not built); a stale drive-id could target the wrong drive if a failed Search load is retried in-place. |

## Open architectural escalations (owner/architect decision pending — NOT DEFs)

### Task 013 (FR-05 create-on-save) — BLOCKED on three coupled forks (2026-07-09)
Task 013 as scoped assumes a **server-side** BU→SPE-container primitive that does not exist. Grounding (Step 0) surfaced three coupled forks requiring owner/architect resolution before coding (POML pre-authorized this escalation; spec marks Unresolved Q#2):
- **Fork A — BU→container**: the "same mechanism as matter/project creation" is **client-side** (`resolveBusinessUnitContainerId` reads `businessunit.sprk_containerid`, passes the id into the BFF). BFF has no BU→container resolver by design (multi-container INV-7 + project CLAUDE.md keep it in the wizards). Recommended: client resolves container id and passes it into the Save/promote request (this IS the existing convention — §6.5 path C, pivot-to-comply). Reject a new BFF resolver.
- **Fork B — transient contract**: `SaveAsync` requires an existing `DocumentSpeId`+`DriveId`; a transient Browse/Upload draft (task 010/012 output) has no SPE item. Save must create the drive-item in the resolved container when `DocumentSpeId` is absent. Client half couples to task 010/012.
- **Fork C — profile analysis**: only seam is `IAppOnlyAnalysisService` (`Services/Ai/`, not `PublicContracts/`) — injecting into `Services/Compose` trips the ADR-013 NetArchTest facade rule, and it is app-only/MI (likely 403s reading the OBO-written Compose file). Recommended: new thin `Services/Ai/PublicContracts/IDocumentProfileAi` facade wrapping the stream overload, run under OBO. **New `Services/Ai/PublicContracts/` surface in core's territory → route to core (redesign-r2), not compose-minted.**

Already clean/buildable once A/B/C settle: indexing (`IPostUploadIndexingEnqueuer`, OBO), record step (idempotent), per-step result shape (`JobAwareCompletionState` already names Compose's 4 steps `container → record → profile-analysis → indexing`).

**Awaiting owner decision**: (1) re-scope 013 to A+B backbone (compose-owned) + file C to core, or (2) hold all of 013 until core weighs in on C.

## How to file NEW deferrals

Invoke `/project-defer-issue-tracking` (or `/defer`). Every entry MUST name a concrete failure mode per CLAUDE.md §11. NEVER add an entry only here without filing the corresponding GitHub Issue.
