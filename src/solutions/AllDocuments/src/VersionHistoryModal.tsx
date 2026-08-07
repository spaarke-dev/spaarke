/**
 * VersionHistoryModal — version-history affordance for the Documents surface
 * (task 051, spec FR-07 / Success Criterion 4).
 *
 * Lists a document's SPE versions (label / timestamp / size) from the task-050
 * OBO list-versions endpoint and opens a selected PRIOR version READ-ONLY
 * (exact bytes). Honest UX copy: this views prior versions — it does NOT
 * restore or branch, and no such affordance exists here (deferred by scope).
 *
 * Shell: `SprkModal` from `@spaarke/ui-components` (ADR-050 / MODAL-DESIGN-SYSTEM
 * — proprietary Fluent v9 modal, Family 2 per MODAL-DECISION-CRITERIA; the
 * Browse/Preview presets are document-stage modals, not pick-lists, so the
 * base shell with a list body is the right thin config here).
 *
 * Styling: Fluent v9 theme tokens ONLY (ADR-021 — legible in light + dark).
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  Button,
} from "@fluentui/react-components";
import {
  HistoryRegular,
  OpenRegular,
  EyeRegular,
  InfoRegular,
} from "@fluentui/react-icons";
import { SprkModal } from "@spaarke/ui-components";
import {
  listVersions,
  openPriorVersionReadOnly,
  formatSize,
  formatVersionTimestamp,
  type IVersionInfo,
} from "./versionHistory";

// ---------------------------------------------------------------------------
// Styles — theme tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  body: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    minHeight: "160px",
  },
  scopeNote: {
    color: tokens.colorNeutralForeground3,
  },
  readOnlyBanner: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
    borderRadius: tokens.borderRadiusMedium,
    borderLeftWidth: "3px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorBrandStroke1,
  },
  list: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    overflowY: "auto",
  },
  row: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow2,
  },
  rowMain: {
    flex: "1 1 0",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: "2px",
  },
  rowMeta: {
    color: tokens.colorNeutralForeground3,
  },
  currentBadge: {
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
  centerState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    flex: "1 1 0",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface VersionHistoryModalProps {
  open: boolean;
  onClose: () => void;
  /** Display name of the document (used in the title + download naming). */
  documentName: string;
  /** File extension (e.g. "docx", "pdf") — drives open-vs-download behavior. */
  fileType?: string;
  /** SPE drive id (sprk_graphdriveid on the sprk_document record). */
  driveId: string;
  /** SPE item id (sprk_graphitemid on the sprk_document record). */
  itemId: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const VersionHistoryModal: React.FC<VersionHistoryModalProps> = ({
  open,
  onClose,
  documentName,
  fileType,
  driveId,
  itemId,
}) => {
  const styles = useStyles();
  const [versions, setVersions] = React.useState<IVersionInfo[]>([]);
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [openingId, setOpeningId] = React.useState<string | null>(null);
  /** The prior version last opened read-only (drives the honest banner). */
  const [viewedVersionId, setViewedVersionId] = React.useState<string | null>(null);

  const load = React.useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const result = await listVersions(driveId, itemId);
      setVersions(result ?? []);
    } catch (err) {
      console.error("[AllDocuments] version list fetch error:", err);
      setError("Failed to load version history.");
    } finally {
      setIsLoading(false);
    }
  }, [driveId, itemId]);

  React.useEffect(() => {
    if (open) {
      setViewedVersionId(null);
      void load();
    }
  }, [open, load]);

  const handleOpenVersion = React.useCallback(
    async (versionId: string) => {
      setOpeningId(versionId);
      setError(null);
      try {
        await openPriorVersionReadOnly(driveId, itemId, versionId, documentName, fileType);
        setViewedVersionId(versionId);
      } catch (err) {
        console.error("[AllDocuments] open prior version error:", err);
        setError("Failed to open this version.");
      } finally {
        setOpeningId(null);
      }
    },
    [driveId, itemId, documentName, fileType]
  );

  return (
    <SprkModal
      open={open}
      onClose={onClose}
      title={`Version history — ${documentName}`}
      size="sm"
      maximizable={false}
      footer={
        <Button appearance="primary" onClick={onClose}>
          Close
        </Button>
      }
    >
      <div className={styles.body}>
        {/* Honest scope copy — view-only, no restore/branch (task 051 scope). */}
        <Text size={200} className={styles.scopeNote}>
          Prior versions open read-only. Restoring or branching from a prior
          version is not available.
        </Text>

        {viewedVersionId && (
          <div
            className={styles.readOnlyBanner}
            role="status"
            data-testid="read-only-banner"
          >
            <EyeRegular fontSize={16} />
            <Text size={200} weight="semibold">
              Viewing a prior version (read-only)
            </Text>
            <Text size={200}>Version {viewedVersionId}</Text>
          </div>
        )}

        {isLoading ? (
          <div className={styles.centerState}>
            <Spinner label="Loading version history..." />
          </div>
        ) : error ? (
          <div className={styles.centerState}>
            <InfoRegular fontSize={24} />
            <Text>{error}</Text>
            <Button appearance="subtle" onClick={load}>
              Retry
            </Button>
          </div>
        ) : versions.length === 0 ? (
          <div className={styles.centerState}>
            <HistoryRegular fontSize={24} />
            <Text>No version history found for this document.</Text>
          </div>
        ) : (
          <div className={styles.list} role="list" aria-label="Document versions">
            {versions.map((version, index) => {
              const isCurrent = index === 0;
              return (
                <div key={version.id} className={styles.row} role="listitem">
                  <div className={styles.rowMain}>
                    <div style={{ display: "flex", alignItems: "center", gap: tokens.spacingHorizontalS }}>
                      <Text size={300} weight="semibold">
                        Version {version.id}
                      </Text>
                      {isCurrent && <span className={styles.currentBadge}>Current</span>}
                    </div>
                    <Text size={200} className={styles.rowMeta}>
                      {formatVersionTimestamp(version.lastModifiedDateTime)}
                      {" · "}
                      {formatSize(version.size)}
                    </Text>
                  </div>
                  {!isCurrent && (
                    <Button
                      appearance="secondary"
                      size="small"
                      icon={
                        openingId === version.id ? (
                          <Spinner size="tiny" />
                        ) : (
                          <OpenRegular />
                        )
                      }
                      disabled={openingId !== null}
                      onClick={() => void handleOpenVersion(version.id)}
                      aria-label={`Open version ${version.id} read-only`}
                    >
                      Open read-only
                    </Button>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </SprkModal>
  );
};

export default VersionHistoryModal;
