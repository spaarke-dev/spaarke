/**
 * EventDeleteAction — the Event record's toolbar "Delete event" (spaarke-SPA-external-access-platform-r4 task 015, FR-36;
 * the rule is the Events list's own, task 054).
 *
 * OFFERED (hide, don't disable) only for an event the caller CREATED (`isCreatedByCaller`, read from the creator stamps of the
 * event the BFF returned). The server re-checks every delete (creator, and Read on every parent) and answers a uniform 403
 * when this guess is wrong; that refusal is shown, never hidden. Asks first (`DeleteOwnConfirm`); a 204 shows the record as
 * deleted and tells its opener (`onRecordDeleted`), so the Events list behind it re-reads when the record closes. The delete
 * in flight holds the record (`onBusyChange`).
 */
import * as React from 'react';
import { deleteOwnEvent, type ODataEvent } from '../../../api/web-api-client';
import { isCreatedByCaller } from '../../../api/delete-own';
import { DeleteFailureNotice, DeleteOwnConfirm, useDeleteOwn } from '../../DeleteOwn';
import { eventOf } from '../../events/eventDetail';
import type { RecordActionHostContext } from '../recordTemplate.types';

export const EventDeleteActionHost: React.FC<RecordActionHostContext> = ({
  record,
  caller,
  onActionChange,
  onBusyChange,
  onRecordDeleted,
}) => {
  const event = eventOf(record);
  const offered = event !== null && isCreatedByCaller(event, caller);

  const del = useDeleteOwn<ODataEvent>({
    noun: 'event',
    remove: e => deleteOwnEvent(e.sprk_eventid),
    onRemoved: () => onRecordDeleted(),
  });

  // The click reads the event and `ask` through a ref, so the offered action stays stable across renders.
  const latest = React.useRef({ event, ask: del.ask });
  latest.current = { event, ask: del.ask };
  React.useEffect(() => {
    if (!offered) {
      onActionChange(null);
      return;
    }
    onActionChange({
      visible: true,
      tooltip: 'Delete event',
      onClick: () => {
        const current = latest.current.event;
        if (current) latest.current.ask(current, current.sprk_name);
      },
    });
    return () => onActionChange(null);
  }, [offered, onActionChange]);

  React.useEffect(() => {
    onBusyChange(del.busy);
  }, [del.busy, onBusyChange]);
  React.useEffect(() => () => onBusyChange(false), [onBusyChange]);

  return (
    <>
      <DeleteFailureNotice del={del} />
      <DeleteOwnConfirm del={del} />
    </>
  );
};
