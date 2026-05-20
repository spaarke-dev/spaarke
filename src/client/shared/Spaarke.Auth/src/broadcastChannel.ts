/**
 * Cross-tab/iframe auth event broadcasting via the BroadcastChannel API.
 *
 * Reserved for invalidation events only (logout, future revocation broadcasts).
 * Tokens are NEVER transported via BroadcastChannel — MSAL.localStorage already
 * provides cross-context token sharing (INV-1, AUDIT-FINDINGS-AUTH-SYSTEM §4.1
 * principle 2).
 *
 * Channel name `spaarke-auth-events` is shared across all `@spaarke/auth`
 * consumers in the same browsing context (same origin). PCFs, Code Pages, and
 * navigateTo dialogs hosted under `https://*.crm.dynamics.com` all see each
 * other's messages.
 *
 * When BroadcastChannel is unavailable (server-side rendering, very old
 * browsers, Jest jsdom < 16), the API degrades to no-ops — callers do not need
 * to feature-detect.
 */

const CHANNEL_NAME = 'spaarke-auth-events';

/** Schema of messages sent over the auth broadcast channel. */
export type AuthBroadcastMessage = { type: 'logout' };

let _channel: BroadcastChannel | null = null;

function getChannel(): BroadcastChannel | null {
  if (typeof BroadcastChannel === 'undefined') return null;
  if (_channel) return _channel;
  try {
    _channel = new BroadcastChannel(CHANNEL_NAME);
  } catch {
    _channel = null;
  }
  return _channel;
}

/** Send a logout notification to all same-origin contexts. */
export function broadcastLogout(): void {
  const channel = getChannel();
  if (!channel) return;
  try {
    channel.postMessage({ type: 'logout' } satisfies AuthBroadcastMessage);
  } catch (err) {
    console.warn('[SpaarkeAuth] broadcastLogout failed:', err);
  }
}

/**
 * Register a listener for auth broadcast events. Returns a disposer that
 * removes the listener. Safe to call when BroadcastChannel is unavailable
 * (disposer is a no-op).
 */
export function onAuthBroadcast(handler: (msg: AuthBroadcastMessage) => void): () => void {
  const channel = getChannel();
  if (!channel) return () => {};

  const listener = (event: MessageEvent<AuthBroadcastMessage>) => {
    if (event.data && typeof event.data === 'object' && 'type' in event.data) {
      handler(event.data);
    }
  };
  channel.addEventListener('message', listener);
  return () => {
    try {
      channel.removeEventListener('message', listener);
    } catch {
      /* ignore */
    }
  };
}

/** Test-only: reset the singleton channel so per-test environments don't leak listeners. */
export function _resetBroadcastChannelForTests(): void {
  if (_channel) {
    try {
      _channel.close();
    } catch {
      /* ignore */
    }
    _channel = null;
  }
}
