/**
 * classificationService - the Classification area's calls to `/api/v1/ontology/admin/classification/categories` (task 103;
 * D-22, FR-59, D-49): the list, create, edit and On/Off.
 *
 * Every route runs as the caller in the BFF, so Dataverse enforces the Spaarke Ontology Administrator role on each write; this
 * module holds no role check and no token. The fetch is injected (ADR-012, ADR-028). A failure reaches here two ways and both are
 * handled: the injected fetch THROWS an `ApiError` (`@spaarke/auth`'s `authenticatedFetch`) or RETURNS a non-2xx `Response`.
 *
 * There is NO classifier confidence anywhere in these shapes: none exists (#5).
 *
 * Task: spaarke-ontology-platform-r1, task 103.
 */

import type { ResponseFetchFn } from '../../../utils/fetchTypes';
import { isApiError, problemOf } from '../../../utils/thrownFetchError';

/** The categories route (relative to the BFF base URL). */
export const ONTOLOGY_ADMIN_CLASSIFICATION_ROUTE = '/api/v1/ontology/admin/classification/categories';

/** How a stored recall measurement compares with the floor (D-49). */
export type ClassificationRecallStatus = 'NotMeasured' | 'MeetsFloor' | 'BelowFloor' | 'SetTooSmall';

/** Where a policy version stands. */
export type ClassificationVersionState = 'InForce' | 'Draft' | 'Earlier';

export interface ClassificationRecall {
  status: ClassificationRecallStatus;
  /** Percent, 0 to 100; null when not measured. */
  recall: number | null;
  labelledSetSize: number | null;
  /** `yyyy-MM-dd`. */
  measuredOn: string | null;
  /** True when the category was edited on a later day than the measurement: the figure may describe the category as it was before. */
  measuredBeforeLastEdit: boolean;
}

/** One policy version whose rule body names the category. */
export interface ClassificationRuleReference {
  policyId: string;
  policyCode: string;
  policyName: string | null;
  policyEnabled: boolean;
  versionId: string;
  versionNumber: number | null;
  versionState: ClassificationVersionState;
}

export interface ClassificationCategory {
  id: string;
  name: string;
  guidance: string | null;
  enabled: boolean;
  /** The row version; sent back with an edit so a concurrent change is detected. */
  version: number;
  /** Communications classified into the category in the last 30 days (those the caller can read); null when unknown. */
  volume30Days: number | null;
  recall: ClassificationRecall;
  readBy: ClassificationRuleReference[];
}

export interface ClassificationList {
  categories: ClassificationCategory[];
  recallFloor: { percent: number; minItems: number };
  volumeWindowDays: number;
  volumeAvailable: boolean;
  /** False: the policy versions could not be read, so an empty `readBy` does NOT mean no rule reads the category. */
  referencesAvailable: boolean;
}

/** A category row as a write returns it. */
export interface ClassificationRow {
  id: string;
  name: string;
  guidance: string | null;
  enabled: boolean;
  version: number;
}

export interface CreateCategoryRequest {
  name: string;
  guidance: string;
}

export interface UpdateCategoryRequest {
  name: string;
  guidance: string;
  /** The version the form loaded. */
  version: number;
}

/** A call that did not succeed. `reasonCode` and `blockedBy` come from the BFF's problem body. */
export class ClassificationRequestError extends Error {
  readonly httpStatus: number | null;
  readonly reasonCode: string | null;
  /** Field errors of a 400. */
  readonly fieldErrors: Readonly<Partial<Record<string, string[]>>>;
  /** The rules that read the category, for a refused rename. */
  readonly blockedBy: ClassificationRuleReference[];
  constructor(
    message: string,
    httpStatus: number | null,
    reasonCode: string | null = null,
    fieldErrors: Readonly<Partial<Record<string, string[]>>> = {},
    blockedBy: ClassificationRuleReference[] = []
  ) {
    super(message);
    this.name = 'ClassificationRequestError';
    this.httpStatus = httpStatus;
    this.reasonCode = reasonCode;
    this.fieldErrors = fieldErrors;
    this.blockedBy = blockedBy;
  }
}

