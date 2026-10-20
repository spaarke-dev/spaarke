/**
 * RuleWizard (task 102; D-22, D-149, D-23, FR-10, FR-58): the acceptance criteria that run without a browser.
 *
 *  - a new rule saves through the authoring service (Off) and a new version publishes, each with the SAME serialised body and plan the
 *    BFF validated and the dry run tested;
 *  - the Test gates the end: no Save / Publish until the test was run on exactly this draft, and any later edit makes it stale;
 *  - Publish asks first, with the supersede and re-raise counts from the dry run;
 *  - every list comes from the catalog (an invented one here): rule types, tables, columns, operators, lanes, severities, work types,
 *    actions, next steps, dismissal reasons, triage categories; no free-text JSON entry; wording offers only templateEligibleFields;
 *  - a code is never offered or chosen in both Actions and Next steps; the catalog's excludes are shown and enforced;
 *  - a refusal by validate (200) or by save / publish (400) shows the BFF's message on the field and the step it concerns;
 *  - Escape and the backdrop never close the wizard; closing with work asks first.
 *
 * Not covered here (needs a browser): the three UI tests of the task - the Test gate seen in a real page, an Invalid body that cannot
 * pass, and dark mode (ADR-021). The wizard renders under webDarkTheme below as a smoke check only.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import {
  REASON_COLUMN_NOT_AUTHORABLE,
  REASON_DRY_RUN_REQUIRED,
  REASON_DUPLICATE_ACTION,
  REASON_POLICY_CODE_TAKEN,
  REASON_SCOPE_SUBJECT_MISMATCH,
} from '../authoringService';
import type { OntologyAuthoringService } from '../authoringService';
import { RuleWizard } from '../RuleWizard';
import type { RuleWizardResult } from '../RuleWizard';
import type { MatterPicker, NewVersionMode, RuleWizardMode } from '../ruleWizardTypes';
import { catalog, dryRunResult, failure, makeAuthoringService, published, saved, validation } from './fixtures';

jest.setTimeout(120000);

const WAIT = { timeout: 20000 };
const find = (id: string) => screen.findByTestId(id, undefined, WAIT);
const byId = (id: string) => screen.getByTestId(id);
const maybe = (id: string) => screen.queryByTestId(id);
const change = (id: string, value: string) => fireEvent.change(byId(id), { target: { value } });
const click = (id: string) => fireEvent.click(byId(id));
const optionValues = (id: string) => Array.from((byId(id) as HTMLSelectElement).options).map(o => o.value);
const valueOf = (id: string) => (byId(id) as HTMLInputElement).value;
/** How many times `text` is rendered anywhere on the page, hidden or not. */
const occurrencesOf = (text: string) => (document.body.textContent ?? '').split(text).length - 1;

const versionMode: NewVersionMode = {
  kind: 'version',
  policyId: 'policy-1',
  code: 'POL-A',
  name: 'Rule A',
  lane: 'Decide',
  versionNumber: 2,
  enabled: true,
  wording: { headline: 'Old headline', messageTemplate: 'Old {{zz_state}}', proposedAction: 'Old action' },
  actions: ['frobnicate'],
  nextSteps: ['ring-bell'],
  priority: 500,
  countsTowardSuppression: true,
  quietWindowDays: 30,
  scope: { tenant: 'tenant-1', matter: null, matterId: null },
};

function setup(
  over: {
    service?: OntologyAuthoringService;
    mode?: RuleWizardMode;
    theme?: typeof webLightTheme;
    matterPicker?: MatterPicker;
  } = {}
) {
  const service = (over.service ?? makeAuthoringService()) as jest.Mocked<OntologyAuthoringService>;
  const onClose = jest.fn<void, [RuleWizardResult | null]>();
  const mode = over.mode ?? { kind: 'new' };
  render(
    <FluentProvider theme={over.theme ?? webLightTheme}>
      <RuleWizard service={service} mode={mode} onClose={onClose} matterPicker={over.matterPicker} />
    </FluentProvider>
  );
  return { service, onClose };
}

// ---- walking the steps --------------------------------------------------------------------------------------------------------

async function fillBasics() {
  await find('rule-wizard-basics');
  change('rule-wizard-code', 'POL-ZZ');
  change('rule-wizard-name', 'Zed rule');
  change('rule-wizard-lane', 'Decide');
}

async function fillCondition() {
  await find('rule-wizard-condition');
  change('rule-wizard-subject', 'zz_widget');
  click('rule-wizard-when-row-add');
  change('rule-wizard-when-row-0-field', 'zz_state');
  change('rule-wizard-when-row-0-value', 'open');
}

function fillWording() {
  change('rule-wizard-headline', 'A headline');
  change('rule-wizard-message', 'About {{zz_state}}.');
  change('rule-wizard-proposed-action', 'Do the thing');
}

async function next(arrivesAt: string) {
  click('rule-wizard-next');
  await find(arrivesAt);
}

/** New rule: through Basics, Condition, Wording, Actions and Scope, to the Test step. */
async function toTestAsNewRule() {
  await fillBasics();
  await next('rule-wizard-condition');
  await fillCondition();
  await next('rule-wizard-wording');
  fillWording();
  await next('rule-wizard-actions');
  change('rule-wizard-code-frobnicate-role', 'action');
  await next('rule-wizard-scope');
  await next('rule-wizard-test');
}

/** New version: the condition is rebuilt (the BFF returns no raw body, D-149); wording and plan are prefilled. */
async function toTestAsNewVersion() {
  await find('rule-wizard-basics');
  await next('rule-wizard-condition');
  await fillCondition();
  await next('rule-wizard-wording');
  await next('rule-wizard-actions');
  await next('rule-wizard-scope');
  await next('rule-wizard-test');
}

async function runTest() {
  click('rule-wizard-run-test');
  await find('rule-wizard-test-result');
}

// ---- the catalog ----------------------------------------------------------------------------------------------------------------

describe('RuleWizard - the catalog', () => {
  it('does not open without the catalog: an unreadable catalog is an error with a retry, never empty lists', async () => {
    const getCatalog = jest
      .fn()
      .mockResolvedValueOnce(failure({ status: 403, message: 'Dataverse refused this for your account.' }))
      .mockResolvedValue({ ok: true, data: catalog });
    setup({ service: makeAuthoringService({ getCatalog }) });

    expect(await find('rule-wizard-catalog-error')).toHaveTextContent('Dataverse refused this for your account.');
    expect(maybe('rule-wizard-basics')).toBeNull();

    click('rule-wizard-catalog-retry');
    await find('rule-wizard-basics');
    expect(getCatalog).toHaveBeenCalledTimes(2);
  });

  it('offers lanes, severities and work types from the catalog, and drops a choice the new lane does not list', async () => {
    setup();
    await find('rule-wizard-basics');

    expect(optionValues('rule-wizard-lane')).toEqual(['', 'Decide', 'Do']);
    expect((byId('rule-wizard-severity') as HTMLSelectElement).disabled).toBe(true);

    change('rule-wizard-lane', 'Decide');
    expect(optionValues('rule-wizard-severity')).toEqual(['', '11', '22']);
    expect(optionValues('rule-wizard-work-type')).toEqual(['', '7']);

    change('rule-wizard-severity', '22');
    change('rule-wizard-lane', 'Do');
    expect(optionValues('rule-wizard-severity')).toEqual(['', '11']);
    expect(valueOf('rule-wizard-severity')).toBe('');
  });

  it('cannot leave Basics without a code, a name and a lane', async () => {
    setup();
    await find('rule-wizard-basics');
    expect(byId('rule-wizard-next')).toBeDisabled();
    change('rule-wizard-code', 'POL-ZZ');
    change('rule-wizard-name', 'Zed');
    expect(byId('rule-wizard-next')).toBeDisabled();
    change('rule-wizard-lane', 'Do');
    expect(byId('rule-wizard-next')).toBeEnabled();
  });
});

