/**
 * The drafted rules of a rule set (ebilling-r1 task 077): type, plain-language description, consequence and status, one row per rule.
 *
 * A plain Fluent v9 DataGrid. The Spaarke DataGrid framework is not used here: it is driven by a `sprk_gridconfiguration` record and
 * reads Dataverse rows through `IDataverseClient`, whereas these rows come only from the BFF (the page must not read rule bodies and
 * clause text with the caller's own Dataverse reads, and must not write them: ADR-002, D-76). See notes/077-rule-review-page.md.
 */

import * as React from 'react';
import {
  DataGrid,
  DataGridBody,
  DataGridCell,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridRow,
  TableCellLayout,
  Text,
  createTableColumn,
  makeStyles,
  tokens,
  type TableColumnDefinition,
} from '@fluentui/react-components';
import { thinScrollbarStyle } from '@spaarke/ui-components';
import { ProposalStateBadge } from './Notices';
import type { ProposalView } from '../services/ruleSetApi';
import { CONSEQUENCE_LABELS, ruleTypeLabel } from '../services/ruleTypes';

const useStyles = makeStyles({
  root: { height: '100%', overflow: 'auto', ...thinScrollbarStyle },
  description: { whiteSpace: 'normal', overflowWrap: 'anywhere' },
  muted: { color: tokens.colorNeutralForeground3 },
  empty: { padding: tokens.spacingHorizontalXXL, textAlign: 'center', color: tokens.colorNeutralForeground3 },
});

export interface ProposalsGridProps {
  proposals: readonly ProposalView[];
  selectedPolicyId: string | null;
  onSelect: (policyId: string) => void;
  /** Shown instead of the grid when there are no rules; the page says what to do next (draft rules). */
  emptyMessage: React.ReactNode;
}

export const ProposalsGrid: React.FC<ProposalsGridProps> = ({ proposals, selectedPolicyId, onSelect, emptyMessage }) => {
  const s = useStyles();

  const columns = React.useMemo<TableColumnDefinition<ProposalView>[]>(
    () => [
      createTableColumn<ProposalView>({
        columnId: 'type',
        renderHeaderCell: () => 'Type',
        renderCell: p => <TableCellLayout truncate>{ruleTypeLabel(p.ruleType)}</TableCellLayout>,
      }),
      createTableColumn<ProposalView>({
        columnId: 'description',
        renderHeaderCell: () => 'Rule',
        renderCell: p => (
          <TableCellLayout>
            <span className={s.description}>{p.descriptionText ?? p.shortHeadline ?? p.name}</span>
          </TableCellLayout>
        ),
      }),
      createTableColumn<ProposalView>({
        columnId: 'consequence',
        renderHeaderCell: () => 'Consequence',
        renderCell: p => <TableCellLayout truncate>{p.consequence ? (CONSEQUENCE_LABELS[p.consequence] ?? p.consequence) : '—'}</TableCellLayout>,
      }),
      createTableColumn<ProposalView>({
        columnId: 'status',
        renderHeaderCell: () => 'Status',
        renderCell: p => <ProposalStateBadge proposal={p} />,
      }),
    ],
    [s.description]
  );

  if (proposals.length === 0) {
    return (
      <div className={s.empty} data-testid="proposals-empty">
        <Text>{emptyMessage}</Text>
      </div>
    );
  }

  return (
    <div className={s.root} data-testid="proposals-grid">
      <DataGrid
        items={proposals as ProposalView[]}
        columns={columns}
        getRowId={(p: ProposalView) => p.policyId}
        selectionMode="single"
        selectedItems={selectedPolicyId ? [selectedPolicyId] : []}
        onSelectionChange={(_, data) => {
          const first = Array.from(data.selectedItems)[0];
          if (typeof first === 'string') onSelect(first);
        }}
        focusMode="composite"
        size="small"
      >
        <DataGridHeader>
          <DataGridRow selectionCell={{ 'aria-label': 'Select a rule' }}>
            {({ renderHeaderCell }) => <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>}
          </DataGridRow>
        </DataGridHeader>
        <DataGridBody<ProposalView>>
          {({ item, rowId }) => (
            <DataGridRow<ProposalView> key={rowId} onClick={() => onSelect(item.policyId)} data-testid={`proposal-row-${item.policyCode}`}>
              {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
            </DataGridRow>
          )}
        </DataGridBody>
      </DataGrid>
    </div>
  );
};
