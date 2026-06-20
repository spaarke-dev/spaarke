/**
 * useCurrentContactId — Resolve the current systemuser's sprk_contact GUID.
 *
 * UAT 2026-06-19: `sprk_todo.sprk_assignedto` was migrated from a systemuser
 * lookup to a `sprk_contact` (custom Person table) lookup. The "Assigned to Me"
 * filter therefore needs the CONTACT GUID corresponding to the current user,
 * not their systemuser GUID.
 *
 * Resolution flow (one query at mount):
 *   1. Query `sprk_contact` where `_sprk_systemuser_value eq <systemUserId>`
 *      (the sprk_contact table has a sprk_systemuser lookup pointing back
 *      to systemuser — relationship sprk_SystemUser_SystemUser_Contact).
 *   2. If exactly one match → return that contact's `sprk_contactid`.
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
 * @see sprk_contact.sprk_systemuser (SystemUser lookup) — the reverse relationship
 */

import * as React from 'react';
import type { IWebApi } from '../types/todo';

export interface IUseCurrentContactIdOptions {
  webApi: IWebApi;
  /** Current systemuser GUID (from Xrm.Utility.getGlobalContext().userSettings.userId). */
  userId?: string;
}

export interface IUseCurrentContactIdResult {
  /** The resolved sprk_contact GUID, or null when not found / still loading. */
  contactId: string | null;
  /** UAT 2026-06-19 — the resolved contact's display name, for UI display (so
   *  the user sees "Jane Doe" not a raw GUID). Null when not resolved. */
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

    // Select sprk_name too so the UI can display the contact name in
    // place of the raw GUID (e.g., quick-add "Assigned To" field).
    const query = `?$select=sprk_contactid,sprk_name&$filter=_sprk_systemuser_value eq ${userId}&$top=2`;

    webApi
      .retrieveMultipleRecords('sprk_contact', query)
      .then(result => {
        if (cancelled) return;
        const entities = (result.entities ?? []) as Array<{ sprk_contactid?: string; sprk_name?: string }>;
        if (entities.length === 0) {
          // eslint-disable-next-line no-console
          console.warn(
            `[useCurrentContactId] No sprk_contact found for systemuser ${userId}. "Assigned to Me" filter will return empty.`
          );
          setContactId(null);
          setContactName(null);
        } else {
          if (entities.length > 1) {
            // eslint-disable-next-line no-console
            console.warn(`[useCurrentContactId] Multiple sprk_contact records for systemuser ${userId}; using first.`);
          }
          setContactId(entities[0].sprk_contactid ?? null);
          setContactName(entities[0].sprk_name ?? null);
        }
        setIsLoading(false);
      })
      .catch((err: Error) => {
        if (cancelled) return;
        // eslint-disable-next-line no-console
        console.warn('[useCurrentContactId] sprk_contact resolve failed:', err);
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
