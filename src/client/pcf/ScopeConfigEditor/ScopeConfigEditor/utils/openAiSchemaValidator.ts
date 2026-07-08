/**
 * Authoring-time catalog JSON validation for the record-form surface
 * (task 053 / FR-P4-04).
 *
 * TWIN of (a) the server `OpenAiFunctionSchemaValidator`
 * (`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/OpenAiFunctionSchemaValidator.cs`
 * — the C# source of truth) and (b) the PlaybookBuilder code-page module
 * (`src/client/code-pages/PlaybookBuilder/src/services/schemaValidation.ts`).
 * The PCF cannot import from the code page and hoisting to
 * @spaarke/ui-components was deferred (parallel-work risk during G-P3 UAT) —
 * keep all three in lock-step; the rule matrix is pinned by tests in each
 * package.
 *
 * WHY: the G-P3 UAT round-1 outage — ONE catalog row with property-level
 * `"required": true` inside `sprk_inputschema` 400-failed every text-path
 * turn. This validator blocks that class of error at the record form.
 */

const LEGAL_TYPE_NAMES = ['object', 'string', 'number', 'integer', 'boolean', 'array', 'null'];

const COMPOSITION_KEYWORDS = ['anyOf', 'oneOf', 'allOf'];

type JsonObject = Record<string, unknown>;

function isPlainObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function kindOf(value: unknown): string {
  if (value === null) return 'null';
  if (Array.isArray(value)) return 'array';
  return typeof value;
}

function validateSchemaObject(schema: unknown, path: string): string | null {
  if (!isPlainObject(schema)) {
    return `${path}: schema must be a JSON object (was ${kindOf(schema)})`;
  }

  // ── type ──
  let declaredType: string | null = null;
  if ('type' in schema) {
    const typeEl = schema.type;
    if (typeof typeEl === 'string') {
      if (!LEGAL_TYPE_NAMES.includes(typeEl)) {
        return `${path}.type: '${typeEl}' is not a legal JSON-Schema type`;
      }
      declaredType = typeEl;
    } else if (Array.isArray(typeEl)) {
      for (const t of typeEl) {
        if (typeof t !== 'string' || !LEGAL_TYPE_NAMES.includes(t)) {
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
    const requiredEl = schema.required;
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
  if ('description' in schema && typeof schema.description !== 'string') {
    return `${path}.description: must be a string (was ${kindOf(schema.description)})`;
  }

  // ── enum ──
  if ('enum' in schema && !Array.isArray(schema.enum)) {
    return `${path}.enum: must be an array (was ${kindOf(schema.enum)})`;
  }

  // ── properties ──
  if ('properties' in schema) {
    const propsEl = schema.properties;
    if (!isPlainObject(propsEl)) {
      return `${path}.properties: must be an object (was ${kindOf(propsEl)})`;
    }
    for (const name of Object.keys(propsEl)) {
      const error = validateSchemaObject(propsEl[name], `${path}.properties.${name}`);
      if (error !== null) return error;
    }
  }

  // ── items (+ the OpenAI array-requires-items rule) ──
  const hasItems = 'items' in schema;
  if (declaredType === 'array' && !hasItems) {
    return `${path}: array schemas must declare 'items' (OpenAI rejects array schemas without items)`;
  }
  if (hasItems) {
    const itemsEl = schema.items;
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
    const apEl = schema.additionalProperties;
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

/**
 * Authoring-strict validation for `sprk_inputschema` / `sprk_outputschemajson`:
 * empty is valid; malformed JSON and non-object roots are ERRORS (unlike the
 * server's projection-time degrade tolerance — at authoring time they are
 * typos); then the OpenAI function-parameters subset walk.
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

  const rootType = root.type;
  if (typeof rootType === 'string' && rootType !== 'object') {
    return `root 'type' must be 'object' for function parameters (was '${rootType}')`;
  }

  return validateSchemaObject(root, '$');
}

/** `sprk_chiptransitions` pinned shape (Binding.cs ChipTransition). Empty is valid. */
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
    const chip: unknown = parsed[i];
    if (!isPlainObject(chip)) {
      return `[${i}]: each chip transition must be an object (was ${kindOf(chip)})`;
    }
    if (typeof chip.target_binding_id !== 'string' || chip.target_binding_id.trim() === '') {
      return `[${i}].target_binding_id: required non-empty string (the Click path resolves it)`;
    }
    if (typeof chip.chip_label !== 'string' || chip.chip_label.trim() === '') {
      return `[${i}].chip_label: required non-empty string (rendered to the user)`;
    }
    if ('bulk_chip_label' in chip && typeof chip.bulk_chip_label !== 'string') {
      return `[${i}].bulk_chip_label: must be a string (was ${kindOf(chip.bulk_chip_label)})`;
    }
    if ('requires_attachments' in chip && typeof chip.requires_attachments !== 'boolean') {
      return `[${i}].requires_attachments: must be a boolean (was ${kindOf(chip.requires_attachments)})`;
    }
    if ('prefill_slots' in chip && !isPlainObject(chip.prefill_slots)) {
      return `[${i}].prefill_slots: must be an object of capability args (was ${kindOf(chip.prefill_slots)})`;
    }
  }

  return null;
}

/** `sprk_oneventbindings` pinned shape: `[{"event","order"}]`. Empty is valid. */
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
    const entry: unknown = parsed[i];
    if (!isPlainObject(entry)) {
      return `[${i}]: each event membership must be an object (was ${kindOf(entry)})`;
    }
    if (typeof entry.event !== 'string' || entry.event.trim() === '') {
      return `[${i}].event: required non-empty platform event token (e.g. document_uploaded)`;
    }
    if (typeof entry.order !== 'number' || !Number.isInteger(entry.order)) {
      return `[${i}].order: required integer (lower runs first)`;
    }
  }

  return null;
}

/** `sprk_matchconditions`: flat key → string | string[]. Empty is valid (always matches). */
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

  for (const key of Object.keys(parsed)) {
    const value = parsed[key];
    const isString = typeof value === 'string';
    const isStringArray = Array.isArray(value) && value.every(v => typeof v === 'string');
    if (!isString && !isStringArray) {
      return `'${key}': values must be a string or array of strings (was ${kindOf(value)})`;
    }
  }

  return null;
}
