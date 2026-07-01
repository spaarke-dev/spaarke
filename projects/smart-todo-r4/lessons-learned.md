# smart-todo-r4 — Lessons Learned

> **Date**: 2026-06-29
> **Status**: R4 complete (PR #406 squash-merged at `80f70a1d4`; closeout-wave commits 027db5d12 → eda9a00b2 → d8c3a468f merged via Path B direct merge at master `9d0ed559a`)
> **Outcome**: 38 R4 tasks closed end-to-end + 7 closeout-wave tasks (R4-110→R4-118 + post-deploy search-filter hotfix). R4-115 deferred to `pcf-orphan-cleanup-r1` sibling project; ribbon expansion (R-9) + test-honesty refactor (R-10) deferred to R5 with concrete plans. Net: R4 scope shipped + 13 UAT rounds resolved.

---

## What went right

1. **The R5 backlog (`projects/smart-todo-r5/design.md`) was kept current from day-one of UAT.** Every UAT issue not in R4 scope was filed against R5 with concrete repro + fix plan, NOT held as a TODO comment. By closeout, R5 had 10 well-scoped entries (F-1→F-8, FU-2/FU-5, PROC-1, R-9, R-10). The discipline meant the R4 → R5 boundary was clean: nothing was ambiguous about "what shipped vs what's deferred."

2. **Pattern D (dual-use widget) shipped as designed.** The `@spaarke/smart-todo-components` peer package + thin LegalWorkspace shim pattern (R4-020) worked end-to-end. The widget on SpaarkeAi dashboard and the kanban in the standalone Code Page share the same `KanbanCard` + `useKanbanColumns` + `KanbanBoard` source. When the yellow-score dark-text fix needed to apply to both surfaces, it shipped once.

3. **R4-110 height-chain audit + chain-robustness fix in shared lib.** Made the structural contract "forgiving" — widget authors can use either `flex:1` OR `height:100%` on widget roots and the chain propagates. Documented in `BUILD-A-NEW-WORKSPACE-WIDGET.md §7.2` + `.claude/patterns/ui/embedded-widget-sizing.md` (3-rule contract simplified to 2). Prevents the rounds 7-12 height-chain debug arc from recurring on future widgets.

4. **"No shims" rule produced concrete cleanups this round.** Three real artifacts addressed:
    - `sprk_todo_dirty_check.js` (deployed v1.1.0 but never registered on form OnLoad) → source + Dataverse webresource DELETED. Parent-side `Xrm.Page.ui.close` patch in `SmartTodoModal.tsx` was the actual working path all along.
    - `sprk_wizard_commands.js` (source-committed but never deployed → Matter "Create To Do" button returned 404) → deployed via `Deploy-WizardCommandsJs.ps1`. Button now functional.
    - `RegardingResolver` Solution scaffold (PCF was deployed but its packaging infrastructure was missing — likely deployed via `pac pcf push` originally) → built proper Solution/ scaffold mirroring `AssociationResolver`. Future redeploys are now reproducible.

5. **R4-114 jest wiring closed the test-shim gap.** 4 SmartTodo test files existed with `jest.fn()`/`jest.Mocked<>` API but no runner — they were docs-disguised-as-tests. Wiring jest@30 + ts-jest + jest-environment-jsdom made them real (77 tests pass, 1 documented `.skip` deferred to R5 R-10). The skip rationale + future-fix plan is at the test site.

6. **Solution scaffold pattern reuse paid off for RegardingResolver.** Cloning AssociationResolver's Solution/ structure (solution.xml, customizations.xml, [Content_Types].xml, pack.ps1) took ~15 min instead of researching the Dataverse Custom Control solution format from scratch.

## What was harder than expected

1. **The 5-round entity-name bug (R3 → R4 carry-over): `sprk_contact` vs `contact`.** The `useCurrentContactId` hook queried `sprk_contact` entity with `sprk_contactid`/`sprk_name` fields. None of those exist — actual schema uses OOB `contact` (extended with custom `sprk_systemuser` lookup), fields `contactid`/`fullname`. Hook returned null → `useTodoItems` filtered by null contactId → 0 records on the kanban + 14-row data-isolation breach (`buildSmartTodoQuery` fell back to `activeClause`-only). **Root cause of duration**: the spaarke-prototype UAT mock harness seeded a `sprk_contact` row with the wrong field names, masking the entity-existence bug entirely. **Lesson**: prototype mocks against the wrong schema = worse than no mocks. **Mitigation**: PROC-1 in R5 ("real-Dataverse smoke before merge").

2. **The 7-round widget height-chain debug arc (rounds 7-12).** Workspace widget collapsed to 40px because `WorkspaceLayoutWidget.root` had `flex:1` but its parent was `display:block` (flex ignored). The fix iterated through layers: defensive `calc(100vh - 200px)` on the SmartTodo section → R4-110 audit + chain-robustness at `WorkspaceTabManagerComponent.content` → follow-up removing `minHeight:0` from `WorkspaceShell.row` after multi-widget overlap regression. **Lesson**: CSS flex chains break silently — `display:block` parent silently nullifies `flex:N` children. Documented in shared-lib pattern doc; future widget authors get the contract upfront.

3. **Form-script registration miss: `sprk_todo_dirty_check.js`.** Script was deployed to Dataverse but never registered as a form OnLoad handler. Console showed zero `[SmartTodo.DirtyCheck]` log messages on form load. Save & Close cascade-navigated the parent page until round 9's parent-side `Xrm.Page.ui.close` monkey-patch (the workaround that became permanent). **Lesson**: deploying a JS web resource ≠ wiring it. Form-designer binding is a separate step that's easy to skip. Captured in R4-113 (deletion of the orphan script).

4. **The bind-key PascalCase issue (UAT round 13).** `@odata.bind` requires PascalCase navigation property names (`sprk_AssignedTo`) but our code passed lowercase column names (`sprk_assignedto`). Dataverse rejected the request with a generic "type-mismatch" error. **Lesson**: when you see "type mismatch" from `@odata.bind`, check casing first — navigation property names are case-sensitive PascalCase per the schema metadata, NOT lowercase column names. Documented in MCP `describe('tables/sprk_todo')` output for future reference.

5. **PCF `noAposStringType` XSD failure mode was novel.** RegardingResolver v1.2.0 manifest import was rejected: `'description-key' attribute is invalid - The value 'Lookup to sprk_recordtype_ref — the resolver discriminator. Bind to the host entity's...' is invalid according to its datatype 'noAposStringType'`. Dataverse PCF manifest XSD forbids apostrophes in attribute VALUES (XML comments are fine — XSD skips them). Burned ~10 min on first import. **Mitigation**: captured in `.claude/skills/pcf-deploy/SKILL.md` Failure Modes table + `.claude/CHANGELOG.md` entry. Future PCF authors get the warning upfront.

6. **Worktree-staleness risk surfaced during deploy verification.** Mid-closeout, asked "what's actually deployed?" — answer required comparing deployed bundle to current source. The worktree HEAD was `f273d9d61` but master was `4844240b4` (17 commits ahead). Source-side grep returned stale results that misled the initial analysis. **Lesson**: when verifying a deployment, ALWAYS `git fetch origin && git log HEAD..origin/master --oneline` first to confirm worktree freshness. The deploy verification skill (or a future check) should make this automatic.

7. **The search-filter wiring bug shipped to production through 13 UAT rounds.** `SmartTodoApp.tsx` owned `searchQuery` state and passed it to `<Header>` (which wrote to it correctly), but **never forwarded the prop to `<SmartToDo>`** — so typing in the SearchBox updated state but the filter predicate never received the query. The kanban widget had its own internal filter so the bug was invisible there; only the standalone Code Page exhibited it. **Why it escaped**: the user kanban test surface was the widget (where filter worked); the Code Page surface was tested for OPEN/render but not for filter behavior. The bug was a prop-wiring oversight, not a logic bug. **Mitigation**: PROC-1 (real-Dataverse smoke per merge) + R5 R-10 (un-skip the related ToolbarActions test).

## Process notes

- **MCP-driven Dataverse verification was a force-multiplier.** Schema verification via `describe('tables/sprk_todo')`, contact resolution via `read_query`, and orphan-webresource deletion via `delete_record` all happened mid-session without context-switching to the maker portal. Net-saved 30-60 min of UAT-cycle latency per check.

- **Two-path deploy discipline held**: PCF v1.2.0 deployed via `pac solution import` (Solution scaffold workflow); JS web resources + icons deployed via PowerShell scripts (`Deploy-WizardCommandsJs.ps1`, `Deploy-WebResourceInline.ps1`); user uploaded smarttodo.html manually via maker portal (last-mile). Each path is reproducible; no path required ad-hoc maker portal clicking beyond the smarttodo upload.

- **Closeout-wave commit cadence (8 commits over 3 days)** was small enough to merge to master via Path B direct merge instead of PR. Branch protection on `master` was disabled this session (per /merge-to-master skill detection); this remains the path-of-least-resistance when CI gates can be locally verified.

- **R4-115 deferral pattern worked.** Cross-project orphan (SpeDocumentViewer PCF webresources deployed but source removed by `pcf-orphan-cleanup-r1`) was correctly assigned to that sibling project's owner instead of bleeding R4 scope. The "wait for the project that owns the source" rule prevented scope creep + future merge conflicts.

## Carry-forward items

| Item | Status | Owner |
|---|---|---|
| **R5 R-9** Ribbon expansion: 5 new entity-ribbon solutions + Matter icon swap | Documented in R5 design.md with concrete plan | R5 project (next) |
| **R5 R-10** ToolbarActions handleEmail injectable-seam refactor + RegardingResolver defensive cleanup | Documented in R5 design.md with concrete plan | R5 project (next) |
| **R5 F-1..F-8** Visual + filter UX items (yellow contrast on PriorityScoreCard, Completed status, filter pane redesign, Priority/Effort fields, inner-modal sizing) | All filed in R5 design.md from R4 UAT rounds 5-13 | R5 project (next) |
| **R5 FU-2** RecordNavigationModalShell chromeMode | Filed in R5 design.md | R5 project (next) |
| **R5 FU-5** LW Kanban rich-feature hoist | Filed in R5 design.md (shared-lib elevation principle) | R5 project (next) |
| **R5 TEST-1 / TEST-2** Vitest expansion + Playwright NFRs | Filed in R5 design.md | R5 project (next) |
| **R5 PROC-1** Real-Dataverse smoke before merge | Filed in R5 design.md — addresses items #1, #4, #7 above | R5 / cross-cutting infra project |
| **SpeDocumentViewer orphan PCF webresources on Dataverse** | Deferred to `pcf-orphan-cleanup-r1` sibling project; coordinate with that owner | Sibling project owner |

## Decisions worth re-using in R5 and beyond

1. **The R5 design.md backlog pattern.** Maintain the next-cycle design doc as a live UAT-overflow artifact. When a UAT issue is out-of-scope-for-now, write it as a one-paragraph "R-NN" entry with concrete repro + proposed fix. By closeout the design doc IS the next cycle's spec input. Mirror for R5 → R6.

2. **Solution scaffold per PCF (no-shims).** Every PCF MUST have its own `Solution/` directory with `pack.ps1`, even if it was originally deployed via `pac pcf push`. The scaffold is the reproducibility contract. R4-112 built one for RegardingResolver; the next PCF to be created should start with the scaffold from day one.

3. **Two-rule contract for embedded widgets** (post R4-110). `BUILD-A-NEW-WORKSPACE-WIDGET.md §7.2` codifies: (1) widget root MUST be `display:flex` OR have `height:100%`, (2) shell side MUST cascade determinate height. Future widget authors don't have to discover the chain — the contract is upfront.

4. **PCF manifest authoring rules** (post R4-112 XSD failure). NEVER use apostrophes in `description-key` attribute values; comments are fine but attribute values must be apostrophe-free. Capture goes in `.claude/skills/pcf-deploy/SKILL.md` Failure Modes table.

5. **Worktree-freshness check before deploy verification.** When asked "what's deployed vs source?" — first action MUST be `git fetch origin && git log HEAD..origin/master --oneline` to confirm worktree is current. Otherwise grep results may be stale.

6. **Real-Dataverse smoke as a merge gate** (PROC-1). Mock/prototype data is structurally insufficient for schema-correctness validation. Adding a pre-merge step that runs one CREATE + one READ against real Dataverse would have caught items #1, #4, and #7 above. Worth its own infrastructure project.

---

*R4 ships. R5 design backlog has 10 well-scoped entries. Process improvements (PROC-1, no-shims discipline, worktree-staleness check) carry forward.*
