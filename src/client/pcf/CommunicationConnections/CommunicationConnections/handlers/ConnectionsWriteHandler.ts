/**
 * ConnectionsWriteHandler — the SOLE write path inside the CommunicationConnections PCF.
 *
 * Wraps `PolymorphicResolverService.applyResolverFields` from `@spaarke/ui-components`
 * exactly as the RegardingResolver PCF's `ResolverWriteHandler` does. The PCF NEVER
 * reimplements the ADR-024 / FR-13 mutual-exclusivity or field-write logic — this
 * module is a thin coordinator that discovers nav-props, looks up the catalog entry,
 * and delegates the SET path to `applyResolverFields` (§11 reuse; no new regarding
 * mechanism).
 *
 * FR-22: zero entity-specific code branches. The host entity is a parameter
 * (`hostEntity`, from the manifest `entity` input, default `sprk_communication`);
 * every Dataverse logical name comes from the shared `TODO_REGARDING_CATALOG`.
 *
 * Beyond the shared regarding write, this handler adds two host-record field
 * updates that are NOT regarding logic — plain column writes on the communication:
 *   - `advanceAssociationStatus` → set `sprk_associationstatus` to Resolved once
 *     all review slots are confirmed.
 *   - `persistOverrideReason` → write a reviewer's override reason back into the
 *     `sprk_associationprovenance` JSON as a feedback signal (NO learning loop).
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see src/client/pcf/RegardingResolver/RegardingResolver/handlers/ResolverWriteHandler.ts (parent pattern)
 */

import {
  applyResolverFields,
  TODO_REGARDING_CATALOG,
  type INavPropEntry,
  type IPolymorphicWebApi,
  type ITodoRegardingTargetCatalogEntry,
} from '@spaarke/ui-components';

// `sprk_associationstatus` = Resolved (task 002 verified integer).
const ASSOCIATION_STATUS_RESOLVED = 100000000;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface IRegardingSelection {
  /** Dataverse logical name of the selected parent entity (e.g., 'sprk_matter'). */
  entityType: string;
  /** GUID of the selected parent record (with or without braces / case). */
  recordId: string;
  /** Display name of the selected parent record (used for sprk_regardingrecordname). */
  recordName: string;
}

export interface IResolverWriteContext {
  /** WebApi instance (Xrm.WebApi-compatible — context.webAPI from PCF). */
  webApi: IPolymorphicWebApi & {
    updateRecord: (entityLogicalName: string, id: string, data: Record<string, unknown>) => Promise<unknown>;
  };
  /** Host entity logical name from the manifest `entity` input property. FR-22 lever. */
  hostEntity: string;
  /** Host record GUID. Empty / undefined when the form is creating a new record. */
  hostRecordId?: string;
}

export interface IResolverWriteResult {
  success: boolean;
  catalogEntry?: ITodoRegardingTargetCatalogEntry;
  payload?: Record<string, unknown>;
  recordNumber?: string | null;
  error?: string;
}

// ---------------------------------------------------------------------------
// Nav-prop discovery (per-host-entity, cached)
// ---------------------------------------------------------------------------

const _navPropCache: Record<string, INavPropEntry[]> = {};

/**
 * Discover ManyToOne navigation properties for the host entity. Mirrors the
 * RegardingResolver `discoverHostNavProps` — parameterized on host entity so the
 * PCF stays entity-agnostic (FR-22).
 */
export async function discoverHostNavProps(
  hostEntity: string,
  fetchImpl: typeof fetch = globalThis.fetch
): Promise<INavPropEntry[]> {
  if (_navPropCache[hostEntity]) {
    return _navPropCache[hostEntity];
  }

  try {
    const hostEntityLower = hostEntity.toLowerCase();
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${hostEntityLower}')/ManyToOneRelationships` +
      `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity`;

    const resp = await fetchImpl(url, { credentials: 'include' });
    if (!resp.ok) {
      console.warn(`[CommunicationConnections] Nav-prop discovery failed for ${hostEntity}: HTTP ${resp.status}`);
      return [];
    }

    const json = (await resp.json()) as {
      value?: {
        ReferencingAttribute: string;
        ReferencingEntityNavigationPropertyName: string;
        ReferencedEntity: string;
      }[];
    };

    const entries: INavPropEntry[] = (json.value ?? []).map(r => ({
      columnName: r.ReferencingAttribute,
      navPropName: r.ReferencingEntityNavigationPropertyName,
      referencedEntity: r.ReferencedEntity,
    }));

    if (entries.length === 0) {
      console.warn(
        `[CommunicationConnections] Nav-prop discovery returned zero entries for hostEntity="${hostEntity}" ` +
          `(normalized "${hostEntityLower}"). Verify the Host Entity input matches a real logical name — ` +
          `all resolver writes will silently fail otherwise.`
      );
    }

    _navPropCache[hostEntity] = entries;
    return entries;
  } catch (err) {
    console.warn(`[CommunicationConnections] Nav-prop discovery error for ${hostEntity}:`, err);
    return [];
  }
}

