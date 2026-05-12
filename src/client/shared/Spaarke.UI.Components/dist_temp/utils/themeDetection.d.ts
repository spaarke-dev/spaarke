/**
 * Theme detection and bridging utilities
 * Detects Power Platform theme and bridges to Fluent UI v9
 */
import { Theme } from '@fluentui/react-components';
import { ThemeMode } from '../types';
export interface IThemeContext {
    fluentDesignLanguage?: {
        tokenTheme?: Theme;
        isDarkTheme?: boolean;
    };
}
/**
 * Detect theme from Power Platform context
 * @param context PCF context (cast to any to access fluentDesignLanguage)
 * @param themeMode User-configured theme mode
 * @returns Fluent UI v9 Theme
 */
export declare function detectTheme(context: any, themeMode?: ThemeMode): Theme;
/**
 * Detect if dark mode is enabled from Power Platform context
 * @param context PCF context
 * @returns true if dark mode, false otherwise
 */
export declare function isDarkMode(context: any): boolean;
//# sourceMappingURL=themeDetection.d.ts.map