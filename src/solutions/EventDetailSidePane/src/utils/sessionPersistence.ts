/**
 * Session Persistence for EventDetailSidePane
 *
 * Persists edit state to sessionStorage so that switching between
 * Calendar and Event tabs in the side pane menu doesn't lose unsaved changes.
 *
 * When Xrm.App.sidePanes deselects a web resource pane, the iframe may be
 * destroyed and recreated. SessionStorage survives this because it's scoped
 * to the browser tab, not the iframe.
 *
 * @module utils/sessionPersistence
 * @see Task 104 - SessionStorage persistence for tab-switch survival
 */

// ─────────────────────────────────────────────────────────────────────────────
// Configuration
// ─────────────────────────────────────────────────────────────────────────────

/** SessionStorage key for persisted state */
const SESSION_KEY = "sprk_eventdetail_state";

/** Maximum age of persisted state (30 minutes) */
const MAX_AGE_MS = 30 * 60 * 1000;

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * State persisted to sessionStorage
 */
export interface PersistedState {
  /** Event ID this state belongs to */
  eventId: string;
  /** Current edited field values (may differ from server state) */
  currentValues: Record<string, unknown>;
  /** Section expand/collapse states (keyed by section ID) */
  sectionStates: Record<string, boolean>;
  /** Field names explicitly edited by user (for dirty tracking) */
  editedFieldNames?: string[];
  /** Scroll position of content area */
  scrollPosition: number;
  /** Timestamp for staleness check */
  timestamp: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Persistence Operations
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Save state to sessionStorage.
 * Called on every field change (debounced by caller).
 *
 * @param state - State to persist
 */
export function persistState(state: PersistedState): void {
  try {
    sessionStorage.setItem(SESSION_KEY, JSON.stringify(state));
  } catch (error) {
    // sessionStorage may be full or unavailable — fail silently
    console.warn("[SessionPersistence] Failed to persist state:", error);
  }
}

/**
 * Restore state from sessionStorage.
 * Returns null if:
 * - No persisted state exists
 * - Persisted state is for a different eventId
 * - Persisted state is stale (older than MAX_AGE_MS)
 *
 * @param eventId - The eventId being loaded (must match persisted state)
 * @returns Persisted state or null
 */
export function restoreState(eventId: string): PersistedState | null {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY);
    if (!raw) return null;

    const state = JSON.parse(raw) as PersistedState;

    // Verify the state is for the same event
    if (state.eventId !== eventId) {
      console.log(
        "[SessionPersistence] Persisted state is for different event:",
        state.eventId,
        "vs",
        eventId
      );
      clearPersistedState();
      return null;
    }

    // Check staleness
    const age = Date.now() - state.timestamp;
    if (age > MAX_AGE_MS) {
      console.log(
        "[SessionPersistence] Persisted state is stale:",
        Math.round(age / 60000),
        "minutes old"
      );
      clearPersistedState();
      return null;
    }

    console.log(
      "[SessionPersistence] Restored state for event:",
      eventId,
      "age:",
      Math.round(age / 1000),
      "seconds"
    );
    return state;
  } catch (error) {
    console.warn("[SessionPersistence] Failed to restore state:", error);
    return null;
  }
}

/**
 * Clear persisted state from sessionStorage.
 * Called on:
 * - Successful save (no more dirty state to preserve)
 * - Explicit close (user discarded changes)
 * - Navigation away from Events module
 */
export function clearPersistedState(): void {
  try {
    sessionStorage.removeItem(SESSION_KEY);
  } catch {
    // Ignore — not critical
  }
}

/**
 * Check if there is persisted state for a given eventId without loading it.
 */
export function hasPersistedState(eventId: string): boolean {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY);
    if (!raw) return false;
    const state = JSON.parse(raw) as PersistedState;
    return state.eventId === eventId && (Date.now() - state.timestamp) < MAX_AGE_MS;
  } catch {
    return false;
  }
}
