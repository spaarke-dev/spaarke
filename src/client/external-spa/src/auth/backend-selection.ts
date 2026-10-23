/**
 * Run-time backend selection — which customer BFF this tab talks to (task 041; spec FR-17, NFR-02; ADR-028 A8).
 *
 * Under owner decision D-13 every customer has its own backend (BFF URL + BFF app registration). One SPA build serves
 * all of them, so the BFF base URL and the scope cannot be baked at build time. This module resolves the ACTIVE
 * backend `{ customerKey, bffBaseUrl, bffScope }` at run time; `bff-client.ts` (URL) and the token acquirers in
 * `msal-auth.ts` / `standalone-plane.ts` / `host/TeamsHostAdapter.ts` (scope) read it.
 *
 * Where backends come from — Spaarke-owned data only:
 *   - the T240c directory (provisioning): a workforce lookup (by group membership) and a CIAM lookup (by customer
 *     key). Not built yet: {@link DirectoryServiceClient} is a stub behind `VITE_USE_DIRECTORY_SERVICE=true`.
 *   - until it ships, dev configuration ({@link DevConfigDirectory}): `VITE_BACKENDS` (a JSON list) or the one-entry
 *     list built from the existing `VITE_BFF_API_URL` + scope keys.
 *
 * Tenant isolation (NFR-02): a URL may carry only a customer key (`?customer={key}`, invite and C3 links — T240d).
 * The key only CHOOSES among backends the directory/config returned; it never becomes a host or a scope. No other
 * query parameter is read here. Every backend — from config or from the directory — is shape-checked (https origin,
 * app id) before it can become active, and an unresolvable selection fails closed (no default host is invented).
 *
 * Scope (ADR-028 A8): `api://{bffAppId}/user_impersonation` on BOTH planes. The former CIAM `SDAP.Access` default is
 * refused.
 *
 * Storage: `localStorage` holds only customer keys (the known-keys list behind the picker, spec "Picker storage");
 * the active key is per tab in `sessionStorage` (it survives the MSAL login redirect, like the realm). Never a URL,
 * scope or token. Every access is wrapped in try/catch: with storage unavailable, selection still works.
 */

import { MSAL_TENANT_ID, getWorkforceEnvConfig, isTenantId, readOptionalEnv } from '../config';
import type { Realm } from './realm';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** The identity plane a backend is resolved for (the same values as the browser realm). */
export type BackendPlane = Realm;

/**
 * One customer backend, as the directory (or dev config) returns it for a plane. `bffAppId` is the app whose
 * `user_impersonation` scope that plane requests: the customer BFF app on the workforce plane; on the CIAM plane the
 * per-customer CIAM API app (`ciamBffAppId`, T240d option 2: a CIAM account cannot get a workforce-tenant API token).
 */
export interface Backend {
  /** Customer key — the only routing value a link may carry. */
  customerKey: string;
  /** Name shown in the customer picker. */
  displayName: string;
  /** The BFF origin, e.g. `https://spaarke-bff-dev.azurewebsites.net` (no path, no trailing slash). */
  bffBaseUrl: string;
  /** The BFF app id: the scope is `api://{bffAppId}/user_impersonation`. */
  bffAppId: string;
  /**
   * The home directories (Entra tenant ids, lower-case GUIDs) whose workforce contacts this customer serves (ADR-028
   * A9). The browser "company account" sign-in picks the backend by the signed-in `tid`: an entry that lists the tid
   * is the candidate; a tid no entry lists is "not set up" and nothing is sent. Absent on entries that serve no home
   * sign-in (and on the one-entry `VITE_BFF_API_URL` fallback).
   */
  homeTenantIds?: string[];
}

/** The backend this tab is using, with its derived scope. URL and scope always come from the same record. */
export interface ActiveBackend {
  plane: BackendPlane;
  customerKey: string;
  displayName: string;
  bffBaseUrl: string;
  bffScope: string;
}

/**
 * The directory contract (T240c). `resolveWorkforce()` is the caller's backends without a key (workforce: by group
 * membership; dev: the configured list). `resolveByKey()` is the CIAM lookup by customer key (invite / deep link).
 */
export interface BackendDirectory {
  resolveWorkforce(): Promise<Backend[]>;
  resolveByKey(key: string): Promise<Backend | null>;
  /**
   * The backends that serve a home directory (ADR-028 A9): optional, because the dev configuration filters its own
   * list. {@link resolveHomeBackendSelection} re-checks every returned entry against the tid.
   */
  resolveByHomeTenant?(tenantId: string): Promise<Backend[]>;
}

