/**
 * The By-rule view (ebilling-r1 task 110, prototype RuleGroups, answer 4): findings grouped by rule, most severe first, then count. Each
 * group offers "Apply suggestion to n lines…" when its suggestions agree on one action; the request is ONE call to the multi-line decision
 * route with a per-line amount (task 100). Clicking a line number selects that line in the pane.
 */

import * as React from 'react';
import { Button, Caption1, Link, Text, makeStyles, tokens } from '@fluentui/react-components';
import { ErrorCircle16Filled, Info16Regular, Warning16Filled } from '@fluentui/react-icons';
import type { ReviewLine } from '../services/invoiceReviewApi';
import { findingText, groupByRule, planSuggestion, type RuleGroup, type SuggestionPlan } from '../services/reviewModel';

const useStyles = makeStyles({
  root: { flex: 1, minHeight: 0, overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS, padding: tokens.spacingHorizontalM },
  group: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  head: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  title: { flex: 1, minWidth: 0 },
  muted: { color: tokens.colorNeutralForeground3 },
  lines: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  warn: { color: tokens.colorStatusWarningForeground1 },
  critical: { color: tokens.colorStatusDangerForeground1 },
  info: { color: tokens.colorNeutralForeground3 },
  empty: { color: tokens.colorNeutralForeground3, padding: tokens.spacingHorizontalL },
});

export interface RuleGroupsProps {
  lines: ReviewLine[];
  canWrite: boolean;
  busy: boolean;
  onSelectLine: (lineId: string) => void;
  onApply: (group: RuleGroup, plan: SuggestionPlan) => void;
}

export function RuleGroups({ lines, canWrite, busy, onSelectLine, onApply }: RuleGroupsProps) {
  const s = useStyles();
  const groups = React.useMemo(() => groupByRule(lines), [lines]);
  if (groups.length === 0) return <div className={s.empty}>No rule findings on these lines.</div>;
  return (
    <div className={s.root} aria-label="Findings by rule">
      {groups.map(g => {
        const plan = canWrite ? planSuggestion(g) : null;
        const Icon = g.severity === 'Critical' ? ErrorCircle16Filled : g.severity === 'Warning' ? Warning16Filled : Info16Regular;
        const tone = g.severity === 'Critical' ? s.critical : g.severity === 'Warning' ? s.warn : s.info;
        return (
          <section key={g.key} className={s.group} data-testid={`rule-group-${g.key}`} aria-label={`${g.label}, ${g.lines.length} lines`}>
            <div className={s.head}>
              <Icon className={tone} />
              <Text weight="semibold" className={s.title}>
                {g.label} · {g.lines.length} {g.lines.length === 1 ? 'line' : 'lines'}
              </Text>
              {plan && (
                <Button size="small" disabled={busy} onClick={() => onApply(g, plan)} data-testid={`apply-${g.key}`}>
                  Apply suggestion to {plan.lines.length} {plan.lines.length === 1 ? 'line' : 'lines'}…
                </Button>
              )}
            </div>
            <Caption1 className={s.muted}>{findingText(g.sample)}</Caption1>
            <div className={s.lines}>
              {g.lines
                .slice()
                .sort((a, b) => a.sequence - b.sequence)
                .map(l => (
                  <Link key={l.id} onClick={() => onSelectLine(l.id)} aria-label={`Open line ${l.sequence}`}>
                    Line {l.sequence}
                  </Link>
                ))}
            </div>
          </section>
        );
      })}
    </div>
  );
}
