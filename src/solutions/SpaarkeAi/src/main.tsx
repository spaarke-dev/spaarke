/**
 * SpaarkeAi Code Page — entry point.
 *
 * Bootstrap sequence:
 *
 *   A. XRM PATH (in-MDA navigateTo or MDA top-frame with Xrm available):
 *      1. resolveRuntimeConfig() — reads Dataverse env vars via Xrm (or localStorage cache)
 *      2. setRuntimeConfig() — stores config in singleton + window globals
 *      3. ensureAuthInitialized() — MSAL silent+popup chain (never redirect in iframe)
 *      4. Render <App />
 *
 *   B. DIRECT URL PATH (top-frame, no Xrm, no localStorage cache — first visit):
 *      1. resolveRuntimeConfig() throws → catch
 *      2. Detect top-frame (window.self === window.top)
 *      3. Fetch config from BFF GET /api/config/client (anonymous)
 *      4. setRuntimeConfig() — stores fetched config
 *      5. handleRedirectPromise() — process MSAL redirect response (post-login return)
 *         - If result has account → MSAL is authenticated; proceed to render
 *         - If no result + no cached account → call loginRedirect() → browser navigates to Azure AD
 *      6. ensureAuthInitialized() — warm the MSAL token cache
 *      7. Render <App />
 *
 * Key rules (from auth.md constraints):
 *   - MUST NOT use redirect flow inside an iframe (breaks the parent frame)
 *   - MUST use redirect in top-frame (popup is unreliable for direct URL bookmarks)
 *   - MUST handle MSAL redirect promise on every page load (post-login return)
 *   - MUST store MSAL cache in localStorage (survives tab/browser close)
 *   - BFF /api/config/client is anonymous — no auth token required
 *
 * Web resource: sprk_spaarkeai
 *
 * @see ADR-006 - Code Pages for standalone dialogs (not PCF)
 * @see ADR-021 - Fluent v9, dark mode required
 * @see ADR-022 - Code Pages bundle their own React 19 (not platform-provided)
 * @see ADR-026 - Vite + vite-plugin-singlefile for Code Pages
 * @see src/solutions/LegalWorkspace/src/main.tsx — canonical bootstrap pattern
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import {
  resolveRuntimeConfig,
  getAuthProvider,
  buildBffApiUrl,
} from "@spaarke/auth";
import { setRuntimeConfig } from "./config/runtimeConfig";
import { ensureAuthInitialized } from "./services/authInit";
import { App } from "./App";
import type { IRuntimeConfig } from "@spaarke/auth";

// ---------------------------------------------------------------------------
// BFF base URL baked in at build time via Vite env var (AIPU-091).
// Used ONLY when resolveRuntimeConfig() throws (no Xrm, no localStorage cache).
// This is not a secret — it is the public HTTPS endpoint of the BFF API.
// Override for non-dev environments: VITE_BFF_BASE_URL=<url> npm run build
// ---------------------------------------------------------------------------
const FALLBACK_BFF_BASE_URL: string =
  import.meta.env.VITE_BFF_BASE_URL ?? "https://spe-api-dev-67e2xz.azurewebsites.net";

// ---------------------------------------------------------------------------
// Client config response shape from GET /api/config/client
// ---------------------------------------------------------------------------
interface IClientConfigResponse {
  bffBaseUrl: string;
  msalClientId: string;
  msalAuthority: string;
  msalScopes: string[];
  tenantId: string;
}

/**
 * Fetch client configuration from the BFF anonymous config endpoint.
 * Used when Xrm is unavailable and localStorage cache is empty (first direct URL visit).
 */
async function fetchClientConfig(): Promise<IRuntimeConfig> {
  const configUrl = buildBffApiUrl(FALLBACK_BFF_BASE_URL, "/config/client");
  console.info("[SpaarkeAi] Fetching client config from BFF:", configUrl);

  const response = await fetch(configUrl, {
    method: "GET",
    headers: { Accept: "application/json" },
    // No credentials — this is an anonymous endpoint, no cookies needed
  });

  if (!response.ok) {
    throw new Error(
      `[SpaarkeAi] Failed to fetch client config: ${response.status} ${response.statusText}`
    );
  }

  const data: IClientConfigResponse = await response.json();

  // Map BFF response to IRuntimeConfig shape
  const config: IRuntimeConfig = {
    bffBaseUrl: data.bffBaseUrl || FALLBACK_BFF_BASE_URL,
    msalClientId: data.msalClientId,
    // bffOAuthScope: first scope from the response
    bffOAuthScope: data.msalScopes?.[0] ?? `api://${data.msalClientId}/user_impersonation`,
    tenantId: data.tenantId ?? "",
  };

  return config;
}

