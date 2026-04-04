# Current Task — Production Release Procedure

## Active Task
- **Task**: PRPR-010 - Create Production Release Procedure Guide
- **Task File**: tasks/010-procedure-guide.poml
- **Phase**: 2
- **Status**: in-progress
- **Started**: 2026-04-04
- **Rigor Level**: FULL
- **Reason**: 14 steps, critical blueprint document

## Quick Recovery
- **Current Step**: Step 14 of 14: Review and verify
- **Next Action**: Verify acceptance criteria
- **Files Modified**: docs/procedures/production-release.md (created)
- **Critical Context**: Procedure guide complete, covers all 6 phases, rollback, hotfix, versioning

## Completed Steps
- [x] Step 1: Study existing docs (loaded all deploy guides via research agents)
- [x] Step 2: Study Provision-Customer.ps1 (understood 13-step pipeline, aligned Deploy-Release as update counterpart)
- [x] Step 3: Study all deploy scripts (14 scripts with exact params/invocations)
- [x] Step 4: Draft procedure structure (6 phases + rollback + hotfix + versioning)
- [x] Step 5: Write pre-flight checklist (tools, auth, git, CI, BFF URL validation, change detection)
- [x] Step 6: Write release phases (Phase 0-6 with dependency diagram and gates)
- [x] Step 7: Write per-component deployment (exact commands for all scripts)
- [x] Step 8: Write multi-environment flow (sequential loop with stop-on-failure)
- [x] Step 9: Write rollback procedures (per-component + full rollback)
- [x] Step 10: Write emergency hotfix path (abbreviated deploy for single component)
- [x] Step 11: Write first-time vs subsequent matrix
- [x] Step 12: Write versioning strategy (git tags, semver, change detection)
- [x] Step 13: Add cross-references (CUSTOMER-DEPLOYMENT-GUIDE, CUSTOMER-ONBOARDING-RUNBOOK, PRODUCTION-DEPLOYMENT-GUIDE, INCIDENT-RESPONSE)
- [x] Step 14: Review and verify

## Files Modified This Session
- docs/procedures/production-release.md (CREATED — 550+ lines)

## Decisions Made
- Phase diagram shows Phases 2-5 in per-environment loop (not linear)
- BFF URL validation is in pre-flight, not per-phase
- Included environment registry inline example for reference
- Referenced Deploy-Release.ps1 and /deploy-new-release skill (will be created in later tasks)
- Added Deploy-SmartTodo, Deploy-ThemeIcons, Deploy-ThemeMenuJs as "additional" web resources outside main orchestrator

## Knowledge Files Loaded
- All 14 deploy scripts (via research agent)
- CUSTOMER-DEPLOYMENT-GUIDE.md, CUSTOMER-ONBOARDING-RUNBOOK.md, PRODUCTION-DEPLOYMENT-GUIDE.md
- CI/CD workflow, BFF URL normalization pattern
- Complete client component inventory (20 Vite, 4 webpack, 14 PCF, 1 external SPA, 3 shared libs)
