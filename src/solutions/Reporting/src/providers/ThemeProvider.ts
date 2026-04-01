/**
 * Theme Provider Utility for Reporting Code Page
 *
 * Thin wrapper over shared @spaarke/ui-components theme utilities.
 * All theme resolution logic lives in:
 *   - themeStorage.ts   → resolveCodePageTheme(), setupCodePageThemeListener(),
 *                         getUserThemePreference(), getEffectiveDarkMode()
 *
 * Theme priority (per ADR-021):
 * 1. localStorage ('spaarke-theme') user preference
 * 2. URL flags parameter (themeOption=dark|light)
 * 3. DOM navbar color fallback (when embedded in Dataverse)
 *
 * @see ADR-021 - Fluent UI v9 Design System (dark mode required)
 * @see ADR-012 - Shared component library
 */

export {
  getUserThemePreference,
  getEffectiveDarkMode,
} from "@spaarke/ui-components";

export {
  resolveCodePageTheme as resolveTheme,
} from "@spaarke/ui-components";

import { setupCodePageThemeListener } from "@spaarke/ui-components";

/**
 * Set up listener for theme changes (localStorage and custom events).
 *
 * Wraps setupCodePageThemeListener from @spaarke/ui-components to maintain
 * the zero-argument callback signature used by Code Page consumers.
 *
 * @param callback - Called when theme changes (no arguments)
 * @returns Cleanup function to remove listeners
 */
export function setupThemeListener(callback: () => void): () => void {
  return setupCodePageThemeListener(() => callback());
}
