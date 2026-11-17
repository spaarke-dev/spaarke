/**
 * Invoices — the invoices of a Project or Matter (R3 task 050, FR-02; moved from R3's record-detail modal, task 005).
 *
 * POST /api/v1/external/api/dataverse/fetch on sprk_invoice filtered by this record's lookup (`detail.invoicesBy`): the
 * SAME Tier-2-scoped module read the Invoices tab runs, so a row shows here exactly when the tab shows it for this record.
 * Columns (`INVOICE_COLUMNS`), order, texts and states are R3's. Refresh (or Try again) remounts the list, which re-reads.
 *
 * OPEN (task 013, owner 2026-11-14). A row is a button that opens that invoice's record in a child modal over this record
 * (`useChildRecordOpener`; focus returns to the row on close, and the section re-reads). Inside a child record there is no
 * opener, so the rows are plain text as before (one child level only).
 */
import * as React from 'react';
import { makeStyles, tokens, MessageBar, MessageBarBody, Spinner } from '@fluentui/react-components';
import { TruncatedListNotice } from '../../TruncatedListNotice';
import { EMPTY_VALUE, formatDetailValue, type DetailRecord } from '../../detail/RecordDetailFields';
import { childRecordAttributesOf, useChildRecordOpener, type OpenChildRecord } from '../childRecord';
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
  // The whole row is the control: the same layout as the plain row, with the button chrome removed and a visible focus ring.
  itemButton: {
    display: 'flex',
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'baseline',
    gap: tokens.spacingHorizontalM,
    width: '100%',
    boxSizing: 'border-box',
    margin: 0,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: 0,
    border: 'none',
    backgroundColor: 'transparent',
    color: 'inherit',
    font: 'inherit',
    textAlign: 'start',
    cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorSubtleBackgroundHover },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: tokens.strokeWidthThick,
      outlineColor: tokens.colorStrokeFocus2,
    },
  },
  itemLi: {
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  // Read by screen readers as the start of the button's name; never seen.
  visuallyHidden: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    margin: '-1px',
    padding: 0,
    overflow: 'hidden',
    clipPath: 'inset(50%)',
    whiteSpace: 'nowrap',
    border: 0,
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
  /** When given, a row opens its record: the entity, the row's accessible-name noun ("invoice"), and the opener. */
  open?: { entity: string; label: string; opener: OpenChildRecord; section: 'invoices' };
}

/** R3's rollup list (the section card supplies the heading the R3 list drew itself). */
const RelatedList: React.FC<RelatedListProps> = ({
  testId,
  idAttribute,
  noun,
  emptyText,
  state,
  primary,
  meta,
  open,
}) => {
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
              {state.rows.map((row, i) => {
                const rowId = typeof row[idAttribute] === 'string' ? (row[idAttribute] as string) : '';
                const text = (
                  <>
                    <span className={styles.itemPrimary}>{primary(row)}</span>
                    <span className={styles.itemMeta}>{meta(row)}</span>
                  </>
                );
                if (!open || !rowId) {
                  return (
                    <li key={String(row[idAttribute] ?? i)} className={styles.item}>
                      {text}
                    </li>
                  );
                }
                return (
                  <li key={rowId} className={styles.itemLi}>
                    <button
                      type="button"
                      className={styles.itemButton}
                      {...childRecordAttributesOf(open.entity, rowId)}
                      onClick={e => open.opener(open.entity, rowId, e.currentTarget, open.section)}
                    >
                      {/* The content names the button (number, status, amount), after a hidden "Open invoice". */}
                      <span className={styles.visuallyHidden}>Open {open.label} </span>
                      {text}
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </>
      )}
    </div>
  );
};

const InvoiceList: React.FC<{ parentAttribute: string; parentId: string }> = ({ parentAttribute, parentId }) => {
  const state = useRelatedRows('sprk_invoice', INVOICE_COLUMNS, parentAttribute, parentId, INVOICE_ORDER);
  const opener = useChildRecordOpener();
  return (
    <RelatedList
      testId="record-detail-invoices"
      idAttribute="sprk_invoiceid"
      noun="invoices"
      emptyText="No invoices for this record."
      state={state}
      open={opener ? { entity: 'sprk_invoice', label: 'invoice', opener, section: 'invoices' } : undefined}
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
