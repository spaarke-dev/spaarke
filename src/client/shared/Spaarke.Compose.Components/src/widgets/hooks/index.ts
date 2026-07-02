/**
 * Spaarke Compose — hooks barrel
 *
 * Project:   spaarkeai-compose-r1
 * Extracted: R2 refactor (ComposeWorkspace.tsx 1795 → ~400 LOC)
 *
 * Re-exports the three workspace-level hooks extracted from the orchestrator:
 *   - useComposeBroadcastChannel    — cross-tab "focus-me" / "force-closed" signaling
 *   - useComposeCheckoutLifecycle   — SPE check-out probe + acquire + conflict handlers
 *   - useComposeHeartbeatGate       — 3-min heartbeat, gated on checkoutStatus === 'acquired' (FU-1 fix)
 */

export { useComposeBroadcastChannel } from './useComposeBroadcastChannel';
export type { UseComposeBroadcastChannelResult } from './useComposeBroadcastChannel';

export { useComposeCheckoutLifecycle } from './useComposeCheckoutLifecycle';
export type {
  UseComposeCheckoutLifecycleOptions,
  UseComposeCheckoutLifecycleResult,
} from './useComposeCheckoutLifecycle';

export { useComposeHeartbeatGate } from './useComposeHeartbeatGate';