// ---- the condition ----------------------------------------------------------------------------------------------------------------

describe('RuleWizard - the condition is built only from the catalog', () => {
  beforeEach(async () => {
    setup();
    await fillBasics();
    await next('rule-wizard-condition');
  });

  it("offers the catalog's rule types, tables, columns, operators, date forms and joins - and no free-text JSON entry", async () => {
    expect(optionValues('rule-wizard-rule-type')).toEqual(['Existence', 'Threshold']);
    expect(optionValues('rule-wizard-subject')).toEqual(['', 'zz_widget', 'zz_gadget']);

    change('rule-wizard-subject', 'zz_widget');
    click('rule-wizard-when-row-add');
    expect(optionValues('rule-wizard-when-row-0-field')).toEqual(['', 'zz_state', 'zz_due', 'zz_colour']);
    expect(optionValues('rule-wizard-when-row-0-test')).toEqual([
      'compare:=',
      'compare:<>',
      'compare:>=',
      'compare:<=',
      'compare:>',
      'compare:<',
      'in',
      'window',
      'null:null',
      'null:notNull',
    ]);

    change('rule-wizard-when-row-0-kind', 'date');
    expect(optionValues('rule-wizard-when-row-0-date-form')).toEqual(['now', 'now-{N}d', 'now+{N}d']);

    click('rule-wizard-add-clause');
    expect(optionValues('rule-wizard-clause-0-related')).toEqual(['', 'zz_note', 'zz_mail']);

    // No text area, and no input that takes a rule body, anywhere in the step.
    expect(document.querySelectorAll('textarea')).toHaveLength(0);
    expect(screen.queryByLabelText(/json/i)).toBeNull();
  });

  it("offers a Threshold only the tables that have a measure, and only that table's measures", () => {
    change('rule-wizard-rule-type', 'Threshold');
    expect(optionValues('rule-wizard-subject')).toEqual(['', 'zz_widget']);
    change('rule-wizard-subject', 'zz_widget');
    expect(optionValues('rule-wizard-threshold-field')).toEqual(['', 'zz_load']);
    expect(optionValues('rule-wizard-threshold-operator')).toEqual(['>=', '<=']);
    change('rule-wizard-threshold-field', 'zz_load');
    expect(byId('rule-wizard-threshold-range')).toHaveTextContent('0 to 250');
  });

  it("stops adding related-record tests at the catalog's limit, and offers each column once per filter", () => {
    change('rule-wizard-subject', 'zz_widget');
    click('rule-wizard-add-clause');
    click('rule-wizard-add-clause');
    expect(byId('rule-wizard-add-clause')).toBeDisabled();

    click('rule-wizard-when-row-add');
    change('rule-wizard-when-row-0-field', 'zz_state');
    click('rule-wizard-when-row-add');
    expect(optionValues('rule-wizard-when-row-1-field')).toEqual(['', 'zz_due', 'zz_colour']);
  });

  it('offers triage categories by name and sends their ids', () => {
    change('rule-wizard-subject', 'zz_widget');
    click('rule-wizard-add-clause');
    change('rule-wizard-clause-0-related', 'zz_mail');
    change('rule-wizard-clause-0-row-0-field', 'zz_triage');

    expect(
      Array.from((byId('rule-wizard-clause-0-row-0-category') as HTMLSelectElement).options).map(o => o.textContent)
    ).toEqual(['Choose a category', 'Cheese', 'Biscuits (off)']);
    change('rule-wizard-clause-0-row-0-category', 'cat-aaa');

    const preview = byId('rule-wizard-body-preview').textContent as string;
    expect(preview).toContain('"zz_triage": "cat-aaa"');
    expect(preview).not.toContain('Cheese');
  });

  it('forgets the values chosen for a column when the row is pointed at another column: the hidden ids are never sent', () => {
    change('rule-wizard-subject', 'zz_widget');
    click('rule-wizard-add-clause');
    change('rule-wizard-clause-0-related', 'zz_mail');
    change('rule-wizard-clause-0-row-0-field', 'zz_triage');
    change('rule-wizard-clause-0-row-0-test', 'in');
    const tick = byId('rule-wizard-clause-0-row-0-category-cat-aaa');
    fireEvent.click(tick.querySelector('input') ?? tick);
    expect(byId('rule-wizard-body-preview').textContent).toContain('cat-aaa');

    change('rule-wizard-clause-0-row-0-field', 'zz_noted');
    // The new column starts empty: the old column's ticked category is gone, not merely hidden.
    expect(valueOf('rule-wizard-clause-0-row-0-list')).toBe('');
    change('rule-wizard-clause-0-row-0-list', '100000001');

    const preview = byId('rule-wizard-body-preview').textContent as string;
    expect(preview).toContain('100000001');
    expect(preview).not.toContain('cat-aaa');
  });

  it('starts the value and the list empty when the column changes', () => {
    change('rule-wizard-subject', 'zz_widget');
    click('rule-wizard-add-clause');
    change('rule-wizard-clause-0-related', 'zz_note');
    change('rule-wizard-clause-0-row-0-field', 'zz_kind');
    change('rule-wizard-clause-0-row-0-value', 'abc');
    change('rule-wizard-clause-0-row-0-field', 'zz_noted');
    expect(valueOf('rule-wizard-clause-0-row-0-value')).toBe('');

    change('rule-wizard-clause-0-row-0-test', 'in');
    change('rule-wizard-clause-0-row-0-list', '1,2');
    change('rule-wizard-clause-0-row-0-field', 'zz_kind');
    expect(valueOf('rule-wizard-clause-0-row-0-list')).toBe('');
  });

  it('says what is missing and keeps Next shut until the condition can be written', () => {
    expect(byId('rule-wizard-next')).toBeDisabled();
    expect(byId('rule-wizard-condition-summary')).toHaveTextContent('Choose what the rule is about.');
    change('rule-wizard-subject', 'zz_widget');
    expect(byId('rule-wizard-next')).toBeEnabled();
    click('rule-wizard-when-row-add');
    expect(byId('rule-wizard-next')).toBeDisabled();
    expect(byId('rule-wizard-condition-summary')).toHaveTextContent('Choose a column.');
  });
});

// ---- refusals -------------------------------------------------------------------------------------------------------------------

