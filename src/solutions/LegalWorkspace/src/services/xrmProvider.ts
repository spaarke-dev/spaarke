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
