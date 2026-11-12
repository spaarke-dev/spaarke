/**
 * The lines grid (ebilling-r1 task 110, prototype LinesGrid): Status · Date · Timekeeper · Narrative · Hours · Amount · Findings · Approved,
 * with a row tick, the findings badge (severity and count; hovering lists the rules), the unmatched-timekeeper mark, the low-confidence value
 * mark and the AI-observation mark.
 *
 * Grid decision (task 110 constraint "grid decision"): the Fluent v9 `DataGrid` PRIMITIVE fed by the read model, not the Spaarke DataGrid
 * framework. The framework is FetchXML / config driven (`DataGrid.tsx` needs a `configId` and an `IDataverseClient`; overrides are cell
 * renderers only) and cannot take 100's read-model rows with row ticks and a per-value click. No virtualisation (F-5: 150 lines). Cells are
 * memoised components with plain props, so selecting a line re-renders only the rows whose props changed.
 *
 * Clicking a value selects the line and boxes THAT value on the PDF; clicking elsewhere on the row selects the line. The tick never
 * selects the line (its click stops there).
 */

import * as React from 'react';
import {
  Badge,
  Checkbox,
  DataGrid,
  DataGridBody,
  DataGridCell,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridRow,
  Spinner,
  Tooltip,
  createTableColumn,
  makeStyles,
  mergeClasses,
  tokens,
  type TableColumnDefinition,
} from '@fluentui/react-components';
import {
  ErrorCircle16Filled,
  Info16Regular,
  PersonWarning16Regular,
  ScanText16Regular,
  Sparkle16Regular,
  Warning16Filled,
} from '@fluentui/react-icons';
import { StatusBadge } from '@spaarke/ui-components';
import type { ReviewLine } from '../services/invoiceReviewApi';
import {
  findingsBadge,
  hasAiObservation,
  hours,
  isAdjustment,
  isDecidable,
  lineStatusView,
  money,
  REVIEWER_ADDED_LABEL,
  REVIEWER_ADDED_MEANING,
  shortDate,
} from '../services/reviewModel';

const useStyles = makeStyles({
  root: { flex: 1, minHeight: 0, overflow: 'auto' },
  grid: { minWidth: '760px' },
  row: { cursor: 'pointer' },
  rowSelected: {
    backgroundColor: tokens.colorBrandBackground2,
    ':hover': { backgroundColor: tokens.colorBrandBackground2Hover },
  },
  rowDimmed: { color: tokens.colorNeutralForeground3 },
  value: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    background: 'none',
    border: 'none',
    padding: 0,
    margin: 0,
    font: 'inherit',
    color: 'inherit',
    textAlign: 'start',
    cursor: 'pointer',
    minWidth: 0,
    ':hover': { textDecorationLine: 'underline' },
    ':focus-visible': { outline: `${tokens.strokeWidthThick} solid ${tokens.colorStrokeFocus2}`, borderRadius: tokens.borderRadiusSmall },
  },
  valueActive: { textDecorationLine: 'underline', fontWeight: tokens.fontWeightSemibold },
  narrative: { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', minWidth: 0, display: 'block' },
  warn: { color: tokens.colorStatusWarningForeground1, flexShrink: 0 },
  ai: { color: tokens.colorBrandForeground1, flexShrink: 0 },
  critical: { color: tokens.colorStatusDangerForeground1 },
  info: { color: tokens.colorNeutralForeground3 },
  badge: { display: 'inline-flex', alignItems: 'center', flexWrap: 'wrap', gap: tokens.spacingHorizontalXXS },
  muted: { color: tokens.colorNeutralForeground3 },
  empty: { padding: tokens.spacingHorizontalL, color: tokens.colorNeutralForeground3 },
});

export interface LinesGridProps {
  lines: ReviewLine[];
  checked: ReadonlySet<string>;
  onCheck: (lineIds: string[], on: boolean) => void;
  /** Ticks are offered (the caller can decide and the line is decidable). */
  canTick: boolean;
  selectedLineId: string | null;
  selectedField: string | null;
  onSelect: (lineId: string, field: string | null) => void;
}

interface ValueProps {
  line: ReviewLine;
  field: string;
  active: boolean;
  onSelect: (lineId: string, field: string | null) => void;
  children: React.ReactNode;
  className?: string;
  label: string;
}

