/**
 * Generic lookup item for search-as-you-type fields.
 * Used by LookupField and SendEmailDialog components.
 */
export interface ILookupItem {
  /** Unique identifier (e.g., Dataverse GUID). */
  id: string;
  /** Display name (e.g., "John Smith (john@example.com)"). */
  name: string;
  /**
   * Dataverse logical name of the table this record came from, when the
   * source search tags it (e.g. `userLookup.ts` — `contact` vs `systemuser`).
   * Additive/optional so existing callers that don't set it are unaffected.
   * Task 060 (FR-10): `RecipientField` surfaces this onto `IRecipient.entityType`
   * so a resolved recipient carries its typed identity through to the caller.
   */
  entityType?: 'contact' | 'systemuser';
  /**
   * First-class email address for the record (R6-5, UAT 2026-07-21). Previously the email was only
   * embedded inside `name` as "Full Name (email)" and parsed back out with a regex, which dropped the
   * address whenever the record had no email or an unusual name format. Recipient pickers should use
   * THIS field when present. Optional/additive — callers that don't set it are unaffected.
   */
  email?: string;
}
