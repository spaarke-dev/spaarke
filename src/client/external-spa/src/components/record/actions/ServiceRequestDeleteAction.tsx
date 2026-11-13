/**
 * ServiceRequestDeleteAction — the Service Request record's toolbar "Delete request" (R4 task 005, FR-05 / FR-07), wired
 * as R3's record-detail modal wired `ServiceRequestActions` (owner decisions 2026-10-11):
 *   - offered to the requester of an open request only (hidden otherwise);
 *   - 204: the record shows as deleted and the opener is told (`onRecordDeleted`), so its grid re-reads on close;
 *   - 409 `has_related_records`: the notice offers "Send recall request", a post to the request's own thread; a posted
 *     recall re-reads the Messages section (`refreshSection('messages')`) and withdraws Delete;
 *   - the recall post in flight holds browsing, Close and Refresh (`onBusyChange`).
 * The confirmation names the request as R3 did (`recordDetailTitle`: its name), not by the modal title.
 */
import * as React from 'react';
import { recordDetailTitle } from '../../detail/RecordDetailFields';
import { ServiceRequestActions, type ServiceRequestDeleteAction } from '../../detail/ServiceRequestActions';
import type { RecordActionHostContext } from '../recordTemplate.types';

export const ServiceRequestDeleteActionHost: React.FC<RecordActionHostContext> = ({
  detail,
  record,
  recordId,
  caller,
  onBusyChange,
  onRecordDeleted,
  refreshSection,
  onActionChange,
}) => {
  const handleActionChange = React.useCallback(
    (action: ServiceRequestDeleteAction) => onActionChange(action.visible ? action : null),
    [onActionChange]
  );
  const handleRecalled = React.useCallback(() => refreshSection('messages'), [refreshSection]);
  return (
    <ServiceRequestActions
      requestId={recordId}
      record={record}
      caller={caller}
      label={recordDetailTitle(detail, record)}
      onDeleted={onRecordDeleted}
      onRecalled={handleRecalled}
      onBusyChange={onBusyChange}
      onDeleteActionChange={handleActionChange}
    />
  );
};
