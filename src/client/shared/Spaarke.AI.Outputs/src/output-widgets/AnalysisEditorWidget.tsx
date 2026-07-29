/**
 * AnalysisEditorWidget
 *
 * Renders an AI-generated analysis as a list of titled sections with body text.
 * When the optional `editable` data flag is true, the user can toggle an edit
 * mode that replaces each body section with a Fluent v9 Textarea. A Save
 * button is shown when in edit mode (calls optional `onSave` prop).
 *
 * Live edit-state restore (task 025 / spec FR-09): when hosted as a workspace tab,
 * `onDataChange` (see `WorkspaceWidgetProps.onDataChange`) is used to persist the
 * IN-PROGRESS edit (isEditing + the draft sections) into the tab's own serialized
 * widgetData, which rides the EXISTING tab-persistence write-through (task 065 /
 * task 025 Analysis-anchor fix). Reopening the tab (or refreshing the page)
 * restores from `data.isEditing`/`data.draftSections` instead of always starting
 * read-only from `data.sections` — so a live, unsaved edit is never silently lost.
 * `onDataChange` is optional and absent in isolated render contexts (e.g. unit
 * tests) — the widget still works read-only/editable in that case, it just has no
 * durability across an unmount.
 *
 * NOT PCF-safe — requires React 19 and Fluent UI v9.
 *
 * Data is passed via props — no direct API calls inside this widget.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from 'react';
import { useState } from 'react';
import { makeStyles, mergeClasses, tokens, Text, Button, Textarea, Divider, Spinner } from '@fluentui/react-components';
import { EditRegular, SaveRegular } from '@fluentui/react-icons';
import type { OutputWidgetProps } from '../types';

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

export interface AnalysisSection {
  /** Section heading displayed as a sub-title (e.g. "Executive Summary"). */
  heading: string;
  /** Body text for this section. Supports plain text (not markdown-rendered). */
  body: string;
}

export interface AnalysisEditorData {
  /** One or more analysis sections to render. */
  sections: AnalysisSection[];
  /**
   * When true, an edit mode toggle button is shown and the user can modify
   * section bodies. Defaults to false (read-only).
   */
  editable?: boolean;
  /**
   * Task 025 (FR-09) restore hint: whether the widget was in edit mode when its
   * live state was last persisted. Read on mount to resume edit mode after a
   * tab close/reopen or page refresh; never set by the caller otherwise.
   */
  isEditing?: boolean;
  /**
   * Task 025 (FR-09) restore hint: the in-progress (unsaved) draft sections, if the
   * widget was mid-edit when its live state was last persisted. Read on mount
   * (alongside `isEditing`) so the user's live edit — not just the last-saved
   * `sections` — is what reappears. Cleared (omitted) once the user Saves or
   * Cancels so a stale draft never haunts a later restore.
   */
  draftSections?: AnalysisSection[];
}

