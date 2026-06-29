/**
 * Canvas-Time Validation — Validates prompt schema nodes against the canvas graph.
 *
 * Runs at save/sync time (not every keystroke). Checks AI nodes with JPS
 * prompt schemas against downstream nodes to detect mismatches:
 *
 *   - Missing task (error): JPS without instruction.task
 *   - Unresolvable $choices (error): $choices references a missing node or field
 *   - Output coverage (warning): downstream template refs without matching output fields
 *   - Choice consistency (warning): downstream choice fields without $choices or enum
 *   - Type compatibility (warning): output field types don't match downstream expectations
 *   - Edge perf hint (warning, R3 task 093 / FR-3H2.3): edge has no data dependency
 *     between source and target — advisory only, does NOT block save
 *
 * @see design.md Section "Canvas-Time Validation (PlaybookBuilder UI)"
 */

import type { Node, Edge } from '@xyflow/react';
import type { PlaybookNodeData } from '../types/canvas';
import type { PromptSchema, OutputFieldDefinition } from '../types/promptSchema';
import { getExecutorByValue } from '../config/executorMetadata';

// ---------------------------------------------------------------------------
// Public Types
// ---------------------------------------------------------------------------

/**
 * A single validation result for a prompt schema node.
 */
export interface PromptSchemaValidation {
  /** Canvas node ID that the validation applies to. */
  nodeId: string;
  /** Severity: "error" prevents save, "warning" is informational. */
  severity: 'error' | 'warning';
  /** Machine-readable rule identifier. */
  rule:
    | 'missing-task'
    | 'unresolvable-choices'
    | 'output-coverage'
    | 'choice-consistency'
    | 'type-compatibility'
    // R3 P5 (task 043): LookupUserMembership node required-field rules. Mirrors
    // server LookupUserMembershipNodeExecutor (task 041) contract — entityType
    // + outputVariable are required; roles + includeRelated are optional.
    | 'lookup-user-membership-missing-entity-type'
    | 'lookup-user-membership-missing-output-variable'
    // R4 hotfix #2 (2026-06-26): EntityNameValidator required-field rules.
    // Mirrors server EntityNameValidatorNodeExecutor (ActionType=141) contract —
    // candidateText + allowList + outputVariable are required; scrubStrategy is
    // optional (defaults to 'sentence'). Without these the executor would
    // silently no-op or fail at runtime; surfacing as 'error' blocks save via
    // the existing hasValidationErrors() consumer in playbookNodeSync.ts.
    | 'entity-name-validator-missing-candidate-text'
    | 'entity-name-validator-missing-allow-list'
    | 'entity-name-validator-missing-output-variable'
    // R3 P9 H2 (task 091): safety-net for duplicate OutputVariable names across
    // nodes. The primary defense is the rename-guard dialog in
    // NodePropertiesForm/Dialog; this rule catches the case where a user
    // hand-edits two nodes to share a non-empty outputVariable, which would
    // silently break {{name.output.*}} resolution at runtime.
    | 'outputvar-collision'
    // R3 P9 H2 (task 093, FR-3H2.3): per-edge advisory — edge connects two nodes
    // whose target config does NOT reference the source's outputVariable via
    // {{<source.outputVariable>.output.*}}. Severity 'warning' ONLY — does NOT
    // block save (FR-3H2.3 binding constraint; enforced by hasValidationErrors).
    | 'edge-no-data-dependency'
    // R7 Wave 8 task 089 (FR-27): node's data.executorType is not in the
    // 33-entry EXECUTOR_METADATA catalog OR is NULL/undefined. The canvas
    // renders the node via UnknownNode (warning-state shell); this validation
    // entry surfaces the same condition in the validation results panel.
    // Severity 'warning' (advisory, non-blocking save) — execution will fail
    // at runtime until the maker picks a known executor type in the Action tab.
    | 'r7-fr27-unknown-executor-type';
  /** Human-readable validation message. */
  message: string;
}

// ---------------------------------------------------------------------------
// Internal Types — downstream node parsing
// ---------------------------------------------------------------------------

/**
 * A single field mapping parsed from a downstream node's configJson.
 * Mirrors the FieldMapping interface in UpdateRecordForm.tsx.
 */
interface ParsedFieldMapping {
  field: string;
  type: string;
  value: string;
  options?: Record<string, number>;
}

/**
 * Parsed downstream node info relevant to validation.
 */
interface DownstreamNodeInfo {
  nodeId: string;
  nodeType: string;
  outputVariable: string;
  fieldMappings: ParsedFieldMapping[];
  /** All template references found in field mapping values. */
  templateRefs: TemplateRef[];
}

/**
 * A parsed Handlebars template reference like `{{output_classify.output.documentType}}`.
 */
