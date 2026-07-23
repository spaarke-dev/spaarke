/**
 * App.tsx — sprk_communicationconversationpage (task 032, FR-14b).
 *
 * Mounts the shared two-pane conversation shell FULL-PAGE in record-less /
 * workspace ("all mode") — no `regarding` prop, so every thread the caller
 * may see (including record-less Direct threads, FR-16) is listed. Mounts the
 * shared components verbatim (NFR-06 reuse-first — no reimplementation):
 *
 *   <ConversationWorkspace />  — left-pane thread list + selection (task 012)
 *   <ConversationView />       — right-pane Teams-style bubbles (task 011),
 *                                wired through the `renderConversation` seam
 *
 * All BFF reads/writes flow through the injected `authenticatedFetch`
 * (ADR-028) — this component never imports `@spaarke/auth` directly; auth
 * bootstrap lives entirely in `main.tsx` / `services/authInit.ts`.
 */
import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import {
  ConversationView,
  ConversationWorkspace,
  createXrmDataService,
  searchUsersAndContacts,
} from '@spaarke/ui-components';
import { authenticatedFetch } from './services/authInit';
import { getBffBaseUrl } from './config/runtimeConfig';
import { getUserId } from './services/xrmProvider';

const useStyles = makeStyles({
  root: {
    height: '100%',
    width: '100%',
    minHeight: 0,
    minWidth: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },
});

export const App: React.FC = () => {
  const styles = useStyles();

  // Resolved once at mount — stable for the page's lifetime (a full reload,
  // not an in-place user switch, is how a Dataverse web resource changes
  // callers). Feeds `<ConversationView />`'s `currentUserSystemUserId` — the
  // STRICT identity comparison FR-02/FR-18 requires for mine/others bubble
  // alignment (never an email-string comparison).
  const currentUserSystemUserId = React.useMemo(() => getUserId(), []);

  const bffBaseUrl = getBffBaseUrl();

  // Recipient directory search for the shell's built-in New-conversation modal
  // (R3 UAT 2026-07-22 item 5a). Same host-context Xrm.WebApi users+contacts
  // lookup the widget host and every Spaarke composer use (no BFF/OBO).
  const lookupDataService = React.useMemo(() => createXrmDataService(), []);
  const handleSearchRecipients = React.useCallback(
    (query: string) => searchUsersAndContacts(lookupDataService, query),
    [lookupDataService]
  );

  return (
    <div className={styles.root}>
      <ConversationWorkspace
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={bffBaseUrl}
        onSearchRecipients={handleSearchRecipients}
        renderConversation={({
          threadId,
          authenticatedFetch: threadFetch,
          bffBaseUrl: threadBffBaseUrl,
          onMarkThreadRead,
        }) => (
          <ConversationView
            threadId={threadId}
            authenticatedFetch={threadFetch}
            bffBaseUrl={threadBffBaseUrl}
            currentUserSystemUserId={currentUserSystemUserId}
            onMarkThreadRead={onMarkThreadRead}
          />
        )}
      />
    </div>
  );
};

App.displayName = 'App';
