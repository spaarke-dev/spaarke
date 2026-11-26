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
 * REFRESH (task 034, FR-21). The thread offers its own Refresh (`onRefreshActionChange`), shown in this card's header ahead of
 * New message: it re-reads the thread only (the draft and the other sections are left alone), whatever the caller's access.
 * The new-message indicator the thread raises is drawn under the thread (`RecordMessages.tsx`).
 *
 * WHO MAY POST (task 006, FR-06 / FR-07). The thread offers New message only when its read answered `canCreate: true`;
 * otherwise the card shows the template's read-only line (`readOnlyNote`, from `recordAccess.ts`).
 */
import * as React from 'react';
import type { IHeaderToolbarSlot } from '@spaarke/ui-components/components/HeaderToolbar';
import { RECORD_ACTION_ICONS } from '@spaarke/ui-components/components/RecordCommandBar';
import { messageRootFor } from '../../../api/messages-client';
import { RecordMessages, type ThreadRefreshAction } from '../../detail/RecordMessages';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordSectionCard } from './RecordSectionCard';

/** The section's own Refresh (task 034): the thread only, so a draft and the other sections are left alone. */
const REFRESH_TOOLTIP = 'Refresh messages';

export const MessagesSection: React.FC<RecordSectionContext> = ({
  detail,
  recordId,
  onBusyChange,
  refreshSignal,
  offerAction,
  headerActions,
  readOnlyNote,
}) => {
  // The thread offers its Refresh once it is shown (and withdraws it when it is not): a read action, so it is shown at every
  // access level and is not in the config's `sectionActions` (those are create actions gated by rights).
  const [refreshAction, setRefreshAction] = React.useState<ThreadRefreshAction | null>(null);
  // A document has no thread (the server answers 404 for it): no section and no read.
  if (!messageRootFor(detail.entity)) return null;
  const RefreshIcon = RECORD_ACTION_ICONS.refresh;
  const actions: IHeaderToolbarSlot[] = refreshAction
    ? [
        {
          key: 'refresh',
          icon: <RefreshIcon />,
          tooltip: REFRESH_TOOLTIP,
          // Never `disabled` while busy: disabling the button just pressed would drop a keyboard user's focus (review fix
          // round 1, F1). The thread's own in-flight guard (`readInFlight`) ignores a second activation.
          onClick: refreshAction.onClick,
        },
        ...headerActions,
      ]
    : headerActions;
  return (
    <RecordSectionCard id="messages" actions={actions} note={readOnlyNote}>
      <RecordMessages
        entity={detail.entity}
        recordId={recordId}
        onSendingChange={onBusyChange}
        refreshSignal={refreshSignal}
        embedded
        onCreateActionChange={offerAction('newMessage')}
        onRefreshActionChange={setRefreshAction}
      />
    </RecordSectionCard>
  );
};
