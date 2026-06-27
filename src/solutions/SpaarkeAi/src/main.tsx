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
// Round 4 Fix 4.1 (2026-05-21): SpaarkeAi embeds `LegalWorkspaceApp` as a
// workspace tab widget. LegalWorkspace has its OWN runtime-config singleton
// (separate from SpaarkeAi's) — when embedded, code paths in the LegalWorkspace
// tree call `getBffBaseUrl()` which reads from LegalWorkspace's singleton.
// Without this dual-init call, document preview and other actions inside the
// embedded tree throw "[LegalWorkspace] Runtime config not initialized."
//
// Both singletons receive the SAME `IRuntimeConfig` so they agree on bffBaseUrl
// / scope / clientId / tenantId. The two are distinct in-process instances by
// design — embedding does not collapse them; it just keeps both warm.
//
// R2 Option D (2026-06-18): replaces the R2 task 002 module-mutation slot.
// SpaarkeAi builds a CUSTOM section registry that injects its notification-
// context loader into the Daily Briefing widget, then registers a wrapper
// renderer that passes the registry to `LegalWorkspaceApp` via the new
// `sections` prop. See `notes/option-d-registry-as-composition.md`.
import {
  LegalWorkspaceApp,
  createLegalWorkspaceSectionRegistry,
  setLegalWorkspaceRuntimeConfig,
} from "@spaarke/legal-workspace";
// R2 task 002 (FR-02): loadSpaarkeAiNotificationContext queries `appnotification`
// via Xrm.WebApi and builds the populated NarrateRequest envelope (categories +
// priorityItems + channels) the BFF /narrate endpoint expects to produce real
// TL;DR + channel bullets. Mirrors the standalone Daily Briefing Code Page
// data path (see file header for cross-reference).
import { loadSpaarkeAiNotificationContext } from "./services/notificationContextLoader";
// R4 task 052 (C-4) + R2 Option D (2026-06-18): register a workspace renderer
// that wraps `LegalWorkspaceApp` with a SpaarkeAi-specific section registry.
// `WorkspaceLayoutWidget` in `@spaarke/ai-widgets` consults `setDefaultWorkspaceRenderer`
// at render time instead of importing `@spaarke/legal-workspace` directly.
// `WorkspaceRenderer` is the function signature contract the slot accepts.
import {
  setDefaultWorkspaceRenderer,
  AppErrorBoundary,
  AppInsightsService,
} from "@spaarke/ui-components";
import type { WorkspaceRenderer } from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// BFF base URL baked in at build time via Vite env var (AIPU-091).
// Used ONLY when resolveRuntimeConfig() throws (no Xrm, no localStorage cache).
// This is not a secret — it is the public HTTPS endpoint of the BFF API.
// Override for non-dev environments: VITE_BFF_BASE_URL=<url> npm run build
// ---------------------------------------------------------------------------
const FALLBACK_BFF_BASE_URL: string =
  import.meta.env.VITE_BFF_BASE_URL ?? "https://spaarke-bff-dev.azurewebsites.net";

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


