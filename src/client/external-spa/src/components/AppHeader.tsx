import * as React from "react";
import { makeStyles, tokens, Text, Button, Tooltip } from "@fluentui/react-components";
import { WeatherMoon20Regular, WeatherSunny20Regular, Person20Regular, Settings20Regular } from "@fluentui/react-icons";
import { SpaarkeLogoSvg } from "./SpaarkeLogoSvg";
import type { PortalUser } from "../types";

const useStyles = makeStyles({
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalXL}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  left: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
  },
  logoWrapper: {
    display: "flex",
    alignItems: "center",
    flexShrink: 0,
  },
  titleDivider: {
    width: "1px",
    height: "20px",
    backgroundColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  right: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
  userButton: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground2,
    cursor: "pointer",
    background: "none",
    border: "none",
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    borderRadius: tokens.borderRadiusMedium,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground3,
    },
  },
  appName: {
    color: tokens.colorNeutralForeground1,
    whiteSpace: "nowrap",
  },
});

interface AppHeaderProps {
  /** Current dark mode state */
  isDark: boolean;
  /** Callback to toggle dark/light theme */
  onToggleDark: () => void;
  /** Authenticated portal user, or null while loading */
  portalUser?: PortalUser | null;
  /** Navigate to settings page */
  onSettingsClick?: () => void;
}

/**
 * AppHeader — top navigation bar for the Secure External Workspace SPA.
 *
 * Displays:
 * - Spaarke full logo (black in light mode, white in dark mode)
 * - App title: "Secure External Workspace"
 * - Authenticated user name — clickable, opens settings page
 * - Dark mode toggle button
 * - Settings icon button
 *
 * Styled exclusively with Fluent v9 design tokens (ADR-021).
 */
export const AppHeader: React.FC<AppHeaderProps> = ({
  isDark,
  onToggleDark,
  portalUser,
  onSettingsClick,
}) => {
  const styles = useStyles();

  return (
    <header className={styles.header} role="banner">
      <div className={styles.left}>
        {/* Spaarke logo — black for light mode, white for dark mode */}
        <div className={styles.logoWrapper}>
          <SpaarkeLogoSvg fill={isDark ? "white" : "black"} height={28} />
        </div>

        <div className={styles.titleDivider} aria-hidden="true" />

        <Text size={400} weight="semibold" className={styles.appName}>
          Secure External Workspace
        </Text>
      </div>

      <div className={styles.right}>
        {portalUser && (
          <Tooltip content="Account settings" relationship="label">
            <button
              className={styles.userButton}
              onClick={onSettingsClick}
              aria-label={`${portalUser.displayName} — open account settings`}
            >
              <Person20Regular aria-hidden="true" />
              <Text size={300}>{portalUser.displayName}</Text>
            </button>
          </Tooltip>
        )}

        <Tooltip
          content={isDark ? "Switch to light mode" : "Switch to dark mode"}
          relationship="label"
        >
          <Button
            appearance="subtle"
            size="small"
            icon={isDark ? <WeatherSunny20Regular /> : <WeatherMoon20Regular />}
            onClick={onToggleDark}
            aria-label={isDark ? "Switch to light mode" : "Switch to dark mode"}
          />
        </Tooltip>

        {onSettingsClick && (
          <Tooltip content="Settings" relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<Settings20Regular />}
              onClick={onSettingsClick}
              aria-label="Open settings"
            />
          </Tooltip>
        )}
      </div>
    </header>
  );
};

export default AppHeader;
