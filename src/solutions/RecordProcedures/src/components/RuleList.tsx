/**
 * The Rules in force tab's table (ebilling-r1 task 084a): the rules that run on the record for the chosen firm and date, then the rules
 * that do not (excluded, or a record rule whose base rule is no longer in force), in the server's order. Each row carries one badge per
 * mark, or "In force" when it has none. Task 084b: a row opens the Rule wizard on a click, or on Enter or Space while it has focus.
 *
 * Marks are rendered generically (`model.ts`): the array is open, an unknown kind is a neutral badge with its own text, and the set of
 * kinds that dim a row is one table (`DIMMED_MARK_KINDS`) so 084e's `Outranked` is a one-line addition.
 */

import * as React from 'react';
import {
  Caption1,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { StatusBadge } from '@spaarke/ui-components';
import { openProps } from './rowOpen';
import type { RuleItem } from '../services/recordProceduresApi';
import { isRuleDimmed, levelLabel, publishedNote, ruleBadges, versionLabel } from '../services/model';

const useStyles = makeStyles({
  dim: { color: tokens.colorNeutralForeground3 },
  open: { cursor: 'pointer' },
  stack: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS, minWidth: 0 },
  sub: { color: tokens.colorNeutralForeground3 },
  clip: { display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  badges: { display: 'flex', gap: tokens.spacingHorizontalXS, flexWrap: 'wrap', alignItems: 'center' },
  empty: { padding: tokens.spacingVerticalL, color: tokens.colorNeutralForeground3 },
});

export interface RuleListProps {
  rules: readonly RuleItem[];
  /** Said when the list (after the filter) has no row. */
  emptyText: string;
  /** A row was clicked, or Enter (or Space) was pressed on it (task 084b). */
  onOpen?: (rule: RuleItem) => void;
}

export const RuleList: React.FC<RuleListProps> = ({ rules, emptyText, onOpen }) => {
  const s = useStyles();
  return (
    <>
      <Table aria-label="Rules in force" size="small" data-testid="rule-list">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Rule</TableHeaderCell>
            <TableHeaderCell style={{ width: '28%' }}>From</TableHeaderCell>
            <TableHeaderCell style={{ width: '26%' }}>Runs as</TableHeaderCell>
            <TableHeaderCell style={{ width: 220 }}>State</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rules.map((r, i) => {
            const dim = isRuleDimmed(r);
            const note = publishedNote(r);
            const procedureVersion = r.procedure?.version;
            return (
              <TableRow
                key={`${r.policyId}-${i}`}
                className={mergeClasses(dim && s.dim, onOpen && s.open)}
                data-testid={`rule-row-${r.policyId}`}
                data-dimmed={dim ? 'true' : 'false'}
                {...openProps(onOpen, () => onOpen?.(r))}
              >
                <TableCell>
                  <span className={s.stack}>
                    <Text className={s.clip} weight={dim ? 'regular' : 'semibold'}>
                      {r.name ? `${r.code} · ${r.name}` : r.code}
                    </Text>
                    {r.ruleType && <Caption1 className={s.sub}>{r.ruleType}</Caption1>}
                  </span>
                </TableCell>
                <TableCell>
                  <span className={s.stack}>
                    <Text className={s.clip}>{r.procedure?.name ?? '—'}</Text>
                    <Caption1 className={s.sub}>
                      {[procedureVersion ? versionLabel(procedureVersion) : null, levelLabel(r.level, r.firmScope?.name)].filter(Boolean).join(' · ')}
                    </Caption1>
                  </span>
                </TableCell>
                <TableCell>
                  <span className={s.stack}>
                    <Text className={s.clip}>{dim ? '—' : (r.parameterSummary ?? '—')}</Text>
                    {note && !dim && <Caption1 className={s.sub}>{note}</Caption1>}
                  </span>
                </TableCell>
                <TableCell>
                  <span className={s.badges}>
                    {ruleBadges(r).map((b, k) => (
                      <StatusBadge key={`${b.label}-${k}`} label={b.label} tone={b.tone} />
                    ))}
                  </span>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
      {rules.length === 0 && (
        <Text className={s.empty} data-testid="rule-empty">
          {emptyText}
        </Text>
      )}
    </>
  );
};
