/**
 * Theme Provider for Semantic Search Code Page
 *
 * Re-exports shared theme utilities from @spaarke/ui-components (themeStorage)
 * with local aliases matching the original API surface used by index.tsx.
 *
 * Theme priority (per ADR-021):
 * 1. localStorage ('spaarke-theme') user preference
 * 2. URL flags parameter (themeOption=dark|light)
 * 3. DOM navbar color fallback (when embedded in Dataverse)
 *
 * @see ADR-021 - Fluent UI v9 Design System (dark mode required)
 * @see ADR-012 - Shared component library
 */

// Deep import per ADR-012 — avoids the barrel which transitively pulls in
// RichTextEditor → @lexical/react .prod.mjs (broken ESM resolution in webpack).
// Webpack alias resolves @spaarke/ui-components → Spaarke.UI.Components/src
// so the path below resolves to .../src/utils/themeStorage.
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
  getEffectiveDarkMode,
} from "@spaarke/ui-components/utils/themeStorage";
import type { Theme } from "@fluentui/react-components";

/**
 * Detect the current theme using the shared resolution chain.
 *
 * @param _params - Ignored (kept for API compatibility). The shared
 *   resolveCodePageTheme reads from localStorage / URL / DOM directly.
 * @returns Fluent UI v9 Theme object
 */
export function detectTheme(_params?: URLSearchParams): Theme {
  return resolveCodePageTheme();
}

/**
 * Check whether the detected theme is dark.
 *
 * @param _params - Ignored (kept for API compatibility).
 * @returns true if the active theme is dark
 */
export function isDarkTheme(_params?: URLSearchParams): boolean {
  return getEffectiveDarkMode();
}

/**
 * Set up listener for theme changes (localStorage and custom events).
 *
 * @param callback - Invoked when the theme may have changed
 * @returns Cleanup function to remove listeners
 */
export function setupThemeListener(callback: () => void): () => void {
  return setupCodePageThemeListener(() => callback());
}