describe('RuleWizard - a refusal is shown at the field and the step it concerns', () => {
  it("validate (200): the BFF's message appears on the Condition step and holds the wizard; an edit clears it", async () => {
    const validate = jest.fn(async () => ({
      ok: true as const,
      data: validation({
        valid: false,
        errors: [{ field: 'ruleBody', message: 'The rule reads zz_secret, which a rule may not use.' }],
        reasonCode: REASON_COLUMN_NOT_AUTHORABLE,
        templateEligibleFields: [],
      }),
    }));
    setup({ service: makeAuthoringService({ validate }) });
    await fillBasics();
    await next('rule-wizard-condition');
    await fillCondition();

    click('rule-wizard-next');
    expect(await find('field-error-ruleBody')).toHaveTextContent('zz_secret');
    expect(maybe('rule-wizard-wording')).toBeNull();
    expect(byId('rule-wizard-condition')).toBeInTheDocument();

    change('rule-wizard-when-row-0-value', 'closed');
    await waitFor(() => expect(maybe('field-error-ruleBody')).toBeNull());
  });

  it('validate (200): a reason code with no listed message still puts its own message on its field', async () => {
    const validate = jest.fn(async () => ({
      ok: true as const,
      data: validation({
        valid: false,
        errors: [],
        reasonCode: REASON_COLUMN_NOT_AUTHORABLE,
        templateEligibleFields: [],
      }),
    }));
    setup({ service: makeAuthoringService({ validate }) });
    await fillBasics();
    await next('rule-wizard-condition');
    await fillCondition();

    click('rule-wizard-next');
    expect(await find('field-error-ruleBody')).toHaveTextContent(/column/i);
    expect(maybe('rule-wizard-wording')).toBeNull();
  });

  it('validate: the refusals of later steps are not shown early, and each step shows its own when it asks', async () => {
    const validate = jest.fn(async () => ({
      ok: true as const,
      data: validation({
        valid: false,
        errors: [
          { field: 'messageTemplate', message: 'The message names a value this condition does not offer.' },
          { field: 'shortHeadline', message: 'Enter a headline.' },
        ],
      }),
    }));
    setup({ service: makeAuthoringService({ validate }) });
    await fillBasics();
    await next('rule-wizard-condition');
    await fillCondition();
    await next('rule-wizard-wording');

    // Condition asked and was accepted; its answer about the wording is not shown before the wording is attempted.
    expect(maybe('field-error-messageTemplate')).toBeNull();
    expect(maybe('field-error-shortHeadline')).toBeNull();

    click('rule-wizard-next');
    expect(await find('field-error-messageTemplate')).toHaveTextContent('does not offer');
    expect(byId('field-error-shortHeadline')).toHaveTextContent('Enter a headline.');
    expect(maybe('rule-wizard-actions')).toBeNull();
  });

  it('a validate call that fails is shown, and the wizard does not move on', async () => {
    const validate = jest.fn(async () =>
      failure({ status: 503, message: 'The rule service could not be reached. Try again.' })
    );
    setup({ service: makeAuthoringService({ validate }) });
    await fillBasics();
    await next('rule-wizard-condition');
    await fillCondition();

    click('rule-wizard-next');
    expect(await find('rule-wizard-validate-failure')).toHaveTextContent('could not be reached');
    expect(maybe('rule-wizard-wording')).toBeNull();
  });
});

// ---- wording ----------------------------------------------------------------------------------------------------------------------

describe('RuleWizard - wording', () => {
  it('offers only the fields validate returned as templateEligibleFields, never the other authorable columns', async () => {
    const validate = jest.fn(async () => ({
      ok: true as const,
      data: validation({ templateEligibleFields: ['zz_state', 'zz_due'] }),
    }));
    setup({ service: makeAuthoringService({ validate }) });
    await fillBasics();
    await next('rule-wizard-condition');
    await fillCondition();
    await next('rule-wizard-wording');

    expect(maybe('rule-wizard-eligible-zz_state')).not.toBeNull();
    expect(maybe('rule-wizard-eligible-zz_due')).not.toBeNull();
    // An authorable column of the table that the BFF did not return is not offered.
    expect(maybe('rule-wizard-eligible-zz_colour')).toBeNull();

    click('rule-wizard-eligible-zz_state');
    expect(valueOf('rule-wizard-message')).toBe('{{zz_state}}');

    change('rule-wizard-message', 'See {{zz_colour}}.');
    expect(byId('rule-wizard-wording-unknown')).toHaveTextContent('{{zz_colour}}');
  });

  it('says so when the condition lets the message quote nothing, and does not template the headline (D-23)', async () => {
    const validate = jest.fn(async () => ({ ok: true as const, data: validation({ templateEligibleFields: [] }) }));
    setup({ service: makeAuthoringService({ validate }) });
    await fillBasics();
    await next('rule-wizard-condition');
    await fillCondition();
    await next('rule-wizard-wording');

    expect(byId('rule-wizard-eligible-none')).toBeInTheDocument();
    expect(screen.getByText(/Values from the record are not filled in here/)).toBeInTheDocument();
  });
});

// ---- actions ----------------------------------------------------------------------------------------------------------------------

describe('RuleWizard - actions come from the lane catalog', () => {
  async function toActions() {
    await fillBasics();
    await next('rule-wizard-condition');
    await fillCondition();
    await next('rule-wizard-wording');
    fillWording();
    await next('rule-wizard-actions');
  }

  it("lists the lane's codes once each, with the catalog's excludes, and the lane's dismissal reasons for information", async () => {
    setup();
    await toActions();

    expect(maybe('rule-wizard-code-frobnicate')).not.toBeNull();
    expect(maybe('rule-wizard-code-ring-bell')).not.toBeNull();
    // The Do lane's code is not offered for a Decide rule.
    expect(maybe('rule-wizard-code-tidy-up')).toBeNull();

    expect(byId('rule-wizard-code-frobnicate-excludes')).toHaveTextContent('Cannot be combined with: Un-frobnicate it');
    expect(byId('rule-wizard-dismissal-reasons')).toHaveTextContent('It is Tuesday');
    expect(byId('rule-wizard-dismissal-reasons')).toHaveTextContent('It is raining');
    expect(byId('rule-wizard-dismissal-reasons')).not.toHaveTextContent('The moon is up');
  });

  it('never offers or chooses one code in both Actions and Next steps', async () => {
    const { service } = setup();
    await toActions();

    // "both-ways" is in the lane's actions AND its next steps: one row, with both roles on it.
    expect(screen.getAllByTestId('rule-wizard-code-both-ways')).toHaveLength(1);
    expect(optionValues('rule-wizard-code-both-ways-role')).toEqual(['', 'action', 'nextStep']);
    expect(optionValues('rule-wizard-code-frobnicate-role')).toEqual(['', 'action']);
    expect(optionValues('rule-wizard-code-ring-bell-role')).toEqual(['', 'nextStep']);

    change('rule-wizard-code-both-ways-role', 'action');
    change('rule-wizard-code-both-ways-role', 'nextStep');
    change('rule-wizard-code-frobnicate-role', 'action');
    click('rule-wizard-next');
    await find('rule-wizard-scope');

    const plan = JSON.parse(
      service.validate.mock.calls[service.validate.mock.calls.length - 1][0].draft.decisionPlan
    ) as {
      actions: string[];
      nextSteps: string[];
    };
    expect(plan).toEqual({ actions: ['frobnicate'], nextSteps: ['both-ways'] });
    expect(plan.actions.filter(code => plan.nextSteps.includes(code))).toEqual([]);
  });

  it('disables a code that cannot be combined with one already chosen, and says which', async () => {
    setup();
    await toActions();

    change('rule-wizard-code-frobnicate-role', 'action');
    expect(byId('rule-wizard-code-defrobnicate-role')).toBeDisabled();
    expect(byId('rule-wizard-code-defrobnicate-blocked')).toHaveTextContent('Not available with Frobnicate it');
    // The chosen one can still be cleared, which frees the other.
    change('rule-wizard-code-frobnicate-role', '');
    expect(byId('rule-wizard-code-defrobnicate-role')).toBeEnabled();
  });

  it('orders the actions: the first is the recommendation', async () => {
    const { service } = setup();
    await toActions();

    change('rule-wizard-code-frobnicate-role', 'action');
    change('rule-wizard-code-both-ways-role', 'action');
    click('rule-wizard-action-up-both-ways');
    click('rule-wizard-next');
    await find('rule-wizard-scope');

    const last = service.validate.mock.calls[service.validate.mock.calls.length - 1][0];
    expect(JSON.parse(last.draft.decisionPlan)).toEqual({ actions: ['both-ways', 'frobnicate'], nextSteps: [] });
  });
});

