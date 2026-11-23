/**
 * RecordSectionCard — the one card every record-template section renders in (spaarke-SPA-external-access-platform-r4
 * task 005, FR-01 / design §6).
 *
 * The card header is the shared `HeaderToolbar` (the record-header toolbar of the model-driven form, as the accepted
 * prototype's `RecordSection`): the section title on the left and the section's icon actions on the right. Since task 009
 * the record's create actions live here (Upload in Documents, New message in Messages, Add event in Events): the
 * template builds them (`headerActions` in the section's context, only the offered ones) and the section passes them as
 * `actions`. A section with none passes nothing.
 *
 * COLLAPSE (task 013, owner 2026-11-14). A chevron button before the title expands and collapses the section
 * (`aria-expanded`, `aria-controls` the body; a native button, so Enter and Space work). DECISION: the header, with the
 * section's action icons, stays visible and usable while the section is collapsed (Upload, New message, Add event and
 * Submit invoice keep working on a collapsed card); only the body (note and content) is taken out of the layout. The body
 * stays mounted, so the list's own dialog and the action it offers to this header are not lost. The state is remembered
 * per user and record type (`recordSectionState.ts`), and a card with no template around it (a test, say) remembers nothing.
 *
 * The card is the section's region (`<section aria-label>`); the content inside keeps R3's test ids
 * (`record-detail-documents`, `record-detail-events`, ...), so the R3 tests ported to the template find the same blocks.
 *
 * ADR-021: Fluent v9 semantic tokens via `makeStyles` only (no hex, no px stroke literal).
 */
import * as React from 'react';
import { Button, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { ChevronDownRegular, ChevronRightRegular } from '@fluentui/react-icons';
import { HeaderToolbar, type IHeaderToolbarSlot } from '@spaarke/ui-components/components/HeaderToolbar';
import { useSectionCollapsed } from '../recordSectionState';
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
  header: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    minWidth: 0,
  },
  headerToolbar: {
    flexGrow: 1,
    flexShrink: 1,
    flexBasis: 0,
    minWidth: 0,
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    minWidth: 0,
  },
  // The body stays MOUNTED while collapsed (a list's create dialog, and the action it offers to the header, live in it), so
  // it is only taken out of the layout. `hidden` is set as well, for assistive technology.
  bodyCollapsed: {
    display: 'none',
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
  const { collapsed, toggle } = useSectionCollapsed(id);
  const bodyId = React.useId();
  return (
    <section className={styles.card} aria-label={title} data-testid={`record-section-${id}`}>
      <div className={styles.header}>
        <Button
          appearance="subtle"
          size="small"
          icon={collapsed ? <ChevronRightRegular /> : <ChevronDownRegular />}
          aria-expanded={!collapsed}
          aria-controls={bodyId}
          aria-label={`${title} section`}
          onClick={toggle}
          data-testid={`section-toggle-${id}`}
        />
        <div className={styles.headerToolbar}>
          <HeaderToolbar title={title} iconSlots={actions} />
        </div>
      </div>
      <div
        id={bodyId}
        className={mergeClasses(styles.body, collapsed && styles.bodyCollapsed)}
        hidden={collapsed}
        data-testid={`section-body-${id}`}
      >
        {note && (
          <p className={styles.note} data-testid={`record-access-note-${id}`}>
            {note}
          </p>
        )}
        {children}
      </div>
    </section>
  );
};
