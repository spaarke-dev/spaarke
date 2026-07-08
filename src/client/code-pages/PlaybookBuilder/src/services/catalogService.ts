/**
 * Catalog service — CRUD for the two closed catalogs (FR-P4-04).
 *
 * SAVE PATH DECISION (docs/standards/DATA-ACCESS-DECISION-CRITERIA.md):
 * direct Dataverse Web API via the page's existing `dataverseClient`
 * (cookie-auth same-origin; the code-page equivalent of `Xrm.WebApi`) — NOT
 * the BFF. Criteria: #1 the BA's own Dataverse privileges must gate catalog
 * authoring (no BFF app-identity bypass), #2 simple single-table CRUD with
 * no orchestration, #3 no AI involvement in the save itself, #4 Dataverse
 * auditing suffices, #6 single-record writes. This also honors ADR-013: the
 * authoring surface never touches AI-internal types — it authors rows the
 * `ConsumerRoutingService` catalog reader consumes.
 *
 * VALIDATION GATE: `saveAction` / `saveBinding` REFUSE to write rows that
 * fail authoring-time validation (`schemaValidation.ts` — the twin of the
 * server's `OpenAiFunctionSchemaValidator`). A BA can never save the schema
 * shape that caused the G-P3 round-1 outage.
 */

import { createRecord, retrieveMultipleRecords, updateRecord } from './dataverseClient';
import type { DataverseRecord } from './dataverseClient';
import {
  validateChipTransitionsJson,
  validateJpsPrompt,
  validateMatchConditionsJson,
  validateOnEventBindingsJson,
  validateSchemaForAuthoring,
} from './schemaValidation';
import { ActionKind, AiModelTier, BindingCaptureMode, BindingDisposition, BindingRisk } from '../types/catalog';
import type { ActionRow, BindingRow } from '../types/catalog';

const ACTION_ENTITY_SET = 'sprk_analysisactions';
const BINDING_ENTITY_SET = 'sprk_playbookconsumers';

const LOG_PREFIX = '[PlaybookBuilder:CatalogService]';

// ─────────────────────────────────────────────────────────────────────────────
// Field-level validation (shared by forms + the save gate)
// ─────────────────────────────────────────────────────────────────────────────

export interface ValidationErrors {
  [field: string]: string;
}

/** All authoring-time errors for an Action row. Empty object = saveable. */
export function validateActionRow(row: ActionRow): ValidationErrors {
  const errors: ValidationErrors = {};

  if (row.name.trim() === '') errors.name = 'Name is required.';
  if (row.actionCode.trim() === '') {
    errors.actionCode = 'Action code is required (stable versioned code, e.g. SUM-CHAT@v1).';
  }

  if (row.kind === ActionKind.Coded && row.workflowClass.trim() === '') {
    errors.workflowClass = 'Coded Actions must name their registered ICodedWorkflow class.';
  }

  if (row.kind === ActionKind.Prompted && row.systemPrompt.trim() === '') {
    errors.systemPrompt = 'Prompted Actions require a prompt (JPS JSON or flat text).';
  }
  const jpsError = validateJpsPrompt(row.systemPrompt);
  if (jpsError !== null) errors.systemPrompt = jpsError;

  const inputError = validateSchemaForAuthoring(row.inputSchema);
  if (inputError !== null) errors.inputSchema = inputError;

  const outputError = validateSchemaForAuthoring(row.outputSchema);
  if (outputError !== null) errors.outputSchema = outputError;

  return errors;
}

/** All authoring-time errors for a Binding row. Empty object = saveable. */
export function validateBindingRow(row: BindingRow): ValidationErrors {
  const errors: ValidationErrors = {};

  if (row.name.trim() === '') errors.name = 'Name is required.';
  if (row.consumerType.trim() === '') {
    errors.consumerType = 'Consumer type is required (stable code, e.g. chat-summarize).';
  }
  if (!Number.isInteger(row.priority)) {
    errors.priority = 'Priority must be an integer (lower wins; 500 default).';
  }
  if (row.actionId === null) {
    errors.actionId = 'Select the target Action — a Binding routes to exactly one execution unit.';
  }
  if (row.toolDescription.trim() === '') {
    errors.toolDescription =
      'Tool description is required — it is the intent surface the agent loop matches this capability on.';
  }

  const chipError = validateChipTransitionsJson(row.chipTransitionsJson);
  if (chipError !== null) errors.chipTransitionsJson = chipError;

  const eventError = validateOnEventBindingsJson(row.onEventBindingsJson);
  if (eventError !== null) errors.onEventBindingsJson = eventError;

  const matchError = validateMatchConditionsJson(row.matchConditionsJson);
  if (matchError !== null) errors.matchConditionsJson = matchError;

  return errors;
}

