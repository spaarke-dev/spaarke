# ADR-050: Canonical Modal Shell (Concise)

> **Status**: Accepted (2026-08-01) · **Amended 2026-10-07 (D-26, spaarke-ontology-platform-r1)** — `WizardShell` is the wizard preset, `WizardModal` retired, in-app launch rule, no platform-chrome injection (root CLAUDE.md §6.5 path B)
> **Domain**: UI/UX — Modal System
> **Project**: spaarke-modal-system
> **References**: strengthens [ADR-021](ADR-021-fluent-design-system.md); preserves the Choice Dialog pattern ([`.claude/patterns/ui/choice-dialog-pattern.md`](../patterns/ui/choice-dialog-pattern.md), formerly ADR-023); composes under [ADR-012](ADR-012-shared-components.md); auth per [ADR-028](ADR-028-spaarke-auth-architecture.md)

---

## Decision

All Spaarke modals are built on **ONE canonical shell — `SprkModal`** (in `@spaarke/ui-components`) plus a small set of **thin presets** (`ConfirmModal`, `ChoiceModal`, `FormModal`, `PreviewModal`, `BrowseModal`, `WizardShell`). **`WizardShell` is the engine-bearing wizard preset**: it renders inside `SprkModal`, and its **embedded** mode (no envelope) is the sanctioned variant for workspace tabs, full pages and pages hosted under platform chrome. **`WizardModal` is retired** (it had zero consumers). *(Amended 2026-10-07, D-26.)* `SprkModal` owns the Fluent v9 `Dialog`/`DialogSurface` envelope, a named size scale, the standard header (browse nav · title · window controls), a native thin-scrollbar body, the standard footer (Cancel-left / actions-right), and the three dismiss modes. Every surface's modal is a **thin config** of `SprkModal` or a preset — never a new envelope.

This replaces ~13 bespoke dialogs + 3 hand-rolled overlays that had drifted into ≥6 conflicting large-modal sizes, contradictory close rules, square-on-4K sizing, and broken centering/a11y. **Net component count decreases.**

