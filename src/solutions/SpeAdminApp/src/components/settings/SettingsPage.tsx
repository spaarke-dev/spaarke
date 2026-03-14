import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  TabList,
  Tab,
  type SelectTabData,
  type SelectTabEvent,
} from "@fluentui/react-components";
import { EnvironmentConfig } from "./EnvironmentConfig";
import { ContainerTypeConfig } from "./ContainerTypeConfig";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/** Tab identifiers for the Settings page */
type SettingsTab = "environments" | "container-type-configs";

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

  pageSubtitle: {
    color: tokens.colorNeutralForeground2,
    marginBottom: tokens.spacingVerticalM,
  },

  tabListWrapper: {
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
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
 * SettingsPage — administration configuration page for the SPE Admin App.
 *
 * Provides a tabbed layout with two configuration sections:
 * - **Environments**: Azure tenant and SPE endpoint configuration
 * - **Container Type Configs**: Business Unit to Container Type mapping
 *
 * Uses Fluent v9 TabList for navigation (ADR-021).
 * Dark mode supported via Fluent design tokens — no hard-coded colors.
 */
export const SettingsPage: React.FC = () => {
  const styles = useStyles();

  // Track the currently selected tab
  const [selectedTab, setSelectedTab] = React.useState<SettingsTab>("environments");

  const handleTabSelect = React.useCallback(
    (_event: SelectTabEvent, data: SelectTabData) => {
      setSelectedTab(data.value as SettingsTab);
    },
    []
  );

  return (
    <div className={styles.root}>
      {/* ── Page Header ── */}
      <div className={styles.header}>
        <Text as="h1" size={600} weight="semibold" className={styles.pageTitle}>
          Settings
        </Text>
        <Text size={300} className={styles.pageSubtitle}>
          Configure SPE environments and container type mappings.
        </Text>

        {/* ── Tab Navigation ── */}
        <div className={styles.tabListWrapper}>
          <TabList
            selectedValue={selectedTab}
            onTabSelect={handleTabSelect}
            aria-label="Settings sections"
          >
            <Tab value="environments">Environments</Tab>
            <Tab value="container-type-configs">Container Type Configs</Tab>
          </TabList>
        </div>
      </div>

      {/* ── Tab Content ── */}
      <div className={styles.content}>
        {selectedTab === "environments" && <EnvironmentConfig />}
        {selectedTab === "container-type-configs" && <ContainerTypeConfig />}
      </div>
    </div>
  );
};
