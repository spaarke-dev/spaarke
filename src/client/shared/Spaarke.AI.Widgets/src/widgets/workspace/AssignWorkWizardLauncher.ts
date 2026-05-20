/**
 * AssignWorkWizardLauncher — thin dispatcher for the `sprk_createworkassignmentwizard`
 * Dataverse Code Page wizard (FR-20, project spaarke-ai-platform-unification-r3, task 045).
 *
 * The Assign Work card on the welcome state's GetStartedCards (task 042) does NOT
 * route to an in-app Workspace tab like the other 6 cards. Instead it crosses the
 * host boundary into Dataverse and opens an existing Code Page web resource via
 * `Xrm.Navigation.navigateTo`. This module exists so that task 042's onCardClick
 * handler stays small — it special-cases the `assign-work` widget_type by calling
 * `launchAssignWorkWizard()` rather than dispatching a `widget_load` event.
 *
 * UQ-05 — data query string format (verified)
 * ===========================================
 * The exact `data` payload, `pageType`, and `webresourceName` match the existing
 * production call site in LegalWorkspace:
 *
 *   src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx:345
 *     {
 *       pageType: "webresource",
 *       webresourceName: "sprk_createworkassignmentwizard",
 *       data: `bffBaseUrl=${encodeURIComponent(getBffBaseUrl())}`,
 *     }
 *
 * The helper in `src/client/webresources/js/sprk_wizard_commands.js:194`
 * uses the same exact pattern when called via the ribbon command path:
 *
 *     var data = (baseData ? baseData + "&" : "") + "bffBaseUrl=" + encodeURIComponent(bffBaseUrl);
 *
 * Note: the original spec prompt and POML referenced `pageType: 'custom'` with a
 * `name` field. That was incorrect — the wizard is delivered as a webresource
 * Code Page, not a Dataverse custom page. Per UQ-05, the existing precedent is
 * canonical. This launcher uses `pageType: "webresource"` + `webresourceName`.
 *
 * Constraints applied
 * ===================
 * - ADR-012: shared library cannot import solution-local config (`getBffBaseUrl`
 *   lives in `src/solutions/LegalWorkspace/src/config/runtimeConfig.ts`).
 *   Therefore the caller MUST pass `bffBaseUrl` in via options.
 * - ADR-021: no Fluent v9 token usage in this file — it is a pure TypeScript
 *   module with no UI. Any placeholder UI for non-host environments is the
 *   responsibility of the calling site (task 042).
 * - ADR-028: `bffBaseUrl` is a base URL only — NOT a token. We MUST NOT pass
 *   any access token through the `data` query string. The Dataverse-hosted
 *   wizard performs its own auth via `@spaarke/auth` after it loads.
 *
 * Vite dev / non-host fallback
 * ============================
 * When `window.Xrm` is unavailable (Vite dev, unit tests, Storybook, etc.) the
 * launcher returns `{ launched: false, reason: 'no-xrm' }` synchronously. The
 * calling site decides how to surface that: a MessageBar in the Workspace pane,
 * a toast, a console.warn, or silent no-op. Keeping the launcher pure avoids
 * coupling shared-lib code to any specific UI shell.
 *
 * React 19, NOT PCF-safe (Xrm.Navigation is host-only).
 *
 * @see projects/spaarke-ai-platform-unification-r3/tasks/045-assign-work-launcher.poml
 * @see src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx:345 (UQ-05 precedent)
 * @see src/client/webresources/js/sprk_wizard_commands.js:194 (UQ-05 precedent)
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Options accepted by {@link launchAssignWorkWizard}.
 */
export interface AssignWorkLaunchOptions {
  /**
   * Spaarke BFF base URL — propagated to the wizard via the `data` query string
   * so the wizard's `runtimeConfig.getBffBaseUrl()` resolves correctly inside
   * the Dataverse host iframe. MUST be a base URL only (no path, no token).
   *
   * The calling site should source this from its own runtime config:
   * - LegalWorkspace: `import { getBffBaseUrl } from '../../config/runtimeConfig'`
   * - SpaarkeAi:      `import { getBffBaseUrl } from '../../config/runtimeConfig'`
   */
  bffBaseUrl: string;

  /**
   * Optional dialog title shown in the Dataverse navigateTo chrome. Defaults to
   * "Create Work Assignment" to match the existing LegalWorkspace call site.
   */
  title?: string;
}

/**
 * Result returned by {@link launchAssignWorkWizard}. Lets the calling site
 * distinguish between successful host dispatch and a Vite-dev / non-host
 * environment so the UI can show a placeholder without the launcher having to
 * mutate the DOM itself.
 */
export type AssignWorkLaunchResult =
  | { launched: true }
  | { launched: false; reason: 'no-xrm' | 'invalid-options' };

// ---------------------------------------------------------------------------
// Internal: Xrm.Navigation feature detection
// ---------------------------------------------------------------------------

/**
 * Minimal shape of `window.Xrm.Navigation` the launcher needs. Typed as a
 * narrow shape rather than `any` so callers get autocompletion + type safety,
 * while still allowing the cast at the boundary (no global Xrm types are
 * shipped in the shared-lib package).
 */
interface XrmNavigationLike {
  navigateTo: (
    pageInput: {
      pageType: 'webresource';
      webresourceName: string;
      data?: string;
    },
    navigationOptions?: {
      target?: 1 | 2;
      width?: { value: number; unit?: 'px' | '%' };
      height?: { value: number; unit?: 'px' | '%' };
      title?: string;
    }
  ) => Promise<unknown>;
}

