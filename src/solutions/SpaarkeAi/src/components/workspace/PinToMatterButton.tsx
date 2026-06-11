/**
 * PinToMatterButton.tsx — R6 Pillar 6b user affordance (D-C-10).
 *
 * Renders a Fluent v9 ToggleButton + Tooltip that promotes a workspace tab
 * from the Redis hot tier (24h TTL) to the Cosmos durable tier attached to
 * the supplied matter. Per Pillar 6a Q4 hybrid persistence model:
 *   - Hot tier: every active-session tab is in Redis.
 *   - Pin promotes the tab to Cosmos with `isPinned: true` AND attaches it
 *     to the supplied `matterId` so it survives Redis TTL expiry.
 *
 * ## Wiring
 *
 * This component is a CONTROLLED affordance — the parent owns the
 * `isPinned` state and the `onPin` callback. The callback's host wires it
 * to `IWorkspaceStateService.PinTabAsync(tenantId, sessionId, tabId, matterId)`
 * via a BFF endpoint. The component intentionally does NOT issue the BFF
 * request directly: keeping the affordance free of network coupling makes
 * it trivially testable AND allows the host to coordinate persistence with
 * other tab mutations (batch write-through, optimistic UI, retry, etc.).
 *
 * Per ADR-015: the dispatched `workspace.tab_edited` event carries FIELD
 * NAMES only (`['isPinned', 'matterContext']`), never the matter id value.
 * The matter id is part of the affordance's `matterId` prop (the parent
 * has already resolved which matter to attach via the active workspace
 * context) and travels in the persistence call, not the PaneEventBus.
 *
 * ## Design notes
 *
 * - Disabled state: when `matterId` is empty (no matter context) or
 *   `disabled` is set, the button is non-interactive. The tooltip explains
 *   the precondition so the user knows why.
 * - Toggle semantics: pinning is reversible via the same affordance. The
 *   `isPinned` prop drives the visual state (filled vs outline pin icon).
 * - Accessibility: `aria-label` derived from the current state ("Pin to
 *   matter" / "Unpin from matter") and `aria-pressed` reflects the toggle.
 *
 * @see FR-39 — three user affordances (this is D-C-10)
 * @see IWorkspaceStateService.PinTabAsync — server-side persistence target
 * @see Pillar 6a Q4 hybrid persistence (Redis hot tier + Cosmos durable)
 * @see ADR-012 — Fluent v9 component patterns
 * @see ADR-015 — tab_edited carries field NAMES only
 * @see ADR-021 — semantic tokens only, dark-mode parity
 * @see ADR-022 — React 19 functional components + hooks
 * @see ADR-030 — additive PaneEventBus events on existing `workspace` channel
 */

import * as React from "react";
import { ToggleButton, Tooltip, makeStyles, tokens } from "@fluentui/react-components";
import { PinRegular, PinFilled } from "@fluentui/react-icons";
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
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface PinToMatterButtonProps {
  /**
   * Tab identifier — required so the dispatched `tab_edited` event can
   * carry the tabId. Empty string disables the button.
   */
  tabId: string;

  /**
   * Session identifier — required so the dispatched `tab_edited` event
   * can be scoped to the active chat session per ADR-015.
   */
  sessionId: string;

  /**
   * Matter identifier — the Dataverse `sprk_matter` GUID this tab will
   * attach to in the Cosmos durable tier. When empty, the button is
   * disabled and the tooltip explains the precondition.
   */
  matterId: string;

  /**
   * Optional human-readable matter name. Shown in the tooltip so the user
   * knows which matter they're pinning to.
   */
  matterName?: string;

  /**
   * Current pin state. Controlled — parent owns the value.
   */
  isPinned: boolean;

  /**
   * Disable the button explicitly (e.g. while a persistence call is in
   * flight).
   */
  disabled?: boolean;

  /**
   * Called when the user clicks the button. Receives the NEW pin state.
   * The parent is responsible for invoking the BFF `PinTabAsync` (or
   * the corresponding unpin operation) and reflecting the result in the
   * `isPinned` prop on the next render.
   *
   * The component dispatches `workspace.tab_edited` BEFORE invoking this
   * callback so observers (trace widget, etc.) see the change immediately.
   */
  onPin: (nextPinned: boolean) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Fluent v9 ToggleButton wrapping the Pin-to-Matter affordance.
 *
 * Visual semantics: a subtle toggle button with a pin icon. The filled
 * `PinFilled` icon indicates the pinned state; the outline `PinRegular`
 * icon indicates unpinned.
 */
export function PinToMatterButton(
  props: PinToMatterButtonProps,
): React.JSX.Element {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  const {
    tabId,
    sessionId,
    matterId,
    matterName,
    isPinned,
    disabled,
    onPin,
  } = props;

  // Disabled when no tabId or no matter context (cannot pin without a
  // matter to attach to), or when the parent explicitly disables.
  const isDisabled =
    disabled === true || tabId.length === 0 || matterId.length === 0;

  const handleClick = React.useCallback((): void => {
    if (isDisabled) return;
    const next = !isPinned;

    // Emit `workspace.tab_edited` with FIELD NAMES only (ADR-015 binding).
    // Subscribers re-resolve the canonical pin state from the tab record
    // or the BFF workspace-state response — the event signals "something
    // changed", not "the value is X".
    dispatch("workspace", {
      type: "tab_edited",
      tabId,
      sessionId,
      editedFields: ["isPinned", "matterContext"],
      timestamp: new Date().toISOString(),
    });

    onPin(next);
  }, [dispatch, isDisabled, tabId, sessionId, isPinned, onPin]);

  const ariaLabel = isPinned ? "Unpin from matter" : "Pin to matter";
  const tooltipContent = (() => {
    if (matterId.length === 0) {
      return "No matter context — open this tab from a matter to enable pinning.";
    }
    if (isPinned) {
      return matterName
        ? `Pinned to matter "${matterName}" — click to unpin.`
        : "Pinned to matter — click to unpin.";
    }
    return matterName
      ? `Pin this tab to matter "${matterName}" so it survives session expiry.`
      : "Pin this tab to the current matter so it survives session expiry.";
  })();

  return (
    <div className={styles.root}>
      <Tooltip content={tooltipContent} relationship="label">
        <ToggleButton
          appearance="subtle"
          size="small"
          icon={isPinned ? <PinFilled /> : <PinRegular />}
          checked={isPinned}
          disabled={isDisabled}
          aria-label={ariaLabel}
          aria-pressed={isPinned}
          onClick={handleClick}
        />
      </Tooltip>
    </div>
  );
}
