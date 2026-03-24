/**
 * telemetry.ts — Application Insights integration for the Corporate Workspace.
 *
 * Initializes the App Insights JS SDK using the instrumentation key from the
 * Dataverse environment variable `sprk_ApplicationInsightsKey`. Once started
 * the SDK automatically captures:
 *   - Page views & page load performance (Navigation Timing API)
 *   - Dependency calls (fetch/XHR — BFF API, Dataverse REST)
 *   - Unhandled exceptions
 *
 * Custom events tracked:
 *   - "WorkspaceBootstrap" — end-to-end bootstrap duration
 *   - "SectionLoaded"      — individual section load times (future)
 *
 * Usage:
 *   import { initTelemetry, trackEvent, trackMetric } from "./services/telemetry";
 *   await initTelemetry();           // call once in main.tsx
 *   trackEvent("WizardOpened", { wizard: "CreateMatter" });
 *   trackMetric("BootstrapMs", 1234);
 */

import { ApplicationInsights } from "@microsoft/applicationinsights-web";

// ---------------------------------------------------------------------------
// Singleton
// ---------------------------------------------------------------------------

let _appInsights: ApplicationInsights | null = null;

/**
 * Initialize Application Insights with the instrumentation key from a
 * Dataverse environment variable.
 *
 * Safe to call multiple times — subsequent calls are no-ops.
 * If the key cannot be resolved, telemetry is silently disabled (no crash).
 */
export async function initTelemetry(): Promise<void> {
  if (_appInsights) return; // already initialised

  try {
    const key = await resolveAppInsightsKey();
    if (!key) {
      console.warn("[Telemetry] sprk_ApplicationInsightsKey not configured — telemetry disabled.");
      return;
    }

    _appInsights = new ApplicationInsights({
      config: {
        instrumentationKey: key,
        enableAutoRouteTracking: false, // SPA inside Dataverse — no route changes
        disableFetchTracking: false,    // track fetch calls (BFF, Dataverse REST)
        enableCorsCorrelation: true,    // correlate cross-origin BFF calls
        enableRequestHeaderTracking: true,
        enableResponseHeaderTracking: true,
        maxBatchSizeInBytes: 10000,
        maxBatchInterval: 15000,        // flush every 15s
      },
    });

    _appInsights.loadAppInsights();
    _appInsights.trackPageView({ name: "CorporateWorkspace" });

    console.info("[Telemetry] Application Insights initialized.", key.substring(0, 8) + "...");
  } catch (err) {
    // Non-fatal — workspace works without telemetry
    console.warn("[Telemetry] Failed to initialize Application Insights:", err);
  }
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/** Track a custom event with optional properties. */
export function trackEvent(name: string, properties?: Record<string, string>): void {
  _appInsights?.trackEvent({ name }, properties);
}

/** Track a numeric metric (e.g., bootstrap duration in ms). */
export function trackMetric(name: string, average: number, properties?: Record<string, string>): void {
  _appInsights?.trackMetric({ name, average }, properties);
}

/** Track an exception. */
export function trackException(error: Error, properties?: Record<string, string>): void {
  _appInsights?.trackException({ exception: error }, properties);
}

/** Flush pending telemetry (call before page unload if needed). */
export function flushTelemetry(): void {
  _appInsights?.flush();
}

// ---------------------------------------------------------------------------
// Env var resolution (uses Dataverse REST API with session cookie)
// ---------------------------------------------------------------------------

async function resolveAppInsightsKey(): Promise<string | null> {
  try {
    const clientUrl = getClientUrl();
    if (!clientUrl) return null;

    const apiBase = `${clientUrl}/api/data/v9.2`;
    const filter = `schemaname eq 'sprk_ApplicationInsightsKey'`;

    // 1. Get definition
    const defResp = await fetch(
      `${apiBase}/environmentvariabledefinitions?$filter=${filter}&$select=environmentvariabledefinitionid,defaultvalue`,
      { headers: { Accept: "application/json", "OData-MaxVersion": "4.0", "OData-Version": "4.0" }, credentials: "include" }
    );
    if (!defResp.ok) return null;

    const defData = await defResp.json();
    const def = defData.value?.[0];
    if (!def) return null;

    // 2. Check for override value
    const valResp = await fetch(
      `${apiBase}/environmentvariablevalues?$filter=_environmentvariabledefinitionid_value eq '${def.environmentvariabledefinitionid}'&$select=value`,
      { headers: { Accept: "application/json", "OData-MaxVersion": "4.0", "OData-Version": "4.0" }, credentials: "include" }
    );
    if (valResp.ok) {
      const valData = await valResp.json();
      const override = valData.value?.[0]?.value;
      if (override) return override;
    }

    return def.defaultvalue ?? null;
  } catch {
    return null;
  }
}

function getClientUrl(): string | null {
  if (typeof window === "undefined") return null;
  const frames: Window[] = [window];
  try { if (window.parent && window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
  try { if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top); } catch { /* cross-origin */ }

  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const ctx = (frame as any).Xrm?.Utility?.getGlobalContext?.();
      if (ctx?.getClientUrl) return ctx.getClientUrl();
    } catch { /* cross-origin */ }
  }
  return null;
}
