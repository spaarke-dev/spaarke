/**
 * inventionModel - the Invention Submission form, as built (r3 task 053): option codes, validation, the answers JSON and
 * the title. Pure (no React); `InventionSubmissionWizard.tsx` renders it.
 *
 * Field set: `notes/intake-form-design.md` section 3.2, as modified by the owner answers of 2026-10-12 (section 6.1):
 *   - NO department question (the server stamps `requesterDepartment` from the contact);
 *   - availability to partners is the SERVER's call (`ExternalAccess:PartnerInventionIntakeEnabled`, surfaced in
 *     `/me` `intakeRequestTypes`); this model has no availability logic - `isIntakeTypeAvailable` owns it;
 *   - the older POML draft (title, inventors, description, first public disclosure date, related project) is superseded
 *     by the design note's four steps.
 *   `inventorContributionPct`, `commercialApplications`, `competitors`, `keywords` and `thirdPartyCode` (nice to have)
 *   are not asked.
 *
 * Answers: a flat object, `schemaVersion: 1`, option CODES not labels (a pure yes/no is a boolean, yes/no/unsure a string
 * code), and no key for a hidden or empty field. Highest sensitivity of the four types: `problemSolved`, `description`
 * and `knownAlternatives` live only in the field-secured answers.
 * Title (`sprk_name`, NOT field-secured): `Invention - {shortTitle}` (section 3.3). `shortTitle` is non-confidential by
 * design and the wizard says so beside the field; no other answer ever reaches the title.
 */
import { INTAKE_SCHEMA_VERSION, MAX_INTAKE_TITLE_LENGTH } from '../api/intake-client';
import {
  addDaysIso,
  choiceError,
  compact,
  dateError,
  emailError,
  textError,
  truncate,
  type FieldError,
} from './intakeValidation';
import { optionLabel, type CodedOption } from './ndaModel';

export const INVENTION_YES_NO: readonly CodedOption[] = [
  { code: 'yes', label: 'Yes' },
  { code: 'no', label: 'No' },
];

export const INVENTION_YES_NO_UNSURE: readonly CodedOption[] = [
  { code: 'yes', label: 'Yes' },
  { code: 'no', label: 'No' },
  { code: 'unsure', label: 'Not sure' },
];

export const INVENTION_PUBLIC_DISCLOSURES: readonly CodedOption[] = [
  { code: 'none', label: 'No' },
  { code: 'done', label: 'Yes, already' },
  { code: 'planned', label: 'Not yet, but planned' },
];

export const INVENTION_DISCLOSURE_METHODS: readonly CodedOption[] = [
  { code: 'publication', label: 'Publication' },
  { code: 'presentation', label: 'Presentation or talk' },
  { code: 'onlinePost', label: 'Website, social post or preprint' },
  { code: 'productSale', label: 'Sale or offer for sale' },
  { code: 'demo', label: 'Demo to a customer or third party' },
  { code: 'openSource', label: 'Open-source release' },
  { code: 'other', label: 'Other' },
];

export const INVENTION_PRODUCT_USES: readonly CodedOption[] = [
  { code: 'no', label: 'No' },
  { code: 'planned', label: 'Planned' },
  { code: 'released', label: 'Already released' },
];

export const INVENTION_LIMITS = {
  shortTitle: { min: 5, max: 150 },
  problemSolved: { min: 20, max: 2000 },
  description: { min: 50, max: 8000 },
  knownAlternatives: { min: 0, max: 3000 },
  coInventorName: { min: 2, max: 150 },
  coInventorOrganization: { min: 0, max: 200 },
  /** An email address (RFC 5321 path limit); `emailError` enforces the same 254. */
  coInventorEmail: { max: 254 },
  /** Rows in the co-inventor list. */
  maxCoInventors: 20,
  disclosureVenue: { min: 0, max: 300 },
  sharedWith: { min: 1, max: 500 },
  productName: { min: 1, max: 200 },
  externalInvolvementDetails: { min: 1, max: 1000 },
  notes: { min: 0, max: 2000 },
  /** A planned disclosure, a planned product release: today up to this many days ahead. */
  futureDays: 3650,
  /** A planned disclosure this close (days) shows the timing warning. */
  warnWithinDays: 60,
  /** The earliest past date accepted (a sanity floor, not a business rule). */
  earliestPast: '1900-01-01',
} as const;

