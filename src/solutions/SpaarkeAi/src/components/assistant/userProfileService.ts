/**
 * userProfileService.ts — My Assistant stated-profile WRITE + ERASURE path (task 042, FR-E1 / FR-F3 / F5).
 *
 * This is the WRITE half of the User Model (task 030 = the server-side READ half). It owns the
 * host-context Dataverse write for `sprk_userprofile`:
 *   1. a TRUE keyed UPSERT by the confirmed Active alternate key `sprk_SystemUser`
 *      (`sprk_userprofiles(sprk_systemuser=<guid>)`) — create-or-update in one atomic PATCH, so there
 *      is NO find-then-create race and the 1:1-per-user invariant is enforced by the unique key index;
 *   2. reconciliation of the chosen practice areas as N:N associates over the intersect
 *      `sprk_userprofile_sprk_practicearea_ref` (nav property
 *      `sprk_UserProfile_sprk_PracticeArea_Ref_sprk_PracticeArea_Ref`, confirmed via `$metadata`);
 *   3. `sprk_profilecompletedon` (the cold-start gate key);
 *   4. a GDPR erasure path (F5): disassociate every intersect row + delete the profile row.
 *
 * ── DATA-ACCESS DECISION (DATA-ACCESS-DECISION-CRITERIA.md + CLAUDE.md §10) ────────────────────────
 * This is a SINGLE-ENTITY host-context write from the SpaarkeAi code page (no AI, no OBO, no
 * cross-system coupling, no bulk, no streaming) → the decision tree lands squarely on the CLIENT
 * (host session), NOT a new BFF endpoint. A new BFF write endpoint would be new hot-path surface
 * (§10 Placement Justification + publish-size + tests) with zero benefit here.
 *
 * WHY raw same-origin Web API fetch rather than the `Xrm.WebApi` JS wrapper: the write requires two
 * things the `Xrm.WebApi` wrapper cannot express — (a) a keyed-upsert PATCH by alternate key (its
 * `updateRecord(id)` takes a GUID only, so it would force a find-then-create the spec forbids), and
 * (b) N:N `$ref` associate/disassociate (no `Xrm.WebApi` method exists). Both are standard OData
 * operations reached over the SAME host session (cookies, same origin), so this stays in the exact
 * auth context `Xrm.WebApi` uses — it is NOT a BFF call and involves no MSAL/OBO token. This mirrors
 * the proven sibling code-page pattern in `PlaybookBuilder/services/dataverseClient.ts` (same-origin
 * cookies; Bearer only when running cross-origin dev). The port is dependency-injected so it is fully
 * unit-testable without a live Xrm/Dataverse.
 *
 * @see projects/spaarkeai-assistant-enhancements-r1/notes/userprofile-schema-contract.md — column contract
 * @see projects/spaarkeai-assistant-enhancements-r1/notes/052-profile-security-review.md — F5 + input-hygiene hand-off
 */

import { getClientUrl } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Schema constants (EXACT logical names — verified against $metadata 2026-07-16)
// ---------------------------------------------------------------------------

/** Entity set (collection) for `sprk_userprofile`. */
export const USERPROFILE_SET = 'sprk_userprofiles';
/** Entity set (collection) for `sprk_practicearea_ref`. */
export const PRACTICEAREA_SET = 'sprk_practicearea_refs';
/**
 * N:N relationship navigation property connecting `sprk_userprofile` → `sprk_practicearea_ref`.
 * Confirmed via `EntityDefinitions(...)/ManyToManyRelationships` (2026-07-16): the SchemaName IS the
 * nav-property name on both sides. (The schema-contract note's guess that the nav prop equals the
 * INTERSECT entity name `sprk_userprofile_sprk_practicearea_ref` was incorrect — corrected here.)
 */
export const PRACTICEAREA_NAV_PROP =
  'sprk_UserProfile_sprk_PracticeArea_Ref_sprk_PracticeArea_Ref';

/** Alternate-key column enforcing 1:1 with the user (EntityKeyIndexStatus=Active, verified 2026-07-16). */
export const SYSTEMUSER_KEY_ATTR = 'sprk_systemuser';

