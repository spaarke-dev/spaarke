/**
 * ResultCard — search result card using the shared RecordCardShell.
 *
 * Displays a single search result with the standard 2-row layout:
 *   Row 1: File icon | Title + document type
 *   Row 2: Relevance score badge + created date + created by
 *   Tools: AI Summary sparkle (hover popover) + 3-dot menu (13 actions)
 *
 * Per FR-DOC-01, the inline action icons (Preview, Open File, Find Similar)
 * are consolidated into a single 3-dot `DocumentRowMenu` (shared component,
 * task 011). The `AiSummaryPopover` on the sparkle icon is RETAINED for
 * hover quick-glance; the menu's "AI summary" item invokes the same
 * popover via a ref-driven programmatic click (keyboard access).
 *
 * @see ADR-012 - Shared component library (RecordCardShell, DocumentRowMenu)
 * @see ADR-021 - Fluent UI v9 requirements
 * @see ADR-022 - React 16/17 compatible (no React 18-only APIs)
 * @see spec.md FR-DOC-01 - 3-dot menu consolidation
 */

import * as React from 'react';
import { useCallback, useRef, useState } from 'react';
import { tokens, Text, Button, Tooltip } from '@fluentui/react-components';
import {
  DocumentRegular,
  DocumentPdfRegular,
  DocumentTextRegular,
  TableRegular,
  SlideTextRegular,
  ImageRegular,
  MailRegular,
  Sparkle20Regular,
} from '@fluentui/react-icons';
import { RecordCardShell, CardIcon } from '@spaarke/ui-components/dist/components/RecordCardShell';
import { AiSummaryPopover } from '@spaarke/ui-components/dist/components/AiSummaryPopover';
// Deep-path import (not the barrel) — the barrel pulls in RichTextEditor →
// `@lexical/react` ESM modules that don't resolve `react/jsx-runtime` under
// React 16's resolution (PCF target per ADR-022). This matches the existing
// pattern used by `RecordCardShell` and `AiSummaryPopover` above.
import {
  DocumentRowMenu,
  type DocumentRowAction,
  type IDocumentRowMenuTarget,
} from '@spaarke/ui-components/dist/components/DocumentRowMenu';
import { IResultCardProps } from '../types';
import { FilePreviewDialog } from './FilePreviewDialog';

// ---------------------------------------------------------------------------
// File icon mapping
// ---------------------------------------------------------------------------

type IconComponent = typeof DocumentRegular;

