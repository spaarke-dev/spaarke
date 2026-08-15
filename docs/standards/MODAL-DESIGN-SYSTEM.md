# Modal Design System — `SprkModal` Sizes, Chrome, and Wiring

> **Status**: Active (binding)
> **Created**: 2026-08-01 by `spaarke-modal-system` task 010 (FR-10)
> **Audience**: Anyone building or extending a custom Fluent v9 modal in `@spaarke/ui-components` or a consumer
> **Companion**: [`MODAL-DECISION-CRITERIA.md`](MODAL-DECISION-CRITERIA.md) — the **decision layer** ("OOB `navigateTo` vs proprietary Fluent v9 dialog vs browse shell"). **This document is the component layer** — once the decision criteria say "proprietary Fluent v9 dialog," this doc tells you the exact sizes, header, footer, dismiss rules, component names, and wiring. It does not repeat the decision tree; read that doc first if you haven't already decided which modal family you need.

---

## Why this document exists

Before `spaarke-modal-system`, ~13 bespoke dialogs each reinvented chrome: six-plus incompatible "large modal" rectangles, six incompatible header patterns, and three hand-rolled overlays that broke centering and accessibility. `SprkModal` (the base shell) + six presets collapse all of that into one component family with one size scale, one header contract, one footer contract, and one dismiss vocabulary. This document is the canonical reference for that family — the numbers here are prototype-validated and locked by owner UAT (2026-07-31).

**Source of truth**: [`SprkModal/sizes.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/SprkModal/sizes.ts) (the size scale), [`SprkModal.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/SprkModal/SprkModal.tsx) (the shell), [`SprkModal/presets/`](../../src/client/shared/Spaarke.UI.Components/src/components/SprkModal/presets/) (the six presets). If code and this doc ever disagree, the code wins (root `CLAUDE.md` §2) — file a correction.

---

## 1. Size scale

`SprkModal` exposes 7 named sizes via the `size` prop (default `md`). Each size is a `SIZE_SPEC` entry consumed by `getSurfaceStyle(size, uiScale)`.

| Size | Width (clamped) | Height | Layout | Use |
|---|---|---|---|---|
| `xs` | `min(480px, 92vw)` | content height | portrait | confirms · deletes · HITL |
| `sm` | `min(560px, 92vw)` | auto height | portrait | simple form · single choice |
| `md` | `min(1040px, 92vw)` | `min(72vh, 720px)` | landscape | forms · compose · quick-start |
| `lg` | `min(1280px, 94vw)` | `min(85vh, 880px)` | landscape | rich content + sidebar (preview) |
| `xl` | `92vw` (no px cap) | `88vh` (uncapped) | landscape | near-full iframe / app host |
| `full` | `100vw` | `100vh` | landscape | maximized state of any size |
| `wizard` | `62vw` (no px cap) | `min(74vh, 760px)` | landscape | wizard (stepper + content) |

Neither `xs` nor `sm` sets an explicit height in `SIZE_SPEC` — both render at the Dialog's natural content height; "content" vs "auto" describes the same underlying mechanism, not two behaviors.

### The width formula is pre-multiplied px, not `calc(var())`

For sizes with a `cap` (`xs`/`sm`/`md`/`lg`), the computed width is:

```
width = min( round(cap * uiScale) + 'px', N + 'vw' )
```

`uiScale` is the numeric `--sprk-ui-scale` factor (1 = 100%), passed as a prop and pre-multiplied by `getSurfaceStyle` in JS — **not** `calc(var(--sprk-ui-scale) * 1040px)` in CSS. This matters because the same `uiScale` value also drives `scaleTheme` (§6); a CSS-var `calc()` approach would double-apply or drift from the JS-computed Fluent theme tokens. `xl`/`wizard` have no `cap` — their width is pure `vw`, so they grow with the viewport by design (no px ceiling needed to hold their aspect).

Every `DialogSurface` additionally clamps `maxWidth: 96vw` (`100vw` for `full`) and `maxHeight: 92vh` (`100vh` for `full`) — a backstop so no size can overflow the viewport regardless of `uiScale`.

### The `md`/`lg` height caps are load-bearing

`md` and `lg` cap their height in **px**, not just `vh`: `min(72vh, 720px)` and `min(85vh, 880px)`. Without the px cap, `72vh` on a 2560×1440 monitor grows to ~1037px — taller than the 1040px-wide `md` surface is wide, so a *landscape-intended* size reads **square** on a tall/high-res monitor. The cap is what holds the rectangle at every supported resolution (1280×720 floor → 2560×1440 upper reference, per design §6.2). Do not remove it when adjusting either size.

---

## 2. Layouts — portrait vs landscape