/**
 * `sprk_primaryrole` closed option set — bound to the GLOBAL set `sprk_timekeeperrole`
 * (IsGlobal=True, verified 2026-07-16, resolving schema-contract finding F-1). Label↔value is the
 * contract the server-side producer (task 030) renders; we present the label and store the value.
 * This is a constrained/system-owned value set: the user picks from a fixed dropdown — the value is
 * NEVER LLM-emitted.
 */
export const PRIMARY_ROLE_OPTIONS: ReadonlyArray<{ value: number; label: string }> = [
  { value: 100000000, label: 'Senior Partner' },
  { value: 100000001, label: 'Partner' },
  { value: 100000002, label: 'Senior Associate' },
  { value: 100000003, label: 'Associate' },
  { value: 100000004, label: 'Paralegal' },
  { value: 100000005, label: 'Specialist' },
  { value: 100000006, label: 'Other' },
  { value: 100000007, label: 'Practice Support' },
  { value: 100000008, label: 'Administrator' },
  { value: 100000009, label: 'Legal Operations' },
  { value: 100000010, label: 'Senior Counsel' },
  { value: 100000011, label: 'General Counsel' },
  { value: 100000012, label: 'Counsel' },
  { value: 100000013, label: 'Associate General Counsel' },
];

/**
 * Input-hygiene caps for the two free-text PII fields (052 hand-off "Input hygiene at write time"):
 * bound the injection + prompt-budget surface BEFORE the text reaches the stable prefix (the server
 * renderer, task 032, does the delimiting/labelling). We normalize newlines + trim + length-cap here.
 */
export const FREE_TEXT_MAX_LEN = 2000;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** A selectable practice area (the constrained N:N target — resolver entity, task 010). */
export interface PracticeArea {
  id: string;
  name: string;
  code: string | null;
}

/** The stored profile as read back for cold-start + prefill. */
export interface UserProfileRow {
  id: string;
  /** Cold-start gate key — null/absent ⇒ profile NOT complete ⇒ show the questionnaire. */
  profileCompletedOn: string | null;
  primaryRole: number | null;
  focusAreas: string | null;
  officeLocation: string | null;
  assistantPreferences: string | null;
  profileVersion: number | null;
  /** Currently-associated practice-area record ids (from the N:N intersect). */
  practiceAreaIds: string[];
}

/** The fields collected by the questionnaire and written on submit. */
export interface UserProfileFormValues {
  primaryRole: number | null;
  practiceAreaIds: string[];
  focusAreas: string;
  officeLocation: string;
  assistantPreferences: string;
}

/**
 * The testable data port. The production implementation talks raw OData to the host Dataverse; tests
 * inject a fake. Keeps the questionnaire + orchestration free of transport concerns.
 */
