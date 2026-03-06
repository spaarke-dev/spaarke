/**
 * ActionCardHandlers.ts
 *
 * Click handler functions for the 5 Quick Start action cards in the
 * Legal Operations Workspace Get Started row.
 *
 * Each handler opens the QuickStartWizardDialog with the appropriate
 * intent string. The dialog is rendered by WorkspaceGrid.tsx.
 *
 * Architecture:
 *   Pure TypeScript utility — no React, no side effects at import.
 *   Handlers are created via `createQuickStartHandlers(options)` so callers
 *   can inject the `onOpenWizard` callback.
 */

// ---------------------------------------------------------------------------
// Logger
// ---------------------------------------------------------------------------

const LOG_PREFIX = "[ActionCardHandlers]";

function logInfo(message: string, ...args: unknown[]): void {
  console.info(`${LOG_PREFIX} ${message}`, ...args);
}

// ---------------------------------------------------------------------------
// Card ID → Intent mapping
// ---------------------------------------------------------------------------

/**
 * Maps action card IDs (from getStartedConfig.ts) to QuickStart wizard intents
 * (from quickStartConfig.ts).
 */
const CARD_INTENT_MAP: Readonly<Record<string, string>> = {
  "assign-to-counsel": "assign-counsel",
  "search-document-files": "document-search",
  "send-email-message": "email-compose",
  "schedule-new-meeting": "meeting-schedule",
};

// ---------------------------------------------------------------------------
// Handler factory options
// ---------------------------------------------------------------------------

/**
 * Options for `createQuickStartHandlers`.
 */
export interface IQuickStartHandlerOptions {
  /**
   * Called when a Quick Start card is clicked. Receives the wizard intent string.
   * The caller should set React state to open QuickStartWizardDialog with this intent.
   */
  onOpenWizard: (intent: string) => void;
}

// ---------------------------------------------------------------------------
// Handler map type
// ---------------------------------------------------------------------------

/**
 * Map of action card ID → click handler function.
 * Keys match the `id` field in ACTION_CARD_CONFIGS (getStartedConfig.ts).
 */
export type QuickStartHandlerMap = Readonly<Record<string, () => void>>;

// ---------------------------------------------------------------------------
// Factory function
// ---------------------------------------------------------------------------

/**
 * Creates click handlers for the 5 Quick Start action cards.
 *
 * Each handler calls `onOpenWizard(intent)` with the card's mapped intent.
 *
 * @param options - Injectable callbacks (onOpenWizard).
 * @returns An object map of card ID → click handler.
 *
 * @example
 * ```typescript
 * const handlers = createQuickStartHandlers({
 *   onOpenWizard: (intent) => setWizardIntent(intent),
 * });
 *
 * // Wire into GetStartedRow:
 * <GetStartedRow onCardClick={{ ...handlers, "create-new-matter": openWizard }} />
 * ```
 */
export function createQuickStartHandlers(
  options: IQuickStartHandlerOptions
): QuickStartHandlerMap {
  const { onOpenWizard } = options;

  const handlers: Record<string, () => void> = {};

  for (const [cardId, intent] of Object.entries(CARD_INTENT_MAP)) {
    handlers[cardId] = () => {
      logInfo(`Card "${cardId}" clicked → opening wizard with intent "${intent}"`);
      onOpenWizard(intent);
    };
  }

  return handlers;
}