// ─────────────────────────────────────────────────────────────────────────────
// Mapping
// ─────────────────────────────────────────────────────────────────────────────

function optionValue<T extends number>(raw: unknown, fallback: T): T {
  return typeof raw === 'number' ? (raw as T) : fallback;
}

function nullableOption<T extends number>(raw: unknown): T | null {
  return typeof raw === 'number' ? (raw as T) : null;
}

function text(raw: unknown): string {
  return typeof raw === 'string' ? raw : '';
}

function mapAction(record: DataverseRecord): ActionRow {
  return {
    id: text(record['sprk_analysisactionid']),
    name: text(record['sprk_name']),
    actionCode: text(record['sprk_actioncode']),
    description: text(record['sprk_description']),
    kind: optionValue(record['sprk_kind'], ActionKind.Prompted),
    workflowClass: text(record['sprk_workflowclass']),
    systemPrompt: text(record['sprk_systemprompt']),
    inputSchema: text(record['sprk_inputschema']),
    outputSchema: text(record['sprk_outputschemajson']),
    modelTier: nullableOption<AiModelTier>(record['sprk_modeltier']),
  };
}

function actionToColumns(row: ActionRow): DataverseRecord {
  return {
    sprk_name: row.name,
    sprk_actioncode: row.actionCode,
    sprk_description: row.description || null,
    sprk_kind: row.kind,
    sprk_workflowclass: row.kind === ActionKind.Coded ? row.workflowClass : null,
    sprk_systemprompt: row.systemPrompt || null,
    sprk_inputschema: row.inputSchema || null,
    sprk_outputschemajson: row.outputSchema || null,
    sprk_modeltier: row.modelTier,
  };
}

function mapBinding(record: DataverseRecord): BindingRow {
  const surfacesRaw = text(record['sprk_surfaces']);
  return {
    id: text(record['sprk_playbookconsumerid']),
    name: text(record['sprk_name']),
    consumerType: text(record['sprk_consumertype']),
    consumerCode: text(record['sprk_consumercode']),
    environment: text(record['sprk_environment']),
    priority: typeof record['sprk_priority'] === 'number' ? (record['sprk_priority'] as number) : 500,
    enabled: record['sprk_enabled'] !== false,
    actionId: text(record['_sprk_action_value']) || null,
    ucid: text(record['sprk_ucid']),
    toolDescription: text(record['sprk_tooldescription']),
    disposition: optionValue(record['sprk_disposition'], BindingDisposition.Informational),
    risk: optionValue(record['sprk_risk'], BindingRisk.None),
    captureMode: optionValue(record['sprk_capturemode'], BindingCaptureMode.LoopElicitation),
    chipTransitionsJson: text(record['sprk_chiptransitions']),
    onEventBindingsJson: text(record['sprk_oneventbindings']),
    matchConditionsJson: text(record['sprk_matchconditions']),
    surfaces: surfacesRaw
      ? surfacesRaw
          .split(',')
          .map(s => s.trim())
          .filter(s => s !== '')
      : [],
    modelTierOverride: nullableOption<AiModelTier>(record['sprk_modeltieroverride']),
  };
}

