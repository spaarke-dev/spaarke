/**
 * RecordHeaderHost — top-level React wrapper for the virtual PCF.
 *
 * Mirrors `MatterHeaderHost.tsx` (itself modelled on VisualHosts
 * `VisualHostHost.tsx`, verified in production 2026-05). Owns the concerns the
 * PCF class no longer touches:
 *   - Wraps `<RecordHeaderView>` in a `<FluentProvider>` so Fluent v9 CSS
 *     variables (`--colorNeutralBackground1`, `--shadow16`, …) are injected
 *     into portal-rendered surfaces (`Popover`, `Tooltip`, `Menu`, `Dialog`,
 *     and the option-set `Dropdown` listbox).
 *   - Resolves the current theme (light / dark / high-contrast) via the
 *     shared `themeStorage` utility.
 *
 * Why the wrapper is required despite `control-type="virtual"`:
 *   Platform-library Fluent theming is applied on the PCFs own root DOM
 *   element. Portal-rendered surfaces mount to `document.body` OUTSIDE the
 *   PCF root, so CSS variables defined only on the PCF root do NOT reach
 *   them. Our own `<FluentProvider>` fixes this because Fluent v9s
 *   `applyStylesToPortals` default (true) explicitly injects theme vars into
 *   portal subtrees. This control renders MORE portal surfaces than
 *   MatterHeader did (date picker, option-set dropdown), so the wrap matters
 *   more here, not less.
 *
 * Theme sources (delegates to `@spaarke/ui-components/dist/utils/themeStorage`):
 *   1. `localStorage['spaarke-theme']` — user explicit preference
 *   2. `context.fluentDesignLanguage.isDarkTheme` — Power Apps native signal
 *   3. DOM navbar detection fallback
 *   4. High-contrast (`(forced-colors: active)` media query) takes precedence
 *
 * @see .claude/patterns/pcf/pcf-build-scaffold.md gotcha 6 — portal-vars
 * @see src/client/pcf/MatterHeader/control/MatterHeaderHost.tsx — the mirror source
 */

import * as React from 'react';
import { FluentProvider, teamsHighContrastTheme, type Theme } from '@fluentui/react-components';
import { resolveThemeWithUserPreference, setupThemeListener } from '@spaarke/ui-components/dist/utils/themeStorage';
import { RecordHeaderView } from './RecordHeaderView';

export interface IRecordHeaderHostProps {
  /** Entity logical name, self-detected by the control class (FR-12). */
  entityName: string;
  /** Record GUID (no braces). Empty string = "no record selected". */
  recordId: string;
  /** Optional inline toolbar title override from the manifest. */
  title?: string;
  /** When `true` (default), the version footer is rendered. */
  showVersion?: boolean;
  /** RAW `layoutJson` manifest value. Parsed by the shared resolver, not here. */
  layoutJson: string | null;
  /**
   * Optional PCF context so the theme resolver can read
   * `context.fluentDesignLanguage.isDarkTheme` (Power Apps native signal).
   * When omitted, resolution falls back to localStorage + navbar DOM detection
   * + the high-contrast media query — still functional.
   */
  context?: unknown;
}

/**
 * High-contrast detection — mirrors VisualHosts ThemeProvider.
 * The shared `themeStorage.resolveThemeWithUserPreference` does not cover
 * high-contrast; we OR it in here so `teamsHighContrastTheme` takes precedence
 * when the OS forced-colors mode is active (NFR-03).
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

export const RecordHeaderHost: React.FC<IRecordHeaderHostProps> = ({
  entityName,
  recordId,
  title,
  showVersion,
  layoutJson,
  context,
}) => {
  const [theme, setTheme] = React.useState<Theme>(() => resolveTheme(context));

  React.useEffect(() => {
    // Shared listener covers localStorage + custom event + Power Apps context.
    const cleanupShared = setupThemeListener(() => setTheme(resolveTheme(context)), context);

    // High-contrast media-query listener (not covered by the shared listener).
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
      {/*
        Keyed on recordId: the 022 hook clears its pending buffers immediately
        on a recordId change but `values` lags one frame until the refetch
        resolves, so display would briefly resolve against the PREVIOUS record.
        Remounting the subtree makes that structurally impossible rather than
        relying on the loading mask alone.
      */}
      <RecordHeaderView
        key={recordId}
        entityName={entityName}
        recordId={recordId}
        title={title}
        showVersion={showVersion}
        layoutJson={layoutJson}
      />
    </FluentProvider>
  );
};
