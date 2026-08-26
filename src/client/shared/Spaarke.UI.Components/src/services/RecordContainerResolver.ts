/**
 * RecordContainerResolver — the CLIENT half of the record-aware SPE container decision.
 *
 * unified-access-control-r2 task 075. Task 076 routes the ~8 client call sites onto this module;
 * none of them may make the decision themselves.
 *
 * ## Why this exists
 *
 * Provisioning gives every secure project its own SharePoint Embedded container and writes the id to
 * the project row. Until this landed, **nothing read it**: uploads resolved the container from the
 * acting user's business unit, so a secure project's documents went into a shared container.
 *
 * SharePoint Embedded permissions are **additive-only** — *"You can't break inheritance on arbitrary
 * files or folders"* — so a document written into a shared container is readable by every member of
 * that container and **no later per-item permission can retract it**. There is no per-item
 * alternative and no after-the-fact repair. That is why a secure record with no container of its own
 * is a hard failure here rather than a fall back to the business-unit cascade: falling back would
 * succeed, silently, and be irreversible.
 *
 * ## The one rule
 *
 * ```
 * secure record      -> its OWN sprk_containerid, or THROW if it has none
 * everything else    -> the caller's existing business-unit cascade (INV-7: still resolved client-side)
 * ```
 *
 * ## Drift with the server half — read this before changing anything
 *
 * The same decision exists in C# at
 * `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/SecureContainerDecision.cs`, because INV-7
 * keeps business-unit resolution client-side while server-side email ingest has no client at all.
 * Two implementations of an isolation rule is a known failure mode, so both are pinned to ONE
 * machine-readable decision table:
 *
 *   `tests/fixtures/secure-container-decision-table.json`
 *
 * `decideContainer` below is driven by that fixture in `__tests__/RecordContainerResolver.test.ts`,
 * and `SecureContainerDecision.Decide` is driven by the same file from
 * `tests/integration/auth/UnifiedAccessControl/SecureContainerDecisionTableTests.cs`. Change one
 * half's behaviour and its test fails; change the fixture to suit one half and the other half's test
 * fails. See `projects/unified-access-control-r2/notes/task-075-record-aware-container-resolver.md`
 * §4 for why two halves exist and what residual risk that leaves.
 *
 * **Do not add an `issecure` test at a call site.** Call sites ask this module; they do not decide.
 *
 * @see ADR-012 — shared component library, no PCF-specific dependencies
 * @see docs/standards/DATA-ACCESS-DECISION-CRITERIA.md — why securability is read via host context
 */

import type { IWebApiLike } from '../types/WebApiLike';

/** The Dataverse column whose presence makes an entity securable. */
export const SECURE_FLAG_ATTRIBUTE = 'sprk_issecure';

/** The stamped container column, on both securable records and the business unit. */
export const CONTAINER_ATTRIBUTE = 'sprk_containerid';

/**
 * Where a resolved container came from. Mirrors the server's `ContainerDecisionOutcome`.
 */
export type ContainerSource =
  /** The secure record's own container. */
  | 'secure-record-own-container'
  /** The caller's non-secure default — the business-unit cascade. */
  | 'non-secure-fallback'
  /**
   * No container available AND the record is not secure. Callers keep their existing behaviour.
   * **Unreachable for a secure record** — that path throws instead.
   */
  | 'unresolved';

export interface ContainerResolution {
  /** `undefined` only when {@link source} is `'unresolved'`. */
  containerId?: string;
  source: ContainerSource;
}

/**
 * Thrown when a SECURE record has no container of its own.
 *
 * A distinct error type rather than a generic `Error` so callers can surface the operator-actionable
 * message ("provision the record's container") instead of a generic upload failure — and so a
 * `catch` that swallows unrelated failures cannot silently swallow this one too.
 */
export class SecureContainerUnresolvedError extends Error {
  public readonly code = 'secure_record_container_missing';

  constructor(
    public readonly entityLogicalName: string,
    public readonly recordId: string,
    /** Whether a non-secure fallback WAS available and was deliberately not used. */
    public readonly fallbackWasAvailable: boolean
  ) {
    super(
      `${entityLogicalName} '${recordId}' is marked secure but has no SharePoint Embedded container ` +
        `of its own. Its content cannot be stored in a shared container, so this operation was ` +
        `refused${
          fallbackWasAvailable
            ? ' — a shared fallback container was available and was deliberately NOT used'
            : ''
        }. Provision the record's own container before uploading to it.`
    );
    this.name = 'SecureContainerUnresolvedError';
  }
}

