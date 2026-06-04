/**
 * insightsRendererConfig.ts — Configuration values for the Insights response
 * renderer surface (R5 task 028 / D2-18).
 *
 * R5 frontend settings convention is intentionally minimal — `runtimeConfig.ts`
 * is reserved for auth bootstrap values (BFF base URL, MSAL client ID, scope).
 * UX-policy thresholds (confidence floor, etc.) live here as typed exports
 * with safe defaults; the renderer reads them lazily so a future settings
 * provider can hot-swap the source without renderer code changes.
 *
 * Per ADR-018 §"Flag Scope Discipline" and R5 CLAUDE.md §3.2: every value in
 * this module is a CONFIGURATION VALUE (numeric / string / structural), NOT a
 * feature flag (boolean kill-switch). The confidence threshold is operator-
 * tunable without code change; UX surface area (the badge) is unconditional.
 *
 * Default values are sourced from spec.md Assumptions §"Confidence threshold
 * default" (= 0.6 per spec FR-15).
 *
 * @see projects/spaarke-ai-platform-unification-r5/spec.md FR-15 + Assumptions
 * @see projects/spaarke-ai-platform-unification-r5/tasks/028-confidence-floor-badge.poml
 * @see .claude/adr/ADR-018-feature-flag-scope-discipline.md
 */

// ---------------------------------------------------------------------------
// Defaults
// ---------------------------------------------------------------------------

/**
 * Default low-confidence threshold. Responses with `confidence < threshold`
 * surface the "Low confidence — verify before relying" badge in
 * `InsightsResponseRenderer`. Per spec FR-15 + Assumptions, the default is
 * 0.6. Operators can override via {@link setInsightsRendererConfig}.
 */
export const DEFAULT_CONFIDENCE_THRESHOLD = 0.6;

// ---------------------------------------------------------------------------
// Config shape
// ---------------------------------------------------------------------------

/**
 * Configuration shape for the Insights response renderer. Kept small — adding
 * a new typed value here is the canonical way to surface operator-tunable UX
 * thresholds without introducing a feature flag.
 */
export interface InsightsRendererConfig {
  /**
   * Threshold below which the renderer surfaces the low-confidence advisory
   * badge. Number in `[0, 1]`. Default `0.6`. Per spec FR-15.
   */
  readonly confidenceThreshold: number;
}

// ---------------------------------------------------------------------------
// Singleton mutable config (read at render time; overridable for tests + ops)
// ---------------------------------------------------------------------------

let _config: InsightsRendererConfig = {
  confidenceThreshold: DEFAULT_CONFIDENCE_THRESHOLD,
};

/**
 * Returns the current renderer configuration. Reads are cheap (singleton
 * lookup) — safe to call on every render. Each invocation returns the LATEST
 * value, so reactive reconfiguration is honored on the next render cycle.
 */
export function getInsightsRendererConfig(): InsightsRendererConfig {
  return _config;
}

/**
 * Replaces (partially or fully) the current renderer configuration. The
 * partial shape merges over the current singleton so callers can tune a
 * single value without restating defaults.
 *
 * Intended call sites:
 *   1. App bootstrap (`main.tsx`) — if operator config from Dataverse
 *      Environment Variables overrides the default threshold.
 *   2. Tests — local reconfiguration per `it` block (always restore in
 *      `afterEach` via {@link resetInsightsRendererConfig}).
 *
 * Per ADR-018: this MUST NOT be used to flip a boolean kill-switch — the
 * badge is unconditional UX. Only the numeric threshold is tunable.
 */
export function setInsightsRendererConfig(
  patch: Partial<InsightsRendererConfig>,
): void {
  _config = { ..._config, ...patch };
}

/**
 * Restores the default configuration. Tests SHOULD call this in `afterEach`
 * to isolate config mutations between cases.
 */
export function resetInsightsRendererConfig(): void {
  _config = { confidenceThreshold: DEFAULT_CONFIDENCE_THRESHOLD };
}
