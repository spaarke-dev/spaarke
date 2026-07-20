/**
 * AttachmentList — presentational attachment list (no PCF / generated-types
 * dependency, so it is unit-testable in isolation and reusable).
 *
 * Renders each `IAttachmentItem` as name + type. Inline-image attachments are
 * expected to be filtered out UPSTREAM (by `filterFileAttachments`); this
 * component renders whatever it is given. `.eml`/`.msg` rows are badged "Email"
 * and carry an external-open affordance — the parent routes them to
 * download/open rather than the inline preview modal.
 */

import * as React from 'react';
import { makeStyles, tokens, shorthands, Text, Link, Badge, Tooltip } from '@fluentui/react-components';
import {
  DocumentRegular,
  DocumentPdfRegular,
  DocumentTextRegular,
  TableRegular,
  SlideTextRegular,
  ImageRegular,
  MailRegular,
  OpenRegular,
} from '@fluentui/react-icons';
import { IAttachmentItem } from './types';
import { isEmailMessageAttachment, fileTypeLabel } from './services/CommunicationAttachmentsService';

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', ...shorthands.overflow('auto'), flex: 1 },
  row: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalL,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  rowDisabled: { cursor: 'default', ':hover': { backgroundColor: 'transparent' } },
  icon: { fontSize: '20px', color: tokens.colorNeutralForeground2, flexShrink: 0 },
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

type IconComponent = typeof DocumentRegular;

export function getFileIcon(name: string): IconComponent {
  const dot = name.lastIndexOf('.');
  const ext = dot >= 0 ? name.slice(dot + 1).toLowerCase() : '';
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
    case 'eml':
    case 'msg':
      return MailRegular;
    default:
      return DocumentRegular;
  }
}

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
        const Icon = getFileIcon(item.name);
        const isEml = isEmailMessageAttachment(item);
        // Read-only never gates opening: this is a preview/open viewer with no
        // mutating actions. A row is openable whenever it resolves a document.
        const canOpen = Boolean(item.documentId);
        const label = item.name || item.documentName || 'Attachment';
        return (
          <div
            key={item.attachmentId}
            role="listitem"
            className={canOpen ? s.row : `${s.row} ${s.rowDisabled}`}
            onClick={canOpen ? () => onActivate(item) : undefined}
          >
            <Icon className={s.icon} aria-hidden="true" />
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
              content={isEml ? 'Email message — opens/downloads' : fileTypeLabel(item.name)}
              relationship="label"
            >
              <Badge
                className={s.typeBadge}
                appearance="outline"
                color={isEml ? 'brand' : 'informative'}
                size="small"
              >
                {isEml ? 'Email' : fileTypeLabel(item.name)}
              </Badge>
            </Tooltip>
            {isEml && <OpenRegular className={s.icon} aria-label="Opens externally" />}
          </div>
        );
      })}
    </div>
  );
};
