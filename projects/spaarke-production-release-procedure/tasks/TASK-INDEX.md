# Task Index — Production Release Procedure

**Project**: spaarke-production-release-procedure
**Branch**: work/spaarke-production-release-procedure
**Total Tasks**: 11
**Status**: In Progress

---

## Task Registry

| # | ID | Title | Phase | Status | Dependencies | Estimate |
|---|-----|-------|-------|--------|--------------|----------|
| 001 | PRPR-001 | Project Setup | 1 | ✅ | — | 1h |
| 010 | PRPR-010 | Create Production Release Procedure Guide | 2 | ✅ | 001 | 4h |
| 020 | PRPR-020 | Create Environment Registry | 3 | ✅ | 010 | 2h |
| 021 | PRPR-021 | Create Build-AllClientComponents.ps1 | 3 | ✅ | 010 | 4h |
| 022 | PRPR-022 | Create Deploy-AllWebResources.ps1 | 3 | ✅ | 010 | 3h |
| 023 | PRPR-023 | Create Deploy-Release.ps1 | 3 | ✅ | 020, 021, 022 | 4h |
| 030 | PRPR-030 | Create deploy-new-release Claude Code Skill | 4 | ✅ | 023 | 3h |
| 031 | PRPR-031 | Register deploy-new-release Skill | 4 | ✅ | 030 | 1h |
| 032 | PRPR-032 | Update scripts/README.md | 4 | ✅ | 021, 022, 023 | 1h |
| 040 | PRPR-040 | Dry-Run Against Dev | 5 | 🔲 | 023, 030 | 2h |
| 041 | PRPR-041 | Live Test Against Demo | 5 | 🔲 | 040 | 3h |
| 090 | PRPR-090 | Project Wrap-Up | wrap-up | 🔲 | 041 | 1h |

---

## Dependency Graph

```
Phase 1:  001 (setup) ✅
            |
Phase 2:  010 (procedure guide)
            |
            ├──────────────┬──────────────┐
Phase 3:  020 (env reg)  021 (build)    022 (web res)
            |              |              |
            └──────────────┴──────────────┘
                           |
                         023 (deploy-release)
                           |
            ┌──────────────┤
Phase 4:  030 (skill)    032 (scripts readme)
            |
          031 (register)
            |
Phase 5:  040 (dry-run dev)
            |
          041 (live test demo)
            |
Wrap-up:  090 (wrap-up)
```

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 020, 021, 022 | 010 complete | Independent Phase 3 scripts — different files |
| B | 031, 032 | 030 + (021,022,023) complete | Registration + docs — different files |

---

## Critical Path

```
001 → 010 → 021 → 023 → 030 → 031 → 040 → 041 → 090
```

Longest chain: 9 tasks. Build-AllClientComponents (021) is on the critical path because
Deploy-Release (023) depends on it.

---

## Phase Summary

| Phase | Tasks | Description | Estimate |
|-------|-------|-------------|----------|
| 1 - Setup | 001 | Project artifacts | 1h ✅ |
| 2 - Procedure | 010 | Human-readable release guide | 4h |
| 3 - Scripts | 020-023 | Environment registry + 3 orchestrator scripts | 13h |
| 4 - Skill | 030-032 | Claude Code skill + registration + docs | 5h |
| 5 - Verification | 040-041 | Dry-run + live test | 5h |
| Wrap-up | 090 | Final cleanup | 1h |
| **Total** | **12** | | **29h** |
