import * as React from "react";
import { makeStyles, tokens, Text, Button, Tooltip } from "@fluentui/react-components";
import { WeatherMoon20Regular, WeatherSunny20Regular, Person20Regular } from "@fluentui/react-icons";
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
  right: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  userInfo: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground2,
  },
  appName: {
    color: tokens.colorNeutralForeground1,
  },
});

interface AppHeaderProps {
  /** Current dark mode state */
  isDark: boolean;
  /** Callback to toggle dark/light theme */
  onToggleDark: () => void;
  /** Authenticated portal user, or null while loading */
  portalUser?: PortalUser | null;
}

/**
 * AppHeader — top navigation bar for the Secure Project Workspace SPA.
 *
 * Displays:
 * - App name / brand
 * - Authenticated portal user name (when available)
 * - Dark mode toggle button
 *
 * Styled exclusively with Fluent v9 design tokens (ADR-021).
 */
export const AppHeader: React.FC<AppHeaderProps> = ({ isDark, onToggleDark, portalUser }) => {
  const styles = useStyles();

  return (
    <header className={styles.header} role="banner">
      <div className={styles.left}>
        <Text size={500} weight="semibold" className={styles.appName}>
          Secure Project Workspace
        </Text>
      </div>

      <div className={styles.right}>
        {portalUser && (
          <div className={styles.userInfo} aria-label={`Signed in as ${portalUser.displayName}`}>
            <Person20Regular aria-hidden="true" />
            <Text size={300}>{portalUser.displayName}</Text>
          </div>
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
      </div>
    </header>
  );
};

export default AppHeader;
