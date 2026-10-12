/**
 * DeleteOwn — the one confirm-then-delete flow shared by the documents, to-do and event lists
 * (spaarke-SPA-external-access-platform-r3 task 054, FR-26).
 *
 *   const del = useDeleteOwn({ noun: 'document', remove: item => deleteOwnDocument(item.id), onRemoved: ... });
 *   <Button onClick={() => del.ask(item, 'Name')} />            // a row's Delete action
 *   <DeleteOwnConfirm del={del} />                              // the confirmation (shared ConfirmModal, ADR-050)
 *   <DeleteFailureNotice del={del} />                           // why a delete did not happen
 *
 * The confirmation is the shared `ConfirmModal` (`@spaarke/ui-components`): no light-dismiss, Cancel on the left,
 * a destructive Confirm that is busy while the call is in flight. Cancel and the x do nothing but close it.
 *
 * On success the row is removed through `onRemoved` (no refetch: the server answered 204, which is the end state). On a
 * failure the dialog closes, the ROW STAYS, and the failure is shown (`deleteFailureOf`): a 403 as an error, never as a
 * hidden row - the server is the authority, and this component only offered the action as a convenience.
 *
 * ADR-021: no colours of its own; the danger styling is the preset's token class.
 */
import * as React from 'react';
import { MessageBar, MessageBarActions, MessageBarBody, Button } from '@fluentui/react-components';
import { DismissRegular } from '@fluentui/react-icons';
import { ConfirmModal } from '@spaarke/ui-components/components/SprkModal';
import { deleteFailureOf, type DeleteFailure } from '../api/delete-own';
import { BffAccessState } from './auth/BffAccessState';

export interface DeleteOwnState<T> {
  /** The row waiting for confirmation, with the name shown in the dialog; null when no dialog is open. */
  pending: { item: T; label: string } | null;
  /** The delete call is in flight. */
  busy: boolean;
  /** Why the last delete did not happen; null when none (cleared by the next `ask` or by `dismissFailure`). */
  failure: DeleteFailure | null;
  noun: string;
  /** A row's Delete action: opens the confirmation. A second ask while a delete is in flight is ignored. */
  ask: (item: T, label: string) => void;
  /** Cancel / x: closes the confirmation and does nothing else. */
  cancel: () => void;
  /** The dialog's Delete: runs the delete once. */
  confirm: () => void;
  dismissFailure: () => void;
}

export interface UseDeleteOwnArgs<T> {
  /** The row type in user-facing text ("document", "task", "event"). */
  noun: string;
  /** The delete call; it throws an `ApiError` on any non-2xx. */
  remove: (item: T) => Promise<void>;
  /** The server said 204: take the row off the screen. */
  onRemoved: (item: T) => void;
}

export function useDeleteOwn<T>({ noun, remove, onRemoved }: UseDeleteOwnArgs<T>): DeleteOwnState<T> {
  const [pending, setPending] = React.useState<{ item: T; label: string } | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [failure, setFailure] = React.useState<DeleteFailure | null>(null);
  // Same-tick lock: a double-click must send one DELETE, before React has re-rendered the busy state.
  const inFlight = React.useRef(false);
  const mounted = React.useRef(true);
  React.useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const ask = React.useCallback((item: T, label: string) => {
    if (inFlight.current) return;
    setFailure(null);
    setPending({ item, label });
  }, []);

  const cancel = React.useCallback(() => {
    if (inFlight.current) return;
    setPending(null);
  }, []);

  const confirm = React.useCallback(() => {
    if (inFlight.current || !pending) return;
    inFlight.current = true;
    setBusy(true);
    const { item } = pending;
    remove(item).then(
      () => {
        inFlight.current = false;
        if (!mounted.current) return;
        setBusy(false);
        setPending(null);
        onRemoved(item);
      },
      (err: unknown) => {
        inFlight.current = false;
        if (!mounted.current) return;
        setBusy(false);
        setPending(null);
        setFailure(deleteFailureOf(err, noun));
      }
    );
  }, [pending, remove, onRemoved, noun]);

  const dismissFailure = React.useCallback(() => setFailure(null), []);

  return { pending, busy, failure, noun, ask, cancel, confirm, dismissFailure };
}

/** The confirmation dialog for a pending delete (shared modal system, ADR-050). */
export function DeleteOwnConfirm<T>({ del }: { del: DeleteOwnState<T> }): React.ReactElement {
  const label = del.pending?.label ?? '';
  return (
    <ConfirmModal
      open={del.pending !== null}
      onClose={del.cancel}
      onConfirm={del.confirm}
      title={`Delete ${del.noun}`}
      message={`Delete "${label}"? This permanently removes it and cannot be undone.`}
      confirmLabel={del.busy ? 'Deleting...' : 'Delete'}
      destructive
      busy={del.busy}
    />
  );
}

/** Why the last delete did not happen: the sign-in state for a 401, else an error bar. The row it was for is still listed. */
export function DeleteFailureNotice<T>({ del }: { del: DeleteOwnState<T> }): React.ReactElement | null {
  if (!del.failure) return null;
  if (del.failure.kind === 'signin') return <BffAccessState failure="signin" />;
  return (
    <MessageBar intent="error" role="alert" data-testid="delete-own-error">
      <MessageBarBody>{del.failure.message}</MessageBarBody>
      <MessageBarActions
        containerAction={
          <Button
            appearance="transparent"
            aria-label="Dismiss"
            icon={<DismissRegular />}
            onClick={del.dismissFailure}
          />
        }
      />
    </MessageBar>
  );
}
