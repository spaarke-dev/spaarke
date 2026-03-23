import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
  Divider,
  Badge,
  Input,
  Field,
  Select,
  Tooltip,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  DialogTrigger,
  ProgressBar,
} from "@fluentui/react-components";
import {
  Dismiss24Regular,
  DocumentRegular,
  CalendarRegular,
  PersonRegular,
  StorageRegular,
  LinkRegular,
  HistoryRegular,
  ImageRegular,
  ArrowClockwise20Regular,
  Copy20Regular,
  CheckmarkCircle20Regular,
} from "@fluentui/react-icons";
import { SidePaneShell } from "@spaarke/ui-components";
import { speApiClient } from "../../services/speApiClient";
import type {
  DriveItem,
  DriveItemVersion,
  Thumbnail,
  SharingLink,
  SharingLinkType,
  SharingLinkScope,
} from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: makeStyles + Fluent design tokens; dark mode automatic)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  // ── Overlay backdrop ───────────────────────────────────────────────────────

  backdrop: {
    position: "fixed",
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    backgroundColor: tokens.colorBackgroundOverlay,
    zIndex: 100,
    display: "flex",
    justifyContent: "flex-end",
  },

  panel: {
    width: "420px",
    maxWidth: "90vw",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    display: "flex",
    flexDirection: "column",
    boxShadow: tokens.shadow64,
    overflowY: "hidden",
  },

  // ── Header ─────────────────────────────────────────────────────────────────

  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  headerTitle: {
    flex: "1 1 auto",
    minWidth: 0,
    overflow: "hidden",
  },

  fileName: {
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    display: "block",
  },

  fileMimeType: {
    color: tokens.colorNeutralForeground3,
    display: "block",
    marginTop: tokens.spacingVerticalXXS,
  },

  // ── Footer ─────────────────────────────────────────────────────────────────

  footer: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-end",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  // ── Sections ───────────────────────────────────────────────────────────────

  section: {
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
  },

  sectionHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    marginBottom: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground2,
  },

  sectionTitle: {
    color: tokens.colorNeutralForeground2,
    textTransform: "uppercase",
    letterSpacing: "0.05em",
  },

  // ── Metadata rows ──────────────────────────────────────────────────────────

  metaGrid: {
    display: "grid",
    gridTemplateColumns: "120px 1fr",
    rowGap: tokens.spacingVerticalS,
    columnGap: tokens.spacingHorizontalM,
  },

  metaLabel: {
    color: tokens.colorNeutralForeground3,
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },

  metaValue: {
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },

  // ── Version history ────────────────────────────────────────────────────────

  versionList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },

  versionRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },

  versionId: {
    color: tokens.colorNeutralForeground2,
    flexShrink: 0,
    fontFamily: tokens.fontFamilyMonospace,
  },

  versionMeta: {
    flex: "1 1 auto",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
  },

  versionSize: {
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },

  // ── Sharing links ──────────────────────────────────────────────────────────

  sharingLinkRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    marginBottom: tokens.spacingVerticalXS,
  },

  sharingLinkUrl: {
    flex: "1 1 auto",
    minWidth: 0,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    color: tokens.colorBrandForeground1,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
  },

  sharingFormRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-end",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
    marginBottom: tokens.spacingVerticalS,
  },

  sharingFormField: {
    flex: "1 1 120px",
  },

  // ── Thumbnail ──────────────────────────────────────────────────────────────

  thumbnailArea: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalM,
    flexWrap: "wrap",
  },

  thumbnailImg: {
    width: "80px",
    height: "80px",
    objectFit: "cover",
    borderRadius: tokens.borderRadiusMedium,
    ...shorthands.border("1px", "solid", tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground3,
  },

  thumbnailPlaceholder: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    flex: "1 1 auto",
  },

  // ── Loading / empty states ─────────────────────────────────────────────────

  centered: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalXL,
    color: tokens.colorNeutralForeground3,
  },

  copiedBadge: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorStatusSuccessForeground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/** Props for the FileDetailPanel component. */
export interface FileDetailPanelProps {
  /** The selected DriveItem to show details for. */
  item: DriveItem;
  /** The SPE container ID this item belongs to. */
  containerId: string;
  /** The container type config ID — required for all API calls. */
  configId: string;
  /** Called when the user closes the panel. */
  onClose: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Utilities
// ─────────────────────────────────────────────────────────────────────────────

/** Format byte count as human-readable string. */
function formatFileSize(bytes: number | undefined): string {
  if (bytes === undefined) return "—";
  if (bytes === 0) return "0 B";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024)
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

/** Format ISO date string to a short locale date+time. */
function formatDate(iso: string | undefined): string {
  if (!iso) return "—";
  try {
    return new Intl.DateTimeFormat(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    }).format(new Date(iso));
  } catch {
    return iso;
  }
}

