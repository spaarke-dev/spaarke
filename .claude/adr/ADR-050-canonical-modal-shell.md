# ADR-050: Canonical Modal Shell (Concise)

> **Status**: Accepted (2026-08-01)
> **Domain**: UI/UX — Modal System
> **Project**: spaarke-modal-system
> **References**: strengthens [ADR-021](ADR-021-fluent-design-system.md); preserves the Choice Dialog pattern ([`.claude/patterns/ui/choice-dialog-pattern.md`](../patterns/ui/choice-dialog-pattern.md), formerly ADR-023); composes under [ADR-012](ADR-012-shared-components.md); auth per [ADR-028](ADR-028-spaarke-auth-architecture.md)

---

## Decision

All Spaarke modals are built on **ONE canonical shell — `SprkModal`** (in `@spaarke/ui-components`) plus a small set of **thin presets** (`ConfirmModal`, `ChoiceModal`, `FormModal`, `PreviewModal`, `BrowseModal`, `WizardModal`). `SprkModal` owns the Fluent v9 `Dialog`/`DialogSurface` envelope, a named size scale, the standard header (browse nav · title · window controls), a native thin-scrollbar body, the standard footer (Cancel-left / actions-right), and the three dismiss modes. Every surface's modal is a **thin config** of `SprkModal` or a preset — never a new envelope.

This replaces ~13 bespoke dialogs + 3 hand-rolled overlays that had drifted into ≥6 conflicting large-modal sizes, contradictory close rules, square-on-4K sizing, and broken centering/a11y. **Net component count decreases.**

Component-layer reference: [`docs/standards/MODAL-DESIGN-SYSTEM.md`](../../docs/standards/MODAL-DESIGN-SYSTEM.md). Decision-layer reference (which family to reach for): [`docs/standards/MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md).

---

## Constraints

### MUST

- **MUST** build every new modal as a thin config of `SprkModal` (or an existing preset) from `@spaarke/ui-components` — one canonical shell, presets as the extension seam (ADR-012).
- **MUST** compose the existing primitives — `ModalWindowControls` (maximize/restore + close ×, Dataverse `FullScreenMaximize/Minimize` glyph) and `RecordNavigationModalShell` (browse "N of M" + cross-frame dirty-check) — rather than forking or re-declaring parallel chrome.
- **MUST** keep the Fluent `Dialog`/`DialogSurface` envelope: its portal mounts above a CSS-transformed ancestor, so centering survives transforms (the bug that forced the hand-rolled overlays).
- **MUST** realize `--sprk-ui-scale` via a **scaled Fluent theme** (`scaleTheme` multiplies the px-valued size/spacing/stroke/radius/font tokens) so Fluent's own internals grow at 2K/4K.
- **MUST** use semantic Fluent v9 tokens only in modal components — **zero hex, zero `'1px'` literals** (use `tokens.strokeWidthThin`), **zero inline color styles**; danger styling via a `makeStyles` token class. (This is the ADR-021 strengthening.)
- **MUST** scroll the body natively with a thin scrollbar; the chevron pager (`ModalScrollArea`, `bodyScroll="arrows"`) is an ADDITIONAL opt-in affordance that never disables native scroll (WCAG).
- **MUST** put Cancel on the LEFT (`footerStart`); navigation/primary actions on the right (`footer`).
- **MUST** compile clean under `@types/react` 18 (PCF) and React 19 (Code Pages).

### MUST NOT

- **MUST NOT** hand-roll a `position:fixed` / `document.createElement` overlay for a modal — retire the three that existed (`ActionConfirmationDialog`, `ConversationModal`, `sprk_DocumentOperations.js`).
- **MUST NOT** use CSS `zoom` to scale a modal (it under-scales a portaled `position:fixed` dialog at 4K) — use the scaled theme.
- **MUST NOT** give a surface its own bespoke modal envelope, size, or close-rule — configure `SprkModal` instead.
- **MUST NOT** hardcode a per-entity modal size — OOB record open is 85%×85% for every entity (see the decision-layer standard).

---

## Key patterns

```tsx
// A form modal — consumer supplies content + intent; the shell supplies all chrome.
<FormModal open={open} onClose={close} onSubmit={save} title="Edit Matter" size="md">
  <MatterFields />
</FormModal>

// Browse across a record set — single header source; guard hook wires the dirty-check.
<BrowseModal
  open={open} onClose={close} title={doc.name}
  nav={{ index, total, onNavigate }}
  onBeforeNavigate={confirmDiscardIfDirty}
  metadata={docMeta}
/>
```

The named size scale (`xs`/`sm`/`md`/`lg`/`xl`/`full`/`wizard`), header/footer contracts, and dismiss semantics (`light`/`explicit`/`alert`) are specified in [`docs/standards/MODAL-DESIGN-SYSTEM.md`](../../docs/standards/MODAL-DESIGN-SYSTEM.md).

---

## Rationale

"Compose, don't create" (root CLAUDE.md §11): one shell that works exceptionally beats thirteen that partially overlap. The scaled-theme (not `zoom`) decision is the only way Fluent v9's fixed-px tokens grow correctly on a portaled dialog at 4K (owner-verified 2026-07-31). A focused, greppable ADR was chosen over extending ADR-021 so the modal contract is self-contained (spec Decisions §11-C).

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-021](ADR-021-fluent-design-system.md) | Parent design system — **strengthened** here (bans `'1px'` + inline color in modal components) |
| [ADR-012](ADR-012-shared-components.md) | Shell + presets live in `@spaarke/ui-components`, not duplicated per solution |
| Choice Dialog pattern ([choice-dialog-pattern.md](../patterns/ui/choice-dialog-pattern.md), ex-ADR-023) | **Preserved** — `ChoiceModal` re-bases the 2–4 rich-choice pattern onto the shell, not superseding it |
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | Modal props pass callbacks / `authenticatedFetch` as functions; never snapshot tokens/auth |

---

**Lines**: ~110
