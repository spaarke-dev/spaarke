/**
 * The Procedures tab's table (ebilling-r1 task 084a): one row per Procedure the record is subject to, with its source, its version and one
 * state badge. Task 084b: a row opens the Procedure wizard on a click, or on Enter or Space while it has focus; the list calls nothing itself.
 *
 * Columns follow the prototype's panel except "Rules": task 084 deliberately returns no per-Procedure rule count (a count would expose rows
 * to the all-or-nothing rights check that the panel does not show), so the rules are on the Rules in force tab.
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
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { StatusBadge } from '@spaarke/ui-components';
import { openProps } from './rowOpen';
import type { GapView, ProcedureItem } from '../services/recordProceduresApi';
import { isProcedureDimmed, overrideNote, procedureState, sourceLabel, versionLines } from '../services/model';

const useStyles = makeStyles({
  dim: { color: tokens.colorNeutralForeground3 },
  open: { cursor: 'pointer' },
  stack: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS, minWidth: 0 },
  sub: { color: tokens.colorNeutralForeground3 },
  clip: { display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  empty: { padding: tokens.spacingVerticalL, color: tokens.colorNeutralForeground3 },
});

export interface ProcedureListProps {
  procedures: readonly ProcedureItem[];
  gaps: readonly GapView[];
  /** Said when the list (after the filter) has no row. */
  emptyText: string;
  /** A row was clicked, or Enter (or Space) was pressed on it (task 084b). */
  onOpen?: (procedure: ProcedureItem) => void;
}

export const ProcedureList: React.FC<ProcedureListProps> = ({ procedures, gaps, emptyText, onOpen }) => {
  const s = useStyles();
  return (
    <>
      <Table aria-label="Procedures" size="small" data-testid="procedure-list">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Procedure</TableHeaderCell>
            <TableHeaderCell style={{ width: '28%' }}>Applies through</TableHeaderCell>
            <TableHeaderCell style={{ width: '22%' }}>Version</TableHeaderCell>
            <TableHeaderCell style={{ width: 190 }}>State</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {procedures.map(p => {
            const dim = isProcedureDimmed(p);
            const winner = p.sources.find(x => x.winning) ?? p.sources[0];
            const others = p.sources.filter(x => x !== winner);
            const state = procedureState(p, gaps);
            const version = versionLines(p);
            const note = overrideNote(p, gaps);
            return (
              <TableRow
                key={p.id}
                className={mergeClasses(dim && s.dim, onOpen && s.open)}
                data-testid={`procedure-row-${p.id}`}
                data-dimmed={dim ? 'true' : 'false'}
                {...openProps(onOpen, () => onOpen?.(p))}
              >
                <TableCell>
                  <span className={s.stack}>
                    <Text className={s.clip} weight={dim ? 'regular' : 'semibold'}>
                      {p.name}
                    </Text>
                    <Caption1 className={s.sub}>
                      {[p.type, p.firm ? `${p.firm.name ?? 'One firm'} invoices only` : null].filter(Boolean).join(' · ')}
                    </Caption1>
                  </span>
                </TableCell>
                <TableCell>
                  <span className={s.stack}>
                    <Text className={s.clip}>{winner ? sourceLabel(winner, p.firm?.name) : '—'}</Text>
                    {others.length > 0 && (
                      <Tooltip relationship="description" content={others.map(x => sourceLabel(x, p.firm?.name)).join(' · ')}>
                        <Caption1 className={s.sub}>and {others.length} more</Caption1>
                      </Tooltip>
                    )}
                    {note && <Caption1 className={s.sub}>{note}</Caption1>}
                  </span>
                </TableCell>
                <TableCell>
                  <span className={s.stack}>
                    <Text>{version.main}</Text>
                    {version.note && <Caption1 className={s.sub}>{version.note}</Caption1>}
                  </span>
                </TableCell>
                <TableCell>
                  <StatusBadge label={state.label} tone={state.tone} />
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
      {procedures.length === 0 && (
        <Text className={s.empty} data-testid="procedure-empty">
          {emptyText}
        </Text>
      )}
    </>
  );
};