Each size carries a `layout: 'portrait' | 'landscape'` (overridable per-instance via the shell's `layout` prop). It is exposed on the body as `data-layout` and is a content/consumer concern, not a shell concern — e.g. `PreviewModal`/`BrowseModal` use it to drive their internal `1fr 320px` stage+meta grid. Portrait sizes (`xs`, `sm`) are for short, single-column content (a message, a form, a choice list). Landscape sizes (`md`, `lg`, `xl`, `full`, `wizard`) are for content that benefits from width — forms with side-by-side fields, preview+metadata, or a wizard's stepper+content grid.

---

## 3. Header contract

The header is `display:flex; justify-content:space-between` with two groups:

- **Left** (`headerLeft`) — optional browse nav (rendered only when the shell's `nav` prop is supplied): a `‹` button, an `aria-live` "`N of M`" counter (tabular-nums), a `›` button — followed by the ellipsized `title` (`white-space:nowrap; overflow:hidden; text-overflow:ellipsis`, always present, `title` attribute for the full string on hover).
- **Right** (`headerRight`) — optional `headerActions` (rendered first) followed by `ModalWindowControls`: a maximize/restore toggle using the Dataverse `FullScreenMaximize20Regular`/`FullScreenMinimize20Regular` glyph (not the four-corner `ArrowMaximize`, so Spaarke modals visually match the OOB dialog's own expand affordance) and a close (`Dismiss20Regular`, `×`). Either control is omitted if its handler prop is absent.

**Single-title-source rule**: the shell owns the header. A preset (e.g. `BrowseModal`) must **never** nest another chrome component that renders its own title/counter (such as `RecordNavigationModalShell`'s own header) inside `SprkModal` — that produces a double header (two titles, two counters). `BrowseModal` demonstrates the correct seam: it forwards the shell's own `nav` prop for the "N of M" chrome, and exposes an `onBeforeNavigate` guard hook so a consumer can still run a cross-frame dirty-check (e.g. delegating to `RecordNavigationModalShell`'s protocol) *without* rendering that shell's nav chrome a second time.

---

## 4. Footer contract

The footer renders **only when `footer || footerStart` is truthy** (`hasFooter` in `SprkModal.tsx`) — a content-only modal (e.g. a pure preview with no primary action) can omit it entirely.

- **`footerStart`** (left slot) — the standard home for **Cancel**. Cancel is **always left**, never mixed into the right-hand action group.
- **`footer`** (right slot) — navigation and primary actions, in reading order (e.g. a wizard's `Skip · Back · Next/Finish`).
- Layout is `justify-content: space-between` when `footerStart` is present, else `flex-end` (a footer with only a primary Close, e.g. `PreviewModal`).
- **Danger primary** styling (a destructive Confirm) is applied via a `makeStyles` **token class** — see `ConfirmModal`'s `styles.danger` (`tokens.colorStatusDangerBackground3` + a `filter: brightness()` hover/active state) — **never** an inline `style={{ color: ... }}` or a hex value (ADR-021 / NFR-03).

---

## 5. Dismiss semantics

The shell's `dismiss` prop (default `light`) gates both the Fluent `Dialog`'s `modalType` and whether backdrop-click/ESC actually close the modal (`onOpenChange` only calls the consumer's `onClose` when `dismiss === 'light'`):

| `dismiss` | `modalType` | Backdrop / ESC | Use |
|---|---|---|---|
| `light` | `modal` | Closes (calls `onClose`) | Previews, browse, low-stakes content — the user can dismiss casually |
| `explicit` | `modal` | **No** light-dismiss — only the `×` or an explicit footer action closes | Forms, wizards, choice pickers — an accidental ESC shouldn't discard in-progress input |
| `alert` | `alert` | **No** ESC/backdrop (Fluent's own alert semantics) | Destructive confirms and blocking dialogs — the user must make an explicit choice |

`ConfirmModal` and `ChoiceModal` both use `xs` + non-`light` dismiss (`alert` and `explicit` respectively) so a Confirm/Cancel or a deliberate choice is always required — see each preset's own doc comment for the reasoning.

---

## 6. Theming

- **No `FluentProvider` inside the shell.** `SprkModal` renders `Dialog`/`DialogSurface` directly and inherits whatever `FluentProvider` theme the host installs — this is what lets the same shell run correctly in a PCF control, a Code Page, or an Office Add-in without forcing a theme choice.
- **Scale is realized by a scaled Fluent *theme*, not CSS `zoom`.** The host multiplies the `--sprk-ui-scale` factor into a cloned Fluent `Theme` via `scaleTheme(base, scale)` ([`scaledTheme.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/SprkModal/scaledTheme.ts)): every px-valued token in the `fontSize`/`lineHeight`/`spacing`/`strokeWidth`/`borderRadius` families is multiplied and rounded; colors and all other tokens are untouched. This is what grows Fluent's *own* internals (button padding, input height, text) at 2K/4K — CSS `zoom` was rejected because it under-scales a portaled `position:fixed` dialog at high DPI. The **same** `uiScale` value must be passed to both `scaleTheme` and the shell's `uiScale` prop so layout and Fluent internals grow together.
- **Semantic tokens only.** Zero hex values, zero `'1px'` literals (use `tokens.strokeWidthThin`), zero inline color styles anywhere in `SprkModal` or its presets (ADR-021, strengthened by this project's NFR-03).
- **Light/dark parity** — every rule above applies unchanged under `webDarkTheme`; nothing in the shell hard-codes a light-mode assumption.
- **Transform-robust centering** — the Fluent `Dialog` portal mounts above a CSS-transformed ancestor, so the modal centers correctly even when hosted inside a scaled/transformed container. This is the invariant the whole system rests on; do not replace the `Dialog`/`DialogSurface` envelope with a hand-rolled `position:fixed` overlay (that is the exact anti-pattern this project retires — three prior overlays did this and broke centering).

### The canonical thin scrollbar (`thinScrollbarStyle`)

The "thin, light-gray, theme-aware" scrollbar is **one shared mixin** — `thinScrollbarStyle` ([`theme/scrollbar.ts`](../../src/client/shared/Spaarke.UI.Components/src/theme/scrollbar.ts)), exported from `@spaarke/ui-components`. It is the single source of truth for that bar across the app (email reading pane, conversation lists, data grids, chat input). Semantic tokens only (`colorNeutralStroke1` / `colorNeutralStroke1Hover` thumb, transparent track), so it resolves correctly under light **and** dark themes; `scrollbarWidth`/`scrollbarColor` cover Firefox and the `::-webkit-scrollbar*` pseudo-elements cover Chromium/Edge/Safari.

- **Inside a modal you get it for free** — `SprkModal`'s default `bodyScroll="native"` already applies the thin scrollbar to the body. Do nothing (`AccessGrantModal` is the reference consumer). `bodyScroll="arrows"` (the `ModalScrollArea` chevron pager) is the opt-in exception, not the default.
- **Any scroll region outside a modal** (a widget list, a side pane, a custom body) — import the mixin and spread it into the scrollable `makeStyles` slot rather than re-declaring the rules:
  ```ts
  import { thinScrollbarStyle } from '@spaarke/ui-components';
  const useStyles = makeStyles({ scrollArea: { overflowY: 'auto', ...thinScrollbarStyle } });
  ```
- **Do not** hand-roll `scrollbarWidth` / `::-webkit-scrollbar` fragments inline — the per-copy drift (different widths, radii, stroke tokens) is exactly what this mixin retired. New call sites spread `thinScrollbarStyle`.

---

## 7. Component names

One base shell + six presets, all importable from `@spaarke/ui-components`:

```ts
import {
  SprkModal,        // base shell — consumer supplies content + intent, shell supplies chrome
  ConfirmModal,      // xs, alert/explicit dismiss, Cancel + Confirm (optional destructive)
  ChoiceModal,        // xs, explicit dismiss, 2-4 rich choices (ADR-023 preserved)
  FormModal,          // sm|md, explicit dismiss, Cancel + Save
  PreviewModal,       // lg landscape, light dismiss, stage+meta grid, single Close
  BrowseModal,        // PreviewModal + the shell's `nav` prop (browse "N of M")
  WizardModal,        // wizard size, explicit dismiss, stepper sidebar + Cancel/Skip/Back/Next
} from '@spaarke/ui-components';
```

Each preset is a **thin config** of `SprkModal` — it owns no `Dialog`/header/footer of its own; it only supplies the size, dismiss mode, and footer slot contents that make sense for its intent. `ChoiceModal` is the one preset **not** ported from the prototype — it was built fresh in this project to re-base `ChoiceDialog`'s existing selection model (the choice-dialog-pattern, ADR-023) onto the canonical shell without forking it.

---

## 8. Wiring recipe

The pattern is always the same: **the consumer supplies content + intent (title, fields, callbacks); the shell supplies all chrome.**

### `FormModal` — a light-edit form

```tsx
<FormModal
  open={isOpen}
  onClose={() => setIsOpen(false)}
  onSubmit={handleSave}
  title="Edit contact details"
  size="md"              // 'sm' | 'md' — default 'md'
  submitLabel="Save"
>
  <Field label="Email"><Input value={email} onChange={onEmailChange} /></Field>
  <Field label="Phone"><Input value={phone} onChange={onPhoneChange} /></Field>
</FormModal>
```

`FormModal` wires `dismiss="explicit"` (an accidental ESC won't discard the form), renders Cancel in `footerStart` bound to `onClose`, and Save in `footer` bound to `onSubmit`. The consumer never touches `Dialog`, the header, or the footer directly — only the fields.

### `BrowseModal` — browse a collection with a nav header

```tsx
<BrowseModal
  open={isOpen}
  onClose={() => setIsOpen(false)}
  title={documents[currentIndex].name}
  metadata={[
    { label: 'Modified', value: formatters.date(documents[currentIndex].modifiedOn) },
    { label: 'Size', value: formatters.fileSize(documents[currentIndex].size) },
  ]}
  nav={{
    index: currentIndex,
    total: documents.length,
    onNavigate: (dir) => setCurrentIndex(dir === 'next' ? currentIndex + 1 : currentIndex - 1),
  }}
  onBeforeNavigate={async (dir) => {
    // optional: run a cross-frame dirty-check / discard-confirm before navigating
    return true;
  }}
>
  <RichFilePreview document={documents[currentIndex]} />
</BrowseModal>
```

`BrowseModal` is fixed at `lg`/`landscape`/unpadded; it forwards `nav` straight to the shell's own header nav group (§3) — it does **not** render a second navigator. `onBeforeNavigate` is the seam for wiring in a dirty-check protocol (e.g. `RecordNavigationModalShell`'s) without nesting that shell's chrome.

---

## 9. Do / Don't

| ✅ Do | ❌ Don't |
|---|---|
| Compose one of the six presets — they cover confirm, choice, form, preview, browse, and wizard intents | Hand-roll a new `<Dialog>` for an intent one of the six presets already covers |
| Put Cancel in `footerStart` (left), every other action in `footer` (right) | Mix Cancel into the right-hand action group, or omit it from a dismissible form |
| Apply danger/destructive styling via a `makeStyles` token class | Apply destructive styling via an inline `style={{ backgroundColor: '#...' }}` |
| Keep the Fluent `Dialog`/`DialogSurface` envelope — it is transform-robust | Replace the envelope with a hand-rolled `position:fixed`/`createElement` overlay |
| Let the body scroll natively (`bodyScroll="native"`, the default — thin scrollbar) | Add a chevron/arrow scroll overlay for ordinary vertical content (`bodyScroll="arrows"` is opt-in for specific cases only) |
| Use semantic tokens (`tokens.*`) exclusively | Use hex colors, `'1px'` string literals, or inline color styles |
| Pass the *same* `uiScale` value to the shell and to `scaleTheme` | Scale layout with CSS `zoom`, or scale the shell without also scaling the host's Fluent theme |
| Forward `nav`/`onBeforeNavigate` for browse UX | Nest `RecordNavigationModalShell`'s own header chrome inside `SprkModal` (double header) |

---

## Cross-links

- **Decision layer (which modal family)** — [`MODAL-DECISION-CRITERIA.md`](MODAL-DECISION-CRITERIA.md) — read this first if you haven't decided between OOB `navigateTo`, a proprietary Fluent v9 dialog, and the browse shell.
- **Component source** — [`SprkModal/`](../../src/client/shared/Spaarke.UI.Components/src/components/SprkModal/) (shell, sizes, scaled theme, presets)
- **Window controls (shared primitive)** — [`ModalWindowControls.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/ModalWindowControls/ModalWindowControls.tsx)
- **Fluent v9 constraint** — [`ADR-021`](../../.claude/adr/ADR-021-fluent-design-system.md) (semantic tokens only; strengthened by this project's NFR-03)
- **Choice-dialog pattern** — [`ADR-023`](../../.claude/adr/ADR-023-choice-dialog-pattern.md) (preserved via `ChoiceModal`)
- **Shared-lib boundary** — [`ADR-012`](../../.claude/adr/ADR-012-shared-components.md) (context-agnostic components live in `@spaarke/ui-components`)
- **Auth in component props** — [`ADR-028`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) (pass `authenticatedFetch` as a function dependency; never snapshot tokens in modal props)
- **Constitutional ADR for this system** — "ADR-050: Canonical Modal Shell" (published by a subsequent task in this project; will formalize the rules summarized above)
- **Anti-patterns catalog** — [`ANTI-PATTERNS.md`](ANTI-PATTERNS.md)

---

*Maintained by the project owner. Updates that change a size number, the header/footer contract, or add a new preset MUST add a row to [`.claude/CHANGELOG.md`](../../.claude/CHANGELOG.md).*