export interface ClassificationService {
  list(): Promise<ClassificationList>;
  create(request: CreateCategoryRequest): Promise<ClassificationRow>;
  update(id: string, request: UpdateCategoryRequest): Promise<ClassificationRow>;
  setEnabled(id: string, enabled: boolean, version: number): Promise<ClassificationRow>;
}

export interface ClassificationServiceOptions {
  /** The injected fetch. An `AuthenticatedFetchFn` is assignable. */
  fetch: ResponseFetchFn;
  /** The BFF base URL, with or without a trailing slash. */
  baseUrl: string;
}

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const text = (v: unknown): string | null => (typeof v === 'string' && v !== '' ? v : null);
const num = (v: unknown): number | null => (typeof v === 'number' && Number.isFinite(v) ? v : null);

const RECALL_STATUSES: ReadonlySet<string> = new Set<ClassificationRecallStatus>([
  'NotMeasured',
  'MeetsFloor',
  'BelowFloor',
  'SetTooSmall',
]);
const VERSION_STATES: ReadonlySet<string> = new Set<ClassificationVersionState>(['InForce', 'Draft', 'Earlier']);

function parseReference(raw: unknown): ClassificationRuleReference | null {
  if (!isObject(raw)) return null;
  const policyId = text(raw.policyId);
  const versionId = text(raw.versionId);
  const policyCode = text(raw.policyCode);
  const state = text(raw.versionState);
  if (!policyId || !versionId || !policyCode || !state || !VERSION_STATES.has(state)) return null;
  return {
    policyId,
    policyCode,
    policyName: text(raw.policyName),
    policyEnabled: raw.policyEnabled === true,
    versionId,
    versionNumber: num(raw.versionNumber),
    versionState: state as ClassificationVersionState,
  };
}

function parseRecall(raw: unknown): ClassificationRecall {
  // A recall that cannot be read is "not measured", never a made-up figure.
  if (!isObject(raw)) return { status: 'NotMeasured', recall: null, labelledSetSize: null, measuredOn: null, measuredBeforeLastEdit: false };
  const status = text(raw.status);
  if (!status || !RECALL_STATUSES.has(status)) {
    return { status: 'NotMeasured', recall: null, labelledSetSize: null, measuredOn: null, measuredBeforeLastEdit: false };
  }
  return {
    status: status as ClassificationRecallStatus,
    recall: num(raw.recall),
    labelledSetSize: num(raw.labelledSetSize),
    measuredOn: text(raw.measuredOn),
    measuredBeforeLastEdit: raw.measuredBeforeLastEdit === true,
  };
}

function parseCategory(raw: unknown): ClassificationCategory | null {
  if (!isObject(raw)) return null;
  const id = text(raw.id);
  const name = text(raw.name);
  const version = num(raw.version);
  if (!id || !name || version === null) return null;
  return {
    id,
    name,
    guidance: text(raw.guidance),
    enabled: raw.enabled === true,
    version,
    volume30Days: num(raw.volume30Days),
    recall: parseRecall(raw.recall),
    readBy: Array.isArray(raw.readBy)
      ? raw.readBy.map(parseReference).filter((r): r is ClassificationRuleReference => r !== null)
      : [],
  };
}

/** Shape the list body. A body without a `categories` array is not an answer: it throws, so the area shows its error state. */
export function parseClassificationList(body: unknown, httpStatus: number | null = null): ClassificationList {
  if (!isObject(body) || !Array.isArray(body.categories)) {
    throw new ClassificationRequestError('The triage categories could not be read.', httpStatus);
  }
  const floor = isObject(body.recallFloor) ? body.recallFloor : {};
  return {
    categories: body.categories.map(parseCategory).filter((c): c is ClassificationCategory => c !== null),
    recallFloor: { percent: num(floor.percent) ?? 80, minItems: num(floor.minItems) ?? 50 },
    volumeWindowDays: num(body.volumeWindowDays) ?? 30,
    volumeAvailable: body.volumeAvailable === true,
    referencesAvailable: body.referencesAvailable === true,
  };
}