/**
 * Blank is blank: null, undefined, empty and whitespace all mean "not set".
 *
 * Dataverse returns an empty string as readily as null for an unset `NVARCHAR`, so a truthiness
 * check alone would treat `'   '` as set and resolve to a whitespace container id — surfacing as a
 * confusing Graph error rather than a refusal. Trimming is shared with the C# half so the same
 * record cannot resolve to two different container ids depending on which half asked.
 */
function normalize(value: string | null | undefined): string | undefined {
  if (typeof value !== 'string') return undefined;
  const trimmed = value.trim();
  return trimmed === '' ? undefined : trimmed;
}

/**
 * THE DECISION. Pure: no I/O, no globals, no clock.
 *
 * Mirrors `SecureContainerDecision.Decide` in C#. Pinned by
 * `tests/fixtures/secure-container-decision-table.json`.
 *
 * @returns `{ outcome: 'fail-closed' }` rather than throwing — the throw belongs to
 *          {@link resolveContainerForRecord}, which knows the record identity to report. Keeping
 *          this function total is what lets the fixture drive it exhaustively.
 */
export function decideContainer(input: {
  isSecure: boolean;
  ownContainerId?: string | null;
  fallbackContainerId?: string | null;
}):
  | { outcome: 'resolved-secure'; containerId: string }
  | { outcome: 'resolved-fallback'; containerId: string }
  | { outcome: 'unresolved' }
  | { outcome: 'fail-closed' } {
  if (input.isSecure) {
    const own = normalize(input.ownContainerId);

    // FAIL CLOSED. Note what is deliberately NOT reachable from this branch: the fallback. It may
    // well be non-empty and usable. Using it is the defect.
    return own === undefined
      ? { outcome: 'fail-closed' }
      : { outcome: 'resolved-secure', containerId: own };
  }

  // A non-secure record's OWN stamped container is deliberately never consulted — only the
  // fallback. Reading it would silently redirect content for any record carrying a stale stamp, and
  // stale stamps demonstrably exist because the creation wizard's BU cascade writes that column
  // today (which task 076 removes). Non-secure behaviour is unchanged by this task, on purpose.
  const fallback = normalize(input.fallbackContainerId);

  return fallback === undefined
    ? { outcome: 'unresolved' }
    : { outcome: 'resolved-fallback', containerId: fallback };
}

/**
 * Minimal metadata probe used to decide whether an entity CAN be secure.
 *
 * Structurally satisfied by `IDataverseClient` (`XrmDataverseClient` for MDA hosts,
 * `BffDataverseClient` for Code Pages / widgets / Office add-ins), so callers pass the client they
 * already have rather than a new dependency.
 */
export interface IEntityMetadataProbe {
  retrieveEntityMetadata(entityName: string): Promise<{
    attributes: Record<string, unknown>;
  }>;
}

/**
 * Per-session memo of entity → securable. Metadata does not change within a session, and this keeps
 * the probe off the hot path for repeat uploads.
 *
 * Only POSITIVE and NEGATIVE answers are memoised — never failures, so a transient metadata outage
 * cannot be cached as "not securable".
 */
const securableMemo = new Map<string, boolean>();

/** Test seam: clear the memo between cases. */
export function __resetSecurableEntityCache(): void {
  securableMemo.clear();
}

/**
 * Whether `entityLogicalName` can carry `sprk_issecure`, from LIVE METADATA rather than a
 * hard-coded list.
 *
 * The list happens to be `sprk_project` / `sprk_matter` / `sprk_workassignment` today. Hard-coding
 * it would be wrong the moment a fourth entity gains the field, and it would be wrong in the worst
 * direction: the new securable entity would resolve through the shared fallback and its content
 * would land in a shared container irreversibly.
 *
 * @throws Propagates metadata failures. "I could not find out whether this entity is securable" must
 *         never be read as "it is not securable" — that is the same isolation failure with an extra
 *         step.
 */
export async function isSecurableEntity(
  probe: IEntityMetadataProbe,
  entityLogicalName: string
): Promise<boolean> {
  const key = entityLogicalName.trim().toLowerCase();
  if (key === '') return false;

  const memoised = securableMemo.get(key);
  if (memoised !== undefined) return memoised;

  // Deliberately NOT wrapped in try/catch. A failure here propagates.
  const metadata = await probe.retrieveEntityMetadata(key);

  const securable =
    !!metadata?.attributes &&
    Object.prototype.hasOwnProperty.call(metadata.attributes, SECURE_FLAG_ATTRIBUTE);

  securableMemo.set(key, securable);
  return securable;
}