// ---------------------------------------------------------------------------
// Task 105 (2026-05-22) — Suppress LegalWorkspace's `useDailyDigestAutoPopup`
// when LegalWorkspaceApp is embedded inside SpaarkeAi.
//
// Background:
//   LegalWorkspace's `WorkspaceGrid.tsx` unconditionally calls
//   `useDailyDigestAutoPopup({ webApi, userId })` on mount. That hook (see
//   `src/solutions/LegalWorkspace/src/hooks/useDailyDigestAutoPopup.ts`) opens
//   `sprk_dailyupdate` as a modal Code Page on first run per browser session,
//   guarded by `sessionStorage.getItem("spaarke_dailyDigestShown")`.
//
//   Inside SpaarkeAi, LegalWorkspaceApp is embedded as a workspace tab widget
//   (`@spaarke/ai-widgets/.../WorkspaceLayoutWidget.tsx`) whenever the user has
//   pinned a workspace — task 101's auto-open dispatches `widget_load` for
//   each pinned layout on cold load (`WorkspacePane.tsx` lines 401-457). The
//   first such embed runs the hook, the hook fires the modal, and the operator
//   sees a "Daily Briefing" popup automatically appearing inside SpaarkeAi.
//
//   Operator feedback (Round 8, 2026-05-22):
//     "Remove the Daily Briefing popup when the app loads."
//
// Fix:
//   Set the hook's sessionStorage sentinel BEFORE any React tree mounts. The
//   hook then short-circuits at its `if (sessionStorage.getItem(SESSION_KEY))
//   return;` guard (file referenced above, lines ~65-70).
//
// Scope:
//   - SpaarkeAi-only: standalone LegalWorkspace runs in a SEPARATE browser tab
//     with its OWN sessionStorage, so this write does not affect that app.
//   - LegalWorkspace source files untouched (FR-25 / NFR-10 preserved).
//   - The inline Daily Briefing section in the Workspace pane's Home tab
//     (`WorkspaceHomeTab.tsx`, task 086) is UNAFFECTED — it renders via
//     `createDailyBriefingRegistration` + `WorkspaceShell` and does NOT use
//     `Xrm.Navigation.navigateTo`. Operator still sees Daily Briefing content
//     inline; only the auto-launched MODAL is suppressed.
//
//   The flag value `"suppressed-by-spaarkeai"` is informational only — the
//   hook checks ANY truthy value at the key. Existing "shown" / "opted-out"
//   values from prior SpaarkeAi loads remain valid; we only set when no value
//   is already present so we don't clobber an operator-side opt-out.
// ---------------------------------------------------------------------------

const DAILY_DIGEST_SESSION_KEY = "spaarke_dailyDigestShown";

function suppressLegalWorkspaceDailyDigestAutoPopup(): void {
  try {
    if (!sessionStorage.getItem(DAILY_DIGEST_SESSION_KEY)) {
      sessionStorage.setItem(DAILY_DIGEST_SESSION_KEY, "suppressed-by-spaarkeai");
    }
  } catch {
    // sessionStorage may be unavailable (private browsing, sandboxed iframe).
    // Failure is non-fatal — the hook's own try/catch around sessionStorage
    // means it will skip the auto-popup in those same constrained contexts.
  }
}