// ---- scope and noise ----------------------------------------------------------------------------------------------------------------

describe('RuleWizard - scope and noise', () => {
  it("offers the quiet window for the Decide lane only, with the catalog's default and limit, and checks the number", async () => {
    setup();
    await fillBasics();
    await next('rule-wizard-condition');
    await fillCondition();
    await next('rule-wizard-wording');
    fillWording();
    await next('rule-wizard-actions');
    change('rule-wizard-code-frobnicate-role', 'action');
    await next('rule-wizard-scope');

    expect(byId('rule-wizard-scope')).toHaveTextContent('Leave it empty for 21');
    expect(byId('rule-wizard-scope')).toHaveTextContent('The most allowed is 90');
    change('rule-wizard-quiet-window', '120');
    expect(byId('field-problem-quietWindowDays')).toHaveTextContent('0 to 90');
    expect(byId('rule-wizard-next')).toBeDisabled();
    change('rule-wizard-quiet-window', '30');
    expect(maybe('field-problem-quietWindowDays')).toBeNull();
    expect(byId('rule-wizard-next')).toBeEnabled();
  });
});

// ---- the end of a new rule ------------------------------------------------------------------------------------------------------------

describe('RuleWizard - a new rule', () => {
  it('saves Off through the authoring service with the body and plan that were validated and tested, then reports it', async () => {
    const { service, onClose } = setup();
    await toTestAsNewRule();
    await runTest();
    expect(byId('rule-wizard-count-new')).toHaveTextContent('6');
    click('rule-wizard-next');
    await find('rule-wizard-save');

    fireEvent.click(screen.getByRole('button', { name: 'Save rule', hidden: true }));
    expect(await find('rule-wizard-saved')).toHaveTextContent('It is Off');

    expect(service.savePolicy).toHaveBeenCalledTimes(1);
    const sent = service.savePolicy.mock.calls[0][0];
    expect(sent).toMatchObject({
      policyCode: 'POL-ZZ',
      name: 'Zed rule',
      lane: 'Decide',
      shortName: null,
      priority: null,
    });
    expect(JSON.parse(sent.draft.ruleBody)).toEqual({
      type: 'Existence',
      subject: 'zz_widget',
      when: { zz_state: 'open' },
      all: [],
    });
    expect(JSON.parse(sent.draft.decisionPlan)).toEqual({ actions: ['frobnicate'], nextSteps: [] });

    // The dry run's proof is bound to these exact strings, so validate, the dry run and the save must carry the same ones.
    const dry = service.dryRun.mock.calls[0][0];
    const validated = service.validate.mock.calls[service.validate.mock.calls.length - 1][0];
    expect(dry.ruleBody).toBe(sent.draft.ruleBody);
    expect(dry.messageTemplate).toBe(sent.draft.messageTemplate);
    expect(dry.lane).toBe('Decide');
    expect(validated.draft).toEqual(sent.draft);

    fireEvent.click(screen.getByRole('button', { name: 'Done', hidden: true }));
    expect(onClose).toHaveBeenCalledWith({ kind: 'saved', policyId: saved.policyId, versionNumber: 1 });
  });

  it('refuses a save the BFF refuses (400): the message is on the field, the banner leads to its step, and nothing is closed', async () => {
    const message =
      'Each code may appear once, in actions or in next steps, never twice in one list and never in both.';
    const savePolicy = jest.fn(async () =>
      failure({
        status: 400,
        reasonCode: REASON_DUPLICATE_ACTION,
        fieldErrors: { decisionPlan: [message] },
        message,
      })
    );
    const { onClose } = setup({ service: makeAuthoringService({ savePolicy }) });
    await toTestAsNewRule();
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');

    fireEvent.click(screen.getByRole('button', { name: 'Save rule', hidden: true }));
    const banner = await find('rule-wizard-problems');
    expect(banner).toHaveTextContent(message);

    click('rule-wizard-problem-decisionPlan');
    expect(await find('rule-wizard-actions')).toBeInTheDocument();
    expect(await find('field-error-decisionPlan')).toHaveTextContent(message);
    expect(onClose).not.toHaveBeenCalled();
  });

  it('puts a code already in use on the code field, on the Basics step', async () => {
    const savePolicy = jest.fn(async () =>
      failure({ status: 409, reasonCode: REASON_POLICY_CODE_TAKEN, message: 'Another rule already uses this code.' })
    );
    setup({ service: makeAuthoringService({ savePolicy }) });
    await toTestAsNewRule();
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');

    fireEvent.click(screen.getByRole('button', { name: 'Save rule', hidden: true }));
    await find('rule-wizard-problems');
    click('rule-wizard-problem-policyCode');
    expect(await find('rule-wizard-basics')).toBeInTheDocument();
    expect(await find('field-error-policyCode')).toHaveTextContent('Another rule already uses this code.');
  });

  it('stops sending after a write whose outcome is unknown, and says so', async () => {
    const savePolicy = jest.fn(async () =>
      failure({ status: 503, outcomeUnknown: true, message: 'The change may not have been saved. Reload to check.' })
    );
    setup({ service: makeAuthoringService({ savePolicy }) });
    await toTestAsNewRule();
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');

    fireEvent.click(screen.getByRole('button', { name: 'Save rule', hidden: true }));
    expect(await find('rule-wizard-write-lock')).toHaveTextContent('may not have been saved');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save rule', hidden: true })).toBeDisabled());
    expect(savePolicy).toHaveBeenCalledTimes(1);
  });

  it("shows a refused save (400) once: in the banner, not again as the shell's own error", async () => {
    const message =
      'Each code may appear once, in actions or in next steps, never twice in one list and never in both.';
    const savePolicy = jest.fn(async () =>
      failure({ status: 400, reasonCode: REASON_DUPLICATE_ACTION, fieldErrors: { decisionPlan: [message] }, message })
    );
    setup({ service: makeAuthoringService({ savePolicy }) });
    await toTestAsNewRule();
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');

    fireEvent.click(screen.getByRole('button', { name: 'Save rule', hidden: true }));
    await find('rule-wizard-problems');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save rule', hidden: true })).toBeEnabled());

    expect(occurrencesOf(message)).toBe(1);
  });

  it("shows a refusal that has no field once: the shell's error bar carries it, with no banner", async () => {
    const message = 'Dataverse refused this for your account. Nothing was changed.';
    const savePolicy = jest.fn(async () => failure({ status: 403, message }));
    setup({ service: makeAuthoringService({ savePolicy }) });
    await toTestAsNewRule();
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');

    fireEvent.click(screen.getByRole('button', { name: 'Save rule', hidden: true }));
    await waitFor(() => expect(occurrencesOf(message)).toBe(1));
    expect(maybe('rule-wizard-problems')).toBeNull();
  });

  it('closes without "nothing has been saved" after a write lock, and tells the host to reload', async () => {
    const savePolicy = jest.fn(async () =>
      failure({ status: 503, outcomeUnknown: true, message: 'The change may not have been saved. Reload to check.' })
    );
    const { onClose } = setup({ service: makeAuthoringService({ savePolicy }) });
    await toTestAsNewRule();
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');
    fireEvent.click(screen.getByRole('button', { name: 'Save rule', hidden: true }));
    await find('rule-wizard-write-lock');

    fireEvent.click(screen.getByRole('button', { name: 'Cancel', hidden: true }));

    expect(screen.queryByRole('alertdialog', { hidden: true })).toBeNull();
    expect(document.body.textContent).not.toContain('Nothing has been saved');
    expect(onClose).toHaveBeenCalledWith({ kind: 'unknown', policyId: null, versionNumber: null });
  });

  it('hands the host the policy a failed save left Off, when the BFF named one', async () => {
    const savePolicy = jest.fn(async () =>
      failure({
        status: 503,
        outcomeUnknown: true,
        policyId: 'policy-half',
        message: 'The rule was created Off but not finished.',
      })
    );
    const { onClose } = setup({ service: makeAuthoringService({ savePolicy }) });
    await toTestAsNewRule();
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');
    fireEvent.click(screen.getByRole('button', { name: 'Save rule', hidden: true }));
    await find('rule-wizard-write-lock');

    fireEvent.click(screen.getByRole('button', { name: 'Cancel', hidden: true }));
    expect(onClose).toHaveBeenCalledWith({ kind: 'unknown', policyId: 'policy-half', versionNumber: null });
  });

  it('ignores the close control while a save is out, so the answer is not lost', async () => {
    let answer: (value: { ok: true; data: typeof saved }) => void = () => undefined;
    const savePolicy = jest.fn(
      () =>
        new Promise<{ ok: true; data: typeof saved }>(resolve => {
          answer = resolve;
        })
    );
    const { onClose } = setup({ service: makeAuthoringService({ savePolicy }) });
    await toTestAsNewRule();
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');
    fireEvent.click(screen.getByRole('button', { name: 'Save rule', hidden: true }));
    await waitFor(() => expect(savePolicy).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByRole('button', { name: 'Close', hidden: true }));
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.queryByRole('alertdialog', { hidden: true })).toBeNull();

    answer({ ok: true, data: saved });
    expect(await find('rule-wizard-saved')).toBeInTheDocument();
  });
});

