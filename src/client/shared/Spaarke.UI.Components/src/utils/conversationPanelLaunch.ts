/**
 * Open the Messages modal of the `CommunicationConversationPanel` PCF from another control on the same record form
 * (#2908: the communications grid's "+ New Message").
 *
 * The modal belongs to the panel. This module does not open a second Messages surface; it asks the panel to open its
 * own modal.
 *
 * {@link requestConversationPanelOpen} dispatches {@link CONVERSATION_PANEL_OPEN_EVENT} on the top window. A panel
 * that hosts the same record ({@link subscribeConversationPanelOpen}) opens its modal and marks the request handled.
 * `dispatchEvent` runs listeners synchronously, so the caller knows at once whether a panel took it. When none did
 * (the panel is not on the form, or an older panel build), the caller tells the user; nothing reloads the page and
 * no other surface opens (owner decision, option C).
 *
 * Origin and shape: only same-origin script can reach the top window to dispatch on it (a cross-origin frame gets a
 * SecurityError), so a request always comes from the Dataverse origin. The listener still checks the payload shape
 * (a logical name and a GUID) and ignores anything else.
 *
 * Plain DOM, no React and no imports, so PCF controls (React 16/17) and code pages (React 19) can both use it.
 */

/** Event name; the payload is a {@link ConversationPanelOpenRequest} in `CustomEvent.detail`. */
export const CONVERSATION_PANEL_OPEN_EVENT = 'spaarke:conversation-panel:open';

/** The record whose conversation should open. */
export interface ConversationPanelHost {
  /** Dataverse logical name, e.g. `sprk_matter`. */
  entityType: string;
  /** Record GUID, with or without braces, any case. */
  id: string;
}

/** `CustomEvent.detail` of {@link CONVERSATION_PANEL_OPEN_EVENT}. A panel that takes the request sets `handled`. */
export interface ConversationPanelOpenRequest extends ConversationPanelHost {
  handled: boolean;
}

/** What {@link openConversationPanel} did: `opened`, or `no-panel` when no panel on the page took the request. */
export type ConversationPanelOpenResult = 'opened' | 'no-panel';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const LOGICAL_NAME = /^[a-z][a-z0-9_]*$/;

/** Validate and normalize a host. Fields are `unknown`: the listener also feeds it untrusted event payloads. */
function normalizeHost(host: { entityType?: unknown; id?: unknown }): ConversationPanelHost | undefined {
  if (typeof host.entityType !== 'string' || typeof host.id !== 'string') return undefined;
  const entityType = host.entityType.trim().toLowerCase();
  const id = host.id.replace(/[{}]/g, '').trim().toLowerCase();
  if (!LOGICAL_NAME.test(entityType) || !GUID.test(id)) return undefined;
  return { entityType, id };
}

/** The top window when it can be reached (same origin), else this window. */
function topWindow(): Window | undefined {
  if (typeof window === 'undefined') return undefined;
  try {
    const top = window.top;
    if (top) {
      void top.location.href; // throws when the top window is cross-origin
      return top;
    }
    // eslint-disable-next-line no-restricted-syntax -- a SecurityError here only means the top window is cross-origin; this window is the documented fallback
  } catch {
    /* cross-origin top: fall back to this window */
  }
  return window;
}

/**
 * Ask a conversation panel on this page to open its Messages modal for `host`.
 *
 * @returns `true` when a panel for the same record took the request.
 */
export function requestConversationPanelOpen(
  host: ConversationPanelHost,
  target: Window | undefined = topWindow()
): boolean {
  const normalized = normalizeHost(host);
  if (!normalized || !target) return false;
  const detail: ConversationPanelOpenRequest = { ...normalized, handled: false };
  try {
    // Build the event in the target window's realm so its listeners see a native CustomEvent.
    const Ctor = ((target as unknown as { CustomEvent?: typeof CustomEvent }).CustomEvent ??
      CustomEvent) as typeof CustomEvent;
    target.dispatchEvent(new Ctor(CONVERSATION_PANEL_OPEN_EVENT, { detail }));
  } catch (err) {
    console.warn('[conversationPanelLaunch] Could not dispatch the open request:', err);
    return false;
  }
  return detail.handled;
}

/**
 * Listen for open requests addressed to `host` (the panel's own record). Requests for another record are ignored.
 *
 * @returns an unsubscribe function.
 */
export function subscribeConversationPanelOpen(
  host: ConversationPanelHost,
  onOpen: () => void,
  target: Window | undefined = topWindow()
): () => void {
  const mine = normalizeHost(host);
  if (!mine || !target) return () => undefined;
  const listener = (event: Event): void => {
    // Untrusted until checked: any same-origin script can dispatch this event name with any payload.
    const raw: unknown = (event as CustomEvent<unknown>).detail;
    if (!raw || typeof raw !== 'object') return;
    const detail = raw as { entityType?: unknown; id?: unknown; handled?: unknown };
    if (detail.handled === true) return;
    const asked = normalizeHost(detail);
    if (!asked || asked.entityType !== mine.entityType || asked.id !== mine.id) return;
    detail.handled = true;
    onOpen();
  };
  target.addEventListener(CONVERSATION_PANEL_OPEN_EVENT, listener);
  return () => target.removeEventListener(CONVERSATION_PANEL_OPEN_EVENT, listener);
}

/**
 * Open the Messages modal for `host` in place, through the panel on this page. Returns `no-panel` when no panel took
 * the request; the caller must then tell the user (nothing else happens: no reload, no other surface).
 */
export function openConversationPanel(
  host: ConversationPanelHost,
  target: Window | undefined = topWindow()
): ConversationPanelOpenResult {
  if (requestConversationPanelOpen(host, target)) return 'opened';
  console.warn('[conversationPanelLaunch] No Messages panel on this page took the request.', host);
  return 'no-panel';
}
