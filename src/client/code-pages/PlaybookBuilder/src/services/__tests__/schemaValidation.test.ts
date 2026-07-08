/**
 * Client twin of the server `OpenAiFunctionSchemaValidatorTests` rule matrix
 * (task 053 / FR-P4-04). Pins the shared rules so the client validator and
 * `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/OpenAiFunctionSchemaValidator.cs`
 * stay in lock-step — including the EXACT G-P3 round-1 outage payload
 * (property-level `"required": true`), pinned invalid forever.
 *
 * ADR-038 KEEP class: this is the authoring-time regression net for the
 * incident where one bad catalog row 400-failed every text-path turn.
 */

import {
  findFirstSchemaError,
  validateChipTransitionsJson,
  validateJpsPrompt,
  validateMatchConditionsJson,
  validateOnEventBindingsJson,
  validateSchemaForAuthoring,
} from '../schemaValidation';

// ─────────────────────────────────────────────────────────────────────────────
// The exact UAT outage payload (CREATE-TASK@v1 as authored before the fix)
// ─────────────────────────────────────────────────────────────────────────────

const UAT_OUTAGE_PAYLOAD = JSON.stringify({
  type: 'object',
  properties: {
    due_date: {
      type: 'string',
      required: true, // ← the killer: property-level boolean required
      elicitation_prompt: "What's the due date for this task?",
    },
    assign_to: {
      type: 'string',
      required: true,
      elicitation_prompt: 'Should I assign it to you or someone else?',
    },
  },
  required: ['due_date', 'assign_to'],
});

const CORRECTED_CREATE_TASK = JSON.stringify({
  type: 'object',
  properties: {
    fileIds: {
      type: 'array',
      items: { type: 'string' },
      description: 'Optional subset of session file ids.',
    },
    due_date: {
      type: 'string',
      elicitation_prompt: "What's the due date for this task?",
      description: "The task's due date as the user stated it.",
    },
    assign_to: {
      type: 'string',
      elicitation_prompt: 'Should I assign it to you or someone else?',
      description: "Who the task is assigned to — 'me' or a person's name.",
    },
  },
  required: ['due_date', 'assign_to'],
});

