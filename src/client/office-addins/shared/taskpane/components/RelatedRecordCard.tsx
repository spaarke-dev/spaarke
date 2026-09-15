import React from 'react';
import { makeStyles, tokens, Text, Card, Badge, Button } from '@fluentui/react-components';
import { LinkRegular } from '@fluentui/react-icons';
import type { DocumentIdentityState } from '../services/documentIdentityService';
import { useRelatedRecord, type RelatedRecordView } from '../hooks/useRelatedRecord';

/**
 * RelatedRecordCard.tsx — spaarkeai-word-add-in-r1 task 026 (FR-09).
 *
 * A READ-BACK for an identified document: when the open document is already filed to a Spaarke record, this
 * shows that record's type, descriptive name and number. It is deliberately the OPPOSITE of
 * `RelatedToPicker.tsx`, which is an INPUT for choosing a NEW record to file an unfiled document to — the two
 * coexist in the Save tab and must stay visually and functionally distinguishable (constraint, POML step 5):
 * this renders as a plain informational `Card` with a type `Badge`, no search box, no chips, no "+ New" button,
 * no selection state.
 *
 * SCOPE — read surface: exactly the FOUR direct association slots `sprk_matter`, `sprk_project`,
 * `sprk_invoice`, `sprk_workassignment` (precedence in that order), sourced from the ALREADY-RESOLVED
 * `documentIdentity` (task 013/012) via `useRelatedRecord` — no second network call, so this can never show a
 * different answer than the rest of the pane. Full rationale + the twelve EXCLUDED `sprk_related*` slots:
 * `projects/spaarkeai-word-add-in-r1/notes/026-slot-scope-decision.md`.
 *
 * SCOPE — click seam only: `onOpenRecord` is exposed but this component does not implement navigation. Task
 * 027 (FR-10) wires it to open the record.
 *
 * Theming (ADR-021): `tokens.*` only, no hex literals. Like `DocumentProfileSection` (the established
 * precedent for a read-back leaf component in this tab), this does NOT call `useOfficeTheme` directly — the
 * pane's root `FluentProvider` (`App.tsx` via `useTheme`, the same Office.js theme-bridge detection
 * `useOfficeTheme` runs) already resolves every `tokens.*` reference for the whole tree; a second, independent
 * theme subscription in this leaf component would not change what renders.
 *
 * Layout (fluent-v9-host-visual-fit pattern, Office narrow pane): the descriptive name WRAPS rather than
 * truncates — an ellipsis would hide the one piece of information a user opened the card to check — while the
 * number stays on its own line so it is never the part that scrolls out of view.
 */

const useStyles = makeStyles({
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  sectionTitle: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground2,
  },
  card: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalM,
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
    flexGrow: 1,
  },
  displayName: {
    overflowWrap: 'break-word',
    wordBreak: 'break-word',
  },
  number: {
    color: tokens.colorNeutralForeground2,
  },
  emptyValue: {
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
  openButton: {
    flexShrink: 0,
  },
  unassociated: {
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
    padding: tokens.spacingVerticalS,
  },
});

export interface RelatedRecordCardProps {
  /** The open document's identity state (task 013/012), threaded from `SaveFlow`. `undefined` = identity does
   * not apply to this host (Outlook) — the card renders nothing, gated by capability (NFR-10). */
  documentIdentity?: DocumentIdentityState;
  /**
   * Click seam for task 027 (FR-10): opens the related record. Absent → the card renders as plain,
   * non-interactive text (no button with nothing to do). This component never navigates on its own.
   */
  onOpenRecord?: (record: RelatedRecordView) => void;
}

export function RelatedRecordCard({
  documentIdentity,
  onOpenRecord,
}: RelatedRecordCardProps): React.ReactElement | null {
  const styles = useStyles();
  const outcome = useRelatedRecord(documentIdentity);

  if (outcome.kind === 'absent') {
    return null;
  }

  return (
    <div className={styles.section}>
      <div className={styles.sectionTitle}>
        <Text weight="semibold">Filed to</Text>
      </div>
      {outcome.kind === 'unassociated' ? (
        <Card>
          <Text size={200} className={styles.unassociated}>
            This document is not filed to a record yet.
          </Text>
        </Card>
      ) : (
        <Card className={styles.card}>
          <div className={styles.body}>
            <Badge appearance="tint" color="informative">
              {outcome.type}
            </Badge>
            {outcome.displayName ? (
              <Text weight="semibold" className={styles.displayName}>
                {outcome.displayName}
              </Text>
            ) : (
              <Text weight="semibold" className={styles.emptyValue}>
                Unnamed record
              </Text>
            )}
            {outcome.number ? (
              <Text size={200} className={styles.number}>
                {outcome.number}
              </Text>
            ) : (
              <Text size={200} className={styles.emptyValue}>
                No number yet
              </Text>
            )}
          </div>
          {onOpenRecord && (
            <Button
              className={styles.openButton}
              appearance="subtle"
              size="small"
              icon={<LinkRegular />}
              onClick={() => onOpenRecord(outcome)}
              aria-label={`Open ${outcome.type} ${outcome.displayName ?? ''}`}
            >
              Open
            </Button>
          )}
        </Card>
      )}
    </div>
  );
}

export default RelatedRecordCard;
