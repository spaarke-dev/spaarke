/**
 * SprkChatPane -- React 19 Code Page Entry Point
 *
 * Opened as a Dataverse side pane HTML web resource via:
 *   Xrm.App.sidePanes.createPane({
 *     title: "SprkChat",
 *     paneId: "sprkchat",
 *     canClose: true,
 *     imageSrc: "...",
 *     webresourceName: "sprk_sprkchatpane",
 *     data: "entityType=sprk_matter&entityId=...&playbookId=...&sessionId=..."
 *   })
 *
 * URL parameters (via Dataverse data envelope):
 *   entityType  - Dataverse entity logical name for record context (optional)
 *   entityId    - Entity record ID for contextual chat (optional)
 *   playbookId  - AI playbook ID for guided interactions (optional)
 *   sessionId   - Existing chat session to resume (optional)
 *   theme       - Theme override: light | dark | highcontrast (optional)
 *
 * Authentication:
 *   Token acquisition is handled independently by App.tsx via authService.ts.
 *   The authService uses Xrm.Utility.getGlobalContext() to acquire Bearer tokens
 *   for the BFF API. No tokens are passed via URL parameters or BroadcastChannel.
 *
 * Runtime configuration:
 *   BFF API base URL, MSAL client ID, and OAuth scope are resolved at runtime
 *   from Dataverse Environment Variables via resolveRuntimeConfig() from
 *   @spaarke/auth. No build-time .env.production values are used for these.
 *
 * Theme detection follows 4-level priority (replaces PH-010-B light-theme-only):
 *   1. User preference (localStorage 'spaarke-theme')
 *   2. URL parameter (?theme=dark|light|highcontrast)
 *   3. Xrm frame-walk (Dataverse host theme)
 *   4. System preference (prefers-color-scheme media query)
 *
 * @see ADR-008 - Endpoint filters for auth (token acquisition via Xrm SDK)
 * @see ADR-021 - Fluent UI v9 design system (React 19 createRoot for Code Pages)
 * @see ADR-022 - PCF platform libraries (does NOT apply to Code Pages)
 */

import { createRoot } from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import { resolveRuntimeConfig } from '@spaarke/auth';
import { App } from './App';
import { detectTheme, setupThemeListener } from './ThemeProvider';

// ---------------------------------------------------------------------------
// Parse URL parameters (Dataverse data envelope unwrap)
// ---------------------------------------------------------------------------

const rawUrlParams = new URLSearchParams(window.location.search);
const dataEnvelope = rawUrlParams.get('data');
const appParams = dataEnvelope ? new URLSearchParams(decodeURIComponent(dataEnvelope)) : rawUrlParams;

const entityType = appParams.get('entityType') ?? '';
const entityId = appParams.get('entityId') ?? '';
const playbookId = appParams.get('playbookId') ?? '';
const sessionId = appParams.get('sessionId') ?? '';

// ---- Analysis launch context params (task 002) ----
// Set by AnalysisWorkspace via openSprkChatPane() with a SprkChatLaunchContext.
// Passed through to App -> detectContext so the BFF can resolve the correct
// playbook and knowledge sources for the analysis context.
const analysisType = appParams.get('analysisType') ?? '';
const matterType = appParams.get('matterType') ?? '';
const practiceArea = appParams.get('practiceArea') ?? '';
const analysisId = appParams.get('analysisId') ?? '';
const sourceFileId = appParams.get('sourceFileId') ?? '';
const sourceContainerId = appParams.get('sourceContainerId') ?? '';
const mode = (appParams.get('mode') ?? '') as 'analysis' | 'general' | '';

// ---------------------------------------------------------------------------
// Theme detection (4-level priority -- replaces PH-010-B)
// ---------------------------------------------------------------------------

let theme = detectTheme(appParams);

// Set body background to match detected theme -- prevents white flash in dark mode.
// Uses the theme object's actual colorNeutralBackground1 value (a resolved hex/rgb string),
// NOT tokens.* (which are CSS variable references that only work inside FluentProvider).
const bgColor = (theme as Record<string, string>).colorNeutralBackground1;
if (bgColor) {
  document.body.style.backgroundColor = bgColor;
}

// ---------------------------------------------------------------------------
// Bootstrap: resolve runtime config, then render
// ---------------------------------------------------------------------------

const container = document.getElementById('root');
if (!container) throw new Error('[SprkChatPane] Root container #root not found in DOM.');

const root = createRoot(container);

/**
 * Async bootstrap: resolve BFF URL + MSAL client ID from Dataverse
 * Environment Variables at runtime, set window globals for @spaarke/auth
 * resolveConfig(), then render the application.
 */
async function bootstrap(): Promise<void> {
  // 1. Resolve runtime config from Dataverse Environment Variables
  const runtimeConfig = await resolveRuntimeConfig();

  // 2. Set window globals so @spaarke/auth resolveConfig() picks them up
  //    when initAuth() is called inside App.tsx
  window.__SPAARKE_MSAL_CLIENT_ID__ = runtimeConfig.msalClientId;
  window.__SPAARKE_BFF_BASE_URL__ = runtimeConfig.bffBaseUrl;
  window.__SPAARKE_BFF_API_SCOPE__ = runtimeConfig.bffOAuthScope;

  const apiBaseUrl = runtimeConfig.bffBaseUrl;

  // 3. Render the app with resolved config
  function renderApp(): void {
    root.render(
      <FluentProvider theme={theme} style={{ height: '100%' }}>
        <App
          entityType={entityType}
          entityId={entityId}
          playbookId={playbookId}
          sessionId={sessionId}
          apiBaseUrl={apiBaseUrl}
          analysisType={analysisType}
          matterType={matterType}
          practiceArea={practiceArea}
          analysisId={analysisId}
          sourceFileId={sourceFileId}
          sourceContainerId={sourceContainerId}
          mode={mode}
        />
      </FluentProvider>
    );
  }

  // Initial render
  renderApp();

  // Theme change listener -- re-render on system/user preference change
  setupThemeListener(() => {
    theme = detectTheme(appParams);
    const updatedBg = (theme as Record<string, string>).colorNeutralBackground1;
    if (updatedBg) {
      document.body.style.backgroundColor = updatedBg;
    }
    renderApp();
  });
}

bootstrap().catch((err) => {
  console.error('[SprkChatPane] Failed to resolve runtime configuration:', err);
  // Render a minimal error state so the user sees something
  root.render(
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <div style={{ padding: '24px', textAlign: 'center', color: 'var(--colorPaletteRedForeground1)' }}>
        <h3>Configuration Error</h3>
        <p>
          Failed to load runtime configuration from Dataverse.
          Ensure the SpaarkeCore solution is imported and environment variables are configured.
        </p>
        <p style={{ fontSize: '12px', color: 'var(--colorNeutralForeground3)' }}>
          {err instanceof Error ? err.message : String(err)}
        </p>
      </div>
    </FluentProvider>
  );
});
