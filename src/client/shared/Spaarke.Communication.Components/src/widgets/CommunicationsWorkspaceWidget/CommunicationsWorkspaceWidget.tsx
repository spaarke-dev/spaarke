/**
 * CommunicationsWorkspaceWidget — upgraded IN PLACE (messaging-communication-app-r3
 * task 031, FR-14a) to mount the shared two-pane conversation shell
 * (`<ConversationWorkspace/>` + `<ConversationView/>`, tasks 011/012) instead
 * of the prior Pattern D filter-chip toolbar + card strip + embedded
 * `<DataGrid>` body (messaging-communication-app-r2 task 030).
 *
 * The registered widget type string `communications-list` and section id
 * `communications` are UNCHANGED (NFR-06) — this is an upgrade, not a second
 * registration:
 *   @see src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts
 *        (Direct-wrapper registration, type `communications-list`)
 *   @see src/solutions/LegalWorkspace/src/sections/communications.registration.ts
 *        (Dashboard-wrapper section shim, `id: "communications"` — Pattern D
 *        dual-use; also reuses THIS component unchanged)
 *
 * Record-less / workspace mode: mounts `<ConversationWorkspace>` WITHOUT a
 * `regarding` prop, so the all-mode thread list (FR-16
 * `GET /api/communications/threads`) includes every thread the caller may
 * see, including record-less Direct threads (Success Criterion 5). This is
 * the workspace (not record-scoped) mount site — the record-scoped
 * right-pane PCF (task 030) is a separate surface that supplies `regarding`.
 *
 * Auth (ADR-028): `authenticatedFetch` is imported directly from
 * `@spaarke/auth` — this widget only ever mounts inside a host (SpaarkeAi /
 * LegalWorkspace Vite Code Page) that has already bootstrapped
 * `@spaarke/auth` via `initAuth()` at its app root before any workspace
 * widget renders, mirroring the established pattern used by
 * `CommunicationTimelineApp`/`CommunicationTimelineRegardingApp` and every
 * `Create*Wizard` `main.tsx`. `ConversationWorkspace`/`ConversationView`
 * themselves stay context-agnostic (ADR-012) — they accept
 * `authenticatedFetch` as an injected prop and never import `@spaarke/auth`
 * directly; this widget is the host-layer seam that performs the import.
 *
 * `currentUserSystemUserId` (required by `<ConversationView>` for FR-02/FR-18
 * mine/others bubble alignment) is resolved via `getCurrentUserId()` from
 * `@spaarke/ui-components` — the SAME current-user identity mechanism already
 * used by `userLookup.ts`/`matterService.ts` (no second identity mechanism
 * introduced, per root CLAUDE.md §11).
 *
 * `configId` remains an accepted (but no longer used) prop purely for
 * backward compatibility with `communications.registration.ts`'s existing
 * `React.createElement(CommunicationsWorkspaceWidget, { configId })` call
 * site — the DataGrid that GUID configured no longer backs this widget's
 * body. See the task 031 coordination note in
 * `projects/messaging-communication-app-r3/notes/task-031-notes.md`.
 *
 * Reuse (root CLAUDE.md §11 / task constraint): thread list + conversation
 * bubbles are MOUNTED from the shared components, never re-implemented here.
 *
 * ADR-021 (Fluent v9 semantic tokens only — dark mode passes through the host
 * `FluentProvider`), ADR-012 (shared component library, context-agnostic),
 * ADR-022 (React 19 functional component), ADR-028 (injected
 * `authenticatedFetch`, no token snapshots).
 *
 * @see src/client/shared/Spaarke.UI.Components/src/components/ConversationWorkspace/ConversationWorkspace.tsx
 * @see src/client/shared/Spaarke.UI.Components/src/components/ConversationView/ConversationView.tsx
 * @see projects/messaging-communication-app-r3/tasks/031-spaarkeai-workspace-widget.poml
 */

import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { authenticatedFetch } from '@spaarke/auth';
import { ConversationWorkspace, ConversationView, getCurrentUserId } from '@spaarke/ui-components';
import type { IConversationRendererProps } from '@spaarke/ui-components';

// ─────────────────────────────────────────────────────────────────────────────
// Styles — `makeStyles` at module scope (ADR-021: Fluent v9 semantic tokens
// only; no hardcoded colors; dark mode passes through the host FluentProvider).
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  // Height/width chain (BUILD-A-NEW-WORKSPACE-WIDGET.md §7.1/§7.2): anchor to
  // the parent tab's box; ConversationWorkspace itself owns the two-pane flex
  // layout beneath this single wrapper.
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    width: '100%',
    minWidth: 0,
    minHeight: 0,
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface CommunicationsWorkspaceWidgetProps {
  /**
   * @deprecated No longer used by this widget's body. Retained ONLY so
   * `communications.registration.ts`'s existing dual-use call site
   * (`React.createElement(CommunicationsWorkspaceWidget, { configId })`)
   * keeps compiling unchanged (NFR-06 — same registered identity, upgraded
   * body). The `sprk_gridconfiguration` GUID it carried configured the prior
   * DataGrid-based body, replaced by the shared two-pane conversation shell.
   * Safe to omit on any new call site.
   */
  configId?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const CommunicationsWorkspaceWidget: React.FC<CommunicationsWorkspaceWidgetProps> = () => {
  const styles = useStyles();

  // Current-user identity (FR-02/FR-18 mine/others bubble alignment) — the
  // established Xrm-host-context mechanism (root CLAUDE.md §11: no second
  // identity mechanism). Falls back to '' when no Xrm host context is
  // reachable (e.g. a non-Xrm test harness); ConversationView then simply
  // never resolves a message as "mine" (every bubble renders left), which is
  // a safe degrade, never a crash.
  const currentUserSystemUserId = React.useMemo(() => getCurrentUserId() ?? '', []);

  // Right-pane renderer seam (see ConversationWorkspace.tsx module header
  // "Renderer seam") — wires the REAL `<ConversationView>` in, mounted (not
  // re-implemented) per root CLAUDE.md §11.
  const renderConversation = React.useCallback(
    ({ threadId, authenticatedFetch: fetchFn, bffBaseUrl }: IConversationRendererProps) => (
      <ConversationView
        threadId={threadId}
        authenticatedFetch={fetchFn}
        bffBaseUrl={bffBaseUrl}
        currentUserSystemUserId={currentUserSystemUserId}
      />
    ),
    [currentUserSystemUserId]
  );

  return (
    <div className={styles.root}>
      {/* Record-less / workspace mode: no `regarding` prop — the all-mode
          thread list (FR-16) includes every thread the caller may see,
          including record-less Direct threads. `bffBaseUrl` is intentionally
          omitted: the free-function `authenticatedFetch` (imported above)
          already resolves relative URLs against the host's configured BFF
          base URL internally (see `@spaarke/auth`'s `authenticatedFetch.ts`
          `resolveUrl` — the same behavior every other production
          `authenticatedFetch`-importing surface in this codebase relies on). */}
      <ConversationWorkspace authenticatedFetch={authenticatedFetch} renderConversation={renderConversation} />
    </div>
  );
};

CommunicationsWorkspaceWidget.displayName = 'CommunicationsWorkspaceWidget';

export default CommunicationsWorkspaceWidget;
