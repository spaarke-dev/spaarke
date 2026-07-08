/**
 * Client-side authoring-time schema validation (FR-P4-04).
 *
 * TWIN of the server-side `OpenAiFunctionSchemaValidator`
 * (`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/OpenAiFunctionSchemaValidator.cs`,
 * the H1 fix of the G-P3 UAT round-1 incident, 2026-07-07). Keep the two rule
 * sets in lock-step — the C# validator is the source of truth; the contract
 * tests in `__tests__/schemaValidation.test.ts` pin the shared rule matrix
 * including the exact UAT outage payload.
 *
 * WHY THIS EXISTS ON THE CLIENT: the CREATE-TASK@v1 Action row carried
 * `"required": true` INSIDE a property definition (invalid JSON Schema).
 * Azure OpenAI rejects the ENTIRE tools request when ANY one projected schema
 * is invalid — one bad catalog row took down every text-path turn. The server
 * now excludes invalid tools at projection time (fail-safe); THIS module keeps
 * the bad row from ever being authored (fail-early). A BA must never be able
 * to save the outage schema.
 *
 * Rules (server-mirrored):
 *   - `required` must be an array of strings, ANYWHERE in the schema
 *     (property-level `"required": true|false` is BANNED — the UAT killer).
 *   - `type` must be a legal type name (or array of legal names).
 *   - Root `type`, when declared, must be `object`.
 *   - `properties` must be an object; each property value a schema object.
 *   - `type: array` schemas MUST declare `items` (OpenAI rejects otherwise);
 *     `items` must be a schema object or array of schema objects.
 *   - `enum` must be an array; `description` a string; `additionalProperties`
 *     boolean-or-object; `anyOf`/`oneOf`/`allOf` arrays of schema objects.
 *   - Unknown keywords (`elicitation_prompt`, `ledger_resolution`, legacy
 *     `args`) are TOLERATED — they carry maker metadata; OpenAI ignores them.
 *
 * AUTHORING STRICTNESS (this module only — deliberately stricter than the
 * server's projection-time tolerance): the server returns null for
 * null/whitespace/malformed-JSON/non-object-root because projections degrade
 * those to a safe default. At authoring time those are BA typos, so
 * `validateSchemaForAuthoring` reports them as errors. `findFirstSchemaError`
 * remains the exact server-tolerance port for the shared rule matrix.
 */

const LEGAL_TYPE_NAMES = new Set(['object', 'string', 'number', 'integer', 'boolean', 'array', 'null']);

const COMPOSITION_KEYWORDS = ['anyOf', 'oneOf', 'allOf'] as const;

type JsonObject = Record<string, unknown>;

function isPlainObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function kindOf(value: unknown): string {
  if (value === null) return 'null';
  if (Array.isArray(value)) return 'array';
  return typeof value;
}

// ─────────────────────────────────────────────────────────────────────────────
// Server-tolerance port (mirror of OpenAiFunctionSchemaValidator.FindFirstError)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Exact behavioral port of the C# `FindFirstError`: returns `null` when the
 * text is safe to project (including the degrade-to-default cases: null /
 * whitespace / malformed JSON / non-object root); otherwise the FIRST
 * validation error (keyword path + reason).
 */
export function findFirstSchemaError(rawSchemaJson: string | null | undefined): string | null {
  if (rawSchemaJson === null || rawSchemaJson === undefined || rawSchemaJson.trim() === '') {
    return null; // Projections substitute their default schema.
  }

  let root: unknown;
  try {
    root = JSON.parse(rawSchemaJson);
  } catch {
    return null; // Malformed JSON degrades to the default schema upstream.
  }

  if (!isPlainObject(root)) {
    return null; // Non-object root degrades to the default schema upstream.
  }

  // Root-specific rule: a declared root type must be object.
  const rootType = root['type'];
  if (typeof rootType === 'string' && rootType !== 'object') {
    return `root 'type' must be 'object' for function parameters (was '${rootType}')`;
  }

  return validateSchemaObject(root, '$');
}

