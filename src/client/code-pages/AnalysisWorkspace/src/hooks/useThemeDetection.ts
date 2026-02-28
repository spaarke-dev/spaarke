/**
 * useThemeDetection -- React hook for 4-level theme resolution in Code Pages
 *
 * Resolves the appropriate Fluent UI v9 theme using a priority chain that
 * replaces the hardcoded webLightTheme (PH-060-B):
 *
 *   1. URL parameter    -- ?theme=light|dark|highcontrast (passed via data envelope)
 *   2. Xrm frame-walk  -- detect Dataverse host theme from parent frames
 *   3. OS preference    -- prefers-color-scheme media query / forced-colors
 *   4. Default          -- webLightTheme
 *
 * Returns: { theme, themeName, isDark }
 *
 * Listens for OS theme changes via matchMedia "change" event and
 * user preference toggles via the "spaarke-theme-change" custom event.
 * Theme changes trigger a React state update, causing re-render with the
 * new theme.
 *
 * Pattern: Adapted from SprkChatPane/src/ThemeProvider.ts for use as a
 * React hook (stateful, with automatic cleanup).
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 * @see .claude/patterns/pcf/theme-management.md
 * @see SprkChatPane/src/ThemeProvider.ts (reference implementation)
 */

import { useState, useEffect, useCallback } from "react";
import {
    type Theme,
    webLightTheme,
    webDarkTheme,
    teamsHighContrastTheme,
} from "@fluentui/react-components";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const THEME_CHANGE_EVENT = "spaarke-theme-change";

/** Theme name values used for resolution and reporting */
export type ThemeName = "light" | "dark" | "highcontrast";

// ---------------------------------------------------------------------------
// Return type
// ---------------------------------------------------------------------------

export interface UseThemeDetectionResult {
    /** The resolved Fluent UI v9 Theme object for FluentProvider */
    theme: Theme;
    /** The resolved theme name (light, dark, highcontrast) */
    themeName: ThemeName;
    /** Whether the current theme is dark (not light, not high-contrast) */
    isDark: boolean;
}

// ---------------------------------------------------------------------------
// Level 1: URL parameter
// ---------------------------------------------------------------------------

/**
 * Resolve theme from an explicit URL parameter.
 * Accepts: "dark", "light", "highcontrast" (case-insensitive).
 */
function getThemeFromUrlParam(params?: URLSearchParams): ThemeName | null {
    if (!params) return null;
    const value = params.get("theme")?.toLowerCase();
    if (value === "dark" || value === "light" || value === "highcontrast") {
        return value;
    }
    return null;
}

// ---------------------------------------------------------------------------
// Level 2: Xrm frame-walk (Dataverse host theme detection)
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Walk the frame hierarchy to detect the Dataverse host theme.
 * Checks window -> window.parent -> window.top for Xrm global context.
 *
 * If Xrm is found, inspects getCurrentTheme().backgroundcolor to determine
 * whether the host is in dark mode. Falls back to body background luminance.
 */
function getThemeFromXrmFrameWalk(): ThemeName | null {
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

    // Fallback: check body background of this frame AND parent frames.
    // The code page's own body is typically transparent; the parent Dataverse
    // form's body carries the actual theme color.
    for (const frame of frames) {
        try {
            const bgColor = (frame as any).getComputedStyle((frame as any).document.body).backgroundColor;
            if (bgColor && bgColor !== "rgba(0, 0, 0, 0)" && bgColor !== "transparent") {
                const dark = isColorDark(bgColor);
                if (dark !== null) return dark ? "dark" : "light";
            }
        } catch {
            /* cross-origin or DOM access failed */
        }
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
// Level 3: OS / system preference
// ---------------------------------------------------------------------------

/**
 * Read the OS theme preference via matchMedia queries.
 * Detects forced-colors (high contrast) as a separate theme.
 */
function getSystemThemePreference(): ThemeName | null {
    try {
        if (window.matchMedia("(forced-colors: active)").matches) return "highcontrast";
        if (window.matchMedia("(prefers-color-scheme: dark)").matches) return "dark";
        if (window.matchMedia("(prefers-color-scheme: light)").matches) return "light";
    } catch {
        /* matchMedia not available */
    }
    return null;
}

// ---------------------------------------------------------------------------
// Theme resolution
// ---------------------------------------------------------------------------

/**
 * Map a ThemeName to a Fluent UI v9 Theme object.
 */
function themeNameToFluentTheme(name: ThemeName): Theme {
    switch (name) {
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
 * Resolve the theme name using the 4-level priority chain.
 *
 * Priority:
 *   1. URL parameter (?theme=dark|light|highcontrast)
 *   2. Xrm frame-walk (Dataverse host theme detection)
 *   3. System preference (prefers-color-scheme / forced-colors)
 *   4. Default: "light"
 */
function resolveThemeName(params?: URLSearchParams): ThemeName {
    try {
        // Level 1: URL parameter
        const urlTheme = getThemeFromUrlParam(params);
        if (urlTheme) return urlTheme;

        // Level 2: Xrm frame-walk
        const xrmTheme = getThemeFromXrmFrameWalk();
        if (xrmTheme) return xrmTheme;

        // Level 3: System preference
        const systemTheme = getSystemThemePreference();
        if (systemTheme) return systemTheme;

        // Level 4: Default
        return "light";
    } catch {
        return "light";
    }
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * useThemeDetection -- React hook that resolves the current theme and
 * re-renders when the OS preference or user preference changes.
 *
 * @param params - Pre-parsed URL parameters (data envelope already unwrapped in index.tsx)
 * @returns { theme, themeName, isDark }
 *
 * @example
 * ```tsx
 * const { theme, isDark } = useThemeDetection(appParams);
 * // Set body background to prevent flash
 * document.body.style.backgroundColor = isDark ? "#292929" : "#ffffff";
 * return <FluentProvider theme={theme}><App /></FluentProvider>;
 * ```
 */
export function useThemeDetection(params?: URLSearchParams): UseThemeDetectionResult {
    const [themeName, setThemeName] = useState<ThemeName>(() => resolveThemeName(params));

    // Re-resolve callback (called on OS change or user preference change)
    const reResolve = useCallback(() => {
        setThemeName(resolveThemeName(params));
    }, [params]);

    // Listen for OS theme changes and custom user preference changes
    useEffect(() => {
        // OS dark mode preference changes
        const darkQuery = window.matchMedia("(prefers-color-scheme: dark)");
        darkQuery.addEventListener("change", reResolve);

        // OS forced-colors (high contrast) changes
        const hcQuery = window.matchMedia("(forced-colors: active)");
        hcQuery.addEventListener("change", reResolve);

        // Custom spaarke theme change event (user preference toggle)
        window.addEventListener(THEME_CHANGE_EVENT, reResolve);

        return () => {
            darkQuery.removeEventListener("change", reResolve);
            hcQuery.removeEventListener("change", reResolve);
            window.removeEventListener(THEME_CHANGE_EVENT, reResolve);
        };
    }, [reResolve]);

    const theme = themeNameToFluentTheme(themeName);
    const isDark = themeName === "dark";

    return { theme, themeName, isDark };
}