/** Reset the nav-prop cache. Test-only. @internal */
export function _resetNavPropCacheForTests(): void {
  for (const k of Object.keys(_navPropCache)) {
    delete _navPropCache[k];
  }
}

// ---------------------------------------------------------------------------
// Catalog lookup
// ---------------------------------------------------------------------------

/**
 * Parse an optional comma-separated allow-list of regarding target logical names.
 * Empty → full canonical catalog.
 */
export function resolveAllowedCatalog(
  regardingTargetsRaw: string | null | undefined
): readonly ITodoRegardingTargetCatalogEntry[] {
  if (!regardingTargetsRaw || !regardingTargetsRaw.trim()) {
    return TODO_REGARDING_CATALOG;
  }
  const allowed = new Set(
    regardingTargetsRaw
      .split(',')
      .map(s => s.trim().toLowerCase())
      .filter(Boolean)
  );
  return TODO_REGARDING_CATALOG.filter(c => allowed.has(c.entityType.toLowerCase()));
}

// ---------------------------------------------------------------------------
// Public: apply a selection (delegates the SET path to the shared service)
// ---------------------------------------------------------------------------

/**
 * File a regarding-target selection ADDITIVELY on the host communication record.
 *
 * ⚠️ DELIBERATE DEVIATION FROM THE RegardingResolver PATTERN (and from task 042's
 * POML wording, which said "write via applyResolverFields ... set status Resolved
 * when all review slots are confirmed"). RegardingResolver is a SINGLE-parent
 * picker (`sprk_todo` is regarding exactly one record) so it CLEAR-AND-SETs — it
 * nulls every other typed regarding lookup before setting the chosen one (FR-13
 * mutual exclusivity). `sprk_communication` is the OPPOSITE: an email is regarding
 * MANY records at once (Matter + Organization + Contact + Invoice …), and the
 * task-015 engine proves it — `IncomingAssociationResolver.PopulateResolverFieldsAsync`
 * writes MULTIPLE `sprk_regarding*` typed lookups in one `UpdateAsync`
 * (`RegardingFieldMap.All`) and picks ONE *primary* only for the denormalized
 * display fields. Clear-and-set here would silently discard every association but
 * the last one confirmed — the exact failure code-review flagged (C1).
 *
 * So this method mirrors the ENGINE, not RegardingResolver:
 *   1. Resolve the catalog entry (reject unknown types).
 *   2. Discover the host nav-props.
 *   3. Delegate the chosen lookup + the denormalized resolver fields to the
 *      SHARED `applyResolverFields` — with NO pre-clear of sibling lookups, so
 *      previously-filed associations survive (additive multi-lookup write).
 *   4. Persist via `webApi.updateRecord` when a host GUID exists.
 *
 * This is ADR-024-COMPLIANT (the engine's own multi-lookup write is ADR-024) and
 * reuses the shared service — it is NOT a new regarding mechanism (§11). The
 * denormalized primary (`sprk_regardingrecordid/name/url/recordtype`) follows the
 * most-recently-filed selection; callers that file several should file the
 * highest-priority one LAST so it owns the primary (see App handleAcceptAll).
 */
