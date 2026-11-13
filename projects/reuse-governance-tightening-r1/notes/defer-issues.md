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
| **Status** | Done: PR #2714 merged 2026-11-12 (`a654da59c4`); #2684 closed. |
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
| **Status** | Done 2026-11-12 — merged to master in PR #2751 (`8c05f5b70f`); #2713 closed. Owner confirmed Power Pages deploys are retired. Skill archived to `.claude/archive/2026-11-12/skills/power-page-deploy/` (repo archive convention, not `_archived/`); INDEX row removed; `azure-deploy`, `deploy-new-release`, `office-addins-deploy`, two docs and `knowledge/sharepoint-embedded/NOTES.md` repointed at the Static Web Apps workflow; 2 baseline lines removed. |
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

### DEF-001 — Consolidate `@spaarke/sdap-client` into `@spaarke/ui-components`

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-13 |
| **Source** | Task 030 package review §1 (owner-approved "Approve all"); filed by task 034 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2801 |

**Description**

The owner approved consolidating the package into ui-components (`src/services/sdap/`, not `pcf-safe`). It fails T1 (the abstracted-I/O service tier ui-components already sanctions) and T2 (no dependencies), and T3 is weak (2 real importers: ui-components and DocumentUploadWizard; 16 of 17 solution listings unused). Out of r1 execution scope (FR-09). Findings: P-2 Medium (delete/download through two BFF route families), P-9 Low, P-10 Low. Failure without it: new BFF-client code can land in either package, and the two route families keep diverging with no single owner.

**Entry-points**

- `src/client/shared/Spaarke.SdapClient/src/operations/DeleteOperation.ts:22`, `DownloadOperation.ts:22`
- `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts:40`; `src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts:46`

**Suggested fix**: migration outline in the issue and `notes/package-reviews.md` §1; tasks 031–033 are the reference implementation.

**Estimated effort**: 1–2 days
**Blockers**: none
**Related**: ADR-012; DEF-006; ISS-004

---

### DEF-002 — Sanction `@spaarke/smart-todo-components` (ADR-012 Pattern D amendment)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-13 |
| **Source** | Task 030 package review §2 (owner-approved); filed by task 034 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2802 |

**Description**

The owner approved sanctioning this package through one ADR-012 amendment (Path B) for Pattern D feature packages, shared with DEF-003 and DEF-004. A package qualifies when it is consumed by 2+ surfaces, is domain-bound in a way ui-components may not be, and is reached by PCFs only through deep imports. This overturns design §6.2's consolidate hypothesis. The amendment is a follow-up PR, not task 080 (080 is review and PRs only; FR-09 limited r1 execution to three packages). Findings: P-8 Medium (~72 `sprk_*` literals vs ADR-012 MUST NOT), a likely duplicate `SmartToDo.tsx` (Medium, not diffed), P-10 Low. Failure without it: the census row stays "under review", so the package has no ADR basis and its entity literals breach ADR-012 with no recorded exception.

**Entry-points**

- `.claude/adr/ADR-012-shared-components.md` (census markers), `docs/adr/ADR-012-shared-component-library.md`, `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs`
- `src/solutions/SmartTodo/src/` (`SmartToDo.tsx`) vs `src/client/shared/Spaarke.SmartTodo.Components/src/`

**Suggested fix**: one amendment PR for DEF-002–004 (DEF-005 can ride along): flip the census rows to Sanctioned with the citation and update the census test in the same PR.

**Estimated effort**: half a day for the amendment (shared); 2–4 hours for the findings
**Blockers**: none
**Related**: DEF-003, DEF-004, DEF-005

---

### DEF-003 — Sanction `@spaarke/communication-components` (Pattern D amendment); de-duplicate regarding-write logic

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-13 |
| **Source** | Task 030 package review §3 (owner-approved); filed by task 034 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2803 |

**Description**

