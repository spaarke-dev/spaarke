/**
 * FieldMappingService — context-agnostic engine for the Field Mapping Framework.
 *
 * Given a source record and a target-record create payload, this engine fetches
 * the field-mapping profile configured for the {source → target} entity pair and
 * applies each rule onto the payload. It is the client counterpart of the BFF
 * push path and the engine every Create*Wizard service wires into (tasks 012+).
 *
 * Design contract (this task, 010 — the shell):
 *   - **Context-agnostic (ADR-012)**: NO `ComponentFramework` / PCF types. All
 *     I/O is injected — an `IDataService` (Dataverse read/write) and an
 *     `AuthenticatedFetchFn` (BFF calls). Mirrors the injected shape of
 *     `EntityCreationService`.
 *   - **Exactly ONE BFF call per invocation**: `GET /api/v1/field-mappings/
 *     profiles/{source}/{target}`. A 404 / no-profile is a graceful no-op
 *     (`{ profileFound: false, fieldsMapped: [], warnings: [] }`), mirroring
 *     `applyResolverFields`' NFR-06 graceful-blank handling.
 *   - **Never throws**: every failure path appends a warning and continues.
 *   - **No `source === target` guard** anywhere — same-entity mapping is
 *     supported (task 014; a negative test asserts the guard's absence).
 *
 * Per-rule apply logic:
 *   - task 012 — Copy (incl. lookup `@odata.bind`) — IMPLEMENTED
 *   - task 013 — Default (literal), Concat/Template (shared `resolveExpression`
 *     placeholder resolver) — IMPLEMENTED (this task)
 *
 * @see types/FieldMappingTypes.ts — the profile/rule/result contract
 * @see services/PolymorphicResolverService.ts — the never-throw pattern mirrored here
 * @see services/EntityCreationService.ts — the injected `IDataService`/fetch shape
 * @see components/CreateInvoiceWizard/invoiceService.ts — the lookup `@odata.bind`
 *   pattern mirrored by the Copy-lookup seam (findNavProp + `_cleanGuid` + bind string)
 * @see spec.md FR-01, FR-02, FR-09 · design.md §4.1, §4.1a
 */

import type { IDataService } from '../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from './EntityCreationService';
import { FieldMappingTypes, type IFieldMappingProfile, type IFieldMappingRule, type IMappingResult } from '../types/FieldMappingTypes';
import { discoverNavProps, findNavProp } from './PolymorphicResolverService';

/**
 * Inputs to {@link applyFieldMappings}. Deliberately matches the
 * `WizardHostProps`-injected surface (`dataService`, `authenticatedFetch`,
 * `bffBaseUrl`) so a Create*Wizard service can call the engine directly with the
 * dependencies it already holds.
 */
export interface IApplyFieldMappingsArgs {
  /** Source entity logical name (e.g. "sprk_matter"). */
  sourceEntity: string;
  /** GUID of the source record to read mapped values from. */
  sourceId: string;
  /** Target entity logical name (e.g. "sprk_event"). May equal `sourceEntity`. */
  targetEntity: string;
  /**
   * The target-record create payload. The apply engines (tasks 012/013) mutate
   * this in place — writing resolved values and `@odata.bind` lookups — exactly
   * as `applyResolverFields` mutates its `entity` argument.
   */
  payload: Record<string, unknown>;
  /** Injected Dataverse data service (used by the apply engines to read source values). */
  dataService: IDataService;
  /** Injected BFF-authenticated fetch (the single profile fetch runs through this). */
  authenticatedFetch: AuthenticatedFetchFn;
  /** BFF API base URL (e.g. "https://api.example.com"). */
  bffBaseUrl: string;
}

/**
 * Shared context handed to each per-rule seam. Bundles the invocation inputs
 * plus the mutable result accumulators so an apply engine (012/013) has
 * everything it needs to read source values, write the payload, and record
 * outcomes.
 */
interface IRuleApplyContext {
  sourceEntity: string;
  sourceId: string;
  targetEntity: string;
  payload: Record<string, unknown>;
  dataService: IDataService;
  /** Target field names successfully written (appended by each seam). */
  fieldsMapped: string[];
  /** Non-fatal diagnostics (appended by each seam). */
  warnings: string[];
  /**
   * The source record's fields needed by every Copy rule AND every
   * Concat/Template rule's `{sprk_field}` placeholders in this invocation,
   * fetched ONCE (task 012, extended task 013) via a single `$select`
   * spanning all of them — never one `retrieveRecord` call per rule. `null`
   * when nothing needed a source read, OR the single batch fetch failed (in
   * which case every Copy/Concat/Template rule gracefully skips; see
   * {@link fetchSourceRecordForRules}).
   */
  sourceRecord: Record<string, unknown> | null;
}

