# Project Plan: Spaarke Modal System

> **Last Updated**: 2026-08-01
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md) · **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Deliver **`SprkModal`** — a single canonical modal shell (envelope + standard header + body + footer, named sizes/layouts, `--sprk-ui-scale`-aware, light/dark, transform-robust) plus a thin preset family — in `@spaarke/ui-components`, a canonical design-guide standard, and a phased conversion that retires ~13 bespoke dialogs and 3 hand-rolled overlays. This is the *component* layer beneath the existing *decision* layer (`MODAL-DECISION-CRITERIA.md`).

**Scope** (key deliverables):
- `SprkModal` base + 6 presets (`ConfirmModal`, `ChoiceModal`, `FormModal`, `PreviewModal`, `BrowseModal`, `WizardModal`) — ported from the prototype visual contract.
- Named size scale (`xs/sm/md/lg/xl/full/wizard`) + scaled Fluent theme (`--sprk-ui-scale`) + native thin scrollbar.
- App-shell UI-scale control (P0.5): auto ≥2560 breakpoint + user "Display size" setting.
- Standards doc `MODAL-DESIGN-SYSTEM.md` + new ADR-050 + pattern pointer + cross-links.
- Phased conversion P1→P7: window-controls rollout, confirms/choices, forms/compose, preview/browse, messages overlay, wizards, OOB launch consolidation.

**Timeline**: ~10–15 dev-days (8 phases, independently shippable) | **Estimated Effort**: ~90–120 hours

**Client-only. Zero BFF / publish-size impact (CLAUDE.md §10 NOT triggered).**

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-012**: shared components live in `@spaarke/ui-components`; not duplicated per solution.
- **ADR-021**: Fluent v9 exclusively; semantic tokens only. *This project strengthens it* (bans hex / `'1px'` literals / inline color styles in modal components).
- **ADR-023**: `ChoiceDialog` pattern preserved via `ChoiceModal` re-base.
- **ADR-028**: never snapshot tokens/auth in modal props; pass `authenticatedFetch` as a function.

**From Spec**:
- Compose existing primitives (`RecordNavigationModalShell`, `ModalWindowControls`, `OrientationToggle`); do NOT create parallel abstractions (CLAUDE.md §11) — net reusable component count **decreases**.
- Keep the Fluent `Dialog` envelope (transform-robust portal); MUST NOT hand-roll overlays.
- Realize `--sprk-ui-scale` via a **scaled theme**, NOT CSS `zoom`.
- Native thin scrollbar body; no chevron scroll overlays for vertical content.
- Keep OOB `navigateTo` for full main-form editing; MUST NOT iframe-embed OOB `main.aspx`.
- Compile clean under `@types/react` 18 (PCF) and React 19 (Code Pages).

### Key Technical Decisions (locked by Mode-1 prototype 2026-07-31 + owner 2026-08-01)

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Base = `SprkModal` + thin `*Modal` presets | Reads better at call sites, tree-shakeable | Public API |
| `md = min(1040·scale, 92vw) × min(72vh, 720·scale)` | 720 read as portrait; height cap holds landscape on tall monitors | FR-02 |
| Cancel always left; actions right (`space-between`) | Matches production "Create New Matter" | FR-04 |
| Dataverse `FullScreenMaximize/Minimize` glyph + × | Match native OOB dialog | FR-03, FR-12 |
| Scaled Fluent theme (NOT CSS `zoom`) | Zoom under-scales portaled fixed dialogs at 4K | FR-06 |
| Native thin scrollbar (chevrons rejected) | Fluent standard; chevrons cover content | FR-07 |
| New focused ADR-050 (not extend ADR-021) | Greppable, self-contained | FR-10 |
| App-shell UI-scale control built here as P0.5 | Scale-compatible sizing is mandatory anyway | FR-06 / P0.5 |
| Wizard re-base = light-first (P6) | Blast radius of all `Create*` wizards | FR-17 |

### Discovered Resources

**Prototype visual contract** (the implementation to copy):
- `c:/code_files/spaarke-prototype/projects/2026-07-sprk-modal-system/src/components/` — `SprkModal.tsx`, `ModalWindowControls.tsx`, `ModalScrollArea.tsx` (rejected arrows variant), `presets.tsx` (ConfirmModal/FormModal/PreviewModal(+nav=Browse)/WizardModal — **ChoiceModal NOT present, build fresh**), `sizes.ts` (`SIZE_SPEC`/`getSurfaceStyle`), `src/theme.ts` (`scaleTheme`/`baseTheme`).

**Applicable Skills**:
- `fluent-v9-component` — component authoring (CRITICAL)
- `code-review`, `adr-check` — quality gates at task-execute Step 9.5
- `adr-aware`, `spaarke-conventions`, `ui-test`, `context-handoff`

**Knowledge / patterns**:
- `.claude/patterns/ui/fluent-v9-component-authoring.md`, `fluent-v9-react-version-boundaries.md`, `fluent-v9-theming.md`, `fluent-v9-portal-gotcha.md`, `record-modal-selection.md`, `choice-dialog-pattern.md`
- `docs/standards/MODAL-DECISION-CRITERIA.md` (decision layer — gets a component-layer cross-link)
- `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md`