Sanction under the DEF-002 amendment; this is the strongest case (9 surfaces, including 3 PCFs through deep imports). Findings:
- P-3 Medium: the regarding-write helpers are duplicated between `ConnectionsWriteHandler` and the RegardingResolver PCF's `ResolverWriteHandler`.
- P-8 Medium: ~317 `sprk_*` literals.
- P-11 Low: an ai-widgets `dist/` path.
- P-12 Low: dead prototype code page `CommunicationPage`.
- P-13 Low: unused `@microsoft/signalr` peer.

Failure without it: the most widely shared feature package has no ADR basis, and P-3's parallel copies drift silently.

**Entry-points**

- `src/client/shared/Spaarke.Communication.Components/src/logic/connections/ConnectionsWriteHandler.ts:141,219,268`; `src/client/pcf/RegardingResolver/RegardingResolver/handlers/ResolverWriteHandler.ts`
- `src/client/code-pages/CommunicationPage/`; `src/client/shared/Spaarke.AI.Widgets/tsconfig.json:35-36`

**Suggested fix**: amendment PR (see DEF-002); extract the shared nav-prop / catalog / selection parts into one module; delete the prototype page; drop the peer.

**Estimated effort**: amendment shared; P-3 about 1 day; the rest 1–2 hours
**Blockers**: none
**Related**: DEF-002, DEF-004

---

### DEF-004 — Sanction `@spaarke/daily-briefing-components` (Pattern D amendment); use the shared nav-prop resolver

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-13 |
| **Source** | Task 030 package review §4 (owner-approved); filed by task 034 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2804 |

**Description**

Sanction under the DEF-002 amendment (Pattern D: the DailyBriefing code page and the LegalWorkspace section). Findings: P-4 Medium (`useInlineTodoCreate` keeps a private nav-prop discovery, one of seven copies in `notes/reuse-scan-dry-run.md`), P-8 Medium (~48 `sprk_*` literals), P-10 Low (unused SpaarkeAi listing). Failure without it: a nav-prop fix made in the shared resolver never reaches this copy, so inline to-do creation can bind the wrong navigation property after a schema change.

**Entry-points**

- `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useInlineTodoCreate.ts:151-165`

**Suggested fix**: amendment PR (see DEF-002); replace the private discovery with the shared resolver; remove the unused listing.

**Estimated effort**: amendment shared; P-4 2–4 hours
**Blockers**: none
**Related**: DEF-002, DEF-003

---

### DEF-005 — Sanction `@spaarke/compose-components` (ADR-012 amendment: editing model + TipTap quarantine)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-13 |
| **Source** | Task 030 package review §5 (owner-approved); filed by task 034 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2805 |

**Description**

The package passes all three tests: a distinct editing-model contract (`docxBridge.ts` exists only here), a TipTap/ProseMirror quarantine of 15 `@tiptap/*` packages, and 2 code-page surfaces. Sanctioning it settles capability-index row 30, which has "two answers" today. Findings: P-8 Medium (a few `sprk_*` literals in `ComposeWorkspace.tsx`), P-11 Low (`dist/` tsconfig paths for `@spaarke/auth` and `@spaarke/ai-widgets`), P-14 Low (`mammoth` pin may be unused; one hard-coded colour). Failure without it: the package that quarantines a heavy dependency has no ADR basis, and capability-index row 30 stays ambiguous.

**Entry-points**

- `src/client/shared/Spaarke.Compose.Components/tsconfig.json`, `package.json` (`mammoth`), `src/widgets/ComposeWorkspace.tsx`
- `.claude/patterns/CAPABILITY-INDEX.md` row 30

**Suggested fix**: amendment naming the contract and the quarantine (it can ride with DEF-002–004); census row → Sanctioned; census test updated in the same PR.

**Estimated effort**: 1–2 hours amendment; 2–3 hours findings
**Blockers**: none
**Related**: DEF-002

---

