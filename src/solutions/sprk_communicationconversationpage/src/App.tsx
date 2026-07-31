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
  createXrmNavigationService,
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

  // Record-lookup service for the shell's built-in New-conversation modal (item 9
  // — name + associate-to-record picker). Same shared Xrm-backed navigation
  // adapter the widget host uses (createXrmNavigationService → Xrm lookup).
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  return (
    <div className={styles.root}>
      <ConversationWorkspace
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={bffBaseUrl}
        navigationService={navigationService}
        renderConversation={({
          threadId,
          threadName,
          authenticatedFetch: threadFetch,
          bffBaseUrl: threadBffBaseUrl,
          onMarkThreadRead,
          onThreadRenamed,
        }) => (
          <ConversationView
            threadId={threadId}
            authenticatedFetch={threadFetch}
            bffBaseUrl={threadBffBaseUrl}
            currentUserSystemUserId={currentUserSystemUserId}
            onMarkThreadRead={onMarkThreadRead}
            // Thread name in the message-pane header (round-8.4 item 3b).
            title={threadName}
            // Inline rename (round-8.4): pencil in the header renames the thread.
            onThreadRenamed={onThreadRenamed}
          />
        )}
      />
    </div>
  );
};

App.displayName = 'App';
