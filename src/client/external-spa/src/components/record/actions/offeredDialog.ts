/**
 * useOfferedDialog — the arrangement shared by the action hosts whose toolbar action opens a child form dialog
 * (spaarke-SPA-external-access-platform-r4 task 015: Edit and Change status).
 *
 * While `offered`, the host offers its action to the record's toolbar (hide, don't disable); a click remembers a SNAPSHOT of
 * what the dialog needs (`snapshot()`, read at click time) and opens the dialog, a React descendant of the record modal
 * (`useChildDialog`: focus returns to the toolbar button on close). The dialog is rendered from that snapshot, not from the
 * live record, so it survives the record being re-read behind it (a 403 re-read withdraws the action, but the dialog keeps
 * its refusal line until the user closes it). Each opening has its own `seq`, so each starts from a clean form.
 */
import * as React from 'react';
import { useChildDialog } from '../../createAction';
import type { RecordHostAction } from '../recordTemplate.types';

export function useOfferedDialog<T>(
  offered: boolean,
  onActionChange: (action: RecordHostAction | null) => void,
  snapshot: () => T
): { open: boolean; shown: { seq: number; value: T } | null; hide: () => void } {
  const dialog = useChildDialog();
  const [shown, setShown] = React.useState<{ seq: number; value: T } | null>(null);
  // The click reads the latest snapshot and `show` through a ref, so the offered action stays stable across renders.
  const latest = React.useRef({ show: dialog.show, snapshot });
  latest.current = { show: dialog.show, snapshot };

  React.useEffect(() => {
    if (!offered) {
      onActionChange(null);
      return;
    }
    onActionChange({
      visible: true,
      onClick: () => {
        setShown(prev => ({ seq: (prev?.seq ?? 0) + 1, value: latest.current.snapshot() }));
        latest.current.show();
      },
    });
    return () => onActionChange(null);
  }, [offered, onActionChange]);

  return { open: dialog.open, shown, hide: dialog.hide };
}
