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
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
  },
  // owner UAT 2026-07-30 R2 item 6 — the "From" label is PLAIN text ("From:"), not
  // a boxed field, matching the composer's own From treatment (the compose agent is
  // applying the same on the compose side; keep them visually consistent). Segoe UI
  // 14px semibold via semantic tokens (Segoe UI is Fluent's default family, so no
  // explicit `fontFamily`); the address value follows on the same line.
  fromLabel: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
  },
  // Boxed field label — copied VERBATIM from the compose composer's `RecipientField`
  // `labelBox` (owner UAT: "must match the Compose form"): same min-width, padding,
  // border stroke, radius, background, foreground, and font size. Used by To/Cc/Bcc;
  // From uses the plain `fromLabel` above (item 6).
  label: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    flexShrink: 0,
    minWidth: '44px',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase300,
  },
  value: {
    minWidth: 0,
    color: tokens.colorNeutralForeground1,
    overflowWrap: 'anywhere',
    // Email addresses: Segoe UI 14px (owner UAT).
    fontFamily: '"Segoe UI", system-ui, sans-serif',
    fontSize: tokens.fontSizeBase300,
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
        <Text className={s.fromLabel}>From:</Text>
        <Text className={s.value}>{from || ''}</Text>
      </div>
      <div className={s.row}>
        <Text className={s.label}>To</Text>
        <Text className={s.value}>{to || ''}</Text>
      </div>
      {cc && (
        <div className={s.row}>
          <Text className={s.label}>Cc</Text>
          <Text className={s.value}>{cc}</Text>
        </div>
      )}
      {bcc && (
        <div className={s.row}>
          <Text className={s.label}>Bcc</Text>
          <Text className={s.value}>{bcc}</Text>
        </div>
      )}
    </div>
  );
};

EmailRecipients.displayName = 'EmailRecipients';
