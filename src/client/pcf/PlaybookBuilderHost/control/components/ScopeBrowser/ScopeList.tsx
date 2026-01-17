/**
 * Scope List Component - DataGrid display for scope items
 *
 * Displays scopes in a sortable, filterable grid with ownership badges,
 * action menus, and drag support.
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { useCallback, useMemo } from 'react';
import {
  DataGrid,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  TableColumnDefinition,
  createTableColumn,
  TableCellLayout,
  Badge,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuDivider,
  Button,
  Text,
  Tooltip,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';
import {
  MoreHorizontal20Regular,
  Eye20Regular,
  Copy20Regular,
  ArrowForward20Regular,
  Delete20Regular,
  Drag16Regular,
  LockClosed16Regular,
  Person16Regular,
} from '@fluentui/react-icons';
import type { ScopeItem, ScopeType, OwnershipType } from './ScopeBrowser';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface ScopeListProps {
  scopes: ScopeItem[];
  scopeType: ScopeType;
  onView?: (scope: ScopeItem) => void;
  onSaveAs?: (scope: ScopeItem) => void;
  onExtend?: (scope: ScopeItem) => void;
  onDelete?: (scope: ScopeItem) => void;
  onDragStart?: (scope: ScopeItem) => void;
  readOnly?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    flex: 1,
    ...shorthands.overflow('auto'),
  },
  grid: {
    minWidth: '100%',
  },
  row: {
    cursor: 'pointer',
    '&:hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  rowDraggable: {
    cursor: 'grab',
    '&:active': {
      cursor: 'grabbing',
    },
  },
  nameCell: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  dragHandle: {
    color: tokens.colorNeutralForeground3,
    opacity: 0,
    transition: 'opacity 0.15s ease',
    '.fui-DataGridRow:hover &': {
      opacity: 1,
    },
  },
  ownershipBadge: {
    flexShrink: 0,
  },
  systemBadge: {
    backgroundColor: tokens.colorPaletteBlueBorderActive,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  customerBadge: {
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorPaletteGreenForeground1,
  },
  immutableIcon: {
    color: tokens.colorNeutralForeground3,
    marginLeft: tokens.spacingHorizontalXS,
  },
  descriptionCell: {
    color: tokens.colorNeutralForeground2,
    whiteSpace: 'nowrap',
    textOverflow: 'ellipsis',
    ...shorthands.overflow('hidden'),
    maxWidth: '300px',
  },
  dateCell: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  actionsCell: {
    display: 'flex',
    justifyContent: 'flex-end',
  },
  parentBadge: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helper Components
// ─────────────────────────────────────────────────────────────────────────────

interface OwnershipBadgeProps {
  ownershipType: OwnershipType;
  isImmutable: boolean;
}

const OwnershipBadge: React.FC<OwnershipBadgeProps> = ({ ownershipType, isImmutable }) => {
  const styles = useStyles();

  return (
    <Badge
      appearance="filled"
      size="small"
      className={ownershipType === 'system' ? styles.systemBadge : styles.customerBadge}
      icon={ownershipType === 'system' ? <LockClosed16Regular /> : <Person16Regular />}
    >
      {ownershipType === 'system' ? 'SYS' : 'CUST'}
      {isImmutable && (
        <Tooltip content="This scope cannot be modified" relationship="label">
          <LockClosed16Regular className={styles.immutableIcon} />
        </Tooltip>
      )}
    </Badge>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const ScopeList: React.FC<ScopeListProps> = ({
  scopes,
  scopeType,
  onView,
  onSaveAs,
  onExtend,
  onDelete,
  onDragStart,
  readOnly = false,
}) => {
  const styles = useStyles();

  // Handle drag start
  const handleDragStart = useCallback(
    (scope: ScopeItem, event: React.DragEvent) => {
      if (onDragStart) {
        // Set drag data for canvas drop
        event.dataTransfer.setData(
          'application/x-scope',
          JSON.stringify({
            type: scopeType,
            scope,
          })
        );
        event.dataTransfer.effectAllowed = 'copy';
        onDragStart(scope);
      }
    },
    [onDragStart, scopeType]
  );

  // Format date for display
  const formatDate = useCallback((dateString: string) => {
    try {
      return new Date(dateString).toLocaleDateString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
      });
    } catch {
      return dateString;
    }
  }, []);

  // Column definitions
  const columns: TableColumnDefinition<ScopeItem>[] = useMemo(
    () => [
      createTableColumn<ScopeItem>({
        columnId: 'name',
        compare: (a, b) => a.displayName.localeCompare(b.displayName),
        renderHeaderCell: () => 'Name',
        renderCell: (item) => (
          <TableCellLayout>
            <div className={styles.nameCell}>
              {onDragStart && (
                <Drag16Regular className={styles.dragHandle} aria-hidden="true" />
              )}
              <OwnershipBadge
                ownershipType={item.ownershipType}
                isImmutable={item.isImmutable}
              />
              <span>{item.displayName}</span>
              {item.parentName && (
                <Text className={styles.parentBadge}>(extends: {item.parentName})</Text>
              )}
            </div>
          </TableCellLayout>
        ),
      }),
      createTableColumn<ScopeItem>({
        columnId: 'description',
        compare: (a, b) => a.description.localeCompare(b.description),
        renderHeaderCell: () => 'Description',
        renderCell: (item) => (
          <TableCellLayout>
            <Tooltip content={item.description} relationship="label">
              <Text className={styles.descriptionCell}>{item.description}</Text>
            </Tooltip>
          </TableCellLayout>
        ),
      }),
      createTableColumn<ScopeItem>({
        columnId: 'modifiedOn',
        compare: (a, b) => new Date(a.modifiedOn).getTime() - new Date(b.modifiedOn).getTime(),
        renderHeaderCell: () => 'Modified',
        renderCell: (item) => (
          <TableCellLayout>
            <Text className={styles.dateCell}>{formatDate(item.modifiedOn)}</Text>
          </TableCellLayout>
        ),
      }),
      createTableColumn<ScopeItem>({
        columnId: 'actions',
        renderHeaderCell: () => '',
        renderCell: (item) => (
          <TableCellLayout>
            <div className={styles.actionsCell}>
              <Menu>
                <MenuTrigger disableButtonEnhancement>
                  <Button
                    appearance="subtle"
                    icon={<MoreHorizontal20Regular />}
                    aria-label="More actions"
                  />
                </MenuTrigger>
                <MenuPopover>
                  <MenuList>
                    {onView && (
                      <MenuItem icon={<Eye20Regular />} onClick={() => onView(item)}>
                        View
                      </MenuItem>
                    )}
                    {onSaveAs && (
                      <MenuItem icon={<Copy20Regular />} onClick={() => onSaveAs(item)}>
                        Save As
                      </MenuItem>
                    )}
                    {onExtend && (
                      <MenuItem icon={<ArrowForward20Regular />} onClick={() => onExtend(item)}>
                        Extend
                      </MenuItem>
                    )}
                    {onDelete && !readOnly && item.ownershipType === 'customer' && (
                      <>
                        <MenuDivider />
                        <MenuItem
                          icon={<Delete20Regular />}
                          onClick={() => onDelete(item)}
                        >
                          Delete
                        </MenuItem>
                      </>
                    )}
                  </MenuList>
                </MenuPopover>
              </Menu>
            </div>
          </TableCellLayout>
        ),
      }),
    ],
    [styles, onView, onSaveAs, onExtend, onDelete, onDragStart, readOnly, formatDate]
  );

  return (
    <div className={styles.container}>
      <DataGrid
        items={scopes}
        columns={columns}
        sortable
        getRowId={(item) => item.id}
        className={styles.grid}
      >
        <DataGridHeader>
          <DataGridRow>
            {({ renderHeaderCell }) => (
              <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
            )}
          </DataGridRow>
        </DataGridHeader>
        <DataGridBody<ScopeItem>>
          {({ item, rowId }) => (
            <DataGridRow<ScopeItem>
              key={rowId}
              className={`${styles.row} ${onDragStart ? styles.rowDraggable : ''}`}
              draggable={!!onDragStart}
              onDragStart={(e) => handleDragStart(item, e)}
            >
              {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
            </DataGridRow>
          )}
        </DataGridBody>
      </DataGrid>
    </div>
  );
};

export default ScopeList;