function bindingToColumns(row: BindingRow): DataverseRecord {
  const columns: DataverseRecord = {
    sprk_name: row.name,
    sprk_consumertype: row.consumerType,
    sprk_consumercode: row.consumerCode || null,
    sprk_environment: row.environment || null,
    sprk_priority: row.priority,
    sprk_enabled: row.enabled,
    sprk_ucid: row.ucid || null,
    sprk_tooldescription: row.toolDescription || null,
    sprk_disposition: row.disposition,
    sprk_risk: row.risk,
    sprk_capturemode: row.captureMode,
    sprk_chiptransitions: row.chipTransitionsJson || null,
    sprk_oneventbindings: row.onEventBindingsJson || null,
    sprk_matchconditions: row.matchConditionsJson || null,
    sprk_surfaces: row.surfaces.length > 0 ? row.surfaces.join(',') : null,
    sprk_modeltieroverride: row.modelTierOverride,
  };

  if (row.actionId) {
    // Navigation property per the Seed-PlaybookConsumers.ps1 convention
    // (`sprk_playbook@odata.bind`) — lookup schema names are lowercase here.
    columns['sprk_action@odata.bind'] = `/${ACTION_ENTITY_SET}(${row.actionId})`;
  }

  return columns;
}

// ─────────────────────────────────────────────────────────────────────────────
// Reads
// ─────────────────────────────────────────────────────────────────────────────

const ACTION_SELECT =
  '$select=sprk_analysisactionid,sprk_name,sprk_actioncode,sprk_description,sprk_kind,' +
  'sprk_workflowclass,sprk_systemprompt,sprk_inputschema,sprk_outputschemajson,sprk_modeltier' +
  '&$orderby=sprk_name asc';

const BINDING_SELECT =
  '$select=sprk_playbookconsumerid,sprk_name,sprk_consumertype,sprk_consumercode,sprk_environment,' +
  'sprk_priority,sprk_enabled,sprk_ucid,sprk_tooldescription,sprk_disposition,sprk_risk,' +
  'sprk_capturemode,sprk_chiptransitions,sprk_oneventbindings,sprk_matchconditions,sprk_surfaces,' +
  'sprk_modeltieroverride,_sprk_action_value' +
  '&$orderby=sprk_consumertype asc';

export async function listActions(): Promise<ActionRow[]> {
  const result = await retrieveMultipleRecords(ACTION_ENTITY_SET, ACTION_SELECT);
  return result.entities.map(mapAction);
}

export async function listBindings(): Promise<BindingRow[]> {
  const result = await retrieveMultipleRecords(BINDING_ENTITY_SET, BINDING_SELECT);
  return result.entities.map(mapBinding);
}

// ─────────────────────────────────────────────────────────────────────────────
// Writes (validation-gated)
// ─────────────────────────────────────────────────────────────────────────────

/** Thrown when a save is attempted on a row that fails authoring validation. */
export class CatalogValidationError extends Error {
  public readonly errors: ValidationErrors;

  constructor(rowKind: 'Action' | 'Binding', errors: ValidationErrors) {
    super(`${rowKind} row failed authoring validation: ${Object.keys(errors).join(', ')}`);
    this.name = 'CatalogValidationError';
    this.errors = errors;
  }
}

/** Create or update an Action row. Refuses invalid rows (fail-early twin of the H1 projection guard). */
export async function saveAction(row: ActionRow): Promise<string> {
  const errors = validateActionRow(row);
  if (Object.keys(errors).length > 0) {
    throw new CatalogValidationError('Action', errors);
  }

  const columns = actionToColumns(row);
  if (row.id) {
    await updateRecord(ACTION_ENTITY_SET, row.id, columns);
    console.info(`${LOG_PREFIX} Updated Action ${row.actionCode} (${row.id})`);
    return row.id;
  }

  const id = await createRecord(ACTION_ENTITY_SET, columns);
  console.info(`${LOG_PREFIX} Created Action ${row.actionCode} (${id})`);
  return id;
}

/** Create or update a Binding row. Refuses invalid rows. */
export async function saveBinding(row: BindingRow): Promise<string> {
  const errors = validateBindingRow(row);
  if (Object.keys(errors).length > 0) {
    throw new CatalogValidationError('Binding', errors);
  }

  const columns = bindingToColumns(row);
  if (row.id) {
    await updateRecord(BINDING_ENTITY_SET, row.id, columns);
    console.info(`${LOG_PREFIX} Updated Binding ${row.consumerType} (${row.id})`);
    return row.id;
  }

  const id = await createRecord(BINDING_ENTITY_SET, columns);
  console.info(`${LOG_PREFIX} Created Binding ${row.consumerType} (${id})`);
  return id;
}
