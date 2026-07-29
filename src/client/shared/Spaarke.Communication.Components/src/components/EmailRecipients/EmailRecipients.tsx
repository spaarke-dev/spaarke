/**
 * EmailRecipients.tsx
 *
 * Reading-pane RECIPIENTS block (email-communication-solution-r5, reading-
 * pane layout redesign). Sits between the full-width `EmailToolbar` and the
 * email body — labeled rows **From / To** always; **Cc / Bcc only when they
 * have a value** (the row is omitted entirely when empty, matching the
 * compose composer's own behavior — never an empty "Cc:" row).
 *
 * Purely presentational, read-only: values in, nothing out. Fed by the
 * composition root (`EmailWorkspace`) from `EmailWorkspaceRecordState`
 * (`EmailWorkspace.mapping.ts`) — the same no-`$select` per-selection read
 * `useEmailWorkspaceRecord` already owns; this component adds no fetch of
 * its own.
 *
 * React-version note (ADR-022/NFR-05): `React.FC` + standard hooks only — no
 * React-18/19-only runtime API and no `as React.ComponentType` cast. Fluent v9
 * semantic tokens only (ADR-021) — themes correctly via the host `FluentProvider`.
 */
import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    padding: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalXL,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  row: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'baseline',
    gap: tokens.spacingHorizontalM,
  },
  label: {
    flexShrink: 0,
    width: '44px',
    color: tokens.colorNeutralForeground3,
  },
  value: {
    minWidth: 0,
    color: tokens.colorNeutralForeground1,
    overflowWrap: 'anywhere',
  },
});

export interface EmailRecipientsProps {
  /** `EmailWorkspaceRecordState.from` — rendered even when empty (always-visible row, mirrors the compose composer). */
  from: string | null;
  /** `EmailWorkspaceRecordState.to` — rendered even when empty (always-visible row). */
  to: string | null;
  /** `EmailWorkspaceRecordState.cc` — row hidden entirely when falsy/empty. */
  cc?: string | null;
  /** `EmailWorkspaceRecordState.bcc` — row hidden entirely when falsy/empty. */
  bcc?: string | null;
}

export const EmailRecipients: React.FC<EmailRecipientsProps> = ({ from, to, cc, bcc }) => {
  const s = useStyles();

  return (
    <div className={s.root} data-testid="email-recipients">
      <div className={s.row}>
        <Text className={s.label} size={200}>
          From
        </Text>
        <Text className={s.value} size={200}>
          {from || ''}
        </Text>
      </div>
      <div className={s.row}>
        <Text className={s.label} size={200}>
          To
        </Text>
        <Text className={s.value} size={200}>
          {to || ''}
        </Text>
      </div>
      {cc && (
        <div className={s.row}>
          <Text className={s.label} size={200}>
            Cc
          </Text>
          <Text className={s.value} size={200}>
            {cc}
          </Text>
        </div>
      )}
      {bcc && (
        <div className={s.row}>
          <Text className={s.label} size={200}>
            Bcc
          </Text>
          <Text className={s.value} size={200}>
            {bcc}
          </Text>
        </div>
      )}
    </div>
  );
};

EmailRecipients.displayName = 'EmailRecipients';
