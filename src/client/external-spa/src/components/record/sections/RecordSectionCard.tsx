/**
 * RecordSectionCard — the one card every record-template section renders in (spaarke-SPA-external-access-platform-r4
 * task 005, FR-01 / design §6).
 *
 * The card header is the shared `HeaderToolbar` (the record-header toolbar of the model-driven form, as the accepted
 * prototype's `RecordSection`): the section title on the left and the section's icon actions on the right. No section has
 * header actions yet (the record's create actions are toolbar actions, task 004), so `actions` defaults to none.
 *
 * The card is the section's region (`<section aria-label>`); the content inside keeps R3's test ids
 * (`record-detail-documents`, `record-detail-events`, ...), so the R3 tests ported to the template find the same blocks.
 *
 * ADR-021: Fluent v9 semantic tokens via `makeStyles` only (no hex, no px stroke literal).
 */
import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { HeaderToolbar, type IHeaderToolbarSlot } from '@spaarke/ui-components/components/HeaderToolbar';
import { RECORD_SECTION_LABELS, type RecordSectionId } from '../recordTemplate.types';

const useStyles = makeStyles({
  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    minWidth: 0,
    paddingBlock: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    boxShadow: tokens.shadow2,
  },
  note: {
    margin: 0,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground3,
  },
});

export interface RecordSectionCardProps {
  /** The section id: its title (`RECORD_SECTION_LABELS`) and its test id (`record-section-{id}`). */
  id: RecordSectionId;
  /** Icon actions in the card header. */
  actions?: IHeaderToolbarSlot[];
  /** Task 006 (FR-07): the one short line saying why the section is read-only; none when null/absent. */
  note?: string | null;
  children: React.ReactNode;
}

const NO_ACTIONS: IHeaderToolbarSlot[] = [];

export const RecordSectionCard: React.FC<RecordSectionCardProps> = ({ id, actions = NO_ACTIONS, note, children }) => {
  const styles = useStyles();
  const title = RECORD_SECTION_LABELS[id];
  return (
    <section className={styles.card} aria-label={title} data-testid={`record-section-${id}`}>
      <HeaderToolbar title={title} iconSlots={actions} />
      {note && (
        <p className={styles.note} data-testid={`record-access-note-${id}`}>
          {note}
        </p>
      )}
      {children}
    </section>
  );
};