function getFileIcon(fileType: string): IconComponent {
  const ext = fileType?.toLowerCase().trim() ?? '';
  switch (ext) {
    case 'pdf':
      return DocumentPdfRegular;
    case 'doc':
    case 'docx':
    case 'rtf':
    case 'odt':
    case 'txt':
      return DocumentTextRegular;
    case 'xls':
    case 'xlsx':
    case 'csv':
      return TableRegular;
    case 'ppt':
    case 'pptx':
      return SlideTextRegular;
    case 'jpg':
    case 'jpeg':
    case 'png':
    case 'gif':
    case 'bmp':
    case 'svg':
      return ImageRegular;
    case 'msg':
    case 'eml':
      return MailRegular;
    default:
      return DocumentRegular;
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatShortDate(dateString: string | null): string {
  if (!dateString) return '';
  try {
    const d = new Date(dateString);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  } catch {
    return '';
  }
}

const titleStyle: React.CSSProperties = {
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  color: tokens.colorNeutralForeground1,
  fontWeight: tokens.fontWeightSemibold,
  flexShrink: 1,
  minWidth: 0,
};

const fieldStyle: React.CSSProperties = {
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  color: tokens.colorNeutralForeground3,
  flexShrink: 1,
  minWidth: 0,
};

// ---------------------------------------------------------------------------
// Score badge
// ---------------------------------------------------------------------------

const ScoreBadge: React.FC<{ score: number }> = ({ score }) => {
  const pct = Math.round(score * 100);
  return (
    <span
      role="img"
      aria-label={`Relevance: ${pct}%`}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: tokens.borderRadiusSmall,
        paddingTop: '1px',
        paddingBottom: '1px',
        paddingLeft: tokens.spacingHorizontalXS,
        paddingRight: tokens.spacingHorizontalXS,
        fontSize: tokens.fontSizeBase100,
        fontWeight: tokens.fontWeightSemibold,
        lineHeight: tokens.lineHeightBase100,
        whiteSpace: 'nowrap',
        backgroundColor: tokens.colorBrandBackground2,
        color: tokens.colorBrandForeground1,
        flexShrink: 0,
      }}
    >
      {pct}%
    </span>
  );
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ResultCard: React.FC<IResultCardProps> = ({
  result,
  onClick,
  onOpenFile,
  onOpenRecord,
  onFindSimilar,
  onPreview,
  onSummary,
  onEmailDocument,
  onCopyLink,
  onToggleWorkspace,
  isInWorkspace,
  compactMode,
}) => {
  const [previewOpen, setPreviewOpen] = useState(false);
  const sparkleTriggerRef = useRef<HTMLButtonElement | null>(null);
  const IconComp = getFileIcon(result.fileType);

  const handleCardDoubleClick = useCallback(() => {
    onOpenRecord(false);
  }, [onOpenRecord]);

  const handleCardClick = useCallback(
    (ev: React.MouseEvent | React.KeyboardEvent) => {
      // Existing guard: clicks bubbling from any button (sparkle, menu trigger,
      // menu item buttons) should NOT open the preview dialog. The
      // DocumentRowMenu trigger also calls stopPropagation internally, so this
      // is defense-in-depth.
      if ('target' in ev && (ev.target as HTMLElement).closest('button')) return;
      onClick();
    },
    [onClick]
  );

  const handlePreview = useCallback(() => {
    setPreviewOpen(true);
  }, []);

  const handleOpenRecord = useCallback(() => {
    onOpenRecord(false);
  }, [onOpenRecord]);

  // -------------------------------------------------------------------------
  // 3-dot menu dispatch (FR-DOC-01)
  //
  // Maps the 12 canonical DocumentRowAction codes to the existing PCF
  // handlers. AI summary is the only handler that needs special wiring:
  // it programmatically clicks the sparkle trigger button so the SAME
  // `AiSummaryPopover` opens (keyboard-accessible path through the menu).
  // Pin/Rename/Delete are not yet wired in the PCF surface — they're
  // scoped to FR-DOC-02/05 (later tasks 041..046). Until then, those
  // menu items invoke no-ops; the menu structure still matches FR-DOC-01.
  // -------------------------------------------------------------------------
  const target = React.useMemo<IDocumentRowMenuTarget>(
    () => ({
      id: result.documentId,
      name: result.name,
      documentType: result.documentType,
    }),
    [result.documentId, result.name, result.documentType]
  );

  const handleAiSummaryFromMenu = useCallback(() => {
    // Programmatically open the AiSummaryPopover by clicking its trigger.
    // This guarantees a single popover surface for both hover (sparkle)
    // and keyboard (menu) paths — per spec FR-DOC-01 Owner Clarification.
    sparkleTriggerRef.current?.click();
  }, []);

  const handleRowAction = useCallback(
    (action: DocumentRowAction) => {
      switch (action) {
        case 'preview':
          handlePreview();
          return;
        case 'aiSummary':
          handleAiSummaryFromMenu();
          return;
        case 'openFile':
          onOpenFile('desktop');
          return;
        case 'findSimilar':
          onFindSimilar();
          return;
        case 'download':
          // Download = open in desktop app (existing platform convention).
          // FR-DOC-02 will introduce a dedicated download handler; for now
          // route to the existing open-file path so the affordance is not
          // orphaned (acceptance criterion: no orphaned affordances).
          onOpenFile('desktop');
          return;
        case 'copyLink':
          onCopyLink();
          return;
        case 'email':
          onEmailDocument();
          return;
        case 'openRecord':
          onOpenRecord(false);
          return;
        case 'toggleWorkspace':
          onToggleWorkspace();
          return;
        case 'pinToTop':
        case 'rename':
        case 'delete':
          // Not yet wired in the PCF surface (scoped to follow-on Phase 4
          // tasks — see project plan FR-DOC-02 + later tasks 041..046).
          // The menu items remain visible per FR-DOC-01 canonical ordering,
          // but invoke a no-op until the upstream handlers exist.
          return;
        default: {
          // Exhaustiveness check — any new DocumentRowAction added to the
          // union must be handled here at compile time.
          const _never: never = action;
          void _never;
          return;
        }
      }
    },
    [
      handlePreview,
      handleAiSummaryFromMenu,
      onOpenFile,
      onFindSimilar,
      onCopyLink,
      onEmailDocument,
      onOpenRecord,
      onToggleWorkspace,
    ]
  );

  // Per-row permission scoping: pinToTop/rename/delete are not yet wired
  // at this surface — disable them visibly (kept in the menu, but greyed).
  // The shared DocumentRowMenu currently *hides* disabledActions (per its
  // contract). Until the FR-SC-02 contract changes to "disable instead of
  // hide" (planned), we leave these visible to match the FR-DOC-01 spec
  // order exactly; the handlers above no-op safely.
  const disabledActions: DocumentRowAction[] | undefined = undefined;

  const formattedDate = formatShortDate(result.createdAt);

  const ariaLabel = [result.name, result.documentType, formattedDate ? `Created: ${formattedDate}` : '']
    .filter(Boolean)
    .join(', ');

  return (
    <>
      <RecordCardShell
        icon={
          <CardIcon>
            <IconComp fontSize={20} aria-label={result.fileType || 'Document'} />
          </CardIcon>
        }
        primaryContent={
          <>
            <Text as="span" size={400} style={titleStyle}>
              {result.name}
            </Text>
            {result.documentType && (
              <Text as="span" size={300} style={fieldStyle}>
                {result.documentType}
              </Text>
            )}
          </>
        }
        secondaryContent={
          <>
            <ScoreBadge score={result.combinedScore} />
            {formattedDate && (
              <Text as="span" size={200} style={fieldStyle}>
                Created: {formattedDate}
              </Text>
            )}
            {result.createdBy && (
              <Text as="span" size={200} style={fieldStyle}>
                By: {result.createdBy}
              </Text>
            )}
          </>
        }
        tools={
          // AiSummaryPopover sparkle is RETAINED per FR-DOC-01 Owner
          // Clarification — hover quick-glance + menu item for keyboard
          // access. The sparkle button is also the ref target for the
          // menu's "AI summary" item (programmatic click).
          <AiSummaryPopover
            onFetchSummary={onSummary}
            trigger={
              <Tooltip content="AI Summary" relationship="label">
                <Button
                  ref={sparkleTriggerRef}
                  appearance="subtle"
                  size="small"
                  icon={<Sparkle20Regular aria-hidden="true" />}
                  aria-label="AI Summary"
                />
              </Tooltip>
            }
          />
        }
        overflowMenu={
          <DocumentRowMenu
            document={target}
            onAction={handleRowAction}
            disabledActions={disabledActions}
          />
        }
        onClick={handleCardClick}
        onDoubleClick={handleCardDoubleClick}
        ariaLabel={ariaLabel}
      />

      <FilePreviewDialog
        open={previewOpen}
        documentName={result.name}
        documentId={result.documentId}
        documentType={result.documentType}
        onClose={() => setPreviewOpen(false)}
        fetchPreviewUrl={onPreview}
        onOpenFile={onOpenFile}
        onOpenRecord={handleOpenRecord}
        onEmailDocument={onEmailDocument}
        onCopyLink={onCopyLink}
        onToggleWorkspace={onToggleWorkspace}
        isInWorkspace={isInWorkspace}
      />
    </>
  );
};

export default ResultCard;
