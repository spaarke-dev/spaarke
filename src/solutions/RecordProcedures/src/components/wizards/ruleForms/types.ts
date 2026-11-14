/**
 * The typed form of one billing rule type (ebilling-r1 task 084c, EB-D-217; D-27).
 *
 * A rule is a closed typed template with JSON parameters. The server's JSON Schemas (`Services/Signals/Schemas/billing-*.schema.json`,
 * validated by `RuleBodySchemaValidator` and `PolicyVersionValidator`) are the authority: a form offers exactly the properties of its
 * type's schema and mirrors their bounds and vocabularies only so a user is told early. A refusal by the server is always shown, field by
 * field (`body.ts` `serverFieldErrors`). `RecordProceduresRuleFormAgreementTests` (ArchTests) reads the `*.form.ts` files and the schemas
 * and fails when a form's fields, requirements, bounds or vocabularies drift from the schema.
 *
 * Conventions the agreement test relies on: one field per line in each `*.form.ts`; `key` and option `value` in single quotes; every label,
 * help and refusal text in double quotes.
 */

export type FieldKind = 'number' | 'choice' | 'multi' | 'codes';

export interface FieldOption {
  value: string;
  label: string;
  /** Listed but not selectable: why the server refuses this value for now (the form says "not available yet" and gives this text). */
  refused?: string;
}

export interface FormField {
  /** The property name in the rule body (the schema's property). */
  key: string;
  kind: FieldKind;
  label: string;
  help: string;
  /** In the schema's top-level `required`. */
  required?: boolean;
  /** The field is shown (and used) only while another field has this value (the schema's `allOf` / `if`). */
  showWhen?: { key: string; equals: string };
  /** number */
  integer?: boolean;
  min?: number;
  /** With `minExclusive` the value must be more than `min`. */
  minExclusive?: boolean;
  max?: number;
  maxExclusive?: boolean;
  /** choice, multi */
  options?: readonly FieldOption[];
}

export interface RuleFormDef {
  /** The `RuleType` member name the server sends in `ruleType` (`RuleTypeName`). */
  ruleType: string;
  label: string;
  fields: readonly FormField[];
  /** Why "Change for this record" is not offered for this type, when it is not. A type with no fields must give one. */
  noChange?: string;
  /** At least one of these keys must be given (the schema's `anyOf`). */
  atLeastOneOf?: readonly string[];
  /** Exactly one of these keys must be given (the schema's `oneOf`, within the condition its fields share). */
  exactlyOneOf?: readonly string[];
}

/** What the inputs hold: a number as typed, a choice as its value, a multi-choice as an array, a code list as one line. */
export type FormValues = Record<string, string | string[]>;