/**
 * Apply the configured field-mapping profile for a {source → target} entity pair
 * onto a target-record create payload.
 *
 * Makes exactly one BFF call (the profile fetch). Never throws — a missing
 * profile, a failed fetch, or any per-rule failure is returned as data.
 *
 * @param args See {@link IApplyFieldMappingsArgs}.
 * @returns {@link IMappingResult} — `{ profileFound, fieldsMapped, warnings }`.
 */
export async function applyFieldMappings(args: IApplyFieldMappingsArgs): Promise<IMappingResult> {
  const { sourceEntity, sourceId, targetEntity, payload, dataService, authenticatedFetch, bffBaseUrl } = args;

  const fieldsMapped: string[] = [];
  const warnings: string[] = [];

  // ── Single BFF call: fetch the profile (with $expand-ed rules) ────────────
  // NOTE: intentionally NO `sourceEntity === targetEntity` guard here or
  // anywhere below — same-entity mapping is a supported scenario (task 014).
  const profile = await fetchProfile(sourceEntity, targetEntity, authenticatedFetch, bffBaseUrl, warnings);

  if (!profile) {
    // 404 / no-profile → graceful no-op (mirror applyResolverFields NFR-06).
    return { profileFound: false, fieldsMapped, warnings };
  }

  const rules = profile.rules ?? [];
  if (rules.length === 0) {
    warnings.push(`Field-mapping profile "${profile.name}" has no rules; nothing to apply.`);
    return { profileFound: true, fieldsMapped, warnings };
  }

  // ── Source-value rules: fetch the source record's fields ONCE, in a single
  // $select spanning every Copy rule (scalar + lookup) AND every Concat/
  // Template rule's `{sprk_field}` placeholders — never one fetch per rule
  // (task 012 constraint, extended task 013). Default rules need no source
  // read at all (they write a static literal).
  const copyRules = rules.filter(r => r.mappingType === FieldMappingTypes.Copy);
  const concatTemplateRules = rules.filter(
    r => r.mappingType === FieldMappingTypes.Concat || r.mappingType === FieldMappingTypes.Template
  );
  const placeholderFields = new Set<string>();
  for (const rule of concatTemplateRules) {
    for (const field of extractPlaceholderFields(rule.expression)) {
      placeholderFields.add(field);
    }
  }
  const sourceRecord =
    copyRules.length > 0 || placeholderFields.size > 0
      ? await fetchSourceRecordForRules(dataService, sourceEntity, sourceId, copyRules, placeholderFields, warnings)
      : null;

  const context: IRuleApplyContext = {
    sourceEntity,
    sourceId,
    targetEntity,
    payload,
    dataService,
    fieldsMapped,
    warnings,
    sourceRecord,
  };

  // Apply rules in Priority order (lower first), matching the BFF push path.
  const ordered = [...rules].sort((a, b) => (a.priority ?? 0) - (b.priority ?? 0));
  for (const rule of ordered) {
    // Each apply is independently guarded — one bad rule never aborts the rest
    // and never throws out of the engine.
    try {
      await applyRule(rule, context);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      warnings.push(`Rule "${rule.sourceField}"→"${rule.targetField}" threw and was skipped: ${message}`);
    }
  }

  return { profileFound: true, fieldsMapped, warnings };
}

/**
 * Fetch the profile for the entity pair via the single BFF endpoint.
 *
 * Returns the parsed profile on 200, or `null` on 404 / no-profile / any
 * non-OK response / network error. Never throws. A 404 is the expected
 * "no profile configured" signal and records NO warning (graceful no-op);
 * other failures record a warning so the caller can surface a diagnostic.
 */
async function fetchProfile(
  sourceEntity: string,
  targetEntity: string,
  authenticatedFetch: AuthenticatedFetchFn,
  bffBaseUrl: string,
  warnings: string[]
): Promise<IFieldMappingProfile | null> {
  const base = (bffBaseUrl ?? '').replace(/\/+$/, '');
  const url =
    `${base}/api/v1/field-mappings/profiles/` +
    `${encodeURIComponent(sourceEntity)}/${encodeURIComponent(targetEntity)}`;

  try {
    const response = await authenticatedFetch(url, { method: 'GET' });

    if (response.status === 404) {
      // No profile configured for this pair — graceful no-op, no warning.
      return null;
    }

    if (!response.ok) {
      const detail = await response.text().catch(() => response.statusText);
      warnings.push(`Field-mapping profile fetch failed (HTTP ${response.status}): ${detail}`);
      return null;
    }

    const dto = (await response.json()) as unknown;
    return normalizeProfile(dto);
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    warnings.push(`Field-mapping profile fetch threw and was treated as no-profile: ${message}`);
    return null;
  }
}

