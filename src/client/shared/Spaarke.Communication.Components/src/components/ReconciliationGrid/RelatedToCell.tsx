/**
 * RelatedToCell.tsx
 *
 * The Pillar E reconciliation grid "Related to" cell (email-communication-intelligence-r2
 * task 052, FR-E3). A THIN grid-cell wrapper that REUSES the shipped
 * `EmailConnectionsReview` card (ADR-045 — no forked picker): the cell shows the
 * confirmed record chip, or a blank "Requires review" + an open-picker icon until
 * the association is confirmed. Clicking the icon opens `EmailConnectionsReview`
 * in a canonical `SprkModal` — the SAME candidate cards + manual lookup + the
 * new-vs-related "Create new record" intent the email-form review uses. Confirming
 * writes the association THROUGH `EmailConnectionsReview`'s existing
 * `applyRegardingSelection`/`RegardingFieldMap` path (ADR-024 single write path;
 * `writeContext.hostRecordId` MUST === `communicationId`) — this cell adds NO
 * second regarding-write path.
 *
 * A confirmed association here is the NFR-10 precondition consumed by the Fields
 * (055) and Tasks (056) reconcile tabs — `onConfirmed` is the handshake that lets
 * those tabs scope on the record (and re-scope on override).
 *
 * ADR-021 (Fluent v9 tokens, light + dark), ADR-022 (`React.FC` + standard hooks —
 * dual-use on the PCF email form mount).
 */
import * as React from 'react';
import { Badge, Button, Text, makeStyles, tokens } from '@fluentui/react-components';
import { PersonSearch20Regular, Link16Regular } from '@fluentui/react-icons';
import { SprkModal } from '@spaarke/ui-components';
import { EmailConnectionsReview } from '../EmailAssociationsAndTracking';
import type { EmailConnectionsReviewProps } from '../EmailAssociationsAndTracking/EmailAssociationsAndTracking.types';

/** `sprk_associationstatus` = Resolved (human-confirmed). */
const ASSOCIATION_STATUS_RESOLVED = 100000000;

const useStyles = makeStyles({
  root: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    maxWidth: '100%',
    minWidth: 0,
  },
  requiresReview: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  chip: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
});

export interface RelatedToCellProps {
  /**
   * The full `EmailConnectionsReview` props for this row (the single regarding-write
   * path lives inside it; the host supplies `writeContext` — with
   * `hostRecordId` === `communicationId` — plus the picker webApi + catalog + the
   * optional `onCreateNewRecord` intent). The cell mounts this verbatim in the
   * picker modal; it does NOT re-implement any of it.
   */
  review: EmailConnectionsReviewProps;
  /**
   * NFR-10 handshake — fired when the association is confirmed inside the picker
   * (wraps `review.onAssociationsChanged`), so the Fields/Tasks tabs (055/056) can
   * scope on the now-confirmed record and re-scope on a later override.
   */
  onConfirmed?: () => void;
  className?: string;
}

/**
 * Whether this row's association is confirmed — either the status is Resolved or a
 * regarding record is already denormalized onto the row.
 */
function isConfirmed(review: EmailConnectionsReviewProps): boolean {
  return (
    review.associationStatus === ASSOCIATION_STATUS_RESOLVED ||
    (typeof review.regardingRecordName === 'string' && review.regardingRecordName.trim().length > 0)
  );
}

export const RelatedToCell: React.FC<RelatedToCellProps> = ({ review, onConfirmed, className }) => {
  const s = useStyles();
  const [open, setOpen] = React.useState(false);

  const confirmed = isConfirmed(review);
  const recordName = review.regardingRecordName?.trim();

  // Confirm inside the picker fires `onAssociationsChanged` — wrap it to also
  // signal the NFR-10 handshake and close the modal.
  const handleAssociationsChanged = React.useCallback(() => {
    review.onAssociationsChanged?.();
    onConfirmed?.();
    setOpen(false);
  }, [review, onConfirmed]);

  // Create-new hands off to the host's create flow; close the picker behind it.
  const handleCreateNew = React.useMemo(
    () =>
      review.onCreateNewRecord
        ? () => {
            review.onCreateNewRecord?.();
            setOpen(false);
          }
        : undefined,
    [review]
  );

  return (
    <span className={className ? `${s.root} ${className}` : s.root}>
      {confirmed && recordName ? (
        <Badge appearance="tint" color="success" icon={<Link16Regular />} className={s.chip} title={recordName}>
          {recordName}
        </Badge>
      ) : (
        <>
          <Text as="span" className={s.requiresReview}>
            Requires review
          </Text>
          <Button
            appearance="subtle"
            size="small"
            icon={<PersonSearch20Regular />}
            aria-label="Review related record"
            data-testid="related-to-open-picker"
            onClick={() => setOpen(true)}
          />
        </>
      )}

      <SprkModal open={open} onClose={() => setOpen(false)} title="Related to" size="md" dismiss="light">
        <EmailConnectionsReview
          {...review}
          onAssociationsChanged={handleAssociationsChanged}
          onCreateNewRecord={handleCreateNew}
        />
      </SprkModal>
    </span>
  );
};
RelatedToCell.displayName = 'RelatedToCell';
