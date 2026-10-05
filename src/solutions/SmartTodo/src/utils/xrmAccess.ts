/**
 * xrmAccess — Extended Xrm utilities for the SmartTodo Code Page.
 *
 * Provides getClientUrl() and setRecordState() for operations that cannot be
 * done through Xrm.WebApi alone (statecode/statuscode changes are silently
 * ignored by Xrm.WebApi.updateRecord in some Dataverse environments).
 *
 * For basic Xrm access (getXrm, getWebApi) use ../services/xrmProvider.ts.
 */

import { cleanGuid, getXrm } from '@spaarke/ui-components';

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Get the Dataverse org client URL (e.g. "https://org.crm.dynamics.com").
 * Used for direct REST API calls that bypass Xrm.WebApi.
 */
export function getClientUrl(): string | null {
  // Shared cross-frame walker (task 081 / C-8).
  const xrm = getXrm() as any;
  if (!xrm) return null;

  // Try Xrm.Utility.getGlobalContext().getClientUrl()
  try {
    const ctx = xrm.Utility?.getGlobalContext?.();
    const url = ctx?.getClientUrl?.();
    if (url) return url;
  } catch {
    /* unavailable */
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
  statuscode: number,
): Promise<void> {
  const clientUrl = getClientUrl();
  if (!clientUrl) throw new Error("Cannot resolve Dataverse client URL");

  const cleanId = cleanGuid(recordId);
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
