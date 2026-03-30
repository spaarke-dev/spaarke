# Task Index — Dark Mode Theme R2

## Status Summary

| Status | Count |
|--------|-------|
| Completed | 20 |
| In Progress | 0 |
| Pending | 1 (042 — ribbon deploy blocked by missing entity) |

---

## All Tasks

### Phase 1: Consolidate Theme Utilities (Serial — Foundation)

| ID | Task | Status | Dependencies | Est |
|----|------|--------|-------------|-----|
| 001 | Consolidate theme utilities into single module | ✅ | none | 3h |
| 002 | Update shared library barrel exports | ✅ | 001 | 30m |
| 003 | Update theme utility unit tests | ✅ | 001 | 2h |

### Phase 2: Migrate Consumers (Parallel Groups A, B, C)

| ID | Task | Status | Dependencies | Group | Est |
|----|------|--------|-------------|-------|-----|
| 010 | Update 6 Code Page ThemeProvider wrappers | ✅ | 001, 002 | **A** | 1h |
| 011 | Update 11 Code Page App.tsx/main.tsx entry points | ✅ | 001, 002 | **A** | 1.5h |
| 012 | Delete duplicate useThemeDetection hooks + SemanticSearch | ✅ | 001, 002 | **A** | 1h |
| 014 | Replace inlined theme code in 3 PCF controls (~470 lines) | ✅ | 001, 002 | **B** | 2h |
| 016 | Remove OS listeners from PCF ThemeServices | ✅ | 001, 002 | **B** | 1h |
| 017 | Fix LegalWorkspace useTheme hook storage key | ✅ | 001, 002 | **C** | 1h |
| 018 | Remove OS listener from sprk_ThemeMenu.js | ✅ | none | **C** | 30m |
| 019 | Verify remaining PCF controls use standard key | ✅ | 001 | **C** | 30m |

### Phase 3: Ribbon Deployment (Parallel Group D — independent)

| ID | Task | Status | Dependencies | Group | Est |
|----|------|--------|-------------|-------|-----|
| 020 | Add theme flyout ribbon to 6 missing entities | ✅ | none | **D** | 2h |
| 021 | Add Form ribbon location to 3 existing entities | ✅ | none | **D** | 1h |
| 022 | Update Auto label to "follows app" | ✅ | 020, 021 | **D** | 30m |

### Phase 4: Dataverse Persistence (Serial)

| ID | Task | Status | Dependencies | Est |
|----|------|--------|-------------|-----|
| 030 | Add ThemePreference to sprk_preferencetype option set | ✅ | none | 30m |
| 031 | Add Dataverse theme sync functions to themeStorage | ✅ | 001, 030 | 2h |
| 032 | Wire Dataverse sync into theme consumers | ✅ | 031 | 1.5h |

### Phase 5: Protocol & Wrap-up

| ID | Task | Status | Dependencies | Group | Est |
|----|------|--------|-------------|-------|-----|
| 040 | Create theme consistency protocol document | ✅ | 001 | **E** | 1h |
| 041 | Integration testing — verify all surfaces | ✅ | Phase 2 complete | **E** | 2h |
| 042 | Deploy to dev environment | ⚠️ | 041 | — | 1h |
| 090 | Project wrap-up | ✅ | 042 | — | 30m |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | File Scope | Max Agents |
|-------|-------|--------------|------------|------------|
| — | 001, 002, 003 | None | Shared library only | 1 (serial) |
| **A** | 010, 011, 012 | Phase 1 complete | Code Page solutions | 3 |
| **B** | 014, 016 | Phase 1 complete | PCF controls | 2 |
| **C** | 017, 018, 019 | Phase 1 complete (018 independent) | LW hook + web resource + PCF verify | 3 |
| **D** | 020, 021, 022 | None (ribbon XML independent) | Ribbon XML only | 3 |
| **E** | 040, 041 | Phase 2-3 complete | Docs + testing | 2 |

**Groups A, B, C can run simultaneously** after Phase 1 (up to 8 concurrent agents).
**Group D can run in parallel with everything** (ribbon XML has no code dependencies).
**Phase 4 (030-032) can run alongside Groups A-C** (Dataverse schema is independent of code changes).

---

## Critical Path

```
001 → 002 → 003 → [A+B+C parallel] → 041 → 042 → 090
                    [D parallel with everything]
                    [030 → 031 → 032 parallel with A-C]
```

**Minimum sequential steps**: 6 (with full parallelization)
**Total estimated effort**: ~23 hours
**Wall clock with parallelization**: ~10 hours

---

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 001 | Breaking change — all consumers depend on this | Run unit tests immediately after; Phase 2 fixes all consumers |
| 014 | PCF build failures after removing inline code | Test each PCF build individually |
| 017 | LegalWorkspace storage key change may reset user preferences | One-time reset is acceptable; document in release notes |
| 030 | Dataverse schema change | Test in dev first; option set values are additive (low risk) |