/**
 * Defensively normalize the raw JSON body into an {@link IFieldMappingProfile}.
 *
 * The BFF already emits camelCase keys matching the interface, so this is mostly
 * a typed pass-through with sane fallbacks — it exists so a malformed body
 * degrades to an empty-rules profile (warning-free no-op downstream) rather than
 * throwing during property access.
 */
function normalizeProfile(dto: unknown): IFieldMappingProfile | null {
  if (dto === null || typeof dto !== 'object') {
    return null;
  }
  const raw = dto as Record<string, unknown>;
  const rawRules = Array.isArray(raw.rules) ? (raw.rules as unknown[]) : [];

  return {
    id: String(raw.id ?? ''),
    name: String(raw.name ?? ''),
    sourceEntity: String(raw.sourceEntity ?? ''),
    targetEntity: String(raw.targetEntity ?? ''),
    syncMode: String(raw.syncMode ?? ''),
    isActive: Boolean(raw.isActive),
    rules: rawRules.map(normalizeRule),
  };
}

/** Defensively normalize a single raw rule object into an {@link IFieldMappingRule}. */
function normalizeRule(raw: unknown): IFieldMappingRule {
  const r = (raw ?? {}) as Record<string, unknown>;
  return {
    id: String(r.id ?? ''),
    sourceField: String(r.sourceField ?? ''),
    targetField: String(r.targetField ?? ''),
    sourceFieldType: String(r.sourceFieldType ?? 'Text'),
    targetFieldType: String(r.targetFieldType ?? 'Text'),
    priority: typeof r.priority === 'number' ? r.priority : 0,
    mappingType: String(r.mappingType ?? FieldMappingTypes.Copy),
    defaultValue: typeof r.defaultValue === 'string' ? r.defaultValue : (r.defaultValue == null ? null : String(r.defaultValue)),
    expression: typeof r.expression === 'string' ? r.expression : (r.expression == null ? null : String(r.expression)),
    isRequired: Boolean(r.isRequired),
    compatibilityMode: String(r.compatibilityMode ?? 'Strict'),
  };
}

// ============================================================================
// PER-RULE DISPATCH SEAMS
// ----------------------------------------------------------------------------
// Each `case` below is a SEAM to be filled in by a later task. The dispatch
// contract (switch on `rule.mappingType`, one branch per type, never throw,
// append to `ctx.fieldsMapped` / `ctx.warnings`) is FIXED — later tasks fill in
// the branch body, they do NOT change the surrounding structure.
//
//   SEAM[012:Copy]     → verbatim source→target copy, incl. lookup @odata.bind — IMPLEMENTED
//   SEAM[013:Default]  → literal from rule.defaultValue when source is empty — IMPLEMENTED
//   SEAM[013:Concat]   → format string from rule.expression — IMPLEMENTED
//   SEAM[013:Template] → format string from rule.expression — IMPLEMENTED
//
// Every seam is a fully graceful, never-throw no-op on failure (warn + skip).
// ============================================================================

/**
 * Dispatch a single rule to its per-type apply seam based on `mappingType`.
 * Shell behavior: every branch is a documented SEAM that warns-and-skips.
 */
async function applyRule(rule: IFieldMappingRule, ctx: IRuleApplyContext): Promise<void> {
  switch (rule.mappingType) {
    case FieldMappingTypes.Copy:
      await applyCopy(rule, ctx);
      return;

    case FieldMappingTypes.Default:
      // SEAM[013:Default] — write rule.defaultValue as a literal.
      applyDefault(rule, ctx);
      return;

    case FieldMappingTypes.Concat:
      // SEAM[013:Concat] — resolve rule.expression against the source record.
      applyExpressionRule(rule, ctx, 'Concat');
      return;

    case FieldMappingTypes.Template:
      // SEAM[013:Template] — resolve rule.expression against the source record.
      applyExpressionRule(rule, ctx, 'Template');
      return;

    default:
      // Unknown/unsupported mappingType (e.g. a newer server value). Graceful
      // skip so the engine never throws on an unrecognized discriminator.
      ctx.warnings.push(
        `Unknown mappingType "${rule.mappingType}" for rule "${rule.sourceField}"→"${rule.targetField}"; skipped.`
      );
      return;
  }
}

