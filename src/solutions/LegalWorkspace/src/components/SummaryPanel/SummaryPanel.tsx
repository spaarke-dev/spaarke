/**
 * SummaryPanel — AI-powered summary panel for the right column of the workspace.
 *
 * R1: Renders a placeholder card indicating that AI summary and Spaarke chat
 * features will be available in R2. Accepts webApi/userId props so the shell
 * is ready for future implementation without layout changes.
 *
 * R2 plans:
 *   - Portfolio summary generated from workspace data
 *   - Spaarke chat assistant for natural-language portfolio queries
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported automatically via token system
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
} from "@fluentui/react-components";
import { SparkleRegular } from "@fluentui/react-icons";
import type { IWebApi } from "../../types/xrm";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  panel: {
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth("1px"),
    ...shorthands.borderStyle("solid"),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
  },
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  headerIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: "20px",
    display: "flex",
    alignItems: "center",
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  body: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    textAlign: "center",
  },
  placeholderIcon: {
    color: tokens.colorNeutralForeground4,
    fontSize: "40px",
    display: "flex",
    alignItems: "center",
  },
  placeholderTitle: {
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightSemibold,
  },
  placeholderText: {
    color: tokens.colorNeutralForeground3,
    maxWidth: "280px",
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISummaryPanelProps {
  /** Xrm.WebApi reference — unused in R1, ready for R2 implementation */
  webApi: IWebApi;
  /** Current user GUID — unused in R1, ready for R2 implementation */
  userId: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SummaryPanel: React.FC<ISummaryPanelProps> = ({
  webApi: _webApi,
  userId: _userId,
}) => {
  const styles = useStyles();

  return (
    <div
      className={styles.panel}
      role="region"
      aria-label="Summary Panel"
    >
      {/* Header */}
      <div className={styles.header}>
        <span className={styles.headerIcon} aria-hidden="true">
          <SparkleRegular />
        </span>
        <Text className={styles.headerTitle} size={400}>
          Summary
        </Text>
      </div>

      {/* Placeholder body */}
      <div className={styles.body}>
        <span className={styles.placeholderIcon} aria-hidden="true">
          <SparkleRegular />
        </span>
        <Text className={styles.placeholderTitle} size={300}>
          AI-Powered Summary
        </Text>
        <Text className={styles.placeholderText} size={200}>
          Your personalized workspace summary will appear here. In R2, this
          panel will provide AI-generated portfolio insights and the Spaarke
          chat assistant for natural-language queries.
        </Text>
      </div>
    </div>
  );
};

SummaryPanel.displayName = "SummaryPanel";