export interface IUserProfilePort {
  getProfileByUser(systemUserId: string): Promise<UserProfileRow | null>;
  listPracticeAreas(): Promise<PracticeArea[]>;
  /** TRUE keyed upsert by `sprk_systemuser`; returns the (created-or-updated) profile id. */
  upsertProfileByUser(
    systemUserId: string,
    fields: {
      name: string;
      primaryRole: number | null;
      focusAreas: string | null;
      officeLocation: string | null;
      assistantPreferences: string | null;
      profileCompletedOn: string;
      profileVersion: number;
    }
  ): Promise<string>;
  associatePracticeArea(profileId: string, practiceAreaId: string): Promise<void>;
  disassociatePracticeArea(profileId: string, practiceAreaId: string): Promise<void>;
  deleteProfile(profileId: string): Promise<void>;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Strip braces + lowercase a GUID for safe use in OData URLs (never hand-roll brace-stripping). */
export function cleanGuid(id: string): string {
  return (id ?? '').replace(/[{}]/g, '').toLowerCase();
}

/**
 * Normalize + cap a free-text PII field before write (052 input hygiene). Returns null for empty so
 * the column is cleared rather than storing whitespace.
 */
export function sanitizeFreeText(value: string | null | undefined): string | null {
  if (value == null) return null;
  const normalized = value.replace(/\r\n?/g, '\n').trim();
  if (normalized.length === 0) return null;
  return normalized.length > FREE_TEXT_MAX_LEN ? normalized.slice(0, FREE_TEXT_MAX_LEN) : normalized;
}

/** Trim + cap the single-line office field; null when empty. */
export function sanitizeOffice(value: string | null | undefined): string | null {
  if (value == null) return null;
  const trimmed = value.replace(/[\r\n]+/g, ' ').trim();
  return trimmed.length === 0 ? null : trimmed.slice(0, 100);
}

/** True when the profile is complete (cold-start gate: incomplete ⇒ keep showing the questionnaire). */
export function isProfileComplete(row: UserProfileRow | null): boolean {
  return row !== null && row.profileCompletedOn !== null && row.profileCompletedOn !== undefined;
}

// ---------------------------------------------------------------------------
// User-scope memory seed (FR-F3 / D-042-01)
// ---------------------------------------------------------------------------

/** Stable key for the single stated-preference fact seeded into User-scope memory. */
export const ASSISTANT_MEMORY_SEED_KEY = 'Assistant preferences';

/** The ONE User-scope memory fact seeded from the questionnaire (server resolves the subject/systemuserid). */
export interface AssistantMemorySeed {
  factType: 'keyFact';
  key: string;
  value: string;
}

/**
 * Composes the ONE User-scope memory seed fact (source=user) from the questionnaire (FR-F3). Only the
 * free-text `assistantPreferences` is seeded: the structured role + practice-area fields are already
 * carried into the prompt by the typed stated-profile READ (task 030), so seeding them into memory would
 * duplicate. Returns null when there is no free-text preference worth seeding (⇒ the caller skips the POST).
 */
export function buildAssistantMemorySeed(form: UserProfileFormValues): AssistantMemorySeed | null {
  const value = sanitizeFreeText(form.assistantPreferences);
  if (!value) return null;
  return { factType: 'keyFact', key: ASSISTANT_MEMORY_SEED_KEY, value };
}

// ---------------------------------------------------------------------------
// Raw Web API port implementation
// ---------------------------------------------------------------------------

/** Error carrying the HTTP status so callers can special-case 404 (no profile yet). */
export class DataverseHttpError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message);
    this.name = 'DataverseHttpError';
  }
}

/** Injectable transport dependencies (defaulted to the host same-origin Web API in production). */
export interface UserProfilePortDeps {
  /** Base URL of the Dataverse Web API, e.g. `https://org.crm.dynamics.com/api/data/v9.2`. */
  getBaseUrl?: () => string;
  /** Fetch implementation (defaults to `window.fetch`). */
  fetchImpl?: typeof fetch;
  /**
   * Optional Dataverse-scoped token getter, used ONLY when the page is NOT same-origin with the org
   * (dev server). In the MDA custom-page host SpaarkeAi runs same-origin, so cookies suffice and this
   * is not invoked. Mirrors the PlaybookBuilder sibling pattern.
   */
  getToken?: () => Promise<string>;
  /** Whether the caller is same-origin with the Dataverse org (defaults to a heuristic). */
  isSameOrigin?: () => boolean;
}

function defaultBaseUrl(): string {
  const clientUrl = getClientUrl();
  if (clientUrl) return `${clientUrl}/api/data/v9.2`;
  // Fallback: same-origin web resource.
  return `${(typeof window !== 'undefined' && window.location?.origin) || ''}/api/data/v9.2`;
}

function defaultIsSameOrigin(getBaseUrl: () => string): boolean {
  try {
    if (typeof window === 'undefined') return true;
    const base = new URL(getBaseUrl(), window.location.href);
    return base.origin === window.location.origin;
  } catch {
    return true;
  }
}

/**
 * Constructs the production port backed by the host Dataverse Web API (raw same-origin OData).
 * All HTTP is funnelled through one `request` helper so associate/disassociate/upsert/delete share
 * the identical auth + error contract.
 */
