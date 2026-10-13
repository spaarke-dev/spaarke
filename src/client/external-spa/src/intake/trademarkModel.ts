/**
 * trademarkModel - the Trademark Search form, as built (r3 task 053): option codes, validation, the answers JSON and the
 * title. Pure (no React); `TrademarkSearchWizard.tsx` renders it.
 *
 * Field set: `notes/intake-form-design.md` section 4.2, as modified by the owner answers of 2026-10-12 (section 6.1):
 *   - NO department question (the server stamps `requesterDepartment` from the contact);
 *   - the territory list is the fixed code list in `trademarkTerritories.ts` (answer 4);
 *   - NO class question: the requester describes the goods and services in words and Legal assigns the Nice classes
 *     (design note section 4.1, takeaway 2). The older POML draft listed "classes"; the design note supersedes it;
 *   - `alternatives`, `businessOwner`, `domainNeeded`, `usedWithHouseMark` and `searchDepth` (nice to have / not
 *     recommended) are not asked.
 *
 * Answers: a flat object, `schemaVersion: 1`, option CODES not labels, and no key for a hidden or empty field.
 * Title (`sprk_name`, NOT field-secured): `Trademark search - {proposedMark}` (section 4.3). The mark is in the title by
 * design (Legal needs it in lists); `goodsServices`, `markMeaning` and `notes` never are.
 */
import { INTAKE_SCHEMA_VERSION, MAX_INTAKE_TITLE_LENGTH } from '../api/intake-client';
import { addDaysIso, choiceError, compact, dateError, textError, truncate, type FieldError } from './intakeValidation';
import { optionLabel, type CodedOption } from './ndaModel';
import { getTrademarkTerritories, TRADEMARK_TERRITORY_OTHER, trademarkTerritoryLabel } from './trademarkTerritories';

export const TRADEMARK_MARK_FORMS: readonly CodedOption[] = [
  { code: 'word', label: 'Name or word only' },
  { code: 'logo', label: 'Logo or design only' },
  { code: 'wordAndLogo', label: 'Name with a logo' },
  { code: 'slogan', label: 'Slogan or tagline' },
];

export const TRADEMARK_MARK_ROLES: readonly CodedOption[] = [
  { code: 'product', label: 'A product' },
  { code: 'service', label: 'A service or programme' },
  { code: 'company', label: 'A company, brand or business unit' },
  { code: 'feature', label: 'A feature or campaign' },
];

export const TRADEMARK_CURRENT_USES: readonly CodedOption[] = [
  { code: 'notYet', label: 'Not yet' },
  { code: 'internal', label: 'Only internally' },
  { code: 'public', label: 'Yes, publicly' },
];

export const TRADEMARK_LIMITS = {
  proposedMark: { min: 1, max: 200 },
  markMeaning: { min: 0, max: 500 },
  goodsServices: { min: 10, max: 2000 },
  territoriesOther: { min: 2, max: 300 },
  useLocations: { min: 0, max: 300 },
  notes: { min: 0, max: 2000 },
  /** `launchDate`: today up to this many days ahead. */
  launchDays: 3650,
  /** `neededBy`: today up to the launch date, or up to this many days ahead when there is none. */
  neededByDays: 365,
  /** The earliest `firstUseDate` accepted (a sanity floor, not a business rule). */
  earliestFirstUse: '1900-01-01',
} as const;

export interface TrademarkForm {
  proposedMark: string;
  markForm: string;
  markMeaning: string;
  goodsServices: string;
  markRole: string;
  territories: string[];
  territoriesOther: string;
  currentUse: string;
  firstUseDate: string;
  useLocations: string;
  launchDate: string;
  neededBy: string;
  notes: string;
}

export function emptyTrademarkForm(): TrademarkForm {
  return {
    proposedMark: '',
    markForm: '',
    markMeaning: '',
    goodsServices: '',
    markRole: '',
    territories: [],
    territoriesOther: '',
    currentUse: '',
    firstUseDate: '',
    useLocations: '',
    launchDate: '',
    neededBy: '',
    notes: '',
  };
}

/** The wizard's three steps and the fields each owns (a step is valid when none of its fields has an error). */
export const TRADEMARK_STEP_FIELDS = {
  mark: ['proposedMark', 'markForm', 'markMeaning'],
  use: ['goodsServices', 'markRole', 'territories', 'territoriesOther'],
  status: ['currentUse', 'firstUseDate', 'useLocations', 'launchDate', 'neededBy', 'notes'],
} as const;

/** A logo file is expected after submitting (shown as a banner, and named on the success screen). */
export const hasLogo = (f: TrademarkForm): boolean => f.markForm === 'logo' || f.markForm === 'wordAndLogo';
export const asksOtherTerritories = (f: TrademarkForm): boolean => f.territories.includes(TRADEMARK_TERRITORY_OTHER);
export const isPublicUse = (f: TrademarkForm): boolean => f.currentUse === 'public';

/** The latest `neededBy` accepted: the launch date when it is a valid future date, else a year ahead. */
export function neededByMax(f: TrademarkForm, today: string): string {
  const fallback = addDaysIso(today, TRADEMARK_LIMITS.neededByDays);
  const launchOk =
    f.launchDate !== '' &&
    dateError(f.launchDate, false, today, addDaysIso(today, TRADEMARK_LIMITS.launchDays), '') === null;
  return launchOk ? f.launchDate : fallback;
}

