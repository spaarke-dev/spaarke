import {
  getDisplaySizePreference,
  setDisplaySizePreference,
  type DisplaySizePreference,
} from '../../utils/themeStorage';

export { getDisplaySizePreference, setDisplaySizePreference };
export type { DisplaySizePreference };

/**
 * uiScale — the app-shell UI-scale derivation (spec FR-06 / design §6.9 / P0.5).
 *
 * Combines:
 *  (a) an auto large-viewport breakpoint (≥2560 CSS px → 1.15, else 1.0), and
 *  (b) a user "Display size: Default / Large / Extra-large" setting persisted
 *      via the EXISTING code-page theme-storage pattern (`themeStorage.ts`),
 * into ONE effective `uiScale` value fed to `scaleTheme(uiScale)` (this
 * folder's `scaledTheme.ts`) and set as the `--sprk-ui-scale` CSS variable at
 * each app-shell `FluentProvider` (App.tsx / LegalWorkspaceApp.tsx). `SprkModal`
 * itself only INHERITS `uiScale` as a prop (see `sizes.ts`) — this module is
 * the app-shell side of the mechanism, not a modal-shell concern.
 *
 * Precedence: `max(settingMultiplier, breakpointMultiplier)`. The auto
 * breakpoint BUMPS a Default (1.0) setting up to 1.15 on a ≥2560 viewport
 * (design §6.9: "the auto breakpoint sets uiScale to 1.15 without a user
 * setting change"); an explicit Large (1.25) / Extra-large (1.5) setting is
 * already larger than the breakpoint bump, so it always wins — `max()` avoids
 * a separate combination rule while keeping the output confined to the four
 * values FR-06 acceptance actually tests (1.0 / 1.15 / 1.25 / 1.5). Literal
 * multiplication (Large × breakpoint = 1.4375) would produce an untested,
 * arbitrary value and was rejected.
 *
 * Storage: reuses `themeStorage.ts`'s localStorage + `THEME_CHANGE_EVENT` +
 * `setupCodePageThemeListener` mechanism (same key family, same listener) —
 * per the binding design constraint, no second storage mechanism or listener
 * is introduced here.
 */

/** Auto large-viewport breakpoint (design §6.9 / FR-06): ≥2560 CSS px → bump. */
export const UI_SCALE_BREAKPOINT_PX = 2560;

/** The `uiScale` the auto breakpoint bumps to when the viewport is ≥2560 CSS px. */
export const UI_SCALE_BREAKPOINT_MULTIPLIER = 1.15;

/**
 * Display-size → fixed `uiScale` multiplier map. Only `default` (1.0) and the
 * breakpoint (1.15) are named exact values in spec FR-06 / design §6.9; the
 * design explicitly leaves Large/Extra-large open ("fixed multipliers you
 * choose"). Chose 1.25 / 1.5 — the SAME two values already named and tested
 * as first-class `uiScale` points throughout the task-002 scale machinery
 * (`sizes.test.ts`, `scaledTheme.test.ts`) and the FR-06 acceptance text
 * itself ("at uiScale 1.0/1.25/1.5 the whole modal... scales... with no
 * clipping") — so Large/Extra-large land exactly on already-verified scale
 * points rather than introducing new, unverified ones.
 */
export const DISPLAY_SIZE_MULTIPLIERS: Record<DisplaySizePreference, number> = {
  default: 1,
  large: 1.25,
  'extra-large': 1.5,
};

/**
 * Detect the auto large-viewport breakpoint from the CURRENT viewport
 * (`window.innerWidth`, CSS px — matches the `min-width` semantics used by
 * `subscribeToViewportBreakpoint`'s matchMedia query). SSR/non-browser safe
 * (returns `false`).
 */
export function isLargeViewport(): boolean {
  if (typeof window === 'undefined' || typeof window.innerWidth !== 'number') {
    return false;
  }
  return window.innerWidth >= UI_SCALE_BREAKPOINT_PX;
}

/**
 * Resolve the single effective `uiScale` from the user's persisted
 * Display-size setting and the auto ≥2560 breakpoint (design §6.9 / FR-06).
 *
 * @param displaySize - Optional override (tests / already-resolved callers).
 *   Defaults to reading the persisted preference via `getDisplaySizePreference()`.
 */
export function getEffectiveUiScale(displaySize?: DisplaySizePreference): number {
  const setting = displaySize ?? getDisplaySizePreference();
  const settingScale = DISPLAY_SIZE_MULTIPLIERS[setting];
  const breakpointScale = isLargeViewport() ? UI_SCALE_BREAKPOINT_MULTIPLIER : 1;
  return Math.max(settingScale, breakpointScale);
}

/**
 * Subscribe to LIVE changes in the auto large-viewport breakpoint via
 * `matchMedia` (avoids resize-listener churn; a `storage`/`THEME_CHANGE_EVENT`
 * listener does not cover viewport resizes, so this is a SEPARATE, narrowly-
 * scoped signal — not a duplicate of the theme-storage listener, which only
 * covers the Display-size SETTING half of `uiScale`).
 *
 * Defensive: no-ops (returns a no-op cleanup) when `matchMedia` is
 * unavailable or throws (older jsdom test environments, sandboxed iframes,
 * very old browsers) — the Display-size setting still drives `uiScale`
 * correctly; only the live re-evaluation on resize is skipped (a reload
 * still picks up the breakpoint via `isLargeViewport()` at next mount).
 *
 * @returns Cleanup function to remove the listener.
 */
export function subscribeToViewportBreakpoint(onChange: () => void): () => void {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return () => {};
  }
  try {
    const mql = window.matchMedia(`(min-width: ${UI_SCALE_BREAKPOINT_PX}px)`);
    const handler = () => onChange();

    if (typeof mql.addEventListener === 'function') {
      mql.addEventListener('change', handler);
      return () => {
        try {
          mql.removeEventListener('change', handler);
        } catch {
          // no-op — best-effort cleanup
        }
      };
    }

    // Legacy fallback (Safari <14): addListener/removeListener.
    const legacyMql = mql as unknown as {
      addListener?: (h: () => void) => void;
      removeListener?: (h: () => void) => void;
    };
    if (typeof legacyMql.addListener === 'function') {
      legacyMql.addListener(handler);
      return () => {
        try {
          legacyMql.removeListener?.(handler);
        } catch {
          // no-op — best-effort cleanup
        }
      };
    }

    return () => {};
  } catch {
    return () => {};
  }
}
