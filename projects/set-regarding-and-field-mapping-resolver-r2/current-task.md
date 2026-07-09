# Current Task — Set-Regarding and Field-Mapping Resolver R2

> **Context recovery file.** Updated by `task-execute` during task work. See `docs/procedures/context-recovery.md`.

## Active Task
- **Project**: set-regarding-and-field-mapping-resolver-r2
- **Task ID**: 090 (wrap-up) — next
- **Phase**: 5: Wrap-up
- **Status**: not-started
- **Rigor Level**: FULL

## Next Action
**090** wrap-up: project-wide code-review + adr-check, LIVE end-to-end verification (wizard-created record per mapping type + matter→matter + no-profile no-op + push regression), git-diff invariant check, /test-diet, README/plan → Complete, lessons-learned, repo-cleanup. Then merge decision. Live-verify + merge need owner engagement.

## Docs phase — COMPLETE
- **040** ✅ architecture doc `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` + root CLAUDE.md §17 pointer (added by main session) + CHANGELOG entry.
- **041** ✅ admin guide `docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md`.

## Completed this session (11 of 16 tasks)
- **001** ✅ schema `sprk_expression` (Memo 2000, nullable)
- **002** ✅ BFF DTO (+5 rule fields) — gates CLEAN
- **003** ✅ BFF tests + publish-size (7662 pass, 49.60 MB, no new CVE)
- **010** ✅ engine shell (context-agnostic `applyFieldMappings`, never-throws, dispatch seams)
- **011** ✅ nav-prop Path A (6-of-7 shared; matter→016)
- **012** ✅ Copy engine (scalar + lookup @odata.bind)
- **013** ✅ Default/Concat/Template (one `resolveExpression`, single source fetch)
- **014** ✅ same-entity support (no guard; verified)
- **015** ✅ engine unit tests (17 tests; caught+fixed FR-09 defect)
- **020/021/022** ✅ Wave B wiring (event/matter/project · todo/workAssignment · invoice/reportCard)
- **030** ✅ seed (all 3 pairs: Event 8 + Invoice 4 + Report Card 8; created missing Report Card `sprk_recordtype_ref` `5bc206a0-…` under owner approval)

## Verification (post-Wave-B, authoritative)
`npm run build` (@spaarke/ui-components) = 0 errors. 157 field-mapping/wizard tests green. Full-suite 16 failures confirmed PRE-EXISTING on master (WorkspaceShell/FilePreview/recordHeader/XrmDataverseClient/EntityCreationService.cascade — untouched here), proven via stash-baseline run. Zero regressions from this project.

## Remaining
- **040** architecture doc + root CLAUDE.md §17 pointer (deps 015/022/030 ✅)
- **041** admin authoring guide docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md (deps 030 ✅)
- **090** wrap-up (FULL: code-review + adr-check across project, live E2E verify, /test-diet, README/plan → Complete, repo-cleanup)
- **016** matterService nav-prop convergence — DEFERRED, awaiting owner go-ahead (§6.5; create-payload-touching, needs payload-equivalence test)

## Follow-ups surfaced during Wave B (fold into 090/docs)
- **021**: To Do follow-on child-creation path (`createTodoRegardingChild`, called from invoice/reportCard wizards) left unwired to respect 022's scope — gracefully no-ops today. Needs a follow-up task.
- **020**: Matter/Project parent link is POST-create N:N, not a pre-create regarding value — engine fires only when an `association` is present (forward-compatible; matter→matter needs the association passed to the service).
- **022**: ReportCardService + TodoService constructors extended with authenticatedFetch/bffBaseUrl (were dataService-only).
- Report Card reverse-lookup: registry `sprk_regardingfield=sprk_regardingreportcard` names the convention only; confirm if a physical column is needed.

## Notes
- Baseline: worktree synced to origin/master 2026-07-09 (0 behind). No plugins, no new PCF, BFF change additive-only (publish flat).
