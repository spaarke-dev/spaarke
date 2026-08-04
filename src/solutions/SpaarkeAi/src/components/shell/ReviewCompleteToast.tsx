/**
 * ReviewCompleteToast.tsx — UAT round-1 owner-approved follow-on (task 071):
 * "Notify me when [an agreement review] completed" (in-app layer only; the
 * durable "closed the browser" notification via the notification spine is a
 * FILED follow-on — see notes/uat-round1-2026-08-03.md).
 *
 * Headless bridge component (renders nothing itself): when an Agreement
 * Review completes while the user has navigated to another workspace tab, it
 * raises a Fluent v9 toast — "Agreement review complete" + a "View findings"
 * action that re-activates the existing Compose tab. Suppressed when Compose
 * is already the visible workspace surface (don't announce what's on screen).
 *
 * ── Completion signal (ADR-030 — subscribe, don't emit) ─────────────────────
 * Subscribes to the EXISTING `workspace.compose_advisory_comments` PaneEventBus
 * event — the SAME event `useNdaReviewAdvisoryCommentsBridge` (conversation/**)
 * already dispatches on review completion (ai-advanced-capabilities-nda-r1 task
 * 031; agreements-r1 task-002 schema split). No new emit; this is a NEW
 * SUBSCRIBER on an existing, additive discriminant.
 *
 * KNOWN GAP (documented, not fixed here — HARD BOUNDARY forbids editing
 * conversation/** this wave): `useNdaReviewAdvisoryCommentsBridge.emitFromResult`
 * (src/solutions/SpaarkeAi/src/components/conversation/useNdaReviewAdvisoryCommentsBridge.ts,
 * `if (advisoryComments.length === 0) return;`) early-returns WITHOUT dispatching
 * `compose_advisory_comments` when a review finds ZERO flagged sections (a clean
 * review). So a zero-findings review completion is NOT observable on the
 * PaneEventBus today, and this toast will not fire for that case. See
 * projects/ai-advanced-capabilities-agreements-r1/notes/071-execution-notes.md
 * for the exact one-line fix recommended for the 070 follow-on (folding the
 * early-return away so the event still dispatches with an empty
 * `advisoryComments: []` — verified safe: ComposeWorkspace's own
 * `onAdvisoryComments` receiver already has its own independent
 * `if (items.length === 0) return;` guard, so relaxing the bridge's guard does
 * not change the findings-materialization behavior for that consumer).
 *
 * ── Visibility signal (ADR-030 — subscribe, don't emit) ──────────────────────
 * "Is Compose the currently active workspace tab?" is derived from the
 * EXISTING `workspace.active_widget_changed` event — WorkspacePane's Round-4
 * Fix-4 broadcast, dispatched on every tab activation (add/switch/close/
 * restore). Its own doc comment says "NO consumers are wired in this task —
 * this is signal infrastructure only" (WorkspacePane.tsx ~line 552); this
 * component is the first consumer. No new discriminant.
 *
 * ── Action: "View findings" → focus the existing Compose tab ────────────────
 * Dispatches `workspace.widget_load` with `widgetType: 'compose'` and
 * `widgetData: { source: 'review-complete-toast' }` — the SAME "source-only
 * re-activation" marker contract WorkspacePane already honours for the
 * add-to-DMS / reporting-email / welcome-compose flows
 * (WorkspacePane.tsx `hasSourceReactivationMarker`, ~line 1445): it REUSES the
 * active (or first open) Compose tab and activates it — it never mints a
 * duplicate tab.
 *
 * ── Toaster reuse (§11 — default to reuse) ───────────────────────────────────
 * Mounts NO new `<Toaster>`. ThreePaneShell's `SessionRestoreManager` already
 * mounts one Toaster for restore-failure toasts; `SPAARKEAI_SHELL_TOASTER_ID`
 * (a fixed string, not `useId()`) lets this component's `useToastController`
 * target that SAME mounted Toaster/portal — one stacking order, one z-index
 * layer, for the whole shell.
 *
 * ── Bounded stacking ──────────────────────────────────────────────────────
 * Uses a FIXED `toastId`. Fluent's toast store is a Map keyed by `toastId` —
 * calling `dispatchToast` again with an id that is still active is a documented
 * no-op (`@fluentui/react-toast` `buildToast`: `if (toasts.has(toastId)) return;`).
 * So a second review completing while the first toast is still showing calls
 * `updateToast({ toastId, content })` instead — REPLACING the toast's content
 * in place rather than stacking a second one. `onStatusChange` (dispatched
 * with 'dismissed' | 'unmounted') resets the in-flight flag so the NEXT
 * completion after the toast has cleared dispatches fresh.
 *
 * @see docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md
 * @see docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md
 * @see PaneEventTypes.ts — `compose_advisory_comments` / `active_widget_changed`
 * @see ThreePaneShell.tsx — mount site + `SPAARKEAI_SHELL_TOASTER_ID`
 * @see ADR-021 — Fluent v9 tokens only, dark mode required (Toast/ToastTitle/Link
 *      are token-driven; no hardcoded colors here)
 * @see ADR-030 — typed PaneEventBus channels; prefer subscribing over new emits
 */