/** The outcome of {@link resolveBackendSelection}. */
export type BackendSelection =
  | { kind: 'selected'; backend: Backend }
  /** More than one backend is known and none is active: show the customer picker. */
  | { kind: 'pick'; options: Backend[] }
  /** Nothing usable: fail closed (the caller shows an error; no request is made). */
  | { kind: 'none'; reason: string };

/**
 * The outcome of {@link resolveHomeBackendSelection}: a {@link BackendSelection} plus `notSetUp` - the signed-in
 * directory is listed by no customer backend, so the caller shows "your organization is not set up" and makes no
 * token request and no BFF call.
 */
export type HomeBackendSelection = BackendSelection | { kind: 'notSetUp' };

/** Raised when no backend can be resolved, or a value from config / the directory is refused. Fail closed. */
export class BackendSelectionError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'BackendSelectionError';
  }
}

// ---------------------------------------------------------------------------
// Validation (applies to config AND directory data)
// ---------------------------------------------------------------------------

/** A customer key: letters, digits, `-` and `_`, 1–64 chars. Anything URL-shaped fails this. */
const CUSTOMER_KEY_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/;

/** A BFF app id (normally a GUID): letters, digits, `.` and `-`; no `/`, `:` or whitespace. */
const BFF_APP_ID_PATTERN = /^[A-Za-z0-9][A-Za-z0-9.-]{0,127}$/;

/** `api://{app id}/user_impersonation` — the only BFF scope on either plane (A8). */
const USER_IMPERSONATION_SCOPE = /^api:\/\/([^/\s]+)\/user_impersonation$/;

export function isCustomerKey(value: unknown): value is string {
  return typeof value === 'string' && CUSTOMER_KEY_PATTERN.test(value);
}

/** The scope for a BFF app id: `api://{bffAppId}/user_impersonation` (ADR-028 A8, both planes). */
export function bffScopeFor(bffAppId: string): string {
  if (!BFF_APP_ID_PATTERN.test(bffAppId)) {
    throw new BackendSelectionError(`[BackendSelection] '${bffAppId}' is not a valid BFF app id.`);
  }
  return `api://${bffAppId}/user_impersonation`;
}

/**
 * Normalise a BFF base URL to its origin. Only an https origin (or http on localhost, for local dev) with no path,
 * query, fragment or credentials is accepted; anything else throws.
 */
export function normalizeBffBaseUrl(value: unknown): string {
  let url: URL | null = null;
  if (typeof value === 'string') {
    try {
      url = new URL(value);
    } catch {
      url = null;
    }
  }
  const localHttp = url?.protocol === 'http:' && (url.hostname === 'localhost' || url.hostname === '127.0.0.1');
  const ok =
    url !== null &&
    (url.protocol === 'https:' || localHttp) &&
    !url.username &&
    !url.password &&
    url.pathname === '/' &&
    !url.search &&
    !url.hash &&
    !(value as string).includes('?') &&
    !(value as string).includes('#');
  if (!ok) {
    throw new BackendSelectionError(
      `[BackendSelection] BFF base URL '${String(value)}' is refused: expected an https origin with no path or query.`
    );
  }
  return (url as URL).origin;
}

/** Validate one backend record (from config or the directory). Throws on any malformed field. */
export function validateBackend(raw: unknown): Backend {
  const b = raw as Partial<Backend> | null;
  if (!b || typeof b !== 'object')
    throw new BackendSelectionError('[BackendSelection] A backend entry is not an object.');
  if (!isCustomerKey(b.customerKey)) {
    throw new BackendSelectionError(`[BackendSelection] Customer key '${String(b.customerKey)}' is refused.`);
  }
  if (typeof b.bffAppId !== 'string' || !BFF_APP_ID_PATTERN.test(b.bffAppId)) {
    throw new BackendSelectionError(
      `[BackendSelection] Backend '${b.customerKey}' has no valid bffAppId (got '${String(b.bffAppId)}').`
    );
  }
  const displayName = typeof b.displayName === 'string' && b.displayName.trim() ? b.displayName.trim() : b.customerKey;
  const homeTenantIds =
    b.homeTenantIds === undefined ? undefined : validateHomeTenantIds(b.customerKey, b.homeTenantIds);
  return {
    customerKey: b.customerKey,
    displayName,
    bffBaseUrl: normalizeBffBaseUrl(b.bffBaseUrl),
    bffAppId: b.bffAppId,
    // Only present when the entry names home directories, so a plain entry keeps its exact shape.
    ...(homeTenantIds ? { homeTenantIds } : {}),
  };
}

