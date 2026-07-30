/**
 * EmailReadingHeader — the reading-pane TITLE BAR (email-communication-
 * solution-r5, reading-pane MAIN-AREA redesign, section #1). Fills the
 * `EmailReadingPaneShell` `renderHeader` slot — the email **Subject** on its
 * own row with a **light-gray background**, rendered ABOVE the full-width
 * `EmailToolbar`.
 *
 * The prior header band carried the subject PLUS a compact tracking trio PLUS
 * an "Open full form" icon. Per the locked owner design:
 *   - the tracking trio (Monitor / High priority / Access) is REMOVED from the
 *     reading pane entirely,
 *   - "Open full form" moved into the toolbar (demoted icon), and
 *   - From/To/Cc/Bcc live in the dedicated `<EmailRecipients />` block.
 * What remains here is just the light-gray title bar showing the subject.
 *
 * Purely presentational — subject in, nothing out; no fetch, no loading/error
 * state. Fed by the host from the shared `EmailWorkspaceRecordState`.
 *
 * React-version note (ADR-022/NFR-05): `React.FC` only — no `as
 * React.ComponentType` cast. Fluent v9 semantic tokens only (ADR-021) — themes
 * correctly via the host `FluentProvider` (the "light gray" is a token, so it
 * resolves in both light and dark mode).
 */
import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import type { IEmailReadingHeaderProps } from './EmailReadingHeader.types';

const useStyles = makeStyles({
  titleBar: {
    display: 'flex',
    alignItems: 'center',
    flexShrink: 0,
    // Shorter title bar (owner UAT) — tighter vertical rhythm above the toolbar.
    paddingBlock: tokens.spacingVerticalXS,
    paddingInline: tokens.spacingHorizontalXL,
    // Light-gray title bar — a semantic token, not a hardcoded hex, so it
    // reads as a subtle gray in light mode and the correct elevated surface in
    // dark mode (ADR-021).
    backgroundColor: tokens.colorNeutralBackground3,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  title: {
    minWidth: 0,
    fontFamily: '"Segoe UI", system-ui, sans-serif',
  },
});

export const EmailReadingHeader: React.FC<IEmailReadingHeaderProps> = ({ subject }) => {
  const s = useStyles();

  return (
    <div className={s.titleBar} data-testid="email-reading-header">
      <Text as="h1" size={500} weight="semibold" className={s.title} truncate wrap={false}>
        {subject || '(no subject)'}
      </Text>
    </div>
  );
};

EmailReadingHeader.displayName = 'EmailReadingHeader';
