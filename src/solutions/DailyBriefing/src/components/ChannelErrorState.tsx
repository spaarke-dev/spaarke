/**
 * ChannelErrorState — Inline error display when a channel's notification
 * fetch fails. Renders inside the channel card accordion panel.
 *
 * Uses Fluent v9 tokens exclusively (ADR-021). No hard-coded colors.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Body1,
  Button,
} from "@fluentui/react-components";
import {
  ErrorCircleRegular,
  ArrowClockwiseRegular,
} from "@fluentui/react-icons";
import type { NotificationCategory } from "../types/notifications";
import { CHANNEL_REGISTRY } from "../types/notifications";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    gap: tokens.spacingVerticalS,
    padding: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalM}`,
    textAlign: "center",
  },
  icon: {
    fontSize: "32px",
    color: tokens.colorPaletteRedForeground1,
  },
  message: {
    color: tokens.colorNeutralForeground3,
    maxWidth: "320px",
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ChannelErrorStateProps {
  /** The channel category that failed to load. */
  category: NotificationCategory;
  /** The error message to display. */
  errorMessage: string;
  /** Called when the user requests a retry. */
  onRetry?: () => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ChannelErrorState: React.FC<ChannelErrorStateProps> = ({
  category,
  errorMessage,
  onRetry,
}) => {
  const styles = useStyles();
  const channelLabel = CHANNEL_REGISTRY[category]?.label ?? category;

  return (
    <div className={styles.root} role="alert">
      <ErrorCircleRegular className={styles.icon} />
      <Body1 className={styles.message}>
        Unable to load {channelLabel}. {errorMessage}
      </Body1>
      {onRetry && (
        <Button
          appearance="subtle"
          icon={<ArrowClockwiseRegular />}
          onClick={onRetry}
          size="small"
        >
          Retry
        </Button>
      )}
    </div>
  );
};