/** The tenant id Entra reports for personal Microsoft accounts (MSA): never a customer directory. */
const CONSUMER_ACCOUNT_TENANT_ID = '9188040d-6c67-4c5b-b112-36a304b66dad';

/**
 * The tenants that are never a customer's HOME directory: Spaarke's own (its employees are licensed users, not workforce
 * contacts, and the A8 tenant-pinned sign-in serves them), the CIAM tenant (partners) and the Microsoft consumer-account tenant
 * (personal accounts are not an organization's directory). Lower-case. Spaarke's tenant id
 * is read when the build configures it (`VITE_TEAMS_MSAL_TENANT_ID`); a build that does not has no Spaarke tenant to compare.
 */
function reservedHomeTenantIds(): Set<string> {
  const reserved = new Set<string>([CONSUMER_ACCOUNT_TENANT_ID]);
  if (isTenantId(MSAL_TENANT_ID)) reserved.add(MSAL_TENANT_ID.toLowerCase());
  const spaarke = readOptionalEnv('VITE_TEAMS_MSAL_TENANT_ID');
  if (isTenantId(spaarke)) reserved.add(spaarke.toLowerCase());
  return reserved;
}

/**
 * `homeTenantIds` must be an array of tenant GUIDs (never an alias such as `organizations`) that are neither Spaarke's
 * tenant nor the CIAM tenant; stored lower-case. Refused here, at config / directory validation, so a mis-typed entry
 * cannot make Spaarke's or the CIAM directory resolve to a customer backend.
 */
function validateHomeTenantIds(customerKey: string, value: unknown): string[] {
  if (!Array.isArray(value) || !value.every(isTenantId)) {
    throw new BackendSelectionError(
      `[BackendSelection] Backend '${customerKey}' has invalid homeTenantIds: expected an array of tenant GUIDs.`
    );
  }
  const ids = [...new Set((value as string[]).map(v => v.toLowerCase()))];
  const reserved = reservedHomeTenantIds();
  if (ids.some(id => reserved.has(id))) {
    throw new BackendSelectionError(
      `[BackendSelection] Backend '${customerKey}' has invalid homeTenantIds: Spaarke's tenant and the CIAM tenant are never a customer's home directory.`
    );
  }
  return ids;
}

/** The app id of an `api://{id}/user_impersonation` scope; throws (naming the env key) for any other scope. */
function appIdFromScope(scope: string, envKey: string): string {
  const match = USER_IMPERSONATION_SCOPE.exec(scope);
  if (!match || !BFF_APP_ID_PATTERN.test(match[1])) {
    throw new BackendSelectionError(
      `[BackendSelection] ${envKey} must be api://{customer BFF app id}/user_impersonation, not '${scope}' ` +
        '(ADR-028 A8: the only BFF scope on both planes).'
    );
  }
  return match[1];
}

// ---------------------------------------------------------------------------
// Dev configuration (until T240c ships)
// ---------------------------------------------------------------------------

/** Customer key of the one-entry fallback built from `VITE_BFF_API_URL`. */
export const DEFAULT_CUSTOMER_KEY = 'default';

/**
 * One `VITE_BACKENDS` entry: `{ customerKey, displayName, bffBaseUrl, bffAppId, ciamBffAppId? }`. `ciamBffAppId` is a
 * dev-only override for the CIAM plane, because dev's CIAM audience is a separate app in the CIAM tenant; on a stamp
 * (T240d option 3) the customer BFF app is also the CIAM audience and it is omitted.
 */
interface DevBackendEntry extends Backend {
  ciamBffAppId?: string;
}

