/**
 * useAccessLevel — resolve the current user's access level for a specific project.
 *
 * Reads the authenticated user's context (via useExternalContext) and looks up
 * the access level string for the given projectId. Maps the Dataverse option set
 * string label ("ViewOnly", "Collaborate", "FullAccess") to the AccessLevel enum.
 *
 * Returns AccessLevel.ViewOnly as the safe default when the project is not found
 * in the user's context, or while the context is still loading.
 *
 * Capability matrix:
 *   ViewOnly    — canUpload=false, canDownload=false, canCreate=false, canUseAi=false, canInvite=false
 *   Collaborate — canUpload=true,  canDownload=true,  canCreate=true,  canUseAi=true,  canInvite=false
 *   FullAccess  — canUpload=true,  canDownload=true,  canCreate=true,  canUseAi=true,  canInvite=true
 *
 * Note: Client-side enforcement is UX only. Server-side enforcement via endpoint
 * filters is the actual security boundary (ADR-008, auth constraint).
 *
 * See: docs/architecture/uac-access-control.md
 */

import { useMemo } from "react";
import { AccessLevel } from "../types";
import { useExternalContext } from "./useExternalContext";

// ---------------------------------------------------------------------------
// Access level mapping
// ---------------------------------------------------------------------------

/**
 * Map the access level string label returned by the BFF API's
 * ExternalUserContextResponse.projects[].accessLevel to the AccessLevel enum.
 *
 * The BFF serialises the Dataverse option set label, not the numeric value.
 * Accepted strings: "ViewOnly", "Collaborate", "FullAccess".
 * All unrecognised values fall back to ViewOnly (fail-safe).
 */
function mapAccessLevelString(label: string | undefined | null): AccessLevel {
  switch (label) {
    case "Collaborate":
      return AccessLevel.Collaborate;
    case "FullAccess":
      return AccessLevel.FullAccess;
    case "ViewOnly":
    default:
      // Default to least-privilege access if label is missing or unrecognised
      return AccessLevel.ViewOnly;
  }
}

// ---------------------------------------------------------------------------
// Capability helpers
// ---------------------------------------------------------------------------

/** Returns true when the access level allows uploading documents. */
export function canUpload(accessLevel: AccessLevel): boolean {
  return accessLevel === AccessLevel.Collaborate || accessLevel === AccessLevel.FullAccess;
}

/** Returns true when the access level allows downloading documents. */
export function canDownload(accessLevel: AccessLevel): boolean {
  return accessLevel === AccessLevel.Collaborate || accessLevel === AccessLevel.FullAccess;
}

/** Returns true when the access level allows creating records (events, tasks, etc.). */
export function canCreate(accessLevel: AccessLevel): boolean {
  return accessLevel === AccessLevel.Collaborate || accessLevel === AccessLevel.FullAccess;
}

/** Returns true when the access level allows triggering AI features (toolbar, semantic search action). */
export function canUseAi(accessLevel: AccessLevel): boolean {
  return accessLevel === AccessLevel.Collaborate || accessLevel === AccessLevel.FullAccess;
}

/** Returns true when the access level allows inviting other external users. */
export function canInvite(accessLevel: AccessLevel): boolean {
  return accessLevel === AccessLevel.FullAccess;
}

// ---------------------------------------------------------------------------
// Hook: useAccessLevel
// ---------------------------------------------------------------------------

export interface UseAccessLevelResult {
  /** The resolved AccessLevel enum value for the given project. */
  accessLevel: AccessLevel;
  /** True while the user context is still loading. */
  isLoading: boolean;
  /** Non-null if the context fetch failed. */
  error: string | null;
  // Capability shortcuts (derived from accessLevel)
  canUpload: boolean;
  canDownload: boolean;
  canCreate: boolean;
  canUseAi: boolean;
  canInvite: boolean;
}

/**
 * Resolve the authenticated user's access level for a specific project.
 *
 * Pulls the user context from useExternalContext and locates the project entry
 * by projectId. The accessLevel string is mapped to the AccessLevel enum.
 *
 * Falls back to AccessLevel.ViewOnly (least privilege) when:
 *   - The context is still loading
 *   - The context fetch failed
 *   - The projectId is not found in the user's project list
 *
 * @param projectId — Dataverse GUID of the sprk_project record.
 *
 * @example
 * ```tsx
 * const { accessLevel, canUpload, isLoading } = useAccessLevel(projectId);
 * if (isLoading) return <Spinner />;
 * return <DocumentLibrary projectId={projectId} accessLevel={accessLevel} />;
 * ```
 */
export function useAccessLevel(projectId: string | undefined | null): UseAccessLevelResult {
  const { context, isLoading, error } = useExternalContext();

  const accessLevel = useMemo<AccessLevel>(() => {
    if (!projectId || !context) {
      return AccessLevel.ViewOnly;
    }

    const projectEntry = context.projects.find(
      (p) => p.projectId === projectId
    );

    return mapAccessLevelString(projectEntry?.accessLevel);
  }, [projectId, context]);

  return {
    accessLevel,
    isLoading,
    error,
    canUpload: canUpload(accessLevel),
    canDownload: canDownload(accessLevel),
    canCreate: canCreate(accessLevel),
    canUseAi: canUseAi(accessLevel),
    canInvite: canInvite(accessLevel),
  };
}