// ---- the test gates the end ----------------------------------------------------------------------------------------------------------------

describe('RuleWizard - Test gates the end', () => {
  it('keeps Next shut on the Test step until the test has run, and Save is only reachable after it', async () => {
    setup();
    await toTestAsNewRule();

    expect(byId('rule-wizard-next')).toBeDisabled();
    expect(maybe('rule-wizard-test-result')).toBeNull();

    await runTest();
    expect(byId('rule-wizard-next')).toBeEnabled();
  });

  it('shows the counts the dry run returned, and the rows', async () => {
    setup();
    await toTestAsNewRule();
    await runTest();

    expect(byId('rule-wizard-count-open')).toHaveTextContent('4');
    expect(byId('rule-wizard-count-suppressed')).toHaveTextContent('1');
    expect(byId('rule-wizard-count-supersede')).toHaveTextContent('3');
    expect(byId('rule-wizard-count-reraise')).toHaveTextContent('2');
    expect(byId('rule-wizard-test-rows')).toHaveTextContent('Widget One');
  });

  it('makes ANY edit after a test stale: the counts go, Next is shut, and the test must be run again', async () => {
    setup();
    await toTestAsNewRule();
    await runTest();
    expect(byId('rule-wizard-next')).toBeEnabled();

    // Back to Wording (Test -> Scope -> Actions -> Wording), change one word, and come forward again.
    for (const id of ['rule-wizard-scope', 'rule-wizard-actions', 'rule-wizard-wording']) {
      click('rule-wizard-back');
      await find(id);
    }
    change('rule-wizard-proposed-action', 'Do the other thing');
    for (const id of ['rule-wizard-actions', 'rule-wizard-scope', 'rule-wizard-test']) {
      await next(id);
    }

    expect(byId('rule-wizard-test-stale')).toBeInTheDocument();
    expect(maybe('rule-wizard-test-result')).toBeNull();
    expect(byId('rule-wizard-next')).toBeDisabled();
    // There is no Save button to reach: it lives on the step after Test.
    expect(screen.queryByRole('button', { name: 'Save rule', hidden: true })).toBeNull();

    await runTest();
    expect(byId('rule-wizard-next')).toBeEnabled();
  });

  it('does not let a test that could not be recorded pass', async () => {
    const dryRun = jest.fn(async () => ({ ok: true as const, data: dryRunResult({ recorded: false }) }));
    setup({ service: makeAuthoringService({ dryRun }) });
    await toTestAsNewRule();
    await runTest();

    expect(byId('rule-wizard-test-unrecorded')).toBeInTheDocument();
    expect(byId('rule-wizard-next')).toBeDisabled();
  });

  it('shows a dry run the BFF refused, on the step it concerns', async () => {
    const dryRun = jest.fn(async () =>
      failure({
        status: 400,
        reasonCode: REASON_COLUMN_NOT_AUTHORABLE,
        fieldErrors: { ruleBody: ['The rule reads zz_secret, which a rule may not use.'] },
        message: 'The rule reads zz_secret, which a rule may not use.',
      })
    );
    setup({ service: makeAuthoringService({ dryRun }) });
    await toTestAsNewRule();

    click('rule-wizard-run-test');
    expect(await find('rule-wizard-test-failure')).toHaveTextContent('zz_secret');
    expect(byId('rule-wizard-problems')).toHaveTextContent('Condition');
    expect(byId('rule-wizard-next')).toBeDisabled();

    click('rule-wizard-problem-ruleBody');
    expect(await find('rule-wizard-condition')).toBeInTheDocument();
    expect(await find('field-error-ruleBody')).toHaveTextContent('zz_secret');
  });
});

