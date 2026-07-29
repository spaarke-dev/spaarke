/**
 * EmailToolbar.tsx
 *
 * Full-width action toolbar for the reading-pane shell (email-communication-
 * solution-r5 task 032, FR-08). Spans the reading-pane width and renders Reply
 * / Reply All / Forward / New / Archive / Create — mirroring the production
 * `CommunicationActionsApp` action-bar verbs
 * (`src/client/pcf/CommunicationActions/CommunicationActions/CommunicationActionsApp.tsx`).
 *
 * This component NEVER re-implements action-bar/compose logic (that would
 * fork the extracted task-022 `logic/actions` — see the task-032 POML
 * escalation trigger). It is a pure DISPATCH seam: each button calls the
 * host-supplied handler in `EmailToolbarActionHandlers` with the selected id.
 * The actual compose dialog / archive fetch / create-from-email launch is
 * task 036's responsibility; an unwired handler here degrades to a
 * console-warned no-op rather than silently doing nothing or throwing.
 *
 * React-version note (ADR-022/NFR-05): `React.FC` + standard event types only
 * — no React-18/19-only runtime API, no `as React.ComponentType` cast.
 * Fluent v9 tokens only (ADR-021) — themes correctly via the host
 * `FluentProvider` in light and dark mode.
 */
import * as React from 'react';
import { makeStyles, tokens, Toolbar, ToolbarButton } from '@fluentui/react-components';
import {
  ArrowReply20Regular,
  ArrowReplyAll20Regular,
  ArrowForward20Regular,
  Mail20Regular,
  CloudArrowUp20Regular,
  Add20Regular,
} from '@fluentui/react-icons';
import type { EmailToolbarActionHandlers } from './EmailReadingPaneShell.types';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    width: '100%',
    minWidth: 0,
    flexShrink: 0,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  bar: {
    flexWrap: 'wrap',
    flexGrow: 1,
    width: '100%',
  },
});

export interface EmailToolbarProps {
  /** The currently selected `sprk_communication` id — record-scoped buttons disable when unset. */
  selectedId?: string;
  /** Dispatch handlers (task 022 seam). Omitted entries no-op with a console warning. */
  actions?: EmailToolbarActionHandlers;
}

/** Unwired-handler fallback — never throws, always visible in the console (task 036 pending-integration marker). */
function warnPendingIntegration(action: string): void {
  // eslint-disable-next-line no-console
  console.warn(`[EmailToolbar] "${action}" has no handler wired yet (pending task 036 compose/dispatch integration).`);
}

export const EmailToolbar: React.FC<EmailToolbarProps> = ({ selectedId, actions }) => {
  const s = useStyles();
  const recordActionsDisabled = !selectedId;

  const handleReply = React.useCallback(() => {
    if (!selectedId) return;
    if (actions?.onReply) actions.onReply(selectedId);
    else warnPendingIntegration('Reply');
  }, [actions, selectedId]);

  const handleReplyAll = React.useCallback(() => {
    if (!selectedId) return;
    if (actions?.onReplyAll) actions.onReplyAll(selectedId);
    else warnPendingIntegration('Reply All');
  }, [actions, selectedId]);

  const handleForward = React.useCallback(() => {
    if (!selectedId) return;
    if (actions?.onForward) actions.onForward(selectedId);
    else warnPendingIntegration('Forward');
  }, [actions, selectedId]);

  const handleNew = React.useCallback(() => {
    if (actions?.onNew) actions.onNew();
    else warnPendingIntegration('New');
  }, [actions]);

  const handleArchive = React.useCallback(() => {
    if (!selectedId) return;
    if (actions?.onArchive) actions.onArchive(selectedId);
    else warnPendingIntegration('Archive');
  }, [actions, selectedId]);

  const handleCreate = React.useCallback(() => {
    if (!selectedId) return;
    if (actions?.onCreate) actions.onCreate(selectedId);
    else warnPendingIntegration('Create');
  }, [actions, selectedId]);

  return (
    <div className={s.root}>
      <Toolbar size="small" className={s.bar} aria-label="Email actions">
        <ToolbarButton icon={<ArrowReply20Regular />} disabled={recordActionsDisabled} onClick={handleReply}>
          Reply
        </ToolbarButton>
        <ToolbarButton icon={<ArrowReplyAll20Regular />} disabled={recordActionsDisabled} onClick={handleReplyAll}>
          Reply All
        </ToolbarButton>
        <ToolbarButton icon={<ArrowForward20Regular />} disabled={recordActionsDisabled} onClick={handleForward}>
          Forward
        </ToolbarButton>
        <ToolbarButton icon={<Mail20Regular />} onClick={handleNew}>
          New
        </ToolbarButton>
        <ToolbarButton icon={<CloudArrowUp20Regular />} disabled={recordActionsDisabled} onClick={handleArchive}>
          Archive
        </ToolbarButton>
        <ToolbarButton icon={<Add20Regular />} disabled={recordActionsDisabled} onClick={handleCreate}>
          Create
        </ToolbarButton>
      </Toolbar>
    </div>
  );
};

EmailToolbar.displayName = 'EmailToolbar';
