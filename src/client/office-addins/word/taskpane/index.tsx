import React from 'react';
import { createRoot, Root } from 'react-dom/client';
import { App } from '@shared/taskpane';
import { HostAdapterFactory, isHostAdapterError } from '@shared/adapters';
import type { IHostAdapter } from '@shared/adapters';
import { WordAdapter } from '@shared/adapters/WordAdapter';
import { authService, apiClient } from '@shared/services';

// Version information - synced with word/manifest.json's "version" field
// (task 011 / FR-05: the unified JSON manifest is now the versioning source
// of truth, mirroring outlook/taskpane/index.tsx's convention; the retained
// word-manifest.xml's 4-part <Version> is kept in step but is not this
// constant's source — XML requires 4-part, the unified manifest requires
// SemVer-style 1-3 part).
const APP_VERSION = '1.0.8';
const BUILD_DATE = process.env.BUILD_DATE || 'unknown';

// Configuration from environment or build-time injection
const CONFIG = {
  clientId: process.env.ADDIN_CLIENT_ID || '',
  tenantId: process.env.TENANT_ID || 'a221a95e-6abc-4434-aecc-e48338a1b2f2',
  bffApiClientId: process.env.BFF_API_CLIENT_ID || '1e40baad-e065-4aea-a8d4-4b7ab273458c',
  bffApiBaseUrl: process.env.BFF_API_BASE_URL || 'https://spaarke-bff-dev.azurewebsites.net',
  // OfficeNaaStrategy legacy-client fallback popup redirect (task 072 / FR-25).
  // Empty string lets AuthService fall back to `${origin}/auth-callback.html`.
  fallbackRedirectUri: process.env.FALLBACK_REDIRECT_URI || '',
};

// Global root for error rendering
let reactRoot: Root | null = null;

/**
 * Render an error message in the taskpane when initialization fails.
 */
function renderError(error: Error | string, stage: string) {
  const container = document.getElementById('root');
  if (!container) return;

  // Task 010 / FR-04: Stage 4 can now reject with a typed `HostAdapterError` — a PLAIN OBJECT
  // `{ code, message }`, not an Error. Without this branch every factory failure (INVALID_HOST,
  // API_NOT_AVAILABLE, unregistered host) rendered as the literal string "[object Object]", i.e. the
  // one failure this change introduces would have been the one nobody could diagnose from the pane.
  // (code-review W-1, 2026-09-09.)
  const errorMessage = isHostAdapterError(error)
    ? `${error.code}: ${error.message}`
    : error instanceof Error
      ? error.message
      : String(error);

  container.innerHTML = `
    <div style="padding: 20px; font-family: 'Segoe UI', sans-serif; height: 100%; box-sizing: border-box;">
      <h2 style="color: var(--colorPaletteRedForeground1); margin: 0 0 16px 0; font-size: 18px;">Initialization Error</h2>
      <p style="color: var(--colorNeutralForeground1); margin: 0 0 12px 0; font-size: 14px;">
        The add-in failed to initialize at stage: <strong>${stage}</strong>
      </p>
      <div style="background: var(--colorPaletteRedBackground1); border: 1px solid var(--colorPaletteRedForeground1); border-radius: 4px; padding: 12px; margin-bottom: 16px;">
        <code style="color: var(--colorPaletteRedForeground1); font-size: 12px; word-break: break-word;">${errorMessage}</code>
      </div>
      <details style="margin-top: 16px;">
        <summary style="cursor: pointer; color: var(--colorBrandForeground1); font-size: 14px;">Debug Info</summary>
        <pre style="background: var(--colorNeutralBackground3); padding: 12px; border-radius: 4px; font-size: 11px; overflow: auto; margin-top: 8px;">
Version: ${APP_VERSION}
Build: ${BUILD_DATE}
Client ID: ${CONFIG.clientId ? CONFIG.clientId.substring(0, 8) + '...' : 'NOT SET'}
Tenant ID: ${CONFIG.tenantId ? CONFIG.tenantId.substring(0, 8) + '...' : 'NOT SET'}
BFF API: ${CONFIG.bffApiBaseUrl}
Stage: ${stage}
        </pre>
      </details>
      <button onclick="location.reload()" style="margin-top: 16px; padding: 8px 16px; background: var(--colorBrandBackground); color: var(--colorNeutralForegroundOnBrand); border: none; border-radius: 4px; cursor: pointer; font-size: 14px;">
        Retry
      </button>
    </div>
  `;
}

/**
 * Word taskpane entry point.
 *
 * Initializes the Word host adapter and renders the shared App component.
 */
