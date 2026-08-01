# Spaarke Modal System

> **Status**: Planning complete (2026-08-01) — 29 tasks across 8 phases (P0–P7 + P0.5). Ready to execute Phase 0.
> **Created**: 2026-07-31

## Graduation Criteria

This project is complete when spec Success Criteria §1–10 are met: `SprkModal` + 6 presets ship in `@spaarke/ui-components` (compiling under React 18 + 19), the 3 hand-rolled overlays + `@deprecated FilePreviewDialog` + legacy `SendEmailDialog` are retired, all `navigateTo` route through the two hubs at the OOB size scale, WCAG 2.1 AA holds per modal, net reusable modal-component count decreases, and `MODAL-DESIGN-SYSTEM.md` + ADR-050 + the pattern pointer are published and cross-linked.

## Artifacts
- [`spec.md`](spec.md) · [`plan.md`](plan.md) · [`CLAUDE.md`](CLAUDE.md) · [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) · [`current-task.md`](current-task.md)

A canonical modal **component library** + **design guide** for every Spaarke surface — the *component layer* beneath the existing *decision layer* ([`docs/standards/MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md)).

## The gap this closes

Spaarke already decides *which modal family* to use, but each custom Fluent v9 dialog rebuilds its own chrome — yielding ≥6 conflicting sizes, ~6 header patterns, contradictory close rules, a copy-pasted Fluent sizing hack, `ModalWindowControls` adopted by only 1 of ~13 dialogs, and 3 hand-rolled overlays. This project composes the existing primitives (`RecordNavigationModalShell`, `ModalWindowControls`, `OrientationToggle`, the `WizardShell` sizing fix) into **one envelope-owning shell** with standard sizes, header/footer, theming, and names, plus a phased conversion plan for the eight surfaces.

## Contents

- [`design.md`](design.md) — full investigation, current-state inventory, proposed system, conversion plan, and open questions.

## Related

- [`docs/standards/MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md) — decision layer (which family).
- [`projects/spaarke-iframe-wizard-pattern-enhancement/`](../spaarke-iframe-wizard-pattern-enhancement/) — complementary sibling (cross-iframe mount *transport*, not modal *chrome*); relationship analyzed in `design.md` §9.
- Owner UAT 2026-07-31 item 4 — the window-controls standardization mandate this project generalizes.
