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
   * The record's email address as a FIRST-CLASS field (e.g. `systemuser.
   * internalemailaddress` / `contact.emailaddress1`), when the source search
   * populates it (`userLookup.ts`). This is the authoritative value a
   * recipient picker must resolve to — `RecipientField` uses it directly
   * rather than re-parsing it back out of the formatted {@link name} string,
   * which loses the email entirely for records that have none. Additive/
   * optional so existing callers that don't set it fall back to name-parsing.
   * Fix for UAT R4 C12-1 (task 123): selecting a no-email contact must NOT
   * produce an invalid recipient made from the display name.
   */
  email?: string;
  /**
   * Dataverse logical name of the table this record came from, when the
   * source search tags it (e.g. `userLookup.ts` — `contact` vs `systemuser`).
   * Additive/optional so existing callers that don't set it are unaffected.
   * Task 060 (FR-10): `RecipientField` surfaces this onto `IRecipient.entityType`
   * so a resolved recipient carries its typed identity through to the caller.
   */
  entityType?: 'contact' | 'systemuser';
  /**
   * An optional second line `LookupField` shows under {@link name} in its result list (for a person: their email), so two options
   * with the same name can be told apart. Display only: the picked item's {@link id} is what a consumer commits. Absent: the option
   * renders as the name alone, exactly as before, so every consumer that does not set it is unchanged.
   * (spaarke-ontology-platform-r1 task 211, #3062.)
   */
  secondaryText?: string;
}
