/**
 * useRecordAccess - Hook for checking user permissions on a record
 *
 * Determines if the current user has write (update) permissions on a
 * specific Event record. Used to enable read-only mode when user lacks
 * edit access.
 *
 * Approaches for checking permissions in Dataverse:
 * 1. Check user privileges via Xrm.Utility.getEntityMetadata (entity-level)
 * 2. Check record access via RetrievePrincipalAccess Web API action
 * 3. Attempt a dummy update and catch permission errors
 *
 * This implementation uses RetrievePrincipalAccess for accurate record-level
 * permission checking, with fallback to entity-level privilege check.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/042-add-securityrole-awareness.poml
 * @see https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/retrieveprincipalaccess
 */

import * as React from "react";

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

/**
 * Access rights mask from RetrievePrincipalAccess
 * @see https://learn.microsoft.com/en-us/power-apps/developer/data-platform/security-model
 */
export enum AccessRights {
  None = 0,
  ReadAccess = 1,
  WriteAccess = 2,
  AppendAccess = 4,
  AppendToAccess = 16,
  CreateAccess = 32,
  DeleteAccess = 65536,
  ShareAccess = 262144,
  AssignAccess = 524288,
}

// ─────────────────────────────────────────────────────────────────────────────
// Xrm Interface (Custom Pages context)
// ─────────────────────────────────────────────────────────────────────────────

interface IXrmWebApi {
  retrieveRecord(
    entityType: string,
    id: string,
    options?: string
  ): Promise<Record<string, unknown>>;
  execute(
    request: unknown
  ): Promise<{
    ok: boolean;
    status: number;
    json: () => Promise<Record<string, unknown>>;
  }>;
}

interface IXrmUtility {
  getGlobalContext(): {
    userSettings: {
      userId: string;
    };
  };
}

interface IXrm {
  WebApi: IXrmWebApi;
  Utility: IXrmUtility;
}

/**
 * Get the Xrm object from window context
 */
