import { apiClient, ApiClientError } from '@shared/services';

/**
 * communicationLookupService.ts
 *
 * Client-side service for looking up an existing `sprk_communication` record by
 * the Outlook email's `internetMessageId`. Backs the FR-27 "save-if-needed"
 * decision: before launching the CreateTodo wizard from the Outlook ribbon, the
 * caller checks whether the email has already been saved to Spaarke. If it has,
 * the wizard launches with `initialRegarding = { entityType: 'sprk_communication',
 * recordId: <foundId>, recordName: <subject> }` directly. If it hasn't, the
 * caller runs the existing save flow first, then re-looks up.
 *
 * Underlying BFF endpoint (consumed; not yet implemented server-side as of
 * smart-todo-decoupling-r3 task 070 — see notes/outlook-ribbon-create-todo.md):
 *
 *   GET /api/office/communications/by-message-id/{internetMessageId}
 *     → 200 { communicationId, subject } when found
 *     → 404 when no `sprk_communication` exists for the given message id
 *
 * Until the BFF endpoint exists, this service surfaces 404 as `null` (treated by
 * the caller as "not saved", which then runs the save flow). This is intentional
 * graceful degradation: the worst-case UX is a redundant save attempt, not a
 * broken click.
 *
 * Product-portability (CLAUDE.md §16): no hardcoded org URLs / tenant IDs.
 * Configuration comes from `apiClient` which is configured at startup from the
 * `BFF_API_BASE_URL` env var (see `outlook/taskpane/index.tsx`).
 *
 * @see projects/smart-todo-decoupling-r3/spec.md FR-27
 * @see projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md
 */

/**
 * Result of looking up an existing `sprk_communication` by Outlook
 * `internetMessageId`. `null` when no matching record exists (email not yet
 * saved to Spaarke), or when the BFF endpoint is not yet deployed.
 */
export interface CommunicationLookupResult {
  /** sprk_communicationid (GUID, lowercased, no braces) */
  communicationId: string;
  /** Display name (typically the email subject) */
  subject: string;
}

/**
 * Response shape returned by the BFF lookup endpoint.
 * @internal
 */
interface BffLookupResponse {
  communicationId: string;
  subject: string;
}

/**
 * Build the BFF endpoint URL for the by-message-id lookup.
 *
 * The internetMessageId is URL-encoded to defend against the `<` / `>` /
 * `@` / `+` characters Outlook may include (RFC-5322 message ids commonly look
 * like `<abc123@host.example.com>`).
 */
function buildEndpoint(internetMessageId: string): string {
  return `/api/office/communications/by-message-id/${encodeURIComponent(internetMessageId)}`;
}

/**
 * Look up an existing `sprk_communication` record by Outlook email
 * `internetMessageId`.
 *
 * @param internetMessageId The RFC-5322 message id of the email (from
 *                          `Office.context.mailbox.item.internetMessageId`).
 *                          When undefined / empty, returns `null` without a
 *                          network call.
 * @returns The found communication record, or `null` if no match (404 from BFF,
 *          or email not yet saved). Re-throws non-404 errors so the caller can
 *          distinguish "not saved" from "lookup failed".
 *
 * @throws {ApiClientError} for any non-404 BFF error (5xx, network, 401).
 *                          Callers MUST handle this — typically by surfacing
 *                          an error MessageBar — rather than treating it as
 *                          "not saved".
 */
export async function findCommunicationByMessageId(
  internetMessageId: string | undefined
): Promise<CommunicationLookupResult | null> {
  // Defensive: undefined / empty / whitespace → no-op without a network call.
  if (!internetMessageId) {
    return null;
  }
  const trimmed = internetMessageId.trim();
  if (trimmed.length === 0) {
    return null;
  }

  try {
    const response = await apiClient.get<BffLookupResponse>(buildEndpoint(trimmed));
    // Defensive: server SHOULD always return both fields; tolerate partial.
    if (!response?.communicationId) {
      return null;
    }
    return {
      communicationId: response.communicationId,
      subject: response.subject ?? '',
    };
  } catch (err) {
    // 404 = "not saved yet" → null (graceful path that triggers the save flow).
    if (err instanceof ApiClientError && err.error.status === 404) {
      return null;
    }
    // All other errors propagate so the caller can show a real error state.
    throw err;
  }
}
