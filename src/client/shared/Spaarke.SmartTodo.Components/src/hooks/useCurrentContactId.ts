/**
 * useCurrentContactId — Resolve the current systemuser's OOB contact GUID.
 *
 * UAT 2026-06-20: `sprk_todo.sprk_assignedto` was migrated from a systemuser
 * lookup to the OOB `contact` (Person) table. The OOB `contact` entity has
 * been extended with a custom `sprk_systemuser` lookup pointing back to
 * systemuser (relationship `sprk_SystemUser_SystemUser_Contact`). The
 * "Assigned to Me" filter therefore needs the CONTACT GUID corresponding to
 * the current user, not their systemuser GUID.
 *
 * IMPORTANT: This queries the OOB `contact` entity (NOT a custom `sprk_contact`
 * entity — there is no such custom table). Fields used:
 *   - `contactid` (PK)
 *   - `fullname` (display name)
 *   - `_sprk_systemuser_value` (OData filter form of `sprk_systemuser` lookup)
 *
 * Resolution flow (one query at mount):
 *   1. Query `contact` where `_sprk_systemuser_value eq <systemUserId>`.
 *   2. If exactly one match → return that contact's `contactid`.
 *   3. If zero matches → return null (user has no associated contact;
 *      surfaces should fall back to "no records" rather than filter by an
 *      invalid GUID).
 *   4. If multiple matches → log warning + use the first (data-quality issue;
 *      shouldn't happen in well-curated Spaarke envs).
 *
 * Result is cached for the hook's lifetime (a single query per mount). The
 * hook's `isLoading` flag is true until the query completes; consumers should
 * suppress their own queries until then OR query with a sentinel and re-query
 * when contactId resolves.
 *
 * @see sprk_todo.sprk_assignedto (Contact lookup) — the field this resolves for
 * @see contact.sprk_systemuser (SystemUser lookup) — the reverse relationship
 */

import * as React from 'react';
import type { IWebApi } from '../types/todo';

export interface IUseCurrentContactIdOptions {
  webApi: IWebApi;
  /** Current systemuser GUID (from Xrm.Utility.getGlobalContext().userSettings.userId). */
  userId?: string;
}

export interface IUseCurrentContactIdResult {
  /** The resolved contact GUID, or null when not found / still loading. */
  contactId: string | null;
  /** UAT 2026-06-20 — the resolved contact's display name (`fullname`), for UI
   *  display so the user sees "Jane Doe" not a raw GUID. Null when not resolved. */
  contactName: string | null;
  /** True while the resolve query is in flight. */
  isLoading: boolean;
}

export function useCurrentContactId(options: IUseCurrentContactIdOptions): IUseCurrentContactIdResult {
  const { webApi, userId } = options;
  const [contactId, setContactId] = React.useState<string | null>(null);
  const [contactName, setContactName] = React.useState<string | null>(null);
  const [isLoading, setIsLoading] = React.useState<boolean>(true);

  React.useEffect(() => {
    if (!userId || !webApi.retrieveMultipleRecords) {
      setIsLoading(false);
      return;
    }

    let cancelled = false;
    setIsLoading(true);

    // OOB `contact` entity. The relevant fields:
    //   - `contactid` (PK)
    //   - `fullname` (display name)
    //   - `_sprk_systemuser_value` (OData form of the custom `sprk_systemuser` lookup)
    const query = `?$select=contactid,fullname&$filter=_sprk_systemuser_value eq ${userId}&$top=2`;

    webApi
      .retrieveMultipleRecords('contact', query)
      .then(result => {
        if (cancelled) return;
        const entities = (result.entities ?? []) as Array<{ contactid?: string; fullname?: string }>;
        if (entities.length === 0) {
          // eslint-disable-next-line no-console
          console.warn(
            `[useCurrentContactId] No contact found for systemuser ${userId}. "Assigned to Me" filter will return empty.`
          );
          setContactId(null);
          setContactName(null);
        } else {
          if (entities.length > 1) {
            // eslint-disable-next-line no-console
            console.warn(`[useCurrentContactId] Multiple contact records for systemuser ${userId}; using first.`);
          }
          setContactId(entities[0].contactid ?? null);
          setContactName(entities[0].fullname ?? null);
        }
        setIsLoading(false);
      })
      .catch((err: Error) => {
        if (cancelled) return;
        // eslint-disable-next-line no-console
        console.warn('[useCurrentContactId] contact resolve failed:', err);
        setContactId(null);
        setContactName(null);
        setIsLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [webApi, userId]);

  return { contactId, contactName, isLoading };
}
