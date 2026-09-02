import React from 'react';
import { createRoot, Root } from 'react-dom/client';
import type { AccountInfo } from '@azure/msal-browser';
import { App } from '@shared/taskpane';
import type { SavedTodoContext } from '@shared/taskpane/components/views/CreateTodoView';
import { OutlookAdapter } from '@shared/adapters/OutlookAdapter';
import { authService, apiClient } from '@shared/services';

// Version information - synced with outlook/manifest.json's "version" field
const APP_VERSION = '1.0.20';
// Task 040 / FR-B0: fallback (used only outside webpack, e.g. non-build test
// contexts) was a stale hardcoded date; webpack's DefinePlugin always injects
// the real build date, so this fallback should never be user-visible.
const BUILD_DATE = process.env.BUILD_DATE || 'unknown';

// Configuration from environment or build-time injection
const CONFIG = {
  clientId: process.env.ADDIN_CLIENT_ID || '',
  tenantId: process.env.TENANT_ID || 'a221a95e-6abc-4434-aecc-e48338a1b2f2',
  bffApiClientId: process.env.BFF_API_CLIENT_ID || '1e40baad-e065-4aea-a8d4-4b7ab273458c',
  bffApiBaseUrl: process.env.BFF_API_BASE_URL || 'https://spaarke-bff-dev.azurewebsites.net',
  // SmartTodo Code Page URL (smart-todo-decoupling-r3 FR-27 / task 070).
  // When set, the "Create To Do" ribbon action opens the wizard from this URL
  // with the launch context query params. Empty string disables the action.
  smartTodoCodePageUrl: process.env.SMARTTODO_CODEPAGE_URL || '',
  // OfficeNaaStrategy legacy-client fallback popup redirect (task 072 / FR-25).
  // Empty string lets AuthService fall back to `${origin}/auth-callback.html`.
  fallbackRedirectUri: process.env.FALLBACK_REDIRECT_URI || '',
};

/**
 * Read the `?action=` query param from the taskpane URL. Used by the Outlook
 * "Create To Do" ribbon (smart-todo-decoupling-r3 FR-27 / task 070): the manifest
 * button opens the taskpane with `?action=createTodo` which routes the App
 * to render CreateTodoView instead of the default tabs.
 */
function readInitialAction(): 'createTodo' | undefined {
  try {
    const params = new URLSearchParams(window.location.search);
    const action = params.get('action');
    return action === 'createTodo' ? 'createTodo' : undefined;
  } catch {
    return undefined;
  }
}

/**
 * In the browser test harness (webpack dev mode) supply a demo "filed" context so
 * the inline Create To Do form is fully interactive without a real save — the
 * `demo-` communication id routes the create-To-Do call to a mocked success in App.
 *
 * In production this returns undefined until the SaveView → regarding wiring lands;
 * until then the Create To Do tab shows a "file this email first" prompt (correct
 * behavior — a To Do needs the record the email is filed to as its regarding).
 *
 * `regardingEntity` is the FRIENDLY type ("Matter"/"Project"/"Invoice") the BFF
 * `POST /api/office/todo` expects; App also maps the Dataverse logical name
 * defensively, so the future Save-flow wiring may pass either form.
 */
function buildDemoSavedContext(): SavedTodoContext | undefined {
  if (process.env.NODE_ENV !== 'development') {
    return undefined;
  }
  return {
    communicationId: 'demo-communication-0001',
    regardingEntity: 'Matter',
    regardingRecordId: '00000000-0000-0000-0000-000000000001',
    regardingName: 'Acme Corp — NDA (demo)',
  };
}

/**
 * True inside the browser test harness (`taskpane-test.html` sets the flag before the
 * bundle loads). Never true in a deployed build.
 */
function isBrowserTestMode(): boolean {
  try {
    return (window as unknown as { __SPAARKE_TEST_MODE__?: boolean }).__SPAARKE_TEST_MODE__ === true;
  } catch {
    return false;
  }
}

/**
 * Browser test harness only: replace the auth service methods with mocks so the taskpane
 * renders the authenticated app (Save + Create To Do tabs) without a real Entra sign-in.
 * The deployed build never calls this — the `.env` placeholder tenant/client would 401
 * against Entra otherwise, which is exactly what a UX harness must not require.
 */
function installMockAuth(): void {
  const mockAccount: AccountInfo = {
    homeAccountId: 'test-home-account',
    environment: 'login.microsoftonline.com',
    tenantId: 'test-tenant',
    username: 'test.user@spaarke.com',
    localAccountId: 'test-local-account',
    name: 'Test User',
  };
  const mutableAuth = authService as unknown as {
    isAuthenticated: () => boolean;
    getAccount: () => AccountInfo | null;
    getAccessToken: (scopes?: string[]) => Promise<string | null>;
    signIn: () => Promise<void>;
    signOut: () => Promise<void>;
  };
  mutableAuth.isAuthenticated = () => true;
  mutableAuth.getAccount = () => mockAccount;
  mutableAuth.getAccessToken = async () => 'mock-access-token';
  mutableAuth.signIn = async () => {};
  mutableAuth.signOut = async () => {};
}

// Global root for error rendering
let reactRoot: Root | null = null;

/**
 * Render an error message in the taskpane when initialization fails.
 */
function renderError(error: Error | string, stage: string) {
  const container = document.getElementById('root');
  if (!container) return;

  const errorMessage = error instanceof Error ? error.message : String(error);

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
 * Outlook taskpane entry point.
 *
 * Initializes the Outlook host adapter and renders the shared App component.
 */
async function init() {
  console.log('[Spaarke] Starting initialization...');
  console.log('[Spaarke] Config:', {
    clientId: CONFIG.clientId ? CONFIG.clientId.substring(0, 8) + '...' : 'NOT SET',
    tenantId: CONFIG.tenantId ? CONFIG.tenantId.substring(0, 8) + '...' : 'NOT SET',
    bffApiBaseUrl: CONFIG.bffApiBaseUrl,
  });

  // Stage 1: Wait for Office.js to be ready
  console.log('[Spaarke] Stage 1: Waiting for Office.js...');
  try {
    await new Promise<void>((resolve, reject) => {
      const timeout = setTimeout(() => {
        reject(new Error('Office.js initialization timeout (10s)'));
      }, 10000);

      Office.onReady(info => {
        clearTimeout(timeout);
        console.log('[Spaarke] Office.js ready:', info);
        resolve();
      });
    });
  } catch (error) {
    renderError(error as Error, 'Office.js initialization');
    throw error;
  }

  // Stage 2: Initialize auth service (mocked in the browser test harness).
  if (isBrowserTestMode()) {
    console.log('[Spaarke] Browser test mode — mocking auth (no real Entra sign-in).');
    installMockAuth();
  } else {
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

  // Stage 4: Create host adapter
  console.log('[Spaarke] Stage 4: Creating host adapter...');
  let hostAdapter: OutlookAdapter;
  try {
    hostAdapter = new OutlookAdapter();
    // Initialize the adapter (connects to Office.js)
    await hostAdapter.initialize();
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
    const initialAction = readInitialAction();
    const initialSavedContext = buildDemoSavedContext();
    reactRoot.render(
      <React.StrictMode>
        <App
          hostAdapter={hostAdapter}
          title="Spaarke for Outlook"
          version={APP_VERSION}
          buildDate={BUILD_DATE}
          {...(initialAction ? { initialAction } : {})}
          {...(initialSavedContext ? { initialSavedContext } : {})}
        />
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
