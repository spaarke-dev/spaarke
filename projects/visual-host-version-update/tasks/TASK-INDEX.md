# Task Index — VisualHost Decoupling & `@spaarke/visuals` Extraction

Status legend: 🔲 not-started · 🔄 in-progress/retry · ✅ complete · ⛔ blocked

## Tasks

| ID | Title | Phase | Rigor | Model/Effort | Deps | Status |
|----|-------|-------|-------|--------------|------|--------|
| VHVU-001 | Harden shared-package build + declare `@spaarke/auth` on ui-components | A0 | FULL | sonnet/high | — | 🔲 |
| VHVU-002 | Shared-lib packaging hygiene (`.tgz` + `files` allow-list + storybook-static) | A0 | STANDARD | sonnet/high | 001 | 🔲 |
| VHVU-003 | Bump VisualHost v1.4.35 (5 locations) + rebuild + confirm `cleanGuid` | A0 | FULL | sonnet/high | 001 | 🔲 |
| VHVU-004 | Deploy v1.4.35 to dev + UAT braced-GUID create | A0 | FULL | sonnet/high | 002,003 | 🔲 |
| VHVU-010 | Add shared `bootstrapWizardPage()` factory + adopt in Event page | A1 | FULL | sonnet/high | 004 | 🔲 |
| VHVU-011 | Build Invoice wizard code page | A1 | FULL | sonnet/high | 010 | 🔲 |
| VHVU-012 | Build Report Card wizard code page | A1 | FULL | sonnet/high | 010 | 🔲 |
| VHVU-020 | Wire `initialAssociation`/`lockAssociation` + `themeOption` in 3 pages | A2 | FULL | sonnet/high | 011,012 | 🔲 |
| VHVU-021 | Verify regarding-resolver + field-mapping parity from "+" | A2 | FULL | sonnet/high | 020 | 🔲 |
| VHVU-030 | Cut over VisualHost "+" to `navigateTo`; delete inline embedding + casts | A3 | FULL | sonnet/high | 020 | 🔲 |
| VHVU-031 | Deploy VisualHost + pages to dev; UAT "+" via navigateTo (dark+light) | A3 | FULL | sonnet/high | 030,021 | 🔲 |
| VHVU-040 | Scaffold `@spaarke/visuals` sibling package | B1 | FULL | sonnet/high | 031 | 🔲 |
| VHVU-041 | Move 15 visuals + 7 utils + viz types into package | B2 | FULL | sonnet/high | 040 | 🔲 |
| VHVU-042 | Reconcile drifted duplication (one `VisualType`, one `EventDueDateCard`) | B2 | FULL | sonnet/high | 041 | 🔲 |
| VHVU-050 | Refactor 3 self-fetch visuals to props-in + split `ViewDataService` | B3 | FULL | sonnet/xhigh | 041 | 🔲 |
| VHVU-060 | Repoint VisualHost to `@spaarke/visuals`; verify build/bundle/drill | B4 | FULL | sonnet/high | 042,050 | 🔲 |
| VHVU-061 | Deploy + UAT visuals parity on dev | B4 | FULL | sonnet/high | 060 | 🔲 |
| VHVU-070 | Author ADR-012 amendment (concise + full) | B5 | FULL | opus/high | 060 | 🔲 |
| VHVU-090 | Project wrap-up (lessons-learned, test-diet, repo-cleanup) | — | STANDARD | sonnet/high | 070,061 | 🔲 |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A1-pages | 011, 012 | 010 (factory) complete | Independent code pages (Invoice / Report Card); `parallel-safe` |

All other tasks are sequential (build-chain dependencies).

## Critical Path
001 → 003 → 004 → 010 → {011,012} → 020 → 030 → 031 → 040 → 041 → {042,050} → 060 → 070 → 090

## Sequencing notes
- **VHVU-004 is the first shippable milestone** (`cleanGuid` to dev). Ship + UAT before A1.
- **`.claude/` write boundary**: VHVU-070 (ADR-012 amendment) is main-session only — never dispatch to a parallel sub-agent.
- **Deploy gates**: 004, 031, 061 — verify before proceeding.
- **Shared-surface**: 001, 002, 040–042 touch shared packages — coordinate with PR #508.
