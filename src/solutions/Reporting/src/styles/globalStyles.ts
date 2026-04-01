/**
 * globalStyles.ts — Page-level dark mode style injection for the Reporting Code Page.
 *
 * Power BI reports are embedded with BackgroundType.Transparent, which makes the
 * report background transparent so the page's body background bleeds through.
 * For this to work correctly in dark mode, the `body` and `html` elements must
 * carry the correct Fluent v9 surface color — not the browser default white.
 *
 * Problem: FluentProvider injects Fluent CSS custom properties onto its root
 * element AFTER React mounts. Until then the browser renders a white body,
 * causing a visible FOUC (Flash Of Unstyled Content) in dark mode.
 *
 * Solution: Apply the resolved theme's `colorNeutralBackground1` token value
 * directly to `document.body` as a `backgroundColor` as early as possible —
 * before the async bootstrap begins — so the initial paint is correct.
 * The same function is called again whenever the theme changes, so the body
 * background tracks the active theme throughout the session.
 *
 * Token values used:
 *   webLightTheme.colorNeutralBackground1 = "#ffffff"
 *   webDarkTheme.colorNeutralBackground1  = "#141414"
 *
 * These are sourced directly from the Fluent v9 theme objects to avoid
 * hard-coding — if Fluent changes their token values, we pick up the change.
 *
 * @see ADR-021 - Fluent UI v9; design tokens only; dark mode required
 */

import { webLightTheme, webDarkTheme, type Theme } from "@fluentui/react-components";
import { resolveCodePageTheme } from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Page-level body style application
// ---------------------------------------------------------------------------

/**
 * Apply the Fluent v9 surface background color to `document.body` and `html`.
 *
 * This ensures the page background matches the active Fluent theme even before
 * React and FluentProvider have mounted. The transparent Power BI report will
 * then correctly show the page's background color in both light and dark mode.
 *
 * @param theme - The active Fluent v9 Theme object (webLightTheme or webDarkTheme)
 */
export function applyBodyTheme(theme: Theme): void {
  try {
    const bg = theme.colorNeutralBackground1;
    const fg = theme.colorNeutralForeground1;

    // Apply to both html and body to cover all pre-React paint scenarios
    document.documentElement.style.setProperty("background-color", bg);
    document.body.style.setProperty("background-color", bg);
    document.body.style.setProperty("color", fg);
    document.body.style.setProperty("margin", "0");
    document.body.style.setProperty("padding", "0");
  } catch {
    // DOM not available (SSR / test env) — silently no-op
  }
}

/**
 * Resolve the current theme and apply it to the page body.
 *
 * Call this as the first synchronous operation in the Code Page bootstrap
 * so the initial paint uses the correct background color, eliminating FOUC.
 *
 * Also registers a theme-change listener so the body background stays in
 * sync whenever the user toggles between light and dark mode.
 *
 * @returns Cleanup function to remove the theme-change listener.
 */
export function initBodyTheme(): () => void {
  // Apply synchronously on first call so the initial paint is correct
  const initialTheme = resolveCodePageTheme();
  applyBodyTheme(initialTheme);

  // Keep body background in sync with theme changes during the session.
  // setupCodePageThemeListener is imported lazily to avoid a top-level
  // import cycle (ThemeProvider re-exports it from @spaarke/ui-components).
  let cleanup: (() => void) | null = null;
  try {
    // Dynamic import to avoid any SSR/test issues with addEventListener
    const { setupCodePageThemeListener } = require("@spaarke/ui-components") as {
      setupCodePageThemeListener: (cb: (t: Theme) => void) => () => void;
    };
    cleanup = setupCodePageThemeListener((newTheme: Theme) => {
      applyBodyTheme(newTheme);
    });
  } catch {
    // Listener setup failed — initial theme is already applied
  }

  return () => {
    cleanup?.();
  };
}

// ---------------------------------------------------------------------------
// Utility: resolve whether a Fluent v9 Theme object is dark
// ---------------------------------------------------------------------------

/**
 * Returns true if the given Fluent v9 theme is a dark theme.
 * Used for conditional styling decisions outside of React components.
 *
 * Detects dark mode by comparing the `colorNeutralBackground1` token:
 * dark themes have a very low-luminance background.
 */
export function isDarkTheme(theme: Theme): boolean {
  // webDarkTheme.colorNeutralBackground1 = "#141414"
  // Compare against the known dark theme value rather than parsing the hex,
  // since Fluent guarantees these standard theme objects remain stable.
  return theme.colorNeutralBackground1 === webDarkTheme.colorNeutralBackground1;
}

// ---------------------------------------------------------------------------
// Re-export token values for use in non-React contexts (pre-mount HTML)
// ---------------------------------------------------------------------------

/**
 * The Fluent v9 surface background token values for light and dark themes.
 * Use these when constructing HTML strings before FluentProvider is mounted
 * (e.g., error fallback content in main.tsx).
 */
export const SURFACE_BG = {
  light: webLightTheme.colorNeutralBackground1,
  dark: webDarkTheme.colorNeutralBackground1,
} as const;

export const SURFACE_FG = {
  light: webLightTheme.colorNeutralForeground1,
  dark: webDarkTheme.colorNeutralForeground1,
} as const;

export const SURFACE_FG3 = {
  light: webLightTheme.colorNeutralForeground3,
  dark: webDarkTheme.colorNeutralForeground3,
} as const;
