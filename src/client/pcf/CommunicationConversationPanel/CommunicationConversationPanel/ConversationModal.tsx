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
 * ── CENTERING (R3 UAT round 5, 2026-07-23) ────────────────────────────────────
 * Earlier rounds used a Fluent `<Dialog modalType="modal">` and then tried to fix
 * its top-anchoring inside the model-driven form by portaling to `document.body`.
 * That did NOT hold: Fluent's `<Dialog>` already portals its `DialogSurface` to
 * `document.body`, and the top-anchoring is caused by a CSS `transform` on an
 * app-shell ancestor (`html`/`body`/a wrapper) that redefines the containing
 * block for `position:fixed` — which portaling to `body` cannot escape when the
 * transform sits AT/above `body`. The repo has no proven recipe to viewport-center
 * a fixed-size Fluent Dialog in that context.
 *
 * So we drop the Fluent Dialog envelope and use the transform-ROBUST pattern the
 * `DocumentRelationshipViewer` PCF already ships: a full-viewport
 * `position:fixed; inset:0` flex overlay that CENTERS the fixed-size surface. A
 * near-full-viewport overlay renders the same regardless of a residual ancestor
 * offset, so the surface reads as centered. We keep the `createPortal` to
 * `document.body` + a re-wrapped `FluentProvider` purely for THEMING the portaled
 * subtree (fluent-v9-portal-gotcha), and implement Esc/backdrop-dismiss ourselves
 * (the Fluent Dialog used to give us those).
 *
 * `<ConversationView/>`'s FR-12 header seam is wired: `title` (the selected
 * thread's name), `regarding` (the host record), and `onOpenRecord` — the last
 * delegated up to the host, which opens the record via the sanctioned OOB
 * `Xrm.Navigation.navigateTo` modal (Layout 1). All BFF reads flow through the
 * injected `authenticatedFetch` (ADR-028). Fluent v9 semantic tokens only —
 * dark mode passes through the re-wrapped `FluentProvider` (ADR-021).
 */

import * as React from 'react';
import * as ReactDOM from 'react-dom';
import { Button, FluentProvider, webLightTheme, makeStyles, tokens } from '@fluentui/react-components';
import { DismissRegular } from '@fluentui/react-icons';
import {
  ConversationWorkspace,
  ConversationView,
  createXrmNavigationService,
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
  // Full-viewport dimmed overlay that CENTERS the surface (round 5) — copied from
  // the DocumentRelationshipViewer PCF's transform-robust modal. `position:fixed;
  // inset:0` + flex-center means a transformed ancestor can no longer top-anchor
  // the surface: the overlay fills whatever box `fixed` resolves to and centers
  // its child within it. High z-index for Dataverse form stacking.
  overlay: {
    position: 'fixed',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    backgroundColor: tokens.colorBackgroundOverlay,
    zIndex: 10000,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  // "Our modal" size (round 3 items 13-15): 1040 × 72vh, centered by the overlay.
  // maxHeight caps it on short viewports so the footer/compose never clips.
  surface: {
    position: 'relative',
    width: 'min(1040px, 95vw)',
    maxWidth: 'min(1040px, 95vw)',
    height: '72vh',
    maxHeight: '90vh',
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusLarge,
    boxShadow: tokens.shadow64,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
  },
  title: {
    display: 'flex',
    alignItems: 'center',
    paddingInlineStart: tokens.spacingHorizontalL,
    // Leave room so the "Messages" title never runs under the corner close button.
    paddingInlineEnd: tokens.spacingHorizontalXXL,
    paddingBlock: tokens.spacingVerticalM,
    margin: 0,
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
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
  content: { flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' },
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

  // Record-lookup service for the shell's built-in New-conversation modal (round
  // 4 PCF item 3 — the ＋ was inert because the PCF host never wired it). The
  // create modal opens record-scoped (regarding = the host record, locked).
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  const surfaceRef = React.useRef<HTMLDivElement | null>(null);

  // Esc-to-dismiss (the Fluent Dialog gave us this for free before). Bound only
  // while open. Keyed on `open`/`onClose` so it re-binds if the handler changes.
  React.useEffect(() => {
    if (!open) return;
    const onKeyDown = (ev: KeyboardEvent) => {
      if (ev.key === 'Escape') {
        ev.stopPropagation();
        onClose();
      }
    };
    document.addEventListener('keydown', onKeyDown, true);
    return () => document.removeEventListener('keydown', onKeyDown, true);
  }, [open, onClose]);

  // Move focus into the surface when it opens so keyboard users land inside the
  // modal (crude focus management — the surface is focusable via tabIndex=-1).
  React.useEffect(() => {
    if (open) surfaceRef.current?.focus();
  }, [open]);

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
        // An email is a sprk_communication row (TimelineMessage.id IS the
        // sprk_communication GUID) — open it via the SAME record-modal path as
        // the regarding record (§B UAT 2026-07-27 item 3).
        onOpenEmail={(msg) => onOpenRecord('sprk_communication', msg.id)}
      />
    ),
    [currentUserSystemUserId, threadNames, entityType, id, onOpenRecord]
    // Cast bridges the React-16-vs-newer `ReactNode` seam between this control's
    // React 16 types and the shared lib's emitted .d.ts (same rationale as the
    // component casts above). Runtime is unaffected.
  ) as unknown as ConversationWorkspaceProps['renderConversation'];

  if (!open) return null;

  // Backdrop click (on the overlay itself, not a descendant) dismisses.
  const handleOverlayMouseDown = (ev: React.MouseEvent<HTMLDivElement>) => {
    if (ev.target === ev.currentTarget) onClose();
  };

  const overlay = (
    <div className={s.overlay} onMouseDown={handleOverlayMouseDown}>
      {/* eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex */}
      <div
        ref={surfaceRef}
        className={s.surface}
        role="dialog"
        aria-modal="true"
        aria-label="Messages"
        tabIndex={-1}
      >
        {/* §B1 (UAT): close "x" pinned to the surface's literal upper-right corner. */}
        <Button
          appearance="subtle"
          className={s.closeButton}
          aria-label="Close conversations"
          icon={<DismissRegular />}
          onClick={onClose}
        />
        {/* §B2 (UAT): modal title = "Messages". */}
        <div className={s.title}>Messages</div>
        <div className={s.content}>
          <div className={s.workspaceHost}>
            <ConversationWorkspaceR16
              authenticatedFetch={authenticatedFetch}
              bffBaseUrl={bffBaseUrl}
              regarding={{ entityType, id }}
              renderConversation={renderConversation}
              navigationService={navigationService}
            />
          </div>
        </div>
      </div>
    </div>
  );

  // Portal to document.body + re-wrap FluentProvider so the portaled subtree stays
  // themed (fluent-v9-portal-gotcha). Centering no longer depends on the portal —
  // the overlay's flex-center does that — but body-level mounting keeps the modal
  // above the form's own stacking contexts.
  return ReactDOM.createPortal(<FluentProvider theme={webLightTheme}>{overlay}</FluentProvider>, document.body);
};
