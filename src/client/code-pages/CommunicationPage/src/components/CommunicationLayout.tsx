/**
 * TYPE-DRIVEN LAYOUT SWITCH — the NFR-04 extensibility seam.
 *
 * `renderByCommunicationType` selects the renderer purely from the
 * `sprk_communicationtype` OPTION-SET VALUE (a number). It never branches on the
 * string literal "email" and never on the `*typename` label — the whole point of
 * FR-16 is that the channel *value* keys the layout, so a future channel is a new
 * registry entry (or falls through to the generic read-only renderer) with NO
 * change to the shell contract.
 *
 * Registry semantics:
 *   - `CommunicationType.Email` → interactive slot (task 041 composer seam)
 *   - Teams / SMS / Notification → generic read-only renderer
 *   - any unregistered value → generic read-only renderer (safe default)
 */

import * as React from 'react';
import { makeStyles, MessageBar, MessageBarBody, MessageBarTitle } from '@fluentui/react-components';
import type { AuthenticatedFetchFn } from '@spaarke/auth';
import {
  CommunicationType,
  type ICommunicationNavCallbacks,
  type ICommunicationPageParams,
  type ICommunicationRecord,
} from '../types/communication';
import { EmailComposerSlot } from './EmailComposerSlot';
import { ReadOnlyCommunicationView } from './ReadOnlyCommunicationView';

/** Everything a renderer might need, assembled once by the shell. */
export interface ILayoutContext {
  params: ICommunicationPageParams;
  /** Null for compose (no record yet). */
  record: ICommunicationRecord | null;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl: string;
  nav: ICommunicationNavCallbacks;
}

type ChannelKind = 'interactive' | 'read-only';

interface IChannelDescriptor {
  kind: ChannelKind;
  /** Fallback label when the record has no formatted `*typename`. */
  label: string;
  render: (ctx: ILayoutContext) => React.JSX.Element;
}

const useStyles = makeStyles({
  // Renderers own their own padding; the wrapper just fills the iframe height.
  fill: { height: '100%' },
});

function renderInteractiveEmail(ctx: ILayoutContext): React.JSX.Element {
  return (
    <EmailComposerSlot
      mode={ctx.params.mode}
      record={ctx.record}
      initialTo={ctx.params.to}
      initialCc={ctx.params.cc}
      initialSubject={ctx.params.subject ?? ctx.record?.sprk_subject ?? undefined}
      initialBody={ctx.params.body ?? ctx.record?.sprk_body ?? undefined}
      associations={ctx.params.associations}
      authenticatedFetch={ctx.authenticatedFetch}
      bffBaseUrl={ctx.bffBaseUrl}
      nav={ctx.nav}
    />
  );
}

function renderReadOnly(label: string) {
  return function readOnlyRenderer(ctx: ILayoutContext): React.JSX.Element {
    // Read-only channels require a persisted record.
    if (!ctx.record) {
      return <MissingRecordNotice channelLabel={label} />;
    }
    return (
      <ReadOnlyCommunicationView
        channelLabel={ctx.record.sprk_communicationtypename ?? label}
        record={ctx.record}
        nav={ctx.nav}
      />
    );
  };
}

/**
 * The channel registry — keyed by the `sprk_communicationtype` option-set VALUE.
 * Add a future channel here (or let it fall through to the read-only default).
 */
const CHANNEL_REGISTRY: Record<number, IChannelDescriptor> = {
  [CommunicationType.Email]: { kind: 'interactive', label: 'Email', render: renderInteractiveEmail },
  [CommunicationType.TeamsMessage]: {
    kind: 'read-only',
    label: 'Teams Message',
    render: renderReadOnly('Teams Message'),
  },
  [CommunicationType.Sms]: { kind: 'read-only', label: 'SMS', render: renderReadOnly('SMS') },
  [CommunicationType.Notification]: {
    kind: 'read-only',
    label: 'Notification',
    render: renderReadOnly('Notification'),
  },
};

/** Safe default for any unregistered / null channel value. */
const DEFAULT_DESCRIPTOR: IChannelDescriptor = {
  kind: 'read-only',
  label: 'Communication',
  render: renderReadOnly('Communication'),
};

/**
 * Resolve the effective channel value. `compose` mode has no record and is the
 * email branch at launch (reference §7.5), so it resolves to `Email` without a
 * "compose" special-case string.
 */
export function resolveChannelValue(record: ICommunicationRecord | null, params: ICommunicationPageParams): number {
  if (record?.sprk_communicationtype != null) {
    return record.sprk_communicationtype;
  }
  // No record (compose): email is the only interactive channel at launch.
  if (params.mode === 'compose') {
    return CommunicationType.Email;
  }
  return record?.sprk_communicationtype ?? CommunicationType.Email;
}

/** THE SWITCH — pick a renderer by option-set value. */
export function renderByCommunicationType(typeValue: number, ctx: ILayoutContext): React.JSX.Element {
  const descriptor = CHANNEL_REGISTRY[typeValue] ?? DEFAULT_DESCRIPTOR;
  return descriptor.render(ctx);
}

/** React entry point: resolves the channel value then delegates to the switch. */
export function CommunicationLayout(props: ILayoutContext): React.JSX.Element {
  const styles = useStyles();
  const typeValue = resolveChannelValue(props.record, props.params);
  return <div className={styles.fill}>{renderByCommunicationType(typeValue, props)}</div>;
}

function MissingRecordNotice({ channelLabel }: { channelLabel: string }): React.JSX.Element {
  return (
    <MessageBar intent="warning">
      <MessageBarBody>
        <MessageBarTitle>No record to display</MessageBarTitle>
        {channelLabel} communications are read-only and require an existing record (open with{' '}
        <code>mode=view&amp;id=…</code>).
      </MessageBarBody>
    </MessageBar>
  );
}
