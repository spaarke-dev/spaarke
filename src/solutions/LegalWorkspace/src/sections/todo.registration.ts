/**
 * todo.registration.ts — SectionRegistration for the "Smart To Do" section.
 *
 * R4 task 100 (W-2 — Open-to-form launch protocol + post-wizard-close refetch, 2026-06-18):
 *   - Rewrote `handleOpenTodo` to use the new `openTodo` launch-context
 *     discriminator (`?action=openTodo&todoId=<guid>`). When the user clicks
 *     Open on a widget card, the SmartTodo Code Page now auto-mounts
 *     `<SmartTodoModal>` on the specific record (closes UAT issue 4 — Open
 *     previously showed the bare Kanban, NOT the To Do main form).
 *   - Added a BroadcastChannel subscriber that listens for `sprk_todo:created`
 *     events emitted by the `CreateTodoWizard` Code Page. On receipt the shim
 *     invokes its captured `refetch` ref so the widget refreshes after the
 *     wizard closes — closes UAT issue 1 (new To Dos created via `+` didn't
 *     appear without a page refresh).
 *
 *   Refetch mechanism choice (BroadcastChannel over the alternatives):
 *     - BroadcastChannel — works cross-tab/cross-iframe; widely supported
 *       in modern Chromium-based MDA clients; cheap to wire; no MDA-specific
 *       coupling. The CreateTodoWizard Code Page wraps its `dataService` to
 *       post on `sprk_todo:created` after a successful create.
 *     - Rejected — Xrm.App event stream: MDA-specific, not portable to
 *       Code-Page-only contexts, and the `notifyEvent` payload is heavier
 *       than needed for a refetch trigger.
 *     - Rejected — visibilitychange polling: fragile (assumes refocus
 *       semantics); refetches even on unrelated focus changes.
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
 *       navigation surface.
 *     - Subscribe to the post-wizard-close `sprk_todo:created` BroadcastChannel
 *       so the widget refetches after `+` creates a new record (R4-100).
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
// R4 task 100 (W-2) — post-wizard-close refetch BroadcastChannel contract.
//
// The CreateTodoWizard Code Page (src/solutions/CreateTodoWizard/src/main.tsx)
// posts a `{ type: SPRK_TODO_CREATED }` message on the SPRK_TODO_CHANNEL_NAME
// channel after each successful `sprk_todo` create. This shim listens and
// invokes the widget's captured `refetch` ref so the list refreshes within
// ~150ms of wizard close (no page refresh required).
//
// MUST stay in lockstep with the wizard wrapper's matching constants — they
// are intentionally inlined on both sides (no shared module) because the
// wizard Code Page does not depend on `@spaarke/smart-todo-components`.
// Keep the values stable; bumping either constant requires a coordinated
// edit to both files.
// ---------------------------------------------------------------------------

const SPRK_TODO_CHANNEL_NAME = "sprk_todo:lifecycle";
const SPRK_TODO_CREATED = "sprk_todo:created";

// ---------------------------------------------------------------------------
// R4 task 100 (W-2) — Open-to-form launch contract constants.
//
// MUST stay in lockstep with `src/solutions/SmartTodo/src/hooks/useLaunchContext.ts`
// (the parser side). Inlined here rather than imported because LegalWorkspace
// does not (and should not) depend on the SmartTodo Code Page's package.
// ---------------------------------------------------------------------------

const SMART_TODO_CODE_PAGE_NAME = "sprk_smarttodo";
const LAUNCH_PARAM_ACTION = "action";
const LAUNCH_PARAM_TODO_ID = "todoId";
const LAUNCH_ACTION_OPEN_TODO = "openTodo";

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

  // UAT 2026-06-20 round 4 — split Open behaviour by selection:
  //
  //   - todoId PRESENT → open the OOB To Do record FORM directly via
  //     `Xrm.Navigation.openForm({ entityName: 'sprk_todo', entityId })`.
  //     Skips the SmartTodo Code Page hop entirely so the user gets a single
  //     record modal instead of the prior "Code Page modal → record modal"
  //     stack (UAT round 4 issue 3). The form gets BPF + business rules +
  //     ribbon for free — same as opening from any view.
  //
  //   - todoId ABSENT  → open the SmartTodo Code Page (no launch data) so
  //     `useLaunchContext` returns undefined → app renders its default 3-col
  //     Kanban view (no auto-modal). This preserves "user wants to just open
  //     the full app" without forcing a card selection first.
  //
  // Falls back to the prior Code-Page-hop path if Xrm.Navigation is somehow
  // unavailable (defensive — should not happen inside MDA).
  const handleOpenTodo = React.useCallback(
    (todoId?: string) => {
      if (todoId) {
        // Preferred path: open the record form directly. No Code Page stack.
        // `Xrm` is the model-driven app global; types are loose because the
        // shared lib doesn't pull in @types/xrm.
        const xrm = (globalThis as unknown as { Xrm?: { Navigation?: { openForm?: (opts: unknown) => Promise<unknown> } } }).Xrm;
        if (xrm?.Navigation?.openForm) {
          void xrm.Navigation.openForm({
            entityName: "sprk_todo",
            entityId: todoId,
            openInNewWindow: false,
          });
          return;
        }
        // Defensive fallback only — should never run in real MDA.
        const data =
          `${LAUNCH_PARAM_ACTION}=${LAUNCH_ACTION_OPEN_TODO}` +
          `&${LAUNCH_PARAM_TODO_ID}=${encodeURIComponent(todoId)}`;
        ctx.onOpenWizard(SMART_TODO_CODE_PAGE_NAME, data, {
          width: { value: 85, unit: "%" },
          height: { value: 85, unit: "%" },
        });
        return;
      }

      // No selection → open the full Smart To Do Code Page at its default view.
      ctx.onOpenWizard(SMART_TODO_CODE_PAGE_NAME, undefined, {
        width: { value: 85, unit: "%" },
        height: { value: 85, unit: "%" },
      });
    },
    [ctx],
  );

  const handleAddTodo = React.useCallback(() => {
    ctx.onOpenWizard("sprk_createtodowizard");
  }, [ctx]);

  // R4 task 100 (W-2) — capture the widget's refetch trigger in a ref so the
  // BroadcastChannel listener below can fire it on `sprk_todo:created`. We
  // ALSO forward the refetch up to the host via `ctx.onRefetchReady` so the
  // host's global refresh affordance keeps working.
  const refetchRef = React.useRef<(() => void) | null>(null);
  const handleRefetchReady = React.useCallback(
    (refetch: () => void) => {
      refetchRef.current = refetch;
      ctx.onRefetchReady(refetch);
    },
    [ctx],
  );

  // R4 task 100 (W-2) — listen for post-wizard-close create broadcasts.
  //
  // The CreateTodoWizard Code Page posts on this channel after a successful
  // `sprk_todo` create. The wizard runs in a separate iframe/window, so
  // BroadcastChannel is the appropriate cross-context transport.
  //
  // Defensive: BroadcastChannel may be unavailable in some sandboxed
  // contexts (old MDA flavors, restrictive iframes). When unavailable the
  // subscription silently no-ops — the host's manual refresh still works.
  React.useEffect(() => {
    if (typeof BroadcastChannel === "undefined") return undefined;

    let channel: BroadcastChannel | null = null;
    try {
      channel = new BroadcastChannel(SPRK_TODO_CHANNEL_NAME);
    } catch (err) {
      // Non-fatal — proceed without the auto-refetch (manual refresh still works)
      console.warn(
        "[LegalWorkspace.todo] BroadcastChannel unavailable; skipping post-wizard-close refetch wiring",
        err,
      );
      return undefined;
    }

    const handleMessage = (ev: MessageEvent) => {
      const data = ev?.data;
      if (
        data &&
        typeof data === "object" &&
        (data as { type?: unknown }).type === SPRK_TODO_CREATED
      ) {
        // Fire-and-forget — the widget refetch is an OData read with its
        // own debounce/cancellation; safe to invoke multiple times if
        // several creates broadcast in succession.
        refetchRef.current?.();
      }
    };

    channel.addEventListener("message", handleMessage);
    return () => {
      try {
        channel?.removeEventListener("message", handleMessage);
        channel?.close();
      } catch {
        // BroadcastChannel cleanup is best-effort.
      }
    };
  }, []);

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
