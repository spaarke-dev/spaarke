/**
 * ActionCardHandlers.ts
 *
 * Click handler functions for the 6 non-Create-Matter action cards in the
 * Legal Operations Workspace Get Started row.
 *
 * Each handler launches the AI Playbook Analysis Builder (AiToolAgent PCF)
 * with a pre-configured context/intent payload. The launcher posts a structured
 * message to window.parent, which the MDA host routes to the embedded AiToolAgent.
 *
 * If the Analysis Builder is unavailable (message channel unreachable, or parent
 * frame is the top-level window without a handler), an informational toast is shown
 * via the `onUnavailable` callback.
 *
 * Architecture:
 *   This module is a Pure TypeScript utility — no React, no side effects at import.
 *   Handlers are created via `createAnalysisBuilderHandlers(options)` so callers
 *   can inject the `onUnavailable` toast callback without coupling to any global
 *   notification mechanism.
 */

import {
  ANALYSIS_BUILDER_CONTEXTS,
  IAnalysisBuilderContext,
  IAnalysisBuilderLaunchMessage,
} from "./analysisBuilderTypes";

// ---------------------------------------------------------------------------
// Logger (light wrapper — avoids console.log in production via build stripping)
// ---------------------------------------------------------------------------

const LOG_PREFIX = "[ActionCardHandlers]";

function logInfo(message: string, ...args: unknown[]): void {
  // eslint-disable-next-line no-console
  console.info(`${LOG_PREFIX} ${message}`, ...args);
}

function logWarn(message: string, ...args: unknown[]): void {
  // eslint-disable-next-line no-console
  console.warn(`${LOG_PREFIX} ${message}`, ...args);
}

function logError(message: string, ...args: unknown[]): void {
  // eslint-disable-next-line no-console
  console.error(`${LOG_PREFIX} ${message}`, ...args);
}

// ---------------------------------------------------------------------------
// Availability detection
// ---------------------------------------------------------------------------

/**
 * Returns true when the current window is embedded in a parent frame — a
 * necessary (though not sufficient) condition for the Analysis Builder
 * postMessage channel to exist.
 *
 * The channel is unavailable when:
 *   - The Custom Page is opened in a standalone tab (window === window.parent)
 *   - The MDA host has not registered a handler for "openAnalysisBuilder"
 *
 * In those cases the fallback toast is shown instead.
 */
function isEmbeddedInParentFrame(): boolean {
  try {
    return window.self !== window.top;
  } catch {
    // Cross-origin parent — treat as embedded (postMessage still works)
    return true;
  }
}

// ---------------------------------------------------------------------------
// Core launcher
// ---------------------------------------------------------------------------

/**
 * Posts an `openAnalysisBuilder` message to the parent MDA frame with the
 * given context/intent payload.
 *
 * Returns `true` if the message was posted successfully, `false` if the
 * channel is unavailable (standalone tab, postMessage threw, etc.).
 */
function postAnalysisBuilderMessage(context: IAnalysisBuilderContext): boolean {
  if (!isEmbeddedInParentFrame()) {
    logWarn(
      "postAnalysisBuilderMessage: Not embedded in a parent frame. " +
        "Analysis Builder is only available when the workspace is open inside " +
        "a Power Apps Model-Driven App."
    );
    return false;
  }

  const message: IAnalysisBuilderLaunchMessage = {
    action: "openAnalysisBuilder",
    context,
  };

  try {
    window.parent.postMessage(message, "*");
    logInfo("Launched Analysis Builder with intent:", context.intent, message);
    return true;
  } catch (err) {
    logError(
      "postAnalysisBuilderMessage: Failed to post message to parent frame.",
      err,
      message
    );
    return false;
  }
}

// ---------------------------------------------------------------------------
// Handler factory options
// ---------------------------------------------------------------------------

/**
 * Options for `createAnalysisBuilderHandlers`.
 */
