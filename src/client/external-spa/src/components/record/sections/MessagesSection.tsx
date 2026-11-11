/**
 * Messages — the record's thread and compose box (R3 task 051 `RecordMessages`, wired as in R3's record-detail modal; task 005).
 *
 * GET/POST /api/v1/external/{root}/{id}/messages for project, matter, work assignment, invoice and service request. A send
 * in flight is reported as busy (`onBusyChange`), which holds browsing, Close and Refresh until it settles (review 051 K3).
 * `refreshSignal` re-reads the thread without losing the draft: Refresh, and the service request recall post.
 */
import * as React from 'react';
import { messageRootFor } from '../../../api/messages-client';
import { RecordMessages } from '../../detail/RecordMessages';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordSectionCard } from './RecordSectionCard';

export const MessagesSection: React.FC<RecordSectionContext> = ({ detail, recordId, onBusyChange, refreshSignal }) => {
  // A document has no thread (the server answers 404 for it): no section and no read.
  if (!messageRootFor(detail.entity)) return null;
  return (
    <RecordSectionCard id="messages">
      <RecordMessages
        entity={detail.entity}
        recordId={recordId}
        onSendingChange={onBusyChange}
        refreshSignal={refreshSignal}
        embedded
      />
    </RecordSectionCard>
  );
};
