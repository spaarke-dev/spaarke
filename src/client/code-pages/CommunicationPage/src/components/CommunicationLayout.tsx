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
import { makeStyles, tokens, MessageBar, MessageBarBody, MessageBarTitle } from '@fluentui/react-components';
import type { AuthenticatedFetchFn } from '@spaarke/auth';
import {
  CommunicationType,
  type ICommunicationNavCallbacks,
  type ICommunicationPageParams,
  type ICommunicationRecord,
} from '../types/communication';
import { EmailComposerSlot } from './EmailComposerSlot';
import { ReadOnlyCommunicationView } from './ReadOnlyCommunicationView';
import { CommunicationHeader } from './CommunicationHeader';
import { ConnectionsEditor } from './ConnectionsEditor';
import { parseProvenance } from './provenance';

/** Prototype-only layout options for hosting the connections editor. */
export type ReviewVariant = 'slim' | 'card' | 'rail';

/** Everything a renderer might need, assembled once by the shell. */
export interface ILayoutContext {
  params: ICommunicationPageParams;
  /** Null for compose (no record yet). */
  record: ICommunicationRecord | null;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl: string;
  nav: ICommunicationNavCallbacks;
  /** Prototype-only: which association-review layout to render (harness comparison). */
  reviewVariant?: ReviewVariant;
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
  // Composed record layout: header band + review panel + channel content, scrolls as one.
  page: {
    height: '100%',
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground2,
    overflow: 'auto',
  },
  reviewWrap: { padding: tokens.spacingHorizontalXL, paddingBottom: 0 },
  body: { flex: 1, minHeight: 0, padding: tokens.spacingHorizontalXL, paddingTop: tokens.spacingVerticalL },
  bodyCard: {
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusLarge,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    overflow: 'auto',
  },
  // Right-rail (variant B) two-column arrangement.
  twoCol: { flex: 1, minHeight: 0, display: 'flex' },
  mainCol: { flex: 1, minWidth: 0, overflow: 'auto' },
  // ~34% "accessories" column, mirroring the OOB model-driven form's 66/34 split.
  railCol: {
    width: '34%',
    minWidth: '340px',
    maxWidth: '520px',
    flexShrink: 0,
    overflow: 'auto',
    borderLeft: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
  },
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
  const channelContent = renderByCommunicationType(typeValue, props);

  // Compose (no persisted record): just the composer — no header/review chrome.
  if (!props.record) {
    return <div className={styles.fill}>{channelContent}</div>;
  }

  // Persisted record: composed entity-form layout — header + association review + content.
  const provenance = parseProvenance(props.record.sprk_associationprovenance);
  const variant: ReviewVariant = props.reviewVariant ?? 'slim';
  const record = props.record;

  // B — right context rail: email main + connections rail (header spans top).
  if (variant === 'rail') {
    return (
      <div className={styles.page}>
        <CommunicationHeader record={record} />
        <div className={styles.twoCol}>
          <div className={styles.mainCol}>{channelContent}</div>
          <div className={styles.railCol}>
            <ConnectionsEditor record={record} provenance={provenance} layout="rail" />
          </div>
        </div>
      </div>
    );
  }

  // A — slim summary bar (edge-to-edge) / C — connections card, both above the email.
  return (
    <div className={styles.page}>
      <CommunicationHeader record={record} />
      {variant === 'slim' ? (
        <ConnectionsEditor record={record} provenance={provenance} layout="summary" />
      ) : (
        <div className={styles.reviewWrap}>
          <ConnectionsEditor record={record} provenance={provenance} layout="card" />
        </div>
      )}
      <div className={styles.body}>
        <div className={styles.bodyCard}>{channelContent}</div>
      </div>
    </div>
  );
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
