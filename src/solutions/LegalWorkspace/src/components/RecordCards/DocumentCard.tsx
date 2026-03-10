/**
 * DocumentCard — 2-row card for sprk_document records in the Documents tab.
 *
 * Follows the same visual pattern as RecordCard but with:
 *   - Dynamic file-type icon (based on sprk_filetype)
 *   - Preview button (eye icon) opens FilePreviewDialog
 *   - Overflow menu: Find Similar, Summary
 *
 * Card double-click opens the record in a new tab via Xrm.Navigation.openForm.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Tooltip,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  shorthands,
} from "@fluentui/react-components";
import {
  EyeRegular,
  DocumentSearchRegular,
  SparkleRegular,
  FolderOpenRegular,
} from "@fluentui/react-icons";
import type { IDocument } from "../../types/entities";
import { getFileTypeIcon } from "../../utils/fileIconMap";
import { navigateToEntity } from "../../utils/navigation";
import { getEffectiveDarkMode } from "../../providers/ThemeProvider";
import { getTenantId } from "../../services/authInit";
import { getDocumentOpenLinks } from "../../services/DocumentApiService";
import { FilePreviewDialog } from "../FilePreview/FilePreviewDialog";

// ---------------------------------------------------------------------------
// Styles (matching RecordCard pattern)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    display: "flex",
    flexDirection: "column",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow2,
    marginBottom: tokens.spacingVerticalS,
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
    ":focus-visible": {
      outlineStyle: "solid",
      outlineWidth: "2px",
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: "-2px",
    },
  },
  mainRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalL,
  },
  typeIconCircle: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flexShrink: 0,
    width: "40px",
    height: "40px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
    marginTop: "2px",
  },
  contentColumn: {
    flex: "1 1 0",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  primaryRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "nowrap",
    minWidth: 0,
  },
  title: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    flexShrink: 0,
    maxWidth: "50%",
  },
  fieldText: {
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
  actionsColumn: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXXS,
    flexShrink: 0,
    marginLeft: tokens.spacingHorizontalL,
  },
  // Find Similar dialog
  findSimilarSurface: {
    padding: "0px",
    width: "85vw",
    maxWidth: "85vw",
    height: "80vh",
    maxHeight: "80vh",
    display: "flex",
    flexDirection: "column",
    ...shorthands.overflow("hidden"),
    ...shorthands.borderRadius(tokens.borderRadiusXLarge),
  },
  findSimilarBody: {
    padding: "0px",
    flex: 1,
    minHeight: 0,
    position: "relative" as const,
  },
  findSimilarFrame: {
    position: "absolute" as const,
    top: 0,
    left: 0,
    width: "100%",
    height: "100%",
    borderWidth: "0px",
  },
  // Summary dialog
  summarySurface: {
    maxWidth: "480px",
    maxHeight: "400px",
  },
});

// ---------------------------------------------------------------------------
// Badge sub-component
// ---------------------------------------------------------------------------

const StatusBadge: React.FC<{ label: string }> = ({ label }) => (
  <span
    role="img"
    aria-label={`Status: ${label}`}
    style={{
      display: "inline-flex",
      alignItems: "center",
      justifyContent: "center",
      borderRadius: tokens.borderRadiusSmall,
      paddingTop: "1px",
      paddingBottom: "1px",
      paddingLeft: tokens.spacingHorizontalXS,
      paddingRight: tokens.spacingHorizontalXS,
      fontSize: tokens.fontSizeBase100,
      fontWeight: tokens.fontWeightSemibold,
      lineHeight: tokens.lineHeightBase100,
      whiteSpace: "nowrap",
      backgroundColor: tokens.colorBrandBackground2,
      color: tokens.colorBrandForeground1,
      flexShrink: 0,
    }}
  >
    {label}
  </span>
);

// ---------------------------------------------------------------------------
// Date formatter
// ---------------------------------------------------------------------------

function formatShortDate(isoDate: string | undefined): string {
  if (!isoDate) return "";
  try {
    const d = new Date(isoDate);
    return d.toLocaleDateString(undefined, {
      month: "short",
      day: "numeric",
      year: "numeric",
    });
  } catch {
    return "";
  }
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IDocumentCardProps {
  document: IDocument;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DocumentCard: React.FC<IDocumentCardProps> = React.memo(
  ({ document: doc }) => {
    const styles = useStyles();

    // Resolve dynamic file icon
    const IconComponent = getFileTypeIcon(doc.sprk_filetype);

    // ----- File Preview Dialog state -----
    const [filePreviewOpen, setFilePreviewOpen] = React.useState(false);

    // ----- Find Similar state -----
    const [findSimilarUrl, setFindSimilarUrl] = React.useState<string | null>(
      null
    );

    // ----- Summary dialog state -----
    const [summaryOpen, setSummaryOpen] = React.useState(false);

    // ----- Card double-click → open record in new tab -----
    const handleCardDoubleClick = React.useCallback(() => {
      navigateToEntity({
        action: "openRecord",
        entityName: "sprk_document",
        entityId: doc.sprk_documentid,
      });
    }, [doc.sprk_documentid]);

    const handleCardKeyDown = React.useCallback(
      (e: React.KeyboardEvent) => {
        if (e.key === "Enter") {
          e.preventDefault();
          navigateToEntity({
            action: "openRecord",
            entityName: "sprk_document",
            entityId: doc.sprk_documentid,
          });
        }
      },
      [doc.sprk_documentid]
    );

    // ----- Preview action (opens FilePreviewDialog) -----
    const handlePreviewClick = React.useCallback(
      (e: React.MouseEvent) => {
        e.stopPropagation();
        setFilePreviewOpen(true);
      },
      []
    );

    // ----- Open File — prefer desktop app, fall back to web -----
    const handleOpenFileClick = React.useCallback(
      async (e: React.MouseEvent) => {
        e.stopPropagation();
        try {
          const links = await getDocumentOpenLinks(doc.sprk_documentid);
          if (links) {
            if (links.desktopUrl) {
              window.location.href = links.desktopUrl;
              return;
            }
            if (links.webUrl) {
              window.open(links.webUrl, "_blank", "noopener,noreferrer");
              return;
            }
          }
        } catch (err) {
          console.error("[DocumentCard] Open file error:", err);
        }
      },
      [doc.sprk_documentid]
    );

    // ----- Find Similar -----
    const handleFindSimilar = React.useCallback(async () => {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm =
          (window.top as any)?.Xrm ??
          (window.parent as any)?.Xrm ??
          (window as any)?.Xrm;
        const clientUrl =
          xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? "";
        const tenantId = await getTenantId();

        const theme = getEffectiveDarkMode() ? "dark" : "light";
        const data = new URLSearchParams({
          documentId: doc.sprk_documentid,
          tenantId,
          theme,
        }).toString();

        const url = `${clientUrl}/WebResources/sprk_documentrelationshipviewer?data=${encodeURIComponent(data)}`;
        setFindSimilarUrl(url);
      } catch (err) {
        console.error("[DocumentCard] Find Similar error:", err);
      }
    }, [doc.sprk_documentid]);

    // ----- Stop propagation on menu/action clicks -----
    const handleActionsClick = React.useCallback((e: React.MouseEvent) => {
      e.stopPropagation();
    }, []);

    // ----- Summary action -----
    const handleSummaryClick = React.useCallback(() => {
      setSummaryOpen(true);
    }, []);

    // ----- Summary text -----
    // Prefer the human-readable TL;DR; fall back to file summary only if
    // it looks like plain text (not structured JSON / entity extraction).
    const summaryText = React.useMemo(() => {
      if (doc.sprk_filetldr) return doc.sprk_filetldr;
      if (doc.sprk_filesummary) {
        const trimmed = doc.sprk_filesummary.trim();
        // Skip structured data (JSON blobs, markdown code fences)
        if (trimmed.startsWith("{") || trimmed.startsWith("[") || trimmed.startsWith("```")) {
          return "No summary available.";
        }
        return doc.sprk_filesummary;
      }
      return "No summary available.";
    }, [doc.sprk_filetldr, doc.sprk_filesummary]);

    // Build card aria label
    const cardAriaLabel = [
      doc.sprk_documentname,
      doc.sprk_documentdescription,
      doc.statuscodeName ? `Status: ${doc.statuscodeName}` : "",
    ]
      .filter(Boolean)
      .join(", ");

    return (
      <>
        <div
          className={styles.card}
          role="listitem"
          tabIndex={0}
          aria-label={cardAriaLabel}
          onDoubleClick={handleCardDoubleClick}
          onKeyDown={handleCardKeyDown}
        >
          <div className={styles.mainRow}>
            {/* File type icon in 40px circle */}
            <div
              className={styles.typeIconCircle}
              aria-label={doc.sprk_filetype ?? "Document"}
              role="img"
            >
              <IconComponent fontSize={20} />
            </div>

            {/* Content: 2 rows */}
            <div className={styles.contentColumn}>
              {/* Row 1: Document name + description */}
              <div className={styles.primaryRow}>
                <Text as="span" size={400} className={styles.title}>
                  {doc.sprk_documentname}
                </Text>
                {doc.sprk_documentdescription && (
                  <Text as="span" size={300} className={styles.fieldText}>
                    {doc.sprk_documentdescription}
                  </Text>
                )}
              </div>

              {/* Row 2: Status badge + dates */}
              <div className={styles.secondaryRow}>
                {doc.statuscodeName && (
                  <StatusBadge label={doc.statuscodeName} />
                )}
                {doc.createdon && (
                  <Text as="span" size={200} className={styles.fieldText}>
                    Created: {formatShortDate(doc.createdon)}
                  </Text>
                )}
                {doc.modifiedon && (
                  <Text as="span" size={200} className={styles.fieldText}>
                    Modified: {formatShortDate(doc.modifiedon)}
                  </Text>
                )}
              </div>
            </div>

            {/* Actions: icon-only buttons — Preview, Summary, Open File, Find Similar */}
            {/* eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions */}
            <div className={styles.actionsColumn} onClick={handleActionsClick}>
              <Tooltip content="Preview" relationship="label">
                <Button
                  appearance="subtle"
                  size="medium"
                  icon={<EyeRegular aria-hidden="true" />}
                  aria-label="Preview document"
                  onClick={handlePreviewClick}
                />
              </Tooltip>
              <Tooltip content="Summary" relationship="label">
                <Button
                  appearance="subtle"
                  size="medium"
                  icon={<SparkleRegular aria-hidden="true" />}
                  aria-label="Summary"
                  onClick={(e: React.MouseEvent) => {
                    e.stopPropagation();
                    handleSummaryClick();
                  }}
                />
              </Tooltip>
              <Tooltip content="Open file" relationship="label">
                <Button
                  appearance="subtle"
                  size="medium"
                  icon={<FolderOpenRegular aria-hidden="true" />}
                  aria-label="Open file"
                  onClick={handleOpenFileClick}
                />
              </Tooltip>
              <Tooltip content="Find Similar" relationship="label">
                <Button
                  appearance="subtle"
                  size="medium"
                  icon={<DocumentSearchRegular aria-hidden="true" />}
                  aria-label="Find Similar"
                  onClick={(e: React.MouseEvent) => {
                    e.stopPropagation();
                    handleFindSimilar();
                  }}
                />
              </Tooltip>
            </div>
          </div>
        </div>

        {/* Find Similar — iframe dialog */}
        <Dialog
          open={!!findSimilarUrl}
          onOpenChange={(_, data) => {
            if (!data.open) setFindSimilarUrl(null);
          }}
        >
          <DialogSurface className={styles.findSimilarSurface}>
            <DialogBody className={styles.findSimilarBody}>
              {findSimilarUrl && (
                <iframe
                  src={findSimilarUrl}
                  title="Document Relationships"
                  className={styles.findSimilarFrame}
                />
              )}
            </DialogBody>
          </DialogSurface>
        </Dialog>

        {/* Summary dialog */}
        <Dialog
          open={summaryOpen}
          onOpenChange={(_, data) => {
            if (!data.open) setSummaryOpen(false);
          }}
        >
          <DialogSurface className={styles.summarySurface}>
            <DialogBody>
              <DialogTitle>Summary</DialogTitle>
              <DialogContent>
                <Text size={200}>{summaryText}</Text>
              </DialogContent>
            </DialogBody>
          </DialogSurface>
        </Dialog>

        {/* File Preview Dialog */}
        <FilePreviewDialog
          open={filePreviewOpen}
          documentId={doc.sprk_documentid}
          documentName={doc.sprk_documentname}
          fileSummary={doc.sprk_filetldr ?? doc.sprk_filesummary}
          onClose={() => setFilePreviewOpen(false)}
        />
      </>
    );
  }
);

DocumentCard.displayName = "DocumentCard";