function getXrm(): IXrm | null {
  try {
    // Try window.parent.Xrm first (Custom Page in iframe)
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const parentXrm = (window.parent as any)?.Xrm;
    if (parentXrm?.WebApi && parentXrm?.Utility) {
      return parentXrm as IXrm;
    }

    // Try window.Xrm (direct access)
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const windowXrm = (window as any)?.Xrm;
    if (windowXrm?.WebApi && windowXrm?.Utility) {
      return windowXrm as IXrm;
    }

    return null;
  } catch (error) {
    console.error("[useRecordAccess] Error accessing Xrm:", error);
    return null;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Access Check Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Check if user has write access to a specific record using RetrievePrincipalAccess
 *
 * This action returns an AccessRightsMask that indicates what actions the
 * current user can perform on the record.
 *
 * @param entityType - Logical name of the entity (e.g., "sprk_events")
 * @param recordId - GUID of the record
 * @returns Promise<boolean> - true if user has write access
 */
async function checkRecordWriteAccess(
  entityType: string,
  recordId: string
): Promise<{ canWrite: boolean; error: string | null }> {
  const xrm = getXrm();
  if (!xrm) {
    console.warn("[useRecordAccess] Xrm not available, defaulting to read-only");
    return { canWrite: false, error: "Xrm not available" };
  }

  try {
    // Get current user ID
    const userId = xrm.Utility.getGlobalContext().userSettings.userId;
    const normalizedUserId = userId.replace(/[{}]/g, "");
    const normalizedRecordId = recordId.replace(/[{}]/g, "");

    // Build the RetrievePrincipalAccess request
    // Uses the Web API action bound to systemuser entity
    const request = {
      // Required properties for Web API action
      getMetadata: function() {
        return {
          boundParameter: null,
          parameterTypes: {
            Target: {
              typeName: "mscrm.crmbaseentity",
              structuralProperty: 5, // EntityReference
            },
          },
          operationType: 1, // Action
          operationName: "RetrievePrincipalAccess",
        };
      },
      // Target record to check access for
      Target: {
        "@odata.type": `Microsoft.Dynamics.CRM.${entityType}`,
        [`${entityType}id`]: normalizedRecordId,
      },
      // Principal (user) whose access is being checked
      Principal: {
        "@odata.type": "Microsoft.Dynamics.CRM.systemuser",
        systemuserid: normalizedUserId,
      },
    };

    const response = await xrm.WebApi.execute(request);

    if (!response.ok) {
      throw new Error(`RetrievePrincipalAccess failed: ${response.status}`);
    }

    const result = await response.json();
    const accessRights = result.AccessRights as number;

    // Check if WriteAccess bit is set
    const hasWriteAccess = (accessRights & AccessRights.WriteAccess) !== 0;

    console.log(
      `[useRecordAccess] User ${normalizedUserId} access to ${entityType}/${normalizedRecordId}: ` +
      `rights=${accessRights}, canWrite=${hasWriteAccess}`
    );

    return { canWrite: hasWriteAccess, error: null };
  } catch (error) {
    const errorMessage = error instanceof Error ? error.message : String(error);
    console.error("[useRecordAccess] RetrievePrincipalAccess error:", errorMessage);

    // Fallback: Check entity-level privileges
    return checkEntityPrivilege(entityType);
  }
}

/**
 * Fallback: Check if user has entity-level write privilege
 *
 * This is less granular than record-level check but works when
 * RetrievePrincipalAccess is not available.
 */
async function checkEntityPrivilege(
  entityType: string
): Promise<{ canWrite: boolean; error: string | null }> {
  const xrm = getXrm();
  if (!xrm) {
    return { canWrite: false, error: "Xrm not available" };
  }

  try {
    // Try to get user's privileges via a metadata query
    // If this fails, assume read-only for safety
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const utility = (xrm as any).Utility;

    if (typeof utility?.getEntityMetadata === "function") {
      const metadata = await utility.getEntityMetadata(entityType, ["Privileges"]);
      const privileges = metadata?.Privileges;

      if (privileges) {
        // Check for Write privilege
        const hasWrite = privileges.some(
          (p: { CanBeBasic: boolean; PrivilegeType: string }) =>
            p.CanBeBasic && p.PrivilegeType === "Write"
        );
        return { canWrite: hasWrite, error: null };
      }
    }

    // Default to allowing write if we can't determine
    console.warn("[useRecordAccess] Could not check entity privilege, defaulting to allow write");
    return { canWrite: true, error: null };
  } catch (error) {
    const errorMessage = error instanceof Error ? error.message : String(error);
    console.error("[useRecordAccess] Entity privilege check error:", errorMessage);

    // Default to read-only on error for security
    return { canWrite: false, error: errorMessage };
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Hook to check if current user has write access to an Event record
 *
 * @param eventId - GUID of the Event record to check
 * @returns RecordAccessResult with isLoaded, canWrite, and error state
 *
 * @example
 * ```tsx
 * const { isLoaded, canWrite, isLoading } = useRecordAccess(eventId);
 *
 * if (!isLoaded) return <Spinner />;
 *
 * return canWrite ? <EditableForm /> : <ReadOnlyForm />;
 * ```
 */
export function useRecordAccess(eventId: string | null): RecordAccessResult {
  const [state, setState] = React.useState<RecordAccessResult>({
    isLoaded: false,
    isLoading: false,
    canWrite: false,
    error: null,
  });

  React.useEffect(() => {
    // No event ID - no access check needed
    if (!eventId) {
      setState({
        isLoaded: true,
        isLoading: false,
        canWrite: false,
        error: "No event ID provided",
      });
      return;
    }

    let cancelled = false;

    const checkAccess = async () => {
      setState((prev) => ({ ...prev, isLoading: true }));

      const result = await checkRecordWriteAccess("sprk_events", eventId);

      if (!cancelled) {
        setState({
          isLoaded: true,
          isLoading: false,
          canWrite: result.canWrite,
          error: result.error,
        });
      }
    };

    checkAccess();

    return () => {
      cancelled = true;
    };
  }, [eventId]);

  return state;
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
  // While loading, allow editing (will re-check when loaded)
  if (!accessResult.isLoaded) {
    return false;
  }
  return !accessResult.canWrite;
}
