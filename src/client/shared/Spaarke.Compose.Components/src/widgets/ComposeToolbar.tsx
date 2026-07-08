/**
 * ComposeToolbar.tsx — Compose workspace command-bar.
 *
 * Renders Open-in-Word (Web + Desktop) + Save buttons. AI actions
 * (Summarize etc.) fire from the Assistant pane through R7's LinearConsumers
 * path — this toolbar owns only DOCX-editor lifecycle actions.
 *
 * Standards (binding):
 *   - ADR-021 (Fluent UI v9 + semantic tokens; dark-mode parity)
 *   - ADR-028 (Spaarke Auth v2 — `useDocumentActions` uses `authenticatedFetch`
 *     from `@spaarke/auth` internally; this component handles NO auth tokens)
 *
 * @see src/client/shared/Spaarke.DocumentOperations/src/hooks/useDocumentActions.ts
 */

import * as React from 'react';
import { Toolbar, ToolbarButton, Tooltip, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { OpenRegular, DesktopRegular, SaveRegular } from '@fluentui/react-icons';
import { useDocumentActions } from '@spaarke/document-operations';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ComposeToolbarProps {
  /**
   * The active Compose document identifier. Either the SPE drive-item id
   * (ephemeral / pre-first-Save) or the `sprk_documentid` GUID (post-promotion).
   * The BFF `/api/documents/{id}/open-links` endpoint resolves both shapes.
   *
   * Empty string disables the Open-in-Word buttons.
   */
  documentId: string;

  /**
   * BFF base URL (host only, e.g. `https://host.azurewebsites.net`). Passed
   * to `useDocumentActions`. Empty string disables all actions.
   */
  bffBaseUrl: string;

  /**
   * Disable the toolbar entirely (e.g. while the host is hydrating).
   */
  disabled?: boolean;

  /**
   * Optional className applied to the underlying Fluent v9 `Toolbar` root.
   */
  className?: string;

  /**
   * Save handler. When provided, the toolbar renders a Save button.
   */
  onSaveRequested?: () => void;

  /**
   * True when the document has unsaved changes.
   */
  isDirty?: boolean;

  /**
   * True while a save is in flight.
   */
  isSaving?: boolean;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  toolbar: {
    width: 'fit-content',
    columnGap: tokens.spacingHorizontalXS,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    paddingInline: tokens.spacingHorizontalS,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function ComposeToolbar(props: ComposeToolbarProps): React.JSX.Element {
  const styles = useStyles();
  const { documentId, bffBaseUrl, disabled, className, onSaveRequested, isDirty = false, isSaving = false } = props;

  const { openInWeb, openInDesktop, isActing } = useDocumentActions({
    bffBaseUrl,
  });

  const isToolbarDisabled = disabled === true;
  const hasDocument = documentId.length > 0 && bffBaseUrl.length > 0;

  const openInWebDisabled = isToolbarDisabled || !hasDocument || isActing;
  const openInDesktopDisabled = isToolbarDisabled || !hasDocument || isActing;

  const handleOpenInWeb = React.useCallback((): void => {
    if (openInWebDisabled) return;
    void openInWeb(documentId);
  }, [openInWebDisabled, openInWeb, documentId]);

  const handleOpenInDesktop = React.useCallback((): void => {
    if (openInDesktopDisabled) return;
    void openInDesktop(documentId);
  }, [openInDesktopDisabled, openInDesktop, documentId]);

  return (
    <Toolbar className={mergeClasses(styles.toolbar, className)} size="small" aria-label="Compose document actions">
      <Tooltip
        content={hasDocument ? 'Open this document in Word for the Web' : 'No document loaded'}
        relationship="label"
      >
        <ToolbarButton
          icon={<OpenRegular />}
          disabled={openInWebDisabled}
          onClick={handleOpenInWeb}
          aria-label="Open in Word for Web"
        >
          Open in Word Web
        </ToolbarButton>
      </Tooltip>

      <Tooltip
        content={hasDocument ? 'Open this document in the Word desktop app' : 'No document loaded'}
        relationship="label"
      >
        <ToolbarButton
          icon={<DesktopRegular />}
          disabled={openInDesktopDisabled}
          onClick={handleOpenInDesktop}
          aria-label="Open in Word Desktop"
        >
          Open in Word Desktop
        </ToolbarButton>
      </Tooltip>

      {onSaveRequested ? (
        <Tooltip
          content={
            isToolbarDisabled || !hasDocument
              ? 'No document loaded'
              : isSaving
                ? 'Saving…'
                : isDirty
                  ? 'Save changes (Ctrl+S)'
                  : 'No unsaved changes'
          }
          relationship="label"
        >
          <ToolbarButton
            icon={<SaveRegular />}
            disabled={isToolbarDisabled || !hasDocument || isSaving || !isDirty}
            onClick={onSaveRequested}
            aria-label={isSaving ? 'Saving' : 'Save changes'}
          >
            {isSaving ? 'Saving…' : 'Save'}
          </ToolbarButton>
        </Tooltip>
      ) : null}
    </Toolbar>
  );
}
