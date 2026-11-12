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

### ISS-002 — Test-InstructionPaths.ps1: 2 false positives on master, and 11 dead directory paths it misses on Windows

| Field | Value |
|---|---|
| **Status** | In Progress: fix in PR #2714 (`fix/instruction-paths-2026-11-11`, by the mcp-r1 session at the owner's request); closes #2684 on merge. Flip to Done then. |
| **Urgency** | next-round |
| **Filed** | 2026-11-11 |
| **Source** | Task 001 checker runs (`notes/001-index-proof.md`); both findings present on `origin/master` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2684 |

> 2026-11-11: #2714 was reviewed here and adopted. It uses the same child-path probe and comma-list strip, plus a start-up self-test, and makes the 11 text fixes. Seeded controls on Windows passed (3 must-fire, 6 must-not-fire). A duplicate local fix was dropped; it is saved outside the repo. Three stale non-path lines were sent to mcp-r1 for #2714: ADR-024 ~220 and ~241, and the full `docs/adr/ADR-011` line 240. Correction to item 2 below: `.claude/worktrees/` *is* gitignored (since 2026-07-16). The false positive came from a folder-only pattern not matching a path written without its slash, which the probe now fixes.

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

#### Update 2026-11-11: 11 more dead paths that the checker hides on Windows

On the Linux CI runner the check reports **13** new missing paths, not 2 (Tier 2 "Markdown Link Validator" job [130795653236](https://github.com/spaarke-dev/spaarke/actions/runs/43101945192/job/130795653236): "Missing paths: 36 (baseline 23, new 13)"). A Windows checkout reports only the 2 above.

##### Why Windows and CI disagree (a defect in the checker)

`Test-InstructionPaths.ps1:89` skips a path when `git check-ignore -q --no-index -- $p` exits 0. On Windows with `core.autocrlf=true`, `.gitignore` is checked out with CRLF endings (`i/lf w/crlf`). There, git 2.51.0.windows.1 reports **any** path that ends in `/` as ignored, and attributes the match to a blank line (`.gitignore:173`, `src/client/pcf/.gitignore:21`). Even `zzz/nothing/` matches. The same path without the slash is not ignored. So on Windows the checker silently skips every directory reference. The 2026-10-08 baseline was recorded that way, which is why these 11 references (some dating from 2025-12) were never in it. On Linux they are found.

**Fix:** make the ignore test give the same answer on both platforms. For example, test the path without its trailing `/` first and handle directory-only patterns explicitly, or read the ignore rules from the index rather than the CRLF working-tree files. Add a must-fire control: a dead `dir/` reference must be reported on Windows.

##### The 11 references

**Dead: the text should change (7)**

| Instruction file | Path named | What happened |
|---|---|---|
| `.claude/adr/ADR-011-dataset-pcf.md` | `src/client/pcf/UniversalDatasetGrid/` | Control deleted 2026-06-05 (`c1c428e92b`, datagrid-framework-r1 #329) |
| `.claude/constraints/pcf.md` | `src/client/pcf/UniversalDatasetGrid/` | Same |
| `.claude/patterns/pcf/error-handling.md` | `src/client/pcf/UniversalDatasetGrid/control/` | Same |
| `.claude/adr/ADR-024-polymorphic-resolver-pattern.md` | `src/client/pcf/RegardingLink/` | Control deleted 2026-03-15 (`ded4e037c2`) |
| `.claude/skills/repo-cleanup/SKILL.md` | `docs/ai-knowledge/catalogs/` | Moved to `.claude/catalogs/` 2026-04-05 (`fb043944c9`) |
| `.claude/skills/spe-integration/SKILL.md` | `src/server/api/Sprk.Bff.Api/Infrastructure/SpeFileStore/` | Never existed. `SpeFileStore` is `Infrastructure/Graph/SpeFileStore.cs` |
| `.claude/skills/foundry-agent/SKILL.md` | `src/server/api/Sprk.Bff.Api/agents/workflows/` | Never existed. No Foundry workflow YAML is in the repo |

**Named on purpose, not created yet: reword so the path is not checkable, or baseline it (4)**

| Instruction file | Path named | Why it is absent |
|---|---|---|
| `.claude/adr/ADR-052-workload-placement.md` | `src/server/functions/` | Rule for where a future Functions project goes; none exists yet |
| `.claude/skills/adr-check/SKILL.md` | `src/server/functions/` | Same rule |
| `.claude/skills/devops-portfolio-status/SKILL.md` | `docs/portfolio/` | Created by `--snapshot` on first use |
| `.claude/skills/power-page-deploy/SKILL.md` | `src/client/external-spa/.site-download/spaarke-external-workspace/` | Created by `pac pages download`. It is not gitignored, so it may also need a `.gitignore` line |

##### Revised effort

2–3 hours: the checker fix with must-fire and must-not-fire controls, 7 text fixes, 4 rewordings or baseline lines, then the 2 original false positives. All edits are in `.claude/` and `scripts/quality/`.

---

### ISS-003 — power-page-deploy skill describes a retired Power Pages deploy for the external SPA

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-11 |
| **Source** | Found in passing while fixing ISS-002 (its `.site-download` path has never been in the repo) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2713 |

**Description**

`.claude/skills/power-page-deploy/SKILL.md` still tells agents to deploy `src/client/external-spa/` to a Power Pages Code Site with `pac pages upload-code-site`. Per `src/client/external-spa/README.md` § Deployment, the SPA now deploys to Azure Static Web Apps through `.github/workflows/deploy-external-spa.yml`. The skill's `--rootPath` folder has never been committed. Two other files it names (`scripts/Deploy-ExternalWorkspaceSpa.ps1`, `src/client/external-spa/powerpages.config.json`) do not exist and are already in the instruction-path baseline.

Failure: an agent working on `**/external-spa/**` (the skill's `appliesTo`) loads this skill and follows a deploy procedure that cannot run. The skill never mentions the real Static Web Apps deploy.

**Entry-points**

- `.claude/skills/power-page-deploy/SKILL.md` (frontmatter `appliesTo`, Quick Reference table)
- `src/client/external-spa/README.md` § Deployment; `.github/workflows/deploy-external-spa.yml`
- `git grep -n power-page-deploy -- .claude` (`INDEX.md:49`, `azure-deploy` 29/398, `deploy-new-release` 444, `office-addins-deploy` 189)

**Suggested fix**

The owner confirms that Power Pages is retired for the external SPA. Then archive the skill under `.claude/skills/_archived/` (move it, never delete). Repoint the four skills above at the Static Web Apps workflow, and remove the skill's baseline lines.

**Estimated effort**: 1–2 hours
**Blockers**: owner confirmation that Power Pages deploys are retired
**Related**: ISS-002 / #2684, PR #2714 (fixes only the false "committed to the repo" claim); `FAILURE-MODES.md` AP-1

---

## Closed
