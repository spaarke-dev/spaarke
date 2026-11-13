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
        // The order the values were given in (a published body's order is kept, new picks follow), no duplicates (084c fix round 1, F1-3).
        const picked = Array.isArray(v) ? Array.from(new Set(v.filter(x => known.includes(x)))) : [];
        if (picked.length === 0) {
          if (needed) errors[f.key] = `Pick at least one for ${f.label.toLowerCase()}.`;
        } else body[f.key] = picked;
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

const isRecord = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);

/**
 * Structural equality of two rule bodies (key order does not matter). With the form `def`, a multi-choice field is a SET: its values compare
 * whatever their order (084c fix round 1, F1-3: an unchanged Duplicate whose published order is not the vocabulary's is not a change).
 */
export function sameBody(a: unknown, b: unknown, def?: RuleFormDef): boolean {
  if (def && isRecord(a) && isRecord(b)) {
    const sets = new Set(def.fields.filter(f => f.kind === 'multi').map(f => f.key));
    const asSets = (o: Record<string, unknown>) =>
      Object.fromEntries(Object.entries(o).map(([k, v]) => [k, sets.has(k) && Array.isArray(v) ? Array.from(new Set(v.map(String))).sort() : v]));
    return sameBody(asSets(a), asSets(b));
  }
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

/** What a field currently holds, as the same kind of text `fieldText` gives for a body, so the two can be compared. */
function valueText(f: FormField, values: FormValues): string {
  const v = values[f.key];
  if (f.kind === 'codes') return typeof v === 'string' ? v.split(/[\s,;]+/).filter(Boolean).join(', ') : '';
  if (f.kind === 'choice') return f.options?.find(o => o.value === v)?.label ?? '';
  return typeof v === 'string' ? v.trim() : '';
}

/**
 * Whether a field's input differs from what a body gives for it (the form's "Published rule: X" hint). A multi-choice field compares as a
 * set (084c fix round 1, F1-3): the same picks in another order are the same value.
 */
export function fieldDiffers(f: FormField, values: FormValues, body: Record<string, unknown>): boolean {
  if (f.kind === 'multi') {
    const known = new Set((f.options ?? []).map(o => o.value));
    const mine = new Set((Array.isArray(values[f.key]) ? (values[f.key] as string[]) : []).filter(x => known.has(x)));
    const theirs = new Set(strings(body[f.key]));
    return mine.size !== theirs.size || [...mine].some(x => !theirs.has(x));
  }
  return fieldText(f, body) !== valueText(f, values);
}

// ─────────────────────────────────────────────────────────────────────────────
// What a change of one record may not do (084c fix round 1, items 6 and 9)
// ─────────────────────────────────────────────────────────────────────────────

/** Item 9 (D-39): why AddedFee's fee kind (and, for `other`, its fee codes) cannot be changed for one record. */
export const FEE_KIND_LOCKED =
  'Changing the fee kind makes a different rule; exclude this one and ask an admin to publish the other.';

/**
 * Item 9: the fields a change for one record may not edit, given the body the form starts from. AddedFee's precedence key is the rule type
 * plus `feeKind`, and for `other` the fee-code set too (D-39, `BillingRuleDefinition.PrecedenceKey`); a record rule takes its base rule's
 * slot, so a change must not move it to another key. The change route does not check this itself, so the form keeps those fields read-only.
 */
export function lockedFields(def: RuleFormDef, start: Record<string, unknown> | null): string[] {
  if (def.ruleType !== 'AddedFee') return [];
  return start?.feeKind === 'other' ? ['feeKind', 'feeCodes'] : ['feeKind'];
}

/** Item 6: the server's reason (PolicyVersionValidator, at save) for a Rejecting Duplicate that compares lines only. */
export const DUPLICATE_REJECT_NEEDS_INVOICE_NUMBER =
  'This rule rejects invoices, and a Duplicate rule whose fields compared do not include Firm invoice number compares lines only: a duplicate line is flagged, never rejected. Keep Firm invoice number to reject a duplicate invoice.';

/**
 * Item 6: what the server will refuse in this change because of the rule's consequence, by field, before anything is sent. A record rule
 * keeps its rule's consequence (the change route copies it), so a Duplicate that Rejects must keep `firmInvoiceNumber` in `matchFields`.
 */
export function consequenceProblems(def: RuleFormDef, values: FormValues, consequence: string | null | undefined): Record<string, string> {
  if (def.ruleType !== 'Duplicate' || consequence !== 'Reject') return {};
  const picked = Array.isArray(values.matchFields) ? values.matchFields : [];
  return picked.includes('firmInvoiceNumber') ? {} : { matchFields: DUPLICATE_REJECT_NEEDS_INVOICE_NUMBER };
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
