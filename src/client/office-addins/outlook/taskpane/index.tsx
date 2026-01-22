import React from 'react';
import { createRoot } from 'react-dom/client';
import { App } from '@shared/taskpane';
import { OutlookHostAdapter } from '../OutlookHostAdapter';
import { authService, apiClient } from '@shared/services';

// Version information - synced with manifest version
const APP_VERSION = '1.0.1';
const BUILD_DATE = process.env.BUILD_DATE || 'Jan 22, 2026';

// Configuration from environment or build-time injection
const CONFIG = {
  clientId: process.env.ADDIN_CLIENT_ID || '',
  tenantId: process.env.TENANT_ID || 'a221a95e-6abc-4434-aecc-e48338a1b2f2',
  bffApiClientId: process.env.BFF_API_CLIENT_ID || '1e40baad-e065-4aea-a8d4-4b7ab273458c',
  bffApiBaseUrl: process.env.BFF_API_BASE_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net',
};

/**
 * Outlook taskpane entry point.
 *
 * Initializes the Outlook host adapter and renders the shared App component.
 */
async function init() {
  // Wait for Office.js to be ready
  await new Promise<void>((resolve) => {
    Office.onReady(() => resolve());
  });

  // Initialize auth service
  await authService.initialize({
    clientId: CONFIG.clientId,
    tenantId: CONFIG.tenantId,
    bffApiClientId: CONFIG.bffApiClientId,
  });

  // Configure API client
  apiClient.configure({
    baseUrl: CONFIG.bffApiBaseUrl,
    bffApiClientId: CONFIG.bffApiClientId,
  });

  // Create host adapter
  const hostAdapter = new OutlookHostAdapter();

  // Render app
  const container = document.getElementById('root');
  if (container) {
    const root = createRoot(container);
    root.render(
      <React.StrictMode>
        <App
          hostAdapter={hostAdapter}
          title="Spaarke for Outlook"
          version={APP_VERSION}
          buildDate={BUILD_DATE}
        />
      </React.StrictMode>
    );
  }
}

// Start initialization
init().catch((error) => {
  console.error('Failed to initialize Outlook add-in:', error);
});
