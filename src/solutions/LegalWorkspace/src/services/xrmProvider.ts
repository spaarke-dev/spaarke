/**
 * Xrm Provider — frame-walk utility for accessing Dataverse APIs from
 * a standalone HTML web resource (Custom Page).
 *
 * Web resources run inside an iframe within the Dataverse shell. The Xrm
 * global is not directly available — we walk up the frame hierarchy to
 * find it on a parent or top window.
 *
 * Per ADR-026: standalone HTML web resources use this pattern instead of
 * the PCF context.webAPI mechanism.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

/**
 * Locate the Xrm global by walking the frame hierarchy.
 *
 * Priority: current window → parent window → top window.
 * Returns null if Xrm is not available (e.g., local dev server).
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function getXrm(): any | null {
  // 1. Current window (direct embedding or test harness)
  if (typeof Xrm !== "undefined" && Xrm?.WebApi) {
    return Xrm;
  }
  // 2. Parent window (iframe in Custom Page)
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const parentXrm = (window.parent as any)?.Xrm;
    if (parentXrm?.WebApi) return parentXrm;
  } catch {
    /* cross-origin — swallow */
  }
  // 3. Top window (nested iframes)
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const topXrm = (window.top as any)?.Xrm;
    if (topXrm?.WebApi) return topXrm;
  } catch {
    /* cross-origin — swallow */
  }
  return null;
}

/**
 * Get the Xrm.WebApi reference for CRUD operations.
 * Returns null if not running inside Dataverse.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function getWebApi(): any | null {
  return getXrm()?.WebApi ?? null;
}

/**
 * Get the current user's GUID.
 * Equivalent to PCF's context.userSettings.userId.
 */
export function getUserId(): string {
  const xrm = getXrm();
  if (xrm?.Utility?.getGlobalContext) {
    const ctx = xrm.Utility.getGlobalContext();
    // getUserId() returns GUID with braces: {xxxxxxxx-xxxx-...}
    const raw = ctx.getUserId?.() ?? ctx.userSettings?.userId ?? "";
    return raw.replace(/[{}]/g, "");
  }
  // Fallback for userSettings directly on Xrm
  if (xrm?.userSettings?.userId) {
    return xrm.userSettings.userId.replace(/[{}]/g, "");
  }
  console.warn("[LegalWorkspace] Unable to resolve userId from Xrm");
  return "";
}

/**
 * Look up the SPE Container ID (sprk_containerid) from the user's Business Unit.
 *
 * Flow: userId → systemuser.businessunitid → businessunit.sprk_containerid
 *
 * Uses the systemuser entity to reliably resolve the BU (Xrm global context
 * does not always expose businessUnitId on userSettings).
 *
 * @param webApi - Xrm.WebApi reference
 * @returns The container ID string, or empty string if not configured.
 */
export async function getSpeContainerIdFromBusinessUnit(
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  webApi: any
): Promise<string> {
  const userId = getUserId();
  if (!userId) {
    console.warn("[LegalWorkspace] No userId — cannot look up SPE container.");
    return "";
  }

  try {
    // Step 1: Get user's business unit ID from systemuser record
    console.info("[LegalWorkspace] Looking up BU for user:", userId);
    const userRecord = await webApi.retrieveRecord(
      "systemuser",
      userId,
      "?$select=businessunitid&$expand=businessunitid($select=businessunitid,sprk_containerid)"
    );

    // The $expand returns the related BU record inline
    const buRecord = userRecord?.businessunitid;
    if (buRecord?.sprk_containerid) {
      const containerId = buRecord.sprk_containerid as string;
      console.info("[LegalWorkspace] SPE containerId (from BU expand):", containerId);
      return containerId;
    }

    // Fallback: if $expand didn't work, try querying BU directly
    const buId = userRecord?._businessunitid_value || userRecord?.businessunitid?.businessunitid;
    if (!buId) {
      console.warn("[LegalWorkspace] Could not resolve businessunitid from systemuser.");
      return "";
    }

    console.info("[LegalWorkspace] Looking up SPE container for BU:", buId);
    const buDirectRecord = await webApi.retrieveRecord(
      "businessunit",
      buId,
      "?$select=sprk_containerid"
    );
    const containerId = (buDirectRecord?.sprk_containerid as string) ?? "";
    console.info("[LegalWorkspace] SPE containerId:", containerId || "(not set)");
    return containerId;
  } catch (err) {
    console.warn("[LegalWorkspace] Failed to look up SPE container:", err);
    return "";
  }
}
