/**
 * Notices and badges of the Rule Review page (ebilling-r1 task 077). Fluent v9 semantic tokens only (ADR-021): nothing here names a
 * colour, so light, dark and high-contrast follow the host theme.
 */

import * as React from 'react';
import {
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { StatusBadge, type StatusBadgeTone } from '@spaarke/ui-components';
import { describeFailure, type ApiFailure, type ProposalView } from '../services/ruleSetApi';
import type { ChangeLine } from '../services/reviewActions';

const useStyles = makeStyles({
  list: { margin: 0, marginTop: tokens.spacingVerticalXS, paddingLeft: tokens.spacingHorizontalL },
  code: { color: tokens.colorNeutralForeground3, fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200 },
  change: { display: 'block' },
  before: { textDecorationLine: 'line-through', color: tokens.colorNeutralForeground3 },
});

export type NoticeIntent = 'error' | 'warning' | 'success' | 'info';

export interface Notice {
  intent: NoticeIntent;
  title?: string;
  text?: string;
  failure?: ApiFailure;
  changes?: ChangeLine[];
}

export const failureNotice = (failure: ApiFailure, title?: string): Notice => ({
  intent: 'error', title: title ?? failure.title ?? 'The request failed', failure,
});

/**
 * A banner for a failure, a warning or a result. A failure shows the server's detail with its stable code and, for a 400, the
 * per-rule errors (rule, field, message); what changed in a rule after it was reloaded is listed under the text.
 */
export const NoticeBar: React.FC<{ notice: Notice; onDismiss?: () => void }> = ({ notice, onDismiss }) => {
  const s = useStyles();
  const { failure } = notice;
  return (
    <MessageBar intent={notice.intent} layout="multiline" role={notice.intent === 'error' ? 'alert' : 'status'} data-testid="notice-bar">
      <MessageBarBody>
        {notice.title && <MessageBarTitle>{notice.title}</MessageBarTitle>}
        {notice.text}
        {failure && notice.text === undefined && ` ${describeFailure(failure)}`}
        {failure && failure.errors.length > 0 && (
          <ul className={s.list} data-testid="notice-errors">
            {failure.errors.map((e, i) => (
              <li key={`${e.policyId ?? 'x'}-${e.field}-${i}`}>
                {e.rule ? <strong>{e.rule}</strong> : null}
                {e.rule ? ' - ' : ''}
                {e.field ? `${e.field}: ` : ''}
                {e.message}
                {e.reason ? <span className={s.code}> ({e.reason})</span> : null}
              </li>
            ))}
          </ul>
        )}
        {notice.changes && notice.changes.length > 0 && (
          <ul className={s.list} data-testid="notice-changes">
            {notice.changes.map(c => (
              <li key={c.field}>
                <span className={s.change}>
                  <strong>{c.field}</strong>: <span className={s.before}>{c.before}</span> {'→'} {c.after}
                </span>
              </li>
            ))}
          </ul>
        )}
      </MessageBarBody>
      {onDismiss && (
        <MessageBarActions>
          <Button appearance="transparent" size="small" onClick={onDismiss}>
            Dismiss
          </Button>
        </MessageBarActions>
      )}
    </MessageBar>
  );
};

/** Full-page notices: the eBilling flag is off, or the caller is not a billing admin. */
const usePage = makeStyles({
  page: {
    display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', height: '100%',
    gap: tokens.spacingVerticalM, padding: tokens.spacingHorizontalXXL, textAlign: 'center',
    backgroundColor: tokens.colorNeutralBackground1, color: tokens.colorNeutralForeground1,
  },
});

export const PageNotice: React.FC<{ testId: string; title: string; children: React.ReactNode }> = ({ testId, title, children }) => {
  const s = usePage();
  return (
    <div className={s.page} data-testid={testId} role="status">
      <Text weight="semibold" size={500}>
        {title}
      </Text>
      <Text>{children}</Text>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Badges
// ─────────────────────────────────────────────────────────────────────────────

const RULE_SET_TONE: Record<string, StatusBadgeTone> = { Draft: 'neutral', Published: 'success', Retired: 'warning' };
const PROPOSAL_TONE: Record<string, StatusBadgeTone> = { Proposed: 'info', Accepted: 'success', Rejected: 'critical' };

export const RuleSetStatusBadge: React.FC<{ status: string }> = ({ status }) => (
  <StatusBadge label={status} tone={RULE_SET_TONE[status] ?? 'neutral'} />
);

/** A rule's review state; a rule that is in force says so instead. */
export const ProposalStateBadge: React.FC<{ proposal: Pick<ProposalView, 'state' | 'inForce'> }> = ({ proposal }) => (
  <StatusBadge
    label={proposal.inForce ? 'In force' : proposal.state}
    tone={proposal.inForce ? 'success' : (PROPOSAL_TONE[proposal.state] ?? 'neutral')}
  />
);
