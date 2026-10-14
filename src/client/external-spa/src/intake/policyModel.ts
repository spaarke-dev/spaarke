/**
 * policyModel - the Policy Question form, as built (r3 task 052): option codes, validation, the answers JSON and the
 * title. Pure (no React); `PolicyQuestionWizard.tsx` renders it.
 *
 * Field set: `notes/intake-form-design.md` section 2.2, as modified by the owner answers of 2026-10-12 (section 6.1):
 *   - NO department question (the server stamps `requesterDepartment` from the contact);
 *   - NO incident routing or link: the design note's warning banner (situation = happened, sensitive topic) pointed at an
 *     incident channel; the owner deferred that (DEF-002), so the wizard shows no banner and sends no routing flag;
 *   - the topic list is the fixed code list in `policyTopics.ts`.
 *   `policyReference` and `peopleInvolved` (nice to have) are not asked.
 *
 * Answers: a flat object, `schemaVersion: 1`, option CODES not labels, and no key for a hidden or empty field.
 * Title (`sprk_name`, NOT field-secured): the TOPIC LABEL only, never question text (sections 2.3 and 2.5):
 * `Policy question – {topicLabel}`, or `Policy question – {topicOther}` (at most 120 characters of it) for `other`.
 */
import { INTAKE_SCHEMA_VERSION, MAX_INTAKE_TITLE_LENGTH } from '../api/intake-client';
import { addDaysIso, choiceError, compact, dateError, textError, truncate, type FieldError } from './intakeValidation';
import { getPolicyTopics, policyTopicLabel, POLICY_TOPIC_OTHER } from './policyTopics';
import { optionLabel, type CodedOption } from './ndaModel';

export const POLICY_SITUATIONS: readonly CodedOption[] = [
  { code: 'planning', label: 'I’m planning to do something' },
  { code: 'happened', label: 'It has already happened' },
  { code: 'general', label: 'General question' },
];

export const POLICY_URGENCIES: readonly CodedOption[] = [
  { code: 'routine', label: 'No rush (within about a week)' },
  { code: 'soon', label: 'In the next few days' },
  { code: 'urgent', label: 'Urgent: it’s blocking a decision now' },
];

export const POLICY_LIMITS = {
  topicOther: { min: 2, max: 200 },
  question: { min: 20, max: 1000 },
  background: { min: 0, max: 4000 },
  /** `neededBy`: today up to this many days ahead. */
  neededByDays: 90,
  /** The `topicOther` text kept in the title. */
  titleTopicOtherMax: 120,
} as const;

export interface PolicyForm {
  topic: string;
  topicOther: string;
  question: string;
  background: string;
  situation: string;
  urgency: string;
  neededBy: string;
}

export function emptyPolicyForm(): PolicyForm {
  return { topic: '', topicOther: '', question: '', background: '', situation: '', urgency: '', neededBy: '' };
}

/** The wizard's two steps and the fields each owns. */
export const POLICY_STEP_FIELDS = {
  question: ['topic', 'topicOther', 'question', 'background', 'situation'],
  timing: ['urgency', 'neededBy'],
} as const;

/** `neededBy` is asked (and required) when the answer is wanted soon. */
export const asksNeededBy = (f: PolicyForm): boolean => f.urgency === 'soon' || f.urgency === 'urgent';

/** Errors by field key for the fields that are currently shown. An empty object means the form is valid. */
export function validatePolicy(f: PolicyForm, today: string): Record<string, string> {
  const errors: Record<string, string> = {};
  const set = (key: string, e: FieldError): void => {
    if (e) errors[key] = e;
  };

  set(
    'topic',
    choiceError(
      f.topic,
      'a topic',
      getPolicyTopics().map(t => t.code)
    )
  );
  if (f.topic === POLICY_TOPIC_OTHER) {
    set('topicOther', textError(f.topicOther, 'the topic', POLICY_LIMITS.topicOther.min, POLICY_LIMITS.topicOther.max));
  }
  set('question', textError(f.question, 'your question', POLICY_LIMITS.question.min, POLICY_LIMITS.question.max));
  set(
    'background',
    textError(f.background, 'the background', POLICY_LIMITS.background.min, POLICY_LIMITS.background.max)
  );
  set(
    'situation',
    choiceError(
      f.situation,
      'which describes this',
      POLICY_SITUATIONS.map(o => o.code)
    )
  );
  set(
    'urgency',
    choiceError(
      f.urgency,
      'when you need an answer',
      POLICY_URGENCIES.map(o => o.code)
    )
  );
  if (asksNeededBy(f)) {
    set(
      'neededBy',
      dateError(
        f.neededBy,
        true,
        today,
        addDaysIso(today, POLICY_LIMITS.neededByDays),
        'from today to 90 days from now'
      )
    );
  }
  return errors;
}

/** The answers JSON: `schemaVersion`, codes, only the keys of fields that are shown and filled. */
export function buildPolicyAnswers(f: PolicyForm): Record<string, unknown> {
  return compact({
    schemaVersion: INTAKE_SCHEMA_VERSION,
    topic: f.topic,
    topicOther: f.topic === POLICY_TOPIC_OTHER ? f.topicOther : undefined,
    question: f.question,
    background: f.background,
    situation: f.situation,
    urgency: f.urgency,
    neededBy: asksNeededBy(f) ? f.neededBy : undefined,
  });
}

/** The topic as the requester should read it: its label, or the text they typed for `other`. */
export function policyTopicText(f: PolicyForm): string {
  if (f.topic === POLICY_TOPIC_OTHER) return truncate(f.topicOther.trim(), POLICY_LIMITS.titleTopicOtherMax);
  return policyTopicLabel(f.topic) ?? '';
}

/** `Policy question – {topic}` (design note section 2.3): the topic label only, never the question text. */
export function policyTitle(f: PolicyForm): string {
  return truncate(`Policy question – ${policyTopicText(f)}`, MAX_INTAKE_TITLE_LENGTH);
}

/** The read-only summary shown on the last step: labels, not codes, only the fields that will be sent. */
export function policyReviewRows(f: PolicyForm): Array<{ label: string; value: string }> {
  const rows: Array<{ label: string; value: string }> = [];
  const add = (label: string, value: string): void => {
    if (value.trim() !== '') rows.push({ label, value: value.trim() });
  };
  add('Topic', f.topic === POLICY_TOPIC_OTHER ? `Other: ${f.topicOther.trim()}` : (policyTopicLabel(f.topic) ?? ''));
  add('Question', f.question);
  add('Background', f.background);
  add('Which describes this', optionLabel(POLICY_SITUATIONS, f.situation));
  add('When you need an answer', optionLabel(POLICY_URGENCIES, f.urgency));
  if (asksNeededBy(f)) add('Needed by', f.neededBy);
  return rows;
}