async function init() {
  console.log('[Spaarke] Starting initialization...');
  console.log('[Spaarke] Config:', {
    clientId: CONFIG.clientId ? CONFIG.clientId.substring(0, 8) + '...' : 'NOT SET',
    tenantId: CONFIG.tenantId ? CONFIG.tenantId.substring(0, 8) + '...' : 'NOT SET',
    bffApiBaseUrl: CONFIG.bffApiBaseUrl,
  });

  // Stage 1: Wait for Office.js to be ready.
  // Task 010 option B (operator decision 2026-09-09): capture the host Office hands us HERE and
  // pass it to the factory at Stage 4. `Office.onReady`'s info.host is populated by the host itself
  // at ready time; `Office.context.host` — which the factory's detectHostType() reads when given no
  // argument — is unpopulated in some Outlook desktop builds. Depending on the latter when the
  // former is already in hand means a bootstrap with no fallback can throw INVALID_HOST and leave
  // the pane never rendering. This does not dodge detection: detectHostType() still runs whenever
  // no host is supplied, and the factory still rejects an unsupported host.
  let readyHost: Office.HostType | undefined;
  console.log('[Spaarke] Stage 1: Waiting for Office.js...');
  try {
    await new Promise<void>((resolve, reject) => {
      const timeout = setTimeout(() => {
        reject(new Error('Office.js initialization timeout (10s)'));
      }, 10000);

      Office.onReady(info => {
        clearTimeout(timeout);
        readyHost = info?.host ?? undefined;
        console.log('[Spaarke] Office.js ready:', info);
        resolve();
      });
    });
  } catch (error) {
    renderError(error as Error, 'Office.js initialization');
    throw error;
  }

  // Stage 2: Initialize auth service
  console.log('[Spaarke] Stage 2: Initializing auth service...');
  try {
    await authService.initialize({
      clientId: CONFIG.clientId,
      tenantId: CONFIG.tenantId,
      bffApiClientId: CONFIG.bffApiClientId,
      ...(CONFIG.fallbackRedirectUri ? { fallbackRedirectUri: CONFIG.fallbackRedirectUri } : {}),
    });
    console.log('[Spaarke] Auth service initialized');
  } catch (error) {
    renderError(error as Error, 'Auth service initialization');
    throw error;
  }

  // Stage 3: Configure API client
  console.log('[Spaarke] Stage 3: Configuring API client...');
  try {
    apiClient.configure({
      baseUrl: CONFIG.bffApiBaseUrl,
      bffApiClientId: CONFIG.bffApiClientId,
    });
    console.log('[Spaarke] API client configured');
  } catch (error) {
    renderError(error as Error, 'API client configuration');
    throw error;
  }

  // Stage 4: Create host adapter via the factory (task 010 / FR-04).
  //
  // The adapter is no longer `new`ed here. `HostAdapterFactory` was dead infrastructure —
  // `registerAdapter()` had zero call sites, so the registry was empty and `create()` always threw
  // INVALID_HOST while both task panes bypassed it. Registration MUST happen before any
  // `create()`/`createAndInitialize()` call in this entry point.
  //
  // There is now exactly ONE Word adapter: `shared/adapters/WordAdapter`, carrying the
  // `getFileAsync(Compressed)` .docx extraction that UAT proved correct on 2026-09-03. The duplicate
  // `word/WordHostAdapter.ts` is deleted.
  console.log('[Spaarke] Stage 4: Creating host adapter...');
  let hostAdapter: IHostAdapter;
  try {
    HostAdapterFactory.registerAdapter('word', WordAdapter);
    // Option B (operator decision 2026-09-09, task 010 escalation trigger 3): hand the factory the
    // host Office reported at Stage 1 rather than making it re-derive one from
    // `Office.context.host`. Both quality gates flagged the no-arg form independently: this is the
    // bootstrap path with NO fallback, so an unpopulated global means the pane never renders.
    // `readyHost` is undefined only if Office.onReady itself reported no host, in which case
    // detectHostType() still runs and a genuine detection failure still surfaces as a typed
    // INVALID_HOST / API_NOT_AVAILABLE rendered by the catch below.
    hostAdapter = await HostAdapterFactory.createAndInitialize(
      readyHost === Office.HostType.Word ? 'word' : undefined
    );
    console.log('[Spaarke] Host adapter created and initialized');
  } catch (error) {
    renderError(error as Error, 'Host adapter creation');
    throw error;
  }

  // Stage 5: Render React app
  console.log('[Spaarke] Stage 5: Rendering React app...');
  const container = document.getElementById('root');
  if (!container) {
    const error = new Error('Root container not found');
    renderError(error, 'React rendering');
    throw error;
  }

  try {
    reactRoot = createRoot(container);
    reactRoot.render(
      <React.StrictMode>
        <App hostAdapter={hostAdapter} title="Spaarke for Word" version={APP_VERSION} buildDate={BUILD_DATE} />
      </React.StrictMode>
    );
    console.log('[Spaarke] React app rendered successfully');
  } catch (error) {
    renderError(error as Error, 'React rendering');
    throw error;
  }
}

// Start initialization
init().catch(error => {
  console.error('[Spaarke] Initialization failed:', error);
  // Error already rendered by renderError() in init stages
});
