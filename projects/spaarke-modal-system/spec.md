# Spaarke Modal System — AI Implementation Specification

> **Status**: Ready for Implementation — all open questions resolved (owner, 2026-08-01)
> **Created**: 2026-07-31
> **Source**: `design.md` (Mode-1 prototype validated 2026-07-31; visual decisions locked in design §0)
> **Prototype reference**: `c:/code_files/spaarke-prototype/projects/2026-07-sprk-modal-system/`

## Executive Summary

Spaarke opens modals from eight surfaces with **no canonical component layer** — every custom Fluent v9 dialog rebuilds its own chrome, producing ≥6 conflicting sizes, ~6 header patterns, contradictory close-affordance rules, a copy-pasted sizing hack, `ModalWindowControls` adopted by only 1 of ~13 dialogs, and 3 hand-rolled overlays. This project delivers **`SprkModal`** — a single canonical modal shell (envelope + standard header + body + footer, named sizes/layouts, `--sprk-ui-scale`-aware, light/dark, transform-robust) plus a thin preset family — in `@spaarke/ui-components`, a canonical design-guide standard, and a phased conversion (P0 build → P7 OOB consolidation) that retires the bespoke dialogs and hand-rolled overlays. It is the *component* layer beneath the existing *decision* layer (`MODAL-DECISION-CRITERIA.md`). Client-only; no BFF impact.

## Scope

### In Scope
- **P0 — Build**: `SprkModal` base + presets (`ConfirmModal`, `ChoiceModal`, `FormModal`, `PreviewModal`, `BrowseModal`, `WizardModal`) in `@spaarke/ui-components`; named size scale; standard header/footer; dismiss semantics; scale-compatible sizing; native thin scrollbar; standards doc; pattern pointer; new ADR.
- **P0.5 — App-shell UI-scale control** (`--sprk-ui-scale`: auto ≥2560 breakpoint + user "Display size" override, scaled-theme). Confirmed in-project (2026-08-01).
- **P1 — Window-controls rollout** to all ~13 existing dialogs (Dataverse expand/collapse glyph + ×).
- **P2 — Confirms & choices** re-based; retire `ActionConfirmationDialog` overlay.
- **P3 — Forms & compose** re-based; unify mid-size to `md`; retire legacy `SendEmailDialog`.
- **P4 — Preview & browse** re-based; retire `@deprecated` `FilePreviewDialog`.
- **P5 — Messages overlay**: replace hand-rolled `ConversationModal` with `SprkModal`.
- **P6 — Wizards** re-based on `SprkModal` chrome tokens (keep `embedded` mode + stepper).
- **P7 — OOB launch consolidation**: route all `navigateTo` through the two sanctioned hubs; apply the OOB size scale; retire solution-local `navigation.ts` copies; convert the `sprk_DocumentOperations.js` DOM overlay.
- Update `docs/standards/MODAL-DECISION-CRITERIA.md` with a "component layer" cross-link; add a pointer row in root `CLAUDE.md` §17.

