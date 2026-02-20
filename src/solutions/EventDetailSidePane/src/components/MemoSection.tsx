/**
 * MemoSection - Related memo display/edit for Event Detail Side Pane
 *
 * Fixed section (not config-driven) shown at the bottom of every side pane.
 * Queries sprk_memo by _sprk_regardingevent_value.
 * - If memo exists: shows editable textarea
 * - If no memo: shows "+ Add Memo" button
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import {
  Textarea,
  Button,
  Text,
  Spinner,
  makeStyles,
  shorthands,
  tokens,
} from "@fluentui/react-components";
import {
  NoteRegular,
  AddRegular,
  SaveRegular,
} from "@fluentui/react-icons";
import { CollapsibleSection } from "./CollapsibleSection";
import { useRelatedRecord } from "../hooks/useRelatedRecord";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("8px"),
  },
  textarea: {
    width: "100%",
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    ...shorthands.gap("8px"),
    ...shorthands.padding("8px", "0"),
  },
  emptyText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  statusRow: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
  saveButton: {
    alignSelf: "flex-end",
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface MemoSectionProps {
  /** Event record ID */
  eventId: string | null;
  /** Whether editing is disabled */
  disabled?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const MemoSection: React.FC<MemoSectionProps> = ({
  eventId,
  disabled = false,
}) => {
  const styles = useStyles();
  const [memoText, setMemoText] = React.useState("");
  const [isDirty, setIsDirty] = React.useState(false);
  const [isSaving, setIsSaving] = React.useState(false);

  const memo = useRelatedRecord({
    entityName: "sprk_memo",
    parentLookupField: "sprk_regardingevent",
    parentId: eventId,
    selectFields: "sprk_memoid,sprk_name,sprk_memobody,createdon,modifiedon",
  });

  // Sync local state when record loads
  React.useEffect(() => {
    if (memo.record) {
      const text = (memo.record["sprk_memobody"] as string) ?? "";
      setMemoText(text);
      setIsDirty(false);
    }
  }, [memo.record]);

  const handleTextChange = React.useCallback(
    (_ev: unknown, data: { value: string }) => {
      setMemoText(data.value);
      setIsDirty(true);
    },
    []
  );

  const handleSave = React.useCallback(async () => {
    if (!isDirty) return;

    setIsSaving(true);
    try {
      if (memo.recordId) {
        // Update existing memo
        const success = await memo.updateRecord({ sprk_memobody: memoText });
        if (success) {
          setIsDirty(false);
        }
      }
    } finally {
      setIsSaving(false);
    }
  }, [isDirty, memo, memoText]);

  const handleAddMemo = React.useCallback(async () => {
    setIsSaving(true);
    try {
      const newId = await memo.createRecord({
        sprk_name: "Event Memo",
        sprk_memobody: "",
      });
      if (newId) {
        setMemoText("");
        setIsDirty(false);
      }
    } finally {
      setIsSaving(false);
    }
  }, [memo]);

  // Auto-save on blur
  const handleBlur = React.useCallback(() => {
    if (isDirty && memo.recordId) {
      handleSave();
    }
  }, [isDirty, memo.recordId, handleSave]);

  return (
    <CollapsibleSection
      title="Memo"
      icon={<NoteRegular />}
      defaultExpanded={true}
    >
      <div className={styles.container}>
        {/* Loading state */}
        {memo.isLoading && (
          <Spinner size="tiny" label="Loading memo..." />
        )}

        {/* Memo exists — show editable textarea */}
        {!memo.isLoading && memo.record && (
          <>
            <Textarea
              className={styles.textarea}
              value={memoText}
              onChange={handleTextChange}
              onBlur={handleBlur}
              disabled={disabled || isSaving}
              placeholder="Enter memo..."
              resize="vertical"
              rows={3}
              appearance="outline"
              aria-label="Event memo"
            />
            <div className={styles.statusRow}>
              <Text size={100}>
                {isDirty ? "Unsaved changes" : "Saved"}
              </Text>
              {isDirty && (
                <Button
                  className={styles.saveButton}
                  appearance="subtle"
                  icon={<SaveRegular />}
                  size="small"
                  onClick={handleSave}
                  disabled={isSaving}
                >
                  Save
                </Button>
              )}
            </div>
          </>
        )}

        {/* No memo — show add button */}
        {!memo.isLoading && !memo.record && (
          <div className={styles.emptyState}>
            <Text className={styles.emptyText}>No memo for this event</Text>
            <Button
              appearance="secondary"
              icon={<AddRegular />}
              onClick={handleAddMemo}
              disabled={disabled || isSaving}
              size="small"
            >
              Add Memo
            </Button>
          </div>
        )}
      </div>
    </CollapsibleSection>
  );
};
