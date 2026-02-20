/**
 * useRecordAccess - Hook for checking user permissions on a record
 *
 * Determines if the current user has write (update) permissions on a
 * specific Event record. Used to enable read-only mode when user lacks
 * edit access.
 *
 * Current implementation: Dataverse enforces row-level security on all
 * Xrm.WebApi calls. A save attempt on a record the user can't write
 * returns 403. The client-side RetrievePrincipalAccess check was unreliable
 * across Dataverse versions and caused noisy 404 errors in console, so it
 * is bypassed for now.
 *
 * TODO: Re-enable with a working RetrievePrincipalAccess format if
 * proactive read-only UX is needed in the future.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/042-add-securityrole-awareness.poml
 */

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Result of the record access check
 */
export interface RecordAccessResult {
  /** Whether the access check has completed */
  isLoaded: boolean;
  /** Whether the check is in progress */
  isLoading: boolean;
  /** Whether the user has write (update) access to the record */
  canWrite: boolean;
  /** Any error that occurred during the check */
  error: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Hook to check if current user has write access to an Event record.
 *
 * Currently returns canWrite: true synchronously — Dataverse enforces
 * permissions server-side on save (403 if unauthorized).
 *
 * @param eventId - GUID of the Event record to check
 * @returns RecordAccessResult with isLoaded, canWrite, and error state
 */
export function useRecordAccess(eventId: string | null): RecordAccessResult {
  return {
    isLoaded: true,
    isLoading: false,
    canWrite: !!eventId,
    error: eventId ? null : "No event ID provided",
  };
}

/**
 * Simple helper to determine if UI should be read-only
 *
 * Returns true if:
 * - Access check is complete AND user lacks write access
 *
 * Returns false if:
 * - Access check is still loading (assume write access until proven otherwise)
 * - Access check completed and user has write access
 */
export function isReadOnly(accessResult: RecordAccessResult): boolean {
  if (!accessResult.isLoaded) {
    return false;
  }
  return !accessResult.canWrite;
}