export interface CoInventor {
  /** Client-only row identity (React key and touched-state key). Never sent: `buildInventionAnswers` picks fields. */
  id: string;
  name: string;
  email: string;
  organization: string;
}

let coInventorSeq = 0;

/** A unique row id: `crypto.randomUUID()` where it exists (it needs a secure context), else a counter. */
const newCoInventorId = (): string =>
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `co-inventor-${++coInventorSeq}`;

export const emptyCoInventor = (): CoInventor => ({ id: newCoInventorId(), name: '', email: '', organization: '' });

export interface InventionForm {
  shortTitle: string;
  problemSolved: string;
  description: string;
  knownAlternatives: string;
  submitterIsInventor: string;
  coInventors: CoInventor[];
  conceptionDate: string;
  publicDisclosure: string;
  disclosureDate: string;
  disclosureMethod: string;
  disclosureVenue: string;
  sharedExternally: string;
  sharedWith: string;
  productUse: string;
  productName: string;
  productDate: string;
  externalInvolvement: string;
  externalInvolvementDetails: string;
  notes: string;
  accuracyConfirmed: boolean;
}

export function emptyInventionForm(): InventionForm {
  return {
    shortTitle: '',
    problemSolved: '',
    description: '',
    knownAlternatives: '',
    submitterIsInventor: '',
    coInventors: [],
    conceptionDate: '',
    publicDisclosure: '',
    disclosureDate: '',
    disclosureMethod: '',
    disclosureVenue: '',
    sharedExternally: '',
    sharedWith: '',
    productUse: '',
    productName: '',
    productDate: '',
    externalInvolvement: '',
    externalInvolvementDetails: '',
    notes: '',
    accuracyConfirmed: false,
  };
}

/**
 * The wizard's four steps and the fields each owns. `coInventors` stands for the list-level error AND every
 * `coInventors.{row}.{field}` error (see `stepHasError`).
 */
export const INVENTION_STEP_FIELDS = {
  invention: ['shortTitle', 'problemSolved', 'description', 'knownAlternatives'],
  inventors: ['submitterIsInventor', 'coInventors', 'conceptionDate'],
  disclosure: [
    'publicDisclosure',
    'disclosureDate',
    'disclosureMethod',
    'disclosureVenue',
    'sharedExternally',
    'sharedWith',
    'productUse',
    'productName',
    'productDate',
    'externalInvolvement',
    'externalInvolvementDetails',
  ],
  confirm: ['notes', 'accuracyConfirmed'],
} as const;

/** Whether any of `fields` has an error; a field also owns the nested keys under it (`coInventors.0.name`). */
export function stepHasError(errors: Record<string, string>, fields: readonly string[]): boolean {
  return Object.keys(errors).some(k => fields.some(f => k === f || k.startsWith(`${f}.`)));
}

export const hasDisclosure = (f: InventionForm): boolean =>
  f.publicDisclosure === 'done' || f.publicDisclosure === 'planned';
export const hasProduct = (f: InventionForm): boolean => f.productUse === 'planned' || f.productUse === 'released';

/**
 * The filing-clock warning: a disclosure that already happened, or a planned one within 60 days (design note
 * section 3.2, step 3). A planned disclosure with no valid date yet shows nothing.
 */
export function showsDisclosureWarning(f: InventionForm, today: string): boolean {
  if (f.publicDisclosure === 'done') return true;
  if (f.publicDisclosure !== 'planned') return false;
  const max = addDaysIso(today, INVENTION_LIMITS.warnWithinDays);
  return f.disclosureDate !== '' && dateError(f.disclosureDate, true, today, max, '') === null;
}

function disclosureDateError(f: InventionForm, today: string): FieldError {
  if (f.publicDisclosure === 'done') {
    return dateError(f.disclosureDate, true, INVENTION_LIMITS.earliestPast, today, 'on or before today');
  }
  return dateError(
    f.disclosureDate,
    true,
    today,
    addDaysIso(today, INVENTION_LIMITS.futureDays),
    'from today to ten years from now'
  );
}