/**
 * Detect whether this page is running in the top-level browser frame.
 *
 * Returns true when:
 *   - Opened via direct URL (bookmark, email link, M365 handoff)
 *   - Opened via navigateTo with target:1 (full-page replacement)
 *
 * Returns false when:
 *   - Opened via navigateTo with target:2 (dialog/panel) — nested iframe
 *   - Embedded in a Dataverse form (nested iframe)
 *
 * WHY THIS MATTERS:
 *   In top-frame: use MSAL loginRedirect (standard SPA pattern, navigates away and back)
 *   In iframe:    use MSAL loginPopup (never redirect — would break the parent MDA frame)
 */
function isTopFrame(): boolean {
  try {
    return window.self === window.top;
  } catch {
    // Cross-origin frame check failed — assume nested iframe (safe fallback)
    return false;
  }
}

/**
 * Handle MSAL redirect flow for top-frame (direct URL) access.
 *
 * On every page load, call handleRedirectPromise() to process any pending
 * redirect response from Azure AD (e.g., user returning from login page).
 *
 * If no redirect result and no cached MSAL account → trigger loginRedirect.
 * The browser will navigate to Azure AD, authenticate, and return to this URL.
 *
 * Returns true if the page should continue to render (authenticated or redirect handled).
 * Returns false if loginRedirect was called (page will navigate away — do not render).
 */
async function handleTopFrameAuth(config: IRuntimeConfig): Promise<boolean> {
  const { PublicClientApplication } = await import("@azure/msal-browser");

  const msalConfig = {
    auth: {
      clientId: config.msalClientId,
      authority: `https://login.microsoftonline.com/${config.tenantId || "organizations"}`,
      // Redirect URI = current URL (without query params — for consistency)
      redirectUri: `${window.location.protocol}//${window.location.host}${window.location.pathname}`,
    },
    cache: {
      // localStorage per auth constraint MUST rule — survives tab/browser close
      cacheLocation: "localStorage" as const,
      storeAuthStateInCookie: true,
    },
    system: {
      loggerOptions: {
        logLevel: 3, // Warning only
        piiLoggingEnabled: false,
      },
    },
  };

  const msalInstance = new PublicClientApplication(msalConfig);
  await msalInstance.initialize();

  // Step 1: Process any pending redirect response (post-login return from Azure AD)
  const redirectResult = await msalInstance.handleRedirectPromise();
  if (redirectResult?.account) {
    console.info(
      "[SpaarkeAi] MSAL redirect response processed — authenticated as:",
      redirectResult.account.username
    );
    // Authenticated — continue to render
    return true;
  }

  // Step 2: Check for cached account (returning user with valid session)
  const accounts = msalInstance.getAllAccounts();
  if (accounts.length > 0) {
    console.info(
      "[SpaarkeAi] MSAL cached account found:",
      accounts[0].username
    );
    // Cached session — continue to render (ensureAuthInitialized will acquire token)
    return true;
  }

  // Step 3: No auth, no cache — initiate redirect to Azure AD login
  // Page will navigate away; on return, handleRedirectPromise() above will catch it.
  console.info("[SpaarkeAi] No MSAL account — initiating loginRedirect...");
  const scope = config.bffOAuthScope;
  await msalInstance.loginRedirect({
    scopes: [scope],
    // prompt: "select_account" can be added for multi-account UX
  });

  // loginRedirect navigates the page away — this return value is never reached.
  // Return false to signal "do not render" to the caller.
  return false;
}

