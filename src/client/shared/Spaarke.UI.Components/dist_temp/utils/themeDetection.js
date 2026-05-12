/**
 * Theme detection and bridging utilities
 * Detects Power Platform theme and bridges to Fluent UI v9
 */
import { webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { spaarkeLight } from '../theme/brand';
/**
 * Detect theme from Power Platform context
 * @param context PCF context (cast to any to access fluentDesignLanguage)
 * @param themeMode User-configured theme mode
 * @returns Fluent UI v9 Theme
 */
export function detectTheme(context, themeMode = 'Auto') {
    // User explicitly chose Spaarke theme
    if (themeMode === 'Spaarke') {
        return spaarkeLight;
    }
    // User explicitly chose Host theme
    if (themeMode === 'Host') {
        const hostTheme = context.fluentDesignLanguage?.tokenTheme;
        if (hostTheme) {
            return hostTheme;
        }
        // Fallback to web theme if host theme unavailable
        const isDark = context.fluentDesignLanguage?.isDarkTheme;
        return isDark ? webDarkTheme : webLightTheme;
    }
    // Auto mode: Try host theme, fallback to Spaarke
    const hostTheme = context.fluentDesignLanguage?.tokenTheme;
    if (hostTheme) {
        return hostTheme;
    }
    // No host theme available - use Spaarke brand theme
    return spaarkeLight;
}
/**
 * Detect if dark mode is enabled from Power Platform context
 * @param context PCF context
 * @returns true if dark mode, false otherwise
 */
export function isDarkMode(context) {
    return context.fluentDesignLanguage?.isDarkTheme ?? false;
}
//# sourceMappingURL=themeDetection.js.map