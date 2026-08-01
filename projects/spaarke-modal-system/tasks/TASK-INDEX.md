# Task Index — Spaarke Modal System

> **Generated**: 2026-08-01 by `/project-pipeline` → task decomposition
> **Total Tasks**: 29 (13 P0 + 1 P0.5 + 2 P1 + 3 P2 + 2 P3 + 2 P4 + 1 P5 + 1 P6 + 3 P7 + 1 wrap-up)
> **Spec**: [../spec.md](../spec.md) · **Plan**: [../plan.md](../plan.md) · **CLAUDE.md**: [../CLAUDE.md](../CLAUDE.md)

---

## Status Legend

🔲 not-started · 🔄 in-progress · ⛔ blocked · ✅ completed · ⏸️ deferred · 🔁 needs-retry

## Task Table

| ID | Title | Phase | Status | Deps | Group | Rigor | Model/Effort |
|----|-------|-------|--------|------|-------|-------|--------------|
| 001 | Size scale + layout tokens (sizes.ts) | 0 | ✅ | none | A | FULL | sonnet/high |
| 002 | Scaled Fluent theme builder (scaleTheme) | 0 | ✅ | none | A | FULL | sonnet/high |
| 003 | Reconcile ModalWindowControls glyph | 0 | ✅ | none | A | FULL | sonnet/high |
| 004 | SprkModal base shell | 0 | ✅ | 001,002,003 | — | FULL | sonnet/high |
| 005 | Presets: ConfirmModal + ChoiceModal (ADR-023) | 0 | ✅ | 004 | B | FULL | sonnet/high |
| 006 | Preset: FormModal | 0 | ✅ | 004 | B | FULL | sonnet/high |
| 007 | Presets: PreviewModal + BrowseModal | 0 | ✅ | 004 | B | FULL | sonnet/high |
| 008 | Preset: WizardModal | 0 | ✅ | 004 | B | FULL | sonnet/high |
| 009 | Barrel exports + a11y snapshot + dual-React verify | 0 | 🔲 | 005,006,007,008 | — | FULL | sonnet/high |
| 010 | Standards doc: MODAL-DESIGN-SYSTEM.md | 0 | 🔲 | 004 | — | STANDARD | sonnet/medium |
| 011 | ADR-050: Canonical Modal Shell 🔒 | 0 | 🔲 | 004 | — | STANDARD | sonnet/high |
| 012 | Pattern pointer: modal-shell.md 🔒 | 0 | 🔲 | 010 | — | MINIMAL | sonnet/medium |
| 013 | Cross-links: DECISION-CRITERIA + root CLAUDE.md §17 🔒 | 0 | 🔲 | 010,011 | — | STANDARD | sonnet/high |
| 020 | P0.5 — App-shell `--sprk-ui-scale` control | 0.5 | 🔲 | 002 | — | FULL | sonnet/high |
| 030 | P1 — Window controls into UI.Components dialogs | 1 | 🔲 | 003 | P1 | STANDARD | sonnet/high |
| 031 | P1 — Window controls into Compose/AI.Widgets/SpaarkeAi | 1 | 🔲 | 003 | P1 | STANDARD | sonnet/high |
| 040 | P2 — Re-base confirms onto ConfirmModal | 2 | 🔲 | 005 | P2 | FULL | sonnet/high |
| 041 | P2 — Re-base ChoiceDialog onto ChoiceModal | 2 | 🔲 | 005 | P2 | FULL | sonnet/high |
| 042 | P2 — Retire ActionConfirmationDialog overlay | 2 | 🔲 | 005 | P2 | FULL | sonnet/high |
| 050 | P3 — Re-base forms onto FormModal (md) | 3 | 🔲 | 006 | P3 | FULL | sonnet/high |
| 051 | P3 — EmailComposer → FormModal; retire legacy SendEmailDialog | 3 | 🔲 | 006 | P3 | FULL | sonnet/high |
| 060 | P4 — RichFilePreviewDialog → Preview/Browse; retire FilePreviewDialog | 4 | 🔲 | 007 | P4 | FULL | sonnet/high |
| 061 | P4 — Re-base FindSimilarDialog onto xl | 4 | 🔲 | 004 | P4 | STANDARD | sonnet/high |
| 070 | P5 — Replace hand-rolled ConversationModal | 5 | 🔲 | 004 | — | FULL | sonnet/**xhigh** |
| 080 | P6 — WizardShell light-first re-base | 6 | 🔲 | 003,008 | — | FULL | sonnet/high |
| 090 | P7 — OOB size scale constants + route via hubs | 7 | 🔲 | none | — | FULL | sonnet/high |
| 091 | P7 — Retire solution-local navigation.ts copies | 7 | 🔲 | 090 | P7 | STANDARD | sonnet/high |
| 092 | P7 — Convert sprk_DocumentOperations.js DOM overlay | 7 | 🔲 | 090 | P7 | STANDARD | sonnet/high |
| 100 | Project wrap-up (MANDATORY) 🔒 | 8 | 🔲 | all | — | FULL | sonnet/high |

🔒 = **main-session only** (writes `.claude/` — sub-agents cannot per CLAUDE.md §3).

---

## Parallel Execution Groups

Tasks in a group run simultaneously once the prerequisite is ✅ (ONE message, multiple `task-execute` calls; max 6 agents/wave).

| Group | Tasks | Prerequisite | Files Touched | Safe |
|-------|-------|--------------|---------------|------|
| **A** | 001, 002, 003 | none | `SprkModal/sizes.ts` · `SprkModal/scaledTheme.ts` · `ModalWindowControls.tsx` | ✅ distinct files |
| **B** | 005, 006, 007, 008 | 004 ✅ | `SprkModal/presets/{Confirm,Choice,Form,Preview,Browse,Wizard}Modal.tsx` | ✅ sibling presets |
| **P1** | 030, 031 | 003 ✅ | UI.Components dialogs vs Compose/AI.Widgets/SpaarkeAi dialogs | ✅ distinct libs |
| **P2** | 040, 041, 042 | 005 ✅ | confirms vs ChoiceDialog vs SprkChat overlay | ✅ distinct files |
| **P3** | 050, 051 | 006 ✅ | NewThread/QuickStart/PinnedMemoryEdit vs EmailComposer | ✅ distinct files |
| **P4** | 060, 061 | 007 ✅ (061 needs 004) | FilePreview vs FindSimilar (×3) | ✅ distinct files |
| **P7** | 091, 092 | 090 ✅ | navigation.ts copies vs sprk_DocumentOperations.js | ✅ distinct files |

**Serial / solo** (dependency joins or high blast radius): 004 (core join), 009 (barrel join), 010/011/012/013 (docs — 011/012/013 main-session), 020, 070 (hardest case), 080 (WizardShell blast radius), 090, 100.

---

## Critical Path

```
[001 · 002 · 003]  →  004 (SprkModal base)  →  [005 · 006 · 007 · 008]  →  009 (exports + a11y)
                                                          │
   docs branch: 004 → 010 → 012 ;  004/011 → 013         │  conversion waves (each gated on its preset):
                                                          ▼
        P1[030·031]   P2[040·041·042]   P3[050·051]   P4[060·061]   P5[070]   P6[080]
        P7: 090 → [091 · 092]   (largely independent of the shell)
                                                          ▼
                                          100 wrap-up (code-review + adr-check + test-diet + repo-cleanup)
```

P0 is the gate for everything. P0.5 (020) needs only 002. P7 (090) is independent of the shell and can start any time. P5 (070) should follow proof of P0 transform-robust centering (validates the shell's hardest invariant).

---

## Rigor Distribution

| Rigor | Count | Applies to |
|-------|-------|-----------|
| **FULL** | 21 | Shell/presets/base, conversions, P0.5, wrap-up |
| **STANDARD** | 6 | Window-controls rollout (030/031), FindSimilar (061), P7 (091/092), standards doc (010), ADR (011), cross-links (013) |
| **MINIMAL** | 1 | Pattern pointer (012) |

`task-execute` Step 0.5 re-derives rigor per task and may override. **TEST-MODIFYING override**: any task adding/modifying `tests/**` runs code-review + adr-check unconditionally.

---

## Phase Summary

| Phase | Tasks | Deliverables |
|-------|-------|--------------|
| 0 Build | 001–013 (13) | SprkModal base + 6 presets + size scale + scaled theme + reconciled window controls + barrel/tests + standards doc + ADR-050 + pattern pointer + cross-links |
| 0.5 App-shell scale | 020 (1) | `--sprk-ui-scale` control (auto breakpoint + Display-size setting) |
| 1 Window-controls | 030–031 (2) | Standard controls on all ~13 dialogs (owner mandate) |
| 2 Confirms & choices | 040–042 (3) | ConfirmModal/ChoiceModal re-base; ActionConfirmationDialog retired |
| 3 Forms & compose | 050–051 (2) | FormModal re-base (md); legacy SendEmailDialog retired |
| 4 Preview & browse | 060–061 (2) | Preview/Browse re-base; @deprecated FilePreviewDialog retired |
| 5 Messages overlay | 070 (1) | ConversationModal → SprkModal (centering validation) |
| 6 Wizards | 080 (1) | WizardShell light-first re-base (embedded + stepper retained) |
| 7 OOB consolidation | 090–092 (3) | OOB size scale + two-hub routing; navigation.ts copies retired; DocumentOperations overlay converted |
| 8 Wrap-up | 100 (1) | code-review + adr-check + test-diet + lessons-learned + INDEX + repo-cleanup |

---

## Hot-Path Coordination (per `projects/INDEX.md`)

- **SpaarkeAi=Y** — tasks 020 (app-shell scale) + 031/050 (QuickStartModal) touch `src/solutions/SpaarkeAi/**`. Many active worktrees also touch SpaarkeAi. Run `/conflict-check` before any SpaarkeAi PR.
- **root-CLAUDE.md=Y** — task 013 adds ONE §17 pointer row + a `.claude/CHANGELOG.md` entry. Low conflict; coordinate at merge.
- **BFF=N** — client-only; CLAUDE.md §10 not triggered.

---

## Next Action

Group **A** (001·002·003) ✅ + **004** shell ✅ + Group **B** (005·006·007·008 presets) ✅ complete (2026-08-01) — build green, 81/81 tests pass. **009 is now unblocked.**

Execute task **009** (barrel exports + a11y snapshot + dual-React verify) — serial/solo join (deps 005,006,007,008 ✅). It wires `SprkModal/index.ts` + `export * from './SprkModal'` in `components/index.ts`, adds the a11y snapshot, and verifies dual-React. Then the docs branch (010→012; 011; 013 — main-session) and the conversion phases (P1–P7) open up. Also runnable any time: **020** (P0.5, needs 002 ✅), **090** (P7 OOB, independent).

Run: `/task-execute projects/spaarke-modal-system/tasks/009-barrel-exports-and-tests.poml`, or say "continue".