export async function applyRegardingSelection(
  ctx: IResolverWriteContext,
  selection: IRegardingSelection,
  catalog: readonly ITodoRegardingTargetCatalogEntry[] = TODO_REGARDING_CATALOG,
  fetchImpl: typeof fetch = globalThis.fetch
): Promise<IResolverWriteResult> {
  const catalogEntry = catalog.find(c => c.entityType === selection.entityType);
  if (!catalogEntry) {
    return {
      success: false,
      error:
        `[CommunicationConnections] Unknown entity type "${selection.entityType}". ` +
        `Allowed: ${catalog.map(c => c.entityType).join(', ')}.`,
    };
  }

  const navProps = await discoverHostNavProps(ctx.hostEntity, fetchImpl);

  // ADDITIVE: start with an EMPTY payload — do NOT null sibling typed lookups.
  // `applyResolverFields` adds only the chosen `@odata.bind` + the 4 denormalized
  // resolver fields, so any regarding lookup already filed on this communication
  // stays filed (multi-association model — see the docstring above).
  const payload: Record<string, unknown> = {};

  // Delegate the SET path to the shared service — THE SOLE regarding write logic.
  const applyResult = await applyResolverFields(
    ctx.webApi,
    payload,
    navProps,
    catalogEntry.entityType,
    catalogEntry.entitySet,
    selection.recordId,
    selection.recordName,
    catalogEntry.navPropHint
  );

  const hasHostGuid = Boolean(ctx.hostRecordId && ctx.hostRecordId.replace(/[{}]/g, '').length === 36);
  if (hasHostGuid) {
    try {
      await ctx.webApi.updateRecord(ctx.hostEntity, (ctx.hostRecordId as string).replace(/[{}]/g, ''), payload);
    } catch (err) {
      return {
        success: false,
        catalogEntry,
        payload,
        recordNumber: applyResult.recordNumber,
        error: err instanceof Error ? err.message : 'updateRecord failed',
      };
    }
  }

  return { success: true, catalogEntry, payload, recordNumber: applyResult.recordNumber };
}

// ---------------------------------------------------------------------------
// Public: advance association status (plain host-field write — NOT regarding logic)
// ---------------------------------------------------------------------------

/**
 * Advance `sprk_associationstatus` to Resolved on the host communication record.
 * Called once all review slots are confirmed. No-op (returns success) when there
 * is no host GUID (CREATE mode — not a review scenario).
 */
export async function advanceAssociationStatus(ctx: IResolverWriteContext): Promise<IResolverWriteResult> {
  const cleanId = ctx.hostRecordId?.replace(/[{}]/g, '');
  if (!cleanId || cleanId.length !== 36) {
    return { success: true }; // Nothing to advance without a persisted record.
  }
  try {
    await ctx.webApi.updateRecord(ctx.hostEntity, cleanId, {
      sprk_associationstatus: ASSOCIATION_STATUS_RESOLVED,
    });
    return { success: true };
  } catch (err) {
    return { success: false, error: err instanceof Error ? err.message : 'status update failed' };
  }
}

// ---------------------------------------------------------------------------
// Public: persist an override reason as a feedback signal (NO learning loop)
// ---------------------------------------------------------------------------

/**
 * Persist a reviewer's override reason back into the `sprk_associationprovenance`
 * JSON as a feedback signal. This mutates only the decision block's override
 * fields and rewrites the column — nothing downstream consumes it (no learning
 * loop; out of R4 scope per task 042 constraint).
 *
 * `nowIso` is injected so callers (and tests) control the timestamp; the App
 * passes `new Date().toISOString()`.
 */
export async function persistOverrideReason(
  ctx: IResolverWriteContext,
  provenanceJson: string,
  overriddenField: string,
  reason: string,
  nowIso: string
): Promise<IResolverWriteResult> {
  const cleanId = ctx.hostRecordId?.replace(/[{}]/g, '');
  if (!cleanId || cleanId.length !== 36) {
    return { success: true };
  }

  let doc: Record<string, unknown>;
  try {
    doc = JSON.parse(provenanceJson) as Record<string, unknown>;
  } catch {
    return { success: false, error: 'provenance JSON was unparseable — override reason not persisted' };
  }

  const decision = (doc.decision as Record<string, unknown> | undefined) ?? {};
  decision.overrideReason = reason;
  decision.overriddenField = overriddenField;
  decision.overriddenAt = nowIso;
  doc.decision = decision;

  try {
    await ctx.webApi.updateRecord(ctx.hostEntity, cleanId, {
      sprk_associationprovenance: JSON.stringify(doc),
    });
    return { success: true };
  } catch (err) {
    return { success: false, error: err instanceof Error ? err.message : 'override-reason update failed' };
  }
}
