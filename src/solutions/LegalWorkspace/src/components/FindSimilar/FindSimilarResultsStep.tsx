/**
 * FindSimilarResultsStep.tsx
 * Step 2 of the Find Similar wizard — tabbed results grid.
 *
 * Displays search results in three domain tabs:
 *   - Documents (sprk_document)
 *   - Matters   (sprk_matter)
 *   - Projects  (sprk_project)
 *
 * Uses the same DataGrid pattern as the Semantic Search code page
 * (SearchResultsGrid.tsx) with dataType-based cell rendering, sortable
 * columns, column visibility picker, and 44px row height.
 */
import * as React from 'react';
import {
  DataGrid,
  DataGridHeader,
  DataGridRow,
  DataGridHeaderCell,
  DataGridBody,
  DataGridCell,
  createTableColumn,
  Button,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItemCheckbox,
  Spinner,
  Tab,
  TabList,
  Text,
  Badge,
  makeStyles,
  tokens,
  type TableColumnDefinition,
  type TableColumnSizingOptions,
} from '@fluentui/react-components';
import { ColumnTriple20Regular } from '@fluentui/react-icons';
import type {
  FindSimilarStatus,
  FindSimilarDomain,
  IFindSimilarResults,
  IDocumentResult,
  IRecordResult,
  IGridRecord,
  IGridColumn,
} from './findSimilarTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFindSimilarResultsStepProps {
  status: FindSimilarStatus;
  results: IFindSimilarResults | null;
  errorMessage: string | null;
  onRetry: () => void;
}

// ---------------------------------------------------------------------------
// Column definitions per domain
// ---------------------------------------------------------------------------

const DOCUMENT_COLUMNS: IGridColumn[] = [
  { name: 'name', displayName: 'Name', dataType: 'SingleLine.Text', visualSizeFactor: 2.5 },
  { name: 'combinedScore', displayName: 'Score', dataType: 'Percentage', visualSizeFactor: 0.8 },
  { name: 'documentType', displayName: 'Doc Type', dataType: 'SingleLine.Text', visualSizeFactor: 1 },
  { name: 'fileType', displayName: 'File Type', dataType: 'FileType', visualSizeFactor: 0.7 },
  { name: 'parentEntityName', displayName: 'Related To', dataType: 'SingleLine.Text', visualSizeFactor: 1.5 },
  { name: 'updatedAt', displayName: 'Updated', dataType: 'DateAndTime.DateOnly', visualSizeFactor: 1 },
];

const MATTER_COLUMNS: IGridColumn[] = [
  { name: 'recordName', displayName: 'Matter Name', dataType: 'SingleLine.Text', visualSizeFactor: 2.5 },
  { name: 'confidenceScore', displayName: 'Score', dataType: 'Percentage', visualSizeFactor: 0.8 },
  { name: 'recordDescription', displayName: 'Description', dataType: 'SingleLine.Text', visualSizeFactor: 2 },
  { name: 'organizations', displayName: 'Organizations', dataType: 'StringArray', visualSizeFactor: 1.5 },
  { name: 'people', displayName: 'People', dataType: 'StringArray', visualSizeFactor: 1.5 },
  { name: 'modifiedAt', displayName: 'Modified', dataType: 'DateAndTime.DateOnly', visualSizeFactor: 1 },
];

const PROJECT_COLUMNS: IGridColumn[] = [
  { name: 'recordName', displayName: 'Project Name', dataType: 'SingleLine.Text', visualSizeFactor: 2.5 },
  { name: 'confidenceScore', displayName: 'Score', dataType: 'Percentage', visualSizeFactor: 0.8 },
  { name: 'recordDescription', displayName: 'Description', dataType: 'SingleLine.Text', visualSizeFactor: 2 },
  { name: 'organizations', displayName: 'Organizations', dataType: 'StringArray', visualSizeFactor: 1.5 },
  { name: 'people', displayName: 'People', dataType: 'StringArray', visualSizeFactor: 1.5 },
  { name: 'modifiedAt', displayName: 'Modified', dataType: 'DateAndTime.DateOnly', visualSizeFactor: 1 },
];

const COLUMNS_BY_DOMAIN: Record<FindSimilarDomain, IGridColumn[]> = {
  documents: DOCUMENT_COLUMNS,
  matters: MATTER_COLUMNS,
  projects: PROJECT_COLUMNS,
};

// ---------------------------------------------------------------------------
// DataType-based cell rendering (matches SearchResultsGrid pattern)
// ---------------------------------------------------------------------------

function renderByDataType(value: unknown, dataType: string): string {
  if (value == null || value === '') return '';

  switch (dataType) {
    case 'Percentage': {
      const num = typeof value === 'number' ? value : Number(value);
      if (isNaN(num)) return String(value);
      return `${Math.round(num * 100)}%`;
    }
    case 'DateAndTime.DateOnly': {
      if (typeof value !== 'string' && typeof value !== 'number') return String(value);
      try {
        return new Date(value as string | number).toLocaleDateString(undefined, {
          year: 'numeric',
          month: 'short',
          day: 'numeric',
        });
      } catch {
        return String(value);
      }
    }
    case 'StringArray': {
      if (Array.isArray(value)) return value.join(', ');
      return typeof value === 'string' ? value : String(value);
    }
    case 'FileType': {
      return typeof value === 'string' ? value.toUpperCase() : String(value);
    }
    default: {
      if (typeof value === 'number') return value.toLocaleString();
      if (Array.isArray(value)) return value.join(', ');
      return String(value);
    }
  }
}

