/**
 * SendToWorkspaceButton.tsx — R6 Pillar 6b user affordance (D-C-08).
 *
 * Renders a Fluent v9 Button + Tooltip that promotes a chat assistant message
 * to a new workspace tab. Per FR-39 + project R6 Pillar 6b:
 *   - User-initiated → tab created with `visibleToAssistant: false` (privacy
 *     default; user must explicitly opt the tab into agent visibility via the
 *     companion `AddToAssistantToggle`).
 *   - The button DISPATCHES a `workspace.widget_load` PaneEventBus event so
 *     the existing WorkspacePane subscriber (workspace.WorkspacePane.tsx)
 *     materializes the new tab via the same pipeline as agent/server-initiated
 *     loads. This intentionally avoids duplicating the chat tool
 *     `send_workspace_artifact` (task 054) — the chat tool is for the agent;
 *     this button is the user mirror.
 *
 * ## Design notes
 *
 * - The component receives the message content + a `widgetType` selector
 *   (`Summary` is the default for assistant-message promotion since the
 *   message is typically narrative/markdown).
 * - The component is callback-friendly: the parent can pass an `onSent`
 *   callback to react to the dispatch (e.g. surface a toast, focus the new
 *   tab). The PaneEventBus dispatch happens UNCONDITIONALLY — the callback
 *   is observer-only.
 * - Disabled state: when `content` is empty/whitespace-only, the button
 *   disables (no useful payload to send).
 * - Accessibility: button carries an `aria-label` distinct from the visible
 *   "Send to Workspace" tooltip so screen readers narrate the action clearly.
 *
 * @see FR-39 — three user affordances (this is D-C-08)
 * @see WorkspacePane.tsx — receiver of `workspace.widget_load`
 * @see ADR-012 — Fluent v9 component patterns
 * @see ADR-021 — semantic tokens only, dark-mode parity
 * @see ADR-022 — React 19 functional components + hooks
 * @see ADR-030 — additive PaneEventBus events on existing channels
 */

import * as React from "react";
import { Button, Tooltip, makeStyles, tokens } from "@fluentui/react-components";
import { SendRegular } from "@fluentui/react-icons";
import { useDispatchPaneEvent } from "@spaarke/ai-widgets";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    // Subtle container so the button sits visually inside the message strip.
    display: "inline-flex",
    alignItems: "center",
    columnGap: tokens.spacingHorizontalXS,
  },
});

// ---------------------------------------------------------------------------
// Public widget-type categorization (matches WorkspaceTab.widgetType union)
//
// Re-declared inline (instead of imported from @spaarke/ai-widgets/types)
// to keep this component decoupled from the canonical WorkspaceTab shape in
// the runtime event payload. The dispatched `widget_load` event uses the
// existing `widgetType: string` field on WorkspacePaneEvent, which the
// WorkspacePane subscriber resolves via WorkspaceWidgetRegistry; the four
// agent-visible categories are the policy layer, not the runtime resolver.
// ---------------------------------------------------------------------------

export type SendToWorkspaceWidgetType =
  | "Summary"
  | "DocumentViewer"
  | "Dashboard"
  | "Table";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SendToWorkspaceButtonProps {
  /**
   * The chat-message content to promote. Empty/whitespace disables the
   * button. Long content is passed through as-is; the receiving tab
   * (typically a Summary widget) handles its own truncation/render.
   */
  content: string;

  /**
   * Tab display name. Defaults to "From Chat" when omitted.
   */
  displayName?: string;

  /**
   * Widget-type category. Defaults to `'Summary'` since chat assistant
   * messages are narrative text. Callers may override (e.g. when promoting
   * a table-shaped message to a `Table` tab).
   */
  widgetType?: SendToWorkspaceWidgetType;

  /**
   * Optional observer callback. Fires AFTER the PaneEventBus event has
   * been dispatched. Receives the same payload the bus carried, so the
   * caller can correlate with the new tab (e.g. show a toast).
   *
   * The component does NOT await this callback — the dispatch is the
   * primary effect.
   */
  onSent?: (payload: {
    content: string;
    widgetType: SendToWorkspaceWidgetType;
    displayName: string;
  }) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Fluent v9 Button + Tooltip wrapper for the "Send to Workspace" affordance.
 *
 * Visual semantics: a small subtle button with the SendRegular icon. Disabled
 * when `content` is empty.
 */
export function SendToWorkspaceButton(
  props: SendToWorkspaceButtonProps,
): React.JSX.Element {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  const { content, displayName, widgetType, onSent } = props;
  const resolvedDisplayName = displayName ?? "From Chat";
  const resolvedWidgetType: SendToWorkspaceWidgetType = widgetType ?? "Summary";

  const isDisabled = !content || content.trim().length === 0;

  const handleClick = React.useCallback((): void => {
    if (isDisabled) return;

    // Dispatch the `workspace.widget_load` event with `widgetData` carrying
    // the promoted content + user-initiated provenance hint. The existing
    // WorkspacePane subscriber will materialize a new tab via
    // WorkspaceTabManager.addTab(...).
    //
    // Note: the runtime `widget_load` event has no `visibleToAssistant`
    // field — that flag lives on the canonical WorkspaceTab record managed
    // server-side by Pillar 6a / WorkspaceStateService. The "user-initiated
    // → visibleToAssistant: false" semantic is established at the
    // persistence layer when the server upserts the tab; this client
    // dispatch only signals the LOCAL pane to render. A future integration
    // (out of scope here) wires the dispatch to a BFF call that persists
    // the canonical tab with the correct visibility default.
    dispatch("workspace", {
      type: "widget_load",
      widgetType: resolvedWidgetType,
      widgetData: {
        kind: resolvedWidgetType,
        // Per-category convention: Summary tabs carry their text in `body`.
        body: content,
        // Provenance hint for downstream observers (e.g. tab provenance
        // affordance — Pillar 6c).
        sourceProvenance: {
          source: "user" as const,
          createdAt: new Date().toISOString(),
        },
      },
      displayName: resolvedDisplayName,
    });

    onSent?.({
      content,
      widgetType: resolvedWidgetType,
      displayName: resolvedDisplayName,
    });
  }, [
    dispatch,
    isDisabled,
    content,
    resolvedWidgetType,
    resolvedDisplayName,
    onSent,
  ]);

  return (
    <div className={styles.root}>
      <Tooltip
        content="Send this message to a new workspace tab"
        relationship="label"
      >
        <Button
          appearance="subtle"
          size="small"
          icon={<SendRegular />}
          disabled={isDisabled}
          aria-label="Send to workspace"
          onClick={handleClick}
        />
      </Tooltip>
    </div>
  );
}
