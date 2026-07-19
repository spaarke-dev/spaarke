/**
 * ConversationPaneChrome — presentational chrome for the ConversationPane
 * thin host, extracted from ConversationPane.tsx by ai-architecture-redesign-r1
 * task 045 (FR-P3-06 decomposition). Markup + styles moved VERBATIM; every
 * component here is stateless presentation (RestoreBanners owns only its
 * local expand toggle).
 *
 * ADR-021: Fluent v9 semantic tokens only — no hardcoded colors.
 */

import * as React from "react";
import { makeStyles, tokens, Button, Spinner, Tag, Text, Tooltip } from "@fluentui/react-components";
import {
  EditRegular,
  DismissRegular,
  ArrowResetRegular,
  CheckmarkCircleRegular,
} from "@fluentui/react-icons";
import type { SelectionChipState } from "./useSelectionChip";

/**
 * Host layout styles (root column + content/chat regions + the SprkChat flex
 * anchor whose `position: relative` hosts the absolutely-positioned
 * HelpAffordance). Consumed by the ConversationPane thin host.
 */
export const useConversationPaneLayoutStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  content: { flex: 1, minHeight: 0, overflow: "hidden", display: "flex", flexDirection: "column" },
  chatWrapper: { flex: 1, minHeight: 0, overflow: "hidden", display: "flex", flexDirection: "column" },
  sprkChatFlex: { flex: 1, minHeight: 0, overflow: "hidden", position: "relative" },
});

const useStyles = makeStyles({
  // ── Auth loading state ────────────────────────────────────────────────────
  loadingContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },

  // ── Playbook header strip (AIPU2-102) ────────────────────────────────────
  playbookHeader: {
    flexShrink: 0,
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorBrandBackground2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorBrandStroke2,
    minHeight: "32px",
  },
  playbookHeaderName: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorBrandForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    flex: "1",
    minWidth: "0",
  },
  changePlaybookButton: {
    flexShrink: 0,
    fontSize: tokens.fontSizeBase100,
    height: "24px",
    minWidth: "0",
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground2,
  },

  // ── Playbook confirmation toast (AIPU2-102) ───────────────────────────────
  toastStrip: {
    flexShrink: 0,
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorStatusSuccessBackground1,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorStatusSuccessForeground3,
  },
  toastIcon: {
    color: tokens.colorStatusSuccessForeground1,
    fontSize: tokens.fontSizeBase300,
    flexShrink: 0,
  },
  toastText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorStatusSuccessForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },

  // ── "Refine this?" selection chip ─────────────────────────────────────────
  refinementChipBar: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  refinementChipLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  refinementChipTag: {
    cursor: "pointer",
    maxWidth: "220px",
  },
  refinementChipTagText: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    fontSize: tokens.fontSizeBase200,
  },
  refinementChipDismiss: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    ":hover": {
      color: tokens.colorNeutralForeground1,
    },
  },

  // ── "N files attached" indicator (R5 task 020 / D2-11) ───────────────────
  filesAttachedIndicator: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  filesAttachedIndicatorText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightSemibold,
  },
  filesAttachedIndicatorHint: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },

  // ── Upload/classify progress row (UP-10, UAT 2026-07-19) ─────────────────
  uploadProgressIndicator: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  uploadProgressText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },

  // ── Conversation restore summary block (AIPU2-106) ────────────────────────
  restoreSummaryBlock: {
    flexShrink: 0,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground3,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    cursor: "pointer",
  },
  restoreSummaryHeader: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  restoreSummaryContent: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    whiteSpace: "pre-wrap",
    maxHeight: "120px",
    overflowY: "auto",
    lineHeight: tokens.lineHeightBase200,
  },
  restoreStaleWarning: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorStatusWarningBackground1,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorStatusWarningForeground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorStatusWarningForeground3,
  },
});

/** Auth-resolving spinner (mirrors R1 ChatPanel behaviour). */
export function AuthLoadingState(): React.JSX.Element {
  const styles = useStyles();
  return (
    <div className={styles.loadingContainer}>
      <Spinner size="medium" label="Initializing AI Chat..." labelPosition="below" />
      <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
        Connecting to Dataverse...
      </Text>
    </div>
  );
}

/** Playbook header strip — shown once a playbook is active (Stage 2+). */
export function PlaybookHeaderStrip(props: {
  name: string;
  onChangePlaybook: () => void;
}): React.JSX.Element {
  const styles = useStyles();
  return (
    <div className={styles.playbookHeader} role="status" aria-label={`Active playbook: ${props.name}`}>
      <Text className={styles.playbookHeaderName} title={props.name}>
        {props.name}
      </Text>
      <Button
        appearance="subtle"
        size="small"
        icon={<ArrowResetRegular />}
        className={styles.changePlaybookButton}
        onClick={props.onChangePlaybook}
        title="Select a different playbook"
        aria-label="Change playbook"
      >
        Change
      </Button>
    </div>
  );
}

