/**
 * The rule sets (ebilling-r1 task 077, FR-28): name, where it applies (client default or a firm), status, "visible to firms" and
 * effective date, with "New rule set" for an admin. Opening a row opens the review of its rules.
 */

import * as React from 'react';
import {
  Button,
  DataGrid,
  DataGridBody,
  DataGridCell,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridRow,
  Spinner,
  TableCellLayout,
  Text,
  createTableColumn,
  makeStyles,
  tokens,
  type TableColumnDefinition,
} from '@fluentui/react-components';
import { AddRegular, ArrowClockwiseRegular } from '@fluentui/react-icons';
import { thinScrollbarStyle } from '@spaarke/ui-components';
import { RuleSetStatusBadge } from './Notices';
import { describeScope } from './useFirmNames';
import type { RuleSetSummary } from '../services/ruleSetApi';

const useStyles = makeStyles({
  root: {
    display: 'flex', flexDirection: 'column', height: '100%', boxSizing: 'border-box', gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL, backgroundColor: tokens.colorNeutralBackground1, color: tokens.colorNeutralForeground1,
  },
  header: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  title: { flexGrow: 1 },
  body: { flexGrow: 1, minHeight: 0, overflow: 'auto', ...thinScrollbarStyle },
  empty: { padding: tokens.spacingHorizontalXXL, textAlign: 'center', color: tokens.colorNeutralForeground3 },
  row: { cursor: 'pointer' },
});

export interface RuleSetListViewProps {
  /** Null while the first load is in flight. */
  ruleSets: RuleSetSummary[] | null;
  firmNames: Record<string, string>;
  canWrite: boolean;
  onOpen: (ruleSetId: string) => void;
  onNew: () => void;
  onRefresh: () => void;
}

export const RuleSetListView: React.FC<RuleSetListViewProps> = ({ ruleSets, firmNames, canWrite, onOpen, onNew, onRefresh }) => {
  const s = useStyles();

  const columns = React.useMemo<TableColumnDefinition<RuleSetSummary>[]>(
    () => [
      createTableColumn<RuleSetSummary>({
        columnId: 'name',
        renderHeaderCell: () => 'Name',
        renderCell: r => <TableCellLayout truncate>{r.name}</TableCellLayout>,
      }),
      createTableColumn<RuleSetSummary>({
        columnId: 'scope',
        renderHeaderCell: () => 'Applies to',
        renderCell: r => <TableCellLayout truncate>{describeScope(r.scope, r.firmId, firmNames)}</TableCellLayout>,
      }),
      createTableColumn<RuleSetSummary>({
        columnId: 'status',
        renderHeaderCell: () => 'Status',
        renderCell: r => <RuleSetStatusBadge status={r.status} />,
      }),
      createTableColumn<RuleSetSummary>({
        columnId: 'visible',
        renderHeaderCell: () => 'Visible to firms',
        renderCell: r => <TableCellLayout>{r.visibleToFirms ? 'Yes' : 'No'}</TableCellLayout>,
      }),
      createTableColumn<RuleSetSummary>({
        columnId: 'effective',
        renderHeaderCell: () => 'Effective',
        renderCell: r => <TableCellLayout>{r.effectiveFrom ?? '—'}</TableCellLayout>,
      }),
      createTableColumn<RuleSetSummary>({
        columnId: 'rules',
        renderHeaderCell: () => 'Rules',
        renderCell: r => <TableCellLayout>{r.ruleCount}</TableCellLayout>,
      }),
    ],
    [firmNames]
  );

  return (
    <div className={s.root} data-testid="ruleset-list">
      <div className={s.header}>
        <Text as="h1" size={500} weight="semibold" className={s.title}>
          Rule sets
        </Text>
        <Button icon={<ArrowClockwiseRegular />} appearance="subtle" onClick={onRefresh} aria-label="Refresh the list" data-testid="list-refresh" />
        {canWrite && (
          <Button appearance="primary" icon={<AddRegular />} onClick={onNew} data-testid="new-ruleset">
            New rule set
          </Button>
        )}
      </div>

      <div className={s.body}>
        {ruleSets === null ? (
          <div className={s.empty}>
            <Spinner label="Loading rule sets" />
          </div>
        ) : ruleSets.length === 0 ? (
          <div className={s.empty} data-testid="list-empty">
            <Text>No rule sets yet. {canWrite ? 'Create one from a guideline document to draft its rules.' : ''}</Text>
          </div>
        ) : (
          <DataGrid items={ruleSets} columns={columns} getRowId={(r: RuleSetSummary) => r.id} focusMode="composite" size="small">
            <DataGridHeader>
              <DataGridRow>{({ renderHeaderCell }) => <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>}</DataGridRow>
            </DataGridHeader>
            <DataGridBody<RuleSetSummary>>
              {({ item, rowId }) => (
                <DataGridRow<RuleSetSummary> key={rowId} className={s.row} onClick={() => onOpen(item.id)} data-testid={`ruleset-row-${item.id}`}>
                  {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
                </DataGridRow>
              )}
            </DataGridBody>
          </DataGrid>
        )}
      </div>
    </div>
  );
};
