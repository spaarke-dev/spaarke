/**
 * Theme Provider Utility
 *
 * Resolves the appropriate Fluent UI theme based on Power Apps context.
 * This will be enhanced in Task B.1 to detect theme from context.
 */

import { Theme, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { IInputs } from '../generated/ManifestTypes';

/**
 * Resolve the appropriate Fluent UI theme based on Power Apps context.
 *
 * This function will be enhanced in Task B.1 to detect theme from context.
 * For now, it returns the light theme.
 *
 * @param context - PCF context
 * @returns Fluent UI theme
 */
export function resolveTheme(context: ComponentFramework.Context<IInputs>): Theme {
    // TODO: Task B.1 will implement dynamic theme detection
    // For now, always return light theme
    return webLightTheme;
}
