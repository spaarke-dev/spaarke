/**
 * DecisionWizardHost - the ONE in-app host for the decision wizard (task 058; D-26; ADR-050 launch rule (a)).
 *
 * A Spaarke React surface (the Console, `ThreePaneShell`) mounts this once. While it is mounted, `launchDecisionWizard(event)`
 * (called by the worklist widget when a line is opened) opens `DecisionWizard` here, in `SprkModal`, over the host's own
 * `FluentProvider` (no provider of its own: the modal inherits light and dark, ADR-021). It is never opened with `navigateTo`
 * and never in a `WizardModal`.
 *
 * One wizard at a time: a second launch while one is open resolves at once with `{ opened: false, reason: 'busy' }`.
 */

import * as React from 'react';
import type { AuthenticatedFetchFn, ResponseFetchFn } from '../../utils/fetchTypes';
import { DecisionWizard } from './DecisionWizard';
import { registerDecisionWizardHost } from './decisionWizardLauncher';
import type { DecisionWizardLaunchRequest, DecisionWizardLaunchResult } from './decisionWizardLauncher';
import { createDecisionWizardService } from './decisionWizardService';
import type {
  DecisionItemLoader,
  DecisionLookupSearch,
  DecisionOpenLookup,
  DecisionOpenRecord,
} from './decisionWizardTypes';

export interface DecisionWizardHostProps {
  /** `@spaarke/auth` authenticated fetch (ADR-028: a function, never a token). */
  authenticatedFetch: AuthenticatedFetchFn | ResponseFetchFn;
  /** Spaarke BFF base URL from the host's runtime config. */
  bffBaseUrl: string;
  /** The app-shell `--sprk-ui-scale` (`useUiScale()`). */
  uiScale?: number;
  /** Default item reader; a launch may bring its own. Without either the wizard says the item cannot be read. */
  loadItem?: DecisionItemLoader;
  /** Default search for Lookup / Person fields; a launch may bring its own. */
  lookup?: DecisionLookupSearch;
  /** Default side-pane lookup for Person / Lookup fields (D-182); a launch may bring its own. */
  openLookup?: DecisionOpenLookup;
  /** Default open-record-in-a-modal for the subject link (D-182); a launch may bring its own. */
  openRecord?: DecisionOpenRecord;
}

interface ActiveLaunch {
  key: number;
  request: DecisionWizardLaunchRequest;
  settle: (result: DecisionWizardLaunchResult) => void;
}

const NO_LOADER: DecisionItemLoader = () =>
  Promise.reject(new Error('This item cannot be opened here yet: no reader for it is connected.'));

export const DecisionWizardHost: React.FC<DecisionWizardHostProps> = ({
  authenticatedFetch,
  bffBaseUrl,
  uiScale,
  loadItem,
  lookup,
  openLookup,
  openRecord,
}) => {
  const [active, setActive] = React.useState<ActiveLaunch | null>(null);
  const activeRef = React.useRef<ActiveLaunch | null>(null);
  const sequence = React.useRef(0);

  React.useEffect(() => {
    const unregister = registerDecisionWizardHost(request => {
      if (activeRef.current) return Promise.resolve({ opened: false, reason: 'busy' as const });
      return new Promise<DecisionWizardLaunchResult>(resolve => {
        sequence.current += 1;
        const next: ActiveLaunch = { key: sequence.current, request, settle: resolve };
        activeRef.current = next;
        setActive(next);
      });
    });
    return () => {
      unregister();
      // Never leave a launcher awaiting a wizard that can no longer close.
      activeRef.current?.settle({ opened: true });
      activeRef.current = null;
      setActive(null);
    };
  }, []);

  const close = React.useCallback(() => {
    const current = activeRef.current;
    activeRef.current = null;
    setActive(null);
    current?.settle({ opened: true });
  }, []);

  const service = React.useMemo(
    () => createDecisionWizardService({ fetch: authenticatedFetch as ResponseFetchFn, baseUrl: bffBaseUrl }),
    [authenticatedFetch, bffBaseUrl]
  );

  if (!active) return null;
  const { request } = active;

  return (
    <DecisionWizard
      key={active.key}
      itemIds={request.visibleList}
      startItemId={request.itemId}
      service={service}
      loadItem={request.loadItem ?? loadItem ?? NO_LOADER}
      lookup={request.lookup ?? lookup}
      openLookup={request.openLookup ?? openLookup}
      openRecord={request.openRecord ?? openRecord}
      onClose={close}
      onRecorded={event => request.onRecorded?.({ signalId: event.signalId, alsoResolved: event.alsoResolved })}
      uiScale={uiScale}
    />
  );
};

DecisionWizardHost.displayName = 'DecisionWizardHost';