// ---- a new version -----------------------------------------------------------------------------------------------------------------------

describe('RuleWizard - a new version', () => {
  it('prefills wording and plan, and starts the condition empty because the BFF returns no raw body (D-149)', async () => {
    setup({ mode: versionMode });
    await find('rule-wizard-basics');
    // The rule's identity is shown, not edited.
    expect(maybe('rule-wizard-code')).toBeNull();
    expect(byId('rule-wizard-basics')).toHaveTextContent('POL-A');

    await next('rule-wizard-condition');
    expect(valueOf('rule-wizard-subject')).toBe('');
    expect(maybe('rule-wizard-when-row-0')).toBeNull();
    await fillCondition();
    await next('rule-wizard-wording');

    expect(valueOf('rule-wizard-headline')).toBe('Old headline');
    expect(valueOf('rule-wizard-message')).toBe('Old {{zz_state}}');
    expect(valueOf('rule-wizard-proposed-action')).toBe('Old action');
    await next('rule-wizard-actions');
    expect(valueOf('rule-wizard-code-frobnicate-role')).toBe('action');
    expect(valueOf('rule-wizard-code-ring-bell-role')).toBe('nextStep');
    await next('rule-wizard-scope');

    // Priority and the suppression flag belong to the rule; a version shows them. The quiet window is part of the body.
    expect(maybe('rule-wizard-priority')).toBeNull();
    expect(byId('rule-wizard-scope-facts')).toHaveTextContent('tenant-1');
    expect(valueOf('rule-wizard-quiet-window')).toBe('30');
  });

  it('asks before publishing, with the supersede and re-raise counts from the dry run; Not yet changes nothing', async () => {
    const { service } = setup({ mode: versionMode });
    await toTestAsNewVersion();
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');

    fireEvent.click(screen.getByRole('button', { name: 'Publish', hidden: true }));
    const confirm = await find('rule-wizard-publish-confirm');
    expect(confirm).toHaveTextContent('3 open items');
    expect(confirm).toHaveTextContent('2 items will be raised again');
    expect(service.publishVersion).not.toHaveBeenCalled();

    const dialog = confirm.closest('[role="alertdialog"]') as HTMLElement;
    fireEvent.click(within(dialog).getByRole('button', { name: 'Not yet', hidden: true }));
    await waitFor(() => expect(maybe('rule-wizard-publish-confirm')).toBeNull());
    expect(service.publishVersion).not.toHaveBeenCalled();
    expect(byId('rule-wizard-save')).toBeInTheDocument();
  });

  it('publishes on confirmation, with the body that was validated and tested, and reports what the re-check did', async () => {
    const { service, onClose } = setup({ mode: versionMode });
    await toTestAsNewVersion();
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');

    fireEvent.click(screen.getByRole('button', { name: 'Publish', hidden: true }));
    const confirm = await find('rule-wizard-publish-confirm');
    const dialog = confirm.closest('[role="alertdialog"]') as HTMLElement;
    fireEvent.click(within(dialog).getByRole('button', { name: 'Publish', hidden: true }));

    expect(await find('rule-wizard-published')).toHaveTextContent('Version 3 of POL-A is now in force');
    expect(byId('rule-wizard-published')).toHaveTextContent('3 open items were replaced and 2 items were raised');

    expect(service.publishVersion).toHaveBeenCalledTimes(1);
    const [policyId, request] = service.publishVersion.mock.calls[0];
    expect(policyId).toBe('policy-1');
    const dry = service.dryRun.mock.calls[0][0];
    expect(dry.policyId).toBe('policy-1');
    expect(dry.lane).toBeUndefined();
    expect(request.draft.ruleBody).toBe(dry.ruleBody);
    expect(request.draft.messageTemplate).toBe(dry.messageTemplate);
    // The quiet window carried over from the rule is written into the rebuilt body (Decide lane).
    expect(JSON.parse(request.draft.ruleBody)).toMatchObject({ quietWindowDays: 30 });
    expect(service.validate.mock.calls[0][0]).toMatchObject({ policyId: 'policy-1' });

    // Fluent's focus manager can leave the dialog marked aria-hidden in jsdom after a nested confirmation closes; it is still the button.
    fireEvent.click(screen.getByRole('button', { name: 'Done', hidden: true }));
    expect(onClose).toHaveBeenCalledWith({ kind: 'published', policyId: published.policyId, versionNumber: 3 });
  });

  async function toPublishConfirmation(mode: NewVersionMode, service?: OntologyAuthoringService) {
    const set = setup({ mode, service });
    await toTestAsNewVersion();
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');
    fireEvent.click(screen.getByRole('button', { name: 'Publish', hidden: true }));
    const confirm = await find('rule-wizard-publish-confirm');
    return { ...set, confirm, dialog: confirm.closest('[role="alertdialog"]') as HTMLElement };
  }

  it("no longer says a scoped rule's counts are worked out without its scope: the test runs within it (task 102b, DI-112)", async () => {
    const scoped: NewVersionMode = { ...versionMode, scope: { tenant: null, matter: 'Matter Nine', matterId: 'm-9' } };
    const { confirm } = await toPublishConfirmation(scoped);
    expect(confirm).toHaveTextContent('3 open items');
    expect(confirm).not.toHaveTextContent('without the rule');
    expect(confirm).not.toHaveTextContent('may be lower');
  });

  it('marks the test stale when the BFF no longer holds its proof (409 dry_run_required), and says it must be run again', async () => {
    const publishVersion = jest.fn(async () =>
      failure({
        status: 409,
        reasonCode: REASON_DRY_RUN_REQUIRED,
        message: 'Run Test on this exact rule before saving it; any edit needs a new test run.',
      })
    );
    const { dialog } = await toPublishConfirmation(versionMode, makeAuthoringService({ publishVersion }));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Publish', hidden: true }));
    await waitFor(() => expect(publishVersion).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish', hidden: true })).toBeDisabled());

    // Back to the Test step: the result is gone and the step says the proof expired.
    fireEvent.click(screen.getByRole('button', { name: 'Back', hidden: true }));
    await find('rule-wizard-test');
    expect(byId('rule-wizard-test-stale')).toHaveTextContent('expired');
    expect(maybe('rule-wizard-test-result')).toBeNull();
    expect(byId('rule-wizard-next')).toBeDisabled();
  });

  it('shows a publish the BFF refused (400) on the field it concerns', async () => {
    const message = 'The message names a value this condition does not offer.';
    const publishVersion = jest.fn(async () =>
      failure({ status: 400, fieldErrors: { messageTemplate: [message] }, message })
    );
    setup({ mode: versionMode, service: makeAuthoringService({ publishVersion }) });
    await toTestAsNewVersion();
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');

    fireEvent.click(screen.getByRole('button', { name: 'Publish', hidden: true }));
    const confirm = await find('rule-wizard-publish-confirm');
    fireEvent.click(
      within(confirm.closest('[role="alertdialog"]') as HTMLElement).getByRole('button', {
        name: 'Publish',
        hidden: true,
      })
    );

    await find('rule-wizard-problems');
    click('rule-wizard-problem-messageTemplate');
    await find('rule-wizard-wording');
    expect(await find('field-error-messageTemplate')).toHaveTextContent(message);
  });
});

// ---- closing --------------------------------------------------------------------------------------------------------------------------------

describe('RuleWizard - closing', () => {
  it('Escape and the backdrop never close it; Cancel closes at once when nothing was entered', async () => {
    const { onClose } = setup();
    await find('rule-wizard-basics');

    fireEvent.keyDown(screen.getByRole('dialog', { hidden: true }), { key: 'Escape', code: 'Escape' });
    const backdrop = document.querySelector('.fui-DialogSurface__backdrop, [class*="backdrop"]');
    if (backdrop) {
      fireEvent.click(backdrop);
    }
    expect(onClose).not.toHaveBeenCalled();
    expect(byId('rule-wizard-basics')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Cancel', hidden: true }));
    expect(onClose).toHaveBeenCalledWith(null);
  });

  it('asks before throwing away what was entered, and keeps editing when told to', async () => {
    const { onClose } = setup();
    await find('rule-wizard-basics');
    change('rule-wizard-name', 'Half done');

    fireEvent.click(screen.getByRole('button', { name: 'Cancel', hidden: true }));
    const check = await screen.findByRole('alertdialog', undefined, WAIT);
    expect(check).toHaveTextContent('Discard this rule?');
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.click(within(check).getByRole('button', { name: 'Keep editing', hidden: true }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull());
    expect(valueOf('rule-wizard-name')).toBe('Half done');

    fireEvent.click(screen.getByRole('button', { name: 'Cancel', hidden: true }));
    const again = await screen.findByRole('alertdialog', undefined, WAIT);
    fireEvent.click(within(again).getByRole('button', { name: 'Discard', hidden: true }));
    expect(onClose).toHaveBeenCalledWith(null);
  });
});

// ---- the theme ---------------------------------------------------------------------------------------------------------------------------------

describe('RuleWizard - theme', () => {
  it('renders under the dark theme (a smoke check; the real dark-mode check needs a browser)', async () => {
    setup({ theme: webDarkTheme });
    expect(await find('rule-wizard-basics')).toBeInTheDocument();
  });
});

// ---- the matter scope (task 102b; D-165, DI-62, DI-112) ----------------------------------------------------------------------------

describe('RuleWizard - matter scope', () => {
  const MATTERS = [
    { id: 'm-1', name: 'Matter One' },
    { id: 'm-2', name: 'Matter Two' },
  ];
  /** The host's picker: an invented subject (the wizard names no table, D-149) and a search the test can read. */
  const makePicker = () => ({ subject: 'zz_widget', search: jest.fn(async (_query: string) => MATTERS) });

  /** New rule about `subject`, through to the Scope and noise step. */
  async function toScopeAsNewRule(subject = 'zz_widget') {
    await fillBasics();
    await next('rule-wizard-condition');
    if (subject === 'zz_widget') {
      await fillCondition();
    } else {
      change('rule-wizard-subject', subject);
    }
    await next('rule-wizard-wording');
    fillWording();
    await next('rule-wizard-actions');
    change('rule-wizard-code-frobnicate-role', 'action');
    await next('rule-wizard-scope');
  }

  const matterInput = () => screen.getByRole('textbox', { name: 'Limit to one matter', hidden: true });

  async function pickMatter(name: string) {
    fireEvent.change(matterInput(), { target: { value: name.split(' ')[1] } });
    fireEvent.click(await screen.findByRole('option', { name, hidden: true }, WAIT));
    await waitFor(() => expect(byId('rule-wizard-scope-value')).toHaveTextContent(`Applies to: ${name}`), WAIT);
  }

  it("offers the shared matter picker only for a rule about the picker's subject, with All matters by default", async () => {
    setup({ matterPicker: makePicker() });
    await toScopeAsNewRule('zz_widget');

    expect(byId('rule-wizard-matter-picker')).toBeInTheDocument();
    expect(byId('rule-wizard-scope-value')).toHaveTextContent('Applies to: All matters');
    expect(byId('rule-wizard-scope')).toHaveTextContent('Leave it empty and the rule applies to all matters.');
  });

  it('does not offer it for a rule about anything else (must-not-fire)', async () => {
    setup({ matterPicker: makePicker() });
    await toScopeAsNewRule('zz_gadget');

    expect(maybe('rule-wizard-matter-picker')).toBeNull();
    expect(byId('rule-wizard-scope-not-offered')).toHaveTextContent(
      'A matter limit is offered only for a rule about matters.'
    );
    expect(byId('rule-wizard-scope-value')).toHaveTextContent('Applies to: All matters');
  });

  it('offers no picker when the host has no matter search', async () => {
    setup();
    await toScopeAsNewRule('zz_widget');

    expect(maybe('rule-wizard-matter-picker')).toBeNull();
    expect(byId('rule-wizard-scope-no-picker')).toBeInTheDocument();
  });

  it('searches through the host, and sends ONLY the chosen id to validate, the test and the save', async () => {
    const matterPicker = makePicker();
    const { service } = setup({ matterPicker });
    await toScopeAsNewRule();

    await pickMatter('Matter Two');
    expect(matterPicker.search).toHaveBeenCalledWith('Two');
    await next('rule-wizard-test');
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');
    fireEvent.click(screen.getByRole('button', { name: 'Save rule', hidden: true }));
    await find('rule-wizard-saved');

    const scopeValidate = service.validate.mock.calls[service.validate.mock.calls.length - 1][0];
    expect(scopeValidate.matterId).toBe('m-2');
    expect('allMatters' in scopeValidate).toBe(false);
    expect(service.dryRun.mock.calls[0][0].matterId).toBe('m-2');
    const save = service.savePolicy.mock.calls[0][0];
    expect(save.matterId).toBe('m-2');
    for (const request of [scopeValidate, service.dryRun.mock.calls[0][0], save]) {
      expect(JSON.stringify(request)).not.toContain('Matter Two');
      expect(JSON.stringify(request)).not.toContain('tenant');
    }
  });

  it('sends no matter for all matters, and says allMatters on validate and the test (fix rounds 1 and 2)', async () => {
    const { service } = setup({ matterPicker: makePicker() });
    await toScopeAsNewRule();
    await next('rule-wizard-test');
    await runTest();

    expect('matterId' in service.dryRun.mock.calls[0][0]).toBe(false);
    expect(service.dryRun.mock.calls[0][0].allMatters).toBe(true);
    expect(service.validate.mock.calls.length).toBeGreaterThan(0);
    for (const [request] of service.validate.mock.calls) {
      expect('matterId' in request).toBe(false);
      expect(request.allMatters).toBe(true); // fix round 2 (K2): validate says the intent like the test and the publish
    }
  });

  it('a test run within one matter is not current after the matter changes', async () => {
    setup({ matterPicker: makePicker() });
    await toScopeAsNewRule();
    await pickMatter('Matter One');
    await next('rule-wizard-test');
    await runTest();
    click('rule-wizard-back');
    await find('rule-wizard-scope');

    fireEvent.click(screen.getByRole('button', { name: 'Clear Limit to one matter', hidden: true }));
    await pickMatter('Matter Two');
    await next('rule-wizard-test');

    expect(maybe('rule-wizard-test-result')).toBeNull();
    expect(byId('rule-wizard-next')).toBeDisabled();
  });

  const scopedVersion: NewVersionMode = {
    ...versionMode,
    scope: { tenant: null, matter: 'Matter Nine', matterId: 'm-9' },
  };

  async function toScopeAsNewVersion() {
    await find('rule-wizard-basics');
    await next('rule-wizard-condition');
    await fillCondition();
    await next('rule-wizard-wording');
    await next('rule-wizard-actions');
    await next('rule-wizard-scope');
  }

  /** Publishes from the Scope and noise step; answers what the confirmation said about the scope (null: nothing). */
  async function publishFromScope(): Promise<string | null> {
    await next('rule-wizard-test');
    await runTest();
    click('rule-wizard-next');
    await find('rule-wizard-save');
    fireEvent.click(screen.getByRole('button', { name: 'Publish', hidden: true }));
    const confirm = await find('rule-wizard-publish-confirm');
    const scopeChange = maybe('rule-wizard-publish-scope-change')?.textContent?.trim() ?? null;
    fireEvent.click(
      within(confirm.closest('[role="alertdialog"]') as HTMLElement).getByRole('button', {
        name: 'Publish',
        hidden: true,
      })
    );
    await find('rule-wizard-published');
    return scopeChange;
  }

  it('a new version shows the scope in force, starts with it and publishes it unchanged', async () => {
    const { service } = setup({ mode: scopedVersion, matterPicker: makePicker() });
    await toScopeAsNewVersion();

    expect(byId('rule-wizard-scope-facts')).toHaveTextContent('In force now');
    expect(byId('rule-wizard-scope-facts')).toHaveTextContent('Matter Nine');
    expect(byId('rule-wizard-scope-value')).toHaveTextContent('Applies to: Matter Nine');
    const scopeChange = await publishFromScope();

    expect(service.dryRun.mock.calls[0][0].matterId).toBe('m-9');
    expect(service.publishVersion.mock.calls[0][1].matterId).toBe('m-9');
    expect('allMatters' in service.publishVersion.mock.calls[0][1]).toBe(false);
    expect(scopeChange).toBeNull();
  });

  it('a new version can change the scope to all matters: the test and the publish say allMatters, and the confirmation says so', async () => {
    const { service } = setup({ mode: scopedVersion, matterPicker: makePicker() });
    await toScopeAsNewVersion();

    fireEvent.click(screen.getByRole('button', { name: 'Clear Limit to one matter', hidden: true }));
    expect(byId('rule-wizard-scope-value')).toHaveTextContent('Applies to: All matters');
    expect(byId('rule-wizard-scope-facts')).toHaveTextContent('Matter Nine'); // the scope in force until the version is published
    const scopeChange = await publishFromScope();

    expect('matterId' in service.dryRun.mock.calls[0][0]).toBe(false);
    expect('matterId' in service.publishVersion.mock.calls[0][1]).toBe(false);
    // Fix round 1 (F4): "All matters" is said explicitly; a publish with no scope intent is refused by the BFF.
    expect(service.dryRun.mock.calls[0][0].allMatters).toBe(true);
    expect(service.publishVersion.mock.calls[0][1].allMatters).toBe(true);
    expect(scopeChange).toBe('This changes the rule from Matter Nine to all matters.');
  });

  it('the confirmation names the scope the test read as stored, and an unscoped rule kept unscoped says nothing (fix round 1, F4)', async () => {
    // The test read a stored scope (Matter Seven) other than the one the page showed: the sentence is about what the publish changes.
    const dryRun = jest.fn(async () => ({
      ok: true as const,
      data: dryRunResult({ storedMatterId: 'm-7', storedMatterName: 'Matter Seven' }),
    }));
    setup({ service: makeAuthoringService({ dryRun }), mode: scopedVersion, matterPicker: makePicker() });
    await toScopeAsNewVersion();
    fireEvent.click(screen.getByRole('button', { name: 'Clear Limit to one matter', hidden: true }));
    await pickMatter('Matter One');

    expect(await publishFromScope()).toBe('This changes the rule from Matter Seven to Matter One.');
  });

  it('an unscoped rule published for all matters states no scope change and still says allMatters (fix round 1, F4)', async () => {
    const { service } = setup({ mode: versionMode, matterPicker: makePicker() });
    await toScopeAsNewVersion();

    expect(await publishFromScope()).toBeNull();
    expect(service.publishVersion.mock.calls[0][1].allMatters).toBe(true);
  });

  it('a new version can change the scope to another matter', async () => {
    const { service } = setup({ mode: scopedVersion, matterPicker: makePicker() });
    await toScopeAsNewVersion();

    fireEvent.click(screen.getByRole('button', { name: 'Clear Limit to one matter', hidden: true }));
    await pickMatter('Matter One');
    const scopeChange = await publishFromScope();

    expect(service.dryRun.mock.calls[0][0].matterId).toBe('m-1');
    expect(service.publishVersion.mock.calls[0][1].matterId).toBe('m-1');
    expect(scopeChange).toBe('This changes the rule from Matter Nine to Matter One.');
  });

  it('places scope_subject_mismatch on the scope field, and the limit can be removed there', async () => {
    const service = makeAuthoringService({
      validate: jest.fn(async (request: { matterId?: string }) => ({
        ok: true as const,
        data:
          request.matterId === undefined
            ? validation()
            : validation({ valid: false, reasonCode: REASON_SCOPE_SUBJECT_MISMATCH, errors: [] }),
      })),
    });
    setup({ service, mode: scopedVersion, matterPicker: makePicker() });
    await find('rule-wizard-basics');
    await next('rule-wizard-condition');
    change('rule-wizard-subject', 'zz_gadget');
    // Condition, Wording and Actions ask validate with the limit still set: the refusal is not theirs, so they can be left.
    await next('rule-wizard-wording');
    await next('rule-wizard-actions');
    await next('rule-wizard-scope');

    expect(maybe('rule-wizard-matter-picker')).toBeNull();
    click('rule-wizard-next'); // leaving Scope asks validate, which refuses the limit on this step's field
    expect(await find('field-error-matterId')).toHaveTextContent(
      'A matter limit is allowed only on a rule about matters.'
    );
    expect(byId('rule-wizard-scope')).toBeInTheDocument();

    click('rule-wizard-scope-clear');
    expect(byId('rule-wizard-scope-value')).toHaveTextContent('Applies to: All matters');
    expect(maybe('field-error-matterId')).toBeNull();
    await next('rule-wizard-test');
  });

  it('shows a dry-run refusal of the matter on the Scope and noise step', async () => {
    const service = makeAuthoringService({
      dryRun: jest.fn(async () => failure({ fieldErrors: { matterId: ['The matter was not found.'] } })),
    });
    setup({ service, mode: scopedVersion, matterPicker: makePicker() });
    await toTestAsNewVersion();
    click('rule-wizard-run-test');

    expect(await find('rule-wizard-problem-matterId')).toHaveTextContent('Scope and noise');
    expect(byId('rule-wizard-problems')).toHaveTextContent('The matter was not found.');
  });
});
