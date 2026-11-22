/**
 * #2908: answer "+ New Message" from the communications grid on the same form. The grid asks through
 * `openConversationPanel` (`@spaarke/ui-components`); this hook is the panel's side of that request.
 *
 * The panel takes a request only when it can open the Messages modal. Taking it tells the grid "opened", so a panel
 * that can never open it (no regarding context for this table, or auth failed) leaves it untaken and the grid tells
 * the user. A request taken while auth is still loading waits for it; if auth then fails, `onFailed` reports why
 * instead of leaving the click with no result.
 */
import * as React from 'react';
import { subscribeConversationPanelOpen } from '@spaarke/ui-components/dist/utils/conversationPanelLaunch';

export interface IUseConversationPanelOpenRequestOptions {
  /** The panel's own record. Requests for any other record are ignored. */
  host: { entityType: string; id: string } | undefined;
  /** False when the modal cannot open here at all (no regarding context, or auth failed): requests stay untaken. */
  canOpen: boolean;
  /** True once the modal can open now (auth and regarding context ready). A request taken earlier waits for this. */
  ready: boolean;
  /** Why opening became impossible after a request was taken (auth failed meanwhile); null otherwise. */
  failure: string | null;
  /** Opens the Messages modal. */
  onOpen: () => void;
  /** Tells the user a taken request cannot be met. */
  onFailed: (reason: string) => void;
}

export function useConversationPanelOpenRequest({
  host,
  canOpen,
  ready,
  failure,
  onOpen,
  onFailed,
}: IUseConversationPanelOpenRequestOptions): void {
  const [pending, setPending] = React.useState(false);
  const entityType = host?.entityType;
  const id = host?.id;

  React.useEffect(() => {
    if (!entityType || !id || !canOpen) return undefined;
    return subscribeConversationPanelOpen({ entityType, id }, () => setPending(true));
  }, [entityType, id, canOpen]);

  React.useEffect(() => {
    if (!pending) return;
    if (ready) {
      setPending(false);
      onOpen();
    } else if (failure) {
      setPending(false);
      onFailed(failure);
    }
  }, [pending, ready, failure, onOpen, onFailed]);
}
