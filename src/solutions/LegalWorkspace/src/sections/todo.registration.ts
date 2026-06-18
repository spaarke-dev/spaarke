/**
 * todo.registration.ts — SectionRegistration for the "Smart To Do" section.
 *
 * R4 task 099 (W-1 — widget chrome consolidation + Pattern D alignment, 2026-06-18):
 *   The shim is now STRUCTURAL-ONLY — it collapses to the canonical Pattern D
 *   shape mirroring `calendar.registration.ts`. Per the 2026-06-18 widget-parity
 *   audit (`projects/smart-todo-r4/notes/d-widget-parity-audit-2026-06-18.md`),
 *   the pre-099 shim added a SECOND title bar (`title: "My To Do List"`) and a
 *   SECOND toolbar (Add + Open buttons) on top of the widget's own PaneHeader
 *   chrome — visible as a duplicate-chrome anti-pattern in UAT screenshots.
 *
 *   Calendar's shim has zero section-level chrome; the widget owns 100%. The
 *   SmartTodo shim now does the same:
 *     - NO `title` on the section config
 *     - NO `toolbar` on the section config
 *     - All user-facing chrome (title "Smart To Do", `[SearchBox, +, Open,
 *       refresh]` toolbar) lives inside `<SmartTodoWidget>`'s PaneHeader.
 *
 *   The shim's remaining job is to bridge LW-internal coupling that does NOT
 *   belong in any shared lib:
 *     - Subscribe to `FeedTodoSyncContext` and forward via the `feedSync` prop.
 *     - Wire host callbacks (`onOpenWizard`) so `+` and Open invoke the host's
 *       navigation surface. (R4-100 will rewrite Open's target to mount
 *       <SmartTodoModal> directly via the `openTodo` discriminator.)
 *     - Catch render-time errors with `<WidgetErrorBoundary>`.
 *
 * R4 task 020 (Pattern D dual-use rebuild — 2026-06-10):
 *   Original shim that wired the shared-lib widget into the LW host. See git
 *   history for the pre-099 version that carried section title + toolbar.
 *
 * Standards: ADR-012 (shared component peer package), ADR-021 (Fluent v9).
 */

import * as React from "react";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { WidgetErrorBoundary } from "@spaarke/ui-components";
import { CheckmarkCircleRegular } from "@fluentui/react-icons";
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
}

const FeedSyncBridgeHost: React.FC<IFeedSyncBridgeHostProps> = ({ ctx }) => {
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
  //
  // NOTE: R4-100 (W-2) will rewrite this target — instead of opening the
  // bare Code Page (which defaults to Kanban), it will pass the
  // `openTodo` discriminator that `useLaunchContext` parses to auto-mount
  // `<SmartTodoModal>` on the clicked record.
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
      ctx.onRefetchReady(refetch);
    },
    [ctx],
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
// Registration — Pattern D structural-only shim (mirrors calendar.registration.ts)
// ---------------------------------------------------------------------------

export const todoRegistration: SectionRegistration = {
  id: "todo",
  label: "Smart To Do",
  description: "Embedded smart to-do list with cross-block sync (R4 Pattern D).",
  icon: CheckmarkCircleRegular,
  category: "productivity",
  defaultHeight: "560px",

  factory(ctx: SectionFactoryContext): ContentSectionConfig {
    // Structural-only — no section title, no section toolbar.
    // The widget (`SmartTodoWidget`) owns 100% of user-facing chrome via
    // its own `<PaneHeader title="Smart To Do" rightSlot={...} />`.
    return {
      id: "todo",
      type: "content",
      title: "Smart To Do",
      style: { overflow: "hidden" },
      renderContent: () => React.createElement(FeedSyncBridgeHost, { ctx }),
    };
  },
};

export default todoRegistration;