import * as React from "react";
import { Toast, ToastTitle, Link, useToastController } from "@fluentui/react-components";
import { usePaneEvent, useDispatchPaneEvent } from "@spaarke/ai-widgets";

/** Fixed so a second completion UPDATES this toast rather than stacking a new one. Exported for tests. */
export const REVIEW_COMPLETE_TOAST_ID = "spaarkeai-review-complete-toast";
/** Fluent's own default is 3000ms; a completion notice earns a longer window. */
const REVIEW_COMPLETE_TOAST_TIMEOUT_MS = 10000;

export interface ReviewCompleteToastProps {
  /** The shell's shared Toaster id — the SAME id `SessionRestoreManager` mounts its `<Toaster>` with. */
  toasterId: string;
}

/**
 * Headless bridge — renders nothing of its own. Mount once, anywhere inside
 * `PaneEventBusProvider` (and inside the host `FluentProvider`, for
 * `useToastController`'s `useFluent()` target-document lookup).
 */
export function ReviewCompleteToast({ toasterId }: ReviewCompleteToastProps): null {
  const { dispatchToast, updateToast, dismissToast } = useToastController(toasterId);
  const dispatch = useDispatchPaneEvent();

  // The currently-active workspace tab's widgetType, fed purely by the EXISTING
  // active_widget_changed broadcast (no new state elsewhere, no polling).
  // undefined until the first tab activation is observed.
  const activeWidgetTypeRef = React.useRef<string | undefined>(undefined);

  // True while the fixed-id toast is queued/visible (cleared by onStatusChange
  // once it dismisses/unmounts) — drives the dispatch-vs-update bounded-stacking
  // decision.
  const toastActiveRef = React.useRef(false);

  const handleViewFindings = React.useCallback((): void => {
    // Source-only re-activation (WorkspacePane.tsx `hasSourceReactivationMarker`) —
    // reuses the active/first-open Compose tab; never mints a duplicate.
    dispatch("workspace", {
      type: "widget_load",
      widgetType: "compose",
      widgetData: { source: "review-complete-toast" },
    });
    dismissToast(REVIEW_COMPLETE_TOAST_ID);
  }, [dispatch, dismissToast]);

  const handleStatusChange = React.useCallback((_event: null, data: { status: string }): void => {
    if (data.status === "dismissed" || data.status === "unmounted") {
      toastActiveRef.current = false;
    }
  }, []);

  const renderToastContent = React.useCallback(
    (): React.ReactElement => (
      <Toast>
        <ToastTitle action={<Link onClick={handleViewFindings}>View findings</Link>}>
          Agreement review complete
        </ToastTitle>
      </Toast>
    ),
    [handleViewFindings]
  );

  usePaneEvent("workspace", (event) => {
    if (event.type === "active_widget_changed") {
      activeWidgetTypeRef.current = event.widgetType;
      return;
    }

    if (event.type !== "compose_advisory_comments") return;

    // Suppress — the Compose surface is already the visible workspace tab
    // (don't announce what's already on screen).
    if (activeWidgetTypeRef.current === "compose") return;

    const content = renderToastContent();

    if (toastActiveRef.current) {
      // Bounded stacking: a second completion (e.g. multi-select batch review,
      // task 041) UPDATES the existing toast in place instead of stacking a
      // new one — `dispatchToast` with a still-active `toastId` is a documented
      // no-op in @fluentui/react-toast, so `updateToast` is required here.
      updateToast({ toastId: REVIEW_COMPLETE_TOAST_ID, content, intent: "success" });
      return;
    }

    toastActiveRef.current = true;
    dispatchToast(content, {
      toastId: REVIEW_COMPLETE_TOAST_ID,
      intent: "success",
      timeout: REVIEW_COMPLETE_TOAST_TIMEOUT_MS,
      // aria-live politeness: left unset — Fluent's default politeness for a
      // 'success' intent applies (no explicit override).
      onStatusChange: handleStatusChange,
    });
  });

  return null;
}

export default ReviewCompleteToast;
