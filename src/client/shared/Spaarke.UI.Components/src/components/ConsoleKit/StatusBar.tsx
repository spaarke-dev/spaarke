/**
 * StatusBar — how a decided item closed, shown at the top of a decided item. A composition on Fluent
 * `MessageBar` (D-46) with the mapped `StatusBadge`; one bar per decided item.
 *
 * Read-only by construction (FR-21, R-13): no action, edit or delete affordance. Forwards a ref and is
 * focusable programmatically (tabIndex -1) so a host can move focus to it after recording.
 *
 * Task: spaarke-ontology-platform-r1, task 057.
 */

import * as React from 'react';
import { MessageBar, MessageBarBody, makeStyles, tokens } from '@fluentui/react-components';
import type { MessageBarProps } from '@fluentui/react-components';
import { StatusBadge } from '../StatusBadge';
import type { StatusBadgeTone } from '../StatusBadge';
import { CONSOLE_STATE_BADGES } from './consoleStatus';
import type { ConsoleState, RecordClass } from './consoleStatus';

export interface StatusBarProps {
  state: ConsoleState;
  /** Decision Record id, e.g. "DR-0042". */
  recordId?: string;
  recordClass?: RecordClass | null;
  /** Who decided (display name). Absent for items that closed with no person. */
  decidedBy?: string | null;
  /** When it closed (ISO 8601). */
  decidedOn?: string | null;
  className?: string;
}

const TONE_TO_INTENT: Record<StatusBadgeTone, NonNullable<MessageBarProps['intent']>> = {
  neutral: 'info',
  info: 'info',
  success: 'success',
  warning: 'warning',
  critical: 'error',
};

/** Where a state's bar intent differs from its badge tone (HANDOFF section 1.1): a dismissal is information, not a warning. */
const STATE_INTENT_OVERRIDE: Partial<Record<ConsoleState, NonNullable<MessageBarProps['intent']>>> = {
  Dismissed: 'info',
};

const useStyles = makeStyles({
  body: { display: 'flex', alignItems: 'center', flexWrap: 'wrap', columnGap: tokens.spacingHorizontalS },
  meta: { color: tokens.colorNeutralForeground3 },
});

export const StatusBar = React.forwardRef<HTMLDivElement, StatusBarProps>(function StatusBar(
  { state, recordId, recordClass, decidedBy, decidedOn, className },
  ref
) {
  const styles = useStyles();
  const badge = CONSOLE_STATE_BADGES[state];
  const when = decidedOn && !Number.isNaN(new Date(decidedOn).getTime()) ? new Date(decidedOn).toLocaleString() : null;
  const parts = [recordId, recordClass, decidedBy ? `by ${decidedBy}` : null, when].filter((p): p is string => !!p);

  const intent = STATE_INTENT_OVERRIDE[state] ?? TONE_TO_INTENT[badge.tone];

  return (
    <MessageBar
      ref={ref}
      intent={intent}
      icon={null}
      tabIndex={-1}
      className={className}
      data-testid="status-bar"
      data-state={state}
      data-intent={intent}
    >
      <MessageBarBody className={styles.body}>
        <StatusBadge label={badge.label} tone={badge.tone} ariaLabel={`${badge.label}. ${badge.description}`} />
        {parts.length > 0 && <span className={styles.meta}>{parts.join(' · ')}</span>}
      </MessageBarBody>
    </MessageBar>
  );
});

StatusBar.displayName = 'StatusBar';
