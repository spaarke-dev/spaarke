/**
 * The signed-in user's id from the host Xrm — the one copy behind the Create*Wizard
 * services' `_getCurrentUserId` (event, invoice, work assignment), which each carried
 * an identical per-frame loop before task 081.
 *
 * Module-internal to `@spaarke/ui-components` (not exported from the package index).
 */
import { cleanGuid } from './guid';
import { getXrm } from './xrmContext';

/**
 * One frame's user id: `Utility.getGlobalContext().userSettings.userId` (Code Page in a
 * Power App iframe), else `Utility.getUserId()` (PCF / direct host). Non-empty only,
 * braces stripped. Never throws.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function readXrmUserId(xrm: any): string | undefined {
  try {
    const fromContext = xrm?.Utility?.getGlobalContext?.()?.userSettings?.userId;
    if (typeof fromContext === 'string' && fromContext.trim() !== '') return cleanGuid(fromContext);
    const direct = typeof xrm?.Utility?.getUserId === 'function' ? xrm.Utility.getUserId() : undefined;
    if (typeof direct === 'string' && direct.trim() !== '') return cleanGuid(direct);
  } catch {
    /* a host getter threw: this frame has no id */
  }
  return undefined;
}

/**
 * The user id from the NEAREST frame (shared walk) that yields a non-empty one. A frame
 * whose Xrm answers with an empty id is skipped and the walk moves on — as the pre-081
 * per-frame loops did (task 081 round 6, review R5-9).
 */
export function getXrmUserId(): string | undefined {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return readXrmUserId(getXrm((x: any) => !!readXrmUserId(x)));
}

// scratch (081 round 8 router proof)