describe('findFirstSchemaError — server-tolerance port (OpenAiFunctionSchemaValidator twin)', () => {
  // ── The UAT payload: pinned invalid forever ──
  it('rejects the exact G-P3 round-1 outage payload (property-level "required": true)', () => {
    const error = findFirstSchemaError(UAT_OUTAGE_PAYLOAD);
    expect(error).not.toBeNull();
    expect(error).toContain('$.properties.due_date.required');
    expect(error).toContain('property-level "required": true is not JSON Schema');
  });

  it('accepts the corrected CREATE-TASK@v1 mirror (object-level required array only)', () => {
    expect(findFirstSchemaError(CORRECTED_CREATE_TASK)).toBeNull();
  });

  // ── Tolerance matrix (mirrors observed Azure OpenAI non-strict behavior) ──
  it.each([
    ['null', null],
    ['undefined', undefined],
    ['empty string', ''],
    ['whitespace', '   \n '],
    ['malformed JSON (degrades to default upstream)', '{ not json'],
    ['non-object root (degrades upstream)', '[1,2,3]'],
    ['legacy args wrapper (unknown keyword tolerated)', '{"args":[{"name":"fileIds"}]}'],
    [
      'unknown maker keywords tolerated',
      '{"type":"object","properties":{"x":{"type":"string","elicitation_prompt":"?","ledger_resolution":"y"}}}',
    ],
    ['missing root type tolerated', '{"properties":{"x":{"type":"string"}}}'],
    ['additionalProperties boolean', '{"type":"object","additionalProperties":false}'],
    ['type arrays of legal names', '{"type":"object","properties":{"x":{"type":["string","null"]}}}'],
    [
      'nested anyOf of schema objects',
      '{"type":"object","properties":{"x":{"anyOf":[{"type":"string"},{"type":"null"}]}}}',
    ],
  ])('tolerates %s', (_label, raw) => {
    expect(findFirstSchemaError(raw as string | null | undefined)).toBeNull();
  });

  // ── Rejection matrix ──
  it.each([
    ['root type not object', '{"type":"array","items":{"type":"string"}}', "root 'type' must be 'object'"],
    [
      'array schema without items (valid Draft 2020-12, rejected by OpenAI)',
      '{"type":"object","properties":{"files":{"type":"array"}}}',
      "array schemas must declare 'items'",
    ],
    ['illegal type name', '{"type":"object","properties":{"x":{"type":"text"}}}', 'not a legal JSON-Schema type'],
    ['type as number', '{"type":"object","properties":{"x":{"type":5}}}', 'must be a string or array of strings'],
    ['required as string', '{"type":"object","required":"due_date"}', 'must be an array of property names'],
    ['required array with non-string entries', '{"type":"object","required":[1]}', 'entries must be strings'],
    ['properties as array', '{"type":"object","properties":[]}', '$.properties: must be an object'],
    [
      'property value not a schema object',
      '{"type":"object","properties":{"x":"string"}}',
      'schema must be a JSON object',
    ],
    [
      'enum not an array',
      '{"type":"object","properties":{"x":{"type":"string","enum":"a,b"}}}',
      '.enum: must be an array',
    ],
    [
      'description not a string',
      '{"type":"object","properties":{"x":{"type":"string","description":5}}}',
      '.description: must be a string',
    ],
    [
      'additionalProperties as string',
      '{"type":"object","additionalProperties":"no"}',
      'must be a boolean or schema object',
    ],
    [
      'anyOf not an array',
      '{"type":"object","properties":{"x":{"anyOf":{"type":"string"}}}}',
      'must be an array of schema objects',
    ],
    [
      'items neither object nor array',
      '{"type":"object","properties":{"x":{"type":"array","items":"string"}}}',
      'must be a schema object or array of schema objects',
    ],
    [
      'error deep inside items',
      '{"type":"object","properties":{"x":{"type":"array","items":{"type":"object","required":true}}}}',
      '$.properties.x.items.required',
    ],
  ])('rejects %s', (_label, raw, expectedFragment) => {
    const error = findFirstSchemaError(raw);
    expect(error).not.toBeNull();
    expect(error).toContain(expectedFragment);
  });
});

describe('validateSchemaForAuthoring — strict layer (BA typos are errors, not tolerance)', () => {
  it('accepts empty (both schema columns are optional)', () => {
    expect(validateSchemaForAuthoring('')).toBeNull();
    expect(validateSchemaForAuthoring('   ')).toBeNull();
  });

  it('rejects malformed JSON at authoring time (server tolerates via degrade; the BA must not)', () => {
    expect(validateSchemaForAuthoring('{ not json')).toContain('not valid JSON');
  });

  it('rejects non-object root at authoring time', () => {
    expect(validateSchemaForAuthoring('[1,2]')).toContain('root must be a JSON object');
  });

  it('still rejects the UAT outage payload', () => {
    expect(validateSchemaForAuthoring(UAT_OUTAGE_PAYLOAD)).toContain('property-level "required": true');
  });

  it('accepts the corrected CREATE-TASK@v1 mirror', () => {
    expect(validateSchemaForAuthoring(CORRECTED_CREATE_TASK)).toBeNull();
  });
});

describe('validateJpsPrompt', () => {
  it('accepts flat text prompts (pass-through path)', () => {
    expect(validateJpsPrompt('Summarize the document below.')).toBeNull();
  });

  it('accepts empty', () => {
    expect(validateJpsPrompt('')).toBeNull();
  });

  it('accepts a valid JPS document with an instruction object', () => {
    expect(
      validateJpsPrompt(JSON.stringify({ instruction: { role: 'You are…', task: 'Do…' }, output: { fields: [] } }))
    ).toBeNull();
  });

  it('rejects a {-prefixed body that is not valid JSON', () => {
    expect(validateJpsPrompt('{ "instruction": ')).toContain('not valid JSON');
  });

  it('rejects JPS JSON without an instruction object', () => {
    expect(validateJpsPrompt('{"output":{"fields":[]}}')).toContain("'instruction' object");
  });
});