export interface ResolveContainerArgs {
  /** Reads the record. Host-context `Xrm.WebApi` on an MDA, a BFF-backed shim elsewhere. */
  webApi: IWebApiLike;
  /** Live-metadata probe — pass the `IDataverseClient` you already have. */
  metadataProbe: IEntityMetadataProbe;
  /** The record whose content is being stored, e.g. `sprk_project`. */
  entityLogicalName: string;
  /** The record id. */
  recordId: string;
  /**
   * The business-unit-resolved container — the existing behaviour, used ONLY when the record is not
   * secure. INV-7: this stays resolved client-side; this module does not resolve it for you.
   */
  fallbackContainerId?: string | null;
  /**
   * OData entity SET name (e.g. `sprk_projects`) if the host's `retrieveRecord` needs it. Most
   * `IWebApiLike` implementations take the logical name; pass this only when yours does not.
   */
  entitySetName?: string;
}

/**
 * Decide which SPE container this record's content belongs in.
 *
 * @throws {SecureContainerUnresolvedError} when the record IS secure and has no container of its
 *         own. **Do not catch this and fall back** — the refusal is the feature.
 * @throws Propagates any metadata or record-read failure, because an undetermined securability
 *         answer must not resolve to a shared container.
 */
export async function resolveContainerForRecord(
  args: ResolveContainerArgs
): Promise<ContainerResolution> {
  const { webApi, metadataProbe, entityLogicalName, recordId, fallbackContainerId } = args;

  const entity = (entityLogicalName ?? '').trim().toLowerCase();
  if (entity === '') {
    throw new Error('resolveContainerForRecord: entityLogicalName is required.');
  }

  // An entity that cannot carry sprk_issecure cannot be secure, so there is nothing to read. This
  // also means non-securable entities cost ZERO extra Dataverse round trips beyond the memoised
  // metadata probe, which is what keeps this cheap enough to sit on every upload path.
  if (!(await isSecurableEntity(metadataProbe, entity))) {
    return toResolution(decideContainer({ isSecure: false, fallbackContainerId }), entity, recordId, fallbackContainerId);
  }

  const id = (recordId ?? '').replace(/^\{|\}$/g, '').trim();
  if (id === '') {
    // A securable entity with no usable id is an indeterminate-securability case: refuse rather
    // than fall through to the shared fallback.
    throw new SecureContainerUnresolvedError(entity, recordId ?? '(none)', !!normalize(fallbackContainerId));
  }

  // Failures propagate — see the throws contract above.
  const record = await webApi.retrieveRecord(
    args.entitySetName ?? entity,
    id,
    `?$select=${SECURE_FLAG_ATTRIBUTE},${CONTAINER_ATTRIBUTE}`
  );

  // FAIL CLOSED ON AN EMPTY READ — this half must not be softer than the C# half.
  //
  // `IWebApiLike.retrieveRecord` is TYPED `Promise<Record<string, unknown>>` (non-nullable) and satisfied
  // STRUCTURALLY, so TypeScript will not warn at any call site if an implementation resolves `null`,
  // `undefined` or `{}` — and the shipped adapters are not the only implementations (PCF `context.webAPI`,
  // host shims, mocks). Without this guard `record?.[flag] === true` is `false` for all three, which routes
  // the content to the BU fallback container. The C# resolver throws `container_record_not_found` on
  // exactly this condition; an asymmetry here would be a fail-OPEN client with a fail-CLOSED server.
  if (record === null || record === undefined) {
    throw new SecureContainerUnresolvedError(entity, id, !!normalize(fallbackContainerId));
  }

  const isSecure = record[SECURE_FLAG_ATTRIBUTE] === true;
  const ownContainerId = record[CONTAINER_ATTRIBUTE];

  return toResolution(
    decideContainer({
      isSecure,
      ownContainerId: typeof ownContainerId === 'string' ? ownContainerId : undefined,
      fallbackContainerId,
    }),
    entity,
    id,
    fallbackContainerId
  );
}

function toResolution(
  decision: ReturnType<typeof decideContainer>,
  entityLogicalName: string,
  recordId: string,
  fallbackContainerId: string | null | undefined
): ContainerResolution {
  switch (decision.outcome) {
    case 'resolved-secure':
      return { containerId: decision.containerId, source: 'secure-record-own-container' };
    case 'resolved-fallback':
      return { containerId: decision.containerId, source: 'non-secure-fallback' };
    case 'unresolved':
      return { source: 'unresolved' };
    case 'fail-closed':
      throw new SecureContainerUnresolvedError(
        entityLogicalName,
        recordId,
        !!normalize(fallbackContainerId)
      );
  }
}
