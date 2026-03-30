/**
 * DocumentRelationshipViewer — React 19 Code Page Entry Point
 *
 * Opened as HTML web resource dialog via:
 *   Xrm.Navigation.navigateTo(
 *     { pageType: "webresource", webresourceName: "sprk_documentrelationshipviewer", data: "documentId=...&tenantId=..." },
 *     { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
 *   )
 *
 * When embedded on a form (no data params), resolves documentId from Xrm form
 * context and tenantId from @spaarke/auth after MSAL initialization.
 *
 * Runtime configuration:
 *   BFF API URL, MSAL Client ID, and OAuth scope are resolved at runtime from
 *   Dataverse Environment Variables via resolveRuntimeConfig() from @spaarke/auth.
 *   No build-time .env.production values are used for these settings.
 */

import { createRoot } from 'react-dom/client';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { resolveCodePageTheme, setupCodePageThemeListener } from '@spaarke/ui-components';
import { resolveRuntimeConfig } from '@spaarke/auth';
import { App } from './App';
import { initializeAuth, getAuthProvider } from './services/authInit';

// Dataverse navigateTo({ pageType: "webresource", data: "k=v&k2=v2" }) wraps
// the caller's data string inside a single `?data=encodedString` query param.
// We unwrap it here so App.tsx always sees flat params (documentId, tenantId, etc.)
const urlParams = new URLSearchParams(window.location.search);
const dataEnvelope = urlParams.get('data');
const params = dataEnvelope ? new URLSearchParams(decodeURIComponent(dataEnvelope)) : urlParams;

// When embedded on a form (no URL params), resolve from Xrm form context
/* eslint-disable @typescript-eslint/no-explicit-any */
if (!params.get('documentId')) {
  try {
    const xrm = (window.parent as any)?.Xrm ?? (window as any)?.Xrm;
    if (xrm) {
      const formContext = xrm.Page;
      const entityId = formContext?.data?.entity?.getId?.()?.replace(/[{}]/g, '');
      if (entityId) params.set('documentId', entityId);
    }
  } catch {
    /* cross-origin or unavailable */
  }
}
/* eslint-enable @typescript-eslint/no-explicit-any */

// Resolve theme using shared utility (localStorage → URL → navbar → light default)
// OS prefers-color-scheme is intentionally NOT consulted (ADR-021)
let currentTheme = resolveCodePageTheme();

const container = document.getElementById('root');
if (!container) throw new Error('[DocumentRelationshipViewer] Root container #root not found in DOM.');

const root = createRoot(container);

/**
 * Bootstrap sequence:
 *   1. Resolve runtime config from Dataverse Environment Variables (BFF URL, MSAL Client ID, OAuth scope)
 *   2. Initialize MSAL auth with resolved config
 *   3. Resolve tenantId from auth if not in URL params
 *   4. Render App with resolved apiBaseUrl
 */
async function bootstrap(): Promise<void> {
  // 1. Resolve runtime config (BFF URL, MSAL client ID, OAuth scope) from Dataverse
  const runtimeConfig = await resolveRuntimeConfig();

  // Set window globals so that @spaarke/auth resolveConfig() fallback finds the
  // correct client ID. This is required because App.tsx calls initializeAuth()
  // without explicit params — the window global ensures it gets the right clientId.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).__SPAARKE_MSAL_CLIENT_ID__ = runtimeConfig.msalClientId;

  // 2. Initialize auth with runtime-resolved config
  await initializeAuth({
    clientId: runtimeConfig.msalClientId,
    bffApiScope: runtimeConfig.bffOAuthScope,
    bffBaseUrl: runtimeConfig.bffBaseUrl,
  });

  // 3. Resolve tenantId from auth if not in URL params
  if (!params.get('tenantId')) {
    try {
      const tenantId = await getAuthProvider().getTenantId();
      if (tenantId) params.set('tenantId', tenantId);
    } catch (err) {
      console.warn('[DocumentRelationshipViewer] Could not resolve tenantId from auth:', err);
    }
  }

  // 4. Render with runtime-resolved BFF URL
  root.render(
    <FluentProvider theme={currentTheme}>
      <App params={params} isDark={currentTheme === webDarkTheme} apiBaseUrl={runtimeConfig.bffBaseUrl} />
    </FluentProvider>
  );
}

bootstrap();
