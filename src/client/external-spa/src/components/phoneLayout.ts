/**
 * Phone layout (R4 task 048, FR-32, #2466): the ONE breakpoint the SPA uses for a phone (the Teams tab on a phone, or a
 * browser this narrow), and the touch-target rule that goes with it.
 *
 * Width-driven, not Teams-only (NFR-03): a browser at 480px or less gets the same one-column layout as the Teams client.
 * Wider layouts (1000px and up) are untouched; every rule here sits inside {@link PHONE_MEDIA}.
 *
 * - {@link PHONE_MEDIA}: the `makeStyles` key for phone-only rules (`[PHONE_MEDIA]: { ... }`).
 * - {@link useIsPhoneWidth}: the same breakpoint for decisions CSS cannot make (which record actions go to the
 *   RecordCommandBar overflow).
 * - {@link usePhoneTouchTargetStyles}: at phone width, every Fluent button, menu item, tab, input and select in the SPA,
 *   portalled dialogs and menus included, is at least 44 x 44px. It selects Fluent's exported class names, so it reaches
 *   the shared components (SprkModal's header, HeaderToolbar, RecordCommandBar, DataGrid) without changing their APIs.
 *   Sizes only: no colour, so every theme is unaffected (ADR-021).
 */
import * as React from 'react';
import {
  buttonClassNames,
  inputClassNames,
  makeStaticStyles,
  menuItemClassNames,
  selectClassNames,
  tabClassNames,
  tableRowClassNames,
  toggleButtonClassNames,
} from '@fluentui/react-components';

/** A viewport this wide or narrower is a phone. */
export const PHONE_MAX_WIDTH_PX = 480;

/** The media query for a phone, for `window.matchMedia`. */
export const PHONE_MEDIA_QUERY = `(max-width: ${PHONE_MAX_WIDTH_PX}px)`;

/** The `makeStyles` key for phone-only rules. */
export const PHONE_MEDIA = `@media ${PHONE_MEDIA_QUERY}`;

/** The minimum touch target at phone width (WCAG 2.5.5, Apple HIG). */
export const TOUCH_TARGET_PX = 44;

/** {@link TOUCH_TARGET_PX} as a CSS length. */
export const TOUCH_TARGET = `${TOUCH_TARGET_PX}px`;

function phoneMediaList(): MediaQueryList | null {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return null;
  return window.matchMedia(PHONE_MEDIA_QUERY);
}

/** True while the viewport is at most {@link PHONE_MAX_WIDTH_PX} wide; follows resizes and rotation. */
export function useIsPhoneWidth(): boolean {
  const [isPhone, setIsPhone] = React.useState<boolean>(() => phoneMediaList()?.matches === true);
  React.useEffect(() => {
    const list = phoneMediaList();
    if (!list) return undefined;
    const update = (): void => setIsPhone(list.matches);
    update();
    list.addEventListener('change', update);
    return () => list.removeEventListener('change', update);
  }, []);
  return isPhone;
}

// Each selector repeats its class so it outranks Fluent's own single-class size rules wherever the stylesheets land.
// `min-width` wins over Fluent's `max-width` on icon-only buttons, so their width reaches 44px too.
const twice = (className: string): string => `.${className}.${className}`;
const TOUCH_TARGET_CSS = `@media ${PHONE_MEDIA_QUERY} {
  ${twice(buttonClassNames.root)}, ${twice(toggleButtonClassNames.root)} {
    min-height: ${TOUCH_TARGET};
    min-width: ${TOUCH_TARGET};
  }
  ${twice(menuItemClassNames.root)}, ${twice(tabClassNames.root)}, ${twice(tableRowClassNames.root)},
  ${twice(inputClassNames.root)}, ${twice(selectClassNames.select)} {
    min-height: ${TOUCH_TARGET};
  }
}`;

/** Mount once at the app root (`App`): applies the phone touch-target rule to the whole SPA. */
export const usePhoneTouchTargetStyles = makeStaticStyles(TOUCH_TARGET_CSS);

/** The CSS {@link usePhoneTouchTargetStyles} applies, exported for its test. */
export const PHONE_TOUCH_TARGET_CSS = TOUCH_TARGET_CSS;
