/**
 * The widget theme comes from the MCP Apps host context, never from the page (ADR-021, FR-12):
 * `hostContext.theme` is "light" or "dark" at init (`app.getHostContext()`) and again on every change (`onhostcontextchanged`).
 *
 * Deliberately NOT used here: `detectDarkModeFromNavbar` (reads Dataverse chrome that does not exist in a Copilot frame),
 * `prefers-color-scheme` (the frame is the host's, and the host decides), and any storage (the widget runs in a sandboxed frame and a
 * remembered theme would go stale the next time the host theme changes). Phase 0 T1 confirmed Chat and Cowork send `theme: "dark"`.
 */
import { webDarkTheme, webLightTheme, type Theme } from '@fluentui/react-components';

/** The slice of the host context this module reads (structural: ext-apps' `McpUiHostContext` satisfies it). */
export interface HostThemeContext {
  theme?: string;
}

export type HostThemeName = 'light' | 'dark';

/** `dark` only when the host says so; anything else (no context yet, "light", an unknown value) is light. */
export function hostThemeName(context: HostThemeContext | null | undefined): HostThemeName {
  return context?.theme === 'dark' ? 'dark' : 'light';
}

/** The Fluent v9 theme for a host context. */
export function themeFromHostContext(context: HostThemeContext | null | undefined): Theme {
  return hostThemeName(context) === 'dark' ? webDarkTheme : webLightTheme;
}
