# Thin Theme-Aware Scrollbar (the Spaarke "modern grey scrollbar")

> **Last Reviewed**: 2026-08-18
> **Status**: Current
> **Recurs often** — this is the canonical answer to "how do I get the thin grey scrollbar we use in the widgets / grids?"

## When

Any Spaarke surface where the browser's default (chunky, opaque) scrollbar looks wrong next to our Fluent v9 UI — dataset grids (Documents), the Email/Code Pages, widget scroll areas, modal scroll bodies, and OOB Dataverse form dialogs. Use this instead of re-inventing `::-webkit-scrollbar` rules inline (which drift).

## The one source of truth

**`src/client/shared/Spaarke.UI.Components/src/theme/scrollbar.ts`** — exported from `@spaarke/ui-components`:

| Export | Styles | Use when |
|---|---|---|
| `thinScrollbarStyle` | the element it is spread into | you have ONE known scroll container |
| `thinScrollbarDescendantStyle` | every scrollable descendant (`& *`) | you want blanket coverage from a surface / Code Page ROOT |

Both are Griffel `GriffelStyle` objects — spread into a `makeStyles` slot:

```ts
import { makeStyles } from '@fluentui/react-components';
import { thinScrollbarStyle, thinScrollbarDescendantStyle } from '@spaarke/ui-components';

const useStyles = makeStyles({
  // single scroller:
  scrollArea: { overflowY: 'auto', ...thinScrollbarStyle },
  // whole surface (Code Page root, dialog root):
  page: { height: '100%', overflow: 'hidden', ...thinScrollbarDescendantStyle },
});
```

The look: **8px** wide, **transparent** track, **`colorNeutralStroke1`** thumb (4px / `borderRadiusMedium`), `Stroke1Hover` on hover. Firefox uses `scrollbar-width:thin` + `scrollbar-color`; Chromium/Edge/Safari use the `::-webkit-scrollbar*` pseudo-elements.

## Why tokens, not hex (theme-awareness)

The thumb color is the semantic token `colorNeutralStroke1`, so it resolves to a light-gray thumb in light theme and a lighter-on-dark thumb in dark theme **automatically** via the active Fluent theme — no light/dark branching, no hardcoded hex (ADR-021). **This matters**: Code Pages like SmartTodo are dark-mode-capable (`resolveCodePageTheme`), so a hardcoded grey would look wrong in dark mode. Always use the token-based style, never a raw hex scrollbar.

## The gotcha: `::-webkit-scrollbar` does NOT cascade

Webkit scrollbar pseudo-elements style **only the element they are declared on** — they do not inherit to descendants. So `thinScrollbarStyle` on a wrapper does nothing for a nested scroller inside it. Two fixes:

1. Spread `thinScrollbarStyle` onto **each** actual `overflow:auto/scroll` element, OR
2. Spread `thinScrollbarDescendantStyle` (the `& *` variant) once at a **root** — covers every nested scroller (kanban columns, grids, dialogs), including ones added later. This is what the SmartTodo Code Page does (`SmartTodoApp.tsx` `page` slot).

## "What sets the length of the scroll indicator (thumb)?"

**The browser does — it is not settable via CSS for native scrollbars.** The thumb length is the ratio of the visible viewport to the total scrollable content: more content → shorter thumb; content barely overflowing → long thumb. `::-webkit-scrollbar-*` only controls **width/height, color, radius, track** — never thumb length. If you truly need a fixed-size or custom-length indicator you must replace the native scrollbar with a JS overlay scrollbar library (we do **not** — native + thin styling is the standard). So a "too-short" thumb is a signal the container has a lot of overflow, not a style bug.

## Non-React surfaces (OOB Dataverse form dialogs, raw index.html)

Where there is no Griffel (`makeStyles`) — e.g. an OOB `sprk_todo` form opened as a `navigateTo` modal, or a Code Page `index.html` global — inject the equivalent **raw CSS**. Mirror the same numbers so the look matches:

```css
*::-webkit-scrollbar { width: 8px; height: 8px; }
*::-webkit-scrollbar-track { background: transparent; }
*::-webkit-scrollbar-thumb { background: #c7c7c7; border-radius: 4px; }         /* light theme */
*::-webkit-scrollbar-thumb:hover { background: #b0b0b0; }
/* dark theme: swap the thumb to a lighter-on-dark grey, e.g. #4a4a4a / #5a5a5a,
   gated on the surface theme — see oob-form-dialog-chrome.md for the injection
   mechanism (form OnLoad, cross-frame, theme-gated). */
```

For the OOB form-dialog case, inject this via the form OnLoad script per [`oob-form-dialog-chrome.md`](oob-form-dialog-chrome.md) — same cross-frame + theme-gating rules apply.

## Do NOT

- **Re-declare `::-webkit-scrollbar` inline** in a new `makeStyles` — spread the canonical object (there is already drift: `DataGrid.tsx` still has an inline copy using `colorNeutralStroke2`; new code must not add more copies).
- **Hardcode a hex thumb** on a theme-aware surface — it breaks in dark mode. Use the token-based style.
- **Spread `thinScrollbarStyle` on a wrapper and expect nested scrollers to thin** — they won't (no cascade); use the descendant variant or annotate the real scroller.
- **Try to set the thumb length** — it is native/proportional; not a CSS knob.

## Live examples

- Canonical: `src/client/shared/Spaarke.UI.Components/src/theme/scrollbar.ts`
- Code Page root (descendant variant): `src/solutions/SmartTodo/src/SmartTodoApp.tsx` (`page` slot)
- Single-scroller consumers: `DataGrid.tsx` (inline — legacy drift), `WorkspaceShell.styles.ts`, `SprkModal`/`ModalScrollArea.tsx`, `ConversationWorkspace` ThreadList

## Related

- [`oob-form-dialog-chrome.md`](oob-form-dialog-chrome.md) — injecting CSS (incl. this scrollbar) into an OOB form dialog, cross-frame + theme-gated
- [`fluent-v9-theming.md`](fluent-v9-theming.md) — semantic tokens + dark-mode theme resolution
- [`modal-shell.md`](modal-shell.md) — `SprkModal`/`ModalScrollArea` (a `thinScrollbarStyle` consumer)