/** Errors by field key for the fields that are currently shown. An empty object means the form is valid. */
export function validateInvention(f: InventionForm, today: string): Record<string, string> {
  const errors: Record<string, string> = {};
  const set = (key: string, e: FieldError): void => {
    if (e) errors[key] = e;
  };
  const choice = (value: string, label: string, options: readonly CodedOption[], key: string): void =>
    set(
      key,
      choiceError(
        value,
        label,
        options.map(o => o.code)
      )
    );

  // Step 1: the invention
  set(
    'shortTitle',
    textError(f.shortTitle, 'a short title', INVENTION_LIMITS.shortTitle.min, INVENTION_LIMITS.shortTitle.max)
  );
  set(
    'problemSolved',
    textError(
      f.problemSolved,
      'the problem it solves',
      INVENTION_LIMITS.problemSolved.min,
      INVENTION_LIMITS.problemSolved.max
    )
  );
  set(
    'description',
    textError(f.description, 'a description', INVENTION_LIMITS.description.min, INVENTION_LIMITS.description.max)
  );
  set(
    'knownAlternatives',
    textError(
      f.knownAlternatives,
      'the similar products or patents',
      INVENTION_LIMITS.knownAlternatives.min,
      INVENTION_LIMITS.knownAlternatives.max
    )
  );

  // Step 2: inventors
  choice(f.submitterIsInventor, 'whether you are an inventor', INVENTION_YES_NO, 'submitterIsInventor');
  if (f.submitterIsInventor === 'no' && f.coInventors.length === 0) {
    errors.coInventors = 'Add at least one inventor.';
  }
  if (f.coInventors.length > INVENTION_LIMITS.maxCoInventors) {
    errors.coInventors = `Add at most ${INVENTION_LIMITS.maxCoInventors} inventors.`;
  }
  f.coInventors.forEach((c, i) => {
    set(
      `coInventors.${i}.name`,
      textError(c.name, 'the inventor’s name', INVENTION_LIMITS.coInventorName.min, INVENTION_LIMITS.coInventorName.max)
    );
    if (c.email.trim() !== '')
      set(`coInventors.${i}.email`, emailError(c.email, 'the inventor', INVENTION_LIMITS.coInventorEmail.max));
    set(
      `coInventors.${i}.organization`,
      textError(
        c.organization,
        'the organization',
        INVENTION_LIMITS.coInventorOrganization.min,
        INVENTION_LIMITS.coInventorOrganization.max
      )
    );
  });
  set('conceptionDate', dateError(f.conceptionDate, false, INVENTION_LIMITS.earliestPast, today, 'on or before today'));

  // Step 3: disclosure, use and outside involvement
  choice(f.publicDisclosure, 'whether it has been shared publicly', INVENTION_PUBLIC_DISCLOSURES, 'publicDisclosure');
  if (hasDisclosure(f)) {
    set('disclosureDate', disclosureDateError(f, today));
    choice(f.disclosureMethod, 'how it was or will be shared', INVENTION_DISCLOSURE_METHODS, 'disclosureMethod');
    set(
      'disclosureVenue',
      textError(
        f.disclosureVenue,
        'the venue',
        INVENTION_LIMITS.disclosureVenue.min,
        INVENTION_LIMITS.disclosureVenue.max
      )
    );
  }
  choice(f.sharedExternally, 'whether it was shared outside the company', INVENTION_YES_NO_UNSURE, 'sharedExternally');
  if (f.sharedExternally === 'yes') {
    set(
      'sharedWith',
      textError(
        f.sharedWith,
        'who it was shared with',
        INVENTION_LIMITS.sharedWith.min,
        INVENTION_LIMITS.sharedWith.max
      )
    );
  }
  choice(f.productUse, 'whether it is used in a product or service', INVENTION_PRODUCT_USES, 'productUse');
  if (hasProduct(f)) {
    set(
      'productName',
      textError(
        f.productName,
        'the product or service name',
        INVENTION_LIMITS.productName.min,
        INVENTION_LIMITS.productName.max
      )
    );
    set(
      'productDate',
      dateError(
        f.productDate,
        false,
        INVENTION_LIMITS.earliestPast,
        addDaysIso(today, INVENTION_LIMITS.futureDays),
        'within the next ten years'
      )
    );
  }
  choice(
    f.externalInvolvement,
    'whether anyone outside the company was involved',
    INVENTION_YES_NO_UNSURE,
    'externalInvolvement'
  );
  if (f.externalInvolvement === 'yes') {
    set(
      'externalInvolvementDetails',
      textError(
        f.externalInvolvementDetails,
        'who was involved',
        INVENTION_LIMITS.externalInvolvementDetails.min,
        INVENTION_LIMITS.externalInvolvementDetails.max
      )
    );
  }

  // Step 4: review and confirm
  set('notes', textError(f.notes, 'the notes', INVENTION_LIMITS.notes.min, INVENTION_LIMITS.notes.max));
  if (!f.accuracyConfirmed) errors.accuracyConfirmed = 'Confirm the statement to submit.';
  return errors;
}

