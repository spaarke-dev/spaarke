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
import { DigestHeader } from "./components/DigestHeader";
import { EmptyState } from "./components/EmptyState";
import { TldrSection } from "./components/TldrSection";
import { ActivityNotesSection } from "./components/ActivityNotesSection";
import { CaughtUpFooter } from "./components/CaughtUpFooter";
import { PreferencesDropdown } from "./components/PreferencesDropdown";
import { useBriefingNarration } from "./hooks/useBriefingNarration";
import { useInlineTodoCreate } from "./hooks/useInlineTodoCreate";
import { useNotificationData } from "./hooks/useNotificationData";
import { TOASTER_ID } from "./utils/toastUtils";
import type { IWebApi } from "./types/notifications";

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

export interface AppProps {
  params: Record<string, string>;
}

/**
 * DailyBriefing App shell component.
 *
 * This is the root component for the Daily Briefing Code Page.
 * Integrates notification data, AI narration, inline to-do creation,
 * and preferences via a narrative digest layout.
 */
export const App: React.FC<AppProps> = ({ params: _params }) => {
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

  const { channels, totalUnreadCount, preferences, loadingState, actions } =
    useNotificationData({ webApi, userId });

  const { markAsRead, refresh, updatePreferences } = actions;

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
  // Navigation helper (kept for potential future use)
  // ---------------------------------------------------------------------------

  const _handleNavigate = React.useCallback(
    (actionUrl: string, regardingEntityType?: string, regardingId?: string) => {
      try {
        if (xrm?.Navigation?.navigateTo && regardingEntityType && regardingId) {
          xrm.Navigation.navigateTo(
            { pageType: "entityrecord", entityName: regardingEntityType, entityId: regardingId },
            { target: 2, width: { value: 80, unit: "%" }, height: { value: 80, unit: "%" } }
          );
          return;
        }
        if (xrm?.Navigation?.navigateTo && actionUrl.includes("pagetype=entityrecord")) {
          const params = new URLSearchParams(actionUrl.split("?")[1] ?? "");
          const etn = params.get("etn");
          const id = params.get("id");
          if (etn && id) {
            xrm.Navigation.navigateTo(
              { pageType: "entityrecord", entityName: etn, entityId: id },
              { target: 2, width: { value: 80, unit: "%" }, height: { value: 80, unit: "%" } }
            );
            return;
          }
        }
        if (xrm?.Navigation?.openUrl) {
          xrm.Navigation.openUrl(actionUrl);
        } else {
          window.open(actionUrl, "_blank");
        }
      } catch {
        window.open(actionUrl, "_blank");
      }
    },
    [xrm]
  );

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
