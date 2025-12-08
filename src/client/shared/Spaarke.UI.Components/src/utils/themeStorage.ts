/**
 * Theme Storage Utilities
 *
 * Centralized theme persistence and detection for all PCF controls.
 * Extends existing themeDetection.ts with localStorage support.
 *
 * @see ADR-012 - Shared component library
 * @see projects/mda-darkmode-theme/spec.md Section 3.4
 */

import { Theme, webLightTheme, webDarkTheme } from "@fluentui/react-components";

// ============================================================================
// Constants
// ============================================================================

export const THEME_STORAGE_KEY = 'spaarke-theme';
export const THEME_CHANGE_EVENT = 'spaarke-theme-change';

export type ThemePreference = 'light' | 'dark' | 'auto';

// ============================================================================
// Storage Functions
// ============================================================================

/**
 * Get user's theme preference from localStorage
 * @returns ThemePreference ('auto' if not set)
 */
export function getUserThemePreference(): ThemePreference {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    if (stored === 'light' || stored === 'dark' || stored === 'auto') {
        return stored;
    }
    return 'auto';
}

/**
 * Set user's theme preference in localStorage
 * Dispatches custom event for same-tab listeners
 */
export function setUserThemePreference(theme: ThemePreference): void {
    localStorage.setItem(THEME_STORAGE_KEY, theme);

    window.dispatchEvent(new CustomEvent(THEME_CHANGE_EVENT, {
        detail: { theme }
    }));
}

// ============================================================================
// Theme Resolution
// ============================================================================

/**
 * Get effective dark mode considering all sources
 *
 * Priority:
 * 1. localStorage (user's explicit preference)
 * 2. Power Platform context (fluentDesignLanguage.isDarkTheme)
 * 3. DOM navbar detection (Custom Pages fallback)
 * 4. System preference (OS/browser)
 *
 * @param context - PCF context (optional)
 * @returns true if dark mode should be active
 */
export function getEffectiveDarkMode(context?: any): boolean {
    const preference = getUserThemePreference();

    // Explicit user choice
    if (preference === 'dark') return true;
    if (preference === 'light') return false;

    // Auto mode: check Power Platform context first
    if (context?.fluentDesignLanguage?.isDarkTheme !== undefined) {
        return context.fluentDesignLanguage.isDarkTheme;
    }

    // Fallback for Custom Pages: check navbar background color
    const navbar = document.querySelector("[data-id='navbar-container']");
    if (navbar) {
        const bg = getComputedStyle(navbar).backgroundColor;
        if (bg === "rgb(10, 10, 10)") return true;   // Dark mode navbar
        if (bg === "rgb(240, 240, 240)") return false; // Light mode navbar
    }

    // Final fallback to system preference
    return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
}

/**
 * Resolve Fluent UI theme based on effective dark mode
 * @param context - PCF context (optional)
 * @returns Fluent UI v9 Theme (webDarkTheme or webLightTheme)
 */
export function resolveThemeWithUserPreference(context?: any): Theme {
    return getEffectiveDarkMode(context) ? webDarkTheme : webLightTheme;
}

// ============================================================================
// Event Listeners
// ============================================================================

export interface ThemeChangeHandler {
    (isDark: boolean): void;
}

/**
 * Set up theme change listeners for PCF controls
 *
 * Listens for:
 * - localStorage changes from other tabs
 * - Custom events from same-tab theme menu
 * - System preference changes (for auto mode)
 *
 * @param onChange - Callback when theme changes
 * @param context - PCF context (optional, for re-evaluating effective theme)
 * @returns Cleanup function to remove listeners
 */
export function setupThemeListener(
    onChange: ThemeChangeHandler,
    context?: any
): () => void {
    const handleStorageChange = (event: StorageEvent) => {
        if (event.key === THEME_STORAGE_KEY) {
            onChange(getEffectiveDarkMode(context));
        }
    };

    const handleThemeEvent = () => {
        onChange(getEffectiveDarkMode(context));
    };

    const handleSystemChange = (event: MediaQueryListEvent) => {
        if (getUserThemePreference() === 'auto') {
            onChange(event.matches);
        }
    };

    // Add listeners
    window.addEventListener('storage', handleStorageChange);
    window.addEventListener(THEME_CHANGE_EVENT, handleThemeEvent);

    const mediaQuery = window.matchMedia?.('(prefers-color-scheme: dark)');
    mediaQuery?.addEventListener('change', handleSystemChange);

    // Return cleanup function
    return () => {
        window.removeEventListener('storage', handleStorageChange);
        window.removeEventListener(THEME_CHANGE_EVENT, handleThemeEvent);
        mediaQuery?.removeEventListener('change', handleSystemChange);
    };
}
