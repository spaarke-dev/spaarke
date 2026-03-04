/**
 * xrmAccess — Utility for accessing Xrm context from within a side pane or
 * dialog iframe.
 *
 * Side pane content runs inside an iframe. The parent Dataverse page provides
 * Xrm on window.parent.Xrm. When deeply nested (e.g. iframe inside a dialog
 * inside the main app), we walk up the frame hierarchy: parent → top → current.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

export interface IXrmWebApi {
  retrieveRecord(entityName: string, id: string, options?: string): Promise<any>;
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: any[] }>;
  updateRecord(entityName: string, id: string, data: Record<string, unknown>): Promise<any>;
}

export interface IXrmNavigation {
  openForm(options: Record<string, unknown>): Promise<any>;
  navigateTo(pageInput: Record<string, unknown>, navigationOptions?: Record<string, unknown>): Promise<any>;
}

export interface IXrm {
  WebApi: IXrmWebApi;
  Navigation: IXrmNavigation;
}

export function getXrm(): IXrm | null {
  // Try parent first (standard side pane context)
  try {
    const parentXrm = (window.parent as any)?.Xrm;
    if (parentXrm?.WebApi) return parentXrm as IXrm;
  } catch { /* cross-origin */ }

  // Try top (deeply nested: iframe inside dialog inside main app)
  try {
    const topXrm = (window.top as any)?.Xrm;
    if (topXrm?.WebApi) return topXrm as IXrm;
  } catch { /* cross-origin */ }

  // Try current window
  const windowXrm = (window as any)?.Xrm;
  if (windowXrm?.WebApi) return windowXrm as IXrm;

  return null;
}

export function getXrmWebApi(): IXrmWebApi | null {
  return getXrm()?.WebApi ?? null;
}

/**
 * Get the Dataverse org client URL (e.g. "https://org.crm.dynamics.com").
 * Used for direct REST API calls that bypass Xrm.WebApi.
 */
export function getClientUrl(): string | null {
  // Walk frame hierarchy to find Xrm.Utility.getGlobalContext
  const framesToCheck: Array<Window | null> = [];
  try { framesToCheck.push(window.parent); } catch { /* cross-origin */ }
  try { framesToCheck.push(window.top); } catch { /* cross-origin */ }
  framesToCheck.push(window);

  for (const frame of framesToCheck) {
    try {
      const ctx = (frame as any)?.Xrm?.Utility?.getGlobalContext?.();
      const url = ctx?.getClientUrl?.();
      if (url) return url;
    } catch { /* cross-origin or unavailable */ }
  }
  return null;
}

/**
 * Set statecode/statuscode on a record via direct REST API PATCH.
 *
 * Xrm.WebApi.updateRecord silently ignores statecode/statuscode in some
 * environments, so we use fetch against the Web API endpoint directly.
 */
export async function setRecordState(
  entitySetName: string,
  recordId: string,
  statecode: number,
  statuscode: number
): Promise<void> {
  const clientUrl = getClientUrl();
  if (!clientUrl) throw new Error("Cannot resolve Dataverse client URL");

  const cleanId = recordId.replace(/[{}]/g, "");
  const url = `${clientUrl}/api/data/v9.2/${entitySetName}(${cleanId})`;
  const response = await fetch(url, {
    method: "PATCH",
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      Accept: "application/json",
      "OData-MaxVersion": "4.0",
      "OData-Version": "4.0",
    },
    body: JSON.stringify({ statecode, statuscode }),
  });

  if (!response.ok && response.status !== 204) {
    const errText = await response.text().catch(() => "");
    throw new Error(`SetState failed (${response.status}): ${errText}`);
  }
}