function parseBackendsJson(json: string): DevBackendEntry[] {
  let parsed: unknown;
  try {
    parsed = JSON.parse(json);
  } catch {
    throw new BackendSelectionError('[BackendSelection] VITE_BACKENDS is not valid JSON.');
  }
  if (!Array.isArray(parsed) || parsed.length === 0) {
    throw new BackendSelectionError('[BackendSelection] VITE_BACKENDS must be a non-empty JSON array.');
  }
  const seen = new Set<string>();
  return parsed.map(raw => {
    const backend = validateBackend(raw);
    if (seen.has(backend.customerKey)) {
      throw new BackendSelectionError(`[BackendSelection] VITE_BACKENDS lists '${backend.customerKey}' twice.`);
    }
    seen.add(backend.customerKey);
    const ciam = (raw as { ciamBffAppId?: unknown }).ciamBffAppId;
    if (ciam !== undefined && (typeof ciam !== 'string' || !BFF_APP_ID_PATTERN.test(ciam))) {
      throw new BackendSelectionError(
        `[BackendSelection] Backend '${backend.customerKey}' has an invalid ciamBffAppId.`
      );
    }
    return { ...backend, ...(ciam !== undefined ? { ciamBffAppId: ciam as string } : {}) };
  });
}

/**
 * The configured dev backends for a plane. `VITE_BACKENDS` when set; otherwise one entry from `VITE_BFF_API_URL`
 * with the plane's app id taken from `VITE_TEAMS_MSAL_BFF_SCOPE` (workforce, read through `getWorkforceEnvConfig`)
 * or `VITE_MSAL_BFF_SCOPE` (CIAM). Throws when nothing is configured or a value is refused (fail closed).
 */
export function devConfigBackends(plane: BackendPlane): Backend[] {
  const json = readOptionalEnv('VITE_BACKENDS');
  if (json) {
    return parseBackendsJson(json).map(({ ciamBffAppId, ...backend }) =>
      plane === 'ciam' && ciamBffAppId ? { ...backend, bffAppId: ciamBffAppId } : backend
    );
  }

  const url = readOptionalEnv('VITE_BFF_API_URL');
  if (!url) {
    throw new BackendSelectionError(
      '[BackendSelection] No backend is configured: set VITE_BACKENDS or VITE_BFF_API_URL (dev), or enable the ' +
        'directory service (VITE_USE_DIRECTORY_SERVICE) once T240c ships.'
    );
  }

  let appId: string;
  if (plane === 'workforce') {
    appId = appIdFromScope(getWorkforceEnvConfig().bffScope, 'VITE_TEAMS_MSAL_BFF_SCOPE');
  } else {
    const scope = readOptionalEnv('VITE_MSAL_BFF_SCOPE');
    if (!scope) {
      throw new BackendSelectionError(
        '[BackendSelection] No CIAM backend scope is configured: set VITE_MSAL_BFF_SCOPE ' +
          '(api://{CIAM BFF app id}/user_impersonation) or VITE_BACKENDS.'
      );
    }
    appId = appIdFromScope(scope, 'VITE_MSAL_BFF_SCOPE');
  }

  return [
    validateBackend({
      customerKey: DEFAULT_CUSTOMER_KEY,
      displayName: new URL(normalizeBffBaseUrl(url)).host,
      bffBaseUrl: url,
      bffAppId: appId,
    }),
  ];
}

/** Dev directory: answers both lookups from the configured list (dev has no membership or invite data). */
export class DevConfigDirectory implements BackendDirectory {
  constructor(private readonly plane: BackendPlane) {}

  async resolveWorkforce(): Promise<Backend[]> {
    return devConfigBackends(this.plane);
  }

  async resolveByKey(key: string): Promise<Backend | null> {
    if (!isCustomerKey(key)) return null;
    return devConfigBackends(this.plane).find(b => b.customerKey === key) ?? null;
  }
}

/**
 * The T240c directory service client — NOT BUILT YET.
 *
 * TODO(provisioning task 240c): call the T240c directory. Workforce: the membership lookup (`sprk-{customerId}-users`)
 * with the caller's workforce token. CIAM: the CIAM-authenticated lookup by customer key (contacts have no membership
 * lookup, so `resolveWorkforce()` returns [] on that plane). The directory URL and its scope are Spaarke-owned
 * configuration, never link input. Every result still passes `validateBackend` in `resolveBackendSelection`.
 *
 * Until then it throws, so a build with `VITE_USE_DIRECTORY_SERVICE=true` fails closed instead of guessing a backend.
 */
export class DirectoryServiceClient implements BackendDirectory {
  constructor(private readonly plane: BackendPlane) {}