// ============================================================================
// SEAM[012:Copy] — verbatim source→target copy, incl. lookup @odata.bind
// ----------------------------------------------------------------------------
// Scalar targets: assign the source value to ctx.payload[targetField].
// Lookup targets (rule.targetFieldType === "Lookup"): resolve the referent
// entity from the source lookup's `_<field>_value` + the
// `@Microsoft.Dynamics.CRM.lookuplogicalname` annotation, pluralize to its
// entity set, discover the TARGET entity's nav-prop via the shared
// `discoverNavProps` (task 011), and write `${navProp}@odata.bind`.
//
// Mirrors the exact lookup-bind pattern already used by CreateInvoiceWizard/
// invoiceService.ts (`findNavProp` + `_cleanGuid` + `/${entitySet}(${guid})`).
// ============================================================================

/**
 * Fetch the source record's fields needed by ALL Copy rules AND all
 * Concat/Template `{sprk_field}` placeholders in ONE `IDataService
 * .retrieveRecord` call (task 012 constraint: fetch once, never once per
 * rule — extended task 013 to also cover placeholder fields so there is
 * still exactly one source fetch per invocation). Scalar Copy rules `$select`
 * the plain field name; lookup Copy rules (`rule.targetFieldType ===
 * "Lookup"`) `$select` the OData `_<field>_value` form so the response
 * carries the `@Microsoft.Dynamics.CRM.lookuplogicalname` annotation the
 * lookup-bind path needs. This is the standard Xrm.WebApi annotation
 * convention — the confirmed production wiring for every Create*Wizard is
 * `createXrmDataService()` (see `VisualHostRoot.tsx`), whose `retrieveRecord`
 * delegates straight to `Xrm.WebApi.retrieveRecord`, which includes these
 * annotations automatically. Placeholder fields are plain scalar reads (no
 * lookup-annotation form needed — `resolveExpression` only ever substitutes
 * the plain value).
 *
 * Never throws: a fetch failure appends ONE warning here and returns `null` —
 * every Copy/Concat/Template rule for this invocation then skips gracefully
 * (see {@link applyCopy}, {@link applyExpressionRule}), rather than each rule
 * re-attempting (and re-failing) its own fetch.
 */
async function fetchSourceRecordForRules(
  dataService: IDataService,
  sourceEntity: string,
  sourceId: string,
  copyRules: IFieldMappingRule[],
  placeholderFields: Set<string>,
  warnings: string[]
): Promise<Record<string, unknown> | null> {
  const selectFields = new Set<string>(placeholderFields);
  for (const rule of copyRules) {
    selectFields.add(rule.targetFieldType === 'Lookup' ? `_${rule.sourceField}_value` : rule.sourceField);
  }

  try {
    const options = `?$select=${Array.from(selectFields).join(',')}`;
    return await dataService.retrieveRecord(sourceEntity, sourceId, options);
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    warnings.push(
      `Failed to fetch source record "${sourceEntity}"(${sourceId}) for Copy/Concat/Template rules; ` +
        `all such rules were skipped: ${message}`
    );
    return null;
  }
}

/**
 * Copy seam entrypoint (task 012). Branches on `rule.targetFieldType`:
 * `"Lookup"` → {@link applyCopyLookup}; anything else → {@link applyCopyScalar}.
 */
async function applyCopy(rule: IFieldMappingRule, ctx: IRuleApplyContext): Promise<void> {
  if (!ctx.sourceRecord) {
    // The batch fetch failed (or there genuinely was nothing to fetch) — the
    // single diagnostic warning was already recorded by
    // fetchSourceRecordForRules; skip this rule silently to avoid per-rule
    // warning spam for one shared root cause.
    return;
  }

  if (rule.targetFieldType === 'Lookup') {
    await applyCopyLookup(rule, ctx);
    return;
  }

  applyCopyScalar(rule, ctx);
}

/**
 * Scalar Copy: read the plain source value and assign it to the target payload
 * field.
 *
 * FR-09 (never-throw contract): a source field that is absent from the parent
 * record (not returned by the batch `$select` — e.g. a stale/renamed field
 * reference in the rule config) is a graceful skip, NOT a silent
 * `payload[targetField] = undefined` write recorded as mapped. `undefined`
 * here can only mean "key not present" (a present-but-explicitly-null field
 * is a legitimate empty value and is still copied through as `null`).
 */