export interface AnalysisEditorWidgetProps extends OutputWidgetProps<AnalysisEditorData> {
  /**
   * Callback invoked when the user clicks Save in edit mode.
   * Receives the updated sections array. The widget does NOT call any API
   * directly — the caller is responsible for persisting the changes.
   */
  onSave?: (updatedSections: AnalysisSection[]) => void;
  /**
   * Task 025 (FR-09) — reports a live edit-state patch (isEditing + draftSections)
   * so the host can persist it into the tab's widgetData (see WorkspaceWidgetProps
   * .onDataChange). Optional — omitted in isolated render contexts (e.g. unit
   * tests); the widget then simply has no cross-remount durability for a
   * mid-edit draft.
   */
  onDataChange?: (patch: Partial<AnalysisEditorData>) => void;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
  },
  toolbar: {
    display: 'flex',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
  },
  sectionList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  heading: {
    fontWeight: tokens.fontWeightSemibold,
  },
  body: {
    color: tokens.colorNeutralForeground2,
    whiteSpace: 'pre-wrap',
  },
  textarea: {
    width: '100%',
    minHeight: '120px',
    fontFamily: tokens.fontFamilyBase,
    fontSize: tokens.fontSizeBase300,
  },
  divider: {
    marginTop: tokens.spacingVerticalXS,
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * AnalysisEditorWidget renders sections of AI analysis. When editable is true,
 * users can toggle edit mode and modify individual section bodies. Changes are
 * surfaced via the onSave prop callback — the widget holds local draft state
 * during editing. When `onDataChange` is supplied (workspace-tab hosting), the
 * live draft is ALSO reported upward on every change so it survives a tab
 * close/reopen or page refresh (task 025 / FR-09) — the widget itself never
 * calls any API or persists anything autonomously.
 */
export default function AnalysisEditorWidget({
  data,
  isLoading,
  error,
  className,
  onSave,
  onDataChange,
}: AnalysisEditorWidgetProps): React.ReactElement {
  const styles = useStyles();

  // Local draft state — task 025 (FR-09): initialised from the RESTORE hints
  // (`data.isEditing` / `data.draftSections`) when present, so a live edit
  // resumes on reopen instead of always starting read-only. Lazy init (the
  // function form) runs ONCE on mount — later prop changes (e.g. a re-fetch
  // refreshing `data.sections`) do not clobber an in-progress edit.
  const [isEditing, setIsEditing] = useState<boolean>(() => data.isEditing ?? false);
  const [draftSections, setDraftSections] = useState<AnalysisSection[]>(
    () => data.draftSections ?? []
  );

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Spinner size="medium" label="Loading analysis..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  const handleEditToggle = (): void => {
    if (!isEditing) {
      // Clone sections into draft when opening edit mode
      const seeded = data.sections.map(s => ({ ...s }));
      setDraftSections(seeded);
      setIsEditing(true);
      onDataChange?.({ isEditing: true, draftSections: seeded });
      return;
    }
    setIsEditing(false);
    onDataChange?.({ isEditing: false });
  };

  const handleSectionBodyChange = (index: number, value: string): void => {
    setDraftSections(prev => {
      const updated = prev.map((s, i) => (i === index ? { ...s, body: value } : s));
      // Task 025 (FR-09): report the LIVE draft on every keystroke. The host
      // (WorkspacePane's tab-persistence write-through) already debounces the
      // actual write, so this is safe to call on every change.
      onDataChange?.({ isEditing: true, draftSections: updated });
      return updated;
    });
  };

  const handleSave = (): void => {
    onSave?.(draftSections);
    setIsEditing(false);
    // Clear the persisted draft — it is now committed via onSave; a stale draft
    // must not haunt a future restore.
    onDataChange?.({ isEditing: false, draftSections: undefined });
  };

  const handleCancel = (): void => {
    setIsEditing(false);
    // Discard the draft — clear the persisted restore hints to match.
    onDataChange?.({ isEditing: false, draftSections: undefined });
  };

  const sectionsToRender = isEditing ? draftSections : data.sections;

  return (
    <div className={mergeClasses(styles.root, className)}>
      {data.editable && (
        <div className={styles.toolbar}>
          {isEditing ? (
            <>
              <Button appearance="subtle" onClick={handleCancel}>
                Cancel
              </Button>
              <Button appearance="primary" icon={<SaveRegular />} onClick={handleSave}>
                Save
              </Button>
            </>
          ) : (
            <Button appearance="subtle" icon={<EditRegular />} onClick={handleEditToggle}>
              Edit
            </Button>
          )}
        </div>
      )}

      <div className={styles.sectionList}>
        {sectionsToRender.map((section, index) => (
          <div key={index} className={styles.section}>
            <Text size={400} className={styles.heading}>
              {section.heading}
            </Text>

            {isEditing ? (
              <Textarea
                className={styles.textarea}
                value={section.body}
                onChange={(_, d) => handleSectionBodyChange(index, d.value)}
                resize="vertical"
              />
            ) : (
              <Text size={300} className={styles.body}>
                {section.body}
              </Text>
            )}

            {index < sectionsToRender.length - 1 && <Divider className={styles.divider} />}
          </div>
        ))}
      </div>
    </div>
  );
}
