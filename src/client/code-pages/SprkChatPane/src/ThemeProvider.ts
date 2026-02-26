/**
 * Theme Provider for SprkChatPane Code Page
 *
 * Resolves the appropriate Fluent UI v9 theme using a 4-level priority chain
 * per the task-012 specification:
 *
 *   1. User preference  — localStorage('spaarke-theme')
 *   2. URL parameter     — ?theme=dark|light|highcontrast (passed via data envelope)
 *   3. Xrm frame-walk   — detect Dataverse host theme from parent frames
 *   4. System preference — prefers-color-scheme media query
 *   5. Default           — webLightTheme
 *
 * Exports:
 *   detectTheme(params?)    — returns a Fluent v9 Theme object
 *   isDarkTheme(params?)    — returns boolean
 *   setupThemeListener()    — subscribes to system + user preference theme changes
 *
 * @see ADR-021 - Fluent UI v9 design system
 * @see theme-management.md pattern
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

const THEME_STORAGE_KEY = "spaarke-theme";
const THEME_CHANGE_EVENT = "spaarke-theme-change";

type ThemeValue = "light" | "dark" | "highcontrast";
type ThemePreference = "auto" | "light" | "dark";

// ---------------------------------------------------------------------------
// Level 1: User preference (localStorage)
// ---------------------------------------------------------------------------

/**
 * Read the user's explicit theme choice from localStorage.
 * Returns null if "auto" or not set (fall through to next level).
 */
function getThemeFromUserPreference(): ThemeValue | null {
    try {
        const pref = localStorage.getItem(THEME_STORAGE_KEY) as ThemePreference | null;
        if (pref === "dark") return "dark";
        if (pref === "light") return "light";
    } catch {
        /* localStorage unavailable (iframe sandbox) */
    }
    return null;
}

// ---------------------------------------------------------------------------
// Level 2: URL parameter
// ---------------------------------------------------------------------------

/**
 * Resolve theme from an explicit URL parameter.
 * The `params` object is the already-unwrapped URLSearchParams (data envelope
 * decoded in index.tsx). Accepts: "dark", "light", "highcontrast".
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
// Level 3: Xrm frame-walk
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Walk the frame hierarchy to find the Xrm global and inspect the
 * Dataverse host theme. Tries:
 *   - window.Xrm
 *   - window.parent.Xrm
 *   - window.top.Xrm
 *
 * If Xrm is found, checks:
 *   a) Xrm.Utility.getGlobalContext().getCurrentTheme() (returns theme info object)
 *   b) Body background color luminance heuristic
 */
function getThemeFromXrmFrameWalk(): ThemeValue | null {
    const frames: Window[] = [window];
    try {
        if (window.parent && window.parent !== window) frames.push(window.parent);
    } catch {
        /* cross-origin */
    }
    try {
        if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top);
    } catch {
        /* cross-origin */
    }

    for (const frame of frames) {
        try {
            const xrm = (frame as any).Xrm;
            if (!xrm) continue;

            // Attempt getCurrentTheme() API
            const ctx = xrm.Utility?.getGlobalContext?.();
            if (ctx?.getCurrentTheme) {
                const themeInfo = ctx.getCurrentTheme();
                // Check the page/content background color -- NOT the navbar color.
                // The navbar color is the brand color and does NOT indicate light/dark mode.
                if (themeInfo?.backgroundcolor) {
                    const dark = isColorDark(themeInfo.backgroundcolor);
                    if (dark !== null) return dark ? "dark" : "light";
                }
            }
        } catch {
            /* cross-origin or unavailable */
        }
    }

    // Fallback: check the document body background (content area, not branded navbar)
    try {
        const bgColor = window.getComputedStyle(document.body).backgroundColor;
        if (bgColor && bgColor !== "rgba(0, 0, 0, 0)" && bgColor !== "transparent") {
            const dark = isColorDark(bgColor);
            if (dark !== null) return dark ? "dark" : "light";
        }
    } catch {
        /* DOM access failed */
    }

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
// Level 4: System preference
// ---------------------------------------------------------------------------

function getSystemThemePreference(): ThemeValue | null {
    try {
        if (window.matchMedia("(prefers-color-scheme: dark)").matches) return "dark";
        if (window.matchMedia("(prefers-color-scheme: light)").matches) return "light";
        // If forced-colors is active, treat as high contrast
        if (window.matchMedia("(forced-colors: active)").matches) return "highcontrast";
    } catch {
        /* matchMedia not available */
    }
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
 *   1. User preference (localStorage 'spaarke-theme')
 *   2. URL parameter (?theme=dark|light|highcontrast)
 *   3. Xrm frame-walk (Dataverse host theme detection)
 *   4. System preference (prefers-color-scheme / forced-colors)
 *   5. Default: webLightTheme
 */
export function detectTheme(params?: URLSearchParams): Theme {
    try {
        // Level 1: User preference (localStorage)
        const userTheme = getThemeFromUserPreference();
        if (userTheme) return themeValueToFluentTheme(userTheme);

        // Level 2: URL parameter
        const urlTheme = getThemeFromParams(params);
        if (urlTheme) return themeValueToFluentTheme(urlTheme);

        // Level 3: Xrm frame-walk
        const xrmTheme = getThemeFromXrmFrameWalk();
        if (xrmTheme) return themeValueToFluentTheme(xrmTheme);

        // Level 4: System preference
        const systemTheme = getSystemThemePreference();
        if (systemTheme) return themeValueToFluentTheme(systemTheme);

        // Default
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
 * Set up listener for theme changes from all sources.
 * Calls the callback when:
 *   - System prefers-color-scheme changes
 *   - Forced colors (high contrast) changes
 *   - User toggles via custom 'spaarke-theme-change' event
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

    // Custom spaarke theme change event (user preference toggle)
    window.addEventListener(THEME_CHANGE_EVENT, handleChange);

    return () => {
        darkQuery.removeEventListener("change", handleChange);
        hcQuery.removeEventListener("change", handleChange);
        window.removeEventListener(THEME_CHANGE_EVENT, handleChange);
    };
}