Component-layer reference: [`docs/standards/MODAL-DESIGN-SYSTEM.md`](../../docs/standards/MODAL-DESIGN-SYSTEM.md). Decision-layer reference (which family to reach for): [`docs/standards/MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md).

---

## Constraints

### MUST

- **MUST** build every new modal as a thin config of `SprkModal` (or an existing preset) from `@spaarke/ui-components` — one canonical shell, presets as the extension seam (ADR-012).
- **MUST** compose the existing primitives rather than forking or re-declaring parallel chrome: `ModalWindowControls` (maximize/restore + close ×, Dataverse `FullScreenMaximize/Minimize` glyph) renders inside the shell header; browse "N of M" is the shell's OWN header nav group (single title/counter source — nesting `RecordNavigationModalShell`'s envelope would double the chrome), with `BrowseModal.onBeforeNavigate` as the composition seam through which a consumer wires `RecordNavigationModalShell`'s cross-frame dirty-check / discard-confirm protocol when needed. *(Amended 2026-08-02, task 100 adr-check — the original wording said "compose `RecordNavigationModalShell`" literally, which the shipped seam-based design deliberately does not do; Path B per CLAUDE.md §6.5.)*
- **MUST** keep the Fluent `Dialog`/`DialogSurface` envelope: its portal mounts above a CSS-transformed ancestor, so centering survives transforms (the bug that forced the hand-rolled overlays).
- **MUST** realize `--sprk-ui-scale` via a **scaled Fluent theme** (`scaleTheme` multiplies the px-valued size/spacing/stroke/radius/font tokens) so Fluent's own internals grow at 2K/4K.
- **MUST** use semantic Fluent v9 tokens only in modal components — **zero hex, zero `'1px'` literals** (use `tokens.strokeWidthThin`), **zero inline color styles**; danger styling via a `makeStyles` token class. (This is the ADR-021 strengthening.)
- **MUST** scroll the body natively with a thin scrollbar; the chevron pager (`ModalScrollArea`, `bodyScroll="arrows"`) is an ADDITIONAL opt-in affordance that never disables native scroll (WCAG).
- **MUST** put Cancel on the LEFT (`footerStart`); navigation/primary actions on the right (`footer`).
- **MUST** compile clean under `@types/react` 18 (PCF) and React 19 (Code Pages).
- **MUST** use `WizardShell` for every multi-step flow. No other wizard engine or step reducer may be introduced. *(Amended 2026-10-07, D-26.)*
- **MUST (launch rule)**: when the caller is a Spaarke React surface (Console/SpaarkeAi widgets, LegalWorkspace sections running in SpaarkeAi, code pages, PCFs with the component in their bundle), open modals and wizards **in-app** via the `SprkModal` family. `Xrm.Navigation.navigateTo(webresource|custom, { target: 2 })` is reserved for **hostless** contexts (model-driven ribbon/command scripts) or a deliberately separate deployable: its title bar, expand and close button are **platform chrome — light-only and not themeable** ([navigateTo](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto); [modern theming — dark mode not supported](https://learn.microsoft.com/en-us/power-apps/user/modern-fluent-design)). *(Amended 2026-10-07, D-26.)*
- **MUST** render `WizardShell` **embedded with `hideTitle`** on a page shown under platform chrome (no double header). This codifies existing practice. *(Amended 2026-10-07, D-26.)*
- **MUST** default wizards to `dismiss="explicit"` and pass `uiScale` like every other preset. *(Amended 2026-10-07, D-26.)*

### MUST NOT

- **MUST NOT** hand-roll a `position:fixed` / `document.createElement` overlay for a modal — retire the three that existed (`ActionConfirmationDialog`, `ConversationModal`, `sprk_DocumentOperations.js`).
- **MUST NOT** use CSS `zoom` to scale a modal (it under-scales a portaled `position:fixed` dialog at 4K) — use the scaled theme.
- **MUST NOT** give a surface its own bespoke modal envelope, size, or close-rule — configure `SprkModal` instead.
- **MUST NOT** hardcode a per-entity modal size — OOB record open is 85%×85% for every entity (see the decision-layer standard).
- **MUST NOT** inject CSS into, or DOM-manipulate, platform dialog chrome ([supported customizations](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/supported-customizations)). A request to do so needs its own root CLAUDE.md §6.5 sign-off. *(Amended 2026-10-07, D-26.)*

**Unchanged by the 2026-10-07 amendment**: Layout 1 OOB `entityrecord` dialogs (85% × 85%), the `fullCover` escalation, and the MUST NOT on per-entity sizes. **`RecordNavigationModalShell`** has zero live consumers; it is kept as the dirty-check protocol library behind `BrowseModal.onBeforeNavigate` (not composed as an envelope).

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

"Compose, don't create" (root CLAUDE.md §11): one shell that works exceptionally beats thirteen that partially overlap. **2026-10-07 amendment (path B)**: `WizardShell` had eight consumers and its own envelope (a violation of the bespoke-envelope MUST NOT) while the `WizardModal` preset had none; the white header users saw on wizards is Dataverse `navigateTo` dialog chrome, which cannot be themed, so only an in-app mount gives dark mode. Alternatives rejected: a project-scoped exception (A — the conflict is general, not project-specific) and complying as written (C — building on `WizardModal` would create a second wizard engine). Source: `projects/spaarke-ontology-platform-r1/notes/modal-wizard-canonical-approach.md` §6; spec FR-60; D-26. The scaled-theme (not `zoom`) decision is the only way Fluent v9's fixed-px tokens grow correctly on a portaled dialog at 4K (owner-verified 2026-07-31). A focused, greppable ADR was chosen over extending ADR-021 so the modal contract is self-contained (spec Decisions §11-C).

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
