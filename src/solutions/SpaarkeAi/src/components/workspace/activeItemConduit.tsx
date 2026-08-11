/**
 * activeItemConduit — widget-agnostic ACTIVE-ITEM conduit (spaarkeai-assistant-enhancements-r3 task 001).
 *
 * WHY THIS EXISTS
 *   Generalizes the shipped Compose "active document follows the active tab" pattern into a
 *   widget-agnostic conduit that carries exactly ONE active-item HANDLE at a time. Two feed patterns
 *   publish to it:
 *     - in-widget selection (a widget selecting a single row/item)  — wired later (task 012)
 *     - tab-focus (the active workspace tab IS the active item)      — wired here for `compose`
 *   Consumers (Assistant pane, future features) read the single handle to know "what one thing is the
 *   user working with right now" without any widget-specific coupling.
 *
 * WHAT IT CARRIES (and DELIBERATELY DOES NOT)
 *   The handle is a THIN identity slice — `{ id, type, label }` and NOTHING ELSE. It NEVER carries item
 *   content / bytes / body (ADR-015 — keep cross-pane conduits content-free). `type` is a widget/context
 *   discriminator string (e.g. 'compose', 'email', 'document'); `label` is a thin identity slice (subject
 *   / filename / title). Item CONTENT travels by other, purpose-built layers (e.g. Compose's
 *   `activeSourceDocRef` bytes → chat-context flow), which this conduit does NOT replace or touch.
 *
 * NON-BUS, SIBLING-PANE (ADR-030)
 *   This is a plain React context — NOT a PaneEventBus event / discriminant — exactly like the shipped
 *   `composeActionBridge`. The PaneEventBus stays content-free and its closed union is untouched. The
 *   handle is held in a ref for a STABLE setter identity (so consumers' memoized wiring does not churn),
 *   with a subscription so `useActiveItem()` re-renders consumers on change.
 *
 * SINGLE-ACTIVE-ITEM INVARIANT
 *   `setActiveItem(handle)` REPLACES any prior handle — there is never more than one. `setActiveItem(null)`
 *   clears it (deselect / tab-switch to a non-single-item surface / grid MULTI-select → no single handle).
 *
 * @see ../shell/ThreePaneShell.tsx — mounts `ActiveItemConduitProvider` above both panes
 * @see ./WorkspacePane.tsx — the tab-focus feed (publishes the active `compose` tab's handle; clears otherwise)
 * @see src/client/shared/Spaarke.Compose.Components/src/context/composeActionBridge.ts — the ref-stable
 *      pattern this mirrors (UNTOUCHED here — that module is under concurrent edit by other worktrees)
 */

import * as React from 'react';

/**
 * The one active-item handle. A THIN identity slice — id/type/label ONLY. A `content` / `bytes` / `body`
 * field here would be a defect (ADR-015). Readonly — consumers must not mutate it.
 */
export interface ActiveItemHandle {
  /** Opaque stable id for the active item (e.g. a Compose document session id, or the tab id). */
  readonly id: string;
  /** Widget/context discriminator (e.g. 'compose', 'email', 'document'). */
  readonly type: string;
  /** Thin identity label (e.g. subject / filename / tab title). NEVER item content. */
  readonly label: string;
}

/** Cross-pane conduit value. Setter identity is stable (ref-backed). */
export interface ActiveItemConduitValue {
  /**
   * Publish the single active item. Passing a new handle REPLACES any prior one (single-active-item
   * invariant); passing `null` clears it. Only `{ id, type, label }` are retained — any extra field on
   * the argument is dropped (content-free guarantee, ADR-015).
   */
  setActiveItem(handle: ActiveItemHandle | null): void;
  /** Read the current active item, or `null` when none is active. */
  getActiveItem(): ActiveItemHandle | null;
  /** Subscribe to change; returns an unsubscribe. Backs {@link useActiveItem}'s re-render. */
  subscribe(listener: () => void): () => void;
}

