/**
 * Invoices — the invoices of a Project or Matter (R3 task 050, FR-02; moved from R3's record-detail modal, task 005).
 *
 * POST /api/v1/external/api/dataverse/fetch on sprk_invoice filtered by this record's lookup (`detail.invoicesBy`): the
 * SAME Tier-2-scoped module read the Invoices tab runs, so a row shows here exactly when the tab shows it for this record.
 * Columns (`INVOICE_COLUMNS`), order, texts and states are R3's. Refresh (or Try again) remounts the list, which re-reads.
 */
import * as React from 'react';
import { makeStyles, tokens, MessageBar, MessageBarBody, Spinner } from '@fluentui/react-components';
import { TruncatedListNotice } from '../../TruncatedListNotice';
import { EMPTY_VALUE, formatDetailValue, type DetailRecord } from '../../detail/RecordDetailFields';
import { useRelatedRows, type ListState } from '../recordReads';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordSectionCard } from './RecordSectionCard';

const useStyles = makeStyles({
  body: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalM,
  },
  list: {
    listStyleType: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
  },
  item: {
    display: 'flex',
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'baseline',
    gap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalS,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  itemPrimary: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    overflowWrap: 'anywhere',
  },
  itemMeta: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    whiteSpace: 'nowrap',
    flexShrink: 0,
  },
});

export const INVOICE_COLUMNS = [
  'sprk_invoiceid',
  'sprk_name',
  'sprk_invoicenumber',
  'sprk_invoicedate',
  'statuscode',
  'sprk_totalamount',
] as const;
const INVOICE_ORDER = { attribute: 'sprk_invoicedate', descending: true };

interface RelatedListProps {
  testId: string;
  idAttribute: string;
  noun: string;
  emptyText: string;
  state: ListState;
  primary: (row: DetailRecord) => string;
  meta: (row: DetailRecord) => string;
}

/** R3's rollup list (the section card supplies the heading the R3 list drew itself). */
const RelatedList: React.FC<RelatedListProps> = ({ testId, idAttribute, noun, emptyText, state, primary, meta }) => {
  const styles = useStyles();
  return (
    <div className={styles.body} data-testid={testId}>
      {state.status === 'loading' && <Spinner size="small" label={`Loading ${noun}...`} />}
      {state.status === 'error' && (
        <MessageBar intent="error">
          <MessageBarBody>The {noun} could not be loaded just now.</MessageBarBody>
        </MessageBar>
      )}
      {state.status === 'ready' && (
        <>
          <TruncatedListNotice truncated={state.truncated} shown={state.rows.length} noun={noun} />
          {state.rows.length === 0 && !state.truncated && (
            <div className={styles.empty} role="status">
              {emptyText}
            </div>
          )}
          {state.rows.length > 0 && (
            <ul className={styles.list}>
              {state.rows.map((row, i) => (
                <li key={String(row[idAttribute] ?? i)} className={styles.item}>
                  <span className={styles.itemPrimary}>{primary(row)}</span>
                  <span className={styles.itemMeta}>{meta(row)}</span>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </div>
  );
};

const InvoiceList: React.FC<{ parentAttribute: string; parentId: string }> = ({ parentAttribute, parentId }) => {
  const state = useRelatedRows('sprk_invoice', INVOICE_COLUMNS, parentAttribute, parentId, INVOICE_ORDER);
  return (
    <RelatedList
      testId="record-detail-invoices"
      idAttribute="sprk_invoiceid"
      noun="invoices"
      emptyText="No invoices for this record."
      state={state}
      primary={row => {
        const number = formatDetailValue(row, { attribute: 'sprk_invoicenumber' });
        return number !== EMPTY_VALUE ? number : formatDetailValue(row, { attribute: 'sprk_name' });
      }}
      meta={row =>
        [
          formatDetailValue(row, { attribute: 'statuscode' }),
          formatDetailValue(row, { attribute: 'sprk_totalamount', kind: 'money' }),
        ].join(' · ')
      }
    />
  );
};

export const InvoicesSection: React.FC<RecordSectionContext> = ({ detail, recordId, refreshSignal, headerActions }) => {
  // No invoice lookup to this record type (a work assignment): no section and no read.
  if (!detail.invoicesBy) return null;
  // `headerActions`: whatever the config lists under `sectionActions.invoices` (Submit invoice, task 010), offered by its
  // action host exactly as in the toolbar.
  return (
    <RecordSectionCard id="invoices" actions={headerActions}>
      <InvoiceList key={refreshSignal} parentAttribute={detail.invoicesBy} parentId={recordId} />
    </RecordSectionCard>
  );
};
