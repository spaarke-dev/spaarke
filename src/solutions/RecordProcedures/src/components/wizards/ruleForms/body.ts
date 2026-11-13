/**
 * The rule body a form describes, and the other way round (ebilling-r1 task 084c). Pure.
 *
 * The bounds checked here are the schema's, as a convenience: the server validates the whole body again (RuleBodySchemaValidator, then
 * PolicyVersionValidator) and its answer is the one that counts. `serverFieldErrors` puts that answer back on the fields it names.
 */

import type { FieldError } from '../../../services/overridesApi';
import type { FormField, FormValues, RuleFormDef } from './types';

/** The code pattern of the schemas' `$defs.code` (pinned against them by the agreement test). */
export const CODE_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$/;
export const MAX_CODES = 500;

/** Whether a field applies, given the other values (a `showWhen` field applies only while its condition holds). */
export const isShown = (field: FormField, values: FormValues): boolean => !field.showWhen || values[field.showWhen.key] === field.showWhen.equals;

/** Split a code list typed by hand: commas, semicolons and whitespace separate; blanks are dropped. */
export const parseCodes = (text: string): string[] =>
  text
    .split(/[\s,;]+/)
    .map(c => c.trim())
    .filter(c => c.length > 0);

const strings = (raw: unknown): string[] => (Array.isArray(raw) ? raw.filter((x): x is string => typeof x === 'string') : []);

/** The inputs of a form, seeded from a rule body (null: every input empty). Members the form does not know are dropped. */
export function toFormValues(def: RuleFormDef, body: Record<string, unknown> | null): FormValues {
  const values: FormValues = {};
  for (const f of def.fields) {
    const raw = body?.[f.key];
    switch (f.kind) {
      case 'number':
        values[f.key] = typeof raw === 'number' ? String(raw) : '';
        break;
      case 'choice':
        values[f.key] = typeof raw === 'string' ? raw : '';
        break;
      case 'multi':
        values[f.key] = strings(raw);
        break;
      case 'codes':
        values[f.key] = strings(raw).join(', ');
        break;
    }
  }
  return values;
}

export type BuildResult = { ok: true; body: Record<string, unknown> } | { ok: false; errors: Record<string, string> };

/**
 * The rule body the inputs describe, or the problems with them keyed by field. Only fields that apply (`isShown`) are read, so a value
 * left in a hidden field never reaches the body.
 */
export function buildBody(def: RuleFormDef, values: FormValues): BuildResult {
  const body: Record<string, unknown> = { type: def.ruleType };
  const errors: Record<string, string> = {};
  const exactlyOne = new Set(def.exactlyOneOf ?? []);

  for (const f of def.fields) {
    if (!isShown(f, values)) continue;
    // A field that only applies under a condition is needed whenever it applies, unless the form says "one of these".
    const needed = f.required === true || (f.showWhen !== undefined && !exactlyOne.has(f.key));
    const v = values[f.key];
    switch (f.kind) {
      case 'number': {
        const text = typeof v === 'string' ? v.trim() : '';
        if (text === '') {
          if (needed) errors[f.key] = `${f.label} is required.`;
          break;
        }
        const n = Number(text);
        if (!Number.isFinite(n)) errors[f.key] = `${f.label} must be a number.`;
        else if (f.integer && !Number.isInteger(n)) errors[f.key] = `${f.label} must be a whole number.`;
        else if (f.min !== undefined && (f.minExclusive ? n <= f.min : n < f.min)) errors[f.key] = `${f.label} must be ${f.minExclusive ? 'more than' : 'at least'} ${f.min}.`;
        else if (f.max !== undefined && (f.maxExclusive ? n >= f.max : n > f.max)) errors[f.key] = `${f.label} must be ${f.maxExclusive ? 'less than' : 'at most'} ${f.max}.`;
        else body[f.key] = n;
        break;
      }
      case 'choice': {
        const text = typeof v === 'string' ? v : '';
        const option = f.options?.find(o => o.value === text);
        if (!option) {
          if (needed) errors[f.key] = `Choose ${f.label.toLowerCase()}.`;
        } else if (option.refused) errors[f.key] = option.refused;
        else body[f.key] = text;
        break;
      }
      case 'multi': {
        const known = (f.options ?? []).map(o => o.value);
        const picked = Array.isArray(v) ? v.filter(x => known.includes(x)) : [];
        if (picked.length === 0) {
          if (needed) errors[f.key] = `Pick at least one for ${f.label.toLowerCase()}.`;
        } else body[f.key] = known.filter(o => picked.includes(o)); // vocabulary order, no duplicates
        break;
      }
      case 'codes': {
        const codes = parseCodes(typeof v === 'string' ? v : '');
        const unique = Array.from(new Set(codes));
        if (unique.length === 0) {
          if (needed) errors[f.key] = `${f.label}: list at least one code.`;
          break;
        }
        const bad = unique.find(c => !CODE_PATTERN.test(c));
        if (bad) errors[f.key] = `${f.label}: "${bad}" is not a valid code (letters, digits, . _ - ; up to 20 characters).`;
        else if (unique.length > MAX_CODES) errors[f.key] = `${f.label}: at most ${MAX_CODES} codes.`;
        else body[f.key] = unique;
        break;
      }
    }
  }

  const any = def.atLeastOneOf;
  if (any && !any.some(k => k in body) && !any.some(k => k in errors)) {
    errors[any[0]] = `Give at least one of: ${labelsOf(def, any)}.`;
  }

  const one = def.exactlyOneOf;
  if (one && one.every(k => def.fields.some(f => f.key === k && isShown(f, values)))) {
    const given = one.filter(k => k in body);
    if (given.length === 0 && !one.some(k => k in errors)) errors[one[0]] = `Give one of: ${labelsOf(def, one)}.`;
    if (given.length > 1) for (const k of given) errors[k] = `Give only one of: ${labelsOf(def, one)}.`;
  }

  return Object.keys(errors).length > 0 ? { ok: false, errors } : { ok: true, body };
}

