/**
 * AttachmentList — presentational attachment list (no PCF / generated-types
 * dependency, so it is unit-testable in isolation and reusable).
 *
 * Renders each `IAttachmentItem` as name + type. Inline-image attachments are
 * expected to be filtered out UPSTREAM (by `filterFileAttachments`); this
 * component renders whatever it is given. `.eml`/`.msg` rows are badged "Email"
 * as a type label but otherwise behave like any other attachment — the parent
 * opens them in the same in-modal preview (UAT R4 B12-4).
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens, shorthands, Text, Link, Badge, Tooltip } from '@fluentui/react-components';
import { CloudCheckmark20Filled, CloudDismiss20Filled } from '@fluentui/react-icons';
import { IAttachmentItem } from './types';
import { isEmailMessageAttachment, fileTypeLabel } from './services/CommunicationAttachmentsService';

/**
 * Maps a row's `uploaded` boolean to its Fluent color token (B12-3 contract):
 * GREEN (`colorPaletteGreenForeground1`) when the document has an uploaded SPE
 * file, RED (`colorPaletteRedForeground1`) when not. Exported so the green/red
 * mapping is unit-testable independently of the griffel style layer. The color
 * MUST be applied to the Fluent icon via `mergeClasses` (not string
 * concatenation) — a hand-concatenated className is re-merged by the icon and
 * the color atomic class is dropped (root cause of the "all dark clouds" bug).
 */
export function uploadStatusColorToken(uploaded: boolean): string {
  return uploaded ? tokens.colorPaletteGreenForeground1 : tokens.colorPaletteRedForeground1;
}

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', ...shorthands.overflow('auto'), flex: 1 },
  // Spaarke list-row standard (docs/standards/UI-DESIGN-STANDARDS.md):
  // 20px min row height · 4px top + 4px bottom padding (spacingVerticalXS).
  // Token names ONLY (ADR-021) — no hardcoded px.
  row: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    minHeight: '20px',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingInline: tokens.spacingHorizontalL,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  rowDisabled: { cursor: 'default', ':hover': { backgroundColor: 'transparent' } },
  // Upload-status indicator (A11-2/A11-3) — SPE cloud glyph, colored via
  // semantic palette tokens so it stays legible in light + dark (ADR-021).
  // Green = the row's document has an uploaded SPE file; red = not uploaded.
  uploadIcon: { fontSize: '20px', flexShrink: 0 },
  uploadedYes: { color: tokens.colorPaletteGreenForeground1 },
  uploadedNo: { color: tokens.colorPaletteRedForeground1 },
  nameWrap: { display: 'flex', flexDirection: 'column', minWidth: 0, flex: 1 },
  nameLink: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorBrandForegroundLink,
    ...shorthands.overflow('hidden'),
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    textAlign: 'left',
  },
  nameText: {
    fontWeight: tokens.fontWeightSemibold,
    ...shorthands.overflow('hidden'),
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  typeBadge: { flexShrink: 0 },
});

export interface IAttachmentListProps {
  items: readonly IAttachmentItem[];
  /** Fired when a row (with a resolvable document) is activated. */
  onActivate: (item: IAttachmentItem) => void;
}

export const AttachmentList: React.FC<IAttachmentListProps> = ({ items, onActivate }) => {
  const s = useStyles();

  return (
    <div className={s.list} role="list" aria-label="Communication attachments">
      {items.map(item => {
        const isUploaded = item.uploaded === true;
        // Left slot = SPE upload-status glyph (A11-2/A11-3), replacing the old
        // file-type glyph. Green cloud-check = uploaded; red cloud-dismiss = not.
        const UploadIcon = isUploaded ? CloudCheckmark20Filled : CloudDismiss20Filled;
        const uploadLabel = isUploaded ? 'File uploaded to SharePoint' : 'File not uploaded to SharePoint';
        const isEml = isEmailMessageAttachment(item);
        // Read-only never gates opening: this is a preview/open viewer with no
        // mutating actions. A row is openable whenever it resolves a document.
        const canOpen = Boolean(item.documentId);
        const label = item.name || item.documentName || 'Attachment';
        return (
          <div
            key={item.attachmentId}
            role="listitem"
            className={canOpen ? s.row : mergeClasses(s.row, s.rowDisabled)}
            onClick={canOpen ? () => onActivate(item) : undefined}
          >
            <Tooltip content={uploadLabel} relationship="label">
              <UploadIcon
                className={mergeClasses(s.uploadIcon, isUploaded ? s.uploadedYes : s.uploadedNo)}
                aria-label={uploadLabel}
              />
            </Tooltip>
            <div className={s.nameWrap}>
              {canOpen ? (
                <Link
                  as="button"
                  appearance="subtle"
                  className={s.nameLink}
                  title={item.name}
                  onClick={(ev: React.MouseEvent) => {
                    ev.stopPropagation();
                    onActivate(item);
                  }}
                >
                  {label}
                </Link>
              ) : (
                <Text className={s.nameText} title={item.name}>
                  {label}
                </Text>
              )}
            </div>
            <Tooltip
              content={isEml ? 'Email message' : fileTypeLabel(item.name)}
              relationship="label"
            >
              <Badge className={s.typeBadge} appearance="outline" color={isEml ? 'brand' : 'informative'} size="small">
                {isEml ? 'Email' : fileTypeLabel(item.name)}
              </Badge>
            </Tooltip>
          </div>
        );
      })}
    </div>
  );
};
