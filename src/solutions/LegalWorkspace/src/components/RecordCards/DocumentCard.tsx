/**
 * DocumentCard — card for sprk_document records in the Documents tab.
 *
 * Thin wrapper around RecordCardShell from @spaarke/ui-components.
 * Handles document-specific tools (preview, AI summary, open file,
 * find similar) and child dialogs.
 *
 * Double-click opens the record in a new tab via Xrm.Navigation.openForm.
 */

import * as React from "react";
import {
  tokens,
  Text,
  Button,
  Tooltip,
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
import { authenticatedFetch } from "../../services/authInit";
import { getDocumentOpenLinks } from "../../services/DocumentApiService";
import { getBffBaseUrl, getTenantId } from "../../config/runtimeConfig";
import { RecordCardShell, CardIcon, AiSummaryPopover, FindSimilarDialog } from "@spaarke/ui-components";
import type { ISummaryData } from "@spaarke/ui-components";
import { FilePreviewDialog } from "../FilePreview/FilePreviewDialog";

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
// Styles (content-specific only — layout handled by RecordCardShell)
// ---------------------------------------------------------------------------

const titleStyle: React.CSSProperties = {
  overflow: "hidden",
  textOverflow: "ellipsis",
  whiteSpace: "nowrap",
  color: tokens.colorNeutralForeground1,
  fontWeight: tokens.fontWeightSemibold,
  flexShrink: 0,
  maxWidth: "50%",
};

const fieldStyle: React.CSSProperties = {
  overflow: "hidden",
  textOverflow: "ellipsis",
  whiteSpace: "nowrap",
  color: tokens.colorNeutralForeground3,
  flexShrink: 1,
  minWidth: 0,
};

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
    const IconComponent = getFileTypeIcon(doc.sprk_filetype);

    // ----- Dialog state -----
    const [filePreviewOpen, setFilePreviewOpen] = React.useState(false);
    const [findSimilarUrl, setFindSimilarUrl] = React.useState<string | null>(null);

    // ----- Card double-click → open record -----
    const handleDoubleClick = React.useCallback(() => {
      navigateToEntity({
        action: "openRecord",
        entityName: "sprk_document",
        entityId: doc.sprk_documentid,
      });
    }, [doc.sprk_documentid]);

    // ----- Card Enter key → same as double-click -----
    const handleKeyDown = React.useCallback(
      (e: React.MouseEvent | React.KeyboardEvent) => {
        if ("key" in e && e.key === "Enter") {
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

    // ----- Tool: Preview -----
    const handlePreview = React.useCallback(() => {
      setFilePreviewOpen(true);
    }, []);

    // ----- Tool: Open File -----
    const handleOpenFile = React.useCallback(async () => {
      try {
        const links = await getDocumentOpenLinks(doc.sprk_documentid);
        if (links?.desktopUrl) {
          window.location.href = links.desktopUrl;
          return;
        }
        const contentUrl = `${getBffBaseUrl()}/api/documents/${encodeURIComponent(doc.sprk_documentid)}/content`;
        const response = await authenticatedFetch(contentUrl);
        if (response.ok) {
          const blob = await response.blob();
          const objectUrl = URL.createObjectURL(blob);
          const a = document.createElement("a");
          a.href = objectUrl;
          a.download = links?.fileName ?? doc.sprk_name ?? "document";
          document.body.appendChild(a);
          a.click();
          document.body.removeChild(a);
          URL.revokeObjectURL(objectUrl);
        }
      } catch (err) {
        console.error("[DocumentCard] Open file error:", err);
      }
    }, [doc.sprk_documentid]);

    // ----- Tool: Find Similar -----
    const handleFindSimilar = React.useCallback(async () => {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm =
          (window.top as any)?.Xrm ??
          (window.parent as any)?.Xrm ??
          (window as any)?.Xrm;
        const clientUrl =
          xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? "";
        const tenantId = getTenantId();
        const theme = getEffectiveDarkMode() ? "dark" : "light";
        const data = new URLSearchParams({
          documentId: doc.sprk_documentid,
          tenantId,
          theme,
        }).toString();
        setFindSimilarUrl(
          `${clientUrl}/WebResources/sprk_documentrelationshipviewer?data=${encodeURIComponent(data)}`
        );
      } catch (err) {
        console.error("[DocumentCard] Find Similar error:", err);
      }
    }, [doc.sprk_documentid]);

    // ----- Tool: AI Summary -----
    const handleFetchSummary = React.useCallback(async (): Promise<ISummaryData> => {
      const tldr = doc.sprk_filetldr ?? null;
      let summary = doc.sprk_filesummary ?? null;
      if (summary) {
        const trimmed = summary.trim();
        if (trimmed.startsWith("{") || trimmed.startsWith("[") || trimmed.startsWith("```")) {
          summary = null;
        }
      }
      return { summary, tldr };
    }, [doc.sprk_filetldr, doc.sprk_filesummary]);

    const ariaLabel = [
      doc.sprk_documentname,
      doc.sprk_documentdescription,
      doc.statuscodeName ? `Status: ${doc.statuscodeName}` : "",
    ]
      .filter(Boolean)
      .join(", ");

    return (
      <>
        <RecordCardShell
          icon={
            <CardIcon>
              <IconComponent fontSize={20} aria-label={doc.sprk_filetype ?? "Document"} />
            </CardIcon>
          }
          primaryContent={
            <>
              <Text as="span" size={400} style={titleStyle}>
                {doc.sprk_documentname}
              </Text>
              {doc.sprk_documentdescription && (
                <Text as="span" size={300} style={fieldStyle}>
                  {doc.sprk_documentdescription}
                </Text>
              )}
            </>
          }
          secondaryContent={
            <>
              {doc.statuscodeName && <StatusBadge label={doc.statuscodeName} />}
              {doc.createdon && (
                <Text as="span" size={200} style={fieldStyle}>
                  Created: {formatShortDate(doc.createdon)}
                </Text>
              )}
              {doc.modifiedon && (
                <Text as="span" size={200} style={fieldStyle}>
                  Modified: {formatShortDate(doc.modifiedon)}
                </Text>
              )}
            </>
          }
          tools={
            <>
              <Tooltip content="Preview" relationship="label">
                <Button
                  appearance="subtle"
                  size="medium"
                  icon={<EyeRegular aria-hidden="true" />}
                  aria-label="Preview document"
                  onClick={handlePreview}
                />
              </Tooltip>
              <AiSummaryPopover
                onFetchSummary={handleFetchSummary}
                trigger={
                  <Tooltip content="AI Summary" relationship="label">
                    <Button
                      appearance="subtle"
                      size="medium"
                      icon={<SparkleRegular aria-hidden="true" />}
                      aria-label="AI Summary"
                    />
                  </Tooltip>
                }
              />
              <Tooltip content="Open file" relationship="label">
                <Button
                  appearance="subtle"
                  size="medium"
                  icon={<FolderOpenRegular aria-hidden="true" />}
                  aria-label="Open file"
                  onClick={handleOpenFile}
                />
              </Tooltip>
              <Tooltip content="Find Similar" relationship="label">
                <Button
                  appearance="subtle"
                  size="medium"
                  icon={<DocumentSearchRegular aria-hidden="true" />}
                  aria-label="Find Similar"
                  onClick={handleFindSimilar}
                />
              </Tooltip>
            </>
          }
          onDoubleClick={handleDoubleClick}
          onClick={handleKeyDown}
          ariaLabel={ariaLabel}
        />

        {/* Child dialogs */}
        <FindSimilarDialog
          open={!!findSimilarUrl}
          onClose={() => setFindSimilarUrl(null)}
          url={findSimilarUrl}
        />
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
