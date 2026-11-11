/**
 * RecordOpener - the one in-app way to open a single record's detail view from somewhere other than a grid row
 * (spaarke-SPA-external-access-platform-r3 task 055; FR-07 deep link, FR-08 notification bell). Since R4 task 005 the
 * view is the record template (`RecordTemplateModal`, the type's `getRecordTemplate` config); an entity with no template
 * opens nothing (its caller is told the opening ended, through `onTerminal`).
 *
 * The grid opens the same modal itself, with the grid's loaded row ids for "1 of N" browsing (`GridWidgetBody`). A
 * notification or a link names ONE record, so this provider owns the same modal for that case: `openRecord(target)` shows it
 * for the record, and closing it returns focus to the control that opened it. It is the seam both the bell and the deep
 * link go through, so a record opens the same way (and is denied the same way) whichever surface asked.
 *
 * The target is an entity name and a GUID (`record-targets.ts`); the modal reads the record through the caller's own
 * external-plane token, so a record the caller cannot see shows the modal's existing "You don't have access" state.
 *
 * ADR-021: renders no markup of its own beyond the modal; Fluent v9 tokens apply inside it.
 */
import * as React from 'react';
import { RecordTemplateModal } from '../record/RecordTemplateModal';
import { getRecordTemplate } from '../record/recordTemplates';
import type { RecordTemplateConfig } from '../record/recordTemplate.types';
import type { RecordTarget } from './record-targets';

export interface OpenRecordOptions {
  /** Focus returns here when the detail view closes (the control that opened it). */
  returnFocusTo?: HTMLElement | null;
  /**
   * Called ONCE, when the opened record reaches a terminal state: loaded, denied or not found (a still-loading or failed-
   * to-load record is not terminal), or the view is closed or replaced by another record, whichever comes first.
   */
  onTerminal?: () => void;
}

export type OpenRecord = (target: RecordTarget, options?: OpenRecordOptions) => void;

/** Outside a provider there is nothing to show the record in, so opening is a no-op (a bell in a test, say). */
const RecordOpenerContext = React.createContext<OpenRecord>(() => undefined);

/** The opener for the surrounding {@link RecordOpenerProvider}. */
export function useOpenRecord(): OpenRecord {
  return React.useContext(RecordOpenerContext);
}

const noopIndexChange = (): void => undefined;

export const RecordOpenerProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  // The last record shown, kept after the modal closes (#2658). A Fluent Dialog unmounted while open, or re-keyed while
  // open, leaves the page aria-hidden for good. So the modal closes with `open={false}`, `seq` keys each opening and
  // changes only while closed, and a record opened over an open one replaces it through props (the modal reads by id).
  const [shown, setShown] = React.useState<{
    target: RecordTarget;
    config: RecordTemplateConfig;
    open: boolean;
    seq: number;
  } | null>(null);
  const returnFocusRef = React.useRef<HTMLElement | null>(null);
  const terminalRef = React.useRef<(() => void) | null>(null);

  /** Runs the pending terminal callback, once. */
  const fireTerminal = React.useCallback(() => {
    const callback = terminalRef.current;
    terminalRef.current = null;
    callback?.();
  }, []);

  const openRecord = React.useCallback<OpenRecord>(
    (next, options) => {
      const config = getRecordTemplate(next.entityName);
      // A type with no record view opens nothing; the opening is over, so its caller is told at once.
      if (!config) {
        options?.onTerminal?.();
        return;
      }
      // A record that is replaced before it settled ends that opening: its caller is told (nothing is pending any more).
      fireTerminal();
      returnFocusRef.current = options?.returnFocusTo ?? null;
      terminalRef.current = options?.onTerminal ?? null;
      setShown(prev =>
        prev?.open ? { ...prev, target: next, config } : { target: next, config, open: true, seq: (prev?.seq ?? 0) + 1 }
      );
    },
    [fireTerminal]
  );

  const handleClose = React.useCallback(() => {
    fireTerminal();
    setShown(prev => (prev ? { ...prev, open: false } : prev));
    const el = returnFocusRef.current;
    returnFocusRef.current = null;
    // After the modal has closed, so its own focus handling cannot take focus back.
    if (el) {
      window.setTimeout(() => {
        if (el.isConnected) el.focus();
      }, 0);
    }
  }, [fireTerminal]);

  return (
    <RecordOpenerContext.Provider value={openRecord}>
      {children}
      {shown && (
        <RecordTemplateModal
          key={shown.seq}
          open={shown.open}
          onClose={handleClose}
          onSettled={fireTerminal}
          config={shown.config}
          recordIds={[shown.target.recordId]}
          index={0}
          onIndexChange={noopIndexChange}
        />
      )}
    </RecordOpenerContext.Provider>
  );
};

export default RecordOpenerProvider;
