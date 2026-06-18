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

import * as React from "react";
import {
  makeStyles,
  tokens,
  Spinner,
  Toaster,
  useToastController,
  useId,
  Toast,
  ToastTitle,
} from "@fluentui/react-components";
import { DigestHeader } from "./DigestHeader";
import { EmptyState } from "./EmptyState";
import { TldrSection } from "./TldrSection";
import { ActivityNotesSection } from "./ActivityNotesSection";
import { CaughtUpFooter } from "./CaughtUpFooter";
import { PreferencesDropdown } from "./PreferencesDropdown";
import {
  useBriefingNarration,
  useInlineTodoCreate,
  useBriefingNotifications,
  useBriefingPreferences,
  useBriefingActions,
} from "../hooks";
import { TOASTER_ID } from "../../../../../solutions/DailyBriefing/src/utils/toastUtils";
import type {
  IWebApi,
  ChannelFetchResult,
} from "../../../../../solutions/DailyBriefing/src/types/notifications";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    boxSizing: "border-box",
  },
  spinnerContainer: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    padding: tokens.spacingHorizontalL,
    boxSizing: "border-box",
    justifyContent: "center",
    alignItems: "center",
  },
  scrollContent: {
    padding: tokens.spacingHorizontalL,
    overflowY: "auto",
    flex: 1,
  },
  activitySection: {
    marginTop: tokens.spacingVerticalXXL,
  },
});

export interface DailyBriefingAppProps {
  params: Record<string, string>;
}

/**
 * DailyBriefingApp — top-level composer for the Daily Briefing surface.
 *
 * Integrates notification data, AI narration, inline to-do creation,
 * and preferences via a narrative digest layout.
 */
export const DailyBriefingApp: React.FC<DailyBriefingAppProps> = ({ params: _params }) => {
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
      } catch { /* cross-origin */ }
    }, 500);
    // Stop polling after 30s
    const timeout = setTimeout(() => { clearInterval(interval); }, 30000);
    return () => { cancelled = true; clearInterval(interval); clearTimeout(timeout); };
  }, [xrm]);

  const webApi = React.useMemo<IWebApi | null>(() => xrm?.WebApi ?? null, [xrm]);

  // Resolve current user ID
  const userId = React.useMemo<string>(() => {
    try {
      return xrm?.Utility?.getGlobalContext()?.userSettings?.userId?.replace(/[{}]/g, "") ?? "";
    } catch {
      return "";
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
  const {
    channels: allChannels,
    loadingState,
    refetch,
  } = useBriefingNotifications(webApi);
  const { preferences, updatePreferences } = useBriefingPreferences(webApi, userId);
  const {
    markAsRead,
    refresh: actionsRefresh,
  } = useBriefingActions(webApi);

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
  const channels: ChannelFetchResult[] = React.useMemo(
    () =>
      allChannels.filter((ch) => {
        if (ch.status !== "success") return true; // always show errors
        return !preferences.disabledChannels.includes(ch.group.meta.category);
      }),
    [allChannels, preferences.disabledChannels]
  );

  // Total unread count after filtering.
  const totalUnreadCount = React.useMemo(
    () =>
      channels.reduce((sum, ch) => {
        if (ch.status === "success") {
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
  const { createTodo, isCreated, isPending, getError: getTodoError } =
    useInlineTodoCreate(webApi);

  // Toaster setup for success/error notifications
  const toasterId = useId(TOASTER_ID);
  const { dispatchToast } = useToastController(toasterId);

  // ---------------------------------------------------------------------------
  // Handlers
  // ---------------------------------------------------------------------------

  /** Add a notification item to To Do and show a toast. */
  const handleAddToTodo = React.useCallback(
    async (itemIds: string[]) => {
      for (const ch of channels) {
        if (ch.status !== "success") continue;
        for (const item of ch.group.items) {
          if (itemIds.includes(item.id)) {
            await createTodo(item);
            dispatchToast(
              <Toast><ToastTitle>Added to To Do</ToastTitle></Toast>,
              { intent: "success", timeout: 3000 }
            );
            // Also mark notification as read
            markAsRead?.(item.id);
            return;
          }
        }
      }
    },
    [channels, createTodo, dispatchToast, markAsRead]
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
  // Computed: channels that are caught up (no narrative bullets)
  // ---------------------------------------------------------------------------

  const caughtUpLabels = React.useMemo(() => {
    const activeCategories = new Set(
      channelNarratives.map((cn) => cn.category)
    );
    return channels
      .filter(
        (ch) =>
          ch.status === "success" &&
          !activeCategories.has(ch.group.meta.category)
      )
      .map((ch) => {
        if (ch.status === "success") return ch.group.meta.label;
        return "";
      })
      .filter(Boolean);
  }, [channels, channelNarratives]);

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  if (loadingState === "loading" || loadingState === "idle") {
    return (
      <div className={styles.spinnerContainer}>
        <Spinner label="Loading daily briefing..." />
      </div>
    );
  }

  // All caught up — no unread notifications at all
  if (
    totalUnreadCount === 0 &&
    channels.every((ch) => ch.status === "success") &&
    !narrationLoading
  ) {
    return (
      <div className={styles.container}>
        <DigestHeader
          totalUnreadCount={totalUnreadCount}
          onRefresh={refresh}
          preferencesSlot={
            <PreferencesDropdown
              preferences={preferences}
              onUpdatePreferences={updatePreferences}
            />
          }
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
        preferencesSlot={
          <PreferencesDropdown
            preferences={preferences}
            onUpdatePreferences={updatePreferences}
          />
        }
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
          />
        </div>
        <CaughtUpFooter channelLabels={caughtUpLabels} />
      </div>
    </div>
  );
};
