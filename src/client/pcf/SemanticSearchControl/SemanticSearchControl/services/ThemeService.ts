/**
 * Theme resolution service for SemanticSearchControl.
 *
 * Follows the theme-management pattern from .claude/patterns/pcf/theme-management.md
 * Supports light, dark, and high-contrast modes per ADR-021.
 */

import {
    Theme,
    webLightTheme,
    webDarkTheme,
    teamsHighContrastTheme,
} from "@fluentui/react-components";
import { IInputs } from "../generated/ManifestTypes";

const THEME_STORAGE_KEY = "spaarke-theme";
const THEME_CHANGE_EVENT = "spaarke-theme-change";

export type ThemePreference = "auto" | "light" | "dark";

/**
 * Get user's explicit theme preference from localStorage.
 */
export function getUserThemePreference(): ThemePreference {
    try {
        const stored = localStorage.getItem(THEME_STORAGE_KEY);
        if (stored === "light" || stored === "dark" || stored === "auto") {
            return stored;
        }
    } catch {
        // localStorage not available (e.g., sandbox)
    }
    return "auto";
}

/**
 * Set user's theme preference and dispatch change event.
 */
export function setUserThemePreference(preference: ThemePreference): void {
    try {
        localStorage.setItem(THEME_STORAGE_KEY, preference);
        window.dispatchEvent(new CustomEvent(THEME_CHANGE_EVENT));
    } catch {
        // localStorage not available
    }
}

/**
 * Detect dark mode from URL flags (Power Apps parameter).
 */
function detectDarkModeFromUrl(): boolean | null {
    try {
        const params = new URLSearchParams(window.location.search);
        const flags = params.get("flags");
        if (flags?.includes("themeOption=dark")) return true;
        if (flags?.includes("themeOption=light")) return false;
    } catch {
        // URL parsing failed
    }
    return null;
}

/**
 * Detect dark mode from navbar background color (Custom Page fallback).
 */
function detectDarkModeFromNavbar(): boolean | null {
    try {
        const navbar = document.querySelector('[data-id="navbar-container"]');
        if (!navbar) return null;
        const bgColor = window.getComputedStyle(navbar).backgroundColor;
        const rgb = bgColor.match(/\d+/g)?.map(Number) ?? [];
        if (rgb.length < 3) return null;
        // Calculate luminance - dark if low
        const luminance = (0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]) / 255;
        return luminance < 0.5;
    } catch {
        // DOM access failed
    }
    return null;
}

/**
 * Detect if system prefers dark mode.
 */
function getSystemPrefersDark(): boolean {
    try {
        return window.matchMedia("(prefers-color-scheme: dark)").matches;
    } catch {
        return false;
    }
}

/**
 * Get the effective dark mode state considering all sources.
 */
export function getEffectiveDarkMode(
    context?: ComponentFramework.Context<IInputs>
): boolean {
    const preference = getUserThemePreference();

    // 1. User explicit choice
    if (preference === "dark") return true;
    if (preference === "light") return false;

    // 2. URL flag (Power Apps dark mode parameter)
    const urlDark = detectDarkModeFromUrl();
    if (urlDark !== null) return urlDark;

    // 3. PCF context (fluentDesignLanguage)
    if (context?.fluentDesignLanguage?.isDarkTheme !== undefined) {
        return context.fluentDesignLanguage.isDarkTheme;
    }

    // 4. Navbar DOM detection (Custom Page fallback)
    const navbarDark = detectDarkModeFromNavbar();
    if (navbarDark !== null) return navbarDark;

    // 5. System preference
    return getSystemPrefersDark();
}

/**
 * Resolve the appropriate Fluent theme based on context.
 *
 * Priority:
 * 1. User explicit preference (localStorage)
 * 2. URL flag (themeOption parameter)
 * 3. PCF context (fluentDesignLanguage)
 * 4. Navbar color detection
 * 5. System preference
 *
 * @param context - PCF context with fluentDesignLanguage
 * @returns Fluent Theme object
 */
export function resolveTheme(
    context?: ComponentFramework.Context<IInputs>
): Theme {
    // Check for high contrast first (from context)
    if (context?.fluentDesignLanguage?.tokenTheme) {
        const tokenTheme = String(context.fluentDesignLanguage.tokenTheme);
        // High contrast themes have specific identifiers
        if (
            tokenTheme === "TeamsHighContrast" ||
            tokenTheme === "HighContrastWhite" ||
            tokenTheme === "HighContrastBlack"
        ) {
            return teamsHighContrastTheme;
        }
    }

    // Resolve dark mode
    const isDark = getEffectiveDarkMode(context);
    return isDark ? webDarkTheme : webLightTheme;
}

/**
 * Set up listeners for theme changes.
 *
 * @param callback - Function to call when theme changes
 * @param context - PCF context
 * @returns Cleanup function to remove listeners
 */
export function setupThemeListener(
    callback: (isDark: boolean) => void,
    context?: ComponentFramework.Context<IInputs>
): () => void {
    // Custom event from theme toggle
    const handleThemeChange = () => callback(getEffectiveDarkMode(context));
    window.addEventListener(THEME_CHANGE_EVENT, handleThemeChange);

    // System preference changes
    let mediaQuery: MediaQueryList | null = null;
    try {
        mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
        mediaQuery.addEventListener("change", handleThemeChange);
    } catch {
        // matchMedia not available
    }

    // Return cleanup function
    return () => {
        window.removeEventListener(THEME_CHANGE_EVENT, handleThemeChange);
        if (mediaQuery) {
            mediaQuery.removeEventListener("change", handleThemeChange);
        }
    };
}

export default {
    resolveTheme,
    setupThemeListener,
    getUserThemePreference,
    setUserThemePreference,
    getEffectiveDarkMode,
};