function applyCopyScalar(rule: IFieldMappingRule, ctx: IRuleApplyContext): void {
  const value = ctx.sourceRecord?.[rule.sourceField];
  if (value === undefined) {
    ctx.warnings.push(
      `Copy rule "${rule.sourceField}"→"${rule.targetField}" skipped: source field "${rule.sourceField}" ` +
        `is missing from the parent record.`
    );
    return;
  }
  ctx.payload[rule.targetField] = value;
  ctx.fieldsMapped.push(rule.targetField);
}

/**
 * Lookup Copy: resolve the source lookup's GUID + referent entity, pluralize
 * the referent to its entity set, discover the TARGET entity's nav-prop for
 * that referent, and write `${navProp}@odata.bind = /${entitySet}(${guid})`.
 *
 * Never throws. An unresolvable annotation or an undiscoverable target
 * nav-prop appends a warning and skips this rule only — other rules continue.
 */
async function applyCopyLookup(rule: IFieldMappingRule, ctx: IRuleApplyContext): Promise<void> {
  const record = ctx.sourceRecord as Record<string, unknown>;
  const valueKey = `_${rule.sourceField}_value`;
  const annotationKey = `${valueKey}@Microsoft.Dynamics.CRM.lookuplogicalname`;

  const guid = record[valueKey];
  const referentEntity = record[annotationKey];

  if (typeof guid !== 'string' || guid.length === 0 || typeof referentEntity !== 'string' || referentEntity.length === 0) {
    ctx.warnings.push(
      `Copy rule "${rule.sourceField}"→"${rule.targetField}" skipped: could not resolve the referent ` +
        `entity for the source lookup (missing or empty "${annotationKey}" annotation).`
    );
    return;
  }

  // Discover the TARGET entity's nav-props (shared, cached — task 011) and
  // find the one bound to the referent entity. `rule.targetField` is passed
  // as the disambiguation hint: it is the exact ReferencingAttribute logical
  // name for the target lookup, so `findNavProp`'s `columnName.includes(hint)`
  // check resolves unambiguously even when several target lookups reference
  // the same entity (e.g. sprk_reportcard has 6 different contact lookups).
  const navProps = await discoverNavProps(ctx.targetEntity);
  const navProp = findNavProp(navProps, referentEntity, rule.targetField);

  if (!navProp) {
    ctx.warnings.push(
      `Copy rule "${rule.sourceField}"→"${rule.targetField}" skipped: no navigation property found ` +
        `on "${ctx.targetEntity}" for referent entity "${referentEntity}".`
    );
    return;
  }

  const entitySet = _resolveEntitySetForReferent(referentEntity);
  ctx.payload[`${navProp}@odata.bind`] = `/${entitySet}(${_cleanGuidForBind(guid)})`;
  ctx.fieldsMapped.push(rule.targetField);
}

/**
 * Strip braces + lowercase a GUID for `@odata.bind` URLs. Mirrors the private
 * `_cleanGuid` helper in `CreateInvoiceWizard/invoiceService.ts` (not exported
 * there, so duplicated here minimally per task 012's constraint).
 */
function _cleanGuidForBind(id: string): string {
  return id.replace(/[{}]/g, '').toLowerCase();
}

/**
 * Pluralize a referent entity logical name to its OData entity-set name.
 * Mirrors the same small-map-plus-`+s`-fallback pattern used by
 * `invoiceService`/`eventService`/`reportCardService`'s private
 * `_resolveEntitySet` (not exported there, so duplicated here minimally per
 * task 012's constraint). This project's confirmed lookup referents — `contact`
 * and `sprk_organization`, the 8 attorney/assigned-resource fields seeded in
 * task 030 — both pluralize correctly with the naive `+s` fallback, so no
 * exception map is included; add one here (mirroring the sibling services)
 * if a future referent needs it.
 */
function _resolveEntitySetForReferent(entityLogicalName: string): string {
  return `${entityLogicalName}s`;
}

// ============================================================================
// SEAM[013:Default] — literal from rule.defaultValue
// ----------------------------------------------------------------------------
// Writes the configured literal directly onto the payload. No source read
// needed — this is why Default rules are excluded from the batched $select
// in fetchSourceRecordForRules.
// ============================================================================

/**
 * Default/Constant seam (task 013): write `rule.defaultValue` verbatim onto
 * `ctx.payload[rule.targetField]`. If the literal is null/undefined/empty,
 * warn and skip — there is nothing to write.
 */
