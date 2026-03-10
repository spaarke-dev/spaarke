/**
 * Generic lookup item for search-as-you-type fields.
 * Used by LookupField and SendEmailDialog components.
 */
export interface ILookupItem {
  /** Unique identifier (e.g., Dataverse GUID). */
  id: string;
  /** Display name (e.g., "John Smith (john@example.com)"). */
  name: string;
}
