/**
 * ConfirmRegistrationStep — Step 4 of the RegisterWizard.
 *
 * Shows a full summary of the registration to be performed:
 *   - Container type name, ID, and billing classification
 *   - Selected delegated permissions
 *   - Selected application permissions
 *
 * The wizard's onFinish button calls the registration API.
 * This step only confirms — no inputs are collected here.
 *
 * ADR-006: Code Page — React 18 patterns, no PCF/ComponentFramework.
 * ADR-021: All styles use makeStyles + Fluent design tokens (no hard-coded colors).
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Badge,
  shorthands,
  Divider,
} from "@fluentui/react-components";
import {
  CheckmarkCircle20Regular,
  Shield20Regular,
  Person20Regular,
  AppGeneric20Regular,
} from "@fluentui/react-icons";
import type { ContainerType } from "../../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalL),
  },
  description: {
    color: tokens.colorNeutralForeground2,
  },
  summarySection: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalM),
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    overflow: "hidden",
  },
  sectionHeader: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  sectionIcon: {
    color: tokens.colorBrandForeground1,
    display: "flex",
    alignItems: "center",
  },
  sectionContent: {
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalS),
  },
  infoRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  infoLabel: {
    color: tokens.colorNeutralForeground3,
    minWidth: "140px",
  },
  permissionChips: {
    display: "flex",
    flexDirection: "row",
    flexWrap: "wrap",
    ...shorthands.gap(tokens.spacingHorizontalXS, tokens.spacingVerticalXS),
  },
  confirmBanner: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalM),
    backgroundColor: tokens.colorBrandBackground2,
    borderRadius: tokens.borderRadiusMedium,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorBrandStroke2,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  confirmIcon: {
    color: tokens.colorBrandForeground1,
    display: "flex",
    alignItems: "center",
    flexShrink: 0,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

function billingLabel(classification: string): string {
  switch (classification) {
    case "standard":
      return "Standard";
    case "trial":
      return "Trial";
    case "directToCustomer":
      return "Direct to Customer";
    default:
      return classification;
  }
}

function billingBadgeColor(
  classification: string
): "success" | "warning" | "informative" {
  switch (classification) {
    case "standard":
      return "success";
    case "trial":
      return "warning";
    default:
      return "informative";
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface ConfirmRegistrationStepProps {
  /** The container type being registered. */
  containerType: ContainerType;
  /** The selected delegated permissions. */
  delegatedPermissions: string[];
  /** The selected application permissions. */
  applicationPermissions: string[];
}

// ─────────────────────────────────────────────────────────────────────────────
// ConfirmRegistrationStep
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Step 4 of the registration wizard — confirm and register.
 *
 * Read-only summary of the selections. The wizard's "Register" button
 * (mapped from finishLabel) triggers onFinish which calls the API.
 */
export const ConfirmRegistrationStep: React.FC<ConfirmRegistrationStepProps> = ({
  containerType,
  delegatedPermissions,
  applicationPermissions,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      <Text size={400} weight="semibold">
        Confirm Registration
      </Text>
      <Text size={300} className={styles.description}>
        Review the registration details below. Click{" "}
        <strong>Register</strong> to submit the request. This cannot be undone
        without re-registering with different permissions.
      </Text>

      {/* Confirm action banner */}
      <div className={styles.confirmBanner}>
        <span className={styles.confirmIcon}>
          <CheckmarkCircle20Regular />
        </span>
        <Text size={300}>
          Clicking <strong>Register</strong> will call POST{" "}
          <code style={{ fontFamily: "monospace", fontSize: "inherit" }}>
            /api/spe/containertypes/{containerType.containerTypeId}/register
          </code>{" "}
          with the selected permissions.
        </Text>
      </div>

      {/* Container Type section */}
      <div className={styles.summarySection}>
        <div className={styles.sectionHeader}>
          <span className={styles.sectionIcon}>
            <AppGeneric20Regular />
          </span>
          <Text size={300} weight="semibold">
            Container Type
          </Text>
        </div>
        <div className={styles.sectionContent}>
          <div className={styles.infoRow}>
            <Text size={200} className={styles.infoLabel}>Name</Text>
            <Text size={300} weight="semibold">{containerType.displayName}</Text>
          </div>
          <Divider />
          <div className={styles.infoRow}>
            <Text size={200} className={styles.infoLabel}>Container Type ID</Text>
            <Text
              size={200}
              style={{ fontFamily: "monospace", color: tokens.colorNeutralForeground2 }}
            >
              {containerType.containerTypeId}
            </Text>
          </div>
          <Divider />
          <div className={styles.infoRow}>
            <Text size={200} className={styles.infoLabel}>Billing Classification</Text>
            <Badge
              color={billingBadgeColor(containerType.billingClassification)}
              appearance="filled"
              size="small"
            >
              {billingLabel(containerType.billingClassification)}
            </Badge>
          </div>
        </div>
      </div>

      {/* Delegated Permissions section */}
      <div className={styles.summarySection}>
        <div className={styles.sectionHeader}>
          <span className={styles.sectionIcon}>
            <Person20Regular />
          </span>
          <Text size={300} weight="semibold">
            Delegated Permissions
          </Text>
          <Badge appearance="outline" size="small">
            {delegatedPermissions.length} selected
          </Badge>
        </div>
        <div className={styles.sectionContent}>
          <div className={styles.permissionChips}>
            {delegatedPermissions.map((perm) => (
              <Badge
                key={perm}
                appearance="filled"
                color="brand"
                size="small"
              >
                {perm}
              </Badge>
            ))}
          </div>
        </div>
      </div>

      {/* Application Permissions section */}
      <div className={styles.summarySection}>
        <div className={styles.sectionHeader}>
          <span className={styles.sectionIcon}>
            <Shield20Regular />
          </span>
          <Text size={300} weight="semibold">
            Application Permissions
          </Text>
          <Badge appearance="outline" size="small">
            {applicationPermissions.length} selected
          </Badge>
        </div>
        <div className={styles.sectionContent}>
          <div className={styles.permissionChips}>
            {applicationPermissions.map((perm) => (
              <Badge
                key={perm}
                appearance="filled"
                color="informative"
                size="small"
              >
                {perm}
              </Badge>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};
