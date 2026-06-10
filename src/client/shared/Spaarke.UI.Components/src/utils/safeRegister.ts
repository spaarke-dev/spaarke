import { reportClientError } from '../services/reportClientError';

/**
 * safeRegister — defensive wrapper for side-effect registration calls.
 *
 * Side-effect import files (e.g. `register-workspace-widgets.ts`,
 * `register-context-widgets.ts`, section registrations in LegalWorkspace)
 * call `registerXxx(...)` at top level. If ONE call throws synchronously —
 * malformed metadata, evaluation error in the factory expression, missing
 * import — all SUBSEQUENT registrations in the file are skipped, leaving
 * the registry partially populated. The surface then renders empty widget
 * tabs (the symptom that originally motivated the brittleness remediation).
 *
 * Wrap each registration call in `safeRegister(...)` so a failure is logged
 * and isolated — the remaining registrations still run.
 *
 * Established 2026-06-09 by ai-spaarke-ai-workspace-UI-r1 brittleness Phase B.4.
 *
 * @example
 *   // Before (one throwing call breaks the rest):
 *   registerWorkspaceWidget('matters-list', metadata, factory);
 *   registerWorkspaceWidget('document-summary', metadata, factory);
 *
 *   // After (each call isolated):
 *   safeRegister('WorkspaceWidget', 'matters-list', () =>
 *     registerWorkspaceWidget('matters-list', metadata, factory)
 *   );
 *   safeRegister('WorkspaceWidget', 'document-summary', () =>
 *     registerWorkspaceWidget('document-summary', metadata, factory)
 *   );
 *
 * @param registryName       - Logical name of the registry (for log prefixing).
 * @param registrationLabel  - Identifier of the thing being registered.
 * @param action             - The registration call (typically a thunk).
 * @returns                    The action's return value, or undefined if it threw.
 */
export function safeRegister<T>(registryName: string, registrationLabel: string, action: () => T): T | undefined {
  try {
    return action();
  } catch (err) {
    reportClientError(err instanceof Error ? err : new Error(String(err)), {
      scope: 'safeRegister',
      registryName,
      registrationLabel,
    });
    return undefined;
  }
}
