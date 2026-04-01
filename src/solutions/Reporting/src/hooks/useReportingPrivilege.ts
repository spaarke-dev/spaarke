/**
 * useReportingPrivilege.ts
 * Hook to determine the current user's Reporting module privilege level.
 *
 * Privilege levels:
 *   - Viewer: can view and export reports (default)
 *   - Author: can view, export, and switch to edit mode
 *   - Admin:  full access including workspace management
 *
 * Privilege is fetched from GET /api/reporting/privilege which the BFF
 * resolves from the user's Dataverse security role membership:
 *   - sprk_ReportingAccess              → Viewer
 *   - sprk_ReportingAccess + Author     → Author
 *   - sprk_ReportingAccess + Admin      → Admin
 *
 * The hook returns a stable result object with { privilege, loading, error }.
 * Defaults to "Viewer" on error to ensure safe, read-only degradation.
 *
 * @see ADR-008 - BFF endpoint filters for authorization
 */

import * as React from "react";
import { fetchUserPrivilege } from "../services/reportingApi";
import type { UserPrivilege } from "../types/reporting";

// ---------------------------------------------------------------------------
// Hook return type
// ---------------------------------------------------------------------------

export interface UseReportingPrivilegeResult {
  privilege: UserPrivilege;
  loading: boolean;
  error: string | null;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Fetches the current user's Reporting module privilege on mount.
 *
 * Returns { privilege: "Viewer", loading: true } while the request is
 * in-flight so callers can gate UI appropriately.
 *
 * Defaults to "Viewer" on fetch failure — this is a safe fallback that
 * hides Author/Admin controls rather than showing them incorrectly.
 */
export function useReportingPrivilege(): UseReportingPrivilegeResult {
  const [privilege, setPrivilege] = React.useState<UserPrivilege>("Viewer");
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);

  React.useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const result = await fetchUserPrivilege();

        if (cancelled) return;

        if (result.ok) {
          setPrivilege(result.data.privilege);
          setError(null);
        } else {
          console.warn("[useReportingPrivilege] Privilege fetch failed:", result.error);
          // Safely degrade to Viewer — do NOT show Author/Admin controls on failure
          setPrivilege("Viewer");
          setError(result.error);
        }
      } catch (err) {
        if (!cancelled) {
          console.error("[useReportingPrivilege] Unexpected error", err);
          setPrivilege("Viewer");
          setError(String(err));
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  return { privilege, loading, error };
}

// ---------------------------------------------------------------------------
// Privilege check helpers
// ---------------------------------------------------------------------------

/** Returns true if the privilege level allows report authoring (edit mode). */
export function canEditReports(privilege: UserPrivilege): boolean {
  return privilege === "Author" || privilege === "Admin";
}

/** Returns true if the privilege level allows workspace administration. */
export function canAdminReports(privilege: UserPrivilege): boolean {
  return privilege === "Admin";
}
