/**
 * Theme Provider for Semantic Search Code Page
 *
 * Resolves the appropriate Fluent UI v9 theme using a 4-level priority chain
 * per ADR-026 (Full-Page Custom Page Standard):
 *
 *   1. URL parameter  — ?theme=dark|light|highcontrast (passed via data envelope)
 *   2. Xrm frame-walk — detect Dataverse host theme from parent frames
 *   3. System preference — prefers-color-scheme media query
 *   4. Default — webLightTheme
 *
 * Exports:
 *   detectTheme(params?)  — returns a Fluent v9 Theme object
 *   isDarkTheme(params?)  — returns boolean
 *   setupThemeListener()  — subscribes to system theme changes
 */

import {
    Theme,
    webLightTheme,
    webDarkTheme,
    teamsHighContrastTheme,
} from "@fluentui/react-components";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const THEME_CHANGE_EVENT = "spaarke-theme-change";

type ThemeValue = "light" | "dark" | "highcontrast";

// ---------------------------------------------------------------------------
// Level 1: URL parameter
// ---------------------------------------------------------------------------

/**
 * Resolve theme from an explicit URL parameter.
 * The `params` object is the already-unwrapped URLSearchParams (data envelope
 * decoded in index.tsx).  Accepts: "dark", "light", "highcontrast".
 */
function getThemeFromParams(params?: URLSearchParams): ThemeValue | null {
    if (!params) return null;
    const value = params.get("theme")?.toLowerCase();
    if (value === "dark" || value === "light" || value === "highcontrast") {
        return value;
    }
    return null;
}

// ---------------------------------------------------------------------------
// Level 2: Xrm frame-walk
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Walk the frame hierarchy to find the Xrm global and inspect the
 * Dataverse host theme.  Tries:
 *   - window.Xrm
 *   - window.parent.Xrm
 *   - window.top.Xrm
 *
 * If Xrm is found, checks:
 *   a) Xrm.Utility.getGlobalContext().getCurrentTheme() (returns theme info object)
 *   b) Navbar DOM element background color (luminance heuristic)
 */
function getThemeFromXrmFrameWalk(): ThemeValue | null {
    // Try to find Xrm in frame hierarchy
    const frames: Window[] = [window];
    try {
        if (window.parent && window.parent !== window) frames.push(window.parent);
    } catch { /* cross-origin */ }
    try {
        if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top);
    } catch { /* cross-origin */ }

    for (const frame of frames) {
        try {
            const xrm = (frame as any).Xrm;
            if (!xrm) continue;

            // Attempt getCurrentTheme() API
            const ctx = xrm.Utility?.getGlobalContext?.();
            if (ctx?.getCurrentTheme) {
                const themeInfo = ctx.getCurrentTheme();
                // Check the page/content background color — NOT the navbar color.
                // The navbar color is the brand color (e.g. red/dark for Spaarke)
                // and does NOT indicate light/dark mode.
                if (themeInfo?.backgroundcolor) {
                    const isDark = isColorDark(themeInfo.backgroundcolor);
                    if (isDark !== null) return isDark ? "dark" : "light";
                }
            }
        } catch { /* cross-origin or unavailable */ }
    }

    // Fallback: check the document body background (content area, not branded navbar)
    try {
        const bgColor = window.getComputedStyle(document.body).backgroundColor;
        if (bgColor && bgColor !== "rgba(0, 0, 0, 0)" && bgColor !== "transparent") {
            const isDark = isColorDark(bgColor);
            if (isDark !== null) return isDark ? "dark" : "light";
        }
    } catch { /* DOM access failed */ }

    return null;
}

/**
 * Determine if a CSS color string represents a dark color.
 * Supports rgb(...) and hex (#RRGGBB / #RGB) formats.
 * Returns null if the color cannot be parsed.
 */