async function bootstrap(): Promise<void> {
  const t0 = performance.now();

  // ai-spaarke-ai-workspace-UI-r1 brittleness Phase D (2026-06-09):
  // Initialize Application Insights so AppErrorBoundary, WidgetErrorBoundary,
  // and safeRegister can ship caught errors to the "Failures" pane via
  // reportClientError(). The instrumentation key is sourced from a build-time
  // Vite env var to avoid extending IRuntimeConfig (which would force a BFF
  // /config/client schema change). When unset (default in dev), the
  // AppInsightsService.initialize() call no-ops with a warn and the
  // boundaries continue to log to console only.
  // Override for non-dev environments: VITE_APP_INSIGHTS_KEY=<key> npm run build
  const appInsightsKey: string = import.meta.env.VITE_APP_INSIGHTS_KEY ?? "";
  if (appInsightsKey) {
    AppInsightsService.initialize(appInsightsKey);
  }

  // Task 105: suppress LegalWorkspace's Daily Digest auto-popup BEFORE any
  // React tree (including any embedded LegalWorkspaceApp instances) mounts.
  // See the docblock above this bootstrap() definition for full rationale.
  suppressLegalWorkspaceDailyDigestAutoPopup();

  // R4 task 052 (C-4) + R2 Option D (2026-06-18): register a SpaarkeAi-specific
  // workspace renderer that wraps `LegalWorkspaceApp` with a custom section
  // registry. `WorkspaceLayoutWidget` (registered into WorkspaceWidgetRegistry
  // by @spaarke/ai-widgets) consults `getDefaultWorkspaceRenderer()` at render
  // time. Without this call, the widget would render a "no renderer registered"
  // empty state.
  //
  // The custom section registry injects `loadSpaarkeAiNotificationContext`
  // into the Daily Briefing widget so the embedded copy renders real bullets
  // on cold load (FR-01 / FR-02) — replaces the R2 task 002 module-mutation
  // slot pattern. The wrapper renderer preserves the `setDefaultWorkspaceRenderer`
  // slot mechanism: WorkspaceLayoutWidget still sees a `WorkspaceRenderer`
  // function, it just renders an instance with custom `sections`. See
  // `projects/spaarke-daily-update-service-r2/notes/option-d-registry-as-composition.md`.
  //
  // FR-25 / NFR-10 preserved: standalone LegalWorkspace + standalone Daily
  // Briefing Code Page do NOT import this module, so the no-options
  // SECTION_REGISTRY default is used there → empty-payload contract preserved.
  const sectionsForSpaarkeAi = createLegalWorkspaceSectionRegistry({
    dailyBriefing: {
      loadNotificationContext: loadSpaarkeAiNotificationContext,
    },
  });
  const SpaarkeAiWorkspaceRenderer: WorkspaceRenderer = (props) => (
    <LegalWorkspaceApp {...props} sections={sectionsForSpaarkeAi} />
  );
  setDefaultWorkspaceRenderer(SpaarkeAiWorkspaceRenderer);

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
    console.warn(
      "[SpaarkeAi] resolveRuntimeConfig() failed:",
      xrmErr instanceof Error ? xrmErr.message : String(xrmErr)
    );

    // Try localStorage cache first (set by a previous successful visit)
    try {
      const cached = localStorage.getItem("spaarke-ai-runtime-config");
      if (cached) {
        config = JSON.parse(cached) as IRuntimeConfig;
        console.info("[SpaarkeAi] Using cached runtime config from localStorage");
      }
    } catch {
      // Parse failed or localStorage unavailable — continue to BFF fetch
    }

    // @ts-expect-error — config may still be undefined if cache miss
    if (!config) {
      // Fetch from BFF anonymous endpoint (works from any origin)
      console.info("[SpaarkeAi] No cached config — fetching from BFF /api/config/client");
      config = await fetchClientConfig();
    }
  }

  setRuntimeConfig(config);

  // Round 4 Fix 4.1 (2026-05-21): also initialize LegalWorkspace's runtime
  // config singleton with the SAME resolved config so the embedded
  // LegalWorkspaceApp's code paths (e.g. `getBffBaseUrl()` from navigateTo
  // handlers + `useWorkspaceLayouts` BFF fetch + document preview) find an
  // initialized singleton instead of throwing. Both singletons hold equivalent
  // values; they remain distinct in-process instances.
  try {
    setLegalWorkspaceRuntimeConfig(config);
  } catch (err) {
    // Non-fatal — if the embedded LegalWorkspace init fails, embedded paths
    // will surface their own "runtime config not initialized" error and the
    // standalone code paths in SpaarkeAi continue to work.
    console.warn(
      "[SpaarkeAi] setLegalWorkspaceRuntimeConfig failed; embedded LegalWorkspace paths may error:",
      err,
    );
  }

  // Cache config to localStorage so subsequent visits resolve instantly
  // without needing Xrm or a BFF fetch.
  try {
    localStorage.setItem("spaarke-ai-runtime-config", JSON.stringify(config));
  } catch {
    // localStorage may be unavailable (private browsing) — non-fatal
  }

  // -------------------------------------------------------------------------
  // 2. Auth initialization — single MSAL instance via @spaarke/auth
  //
  //    Uses the existing SpaarkeAuthProvider which manages a single MSAL
  //    ConfidentialClientApplication with silent + popup fallback.
  //    This works in ALL contexts (MDA, navigateTo, direct URL, top-frame).
  //
  //    IMPORTANT: Do NOT create a separate PublicClientApplication here.
  //    Multiple MSAL instances with the same clientId and localStorage cache
  //    cause "interaction_in_progress" errors.
  // -------------------------------------------------------------------------
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

  // Session restore: ?sessionId=<guid> triggers the restore flow (AIPU2-106).
  const sessionId =
    searchParams.get("sessionId") ??
    dataParams.get("sessionId") ??
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
      <AppErrorBoundary surfaceName="SpaarkeAi">
        <App
          entityLogicalName={entityLogicalName}
          entityId={entityId}
          matterId={matterId}
          sessionId={sessionId}
        />
      </AppErrorBoundary>
    </React.StrictMode>
  );

  const bootstrapMs = Math.round(performance.now() - t0);
  console.info(`[SpaarkeAi] Bootstrap complete in ${bootstrapMs}ms`);
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
