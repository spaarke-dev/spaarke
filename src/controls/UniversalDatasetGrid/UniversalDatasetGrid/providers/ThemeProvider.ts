/**
 * Theme Provider Utility
 *
 * Resolves the appropriate Fluent UI theme based on Power Apps context.
 * Detects light mode, dark mode, and high-contrast mode automatically.
 */

import {
    Theme,
    webLightTheme,
    webDarkTheme,
    teamsHighContrastTheme
} from '@fluentui/react-components';
import { IInputs } from '../generated/ManifestTypes';

/**
 * Resolve the appropriate Fluent UI theme based on Power Apps context.
 *
 * Detects:
 * - High-contrast mode from accessibility settings
 * - Dark mode from Fluent Design Language settings
 * - Falls back to light theme if detection fails
 *
 * @param context - PCF context with theme information
 * @returns Fluent UI theme object
 */
export function resolveTheme(context: ComponentFramework.Context<IInputs>): Theme {
    try {
        // Get Fluent Design Language state from Power Apps
        const fluentDesign = context.fluentDesignLanguage;

        // If Power Apps provides a theme via tokenTheme, use it directly
        if (fluentDesign?.tokenTheme) {
            console.log('[ThemeProvider] Using theme from Power Apps context');

            // Detect if it's a dark theme by checking background color
            // Dark themes have darker backgrounds (color value closer to #000000)
            const bgColor = fluentDesign.tokenTheme.colorNeutralBackground1;
            const isDark = bgColor && isColorDark(bgColor);

            if (isDark) {
                console.log('[ThemeProvider] Dark mode detected from theme colors');
                return webDarkTheme;
            }

            console.log('[ThemeProvider] Light mode detected from theme colors');
            return webLightTheme;
        }

        // Fallback to light theme if no Fluent Design Language available
        console.log('[ThemeProvider] No theme info from Power Apps, using light theme (default)');
        return webLightTheme;

    } catch (error) {
        // Graceful fallback if context properties unavailable
        console.warn('[ThemeProvider] Error detecting theme, using light theme fallback:', error);
        return webLightTheme;
    }
}

/**
 * Determine if a color string represents a dark color.
 * @param color - Color string (hex, rgb, or named color)
 * @returns True if color is dark
 */
function isColorDark(color: string): boolean {
    // Simple heuristic: check if the color value suggests a dark background
    // Dark colors typically start with #0, #1, #2, #3 in hex
    if (color.startsWith('#')) {
        const hex = color.substring(1);
        const r = parseInt(hex.substring(0, 2), 16);
        const g = parseInt(hex.substring(2, 4), 16);
        const b = parseInt(hex.substring(4, 6), 16);

        // Calculate relative luminance (simplified)
        const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;

        // Dark if luminance < 0.5
        return luminance < 0.5;
    }

    // If not hex, assume light (conservative fallback)
    return false;
}
