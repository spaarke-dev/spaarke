import { authenticatedJsonFetch } from '@shared/services/authenticatedJsonFetch';

/**
 * quickCreateDefaultsService.ts — `GET /api/office/quickcreate/defaults` (task 100, owner decision B).
 *
 * The "+ New" form prefills Assigned To with the signed-in user's OWN linked contact. The server states it
 * (`RecordCreationService.ResolveDefaultAssigneeAsync`), so the prefill is exactly the contact the server assigns a
 * Matter or Project when the request names none — the pane never guesses it from the signed-in email.
 *
 * @see src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs (GetQuickCreateDefaultsAsync)
 */

/** A contact as the Assigned To field shows it. */
export interface DefaultAssignee {
  id: string;
  name: string;
  email?: string;
}

interface QuickCreateDefaultsWire {
  assignedTo?: { id?: unknown; name?: unknown; email?: unknown } | null;
}

/**
 * The prefill for Assigned To, or `null` when the user has no linked contact. THROWS on a non-2xx response or a
 * network failure — the caller decides what an unavailable prefill means (the form: an empty, still-usable field).
 */
export async function fetchDefaultAssignee(
  apiBaseUrl: string,
  token: string,
  getRetryToken: () => Promise<string>
): Promise<DefaultAssignee | null> {
  const res = await authenticatedJsonFetch(
    `${apiBaseUrl}/api/office/quickcreate/defaults`,
    { headers: { 'Content-Type': 'application/json' } },
    token,
    { getRetryToken }
  );
  if (!res.ok) {
    throw new Error(`Quick-create defaults request failed (${res.status})`);
  }

  const data = (await res.json()) as QuickCreateDefaultsWire;
  const a = data?.assignedTo;
  if (!a || typeof a.id !== 'string' || typeof a.name !== 'string' || a.id.length === 0) {
    return null;
  }
  return { id: a.id, name: a.name, ...(typeof a.email === 'string' && a.email ? { email: a.email } : {}) };
}