export function createDataverseUserProfilePort(deps: UserProfilePortDeps = {}): IUserProfilePort {
  const getBaseUrl = deps.getBaseUrl ?? defaultBaseUrl;
  const fetchImpl = deps.fetchImpl ?? ((...args: Parameters<typeof fetch>) => fetch(...args));
  const isSameOrigin = deps.isSameOrigin ?? (() => defaultIsSameOrigin(getBaseUrl));

  async function request(path: string, init: RequestInit = {}): Promise<Response> {
    const headers: Record<string, string> = {
      Accept: 'application/json',
      'OData-MaxVersion': '4.0',
      'OData-Version': '4.0',
      'Content-Type': 'application/json; charset=utf-8',
      ...(init.headers as Record<string, string> | undefined),
    };
    // Same-origin (MDA host): rely on the session cookie. Cross-origin dev: attach a Bearer token.
    if (!isSameOrigin() && deps.getToken) {
      try {
        headers['Authorization'] = `Bearer ${await deps.getToken()}`;
      } catch {
        /* proceed without — the request will 401 and surface a clear error */
      }
    }
    const url = path.startsWith('http') ? path : `${getBaseUrl()}${path}`;
    const response = await fetchImpl(url, { credentials: 'include', ...init, headers });
    if (!response.ok) {
      const body = await response.text().catch(() => '');
      throw new DataverseHttpError(response.status, `Dataverse ${response.status} ${init.method ?? 'GET'} ${path}: ${body}`);
    }
    return response;
  }

  return {
    async getProfileByUser(systemUserId: string): Promise<UserProfileRow | null> {
      const key = cleanGuid(systemUserId);
      const select =
        '$select=sprk_userprofileid,sprk_profilecompletedon,sprk_primaryrole,sprk_focusareas,sprk_officelocation,sprk_assistantpreferences,sprk_profileversion';
      const expand = `$expand=${PRACTICEAREA_NAV_PROP}($select=sprk_practicearea_refid)`;
      // UAT 2026-07-18 FIX: `sprk_systemuser` is a Lookup alternate key, and Dataverse rejects the
      // key-URL form `sprk_userprofiles(sprk_systemuser=<guid>)` for lookup keys with a 400 ("The key
      // in the request URI is not valid"). That 400 was fataling the My Assistant load (it shares a
      // Promise.all with listPracticeAreas, so the practice-areas dropdown came up empty). Query by the
      // lookup's `_value` filter instead — verified working. A no-match returns an empty collection
      // (→ null here), so no 404 special-case is needed.
      const filter = `$filter=_${SYSTEMUSER_KEY_ATTR}_value eq ${key}`;
      const path = `/${USERPROFILE_SET}?${select}&${expand}&${filter}&$top=1`;
      const response = await request(path, { method: 'GET' });
      const body = (await response.json()) as { value?: Array<Record<string, unknown>> };
      const row = body.value?.[0];
      if (!row) return null;
      const related = (row[PRACTICEAREA_NAV_PROP] as Array<Record<string, unknown>> | undefined) ?? [];
      return {
        id: String(row.sprk_userprofileid ?? ''),
        profileCompletedOn: (row.sprk_profilecompletedon as string | null) ?? null,
        primaryRole: (row.sprk_primaryrole as number | null) ?? null,
        focusAreas: (row.sprk_focusareas as string | null) ?? null,
        officeLocation: (row.sprk_officelocation as string | null) ?? null,
        assistantPreferences: (row.sprk_assistantpreferences as string | null) ?? null,
        profileVersion: (row.sprk_profileversion as number | null) ?? null,
        practiceAreaIds: related
          .map((r) => String(r.sprk_practicearea_refid ?? ''))
          .filter((id) => id.length > 0),
      };
    },

    async listPracticeAreas(): Promise<PracticeArea[]> {
      const path = `/${PRACTICEAREA_SET}?$select=sprk_practicearea_refid,sprk_practiceareaname,sprk_practiceareacode&$orderby=sprk_practiceareaname`;
      const response = await request(path, { method: 'GET' });
      const body = (await response.json()) as { value?: Array<Record<string, unknown>> };
      return (body.value ?? []).map((r) => ({
        id: String(r.sprk_practicearea_refid ?? ''),
        name: String(r.sprk_practiceareaname ?? ''),
        code: (r.sprk_practiceareacode as string | null) ?? null,
      }));
    },

    async upsertProfileByUser(systemUserId, fields): Promise<string> {
      const key = cleanGuid(systemUserId);
      const body: Record<string, unknown> = {
        sprk_name: fields.name,
        sprk_primaryrole: fields.primaryRole,
        sprk_focusareas: fields.focusAreas,
        sprk_officelocation: fields.officeLocation,
        sprk_assistantpreferences: fields.assistantPreferences,
        sprk_profilecompletedon: fields.profileCompletedOn,
        sprk_profileversion: fields.profileVersion,
      };
      // UAT 2026-07-18 FIX: the previous TRUE keyed upsert PATCHed `sprk_userprofiles(sprk_systemuser=<key>)`,
      // but that lookup-alternate-key URL form is rejected by Dataverse (400 — same defect as the read).
      // Emulate upsert with read-then-write instead: find the existing row by the lookup `_value` filter;
      // PATCH it by primary id when present, else POST a create binding the `sprk_SystemUser` lookup. Not
      // atomic, but correct — and the profile is 1:1 per user (Active alternate-key index still guards duplicates).
      const findPath = `/${USERPROFILE_SET}?$select=sprk_userprofileid&$filter=_${SYSTEMUSER_KEY_ATTR}_value eq ${key}&$top=1`;
      const findResp = await request(findPath, { method: 'GET' });
      const found = (await findResp.json()) as { value?: Array<{ sprk_userprofileid?: string }> };
      const existingId = found.value?.[0]?.sprk_userprofileid;
      if (existingId) {
        await request(`/${USERPROFILE_SET}(${cleanGuid(existingId)})`, {
          method: 'PATCH',
          body: JSON.stringify(body),
        });
        return cleanGuid(existingId);
      }
      // Create — bind the systemuser lookup (nav property `sprk_SystemUser`, verified via metadata).
      const createBody = { ...body, 'sprk_SystemUser@odata.bind': `/systemusers(${key})` };
      const response = await request(`/${USERPROFILE_SET}`, {
        method: 'POST',
        headers: { Prefer: 'return=representation' },
        body: JSON.stringify(createBody),
      });
      const repr = (await response.json().catch(() => null)) as Record<string, unknown> | null;
      const fromBody = repr && typeof repr.sprk_userprofileid === 'string' ? repr.sprk_userprofileid : null;
      if (fromBody) return cleanGuid(fromBody);
      const entityId = response.headers.get('OData-EntityId');
      const match = entityId?.match(/\(([0-9a-f-]+)\)/i);
      if (match) return cleanGuid(match[1]);
      throw new Error('upsertProfileByUser: could not resolve the profile id from the create response.');
    },

    async associatePracticeArea(profileId, practiceAreaId): Promise<void> {
      const pid = cleanGuid(profileId);
      const paid = cleanGuid(practiceAreaId);
      await request(`/${USERPROFILE_SET}(${pid})/${PRACTICEAREA_NAV_PROP}/$ref`, {
        method: 'POST',
        body: JSON.stringify({ '@odata.id': `${getBaseUrl()}/${PRACTICEAREA_SET}(${paid})` }),
      });
    },

    async disassociatePracticeArea(profileId, practiceAreaId): Promise<void> {
      const pid = cleanGuid(profileId);
      const paid = cleanGuid(practiceAreaId);
      await request(`/${USERPROFILE_SET}(${pid})/${PRACTICEAREA_NAV_PROP}(${paid})/$ref`, {
        method: 'DELETE',
      });
    },

    async deleteProfile(profileId): Promise<void> {
      await request(`/${USERPROFILE_SET}(${cleanGuid(profileId)})`, { method: 'DELETE' });
    },
  };
}

