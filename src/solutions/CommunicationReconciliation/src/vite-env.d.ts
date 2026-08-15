/// <reference types="vite/client" />

/**
 * Vite build-time environment variables for the Communication Reconciliation Code Page.
 *
 * All VITE_* variables are public — do not place secrets here.
 */
interface ImportMetaEnv {
  /**
   * Application Insights instrumentation key (optional). When set, errors
   * caught by AppErrorBoundary route to the "Failures" pane via
   * reportClientError(). Absent in dev → no-op (boundary still logs to
   * console).
   *
   * Set via CI/CD pipeline env var: VITE_APP_INSIGHTS_KEY=<key> npm run build
   */
  readonly VITE_APP_INSIGHTS_KEY?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