async function bootstrap(): Promise<void> {
  const t0 = performance.now();

  // -------------------------------------------------------------------------
  // 1. Resolve runtime config
  //    Primary: resolveRuntimeConfig() — reads from Xrm env vars or localStorage
  //    Fallback: fetch from BFF /api/config/client (direct URL, no Xrm, no cache)
  // -------------------------------------------------------------------------
  let config: IRuntimeConfig;

  try {
    // Xrm path (in-MDA launch) OR localStorage cache (repeat direct URL visit)
    config = await resolveRuntimeConfig();
  } catch (xrmErr) {
    // Xrm not available AND no localStorage cache — first direct URL visit
    console.warn(
      "[SpaarkeAi] resolveRuntimeConfig() failed (Xrm unavailable, no localStorage cache):",
      xrmErr instanceof Error ? xrmErr.message : String(xrmErr)
    );

    if (!isTopFrame()) {
      // We're in an iframe without Xrm and without localStorage cache.
      // This is unusual — navigateTo normally runs within a frame where Xrm
      // is available via frame-walk, or the user has a localStorage cache.
      // Re-throw to show the error UI; popup auth can't recover config.
      throw new Error(
        "[SpaarkeAi] Running in an iframe without Xrm context or cached config. " +
          "Open SpaarkeAi from within the Dataverse Model-Driven App to initialize the configuration cache."
      );
    }

    // Top-frame: fetch config from BFF anonymous endpoint
    config = await fetchClientConfig();
  }

  setRuntimeConfig(config);

  // -------------------------------------------------------------------------
  // 2. Auth initialization — mode depends on frame context
  // -------------------------------------------------------------------------
  const inTopFrame = isTopFrame();

  if (inTopFrame && !_hasXrmContext()) {
    // Direct URL path: top-frame without Xrm context.
    // Use MSAL redirect flow — acquireTokenSilent first, loginRedirect fallback.
    // This must run BEFORE rendering to prevent the app from rendering
    // unauthenticated and then immediately navigating away.
    const shouldRender = await handleTopFrameAuth(config);
    if (!shouldRender) {
      // loginRedirect was called — page will navigate away. Stop here.
      return;
    }
  }

  // Standard auth init (Xrm path OR top-frame after redirect return with cached account)
  // ensureAuthInitialized uses the silent+popup chain from SpaarkeAuthProvider.
  try {
    await ensureAuthInitialized();

    // Patch the stored config if tenant ID was empty at resolveRuntimeConfig() time.
    // MSAL account.tenantId (from the authenticated JWT) is the authoritative source.
    if (!config.tenantId) {
      const tenantId = await getAuthProvider().getTenantId();
      if (tenantId) {
        setRuntimeConfig({ ...config, tenantId });
        console.info(
          "[SpaarkeAi] Tenant ID resolved from MSAL account:",
          tenantId.substring(0, 8) + "..."
        );
      }
    }
  } catch (err) {
    // Auth init failure is non-fatal for rendering — authenticated API calls will
    // fail individually and surface errors in the relevant components.
    console.warn("[SpaarkeAi] Eager auth init failed, will retry on first use:", err);
  }

  // -------------------------------------------------------------------------
  // 3. Parse URL parameters for entity context.
  //
  //    Dataverse passes data to web resources via two mechanisms:
  //      a) Direct query params: ?entityType=sprk_matter&entityId=<guid>&matterId=<guid>
  //      b) Encoded data param: ?data=entityType%3Dsprk_matter%26entityId%3D<guid>
  //
  //    useEntityResolver (inside StandaloneAiProvider) also resolves entity context
  //    via Xrm frame-walk as a fallback, so URL params are supplementary hints.
  // -------------------------------------------------------------------------
  const searchParams = new URLSearchParams(window.location.search);

  // Also decode the Dataverse `data` parameter (URL-encoded key=value pairs)
  const dataParam = searchParams.get("data");
  const dataParams = new URLSearchParams(
    dataParam ? decodeURIComponent(dataParam) : ""
  );

  const entityLogicalName =
    searchParams.get("entityType") ??
    dataParams.get("entityType") ??
    undefined;

  const entityId =
    searchParams.get("entityId") ??
    dataParams.get("entityId") ??
    undefined;

  const matterId =
    searchParams.get("matterId") ??
    dataParams.get("matterId") ??
    undefined;

  // -------------------------------------------------------------------------
  // 4. Render the app
  // -------------------------------------------------------------------------
  const rootElement = document.getElementById("root");
  if (!rootElement) {
    console.error("[SpaarkeAi] Root element not found");
    return;
  }

  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App
        entityLogicalName={entityLogicalName}
        entityId={entityId}
        matterId={matterId}
      />
    </React.StrictMode>
  );

  const bootstrapMs = Math.round(performance.now() - t0);
  console.info(`[SpaarkeAi] Bootstrap complete in ${bootstrapMs}ms`);
}

/**
 * Check if Xrm context is available in the current frame or parent frames.
 * Used to distinguish "top-frame with Xrm" (MDA sitemap page) from
 * "top-frame without Xrm" (direct URL bookmark).
 */
function _hasXrmContext(): boolean {
  const frames: Window[] = [window];
  try {
    if (window.parent && window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window) frames.push(window.top);
  } catch {
    /* cross-origin */
  }

  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (frame as any).Xrm;
      if (xrm?.Utility?.getGlobalContext?.()) return true;
    } catch {
      /* cross-origin */
    }
  }

  return false;
}

bootstrap().catch((err) => {
  console.error("[SpaarkeAi] Bootstrap failed:", err);

  // Show a user-friendly error in the DOM
  const rootElement = document.getElementById("root");
  if (rootElement) {
    rootElement.innerHTML = `
      <div style="padding: 40px; text-align: center; font-family: 'Segoe UI', sans-serif;">
        <h2>Configuration Error</h2>
        <p>Unable to load SpaarkeAi configuration from Dataverse environment variables.</p>
        <p style="color: var(--colorNeutralForeground3); font-size: 12px;">${err instanceof Error ? err.message : String(err)}</p>
      </div>
    `;
  }
});
