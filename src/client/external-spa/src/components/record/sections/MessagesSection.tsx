/**
 * Messages — the record's thread and compose box (R3 task 051 `RecordMessages`, wired as in R3's record-detail modal; task 005).
 *
 * GET/POST /api/v1/external/{root}/{id}/messages for project, matter, work assignment, invoice and service request. A send
 * in flight is reported as busy (`onBusyChange`), which holds browsing, Close and Refresh until it settles (review 051 K3).
 * `refreshSignal` re-reads the thread without losing the draft: Refresh, and the service request recall post.
 *
 * NEW MESSAGE (task 004, FR-03; task 009). When the record's config lists New message, the button in this card's header is
 * the entry point: the thread offers it through `offerAction('newMessage')` (available exactly when its inline compose box
 * would have been shown, which it then does not draw), the template hands it back as `headerActions`, and the thread
 * renders the compose box in a `FormModal` inside this section, so the dialog is a descendant of the record modal.
 *
 * WHO MAY POST (task 006, FR-06 / FR-07). The thread offers New message only when its read answered `canCreate: true`;
 * otherwise the card shows the template's read-only line (`readOnlyNote`, from `recordAccess.ts`).
 */
import * as React from 'react';
import { messageRootFor } from '../../../api/messages-client';
import { RecordMessages } from '../../detail/RecordMessages';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordSectionCard } from './RecordSectionCard';

export const MessagesSection: React.FC<RecordSectionContext> = ({
  detail,
  recordId,
  onBusyChange,
  refreshSignal,
  offerAction,
  headerActions,
  readOnlyNote,
}) => {
  // A document has no thread (the server answers 404 for it): no section and no read.
  if (!messageRootFor(detail.entity)) return null;
  return (
    <RecordSectionCard id="messages" actions={headerActions} note={readOnlyNote}>
      <RecordMessages
        entity={detail.entity}
        recordId={recordId}
        onSendingChange={onBusyChange}
        refreshSignal={refreshSignal}
        embedded
        onCreateActionChange={offerAction('newMessage')}
      />
    </RecordSectionCard>
  );
};