/** The answers JSON: `schemaVersion`, codes, only the keys of fields that are shown and filled. */
export function buildInventionAnswers(f: InventionForm): Record<string, unknown> {
  const disclosed = hasDisclosure(f);
  const product = hasProduct(f);
  return compact({
    schemaVersion: INTAKE_SCHEMA_VERSION,
    shortTitle: f.shortTitle,
    problemSolved: f.problemSolved,
    description: f.description,
    knownAlternatives: f.knownAlternatives,
    submitterIsInventor: f.submitterIsInventor === 'yes',
    coInventors: f.coInventors.map(c => compact({ name: c.name, email: c.email, organization: c.organization })),
    conceptionDate: f.conceptionDate,
    publicDisclosure: f.publicDisclosure,
    disclosureDate: disclosed ? f.disclosureDate : undefined,
    disclosureMethod: disclosed ? f.disclosureMethod : undefined,
    disclosureVenue: disclosed ? f.disclosureVenue : undefined,
    sharedExternally: f.sharedExternally,
    sharedWith: f.sharedExternally === 'yes' ? f.sharedWith : undefined,
    productUse: f.productUse,
    productName: product ? f.productName : undefined,
    productDate: product ? f.productDate : undefined,
    externalInvolvement: f.externalInvolvement,
    externalInvolvementDetails: f.externalInvolvement === 'yes' ? f.externalInvolvementDetails : undefined,
    notes: f.notes,
    accuracyConfirmed: f.accuracyConfirmed,
  });
}

/** `Invention – {shortTitle}` (design note section 3.3): the non-confidential short title only, whole title <= 850. */
export function inventionTitle(f: InventionForm): string {
  return truncate(`Invention – ${f.shortTitle.trim()}`, MAX_INTAKE_TITLE_LENGTH);
}

/** The read-only summary shown on the last step: labels, not codes, only the fields that will be sent. */
export function inventionReviewRows(f: InventionForm): Array<{ label: string; value: string }> {
  const rows: Array<{ label: string; value: string }> = [];
  const add = (label: string, value: string): void => {
    if (value.trim() !== '') rows.push({ label, value: value.trim() });
  };
  add('Short title', f.shortTitle);
  add('Problem it solves', f.problemSolved);
  add('Description', f.description);
  add('Similar products or patents', f.knownAlternatives);
  add('You are an inventor', optionLabel(INVENTION_YES_NO, f.submitterIsInventor));
  add(
    'Other inventors',
    f.coInventors
      .map(c => [c.name.trim(), c.email.trim(), c.organization.trim()].filter(p => p !== '').join(', '))
      .join('\n')
  );
  add('Conceived around', f.conceptionDate);
  add('Shared publicly', optionLabel(INVENTION_PUBLIC_DISCLOSURES, f.publicDisclosure));
  if (hasDisclosure(f)) {
    add(f.publicDisclosure === 'planned' ? 'Planned date' : 'Date shared', f.disclosureDate);
    add('How', optionLabel(INVENTION_DISCLOSURE_METHODS, f.disclosureMethod));
    add('Where', f.disclosureVenue);
  }
  add('Shared outside the company', optionLabel(INVENTION_YES_NO_UNSURE, f.sharedExternally));
  if (f.sharedExternally === 'yes') add('Shared with', f.sharedWith);
  add('Used in a product or service', optionLabel(INVENTION_PRODUCT_USES, f.productUse));
  if (hasProduct(f)) {
    add('Product or service', f.productName);
    add('Release date', f.productDate);
  }
  add('Outside involvement', optionLabel(INVENTION_YES_NO_UNSURE, f.externalInvolvement));
  if (f.externalInvolvement === 'yes') add('Who was involved', f.externalInvolvementDetails);
  add('Notes', f.notes);
  return rows;
}
