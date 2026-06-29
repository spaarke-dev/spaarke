/**
 * DailyBriefingApp — top-level composer for the Daily Briefing surface.
 *
 * Composes the digest header, AI-narrated TL;DR, channel sections, and
 * caught-up footer into a single self-contained app shell. Resolves Xrm via
 * frame-walking with polling (welcome-screen / left-nav timing) and wires
 * data, narration, and inline To-Do creation hooks.
 *
 * Hoisted into `@spaarke/daily-briefing-components/components` by R2 task 011
 * (Wave 3 / Group A). Source of truth; the original-location top-level entry
 * at `src/solutions/DailyBriefing/src/App.tsx` is now a re-export shim
 * pending full cleanup in R2 task 017.
 *
 * INTERIM IMPORT NOTES (post-task 014):
 *   - `hooks/*` are now consumed from the hoisted barrel `../hooks`.
 *   - Notification data is composed from three independent hooks per FR-06:
 *     `useBriefingNotifications` + `useBriefingPreferences` + `useBriefingActions`.
 *     Cross-hook coordination happens at THIS consumer via effects (Option A —
 *     see effect-coordination block below). The hooks themselves share NO
 *     internal state; this is intentional per FR-06.
 *   - `types/notifications` and `utils/toastUtils` will be hoisted in
 *     R2 task 015 (toastUtils) / task 016 (types/utils consolidation).
 *   - Until then, this component reaches back across the package boundary
 *     via a relative path for `types/notifications` and `utils/toastUtils` —
 *     intentional, temporary debt cleaned up in task 015/016.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Spinner,
  Toaster,
  useToastController,
  useId,
  Toast,
  ToastTitle,
  ToastBody,
} from '@fluentui/react-components';
import { DigestHeader } from './DigestHeader';
import { EmptyState } from './EmptyState';
import { TldrSection } from './TldrSection';
import { ActivityNotesSection } from './ActivityNotesSection';
import { CaughtUpFooter } from './CaughtUpFooter';
import { PreferencesDropdown } from './PreferencesDropdown';
import {
  useBriefingNarration,
  useInlineTodoCreate,
  useBriefingNotifications,
  useBriefingPreferences,
  useBriefingActions,
} from '../hooks';
import { TOASTER_ID } from '../utils/toastUtils';
import type { IWebApi, ChannelFetchResult } from '../types/notifications';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    boxSizing: 'border-box',
  },
  spinnerContainer: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    padding: tokens.spacingHorizontalL,
    boxSizing: 'border-box',
    justifyContent: 'center',
    alignItems: 'center',
  },
  scrollContent: {
    padding: tokens.spacingHorizontalL,
    overflowY: 'auto',
    flex: 1,
  },
  activitySection: {
    marginTop: tokens.spacingVerticalXXL,
  },
});

export interface DailyBriefingAppProps {
  params: Record<string, string>;
  /**
   * R7 task 095 / FR-18 — host-supplied callback for the "Browse Playbooks"
   * overflow menu item on the DigestHeader. The standalone DailyBriefing
   * Code Page and the SpaarkeAi briefing widget each wire this to their own
   * `Xrm.Navigation.navigateTo({pageType:'webresource',
   * webresourceName:'sprk_playbooklibrary', data:''}, {target:2, ...})`
   * thunk (shared lib stays Xrm-free per ADR-012). The launch reaches the
   * existing Library Code Page wrapper which preserves Path A.5 routing
   * (`IConsumerRoutingService → IInvokePlaybookAi`) per ADR-013.
   *
   * Optional — when omitted, the overflow menu is not rendered (back-compat
   * for non-Dataverse hosts).
   */
  onBrowsePlaybooks?: () => void;
}

/**
 * DailyBriefingApp — top-level composer for the Daily Briefing surface.
 *
 * Integrates notification data, AI narration, inline to-do creation,
 * and preferences via a narrative digest layout.
 */