**Reusable code (compose, don't fork)**:
- `RecordNavigationModalShell` (browse chrome + dirty-check), `ModalWindowControls`, `OrientationToggle`, `WizardShell` (sizing clamp-bypass), `RichFilePreview` (renderer/envelope split).

---

## 3. Implementation Approach

### Phase Structure

```
P0   Build shell + presets + tokens + scaled theme + standards/ADR/pattern   (foundation)
P0.5 App-shell UI-scale control (--sprk-ui-scale auto breakpoint + setting)
P1   Window-controls rollout to all ~13 dialogs (mandate; high-visibility)
P2   Confirms & choices; retire ActionConfirmationDialog overlay
P3   Forms & compose; unify md; retire legacy SendEmailDialog
P4   Preview & browse; retire @deprecated FilePreviewDialog
P5   Messages overlay: replace hand-rolled ConversationModal (hardest case)
P6   Wizards: light-first re-base (keep embedded + stepper)
P7   OOB launch consolidation (two hubs, OOB size scale, retire navigation.ts)
```

### Critical Path

```
P0: 001/002/003 (foundation) → 004 (SprkModal base) → 005-008 (presets) → 009 (exports+tests)
                                                                    │
   ┌────────────────────────────────────────────────────────────────┘
   ▼
P1 (030/031) · P2 (040-042) · P3 (050-051) · P4 (060-061) · P5 (070) · P6 (080)  — all gated on P0 presets
P7 (090→091/092) — largely independent of the shell (OOB launch layer)
100 wrap-up — gated on all
```

**High-Risk Items:**
- **P5 ConversationModal** (`high`) — the hand-roll exists *because* of a transform-ancestor centering bug; re-basing validates the shell's hardest requirement. Mitigation: build P5 only after P0 centering is proven; keep the old overlay until visual QA passes.
- **P6 WizardShell** (`high` blast radius) — all `Create*`/direct wizards + iframe web-resource builds inherit. Mitigation: light-first (tokens/size/window-controls only; keep `embedded` mode + stepper envelope); verify iframe builds unaffected.
- **Dual-React drift** — shared-lib UI must compile under `@types/react` 18 (PCF) and 19 (Code Page). Mitigation: no React-18/19-exclusive APIs; verify both consumers build (NFR-04).

---

## 4. Phase Breakdown

### P0 — Build the shell (foundation)

**Objectives:** Port the prototype into `@spaarke/ui-components` as production `SprkModal` + presets; publish standards/ADR/pattern.

**Deliverables:**
- [ ] `sizes.ts` (size scale + `getSurfaceStyle`) + scaled theme (`scaleTheme`/`baseTheme`)
- [ ] `ModalWindowControls` reconciled to the Dataverse glyph
- [ ] `SprkModal` base (envelope + header + native-thin-scroll body + footer + dismiss + maximizable + nav)
- [ ] `ConfirmModal`, `ChoiceModal` (fresh, ADR-023), `FormModal`, `PreviewModal`, `BrowseModal` (nav), `WizardModal`
- [ ] Barrel exports + unit/a11y tests
- [ ] `docs/standards/MODAL-DESIGN-SYSTEM.md`, ADR-050, `.claude/patterns/ui/modal-shell.md`, `MODAL-DECISION-CRITERIA.md` cross-link, root `CLAUDE.md` §17 pointer

**Outputs**: `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/**` + docs/ADR/pattern.

### P0.5 — App-shell UI-scale control

**Objectives:** Land the `--sprk-ui-scale` app-shell control (auto ≥2560 breakpoint + user "Display size" Default/Large/Extra-large) driving the scaled theme; persist via the existing code-page theme-storage pattern.

**Outputs**: scale control in SpaarkeAi + LegalWorkspace + code-page shells (SpaarkeAi hot-path).

### P1 — Window-controls rollout (mandate)

**Objectives:** Drop the reconciled `ModalWindowControls` into every existing dialog header via an interim adapter, ahead of full re-basing (satisfies the 2026-07-31 owner mandate quickly and visibly).

**Targets**: all ~13 custom dialogs. **Risk**: low, high-visibility.

### P2 — Confirms & choices

**Objectives:** Re-base onto `ConfirmModal`/`ChoiceModal`; **retire `ActionConfirmationDialog`** hand-rolled overlay.
**Targets**: ChoiceDialog, ComposeConflictDialog, PinnedMemory×2, CloseProjectDialog, ActionConfirmationDialog.

### P3 — Forms & compose

**Objectives:** Re-base onto `FormModal`; resolve mid-size to `md`; **retire legacy `SendEmailDialog`**.
**Targets**: NewThreadModal, SendEmailDialog×2, PinnedMemoryEditDialog, QuickStartModal (SpaarkeAi hot-path).

### P4 — Preview & browse

**Objectives:** Re-base onto `PreviewModal`/`BrowseModal`; **retire @deprecated `FilePreviewDialog`**.
**Targets**: RichFilePreviewDialog, FilePreviewDialog, FindSimilarDialog.

### P5 — Messages overlay

**Objectives:** Replace hand-rolled `ConversationModal` with `md` `SprkModal` — validates transform-robust centering.
**Targets**: ConversationModal. **Risk**: high.

### P6 — Wizards

**Objectives:** Light-first re-base of `WizardShell` internals on `SprkModal` header/footer/size tokens + `ModalWindowControls`; retain `embedded` mode + stepper.
**Targets**: WizardShell + all `Create*`/direct wizards. **Risk**: high (blast radius).

### P7 — OOB launch consolidation

**Objectives:** Route all `navigateTo` through the two hubs at the OOB size scale (`record` 85%, `create-form` 70/80%, `wizard` 60/70%); retire solution-local `navigation.ts` copies; convert `sprk_DocumentOperations.js` DOM overlay.
**Targets**: ~85–100 OOB call sites, ribbon JS. **Risk**: medium.

---

## 5. Dependencies

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| Prototype visual contract | `spaarke-prototype/projects/2026-07-sprk-modal-system/` | Done (validated 2026-07-31) |
| `RecordNavigationModalShell` / `ModalWindowControls` / `OrientationToggle` / `WizardShell` | `@spaarke/ui-components` | Production |
| OOB launch hubs (`xrmNavigationServiceAdapter`, `wizardLaunchers.ts`) | `@spaarke/ui-components` / WorkspaceShell | Production |

### External Dependencies
- None (client-only; no BFF, no external service, no approvals beyond code review).

### Active-project coordination (hot-path — per `projects/INDEX.md`)
- **SpaarkeAi=Y** — P0.5 (app-shell scale) + P3 (QuickStartModal) touch `src/solutions/SpaarkeAi/**`. Many active worktrees also touch SpaarkeAi (compose-r5, analysis-hub-r1, agreements-r1, email-r5, messaging-r2/r3, notification-spine-r1). Our touch is narrow (scale wiring + one dialog re-base). Run `/conflict-check` before any SpaarkeAi PR.
- **root-CLAUDE.md=Y** — task 013 adds ONE §17 pointer row. Low conflict; coordinate at merge.

---

## 6. Testing Strategy

**Unit Tests**: shell render/chrome/sizing/dismiss/maximize; each preset's config; ChoiceModal ADR-023 behavior; scaled-theme token multiplication; native-scroll style presence.

**UI / a11y Tests** (per `ui-test`, task-execute Step 9.7): WCAG 2.1 AA per modal — focus trap, ESC per `dismiss`, `aria-modal`, announced title, keyboard-operable window controls + nav, native scroll preserved; ADR-021 dark-mode parity; landscape aspect at 1280/1440/2560; `--sprk-ui-scale` 1.0–1.5 with no clipping.

**Consumer regression**: build PCF (`@types/react` 18) + Code Page (React 19) consumers after each shared-lib wave; iframe wizard web-resource builds unaffected (P6).

---

## 7. Acceptance Criteria

Mirrors spec Success Criteria §1–10:
- [ ] `SprkModal` + 6 presets ship, compile under React 18 + 19.
- [ ] Every size fits + holds landscape aspect at 1280/1440/2560.
- [ ] `--sprk-ui-scale` 1.0–1.5 scales the whole modal (incl. Fluent internals), no clipping.
- [ ] All ~13 custom dialogs carry the standard window controls (P1).
- [ ] Hand-rolled overlays retired: `ActionConfirmationDialog`, `ConversationModal`, `sprk_DocumentOperations.js`.
- [ ] `@deprecated FilePreviewDialog` + legacy `SendEmailDialog` removed.
- [ ] All `navigateTo` route through the two hubs at the OOB size scale; solution-local `navigation.ts` retired.
- [ ] Net reusable modal-component count decreases (before/after inventory).
- [ ] WCAG 2.1 AA per modal.
- [ ] Standards doc + ADR-050 + pattern pointer published + cross-linked.

---

## 8. Risk Register

| ID | Risk | Prob | Impact | Mitigation |
|----|------|------|--------|------------|
| R1 | ConversationModal (P5) re-base re-triggers the transform-ancestor centering bug | Med | High | Prove P0 centering first; keep old overlay until visual QA green |
| R2 | WizardShell (P6) re-base breaks `Create*` wizards / iframe builds | Med | High | Light-first (keep envelope + embedded mode); build all wizard web-resources |
| R3 | Shared-lib change breaks a PCF (React 18) or Code Page (React 19) consumer | Med | Med | No React-exclusive APIs; build both consumers per wave (NFR-04) |
| R4 | OOB 80%→85/70 collapse (P7) reads wrong on some surfaces | Low | Med | One-time per-surface visual review during P7 (non-blocking earlier) |
| R5 | Token-discipline regressions (hex / `'1px'` / inline color) creep into re-bases | Med | Low | grep gate + code-review Step 6 per wave; NFR-03 |

---

## 9. Next Steps

1. **Review this PLAN.md** + TASK-INDEX.
2. **Execute Phase 0** foundation (tasks 001–003 parallel → 004 → presets).
3. Ship phases incrementally (each independently shippable).

---

**Status**: Ready for Tasks
**Next Action**: Execute Phase 0 (task 001 — size scale tokens)

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
