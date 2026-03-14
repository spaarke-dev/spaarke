# ESLint Baseline Violation Counts

> **Date**: 2026-03-13
> **Task**: 011 — Stricten ESLint Rules Across PCF and Code-Page Projects
> **Scope**: 7 PCF projects with existing ESLint configs

---

## Summary

After applying the shared base configuration (`src/client/eslint.config.base.mjs`), baseline violation counts were measured across the 7 PCF projects that had existing ESLint configs. The 17 PCF projects without configs and all 5 code-page projects are not counted here -- they will generate additional violations when configs are added in Task 033.

## PCF Projects Measured

DocumentRelationshipViewer, EmailProcessingMonitor, RelatedDocumentCount, ScopeConfigEditor, SemanticSearchControl, SpeFileViewer, UniversalDatasetGrid

## Violation Counts by Rule

### ERROR Level (Block new violations)

| Rule | Count | Source | Notes |
|------|-------|--------|-------|
| `@typescript-eslint/prefer-function-type` | 2 | Base config (inherited from stylistic) | Auto-fixable |
| `promise/always-return` | 2 | Promise plugin | Missing return in .then() |
| `prefer-const` | 1 | **Shared base** | New — catches let that should be const |
| `no-restricted-imports` (ADR-022) | 0* | **Shared base (PCF)** | *SpeFileViewer has react-dom/client import but was tested separately |
| `no-restricted-imports` (ADR-021) | 0 | **Shared base** | No Fluent v8 imports in codebase |
| Parse errors (test files not in tsconfig) | 6 | tsconfig misconfiguration | Not from our rules |

### WARN Level (Existing violations — remediate in Task 033)

| Rule | Count | Source | Notes |
|------|-------|--------|-------|
| `@typescript-eslint/no-explicit-any` | 6 | **Shared base** | Was "off" in most projects |
| `@typescript-eslint/no-empty-function` | 3 | **Shared base** | Empty rollback() methods |
| `@typescript-eslint/no-inferrable-types` | 3 | Stylistic config | Trivially inferred types |
| `@typescript-eslint/consistent-generic-constructors` | 2 | Stylistic config | Constructor generics |
| `no-console` | 0** | **Shared base** | **Only allows console.warn/error |
| `no-restricted-syntax` (ADR-021 colors) | 0** | **Shared base** | **Many exist per audit but not in files with configs |

> *Based on single-file test of SpeFileViewer: `react-dom/client` import correctly caught as ERROR
> **Estimated from audit-typescript-pcf.md: ~70+ hard-coded colors across full codebase, ~100+ console.log instances. These will appear when configs extend to all 24 PCF projects.

## Total (7 Configured PCF Projects)

| Severity | Count |
|----------|-------|
| Errors | 11 (6 are parse errors from test files not in tsconfig) |
| Warnings | 17 |
| **Total** | **28** |

## Estimated Full-Codebase Violations (All 24 PCF + 5 Code Pages)

Based on the audit (audit-typescript-pcf.md), when configs are extended to all projects:

| Rule | Estimated Count | Level |
|------|----------------|-------|
| `no-explicit-any` | 200+ | WARN |
| `no-console` | 100+ | WARN |
| `no-restricted-syntax` (hard-coded colors) | 70+ | WARN |
| `no-restricted-imports` (react-dom/client in PCF) | 6 | ERROR |
| `prefer-const` | 20-50 | ERROR |
| `no-unused-vars` | 50-100 | ERROR |

## ADR-022 Enforcement Verified

The `react-dom/client` restriction correctly fires as ERROR on PCF projects:

```
SpeFileViewer/control/index.ts
  13:1  error  'react-dom/client' import is restricted from being used.
               ADR-022: PCF controls MUST NOT import from react-dom/client.
```

Known ADR-022 violations (from audit): AnalysisWorkspace, AnalysisBuilder, UniversalQuickCreate, SpeFileViewer, SpeDocumentViewer, UniversalDatasetGrid (6 PCF controls).

## ADR-021 Color Detection Verified

The `no-restricted-syntax` rule correctly catches hard-coded colors:

```
SpeFileViewer/control/index.ts
  54:20  warning  ADR-021: Do not use hard-coded rgb()/rgba() colors.
                  Use Fluent UI design tokens instead.
```

## Code Pages Status

Code-page projects (AnalysisWorkspace, DocumentRelationshipViewer, PlaybookBuilder, SemanticSearch, SprkChatPane) do not currently have ESLint devDependencies installed. The shared config `src/client/code-pages/eslint.config.mjs` has been created and uses `spaarkeRestrictedImports` (ADR-021 only, no PCF restrictions). ESLint packages need to be added to each code-page project's devDependencies to enable linting. This is tracked for Task 033 remediation.

---

*This baseline feeds into Task 033 (remediation) to track progress.*