  async resolveWorkforce(): Promise<Backend[]> {
    throw new BackendSelectionError(
      `[BackendSelection] The T240c directory service is not available yet (plane '${this.plane}'); ` +
        'unset VITE_USE_DIRECTORY_SERVICE to use the dev backend list.'
    );
  }

  async resolveByKey(_key: string): Promise<Backend | null> {
    throw new BackendSelectionError('[BackendSelection] The T240c directory service is not available yet.');
  }
}

function isDirectoryServiceEnabled(): boolean {
  return readOptionalEnv('VITE_USE_DIRECTORY_SERVICE') === 'true';
}

/**
 * True when this SPA's backend directory for `plane` is exactly ONE configured backend (the dev configuration; e.g. the
 * single `default` entry built from `VITE_BFF_API_URL`). Task 185 (pass 1 K1): the invite code is then sent to that
 * backend even when the invite link's `?customer=` key differs, since it is the only backend the link can mean. False
 * behind the T240c directory flag (the directory is not a fixed list) and when nothing usable is configured. Never throws.
 */
export function hasSingleConfiguredBackend(plane: BackendPlane): boolean {
  if (isDirectoryServiceEnabled()) return false;
  try {
    return devConfigBackends(plane).length === 1;
  } catch {
    return false;
  }
}

/** The directory for a plane: the T240c client behind its flag, else the dev configuration. */
export function createBackendDirectory(plane: BackendPlane): BackendDirectory {
  return isDirectoryServiceEnabled() ? new DirectoryServiceClient(plane) : new DevConfigDirectory(plane);
}

// ---------------------------------------------------------------------------
// Storage — customer keys only
// ---------------------------------------------------------------------------

/** localStorage: the customer keys this browser has used (drives the picker on a cold landing). Keys only. */
const KNOWN_KEYS_STORAGE_KEY = 'spaarke.ext.knownCustomers';
/** sessionStorage: the active customer key for this tab (survives the login redirect). */
const ACTIVE_KEY_STORAGE_KEY = 'spaarke.ext.activeCustomer';
const MAX_KNOWN_KEYS = 20;

/** The remembered customer keys (validated; tampered entries dropped). [] when storage is unavailable. */
export function getKnownCustomerKeys(): string[] {
  try {
    const raw = localStorage.getItem(KNOWN_KEYS_STORAGE_KEY);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return [...new Set(parsed.filter(isCustomerKey))].slice(0, MAX_KNOWN_KEYS);
  } catch {
    return [];
  }
}

/** Remember a customer key (most recent first). Best-effort. */
export function rememberCustomerKey(key: string): void {
  if (!isCustomerKey(key)) return;
  try {
    const keys = [key, ...getKnownCustomerKeys().filter(k => k !== key)].slice(0, MAX_KNOWN_KEYS);
    localStorage.setItem(KNOWN_KEYS_STORAGE_KEY, JSON.stringify(keys));
  } catch {
    // localStorage unavailable (private mode / blocked): the picker simply knows fewer keys next time.
  }
}

function getStoredActiveKey(): string | null {
  try {
    const raw = sessionStorage.getItem(ACTIVE_KEY_STORAGE_KEY);
    return isCustomerKey(raw) ? raw : null;
  } catch {
    return null;
  }
}

function setStoredActiveKey(key: string): void {
  try {
    sessionStorage.setItem(ACTIVE_KEY_STORAGE_KEY, key);
  } catch {
    // sessionStorage unavailable: after the login redirect the selection runs again (picker or the single default).
  }
}

function clearStoredActiveKey(): void {
  try {
    sessionStorage.removeItem(ACTIVE_KEY_STORAGE_KEY);
  } catch {
    // no-op
  }
}

