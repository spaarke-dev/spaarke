import * as React from "react";
import {
  makeStyles,
  tokens,
  Spinner,
} from "@fluentui/react-components";
import { AiBriefing } from "./components/AiBriefing";
import { ChannelCard } from "./components/ChannelCard";
import { DigestHeader } from "./components/DigestHeader";
import { EmptyState } from "./components/EmptyState";
import { NarrativeSummary } from "./components/NarrativeSummary";
import { PreferencesPanel } from "./components/PreferencesPanel";
import { useAiBriefing } from "./hooks/useAiBriefing";
import { useNotificationData } from "./hooks/useNotificationData";
import type { IWebApi, ChannelGroup } from "./types/notifications";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    padding: tokens.spacingHorizontalL,
    boxSizing: "border-box",
    gap: tokens.spacingVerticalM,
  },
  content: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    overflowY: "auto",
  },
});

export interface AppProps {
  params: Record<string, string>;
}

/**
 * DailyBriefing App shell component.
 *
 * This is the root component for the Daily Briefing Code Page.
 * Integrates notification data, channel views, and preferences panel.
 */
export const App: React.FC<AppProps> = ({ params: _params }) => {
  const styles = useStyles();

  // Resolve Xrm.WebApi — available when hosted in Dataverse Code Page
  const webApi = React.useMemo<IWebApi | null>(() => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (window as any).Xrm;
      return xrm?.WebApi ?? null;
    } catch {
      return null;
    }
  }, []);

  // Resolve current user ID
  const userId = React.useMemo<string>(() => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (window as any).Xrm;
      return xrm?.Utility?.getGlobalContext()?.userSettings?.userId?.replace(/[{}]/g, "") ?? "";
    } catch {
      return "";
    }
  }, []);

  const { channels, totalUnreadCount, preferences, loadingState, actions } =
    useNotificationData({ webApi, userId });

  // AI briefing — fetches summary from BFF once notification data is loaded
  const aiBriefingResult = useAiBriefing(channels, loadingState);

  const [isMarkingAll, setIsMarkingAll] = React.useState(false);

  const handleMarkAllRead = React.useCallback(async () => {
    setIsMarkingAll(true);
    try {
      await actions.markAllAsRead();
    } finally {
      setIsMarkingAll(false);
    }
  }, [actions]);

  const handleNavigate = React.useCallback((actionUrl: string) => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (window as any).Xrm;
      if (xrm?.Navigation?.openUrl) {
        xrm.Navigation.openUrl(actionUrl);
      } else {
        window.open(actionUrl, "_blank");
      }
    } catch {
      window.open(actionUrl, "_blank");
    }
  }, []);

  // Extract ChannelGroup[] from successful results for the narrative summary
  const successGroups: ChannelGroup[] = React.useMemo(
    () =>
      channels
        .filter((ch): ch is { status: "success"; group: ChannelGroup } => ch.status === "success")
        .map((ch) => ch.group),
    [channels]
  );

  if (loadingState === "loading" || loadingState === "idle") {
    return (
      <div className={styles.container}>
        <div className={styles.content}>
          <Spinner label="Loading daily briefing..." />
        </div>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <DigestHeader
        totalUnreadCount={totalUnreadCount}
        onMarkAllRead={handleMarkAllRead}
        onRefresh={actions.refresh}
        isMarkingAll={isMarkingAll}
      />
      <div className={styles.content}>
        {totalUnreadCount === 0 && channels.every((ch) => ch.status === "success") ? (
          <EmptyState />
        ) : (
          <>
            {/* AI-generated briefing summary (above everything) */}
            <AiBriefing
              briefingResult={aiBriefingResult}
              dataLoading={false}
            />

            {/* Narrative TL;DR summary */}
            {successGroups.length > 0 && (
              <NarrativeSummary groups={successGroups} />
            )}

            {/* Channel cards */}
            {channels.map((ch) => {
              const category =
                ch.status === "error" ? ch.category : ch.group.meta.category;
              return (
                <ChannelCard
                  key={category}
                  channelResult={ch}
                  defaultOpen={
                    ch.status === "success" && ch.group.unreadCount > 0
                  }
                  onMarkAsRead={actions.markAsRead}
                  onNavigate={handleNavigate}
                  onRetry={actions.refresh}
                />
              );
            })}
          </>
        )}

        {/* Preferences panel (collapsible) */}
        <PreferencesPanel
          preferences={preferences}
          onUpdatePreferences={actions.updatePreferences}
        />
      </div>
    </div>
  );
};
