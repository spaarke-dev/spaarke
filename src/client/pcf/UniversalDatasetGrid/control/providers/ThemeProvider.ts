/**
 * Theme Provider Utility
 *
 * Resolves the appropriate Fluent UI theme based on Power Apps context.
 * Detects light mode, dark mode, and high-contrast mode automatically.
 *
 * Theme priority (per spec):
 * 1. localStorage ('spaarke-theme') user preference
 * 2. Power Apps context (fluentDesignLanguage.isDarkTheme)
 * 3. DOM navbar color fallback
 * 4. System preference (prefers-color-scheme)
 *
 * Note: Per-control theme toggle removed in favor of global theme menu.
 * See: projects/mda-darkmode-theme/spec.md
 */

import {
    Theme,
    webLightTheme,
    webDarkTheme
} from '@fluentui/react-components';
import { IInputs } from '../generated/ManifestTypes';
import { logger } from '../utils/logger';

// ─────────────────────────────────────────────────────────────────────────────
// Theme Storage Utilities
// TODO: Import from '@spaarke/ui-components' when package is published
// These are inlined for now per ADR-012 transition plan
// ─────────────────────────────────────────────────────────────────────────────

const STORAGE_KEY = 'spaarke-theme';
const THEME_CHANGE_EVENT = 'spaarke-theme-change';

type ThemePreference = 'auto' | 'light' | 'dark';

/**
 * Get the user's theme preference from localStorage
 */
export function getUserThemePreference(): ThemePreference {
    try {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (stored === 'light' || stored === 'dark' || stored === 'auto') {
            return stored;
        }
    } catch {
        // localStorage not available (SSR, private browsing, etc.)
    }
    return 'auto';
}

/**
 * Detect dark mode from DOM navbar color (Power Apps fallback)
 */
function detectDarkModeFromNavbar(): boolean | null {
    try {
        const navbar = document.querySelector('[data-id="navbar-container"]');
        if (navbar) {
            const bgColor = window.getComputedStyle(navbar).backgroundColor;
            // rgb(10, 10, 10) = dark, rgb(240, 240, 240) = light
            if (bgColor === 'rgb(10, 10, 10)') {
                return true;
            }
            if (bgColor === 'rgb(240, 240, 240)') {
                return false;
            }
        }
    } catch {
        // DOM access failed
    }
    return null;
}

/**
 * Get system theme preference
 */
function getSystemThemePreference(): boolean {
    try {
        return window.matchMedia('(prefers-color-scheme: dark)').matches;
    } catch {
        return false;
    }
}

/**
 * Get effective dark mode state considering all sources
 *
 * Priority:
 * 1. localStorage user preference (if not 'auto')
 * 2. Power Apps context (isDarkTheme)
 * 3. DOM navbar color fallback
 * 4. System preference
 *
 * @param context - PCF context (optional)
 * @returns boolean - true if dark mode should be active
 */
export function getEffectiveDarkMode(context?: ComponentFramework.Context<IInputs>): boolean {
    const preference = getUserThemePreference();

    // 1. User explicit choice overrides everything
    if (preference === 'dark') {
        return true;
    }
    if (preference === 'light') {
        return false;
    }

    // 2. Check Power Apps context
    if (context?.fluentDesignLanguage?.isDarkTheme !== undefined) {
        return context.fluentDesignLanguage.isDarkTheme;
    }

    // 3. Check DOM navbar color
    const navbarDark = detectDarkModeFromNavbar();
    if (navbarDark !== null) {
        return navbarDark;
    }

    // 4. Fall back to system preference
    return getSystemThemePreference();
}

/**
 * Set up listener for theme changes (localStorage and system preference)
 *
 * @param callback - Called when theme changes with new isDark value
 * @param context - PCF context (optional, for context-based theme detection)
 * @returns Cleanup function to remove listeners
 */
export function setupThemeListener(
    callback: (isDark: boolean) => void,
    context?: ComponentFramework.Context<IInputs>
): () => void {
    // Handle custom theme change event (from ribbon menu)
    const handleThemeChange = () => {
        callback(getEffectiveDarkMode(context));
    };

    // Handle system preference change
    const handleSystemChange = () => {
        // Only respond if user preference is 'auto'
        if (getUserThemePreference() === 'auto') {
            callback(getEffectiveDarkMode(context));
        }
    };

    // Listen for custom event
    window.addEventListener(THEME_CHANGE_EVENT, handleThemeChange);

    // Listen for system preference changes
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    mediaQuery.addEventListener('change', handleSystemChange);

    // Return cleanup function
    return () => {
        window.removeEventListener(THEME_CHANGE_EVENT, handleThemeChange);
        mediaQuery.removeEventListener('change', handleSystemChange);
    };
}

/**
 * Resolve the appropriate Fluent UI theme based on user preference and context.
 *
 * Uses getEffectiveDarkMode() which implements the priority chain:
 * 1. localStorage user preference
 * 2. Power Apps context
 * 3. DOM navbar color
 * 4. System preference
 *
 * @param context - PCF context with theme information
 * @returns Fluent UI theme object
 */
export function resolveTheme(context: ComponentFramework.Context<IInputs>): Theme {
    try {
        const isDark = getEffectiveDarkMode(context);
        logger.debug('ThemeProvider', `Theme resolved: ${isDark ? 'dark' : 'light'}`);
        return isDark ? webDarkTheme : webLightTheme;
    } catch (error) {
        logger.warn('ThemeProvider', 'Error resolving theme, using light theme fallback', error);
        return webLightTheme;
    }
}