/** A value that boxes itself on the PDF when clicked. */
const Value = React.memo(function Value({ line, field, active, onSelect, children, className, label }: ValueProps) {
  const s = useStyles();
  const low = line.fields[field]?.lowConfidence === true;
  return (
    <button
      type="button"
      className={mergeClasses(s.value, active && s.valueActive, className)}
      aria-label={`${label}, line ${line.sequence}${low ? ', check this value' : ''}. Show on the invoice`}
      onClick={e => {
        e.stopPropagation();
        onSelect(line.id, field);
      }}
    >
      {children}
      {low && (
        <Tooltip content="Low confidence: compare with the document" relationship="description">
          <ScanText16Regular className={s.warn} data-testid={`lowconf-${line.id}-${field}`} />
        </Tooltip>
      )}
    </button>
  );
});

const FindingsCell = React.memo(function FindingsCell({ line }: { line: ReviewLine }) {
  const s = useStyles();
  const badge = findingsBadge(line);
  if (badge.count === 0) return <span className={s.muted}>—</span>;
  const Icon = badge.severity === 'Critical' ? ErrorCircle16Filled : badge.severity === 'Warning' ? Warning16Filled : Info16Regular;
  const tone = badge.severity === 'Critical' ? s.critical : badge.severity === 'Warning' ? s.warn : s.info;
  return (
    <Tooltip content={badge.names.join(' · ')} relationship="description">
      <span className={s.badge} data-testid={`findings-${line.id}`} tabIndex={0} aria-label={`${badge.count} ${badge.count === 1 ? 'finding' : 'findings'}`}>
        <Icon className={tone} />
        {badge.count}
      </span>
    </Tooltip>
  );
});

const StatusCell = React.memo(function StatusCell({ line }: { line: ReviewLine }) {
  const s = useStyles();
  const view = lineStatusView(line);
  if (line.status === 'Pending') {
    return <Spinner size="extra-tiny" label={view.label} labelPosition="after" data-testid={`processing-${line.id}`} />;
  }
  return (
    <span className={s.badge}>
      <StatusBadge label={view.label} tone={view.tone} />
      {line.reviewerAdded && (
        <Tooltip content={REVIEWER_ADDED_MEANING} relationship="description">
          <Badge size="small" appearance="tint" color="informative" tabIndex={0} aria-label="Added by reviewer" data-testid={`reviewer-added-${line.id}`}>
            {REVIEWER_ADDED_LABEL}
          </Badge>
        </Tooltip>
      )}
    </span>
  );
});