// ---------------------------------------------------------------------------
// Orchestration — save + erase (transport-agnostic; operate on IUserProfilePort)
// ---------------------------------------------------------------------------

export interface SaveProfileArgs {
  systemUserId: string;
  /** Display name for the required `sprk_name` primary column (falls back to a deterministic label). */
  displayName?: string;
  form: UserProfileFormValues;
  /** Optionally pass a pre-fetched row to skip the read (e.g. the cold-start prefetch). */
  existing?: UserProfileRow | null;
}

export interface SaveProfileResult {
  profileId: string;
  associated: string[];
  disassociated: string[];
}

/**
 * Persists the questionnaire: keyed upsert (idempotent, no find-then-create) → sets
 * `sprk_profilecompletedon` (cold-start gate satisfied) → reconciles the N:N practice-area set
 * (associate additions, disassociate removals). Idempotent: re-submitting the same values upserts the
 * SAME row and produces an empty add/remove delta.
 */
export async function saveMyAssistantProfile(
  port: IUserProfilePort,
  args: SaveProfileArgs
): Promise<SaveProfileResult> {
  const existing = args.existing !== undefined ? args.existing : await port.getProfileByUser(args.systemUserId);

  const profileId = await port.upsertProfileByUser(args.systemUserId, {
    name: args.displayName?.trim() || `User Profile ${cleanGuid(args.systemUserId)}`,
    primaryRole: args.form.primaryRole,
    focusAreas: sanitizeFreeText(args.form.focusAreas),
    officeLocation: sanitizeOffice(args.form.officeLocation),
    assistantPreferences: sanitizeFreeText(args.form.assistantPreferences),
    profileCompletedOn: new Date().toISOString(),
    profileVersion: (existing?.profileVersion ?? 0) + 1,
  });

  const current = new Set(existing?.practiceAreaIds.map(cleanGuid) ?? []);
  const selected = new Set(args.form.practiceAreaIds.map(cleanGuid));
  const toAdd = [...selected].filter((id) => !current.has(id));
  const toRemove = [...current].filter((id) => !selected.has(id));

  for (const id of toAdd) {
    await port.associatePracticeArea(profileId, id);
  }
  for (const id of toRemove) {
    await port.disassociatePracticeArea(profileId, id);
  }

  return { profileId, associated: toAdd, disassociated: toRemove };
}

