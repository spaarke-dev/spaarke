/**
 * ConversationModal — the open/expand affordance's destination (task 030 / FR-13).
 *
 * A proprietary Fluent v9 `<Dialog>` (Family 2 per docs/standards/
 * MODAL-DECISION-CRITERIA.md — it hosts our OWN conversation surface, not an
 * OOB Dataverse form) that MOUNTS the shared two-pane `<ConversationWorkspace/>`
 * in RECORD mode: `regarding={{ entityType, id }}` scopes the thread list to the
 * host record (server-access-filtered `by-regarding` — NO second regarding
 * mechanism, NFR-06 / ADR-024). The shell's `renderConversation` seam is wired
 * to the shared `<ConversationView/>`, so once the modal is open the shared
 * widget OWNS thread navigation — selecting another thread in the left pane
 * re-renders the right pane in place; the user never returns to the PCF to
 * switch threads.
 *
 * The shared widget is reused AS-IS — this file mounts it and passes props; it
 * reimplements no thread-list / bubbles / quick-view (NFR-06).
 *
 * `<ConversationView/>`'s FR-12 header seam is wired: `title` (the selected
 * thread's name), `regarding` (the host record), and `onOpenRecord` — the last
 * delegated up to the host, which opens the record via the sanctioned OOB
 * `Xrm.Navigation.navigateTo` modal (Layout 1). All BFF reads flow through the
 * injected `authenticatedFetch` (ADR-028). Fluent v9 semantic tokens only —
 * dark mode passes through the host `FluentProvider` (ADR-021).
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  Button,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { DismissRegular } from '@fluentui/react-icons';
import {
  ConversationWorkspace,
  ConversationView,
  type ConversationWorkspaceProps,
  type ConversationViewProps,
  type IConversationRendererProps,
  type AuthenticatedFetchFn,
} from '@spaarke/ui-components';

// React 16 type seam — cast the shared components at the boundary (same pattern
// as the sibling CommunicationTimelineRegarding control). Runtime is unaffected.
const ConversationWorkspaceR16 = ConversationWorkspace as unknown as React.ComponentType<ConversationWorkspaceProps>;
const ConversationViewR16 = ConversationView as unknown as React.ComponentType<ConversationViewProps>;

const useStyles = makeStyles({
  // "Our modal" size (round 3 items 13-15): 1040 × 72vh, centered (matches the
  // shared NewThreadModal). 72vh (vs 85vh) leaves headroom so the modal reads as
  // centered rather than top-anchored inside the Dataverse form host.
  surface: {
    width: 'min(1040px, 95vw)',
    maxWidth: 'min(1040px, 95vw)',
    height: '72vh',
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    // Anchor point for the absolutely-positioned close button (§B1).
    position: 'relative',
  },
  body: { height: '100%', display: 'flex', flexDirection: 'column', minHeight: 0 },
  title: {
    paddingInline: tokens.spacingHorizontalL,
    // Leave room so the "Messages" title never runs under the corner close button.
    paddingInlineEnd: tokens.spacingHorizontalXXL,
    paddingBlock: tokens.spacingVerticalM,
    margin: 0,
  },
  // §B1 (UAT): the close "x" pinned to the modal's upper-right corner —
  // independent of the title row's own layout/padding, so it reads
  // unambiguously as "the corner", not just "the right end of a padded row".
  closeButton: {
    position: 'absolute',
    top: tokens.spacingVerticalM,
    right: tokens.spacingHorizontalM,
    zIndex: 1,
  },
  content: { flex: 1, minHeight: 0, padding: 0, display: 'flex', flexDirection: 'column' },
  workspaceHost: { flex: 1, minHeight: 0, minWidth: 0, display: 'flex' },
});

export interface IConversationModalProps {
  open: boolean;
  onClose: () => void;
  entityType: string;
  id: string;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl?: string;
  /** The caller's `systemuserid` — drives mine/others bubble alignment (FR-02/FR-18). */
  currentUserSystemUserId: string;
  /** threadId → display name, from the record read; supplies `<ConversationView/>`'s FR-12 title. */
  threadNames: Record<string, string>;
  /** Host-provided OOB record open (Layout 1). The host closes this modal first, then navigates. */
  onOpenRecord: (entityType: string, id: string) => void;
}

export const ConversationModal: React.FC<IConversationModalProps> = ({
  open,
  onClose,
  entityType,
  id,
  authenticatedFetch,
  bffBaseUrl,
  currentUserSystemUserId,
  threadNames,
  onOpenRecord,
}) => {
  const s = useStyles();

  const renderConversation = React.useCallback(
    (props: IConversationRendererProps) => (
      <ConversationViewR16
        threadId={props.threadId}
        authenticatedFetch={props.authenticatedFetch}
        bffBaseUrl={props.bffBaseUrl}
        currentUserSystemUserId={currentUserSystemUserId}
        title={threadNames[props.threadId]}
        regarding={{ entityType, id }}
        onOpenRecord={onOpenRecord}
      />
    ),
    [currentUserSystemUserId, threadNames, entityType, id, onOpenRecord]
    // Cast bridges the React-16-vs-newer `ReactNode` seam between this control's
    // React 16 types and the shared lib's emitted .d.ts (same rationale as the
    // component casts above). Runtime is unaffected.
  ) as unknown as ConversationWorkspaceProps['renderConversation'];

  return (
    <Dialog open={open} onOpenChange={(_ev, data) => (!data.open ? onClose() : undefined)} modalType="modal">
      <DialogSurface className={s.surface}>
        {/* §B1 (UAT): moved out of DialogTitle's inline action slot so it pins to the
            surface's literal upper-right corner regardless of title row padding. */}
        <Button
          appearance="subtle"
          className={s.closeButton}
          aria-label="Close conversations"
          icon={<DismissRegular />}
          onClick={onClose}
        />
        <DialogBody className={s.body}>
          {/* §B2 (UAT): modal title = "Messages". */}
          <DialogTitle className={s.title}>Messages</DialogTitle>
          <DialogContent className={s.content}>
            <div className={s.workspaceHost}>
              <ConversationWorkspaceR16
                authenticatedFetch={authenticatedFetch}
                bffBaseUrl={bffBaseUrl}
                regarding={{ entityType, id }}
                renderConversation={renderConversation}
              />
            </div>
          </DialogContent>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};
