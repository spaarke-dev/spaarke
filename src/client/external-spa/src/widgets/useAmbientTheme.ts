/**
 * useAmbientTheme — resolves and live-tracks the ambient light / dark / Teams theme (the same source as the app shell:
 * `resolveCodePageTheme` + `setupCodePageThemeListener`, which `main.tsx` also uses).
 *
 * The shared grid takes the theme to style its own popover / menu portals, which render outside the React tree, so every
 * host of `<ExternalDataGrid>` hands it this value (`GridWidgetBody`, and `DocumentLibrary` for the record Documents list).
 * It lives in its own file so a list component can use it without importing `GridWidgetBody` (which imports the record
 * template, which imports the lists: a cycle).
 */
import * as React from 'react';
import { webLightTheme, type Theme } from '@fluentui/react-components';
import { resolveCodePageTheme, setupCodePageThemeListener } from '@spaarke/ui-components/utils/themeStorage';

export function useAmbientTheme(): Theme {
  const [theme, setTheme] = React.useState<Theme>(() => resolveCodePageTheme() ?? webLightTheme);
  React.useEffect(() => setupCodePageThemeListener(next => setTheme(next ?? webLightTheme)), []);
  return theme;
}
