/**
 * ConversationModal — the open/expand affordance's destination (task 030 / FR-13).
 *
 * A proprietary Fluent v9 modal (Family 2 per docs/standards/
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
 * ── ENVELOPE (spaarke-modal-system P5, task 070 / FR-16) ──────────────────────
 * This modal now composes the canonical `<SprkModal size="md">` shell (imported
 * from `@spaarke/ui-components/pcf-safe`) INSTEAD of a hand-rolled
 * `createPortal` + `position:fixed` overlay. The hand-roll existed BECAUSE an
 * earlier round hit a centering bug: a CSS `transform` on an app-shell ancestor
 * (`html`/`body`/a wrapper) redefines the containing block for `position:fixed`,
 * so a raw fixed overlay top-anchored inside the model-driven form. The shell's
 * Fluent `Dialog` portal mounts ABOVE the transformed ancestor (design §4.6 /
 * FR-08), so it centers correctly where the raw overlay did not — this PCF is the
 * VALIDATION of that transform-robust invariant (structural proof in
 * `__tests__/ConversationModal.transform.test.tsx`).
 *
 * What the shell now owns (all previously hand-rolled here): the dimmed backdrop,
 * the centered fixed-size surface, the "Messages" header, the maximize/restore + ×
 * window controls (`ModalWindowControls`, composed INSIDE the shell — no longer
 * imported by this file), Esc + backdrop dismiss (`dismiss="light"`), the focus
 * trap, and the native thin-scrollbar body. `padded={false}` lets the two-pane
 * workspace fill the body edge-to-edge and manage its own internal scroll.
 * Maximize replaces the old `expanded` toggle — the shell's `full` size (a
 * viewport-fill) supersedes the old 96vw×94vh "expand".
 *
 * Theme: the shell renders NO `FluentProvider`; it inherits the host provider
 * (`CommunicationConversationPanelHost`'s light/dark-aware
 * `resolveThemeWithUserPreference`) and — being a React descendant of it — the
 * Fluent Dialog portal is themed via `applyStylesToPortals` (same document;
 * fluent-v9-portal-gotcha). This also RETIRES the hand-roll's hard-coded
 * `webLightTheme`, which had pinned the modal to light regardless of the host
 * theme (ADR-021 dark parity / NFR-03).
 *
 * `<ConversationView/>`'s FR-12 header seam is wired: `title` (the selected
 * thread's name), `regarding` (the host record), and `onOpenRecord` — the last
 * delegated up to the host, which opens the record via the sanctioned OOB
 * `Xrm.Navigation.navigateTo` modal (Layout 1). All BFF reads flow through the
 * injected `authenticatedFetch` (ADR-028 — passed through as a FUNCTION, never
 * snapshotted). Fluent v9 semantic tokens only (ADR-021).
 */

import * as React from 'react';
import { makeStyles } from '@fluentui/react-components';
import {
  ConversationWorkspace,
  ConversationView,
  createXrmNavigationService,
  type ConversationWorkspaceProps,
  type ConversationViewProps,
  type IConversationRendererProps,
  type AuthenticatedFetchFn,
} from '@spaarke/ui-components';
// PCF deep-import of the PCF-safe entry (ADR-012 PCF import pattern): webpack
// node-resolves `dist/pcf-safe.js`; the `/dist/` form matches this control's
// existing `@spaarke/ui-components/dist/utils/themeStorage` import. (The
// pcf-safe.ts header's `src/pcf-safe` form is for source-consuming PCFs; this one
// consumes the built dist per its tsconfig `@spaarke/ui-components/*` → `dist/*`.)
import { SprkModal, type SprkModalProps } from '@spaarke/ui-components/dist/pcf-safe';

// React 16 type seam — cast the shared components at the boundary (the shared lib
// carries @types/react 19; importing its emitted types into this React-16 PCF
// otherwise trips the ADR-022 ReactNode drift). Same pattern as the sibling
// CommunicationTimelineRegarding control. Runtime is unaffected.
const ConversationWorkspaceR16 = ConversationWorkspace as unknown as React.ComponentType<ConversationWorkspaceProps>;
const ConversationViewR16 = ConversationView as unknown as React.ComponentType<ConversationViewProps>;
// The canonical modal shell, re-cast at the same React-16 boundary. `children` is
// retyped to this control's React-16 `ReactNode` so the workspace element assigns
// cleanly (the shared lib's props carry @types/react 19 ReactNode — ADR-022 drift).
const SprkModalR16 = SprkModal as unknown as React.ComponentType<
  Omit<SprkModalProps, 'children'> & { children?: React.ReactNode }
>;

const useStyles = makeStyles({
  // The two-pane ConversationWorkspace fills the shell body (padded={false}) and
  // owns its own internal scrolling. `height:100%` resolves against the shell's
  // definite `md` height; flex lays the two panes out horizontally. Layout only —
  // no color/spacing literals (ADR-021 / NFR-03).
  workspaceHost: { display: 'flex', minHeight: 0, minWidth: 0, height: '100%', width: '100%' },
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
  /** Thread to open on first load (round-8.4 item 8) — set when the modal is opened by double-clicking a message row. */
  initialThreadId?: string;
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
  initialThreadId,
}) => {
  const s = useStyles();

  // Record-lookup service for the shell's built-in New-conversation modal (round
  // 4 PCF item 3 — the ＋ was inert because the PCF host never wired it). The
  // create modal opens record-scoped (regarding = the host record, locked).
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  const renderConversation = React.useCallback(
    (props: IConversationRendererProps) => (
      <ConversationViewR16
        threadId={props.threadId}
        authenticatedFetch={props.authenticatedFetch}
        bffBaseUrl={props.bffBaseUrl}
        currentUserSystemUserId={currentUserSystemUserId}
        // Prefer the shell's live thread name (updates after a rename); fall back to the record-read map (round-8.4).
        title={props.threadName ?? threadNames[props.threadId]}
        // Inline rename (round-8.4): pencil in the header renames the thread; shell refreshes the list.
        onThreadRenamed={props.onThreadRenamed}
        regarding={{ entityType, id }}
        onOpenRecord={onOpenRecord}
        // An email is a sprk_communication row (TimelineMessage.id IS the
        // sprk_communication GUID) — open it via the SAME record-modal path as
        // the regarding record (§B UAT 2026-07-27 item 3).
        onOpenEmail={msg => onOpenRecord('sprk_communication', msg.id)}
      />
    ),
    [currentUserSystemUserId, threadNames, entityType, id, onOpenRecord]
    // Cast bridges the React-16-vs-newer `ReactNode` seam between this control's
    // React 16 types and the shared lib's emitted .d.ts (same rationale as the
    // component casts above). Runtime is unaffected.
  ) as unknown as ConversationWorkspaceProps['renderConversation'];

  // The shell owns the envelope, header ("Messages"), maximize/restore + ×,
  // Esc/backdrop dismiss (`light`), focus trap, and the native thin-scrollbar
  // body. Centering survives a transformed ancestor because the Fluent Dialog
  // portal mounts above it (FR-08). `padded={false}` → the workspace fills the
  // body edge-to-edge and manages its own scrolling.
  return (
    <SprkModalR16 open={open} onClose={onClose} title="Messages" size="md" dismiss="light" padded={false}>
      <div className={s.workspaceHost}>
        <ConversationWorkspaceR16
          authenticatedFetch={authenticatedFetch}
          bffBaseUrl={bffBaseUrl}
          regarding={{ entityType, id }}
          renderConversation={renderConversation}
          navigationService={navigationService}
          initialThreadId={initialThreadId}
        />
      </div>
    </SprkModalR16>
  );
};
