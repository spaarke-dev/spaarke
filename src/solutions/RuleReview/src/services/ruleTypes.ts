/**
 * The typed parameter inputs of the eleven billing rule types (ebilling-r1 task 077, FR-28).
 *
 * A rule is a closed typed template with JSON parameters (D-27): the server's JSON schemas
 * (`Services/Signals/Schemas/billing-*.schema.json`, task 070) are the authority and `PolicyVersionValidator` refuses anything
 * outside them at accept, edit and publish. This catalogue only decides WHICH INPUT to show for each parameter (a bounded number,
 * a set of closed-vocabulary choices, a list of codes) and gives quick feedback on the same bounds, so an admin is not sent to the
 * server to learn that 25 is not a number of hours in a day. A bound that drifts from the schema is caught by the server (the 400
 * is shown); a rule type this page does not know is shown read-only (its parameters as JSON) and can still have its consequence,
 * wording and clause edited.
 *
 * Composed of ordinary Fluent v9 inputs by the editor component; no shared-library component is added (task escalation trigger,
 * resolved in notes/077-rule-review-page.md).
 */

export interface NumberParam {
  kind: 'number';
  key: string;
  label: string;
  help: string;
  integer?: boolean;
  /** Smallest allowed value; with `minExclusive` the value must be greater than it. */
  min: number;
  minExclusive?: boolean;
  max: number;
  required: boolean;
}

export interface MultiChoiceParam {
  kind: 'multi';
  key: string;
  label: string;
  help: string;
  options: readonly string[];
}

export interface ChoiceParam {
  kind: 'choice';
  key: string;
  label: string;
  help: string;
  options: readonly string[];
}

export interface CodeListParam {
  kind: 'codes';
  key: string;
  label: string;
  help: string;
  required: boolean;
}

export type ParamDef = NumberParam | MultiChoiceParam | ChoiceParam | CodeListParam;

export interface RuleTypeDef {
  type: string;
  label: string;
  params: readonly ParamDef[];
  /** For a type whose parameters are optional one by one: at least one of these keys is needed. */
  atLeastOneOf?: readonly string[];
}

const CODE_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$/;
export const MAX_CODES = 500;