### DEF-006 — Consolidate `@spaarke/ai-outputs` into `@spaarke/ai-widgets`

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-13 |
| **Source** | Task 030 package review §7 (owner-approved); filed by task 034 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2806 |

**Description**

Move the package into ai-widgets (`src/outputs/`). Its only real importer is ai-widgets, and it fails T2 and T3. Findings:
- P-6 Medium: pane-event shapes are defined three times and `IChatSession` twice; the ai-context half was fixed by task 031.
- P-7 Medium: the registry metadata matches `OutputWidgetType` by string, not by import.
- Low: P-10, P-11, and no `exports` map.

Failure without it: renaming an `OutputWidgetType` value compiles and silently breaks the widget lookup, so that output widget stops rendering.

**Entry-points**

- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts:67`
- `src/client/shared/Spaarke.AI.Widgets/src/types/aiContext.ts:82,129`; `src/client/shared/Spaarke.AI.Outputs/src/types/index.ts:166-193`; `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/types.ts:178,203`

**Suggested fix**: migration outline in the issue; task 031 is the reference implementation.

**Estimated effort**: 1 day
**Blockers**: none
**Related**: DEF-001

---

### ISS-004 — Open-in-desktop is implemented on nine surfaces with different behaviour (P-1)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-13 |
| **Source** | Task 030 finding P-1; re-verified by grep in task 034 (three more call sites than the review listed) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2807 |

**Description**

Every surface calls `GET /api/documents/{id}/open-links`, but each handles the response differently:
- the shared hook falls back to `webUrl` in a new tab;
- LegalWorkspace and SpeDocumentViewer set `location.href`;
- the SemanticSearchControl PCF and DocumentRelationshipViewer use `window.open(…, '_self')`, and DocumentRelationshipViewer downloads through `/content` when there is no `desktopUrl`;
- the CommunicationAttachments PCF uses `desktopUrl || webUrl`;
- the legacy web resource `sprk_DocumentOperations.js` keeps its own copy.

The Office add-ins differ on purpose and are out of scope. Failure: the same click behaves differently by surface, and error-handling fixes in the shared hook do not reach the others. Moving document-operations into ui-components (task 033) did not fix this; each surface needs migrating and checking by hand.

**Entry-points**

- `git grep -n "open-links" -- src ':!**/bundle.js' ':!**/*.test.*'`
- `src/client/shared/Spaarke.UI.Components/src/hooks/useDocumentActions.ts:116,161-165`

**Suggested fix**: decide one behaviour, then migrate surface by surface. PCFs need a `pcf-safe`, React-free helper.

**Estimated effort**: 2–3 days
**Blockers**: none
**Related**: DEF-001 (P-2)

---

### ISS-005 — LegalWorkspace and SemanticSearch fail their own standalone tsc, and no gate runs it

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-13 |
| **Source** | Found in passing, tasks 032 and 033 (`notes/032-legal-workspace.md`, `notes/033-document-operations.md`) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2808 |

**Description**

LegalWorkspace's `tsc --noEmit` reports 234 errors (174 its own). Three matter:
- a re-export of the removed `ComposeWorkspacePlaceholder` in `sections/index.ts:23`, which fails the bundle for its first importer;
- a type import of the missing `NarrateRequest` in `sectionRegistry.ts:34`;
- deep `@spaarke/ai-widgets/...` imports its tsconfig cannot resolve.

SemanticSearch reports 23 × TS6059 (`@spaarke/auth` source outside `rootDir`) and 2 × TS2352 in `src/index.tsx:60,109`. Both builds are bundler-only, so a type error reaches review only if it also breaks bundling.

**Entry-points**

- `cd src/solutions/LegalWorkspace && npx tsc --noEmit -p .`
- `cd src/client/code-pages/SemanticSearch && npx tsc --noEmit -p .`

**Suggested fix**: fix the listed items, add the missing types, then decide on a typecheck step for each project.

**Estimated effort**: 2–4 hours plus the gate decision
**Blockers**: none
**Related**: —

---
## Closed