### Out of Scope
- Replacing OOB `Xrm.Navigation.navigateTo` for **full maker-authored main-form editing** (business rules/subgrids/ribbon/form scripts). OOB stays OOB (design §5); we standardize only its size.
- Any BFF / server change (client-only).
- Power Automate / Dataverse plugins (core-product constraint).
- The cross-iframe **mount transport** concern — that is the (superseded) `spaarke-iframe-wizard-pattern-enhancement` project; this project is not a dependency of it (design §9).
- Redesign of modal *content* (this standardizes the frame, not what's inside).
- A new virtualized list / "lazy load" component — noted as a separate content concern, not this project (design §6.10).

### Affected Areas
- `src/client/shared/Spaarke.UI.Components/src/components/` — NEW `SprkModal/` + presets; modify `ModalWindowControls`, `ChoiceDialog`, `RichFilePreviewDialog`, `FilePreviewDialog`(retire), `SendEmailDialog`×2, `NewThreadModal`, `WizardShell`, `CloseProjectDialog`, `SprkChat/ActionConfirmationDialog`(retire), `RecordNavigationModalShell`(compose), `OrientationToggle`.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeConflictDialog.tsx`
- `src/client/shared/Spaarke.AI.Widgets/src/components/memory/PinnedMemory*.tsx`
- `src/client/pcf/CommunicationConversationPanel/.../ConversationModal.tsx` (P5)
- `src/solutions/SpaarkeAi/src/components/conversation/QuickStartModal.tsx` (P3)
- `src/solutions/{LegalWorkspace,SmartTodo}/src/utils/navigation.ts` (P7 — retire)
- `src/client/webresources/js/sprk_DocumentOperations.js` (P7 — convert)
- `docs/standards/MODAL-DESIGN-SYSTEM.md` (NEW); `docs/standards/MODAL-DECISION-CRITERIA.md`; `.claude/patterns/ui/modal-shell.md` (NEW); `.claude/adr/` (NEW ADR); root `CLAUDE.md` §17.

## Requirements

### Functional Requirements

**Core shell (P0)**
1. **FR-01 — SprkModal base.** A `SprkModal` component in `@spaarke/ui-components` owns the Fluent v9 `Dialog`/`DialogSurface` envelope + standard header + body + footer, taking `size`, `layout`, `dismiss`, `uiScale`, `maximizable`, `nav`, `headerActions`, `footerStart`, `footer`, `padded`, `bodyScroll`. — Acceptance: consumer supplies content + intent only; shell renders all chrome; matches prototype `SprkModal.tsx`.
2. **FR-02 — Named size scale.** Sizes `xs/sm/md/lg/xl/full/wizard` per design §6.2, width `min(cap·uiScale px, N·vw)`, height `min(N·vh, maxPx·uiScale)` for `md`(1040/720)/`lg`(1280/880). — Acceptance: at 1280/1440/2560 CSS px every size fits with footer visible and holds landscape aspect (no square `md`/`lg` on tall monitors).
3. **FR-03 — Standard header.** `space-between`: left = optional `‹ N of M ›` nav + ellipsized title; right = optional actions + `ModalWindowControls` (maximize/restore + ×) using the **Dataverse `FullScreenMaximize/Minimize` glyph**. Bottom border `strokeWidthThin`/`colorNeutralStroke2`. — Acceptance: title-source rule prevents double-header; controls present on every modal.
4. **FR-04 — Standard footer.** `space-between`: **Cancel always left** (`footerStart`), navigation/primary actions right (`footer`, N buttons). Danger primary via a token class (no inline color). Footer omittable. — Acceptance: wizard shows Cancel · Skip · Back · Next; form shows Cancel⟷Save; matches production "Create New Matter".
5. **FR-05 — Dismiss semantics.** `dismiss` = `light` (backdrop+ESC) | `explicit` (no light-dismiss) | `alert` (`modalType="alert"`, no ESC/backdrop). — Acceptance: destructive/blocking dialogs use `alert`; forms use `explicit`.
6. **FR-06 — Scale-compatible sizing.** All dimensions expressed as `min(calc(px * var(--sprk-ui-scale, 1)), vw)`; scale realized via a **scaled Fluent theme** (NOT CSS `zoom` — rejected: under-scales portaled fixed dialogs at 4K). — Acceptance: at `--sprk-ui-scale` 1.0/1.15/1.25/1.5 the whole modal (incl. Fluent internals) grows without clipping.
7. **FR-07 — Native thin scrollbar body.** Body scrolls natively with a modern thin scrollbar (`scrollbar-width: thin`, rounded inset thumb, hover-darken, `scrollbar-gutter: stable`). No chevron scroll overlay. — Acceptance: matches Fluent standard; native wheel/keyboard/touch work.
8. **FR-08 — Theming + centering.** No `FluentProvider` inside the shell (inherits host); semantic tokens only; light/dark parity; transform-robust centering via the Fluent portal (mounts above transformed ancestors). — Acceptance: correct in light+dark; centered when an ancestor has a CSS transform.
9. **FR-09 — Presets.** `ConfirmModal` (+destructive), `ChoiceModal` (ADR-023 preserved), `FormModal`, `PreviewModal`, `BrowseModal` (nav), `WizardModal` (stepper sidebar) — each a thin config of `SprkModal`. — Acceptance: each renders standard chrome; `ChoiceModal` preserves ADR-023 behavior.
10. **FR-10 — Standards + pattern + ADR.** `docs/standards/MODAL-DESIGN-SYSTEM.md` (sizes, layouts, header/footer, theming, names, wiring, do/don't); `.claude/patterns/ui/modal-shell.md` pointer; new ADR "Canonical Modal Shell"; `MODAL-DECISION-CRITERIA.md` component-layer cross-link; `CLAUDE.md` §17 pointer. — Acceptance: docs published + cross-linked.
11. **FR-11 — OOB size scale.** Named OOB sizes `record` (85%×85%), `create-form` (70%×80%), `wizard` (60%×70%); all OOB launches route through `xrmNavigationServiceAdapter` / `wizardLaunchers`. — Acceptance: one source of truth for OOB dimensions.

**Conversion (P1–P7)**
12. **FR-12 (P1)** — `ModalWindowControls` reconciled to the Dataverse glyph and adopted by all ~13 dialogs. — Acceptance: every custom modal shows the standard maximize/restore + ×.
13. **FR-13 (P2)** — `ChoiceDialog`, `ComposeConflictDialog`, `PinnedMemoryEditDialog`, `PinnedMemoryDeleteConfirmation`, `CloseProjectDialog` re-based; **`ActionConfirmationDialog` overlay retired**. — Acceptance: no hand-rolled confirm overlay remains.
14. **FR-14 (P3)** — `NewThreadModal`, both `SendEmailDialog`s (legacy retired), `QuickStartModal` re-based to `FormModal`/`md`. — Acceptance: single mid-size (`md` 1040×72vh); legacy `SendEmailDialog` removed.
15. **FR-15 (P4)** — `RichFilePreviewDialog`→`PreviewModal`/`BrowseModal`; **`FilePreviewDialog` (@deprecated) retired**; `FindSimilarDialog` re-based. — Acceptance: one preview/browse surface; deprecated dialog gone.
16. **FR-16 (P5)** — hand-rolled `ConversationModal` replaced by `SprkModal` (`md`), validating transform-robust centering. — Acceptance: Messages modal uses the shell; centering correct.
17. **FR-17 (P6)** — `WizardShell` internals re-based on `SprkModal` header/footer/size tokens + `ModalWindowControls`, retaining `embedded` mode + stepper; all `Create*`/direct wizards inherit. — Acceptance: wizard chrome matches the standard; iframe web-resource builds unaffected.
18. **FR-18 (P7)** — all `navigateTo` routed through the two hubs at the OOB size scale; solution-local `navigation.ts` copies retired; `sprk_DocumentOperations.js` DOM overlay converted to a supported path. — Acceptance: no bypassing launch sites; no hand-rolled DOM overlay remains.

### Non-Functional Requirements
- **NFR-01 — Resolution.** Build/QA baseline `1440×900`; hard floor `1280×720` (nothing clips, footer + primary always visible); upper `2560×1440` (no stretch/lost). (design §6.2)
- **NFR-02 — Accessibility (WCAG 2.1 AA).** Focus trap, ESC per `dismiss`, `aria-modal`, announced title, keyboard-operable window controls + nav, native scroll preserved.
- **NFR-03 — Token discipline.** Semantic tokens only; **no** hex, `'1px'` literals, or inline color styles in modal components (strengthens ADR-021).
- **NFR-04 — Dual React compat.** Compiles clean under `@types/react` 18 (PCF consumers) and React 19 (Code Pages) — see `[[feedback_shared-lib-react-version-tension]]`.
- **NFR-05 — Client-only.** Zero BFF / publish-size impact (CLAUDE.md §10 not triggered).
- **NFR-06 — Reuse.** Net reusable-component count **decreases** (composes `RecordNavigationModalShell` + `ModalWindowControls`; retires ~13 bespoke dialogs + 3 overlays).

## Technical Constraints

### Applicable ADRs
- **ADR-012** — shared components live in `@spaarke/ui-components`; not duplicated per solution.
- **ADR-021** — Fluent UI v9 exclusively; semantic tokens only. *This project strengthens it.*
- **ADR-023** — `ChoiceDialog` pattern preserved via `ChoiceModal`.
- **ADR-028** — never snapshot tokens/auth in modal props; pass `authenticatedFetch` as a function.

### MUST Rules
- ✅ MUST compose existing primitives (`RecordNavigationModalShell`, `ModalWindowControls`, `OrientationToggle`) — do NOT create parallel abstractions (CLAUDE.md §11).
- ✅ MUST use semantic tokens only; MUST NOT use hex / `'1px'` literals / inline color styles.
- ✅ MUST keep the Fluent `Dialog` envelope (transform-robust portal) — MUST NOT hand-roll overlays.
- ✅ MUST realize `--sprk-ui-scale` via a scaled theme; MUST NOT use CSS `zoom`.
- ✅ MUST scroll body natively (thin scrollbar); MUST NOT add chevron scroll overlays for vertical content.
- ✅ MUST keep OOB `navigateTo` for full main-form editing; MUST NOT iframe-embed OOB `main.aspx` (MODAL-DECISION-CRITERIA anti-pattern #4).

### Existing Patterns
- Prototype: `spaarke-prototype/projects/2026-07-sprk-modal-system/src/components/{SprkModal,ModalWindowControls,ModalScrollArea,presets,sizes}` — the visual contract to implement.
- `RecordNavigationModalShell/README.md` — browse chrome + dirty-check.
- `RichFilePreviewDialog.tsx` — the renderer/envelope split to formalize.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>N</bff>
  <spaarkeai>Y</spaarkeai>        <!-- P3 re-bases src/solutions/SpaarkeAi QuickStartModal; SpaarkeAi consumes the shell -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>Y</root-claude-md> <!-- §17 pointer to MODAL-DESIGN-SYSTEM.md -->
</hot-path-declaration>
```
BFF=N → no publish-size / bff-extensions concern.

### New Components (§11 three-question gate)
| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `SprkModal` base | `RecordNavigationModalShell` (chrome, no envelope), `WizardShell` (wizard-only envelope), ~13 bespoke dialogs | Composes `RecordNavigationModalShell` + `ModalWindowControls`; the *unified envelope+size+footer* is genuinely new surface that lets all bespoke dialogs collapse into it (net count ↓) | Without it the ~13 dialogs keep diverging: contradictory close rules, ≥6 sizes, square-on-4K, 3 hand-rolled overlays that break centering/a11y |
| Presets (`Confirm/Choice/Form/Preview/Browse/Wizard Modal`) | the bespoke dialogs they replace | They ARE the extension seam (thin configs of `SprkModal`) | Callers otherwise re-hand-roll chrome per surface |
| `MODAL-DESIGN-SYSTEM.md` + ADR + pattern | `MODAL-DECISION-CRITERIA.md` (decision layer only) | Complements, not duplicates (component layer) | New dialogs drift with no component standard to cite |

## ADR Tensions (per CLAUDE.md §6.5)

> No ADR tensions surfaced at design time. The project **strengthens** ADR-021 (bans `'1px'`/inline colors in modals) and **preserves** ADR-023 (`ChoiceModal` re-base). ADR-012/028 apply without exception. The one reconciliation is cosmetic: the shipped `ModalWindowControls` icon (owner UAT 2026-07-31, `ArrowMaximize`) is updated to the Dataverse `FullScreenMaximize/Minimize` glyph per owner review 2026-07-31 — same owner, refined decision, not an ADR conflict.

## Success Criteria
1. [ ] `SprkModal` + 6 presets ship in `@spaarke/ui-components`, compiling under `@types/react` 18 + 19 — Verify: build both PCF and Code Page consumers.
2. [ ] Every size fits + holds landscape aspect at 1280/1440/2560 — Verify: visual check against the prototype contract at all three widths.
3. [ ] `--sprk-ui-scale` at 1.0–1.5 scales the whole modal (incl. Fluent internals) with no clipping — Verify: scaled-theme demo.
4. [ ] All ~13 custom dialogs carry the standard window controls (P1) — Verify: grep + visual.
5. [ ] Hand-rolled overlays retired: `ActionConfirmationDialog`, `ConversationModal`, `sprk_DocumentOperations.js` — Verify: grep for `position:fixed;inset` / `createElement` overlay patterns returns none in scope.
6. [ ] `@deprecated FilePreviewDialog` + legacy `SendEmailDialog` removed — Verify: file absence + no imports.
7. [ ] All `navigateTo` route through the two hubs at the OOB size scale; solution-local `navigation.ts` retired (P7) — Verify: grep call sites.
8. [ ] Net reusable modal-component count decreases — Verify: before/after inventory.
9. [ ] WCAG 2.1 AA per modal (focus/ESC/aria/keyboard/native-scroll) — Verify: a11y snapshot + manual keyboard pass.
10. [ ] Standards doc + ADR + pattern pointer published and cross-linked — Verify: files exist + `MODAL-DECISION-CRITERIA.md` links the component layer.

## Dependencies

### Prerequisites
- Prototype visual contract (done — `spaarke-prototype/projects/2026-07-sprk-modal-system/`).
- The two OOB launch hubs (`xrmNavigationServiceAdapter`, `wizardLaunchers`) exist (they do).

### External
- None (client-only; no BFF, no external service, no approvals beyond code review).

## Owner Clarifications

*Locked during the Mode-1 prototype review (2026-07-31):*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Naming | Base + preset names? | `SprkModal` + `*Modal` presets | Public API names |
| Mid-size | `md` dimensions? | `min(1040px,92vw) × min(72vh,720px)` (height cap) | FR-02; fixes square-on-tall-monitor |
| Footer | Button layout? | Cancel always left; actions right (`space-between`) | FR-04 |
| Window controls | Which glyph? | Dataverse `FullScreenMaximize/Minimize` + × | FR-03, FR-12 |
| Centering | Envelope? | Fluent `Dialog` (portal is transform-robust) | FR-08; P5 is validation |
| Scale mechanism | Scaled theme or CSS zoom? | **Scaled theme** (zoom under-scales at 4K) | FR-06 |
| Body scroll | Scrollbar or chevrons? | **Modern native thin scrollbar** (Fluent standard); chevrons rejected | FR-07 |
| OOB vs custom | Go 100% custom? | No — OOB reserved for full main form; SprkModal default + hybrid escalation | Scope §5 |
| Resolution | Build target? | Baseline 1440×900, floor 1280×720, upper 2560×1440 | NFR-01 |

## Decisions Confirmed (owner, 2026-08-01)

*Resolutions for the design §11 open items:*

- **ADR strategy (§11-C):** ✅ **New focused ADR** "Canonical Modal Shell" (references ADR-021/023) — greppable, self-contained. (FR-10)
- **UI-scale ownership (§11-I):** ✅ Build the app-shell scale control (`--sprk-ui-scale` auto ≥2560 breakpoint + "Display size" setting) **as Phase 0.5 in this project**. Scale-compatible sizing in the shell is mandatory regardless. (FR-06 + new P0.5)
- **Wizard re-base depth (§11-G):** ✅ **Light-first** — align tokens/size/window-controls in P6; keep the WizardShell envelope + `embedded` mode intact; no full internal re-base this pass. (FR-17)
- **Iframe project (§11-E):** ✅ **Close as superseded** — not a dependency of this project; a second mount push-channel is forbidden (Notification Spine, ADR-047). Formal closure is a separate portfolio action.
- **Program size:** ✅ One project, 8 phases (P0–P7); `project-pipeline` decomposes into tasks. Phases independently shippable.

### Remaining default assumption
- **OOB consolidation (§11-H):** collapsing current 80% sites to `record` (85%) / `create-form` (70-80%) is acceptable, pending a one-time visual review **during P7** (not blocking earlier phases). (FR-18)

## Unresolved Questions
*All design §11 open items resolved by owner 2026-08-01 (see Decisions Confirmed). None block implementation.*
- [ ] (P7 only) Confirm the 80%→85/70 OOB size collapse is visually acceptable per surface — resolved during P7 review.

---
*AI-optimized specification. Original design: `design.md` (prototype-validated). Preserve both as project artifacts.*
