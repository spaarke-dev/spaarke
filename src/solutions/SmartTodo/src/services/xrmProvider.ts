/**
 * Xrm Provider — Dataverse API access for a standalone HTML web resource
 * (Custom Page).
 *
 * Web resources run inside an iframe within the Dataverse shell. The Xrm
 * global is not directly available — callers resolve it with the shared
 * `getXrm` from @spaarke/ui-components (task 081 / C-8: the former
 * `getHostXrm` wrapper here, and its same-named twin in the other Code Page,
 * were removed so each call site states the Xrm capability it needs).
 *
 * Per ADR-026: standalone HTML web resources use this pattern instead of
 * the PCF context.webAPI mechanism.
 */

import { cleanGuid, getXrm } from '@spaarke/ui-components';

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
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const xrm: any = getXrm('utility') ?? getXrm((x: any) => !!x.userSettings?.userId);
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
