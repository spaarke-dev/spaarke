import React from 'react';
import { createRoot, Root } from 'react-dom/client';
import { App } from '@shared/taskpane';
import { OutlookAdapter } from '@shared/adapters/OutlookAdapter';
import { authService, apiClient } from '@shared/services';

// Version information - synced with manifest version
const APP_VERSION = '1.0.6';
const BUILD_DATE = process.env.BUILD_DATE || 'Jan 23, 2026';

// Configuration from environment or build-time injection
const CONFIG = {
  clientId: process.env.ADDIN_CLIENT_ID || '',
  tenantId: process.env.TENANT_ID || 'a221a95e-6abc-4434-aecc-e48338a1b2f2',
  bffApiClientId: process.env.BFF_API_CLIENT_ID || '1e40baad-e065-4aea-a8d4-4b7ab273458c',
  bffApiBaseUrl: process.env.BFF_API_BASE_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net',
};

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
      <h2 style="color: #d13438; margin: 0 0 16px 0; font-size: 18px;">Initialization Error</h2>
      <p style="color: #323130; margin: 0 0 12px 0; font-size: 14px;">
        The add-in failed to initialize at stage: <strong>${stage}</strong>
      </p>
      <div style="background: #fdf3f4; border: 1px solid #d13438; border-radius: 4px; padding: 12px; margin-bottom: 16px;">
        <code style="color: #d13438; font-size: 12px; word-break: break-word;">${errorMessage}</code>
      </div>
      <details style="margin-top: 16px;">
        <summary style="cursor: pointer; color: #0078d4; font-size: 14px;">Debug Info</summary>
        <pre style="background: #f3f2f1; padding: 12px; border-radius: 4px; font-size: 11px; overflow: auto; margin-top: 8px;">
Version: ${APP_VERSION}
Build: ${BUILD_DATE}
Client ID: ${CONFIG.clientId ? CONFIG.clientId.substring(0, 8) + '...' : 'NOT SET'}
Tenant ID: ${CONFIG.tenantId ? CONFIG.tenantId.substring(0, 8) + '...' : 'NOT SET'}
BFF API: ${CONFIG.bffApiBaseUrl}
Stage: ${stage}
        </pre>
      </details>
      <button onclick="location.reload()" style="margin-top: 16px; padding: 8px 16px; background: #0078d4; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 14px;">
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

      Office.onReady((info) => {
        clearTimeout(timeout);
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
    reactRoot.render(
      <React.StrictMode>
        <App
          hostAdapter={hostAdapter}
          title="Spaarke for Outlook"
          version={APP_VERSION}
          buildDate={BUILD_DATE}
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
init().catch((error) => {
  console.error('[Spaarke] Initialization failed:', error);
  // Error already rendered by renderError() in init stages
});
