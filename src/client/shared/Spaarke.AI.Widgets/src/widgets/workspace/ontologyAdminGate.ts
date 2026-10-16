/**
 * ontologyAdminGate - registers the "Ontology admin" workspace tab only when the BFF capability probe says yes
 * (spaarke-ontology-platform-r1 task 104; D-22, FR-56).
 *
 * The host starts the gate once, after sign-in (`startOntologyAdminGate`, SpaarkeAi `main.tsx`). It asks
 * `GET /api/v1/ontology/admin/capabilities` once per page load; on `canAuthorPolicies: true` it calls
 * `registerOntologyAdminWidget()`, and only then. Any other answer, or a failure, leaves the type unregistered, so for that user
 * nothing can open the tab. The gate decides what is SHOWN, never what is ALLOWED: every admin write route authorizes itself as the
 * caller, and the BFF never reads this answer.
 *
 * Two readers:
 *   - `useOntologyAdminTabAvailable()` - the Manage Workspaces pane lists "Ontology admin" while it is true.
 *   - `whenOntologyAdminGateSettled()` - the workspace pane awaits it (bounded) before restoring persisted tabs, so a reloaded admin
 *     tab resolves to the real widget rather than the unknown-type fallback.
 */

import * as React from 'react';
import { fetchOntologyAdminCapability } from '@spaarke/ui-components';
import type { OntologyAdminCapabilityOptions } from '@spaarke/ui-components';
import { ONTOLOGY_ADMIN_WIDGET_TYPE, registerOntologyAdminWidget } from './register-workspace-widgets';
import { resolveWorkspaceWidget } from '../../registry/WorkspaceWidgetRegistry';
import type { WorkspaceWidgetComponent } from '../../types/widget-types';

type GateState = 'not-started' | 'pending' | 'available' | 'unavailable';

let state: GateState = 'not-started';
let pending: Promise<boolean> | null = null;
const listeners = new Set<() => void>();

function setState(next: GateState): void {
  state = next;
  listeners.forEach(listener => listener());
}

/**
 * Starts the gate (once per page load; later calls return the first call's promise). Resolves true when the tab was registered.
 * Never rejects.
 */
export function startOntologyAdminGate(options: OntologyAdminCapabilityOptions): Promise<boolean> {
  if (pending) return pending;
  setState('pending');
  const started = fetchOntologyAdminCapability(options).then((canAuthorPolicies: boolean) => {
    if (canAuthorPolicies) {
      registerOntologyAdminWidget();
    }
    setState(canAuthorPolicies ? 'available' : 'unavailable');
    return canAuthorPolicies;
  });
  pending = started;
  return started;
}

/** The longest a tab restore waits for the probe; past it, restore proceeds and the gate still registers when it answers. */
export const ONTOLOGY_ADMIN_GATE_RESTORE_WAIT_MS = 3000;

/** Resolves when the gate has answered (or was never started), or after `timeoutMs`, whichever is first. Never rejects. */
export function whenOntologyAdminGateSettled(timeoutMs: number = ONTOLOGY_ADMIN_GATE_RESTORE_WAIT_MS): Promise<void> {
  if (!pending) return Promise.resolve();
  let timer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<void>(resolve => {
    timer = setTimeout(resolve, timeoutMs);
  });
  return Promise.race([pending.then(() => undefined), timeout]).finally(() => {
    if (timer !== undefined) clearTimeout(timer);
  });
}

/**
 * The resolver for RESTORING persisted tabs: as `resolveWorkspaceWidget`, except an 'ontology-admin' tab resolves to null (the
 * tab manager then skips it) unless the gate says the tab is available. Without it, a persisted admin tab restores as the
 * unknown-type fallback (a caller whose role was revoked, or an administrator whose probe outlasted the restore wait), and since
 * the type is single-instance, reopening it from Manage Workspaces would only focus that dead tab.
 */
export function resolveRestorableWorkspaceWidget(widgetType: string): Promise<WorkspaceWidgetComponent | null> {
  if (widgetType === ONTOLOGY_ADMIN_WIDGET_TYPE && !isOntologyAdminTabAvailable()) {
    return Promise.resolve(null);
  }
  return resolveWorkspaceWidget(widgetType);
}

/** True only once the probe said yes and the tab is registered. */
export function isOntologyAdminTabAvailable(): boolean {
  return state === 'available';
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** React reader of {@link isOntologyAdminTabAvailable}; re-renders when the gate answers. */
export function useOntologyAdminTabAvailable(): boolean {
  return React.useSyncExternalStore(subscribe, isOntologyAdminTabAvailable, isOntologyAdminTabAvailable);
}
