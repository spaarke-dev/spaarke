/**
 * WizardSuccessScreen.tsx
 *
 * Generic, domain-free success screen rendered by WizardShell after the
 * consumer's `onFinish` callback resolves with an IWizardSuccessConfig.
 *
 * Layout:
 *   +--------------------------------------------------------------+
 *   |                       [icon]                                  |
 *   |                     title text                                |
 *   |                    body content                               |
 *   |              [Action 1]   [Action 2]                          |
 *   |                                                               |
 *   |  -- Warnings (optional) ------------------------------------ |
 *   |  ! Warning message 1                                         |
 *   |  ! Warning message 2                                         |
 *   +--------------------------------------------------------------+
 *
 * All content is injected via IWizardSuccessConfig — this component
 * has ZERO domain-specific knowledge.
 *
 * Constraints:
 *   - Fluent v9 only: Text, MessageBar — ZERO hardcoded colors
 *   - makeStyles with semantic tokens
 *   - No domain-specific imports
 */

import * as React from "react";
import {
  Text,
  MessageBar,
  MessageBarBody,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import type { IWizardSuccessConfig } from "./wizardShellTypes";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface Props {
  config: IWizardSuccessConfig;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    gap: tokens.spacingVerticalL,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXL,
    textAlign: "center",
  },

  iconWrapper: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    marginBottom: tokens.spacingVerticalS,
  },

  titleText: {
    color: tokens.colorNeutralForeground1,
  },

  body: {
    color: tokens.colorNeutralForeground2,
    maxWidth: "400px",
  },

  actionsRow: {
    display: "flex",
    gap: tokens.spacingHorizontalM,
    justifyContent: "center",
    marginTop: tokens.spacingVerticalS,
  },

  warningsSection: {
    width: "100%",
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    marginTop: tokens.spacingVerticalM,
    alignItems: "stretch",
    textAlign: "left",
  },
});

// ---------------------------------------------------------------------------
// WizardSuccessScreen (exported)
// ---------------------------------------------------------------------------

export const WizardSuccessScreen: React.FC<Props> = ({ config }) => {
  const styles = useStyles();
  const hasWarnings = config.warnings != null && config.warnings.length > 0;

  return (
    <div className={styles.root}>
      {/* Icon */}
      <div className={styles.iconWrapper} aria-hidden="true">
        {config.icon}
      </div>

      {/* Title */}
      <Text as="h2" size={600} weight="semibold" className={styles.titleText}>
        {config.title}
      </Text>

      {/* Body */}
      <div className={styles.body}>{config.body}</div>

      {/* Action buttons */}
      <div className={styles.actionsRow}>{config.actions}</div>

      {/* Warnings */}
      {hasWarnings && (
        <div className={styles.warningsSection} aria-live="polite">
          {config.warnings!.map((warning, i) => (
            <MessageBar key={i} intent="warning">
              <MessageBarBody>{warning}</MessageBarBody>
            </MessageBar>
          ))}
        </div>
      )}
    </div>
  );
};
