/**
 * MessageAttachments.tsx
 *
 * Renders a message's attachment chips with an OPEN/preview/download affordance
 * (task 042 / FR-20). Shared by `MessageRow` (CommunicationTimeline),
 * `MessageBubble` and `EmailInFlowBlock` (ConversationView) so the open UX is
 * authored ONCE, not forked three times (root CLAUDE.md §11).
 *
 * Context-agnostic (ADR-012): this component never opens a document itself — it
 * has no `Xrm`/navigation/BFF surface. When the host wires `onOpenAttachment`,
 * each attachment that resolved to a governed `sprk_document` (has a
 * `documentId`) renders as a keyboard-operable button that hands the row back
 * to the host. The host mounts the EXISTING SPE document-viewer path — the
 * shared `<RichFilePreviewDialog />` fed by `/api/documents/{id}/preview-url` +
 * `/open-links` (the same wiring `CommunicationAttachmentsApp` already uses) —
 * NOT a new inline previewer (FR-20 constraint / escalation trigger).
 *
 * Access-filtering (NFR-01): the open affordance is gated on a resolved
 * `documentId`, which is only present on attachments the impersonated,
 * access-filtered thread read returned for a message this caller may read.
 * There is no client path that fabricates a retrieval id, and the BFF
 * preview/open-links endpoints re-enforce document-level access under OBO — so
 * a user without access can neither see nor retrieve the attachment (no
 * over-disclosure). An attachment with no `documentId` renders as a
 * non-interactive chip (nothing to open).
 *
 * Fluent v9 only — `makeStyles` + semantic `tokens`; dark mode passes through
 * the host `FluentProvider` (ADR-021). No hardcoded colors.
 */
import * as React from 'react';
import { Badge, Button, Tooltip, makeStyles, tokens } from '@fluentui/react-components';
import { DocumentRegular, OpenRegular } from '@fluentui/react-icons';
import type { TimelineAttachment, TimelineMessage } from '../CommunicationTimeline.types';

export interface IMessageAttachmentsProps {
  attachments: TimelineAttachment[];
  /** Handed back to `onOpenAttachment` so the host has full row context (thread/sender/etc.). */
  message: TimelineMessage;
  /**
   * Fired when the user activates an attachment that resolved to a governed
   * Document. The host opens it via the existing SPE document-viewer path
   * (`RichFilePreviewDialog`). Omit it and every attachment renders as a
   * non-interactive chip (the pre-task-042 display).
   */
  onOpenAttachment?: (attachment: TimelineAttachment, message: TimelineMessage) => void;
  className?: string;
}

const useStyles = makeStyles({
  row: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
    marginTop: tokens.spacingVerticalXXS,
  },
});

export const MessageAttachments: React.FC<IMessageAttachmentsProps> = ({
  attachments,
  message,
  onOpenAttachment,
  className,
}) => {
  const styles = useStyles();
  if (!attachments || attachments.length === 0) return null;

  return (
    <div className={className ?? styles.row}>
      {attachments.map(a => {
        const label = a.fileName ?? 'Attachment';
        // Openable ONLY when the attachment resolved to a governed Document AND
        // the host wired an open handler. Everything else is a passive chip.
        const canOpen = !!a.documentId && !!onOpenAttachment;
        if (canOpen) {
          return (
            <Tooltip key={a.id} content={`Open ${label}`} relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<OpenRegular />}
                iconPosition="before"
                aria-label={`Open attachment ${label}`}
                onClick={() => onOpenAttachment?.(a, message)}
              >
                {label}
              </Button>
            </Tooltip>
          );
        }
        return (
          <Badge key={a.id} appearance="outline" icon={<DocumentRegular />} size="small">
            {label}
          </Badge>
        );
      })}
    </div>
  );
};

MessageAttachments.displayName = 'MessageAttachments';