export const RULE_TYPES: readonly RuleTypeDef[] = [
  { type: 'ApprovedTimekeeper', label: 'Approved timekeeper', params: [] },
  {
    type: 'ApprovedRate',
    label: 'Approved rate',
    params: [
      {
        kind: 'number', key: 'tolerancePercent', label: 'Tolerance (%)', min: 0, max: 100, required: true,
        help: 'Percent over the approved rate still accepted, 0 to 100 (0 = the approved rate exactly).',
      },
    ],
  },
  {
    type: 'TimeIncrement',
    label: 'Time increment',
    params: [
      {
        kind: 'number', key: 'incrementHours', label: 'Increment (hours)', min: 0, minExclusive: true, max: 1, required: true,
        help: 'The billing increment in hours, for example 0.1 (six minutes). More than 0, at most 1.',
      },
    ],
  },
  {
    type: 'DailyHoursCap',
    label: 'Daily hours cap',
    params: [
      {
        kind: 'number', key: 'maxHoursPerDay', label: 'Most hours per day', min: 0, minExclusive: true, max: 24, required: true,
        help: 'The most hours one timekeeper may bill on one day. More than 0, at most 24.',
      },
    ],
  },
  {
    type: 'NonBillableActivity',
    label: 'Non-billable activity',
    params: [
      {
        kind: 'codes', key: 'codes', label: 'Non-billable codes', required: true,
        help: 'Activity or task codes, separated by commas or spaces (for example L110, L120).',
      },
    ],
  },
  {
    type: 'ExpenseRule',
    label: 'Expense rule',
    atLeastOneOf: ['nonReimbursableCodes', 'maxAmountPerItem'],
    params: [
      {
        kind: 'codes', key: 'nonReimbursableCodes', label: 'Non-reimbursable expense codes', required: false,
        help: 'Expense codes that are not reimbursable, separated by commas or spaces. Leave empty to use only a cap.',
      },
      {
        kind: 'number', key: 'maxAmountPerItem', label: 'Most per expense item', min: 0, minExclusive: true, max: 1_000_000_000, required: false,
        help: 'The most one expense item may be, in the invoice currency. Leave empty to use only the codes.',
      },
    ],
  },
  {
    type: 'InvoiceAge',
    label: 'Invoice age',
    params: [
      {
        kind: 'number', key: 'maxAgeDays', label: 'Most days before the invoice date', integer: true, min: 1, max: 3650, required: true,
        help: 'A line dated more days than this before the invoice date is flagged. 1 to 3650.',
      },
    ],
  },
  {
    type: 'CoreRecordWindow',
    label: 'Core record window',
    params: [
      {
        kind: 'number', key: 'graceDays', label: 'Grace days', integer: true, min: 0, max: 365, required: true,
        help: 'Days allowed outside the core record open and close dates. 0 to 365.',
      },
    ],
  },
  {
    type: 'Duplicate',
    label: 'Duplicate',
    params: [
      {
        kind: 'multi', key: 'matchFields', label: 'Fields compared',
        options: ['firmInvoiceNumber', 'timekeeper', 'lineDate', 'hours', 'narrative', 'amount'],
        help: 'firmInvoiceNumber compares invoices; the others compare lines. Pick at least one.',
      },
    ],
  },
  {
    type: 'RequiredData',
    label: 'Required data',
    params: [
      {
        kind: 'multi', key: 'fields', label: 'Required fields',
        options: [
          'firmInvoiceNumber', 'invoiceDate', 'billingPeriodStart', 'billingPeriodEnd', 'timekeeper', 'lineDate',
          'narrative', 'taskCode', 'activityCode', 'expenseCode',
        ],
        help: 'The header or line data that must be present. Pick at least one.',
      },
    ],
  },
  {
    type: 'AiObservation',
    label: 'AI observation',
    params: [
      {
        kind: 'choice', key: 'observation', label: 'Observation', options: ['blockBilling', 'vagueDescription'],
        help: 'The kind of observation the AI looks for.',
      },
      {
        kind: 'number', key: 'minConfidence', label: 'Minimum confidence', min: 0, minExclusive: true, max: 1, required: true,
        help: 'Confidence at or above which the observation counts. More than 0, at most 1. An AI observation can only flag: it never rejects.',
      },
    ],
  },
];

export const CONSEQUENCES = ['Reject', 'Flag', 'SuggestAdjustment'] as const;

export const CONSEQUENCE_LABELS: Record<string, string> = {
  Reject: 'Reject the invoice',
  Flag: 'Flag for review',
  SuggestAdjustment: 'Suggest an adjustment',
};

export function ruleTypeDef(type: string | null | undefined): RuleTypeDef | undefined {
  return RULE_TYPES.find(t => t.type === type);
}

export function ruleTypeLabel(type: string | null | undefined): string {
  return ruleTypeDef(type)?.label ?? type ?? 'Unknown type';
}

/** The text of a parameter input: a number as typed, a list as an array, a code list as one comma-separated line. */
export type ParamValues = Record<string, string | string[]>;

/** Seed the inputs of a rule's parameters from its body. */
export function toParamValues(def: RuleTypeDef, body: Record<string, unknown> | null): ParamValues {
  const values: ParamValues = {};
  for (const p of def.params) {
    const raw = body?.[p.key];
    switch (p.kind) {
      case 'number':
        values[p.key] = typeof raw === 'number' ? String(raw) : '';
        break;
      case 'choice':
        values[p.key] = typeof raw === 'string' ? raw : '';
        break;
      case 'multi':
        values[p.key] = Array.isArray(raw) ? raw.filter((x): x is string => typeof x === 'string') : [];
        break;
      case 'codes':
        values[p.key] = Array.isArray(raw) ? raw.filter((x): x is string => typeof x === 'string').join(', ') : '';
        break;
    }
  }
  return values;
}

