/**
 * PlaybookBuilder -- React 19 Code Page Entry Point
 *
 * FR-P4-04 de-scope: this page is the BA catalog authoring surface (Action +
 * Binding rows over the two closed catalogs). The graph/canvas authoring it
 * previously hosted is retired (ratified OQ-2; the engine is frozen). It is
 * opened standalone or via Xrm.Navigation.navigateTo:
 *   Xrm.Navigation.navigateTo(
 *     { pageType: "webresource", webresourceName: "sprk_playbookbuilder" },
 *     { target: 2, width: { value: 95, unit: "%" }, height: { value: 95, unit: "%" } }
 *   )
 *
 * Runtime configuration:
 *   BFF API base URL and MSAL client ID are resolved at runtime from Dataverse
 *   Environment Variables via resolveRuntimeConfig() from @spaarke/auth.
 *   Build-time .env.production values are NOT used for these settings.
 *
 * Theme detection follows 4-level priority:
 *   1. URL parameter (?theme=dark|light|highcontrast)
 *   2. Xrm frame-walk (Dataverse host theme)
 *   3. System preference (prefers-color-scheme)
 *   4. Default: webLightTheme
 *
 * @see ADR-006 - Code Pages for standalone dialogs
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 * @see ADR-022 - React 19 for Code Pages (exempt from PCF React 16)
 */

import { useState, useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import { resolveRuntimeConfig } from '@spaarke/auth';
import { resolveCodePageTheme, setupCodePageThemeListener } from '@spaarke/ui-components';
import { App } from './App';

// ---------------------------------------------------------------------------
// ThemeRoot -- wrapper that uses useThemeDetection hook
// ---------------------------------------------------------------------------

function ThemeRoot(): JSX.Element {
  const [theme, setTheme] = useState(resolveCodePageTheme);

  useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  useEffect(() => {
    const bgColor = (theme as Record<string, string>).colorNeutralBackground1;
    if (bgColor) {
      document.body.style.backgroundColor = bgColor;
    }
  }, [theme]);

  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <App />
    </FluentProvider>
  );
}

// ---------------------------------------------------------------------------
// Bootstrap: resolve runtime config then render
// ---------------------------------------------------------------------------

/**
 * Async bootstrap: resolve BFF URL + MSAL client ID from Dataverse
 * Environment Variables at runtime, set window globals for @spaarke/auth
 * resolveConfig(), then render the application.
 */
async function bootstrap(): Promise<void> {
  const container = document.getElementById('root');
  if (!container) throw new Error('[PlaybookBuilder] Root container #root not found in DOM.');

  // Resolve BFF URL + MSAL client ID from Dataverse Environment Variables at runtime
  const runtimeConfig = await resolveRuntimeConfig();

  // Set window globals so @spaarke/auth resolveConfig() can pick them up
  // when initAuth() is called later during authentication
  window.__SPAARKE_MSAL_CLIENT_ID__ = runtimeConfig.msalClientId;
  window.__SPAARKE_BFF_BASE_URL__ = runtimeConfig.bffBaseUrl;

  createRoot(container).render(<ThemeRoot />);
}

bootstrap();