function isColorDark(color: string): boolean | null {
    if (!color) return null;

    let r: number, g: number, b: number;

    // Try rgb(r, g, b) format
    const rgbMatch = color.match(/\d+/g)?.map(Number);
    if (rgbMatch && rgbMatch.length >= 3) {
        [r, g, b] = rgbMatch;
    } else if (color.startsWith("#")) {
        // Try hex format
        const hex = color.replace("#", "");
        if (hex.length === 3) {
            r = parseInt(hex[0] + hex[0], 16);
            g = parseInt(hex[1] + hex[1], 16);
            b = parseInt(hex[2] + hex[2], 16);
        } else if (hex.length >= 6) {
            r = parseInt(hex.substring(0, 2), 16);
            g = parseInt(hex.substring(2, 4), 16);
            b = parseInt(hex.substring(4, 6), 16);
        } else {
            return null;
        }
    } else {
        return null;
    }

    // Relative luminance calculation (ITU-R BT.601)
    const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
    return luminance < 0.5;
}

/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// Level 3: System preference
// ---------------------------------------------------------------------------

function getSystemThemePreference(): ThemeValue | null {
    try {
        if (window.matchMedia("(prefers-color-scheme: dark)").matches) return "dark";
        if (window.matchMedia("(prefers-color-scheme: light)").matches) return "light";
        // If forced-colors is active, treat as high contrast
        if (window.matchMedia("(forced-colors: active)").matches) return "highcontrast";
    } catch { /* matchMedia not available */ }
    return null;
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Map a ThemeValue to a Fluent UI v9 Theme object.
 */
function themeValueToFluentTheme(value: ThemeValue): Theme {
    switch (value) {
        case "dark":
            return webDarkTheme;
        case "highcontrast":
            return teamsHighContrastTheme;
        case "light":
        default:
            return webLightTheme;
    }
}

/**
 * Detect the current theme using the 4-level priority chain.
 *
 * @param params - Pre-parsed URL parameters (data envelope already unwrapped)
 * @returns Fluent UI v9 Theme object
 *
 * Priority:
 *   1. URL parameter (?theme=dark|light|highcontrast)
 *   2. Xrm frame-walk (Dataverse host theme detection)
 *   3. System preference (prefers-color-scheme / forced-colors)
 *   4. Default: webLightTheme
 */
export function detectTheme(params?: URLSearchParams): Theme {
    try {
        // Level 1: URL parameter
        const urlTheme = getThemeFromParams(params);
        if (urlTheme) return themeValueToFluentTheme(urlTheme);

        // Level 2: Xrm frame-walk
        const xrmTheme = getThemeFromXrmFrameWalk();
        if (xrmTheme) return themeValueToFluentTheme(xrmTheme);

        // Level 3: System preference
        const systemTheme = getSystemThemePreference();
        if (systemTheme) return themeValueToFluentTheme(systemTheme);

        // Level 4: Default
        return webLightTheme;
    } catch {
        return webLightTheme;
    }
}

/**
 * Check whether the detected theme is dark.
 *
 * @param params - Pre-parsed URL parameters (data envelope already unwrapped)
 * @returns true if the active theme is dark (not light, not high-contrast)
 */
export function isDarkTheme(params?: URLSearchParams): boolean {
    const theme = detectTheme(params);
    return theme === webDarkTheme;
}

/**
 * Set up listener for system theme changes.
 * Calls the callback when prefers-color-scheme or forced-colors changes.
 * Also listens for the custom 'spaarke-theme-change' event.
 *
 * @param callback - Invoked when the theme may have changed
 * @returns Cleanup function to remove listeners
 */
export function setupThemeListener(callback: () => void): () => void {
    const handleChange = () => callback();

    // System preference changes
    const darkQuery = window.matchMedia("(prefers-color-scheme: dark)");
    darkQuery.addEventListener("change", handleChange);

    // Forced colors (high contrast) changes
    const hcQuery = window.matchMedia("(forced-colors: active)");
    hcQuery.addEventListener("change", handleChange);

    // Custom spaarke theme change event
    window.addEventListener(THEME_CHANGE_EVENT, handleChange);

    return () => {
        darkQuery.removeEventListener("change", handleChange);
        hcQuery.removeEventListener("change", handleChange);
        window.removeEventListener(THEME_CHANGE_EVENT, handleChange);
    };
}
