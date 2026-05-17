/**
 * AnalysisEditorWidget
 *
 * Renders an AI-generated analysis as a list of titled sections with body text.
 * When the optional `editable` data flag is true, the user can toggle an edit
 * mode that replaces each body section with a Fluent v9 Textarea. A Save
 * button is shown when in edit mode (calls optional `onSave` prop).
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
}

export interface AnalysisEditorWidgetProps extends OutputWidgetProps<AnalysisEditorData> {
  /**
   * Callback invoked when the user clicks Save in edit mode.
   * Receives the updated sections array. The widget does NOT call any API
   * directly — the caller is responsible for persisting the changes.
   */
  onSave?: (updatedSections: AnalysisSection[]) => void;
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
 * during editing but never persists anything autonomously.
 */
export default function AnalysisEditorWidget({
  data,
  isLoading,
  error,
  className,
  onSave,
}: AnalysisEditorWidgetProps): React.ReactElement {
  const styles = useStyles();

  // Local draft state — initialised from props when entering edit mode
  const [isEditing, setIsEditing] = useState(false);
  const [draftSections, setDraftSections] = useState<AnalysisSection[]>([]);

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
      setDraftSections(data.sections.map(s => ({ ...s })));
    }
    setIsEditing(prev => !prev);
  };

  const handleSectionBodyChange = (index: number, value: string): void => {
    setDraftSections(prev => prev.map((s, i) => (i === index ? { ...s, body: value } : s)));
  };

  const handleSave = (): void => {
    onSave?.(draftSections);
    setIsEditing(false);
  };

  const sectionsToRender = isEditing ? draftSections : data.sections;

  return (
    <div className={mergeClasses(styles.root, className)}>
      {data.editable && (
        <div className={styles.toolbar}>
          {isEditing ? (
            <>
              <Button appearance="subtle" onClick={() => setIsEditing(false)}>
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
