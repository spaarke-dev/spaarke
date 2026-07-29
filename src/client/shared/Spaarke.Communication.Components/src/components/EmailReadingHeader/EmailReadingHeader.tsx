/**
 * EmailReadingHeader — the reading-pane HEADER BAND (email-communication-
 * solution-r5, reading-pane layout redesign; supersedes task 034's original
 * From/To/Cc/Bcc header, spec FR-11). Fills the `EmailReadingPaneShell`
 * `renderHeader` slot — rendered ABOVE the full-width `EmailToolbar`.
 *
 * Layout: the **Subject** prominent (Segoe UI, ~20px, semibold) on the left;
 * on the right, the tracking trio (Monitor / High priority / Access —
 * `EmailTrackingPanel` rendered `compact`) plus a small, demoted "Open full
 * form" icon-only button. From/To/Cc/Bcc moved OUT of this band — they now
 * render in the dedicated `<EmailRecipients />` block (compose-style, always-
 * From/To + conditional Cc/Bcc) directly above the body.
 *
 * BUG FIX (this task): this component used to run its OWN
 * `retrieveRecord('sprk_communication', id, $select)` call — a SECOND,
 * redundant per-selection Dataverse read (the composition root,
 * `EmailWorkspace`, already owns the ONE per-selection read via
 * `useEmailWorkspaceRecord`/`EmailWorkspace.mapping.ts`). That `$select`
 * string call failed at runtime, surfacing "Could not load this email
 * header." on every selection. This component is now PURELY presentational —
 * every value + write callback arrives as a prop, fed by the host from the
 * shared `EmailWorkspaceRecordState`. There is no fetch, no loading state, no
 * error state left in this file.
 *
 * React-version note (ADR-022/NFR-05): `React.FC` + standard hooks only — no
 * React-18/19-only runtime API and no `as React.ComponentType` cast. Fluent v9
 * semantic tokens only (ADR-021) — themes correctly via the host `FluentProvider`.
 */
import * as React from 'react';
import { makeStyles, tokens, Text, Toolbar } from '@fluentui/react-components';
import { EmailTrackingPanel } from '../EmailAssociationsAndTracking';
import { OpenFullFormButton } from '../EmailComposeActions';
import type { IEmailReadingHeaderProps } from './EmailReadingHeader.types';

const useStyles = makeStyles({
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalL,
    padding: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalXL,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  title: {
    flex: 1,
    minWidth: 0,
    fontFamily: '"Segoe UI", system-ui, sans-serif',
  },
  trackingCompact: {
    flexShrink: 0,
  },
  openFullFormToolbar: {
    flexShrink: 0,
    minWidth: 0,
    padding: 0,
  },
});

export const EmailReadingHeader: React.FC<IEmailReadingHeaderProps> = ({
  communicationId,
  subject,
  monitor,
  highPriority,
  accessPermission,
  accessPermissionOptions,
  onMonitorChange,
  onHighPriorityChange,
  onAccessPermissionChange,
  onOpenFullForm,
}) => {
  const s = useStyles();

  return (
    <div className={s.header} data-testid="email-reading-header">
      <Text as="h1" size={500} weight="semibold" className={s.title} truncate wrap={false}>
        {subject || '(no subject)'}
      </Text>

      <div className={s.trackingCompact}>
        <EmailTrackingPanel
          compact
          monitor={monitor}
          highPriority={highPriority}
          accessPermission={accessPermission}
          accessPermissionOptions={accessPermissionOptions}
          onMonitorChange={onMonitorChange}
          onHighPriorityChange={onHighPriorityChange}
          onAccessPermissionChange={onAccessPermissionChange}
        />
      </div>

      {/* `OpenFullFormButton` wraps a Fluent `ToolbarButton` (task 036) — hosting
          it inside its own single-button `Toolbar` matches Fluent's expected
          parent context (the shell's OWN `EmailToolbar` is a separate, sibling
          `Toolbar` instance below this band). Demoted per the redesign: icon
          only, right-aligned, no label. */}
      <Toolbar aria-label="Record actions" className={s.openFullFormToolbar}>
        <OpenFullFormButton communicationId={communicationId} onOpenFullForm={onOpenFullForm} />
      </Toolbar>
    </div>
  );
};

EmailReadingHeader.displayName = 'EmailReadingHeader';