/** Returns true if the item is a file (not a folder). */
function isFile(item: DriveItem): boolean {
  return !item.folder;
}

// ─────────────────────────────────────────────────────────────────────────────
// FileDetailPanel component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * FileDetailPanel — slide-in side panel showing comprehensive file details.
 *
 * Displays:
 * - File metadata (name, size, dates, MIME type, created-by, modified-by)
 * - Version history list (files only) with dates and authors
 * - Sharing link management — create new sharing links, copy to clipboard
 * - Thumbnail preview (files only, when available from Graph API)
 *
 * ADR compliance:
 * - ADR-021: All UI uses Fluent v9 makeStyles + design tokens; dark mode automatic
 * - ADR-012: SidePaneShell reused from @spaarke/ui-components shared library
 * - ADR-006: Code Page (React 18 + bundled, not PCF)
 */
export const FileDetailPanel: React.FC<FileDetailPanelProps> = ({
  item,
  containerId,
  configId,
  onClose,
}) => {
  const styles = useStyles();

  // ── Versions state ─────────────────────────────────────────────────────────

  const [versions, setVersions] = React.useState<DriveItemVersion[]>([]);
  const [versionsLoading, setVersionsLoading] = React.useState(false);
  const [versionsError, setVersionsError] = React.useState<string | null>(null);

  // ── Thumbnails state ───────────────────────────────────────────────────────

  const [thumbnails, setThumbnails] = React.useState<Thumbnail[]>([]);
  const [thumbnailsLoading, setThumbnailsLoading] = React.useState(false);

  // ── Sharing links state ────────────────────────────────────────────────────

  const [generatedLinks, setGeneratedLinks] = React.useState<SharingLink[]>([]);
  const [linkType, setLinkType] = React.useState<SharingLinkType>("view");
  const [linkScope, setLinkScope] = React.useState<SharingLinkScope>("organization");
  const [linkExpiry, setLinkExpiry] = React.useState<string>("");
  const [creatingLink, setCreatingLink] = React.useState(false);
  const [linkError, setLinkError] = React.useState<string | null>(null);

  // Tracks which link URLs have been recently copied (for visual feedback)
  const [copiedUrl, setCopiedUrl] = React.useState<string | null>(null);

  // ── Sharing link dialog state ──────────────────────────────────────────────

  const [linkDialogOpen, setLinkDialogOpen] = React.useState(false);

  // ── Load versions (files only) ─────────────────────────────────────────────

  const loadVersions = React.useCallback(async () => {
    if (!isFile(item)) return;
    setVersionsLoading(true);
    setVersionsError(null);
    try {
      const result = await speApiClient.metadata.listVersions(
        containerId,
        item.id,
        configId
      );
      setVersions(result);
    } catch (err) {
      const message =
        err instanceof Error ? err.message : "Failed to load version history.";
      setVersionsError(message);
    } finally {
      setVersionsLoading(false);
    }
  }, [containerId, item.id, configId, item]);

  // ── Load thumbnails (files only) ───────────────────────────────────────────

  const loadThumbnails = React.useCallback(async () => {
    if (!isFile(item)) return;
    setThumbnailsLoading(true);
    try {
      const result = await speApiClient.metadata.getThumbnails(
        containerId,
        item.id,
        configId
      );
      setThumbnails(result);
    } catch {
      // Thumbnails are optional — silently ignore errors
      setThumbnails([]);
    } finally {
      setThumbnailsLoading(false);
    }
  }, [containerId, item.id, configId, item]);

  // Load on mount / item change
  React.useEffect(() => {
    setVersions([]);
    setThumbnails([]);
    setGeneratedLinks([]);
    setVersionsError(null);
    setLinkError(null);
    void loadVersions();
    void loadThumbnails();
  }, [item.id, loadVersions, loadThumbnails]);

  // ── Create sharing link ────────────────────────────────────────────────────

  const handleCreateLink = React.useCallback(async () => {
    setCreatingLink(true);
    setLinkError(null);
    try {
      const link = await speApiClient.metadata.createSharingLink(
        containerId,
        item.id,
        configId,
        {
          type: linkType,
          scope: linkScope,
          expirationDateTime: linkExpiry || undefined,
        }
      );
      setGeneratedLinks((prev) => [link, ...prev]);
      setLinkDialogOpen(false);
      setLinkExpiry("");
    } catch (err) {
      const message =
        err instanceof Error ? err.message : "Failed to create sharing link.";
      setLinkError(message);
    } finally {
      setCreatingLink(false);
    }
  }, [containerId, item.id, configId, linkType, linkScope, linkExpiry]);

  // ── Copy link to clipboard ─────────────────────────────────────────────────

  const handleCopyLink = React.useCallback(async (url: string) => {
    try {
      await navigator.clipboard.writeText(url);
      setCopiedUrl(url);
      // Clear visual feedback after 2 seconds
      setTimeout(() => setCopiedUrl((prev) => (prev === url ? null : prev)), 2000);
    } catch {
      // Fallback: select text in a temp element
      const el = document.createElement("textarea");
      el.value = url;
      document.body.appendChild(el);
      el.select();
      document.execCommand("copy");
      document.body.removeChild(el);
      setCopiedUrl(url);
      setTimeout(() => setCopiedUrl((prev) => (prev === url ? null : prev)), 2000);
    }
  }, []);

  // ── Derived values ─────────────────────────────────────────────────────────

  const itemIsFile = isFile(item);
  const mimeType = item.file?.mimeType ?? "Folder";
  const firstThumbnail = thumbnails[0];
  const mediumThumbnailUrl =
    firstThumbnail?.medium?.url ?? firstThumbnail?.large?.url ?? firstThumbnail?.small?.url;

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  /** Panel header — file name and close button. */
  const header = (
    <div className={styles.header}>
      <div className={styles.headerTitle}>
        <Text size={400} weight="semibold" className={styles.fileName}>
          {item.name}
        </Text>
        <Text size={200} className={styles.fileMimeType}>
          {itemIsFile ? mimeType : "Folder"}
        </Text>
      </div>
      <Tooltip content="Close panel" relationship="label">
        <Button
          appearance="subtle"
          icon={<Dismiss24Regular />}
          onClick={onClose}
          aria-label="Close file detail panel"
        />
      </Tooltip>
    </div>
  );

  /** Panel footer — refresh action. */
  const footer = (
    <div className={styles.footer}>
      <Button
        appearance="subtle"
        size="small"
        icon={<ArrowClockwise20Regular />}
        onClick={() => {
          void loadVersions();
          void loadThumbnails();
        }}
        disabled={versionsLoading || thumbnailsLoading}
      >
        Refresh
      </Button>
    </div>
  );

  return (
    // Backdrop overlay — click outside to close
    <div
      className={styles.backdrop}
      onClick={(e) => {
        // Close when clicking the backdrop, not the panel itself
        if (e.target === e.currentTarget) onClose();
      }}
      role="dialog"
      aria-label="File detail panel"
      aria-modal="true"
    >
      <div className={styles.panel} onClick={(e) => e.stopPropagation()}>
        {/* ADR-012: SidePaneShell from @spaarke/ui-components for layout structure */}
        <SidePaneShell header={header} footer={footer}>

          {/* ── Section 1: Metadata ── */}
          <div className={styles.section}>
            <div className={styles.sectionHeader}>
              <DocumentRegular />
              <Text size={200} weight="semibold" className={styles.sectionTitle}>
                File Details
              </Text>
            </div>
            <div className={styles.metaGrid}>
              {/* Name */}
              <Text size={200} className={styles.metaLabel}>
                <DocumentRegular style={{ fontSize: "14px" }} /> Name
              </Text>
              <Tooltip content={item.name} relationship="label">
                <Text size={200} className={styles.metaValue}>
                  {item.name}
                </Text>
              </Tooltip>

              {/* Type */}
              <Text size={200} className={styles.metaLabel}>
                Type
              </Text>
              <Text size={200} className={styles.metaValue}>
                {itemIsFile ? (mimeType || "File") : "Folder"}
              </Text>

              {/* Size */}
              <Text size={200} className={styles.metaLabel}>
                <StorageRegular style={{ fontSize: "14px" }} /> Size
              </Text>
              <Text size={200} className={styles.metaValue}>
                {itemIsFile ? formatFileSize(item.size) : "—"}
              </Text>

              {/* Created */}
              <Text size={200} className={styles.metaLabel}>
                <CalendarRegular style={{ fontSize: "14px" }} /> Created
              </Text>
              <Text size={200} className={styles.metaValue}>
                {formatDate(item.createdDateTime)}
              </Text>

              {/* Created by */}
              <Text size={200} className={styles.metaLabel}>
                <PersonRegular style={{ fontSize: "14px" }} /> Created by
              </Text>
              <Text size={200} className={styles.metaValue}>
                {item.createdBy?.user?.displayName ?? "—"}
              </Text>

              {/* Modified */}
              <Text size={200} className={styles.metaLabel}>
                <CalendarRegular style={{ fontSize: "14px" }} /> Modified
              </Text>
              <Text size={200} className={styles.metaValue}>
                {formatDate(item.lastModifiedDateTime)}
              </Text>

              {/* Modified by */}
              <Text size={200} className={styles.metaLabel}>
                <PersonRegular style={{ fontSize: "14px" }} /> Modified by
              </Text>
              <Text size={200} className={styles.metaValue}>
                {item.lastModifiedBy?.user?.displayName ?? "—"}
              </Text>

              {/* Web URL */}
              {item.webUrl && (
                <>
                  <Text size={200} className={styles.metaLabel}>
                    Web URL
                  </Text>
                  <Tooltip content={item.webUrl} relationship="label">
                    <Text
                      size={200}
                      style={{
                        color: tokens.colorBrandForeground1,
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                        cursor: "pointer",
                      }}
                      onClick={() => window.open(item.webUrl, "_blank", "noopener,noreferrer")}
                    >
                      Open in browser
                    </Text>
                  </Tooltip>
                </>
              )}
            </div>
          </div>

          <Divider />

          {/* ── Section 2: Thumbnail Preview (files only) ── */}
          {itemIsFile && (
            <>
              <div className={styles.section}>
                <div className={styles.sectionHeader}>
                  <ImageRegular />
                  <Text size={200} weight="semibold" className={styles.sectionTitle}>
                    Thumbnail Preview
                  </Text>
                </div>

                {thumbnailsLoading && (
                  <div className={styles.centered}>
                    <Spinner size="tiny" label="Loading thumbnail..." />
                  </div>
                )}

                {!thumbnailsLoading && mediumThumbnailUrl && (
                  <div className={styles.thumbnailArea}>
                    <img
                      src={mediumThumbnailUrl}
                      alt={`Thumbnail for ${item.name}`}
                      className={styles.thumbnailImg}
                      style={{ width: "auto", height: "120px", maxWidth: "100%" }}
                      onError={(e) => {
                        // Hide broken image
                        (e.target as HTMLImageElement).style.display = "none";
                      }}
                    />
                  </div>
                )}

                {!thumbnailsLoading && !mediumThumbnailUrl && (
                  <div className={styles.thumbnailPlaceholder}>
                    <ImageRegular style={{ fontSize: "32px" }} />
                    <Text size={200}>
                      No thumbnail available for this file type.
                    </Text>
                  </div>
                )}
              </div>

              <Divider />
            </>
          )}

          {/* ── Section 3: Version History (files only) ── */}
          {itemIsFile && (
            <>
              <div className={styles.section}>
                <div className={styles.sectionHeader}>
                  <HistoryRegular />
                  <Text size={200} weight="semibold" className={styles.sectionTitle}>
                    Version History
                  </Text>
                  {versions.length > 0 && (
                    <Badge appearance="tint" color="informative" size="small">
                      {versions.length}
                    </Badge>
                  )}
                </div>

                {versionsLoading && (
                  <div className={styles.centered}>
                    <Spinner size="tiny" label="Loading versions..." />
                  </div>
                )}

                {versionsError && (
                  <MessageBar intent="error">
                    <MessageBarBody>{versionsError}</MessageBarBody>
                  </MessageBar>
                )}

                {!versionsLoading && !versionsError && versions.length === 0 && (
                  <div className={styles.centered}>
                    <Text size={200}>No version history available.</Text>
                  </div>
                )}

                {!versionsLoading && !versionsError && versions.length > 0 && (
                  <div className={styles.versionList}>
                    {versions.map((version, index) => (
                      <div key={version.id} className={styles.versionRow}>
                        <Text size={200} className={styles.versionId}>
                          v{version.id}
                        </Text>
                        <div className={styles.versionMeta}>
                          <Text size={200} style={{ color: tokens.colorNeutralForeground1 }}>
                            {formatDate(version.lastModifiedDateTime)}
                          </Text>
                          {version.lastModifiedBy?.user?.displayName && (
                            <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
                              {version.lastModifiedBy.user.displayName}
                            </Text>
                          )}
                        </div>
                        <Text size={100} className={styles.versionSize}>
                          {formatFileSize(version.size)}
                        </Text>
                        {index === versions.length - 1 && (
                          <Badge appearance="tint" color="brand" size="small">
                            latest
                          </Badge>
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <Divider />
            </>
          )}

          {/* ── Section 4: Sharing Link Management (files only) ── */}
          {itemIsFile && (
            <div className={styles.section}>
              <div className={styles.sectionHeader}>
                <LinkRegular />
                <Text size={200} weight="semibold" className={styles.sectionTitle}>
                  Sharing Links
                </Text>
                <Button
                  size="small"
                  appearance="primary"
                  onClick={() => setLinkDialogOpen(true)}
                  style={{ marginLeft: "auto" }}
                >
                  Create Link
                </Button>
              </div>

              {/* Generated links list */}
              {generatedLinks.length === 0 && (
                <div className={styles.centered}>
                  <LinkRegular style={{ fontSize: "32px" }} />
                  <Text size={200}>
                    No sharing links created yet. Click &quot;Create Link&quot; to generate one.
                  </Text>
                </div>
              )}

              {generatedLinks.map((link, index) => {
                const url = link.link?.webUrl ?? "";
                const isCopied = copiedUrl === url;
                return (
                  <div key={index} className={styles.sharingLinkRow}>
                    <Tooltip content={url} relationship="label">
                      <Text className={styles.sharingLinkUrl}>
                        {url || "—"}
                      </Text>
                    </Tooltip>
                    <Badge appearance="tint" color="informative" size="small">
                      {link.link?.type ?? "view"}
                    </Badge>
                    <Badge appearance="tint" color="subtle" size="small">
                      {link.link?.scope ?? "organization"}
                    </Badge>
                    {url && (
                      isCopied ? (
                        <span className={styles.copiedBadge}>
                          <CheckmarkCircle20Regular />
                          <Text size={100}>Copied!</Text>
                        </span>
                      ) : (
                        <Tooltip content="Copy link to clipboard" relationship="label">
                          <Button
                            appearance="subtle"
                            size="small"
                            icon={<Copy20Regular />}
                            onClick={() => void handleCopyLink(url)}
                            aria-label="Copy sharing link"
                          />
                        </Tooltip>
                      )
                    )}
                  </div>
                );
              })}
            </div>
          )}

        </SidePaneShell>
      </div>

      {/* ── Create Sharing Link Dialog ── */}
      <Dialog
        open={linkDialogOpen}
        onOpenChange={(_e, { open }) => {
          if (!open) {
            setLinkDialogOpen(false);
            setLinkError(null);
          }
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Create Sharing Link</DialogTitle>
            <DialogContent>
              <div style={{ display: "flex", flexDirection: "column", gap: tokens.spacingVerticalM }}>
                <Field label="Link type">
                  <Select
                    value={linkType}
                    onChange={(_e, { value }) =>
                      setLinkType(value as SharingLinkType)
                    }
                  >
                    <option value="view">View (read-only)</option>
                    <option value="edit">Edit (read-write)</option>
                    <option value="embed">Embed</option>
                  </Select>
                </Field>

                <Field label="Scope">
                  <Select
                    value={linkScope}
                    onChange={(_e, { value }) =>
                      setLinkScope(value as SharingLinkScope)
                    }
                  >
                    <option value="organization">Organization (internal users)</option>
                    <option value="anonymous">Anonymous (anyone with the link)</option>
                    <option value="users">Specific users</option>
                  </Select>
                </Field>

                <Field
                  label="Expiration date (optional)"
                  hint="Leave blank for no expiration."
                >
                  <Input
                    type="datetime-local"
                    value={linkExpiry}
                    onChange={(_e, { value }) => setLinkExpiry(value)}
                  />
                </Field>

                {linkError && (
                  <MessageBar intent="error">
                    <MessageBarBody>{linkError}</MessageBarBody>
                  </MessageBar>
                )}

                {creatingLink && (
                  <ProgressBar />
                )}
              </div>
            </DialogContent>
            <DialogActions>
              <DialogTrigger disableButtonEnhancement>
                <Button appearance="secondary" disabled={creatingLink}>
                  Cancel
                </Button>
              </DialogTrigger>
              <Button
                appearance="primary"
                onClick={() => void handleCreateLink()}
                disabled={creatingLink}
                icon={creatingLink ? <Spinner size="tiny" /> : undefined}
              >
                {creatingLink ? "Creating..." : "Create Link"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
};
