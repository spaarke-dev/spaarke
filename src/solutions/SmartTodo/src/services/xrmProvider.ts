/**
 * Xrm Provider — Dataverse API access for a standalone HTML web resource
 * (Custom Page).
 *
 * Web resources run inside an iframe within the Dataverse shell. The Xrm
 * global is not directly available — the shared cross-frame walker
 * (`getXrm` in @spaarke/ui-components) finds it on a parent or top window.
 *
 * Per ADR-026: standalone HTML web resources use this pattern instead of
 * the PCF context.webAPI mechanism.
 */

import { cleanGuid, getXrm as getSharedXrm } from '@spaarke/ui-components';

/**
 * Locate the Xrm global (current window → parent → top).
 * Returns null if Xrm is not available (e.g., local dev server).
 *
 * Thin wrapper kept for this package's importers; delegates to the shared
 * cross-frame walker (task 081 / C-8). It no longer writes `window.Xrm`.
 * Named `getHostXrm` (was `getXrm`) so no second export shares the shared
 * walker's name.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function getHostXrm(): any | null {
  return getSharedXrm() ?? null;
}

/**
 * Get the Xrm.WebApi reference for CRUD operations.
 * Returns null if not running inside Dataverse.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function getWebApi(): any | null {
  return getHostXrm()?.WebApi ?? null;
}

/**
 * Get the current user's GUID.
 * Equivalent to PCF's context.userSettings.userId.
 */
export function getUserId(): string {
  const xrm = getHostXrm();
  if (xrm?.Utility?.getGlobalContext) {
    const ctx = xrm.Utility.getGlobalContext();
    // getUserId() returns GUID with braces: {xxxxxxxx-xxxx-...}
    const raw = ctx.getUserId?.() ?? ctx.userSettings?.userId ?? "";
    return cleanGuid(raw);
  }
  // Fallback for userSettings directly on Xrm
  if (xrm?.userSettings?.userId) {
    return cleanGuid(xrm.userSettings.userId);
  }
  console.warn("[SmartTodo] Unable to resolve userId from Xrm");
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
    console.warn("[SmartTodo] No userId — cannot look up SPE container.");
    return "";
  }

  try {
    // Step 1: Get user's business unit ID from systemuser record
    console.info("[SmartTodo] Looking up BU for user:", userId);
    const userRecord = await webApi.retrieveRecord(
      "systemuser",
      userId,
      "?$select=businessunitid&$expand=businessunitid($select=businessunitid,sprk_containerid)"
    );

    // The $expand returns the related BU record inline
    const buRecord = userRecord?.businessunitid;
    if (buRecord?.sprk_containerid) {
      const containerId = buRecord.sprk_containerid as string;
      console.info("[SmartTodo] SPE containerId (from BU expand):", containerId);
      return containerId;
    }

    // Fallback: if $expand didn't work, try querying BU directly
    const buId = userRecord?._businessunitid_value || userRecord?.businessunitid?.businessunitid;
    if (!buId) {
      console.warn("[SmartTodo] Could not resolve businessunitid from systemuser.");
      return "";
    }

    console.info("[SmartTodo] Looking up SPE container for BU:", buId);
    const buDirectRecord = await webApi.retrieveRecord(
      "businessunit",
      buId,
      "?$select=sprk_containerid"
    );
    const containerId = (buDirectRecord?.sprk_containerid as string) ?? "";
    console.info("[SmartTodo] SPE containerId:", containerId || "(not set)");
    return containerId;
  } catch (err) {
    console.warn("[SmartTodo] Failed to look up SPE container:", err);
    return "";
  }
}
