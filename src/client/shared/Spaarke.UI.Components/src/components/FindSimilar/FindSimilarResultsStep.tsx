/**
 * FindSimilarResultsStep.tsx
 * Step 2 of the Find Similar Records wizard — tabbed results grid.
 *
 * Displays search results in three domain tabs:
 *   - Documents (sprk_document) — Name, Score, File Type + action icons
 *   - Matters   (sprk_matter)   — Matter Name, Score, Description + action icon
 *   - Projects  (sprk_project)  — Project Name, Score, Description + action icon
 *
 * Uses progressive rendering (intersection observer) instead of scrollbars.
 *
 * Shared library version — external dependencies (navigation, file preview
 * services) are injected via callback props.
 */
import * as React from "react";
import {
  DataGrid,
  DataGridHeader,
  DataGridRow,
  DataGridHeaderCell,
  DataGridBody,
  DataGridCell,
  createTableColumn,
  Button,
  Spinner,
  Tab,
  TabList,
  Text,
  Badge,
  Tooltip,
  makeStyles,
  tokens,
  type TableColumnDefinition,
  type TableColumnSizingOptions,
} from "@fluentui/react-components";
import { EyeRegular, OpenRegular } from "@fluentui/react-icons";
import { FilePreviewDialog } from "../FilePreview";
import type { IFilePreviewServices } from "../FilePreview/filePreviewTypes";
import type {
  FindSimilarStatus,
  FindSimilarDomain,
  IFindSimilarResults,
  IDocumentResult,
  IRecordResult,
  IGridRecord,
  IGridColumn,
  INavigationMessage,
} from "./findSimilarTypes";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFindSimilarResultsStepProps {
  status: FindSimilarStatus;
  results: IFindSimilarResults | null;
  errorMessage: string | null;
  onRetry: () => void;
  /** Navigate to a Dataverse entity record. */
  onNavigateToEntity: (message: INavigationMessage) => void;
  /** Service callbacks for the FilePreviewDialog. */
  filePreviewServices: IFilePreviewServices;
}

// ---------------------------------------------------------------------------
// Column definitions per domain (simplified per user feedback)
// ---------------------------------------------------------------------------

const DOCUMENT_COLUMNS: IGridColumn[] = [
  {
    name: "name",
    displayName: "Name",
    dataType: "SingleLine.Text",
    visualSizeFactor: 3,
  },
  {
    name: "combinedScore",
    displayName: "Score",
    dataType: "Percentage",
    visualSizeFactor: 0.8,
  },
  {
    name: "fileType",
    displayName: "File Type",
    dataType: "FileType",
    visualSizeFactor: 1,
  },
];

const MATTER_COLUMNS: IGridColumn[] = [
  {
    name: "recordName",
    displayName: "Matter Name",
    dataType: "SingleLine.Text",
    visualSizeFactor: 2.5,
  },
  {
    name: "confidenceScore",
    displayName: "Score",
    dataType: "Percentage",
    visualSizeFactor: 0.8,
  },
  {
    name: "recordDescription",
    displayName: "Description",
    dataType: "SingleLine.Text",
    visualSizeFactor: 3,
  },
];

const PROJECT_COLUMNS: IGridColumn[] = [
  {
    name: "recordName",
    displayName: "Project Name",
    dataType: "SingleLine.Text",
    visualSizeFactor: 2.5,
  },
  {
    name: "confidenceScore",
    displayName: "Score",
    dataType: "Percentage",
    visualSizeFactor: 0.8,
  },
  {
    name: "recordDescription",
    displayName: "Description",
    dataType: "SingleLine.Text",
    visualSizeFactor: 3,
  },
];

const COLUMNS_BY_DOMAIN: Record<FindSimilarDomain, IGridColumn[]> = {
  documents: DOCUMENT_COLUMNS,
  matters: MATTER_COLUMNS,
  projects: PROJECT_COLUMNS,
};

// ---------------------------------------------------------------------------
// Lazy loading constants
// ---------------------------------------------------------------------------

const PAGE_SIZE = 10;

// ---------------------------------------------------------------------------
// DataType-based cell rendering
// ---------------------------------------------------------------------------

function renderByDataType(value: unknown, dataType: string): string {
  if (value == null || value === "") return "";

  switch (dataType) {
    case "Percentage": {
      const num = typeof value === "number" ? value : Number(value);
      if (isNaN(num)) return String(value);
      return `${Math.round(num * 100)}%`;
    }
    case "FileType": {
      return typeof value === "string" ? value.toUpperCase() : String(value);
    }
    default: {
      if (typeof value === "number") return value.toLocaleString();
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
    entityName: "sprk_document",
    name: d.name,
    combinedScore: d.combinedScore,
    fileType: d.fileType,
    parentEntityType: d.parentEntityType,
    parentEntityName: d.parentEntityName,
    parentEntityId: d.parentEntityId,
  }));
}

