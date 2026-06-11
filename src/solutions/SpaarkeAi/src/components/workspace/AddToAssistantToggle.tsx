/**
 * AddToAssistantToggle.tsx — R6 Pillar 6b user affordance (D-C-09).
 *
 * Renders a Fluent v9 Switch + Tooltip that flips a workspace tab's
 * `visibleToAssistant` flag from `false` → `true` (or back). Per Pillar 9
 * privacy default (CLAUDE.md §9):
 *   - Agent-created tabs default `visibleToAssistant: true`.
 *   - User-created tabs default `visibleToAssistant: false`.
 *   - Override via this affordance — user explicitly opts the tab into the
 *     agent's per-turn prompt-snapshot.
 *
 * ## Wiring
 *
 * The toggle is a CONTROLLED component (parent supplies `visibleToAssistant`
 * and `onChange`). It also dispatches a `workspace.tab_edited` PaneEventBus
 * event carrying `tabId` + `editedFields: ['visibleToAssistant']` so other
 * panes (trace widget, context pane, assistant pane) observe the change.
 *
 * Per ADR-015: the event payload contains FIELD NAMES, not VALUES — the
 * new boolean lives only on the in-memory tab record + future BFF
 * persistence call (out of scope here). The PaneEventBus surface stays
 * Tier-1 telemetry-safe.
 *
 * ## Design notes
 *
 * - The visibility change is the parent's responsibility (host wires
 *   `onChange` to a persistence call — Pillar 6a `WorkspaceStateService`
 *   write-through). This component does NOT call the BFF directly to keep
 *   the affordance free of network coupling and trivially testable.
 * - Disabled state: when `tabId` is empty or `disabled` is set (e.g. a
 *   read-only tab where `canEdit === false`), the toggle is non-interactive.
 * - Accessibility: the Switch has an `aria-label` derived from the current
 *   state ("Visible to assistant" / "Hidden from assistant").
 *
 * @see FR-39 — three user affordances (this is D-C-09)
 * @see Pillar 9 — `visibleToAssistant` visibility contract
 * @see ADR-015 — tab_edited carries field NAMES only
 * @see ADR-012 — Fluent v9 component patterns
 * @see ADR-021 — semantic tokens only, dark-mode parity
 * @see ADR-022 — React 19 functional components + hooks
 * @see ADR-030 — additive PaneEventBus events on existing `workspace` channel
 */

import * as React from "react";
import {
  Switch,
  Tooltip,
  makeStyles,
  tokens,
  Label,
} from "@fluentui/react-components";
import { useDispatchPaneEvent } from "@spaarke/ai-widgets";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "inline-flex",
    alignItems: "center",
    columnGap: tokens.spacingHorizontalXS,
  },
  label: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface AddToAssistantToggleProps {
  /**
   * Tab identifier — required so the dispatched `tab_edited` event can
   * carry the tabId. Empty string disables the toggle.
   */
  tabId: string;

  /**
   * Session identifier — required so the dispatched `tab_edited` event
   * can be scoped to the active chat session per ADR-015.
   */
  sessionId: string;

  /**
   * Current `visibleToAssistant` state. Controlled — parent owns the value.
   */
  visibleToAssistant: boolean;

  /**
   * Disable the toggle (e.g. when the tab has `canEdit === false`).
   */
  disabled?: boolean;

  /**
   * Called when the user toggles the switch. Receives the NEW value. The
   * parent is responsible for persisting (e.g. BFF write-through).
   *
   * The component dispatches a `workspace.tab_edited` PaneEventBus event
   * before invoking this callback so observers see the change even if the
   * parent defers persistence.
   */
  onChange: (next: boolean) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Fluent v9 Switch wrapping the tab visibility-to-assistant affordance.
 *
 * Visual semantics: a labeled switch ("Visible to assistant") with a tooltip
 * that explains the privacy implication.
 */
export function AddToAssistantToggle(
  props: AddToAssistantToggleProps,
): React.JSX.Element {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  const { tabId, sessionId, visibleToAssistant, disabled, onChange } = props;

  const isDisabled = disabled === true || tabId.length === 0;

  const handleChange = React.useCallback(
    (
      _ev: React.ChangeEvent<HTMLInputElement>,
      data: { checked: boolean },
    ): void => {
      if (isDisabled) return;
      const next = data.checked;

      // Emit `workspace.tab_edited` with FIELD NAMES only (ADR-015 binding).
      // The new boolean value is NOT included in the event — only the fact
      // that `visibleToAssistant` was edited. Downstream consumers
      // re-resolve the value via the tab record (in-memory or from the BFF
      // workspace-state response).
      dispatch("workspace", {
        type: "tab_edited",
        tabId,
        sessionId,
        editedFields: ["visibleToAssistant"],
        timestamp: new Date().toISOString(),
      });

      onChange(next);
    },
    [dispatch, isDisabled, tabId, sessionId, onChange],
  );

  const ariaLabel = visibleToAssistant
    ? "Hide from assistant"
    : "Add to assistant";
  const tooltipContent = visibleToAssistant
    ? "Currently visible to assistant — toggle off to hide this tab from the agent's view."
    : "Currently hidden from assistant — toggle on so the agent can read this tab.";

  return (
    <div className={styles.root}>
      <Tooltip content={tooltipContent} relationship="description">
        <Switch
          checked={visibleToAssistant}
          disabled={isDisabled}
          aria-label={ariaLabel}
          onChange={handleChange}
        />
      </Tooltip>
      <Label className={styles.label}>Visible to assistant</Label>
    </div>
  );
}
