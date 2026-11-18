/**
 * useAssignablePeople — the "Assigned To" options of the Add event and Edit event dialogs
 * (spaarke-SPA-external-access-platform-r4 task 015, FR-37).
 *
 * Read from `GET /{root}/{id}/assignable-people` when the dialog opens (`enabled`), never before: the BFF answers 403 to a
 * caller who may neither create nor edit, and the SPA asks only when it offers the dialog. The options are exactly the answer,
 * "Me" (the `isMe` row) first, the rest in the BFF's order.
 *
 * A 403 is NOT a failure: a workforce role that may create events but may not read contacts gets 403 here, and for that caller
 * nobody is assignable. It is `ready` with no people, so the dialog hides the field and says nothing. Any other failed read
 * is `failed`: the dialog hides the field, says so in one short line, and never blocks the save.
 */
import * as React from 'react';
import { getAssignablePeople, type AssignablePerson, type EventsRoot } from '../../api/web-api-client';
import { ApiError } from '../../types';

export type AssignablePeopleState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'ready'; people: AssignablePerson[] }
  | { status: 'failed' };

/** "Me" first, then the others in the order given. */
export function meFirst(people: readonly AssignablePerson[]): AssignablePerson[] {
  return [...people.filter(p => p.isMe), ...people.filter(p => !p.isMe)];
}

export function useAssignablePeople(
  root: EventsRoot | null,
  recordId: string,
  enabled: boolean,
  /** A new value per opening of the dialog, so each opening asks again (the people on a record change). */
  opening = 0
): AssignablePeopleState {
  const [state, setState] = React.useState<{ key: string; value: AssignablePeopleState } | null>(null);
  const key = `${root ?? ''}:${recordId}:${opening}`;

  React.useEffect(() => {
    if (!enabled || !root || !recordId) return;
    let cancelled = false;
    getAssignablePeople(root, recordId).then(
      people => {
        if (!cancelled) setState({ key, value: { status: 'ready', people: meFirst(people) } });
      },
      (err: unknown) => {
        if (cancelled) return;
        const notReadable = err instanceof ApiError && err.statusCode === 403;
        setState({ key, value: notReadable ? { status: 'ready', people: [] } : { status: 'failed' } });
      }
    );
    return () => {
      cancelled = true;
    };
  }, [enabled, root, recordId, key]);

  if (!enabled || !root || !recordId) return { status: 'idle' };
  return state && state.key === key ? state.value : { status: 'loading' };
}