export interface EraseProfileArgs {
  systemUserId: string;
  /**
   * Optional best-effort erase of the caller's User-scope AI memory (GDPR / F5). Wire to the EXISTING
   * `DELETE /api/memory/user` governance endpoint (task 052). Failure here NEVER blocks the profile
   * delete. Reuses an existing endpoint — NO new BFF surface.
   */
  eraseUserMemory?: () => Promise<void>;
}

export interface EraseProfileResult {
  deleted: boolean;
  disassociated: string[];
  memoryErased: boolean;
}

/**
 * GDPR erasure (F5, owner-approved into task 042): disassociate every N:N intersect row, delete the
 * `sprk_userprofile` row, and best-effort erase the caller's User-scope AI memory. Idempotent — a
 * no-op (deleted:false) when no profile exists.
 */
export async function eraseMyAssistantProfile(
  port: IUserProfilePort,
  args: EraseProfileArgs
): Promise<EraseProfileResult> {
  const existing = await port.getProfileByUser(args.systemUserId);

  let disassociated: string[] = [];
  let deleted = false;
  if (existing) {
    // Explicitly disassociate the intersect rows first (F5 wording), then delete the profile row.
    for (const paId of existing.practiceAreaIds) {
      await port.disassociatePracticeArea(existing.id, paId);
    }
    disassociated = [...existing.practiceAreaIds];
    await port.deleteProfile(existing.id);
    deleted = true;
  }

  let memoryErased = false;
  if (args.eraseUserMemory) {
    try {
      await args.eraseUserMemory();
      memoryErased = true;
    } catch {
      // Best-effort — never block the profile erasure on the memory call.
      memoryErased = false;
    }
  }

  return { deleted, disassociated, memoryErased };
}
