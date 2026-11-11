# Defer / Issue Tracking — reuse-governance-tightening-r1

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label reuse-governance-tightening-r1` (visible to whole team via portfolio board)
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it.

---

## Open (in priority order)

### ISS-001 — node-executor-authoring.md still names the renamed `ActionType` enum

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-11 |
| **Source** | Task 001 (capability index row 16) verification; rename commit `d2f768e8d5` (2026-06-28) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2683 |

**Description**

`.claude/patterns/ai/node-executor-authoring.md` tells an agent adding a playbook node executor to add a value to the `ActionType` enum in `INodeExecutor.cs` and to set `SupportedActionTypes`. Commit `d2f768e8d5` renamed them to `ExecutorType` and `SupportedExecutorTypes`. `NodeService.MapCanvasTypeToActionType` kept its name. Failure: an agent following the pointer looks for an enum and a property that do not exist, then either stalls or recreates them under the old names.

**Entry-points**

- `.claude/patterns/ai/node-executor-authoring.md:9,12,16,25,34`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs:26` (`SupportedExecutorTypes`), `:128` (`enum ExecutorType`)

**Suggested fix**

Rename `ActionType` → `ExecutorType` and `SupportedActionTypes` → `SupportedExecutorTypes` in the pointer; keep `MapCanvasTypeToActionType` (still the method name).

**Estimated effort**: 15 minutes
**Blockers**: none
**Related**: `.claude/adr/ADR-043-ai-capability-execution-spine.md`; `.claude/patterns/CAPABILITY-INDEX.md` row 16

---

### ISS-002 — Test-InstructionPaths.ps1 exits 1 on master with two false positives

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-11 |
| **Source** | Task 001 checker runs (`notes/001-index-proof.md`); both findings present on `origin/master` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2684 |

**Description**

`scripts/quality/Test-InstructionPaths.ps1` reports two "new" missing paths that exist:
1. `.claude/adr/ADR-028-spaarke-auth-architecture.md -> src/server/shared/Spaarke.Core/Auth/CallerIdentity.cs:113,247-249`. The file exists; the suffix stripper (`:\d+(-\d+)?$`) does not handle a comma list of lines (added in `209ee27e0c`).
2. `.claude/FAILURE-MODES.md -> .claude/worktrees` (line 1185). This is a runtime folder that is untracked and not gitignored (added in `fafbad73d7`).

Failure: the ratcheted check exits 1 on every branch, so its advisory CI result and `doc-drift-audit` are always red. A real new dead path is then indistinguishable from the standing noise.

**Entry-points**

- `pwsh -File scripts/quality/Test-InstructionPaths.ps1`
- `scripts/quality/Test-InstructionPaths.ps1:82` (suffix stripping)

**Suggested fix**

Extend the line-suffix regex to comma lists (`:\d+(-\d+)?(,\d+(-\d+)?)*$`) with a must-fire/must-not-fire check. For `.claude/worktrees`, either gitignore the folder (it holds agent worktree copies) or reword the reference so it is not a checkable path.

**Estimated effort**: 30–60 minutes
**Blockers**: none
**Related**: root `CLAUDE.md` §16 (enforcement ladder: guards ship with must-fire/must-not-fire controls)

---

## Closed
