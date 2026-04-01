/**
 * NarrativeBullet -- renders a single AI-narrated bullet with action buttons.
 *
 * Each bullet shows the narrative text, a clickable record link that opens
 * the entity in a Dataverse dialog, and two action buttons: "Add to To Do"
 * and "Dismiss".
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only, dark mode via semantic tokens
 *   - Opens records via Xrm.Navigation.openForm
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Tooltip,
  Spinner,
} from "@fluentui/react-components";
import { DismissRegular } from "@fluentui/react-icons";
import { MicrosoftToDoIcon } from "../icons/MicrosoftToDoIcon";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalS,
    marginBottom: tokens.spacingVerticalL,
  },
  bullet: {
    color: tokens.colorNeutralForeground1,
    flexShrink: 0,
    lineHeight: tokens.lineHeightBase400,
    userSelect: "none",
  },
  content: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
  narrativeText: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
  },
  entityLink: {
    color: tokens.colorBrandForeground1,
    cursor: "pointer",
    textDecorationLine: "none",
    ":hover": {
      textDecorationLine: "underline",
    },
  },
  actions: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  todoIconDefault: {
    color: tokens.colorNeutralForeground3,
  },
  todoIconActive: {
    color: tokens.colorBrandForeground1,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface NarrativeBulletProps {
  /** AI-generated narrative text for this bullet. */
  narrative: string;
  /** Display name of the primary entity (shown as clickable link). */
  primaryEntityName: string;
  /** Dataverse logical name of the primary entity. */
  primaryEntityType: string;
  /** GUID of the primary entity record. */
  primaryEntityId: string;
  /** Notification IDs covered by this bullet. */
  itemIds: string[];
  /** Callback to add the covered notifications to To Do. */
  onAddToTodo: (itemIds: string[]) => void;
  /** Callback to dismiss the covered notifications. */
  onDismiss: (itemIds: string[]) => void;
  /** Whether a To Do has been created for this bullet. */
  isTodoCreated: boolean;
  /** Whether a To Do creation is in progress. */
  isTodoPending: boolean;
  /** Error message from a failed To Do creation. */
  todoError?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NarrativeBullet: React.FC<NarrativeBulletProps> = ({
  narrative,
  primaryEntityName,
  primaryEntityType,
  primaryEntityId,
  itemIds,
  onAddToTodo,
  onDismiss,
  isTodoCreated,
  isTodoPending,
  todoError,
}) => {
  const styles = useStyles();

  const handleLinkClick = () => {
    if (!primaryEntityType || !primaryEntityId) return;
    const xrm =
      (window as any)?.Xrm ??
      (window.parent as any)?.Xrm ??
      (window.top as any)?.Xrm;
    if (!xrm?.Navigation?.navigateTo) return;
    xrm.Navigation.navigateTo(
      {
        pageType: "entityrecord",
        entityName: primaryEntityType,
        entityId: primaryEntityId,
      },
      { target: 2, width: { value: 80, unit: "%" }, height: { value: 80, unit: "%" } }
    ).catch(() => { /* user closed dialog */ });
  };

  const handleAddToTodo = () => {
    if (!isTodoCreated && !isTodoPending) {
      onAddToTodo(itemIds);
    }
  };

  const handleDismiss = () => {
    onDismiss(itemIds);
  };

  // Determine To Do button tooltip
  let todoTooltip = "Add to To Do";
  if (isTodoCreated) todoTooltip = "Added to To Do";
  if (todoError) todoTooltip = todoError;

  return (
    <div className={styles.root}>
      <Text size={400} className={styles.bullet}>
        &bull;
      </Text>
      <div className={styles.content}>
        <Text size={400} className={styles.narrativeText}>
          {narrative}
        </Text>
        {primaryEntityName && primaryEntityType && primaryEntityId && (
          <Text
            size={300}
            className={styles.entityLink}
            onClick={handleLinkClick}
            role="link"
            tabIndex={0}
            onKeyDown={(e: React.KeyboardEvent) => {
              if (e.key === "Enter" || e.key === " ") handleLinkClick();
            }}
          >
            {primaryEntityName} &#8599;
          </Text>
        )}
      </div>
      <div className={styles.actions}>
        <Tooltip content={todoTooltip} relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={
              isTodoPending ? (
                <Spinner size="tiny" />
              ) : (
                <MicrosoftToDoIcon
                  size={16}
                  active={isTodoCreated}
                  className={
                    isTodoCreated
                      ? styles.todoIconActive
                      : styles.todoIconDefault
                  }
                />
              )
            }
            onClick={handleAddToTodo}
            disabled={isTodoCreated || isTodoPending}
            aria-label={todoTooltip}
          />
        </Tooltip>
        <Tooltip content="Dismiss" relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={<DismissRegular />}
            onClick={handleDismiss}
            aria-label="Dismiss"
          />
        </Tooltip>
      </div>
    </div>
  );
};
