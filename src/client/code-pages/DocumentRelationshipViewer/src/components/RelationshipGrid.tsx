/**
 * RelationshipGrid — Grid/table view of document relationships
 *
 * NEW component for Code Page. Renders the same relationship data
 * as the graph view but in a tabular format using Fluent v9 DataGrid.
 *
 * Columns: Document, Relationship, Similarity, Type, Parent, Modified
 */

import React, { useMemo, useCallback, useEffect } from 'react';
import {
  makeStyles,
  tokens,
  DataGrid,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  TableColumnDefinition,
  TableColumnSizingOptions,
  createTableColumn,
  Badge,
  Text,
  TableCellLayout,
} from '@fluentui/react-components';
import {
  Document20Regular,
  DocumentPdf20Regular,
  DocumentText20Regular,
  Table20Regular,
  SlideText20Regular,
  Mail20Regular,
  Image20Regular,
  Code20Regular,
  FolderZip20Regular,
  DocumentQuestionMark20Regular,
  Eye20Regular,
} from '@fluentui/react-icons';
import type { DocumentNode, DocumentNodeData } from '../types/graph';

export interface GridRow {
  id: string;
  data: DocumentNodeData;
}

export interface RelationshipGridProps {
  /** All nodes from the API (including source node) */
  nodes: DocumentNode[];
  /** Whether dark mode is enabled */
  isDarkMode?: boolean;
  /** Quick search filter string (case-insensitive document name match) */
  searchQuery?: string;
  /** Callback to expose filtered rows to parent (for CSV export) */
  onFilteredRowsChange?: (rows: GridRow[]) => void;
  /** Callback when a row is clicked — opens FilePreviewDialog */
  onRowClick?: (documentId: string, documentName: string) => void;
  /** Callback when a row is hovered — prefetch preview URL */
  onRowHover?: (documentId: string) => void;
}

