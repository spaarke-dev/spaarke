/**
 * templatedDrafts — deterministic starting values for the plan's text fields (D-53: drafts in R1 are TEMPLATED, not AI).
 *
 * A template is a plain function of the item's OWN data: its rule sentence, its headline and its core record's name. There is
 * no AI call and no "Assistant draft" badge. A field is only prefilled when its parameter code has a template AND the
 * parameter is free text; the value is a starting point the user edits, never a choice (so it does not count toward the
 * discard check, see `hasUnrecordedChoices`). The templates key on the PARAMETER code (`subject`, `body`, `title`,
 * `description`), never on an action or a rule, so a new catalog action with those parameter codes is covered with no code here.
 *
 * Task: spaarke-ontology-platform-r1, task 058.
 */

import { composeIssueLine } from '../Worklist/composeIssueLine';
import { actionScope, nextScope, withDefaults } from './decisionDraft';
import type { ItemDraft, StepScope } from './decisionDraft';
import type { DecisionAction, DecisionPlan, DecisionWizardItem } from './decisionWizardTypes';

interface TemplateContext {
  sentence: string | null;
  headline: string;
  ruleShortName: string | null;
  coreName: string | null;
}

type Template = (context: TemplateContext) => string | null;

const about = (c: TemplateContext): string => c.coreName ?? c.headline;

/** Parameter code to template. Free-text parameters only. */
const TEMPLATES: Readonly<Partial<Record<string, Template>>> = {
  subject: c => `${c.ruleShortName ?? 'Question'}: ${about(c)}`,
  body: c =>
    [
      'Hello,',
      '',
      c.sentence ? `Regarding ${about(c)}: ${c.sentence}` : `Regarding ${about(c)}.`,
      '',
      'Could you please confirm the position so that we can decide how to proceed?',
      '',
      'Thank you.',
    ].join('\n'),
  title: c => `Follow up: ${c.headline}`,
  description: c => (c.sentence ? `${c.sentence} (${about(c)})` : `Follow up on ${c.headline}`),
};

function contextOf(item: DecisionWizardItem): TemplateContext {
  const line = composeIssueLine(item.row);
  const coreName = item.core?.name?.trim();
  return {
    sentence: item.sentence?.trim() || null,
    headline: line.headline,
    ruleShortName: line.ruleShortName,
    coreName: coreName ? coreName : null,
  };
}

function defaultsFor(action: DecisionAction, context: TemplateContext): Record<string, string> {
  const out: Record<string, string> = {};
  for (const parameter of action.parameters) {
    // Task 167 (D-193): a starting value the server computed for the parameter (Create budget's currency, from the matter's business
    // unit): data from the plan, never a template. It is offered, not chosen: the user can change it, and it is not a choice.
    // Task 168 (D-194): a yes/no's fixed default (Assign Task's Send email notification is ticked).
    if ((parameter.kind === 'Choice' || parameter.kind === 'Boolean') && parameter.defaultValue) {
      out[parameter.code] = parameter.defaultValue;
      continue;
    }

    if (parameter.kind !== 'Text' && parameter.kind !== 'TextArea') continue;
    const value = TEMPLATES[parameter.code]?.(context);
    if (value) out[parameter.code] = value;
  }
  // Task 160 (D-179): the plan's To pre-fill (data from the server, never a template), in the shape `to` is committed in.
  // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- wire data: a recipient without an address is skipped, never a throw
  const prefill = (action.prefillTo ?? []).map(r => r.email?.trim()).filter((email): email is string => !!email);
  if (prefill.length > 0 && action.parameters.some(p => p.code === 'to')) out.to = prefill.join('; ');
  return out;
}

/** The templated starting values for every action and Next step of the plan, as a map by step scope. */
export function templatedDefaults(
  plan: DecisionPlan,
  item: DecisionWizardItem
): Partial<Record<StepScope, Record<string, string>>> {
  const context = contextOf(item);
  const out: Partial<Record<StepScope, Record<string, string>>> = {};
  for (const action of plan.actions) {
    const values = defaultsFor(action, context);
    if (Object.keys(values).length > 0) out[actionScope(action.code)] = values;
  }
  for (const next of plan.nextSteps) {
    const values = defaultsFor(next, context);
    if (Object.keys(values).length > 0) out[nextScope(next.code)] = values;
  }
  return out;
}

/** Apply the templated values to a fresh draft as starting values (not choices). */
export function withTemplatedDefaults(draft: ItemDraft, plan: DecisionPlan, item: DecisionWizardItem): ItemDraft {
  let next = draft;
  const all = templatedDefaults(plan, item);
  for (const scope of Object.keys(all) as StepScope[]) {
    const values = all[scope];
    if (values) next = withDefaults(next, scope, values);
  }
  return next;
}
