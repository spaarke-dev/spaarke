/**
 * ModeToggle.tsx
 * View / Edit mode toggle for the Reporting toolbar.
 *
 * Only rendered when the current user has Author or Admin privilege.
 * Calls report.switchMode("view") or report.switchMode("edit") on the
 * Power BI Report object via the provided `report` prop.
 *
 * When switching into edit mode the component requests a new embed token
 * with editing permissions (allowEdit=true) from the BFF if the current
 * session does not already hold one.
 *
 * @see ADR-021 - Fluent UI v9 only; design tokens; dark mode required
 * @see ADR-008 - BFF endpoint filters for auth
 *
 * Power BI SDK reference:
 *   report.switchMode("view")  — read-only with filter pane
 *   report.switchMode("edit")  — full authoring toolbar
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  ToggleButton,
  Spinner,
  Tooltip,
  MessageBar,
  MessageBarBody,
} from "@fluentui/react-components";
import { EditRegular, EyeRegular } from "@fluentui/react-icons";
import type { ReportMode } from "../types/reporting";
import { fetchEmbedToken } from "../services/reportingApi";

// ---------------------------------------------------------------------------
// Styles — Fluent design tokens only, no hard-coded colors (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
  errorMessage: {
    maxWidth: "280px",
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/** Minimal Power BI Report interface — only the methods this component uses. */
export interface PbiReportRef {
  /** Switch the report between view and edit modes. */
  switchMode(mode: "view" | "edit"): Promise<void>;
  /** Update the access token on the embedded report (used after fetching edit token). */
  setAccessToken(token: string): Promise<void>;
}

export interface ModeToggleProps {
  /** Currently selected mode. */
  mode: ReportMode;
  /** Callback when the user requests a mode change. */
  onModeChange: (newMode: ReportMode) => void;
  /** Reference to the embedded Power BI Report object. */
  report: PbiReportRef | null;
  /** The report's Dataverse record GUID — used to request the edit embed token. */
  reportId: string | null;
  /** Whether the component is disabled (e.g. no report loaded). */
  disabled?: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Toggle between View and Edit report modes.
 *
 * Handles edit-mode token acquisition automatically:
 * when the user switches to edit, a new embed token with
 * `allowEdit=true` is fetched from the BFF before calling
 * `report.switchMode("edit")`.
 */
export const ModeToggle: React.FC<ModeToggleProps> = ({
  mode,
  onModeChange,
  report,
  reportId,
  disabled = false,
}) => {
  const styles = useStyles();

  const [switching, setSwitching] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const isEditMode = mode === "edit";

  const handleToggle = React.useCallback(async () => {
    if (!report || !reportId || switching) return;

    const targetMode: ReportMode = isEditMode ? "view" : "edit";
    setSwitching(true);
    setError(null);

    try {
      if (targetMode === "edit") {
        // Fetch edit-capable embed token before switching
        const tokenResult = await fetchEmbedToken(reportId, /* allowEdit */ true);
        if (!tokenResult.ok) {
          setError(`Could not enter edit mode: ${tokenResult.error}`);
          return;
        }
        // Update the embedded report's access token before switching mode
        await report.setAccessToken(tokenResult.data.token);
      }

      await report.switchMode(targetMode);
      onModeChange(targetMode);
    } catch (err) {
      console.error("[ModeToggle] Mode switch failed", err);
      setError(
        `Failed to switch to ${targetMode} mode. Please try again.`
      );
    } finally {
      setSwitching(false);
    }
  }, [isEditMode, report, reportId, switching, onModeChange]);

  return (
    <div className={styles.container}>
      {switching && (
        <Spinner size="tiny" aria-label={`Switching to ${isEditMode ? "view" : "edit"} mode`} />
      )}

      <Tooltip
        content={isEditMode ? "Switch to view mode" : "Switch to edit mode"}
        relationship="label"
      >
        <ToggleButton
          appearance="subtle"
          size="medium"
          icon={isEditMode ? <EyeRegular /> : <EditRegular />}
          checked={isEditMode}
          disabled={disabled || switching || !report}
          onClick={handleToggle}
          aria-label={isEditMode ? "Switch to view mode" : "Switch to edit mode"}
          aria-pressed={isEditMode}
        >
          {isEditMode ? "View" : "Edit"}
        </ToggleButton>
      </Tooltip>

      {error && (
        <div className={styles.errorMessage} role="alert" aria-live="assertive">
          <MessageBar intent="error" layout="singleline">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        </div>
      )}
    </div>
  );
};