describe('validateChipTransitionsJson — sprk_chiptransitions pinned shape', () => {
  it('accepts empty (no chips)', () => {
    expect(validateChipTransitionsJson('')).toBeNull();
  });

  it('accepts the full authored shape incl. G-P1/G-P2 optional members', () => {
    const raw = JSON.stringify([
      {
        target_binding_id: '11111111-2222-3333-4444-555555555555',
        chip_label: 'Summarize this document',
        bulk_chip_label: 'Summarize',
        requires_attachments: true,
        prefill_slots: { styleHint: 'executive' },
      },
    ]);
    expect(validateChipTransitionsJson(raw)).toBeNull();
  });

  it.each([
    ['non-array root', '{"chip_label":"x"}', 'must be a JSON array'],
    ['missing target_binding_id', '[{"chip_label":"x"}]', 'target_binding_id'],
    ['missing chip_label', '[{"target_binding_id":"abc"}]', 'chip_label'],
    ['boolean chip entry', '[true]', 'must be an object'],
    [
      'requires_attachments non-boolean',
      '[{"target_binding_id":"a","chip_label":"b","requires_attachments":"yes"}]',
      'must be a boolean',
    ],
    [
      'prefill_slots non-object',
      '[{"target_binding_id":"a","chip_label":"b","prefill_slots":[1]}]',
      'must be an object',
    ],
    ['malformed JSON', '[{', 'not valid JSON'],
  ])('rejects %s', (_label, raw, fragment) => {
    const error = validateChipTransitionsJson(raw);
    expect(error).not.toBeNull();
    expect(error).toContain(fragment);
  });
});

describe('validateOnEventBindingsJson — sprk_oneventbindings pinned shape', () => {
  it('accepts empty and the canonical example', () => {
    expect(validateOnEventBindingsJson('')).toBeNull();
    expect(validateOnEventBindingsJson('[{"event":"document_uploaded","order":2}]')).toBeNull();
  });

  it.each([
    ['non-array root', '{"event":"document_uploaded"}', 'must be a JSON array'],
    ['missing event', '[{"order":1}]', '.event'],
    ['empty event token', '[{"event":"","order":1}]', '.event'],
    ['non-integer order', '[{"event":"document_uploaded","order":1.5}]', '.order'],
    ['missing order', '[{"event":"document_uploaded"}]', '.order'],
  ])('rejects %s', (_label, raw, fragment) => {
    const error = validateOnEventBindingsJson(raw);
    expect(error).not.toBeNull();
    expect(error).toContain(fragment);
  });
});

describe('validateMatchConditionsJson — flat key → string | string[] predicate', () => {
  it('accepts empty, flat strings, and string arrays', () => {
    expect(validateMatchConditionsJson('')).toBeNull();
    expect(validateMatchConditionsJson('{"entityType":"sprk_matter"}')).toBeNull();
    expect(validateMatchConditionsJson('{"entityType":["sprk_matter","sprk_project"]}')).toBeNull();
  });

  it.each([
    ['array root', '["a"]', 'must be a flat JSON object'],
    ['nested object value', '{"a":{"b":"c"}}', 'string or array of strings'],
    ['number value', '{"a":5}', 'string or array of strings'],
    ['mixed array value', '{"a":["x",1]}', 'string or array of strings'],
  ])('rejects %s', (_label, raw, fragment) => {
    const error = validateMatchConditionsJson(raw);
    expect(error).not.toBeNull();
    expect(error).toContain(fragment);
  });
});