export type BuildResult =
  | { ok: true; body: Record<string, unknown> }
  | { ok: false; errors: Record<string, string> };

/** Split a code list typed by hand: commas, semicolons and whitespace separate; blanks are dropped. */
export function parseCodes(text: string): string[] {
  return text.split(/[\s,;]+/).map(c => c.trim()).filter(c => c.length > 0);
}

/**
 * The rule body the inputs describe, or the problems with them (keyed by parameter). The same bounds as the server's schema, as a
 * convenience; the server validates again on edit, accept and publish and its answer is the one that counts.
 */
export function buildBody(def: RuleTypeDef, values: ParamValues): BuildResult {
  const body: Record<string, unknown> = { type: def.type };
  const errors: Record<string, string> = {};

  for (const p of def.params) {
    const v = values[p.key];
    switch (p.kind) {
      case 'number': {
        const text = typeof v === 'string' ? v.trim() : '';
        if (text === '') {
          if (p.required) errors[p.key] = `${p.label} is required.`;
          break;
        }

        const n = Number(text);
        if (!Number.isFinite(n)) {
          errors[p.key] = `${p.label} must be a number.`;
        } else if (p.integer && !Number.isInteger(n)) {
          errors[p.key] = `${p.label} must be a whole number.`;
        } else if (p.minExclusive ? n <= p.min : n < p.min) {
          errors[p.key] = `${p.label} must be ${p.minExclusive ? 'more than' : 'at least'} ${p.min}.`;
        } else if (n > p.max) {
          errors[p.key] = `${p.label} must be at most ${p.max}.`;
        } else {
          body[p.key] = n;
        }
        break;
      }
      case 'choice': {
        const text = typeof v === 'string' ? v : '';
        if (!p.options.includes(text)) errors[p.key] = `Choose ${p.label.toLowerCase()}.`;
        else body[p.key] = text;
        break;
      }
      case 'multi': {
        const picked = Array.isArray(v) ? v.filter(x => p.options.includes(x)) : [];
        if (picked.length === 0) errors[p.key] = `Pick at least one for ${p.label.toLowerCase()}.`;
        else body[p.key] = p.options.filter(o => picked.includes(o)); // vocabulary order, no duplicates
        break;
      }
      case 'codes': {
        const codes = parseCodes(typeof v === 'string' ? v : '');
        const unique = Array.from(new Set(codes));
        if (unique.length === 0) {
          if (p.required) errors[p.key] = `${p.label}: list at least one code.`;
          break;
        }

        const bad = unique.find(c => !CODE_PATTERN.test(c));
        if (bad) errors[p.key] = `${p.label}: "${bad}" is not a valid code (letters, digits, . _ - ; up to 20 characters).`;
        else if (unique.length > MAX_CODES) errors[p.key] = `${p.label}: at most ${MAX_CODES} codes.`;
        else body[p.key] = unique;
        break;
      }
    }
  }

  if (def.atLeastOneOf && !def.atLeastOneOf.some(k => k in body) && !def.atLeastOneOf.some(k => k in errors)) {
    errors[def.atLeastOneOf[0]] = `Give at least one of: ${def.atLeastOneOf.join(', ')}.`;
  }

  return Object.keys(errors).length > 0 ? { ok: false, errors } : { ok: true, body };
}

/** Structural equality of two rule bodies (key order does not matter). */
export function sameBody(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  if (typeof a !== typeof b || a === null || b === null || typeof a !== 'object') return false;
  if (Array.isArray(a) || Array.isArray(b)) {
    return Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((x, i) => sameBody(x, b[i]));
  }

  const ak = Object.keys(a as object);
  const bk = Object.keys(b as object);
  return ak.length === bk.length && ak.every(k => k in (b as object) && sameBody((a as Record<string, unknown>)[k], (b as Record<string, unknown>)[k]));
}
