/**
 * buildTodoIframeUrl — Pure helper that constructs the OOB MDA "To Do main
 * form" iframe URL for the SmartTodoModal (R4 task 040).
 *
 * Per spec FR-13 / NFR-03:
 *   - The form ID is the OOB MDA "To Do main form" GUID
 *     (`eca59df4-1364-f111-ab0c-7ced8ddc4cc6`).
 *   - The host MUST be supplied by the caller (typically via
 *     `Xrm.Utility.getGlobalContext().getClientUrl()` — see `utils/xrmAccess.ts`).
 *     NO hardcoded environment URLs (NFR-03).
 *   - `navbar=off` suppresses MDA chrome so only the form renders inside the
 *     iframe.
 *
 * Resulting URL shape:
 *   <client>/main.aspx?pagetype=entityrecord&etn=sprk_todo&id=<id>
 *     &formid=<formid>&navbar=off
 *
 * NOTE: This helper deliberately AVOIDS any Xrm or DOM access — it is pure so
 * task 040's unit tests (and any future tests) can pin URL construction without
 * mocking the global Xrm namespace.
 */

/**
 * OOB MDA "To Do main form" GUID (binding to spec FR-13).
 *
 * This is a SOLUTION-PORTABLE GUID assigned by Dataverse at form creation; it
 * is identical across environments because the form is owned by the same
 * managed solution layer. This is the ONLY hardcoded ID that survives the
 * NFR-03 portability rule because form GUIDs are part of the schema contract
 * itself (cf. `sprk_regarding_presave.js` for the same form ID used on the
 * iframe side).
 */
export const TODO_MAIN_FORM_ID = 'eca59df4-1364-f111-ab0c-7ced8ddc4cc6';

export interface BuildTodoIframeUrlOptions {
  /**
   * The Dataverse environment client URL (no trailing slash). E.g.
   * `https://contoso.crm.dynamics.com`. Pass the result of
   * `getClientUrl()` (see `utils/xrmAccess.ts`).
   */
  clientUrl: string;
  /**
   * The `sprk_todoid` GUID of the record to load in the iframe.
   * Braces are stripped if present (Dataverse main.aspx tolerates raw GUIDs).
   */
  todoId: string;
  /**
   * Optional override of the OOB main form GUID. Defaults to
   * {@link TODO_MAIN_FORM_ID}. Provided for test scenarios; production code
   * SHOULD NOT pass this argument.
   */
  formId?: string;
}

/**
 * Build the iframe URL for the OOB MDA To Do main form.
 *
 * @throws Error if `clientUrl` or `todoId` is empty / falsy.
 */
export function buildTodoIframeUrl(opts: BuildTodoIframeUrlOptions): string {
  const { clientUrl, todoId, formId = TODO_MAIN_FORM_ID } = opts;
  if (!clientUrl) {
    throw new Error('buildTodoIframeUrl: clientUrl is required');
  }
  if (!todoId) {
    throw new Error('buildTodoIframeUrl: todoId is required');
  }

  // Normalize: strip trailing slash + brace decoration.
  const host = clientUrl.replace(/\/+$/, '');
  const cleanId = todoId.replace(/[{}]/g, '');

  return (
    `${host}/main.aspx?pagetype=entityrecord` +
    `&etn=sprk_todo` +
    `&id=${cleanId}` +
    `&formid=${formId}` +
    `&navbar=off`
  );
}
