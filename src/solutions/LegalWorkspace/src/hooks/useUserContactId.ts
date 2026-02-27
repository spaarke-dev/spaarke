/**
 * useUserContactId — resolves the linked contact ID for a systemuser.
 *
 * Many Dataverse lookup fields (sprk_assignedattorney, sprk_assignedparalegal)
 * reference contacts rather than systemusers. This hook resolves the
 * systemuser._contactid_value lookup once and caches the result at module level
 * for the lifetime of the session (contact link never changes mid-session).
 *
 * Usage:
 *   const { contactId, isResolved } = useUserContactId(service, userId);
 *   // contactId is string | null (null = no linked contact)
 *   // isResolved is true once the lookup completes
 */

import { useState, useEffect } from 'react';
import { DataverseService } from '../services/DataverseService';

// ---------------------------------------------------------------------------
// Module-level cache (never expires — contact link is static per session)
// ---------------------------------------------------------------------------

const _contactIdCache = new Map<string, string | null>();

// ---------------------------------------------------------------------------
// Return type
// ---------------------------------------------------------------------------

export interface IUseUserContactIdResult {
  /** The linked contact GUID, or null if the user has no linked contact. */
  contactId: string | null;
  /** True once the contact lookup has completed (regardless of result). */
  isResolved: boolean;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useUserContactId(
  service: DataverseService,
  userId: string
): IUseUserContactIdResult {
  const [contactId, setContactId] = useState<string | null>(null);
  const [isResolved, setIsResolved] = useState<boolean>(false);

  useEffect(() => {
    if (!userId) {
      return;
    }

    // Check module-level cache first
    if (_contactIdCache.has(userId)) {
      setContactId(_contactIdCache.get(userId) ?? null);
      setIsResolved(true);
      return;
    }

    let cancelled = false;

    service.getUserContactId(userId).then((result) => {
      if (cancelled) return;

      const resolved = result.success ? (result.data ?? null) : null;
      _contactIdCache.set(userId, resolved);
      setContactId(resolved);
      setIsResolved(true);
    });

    return () => {
      cancelled = true;
    };
  }, [service, userId]);

  return { contactId, isResolved };
}
