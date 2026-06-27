# TASK-INDEX — Email Communication Solution R3

> **Generated**: 2026-06-05 by `/project-pipeline`
> **Total tasks**: 79 (1 Wave 0 ADR + 7 Wave 0 server/client + 20 Wave 1 + 16 Wave 2 + 6 Wave 3 + 6 Wave 4 + 8 Wave 5 + 14 Wave 6 + 1 wrap-up)
> **Status legend**: 🔲 not-started · 🟡 in-progress · ✅ completed · ⛔ blocked · 🔄 needs-retry

---

## Task Registry

| ID | Title | Wave | Status | Rigor | Dependencies | Parallel | FR-Refs |
|---|---|---|---|---|---|---|---|
| 001 | Create ADR-033 — Communication Architecture | 0 | 🔲 | STANDARD | none | — (.claude) | FR-26 |
| 002 | BFF DTO — add `AttachmentDriveItemIds` (non-breaking) | 0 | 🔲 | FULL | 001 | A | FR-21 |
| 003 | BFF — `Internet-Message-Id` post-send capture (UQ3 spike + impl) | 0 | 🔲 | FULL | 001 | A | FR-22 |
| 004 | Dataverse schema — verify/add `sprk_inreplyto` + `sprk_internetmessageid` | 0 | 🔲 | STANDARD | 001 | A | FR-23 |
| 005 | Client wrapper — `attachmentDriveItemIds` + `SendCommunicationError` | 0 | 🔲 | FULL | 001, 002 | — | FR-09, FR-20 |
| 006 | File backlog referrals (#12, #13, #14, #15) | 0 | 🔲 | MINIMAL | 001 | B | (backlog) |
| 007 | Wave 0 BFF deploy + integration tests | 0 | 🔲 | STANDARD | 002, 003, 004, 005 | — | (deploy) |
| 008 | Wave 0 unit tests (DTO mapping + `Internet-Message-Id` + client wrapper) | 0 | 🔲 | STANDARD | 002, 003, 005 | B | (testing) |
| 010 | Scaffold `EmailComposer/` directory | 1 | 🔲 | STANDARD | 007 | — | (scaffold) |
| 011 | EmailComposer engine — state machine + reducer + imperative handle | 1 | 🔲 | FULL | 010 | — | FR-01 |
| 012 | `RecipientField` sub-component | 1 | 🔲 | FULL | 011 | C | FR-05 |
| 013 | `BodyEditor` sub-component | 1 | 🔲 | FULL | 011 | C | FR-06 |
| 014 | `AttachmentList` sub-component framework | 1 | 🔲 | FULL | 011 | C | FR-07 |
| 015 | Attachment source — local picker | 1 | 🔲 | STANDARD | 014 | D | FR-07 |
| 016 | Attachment source — SPE picker | 1 | 🔲 | STANDARD | 014 | D | FR-07 |
| 017 | Attachment source — related-record picker | 1 | 🔲 | STANDARD | 014 | D | FR-07 |
| 018 | Attachment source — wizard-context picker | 1 | 🔲 | STANDARD | 014 | D | FR-07 |
| 019 | `ComposerActionBar` sub-component | 1 | 🔲 | STANDARD | 011 | C | (sub-comp) |
| 020 | Validation contract + canonical error codes | 1 | 🔲 | FULL | 012, 013, 014, 015, 016, 017, 018 | — | FR-08, FR-09 |
| 021 | `<SendEmailStep />` wrapper (inline mount) | 1 | 🔲 | STANDARD | 020 | E | FR-02 |
| 022 | `<SendEmailDialog />` wrapper (dialog mount; rewrite existing) | 1 | 🔲 | FULL | 005, 020 | E | FR-03 |
| 023 | `<SendEmailPage />` wrapper (page mount) | 1 | 🔲 | FULL | 020 | E | FR-04 |
| 024 | Mode: `view` | 1 | 🔲 | FULL | 020 | F | FR-10 |
| 025 | Mode: `reply` (+ `sprk_inreplyto` stamping) | 1 | 🔲 | FULL | 020, 003 | F | FR-11 |
| 026 | Mode: `forward` | 1 | 🔲 | FULL | 020, 003 | F | FR-12 |
| 027 | Mode: `draft` | 1 | 🔲 | FULL | 020 | F | FR-13 |
| 028 | EmailComposer unit tests (engine + modes + validation + caps) | 1 | 🔲 | STANDARD | 011-027 | — | (testing) |
| 029 | Wave 1 deploy verification (shared-lib build + dependent solutions rebuild) | 1 | 🔲 | STANDARD | 028 | — | (deploy) |
| 030 | Scaffold `src/client/code-pages/EmailComposer/` | 2 | 🔲 | STANDARD | 029 | — | FR-18 |
| 031 | Code Page auth bootstrap (`@spaarke/auth` v2) | 2 | 🔲 | FULL | 030 | — | FR-18 |
| 032 | Code Page URL parameter parsing | 2 | 🔲 | STANDARD | 030 | — | FR-18 |
| 033 | Mount `<SendEmailPage />` wrapper into Code Page | 2 | 🔲 | FULL | 023, 031, 032 | — | FR-18 |
| 034 | Code Page build pipeline (`build-webresource.ps1`) | 2 | 🔲 | STANDARD | 033 | — | FR-18 |
| 035 | UQ1 — audit existing `sprk_communication` form customizations | 2 | 🔲 | STANDARD | 030 | G | FR-19 (Surf 2) |
| 036 | Deploy Code Page to dev (`sprk_emailcomposer`) | 2 | 🔲 | STANDARD | 034 | — | FR-18 |
| 037 | Ribbon button "+ New Email" on `sprk_communication` views | 2 | 🔲 | STANDARD | 036 | H | FR-19 (Surf 1) |
| 038 | Form Component Control swap + admin fallback (NFR-07) | 2 | 🔲 | STANDARD | 035, 036 | H | FR-19 (Surf 2), NFR-07 |
| 039 | Preserve standard form as admin fallback (docs) | 2 | 🔲 | MINIMAL | 038 | — | NFR-07 |
| 040 | Embeddable launch documentation (Surface 3) | 2 | 🔲 | MINIMAL | 036 | H | FR-19 (Surf 3) |
| 041 | UI test — compose mode | 2 | 🔲 | STANDARD | 037 | I | FR-18 |
| 042 | UI test — view mode | 2 | 🔲 | STANDARD | 038 | I | FR-10 |
| 043 | UI test — reply mode (`sprk_inreplyto` stamping) | 2 | 🔲 | STANDARD | 003, 036 | I | FR-11, FR-18 |
| 044 | UI test — forward + draft modes | 2 | 🔲 | STANDARD | 036 | I | FR-12, FR-13 |
| 045 | UI test — dark mode compliance (ADR-021, NFR-09) | 2 | 🔲 | STANDARD | 036 | I | NFR-09 |
| 050 | `SendEmailDialog` — finalize canonical wrapper integration | 3 | 🔲 | FULL | 029, 022 | — | FR-03 |
| 051 | `FilePreviewDialog` migration (remove inline `fetch`) | 3 | 🔲 | FULL | 050 | — | FR-14 |
| 052 | Update `ISendEmailDialogProps` interface (remove `onSend`) | 3 | 🔲 | STANDARD | 050 | — | FR-14 |
| 053 | Audit other `SendEmailDialog` consumers | 3 | 🔲 | STANDARD | 050, 052 | — | FR-14 |
| 054 | Build + deploy LegalWorkspace (Wave 3) | 3 | 🔲 | STANDARD | 051, 053 | — | (deploy) |
| 055 | Smoke test — FilePreview email flow | 3 | 🔲 | STANDARD | 054 | — | FR-14 |
| 060 | Migrate shared `SummarizeFilesDialog` (line 436 fetch → wrapper) | 4 | 🔲 | FULL | 055, 021 | — | FR-15 |
| 061 | Delete LegalWorkspace `SummarizeFiles/` fork (9 files) | 4 | 🔲 | STANDARD | 060 | — | FR-15, FR-25 |
| 062 | Re-export `SummarizeFiles` from `@spaarke/ui-components` | 4 | 🔲 | STANDARD | 061 | — | FR-15, FR-25 |
| 063 | Build + deploy SummarizeFilesWizard solution | 4 | 🔲 | STANDARD | 060, 062 | J | (deploy) |
| 064 | Build + deploy LegalWorkspace (Wave 4) | 4 | 🔲 | STANDARD | 061, 062 | J | (deploy) |
| 065 | Smoke test — Summarize email step (both solutions) | 4 | 🔲 | STANDARD | 063, 064 | — | FR-15 |
| 070 | Refactor `EntityCreationService.sendEmail()` to thin adapter (≤30 LOC) | 5 | 🔲 | FULL | 065 | — | FR-16 |
| 071 | Migrate shared `CreateRecordWizard/steps/SendEmailStep.tsx` (covers 4 wizards) | 5 | 🔲 | FULL | 070, 021 | — | FR-16 |
| 072 | Migrate `CreateMatterWizard/SendEmailStep.tsx` (separate file) | 5 | 🔲 | FULL | 071 | K | FR-16 |
| 073 | Resolve `WorkAssignmentWizardDialog.tsx:31` cross-package import | 5 | 🔲 | STANDARD | 071 | K | FR-25 |
| 074 | Delete LegalWorkspace `CreateMatter/SendEmailStep.tsx` fork (sole email fork) | 5 | 🔲 | STANDARD | 072 | K | FR-25 |
| 075 | `DocumentEmailWizard` migration to `attachmentDriveItemIds` (fixes latent bug line 494) | 5 | 🔲 | FULL | 070, 005, 002 | K | FR-17 |
| 076 | Build + deploy all 5 wizards + LegalWorkspace (single PR per Owner Clarification) | 5 | 🔲 | STANDARD | 071-075 | — | (deploy) |
| 077 | Wave 5 smoke tests (5 wizards + DocumentEmailWizard attachments regression) | 5 | 🔲 | STANDARD | 076 | — | FR-16, FR-17 |
| 080 | Audit Dataverse entry points for `sprk_communication_send.js` | 6 | 🔲 | STANDARD | 077 | — | FR-24 |
| 081 | Delete `src/client/webresources/js/sprk_communication_send.js` (~1.15K LOC) | 6 | 🔲 | STANDARD | 080 | L | FR-24 |
| 082 | Delete infrastructure-ribbon duplicate `sprk_communication_send.js` (~1.15K LOC) | 6 | 🔲 | STANDARD | 080 | L | FR-24 |
| 083 | Delete Dataverse web resource record `sprk_communication_send` | 6 | 🔲 | STANDARD | 081, 082 | — | FR-24 |
| 084 | Final LegalWorkspace email-fork audit (confirm zero remain) | 6 | 🔲 | STANDARD | 083 | — | FR-25 |
| 085 | NEW `docs/guides/EMAIL-COMPOSER-COMPONENT-GUIDE.md` | 6 | 🔲 | STANDARD | 077 | M | FR-27 #2 |
| 086 | UPDATE `.claude/patterns/api/send-email-integration.md` | 6 | 🔲 | STANDARD | 077 | — (.claude) | FR-27 #5 |
| 087 | UPDATE `.claude/FAILURE-MODES.md` AP-4 + `.claude/constraints/bff-extensions.md` | 6 | 🔲 | STANDARD | 077, 001 | — (.claude) | FR-27 #6, #7 |
| 088 | NEW `docs/standards/COMMUNICATION-ATTACHMENT-POLICY.md` + UPDATE `comm-service-architecture.md` | 6 | 🔲 | STANDARD | 077 | M | FR-27 #3, #8 |
| 089 | NEW `docs/data-model/sprk_communication-form.md` + UPDATE `SHARED-UI-COMPONENTS-GUIDE.md` | 6 | 🔲 | STANDARD | 077 | M | FR-27 #4, #9 |
| 090 | RETIRE banners on `email-to-document-architecture.md` + `email-to-document-automation.md` | 6 | 🔲 | MINIMAL | 077 | M | FR-27 #10, #11 |
| 091 | UPDATE `docs/architecture/sdap-overview.md` (`sprk_email*` field refs) | 6 | 🔲 | MINIMAL | 077 | M | FR-27 #12 |
| 092 | UPDATE root `MEMORY.md` + confirm `CLAUDE.md` §16 ADR-033 reachable | 6 | 🔲 | MINIMAL | 077 | — (root) | FR-27 #13 |
| 093 | Run `/doc-drift-audit` + fix orphans | 6 | 🔲 | STANDARD | 084-092 | — | FR-27 |
| 099 | Project wrap-up (code-review + adr-check + repo-cleanup + lessons-learned) | Wrap-up | 🔲 | FULL | 093 (+ all prior) | — | (wrap) |

---

## Dependency Graph (Critical Path)

```
001 (ADR-033) ──┬─► 002 ──► 005 ──┐
                ├─► 003 ──────────┤
                ├─► 004 ──────────┤
                ├─► 006           │
                └─► 008 ──────────┤
                                  ▼
                                 007 (Wave 0 deploy)
                                  │
                                  ▼
                                 010 (scaffold)
                                  │
                                  ▼
                                 011 (engine)
                                  │
                ┌─────────────────┼─────────────────┐
                ▼                 ▼                 ▼
              012-014           019              (group C)
                │                 │
                ▼                 │
             015-018              │  (group D after 014)
                │                 │
                └─────────────────┴─► 020 (validation aggregation)
                                       │
                          ┌────────────┼────────────┐
                          ▼            ▼            ▼
                        021          022          023  (group E — 3 wrappers)
                                                       │
                                       (and 024-027 modes, group F)
                                                       │
                                                       ▼
                                                     028 (engine tests)
                                                       │
                                                       ▼
                                                     029 (Wave 1 deploy verify)
                                                       │
                                                       ▼
                                                     030 (Code Page scaffold)
                                                       │
                                                       ▼
                                                  031, 032, 035 (G)
                                                       │
                                                       ▼
                                                     033 → 034 → 036 (Code Page deploy hub)
                                                       │
                                                       ▼
                                                  037, 038, 040 (H) + 041-045 (I)
                                                       │
                                                       ▼
                                                  Wave 3 (050-055)
                                                       │
                                                       ▼
                                                  Wave 4 (060-065)
                                                       │
                                                       ▼
                                                  Wave 5 (070-077, group K within)
                                                       │
                                                       ▼
                                                  Wave 6 (080-093, groups L, M within)
                                                       │
                                                       ▼
                                                     099 (wrap-up)
```

**Critical path length** (longest dependency chain through the project):
`001 → 002 → 005 → 007 → 010 → 011 → 014 → 015 → 020 → 022 → 029 → 030 → 031 → 033 → 034 → 036 → 037 → 041 → (or other long branch) ... → 077 → 080 → 081 → 083 → 084 → 093 → 099` (approximately 26 nodes deep). Parallel groups shorten wall-clock time considerably.

---

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met. Max concurrency: **6 agents per wave** (per project-pipeline Step 5 hard limit).

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| **A** | 002, 003, 004 | 001 ✅ | Separate BFF/Dataverse files | ✅ Yes — verified disjoint |
| **B** | 006, 008 | 005 ✅ (for 008); 001 (for 006) | backlog file (006) + test files (008) | ✅ Yes |
| **C** | 012, 013, 014, 019 | 011 ✅ | Separate sub-component files | ✅ Yes |
| **D** | 015, 016, 017, 018 | 014 ✅ | Separate attachment source files | ✅ Yes |
| **E** | 021, 022, 023 | 020 ✅ | Separate wrapper files | ✅ Yes |
| **F** | 024, 025, 026, 027 | 020 ✅ (+ 003 for 025/026) | Separate mode files | ✅ Yes |
| **G** | 035 ‖ 031, 032 | 030 ✅ | 035 = audit (no code) ‖ 031/032 = Code Page App.tsx | ⚠ 031/032 NOT safe with each other (both touch App.tsx); 035 safe with both |
| **H** | 037, 038, 040 | 036 ✅ (+ 035 for 038) | Separate Dataverse customizations + docs | ✅ Yes |
| **I** | 041, 042, 043, 044, 045 | 036, 037, 038 ✅ | UI test definitions (separate `<ui-tests>` per task) | ✅ Yes |
| **J** | 063, 064 | 060, 061, 062 ✅ | Separate solution builds | ✅ Yes |
| **K** | 072, 073, 074, 075 | 071 ✅ | Separate wizard files / fork delete / cross-package import / DocumentEmailWizard | ✅ Yes — verified disjoint |
| **L** | 081, 082 | 080 ✅ | Separate webresource files | ✅ Yes |
| **M** | 085, 088, 089, 090, 091 | 077 ✅ | Separate `docs/` file targets | ✅ Yes (NOTE: 088 + 091 touch `docs/architecture/` — collision risk with PR #360 logged in task notes) |

### `.claude/` write-boundary (main-session-only, NOT parallel-safe)
- **001** — creates `.claude/adr/ADR-033-*.md`
- **086** — updates `.claude/patterns/api/send-email-integration.md`
- **087** — updates `.claude/FAILURE-MODES.md` + `.claude/constraints/bff-extensions.md`
- **092** — touches root `MEMORY.md` + confirms root `CLAUDE.md` ADR-033 reachability

Per CLAUDE.md §3 "Sub-Agent Write Boundary", these MUST run from the main session. Sub-agents would fail with "Edit denied on `.claude/...`".

### Active PR collision warning (PR #360 — `audit-r1-docs-update`)
Tasks **086, 087, 088, 091** must run a rebase check before editing — PR #360 touches overlapping paths (`.claude/patterns/`, `.claude/constraints/`, `docs/architecture/`). Each affected task POML includes the rebase-check step.

---

## Wave Summary

| Wave | Theme | Tasks | Critical gate |
|---|---|---|---|
| **0** | ADR + BFF non-breaking + schema + client wrapper foundation | 8 (001-008) | 007 (BFF deploy) blocks Wave 1 |
| **1** | EmailComposer engine + 3 wrappers + sub-components + tests | 20 (010-029) | 029 (Wave 1 deploy verify) blocks Wave 2 |
| **2** | Code Page + 3 entry surfaces + UI tests | 16 (030-045) | 036 (Code Page deploy) opens up Wave 2 parallelism; UI tests block 099 |
| **3** | `SendEmailDialog` rewrite + `FilePreviewDialog` migration | 6 (050-055) | 055 (smoke) blocks Wave 4 |
| **4** | `SummarizeFilesDialog` migration + LegalWorkspace fork delete | 6 (060-065) | 065 (smoke) blocks Wave 5 |
| **5** | 5-wizard migration + `DocumentEmailWizard` fix + LegalWorkspace `CreateMatter` fork delete + cross-package import fix (single PR) | 8 (070-077) | 077 (smoke) blocks Wave 6 |
| **6** | Retirement (~2.3K LOC) + 13 documentation updates + drift audit | 14 (080-093) | 093 (drift audit) blocks wrap-up |
| **Wrap-up** | code-review + adr-check + repo-cleanup + lessons-learned | 1 (099) | end |

---

## How to Execute Parallel Groups

1. Check all prerequisites are ✅ (in this table)
2. Invoke the `Skill` tool with multiple `task-execute` invocations in ONE message — one per task in the group
3. Each invocation runs `task-execute` for one task with full context loading
4. Wait for all to complete before next group
5. **Max concurrency**: 6 agents per wave
6. **After each wave**: main session runs build verification (`dotnet build`, `npm run build`) before dispatching next wave's first group — see project-pipeline Step 5 "Build verification between waves (mandatory)"

For details see [`.claude/skills/task-execute/SKILL.md`](../../../.claude/skills/task-execute/SKILL.md) and [`.claude/skills/project-pipeline/SKILL.md`](../../../.claude/skills/project-pipeline/SKILL.md) Step 5.

---

## High-Risk Items (Cross-Reference with `plan.md` §8 Risk Register)

| Risk | Task(s) | Mitigation |
|---|---|---|
| Form Component Control swap breaks existing form customizations | 035, 038 | UQ1 audit gates the swap; standard form retained as admin fallback (NFR-07) |
| `Internet-Message-Id` retrieval strategy unreliable | 003 | 1-hour UQ3 spike + best-effort failure mode |
| Wave 5 single-PR unreviewable | 076 | Owner-clarified; PR description structures per-wizard sections |
| ADR-033 number conflict | 001 | Pre-flight confirmed free; task 001 re-checks slot before file creation |
| `sprk_communication_send.js` consumer missed during audit | 080 | Comprehensive Dataverse audit; PR description lists every entry point |
| PR #360 collision on `.claude/patterns/` + `docs/architecture/` | 086, 087, 088, 091 | Each task includes rebase-check pre-step |
| LegalWorkspace fork retirement breaks consumer (only CreateMatter is a fork) | 074, 084 | Empirical delta documented in task POMLs; final audit (084) confirms before completion |

---

*Updated by `task-execute` (✅ on completion) and at wrap-up (099). Reset only if waves are re-planned.*