export const ActiveItemConduitContext = React.createContext<ActiveItemConduitValue | null>(null);
ActiveItemConduitContext.displayName = 'ActiveItemConduitContext';

function sameHandle(a: ActiveItemHandle | null, b: ActiveItemHandle | null): boolean {
  if (a === b) return true;
  if (a === null || b === null) return false;
  return a.id === b.id && a.type === b.type && a.label === b.label;
}

export interface ActiveItemConduitProviderProps {
  children?: React.ReactNode;
}

/**
 * Provides the {@link ActiveItemConduitContext}. Holds the single handle in a ref (so `setActiveItem`
 * is stable) plus a listener set (so `useActiveItem` consumers re-render on change). Republishing an
 * equal handle is a no-op — keeps the snapshot reference stable for `useSyncExternalStore`.
 */
export function ActiveItemConduitProvider(props: ActiveItemConduitProviderProps): React.JSX.Element {
  const handleRef = React.useRef<ActiveItemHandle | null>(null);
  const listenersRef = React.useRef<Set<() => void>>(new Set());

  const notify = React.useCallback((): void => {
    for (const listener of listenersRef.current) listener();
  }, []);

  const setActiveItem = React.useCallback(
    (handle: ActiveItemHandle | null): void => {
      // Content-free normalization (ADR-015): retain ONLY id/type/label; drop any bytes/body/content a
      // caller might pass. Single-active-item invariant: this REPLACES any prior handle (or clears it).
      const next: ActiveItemHandle | null =
        handle == null ? null : { id: handle.id, type: handle.type, label: handle.label };
      if (sameHandle(handleRef.current, next)) return; // no-op → stable snapshot, no redundant re-render
      handleRef.current = next;
      notify();
    },
    [notify]
  );

  const getActiveItem = React.useCallback((): ActiveItemHandle | null => handleRef.current, []);

  const subscribe = React.useCallback((listener: () => void): (() => void) => {
    listenersRef.current.add(listener);
    return () => {
      listenersRef.current.delete(listener);
    };
  }, []);

  const value = React.useMemo<ActiveItemConduitValue>(
    () => ({ setActiveItem, getActiveItem, subscribe }),
    [setActiveItem, getActiveItem, subscribe]
  );

  return React.createElement(ActiveItemConduitContext.Provider, { value }, props.children);
}

/**
 * Consume the conduit. Returns `null` when rendered OUTSIDE an {@link ActiveItemConduitProvider} (e.g. a
 * standalone / isolated mount or a unit test) — mirroring `useComposeActionBridge`, so publishers and
 * consumers no-op safely off-shell.
 */
export function useActiveItemConduit(): ActiveItemConduitValue | null {
  return React.useContext(ActiveItemConduitContext);
}

/**
 * Convenience READ hook — returns the current active-item handle (or `null`), re-rendering the consumer
 * whenever it changes. `null` off-provider.
 */
export function useActiveItem(): ActiveItemHandle | null {
  const conduit = useActiveItemConduit();
  const subscribe = React.useCallback(
    (onStoreChange: () => void): (() => void) => (conduit ? conduit.subscribe(onStoreChange) : () => {}),
    [conduit]
  );
  const getSnapshot = React.useCallback((): ActiveItemHandle | null => (conduit ? conduit.getActiveItem() : null), [
    conduit,
  ]);
  return React.useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}

/**
 * Stable PUBLISHER hook for feeds (the WorkspacePane tab-focus effect; future in-widget selection feeds).
 * Returns a function that publishes the single handle (or clears with `null`). Off-provider it is an inert
 * no-op. Identity is stable per conduit, so callers can safely list it in effect deps.
 */
export function usePublishActiveItem(): (handle: ActiveItemHandle | null) => void {
  const conduit = useActiveItemConduit();
  return React.useCallback(
    (handle: ActiveItemHandle | null): void => {
      conduit?.setActiveItem(handle ?? null);
    },
    [conduit]
  );
}
