import { AppInsightsService } from './AppInsightsService';

/**
 * Structured context for a client-side error. Used by reportClientError so
 * the App Insights "Failures" pane can be filtered + grouped meaningfully
 * (by surface, by widget, by registry).
 *
 * Avoid PII: error messages and component stacks are code-level identifiers,
 * but custom properties added here MUST be scope/structural data only — no
 * userId, email, document content, query strings, or other user input.
 */
export interface ClientErrorContext {
  /** Where the error was caught — one of: 'AppErrorBoundary' | 'WidgetErrorBoundary' | 'safeRegister' | (future scopes). */
  scope: string;
  /** Logical surface name — e.g. 'SpaarkeAi', 'Daily Briefing', 'Workspace Layout Wizard'. */
  surface?: string;
  /** Widget type, when scope = WidgetErrorBoundary. */
  widgetType?: string;
  /** Registry name + registration label, when scope = safeRegister. */
  registryName?: string;
  registrationLabel?: string;
  /** React component stack (from React.ErrorInfo). Truncated to keep payload small. */
  componentStack?: string;
}

/**
 * Optional override hook — if set via `setClientErrorTelemetryHook`, this
 * function is called INSTEAD of the default AppInsightsService path. Lets
 * a surface route errors to a custom telemetry sink (a non-App-Insights
 * backend, a test recorder, etc.) without changing call sites.
 */
type TelemetryHook = (error: Error, context: ClientErrorContext) => void;
let _telemetryHook: TelemetryHook | null = null;

/**
 * Override the default App Insights telemetry path. Pass null to restore
 * the default. Surfaces typically call this once during bootstrap, not at
 * all if they want the default AppInsightsService behavior.
 */
export function setClientErrorTelemetryHook(hook: TelemetryHook | null): void {
  _telemetryHook = hook;
}

/**
 * Report a client-side error to telemetry.
 *
 * Always logs to console.error so the developer console + browser devtools
 * surface the error during local development. Additionally:
 *   - If a custom telemetry hook was set via setClientErrorTelemetryHook, it
 *     is called and the App Insights path is skipped.
 *   - Otherwise, AppInsightsService.trackException is called with the error
 *     + structured context as custom properties. If App Insights is not
 *     initialized, this is a logged no-op.
 *
 * Established 2026-06-09 by ai-spaarke-ai-workspace-UI-r1 brittleness Phase D.
 *
 * @example
 *   // Inside AppErrorBoundary.componentDidCatch:
 *   reportClientError(error, {
 *     scope: 'AppErrorBoundary',
 *     surface: this.props.surfaceName,
 *     componentStack: errorInfo.componentStack ?? undefined,
 *   });
 */
export function reportClientError(error: Error, context: ClientErrorContext): void {
  console.error(`[${context.scope}${context.surface ? `:${context.surface}` : ''}]`, error, context);

  if (_telemetryHook) {
    try {
      _telemetryHook(error, context);
    } catch (hookErr) {
      console.warn('[reportClientError] custom telemetry hook threw:', hookErr);
    }
    return;
  }

  // Truncate componentStack to keep App Insights payload small (1024 char cap).
  const componentStack =
    context.componentStack && context.componentStack.length > 1024
      ? context.componentStack.substring(0, 1024) + '… [truncated]'
      : context.componentStack;

  AppInsightsService.trackException(error, {
    scope: context.scope,
    surface: context.surface,
    widgetType: context.widgetType,
    registryName: context.registryName,
    registrationLabel: context.registrationLabel,
    errorName: error.name,
    componentStack,
  });
}