function mapRecordResults(records: IRecordResult[]): IGridRecord[] {
  return records.map((r) => ({
    id: r.recordId,
    entityName: r.recordType,
    recordName: r.recordName,
    confidenceScore: r.confidenceScore,
    recordDescription: r.recordDescription,
  }));
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    height: "100%",
  },
  loadingContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalL,
    minHeight: "300px",
  },
  errorContainer: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  tabContent: {
    display: "flex",
    flexDirection: "column",
    flex: 1,
    minHeight: 0,
  },
  gridContainer: {
    flex: 1,
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingVerticalXXL,
    minHeight: "200px",
  },
  cell: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },
  headerCell: {
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    fontWeight: tokens.fontWeightSemibold,
  },
  actionsCell: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXXS,
  },
  stepTitle: {
    display: "block",
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    display: "block",
    color: tokens.colorNeutralForeground3,
  },
  sentinel: {
    height: "1px",
    width: "100%",
  },
  loadMoreText: {
    textAlign: "center",
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
});

// ---------------------------------------------------------------------------
// Domain tab grid (inner component)
// ---------------------------------------------------------------------------

interface IDomainGridProps {
  records: IGridRecord[];
  columns: IGridColumn[];
  domain: FindSimilarDomain;
  onNavigateToEntity: (message: INavigationMessage) => void;
  filePreviewServices: IFilePreviewServices;
}

