import { useState, useEffect, useCallback } from 'react';
import {
  getDisplaySizePreference,
  setDisplaySizePreference,
  setupCodePageThemeListener,
  type DisplaySizePreference,
} from '../utils/themeStorage';
import { getEffectiveUiScale, subscribeToViewportBreakpoint } from '../components/SprkModal/uiScale';

export interface IUseUiScaleResult {
  /**
   * The effective `--sprk-ui-scale` factor — `max()` of the Display-size
   * setting's multiplier and the auto ≥2560 breakpoint bump (spec FR-06 /
   * design §6.9). Feed to `scaleTheme(theme, uiScale)` and the
   * `--sprk-ui-scale` CSS variable at the app-shell `FluentProvider`; this is
   * also the SEAM future conversion tasks (P2+) thread as the `uiScale` prop
   * into any `SprkModal` instance the shell renders.
   */
  uiScale: number;
  /** The user's persisted "Display size" setting (Default/Large/Extra-large). */
  displaySize: DisplaySizePreference;
  /**
   * Persist a new Display-size setting. Reuses the EXISTING theme-storage
   * localStorage + `THEME_CHANGE_EVENT` mechanism (via
   * `setDisplaySizePreference`) — no second storage mechanism or listener.
   */
  setDisplaySize: (size: DisplaySizePreference) => void;
}

/**
 * useUiScale — the app-shell hook resolving + exposing the single `uiScale`
 * value (spec FR-06 / design §6.9 / P0.5): the auto ≥2560 CSS px breakpoint
 * combined with the persisted "Display size" setting.
 *
 * Consumed by each app shell's root component (SpaarkeAi `App.tsx`,
 * LegalWorkspace `LegalWorkspaceApp.tsx`) to:
 *   1. feed `scaleTheme(theme, uiScale)` at the `FluentProvider` (Fluent
 *      internals — buttons/inputs/spacing/text — grow in lock-step), and
 *   2. set the `--sprk-ui-scale` CSS variable (our layout / any `SprkModal`
 *      instance the shell renders).
 *
 * Reactivity: recomputes on the SAME `setupCodePageThemeListener` the theme
 * system already uses (Display-size persistence dispatches the SAME
 * `THEME_CHANGE_EVENT` — see `setDisplaySizePreference` — so no second
 * listener is registered here), plus a narrow `matchMedia`-based subscription
 * to the auto breakpoint (a viewport-resize signal, not a storage concern).
 *
 * React-16/17-safe (`useState`/`useEffect`/`useCallback` only — NFR-04); safe
 * to call from both PCF (React 16/17) and Code Page (React 19) consumers,
 * though the app-shell WIRING this task performs is Code-Page-only.
 */
export function useUiScale(): IUseUiScaleResult {
  const [displaySize, setDisplaySizeState] = useState<DisplaySizePreference>(getDisplaySizePreference);
  const [uiScale, setUiScale] = useState<number>(() => getEffectiveUiScale());

  const recompute = useCallback(() => {
    setDisplaySizeState(getDisplaySizePreference());
    setUiScale(getEffectiveUiScale());
  }, []);

  useEffect(() => {
    // Reuses the EXISTING Code-Page theme-storage listener (localStorage +
    // THEME_CHANGE_EVENT) — the SAME listener the theme system uses. This
    // hook's `recompute` ignores the `Theme` argument the listener passes;
    // TypeScript allows a callback with fewer params where more are expected.
    const cleanupThemeListener = setupCodePageThemeListener(recompute);
    // The auto ≥2560 breakpoint needs its own live signal (viewport size is
    // not a storage concern) — matchMedia, not a resize listener/poll.
    const cleanupBreakpoint = subscribeToViewportBreakpoint(recompute);
    return () => {
      cleanupThemeListener();
      cleanupBreakpoint();
    };
  }, [recompute]);

  const setDisplaySize = useCallback((size: DisplaySizePreference) => {
    setDisplaySizePreference(size);
    setDisplaySizeState(size);
    setUiScale(getEffectiveUiScale(size));
  }, []);

  return { uiScale, displaySize, setDisplaySize };
}