/** The `?customer=` key of a query string, if it is a well-formed key. It only chooses; it is never a host or scope. */
export function readCustomerQueryKey(
  search: string = typeof window !== 'undefined' ? window.location.search : ''
): string | null {
  try {
    const value = new URLSearchParams(search).get('customer');
    return isCustomerKey(value) ? value : null;
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// Selection
// ---------------------------------------------------------------------------

export interface ResolveBackendOptions {
  /** Defaults to {@link createBackendDirectory}(plane). */
  directory?: BackendDirectory;
  /** Defaults to `window.location.search`. Only its `customer` parameter is read. */
  search?: string;
}

/**
 * Resolve which backend this tab uses on a plane. Candidates come ONLY from the directory / config:
 *   - workforce: `resolveWorkforce()` (the caller's backends);
 *   - CIAM: `resolveWorkforce()` (dev: the configured list; T240c: none) plus `resolveByKey()` for the `?customer=`
 *     key, the tab's active key and the remembered keys.
 * Then: a `?customer=` key among the candidates wins; else the tab's active key; else the only candidate; several
 * → `pick`; none, or a directory/config failure → `none` (fail closed). A key that is not among the candidates is
 * ignored. Invalid directory records are dropped.
 */
export async function resolveBackendSelection(
  plane: BackendPlane,
  options: ResolveBackendOptions = {}
): Promise<BackendSelection> {
  const directory = options.directory ?? createBackendDirectory(plane);
  const queryKey = readCustomerQueryKey(options.search);
  const storedKey = getStoredActiveKey();

  const candidates = new Map<string, Backend>();
  const add = (raw: unknown) => {
    try {
      const backend = validateBackend(raw);
      if (!candidates.has(backend.customerKey)) candidates.set(backend.customerKey, backend);
    } catch (err) {
      console.warn('[BackendSelection] Ignoring an invalid backend record:', err);
    }
  };

  try {
    for (const backend of await directory.resolveWorkforce()) add(backend);
    if (plane === 'ciam') {
      const keys = [queryKey, storedKey, ...getKnownCustomerKeys()].filter(
        (k): k is string => !!k && !candidates.has(k)
      );
      for (const key of [...new Set(keys)]) {
        const backend = await directory.resolveByKey(key);
        if (backend && backend.customerKey === key) add(backend);
      }
    }
  } catch (err) {
    return { kind: 'none', reason: err instanceof Error ? err.message : 'The backend directory could not be read.' };
  }

  if (queryKey && candidates.has(queryKey)) return { kind: 'selected', backend: candidates.get(queryKey) as Backend };
  if (storedKey && candidates.has(storedKey)) {
    return { kind: 'selected', backend: candidates.get(storedKey) as Backend };
  }
  const all = [...candidates.values()];
  if (all.length === 1) return { kind: 'selected', backend: all[0] };
  if (all.length > 1) return { kind: 'pick', options: all };
  return { kind: 'none', reason: 'No customer backend is available for this sign-in.' };
}

/**
 * Resolve the backend for a workforce HOME sign-in from the signed-in tenant id (ADR-028 A9; task 058): tid -> the
 * directory entries that list it (`homeTenantIds`) -> the backend. Candidates come ONLY from the directory / config and
 * are always re-filtered by the tid, so a directory that returns more than it was asked for cannot widen the match.
 *   - one entry lists the tid -> `selected`;
 *   - several -> a stored active key or a `?customer=` key may choose AMONG THEM (a key naming any other entry is
 *     ignored, and no key can make an unlisted tid "set up"); otherwise `pick`;
 *   - none (including a non-GUID tid, the Spaarke / CIAM tenants, and the one-entry `VITE_BFF_API_URL` fallback, which
 *     lists no home directory) -> `notSetUp`: the caller makes no token request and no BFF call;
 *   - a directory / config failure -> `none` (fail closed, no default host).
 */
export async function resolveHomeBackendSelection(
  tenantId: string,
  options: ResolveBackendOptions = {}
): Promise<HomeBackendSelection> {
  if (!isTenantId(tenantId)) return { kind: 'notSetUp' };
  const tid = tenantId.toLowerCase();
  const directory = options.directory ?? createBackendDirectory('workforce');

  let raw: Backend[];
  try {
    raw = directory.resolveByHomeTenant ? await directory.resolveByHomeTenant(tid) : await directory.resolveWorkforce();
  } catch (err) {
    return { kind: 'none', reason: err instanceof Error ? err.message : 'The backend directory could not be read.' };
  }

  const candidates = new Map<string, Backend>();
  for (const entry of raw) {
    try {
      const backend = validateBackend(entry);
      if (backend.homeTenantIds?.includes(tid) && !candidates.has(backend.customerKey)) {
        candidates.set(backend.customerKey, backend);
      }
    } catch (err) {
      console.warn('[BackendSelection] Ignoring an invalid backend record:', err);
    }
  }

  const all = [...candidates.values()];
  if (all.length === 0) return { kind: 'notSetUp' };
  const queryKey = readCustomerQueryKey(options.search);
  if (queryKey && candidates.has(queryKey)) return { kind: 'selected', backend: candidates.get(queryKey) as Backend };
  const storedKey = getStoredActiveKey();
  if (storedKey && candidates.has(storedKey)) {
    return { kind: 'selected', backend: candidates.get(storedKey) as Backend };
  }
  if (all.length === 1) return { kind: 'selected', backend: all[0] };
  return { kind: 'pick', options: all };
}

/**
 * The display name of the customer a `?customer={key}` landing link names, for the landing wording ("Work for
 * Contoso?"), or null. Synchronous and configuration-only: it reads the configured workforce backends, so it is null
 * behind the T240c directory flag, for an unknown or malformed key, and on any configuration error. Display only - the
 * key never selects a backend here.
 */
export function landingCustomerName(
  search: string = typeof window !== 'undefined' ? window.location.search : ''
): string | null {
  const key = readCustomerQueryKey(search);
  if (!key || isDirectoryServiceEnabled()) return null;
  try {
    return devConfigBackends('workforce').find(b => b.customerKey === key)?.displayName ?? null;
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// The active backend
// ---------------------------------------------------------------------------

let activeBackend: ActiveBackend | null = null;

/**
 * Make a backend (from {@link resolveBackendSelection} or the picker's options) the tab's active backend. Re-validates
 * it, derives the scope, stores the key for this tab and remembers it.
 */
export function activateBackend(plane: BackendPlane, backend: Backend): ActiveBackend {
  const valid = validateBackend(backend);
  activeBackend = {
    plane,
    customerKey: valid.customerKey,
    displayName: valid.displayName,
    bffBaseUrl: valid.bffBaseUrl,
    bffScope: bffScopeFor(valid.bffAppId),
  };
  setStoredActiveKey(valid.customerKey);
  rememberCustomerKey(valid.customerKey);
  return activeBackend;
}

/** The picker's choice: the key must be one of the offered options (never free input). */
export function chooseBackend(plane: BackendPlane, options: readonly Backend[], key: string): ActiveBackend {
  const backend = options.find(b => b.customerKey === key);
  if (!backend) throw new BackendSelectionError(`[BackendSelection] '${key}' is not one of the offered backends.`);
  return activateBackend(plane, backend);
}

/** Forget the active backend (e.g. "choose a different sign-in"). The remembered keys stay. */
export function clearActiveBackend(): void {
  activeBackend = null;
  clearStoredActiveKey();
}

/** The active backend, or throws (fail closed): no request is made without one. */
export function getActiveBackend(): ActiveBackend {
  if (!activeBackend) {
    throw new BackendSelectionError('[BackendSelection] No backend is active; the request was not sent.');
  }
  return activeBackend;
}

/**
 * The BFF scope a plane's token acquirer requests. The active backend's scope when it belongs to this plane (a backend
 * resolved for the other plane throws). Before any selection — direct calls outside the bootstrap — the dev
 * configuration's single backend for the plane; otherwise throws (fail closed).
 */
export function activeBffScope(plane: BackendPlane): string {
  if (activeBackend) {
    if (activeBackend.plane !== plane) {
      throw new BackendSelectionError(
        `[BackendSelection] The active backend belongs to the ${activeBackend.plane} plane, not ${plane}; no token is requested.`
      );
    }
    return activeBackend.bffScope;
  }
  if (!isDirectoryServiceEnabled()) {
    const configured = devConfigBackends(plane);
    if (configured.length === 1) return bffScopeFor(configured[0].bffAppId);
  }
  throw new BackendSelectionError(`[BackendSelection] No ${plane} backend is active; no token is requested.`);
}

/**
 * The absolute URL for a BFF path on a backend. The path must be root-relative (`/…`); the result must stay on the
 * backend's origin, so a crafted path (`//host`, `@host`, `\\host`) can never move the request — and its Bearer
 * token — to another host.
 */
export function bffRequestUrl(backend: Pick<ActiveBackend, 'bffBaseUrl'>, path: string): string {
  if (!path.startsWith('/') || path.startsWith('//') || path.includes('\\')) {
    throw new BackendSelectionError(
      `[BackendSelection] BFF path '${path}' is not root-relative; the request was not sent.`
    );
  }
  const url = `${backend.bffBaseUrl}${path}`;
  if (new URL(url).origin !== backend.bffBaseUrl) {
    throw new BackendSelectionError(
      '[BackendSelection] The request URL left the backend origin; the request was not sent.'
    );
  }
  return url;
}
