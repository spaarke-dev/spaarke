/**
 * ndaModel - the NDA Assessment form, as built (r3 task 052): option codes, validation, the answers JSON and the title.
 * Pure (no React); `NdaAssessmentWizard.tsx` renders it.
 *
 * Field set: `notes/intake-form-design.md` section 1.2, as modified by the owner answers of 2026-10-12 (section 6.1):
 *   - NO department question (the server stamps `requesterDepartment` from the contact);
 *   - the wizard covers BOTH paths, chosen by `ndaAction`: `review` (the counterparty's paper; the requester attaches the
 *     draft under Documents after submitting, task 054) or `issue` (our standard NDA: also signer name, signer email,
 *     term and governing law, and `neededBy` becomes required). `ndaAction` REPLACES the design note's `paperSource`
 *     (review = their paper, issue = ours), so the two cannot disagree;
 *   - no document generation (DEF-001): the issue path only collects what Legal needs.
 *
 * Answers: a flat object, `schemaVersion: 1`, option CODES not labels, and no key for a hidden or empty field.
 * Title (`sprk_name`, NOT field-secured): `NDA – {counterpartyName}` only (section 1.3).
 */
import { INTAKE_SCHEMA_VERSION, MAX_INTAKE_TITLE_LENGTH } from '../api/intake-client';
import {
  addDaysIso,
  businessDaysUntil,
  choiceError,
  compact,
  dateError,
  emailError,
  textError,
  truncate,
  type FieldError,
} from './intakeValidation';

export interface CodedOption {
  code: string;
  label: string;
}

export const NDA_ACTIONS: readonly CodedOption[] = [
  { code: 'review', label: 'Review the other party’s NDA (they sent their own draft)' },
  { code: 'issue', label: 'Issue our standard NDA' },
];

export const NDA_COUNTERPARTY_TYPES: readonly CodedOption[] = [
  { code: 'customer', label: 'Customer or prospect' },
  { code: 'vendor', label: 'Vendor or supplier' },
  { code: 'partner', label: 'Partner or collaborator' },
  { code: 'investment', label: 'Investment, acquisition or financing' },
  { code: 'individual', label: 'Individual (consultant, candidate, advisor)' },
  { code: 'other', label: 'Other' },
];

export const NDA_DISCLOSURE_DIRECTIONS: readonly CodedOption[] = [
  { code: 'mutual', label: 'Both sides' },
  { code: 'weDisclose', label: 'Only us' },
  { code: 'theyDisclose', label: 'Only them' },
  { code: 'unsure', label: 'Not sure' },
];

export const NDA_SENSITIVE_INFO: readonly CodedOption[] = [
  { code: 'personalData', label: 'Personal or customer data' },
  { code: 'sourceCode', label: 'Source code or technical designs' },
  { code: 'financial', label: 'Non-public financial information' },
  { code: 'regulated', label: 'Regulated or export-controlled information' },
  { code: 'none', label: 'None of these' },
];

/** Term choices for the issue path (design note section 1.2, "proposedTerm": 1 / 2 / 3 / 5 years). */
export const NDA_TERMS: readonly CodedOption[] = [
  { code: 'oneYear', label: '1 year' },
  { code: 'twoYears', label: '2 years' },
  { code: 'threeYears', label: '3 years' },
  { code: 'fiveYears', label: '5 years' },
];

/** The caps, in one place (UTF-16 units of the trimmed value). The design note's, plus the issue-path fields (recorded). */
export const NDA_LIMITS = {
  counterpartyName: { min: 2, max: 200 },
  counterpartyTypeOther: { min: 2, max: 200 },
  counterpartyCountry: { min: 0, max: 100 },
  purpose: { min: 10, max: 1000 },
  signerName: { min: 2, max: 150 },
  signerEmail: { max: 254 },
  governingLaw: { min: 2, max: 200 },
  neededByReason: { min: 0, max: 300 },
  notes: { min: 0, max: 2000 },
  /** `neededBy`: today up to this many days ahead. */
  neededByDays: 365,
} as const;

