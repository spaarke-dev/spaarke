/**
 * `data=` URL parser for the Communication Code Page (reference §7.3).
 *
 * Dataverse `navigateTo({ pageType: "webresource", data: "k=v&k2=v2" })` wraps
 * the caller's data string inside a single `?data=<encoded>` query param, so we
 * unwrap that envelope first, then read flat params.
 *
 * Supported params:
 *   mode          compose | view | reply | forward | draft   (required)
 *   id            sprk_communication GUID (required for view/reply/forward/draft)
 *   to, cc        `;`/`,`-separated recipient lists
 *   subject, body compose pre-fill
 *   associatedTo  `<entityType>:<guid>` (repeatable) — stamps an association
 *   bffBaseUrl, tenantId, scope, clientId   standard @spaarke/auth env vars
 */

import {
  COMMUNICATION_MODES,
  type CommunicationMode,
  type ICommunicationAssociation,
  type ICommunicationPageParams,
} from '../types/communication';

const MODES_REQUIRING_ID: readonly CommunicationMode[] = ['view', 'reply', 'forward', 'draft'];

/** Unwrap the Dataverse `?data=<encoded>` envelope into flat search params. */
export function unwrapDataEnvelope(search: string): URLSearchParams {
  const outer = new URLSearchParams(search);
  const envelope = outer.get('data');
  if (envelope) {
    // The envelope may be single- or double-encoded depending on the caller;
    // URLSearchParams handles the inner decoding of `&`/`=` for us.
    try {
      return new URLSearchParams(decodeURIComponent(envelope));
    } catch {
      return new URLSearchParams(envelope);
    }
  }
  return outer;
}

function splitRecipients(raw: string | null): string[] {
  if (!raw) return [];
  return raw
    .split(/[;,]/)
    .map(s => s.trim())
    .filter(s => s.length > 0);
}

function parseAssociations(params: URLSearchParams): ICommunicationAssociation[] {
  const out: ICommunicationAssociation[] = [];
  // Support repeated associatedTo params.
  for (const raw of params.getAll('associatedTo')) {
    const idx = raw.indexOf(':');
    if (idx <= 0 || idx === raw.length - 1) continue; // malformed — skip
    const entityType = raw.slice(0, idx).trim();
    const entityId = raw.slice(idx + 1).trim();
    if (entityType && entityId) out.push({ entityType, entityId });
  }
  return out;
}

function normalizeMode(raw: string | null): { mode: CommunicationMode; warning?: string } {
  const value = (raw ?? '').trim().toLowerCase();
  if (COMMUNICATION_MODES.includes(value as CommunicationMode)) {
    return { mode: value as CommunicationMode };
  }
  return {
    mode: 'compose',
    warning: raw ? `Unknown mode "${raw}" — defaulting to "compose".` : 'No mode provided — defaulting to "compose".',
  };
}

/** Parse the (already unwrapped) flat params into the typed page contract. */
export function parseCommunicationParams(params: URLSearchParams): ICommunicationPageParams {
  const warnings: string[] = [];

  const { mode, warning: modeWarning } = normalizeMode(params.get('mode'));
  if (modeWarning) warnings.push(modeWarning);

  const id = params.get('id')?.trim() || undefined;
  if (MODES_REQUIRING_ID.includes(mode) && !id) {
    warnings.push(`Mode "${mode}" requires an "id" (sprk_communication GUID) but none was provided.`);
  }

  return {
    mode,
    id,
    to: splitRecipients(params.get('to')),
    cc: splitRecipients(params.get('cc')),
    subject: params.get('subject')?.trim() || undefined,
    body: params.get('body') ?? undefined,
    associations: parseAssociations(params),
    auth: {
      bffBaseUrl: params.get('bffBaseUrl')?.trim() || undefined,
      tenantId: params.get('tenantId')?.trim() || undefined,
      scope: params.get('scope')?.trim() || undefined,
      clientId: params.get('clientId')?.trim() || undefined,
    },
    warnings,
  };
}

/** Convenience: unwrap `window.location.search` and parse in one call. */
export function parseFromLocation(search: string): ICommunicationPageParams {
  return parseCommunicationParams(unwrapDataEnvelope(search));
}
