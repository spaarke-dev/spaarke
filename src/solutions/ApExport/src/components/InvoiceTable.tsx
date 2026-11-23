/**
 * The selected invoices as a table: firm, record, invoice number, tracking number, date, approved total, status, and
 * an optional outcome column (an export refusal or a Mark Paid answer). Fluent v9, semantic tokens only (ADR-021).
 */

import * as React from 'react';
import {
  Badge,
  makeStyles,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  tokens,
} from '@fluentui/react-components';
import { formatAmount, type InvoiceRow } from '../services/invoices';

const useStyles = makeStyles({
  number: { fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' },
  muted: { color: tokens.colorNeutralForeground3 },
  outcome: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
});

export type OutcomeTone = 'success' | 'warning' | 'danger' | 'informative' | 'subtle';

export interface RowOutcome {
  label: string;
  tone: OutcomeTone;
  detail?: string;
}

export interface InvoiceTableProps {
  rows: InvoiceRow[];
  /** Outcome per invoice id; the column shows only when given. */
  outcomes?: ReadonlyMap<string, RowOutcome>;
  outcomeHeader?: string;
}

const dash = '—';

export const InvoiceTable: React.FC<InvoiceTableProps> = ({ rows, outcomes, outcomeHeader = 'Result' }) => {
  const s = useStyles();
  return (
    <Table size="small" aria-label="Selected invoices">
      <TableHeader>
        <TableRow>
          <TableHeaderCell>Firm</TableHeaderCell>
          <TableHeaderCell>Record</TableHeaderCell>
          <TableHeaderCell>Invoice number</TableHeaderCell>
          <TableHeaderCell>Tracking number</TableHeaderCell>
          <TableHeaderCell>Date</TableHeaderCell>
          <TableHeaderCell>Approved total</TableHeaderCell>
          <TableHeaderCell>Status</TableHeaderCell>
          {outcomes && <TableHeaderCell>{outcomeHeader}</TableHeaderCell>}
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map(row => {
          const outcome = outcomes?.get(row.id);
          return (
            <TableRow key={row.id}>
              <TableCell>
                {row.found ? (
                  row.firmName ?? dash
                ) : (
                  <Text className={s.muted} title={row.id}>
                    Not available ({row.id.slice(0, 8)})
                  </Text>
                )}
              </TableCell>
              <TableCell>{row.recordName ?? dash}</TableCell>
              <TableCell>{row.invoiceNumber ?? row.name ?? dash}</TableCell>
              <TableCell>{row.trackingNumber ?? dash}</TableCell>
              <TableCell className={s.number}>{row.invoiceDate ?? dash}</TableCell>
              <TableCell className={s.number}>
                {row.approvedTotal === null ? dash : formatAmount(row.approvedTotal, row.currency)}
              </TableCell>
              <TableCell>{row.statusLabel ?? dash}</TableCell>
              {outcomes && (
                <TableCell>
                  {outcome ? (
                    <div className={s.outcome}>
                      <Badge appearance="tint" color={outcome.tone} shape="rounded">
                        {outcome.label}
                      </Badge>
                      {outcome.detail && <Text size={200}>{outcome.detail}</Text>}
                    </div>
                  ) : (
                    dash
                  )}
                </TableCell>
              )}
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
};
