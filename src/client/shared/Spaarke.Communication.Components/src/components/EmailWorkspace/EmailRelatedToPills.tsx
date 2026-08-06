/**
 * EmailRelatedToPills.tsx
 *
 * Read-only display of the email's CONFIRMED associations as pills — the
 * "Related to" section body (email-communication-solution-r5, reading-pane
 * MAIN-AREA redesign, section #5). Mirrors the compose form's "Related to"
 * chip display: one pill per filed regarding record (Matter / Project /
 * Contact / …), each with its entity icon + type + record name. This is the
 * confirmed-state display ONLY — every write affordance (confirm / change /
 * remove / link-another) lives in the separate Association resolver (section
 * #6, `EmailConnectionsReview`), per the owner's two-section (option B) choice.
 *
 * Fed straight from `EmailWorkspaceRecordState.filedAssociations` (the record's
 * actually-populated `sprk_regarding*` lookups). Fluent v9 tokens only
 * (ADR-021, dark-mode correct). `React.FC` — no `as React.ComponentType` cast
 * (NFR-05).
 */
import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { entityLabel, type FiledAssociation } from '../../logic/connections';
import { entityIcon } from '../EmailAssociationsAndTracking/EmailConnectionsReview.helpers';

const useStyles = makeStyles({
  root: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalS },
  pill: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    maxWidth: '100%',
    paddingBlock: tokens.spacingVerticalXXS,
    paddingInline: tokens.spacingHorizontalSNudge,
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorNeutralBackground3,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  icon: { color: tokens.colorNeutralForeground3, display: 'flex', flexShrink: 0, fontSize: '16px' },
  type: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200, flexShrink: 0 },
  name: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  empty: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
});

export interface EmailRelatedToPillsProps {
  /** The record's confirmed regarding lookups (`EmailWorkspaceRecordState.filedAssociations`). */
  associations: FiledAssociation[];
}

export const EmailRelatedToPills: React.FC<EmailRelatedToPillsProps> = ({ associations }) => {
  const s = useStyles();

  if (associations.length === 0) {
    return <Text className={s.empty}>Not filed to any record yet.</Text>;
  }

  return (
    <div className={s.root} data-testid="email-related-to-pills">
      {associations.map(a => (
        <span
          key={`${a.entityType}:${a.recordId}`}
          className={s.pill}
          title={`${entityLabel(a.entityType)} · ${a.recordName}`}
        >
          <span className={s.icon}>{entityIcon(a.entityType)}</span>
          <Text className={s.type}>{entityLabel(a.entityType)}</Text>
          <Text className={s.name}>{a.recordName}</Text>
        </span>
      ))}
    </div>
  );
};

EmailRelatedToPills.displayName = 'EmailRelatedToPills';