function applyDefault(rule: IFieldMappingRule, ctx: IRuleApplyContext): void {
  const value = rule.defaultValue;
  if (value === null || value === undefined || value === '') {
    ctx.warnings.push(
      `Default rule for "${rule.targetField}" skipped: rule.defaultValue is empty.`
    );
    return;
  }

  ctx.payload[rule.targetField] = value;
  ctx.fieldsMapped.push(rule.targetField);
}

// ============================================================================
// SEAM[013:Concat] / SEAM[013:Template] — shared placeholder resolver
// ----------------------------------------------------------------------------
// Concat and Template differ only by author intent (per design.md §4.1); both
// resolve `rule.expression` — a format string containing `{sprk_field}`
// tokens — against the parent record's values, fetched once alongside Copy
// rules (see fetchSourceRecordForRules). Only text/memo targets are
// supported; a Lookup target is guarded and skipped (a format string cannot
// bind a lookup).
// ============================================================================

/**
 * Extract the distinct field names referenced by `{sprk_field}` placeholders
 * in an expression, so callers can include them in the batched source
 * `$select` up front (task 013 constraint: parse tokens before fetching, no
 * per-rule round-trip). Returns an empty array for a null/empty expression.
 *
 * Uses a fresh regex literal (not a shared module-level global-flag regex) —
 * a global `RegExp` carries `lastIndex` state, so a single shared instance
 * reused across `matchAll`/`replace` call sites is a needless footgun.
 */
function extractPlaceholderFields(expression: string | null | undefined): string[] {
  if (!expression) {
    return [];
  }
  const fields = new Set<string>();
  for (const match of expression.matchAll(/\{([^{}]+)\}/g)) {
    fields.add(match[1]);
  }
  return Array.from(fields);
}

/**
 * Resolve every `{sprk_field}` placeholder in `expression` against
 * `parentValues`, substituting the parent record's value for each field.
 *
 * An unresolved placeholder (the field is missing from `parentValues`, or its
 * value is null/undefined — including when `parentValues` itself is `null`
 * because the batch fetch failed) is OMITTED from the output (replaced with
 * an empty string) — never left as the literal `"{sprk_field}"`, and the
 * field name is reported back via `unresolved` so the caller can append a
 * warning. This helper never throws.
 */
function resolveExpression(
  expression: string,
  parentValues: Record<string, unknown> | null
): { resolved: string; unresolved: string[] } {
  const unresolved: string[] = [];

  const resolved = expression.replace(/\{([^{}]+)\}/g, (_match, fieldName: string) => {
    const value = parentValues ? parentValues[fieldName] : undefined;
    if (value === null || value === undefined) {
      unresolved.push(fieldName);
      return '';
    }
    return String(value);
  });

  return { resolved, unresolved };
}

/**
 * Concat/Template seam entrypoint (task 013). Guards non-text targets (a
 * Lookup target cannot bind a format string), then resolves `rule.expression`
 * via {@link resolveExpression} against the pre-fetched source record and
 * writes the result onto the payload.
 */
function applyExpressionRule(rule: IFieldMappingRule, ctx: IRuleApplyContext, typeLabel: 'Concat' | 'Template'): void {
  if (rule.targetFieldType === 'Lookup') {
    ctx.warnings.push(
      `${typeLabel} rule "${rule.sourceField}"→"${rule.targetField}" skipped: a format string cannot bind ` +
        `a Lookup target.`
    );
    return;
  }

  const expression = rule.expression;
  if (!expression) {
    ctx.warnings.push(`${typeLabel} rule for "${rule.targetField}" skipped: rule.expression is empty.`);
    return;
  }

  const referencedFields = extractPlaceholderFields(expression);
  if (referencedFields.length > 0 && !ctx.sourceRecord) {
    // This rule's placeholders need the shared batch fetch, and that fetch
    // failed — the single diagnostic warning was already recorded by
    // fetchSourceRecordForRules (mirrors applyCopy's identical guard); skip
    // silently rather than writing a garbled partial string to the payload
    // and re-warning once per placeholder for one shared root cause.
    return;
  }

  const { resolved, unresolved } = resolveExpression(expression, ctx.sourceRecord);
  for (const field of unresolved) {
    ctx.warnings.push(
      `${typeLabel} rule "${rule.targetField}": placeholder "{${field}}" could not be resolved from the ` +
        `parent record and was omitted.`
    );
  }

  ctx.payload[rule.targetField] = resolved;
  ctx.fieldsMapped.push(rule.targetField);
}