const labelsOf = (def: RuleFormDef, keys: readonly string[]): string => keys.map(k => def.fields.find(f => f.key === k)?.label ?? k).join(' or ');

/** Structural equality of two rule bodies (key order does not matter). */
export function sameBody(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  if (typeof a !== typeof b || a === null || b === null || typeof a !== 'object') return false;
  if (Array.isArray(a) || Array.isArray(b)) return Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((x, i) => sameBody(x, b[i]));
  const ak = Object.keys(a as object);
  const bk = Object.keys(b as object);
  return ak.length === bk.length && ak.every(k => k in (b as object) && sameBody((a as Record<string, unknown>)[k], (b as Record<string, unknown>)[k]));
}

/** One field's value in words, from a rule body (empty string when the body does not give it). */
export function fieldText(f: FormField, body: Record<string, unknown>): string {
  const raw = body[f.key];
  if (raw === undefined || raw === null) return '';
  const label = (v: unknown) => f.options?.find(o => o.value === v)?.label ?? String(v);
  return Array.isArray(raw) ? raw.map(label).join(', ') : label(raw);
}

/** The values of a rule body in one line, for the Impact and Confirm steps: "Most hours per day 12". */
export function describeBody(def: RuleFormDef, body: Record<string, unknown>): string {
  return def.fields
    .map(f => ({ f, text: fieldText(f, body) }))
    .filter(x => x.text !== '')
    .map(x => `${x.f.label} ${x.text}`)
    .join('; ');
}

// ─────────────────────────────────────────────────────────────────────────────
// The server's refusal, on the fields
// ─────────────────────────────────────────────────────────────────────────────

export interface ServerFieldErrors {
  /** Messages on the fields of the form, by the body property they name. */
  byField: Record<string, string[]>;
  /** The firm field's messages (the request's `firmId`). */
  firm: string[];
  /** Everything the server said that names no field of this form (a template, a consequence, the reason, the body as a whole). */
  general: string[];
}

/**
 * Puts a 400 refusal's `errors` back on the form. A body error is worded "<JSON pointer>: <message>" (RuleBodySchemaValidator), so
 * "/maxHoursPerDay: ..." belongs to `maxHoursPerDay`, and "Required properties [a, b] are not present" to both; a message that names no
 * field of the form is general. Nothing the server said is dropped.
 */
export function serverFieldErrors(def: RuleFormDef, errors: readonly FieldError[]): ServerFieldErrors {
  const out: ServerFieldErrors = { byField: {}, firm: [], general: [] };
  const known = new Set(def.fields.map(f => f.key));
  const put = (key: string, message: string) => (out.byField[key] ??= []).push(message);
  for (const e of errors) {
    if (e.field === 'firmId') {
      out.firm.push(e.message);
      continue;
    }
    if (e.field !== 'ruleBody') {
      out.general.push(e.message);
      continue;
    }
    const pointer = /^\/([^/:\s]+)(?:\/[^:]*)?:\s*(.*)$/s.exec(e.message);
    if (pointer && known.has(pointer[1])) {
      put(pointer[1], pointer[2] || e.message);
      continue;
    }
    const required = /Required properties \[([^\]]*)\]/i.exec(e.message);
    const named = required ? required[1].split(',').map(s => s.trim().replace(/^"|"$/g, '')).filter(k => known.has(k)) : [];
    if (named.length > 0) {
      for (const k of named) put(k, e.message.replace(/^:\s*/, ''));
      continue;
    }
    out.general.push(e.message.replace(/^:\s*/, ''));
  }
  return out;
}
