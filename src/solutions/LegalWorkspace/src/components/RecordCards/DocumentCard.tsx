/**
 * DocumentCard — 2-row card for sprk_document records in the Documents tab.
 *
 * Follows the same visual pattern as RecordCard but with:
 *   - Dynamic file-type icon (based on sprk_filetype)
 *   - Standalone Preview button (eye icon)
 *   - Custom overflow menu: Open File in Web, Open File in Desktop,
 *     Open Record, Find Similar, Summary
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
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Spinner,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  shorthands,
} from "@fluentui/react-components";
import {
  MoreVerticalRegular,
  EyeRegular,
  GlobeRegular,
  DesktopRegular,
  OpenRegular,
  DocumentSearchRegular,
  SparkleRegular,
} from "@fluentui/react-icons";
import type { IDocument } from "../../types/entities";
import { getFileTypeIcon } from "../../utils/fileIconMap";
import { navigateToEntity } from "../../utils/navigation";
import { getEffectiveDarkMode } from "../../providers/ThemeProvider";
import {
  getDocumentPreviewUrl,
  getDocumentOpenLinks,
} from "../../services/DocumentApiService";
import { getTenantId } from "../../services/bffAuthProvider";

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
  // Preview popover
  previewSurface: {
    width: "600px",
    height: "400px",
    padding: "0px",
    ...shorthands.overflow("hidden"),
  },
  previewFrame: {
    width: "100%",
    height: "100%",
    borderWidth: "0px",
  },
  previewLoading: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    width: "100%",
    height: "100%",
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

    // ----- Preview state -----
    const [previewUrl, setPreviewUrl] = React.useState<string | null>(null);
    const [previewLoading, setPreviewLoading] = React.useState(false);

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

    // ----- Preview action -----
    const handlePreviewClick = React.useCallback(
      async (e: React.MouseEvent) => {
        e.stopPropagation();
        if (previewUrl) return; // Already loaded
        setPreviewLoading(true);
        const url = await getDocumentPreviewUrl(doc.sprk_documentid);
        setPreviewUrl(url);
        setPreviewLoading(false);
      },
      [doc.sprk_documentid, previewUrl]
    );

    // ----- Open File in Web -----
    const handleOpenInWeb = React.useCallback(async () => {
      const links = await getDocumentOpenLinks(doc.sprk_documentid);
      if (links?.webUrl) {
        window.open(links.webUrl, "_blank", "noopener,noreferrer");
      } else {
        console.warn("[DocumentCard] No web URL available for", doc.sprk_documentid);
      }
    }, [doc.sprk_documentid]);

    // ----- Open File in Desktop -----
    const handleOpenInDesktop = React.useCallback(async () => {
      const links = await getDocumentOpenLinks(doc.sprk_documentid);
      if (links?.desktopUrl) {
        window.location.href = links.desktopUrl;
      } else {
        console.warn("[DocumentCard] No desktop URL available for", doc.sprk_documentid);
      }
    }, [doc.sprk_documentid]);

    // ----- Open Record (new tab) -----
    const handleOpenRecord = React.useCallback(() => {
      navigateToEntity({
        action: "openRecord",
        entityName: "sprk_document",
        entityId: doc.sprk_documentid,
      });
    }, [doc.sprk_documentid]);

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

            {/* Actions: Preview button + overflow menu */}
            {/* eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions */}
            <div className={styles.actionsColumn} onClick={handleActionsClick}>
              {/* Preview button (standalone) */}
              <Popover
                withArrow
                onOpenChange={(_, data) => {
                  if (!data.open) setPreviewUrl(null);
                }}
              >
                <PopoverTrigger disableButtonEnhancement>
                  <Tooltip content="Preview" relationship="label">
                    <Button
                      appearance="subtle"
                      size="medium"
                      icon={<EyeRegular aria-hidden="true" />}
                      aria-label="Preview document"
                      onClick={handlePreviewClick}
                    />
                  </Tooltip>
                </PopoverTrigger>
                <PopoverSurface className={styles.previewSurface}>
                  {previewLoading ? (
                    <div className={styles.previewLoading}>
                      <Spinner size="small" label="Loading preview..." />
                    </div>
                  ) : previewUrl ? (
                    <iframe
                      src={previewUrl}
                      title="Document preview"
                      className={styles.previewFrame}
                    />
                  ) : (
                    <div className={styles.previewLoading}>
                      <Text size={300}>Preview not available.</Text>
                    </div>
                  )}
                </PopoverSurface>
              </Popover>

              {/* Overflow menu */}
              <Menu>
                <MenuTrigger disableButtonEnhancement>
                  <Tooltip content="More actions" relationship="label">
                    <Button
                      appearance="subtle"
                      size="medium"
                      icon={<MoreVerticalRegular aria-hidden="true" />}
                      aria-label="More actions"
                    />
                  </Tooltip>
                </MenuTrigger>
                <MenuPopover>
                  <MenuList>
                    <MenuItem
                      icon={<GlobeRegular />}
                      onClick={handleOpenInWeb}
                    >
                      Open File in Web
                    </MenuItem>
                    <MenuItem
                      icon={<DesktopRegular />}
                      onClick={handleOpenInDesktop}
                    >
                      Open File in Desktop
                    </MenuItem>
                    <MenuItem
                      icon={<OpenRegular />}
                      onClick={handleOpenRecord}
                    >
                      Open Record
                    </MenuItem>
                    <MenuItem
                      icon={<DocumentSearchRegular />}
                      onClick={handleFindSimilar}
                    >
                      Find Similar
                    </MenuItem>
                    <MenuItem
                      icon={<SparkleRegular />}
                      onClick={handleSummaryClick}
                    >
                      Summary
                    </MenuItem>
                  </MenuList>
                </MenuPopover>
              </Menu>

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
      </>
    );
  }
);

DocumentCard.displayName = "DocumentCard";
