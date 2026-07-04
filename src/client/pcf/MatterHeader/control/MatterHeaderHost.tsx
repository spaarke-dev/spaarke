/**
 * MatterHeaderHost — top-level React wrapper for the virtual PCF.
 *
 * Mirrors VisualHost's `VisualHostHost.tsx` pattern (verified working in
 * production 2026-05). Owns the concerns the PCF class no longer touches:
 *   - Wraps `<MatterHeaderView>` in a `<FluentProvider>` so Fluent v9 CSS
 *     variables (`--colorNeutralBackground1`, `--shadow16`, …) are injected
 *     into portal-rendered surfaces (`Popover`, `Tooltip`, `Menu`, `Dialog`).
 *   - Resolves the current theme (light / dark / high-contrast) via the
 *     shared `themeStorage` utility (v1.0.12 — DEF-09).
 *
 * Why the wrapper is required despite `control-type="virtual"`:
 *   Platform-library Fluent theming is applied on the PCF's own root DOM
 *   element. Portal-rendered surfaces mount to `document.body` OUTSIDE the
 *   PCF root, so CSS variables defined only on the PCF root do NOT reach
 *   them. Our own `<FluentProvider>` fixes this because Fluent v9's
 *   `applyStylesToPortals` default (true) explicitly injects theme vars
 *   into portal subtrees.
 *
 * Theme sources (v1.0.12 — DEF-09; delegates to
 * `@spaarke/ui-components/utils/themeStorage`):
 *   1. `localStorage['spaarke-theme']` — user's explicit preference
 *   2. `context.fluentDesignLanguage.isDarkTheme` — Power Apps native signal
 *   3. DOM navbar detection fallback
 *   4. High-contrast (`(forced-colors: active)` media query) takes precedence
 *   Listener: `setupThemeListener` re-resolves on `localStorage` changes and
 *   the custom `spaarke-theme-change` event, plus a local `matchMedia` listener
 *   for high-contrast transitions.
 *
 * @see docs/patterns/pcf/pcf-build-scaffold.md §6 — Portal-vars gotcha
 * @see src/client/pcf/VisualHost/control/VisualHostHost.tsx — canonical reference
 */

import * as React from 'react';
import { FluentProvider, teamsHighContrastTheme, type Theme } from '@fluentui/react-components';
import { resolveThemeWithUserPreference, setupThemeListener } from '@spaarke/ui-components/dist/utils/themeStorage';
import { MatterHeaderView } from './MatterHeaderView';

export interface IMatterHeaderHostProps {
  recordId: string;
  title?: string;
  showVersion?: boolean;
  /**
   * Optional PCF context so the theme resolver can read
   * `context.fluentDesignLanguage.isDarkTheme` (Power Apps native signal).
   * When omitted, resolution falls back to localStorage + navbar DOM
   * detection + high-contrast media query — still functional.
   */
  context?: unknown;
}

/**
 * High-contrast detection — mirrors VisualHost's ThemeProvider.
 * The shared `themeStorage.resolveThemeWithUserPreference` doesn't cover
 * high-contrast; we OR it in here so `teamsHighContrastTheme` takes
 * precedence when the OS forced-colors mode is active.
 */
function isHighContrast(): boolean {
  if (typeof window === 'undefined' || !window.matchMedia) return false;
  try {
    if (window.matchMedia('(forced-colors: active)').matches) return true;
    if (window.matchMedia('(-ms-high-contrast: active)').matches) return true;
  } catch {
    // matchMedia unavailable
  }
  if (typeof document !== 'undefined' && document.body) {
    if (document.body.classList.contains('high-contrast')) return true;
    if (document.body.classList.contains('ms-highContrast')) return true;
  }
  return false;
}

function resolveTheme(context?: unknown): Theme {
  if (isHighContrast()) return teamsHighContrastTheme;
  return resolveThemeWithUserPreference(context);
}

export const MatterHeaderHost: React.FC<IMatterHeaderHostProps> = ({ recordId, title, showVersion, context }) => {
  const [theme, setTheme] = React.useState<Theme>(() => resolveTheme(context));

  React.useEffect(() => {
    // Shared listener covers localStorage + custom event + Power Apps context.
    const cleanupShared = setupThemeListener(() => setTheme(resolveTheme(context)), context);

    // High-contrast media-query listener (not covered by shared listener).
    let hcQuery: MediaQueryList | null = null;
    const handleHc = (): void => setTheme(resolveTheme(context));
    try {
      hcQuery = window.matchMedia('(forced-colors: active)');
      hcQuery.addEventListener('change', handleHc);
    } catch {
      // matchMedia unavailable
    }

    return () => {
      cleanupShared();
      if (hcQuery) hcQuery.removeEventListener('change', handleHc);
    };
  }, [context]);

  return (
    <FluentProvider theme={theme} style={{ width: '100%' }}>
      <MatterHeaderView recordId={recordId} title={title} showVersion={showVersion} />
    </FluentProvider>
  );
};