// ---------------------------------------------------------------------------
// Result mapping helpers
// ---------------------------------------------------------------------------

function mapDocumentResults(docs: IDocumentResult[]): IGridRecord[] {
  return docs.map((d) => ({
    id: d.documentId,
    entityName: 'sprk_document',
    name: d.name,
    combinedScore: d.combinedScore,
    documentType: d.documentType,
    fileType: d.fileType,
    parentEntityName: d.parentEntityName,
    parentEntityType: d.parentEntityType,
    parentEntityId: d.parentEntityId,
    updatedAt: d.updatedAt,
    createdAt: d.createdAt,
  }));
}

function mapRecordResults(records: IRecordResult[]): IGridRecord[] {
  return records.map((r) => ({
    id: r.recordId,
    entityName: r.recordType,
    recordName: r.recordName,
    confidenceScore: r.confidenceScore,
    recordDescription: r.recordDescription,
    organizations: r.organizations,
    people: r.people,
    matchReasons: r.matchReasons,
    modifiedAt: r.modifiedAt,
    createdAt: r.createdAt,
  }));
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    height: '100%',
  },
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalL,
    minHeight: '300px',
  },
  errorContainer: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  tabContent: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
    overflow: 'hidden',
  },
  gridToolbar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
    minHeight: '32px',
  },
  gridContainer: {
    flex: 1,
    overflow: 'auto',
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingVerticalXXL,
    minHeight: '200px',
  },
  cell: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },
  headerCell: {
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    fontWeight: tokens.fontWeightSemibold,
  },
  stepTitle: {
    display: 'block',
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    display: 'block',
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Domain tab grid (inner component)
// ---------------------------------------------------------------------------

interface IDomainGridProps {
  records: IGridRecord[];
  columns: IGridColumn[];
  domain: FindSimilarDomain;
}

const DomainGrid: React.FC<IDomainGridProps> = ({ records, columns, domain }) => {
  const styles = useStyles();
  const [hiddenColumns, setHiddenColumns] = React.useState<Set<string>>(new Set());

  // Reset hidden columns when domain changes
  React.useEffect(() => {
    setHiddenColumns(new Set());
  }, [domain]);

  const visibleColumns = React.useMemo(
    () => columns.filter((col) => !hiddenColumns.has(col.name)),
    [columns, hiddenColumns],
  );

  type GridItem = IGridRecord & { _rowId: number };

  const tableColumns: TableColumnDefinition<GridItem>[] = React.useMemo(
    () =>
      visibleColumns.map((col) =>
        createTableColumn<GridItem>({
          columnId: col.name,
          compare: (a, b) => {
            const aVal = a[col.name];
            const bVal = b[col.name];
            if (typeof aVal === 'number' && typeof bVal === 'number') {
              return aVal - bVal;
            }
            return String(aVal ?? '').localeCompare(String(bVal ?? ''));
          },
          renderHeaderCell: () => col.displayName,
          renderCell: (item) => renderByDataType(item[col.name], col.dataType),
        }),
      ),
    [visibleColumns],
  );

  const columnSizingOptions: TableColumnSizingOptions = React.useMemo(() => {
    const options: TableColumnSizingOptions = {};
    for (const col of visibleColumns) {
      const defaultWidth = col.visualSizeFactor ? Math.round(col.visualSizeFactor * 100) : 150;
      options[col.name] = {
        defaultWidth,
        minWidth: Math.max(80, Math.round(defaultWidth * 0.5)),
        idealWidth: defaultWidth,
      };
    }
    return options;
  }, [visibleColumns]);

  const columnCheckedValues = React.useMemo(() => {
    const visible = columns
      .filter((col) => !hiddenColumns.has(col.name))
      .map((col) => col.name);
    return { columns: visible };
  }, [columns, hiddenColumns]);

  const handleCheckedValueChange = React.useCallback(
    (_ev: unknown, data: { name: string; checkedItems: string[] }) => {
      if (data.name !== 'columns') return;
      const visibleSet = new Set(data.checkedItems);
      const newHidden = new Set<string>();
      for (const col of columns) {
        if (!visibleSet.has(col.name)) {
          newHidden.add(col.name);
        }
      }
      setHiddenColumns(newHidden);
    },
    [columns],
  );

  const items: GridItem[] = React.useMemo(
    () => records.map((r, i) => ({ ...r, _rowId: i })),
    [records],
  );

  if (records.length === 0) {
    return (
      <div className={styles.emptyState}>
        <Text size={400} weight="semibold">No results found</Text>
        <Text size={200}>
          No similar {domain} were found for the uploaded files.
        </Text>
      </div>
    );
  }

  return (
    <>
      <div className={styles.gridToolbar}>
        <Menu checkedValues={columnCheckedValues} onCheckedValueChange={handleCheckedValueChange}>
          <MenuTrigger disableButtonEnhancement>
            <Button
              appearance="subtle"
              size="small"
              icon={<ColumnTriple20Regular />}
              aria-label="Choose columns"
            >
              Columns
            </Button>
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              {columns.map((col) => (
                <MenuItemCheckbox key={col.name} name="columns" value={col.name}>
                  {col.displayName}
                </MenuItemCheckbox>
              ))}
            </MenuList>
          </MenuPopover>
        </Menu>
      </div>

      <div className={styles.gridContainer}>
        <DataGrid
          items={items}
          columns={tableColumns}
          sortable
          resizableColumns
          columnSizingOptions={columnSizingOptions}
          getRowId={(item: GridItem) => item._rowId}
          style={{ minWidth: '100%' }}
          aria-label={`Similar ${domain} results`}
        >
          <DataGridHeader>
            <DataGridRow>
              {({ renderHeaderCell }) => (
                <DataGridHeaderCell className={styles.headerCell}>
                  {renderHeaderCell()}
                </DataGridHeaderCell>
              )}
            </DataGridRow>
          </DataGridHeader>
          <DataGridBody<GridItem>>
            {({ item, rowId }) => (
              <DataGridRow<GridItem> key={rowId} style={{ height: '44px' }}>
                {({ renderCell }) => (
                  <DataGridCell className={styles.cell}>
                    {renderCell(item)}
                  </DataGridCell>
                )}
              </DataGridRow>
            )}
          </DataGridBody>
        </DataGrid>
      </div>
    </>
  );
};

// ---------------------------------------------------------------------------
// Main component
// ---------------------------------------------------------------------------

export const FindSimilarResultsStep: React.FC<IFindSimilarResultsStepProps> = ({
  status,
  results,
  errorMessage,
  onRetry,
}) => {
  const styles = useStyles();
  const [activeDomain, setActiveDomain] = React.useState<FindSimilarDomain>('documents');

  // Derive grid data — must be above early returns to satisfy rules of hooks
  const gridData: { records: IGridRecord[]; count: number } = React.useMemo(() => {
    if (!results) return { records: [], count: 0 };
    switch (activeDomain) {
      case 'documents':
        return { records: mapDocumentResults(results.documents ?? []), count: results.documentsTotalCount };
      case 'matters':
        return { records: mapRecordResults(results.matters ?? []), count: results.mattersTotalCount };
      case 'projects':
        return { records: mapRecordResults(results.projects ?? []), count: results.projectsTotalCount };
    }
  }, [activeDomain, results]);

  // Loading state
  if (status === 'loading') {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="large" label="Searching for similar items..." labelPosition="below" />
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          Extracting text and running semantic search. This may take a moment.
        </Text>
      </div>
    );
  }

  // Error state
  if (status === 'error') {
    return (
      <div className={styles.errorContainer}>
        <Text as="h2" size={500} weight="semibold">
          Search Results
        </Text>
        <Text size={300} style={{ color: tokens.colorPaletteRedForeground1 }}>
          {errorMessage || 'An error occurred while searching.'}
        </Text>
        <Button appearance="primary" onClick={onRetry} style={{ alignSelf: 'flex-start' }}>
          Retry Search
        </Button>
      </div>
    );
  }

  // Idle state
  if (status === 'idle' || !results) {
    return (
      <div className={styles.container}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Search Results
        </Text>
        <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
          Upload files and proceed to search for similar items.
        </Text>
      </div>
    );
  }

  const totalFound = results.documentsTotalCount + results.mattersTotalCount + results.projectsTotalCount;

  // Success state
  return (
    <div className={styles.container}>
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Search Results
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Found {totalFound} similar item{totalFound !== 1 ? 's' : ''} across documents, matters, and projects.
        </Text>
      </div>

      <TabList
        selectedValue={activeDomain}
        onTabSelect={(_e, data) => setActiveDomain(data.value as FindSimilarDomain)}
      >
        <Tab value="documents">
          Documents
          {results.documentsTotalCount > 0 && (
            <Badge appearance="tint" color="informative" size="small" style={{ marginLeft: '6px' }}>
              {results.documentsTotalCount}
            </Badge>
          )}
        </Tab>
        <Tab value="matters">
          Matters
          {results.mattersTotalCount > 0 && (
            <Badge appearance="tint" color="informative" size="small" style={{ marginLeft: '6px' }}>
              {results.mattersTotalCount}
            </Badge>
          )}
        </Tab>
        <Tab value="projects">
          Projects
          {results.projectsTotalCount > 0 && (
            <Badge appearance="tint" color="informative" size="small" style={{ marginLeft: '6px' }}>
              {results.projectsTotalCount}
            </Badge>
          )}
        </Tab>
      </TabList>

      <div className={styles.tabContent}>
        <DomainGrid
          records={gridData.records}
          columns={COLUMNS_BY_DOMAIN[activeDomain]}
          domain={activeDomain}
        />
      </div>
    </div>
  );
};
