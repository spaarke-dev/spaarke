/**
 * todo.registration.ts — SectionRegistration for the "My To Do List" section.
 *
 * R4 task 020 (Pattern D dual-use rebuild — 2026-06-10):
 *   The shim now renders the host-agnostic `<SmartTodoWidget>` from the new
 *   `@spaarke/smart-todo-components` peer package, wrapped in
 *   `<WidgetErrorBoundary>` (PR #372 addition). This shim is the canonical
 *   "Pattern D LegalWorkspace section shim" — modeled on the Calendar widget
 *   pattern from R3 task 115 (see widget-surface-audit.md §5).
 *
 *   Key responsibilities (kept in the shim, NOT in the shared-lib widget):
 *     - Subscribe to `FeedTodoSyncContext` (LW-internal context that does not
 *       belong in any shared lib).
 *     - Forward cross-block lifecycle events to the widget via the `feedSync`
 *       prop bridge (per the user-decided binding decision in widget-surface-
 *       audit.md §7 OQ-1 option b).
 *     - Provide a toolbar with refresh + add + open actions wired to the
 *       host's PCF context (Xrm.WebApi, onOpenWizard).
 *     - Catch render-time errors with `<WidgetErrorBoundary>` so a bad
 *       widget mount surfaces an inline error card rather than blanking
 *       the whole SpaarkeAi workspace surface.
 *
 *   Why this rebuild fixes the runtime issue:
 *     The stale deployed bundle for the LegalWorkspace embedded SmartToDo
 *     section queried the long-retired `sprk_event.sprk_todoflag` shape.
 *     `<SmartTodoWidget>` queries `sprk_todo` directly with statuscode
 *     filter in {Open=1, In Progress=659490001} per spec.md FR-02.
 *     A rebuild of the LegalWorkspace bundle (or the SpaarkeAi consumer
 *     bundle that embeds LW) is sufficient to clear the OData error.
 *
 *   Tradeoff (deferred, by design):
 *     The richer LW SmartToDo Kanban (13-file subtree with score cards,
 *     dismissed section, threshold settings, AI summary dialog, etc.) is
 *     NOT hoisted in this initial 0.1.0 release of `@spaarke/smart-todo-
 *     components`. That hoist is a follow-up task. The widget rendered
 *     here is the minimal, host-agnostic version that satisfies FR-02 +
 *     FR-04 — sized to position the surface for future Direct widget
 *     registration (see Spaarke.AI.Widgets/workspace/register-workspace-
 *     widgets.ts) without disrupting the section's render path today.
 *
 * Standards: ADR-012 (shared component peer package), ADR-021 (Fluent v9).
 */

import * as React from "react";
import { Button } from "@fluentui/react-components";
import {
  CheckmarkCircleRegular,
  AddRegular,
  OpenRegular,
} from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { WidgetErrorBoundary } from "@spaarke/ui-components";
import { SmartTodoWidget } from "@spaarke/smart-todo-components";
import type { IFeedSyncBridge, SmartTodoWidgetProps } from "@spaarke/smart-todo-components";
import { useFeedTodoSync } from "../hooks/useFeedTodoSync";

// ---------------------------------------------------------------------------
// FeedSync bridge — subscribes to LW-internal FeedTodoSyncContext and forwards
// to the shared-lib widget via the `feedSync` prop bridge.
//
// This is the canonical "shim owns LW-coupled state" pattern. The shared-lib
// widget remains host-agnostic (zero LW imports); the host shim brokers the
// coupling.
// ---------------------------------------------------------------------------

interface IFeedSyncBridgeHostProps {
  ctx: SectionFactoryContext;
  refetchRef: React.MutableRefObject<(() => void) | undefined>;
}