interface XrmGlobalLike {
  Navigation?: XrmNavigationLike;
}

/**
 * Reads `window.Xrm.Navigation` if the host has injected it. Returns `null` in
 * non-host environments (Vite dev, jsdom unit tests, Storybook). Wrapped in a
 * try/catch so any getter that throws (rare, but observed in some PCF harnesses)
 * is treated the same as "not present".
 *
 * The type assertion at the boundary is the only `as unknown as` cast in this
 * file — it's intentional and documented: `window.Xrm` is not declared in our
 * shared-lib's TypeScript libs because that would force every consumer (PCF,
 * Code Page, SPA) to include Dataverse type definitions even when they don't
 * need them.
 */
function getXrmNavigation(): XrmNavigationLike | null {
  if (typeof window === 'undefined') {
    return null;
  }
  try {
    const xrm = (window as unknown as { Xrm?: XrmGlobalLike }).Xrm;
    if (!xrm || !xrm.Navigation || typeof xrm.Navigation.navigateTo !== 'function') {
      return null;
    }
    return xrm.Navigation;
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Web resource name of the existing Dataverse Code Page wizard. Exported so
 * tests can assert the launcher uses the canonical name and so the calling
 * site can log it if needed. The name is intentionally a constant — there is
 * one and only one Assign Work wizard.
 */
export const ASSIGN_WORK_WEBRESOURCE_NAME = 'sprk_createworkassignmentwizard';

/**
 * Default Dataverse dialog chrome — matches the existing LegalWorkspace call
 * site (WorkspaceGrid.tsx:346). `target: 2` opens as a dialog rather than
 * inline; 60% × 70% gives the wizard enough room for the 3-step flow.
 */
const DEFAULT_DIALOG_OPTIONS = {
  target: 2 as const,
  width: { value: 60, unit: '%' as const },
  height: { value: 70, unit: '%' as const },
};

/**
 * Launch the Assign Work wizard.
 *
 * Behaviour:
 *   1. Validates `bffBaseUrl` is a non-empty string. Returns
 *      `{ launched: false, reason: 'invalid-options' }` if not.
 *   2. Feature-detects `window.Xrm.Navigation.navigateTo`. Returns
 *      `{ launched: false, reason: 'no-xrm' }` if absent (Vite dev, unit tests,
 *      Storybook, etc.) — the calling site decides what UI to show.
 *   3. Otherwise calls `Xrm.Navigation.navigateTo` with the exact `data` format
 *      verified against the existing precedent (UQ-05) and returns
 *      `{ launched: true }`. The promise returned by Xrm is intentionally NOT
 *      awaited — the Dataverse navigateTo promise resolves only when the
 *      dialog closes, which we don't need to block on here. Any rejection
 *      (user cancel, dialog error) is swallowed to match the LegalWorkspace
 *      precedent's silent catch.
 *
 * Note on async: the function returns synchronously. It does not return a
 * Promise. Callers that need to know when the dialog closes should chain a
 * separate `Xrm.Navigation.navigateTo(...)` call themselves; that level of
 * coordination is intentionally outside this thin dispatcher's scope.
 *
 * @example
 * ```ts
 * // Calling site (e.g. task 042's onCardClick handler):
 * import { launchAssignWorkWizard } from '@spaarke/ai-widgets';
 * import { getBffBaseUrl } from '../../config/runtimeConfig';
 *
 * function handleAssignWorkCard() {
 *   const result = launchAssignWorkWizard({ bffBaseUrl: getBffBaseUrl() });
 *   if (!result.launched && result.reason === 'no-xrm') {
 *     // Show "Open in Spaarke (host required)" placeholder.
 *     showHostRequiredToast();
 *   }
 * }
 * ```
 */
export function launchAssignWorkWizard(
  options: AssignWorkLaunchOptions
): AssignWorkLaunchResult {
  // Defensive: an empty / non-string bffBaseUrl would silently produce a broken
  // query string. Fail fast so callers see the problem in dev rather than in
  // production where the wizard would just appear to misbehave.
  if (!options || typeof options.bffBaseUrl !== 'string' || options.bffBaseUrl.length === 0) {
    return { launched: false, reason: 'invalid-options' };
  }

  const navigation = getXrmNavigation();
  if (navigation === null) {
    return { launched: false, reason: 'no-xrm' };
  }

  // UQ-05 verified format — see file header for precedent citations.
  const data = `bffBaseUrl=${encodeURIComponent(options.bffBaseUrl)}`;
  const title = options.title ?? 'Create Work Assignment';

  // Fire-and-forget — the navigateTo promise resolves on dialog close, but
  // the launcher doesn't need to block on that. Any rejection is swallowed
  // (matches WorkspaceGrid.tsx:348 precedent: "User cancelled or dialog
  // error — ignore").
  navigation
    .navigateTo(
      {
        pageType: 'webresource',
        webresourceName: ASSIGN_WORK_WEBRESOURCE_NAME,
        data,
      },
      {
        ...DEFAULT_DIALOG_OPTIONS,
        title,
      }
    )
    .catch(() => {
      // Intentional: user cancel / dialog error — ignore (matches precedent).
    });

  return { launched: true };
}
