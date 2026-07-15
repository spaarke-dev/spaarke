/**
 * Communication entity-form header (W4 / ref §5.10 "first-class entity-form feel").
 *
 * A composed header band for a persisted communication: subject as the record
 * title, channel + status pills, and the participant/received metadata line.
 * Token-based + theme-aware; frames the review panel + channel content below.
 */

import * as React from 'react';
import { makeStyles, tokens, Text, Badge } from '@fluentui/react-components';
import { Mail20Regular, Chat20Regular, ChatBubblesQuestion20Regular, Alert20Regular } from '@fluentui/react-icons';
import { AssociationStatus, CommunicationType, type ICommunicationRecord } from '../types/communication';

const useStyles = makeStyles({
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingVerticalL,
    paddingInline: tokens.spacingHorizontalXL,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  titleRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM },
  channelIcon: { color: tokens.colorBrandForeground1, fontSize: '20px' },
  title: { flex: 1, minWidth: 0 },
  pills: { display: 'flex', gap: tokens.spacingHorizontalS, alignItems: 'center' },
  meta: { display: 'flex', gap: tokens.spacingHorizontalL, flexWrap: 'wrap', color: tokens.colorNeutralForeground3 },
  metaItem: { display: 'flex', gap: tokens.spacingHorizontalXS, alignItems: 'baseline' },
  metaLabel: { color: tokens.colorNeutralForeground4 },
});

const CHANNEL_ICON: Record<number, React.JSX.Element> = {
  [CommunicationType.Email]: <Mail20Regular />,
  [CommunicationType.TeamsMessage]: <Chat20Regular />,
  [CommunicationType.Sms]: <ChatBubblesQuestion20Regular />,
  [CommunicationType.Notification]: <Alert20Regular />,
};

const STATUS_PILL: Record<number, { label: string; color: 'success' | 'warning' | 'danger' | 'informative' }> = {
  [AssociationStatus.Resolved]: { label: 'Filed', color: 'success' },
  [AssociationStatus.Suggested]: { label: 'Suggested', color: 'warning' },
  [AssociationStatus.PendingReview]: { label: 'Pending Review', color: 'informative' },
  [AssociationStatus.Unresolved]: { label: 'Pending Review', color: 'informative' },
  [AssociationStatus.Ambiguous]: { label: 'Ambiguous', color: 'danger' },
};

function fmtDate(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const d = new Date(iso);
  if (isNaN(d.getTime())) return null;
  return d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
}

export function CommunicationHeader({ record }: { record: ICommunicationRecord }): React.JSX.Element {
  const s = useStyles();
  const type = record.sprk_communicationtype ?? CommunicationType.Email;
  const status = record.sprk_associationstatus != null ? STATUS_PILL[record.sprk_associationstatus] : undefined;
  const received = fmtDate(record.sprk_receiveddate);

  return (
    <div className={s.header}>
      <div className={s.titleRow}>
        <span className={s.channelIcon}>{CHANNEL_ICON[type] ?? <Mail20Regular />}</span>
        <Text as="h1" size={500} weight="semibold" className={s.title} truncate wrap={false}>
          {record.sprk_subject || '(no subject)'}
        </Text>
        <div className={s.pills}>
          <Badge appearance="tint" color="brand">
            {record.sprk_communicationtypename ?? 'Communication'}
          </Badge>
          {status && (
            <Badge appearance="filled" color={status.color}>
              {status.label}
            </Badge>
          )}
        </div>
      </div>
      <div className={s.meta}>
        {record.sprk_from && (
          <span className={s.metaItem}>
            <Text size={200} className={s.metaLabel}>
              From
            </Text>
            <Text size={200}>{record.sprk_from}</Text>
          </span>
        )}
        {record.sprk_to && (
          <span className={s.metaItem}>
            <Text size={200} className={s.metaLabel}>
              To
            </Text>
            <Text size={200}>{record.sprk_to}</Text>
          </span>
        )}
        {received && (
          <span className={s.metaItem}>
            <Text size={200} className={s.metaLabel}>
              {record.sprk_directionname === 'Outgoing' ? 'Sent' : 'Received'}
            </Text>
            <Text size={200}>{received}</Text>
          </span>
        )}
      </div>
    </div>
  );
}