const DomainGrid: React.FC<IDomainGridProps> = ({
  records,
  columns,
  domain,
  onNavigateToEntity,
  filePreviewServices,
}) => {
  const styles = useStyles();

  // Preview dialog state
  const [previewOpen, setPreviewOpen] = React.useState(false);
  const [previewDocId, setPreviewDocId] = React.useState("");
  const [previewDocName, setPreviewDocName] = React.useState("");

  // Lazy loading state
  const [visibleCount, setVisibleCount] = React.useState(PAGE_SIZE);
  const sentinelRef = React.useRef<HTMLDivElement>(null);

  // Reset visible count when domain or records change
  React.useEffect(() => {
    setVisibleCount(PAGE_SIZE);
  }, [domain, records]);

  // Intersection observer for lazy loading
  React.useEffect(() => {
    const sentinel = sentinelRef.current;
    if (!sentinel) return;

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0].isIntersecting) {
          setVisibleCount((prev) => Math.min(prev + PAGE_SIZE, records.length));
        }
      },
      { threshold: 0.1 },
    );

    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [records.length]);

  type GridItem = IGridRecord & { _rowId: number };

  // Row action handler
  const handleOpenRecord = React.useCallback(
    (record: IGridRecord) => {
      onNavigateToEntity({
        action: "openRecord",
        entityName: record.entityName,
        entityId: record.id,
      });
    },
    [onNavigateToEntity],
  );

  // Build table columns (data columns + actions column)
  const tableColumns: TableColumnDefinition<GridItem>[] = React.useMemo(() => {
    const dataCols = columns.map((col) =>
      createTableColumn<GridItem>({
        columnId: col.name,
        compare: (a, b) => {
          const aVal = a[col.name];
          const bVal = b[col.name];
          if (typeof aVal === "number" && typeof bVal === "number") {
            return aVal - bVal;
          }
          return String(aVal ?? "").localeCompare(String(bVal ?? ""));
        },
        renderHeaderCell: () => col.displayName,
        renderCell: (item) => renderByDataType(item[col.name], col.dataType),
      }),
    );

    // Actions column
    const actionsCol = createTableColumn<GridItem>({
      columnId: "_actions",
      compare: () => 0,
      renderHeaderCell: () => "",
      renderCell: (item) => {
        if (domain === "documents") {
          return (
            <div className={styles.actionsCell}>
              <Tooltip content="Preview" relationship="label">
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<EyeRegular />}
                  aria-label="Preview document"
                  onClick={(e) => {
                    e.stopPropagation();
                    setPreviewDocId(item.id);
                    setPreviewDocName(item.name ?? item.id);
                    setPreviewOpen(true);
                  }}
                />
              </Tooltip>
            </div>
          );
        }

        // Matters and Projects — single "Open" action
        const label = domain === "matters" ? "Open matter" : "Open project";
        return (
          <div className={styles.actionsCell}>
            <Tooltip content={label} relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<OpenRegular />}
                aria-label={label}
                onClick={(e) => {
                  e.stopPropagation();
                  handleOpenRecord(item);
                }}
              />
            </Tooltip>
          </div>
        );
      },
    });

    return [...dataCols, actionsCol];
  }, [columns, domain, styles.actionsCell, handleOpenRecord]);

  const columnSizingOptions: TableColumnSizingOptions = React.useMemo(() => {
    const options: TableColumnSizingOptions = {};
    for (const col of columns) {
      const defaultWidth = col.visualSizeFactor
        ? Math.round(col.visualSizeFactor * 100)
        : 150;
      options[col.name] = {
        defaultWidth,
        minWidth: Math.max(80, Math.round(defaultWidth * 0.5)),
        idealWidth: defaultWidth,
      };
    }
    // Actions column sizing
    options["_actions"] = {
      defaultWidth: 48,
      minWidth: 48,
      idealWidth: 48,
    };
    return options;
  }, [columns]);

  const visibleRecords = React.useMemo(
    () => records.slice(0, visibleCount),
    [records, visibleCount],
  );

  const items: GridItem[] = React.useMemo(
    () => visibleRecords.map((r, i) => ({ ...r, _rowId: i })),
    [visibleRecords],
  );

  if (records.length === 0) {
    return (
      <div className={styles.emptyState}>
        <Text size={400} weight="semibold">
          No results found
        </Text>
        <Text size={200}>
          No similar {domain} were found for the uploaded files.
        </Text>
      </div>
    );
  }

  const hasMore = visibleCount < records.length;

  return (
    <>
      <div className={styles.gridContainer}>
        <DataGrid
          items={items}
          columns={tableColumns}
          sortable
          resizableColumns
          columnSizingOptions={columnSizingOptions}
          getRowId={(item: GridItem) => item._rowId}
          style={{ minWidth: "100%" }}
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
              <DataGridRow<GridItem> key={rowId} style={{ height: "44px" }}>
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

      {hasMore && (
        <>
          <div ref={sentinelRef} className={styles.sentinel} />
          <Text size={200} className={styles.loadMoreText}>
            Showing {visibleCount} of {records.length} results...
          </Text>
        </>
      )}

      {/* File Preview Dialog (documents domain) */}
      {domain === "documents" && (
        <FilePreviewDialog
          open={previewOpen}
          documentId={previewDocId}
          documentName={previewDocName}
          onClose={() => setPreviewOpen(false)}
          services={filePreviewServices}
        />
      )}
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
  onNavigateToEntity,
  filePreviewServices,
}) => {
  const styles = useStyles();
  const [activeDomain, setActiveDomain] =
    React.useState<FindSimilarDomain>("documents");

  // Derive grid data — must be above early returns to satisfy rules of hooks
  const gridData: { records: IGridRecord[]; count: number } =
    React.useMemo(() => {
      if (!results) return { records: [], count: 0 };
      switch (activeDomain) {
        case "documents":
          return {
            records: mapDocumentResults(results.documents ?? []),
            count: results.documentsTotalCount,
          };
        case "matters":
          return {
            records: mapRecordResults(results.matters ?? []),
            count: results.mattersTotalCount,
          };
        case "projects":
          return {
            records: mapRecordResults(results.projects ?? []),
            count: results.projectsTotalCount,
          };
      }
    }, [activeDomain, results]);

  // Loading state
  if (status === "loading") {
    return (
      <div className={styles.loadingContainer}>
        <Spinner
          size="large"
          label="Searching for similar items..."
          labelPosition="below"
        />
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          Extracting text and running semantic search. This may take a moment.
        </Text>
      </div>
    );
  }

  // Error state
  if (status === "error") {
    return (
      <div className={styles.errorContainer}>
        <Text as="h2" size={500} weight="semibold">
          Search Results
        </Text>
        <Text size={300} style={{ color: tokens.colorPaletteRedForeground1 }}>
          {errorMessage || "An error occurred while searching."}
        </Text>
        <Button
          appearance="primary"
          onClick={onRetry}
          style={{ alignSelf: "flex-start" }}
        >
          Retry Search
        </Button>
      </div>
    );
  }

  // Idle state
  if (status === "idle" || !results) {
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

  const totalFound =
    results.documentsTotalCount +
    results.mattersTotalCount +
    results.projectsTotalCount;

  // Success state
  return (
    <div className={styles.container}>
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Search Results
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Found {totalFound} similar item{totalFound !== 1 ? "s" : ""} across
          documents, matters, and projects.
        </Text>
      </div>

      <TabList
        selectedValue={activeDomain}
        onTabSelect={(_e, data) =>
          setActiveDomain(data.value as FindSimilarDomain)
        }
      >
        <Tab value="documents">
          Documents
          {results.documentsTotalCount > 0 && (
            <Badge
              appearance="tint"
              color="informative"
              size="small"
              style={{ marginLeft: "6px" }}
            >
              {results.documentsTotalCount}
            </Badge>
          )}
        </Tab>
        <Tab value="matters">
          Matters
          {results.mattersTotalCount > 0 && (
            <Badge
              appearance="tint"
              color="informative"
              size="small"
              style={{ marginLeft: "6px" }}
            >
              {results.mattersTotalCount}
            </Badge>
          )}
        </Tab>
        <Tab value="projects">
          Projects
          {results.projectsTotalCount > 0 && (
            <Badge
              appearance="tint"
              color="informative"
              size="small"
              style={{ marginLeft: "6px" }}
            >
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
          onNavigateToEntity={onNavigateToEntity}
          filePreviewServices={filePreviewServices}
        />
      </div>
    </div>
  );
};
