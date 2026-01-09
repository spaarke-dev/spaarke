/**
 * Host-Builder Communication Message Types
 *
 * Defines the postMessage protocol between:
 * - PCF Host (PlaybookBuilderHost) - runs in Dataverse
 * - Builder App (React 18) - runs in iframe
 *
 * Message Flow:
 * 1. Builder loads → sends READY
 * 2. Host receives READY → sends INIT with playbook data
 * 3. User edits canvas → Builder sends DIRTY_CHANGE
 * 4. User clicks save → Builder sends SAVE_REQUEST
 * 5. Host saves via API → sends SAVE_SUCCESS or SAVE_ERROR
 * 6. (Optional) Host refreshes token → sends AUTH_TOKEN
 */

// ─────────────────────────────────────────────────────────────────────────────
// Host → Builder Messages
// ─────────────────────────────────────────────────────────────────────────────

/**
 * INIT: Sent by host after receiving READY.
 * Contains playbook data and initial configuration.
 */
export interface InitMessage {
  type: 'INIT';
  payload: {
    playbookId: string;
    playbookName: string;
    canvasJson: string;
    authToken?: string;
  };
}

/**
 * AUTH_TOKEN: Sent by host when token is refreshed.
 * Builder should use this for subsequent API calls.
 */
export interface AuthTokenMessage {
  type: 'AUTH_TOKEN';
  payload: {
    token: string;
    expiresAt: number; // Unix timestamp
  };
}

/**
 * SAVE_SUCCESS: Sent by host after successful save.
 */
export interface SaveSuccessMessage {
  type: 'SAVE_SUCCESS';
  payload: {
    timestamp: number;
  };
}

/**
 * SAVE_ERROR: Sent by host when save fails.
 */
export interface SaveErrorMessage {
  type: 'SAVE_ERROR';
  payload: {
    error: string;
    code?: string;
  };
}

/**
 * THEME_CHANGE: Sent by host when theme changes.
 */
export interface ThemeChangeMessage {
  type: 'THEME_CHANGE';
  payload: {
    theme: 'light' | 'dark';
  };
}

/** All messages host can send to builder */
export type HostToBuilderMessage =
  | InitMessage
  | AuthTokenMessage
  | SaveSuccessMessage
  | SaveErrorMessage
  | ThemeChangeMessage;

// ─────────────────────────────────────────────────────────────────────────────
// Builder → Host Messages
// ─────────────────────────────────────────────────────────────────────────────

/**
 * READY: Sent by builder when loaded and ready.
 * Host should respond with INIT.
 */
export interface ReadyMessage {
  type: 'READY';
}

/**
 * DIRTY_CHANGE: Sent by builder when dirty state changes.
 * Host uses this to warn about unsaved changes.
 */
export interface DirtyChangeMessage {
  type: 'DIRTY_CHANGE';
  payload: {
    isDirty: boolean;
  };
}

/**
 * SAVE_REQUEST: Sent by builder when user clicks save.
 * Includes serialized canvas state.
 */
export interface SaveRequestMessage {
  type: 'SAVE_REQUEST';
  payload: {
    canvasJson: string;
  };
}

/**
 * CANVAS_UPDATE: Sent by builder on canvas changes (for auto-sync).
 * Host may use for real-time backup or preview.
 */
export interface CanvasUpdateMessage {
  type: 'CANVAS_UPDATE';
  payload: {
    canvasJson: string;
  };
}

/**
 * REQUEST_TOKEN: Sent by builder when token is expired.
 * Host should respond with AUTH_TOKEN.
 */
export interface RequestTokenMessage {
  type: 'REQUEST_TOKEN';
}

/** All messages builder can send to host */
export type BuilderToHostMessage =
  | ReadyMessage
  | DirtyChangeMessage
  | SaveRequestMessage
  | CanvasUpdateMessage
  | RequestTokenMessage;

// ─────────────────────────────────────────────────────────────────────────────
// Type Guards
// ─────────────────────────────────────────────────────────────────────────────

export function isHostMessage(msg: unknown): msg is HostToBuilderMessage {
  if (!msg || typeof msg !== 'object') return false;
  const m = msg as { type?: string };
  return ['INIT', 'AUTH_TOKEN', 'SAVE_SUCCESS', 'SAVE_ERROR', 'THEME_CHANGE'].includes(m.type ?? '');
}

export function isBuilderMessage(msg: unknown): msg is BuilderToHostMessage {
  if (!msg || typeof msg !== 'object') return false;
  const m = msg as { type?: string };
  return ['READY', 'DIRTY_CHANGE', 'SAVE_REQUEST', 'CANVAS_UPDATE', 'REQUEST_TOKEN'].includes(m.type ?? '');
}
