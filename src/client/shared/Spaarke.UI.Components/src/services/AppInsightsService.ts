/**
 * AppInsightsService — Singleton Application Insights wrapper.
 *
 * Direct browser SDK connection (NOT BFF-proxied) per FR-TEL-01.
 * Instrumentation key is sourced via the PCF manifest-property env-var
 * pattern (same mechanism as `apiBaseUrl`, `tenantId`, `clientAppId`,
 * `bffAppId`) — no hardcoding.
 *
 * **No PII**: event payloads MUST contain only event names + structured
 * properties (no userId, email, document content, raw query strings).
 *
 * Consumers:
 *  - SemanticSearchControl (FR-DOC-07 search events, future task 046)
 *  - VisualHost (`card_rendered` / `card_expanded`, future task 047/071)
 *
 * Compatibility: `@microsoft/applicationinsights-web` is framework-agnostic
 * and works on React 16/17/18 — safe for PCF (ADR-022) and Code Pages.
 *
 * @see ADR-012 (shared component library)
 * @see ADR-022 (PCF React 16/17 boundary)
 * @see spec FR-TEL-01
 */

import { ApplicationInsights, SeverityLevel } from '@microsoft/applicationinsights-web';

class AppInsightsServiceImpl {
  private _appInsights: ApplicationInsights | null = null;
  private _initialized = false;

  /**
   * Initialize Application Insights with the given instrumentation key.
   * Idempotent — second + subsequent calls are no-ops.
   *
   * @param key - Instrumentation key (NOT a full connection string).
   *              Empty / undefined keys are ignored (warn) so callers can
   *              pass `context.parameters.appInsightsKey?.raw` directly
   *              without null-checking.
   */
  public initialize(key: string): void {
    if (this._initialized) {
      return;
    }
    if (!key || key.trim().length === 0) {
      // eslint-disable-next-line no-console
      console.warn(
        '[AppInsightsService] initialize() called with empty key — skipping. ' +
          'Telemetry is disabled for this surface.'
      );
      return;
    }

    try {
      this._appInsights = new ApplicationInsights({
        config: {
          instrumentationKey: key,
          // Sensible defaults — no PII collection, no automatic route tracking
          // (PCF surfaces handle their own navigation), no cookie usage.
          disableTelemetry: false,
          disableCookiesUsage: true,
          disableFetchTracking: true,
          disableAjaxTracking: true,
          enableAutoRouteTracking: false,
          // Page-view tracking is opt-in per surface; we only auto-track
          // the initial load.
          autoTrackPageVisitTime: false,
        },
      });
      this._appInsights.loadAppInsights();
      this._initialized = true;
    } catch (err) {
      // eslint-disable-next-line no-console
      console.warn('[AppInsightsService] Failed to initialize:', err);
      this._appInsights = null;
      this._initialized = false;
    }
  }

  /**
   * Track a custom event. Warns (does NOT throw) if not initialized so a
   * pre-init call during cold-load is harmless.
   *
   * **No PII** — `properties` must contain only structured, non-identifying
   * values (counts, durations, enum values). Never include userId, email,
   * document content, or raw query strings.
   *
   * @param name - Event name (e.g. `card_rendered`, `search_executed`).
   * @param properties - Optional structured property bag.
   */
  public trackEvent(name: string, properties?: Record<string, unknown>): void {
    if (!this._initialized || !this._appInsights) {
      // eslint-disable-next-line no-console
      console.warn(`[AppInsightsService] trackEvent('${name}') called before initialize() — event dropped.`);
      return;
    }
    try {
      this._appInsights.trackEvent({ name }, properties);
    } catch (err) {
      // eslint-disable-next-line no-console
      console.warn(`[AppInsightsService] trackEvent('${name}') failed:`, err);
    }
  }

  /**
   * Track an exception with structured properties.
   *
   * Used by `reportClientError` (utils) to ship errors caught by
   * AppErrorBoundary / WidgetErrorBoundary / safeRegister to App Insights.
   * Renders in the App Insights "Failures" pane with proper stack-trace
   * grouping.
   *
   * @param error      - The caught Error instance.
   * @param properties - Optional structured property bag (no PII).
   * @param severity   - SeverityLevel (default: Error).
   */
  public trackException(
    error: Error,
    properties?: Record<string, unknown>,
    severity: SeverityLevel = SeverityLevel.Error
  ): void {
    if (!this._initialized || !this._appInsights) {
      console.warn(`[AppInsightsService] trackException('${error.message}') called before initialize() — dropped.`);
      return;
    }
    try {
      this._appInsights.trackException({ exception: error, severityLevel: severity }, properties);
    } catch (err) {
      console.warn('[AppInsightsService] trackException failed:', err);
    }
  }

  /**
   * Flush queued telemetry (mainly useful for tests / page-unload paths).
   * Safe no-op if not initialized.
   */
  public flush(): void {
    if (!this._initialized || !this._appInsights) {
      return;
    }
    try {
      this._appInsights.flush();
    } catch (err) {
      // eslint-disable-next-line no-console
      console.warn('[AppInsightsService] flush() failed:', err);
    }
  }

  /**
   * Test-only: returns whether initialize() has been called successfully.
   */
  public get isInitialized(): boolean {
    return this._initialized;
  }
}

/**
 * Singleton instance. Both PCF surfaces consume the same instance to
 * guarantee a single SDK initialization per browser-window context.
 */
export const AppInsightsService = new AppInsightsServiceImpl();
