/**
 * errorTelemetry.ts — Error-only telemetry helper for SpaarkeAi.
 *
 * Wraps the existing Application Insights channel so all SpaarkeAi error-emission
 * sites share a single typed boundary. Per FR-24 / OC-09, only error/failure
 * paths are tracked through this module — no happy-path events.
 *
 * Design notes:
 *   - Safe no-op when the App Insights instance is unavailable (Vite dev,
 *     unit tests, or environments without an instrumentation key). The helper
 *     MUST NOT throw under any circumstance.
 *   - The helper does NOT initialize App Insights itself. A future bootstrap
 *     step (or sibling module) is expected to call `setAppInsightsInstance()`
 *     during app startup. Until then, all `logTelemetryError(...)` calls
 *     silently no-op.
 *   - Properties pass through unchanged. PII scrubbing is downstream policy
 *     and is out of scope for this thin wrapper.
 *
 * Consumed by (FR-24 emission sites):
 *   - Daily Briefing 429 rate-limited responses — task 035 (FR-16 / NFR-11)
 *   - Chat file-extraction failures — task 024 / 025 (FR-07 wiring)
 *   - HistoryOverlay session-list load failures — task 022 (FR-03)
 *
 * @see ADR-012 — Shared component principles (this stays SpaarkeAi-local for now)
 * @see ADR-028 — No auth-token coupling; this helper writes only to App Insights
 * @see src/solutions/LegalWorkspace/src/services/telemetry.ts — sibling pattern
 *      using the same `@microsoft/applicationinsights-web` SDK
 */

import type { ApplicationInsights } from "@microsoft/applicationinsights-web";

// ---------------------------------------------------------------------------
// FR-24 event-name constants
//
// All event names emitted by this helper share the `spaarke-ai-error.` prefix
// so they can be filtered with a single App Insights KQL clause such as:
//
//   customEvents | where name startswith "spaarke-ai-error."
// ---------------------------------------------------------------------------

/**
 * Emitted when the Daily Briefing endpoint returns HTTP 429 (rate limited).
 * Consumed by task 035 (Daily Briefing 429 + empty state).
 */
export const TELEMETRY_DAILY_BRIEFING_429 =
  "spaarke-ai-error.daily-briefing.rate-limited";

/**
 * Emitted when client-side file extraction (PDF.js / Mammoth) fails for a
 * chat attachment. Consumed by task 024 / 025 (useChatFileAttachment hook +
 * SprkChat toolbar restructure).
 */
export const TELEMETRY_FILE_EXTRACTION_FAILURE =
  "spaarke-ai-error.chat.file-extraction-failure";

/**
 * Emitted when the HistoryOverlay session-list fetch fails. Consumed by
 * task 022 (HistoryOverlay component + wiring).
 */
export const TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE =
  "spaarke-ai-error.history-overlay.load-failure";

// ---------------------------------------------------------------------------
// Internal state — App Insights singleton resolved via setter
// ---------------------------------------------------------------------------

let _appInsights: ApplicationInsights | null = null;

/**
 * Wire an App Insights instance into the error-telemetry helper. Typically
 * called once during app bootstrap (after `new ApplicationInsights(...).loadAppInsights()`).
 *
 * Passing `null` or `undefined` clears the instance and reverts the helper
 * to no-op mode — useful for unit tests and shutdown paths.
 */
export function setAppInsightsInstance(
  instance: ApplicationInsights | null | undefined,
): void {
  _appInsights = instance ?? null;
}

/**
 * Internal test helper — resets the singleton between tests. Not part of the
 * public API; do not call from production code.
 *
 * @internal
 */
export function __resetForTests(): void {
  _appInsights = null;
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Log an error-only telemetry event to Application Insights.
 *
 * If no App Insights instance has been wired (via `setAppInsightsInstance`),
 * the call silently no-ops. The helper never throws.
 *
 * @param eventName  Event name — should be one of the `TELEMETRY_*` constants
 *                   exported from this module, but any string is accepted.
 *                   Custom event names SHOULD use the `spaarke-ai-error.`
 *                   prefix for consistent KQL filtering.
 * @param properties Free-form properties payload. Passes through to
 *                   `trackEvent` unchanged. Use this for HTTP status codes,
 *                   error messages, correlation IDs, etc.
 *
 * @example
 *   logTelemetryError(TELEMETRY_DAILY_BRIEFING_429, {
 *     statusCode: 429,
 *     retryAfterSeconds: 30,
 *     correlationId: "abc123",
 *   });
 */
export function logTelemetryError(
  eventName: string,
  properties: Record<string, unknown>,
): void {
  if (!_appInsights) {
    // No-op — App Insights not initialized (dev env, tests, missing key).
    return;
  }

  try {
    // The App Insights SDK accepts any JSON-serializable map. Casting to
    // `Record<string, any>` matches the SDK's loose typing while preserving
    // our stricter `Record<string, unknown>` external contract.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    _appInsights.trackEvent({ name: eventName }, properties as Record<string, any>);
  } catch {
    // Never propagate telemetry failures to the caller. An error path that
    // throws while logging an error is a worse user experience than silent
    // loss of one telemetry event.
  }
}
