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
import type { ConsoleState, ConsoleStatusOverride, RecordClass } from './consoleStatus';

export interface StatusBarProps {
  state: ConsoleState;
  /**
   * Replaces the label and tone the Console table gives `state` (the bar intent follows the override tone). Use it for
   * outcomes the Console has no state for; `state` is then only echoed in `data-state`. Absent: output is unchanged.
   */
  statusOverride?: ConsoleStatusOverride;
  /** Decision Record id, e.g. "DR-0042". */
  recordId?: string;
  recordClass?: RecordClass | null;
  /** Who decided (display name). Absent for items that closed with no person. */
  decidedBy?: string | null;
  /** When it closed (ISO 8601). */
  decidedOn?: string | null;
  /**
   * True in the moment a decision was just recorded in this session (task 058): the bar leads with "Recorded". A bar for an
   * item that was already closed when opened leaves this off.
   */
  recorded?: boolean;
  /** For `Dismissed`: why. Shown as "Reason: ...". */
  reason?: string | null;
  /** For `Superseded`: what replaced it (a record number or name). Shown as "Superseded by ...". */
  supersededBy?: string | null;
  /** A short qualifier that follows the state, for example "Inquiry sent, still open" (task 058). */
  note?: string | null;
  /** How many other open items are left on the same card after this one closed. `0` reads "No other open items". */
  openItemsLeft?: number | null;
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
  {
    state,
    statusOverride,
    recordId,
    recordClass,
    decidedBy,
    decidedOn,
    recorded,
    reason,
    supersededBy,
    note,
    openItemsLeft,
    className,
  },
  ref
) {
  const styles = useStyles();
  const mapped = CONSOLE_STATE_BADGES[state];
  const badge = statusOverride ?? mapped;
  const badgeDescription = statusOverride ? statusOverride.description : mapped.description;
  const when = decidedOn && !Number.isNaN(new Date(decidedOn).getTime()) ? new Date(decidedOn).toLocaleString() : null;
  const left =
    typeof openItemsLeft === 'number'
      ? openItemsLeft > 0
        ? `${openItemsLeft} other open ${openItemsLeft === 1 ? 'item' : 'items'} left`
        : 'No other open items'
      : null;
  const parts = [
    recorded ? 'Recorded' : null,
    recordId,
    recordClass,
    decidedBy ? `by ${decidedBy}` : null,
    when,
    reason?.trim() ? `Reason: ${reason.trim()}` : null,
    supersededBy?.trim() ? `Superseded by ${supersededBy.trim()}` : null,
    note?.trim() ? note.trim() : null,
    left,
  ].filter((p): p is string => !!p);

  const intent = statusOverride
    ? TONE_TO_INTENT[badge.tone]
    : (STATE_INTENT_OVERRIDE[state] ?? TONE_TO_INTENT[badge.tone]);

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
        <StatusBadge
          label={badge.label}
          tone={badge.tone}
          ariaLabel={badgeDescription ? `${badge.label}. ${badgeDescription}` : badge.label}
        />
        {parts.length > 0 && <span className={styles.meta}>{parts.join(' · ')}</span>}
      </MessageBarBody>
    </MessageBar>
  );
});

StatusBar.displayName = 'StatusBar';