export interface NdaForm {
  ndaAction: string;
  counterpartyName: string;
  counterpartyType: string;
  counterpartyTypeOther: string;
  counterpartyCountry: string;
  purpose: string;
  disclosureDirection: string;
  sensitiveInfo: string[];
  counterpartySignerName: string;
  counterpartySignerEmail: string;
  term: string;
  governingLaw: string;
  neededBy: string;
  neededByReason: string;
  notes: string;
}

export function emptyNdaForm(): NdaForm {
  return {
    ndaAction: '',
    counterpartyName: '',
    counterpartyType: '',
    counterpartyTypeOther: '',
    counterpartyCountry: '',
    purpose: '',
    disclosureDirection: '',
    sensitiveInfo: [],
    counterpartySignerName: '',
    counterpartySignerEmail: '',
    term: '',
    governingLaw: '',
    neededBy: '',
    neededByReason: '',
    notes: '',
  };
}

/** The wizard's three steps and the fields each owns (a step is valid when none of its fields has an error). */
export const NDA_STEP_FIELDS = {
  counterparty: ['ndaAction', 'counterpartyName', 'counterpartyType', 'counterpartyTypeOther', 'counterpartyCountry'],
  terms: [
    'purpose',
    'disclosureDirection',
    'sensitiveInfo',
    'counterpartySignerName',
    'counterpartySignerEmail',
    'term',
    'governingLaw',
  ],
  timing: ['neededBy', 'neededByReason', 'notes'],
} as const;

export const isIssue = (f: NdaForm): boolean => f.ndaAction === 'issue';
export const isReview = (f: NdaForm): boolean => f.ndaAction === 'review';

/** Whether the "what is driving the date" field is shown: a valid date at most 3 business days away. */
export function showsNeededByReason(f: NdaForm, today: string): boolean {
  return (
    f.neededBy !== '' &&
    dateError(f.neededBy, false, today, addDaysIso(today, NDA_LIMITS.neededByDays), '') === null &&
    businessDaysUntil(f.neededBy, today) <= 3
  );
}

/** Errors by field key for the fields that are currently shown. An empty object means the form is valid. */
export function validateNda(f: NdaForm, today: string): Record<string, string> {
  const errors: Record<string, string> = {};
  const set = (key: string, e: FieldError): void => {
    if (e) errors[key] = e;
  };

  set(
    'ndaAction',
    choiceError(
      f.ndaAction,
      'what you need',
      NDA_ACTIONS.map(o => o.code)
    )
  );
  set(
    'counterpartyName',
    textError(
      f.counterpartyName,
      'the other party’s name',
      NDA_LIMITS.counterpartyName.min,
      NDA_LIMITS.counterpartyName.max
    )
  );
  set(
    'counterpartyType',
    choiceError(
      f.counterpartyType,
      'your relationship with them',
      NDA_COUNTERPARTY_TYPES.map(o => o.code)
    )
  );
  if (f.counterpartyType === 'other') {
    set(
      'counterpartyTypeOther',
      textError(
        f.counterpartyTypeOther,
        'the relationship',
        NDA_LIMITS.counterpartyTypeOther.min,
        NDA_LIMITS.counterpartyTypeOther.max
      )
    );
  }
  set(
    'counterpartyCountry',
    textError(
      f.counterpartyCountry,
      'the country',
      NDA_LIMITS.counterpartyCountry.min,
      NDA_LIMITS.counterpartyCountry.max
    )
  );

  set('purpose', textError(f.purpose, 'what the NDA is for', NDA_LIMITS.purpose.min, NDA_LIMITS.purpose.max));
  set(
    'disclosureDirection',
    choiceError(
      f.disclosureDirection,
      'who will share confidential information',
      NDA_DISCLOSURE_DIRECTIONS.map(o => o.code)
    )
  );
  if (f.sensitiveInfo.some(c => !NDA_SENSITIVE_INFO.some(o => o.code === c))) {
    errors.sensitiveInfo = 'Choose from the list.';
  }
  if (isIssue(f)) {
    set(
      'counterpartySignerName',
      textError(f.counterpartySignerName, 'the signer’s name', NDA_LIMITS.signerName.min, NDA_LIMITS.signerName.max)
    );
    set('counterpartySignerEmail', emailError(f.counterpartySignerEmail, 'the signer'));
    set(
      'term',
      choiceError(
        f.term,
        'a term',
        NDA_TERMS.map(o => o.code)
      )
    );
    set(
      'governingLaw',
      textError(f.governingLaw, 'the governing law', NDA_LIMITS.governingLaw.min, NDA_LIMITS.governingLaw.max)
    );
  }

  set(
    'neededBy',
    dateError(
      f.neededBy,
      isIssue(f),
      today,
      addDaysIso(today, NDA_LIMITS.neededByDays),
      'from today to a year from now'
    )
  );
  if (showsNeededByReason(f, today)) {
    set(
      'neededByReason',
      textError(f.neededByReason, 'the reason', NDA_LIMITS.neededByReason.min, NDA_LIMITS.neededByReason.max)
    );
  }
  set('notes', textError(f.notes, 'the notes', NDA_LIMITS.notes.min, NDA_LIMITS.notes.max));
  return errors;
}