export interface IAnalysisBuilderHandlerOptions {
  /**
   * Called when the Analysis Builder is unavailable (e.g. standalone tab,
   * postMessage failure). Use this to show a Fluent v9 Toast notification
   * explaining that the feature requires the MDA context.
   *
   * @param displayName - The human-readable label of the card that was clicked.
   * @param intent      - The intent identifier that was attempted.
   */
  onUnavailable?: (displayName: string, intent: string) => void;
}

// ---------------------------------------------------------------------------
// Handler map type
// ---------------------------------------------------------------------------

/**
 * Map of action card ID → click handler function.
 * Keys match the `id` field in ACTION_CARD_CONFIGS (getStartedConfig.ts).
 */
export type AnalysisBuilderHandlerMap = Readonly<
  Record<string, () => void>
>;

// ---------------------------------------------------------------------------
// Factory function
// ---------------------------------------------------------------------------

/**
 * Creates click handlers for the 6 non-Create-Matter action cards.
 *
 * Each handler:
 *   1. Looks up the pre-defined context payload from ANALYSIS_BUILDER_CONTEXTS
 *   2. Posts an `openAnalysisBuilder` message to window.parent
 *   3. Calls `onUnavailable` if the channel is not reachable
 *
 * @param options - Injectable callbacks (onUnavailable toast).
 * @returns       - An object map of card ID → click handler.
 *
 * @example
 * ```typescript
 * const handlers = createAnalysisBuilderHandlers({
 *   onUnavailable: (displayName) => {
 *     dispatchToast(
 *       <Toast><ToastTitle>{displayName} requires the full workspace</ToastTitle></Toast>,
 *       { intent: "info" }
 *     );
 *   },
 * });
 *
 * // Wire into GetStartedRow:
 * <GetStartedRow onCardClick={{ ...handlers, "create-new-matter": openWizard }} />
 * ```
 */
export function createAnalysisBuilderHandlers(
  options: IAnalysisBuilderHandlerOptions = {}
): AnalysisBuilderHandlerMap {
  const { onUnavailable } = options;

  /**
   * Builds an individual handler for a given card ID.
   * The closure captures the resolved context at creation time.
   */
  function buildHandler(cardId: string): () => void {
    const context = ANALYSIS_BUILDER_CONTEXTS[cardId];

    if (!context) {
      logError(
        `buildHandler: No context found for card ID "${cardId}". ` +
          "Check ANALYSIS_BUILDER_CONTEXTS in analysisBuilderTypes.ts."
      );
      // Return a no-op rather than throwing — unknown card IDs should not crash the app
      return () => {
        logWarn(`No handler configured for card "${cardId}".`);
      };
    }

    return (): void => {
      const posted = postAnalysisBuilderMessage(context);

      if (!posted && onUnavailable) {
        onUnavailable(context.displayName, context.intent);
      }
    };
  }

  return {
    "create-new-project": buildHandler("create-new-project"),
    "assign-to-counsel": buildHandler("assign-to-counsel"),
    "analyze-new-document": buildHandler("analyze-new-document"),
    "search-document-files": buildHandler("search-document-files"),
    "send-email-message": buildHandler("send-email-message"),
    "schedule-new-meeting": buildHandler("schedule-new-meeting"),
  };
}

// ---------------------------------------------------------------------------
// Default unavailability message
// ---------------------------------------------------------------------------

/**
 * Returns a human-readable message explaining why the Analysis Builder is
 * unavailable. Used when no `onUnavailable` callback is provided.
 *
 * @param displayName - The action card label (e.g. "Create New Project").
 */
export function getAnalysisBuilderUnavailableMessage(
  displayName: string
): string {
  return (
    `"${displayName}" requires the AI Playbook Analysis Builder, which is ` +
    "available when the workspace is open inside the Legal Operations Model-Driven App. " +
    "Please open the workspace from the main navigation."
  );
}
