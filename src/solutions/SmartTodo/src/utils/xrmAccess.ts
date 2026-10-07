/**
 * xrmAccess — Extended Xrm utilities for the SmartTodo Code Page.
 *
 * Provides getClientUrl() and setRecordState() for operations that cannot be
 * done through Xrm.WebApi alone (statecode/statuscode changes are silently
 * ignored by Xrm.WebApi.updateRecord in some Dataverse environments).
 *
 * For basic Xrm access use `getXrm` from @spaarke/ui-components (or getWebApi
 * in ../services/xrmProvider.ts).
 */

import { cleanGuid, getXrm } from '@spaarke/ui-components';

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Get the Dataverse org client URL (e.g. "https://org.crm.dynamics.com").
 * Used for direct REST API calls that bypass Xrm.WebApi.
 */
export function getClientUrl(): string | null {
  // Shared cross-frame walker (task 081 / C-8) with the 'clientUrl' capability
  // checked PER FRAME: the nearest frame whose Xrm returns a client URL wins,
  // so a frame with WebApi but no usable Utility is skipped (the behaviour of
  // the former local fallback walk, which task 081 round 2 had dropped).
  try {
    const url = getXrm('clientUrl')?.Utility?.getGlobalContext().getClientUrl();
    return url || null;
  } catch {
    return null;
  }
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
