/**
 * The RecordFamily page's launch parameters (field-mapping-server-write-path-r1 task 075).
 *
 * The ribbon (`Spaarke.FieldMapping.Push.pushUpdates` in `sprk_fieldmapping_push.js`) opens this page with the
 * dialog data `entityName=<logical name>&id=<record id>`. `readFamilyParams` takes the already-parsed data
 * (`parseDataParams()` from `@spaarke/ui-components`) and returns the validated anchor, or `null` when either
 * value is missing or malformed - the page then shows its error bar instead of calling the server.
 *
 * Validation is a client-side courtesy (a clean error, no request built from junk). The server is still the
 * authority: it answers 404 for an unknown or unreadable anchor (FR-31). A contract test in
 * `Spaarke.UI.Components` (`fieldMappingPush.opensFamily.test.ts`) runs the exact string the ribbon builds
 * through this function, so renaming a key on either side fails it.
 *
 * Pure and dependency-free so it can be tested without rendering the page.
 */

/** A Dataverse table logical name: lower-case letters, digits and underscores, not starting with a digit. */
const LOGICAL_NAME = /^[a-z_][a-z0-9_]*$/;

/** A GUID, with or without braces (the ribbon strips them; the form API returns them braced). */
const GUID_BODY = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}';
const GUID = new RegExp(`^(?:${GUID_BODY}|\\{${GUID_BODY}\\})$`);

export interface IFamilyParams {
  /** The anchor's table logical name. */
  entityName: string;
  /** The anchor's id as given (braces and case are normalised by the modal before it enters a URL). */
  id: string;
}

export function readFamilyParams(params: Readonly<Record<string, string | undefined>>): IFamilyParams | null {
  const entityName = params.entityName ?? '';
  const id = params.id ?? '';
  if (!LOGICAL_NAME.test(entityName) || !GUID.test(id)) return null;
  return { entityName, id };
}
