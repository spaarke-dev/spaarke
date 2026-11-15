/**
 * ChildRecordModal — a record opened in a child modal over the record being shown
 * (spaarke-SPA-external-access-platform-r4 task 013, owner retest 2026-11-14: "for Invoices I don't see how to open the
 * invoice record (should open on top of this modal with invoice modal)"; documents likewise).
 *
 * WHAT IT HOSTS. `RecordTemplateModal` is itself the canonical `SprkModal` (size `lg`, with its own header, title and Close),
 * so the child IS that component, in its `nested` variant: one dialog, not a dialog inside a dialog. The only difference
 * `nested` makes is that the child's own rows do not open a further modal (one child level only; they behave as before).
 * Everything else is the record template unchanged: the child reads its own record and shows only its own access and
 * actions exactly as when it is opened from a grid (FR-06 / FR-07); nothing is inherited from the parent. This is not
 * `useOpenRecord`, which REPLACES the open record.
 *
 * WHERE IT LIVES. `RecordTemplateModal` mounts it as a React descendant of its own body (the same arrangement as every
 * child dialog, `SubmitInvoiceAction`), so focus stays trapped in the child while it is open and Escape closes the child,
 * not the record behind it. Sections reach it through `childRecord.ts` (`useChildRecordOpener`), never by import.
 *
 * FOCUS AND REFRESH. `useChildDialog` remembers the control that opened it and returns focus to it on close. The close also
 * asks the activating section to re-read when the opener named one (an invoice child may have changed something: a
 * message; its Files are read-only), which draws the section's rows anew, so `refocusChildRecordRow` puts focus on the same row once it is back. A
 * document child names none (it cannot change the list), so the parent's grid keeps its sort.
 */
import * as React from 'react';
import { useChildDialog } from '../createAction';
import { childRecordKeyOf, refocusChildRecordRow, type OpenChildRecord } from './childRecord';
import { RecordTemplateModal } from './RecordTemplateModal';
import type { RecordSectionId } from './recordTemplate.types';
import { getRecordTemplate } from './recordTemplates';

const noopIndexChange = (): void => undefined;

export interface ChildRecordModalProps {
  open: boolean;
  entity: string;
  recordId: string;
  onClose: () => void;
}

export const ChildRecordModal: React.FC<ChildRecordModalProps> = ({ open, entity, recordId, onClose }) => {
  const config = getRecordTemplate(entity);
  if (!config) return null;
  return (
    <RecordTemplateModal
      nested
      open={open}
      onClose={onClose}
      config={config}
      recordIds={[recordId]}
      index={0}
      onIndexChange={noopIndexChange}
    />
  );
};

interface ChildTarget {
  entity: string;
  recordId: string;
  /** The section to re-read on close; absent = nothing to re-read. */
  section: RecordSectionId | undefined;
  /** A new value per opening, so each opening is a fresh modal (a Fluent dialog is never re-keyed while open). */
  seq: number;
}

/**
 * The one child-record host of a record modal: the opener its sections get (`ChildRecordContext`) and the modal element to
 * render. `refreshSection` re-reads the section the child was opened from when the child closes.
 */
export function useChildRecordHost(refreshSection: (section: RecordSectionId) => void): {
  open: OpenChildRecord;
  modal: React.ReactNode;
} {
  const dialog = useChildDialog();
  const [target, setTarget] = React.useState<ChildTarget | null>(null);
  const targetRef = React.useRef<ChildTarget | null>(null);
  targetRef.current = target;
  const { show, hide } = dialog;

  const open = React.useCallback<OpenChildRecord>(
    (entity, recordId, trigger, section) => {
      if (!getRecordTemplate(entity)) return;
      // Some browsers do not focus a button on click; focusing it first makes it the control focus returns to.
      trigger?.focus();
      setTarget(prev => ({ entity, recordId, section, seq: (prev?.seq ?? 0) + 1 }));
      show();
    },
    [show]
  );

  const close = React.useCallback((): void => {
    hide();
    const shown = targetRef.current;
    if (!shown) return;
    if (shown.section) refreshSection(shown.section);
    refocusChildRecordRow(childRecordKeyOf(shown.entity, shown.recordId));
  }, [hide, refreshSection]);

  // Kept mounted (closed) after the first opening: a Fluent dialog unmounted while open leaves the page aria-hidden (#2658).
  const modal = target ? (
    <ChildRecordModal
      key={target.seq}
      open={dialog.open}
      entity={target.entity}
      recordId={target.recordId}
      onClose={close}
    />
  ) : null;
  return { open, modal };
}
