/**
 * ResultCard — search result card using the shared RecordCardShell.
 *
 * Displays a single search result with the standard 2-row layout:
 *   Row 1: File icon | Title + document type
 *   Row 2: Relevance score badge + created date + created by
 *   Tools: Preview, AI Summary, Open File, Find Similar
 *
 * @see ADR-012 - Shared component library (RecordCardShell)
 * @see ADR-021 - Fluent UI v9 requirements
 */

import * as React from 'react';
import { useCallback, useState } from 'react';
import { tokens, Text, Button, Tooltip } from '@fluentui/react-components';
import {
  DocumentRegular,
  DocumentPdfRegular,
  DocumentTextRegular,
  TableRegular,
  SlideTextRegular,
  ImageRegular,
  MailRegular,
  Eye20Regular,
  Sparkle20Regular,
  DocumentSearchRegular,
  FolderOpenRegular,
} from '@fluentui/react-icons';
import { RecordCardShell, CardIcon } from '@spaarke/ui-components/dist/components/RecordCardShell';
import { AiSummaryPopover } from '@spaarke/ui-components/dist/components/AiSummaryPopover';
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
  const IconComp = getFileIcon(result.fileType);

  const handleCardDoubleClick = useCallback(() => {
    onOpenRecord(false);
  }, [onOpenRecord]);

  const handleCardClick = useCallback(
    (ev: React.MouseEvent | React.KeyboardEvent) => {
      if ('target' in ev && (ev.target as HTMLElement).closest('button')) return;
      onClick();
    },
    [onClick]
  );

  const handlePreview = useCallback(() => {
    setPreviewOpen(true);
  }, []);

  const handleOpenFile = useCallback(() => {
    onOpenFile('desktop');
  }, [onOpenFile]);

  const handleOpenRecord = useCallback(() => {
    onOpenRecord(false);
  }, [onOpenRecord]);

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
          <>
            <Tooltip content="Preview" relationship="label">
              <Button
                appearance="subtle"
                size="medium"
                icon={<Eye20Regular aria-hidden="true" />}
                aria-label="Preview document"
                onClick={handlePreview}
              />
            </Tooltip>
            <AiSummaryPopover
              onFetchSummary={onSummary}
              trigger={
                <Tooltip content="AI Summary" relationship="label">
                  <Button
                    appearance="subtle"
                    size="medium"
                    icon={<Sparkle20Regular aria-hidden="true" />}
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
                onClick={onFindSimilar}
              />
            </Tooltip>
          </>
        }
        onClick={handleCardClick}
        onDoubleClick={handleCardDoubleClick}
        ariaLabel={ariaLabel}
      />

      <FilePreviewDialog
        open={previewOpen}
        documentName={result.name}
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