/** The answers JSON: `schemaVersion`, codes, only the keys of fields that are shown and filled. */
export function buildNdaAnswers(f: NdaForm, today: string): Record<string, unknown> {
  const issue = isIssue(f);
  return compact({
    schemaVersion: INTAKE_SCHEMA_VERSION,
    ndaAction: f.ndaAction,
    counterpartyName: f.counterpartyName,
    counterpartyType: f.counterpartyType,
    counterpartyTypeOther: f.counterpartyType === 'other' ? f.counterpartyTypeOther : undefined,
    counterpartyCountry: f.counterpartyCountry,
    purpose: f.purpose,
    disclosureDirection: f.disclosureDirection,
    sensitiveInfo: f.sensitiveInfo,
    counterpartySignerName: issue ? f.counterpartySignerName : undefined,
    counterpartySignerEmail: issue ? f.counterpartySignerEmail : undefined,
    term: issue ? f.term : undefined,
    governingLaw: issue ? f.governingLaw : undefined,
    neededBy: f.neededBy,
    neededByReason: showsNeededByReason(f, today) ? f.neededByReason : undefined,
    notes: f.notes,
  });
}

/** `NDA – {counterpartyName}` (design note section 1.3): the counterparty name and nothing else, whole title <= 850. */
export function ndaTitle(f: NdaForm): string {
  return truncate(`NDA – ${f.counterpartyName.trim()}`, MAX_INTAKE_TITLE_LENGTH);
}

export function optionLabel(options: readonly CodedOption[], code: string): string {
  return options.find(o => o.code === code)?.label ?? code;
}

/** The read-only summary shown on the last step: labels, not codes, only the fields that will be sent. */
export function ndaReviewRows(f: NdaForm, today: string): Array<{ label: string; value: string }> {
  const rows: Array<{ label: string; value: string }> = [];
  const add = (label: string, value: string): void => {
    if (value.trim() !== '') rows.push({ label, value: value.trim() });
  };
  add('What you need', optionLabel(NDA_ACTIONS, f.ndaAction));
  add('Other party', f.counterpartyName);
  add('Relationship', optionLabel(NDA_COUNTERPARTY_TYPES, f.counterpartyType));
  if (f.counterpartyType === 'other') add('Relationship (described)', f.counterpartyTypeOther);
  add('Country', f.counterpartyCountry);
  add('Purpose', f.purpose);
  add('Who shares confidential information', optionLabel(NDA_DISCLOSURE_DIRECTIONS, f.disclosureDirection));
  add('Sensitive information', f.sensitiveInfo.map(c => optionLabel(NDA_SENSITIVE_INFO, c)).join(', '));
  if (isIssue(f)) {
    add('Signer name', f.counterpartySignerName);
    add('Signer email', f.counterpartySignerEmail);
    add('Term', optionLabel(NDA_TERMS, f.term));
    add('Governing law', f.governingLaw);
  }
  add('Needed by', f.neededBy);
  if (showsNeededByReason(f, today)) add('Reason for the date', f.neededByReason);
  add('Notes', f.notes);
  return rows;
}