function parseRow(body: unknown, httpStatus: number | null): ClassificationRow {
  if (isObject(body)) {
    const id = text(body.id);
    const name = text(body.name);
    const version = num(body.version);
    if (id && name && version !== null) {
      return { id, name, guidance: text(body.guidance), enabled: body.enabled === true, version };
    }
  }
  throw new ClassificationRequestError('The saved category could not be read.', httpStatus);
}

function parseFieldErrors(raw: unknown): Record<string, string[]> {
  const result: Record<string, string[]> = {};
  if (!isObject(raw)) return result;
  for (const [field, messages] of Object.entries(raw)) {
    if (Array.isArray(messages)) {
      result[field] = messages.filter((m): m is string => typeof m === 'string');
    }
  }
  return result;
}

function errorFromProblem(problem: unknown, httpStatus: number, fallback: string): ClassificationRequestError {
  const body = isObject(problem) ? problem : {};
  const blockedBy = Array.isArray(body.blockedBy)
    ? body.blockedBy.map(parseReference).filter((r): r is ClassificationRuleReference => r !== null)
    : [];
  return new ClassificationRequestError(
    text(body.detail) ?? text(body.title) ?? fallback,
    httpStatus,
    text(body.reasonCode),
    parseFieldErrors(body.errors),
    blockedBy
  );
}

export function createClassificationService({ fetch, baseUrl }: ClassificationServiceOptions): ClassificationService {
  const root = `${baseUrl.replace(/\/+$/, '')}${ONTOLOGY_ADMIN_CLASSIFICATION_ROUTE}`;

  /** One call: the 2xx body, or a `ClassificationRequestError` (a thrown `ApiError` and a returned non-2xx are both handled). */
  async function call(address: string, method: string, body: unknown, fallback: string): Promise<unknown> {
    const init: RequestInit = {
      method,
      headers: body === undefined ? { Accept: 'application/json' } : { Accept: 'application/json', 'Content-Type': 'application/json' },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    };
    let response: Response;
    try {
      response = await fetch(address, init);
    } catch (err) {
      if (isApiError(err)) {
        throw errorFromProblem(problemOf(err), err.status, `${fallback} (${err.status}).`);
      }
      throw new ClassificationRequestError(`${fallback}.`, null);
    }
    if (!response.ok) {
      let problem: unknown;
      try {
        problem = await response.json();
      } catch {
        problem = undefined;
      }
      throw errorFromProblem(problem, response.status, `${fallback} (${response.status}).`);
    }
    try {
      return await response.json();
    } catch {
      throw new ClassificationRequestError(`${fallback}: the answer could not be read.`, response.status);
    }
  }

  return {
    async list() {
      const body = await call(root, 'GET', undefined, 'The triage categories could not be read');
      return parseClassificationList(body);
    },
    async create(request) {
      const body = await call(root, 'POST', { name: request.name, guidance: request.guidance }, 'The category could not be created');
      return parseRow(body, 201);
    },
    async update(id, request) {
      const body = await call(
        `${root}/${encodeURIComponent(id)}`,
        'PATCH',
        { name: request.name, guidance: request.guidance, version: request.version },
        'The category could not be saved'
      );
      return parseRow(body, 200);
    },
    async setEnabled(id, enabled, version) {
      const body = await call(
        `${root}/${encodeURIComponent(id)}/enabled`,
        'PUT',
        { enabled, version },
        'The category could not be changed'
      );
      return parseRow(body, 200);
    },
  };
}