/** Errors by field key for the fields that are currently shown. An empty object means the form is valid. */
export function validateTrademark(f: TrademarkForm, today: string): Record<string, string> {
  const errors: Record<string, string> = {};
  const set = (key: string, e: FieldError): void => {
    if (e) errors[key] = e;
  };

  set(
    'proposedMark',
    textError(
      f.proposedMark,
      'the proposed name or mark',
      TRADEMARK_LIMITS.proposedMark.min,
      TRADEMARK_LIMITS.proposedMark.max
    )
  );
  set(
    'markForm',
    choiceError(
      f.markForm,
      'the kind of mark',
      TRADEMARK_MARK_FORMS.map(o => o.code)
    )
  );
  set(
    'markMeaning',
    textError(f.markMeaning, 'the meaning', TRADEMARK_LIMITS.markMeaning.min, TRADEMARK_LIMITS.markMeaning.max)
  );

  set(
    'goodsServices',
    textError(
      f.goodsServices,
      'what it will be used for',
      TRADEMARK_LIMITS.goodsServices.min,
      TRADEMARK_LIMITS.goodsServices.max
    )
  );
  if (f.markRole !== '' && !TRADEMARK_MARK_ROLES.some(o => o.code === f.markRole)) {
    errors.markRole = 'Choose what it will name from the list.';
  }
  const territoryCodes = getTrademarkTerritories().map(t => t.code);
  if (f.territories.length === 0) {
    errors.territories = 'Choose at least one place.';
  } else if (f.territories.some(c => !territoryCodes.includes(c))) {
    errors.territories = 'Choose from the list.';
  }
  if (asksOtherTerritories(f)) {
    set(
      'territoriesOther',
      textError(
        f.territoriesOther,
        'the other countries',
        TRADEMARK_LIMITS.territoriesOther.min,
        TRADEMARK_LIMITS.territoriesOther.max
      )
    );
  }

  set(
    'currentUse',
    choiceError(
      f.currentUse,
      'whether the name is in use',
      TRADEMARK_CURRENT_USES.map(o => o.code)
    )
  );
  if (isPublicUse(f)) {
    set(
      'firstUseDate',
      dateError(f.firstUseDate, false, TRADEMARK_LIMITS.earliestFirstUse, today, 'on or before today')
    );
    set(
      'useLocations',
      textError(
        f.useLocations,
        'where it is used',
        TRADEMARK_LIMITS.useLocations.min,
        TRADEMARK_LIMITS.useLocations.max
      )
    );
  }
  set(
    'launchDate',
    dateError(
      f.launchDate,
      false,
      today,
      addDaysIso(today, TRADEMARK_LIMITS.launchDays),
      'from today to ten years from now'
    )
  );
  set(
    'neededBy',
    dateError(
      f.neededBy,
      false,
      today,
      neededByMax(f, today),
      f.launchDate !== '' && neededByMax(f, today) === f.launchDate
        ? 'from today up to the launch date'
        : 'from today to a year from now'
    )
  );
  set('notes', textError(f.notes, 'the notes', TRADEMARK_LIMITS.notes.min, TRADEMARK_LIMITS.notes.max));
  return errors;
}

/** The answers JSON: `schemaVersion`, codes, only the keys of fields that are shown and filled. */
export function buildTrademarkAnswers(f: TrademarkForm): Record<string, unknown> {
  const pub = isPublicUse(f);
  return compact({
    schemaVersion: INTAKE_SCHEMA_VERSION,
    proposedMark: f.proposedMark,
    markForm: f.markForm,
    markMeaning: f.markMeaning,
    goodsServices: f.goodsServices,
    markRole: f.markRole,
    territories: f.territories,
    territoriesOther: asksOtherTerritories(f) ? f.territoriesOther : undefined,
    currentUse: f.currentUse,
    firstUseDate: pub ? f.firstUseDate : undefined,
    useLocations: pub ? f.useLocations : undefined,
    launchDate: f.launchDate,
    neededBy: f.neededBy,
    notes: f.notes,
  });
}

/** `Trademark search – {proposedMark}` (design note section 4.3): the mark and nothing else, whole title <= 850. */
export function trademarkTitle(f: TrademarkForm): string {
  return truncate(`Trademark search – ${f.proposedMark.trim()}`, MAX_INTAKE_TITLE_LENGTH);
}

/** The read-only summary shown on the last step: labels, not codes, only the fields that will be sent. */
export function trademarkReviewRows(f: TrademarkForm): Array<{ label: string; value: string }> {
  const rows: Array<{ label: string; value: string }> = [];
  const add = (label: string, value: string): void => {
    if (value.trim() !== '') rows.push({ label, value: value.trim() });
  };
  add('Proposed name or mark', f.proposedMark);
  add('Kind of mark', optionLabel(TRADEMARK_MARK_FORMS, f.markForm));
  add('Meaning', f.markMeaning);
  add('Used for', f.goodsServices);
  add('It will name', f.markRole === '' ? '' : optionLabel(TRADEMARK_MARK_ROLES, f.markRole));
  add('Where', f.territories.map(c => trademarkTerritoryLabel(c) ?? c).join(', '));
  if (asksOtherTerritories(f)) add('Other countries', f.territoriesOther);
  add('Already in use', optionLabel(TRADEMARK_CURRENT_USES, f.currentUse));
  if (isPublicUse(f)) {
    add('In use since', f.firstUseDate);
    add('Used in', f.useLocations);
  }
  add('Planned launch', f.launchDate);
  add('Result needed by', f.neededBy);
  add('Notes', f.notes);
  return rows;
}