const useStyles = makeStyles({
  container: {
    width: '100%',
    height: '100%',
    overflow: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    scrollbarWidth: 'none',
    '&::-webkit-scrollbar': { display: 'none' },
  },
  grid: {
    width: '100%',
    tableLayout: 'fixed',
    '& th, & td': {
      paddingRight: '12px',
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      whiteSpace: 'nowrap',
      boxSizing: 'border-box',
    },
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '200px',
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
  nameCell: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    overflow: 'hidden',
  },
  nameIcon: {
    flexShrink: 0,
    color: tokens.colorBrandForeground1,
  },
  nameText: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  sourceBadge: {
    flexShrink: 0,
  },
  similarityHigh: {
    color: tokens.colorStatusSuccessForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  similarityMed: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  similarityLow: { color: tokens.colorStatusWarningForeground1 },
  similarityNone: { color: tokens.colorNeutralForeground3 },
  badgeContainer: {
    display: 'flex',
    gap: tokens.spacingHorizontalXXS,
    flexWrap: 'wrap',
  },
  clickableRow: {
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
});

const getFileIcon = (fileType: string): React.ReactElement => {
  const type = fileType.toLowerCase();
  switch (type) {
    case 'pdf':
      return <DocumentPdf20Regular />;
    case 'docx':
    case 'doc':
    case 'txt':
      return <DocumentText20Regular />;
    case 'xlsx':
    case 'xls':
    case 'csv':
      return <Table20Regular />;
    case 'pptx':
    case 'ppt':
      return <SlideText20Regular />;
    case 'msg':
    case 'eml':
      return <Mail20Regular />;
    case 'jpg':
    case 'jpeg':
    case 'png':
    case 'gif':
      return <Image20Regular />;
    case 'html':
    case 'htm':
    case 'xml':
    case 'json':
      return <Code20Regular />;
    case 'zip':
    case 'rar':
      return <FolderZip20Regular />;
    case 'file':
    case 'unknown':
      return <DocumentQuestionMark20Regular />;
    default:
      return <Document20Regular />;
  }
};

const formatDate = (isoDate: string | undefined): string => {
  if (!isoDate) return '—';
  try {
    return new Date(isoDate).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  } catch {
    return '—';
  }
};

const getRelationshipBadgeColor = (type: string): 'brand' | 'success' | 'warning' | 'informative' => {
  switch (type) {
    case 'semantic':
      return 'brand';
    case 'same_matter':
    case 'same_project':
      return 'success';
    case 'same_email':
    case 'same_thread':
      return 'warning';
    default:
      return 'informative';
  }
};

export const RelationshipGrid: React.FC<RelationshipGridProps> = ({
  nodes,
  searchQuery,
  onFilteredRowsChange,
  onRowClick,
  onRowHover,
}) => {
  const styles = useStyles();

  // Filter out hub nodes (matter/project/invoice/email) — show only documents
  const allRows = useMemo((): GridRow[] => {
    return nodes
      .filter(n => {
        const nodeType = n.data.nodeType;
        return nodeType !== 'matter' && nodeType !== 'project' && nodeType !== 'invoice' && nodeType !== 'email';
      })
      .map(n => ({ id: n.id, data: n.data }));
  }, [nodes]);

  // Apply search filter (case-insensitive document name match)
  const rows = useMemo((): GridRow[] => {
    if (!searchQuery || searchQuery.trim() === '') return allRows;
    const query = searchQuery.toLowerCase();
    return allRows.filter(row => row.data.name.toLowerCase().includes(query));
  }, [allRows, searchQuery]);

  // Notify parent of filtered rows for CSV export
  useEffect(() => {
    onFilteredRowsChange?.(rows);
  }, [rows, onFilteredRowsChange]);

  const handleRowClickInternal = useCallback(
    (row: GridRow) => {
      if (onRowClick && row.data.documentId) {
        onRowClick(row.data.documentId, row.data.name);
      }
    },
    [onRowClick]
  );

  const handleRowHoverInternal = useCallback(
    (row: GridRow) => {
      if (onRowHover && row.data.documentId) {
        onRowHover(row.data.documentId);
      }
    },
    [onRowHover]
  );

  const columns: TableColumnDefinition<GridRow>[] = useMemo(
    () => [
      createTableColumn<GridRow>({
        columnId: 'name',
        compare: (a, b) => a.data.name.localeCompare(b.data.name),
        renderHeaderCell: () => 'Document',
        renderCell: row => (
          <TableCellLayout>
            <div className={styles.nameCell}>
              <span className={styles.nameIcon}>{getFileIcon(row.data.fileType ?? 'file')}</span>
              <Text className={styles.nameText} title={row.data.name}>
                {row.data.name}
              </Text>
              {row.data.isSource && (
                <Badge className={styles.sourceBadge} appearance="filled" color="brand" size="small">
                  Source
                </Badge>
              )}
            </div>
          </TableCellLayout>
        ),
      }),
      createTableColumn<GridRow>({
        columnId: 'relationship',
        compare: (a, b) => (a.data.relationshipLabel ?? '').localeCompare(b.data.relationshipLabel ?? ''),
        renderHeaderCell: () => 'Relationship',
        renderCell: row => (
          <TableCellLayout>
            {row.data.isSource ? (
              <Badge appearance="outline" color="brand" size="small">
                Source
              </Badge>
            ) : row.data.isOrphanFile ? (
              <Badge appearance="outline" color="warning" size="small">
                File only
              </Badge>
            ) : row.data.relationshipTypes && row.data.relationshipTypes.length > 0 ? (
              <div className={styles.badgeContainer}>
                {row.data.relationshipTypes.map(rel => (
                  <Badge key={rel.type} appearance="outline" color={getRelationshipBadgeColor(rel.type)} size="small">
                    {rel.label}
                  </Badge>
                ))}
              </div>
            ) : row.data.relationshipLabel ? (
              <Badge appearance="outline" color="informative" size="small">
                {row.data.relationshipLabel}
              </Badge>
            ) : (
              <Text>—</Text>
            )}
          </TableCellLayout>
        ),
      }),
      createTableColumn<GridRow>({
        columnId: 'similarity',
        compare: (a, b) => (b.data.similarity ?? 0) - (a.data.similarity ?? 0),
        renderHeaderCell: () => 'Similarity',
        renderCell: row => {
          if (row.data.isSource)
            return (
              <TableCellLayout>
                <Text className={styles.similarityNone}>—</Text>
              </TableCellLayout>
            );
          const sim = row.data.similarity ?? 0;
          const pct = `${Math.round(sim * 100)}%`;
          const cls =
            sim >= 0.9
              ? styles.similarityHigh
              : sim >= 0.75
                ? styles.similarityMed
                : sim >= 0.65
                  ? styles.similarityLow
                  : styles.similarityNone;
          return (
            <TableCellLayout>
              <Text className={cls}>{pct}</Text>
            </TableCellLayout>
          );
        },
      }),
      createTableColumn<GridRow>({
        columnId: 'type',
        compare: (a, b) => (a.data.documentType ?? '').localeCompare(b.data.documentType ?? ''),
        renderHeaderCell: () => 'Type',
        renderCell: row => (
          <TableCellLayout>
            <Text>{row.data.documentType ?? row.data.fileType?.toUpperCase() ?? '—'}</Text>
          </TableCellLayout>
        ),
      }),
      createTableColumn<GridRow>({
        columnId: 'parent',
        compare: (a, b) => (a.data.parentEntityName ?? '').localeCompare(b.data.parentEntityName ?? ''),
        renderHeaderCell: () => 'Parent Entity',
        renderCell: row => (
          <TableCellLayout>
            <Text>{row.data.parentEntityName ?? '—'}</Text>
          </TableCellLayout>
        ),
      }),
      createTableColumn<GridRow>({
        columnId: 'modified',
        compare: (a, b) => (a.data.modifiedOn ?? '').localeCompare(b.data.modifiedOn ?? ''),
        renderHeaderCell: () => 'Modified',
        renderCell: row => (
          <TableCellLayout>
            <Text>{formatDate(row.data.modifiedOn)}</Text>
          </TableCellLayout>
        ),
      }),
      createTableColumn<GridRow>({
        columnId: 'preview',
        renderHeaderCell: () => 'Preview',
        renderCell: row => (
          <TableCellLayout>
            {row.data.documentId && (
              <Eye20Regular
                style={{
                  color: tokens.colorBrandForeground1,
                  cursor: 'pointer',
                }}
              />
            )}
          </TableCellLayout>
        ),
      }),
    ],
    [styles, handleRowClickInternal]
  );

  const columnSizingOptions: TableColumnSizingOptions = useMemo(
    () => ({
      name: { defaultWidth: 600, minWidth: 300, idealWidth: 600 },
      relationship: { defaultWidth: 160, minWidth: 100, idealWidth: 160 },
      similarity: { defaultWidth: 100, minWidth: 80, idealWidth: 100 },
      type: { defaultWidth: 100, minWidth: 80, idealWidth: 100 },
      parent: { defaultWidth: 180, minWidth: 100, idealWidth: 180 },
      modified: { defaultWidth: 130, minWidth: 100, idealWidth: 130 },
      preview: { defaultWidth: 60, minWidth: 50, idealWidth: 60 },
    }),
    []
  );

  if (rows.length === 0) {
    return (
      <div className={styles.container}>
        <div className={styles.emptyState}>
          <Text size={400}>No documents to display</Text>
          <Text size={200}>Load a document with AI embeddings to see related documents</Text>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <DataGrid
        className={styles.grid}
        items={rows}
        columns={columns}
        sortable
        resizableColumns
        columnSizingOptions={columnSizingOptions}
        getRowId={row => row.id}
        focusMode="composite"
      >
        <DataGridHeader>
          <DataGridRow>
            {({ renderHeaderCell }) => <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>}
          </DataGridRow>
        </DataGridHeader>
        <DataGridBody<GridRow>>
          {({ item, rowId }) => (
            <DataGridRow<GridRow>
              key={rowId}
              className={onRowClick ? styles.clickableRow : undefined}
              onClick={() => handleRowClickInternal(item)}
              onMouseEnter={() => handleRowHoverInternal(item)}
            >
              {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
            </DataGridRow>
          )}
        </DataGridBody>
      </DataGrid>
    </div>
  );
};

export default RelationshipGrid;