function validateSchemaObject(schema: unknown, path: string): string | null {
  if (!isPlainObject(schema)) {
    return `${path}: schema must be a JSON object (was ${kindOf(schema)})`;
  }

  // ── type ──
  let declaredType: string | null = null;
  if ('type' in schema) {
    const typeEl = schema['type'];
    if (typeof typeEl === 'string') {
      if (!LEGAL_TYPE_NAMES.has(typeEl)) {
        return `${path}.type: '${typeEl}' is not a legal JSON-Schema type`;
      }
      declaredType = typeEl;
    } else if (Array.isArray(typeEl)) {
      for (const t of typeEl) {
        if (typeof t !== 'string' || !LEGAL_TYPE_NAMES.has(t)) {
          return `${path}.type: type arrays must contain legal type-name strings`;
        }
        if (t === 'array') declaredType = 'array';
      }
    } else {
      return `${path}.type: must be a string or array of strings (was ${kindOf(typeEl)})`;
    }
  }

  // ── required — THE UAT killer ──
  if ('required' in schema) {
    const requiredEl = schema['required'];
    if (!Array.isArray(requiredEl)) {
      return (
        `${path}.required: must be an array of property names ` +
        `(was ${kindOf(requiredEl)} — property-level "required": true is not JSON Schema; ` +
        'list the property in the OBJECT-level required array instead)'
      );
    }
    for (const item of requiredEl) {
      if (typeof item !== 'string') {
        return `${path}.required: entries must be strings (found ${kindOf(item)})`;
      }
    }
  }

  // ── description ──
  if ('description' in schema && typeof schema['description'] !== 'string') {
    return `${path}.description: must be a string (was ${kindOf(schema['description'])})`;
  }

  // ── enum ──
  if ('enum' in schema && !Array.isArray(schema['enum'])) {
    return `${path}.enum: must be an array (was ${kindOf(schema['enum'])})`;
  }

  // ── properties ──
  if ('properties' in schema) {
    const propsEl = schema['properties'];
    if (!isPlainObject(propsEl)) {
      return `${path}.properties: must be an object (was ${kindOf(propsEl)})`;
    }
    for (const [name, value] of Object.entries(propsEl)) {
      const error = validateSchemaObject(value, `${path}.properties.${name}`);
      if (error !== null) return error;
    }
  }

  // ── items (+ the OpenAI array-requires-items rule) ──
  const hasItems = 'items' in schema;
  if (declaredType === 'array' && !hasItems) {
    return `${path}: array schemas must declare 'items' (OpenAI rejects array schemas without items)`;
  }
  if (hasItems) {
    const itemsEl = schema['items'];
    if (isPlainObject(itemsEl)) {
      const itemError = validateSchemaObject(itemsEl, `${path}.items`);
      if (itemError !== null) return itemError;
    } else if (Array.isArray(itemsEl)) {
      for (let i = 0; i < itemsEl.length; i++) {
        const tupleError = validateSchemaObject(itemsEl[i], `${path}.items[${i}]`);
        if (tupleError !== null) return tupleError;
      }
    } else {
      return `${path}.items: must be a schema object or array of schema objects (was ${kindOf(itemsEl)})`;
    }
  }

  // ── additionalProperties ──
  if ('additionalProperties' in schema) {
    const apEl = schema['additionalProperties'];
    if (typeof apEl !== 'boolean') {
      if (isPlainObject(apEl)) {
        const apError = validateSchemaObject(apEl, `${path}.additionalProperties`);
        if (apError !== null) return apError;
      } else {
        return `${path}.additionalProperties: must be a boolean or schema object (was ${kindOf(apEl)})`;
      }
    }
  }

  // ── anyOf / oneOf / allOf ──
  for (const keyword of COMPOSITION_KEYWORDS) {
    if (!(keyword in schema)) continue;
    const compEl = schema[keyword];
    if (!Array.isArray(compEl)) {
      return `${path}.${keyword}: must be an array of schema objects (was ${kindOf(compEl)})`;
    }
    for (let j = 0; j < compEl.length; j++) {
      const branchError = validateSchemaObject(compEl[j], `${path}.${keyword}[${j}]`);
      if (branchError !== null) return branchError;
    }
  }

  return null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Authoring-time strict layer
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Authoring-time validation for `sprk_inputschema` / `sprk_outputschemajson`:
 * the server-tolerance rules PLUS strictness for the degrade cases (at
 * authoring time, malformed JSON / non-object root are typos, not tolerable).
 * Empty is valid (both columns are optional on the table).
 */
export function validateSchemaForAuthoring(rawSchemaJson: string): string | null {
  if (rawSchemaJson.trim() === '') return null;

  let root: unknown;
  try {
    root = JSON.parse(rawSchemaJson);
  } catch (err) {
    return `not valid JSON: ${err instanceof Error ? err.message : 'parse error'}`;
  }

  if (!isPlainObject(root)) {
    return `root must be a JSON object (was ${kindOf(root)})`;
  }

  const rootType = root['type'];
  if (typeof rootType === 'string' && rootType !== 'object') {
    return `root 'type' must be 'object' for function parameters (was '${rootType}')`;
  }

  return validateSchemaObject(root, '$');
}

// ─────────────────────────────────────────────────────────────────────────────
// JPS prompt sanity (sprk_systemprompt)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * `sprk_systemprompt` carries either flat prompt text (passes through) or a
 * JPS document (starts with `{` — rendered by PromptSchemaRenderer). A JPS
 * body that is not valid JSON silently falls back to JSON-as-plain-text at
 * runtime, so it is an authoring error here.
 */
export function validateJpsPrompt(rawPrompt: string): string | null {
  const trimmed = rawPrompt.trim();
  if (trimmed === '' || !trimmed.startsWith('{')) return null;

  let parsed: unknown;
  try {
    parsed = JSON.parse(trimmed);
  } catch (err) {
    return `prompt starts with '{' (JPS) but is not valid JSON: ${err instanceof Error ? err.message : 'parse error'}`;
  }

  if (!isPlainObject(parsed) || !isPlainObject(parsed['instruction'])) {
    return "JPS prompt must carry an 'instruction' object (role/task/constraints) — see notes/jps/*.jps.json examples";
  }

  return null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Binding JSON columns (pinned shapes per Binding.cs)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * `sprk_chiptransitions`: array of
 * `{ target_binding_id, chip_label, bulk_chip_label?, requires_attachments?, prefill_slots? }`.
 * Empty is valid (no chips).
 */
export function validateChipTransitionsJson(raw: string): string | null {
  if (raw.trim() === '') return null;

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch (err) {
    return `not valid JSON: ${err instanceof Error ? err.message : 'parse error'}`;
  }

  if (!Array.isArray(parsed)) {
    return `must be a JSON array of chip transitions (was ${kindOf(parsed)})`;
  }

  for (let i = 0; i < parsed.length; i++) {
    const chip = parsed[i];
    if (!isPlainObject(chip)) {
      return `[${i}]: each chip transition must be an object (was ${kindOf(chip)})`;
    }
    if (typeof chip['target_binding_id'] !== 'string' || chip['target_binding_id'].trim() === '') {
      return `[${i}].target_binding_id: required non-empty string (the Click path resolves it)`;
    }
    if (typeof chip['chip_label'] !== 'string' || chip['chip_label'].trim() === '') {
      return `[${i}].chip_label: required non-empty string (rendered to the user)`;
    }
    if ('bulk_chip_label' in chip && typeof chip['bulk_chip_label'] !== 'string') {
      return `[${i}].bulk_chip_label: must be a string (was ${kindOf(chip['bulk_chip_label'])})`;
    }
    if ('requires_attachments' in chip && typeof chip['requires_attachments'] !== 'boolean') {
      return `[${i}].requires_attachments: must be a boolean (was ${kindOf(chip['requires_attachments'])})`;
    }
    if ('prefill_slots' in chip && !isPlainObject(chip['prefill_slots'])) {
      return `[${i}].prefill_slots: must be an object of capability args (was ${kindOf(chip['prefill_slots'])})`;
    }
  }

  return null;
}

/**
 * `sprk_oneventbindings`: array of `{ event, order }`, e.g.
 * `[{"event":"document_uploaded","order":2}]`. Empty is valid.
 */
export function validateOnEventBindingsJson(raw: string): string | null {
  if (raw.trim() === '') return null;

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch (err) {
    return `not valid JSON: ${err instanceof Error ? err.message : 'parse error'}`;
  }

  if (!Array.isArray(parsed)) {
    return `must be a JSON array of event memberships (was ${kindOf(parsed)})`;
  }

  for (let i = 0; i < parsed.length; i++) {
    const entry = parsed[i];
    if (!isPlainObject(entry)) {
      return `[${i}]: each event membership must be an object (was ${kindOf(entry)})`;
    }
    if (typeof entry['event'] !== 'string' || entry['event'].trim() === '') {
      return `[${i}].event: required non-empty platform event token (e.g. document_uploaded)`;
    }
    if (typeof entry['order'] !== 'number' || !Number.isInteger(entry['order'])) {
      return `[${i}].order: required integer (lower runs first)`;
    }
  }

  return null;
}

/**
 * `sprk_matchconditions`: flat object of key → string | string[]. Empty is
 * valid (always matches).
 */
export function validateMatchConditionsJson(raw: string): string | null {
  if (raw.trim() === '') return null;

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch (err) {
    return `not valid JSON: ${err instanceof Error ? err.message : 'parse error'}`;
  }

  if (!isPlainObject(parsed)) {
    return `must be a flat JSON object of key → string | string[] (was ${kindOf(parsed)})`;
  }

  for (const [key, value] of Object.entries(parsed)) {
    const isString = typeof value === 'string';
    const isStringArray = Array.isArray(value) && value.every(v => typeof v === 'string');
    if (!isString && !isStringArray) {
      return `'${key}': values must be a string or array of strings (was ${kindOf(value)})`;
    }
  }

  return null;
}
