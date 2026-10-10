/**
 * AppInsightsService — Singleton Application Insights wrapper.
 *
 * Direct browser SDK connection (NOT BFF-proxied) per FR-TEL-01.
 *
 * WHERE THE CONNECTION STRING COMES FROM (#1537): at runtime, from the BFF the
 * surface already talks to — `GET /api/config/client` returns the environment's
 * browser App Insights connection string, and `@spaarke/auth`'s
 * `getTelemetryConnectionString(bffBaseUrl)` fetches it once and caches it. Every
 * surface calls `initializeFromRuntime(() => getTelemetryConnectionString(url))`.
 * No form property and no build-time value decides it: shipped forms and builds
 * carried Spaarke's DEV key, which sent customer usage data to a Spaarke dev
 * resource. The provider is injected (rather than imported from `@spaarke/auth`
 * here) so React-free consumers such as VisualHost do not pull the auth stack in
 * through this module.
 *
 * Telemetry is best-effort: when the lookup fails or the BFF has no connection
 * string, telemetry is off and nothing throws.
 *
 * **No PII**: event payloads MUST contain only event names + structured
 * properties (no userId, email, document content, raw query strings).
 *
 * Consumers: SemanticSearchControl, VisualHost (PCFs); DailyBriefing, EmailPage,
 * CommunicationReconciliation (code pages); `reportClientError`.
 *
 * Compatibility: `@microsoft/applicationinsights-web` is framework-agnostic
 * and works on React 16/17/18 — safe for PCF (ADR-022) and Code Pages.
 *
 * @see ADR-012 (shared component library)
 * @see ADR-022 (PCF React 16/17 boundary)
 * @see spec FR-TEL-01
 */

import { ApplicationInsights, SeverityLevel } from '@microsoft/applicationinsights-web';

/** Resolves the connection string for this environment; `''`/null/undefined means "telemetry off". */
export type TelemetryConnectionStringProvider = () => Promise<string | null | undefined>;

/**
 * Upper bound on telemetry calls held while `initializeFromRuntime()` is waiting
 * for the connection string. Events past it are dropped (best-effort telemetry).
 */
const MAX_PENDING_CALLS = 50;

class AppInsightsServiceImpl {
  private _appInsights: ApplicationInsights | null = null;
  private _initialized = false;
  private _pendingInit: Promise<boolean> | null = null;
  private _pendingCalls: Array<() => void> = [];

  /**
   * Initialize Application Insights. Idempotent — second + subsequent calls are no-ops.
   *
   * @param connectionStringOrKey - An App Insights connection string
   *        (`InstrumentationKey=…;IngestionEndpoint=…`), or a bare instrumentation
   *        key. Empty values are ignored (warn): telemetry stays off.
   */
  public initialize(connectionStringOrKey: string): void {
    if (this._initialized) {
      return;
    }
    const value = (connectionStringOrKey ?? '').trim();
    if (!value) {
      // eslint-disable-next-line no-console
      console.warn(
        '[AppInsightsService] initialize() called with no connection string — skipping. ' +
          'Telemetry is disabled for this surface.'
      );
      return;
    }

    try {
      this._appInsights = new ApplicationInsights({
        config: {
          ...(value.includes('=') ? { connectionString: value } : { instrumentationKey: value }),
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
   * Initialize from the environment's runtime connection string (#1537).
   *
   * Pass `() => getTelemetryConnectionString(bffBaseUrl)` from `@spaarke/auth`.
   * Telemetry calls made while the lookup is in flight are held (up to
   * {@link MAX_PENDING_CALLS}) and sent once it succeeds, so first-render events
   * are not lost. Never rejects: a failing provider or an empty answer leaves
   * telemetry off. Idempotent — concurrent callers share one lookup, and a
   * later call after a failed lookup tries again.
   *
   * @returns whether telemetry is on afterwards.
   */
  public initializeFromRuntime(provider: TelemetryConnectionStringProvider): Promise<boolean> {
    if (this._initialized) {
      return Promise.resolve(true);
    }
    if (this._pendingInit) {
      return this._pendingInit;
    }

    this._pendingInit = (async () => {
      let connectionString = '';
      try {
        connectionString = ((await provider()) ?? '').trim();
      } catch (err) {
        // eslint-disable-next-line no-console
        console.warn('[AppInsightsService] Telemetry connection string lookup failed — telemetry off:', err);
      }
      if (connectionString) {
        this.initialize(connectionString);
      } else {
        // eslint-disable-next-line no-console
        console.warn('[AppInsightsService] No telemetry connection string for this environment — telemetry off.');
      }

      const held = this._pendingCalls;
      this._pendingCalls = [];
      this._pendingInit = null;
      if (this._initialized) {
        for (const call of held) call();
      }
      return this._initialized;
    })();
    return this._pendingInit;
  }

  /** While a runtime lookup is in flight, hold the call; returns whether it was held. */
  private holdWhilePending(call: () => void): boolean {
    if (!this._pendingInit) {
      return false;
    }
    if (this._pendingCalls.length < MAX_PENDING_CALLS) {
      this._pendingCalls.push(call);
    }
    return true;
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
      if (this.holdWhilePending(() => this.trackEvent(name, properties))) {
        return;
      }
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
      if (this.holdWhilePending(() => this.trackException(error, properties, severity))) {
        return;
      }
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