export function LinesGrid({ lines, checked, onCheck, canTick, selectedLineId, selectedField, onSelect }: LinesGridProps) {
  const s = useStyles();
  const tickable = React.useMemo(() => (canTick ? lines.filter(isDecidable).map(l => l.id) : []), [canTick, lines]);
  const allTicked = tickable.length > 0 && tickable.every(id => checked.has(id));
  const someTicked = tickable.some(id => checked.has(id));

  const columns = React.useMemo<TableColumnDefinition<ReviewLine>[]>(() => {
    const active = (line: ReviewLine, field: string) => line.id === selectedLineId && field === selectedField;
    return [
      createTableColumn<ReviewLine>({
        columnId: 'tick',
        renderHeaderCell: () =>
          canTick ? (
            <Checkbox
              aria-label="Select all lines that can be decided"
              checked={allTicked ? true : someTicked ? 'mixed' : false}
              disabled={tickable.length === 0}
              onChange={(_, d) => onCheck(tickable, d.checked === true)}
            />
          ) : null,
        renderCell: line =>
          canTick && isDecidable(line) ? (
            <Checkbox
              aria-label={`Select line ${line.sequence}`}
              checked={checked.has(line.id)}
              onClick={e => e.stopPropagation()}
              onChange={(_, d) => onCheck([line.id], d.checked === true)}
            />
          ) : null,
      }),
      createTableColumn<ReviewLine>({ columnId: 'status', renderHeaderCell: () => 'Status', renderCell: line => <StatusCell line={line} /> }),
      createTableColumn<ReviewLine>({
        columnId: 'date',
        renderHeaderCell: () => 'Date',
        renderCell: line => (
          <Value line={line} field="Date" label="Date" active={active(line, 'Date')} onSelect={onSelect}>
            {shortDate(line.date)}
          </Value>
        ),
      }),
      createTableColumn<ReviewLine>({
        columnId: 'timekeeper',
        renderHeaderCell: () => 'Timekeeper',
        renderCell: line =>
          isAdjustment(line) || line.type === 'Expense' ? (
            <span className={s.muted}>{line.type === 'Expense' ? 'Expense' : 'Adjustment'}</span>
          ) : (
            <Value line={line} field="TimekeeperAsBilled" label="Timekeeper" active={active(line, 'TimekeeperAsBilled')} onSelect={onSelect}>
              <span className={s.narrative}>{line.timekeeper?.name ?? line.timekeeperAsBilled ?? '—'}</span>
              {!line.timekeeper && line.type === 'Fee' && (
                <Tooltip content="Timekeeper not on the firm's roster" relationship="description">
                  <PersonWarning16Regular className={s.warn} data-testid={`unmatched-${line.id}`} />
                </Tooltip>
              )}
            </Value>
          ),
      }),
      createTableColumn<ReviewLine>({
        columnId: 'narrative',
        renderHeaderCell: () => 'Narrative',
        renderCell: line => (
          <Value line={line} field="Narrative" label="Narrative" active={active(line, 'Narrative')} onSelect={onSelect} className={s.narrative}>
            <span className={s.narrative}>{line.narrative ?? '—'}</span>
            {hasAiObservation(line) && (
              <Tooltip content="AI observation on the narrative" relationship="description">
                <Sparkle16Regular className={s.ai} data-testid={`aiobs-${line.id}`} />
              </Tooltip>
            )}
          </Value>
        ),
      }),
      createTableColumn<ReviewLine>({
        columnId: 'hours',
        renderHeaderCell: () => 'Hours',
        renderCell: line =>
          line.type === 'Fee' ? (
            <Value line={line} field="Hours" label="Hours" active={active(line, 'Hours')} onSelect={onSelect}>
              {hours(line.quantity)}
            </Value>
          ) : (
            <span className={s.muted}>—</span>
          ),
      }),
      createTableColumn<ReviewLine>({
        columnId: 'amount',
        renderHeaderCell: () => 'Amount',
        renderCell: line => (
          <Value line={line} field="Amount" label="Amount" active={active(line, 'Amount')} onSelect={onSelect}>
            {money(line.amount)}
          </Value>
        ),
      }),
      createTableColumn<ReviewLine>({ columnId: 'findings', renderHeaderCell: () => 'Findings', renderCell: line => <FindingsCell line={line} /> }),
      createTableColumn<ReviewLine>({
        columnId: 'approved',
        renderHeaderCell: () => 'Approved',
        renderCell: line => <span className={line.approvedAmount === null ? s.muted : undefined}>{money(line.approvedAmount)}</span>,
      }),
    ];
  }, [allTicked, canTick, checked, onCheck, onSelect, s, selectedField, selectedLineId, someTicked, tickable]);

  if (lines.length === 0) {
    return <div className={s.empty}>No lines to show.</div>;
  }

  return (
    <div className={s.root}>
      <DataGrid
        className={s.grid}
        items={lines}
        columns={columns}
        getRowId={(line: ReviewLine) => line.id}
        size="small"
        focusMode="composite"
        aria-label="Invoice lines"
        columnSizingOptions={{
          tick: { idealWidth: 32, minWidth: 32 },
          status: { idealWidth: 110, minWidth: 96 },
          date: { idealWidth: 96, minWidth: 88 },
          timekeeper: { idealWidth: 140, minWidth: 100 },
          narrative: { idealWidth: 260, minWidth: 120 },
          hours: { idealWidth: 64, minWidth: 56 },
          amount: { idealWidth: 100, minWidth: 88 },
          findings: { idealWidth: 76, minWidth: 64 },
          approved: { idealWidth: 100, minWidth: 88 },
        }}
      >
        <DataGridHeader>
          <DataGridRow>{({ renderHeaderCell }) => <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>}</DataGridRow>
        </DataGridHeader>
        <DataGridBody<ReviewLine>>
          {({ item, rowId }) => (
            <DataGridRow<ReviewLine>
              key={rowId}
              data-testid={`line-row-${item.id}`}
              aria-selected={item.id === selectedLineId}
              className={mergeClasses(s.row, item.id === selectedLineId && s.rowSelected, isAdjustment(item) && s.rowDimmed)}
              onClick={() => onSelect(item.id, null)}
            >
              {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
            </DataGridRow>
          )}
        </DataGridBody>
      </DataGrid>
    </div>
  );
}
