/**
 * decisionWizardLauncher - the seam between "a worklist line was opened" and "the decision wizard mounted in this app".
 *
 * The worklist widget (task 059) raises `OpenItemEvent` from its lines (`MatterCard.onOpenItem`) and calls
 * `launchDecisionWizard(event)`. If a `DecisionWizardHost` is mounted (the Console mounts one, D-26: in-app, never a
 * `navigateTo` code page), the wizard opens there; if none is mounted the call reports `{ opened: false }` and the caller
 * decides what to do (it must NOT fall back to opening a code page or a WizardModal).
 *
 * The widget owns the data routes, so it may pass its own `loadItem`, `lookup`, `openLookup` and `openRecord` with the launch; they win over the host's.
 *
 * Task: spaarke-ontology-platform-r1, task 058.
 */

import type { OpenItemEvent } from '../Worklist/types';
import type {
  DecisionItemLoader,
  DecisionLookupSearch,
  DecisionOpenLookup,
  DecisionOpenRecord,
} from './decisionWizardTypes';

export interface DecisionWizardLaunchRequest extends OpenItemEvent {
  /** The widget's reader for one item (task 038 / 045). Overrides the host's. */
  loadItem?: DecisionItemLoader;
  /** The widget's search for Lookup / Person fields. Overrides the host's. */
  lookup?: DecisionLookupSearch;
  /** The widget's side-pane lookup for Person / Lookup fields (D-182). Overrides the host's. */
  openLookup?: DecisionOpenLookup;
  /** The widget's open-record-in-a-modal for the subject link (D-182). Overrides the host's. */
  openRecord?: DecisionOpenRecord;
  /** Called after an item was recorded, so the widget can refresh. */
  onRecorded?: (event: { signalId: string; alsoResolved: readonly string[] }) => void;
}

export interface DecisionWizardLaunchResult {
  /** False when no host is mounted, or a decision wizard is already open (one at a time). */
  opened: boolean;
  /** Why it did not open. */
  reason?: 'no-host' | 'busy';
}

/** Resolves when the wizard closes. */
export type DecisionWizardOpener = (request: DecisionWizardLaunchRequest) => Promise<DecisionWizardLaunchResult>;

let opener: DecisionWizardOpener | null = null;

/** Called by `DecisionWizardHost` on mount; the returned function unregisters it (only if it is still the registered one). */
export function registerDecisionWizardHost(next: DecisionWizardOpener): () => void {
  opener = next;
  return () => {
    if (opener === next) opener = null;
  };
}

/** True when a host is mounted and a launch would open the wizard. */
export function isDecisionWizardHostMounted(): boolean {
  return opener !== null;
}

/**
 * Open the decision wizard on the activated line. The promise resolves when the wizard closes (`opened: true`) or at once when
 * it could not open.
 */
export async function launchDecisionWizard(request: DecisionWizardLaunchRequest): Promise<DecisionWizardLaunchResult> {
  if (!opener) return { opened: false, reason: 'no-host' };
  return opener(request);
}