export const DailyBriefingApp: React.FC<DailyBriefingAppProps> = ({ params: _params, onBrowsePlaybooks }) => {
  const styles = useStyles();

  // Resolve Xrm via frame-walking with polling for welcome screen timing.
  // Xrm may not be available immediately when loaded as MDA welcome screen.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const [xrm, setXrm] = React.useState<any>(() => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const w = window as any;
      return w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm ?? null;
    } catch {
      return null;
    }
  });

  // Poll for Xrm if not available on mount (welcome screen / left nav timing)
  React.useEffect(() => {
    if (xrm?.WebApi) return; // Already available
    let cancelled = false;
    const interval = setInterval(() => {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const w = window as any;
        const found = w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm ?? null;
        if (found?.WebApi && !cancelled) {
          setXrm(found);
          clearInterval(interval);
        }
      } catch {
        /* cross-origin */
      }
    }, 500);
    // Stop polling after 30s
    const timeout = setTimeout(() => {
      clearInterval(interval);
    }, 30000);
    return () => {
      cancelled = true;
      clearInterval(interval);
      clearTimeout(timeout);
    };
  }, [xrm]);

  const webApi = React.useMemo<IWebApi | null>(() => xrm?.WebApi ?? null, [xrm]);

  // Resolve current user ID
  const userId = React.useMemo<string>(() => {
    try {
      return xrm?.Utility?.getGlobalContext()?.userSettings?.userId?.replace(/[{}]/g, '') ?? '';
    } catch {
      return '';
    }
  }, [xrm]);

  // ---------------------------------------------------------------------------
  // Notification data — composed from three independent hooks per FR-06.
  //
  // Cross-hook coordination (Option A — consumer-layer effect-based):
  //   - When `preferences.disabledChannels` changes, refetch notifications so
  //     the filtered set is in sync.
  //   - When `actionsRefresh` bumps (any successful mark-read / mark-all-read /
  //     dismiss), refetch notifications so the rendered state matches Dataverse.
  //
  // The three hooks intentionally share NO internal state. Channel filtering
  // by `disabledChannels` happens HERE at the consumer (downstream of fetch).
  // See task 014 / FR-06 / spec.md.
  // ---------------------------------------------------------------------------
  const { channels: allChannels, loadingState, refetch } = useBriefingNotifications(webApi);
  const { preferences, updatePreferences } = useBriefingPreferences(webApi, userId);
  // R3 task 031 — destructure the three new per-item action handlers added by
  // task 030 (`markChecked`, `markRemoved`, `extendTtl`). Each is a JSX-agnostic
  // promise-returning function accepting an optional `BriefingActionOptions`
  // bag with `onOptimistic` / `onSuccess` / `onRevert` / `onError` callbacks.
  // We compose toast dispatch + optimistic-removal local state below (see the
  // `handleCheck` / `handleRemove` / `handleKeep` callbacks). The existing
  // `markAsRead` is preserved for ADR-024 ("Add to To Do" auto-mark-read).
  const { markAsRead, markChecked, markRemoved, extendTtl, refresh: actionsRefresh } = useBriefingActions(webApi);

  // R3 task 031 — optimistic-UI ledger for the 3 new per-item actions.
  //
  // The 3 actions write to `sprk_briefingstate` (Checked / Removed) or
  // `ttlinseconds` (Keep +7d). Since `useBriefingActions` is JSX-agnostic, the
  // consumer owns the optimistic-vs-confirmed flip. Item IDs we have
  // *optimistically* flipped to Checked / Removed are tracked here; the next
  // `refetch` (triggered by `actionsRefresh` bump on success — see Effect 2)
  // replaces this overlay with fresh server state. Failure path calls
  // `setOptimisticChecked` / `setOptimisticRemoved` to remove the ID, snapping
  // the UI back. Keep +7d has no immediate UI effect; tracked here only for
  // potential future spinner UX.
  const [optimisticChecked, setOptimisticChecked] = React.useState<Set<string>>(() => new Set<string>());
  const [optimisticRemoved, setOptimisticRemoved] = React.useState<Set<string>>(() => new Set<string>());

  // Effect 1: refetch when disabled-channels set changes.
  // Cross-hook coordination at the consumer (per FR-06 Option A).
  React.useEffect(() => {
    refetch();
    // We deliberately omit `refetch` from deps: it's a stable useCallback
    // reference from useBriefingNotifications, and including it would only
    // re-trigger when the hook itself re-mounts, which already triggers fetch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [preferences.disabledChannels]);

  // Effect 2: refetch after any mutation action (mark-read / mark-all / dismiss).
  React.useEffect(() => {
    if (actionsRefresh === 0) return; // skip initial render
    refetch();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [actionsRefresh]);

  // Apply `disabledChannels` filter at the consumer (was previously inside
  // useNotificationData). Errors always show through regardless of filter.
  //
  // R3 task 031 — also apply the optimistic overlay:
  //   - Items in `optimisticRemoved` are filtered out (visually removed
  //     before the server-side refetch lands).
  //   - Items in `optimisticChecked` have `isRead: true` overlaid so they
  //     render as checked (matches the server-side `sprk_briefingstate = 1`
  //     write that lands after refetch).
  //   - `unreadCount` is recomputed against the overlay so the digest header
  //     count and "caught up" detection stay in sync.
  const channels: ChannelFetchResult[] = React.useMemo(
    () =>
      allChannels
        .filter(ch => {
          if (ch.status !== 'success') return true; // always show errors
          return !preferences.disabledChannels.includes(ch.group.meta.category);
        })
        .map(ch => {
          if (ch.status !== 'success') return ch;
          if (optimisticChecked.size === 0 && optimisticRemoved.size === 0) return ch;
          const filteredItems = ch.group.items
            .filter(item => !optimisticRemoved.has(item.id))
            .map(item => (optimisticChecked.has(item.id) ? { ...item, isRead: true } : item));
          return {
            ...ch,
            group: {
              ...ch.group,
              items: filteredItems,
              unreadCount: filteredItems.filter(item => !item.isRead).length,
            },
          };
        }),
    [allChannels, preferences.disabledChannels, optimisticChecked, optimisticRemoved]
  );

  // Total unread count after filtering.
  const totalUnreadCount = React.useMemo(
    () =>
      channels.reduce((sum, ch) => {
        if (ch.status === 'success') {
          return sum + ch.group.unreadCount;
        }
        return sum;
      }, 0),
    [channels]
  );

  const refresh = refetch;

  // AI narration — fetches TL;DR + per-channel narrative bullets from BFF
  const {
    tldr,
    channelNarratives,
    isLoading: narrationLoading,
    isUnavailable,
    unavailableReason,
    error: narrationError,
    generatedAt,
  } = useBriefingNarration(channels, loadingState);

  // Inline To Do creation from narrative bullets
  const { createTodo, isCreated, isPending, getError: getTodoError } = useInlineTodoCreate(webApi);

  // Toaster setup for success/error notifications
  const toasterId = useId(TOASTER_ID);
  const { dispatchToast } = useToastController(toasterId);

  // ---------------------------------------------------------------------------
  // Handlers
  // ---------------------------------------------------------------------------

  /**
   * Add a notification item to To Do and show a confirmation toast.
   * R2.2 Item 3: shows the To Do title in the toast and surfaces a failure
   * toast when createTodo throws (existing tooltip-only error path was too
   * easy to miss).
   */
  const handleAddToTodo = React.useCallback(
    async (itemIds: string[]) => {
      for (const ch of channels) {
        if (ch.status !== 'success') continue;
        for (const item of ch.group.items) {
          if (itemIds.includes(item.id)) {
            try {
              await createTodo(item);
              // useInlineTodoCreate swallows exceptions and surfaces them via
              // its getError() — check that path too so the user sees a toast
              // even when the underlying createRecord call failed.
              const err = getTodoError(item.id);
              if (err) {
                dispatchToast(
                  <Toast>
                    <ToastTitle>Could not add to To Do</ToastTitle>
                    <ToastBody>{err}</ToastBody>
                  </Toast>,
                  { intent: 'error', timeout: 5000 }
                );
              } else {
                dispatchToast(
                  <Toast>
                    <ToastTitle>Added to To Do</ToastTitle>
                    <ToastBody>{item.title}</ToastBody>
                  </Toast>,
                  { intent: 'success', timeout: 3000 }
                );
                // Mark notification as read only on success
                markAsRead?.(item.id);
              }
            } catch (e) {
              // Defensive: createTodo isn't supposed to throw (it catches
              // internally), but if a future change re-throws we still want
              // the user to see a toast.
              dispatchToast(
                <Toast>
                  <ToastTitle>Could not add to To Do</ToastTitle>
                  <ToastBody>{e instanceof Error ? e.message : String(e)}</ToastBody>
                </Toast>,
                { intent: 'error', timeout: 5000 }
              );
            }
            return;
          }
        }
      }
    },
    [channels, createTodo, getTodoError, dispatchToast, markAsRead]
  );

  /** Dismiss notification items by marking them as read. */
  const handleDismiss = React.useCallback(
    (itemIds: string[]) => {
      for (const id of itemIds) {
        markAsRead?.(id);
      }
    },
    [markAsRead]
  );

  // ---------------------------------------------------------------------------
  // R3 task 031 — 3 new per-item handlers (FR-4 / FR-5 / FR-6).
  //
  // Each handler:
  //   1. Destructures the corresponding `useBriefingActions` hook function
  //      (which is JSX-agnostic per task 030 design).
  //   2. Provides an options bag with `onOptimistic` (flip local Set →
  //      overlays UI immediately), `onSuccess` (dispatch success toast),
  //      `onRevert` (un-flip local Set on failure), and `onError`
  //      (dispatch error toast).
  //   3. The `actionsRefresh` bump on success (inside the hook) triggers
  //      Effect 2 → refetch → the optimistic overlay is replaced by fresh
  //      server data on next render cycle.
  //
  // Mirrors the existing `handleAddToTodo` toast-dispatch pattern. Per ADR-024,
  // existing "Add to To Do" behavior is preserved unchanged.
  // ---------------------------------------------------------------------------

  /** R3 FR-4 — mark a single briefing item as Checked (read in widget terms). */
  const handleCheck = React.useCallback(
    (itemId: string) => {
      void markChecked(itemId, {
        onOptimistic: id => {
          setOptimisticChecked(prev => {
            const next = new Set(prev);
            next.add(id);
            return next;
          });
        },
        onSuccess: () => {
          dispatchToast(
            <Toast>
              <ToastTitle>Marked as read</ToastTitle>
            </Toast>,
            { intent: 'success', timeout: 3000 }
          );
        },
        onRevert: id => {
          setOptimisticChecked(prev => {
            const next = new Set(prev);
            next.delete(id);
            return next;
          });
        },
        onError: err => {
          dispatchToast(
            <Toast>
              <ToastTitle>Could not mark as read</ToastTitle>
              <ToastBody>{err.message}</ToastBody>
            </Toast>,
            { intent: 'error', timeout: 5000 }
          );
        },
      });
    },
    [markChecked, dispatchToast]
  );

  /** R3 FR-5 — remove a single briefing item from the widget (does not delete record). */
  const handleRemove = React.useCallback(
    (itemId: string) => {
      void markRemoved(itemId, {
        onOptimistic: id => {
          setOptimisticRemoved(prev => {
            const next = new Set(prev);
            next.add(id);
            return next;
          });
        },
        onSuccess: () => {
          dispatchToast(
            <Toast>
              <ToastTitle>Removed from briefing</ToastTitle>
            </Toast>,
            { intent: 'success', timeout: 3000 }
          );
        },
        onRevert: id => {
          setOptimisticRemoved(prev => {
            const next = new Set(prev);
            next.delete(id);
            return next;
          });
        },
        onError: err => {
          dispatchToast(
            <Toast>
              <ToastTitle>Could not remove from briefing</ToastTitle>
              <ToastBody>{err.message}</ToastBody>
            </Toast>,
            { intent: 'error', timeout: 5000 }
          );
        },
      });
    },
    [markRemoved, dispatchToast]
  );

  /**
   * R4 task 046+047 / FR-18 + FR-19 — open a Dataverse record in a modal dialog.
   *
   * Single code path for both entry points (regarding-name link click in
   * NarrativeBullet AND the "Open record" overflow menu item):
   *
   *   - Calls `Xrm.Navigation.navigateTo({pageType: 'entityrecord', entityName,
   *     entityId}, {target: 2, width: {value: 80, unit: '%'}, height: {value: 80,
   *     unit: '%'}})`. `target: 2` opens a dialog overlay; 80%×80% sizing matches
   *     the FR-19 spec.
   *
   *   - On `.catch(err)` dispatches a non-blocking Fluent v9 Toaster toast with
   *     intent `'warning'` and message "Cannot open record — you may not have
   *     access." (AC-19b). This covers Dataverse 403 (no read privilege) AND
   *     all other navigation rejections (record not found, model-driven app
   *     missing form, etc.). The toast surfaces a graceful degradation cue
   *     instead of an error overlay or silent failure.
   *
   * The Toaster instance is mounted once at app root (line ~545); this handler
   * uses the shared `dispatchToast` controller obtained via `useToastController`.
   *
   * Xrm is resolved via the polled `xrm` state (welcome-screen timing). If
   * `Xrm.Navigation.navigateTo` is unavailable (e.g., outside a model-driven app
   * host), the toast is dispatched directly so the user still gets a cue.
   */
  const handleOpenRecord = React.useCallback(
    (entityType: string, entityId: string) => {
      if (!entityType || !entityId) return;
      const dispatchAccessToast = (): void => {
        dispatchToast(
          <Toast>
            <ToastTitle>Cannot open record</ToastTitle>
            <ToastBody>You may not have access.</ToastBody>
          </Toast>,
          { intent: 'warning', timeout: 5000 }
        );
      };
      const navigateTo: ((page: object, options?: object) => Promise<unknown>) | undefined =
        xrm?.Navigation?.navigateTo;
      if (typeof navigateTo !== 'function') {
        dispatchAccessToast();
        return;
      }
      navigateTo(
        {
          pageType: 'entityrecord',
          entityName: entityType,
          entityId: entityId,
        },
        { target: 2, width: { value: 80, unit: '%' }, height: { value: 80, unit: '%' } }
      ).catch(() => {
        // FR-19 / AC-19b — 403 (and any other navigation rejection) surfaces a
        // non-blocking toast. We do not differentiate error codes because the
        // Xrm.Navigation contract does not guarantee structured error info; the
        // user-facing message is the same regardless.
        dispatchAccessToast();
      });
    },
    [xrm, dispatchToast]
  );

  /**
   * R3 FR-6 — extend a single briefing item's TTL by 7 calendar days. The
   * `onSuccess(newTtlSeconds)` callback receives the new TTL value so the toast
   * can render the effective expiry date. The expiry is computed from
   * `createdon + newTtlSeconds`; locating the corresponding `NotificationItem`
   * by id supplies the `createdOn` ISO timestamp. If the item cannot be found
   * (defensive — race condition with refetch), the toast falls back to a
   * generic "7 more days" copy.
   */
  const handleKeep = React.useCallback(
    (itemId: string, currentTtlSeconds: number) => {
      void extendTtl(itemId, currentTtlSeconds, {
        // No immediate UI change — Keep +7d is silent until the toast lands.
        // We could surface a transient spinner here in future UX iteration.
        onSuccess: newTtlSeconds => {
          // Locate the item to compute the new effective expiry date.
          let createdOn: string | undefined;
          for (const ch of channels) {
            if (ch.status !== 'success') continue;
            const found = ch.group.items.find(it => it.id === itemId);
            if (found) {
              createdOn = found.createdOn;
              break;
            }
          }
          let body: string | undefined;
          if (createdOn) {
            try {
              const newExpiry = new Date(new Date(createdOn).getTime() + newTtlSeconds * 1000);
              const formatter = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' });
              body = `New expiry: ${formatter.format(newExpiry)}`;
            } catch {
              /* fall through to default body */
            }
          }
          dispatchToast(
            <Toast>
              <ToastTitle>Kept on briefing for 7 more days</ToastTitle>
              {body ? <ToastBody>{body}</ToastBody> : null}
            </Toast>,
            { intent: 'success', timeout: 3000 }
          );
        },
        onError: err => {
          dispatchToast(
            <Toast>
              <ToastTitle>Could not extend briefing</ToastTitle>
              <ToastBody>{err.message}</ToastBody>
            </Toast>,
            { intent: 'error', timeout: 5000 }
          );
        },
      });
    },
    [extendTtl, channels, dispatchToast]
  );

  // ---------------------------------------------------------------------------
  // Computed: channels that are caught up (no narrative bullets)
  // ---------------------------------------------------------------------------

  const caughtUpLabels = React.useMemo(() => {
    const activeCategories = new Set(channelNarratives.map(cn => cn.category));
    return channels
      .filter(ch => ch.status === 'success' && !activeCategories.has(ch.group.meta.category))
      .map(ch => {
        if (ch.status === 'success') return ch.group.meta.label;
        return '';
      })
      .filter(Boolean);
  }, [channels, channelNarratives]);

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  if (loadingState === 'loading' || loadingState === 'idle') {
    return (
      <div className={styles.spinnerContainer}>
        <Spinner label="Loading daily briefing..." />
      </div>
    );
  }

  // All caught up — no unread notifications at all
  if (totalUnreadCount === 0 && channels.every(ch => ch.status === 'success') && !narrationLoading) {
    return (
      <div className={styles.container}>
        <DigestHeader
          totalUnreadCount={totalUnreadCount}
          onRefresh={refresh}
          preferencesSlot={<PreferencesDropdown preferences={preferences} onUpdatePreferences={updatePreferences} />}
          onBrowsePlaybooks={onBrowsePlaybooks}
        />
        <div className={styles.scrollContent}>
          <EmptyState />
        </div>
        <Toaster toasterId={toasterId} position="bottom-end" />
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <Toaster toasterId={toasterId} position="bottom-end" />
      <DigestHeader
        totalUnreadCount={totalUnreadCount}
        onRefresh={refresh}
        preferencesSlot={<PreferencesDropdown preferences={preferences} onUpdatePreferences={updatePreferences} />}
        onBrowsePlaybooks={onBrowsePlaybooks}
      />
      <div className={styles.scrollContent}>
        <TldrSection
          tldr={tldr}
          isLoading={narrationLoading}
          isUnavailable={isUnavailable}
          unavailableReason={unavailableReason}
          error={narrationError}
          generatedAt={generatedAt}
        />
        <div className={styles.activitySection}>
          <ActivityNotesSection
            channelNarratives={channelNarratives}
            channels={channels}
            onAddToTodo={handleAddToTodo}
            onDismiss={handleDismiss}
            isTodoCreated={isCreated}
            isTodoPending={isPending}
            getTodoError={getTodoError}
            isLoading={narrationLoading}
            // R3 task 031 — wire the 3 new per-item actions (FR-4 / FR-5 / FR-6).
            // Each handler composes the corresponding `useBriefingActions` hook
            // function with optimistic-overlay state + toast dispatch.
            onCheck={handleCheck}
            onRemove={handleRemove}
            onKeep={handleKeep}
            // R4 task 046+047 / FR-18 + FR-19 — single Open record path. The
            // handler dispatches the navigation modal AND surfaces a Toaster
            // toast on 403 (or any other rejection) via the app-root Toaster
            // instance mounted below.
            onOpenRecord={handleOpenRecord}
          />
        </div>
        <CaughtUpFooter channelLabels={caughtUpLabels} />
      </div>
    </div>
  );
};