interface TemplateRef {
  /** The raw template string, e.g. `{{output_classify.output.documentType}}`. */
  raw: string;
  /** The output variable name, e.g. `output_classify`. */
  outputVariable: string;
  /** The field name, e.g. `documentType`. */
  fieldName: string;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const LOG_PREFIX = '[PlaybookBuilder:CanvasValidation]';

/**
 * Regex to extract Handlebars template refs:  {{output_VAR.output.FIELD}}
 * Captures group 1 = outputVariable, group 2 = fieldName.
 */
const TEMPLATE_REF_RE = /\{\{(output_\w+)\.output\.(\w+)\}\}/g;

/**
 * Regex to parse $choices reference: "downstream:nodeVar.fieldName"
 * Captures group 1 = nodeVar (outputVariable), group 2 = fieldName.
 */
const CHOICES_REF_RE = /^downstream:(\w+)\.(.+)$/;

/**
 * AI node types that can have prompt schemas.
 */
const AI_NODE_TYPES = new Set(['aiAnalysis', 'aiCompletion']);

/**
 * Type compatibility map: OutputFieldType -> compatible FieldMappingTypes.
 * JPS output field types are: string, number, boolean, array, object.
 * UpdateRecord field mapping types are: string, choice, boolean, number.
 */
const TYPE_COMPAT: Record<string, string[]> = {
  string: ['string', 'choice'],
  number: ['number'],
  boolean: ['boolean'],
  array: ['string'],
  object: ['string'],
};

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Validate all AI nodes with prompt schemas against the canvas graph.
 *
 * @param nodes - All canvas nodes.
 * @param edges - All canvas edges.
 * @returns Array of validation results (errors and warnings).
 */
export function validatePromptSchemaNodes(nodes: Node<PlaybookNodeData>[], edges: Edge[]): PromptSchemaValidation[] {
  const results: PromptSchemaValidation[] = [];

  // Build lookup maps
  const nodeById = new Map<string, Node<PlaybookNodeData>>();
  const nodeByOutputVar = new Map<string, Node<PlaybookNodeData>>();
  for (const node of nodes) {
    nodeById.set(node.id, node);
    const outputVar = node.data.outputVariable;
    if (outputVar) {
      nodeByOutputVar.set(outputVar, node);
    }
  }

  // Build outgoing edge map: sourceId -> [targetIds]
  const outgoingEdges = buildOutgoingEdgeMap(edges);

  // Find AI nodes with prompt schemas
  for (const node of nodes) {
    if (!AI_NODE_TYPES.has(node.data.type)) continue;

    const schema = node.data.promptSchema;
    if (!schema) continue;

    // Rule (a): Missing task
    results.push(...validateMissingTask(node.id, schema));

    // Gather downstream node info for graph-aware rules
    const downstreamInfos = collectDownstreamInfo(node.id, outgoingEdges, nodeById);

    // Rule (b): Unresolvable $choices
    results.push(...validateUnresolvableChoices(node.id, schema, nodeByOutputVar, downstreamInfos));

    // Rule (c): Output coverage
    results.push(...validateOutputCoverage(node.id, schema, downstreamInfos));

    // Rule (d): Choice consistency
    results.push(...validateChoiceConsistency(node.id, schema, downstreamInfos));

    // Rule (e): Type compatibility
    results.push(...validateTypeCompatibility(node.id, schema, downstreamInfos));
  }

  // R3 P5 (task 043): per-ActionType config-shape validation for non-AI nodes.
  // Currently covers LookupUserMembership (FR-1B.4); extend here when other
  // per-ActionType forms add required fields that must block save.
  for (const node of nodes) {
    if (node.data.type === 'lookupUserMembership') {
      results.push(...validateLookupUserMembershipNode(node.id, node));
    }
    // R4 hotfix #2 (2026-06-26): EntityNameValidator per-ActionType config
    // validation. Required fields: candidateText + allowList (in configJson)
    // and outputVariable (on node.data). Mirrors LookupUserMembership pattern.
    if (node.data.type === 'entityNameValidator') {
      results.push(...validateEntityNameValidatorNode(node.id, node));
    }
  }

  // R3 P9 H2 (task 091): OutputVariable collision safety net (FR-3H2.1).
  // Catches the case where two nodes share a non-empty outputVariable —
  // the primary UX defense is the rename-guard dialog in
  // NodePropertiesForm/Dialog, but a user can still hand-edit a second node
  // to collide. Runs over the full nodes array (cross-node rule), not per-type.
  results.push(...validateOutputVariableCollisions(nodes));

  // R7 Wave 8 task 089 (FR-27): unknown executor type advisory.
  // Flags any node whose data.executorType is not present in EXECUTOR_METADATA.
  // The renderer (UnknownNode.tsx, coerced via canvasStore.coerceUnknownNodeTypes)
  // already shows a warning-state shell, but a validation entry makes the
  // condition discoverable from the validation results panel + blocks downstream
  // confusion. Severity = 'warning' (advisory, non-blocking) per existing
  // pattern; the maker can still save the playbook but execution will fail
  // until they pick a known executor type.
  for (const node of nodes) {
    // Start nodes never carry executorType — never flag them.
    if (node.data.type === 'start') continue;
    const executorType = node.data.executorType;
    const known = typeof executorType === 'number' ? getExecutorByValue(executorType) : undefined;
    if (!known) {
      results.push({
        nodeId: node.id,
        severity: 'warning',
        rule: 'r7-fr27-unknown-executor-type',
        message:
          executorType === undefined || executorType === null
            ? 'Executor Type not set — pick an Executor Type in the Action tab to enable execution.'
            : `Unknown Executor Type ${executorType}. Pick a known type from the Action tab to enable execution.`,
      });
    }
  }

  // Per-edge advisory rules (task 093) — sibling to the per-NODE-type loop above.
  // Iterates edges (not nodes) to flag edges that lack a data dependency between
  // their source and target nodes. Warnings only — does NOT block save.
  // R3 P9 H2 (task 093, FR-3H2.3): edge-no-data-dependency advisory.
  for (const edge of edges) {
    results.push(...validateEdgePerfHint(edge, nodeById));
  }

  if (results.length > 0) {
    console.info(
      `${LOG_PREFIX} Validation found ${results.filter(r => r.severity === 'error').length} error(s), ` +
        `${results.filter(r => r.severity === 'warning').length} warning(s)`
    );
  }

  return results;
}

/**
 * Check whether validation results contain any errors (which should prevent save).
 */
export function hasValidationErrors(results: PromptSchemaValidation[]): boolean {
  return results.some(r => r.severity === 'error');
}

/**
 * Group validation results by node ID for easy consumption by node components.
 */
export function groupValidationsByNode(
  results: PromptSchemaValidation[]
): Map<string, { errors: string[]; warnings: string[] }> {
  const grouped = new Map<string, { errors: string[]; warnings: string[] }>();

  for (const r of results) {
    if (!grouped.has(r.nodeId)) {
      grouped.set(r.nodeId, { errors: [], warnings: [] });
    }
    const entry = grouped.get(r.nodeId)!;
    if (r.severity === 'error') {
      entry.errors.push(r.message);
    } else {
      entry.warnings.push(r.message);
    }
  }

  return grouped;
}

// ---------------------------------------------------------------------------
// Rule Implementations
// ---------------------------------------------------------------------------

/**
 * Rule (a): Missing task — error if JPS format detected and instruction.task is empty/missing.
 */
function validateMissingTask(nodeId: string, schema: PromptSchema): PromptSchemaValidation[] {
  if (!schema.instruction || typeof schema.instruction.task !== 'string' || schema.instruction.task.trim() === '') {
    return [
      {
        nodeId,
        severity: 'error',
        rule: 'missing-task',
        message: 'instruction.task is required. Provide the specific work the AI must perform.',
      },
    ];
  }
  return [];
}

/**
 * Rule (b): Unresolvable $choices — error if output field has $choices but
 * the referenced downstream node doesn't exist or has no matching field.
 */
function validateUnresolvableChoices(
  nodeId: string,
  schema: PromptSchema,
  nodeByOutputVar: Map<string, Node<PlaybookNodeData>>,
  downstreamInfos: DownstreamNodeInfo[]
): PromptSchemaValidation[] {
  const results: PromptSchemaValidation[] = [];
  const outputFields = schema.output?.fields ?? [];

  for (const field of outputFields) {
    if (!field.$choices) continue;

    const parsed = parseChoicesRef(field.$choices);
    if (!parsed) {
      results.push({
        nodeId,
        severity: 'error',
        rule: 'unresolvable-choices',
        message: `output.fields["${field.name}"].$choices has invalid format: "${field.$choices}". Expected "downstream:nodeVar.fieldName".`,
      });
      continue;
    }

    // Check that the referenced node exists
    const targetNode = nodeByOutputVar.get(parsed.outputVariable);
    if (!targetNode) {
      results.push({
        nodeId,
        severity: 'error',
        rule: 'unresolvable-choices',
        message: `output.fields["${field.name}"].$choices references node "${parsed.outputVariable}" which does not exist on the canvas.`,
      });
      continue;
    }

    // Check that the referenced node is a downstream node with a matching field
    const matchingDownstream = downstreamInfos.find(d => d.outputVariable === parsed.outputVariable);
    if (!matchingDownstream) {
      // The node exists but is not directly downstream — still flag it
      results.push({
        nodeId,
        severity: 'error',
        rule: 'unresolvable-choices',
        message: `output.fields["${field.name}"].$choices references node "${parsed.outputVariable}" which is not a downstream node.`,
      });
      continue;
    }

    // Check that the downstream node has a matching field in fieldMappings
    const matchingField = matchingDownstream.fieldMappings.find(fm => fm.field === parsed.fieldName);
    if (!matchingField) {
      results.push({
        nodeId,
        severity: 'error',
        rule: 'unresolvable-choices',
        message: `output.fields["${field.name}"].$choices references field "${parsed.fieldName}" on node "${parsed.outputVariable}", but that node has no matching fieldMapping.`,
      });
      continue;
    }

    // Check that the matching field has options (i.e., it's a choice field)
    if (!matchingField.options || Object.keys(matchingField.options).length === 0) {
      results.push({
        nodeId,
        severity: 'error',
        rule: 'unresolvable-choices',
        message: `output.fields["${field.name}"].$choices references field "${parsed.fieldName}" on node "${parsed.outputVariable}", but that field has no options defined.`,
      });
    }
  }

  return results;
}

/**
 * Rule (c): Output coverage — warning if downstream nodes reference
 * {{output_X.output.field}} but the AI node doesn't have that field in output.fields.
 */
function validateOutputCoverage(
  nodeId: string,
  schema: PromptSchema,
  downstreamInfos: DownstreamNodeInfo[]
): PromptSchemaValidation[] {
  const results: PromptSchemaValidation[] = [];
  const outputFieldNames = new Set((schema.output?.fields ?? []).map(f => f.name));

  for (const downstream of downstreamInfos) {
    for (const ref of downstream.templateRefs) {
      // Only check refs that target this AI node's outputVariable
      // We don't know this node's outputVariable from the schema alone,
      // so we check against all refs that reference fields we might produce
      if (!outputFieldNames.has(ref.fieldName) && outputFieldNames.size > 0) {
        // This ref's field is not in our output fields — could be
        // referencing a different upstream node. Only warn if the
        // ref's outputVariable matches a node outputVariable that
        // traces back to this AI node.
        // For simplicity: skip if we can't confirm this ref targets us
      }
    }
  }

  // Reverse check: collect all template refs in downstream nodes that reference
  // this node's output variable. Compare those fields against output.fields.
  // We need the current node's outputVariable for this.
  // Since we're operating on Node<PlaybookNodeData>, get it from the parent scope.
  // Actually, let's use the node we already have:
  // Note: we receive nodeId but need outputVariable. Let's collect refs differently.

  // Collect all template refs across all downstream nodes
  const allDownstreamRefs = downstreamInfos.flatMap(d => d.templateRefs);

  // We don't have the AI node's outputVariable in this function,
  // so we match by checking if any downstream ref's fieldName is
  // NOT in output.fields. We filter to refs whose outputVariable
  // prefix matches any convention (output_<canvasId fragment>).
  // More precisely: collect unique fieldNames referenced by downstream nodes
  // and compare to what this node's output.fields declares.
  // Since there could be multiple upstream AI nodes, we need to be selective.
  // We'll rely on the edge graph: downstream nodes are connected to THIS node,
  // so template refs that match our output fields are relevant.
  for (const downstream of downstreamInfos) {
    for (const ref of downstream.templateRefs) {
      // If this field IS in our output fields, that's fine (coverage confirmed)
      if (outputFieldNames.has(ref.fieldName)) continue;

      // If output.fields is empty, we can't validate coverage
      if (outputFieldNames.size === 0) continue;

      // The downstream node references a field we don't produce.
      // This could be from a different upstream node, so only warn
      // if the schema has output fields at all (indicating JPS is being used).
      results.push({
        nodeId,
        severity: 'warning',
        rule: 'output-coverage',
        message: `Downstream node references "{{${ref.outputVariable}.output.${ref.fieldName}}}" but output.fields does not include a field named "${ref.fieldName}".`,
      });
    }
  }

  return results;
}

/**
 * Rule (d): Choice consistency — warning if downstream UpdateRecord has
 * choice options for a field, but AI node doesn't have $choices or enum
 * for a matching output field.
 */
function validateChoiceConsistency(
  nodeId: string,
  schema: PromptSchema,
  downstreamInfos: DownstreamNodeInfo[]
): PromptSchemaValidation[] {
  const results: PromptSchemaValidation[] = [];
  const outputFields = schema.output?.fields ?? [];

  // Build a map of output field name -> definition for quick lookup
  const outputFieldMap = new Map<string, OutputFieldDefinition>();
  for (const field of outputFields) {
    outputFieldMap.set(field.name, field);
  }

  for (const downstream of downstreamInfos) {
    for (const mapping of downstream.fieldMappings) {
      // Only check choice-type field mappings that have options
      if (mapping.type !== 'choice' || !mapping.options) continue;
      if (Object.keys(mapping.options).length === 0) continue;

      // Find which output field name this mapping's value template references
      const refs = extractTemplateRefs(mapping.value);
      for (const ref of refs) {
        const outputField = outputFieldMap.get(ref.fieldName);
        if (!outputField) continue; // Not our field, or not defined

        // Output field exists — check if it has $choices or enum
        const hasChoices = Boolean(outputField.$choices);
        const hasEnum = Array.isArray(outputField.enum) && outputField.enum.length > 0;

        if (!hasChoices && !hasEnum) {
          results.push({
            nodeId,
            severity: 'warning',
            rule: 'choice-consistency',
            message:
              `output.fields["${ref.fieldName}"] maps to choice field "${mapping.field}" on node "${downstream.outputVariable}" ` +
              `which has ${Object.keys(mapping.options).length} options, but the output field has no $choices or enum. ` +
              `Consider adding $choices: "downstream:${downstream.outputVariable}.${mapping.field}" for consistency.`,
          });
        }
      }
    }
  }

  return results;
}

/**
 * Rule (e): Type compatibility — warning if output field type doesn't match
 * downstream UpdateRecord field type expectation.
 */
function validateTypeCompatibility(
  nodeId: string,
  schema: PromptSchema,
  downstreamInfos: DownstreamNodeInfo[]
): PromptSchemaValidation[] {
  const results: PromptSchemaValidation[] = [];
  const outputFields = schema.output?.fields ?? [];

  // Build a map of output field name -> definition
  const outputFieldMap = new Map<string, OutputFieldDefinition>();
  for (const field of outputFields) {
    outputFieldMap.set(field.name, field);
  }

  for (const downstream of downstreamInfos) {
    for (const mapping of downstream.fieldMappings) {
      // Find which output field this mapping references
      const refs = extractTemplateRefs(mapping.value);
      for (const ref of refs) {
        const outputField = outputFieldMap.get(ref.fieldName);
        if (!outputField) continue;

        // Check type compatibility
        const compatibleTypes = TYPE_COMPAT[outputField.type];
        if (compatibleTypes && !compatibleTypes.includes(mapping.type)) {
          results.push({
            nodeId,
            severity: 'warning',
            rule: 'type-compatibility',
            message:
              `output.fields["${ref.fieldName}"] has type "${outputField.type}" but downstream field "${mapping.field}" ` +
              `on node "${downstream.outputVariable}" expects type "${mapping.type}". ` +
              `Compatible types for "${outputField.type}" are: ${compatibleTypes.join(', ')}.`,
          });
        }
      }
    }
  }

  return results;
}

/**
 * R3 P5 (task 043): Validate a LookupUserMembership node's config.
 *
 * Mirrors the server-side LookupUserMembershipNodeExecutor (task 041) contract:
 *   - entityType (string, required) — Dataverse logical name; resolver validates at runtime
 *   - outputVariable (string, required) — canvas variable bound to resolved user IDs
 *   - roles (string[], optional) — empty = all roles
 *   - includeRelated (boolean, optional, default false) — 1-hop transitive per owner Q3
 *
 * Per spec FR-1B.4 + owner Q4 (entityType is free-text — no allow-list).
 * Missing required fields produce 'error' severity which blocks playbook save
 * via the existing hasValidationErrors() consumer in playbookNodeSync.ts.
 */
function validateLookupUserMembershipNode(nodeId: string, node: Node<PlaybookNodeData>): PromptSchemaValidation[] {
  const results: PromptSchemaValidation[] = [];

  // Parse configJson defensively (matches LookupUserMembershipForm.parseConfig).
  let entityType = '';
  const configJsonStr = node.data.configJson;
  if (typeof configJsonStr === 'string' && configJsonStr.length > 0) {
    try {
      const parsed = JSON.parse(configJsonStr) as { entityType?: unknown };
      if (typeof parsed.entityType === 'string') {
        entityType = parsed.entityType;
      }
    } catch {
      // Malformed configJson — treat entityType as missing; rule below fires.
    }
  }

  if (entityType.trim() === '') {
    results.push({
      nodeId,
      severity: 'error',
      rule: 'lookup-user-membership-missing-entity-type',
      message:
        'Lookup User Membership: Entity Type is required (Dataverse logical name of the parent record, e.g. sprk_matter).',
    });
  }

  // outputVariable lives on node.data (NOT configJson) — the shared field set
  // by the Basic section, consumed by IMembershipResolverService binding.
  const outputVariable = typeof node.data.outputVariable === 'string' ? node.data.outputVariable.trim() : '';
  if (outputVariable === '') {
    results.push({
      nodeId,
      severity: 'error',
      rule: 'lookup-user-membership-missing-output-variable',
      message:
        'Lookup User Membership: Output Variable is required so downstream nodes can reference the resolved user IDs.',
    });
  }

  return results;
}

/**
 * R4 hotfix #2 (2026-06-26): Validate an EntityNameValidator node's config.
 *
 * Mirrors the server-side EntityNameValidatorNodeExecutor (ActionType=141)
 * contract:
 *   - candidateText (string, required) — template expression resolving to the
 *     LLM-emitted text to scrub
 *   - allowList (string, required) — template expression resolving to the
 *     string[] of permitted entity names
 *   - scrubStrategy (string, optional, default 'sentence') — 'sentence' | 'phrase'
 *   - outputVariable (string, required) — canvas variable bound to scrubbed text
 *
 * Missing required fields produce 'error' severity which blocks playbook save
 * via the existing hasValidationErrors() consumer in playbookNodeSync.ts.
 *
 * Pattern peer: validateLookupUserMembershipNode (R3 task 043). The form
 * (EntityNameValidatorForm.tsx) already marks these fields visually with the
 * Fluent `required` Label prop — this validator enforces the contract at save.
 */
function validateEntityNameValidatorNode(nodeId: string, node: Node<PlaybookNodeData>): PromptSchemaValidation[] {
  const results: PromptSchemaValidation[] = [];

  // Parse configJson defensively (matches EntityNameValidatorForm.parseConfig).
  let candidateText = '';
  let allowList = '';
  const configJsonStr = node.data.configJson;
  if (typeof configJsonStr === 'string' && configJsonStr.length > 0) {
    try {
      const parsed = JSON.parse(configJsonStr) as { candidateText?: unknown; allowList?: unknown };
      if (typeof parsed.candidateText === 'string') {
        candidateText = parsed.candidateText;
      }
      if (typeof parsed.allowList === 'string') {
        allowList = parsed.allowList;
      }
    } catch {
      // Malformed configJson — treat both fields as missing; rules below fire.
    }
  }

  if (candidateText.trim() === '') {
    results.push({
      nodeId,
      severity: 'error',
      rule: 'entity-name-validator-missing-candidate-text',
      message: 'Entity Name Validator: Candidate text source binding is required (e.g. {{narrate.output.result}}).',
    });
  }

  if (allowList.trim() === '') {
    results.push({
      nodeId,
      severity: 'error',
      rule: 'entity-name-validator-missing-allow-list',
      message: 'Entity Name Validator: Allow-list source binding is required (string[] of permitted entity names).',
    });
  }

  // outputVariable lives on node.data (shared Basic-section field, NOT in
  // configJson — the EntityNameValidatorForm authored a redundant copy that
  // the server contract does not consume).
  const outputVariable = typeof node.data.outputVariable === 'string' ? node.data.outputVariable.trim() : '';
  if (outputVariable === '') {
    results.push({
      nodeId,
      severity: 'error',
      rule: 'entity-name-validator-missing-output-variable',
      message:
        'Entity Name Validator: Output Variable is required so downstream nodes can reference the scrubbed text.',
    });
  }

  return results;
}

/**
 * R3 P9 H2 (task 091): Validate OutputVariable uniqueness across all nodes.
 *
 * Emits an 'error' for every node whose non-empty outputVariable is shared by
 * at least one other node. Each colliding node receives its own result so the
 * NodeValidationBadge can surface the message inline on every offender.
 *
 * Trim()'d empty values are skipped (the missing-output-variable rules cover
 * those cases for node types that require an outputVariable).
 */
function validateOutputVariableCollisions(nodes: Node<PlaybookNodeData>[]): PromptSchemaValidation[] {
  const results: PromptSchemaValidation[] = [];

  // Build outputVariable -> [nodeIds] map (only non-empty values).
  const byVar = new Map<string, string[]>();
  for (const node of nodes) {
    const ov = typeof node.data.outputVariable === 'string' ? node.data.outputVariable.trim() : '';
    if (ov === '') continue;
    if (!byVar.has(ov)) byVar.set(ov, []);
    byVar.get(ov)!.push(node.id);
  }

  for (const [name, ids] of byVar.entries()) {
    if (ids.length < 2) continue;
    for (const id of ids) {
      results.push({
        nodeId: id,
        severity: 'error',
        rule: 'outputvar-collision',
        message: `Duplicate OutputVariable name "${name}". Each node must have a unique OutputVariable so {{${name}.output.*}} references resolve unambiguously.`,
      });
    }
  }

  return results;
}

/**
 * R3 P9 H2 (task 093, FR-3H2.3): Per-edge advisory — flags edges where the
 * target node's serialized configuration does NOT reference the source node's
 * `outputVariable` via `{{<source.outputVariable>.output.*}}`.
 *
 * Such edges force sequential execution without communicating any data, which
 * usually means the author wired them by accident (e.g. dragged a connection
 * to enforce ordering when none is needed). The author may have a legitimate
 * reason (side-effect-only sequencing), so this is **advisory only** —
 * severity 'warning', NOT 'error'. Save remains allowed (per FR-3H2.3 binding;
 * enforced by `hasValidationErrors` in `playbookNodeSync.validateAndSyncNodes`).
 *
 * Skips when:
 *   - Source node missing or has no non-empty outputVariable (nothing to reference)
 *   - Target node missing
 *   - Target's configuration cannot be parsed (graceful degradation)
 *
 * Reuses `parseDownstreamNode` for reference enumeration so the scan covers the
 * same field set as the other downstream-graph rules (configJson.fieldMappings,
 * legacy fields dict, template/body/subject/description, plus node.data
 * template / emailBody / emailSubject).
 */
function validateEdgePerfHint(edge: Edge, nodeById: Map<string, Node<PlaybookNodeData>>): PromptSchemaValidation[] {
  const sourceNode = nodeById.get(edge.source);
  const targetNode = nodeById.get(edge.target);
  if (!sourceNode || !targetNode) return [];

  // No outputVariable to look for — can't have a data dependency.
  const sourceOutputVar =
    typeof sourceNode.data.outputVariable === 'string' ? sourceNode.data.outputVariable.trim() : '';
  if (sourceOutputVar === '') return [];

  // Reuse the same scanner the other downstream rules use. Graceful on parse failure.
  const targetInfo = parseDownstreamNode(targetNode);
  if (!targetInfo) return [];

  const hasDataDependency = targetInfo.templateRefs.some(ref => ref.outputVariable === sourceOutputVar);
  if (hasDataDependency) return [];

  // No reference — emit advisory warning attached to the SOURCE node so the
  // existing NodeValidationBadge surface (consumed by NodePropertiesForm) picks
  // it up via the badge's existing `warnings` prop. Attaching to source mirrors
  // the existing per-rule pattern of "validation on the node the user is
  // typically editing when they wire the edge".
  return [
    {
      nodeId: sourceNode.id,
      severity: 'warning',
      rule: 'edge-no-data-dependency',
      message:
        `Edge from "${sourceNode.data.label || sourceNode.id}" to "${targetNode.data.label || targetNode.id}" ` +
        `does not reference {{${sourceOutputVar}.output.*}} in the target's configuration. ` +
        'This edge forces sequential execution. Confirm or remove?',
    },
  ];
}

// ---------------------------------------------------------------------------
// Public helpers — exposed for the OutputVariable rename guard (task 091)
// ---------------------------------------------------------------------------

/**
 * A discovered reference to a node's OutputVariable found inside another
 * node's serialized configuration. Returned by {@link findOutputVariableReferences}.
 */
export interface OutputVariableReference {
  /** Canvas node ID where the reference was found. */
  nodeId: string;
  /** Human-friendly node label (falls back to nodeId). */
  nodeLabel: string;
  /** Node type (e.g. 'updateRecord', 'sendEmail'). */
  nodeType: string;
  /** All distinct raw template strings (e.g. `{{output_classify.output.documentType}}`). */
  rawRefs: string[];
}

/**
 * Scan every node in the canvas (except `selfNodeId`) for `{{<oldName>.output.*}}`
 * references in its serialized configuration. Reuses the same field-set that
 * the existing downstream-info parser walks (configJson.fieldMappings,
 * configJson.fields, configJson.{template,body,subject,description}, and the
 * `template` / `emailBody` / `emailSubject` fields on node.data).
 *
 * Used by the rename-guard dialog in NodePropertiesForm / NodePropertiesDialog
 * to ask the user how to handle existing downstream references when they
 * rename an OutputVariable.
 *
 * @param oldName       The OutputVariable name to search for.
 * @param nodes         All canvas nodes.
 * @param selfNodeId    The node currently being renamed (excluded from scan).
 * @returns One entry per node that has at least one matching template reference;
 *          empty array if no references exist anywhere.
 */
export function findOutputVariableReferences(
  oldName: string,
  nodes: Node<PlaybookNodeData>[],
  selfNodeId: string
): OutputVariableReference[] {
  const trimmedOld = oldName.trim();
  if (trimmedOld === '') return [];

  const results: OutputVariableReference[] = [];

  for (const node of nodes) {
    if (node.id === selfNodeId) continue;

    const matched = new Set<string>();
    const collect = (value: unknown) => {
      if (typeof value !== 'string' || value.length === 0) return;
      const refs = extractTemplateRefs(value);
      for (const ref of refs) {
        if (ref.outputVariable === trimmedOld) {
          matched.add(ref.raw);
        }
      }
    };

    // Scan node.data direct template-like fields (same set as parseDownstreamNode).
    collect(node.data.template as unknown);
    collect(node.data.emailBody as unknown);
    collect(node.data.emailSubject as unknown);
    collect(node.data.conditionJson as unknown);

    // Scan configJson — defensive JSON.parse mirrors parseDownstreamNode.
    const configJsonStr = node.data.configJson;
    if (typeof configJsonStr === 'string' && configJsonStr.length > 0) {
      try {
        const parsed = JSON.parse(configJsonStr) as Record<string, unknown>;

        // fieldMappings[].value
        const fm = parsed.fieldMappings;
        if (Array.isArray(fm)) {
          for (const entry of fm) {
            if (entry && typeof entry === 'object' && 'value' in entry) {
              collect((entry as Record<string, unknown>).value);
            }
          }
        }

        // legacy fields {key: value}
        if (parsed.fields && typeof parsed.fields === 'object' && !Array.isArray(parsed.fields)) {
          for (const value of Object.values(parsed.fields as Record<string, unknown>)) {
            collect(value);
          }
        }

        // Top-level template fields (deliverOutput, sendEmail, etc.)
        for (const key of ['template', 'body', 'subject', 'description']) {
          collect(parsed[key]);
        }
      } catch {
        // Malformed configJson — skip silently (graceful degradation, matches parseDownstreamNode).
      }
    }

    if (matched.size > 0) {
      results.push({
        nodeId: node.id,
        nodeLabel: typeof node.data.label === 'string' && node.data.label.trim() !== '' ? node.data.label : node.id,
        nodeType: node.data.type,
        rawRefs: Array.from(matched),
      });
    }
  }

  return results;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Build map: sourceNodeId -> [targetNodeIds] from edges.
 */
function buildOutgoingEdgeMap(edges: Edge[]): Map<string, string[]> {
  const map = new Map<string, string[]>();
  for (const e of edges) {
    if (!map.has(e.source)) map.set(e.source, []);
    map.get(e.source)!.push(e.target);
  }
  return map;
}

/**
 * Collect downstream node info by traversing outgoing edges from
 * the given AI node. Parses configJson of downstream nodes to
 * extract fieldMappings and template refs.
 *
 * Only traverses direct descendants (one level) to keep validation practical.
 */
function collectDownstreamInfo(
  aiNodeId: string,
  outgoingEdges: Map<string, string[]>,
  nodeById: Map<string, Node<PlaybookNodeData>>
): DownstreamNodeInfo[] {
  const downstreamIds = outgoingEdges.get(aiNodeId) ?? [];
  const infos: DownstreamNodeInfo[] = [];

  for (const targetId of downstreamIds) {
    const targetNode = nodeById.get(targetId);
    if (!targetNode) continue;

    const info = parseDownstreamNode(targetNode);
    if (info) {
      infos.push(info);
    }
  }

  return infos;
}

/**
 * Parse a downstream canvas node to extract its fieldMappings and template refs.
 * Returns null if the node can't be parsed (graceful degradation).
 */
function parseDownstreamNode(node: Node<PlaybookNodeData>): DownstreamNodeInfo | null {
  try {
    const outputVariable = (node.data.outputVariable as string) ?? `output_${node.id}`;
    const fieldMappings: ParsedFieldMapping[] = [];
    const templateRefs: TemplateRef[] = [];

    // Parse configJson for fieldMappings
    const configJsonStr = node.data.configJson;
    if (typeof configJsonStr === 'string' && configJsonStr.length > 0) {
      const parsed = JSON.parse(configJsonStr);

      // Extract fieldMappings array (UpdateRecord nodes)
      if (Array.isArray(parsed.fieldMappings)) {
        for (const fm of parsed.fieldMappings) {
          if (fm && typeof fm.field === 'string' && typeof fm.type === 'string') {
            const mapping: ParsedFieldMapping = {
              field: fm.field,
              type: fm.type,
              value: typeof fm.value === 'string' ? fm.value : '',
              options: fm.options && typeof fm.options === 'object' ? fm.options : undefined,
            };
            fieldMappings.push(mapping);

            // Extract template refs from the value
            templateRefs.push(...extractTemplateRefs(mapping.value));
          }
        }
      }

      // Also scan legacy "fields" dict for template refs
      if (parsed.fields && typeof parsed.fields === 'object' && !Array.isArray(parsed.fields)) {
        for (const [field, value] of Object.entries(parsed.fields)) {
          if (typeof value === 'string') {
            fieldMappings.push({
              field,
              type: 'string',
              value,
            });
            templateRefs.push(...extractTemplateRefs(value));
          }
        }
      }

      // Scan template fields (deliverOutput, sendEmail, etc.)
      for (const key of ['template', 'body', 'subject', 'description']) {
        if (typeof parsed[key] === 'string') {
          templateRefs.push(...extractTemplateRefs(parsed[key]));
        }
      }
    }

    // Also scan template-like fields directly on node data
    if (typeof node.data.template === 'string') {
      templateRefs.push(...extractTemplateRefs(node.data.template as string));
    }
    if (typeof node.data.emailBody === 'string') {
      templateRefs.push(...extractTemplateRefs(node.data.emailBody as string));
    }
    if (typeof node.data.emailSubject === 'string') {
      templateRefs.push(...extractTemplateRefs(node.data.emailSubject as string));
    }

    return {
      nodeId: node.id,
      nodeType: node.data.type,
      outputVariable,
      fieldMappings,
      templateRefs,
    };
  } catch (err) {
    // Graceful: if downstream node can't be parsed, skip it
    console.warn(`${LOG_PREFIX} Failed to parse downstream node ${node.id}:`, err);
    return null;
  }
}

/**
 * Extract all {{output_X.output.field}} template references from a string.
 */
function extractTemplateRefs(value: string): TemplateRef[] {
  if (!value) return [];

  const refs: TemplateRef[] = [];
  let match: RegExpExecArray | null;

  // Reset regex state
  TEMPLATE_REF_RE.lastIndex = 0;
  while ((match = TEMPLATE_REF_RE.exec(value)) !== null) {
    refs.push({
      raw: match[0],
      outputVariable: match[1],
      fieldName: match[2],
    });
  }

  return refs;
}

/**
 * Parse a $choices reference string like "downstream:nodeVar.fieldName".
 * Returns the parsed parts or null if the format is invalid.
 */
function parseChoicesRef(ref: string): { outputVariable: string; fieldName: string } | null {
  const match = CHOICES_REF_RE.exec(ref);
  if (!match) return null;
  return {
    outputVariable: match[1],
    fieldName: match[2],
  };
}
