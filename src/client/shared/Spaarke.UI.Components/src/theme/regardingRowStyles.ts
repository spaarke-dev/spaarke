/**
 * Shared "Row-2" styles for the regarding/connection record rows on a Dataverse form: a 1fr : 2fr grid with the record
 * number on the left and the record name on the right, plus the OOB-parity field label and value text.
 *
 * Before this file, `RegardingResolver` and `CommunicationConnections` each copied these blocks ("RegardingResolver
 * parity") and had to be kept in step by hand (#2967). Spread the objects into a `makeStyles` slot:
 * ```ts
 * const useStyles = makeStyles({ row2: { ...regardingRowStyles.row2 }, fieldLabel: { ...regardingRowStyles.fieldLabel } });
 * ```
 * The label / value text deliberately mirrors the OOB form (Segoe UI 12px #616161 / 14px #242424) — a documented
 * exception to ADR-021 tokens (SRFR-039 Path A). Everything else uses tokens. Pure objects: no React, PCF-safe.
 */
import { tokens } from '@fluentui/react-components';

const OOB_FONT_FAMILY =
  '"Segoe UI", "Segoe UI Web (West European)", -apple-system, BlinkMacSystemFont, Roboto, "Helvetica Neue", sans-serif';

export const regardingRowStyles = {
  /** Two-column grid: number cell 1/3 (left), name cell 2/3 (right). Empty cells hide entirely. */
  row2: {
    display: 'grid',
    gridTemplateColumns: '1fr 2fr',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalXS}`,
    minHeight: '24px',
    alignItems: 'start',
  },
  /** Per-cell wrapper: label above, value below. */
  numberCell: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  nameCell: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  /** OOB-parity field label: 12px / 400 / #616161 / Segoe UI. */
  fieldLabel: {
    fontFamily: OOB_FONT_FAMILY,
    fontSize: '12px',
    fontWeight: 400,
    color: '#616161',
    lineHeight: '16px',
  },
  recordNumber: {
    fontWeight: tokens.fontWeightSemibold,
  },
  /** Record-name value: plain text (not a Link), 14px / 400 / #242424, ellipsis on overflow. */
  recordName: {
    fontFamily: OOB_FONT_FAMILY,
    fontSize: '14px',
    fontWeight: 400,
    color: '#242424',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
} as const;
