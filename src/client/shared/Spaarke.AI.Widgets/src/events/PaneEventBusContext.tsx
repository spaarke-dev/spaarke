/**
 * @spaarke/ai-widgets — PaneEventBusContext & PaneEventBusProvider
 *
 * React context that distributes a shared PaneEventBus instance to the
 * component tree. The bus instance is created once when the provider mounts
 * and is never recreated, making it stable across re-renders.
 *
 * Usage:
 *   // At the root of the three-pane shell:
 *   <PaneEventBusProvider>
 *     <WorkspacePane />
 *     <ContextPane />
 *     <ConversationPane />
 *   </PaneEventBusProvider>
 *
 *   // Inside any descendant component:
 *   const dispatch = useDispatchPaneEvent();
 *   usePaneEvent('context', (event) => { ... });
 *
 * @see PaneEventBus — the bus implementation
 * @see usePaneEvent — subscription hook (reads from this context)
 * @see useDispatchPaneEvent — dispatch hook (reads from this context)
 */

import React, { createContext, useContext, useRef, type ReactNode } from 'react';
import { PaneEventBus } from './PaneEventBus';

// ---------------------------------------------------------------------------
// Context
// ---------------------------------------------------------------------------

/**
 * React context value — either a PaneEventBus instance or null when accessed
 * outside a PaneEventBusProvider.
 */
const PaneEventBusContext = createContext<PaneEventBus | null>(null);

// Give the context a display name so React DevTools labels it clearly.
PaneEventBusContext.displayName = 'PaneEventBusContext';

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

/** Props for PaneEventBusProvider. */
export interface PaneEventBusProviderProps {
  /** Child components that will have access to the event bus. */
  children: ReactNode;

  /**
   * Optional externally-created bus instance.
   *
   * Passing a bus instance from outside (e.g. in tests) lets callers control
   * the bus lifecycle and inspect subscriber counts without reaching into
   * React internals. When omitted, the provider creates its own stable instance.
   */
  bus?: PaneEventBus;
}

/**
 * Provides a stable PaneEventBus instance to all descendant components.
 *
 * The bus is created exactly once per provider mount (stored in a ref to
 * survive re-renders) and torn down only when the provider unmounts.
 *
 * Nest providers only when you need isolated event scopes (rare). Most
 * applications use a single provider at the shell root.
 *
 * @example
 * function SpaarkeAiShell() {
 *   return (
 *     <PaneEventBusProvider>
 *       <WorkspacePane />
 *       <ContextPane />
 *       <ConversationPane />
 *     </PaneEventBusProvider>
 *   );
 * }
 */
export function PaneEventBusProvider({ children, bus: externalBus }: PaneEventBusProviderProps): React.JSX.Element {
  // useRef — the bus is created at most once per provider lifetime. This
  // avoids the "new instance on every render" pitfall of useState initialisers
  // when an external bus is not provided.
  const busRef = useRef<PaneEventBus>(externalBus ?? new PaneEventBus());

  // If the caller swaps the external bus prop (unusual), update the ref so
  // hooks always dispatch on the current bus. React context re-renders
  // downstream subscribers automatically when the value changes.
  if (externalBus !== undefined && busRef.current !== externalBus) {
    busRef.current = externalBus;
  }

  return <PaneEventBusContext.Provider value={busRef.current}>{children}</PaneEventBusContext.Provider>;
}

// ---------------------------------------------------------------------------
// usePaneEventBus (internal)
// ---------------------------------------------------------------------------

/**
 * Internal hook that retrieves the PaneEventBus from context.
 *
 * Throws a descriptive error when used outside a PaneEventBusProvider so
 * developers get an actionable message instead of a cryptic null-access crash.
 *
 * This hook is intentionally NOT exported from the package barrel — consumers
 * should use usePaneEvent and useDispatchPaneEvent instead.
 */
export function usePaneEventBus(): PaneEventBus {
  const bus = useContext(PaneEventBusContext);

  if (bus === null) {
    throw new Error(
      '[PaneEventBus] usePaneEvent / useDispatchPaneEvent must be called inside a <PaneEventBusProvider>. ' +
        'Wrap the three-pane shell root with <PaneEventBusProvider> to fix this.'
    );
  }

  return bus;
}
