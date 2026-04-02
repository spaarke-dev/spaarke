/**
 * App — Document List dialog.
 *
 * Fetches all sprk_document records for the current user via Xrm.WebApi
 * and renders them in a scrollable list. Opened as a dialog from the
 * Corporate Workspace My Documents toolbar.
 *
 * Web resource name: sprk_alldocuments
 */

import * as React from "react";
import {
  FluentProvider,
  makeStyles,
  tokens,
  Text,
  Spinner,
  Button,
  Tooltip,
} from "@fluentui/react-components";
import {
  ArrowClockwiseRegular,
  DocumentRegular,
  DocumentPdfRegular,
  DocumentTextRegular,
  TableRegular,
  SlideTextRegular,
} from "@fluentui/react-icons";
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface IDocument {
  sprk_documentid: string;
  sprk_documentname: string;
  sprk_documentdescription?: string;
  sprk_filetype?: string;
  statuscode?: number;
  "statuscode@OData.Community.Display.V1.FormattedValue"?: string;
  createdon?: string;
  modifiedon?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    height: "100%",
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground2,
  },
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
    flexShrink: 0,
  },
  list: {
    flex: "1 1 0",
    overflowY: "auto",
    padding: tokens.spacingVerticalS,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
  card: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow2,
    borderLeftWidth: "3px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorBrandStroke1,
    cursor: "pointer",
    transitionProperty: "background-color, box-shadow",
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      boxShadow: tokens.shadow4,
    },
  },
  iconCircle: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flexShrink: 0,
    width: "36px",
    height: "36px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
    marginTop: "2px",
  },
  content: {
    flex: "1 1 0",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: "2px",
  },
  primaryRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "nowrap",
    minWidth: 0,
  },
  name: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  description: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    color: tokens.colorNeutralForeground3,
    flexShrink: 1,
    minWidth: 0,
  },
  secondaryRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
  },
  badge: {
    display: "inline-flex",
    alignItems: "center",
    borderRadius: tokens.borderRadiusSmall,
    paddingTop: "1px",
    paddingBottom: "1px",
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase100,
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    flex: "1 1 0",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function getXrm(): any {
  return (window as any)?.Xrm ?? (window.parent as any)?.Xrm ?? (window.top as any)?.Xrm;
}

function getFileIcon(filetype?: string) {
  const t = (filetype ?? "").toLowerCase();
  if (t === "pdf") return DocumentPdfRegular;
  if (t === "doc" || t === "docx") return DocumentTextRegular;
  if (t === "xls" || t === "xlsx" || t === "csv") return TableRegular;
  if (t === "ppt" || t === "pptx") return SlideTextRegular;
  return DocumentRegular;
}

function formatDate(iso?: string): string {
  if (!iso) return "";
  try {
    return new Date(iso).toLocaleDateString(undefined, {
      month: "short", day: "numeric", year: "numeric",
    });
  } catch {
    return "";
  }
}

async function fetchDocuments(): Promise<IDocument[]> {
  const xrm = getXrm();
  if (!xrm?.WebApi) return [];

  const result = await xrm.WebApi.retrieveMultipleRecords(
    "sprk_document",
    "?$select=sprk_documentid,sprk_documentname,sprk_documentdescription,sprk_filetype,statuscode,createdon,modifiedon&$orderby=modifiedon desc&$top=200"
  );
  return result?.entities ?? [];
}

function openRecord(documentId: string): void {
  const xrm = getXrm();
  xrm?.Navigation?.openForm?.({ entityName: "sprk_document", entityId: documentId });
}

// ---------------------------------------------------------------------------
// DocumentRow
// ---------------------------------------------------------------------------

const DocumentRow: React.FC<{ doc: IDocument }> = React.memo(({ doc }) => {
  const styles = useStyles();
  const Icon = getFileIcon(doc.sprk_filetype);
  const statusLabel = doc["statuscode@OData.Community.Display.V1.FormattedValue"];

  return (
    <div
      className={styles.card}
      role="listitem"
      tabIndex={0}
      aria-label={doc.sprk_documentname}
      onDoubleClick={() => openRecord(doc.sprk_documentid)}
      onKeyDown={(e) => { if (e.key === "Enter") openRecord(doc.sprk_documentid); }}
    >
      <div className={styles.iconCircle}>
        <Icon fontSize={18} />
      </div>
      <div className={styles.content}>
        <div className={styles.primaryRow}>
          <Text size={300} className={styles.name}>{doc.sprk_documentname}</Text>
          {doc.sprk_documentdescription && (
            <Text size={200} className={styles.description}>{doc.sprk_documentdescription}</Text>
          )}
        </div>
        <div className={styles.secondaryRow}>
          {statusLabel && <span className={styles.badge}>{statusLabel}</span>}
          {doc.modifiedon && (
            <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
              Modified: {formatDate(doc.modifiedon)}
            </Text>
          )}
          {doc.createdon && (
            <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
              Created: {formatDate(doc.createdon)}
            </Text>
          )}
        </div>
      </div>
    </div>
  );
});
DocumentRow.displayName = "DocumentRow";

// ---------------------------------------------------------------------------
// App
// ---------------------------------------------------------------------------

export const App: React.FC = () => {
  const styles = useStyles();
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const [documents, setDocuments] = React.useState<IDocument[]>([]);
  const [isLoading, setIsLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  const load = React.useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const docs = await fetchDocuments();
      setDocuments(docs);
    } catch (err) {
      console.error("[AllDocuments] fetch error:", err);
      setError("Failed to load documents.");
    } finally {
      setIsLoading(false);
    }
  }, []);

  React.useEffect(() => { load(); }, [load]);

  return (
    <FluentProvider theme={theme}>
      <div className={styles.root}>
        {/* Header */}
        <div className={styles.header}>
          <Text size={500} weight="semibold">Document List</Text>
          <Tooltip content="Refresh" relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<ArrowClockwiseRegular />}
              aria-label="Refresh"
              onClick={load}
              disabled={isLoading}
            />
          </Tooltip>
        </div>

        {/* Body */}
        {isLoading ? (
          <div className={styles.emptyState}>
            <Spinner label="Loading documents..." />
          </div>
        ) : error ? (
          <div className={styles.emptyState}>
            <Text>{error}</Text>
            <Button appearance="subtle" onClick={load}>Retry</Button>
          </div>
        ) : documents.length === 0 ? (
          <div className={styles.emptyState}>
            <DocumentRegular fontSize={32} />
            <Text>No documents found.</Text>
          </div>
        ) : (
          <div className={styles.list} role="list" aria-label={`${documents.length} documents`}>
            {documents.map((doc) => (
              <DocumentRow key={doc.sprk_documentid} doc={doc} />
            ))}
          </div>
        )}
      </div>
    </FluentProvider>
  );
};
