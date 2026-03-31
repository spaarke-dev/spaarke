/**
 * ChannelCard — Collapsible card per notification category/channel.
 *
 * Renders a Fluent v9 Accordion item containing the channel header
 * (icon, label, unread badge) and a list of NotificationItem entries.
 * When the channel fetch failed, renders ChannelErrorState inline.
 *
 * Uses Fluent v9 tokens exclusively (ADR-021). No hard-coded colors.
 * Uses @fluentui/react-icons for channel icons.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Accordion,
  AccordionItem,
  AccordionHeader,
  AccordionPanel,
  Badge,
  Button,
  Card,
  Divider,
} from "@fluentui/react-components";
import {
  CheckmarkCircleRegular,
} from "@fluentui/react-icons";
import type { ChannelFetchResult } from "../types/notifications";
import { CHANNEL_REGISTRY } from "../types/notifications";
import { NotificationItem } from "./NotificationItem";
import { ChannelErrorState } from "./ChannelErrorState";
import { getChannelIcon } from "./channelIcons";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    padding: "0",
    overflow: "hidden",
  },
  headerContent: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    width: "100%",
  },
  channelIcon: {
    fontSize: "20px",
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  channelLabel: {
    flex: 1,
    fontWeight: tokens.fontWeightSemibold,
  },
  badge: {
    flexShrink: 0,
  },
  panel: {
    display: "flex",
    flexDirection: "column",
    gap: "0",
  },
  footer: {
    display: "flex",
    justifyContent: "flex-end",
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ChannelCardProps {
  /** Channel fetch result — success with items or error state. */
  channelResult: ChannelFetchResult;
  /** Whether this accordion item should be open by default. */
  defaultOpen?: boolean;
  /** Called when a notification is marked as read. */
  onMarkAsRead?: (notificationId: string) => void;
  /** Called when user navigates to a notification's source record. */
  onNavigate?: (actionUrl: string) => void;
  /** Called to retry a failed channel fetch. */
  onRetry?: () => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ChannelCard: React.FC<ChannelCardProps> = ({
  channelResult,
  defaultOpen,
  onMarkAsRead,
  onNavigate,
  onRetry,
}) => {
  const styles = useStyles();

  // Determine channel metadata and content based on result status
  const isError = channelResult.status === "error";
  const category = isError
    ? channelResult.category
    : channelResult.group.meta.category;
  const meta = CHANNEL_REGISTRY[category];
  const unreadCount = isError ? 0 : channelResult.group.unreadCount;
  const items = isError ? [] : channelResult.group.items;

  const ChannelIcon = getChannelIcon(meta.iconName);
  const accordionValue = `channel-${category}`;

  return (
    <Card className={styles.card} size="small">
      <Accordion
        defaultOpenItems={defaultOpen ? [accordionValue] : []}
        collapsible
      >
        <AccordionItem value={accordionValue}>
          <AccordionHeader expandIconPosition="end">
            <div className={styles.headerContent}>
              <ChannelIcon className={styles.channelIcon} />
              <span className={styles.channelLabel}>{meta.label}</span>
              {unreadCount > 0 && (
                <Badge
                  className={styles.badge}
                  appearance="filled"
                  color="brand"
                  size="small"
                >
                  {unreadCount}
                </Badge>
              )}
              {!isError && unreadCount === 0 && items.length > 0 && (
                <CheckmarkCircleRegular
                  style={{ color: tokens.colorPaletteGreenForeground1 }}
                />
              )}
            </div>
          </AccordionHeader>
          <AccordionPanel>
            <div className={styles.panel}>
              {isError ? (
                <ChannelErrorState
                  category={channelResult.category}
                  errorMessage={channelResult.error}
                  onRetry={onRetry}
                />
              ) : (
                <>
                  {items.map((item, idx) => (
                    <React.Fragment key={item.id}>
                      {idx > 0 && <Divider />}
                      <NotificationItem
                        item={item}
                        onMarkAsRead={onMarkAsRead}
                        onNavigate={onNavigate}
                      />
                    </React.Fragment>
                  ))}
                  {items.length === 0 && (
                    <div
                      style={{
                        padding: tokens.spacingVerticalL,
                        textAlign: "center",
                        color: tokens.colorNeutralForeground3,
                      }}
                    >
                      No notifications in this channel.
                    </div>
                  )}
                </>
              )}
            </div>
            {!isError && items.some((i) => !i.isRead) && onMarkAsRead && (
              <>
                <Divider />
                <div className={styles.footer}>
                  <Button
                    appearance="subtle"
                    size="small"
                    onClick={() => {
                      items
                        .filter((i) => !i.isRead)
                        .forEach((i) => onMarkAsRead(i.id));
                    }}
                  >
                    Mark all as read
                  </Button>
                </div>
              </>
            )}
          </AccordionPanel>
        </AccordionItem>
      </Accordion>
    </Card>
  );
};
