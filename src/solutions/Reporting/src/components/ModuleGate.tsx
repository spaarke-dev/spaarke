/**
 * ModuleGate.tsx
 * Wrapper component that verifies the Reporting module is enabled and the
 * current user is authorized before rendering the report viewer.
 *
 * On mount it calls GET /api/reporting/status (lightweight BFF probe).
 * The BFF ReportingAuthorizationFilter returns:
 *   200 OK       → module enabled and user has sprk_ReportingAccess role
 *   404 Not Found → module is disabled (sprk_ReportingModuleEnabled = false)
 *   403 Forbidden → module is enabled but user lacks security role
 *   401           → user is not authenticated (treated as unauthorized)
 *
 * URL parameter override for testing:
 *   ?moduleStatus=disabled      → renders disabled state
 *   ?moduleStatus=unauthorized  → renders unauthorized state
 *   ?moduleStatus=ok            → skips BFF call, renders children
 *
 * @see ADR-021 - Fluent UI v9 only; design tokens; dark mode
 * @see ADR-008 - BFF endpoint filters return 404/403 for gate failures
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Spinner,
  MessageBar,
  MessageBarTitle,
  MessageBarBody,
  Text,
} from "@fluentui/react-components";
import { LockClosedRegular, WarningRegular } from "@fluentui/react-icons";
import { authenticatedFetch } from "../services/authInit";
import { getBffBaseUrl } from "../config/runtimeConfig";
import { REPORTING_STATUS_PATH } from "../config/reportingConfig";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** All possible states the module gate can be in. */
export type ModuleStatus = "loading" | "ok" | "disabled" | "unauthorized" | "error";

// ---------------------------------------------------------------------------
// Styles — Fluent design tokens only, no hard-coded colors (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /** Full-page center wrapper — used for all non-OK states */
  gateContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    width: "100%",
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground1,
    boxSizing: "border-box",
  },
  /** Icon displayed above the message bar */
  gateIcon: {
    fontSize: "48px",
    color: tokens.colorNeutralForeground3,
    lineHeight: 1,
  },
  /** Constrain message bar width on large screens */
  messageBarWrapper: {
    maxWidth: "560px",
    width: "100%",
  },
  /** Spinner label text */
  loadingLabel: {
    color: tokens.colorNeutralForeground2,
  },
});

// ---------------------------------------------------------------------------
// Helper: resolve URL parameter override
// ---------------------------------------------------------------------------

/**
 * Reads the `moduleStatus` URL search param for developer overrides.
 * Returns null if no override is present.
 */
function resolveUrlParamOverride(): ModuleStatus | null {
  try {
    const params = new URLSearchParams(window.location.search);
    const override = params.get("moduleStatus");
    if (override === "disabled") return "disabled";
    if (override === "unauthorized") return "unauthorized";
    if (override === "ok") return "ok";
    if (override === "error") return "error";
  } catch {
    // URLSearchParams not available (SSR / test env) — ignore
  }
  return null;
}

// ---------------------------------------------------------------------------
// Helper: probe BFF status endpoint
// ---------------------------------------------------------------------------

/**
 * Calls GET /api/reporting/status and maps the HTTP response to a ModuleStatus.
 * - 200 → "ok"
 * - 404 → "disabled"   (module gate blocked: sprk_ReportingModuleEnabled = false)
 * - 401 / 403 → "unauthorized"
 * - anything else → "error"
 */
async function probeModuleStatus(): Promise<ModuleStatus> {
  try {
    const url = `${getBffBaseUrl()}${REPORTING_STATUS_PATH}`;
    const response = await authenticatedFetch(url, { method: "GET" });

    if (response.ok) return "ok";
    if (response.status === 404) return "disabled";
    if (response.status === 401 || response.status === 403) return "unauthorized";

    console.warn(
      `[ModuleGate] Unexpected status from ${REPORTING_STATUS_PATH}: ${response.status}`
    );
    return "error";
  } catch (err) {
    console.error("[ModuleGate] BFF probe failed:", err);
    return "error";
  }
}

// ---------------------------------------------------------------------------
// Sub-components for gate states
// ---------------------------------------------------------------------------

const LoadingState: React.FC = () => {
  const styles = useStyles();
  return (
    <div className={styles.gateContainer} role="status" aria-label="Checking Reporting access">
      <Spinner
        size="large"
        label={
          <Text size={300} className={styles.loadingLabel}>
            Checking Reporting access…
          </Text>
        }
        labelPosition="below"
      />
    </div>
  );
};

const DisabledState: React.FC = () => {
  const styles = useStyles();
  return (
    <div className={styles.gateContainer} role="alert" aria-live="assertive">
      <WarningRegular className={styles.gateIcon} aria-hidden="true" />
      <div className={styles.messageBarWrapper}>
        <MessageBar intent="warning" layout="multiline">
          <MessageBarBody>
            <MessageBarTitle>Reporting module is not enabled</MessageBarTitle>
            Reporting module is not enabled for this environment. Contact your administrator to
            enable it.
          </MessageBarBody>
        </MessageBar>
      </div>
    </div>
  );
};

const UnauthorizedState: React.FC = () => {
  const styles = useStyles();
  return (
    <div className={styles.gateContainer} role="alert" aria-live="assertive">
      <LockClosedRegular className={styles.gateIcon} aria-hidden="true" />
      <div className={styles.messageBarWrapper}>
        <MessageBar intent="error" layout="multiline">
          <MessageBarBody>
            <MessageBarTitle>Access required</MessageBarTitle>
            You don't have access to the Reporting module. Contact your administrator to request
            the Reporting Access security role.
          </MessageBarBody>
        </MessageBar>
      </div>
    </div>
  );
};

const ErrorState: React.FC = () => {
  const styles = useStyles();
  return (
    <div className={styles.gateContainer} role="alert" aria-live="assertive">
      <WarningRegular className={styles.gateIcon} aria-hidden="true" />
      <div className={styles.messageBarWrapper}>
        <MessageBar intent="warning" layout="multiline">
          <MessageBarBody>
            <MessageBarTitle>Unable to load Reporting</MessageBarTitle>
            An error occurred while checking Reporting access. Please refresh the page. If the
            problem persists, contact your administrator.
          </MessageBarBody>
        </MessageBar>
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// ModuleGate — main export
// ---------------------------------------------------------------------------

export interface ModuleGateProps {
  /** Content to render when module is enabled and user is authorized. */
  children: React.ReactNode;
}

/**
 * Checks module status on mount and conditionally renders children.
 * Supports URL parameter override (?moduleStatus=disabled|unauthorized|ok|error).
 */
export const ModuleGate: React.FC<ModuleGateProps> = ({ children }) => {
  const [status, setStatus] = React.useState<ModuleStatus>(() => {
    // Apply URL param override synchronously on first render to avoid flash
    const override = resolveUrlParamOverride();
    return override ?? "loading";
  });

  React.useEffect(() => {
    // If a URL param override was detected on mount, skip the BFF call
    const override = resolveUrlParamOverride();
    if (override !== null) {
      setStatus(override);
      return;
    }

    let cancelled = false;

    probeModuleStatus().then((result) => {
      if (!cancelled) {
        setStatus(result);
      }
    });

    return () => {
      cancelled = true;
    };
  }, []);

  switch (status) {
    case "loading":
      return <LoadingState />;
    case "disabled":
      return <DisabledState />;
    case "unauthorized":
      return <UnauthorizedState />;
    case "error":
      return <ErrorState />;
    case "ok":
      return <>{children}</>;
  }
};
