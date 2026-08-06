/**
 * sprk_communicationconversationpage entry point — Surface 2b (task 032, FR-14b).
 *
 * Standalone Vite Code Page rendering the shared two-pane conversation widget
 * (`<ConversationWorkspace />` + `<ConversationView />`) FULL-PAGE, record-less
 * / workspace mode. Bootstraps `@spaarke/auth` BEFORE rendering — mirrors
 * `src/solutions/LegalWorkspace/src/main.tsx`'s BLOCKING bootstrap (not
 * DailyBriefing's non-blocking one): the conversation list is this page's
 * entire content, not an optional enhancement layered onto an
 * already-rendered digest, so `authenticatedFetch` must be safely callable
 * the instant `<ConversationWorkspace />` fires its first thread-list load.
 *
 * @see ADR-006 - Code Pages for standalone surfaces (not PCF)
 * @see ADR-021 - Fluent v9 / dark mode via the page FluentProvider
 * @see ADR-022 - Code pages bundle their own React 18 (not platform-provided)
 * @see ADR-028 - Spaarke Auth Architecture
 */

import * as React from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import { AppErrorBoundary, useTheme } from '@spaarke/ui-components';
import { resolveRuntimeConfig, getAuthProvider } from '@spaarke/auth';
import { setRuntimeConfig } from './config/runtimeConfig';
import { ensureAuthInitialized } from './services/authInit';
import { App } from './App';

function Root(): React.ReactElement {
  const { theme } = useTheme();

  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <AppErrorBoundary surfaceName="Conversation">
        <App />
      </AppErrorBoundary>
    </FluentProvider>
  );
}

async function bootstrap(): Promise<void> {
  // 1. Resolve runtime config from Dataverse Environment Variables.
  const config = await resolveRuntimeConfig();
  setRuntimeConfig(config);

  // 2. Eagerly initialize MSAL auth BEFORE rendering — see module header.
  //    Non-fatal on failure: rendering still proceeds so the error surfaces
  //    inline (ConversationWorkspace's own error state) rather than blanking
  //    the whole page; a retry happens naturally on the next authenticatedFetch
  //    call (createCodePageAuthInitializer nulls out its promise on failure).
  try {
    await ensureAuthInitialized();

    // Backfill tenantId from the authenticated MSAL account if the env var /
    // Xrm fallback returned empty (mirrors LegalWorkspace's main.tsx).
    if (!config.tenantId) {
      const tenantId = await getAuthProvider().getTenantId();
      if (tenantId) {
        setRuntimeConfig({ ...config, tenantId });
      }
    }
  } catch (err) {
    console.warn('[CommunicationConversationPage] Auth bootstrap failed:', err);
  }

  // 3. Render.
  const rootElement = document.getElementById('root');
  if (!rootElement) {
    console.error('[CommunicationConversationPage] Root element not found');
    return;
  }

  createRoot(rootElement).render(
    <React.StrictMode>
      <Root />
    </React.StrictMode>
  );
}

bootstrap().catch(err => {
  console.error('[CommunicationConversationPage] Bootstrap failed:', err);

  const rootElement = document.getElementById('root');
  if (rootElement) {
    rootElement.innerHTML = `
      <div style="padding: 40px; text-align: center; font-family: 'Segoe UI', sans-serif;">
        <h2>Configuration Error</h2>
        <p>Unable to load the conversation page from Dataverse environment variables.</p>
        <p style="color: #616161; font-size: 12px;">${err instanceof Error ? err.message : String(err)}</p>
      </div>
    `;
  }
});
