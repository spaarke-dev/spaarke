/**
 * App — the channel-aware Communication Code Page body.
 *
 * Responsibilities (task 040 shell):
 *   1. Take the parsed `data=` params + resolved auth from the bootstrap.
 *   2. Read the `sprk_communication` record via `Xrm.WebApi` when an `id` is present.
 *   3. Delegate rendering to the TYPE-DRIVEN layout switch (`CommunicationLayout`),
 *      which keys off `sprk_communicationtype` — email interactive, others read-only.
 *
 * It does NOT mount `<EmailComposer />` (task 041) or `RegardingResolver`
 * (task 042) — those layer onto the seams inside `EmailComposerSlot`.
 */

import * as React from 'react';
import { makeStyles, tokens, Spinner, MessageBar, MessageBarBody, MessageBarTitle } from '@fluentui/react-components';
import type { AuthenticatedFetchFn } from '@spaarke/auth';
import type { ICommunicationPageParams } from './types/communication';
import { useCommunicationRecord } from './hooks/useCommunicationRecord';
import { buildNavCallbacks } from './services/navigation';
import { CommunicationLayout } from './components/CommunicationLayout';

export interface IAppProps {
  params: ICommunicationPageParams;
  /** ADR-028 function-based transport — the ONLY auth object passed in. */
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl: string;
}

const useStyles = makeStyles({
  root: {
    height: '100%',
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  centered: {
    flex: 1,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
});

export function App({ params, authenticatedFetch, bffBaseUrl }: IAppProps): React.JSX.Element {
  const styles = useStyles();
  const load = useCommunicationRecord(params.id);
  const nav = React.useMemo(() => buildNavCallbacks(), []);

  return (
    <div className={styles.root}>
      {params.warnings.length > 0 && (
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>Check the launch parameters</MessageBarTitle>
            {params.warnings.join(' ')}
          </MessageBarBody>
        </MessageBar>
      )}

      {load.status === 'loading' && (
        <div className={styles.centered}>
          <Spinner label="Loading communication…" />
        </div>
      )}

      {load.status === 'error' && (
        <div className={styles.centered}>
          <MessageBar intent="error">
            <MessageBarBody>
              <MessageBarTitle>Could not load the communication</MessageBarTitle>
              {load.error}
            </MessageBarBody>
          </MessageBar>
        </div>
      )}

      {load.status === 'loaded' && (
        <CommunicationLayout
          params={params}
          record={load.record}
          authenticatedFetch={authenticatedFetch}
          bffBaseUrl={bffBaseUrl}
          nav={nav}
        />
      )}
    </div>
  );
}