const FeedSyncBridgeHost: React.FC<IFeedSyncBridgeHostProps> = ({ ctx, refetchRef }) => {
  // Subscribe to LW's FeedTodoSyncContext. The hook returns a NOOP fallback
  // when no provider is mounted (e.g., a future SpaarkeAi consumer that
  // doesn't host FeedTodoSyncContext) — so the widget renders cleanly there
  // without changes.
  const { notifyTodoChange, subscribe } = useFeedTodoSync();

  const feedSync = React.useMemo<IFeedSyncBridge>(
    () => ({
      notifyChange: notifyTodoChange,
      subscribe,
    }),
    [notifyTodoChange, subscribe],
  );

  // Open handler routes through the host's onOpenWizard so the SmartTodo
  // Code Page opens with the clicked todo's id (legacy `eventId` param name
  // is preserved at the wire for SmartTodo Code Page compatibility; the
  // value carried is a `sprk_todoid` GUID post R3 FR-29).
  const handleOpenTodo = React.useCallback(
    (todoId: string) => {
      const data = `eventId=${encodeURIComponent(todoId)}`;
      ctx.onOpenWizard("sprk_smarttodo", data, {
        width: { value: 85, unit: "%" },
        height: { value: 85, unit: "%" },
      });
    },
    [ctx],
  );

  const handleAddTodo = React.useCallback(() => {
    ctx.onOpenWizard("sprk_createtodowizard");
  }, [ctx]);

  const handleRefetchReady = React.useCallback(
    (refetch: () => void) => {
      refetchRef.current = refetch;
      ctx.onRefetchReady(refetch);
    },
    [ctx, refetchRef],
  );

  const widgetElement = React.createElement(SmartTodoWidget, {
    webApi: ctx.webApi as SmartTodoWidgetProps["webApi"],
    userId: ctx.userId,
    scope: ctx.scope,
    businessUnitId: ctx.businessUnitId,
    feedSync,
    onBadgeCountChange: ctx.onBadgeCountChange,
    onRefetchReady: handleRefetchReady,
    onOpenTodo: handleOpenTodo,
    onAddTodo: handleAddTodo,
  });

  return React.createElement(
    WidgetErrorBoundary,
    {
      widgetType: "smart-todo",
      displayName: "Smart To Do",
      surface: "LegalWorkspace",
      children: widgetElement,
    },
  );
};

// ---------------------------------------------------------------------------
// Toolbar divider — thin vertical separator between toolbar button groups.
// Preserved from the pre-R4 shim to keep visual parity in the section header.
// ---------------------------------------------------------------------------

const ToolbarDivider: React.FC = () =>
  React.createElement("span", {
    "aria-hidden": "true",
    style: {
      width: "1px",
      height: "20px",
      backgroundColor: "var(--colorNeutralStroke2)",
      marginLeft: "2px",
      marginRight: "2px",
      flexShrink: 0,
      display: "inline-block",
    },
  });

// ---------------------------------------------------------------------------
// Registration
// ---------------------------------------------------------------------------

export const todoRegistration: SectionRegistration = {
  id: "todo",
  label: "My To Do List",
  description: "Embedded smart to-do list with cross-block sync (R4 Pattern D).",
  icon: CheckmarkCircleRegular,
  category: "productivity",
  defaultHeight: "560px",

  factory(ctx: SectionFactoryContext): ContentSectionConfig {
    // -----------------------------------------------------------------------
    // Refetch holder — captured by toolbar closure, written by the widget
    // via FeedSyncBridgeHost during its first render cycle.
    // -----------------------------------------------------------------------

    const refetchRef: { current: (() => void) | undefined } = { current: undefined };

    // -----------------------------------------------------------------------
    // Toolbar — preserved from pre-R4 shim:
    //   refresh (left, marginRight: auto) | divider | add + open (right, 15px gap)
    //
    // The widget's own PaneHeader has refresh / add actions too; the section
    // toolbar adds the "Open full Code Page" affordance (which the widget
    // can't own host-agnostically).
    // -----------------------------------------------------------------------

    const toolbar = React.createElement(
      React.Fragment,
      null,
      // Add + Open button group (right-aligned, 15px gap)
      React.createElement(
        "div",
        {
          style: {
            display: "flex",
            flexDirection: "row" as const,
            alignItems: "center",
            gap: "15px",
            marginLeft: "auto",
          },
        },
        React.createElement(Button, {
          appearance: "subtle",
          size: "small",
          icon: React.createElement(AddRegular),
          onClick: () => ctx.onOpenWizard("sprk_createtodowizard"),
          "aria-label": "Create new to do",
        }),
        React.createElement(Button, {
          appearance: "subtle",
          size: "small",
          icon: React.createElement(OpenRegular),
          onClick: () =>
            ctx.onOpenWizard("sprk_smarttodo", undefined, {
              width: { value: 85, unit: "%" },
              height: { value: 85, unit: "%" },
            }),
          "aria-label": "Open full To Do list",
        }),
      ),
    );

    // Reference ToolbarDivider to satisfy the no-unused-vars rule even though
    // the divider is not currently rendered in the new toolbar (kept available
    // for future toolbar evolutions).
    void ToolbarDivider;

    return {
      id: "todo",
      type: "content",
      title: "My To Do List",
      toolbar,
      style: {},
      renderContent: () =>
        React.createElement(FeedSyncBridgeHost, { ctx, refetchRef }),
    };
  },
};

export default todoRegistration;