/** Playbook confirmation toast strip (auto-dismissed by the selection hook). */
export function PlaybookToast(props: { name: string }): React.JSX.Element {
  const styles = useStyles();
  return (
    <div
      className={styles.toastStrip}
      role="status"
      aria-live="polite"
      aria-label={`Playbook switched to ${props.name}`}
    >
      <CheckmarkCircleRegular className={styles.toastIcon} />
      <Text className={styles.toastText}>
        Switched to <strong>{props.name}</strong>
      </Text>
    </div>
  );
}

/** Restore-context banners (AIPU2-106): stale-entity warning + collapsible summary. */
export function RestoreBanners(props: {
  hasStaleEntities: boolean;
  conversationSummary: string | null | undefined;
}): React.JSX.Element {
  const styles = useStyles();
  const [summaryExpanded, setSummaryExpanded] = React.useState(false);
  return (
    <>
      {props.hasStaleEntities && (
        <div className={styles.restoreStaleWarning} role="alert">
          Some referenced entities have changed since this session was saved. Results may differ
          from the original analysis.
        </div>
      )}
      {props.conversationSummary && (
        <div
          className={styles.restoreSummaryBlock}
          role="region"
          aria-label="Previous conversation summary"
          onClick={() => setSummaryExpanded((prev) => !prev)}
        >
          <div className={styles.restoreSummaryHeader}>
            {summaryExpanded ? "▼" : "▶"} Previous conversation
          </div>
          {summaryExpanded && (
            <div className={styles.restoreSummaryContent}>{props.conversationSummary}</div>
          )}
        </div>
      )}
    </>
  );
}

/** "Refine this?" chip bar (AIPU2-101) — visible while workspace text is selected. */
export function RefinementChipBar(props: {
  chip: SelectionChipState;
  onClick: () => void;
  onDismiss: (e: React.MouseEvent) => void;
}): React.JSX.Element {
  const styles = useStyles();
  const { chip } = props;
  return (
    <div className={styles.refinementChipBar} role="region" aria-label="Refinement suggestion">
      <Text className={styles.refinementChipLabel}>Refine this?</Text>
      <Tooltip content={chip.selectedText} relationship="description" positioning="above-start">
        <Tag
          className={styles.refinementChipTag}
          appearance="brand"
          icon={<EditRegular />}
          onClick={props.onClick}
          role="button"
          aria-label={`Refine selected text from ${chip.contextLabel}`}
        >
          <span className={styles.refinementChipTagText}>
            {chip.selectedText.length > 40
              ? `${chip.selectedText.slice(0, 37)}…`
              : chip.selectedText}
          </span>
        </Tag>
      </Tooltip>
      <Button
        appearance="subtle"
        size="small"
        icon={<DismissRegular />}
        className={styles.refinementChipDismiss}
        aria-label="Dismiss refinement suggestion"
        onClick={props.onDismiss}
      />
    </div>
  );
}

/**
 * Upload/classify progress row (UP-10, UAT 2026-07-19) — rendered above the
 * SprkChat input zone WHILE the composer is locked during the ingest window, so
 * the user knows to wait. Shows "Attaching file…" during the `/documents`
 * promotion POST, then "Classifying file…" during the Event classify SSE stream.
 * Returns null when idle. `role="status"` + `aria-live="polite"` for a11y.
 */
export function UploadProgressIndicator(props: {
  attaching: boolean;
  classifying: boolean;
}): React.JSX.Element | null {
  const styles = useStyles();
  if (!props.attaching && !props.classifying) return null;
  // Attach precedes classify; if both are somehow set, surface the earlier stage.
  const label = props.attaching ? "Attaching file…" : "Classifying file…";
  return (
    <div
      className={styles.uploadProgressIndicator}
      role="status"
      aria-live="polite"
      data-testid="upload-progress-indicator"
    >
      <Spinner size="tiny" />
      <Text className={styles.uploadProgressText}>{label}</Text>
    </div>
  );
}

/**
 * "N files attached" indicator (R5 task 020 / D2-11) — rendered above the
 * SprkChat input zone whenever the session has uploaded files. `role="status"`
 * + `aria-live="polite"` so screen readers announce count changes.
 */
export function FilesAttachedIndicator(props: {
  uploadedFileCount: number;
  promotedCount: number;
}): React.JSX.Element {
  const styles = useStyles();
  const { uploadedFileCount, promotedCount } = props;
  return (
    <div
      className={styles.filesAttachedIndicator}
      role="status"
      aria-live="polite"
      data-testid="files-attached-indicator"
    >
      <Text className={styles.filesAttachedIndicatorText}>
        {uploadedFileCount === 1 ? "1 file attached" : `${uploadedFileCount} files attached`}
      </Text>
      <Text className={styles.filesAttachedIndicatorHint}>
        {uploadedFileCount === 1
          ? "available for this session"
          : "available for this session — combined Summarize will fold all into one"}
      </Text>
      {/* R5 task 036: Held vs Indexed visibility without opening the workspace pane. */}
      {promotedCount > 0 && (
        <Text className={styles.filesAttachedIndicatorHint} data-testid="files-promoted-indicator">
          {`(${promotedCount} indexed)`}
        </Text>
      )}
    </div>
  );
}
