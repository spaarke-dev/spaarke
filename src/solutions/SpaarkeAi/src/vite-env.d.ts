/// <reference types="vite/client" />

/**
 * Vite build-time environment variables for SpaarkeAi Code Page.
 *
 * These are embedded in the bundle at build time, not at runtime.
 * All VITE_* variables are public — do not place secrets here.
 */
interface ImportMetaEnv {
  /**
   * BFF API base URL — used as fallback when resolveRuntimeConfig() cannot
   * reach Xrm (direct URL access without the Dataverse MDA shell, no localStorage cache).
   *
   * Set in .env (development) or via CI/CD pipeline env var (production).
   * Example: https://spe-api-dev-67e2xz.azurewebsites.net
   */
  readonly VITE_BFF_BASE_URL: string;

  /**
   * Application Insights instrumentation key (optional). When set, errors
   * caught by AppErrorBoundary / WidgetErrorBoundary / safeRegister are
   * shipped to App Insights "Failures" pane via reportClientError() — see
   * brittleness Phase D (2026-06-09). Absent in dev → boundary logs to
   * console only.
   *
   * Set via CI/CD pipeline env var: VITE_APP_INSIGHTS_KEY=<key> npm run build
   */
  readonly VITE_APP_INSIGHTS_KEY?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
