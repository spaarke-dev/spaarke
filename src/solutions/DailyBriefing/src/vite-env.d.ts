/// <reference types="vite/client" />

/**
 * Vite build-time environment variables for DailyBriefing Code Page.
 *
 * All VITE_* variables are public — do not place secrets here.
 */
interface ImportMetaEnv {
  /**
   * Application Insights instrumentation key (optional). When set, errors
   * caught by AppErrorBoundary / safeRegister / WidgetErrorBoundary are
   * shipped to App Insights "Failures" pane via reportClientError().
   *
   * Set via CI/CD pipeline env var: VITE_APP_INSIGHTS_KEY=<key> npm run build
   */
  readonly VITE_APP_INSIGHTS_KEY?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
