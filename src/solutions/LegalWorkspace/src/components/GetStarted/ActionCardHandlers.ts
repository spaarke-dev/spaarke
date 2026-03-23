/**
 * ActionCardHandlers.ts
 *
 * Click handler functions for the playbook-intent action cards in the
 * Legal Operations Workspace Get Started row.
 *
 * Each handler opens the Playbook Library Code Page via Xrm.Navigation.navigateTo,
 * passing the entity context and intent as query parameters.
 *
 * Architecture:
 *   Pure TypeScript utility — no React, no side effects at import.
 *   Handlers are created via `createPlaybookHandlers(options)` so callers
 *   can inject the refetch callback.
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
 * Maps action card IDs (from getStartedConfig.ts) to Playbook Library intents.
 */
const CARD_INTENT_MAP: Readonly<Record<string, string>> = {
  "send-email-message": "email-compose",
  "schedule-new-meeting": "meeting-schedule",
};

// ---------------------------------------------------------------------------
// Handler factory options
// ---------------------------------------------------------------------------

/**
 * Options for `createPlaybookHandlers`.
 */
export interface IPlaybookHandlerOptions {
  /**
   * Called after the navigateTo dialog closes (whether successful or cancelled).
   * Callers should use this to refetch workspace data.
   */
  onDialogClose?: () => void;

  /**
   * BFF API base URL to pass to the Code Page via the navigateTo data parameter.
   * Required so the Playbook Library Code Page can make MSAL-authenticated BFF calls.
   * Callers should provide this from getBffBaseUrl() in the LegalWorkspace runtimeConfig.
   */
  bffBaseUrl?: string;
}

// ---------------------------------------------------------------------------
// Handler map type
// ---------------------------------------------------------------------------

/**
 * Map of action card ID → click handler function.
 * Keys match the `id` field in ACTION_CARD_CONFIGS (getStartedConfig.ts).
 */
export type PlaybookHandlerMap = Readonly<Record<string, () => void>>;

// ---------------------------------------------------------------------------
// navigateTo helper
// ---------------------------------------------------------------------------

/**
 * Opens the Playbook Library Code Page in a modal dialog via Xrm.Navigation.navigateTo.
 */
async function openPlaybookIntent(
  intent: string,
  onDialogClose?: () => void,
  bffBaseUrl?: string
): Promise<void> {
  try {
    const bffParam = bffBaseUrl ? `&bffBaseUrl=${encodeURIComponent(bffBaseUrl)}` : "";
    const data = `intent=${intent}${bffParam}`;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (window as any).Xrm?.Navigation?.navigateTo(
      { pageType: "webresource", webresourceName: "sprk_playbooklibrary", data },
      { target: 2, width: { value: 60, unit: "%" }, height: { value: 70, unit: "%" }, title: "Playbook Library" }
    );
    onDialogClose?.();
  } catch {
    onDialogClose?.();
  }
}

// ---------------------------------------------------------------------------
// Factory function
// ---------------------------------------------------------------------------

/**
 * Creates click handlers for the playbook-intent action cards.
 *
 * Each handler calls `openPlaybookIntent(intent)` which opens the Playbook Library
 * Code Page dialog via Xrm.Navigation.navigateTo.
 *
 * @param options - Injectable callbacks (onDialogClose).
 * @returns An object map of card ID → click handler.
 *
 * @example
 * ```typescript
 * const handlers = createPlaybookHandlers({
 *   onDialogClose: () => refetchData(),
 * });
 *
 * // Wire into GetStartedRow:
 * <GetStartedRow onCardClick={{ ...handlers, "create-new-matter": openWizard }} />
 * ```
 */
export function createPlaybookHandlers(
  options: IPlaybookHandlerOptions = {}
): PlaybookHandlerMap {
  const { onDialogClose, bffBaseUrl } = options;

  const handlers: Record<string, () => void> = {};

  for (const [cardId, intent] of Object.entries(CARD_INTENT_MAP)) {
    handlers[cardId] = () => {
      logInfo(`Card "${cardId}" clicked → opening Playbook Library with intent "${intent}"`);
      void openPlaybookIntent(intent, onDialogClose, bffBaseUrl);
    };
  }

  return handlers;
}

