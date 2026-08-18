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
import { WidgetErrorBoundary, navigateToEntityRecordSurfaceAsync } from "@spaarke/ui-components";
import { getOobModalSize } from "@spaarke/ui-components/utils/adapters/oobModalSizes";
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
// SmartTodo Code Page name — used when the widget's Open handler is called
// with no selected todoId (the "just open the app" case). R2 FR-13 retired
// the `openTodo` launch-context params (they routed through the retired
// iframe-hosted SmartTodoModal); only the base Code Page name remains.
// ---------------------------------------------------------------------------

const SMART_TODO_CODE_PAGE_NAME = "sprk_smarttodo";

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

  // Open behaviour splits by selection state:
  //
  //   - todoId PRESENT → open the OOB sprk_todo record FORM at Layout 1 via
  //     the shared `navigateToEntityRecordSurfaceAsync` launcher (task 031 /
  //     spec FR-11 — "one code path for create + open"; consolidates this
  //     call site's previously-inline `Xrm.Navigation.navigateTo` onto the
  //     SAME function `SmartTodoApp.tsx`'s open path + task 030's create path
  //     use). The launcher applies Layout 1 sizing (85% × 85%, centered,
  //     dialog target — ai-spaarke-ai-workspace-UI-r2 FR-13/FR-20) and the
  //     frame-walking `resolveXrmNavigation()` resolver (window/parent/top —
  //     strictly more robust than this file's prior single-frame
  //     `globalThis.Xrm` check). `target: 2` = dialog mode (modal overlay
  //     over the current page); the current SpaarkeAi page stays mounted so
  //     when the dialog closes, the user is back exactly where they were
  //     (widget context preserved).
  //
  //   - todoId ABSENT  → open the SmartTodo Code Page (no launch data) so
  //     `useLaunchContext` returns undefined → app renders its default 3-col
  //     Kanban view (no auto-modal). This preserves "user wants to just open
  //     the full app" without forcing a card selection first. UNCHANGED by
  //     task 031 (constraint: "no selection" branch stays intact).
  //
  // Falls back to openForm (page-nav) if the launcher reports no reachable
  // Xrm host (`outcome.launched === false`) — defensive only, should not
  // happen inside MDA. This fallback predates task 031 and is preserved
  // as-is; it is orthogonal to which primary function issues the
  // entityrecord navigateTo call (that part is now the ONE shared launcher).
  const handleOpenTodo = React.useCallback(
    (todoId?: string) => {
      if (todoId) {
        void navigateToEntityRecordSurfaceAsync({
          entityName: "sprk_todo",
          entityId: todoId,
          // smart-todo-r5 UAT 2026-08-18 #1 — uniform dialog title (not sprk_name).
          title: "Smart To Do Item",
          // smart-todo-r5 UAT 2026-08-18 #3 — one size down: fullCover(100%) → record(85%).
          size: getOobModalSize("record"),
        }).then((outcome) => {
          if (outcome.launched) {
            // task 033 (FR-14) — the OOB form dialog closed. An existing-record
            // OPEN never resolves with `savedEntityReference` (CREATE-only, per
            // MS Learn — see wizardLaunchers.ts outcome-shape note), so there is
            // no reliable save-vs-cancel signal; refetch UNCONDITIONALLY so a
            // Save & Close reflects in the widget's list without a manual
            // reload. A redundant refetch on cancel is tolerable; a missing one
            // on save is not. The promise resolves AFTER Save & Close commits,
            // so the refetch reads committed data.
            //
            //  - `refetchRef.current` is the AUTHORITATIVE refresh for THIS
            //    widget (re-queries the list; reflects edits AND completions).
            //  - `feedSync.notifyChange(todoId, true)` fans the change out to
            //    sibling blocks (ActivityFeed / other SmartTodo instances) that
            //    subscribe to FeedTodoSyncContext. `isActive: true` is the
            //    CONSERVATIVE choice: a subscriber treats `true` as
            //    "reconcile-by-refetch, no-op if already listed" but treats
            //    `false` as "REMOVE this todo now" (see useTodoItems.ts). Since
            //    we cannot know from the resolve value whether the user
            //    completed the todo, `false` could wrongly drop a still-active
            //    row cross-block, whereas `true` at worst leaves a
            //    just-completed row until the sibling's own next refresh
            //    (tolerable staleness, never data loss).
            refetchRef.current?.();
            feedSync.notifyChange(todoId, true);
            return;
          }

          // Loose typing — the shared lib doesn't pull in @types/xrm.
          const xrm = (globalThis as unknown as {
            Xrm?: {
              Navigation?: {
                openForm?: (opts: unknown) => Promise<unknown>;
              };
            };
          }).Xrm;
          if (xrm?.Navigation?.openForm) {
            // Defensive — page-nav fallback only.
            void xrm.Navigation.openForm({
              entityName: "sprk_todo",
              entityId: todoId,
              openInNewWindow: false,
            });
            return;
          }
          // Nothing more to do — Xrm.Navigation isn't available. (The pre-R2
          // Code-Page-hop last-resort fallback was retired per R2 FR-13; it
          // routed through `openTodo` launch context to open the retired
          // iframe modal, which no longer exists.)
          // eslint-disable-next-line no-console
          console.warn(
            "[todo.registration] Xrm.Navigation unavailable; cannot open sprk_todo",
          );
        });
        return;
      }

      // No selection → open the full Smart To Do Code Page at its default view.
      ctx.onOpenWizard(SMART_TODO_CODE_PAGE_NAME, undefined, {
        width: { value: 85, unit: "%" },
        height: { value: 85, unit: "%" },
      });
    },
    // `feedSync` added (task 033) — the todoId branch now fans the post-close
    // change out via `feedSync.notifyChange`. `refetchRef` is a ref (no dep).
    [ctx, feedSync],
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
      // `hideTitle` suppresses the SectionPanel header bar while keeping `title`
      // for aria-labels. SmartTodoWidget renders its OWN `<PaneHeader
      // title="Smart To Do" />`, so without this the workspace stacked two
      // identical "Smart To Do" titles (UAT #4, 2026-08-17 — operator: "remove
      // the top 'Smart To Do', only use the title in the code page itself").
      hideTitle: true,
      style: { overflow: "hidden" },
      renderContent: () => React.createElement(FeedSyncBridgeHost, { ctx }),
    };
  },
};

export default todoRegistration;
