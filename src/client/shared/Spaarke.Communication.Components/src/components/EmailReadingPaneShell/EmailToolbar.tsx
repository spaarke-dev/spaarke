/**
 * EmailToolbar.tsx
 *
 * Full-width action toolbar for the reading-pane shell (email-communication-
 * solution-r5, reading-pane MAIN-AREA redesign, section #2). Spans the
 * reading-pane width, rendered BELOW the title bar. Layout:
 *   - LEFT — the email verbs Reply / Reply All / Forward / New as icon+text,
 *     left-aligned (unchanged intent; opens the ONE canonical composer).
 *   - RIGHT — a right-aligned group of ICON-ONLY buttons with tooltips:
 *     Save to SharePoint, Create Event, Create To Do, Link Invoice, and the
 *     demoted Open full form. The create/save actions operate on the email's
 *     RESOLVED association (mirrors the `CommunicationActions` PCF action bar).
 *
 * This component NEVER re-implements action-bar / compose / archive / create
 * logic — it is a pure DISPATCH seam: each button calls the host-supplied
 * handler in `EmailToolbarActionHandlers` with the selected id. The real
 * compose dialog / archive fetch / create-from-email launch is the host's
 * responsibility (`EmailWorkspace` wires them via `useEmailComposeActions` +
 * the `launchCreate` / archive seams). An unwired handler degrades to a
 * console-warned no-op.
 *
 * The demoted "Open full form" trigger reuses the shared `OpenFullFormButton`
 * (§11) rather than re-declaring a navigation button here.
 *
 * React-version note (ADR-022/NFR-05): `React.FC` + standard event types only
 * — no `as React.ComponentType` cast. Fluent v9 tokens only (ADR-021) — themes
 * correctly via the host `FluentProvider` in light and dark mode.
 */
import * as React from 'react';
import { makeStyles, tokens, Toolbar, ToolbarButton, ToolbarDivider, Tooltip } from '@fluentui/react-components';
import {
  ArrowReply20Regular,
  ArrowReplyAll20Regular,
  ArrowForward20Regular,
  Mail20Regular,
  CloudArrowUp20Regular,
  CalendarLtr20Regular,
  CheckmarkCircle20Regular,
  Receipt20Regular,
} from '@fluentui/react-icons';
import { OpenFullFormButton } from '../EmailComposeActions';
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
    alignItems: 'center',
  },
  // Pushes the divider + icon-only cluster to the toolbar's far right; the
  // labelled verbs keep their left placement (mirrors CommunicationActionsApp).
  dividerPush: { marginInlineStart: 'auto' },
  rightGroup: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS },
});

export interface EmailToolbarProps {
  /** The currently selected `sprk_communication` id — record-scoped buttons disable when unset. */
  selectedId?: string;
  /** Dispatch handlers. Omitted entries no-op with a console warning. */
  actions?: EmailToolbarActionHandlers;
}

/** Unwired-handler fallback — never throws, always visible in the console. */
function warnPendingIntegration(action: string): void {
  // eslint-disable-next-line no-console
  console.warn(`[EmailToolbar] "${action}" has no handler wired yet.`);
}

export const EmailToolbar: React.FC<EmailToolbarProps> = ({ selectedId, actions }) => {
  const s = useStyles();
  const recordActionsDisabled = !selectedId;

  // A record-scoped dispatch that no-ops (with a console warning) when unwired.
  const dispatch = React.useCallback(
    (action: string, handler?: (id: string) => void) => {
      if (!selectedId) return;
      if (handler) handler(selectedId);
      else warnPendingIntegration(action);
    },
    [selectedId]
  );

  const handleNew = React.useCallback(() => {
    if (actions?.onNew) actions.onNew();
    else warnPendingIntegration('New');
  }, [actions]);

  return (
    <div className={s.root}>
      <Toolbar size="small" className={s.bar} aria-label="Email actions">
        {/* Left group — email verbs (icon + text). */}
        <ToolbarButton
          icon={<ArrowReply20Regular />}
          disabled={recordActionsDisabled}
          onClick={() => dispatch('Reply', actions?.onReply)}
        >
          Reply
        </ToolbarButton>
        <ToolbarButton
          icon={<ArrowReplyAll20Regular />}
          disabled={recordActionsDisabled}
          onClick={() => dispatch('Reply All', actions?.onReplyAll)}
        >
          Reply All
        </ToolbarButton>
        <ToolbarButton
          icon={<ArrowForward20Regular />}
          disabled={recordActionsDisabled}
          onClick={() => dispatch('Forward', actions?.onForward)}
        >
          Forward
        </ToolbarButton>
        <ToolbarButton icon={<Mail20Regular />} onClick={handleNew}>
          New
        </ToolbarButton>

        {/* Divider + icon-only cluster pushed to the far right. */}
        <ToolbarDivider className={s.dividerPush} />

        <div className={s.rightGroup}>
          <Tooltip content="Save to SharePoint" relationship="label">
            <ToolbarButton
              icon={<CloudArrowUp20Regular />}
              aria-label="Save to SharePoint"
              disabled={recordActionsDisabled}
              onClick={() => dispatch('Save to SharePoint', actions?.onSaveToSharePoint)}
            />
          </Tooltip>
          <Tooltip content="Create Event" relationship="label">
            <ToolbarButton
              icon={<CalendarLtr20Regular />}
              aria-label="Create Event"
              disabled={recordActionsDisabled}
              onClick={() => dispatch('Create Event', actions?.onCreateEvent)}
            />
          </Tooltip>
          <Tooltip content="Create To Do" relationship="label">
            <ToolbarButton
              icon={<CheckmarkCircle20Regular />}
              aria-label="Create To Do"
              disabled={recordActionsDisabled}
              onClick={() => dispatch('Create To Do', actions?.onCreateTodo)}
            />
          </Tooltip>
          <Tooltip content="Link Invoice" relationship="label">
            <ToolbarButton
              icon={<Receipt20Regular />}
              aria-label="Link Invoice"
              disabled={recordActionsDisabled}
              onClick={() => dispatch('Link Invoice', actions?.onLinkInvoice)}
            />
          </Tooltip>
          {selectedId && (
            <OpenFullFormButton
              communicationId={selectedId}
              onOpenFullForm={cid => {
                if (actions?.onOpenFullForm) actions.onOpenFullForm(cid);
                else warnPendingIntegration('Open full form');
              }}
            />
          )}
        </div>
      </Toolbar>
    </div>
  );
};

EmailToolbar.displayName = 'EmailToolbar';
