import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";
import { EnvironmentConfig } from "./EnvironmentConfig";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },

  header: {
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    flexShrink: 0,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
  },

  pageTitle: {
    marginBottom: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground1,
  },

  /**
   * `display: block` so the description sits on its own line beneath the title rather than
   * running on beside it (operator-directed, UAT 2026-08-26).
   */
  pageSubtitle: {
    display: "block",
    color: tokens.colorNeutralForeground2,
  },

  content: {
    flex: "1 1 auto",
    overflow: "auto",
    minHeight: 0,
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// SettingsPage Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SettingsPage — SPE **environment** administration.
 *
 * Renamed in substance on 2026-08-26 (UAT): this page used to be "Settings" with two tabs,
 * Environments and Container Type Configs. The configs moved to the Container Types page, where
 * their subject actually lives, leaving environments as this page's only content — so the tab
 * strip went with them. A TabList with one tab is a control that cannot be operated.
 *
 * The nav label is "Environments" (`AppShell.tsx`), and this title now matches it.
 *
 * Dark mode supported via Fluent design tokens — no hard-coded colors (ADR-021).
 */
export const SettingsPage: React.FC = () => {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      {/* ── Page Header ── */}
      <div className={styles.header}>
        <Text as="h1" size={600} weight="semibold" className={styles.pageTitle}>
          Environments
        </Text>
        <Text size={300} className={styles.pageSubtitle}>
          Configure the Azure tenants and SharePoint Embedded endpoints that container type
          configurations point at.
        </Text>
      </div>

      {/* ── Content ── */}
      <div className={styles.content}>
        <EnvironmentConfig />
      </div>
    </div>
  );
};
