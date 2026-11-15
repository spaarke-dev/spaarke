/**
 * The decision wizard in the widget, end to end (task 032; FR-13, FR-15, NFR-06). The real code runs from the host down: the lane, the
 * shared worklist service, the MCP shim, `WizardDialog`, the shared `DecisionWizardHost` / `DecisionWizard` / `DecisionItemWizard` and its
 * steps, the type-ahead and the commit. The only double is the HOST (`callServerTool`, `requestDisplayMode`, `openLink`).
 *
 * Stated contract (acceptance criteria):
 *  - one Work Item of each lookup-bearing action type (revise-budget, reassign, add-todo, assign-work, assign-task, an inquiry to an
 *    outside firm) is completed with its lookup resolved through the type-ahead; the row then leaves the lane and the count drops by one
 *  - every parameter kind (text, text area, choice, money, date, boolean, person, lookup) renders and submits
 *  - a `because` rationale typed on Confirm is sent in the review
 *  - negative: a refusal (not_authorized, a stale plan) shows the server's message and leaves the Work Item open and in the lane;
 *    decided_in_invoice_review says 'Decide this in Invoice Review'; a host that does not answer the commit says it may not have been recorded
 *  - success warnings (inquiry_email_not_archived) are worded in the wizard
 *  - negative: nothing but the plan and the lookup is called before Confirm, and the commit is never called on open, step change or close
 *  - the display chain: fullscreen granted -> the dialog; refused -> master-detail beside the lane; too narrow for that -> openLink to the Console
 */
import { configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App } from '../App';
import { DISCONNECTED, McpHostContext, type McpHostConnection } from '../mcp/mcpHostContext';
import type { DecisionReview } from '@spaarke/ui-components/components/DecisionWizard/decisionWizardTypes';
import { CONSOLE_BASE, MATTERS, guid, installResponseStandIn, toolError } from './laneFixtures';
import {
  BUDGET,
  COMPLETE_TOOL,
  FIRM,
  LOOKUP_TOOL,
  PAGE_TOOL,
  PERSON,
  PERSON_2,
  PLAN_TOOL,
  PLANS,
  everyKind,
  wizardHost,
  type ServerItem,
  type WizardHost,
  type WizardHostOptions,
} from './wizardFixtures';

installResponseStandIn();
// The type-ahead waits 300 ms in the shared field and 300 ms more in the widget before it asks the server.
configure({ asyncUtilTimeout: 4000 });

const LANE_OPEN_KEY = 'spaarke.worklist.laneOpen';

const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });
const enter = (element: HTMLElement, value: string) => fireEvent.change(element, { target: { value } });
const lane = () => screen.getByTestId('worklist-lane-Decide');
const lines = () => within(lane()).getAllByTestId('issue-line');
const lineIds = () => lines().map(l => l.getAttribute('data-signal-id'));
const count = () => screen.getByTestId('lane-count-Decide');

const items = (plan: ServerItem['plan'], more = 2): ServerItem[] => [
  { n: 1, plan, core: MATTERS.a.id },
  ...Array.from({ length: more }, (_, i) => ({ n: i + 2, plan: PLANS.reviseBudget, core: MATTERS.a.id })),
];

function mount(host: WizardHost, connection: Partial<McpHostConnection> = {}) {
  const value: McpHostConnection = {
    ...DISCONNECTED,
    isConnected: true,
    hostContext: { theme: 'light', availableDisplayModes: ['inline', 'fullscreen'] },
    callTool: host.callTool,
    openLink: host.openLink,
    requestDisplayMode: host.requestDisplayMode,
    toolResult: { consoleBaseUrl: CONSOLE_BASE },
    ...connection,
  };
  return render(
    <McpHostContext.Provider value={value}>
      <App />
    </McpHostContext.Provider>
  );
}

/** Opens the lane (collapsed by default) and the Work Item `n`'s row; resolves when the wizard shows What was found. */
async function openItem(n: number) {
  await screen.findByTestId('decide-lane');
  fireEvent.click(lines().find(l => l.getAttribute('data-signal-id') === guid('5', n))!);
  return screen.findByTestId('step-found');
}

/** Types into a type-ahead field and picks the candidate: the field waits, the widget waits, the server answers, the option is chosen. */
async function pick(placeholder: RegExp, typed: string, candidate: string) {
  const field = screen.getByPlaceholderText(placeholder);
  fireEvent.focus(field);
  enter(field, typed);
  fireEvent.click(await screen.findByText(candidate));
}

const next = () => fireEvent.click(button('Next'));
const skip = () => fireEvent.click(screen.getByRole('button', { name: /^skip$/i }));

/**
 * Waits for Confirm's Record button to be usable. Confirm holds its button for half a second after it appears (NFR-06: a double click on
 * Next must not record), so a test that means to record waits for that, as a person reading the step would.
 */
async function recordButtonReady(name: RegExp = /^(record decision|dismiss and record)$/i) {
  await screen.findByTestId('step-confirm');
  await waitFor(() => expect(screen.getByRole('button', { name })).toBeEnabled());
  return screen.getByRole('button', { name });
}

/** Records on the Confirm step with a because; resolves with the review the host received. */
async function confirmAndRecord(host: WizardHost, because = 'Because the matter settled.') {
  await screen.findByTestId('step-confirm');
  enter(screen.getByRole('textbox', { name: /^Because/ }), because);
  fireEvent.click(await recordButtonReady());
  await waitFor(() => expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(1));
  return host.callsTo(COMPLETE_TOOL)[0].args as { signalId: string; review: DecisionReview };
}

beforeEach(() => {
  window.localStorage.clear();
  window.localStorage.setItem(LANE_OPEN_KEY, JSON.stringify({ Decide: true }));
});

describe('each lookup-bearing action type is completed with its lookup resolved through the type-ahead', () => {
  const cases: Array<{
    name: string;
    plan: ServerItem['plan'];
    fill: () => Promise<void>;
    values: (review: DecisionReview) => unknown;
    expected: unknown;
  }> = [
    {
      name: 'revise-budget (a budget Lookup)',
      plan: PLANS.reviseBudget,
      fill: async () => {
        await screen.findByTestId('step-action-revise-budget');
        await pick(/search budget to revise/i, 'fy26 lit', BUDGET.name);
        enter(screen.getByRole('textbox', { name: /^New budget/ }), '250000');
        enter(screen.getByRole('textbox', { name: /^Reason for revision/ }), 'Appeal added');
        next();
      },
      values: r => r.actions[0].values,
      expected: { budget: BUDGET.id, amount: 250000, reason: 'Appeal added' },
    },
    {
      name: 'reassign (a Person)',
      plan: PLANS.reassign,
      fill: async () => {
        await screen.findByTestId('step-action-reassign');
        await pick(/search reassign to/i, 'dan', PERSON_2.name);
        next();
      },
      values: r => r.actions[0].values,
      expected: { assignee: PERSON_2.id },
    },
    {
      name: 'add-todo (a Person, as a Next step)',
      plan: PLANS.addTodo,
      fill: async () => {
        await screen.findByTestId('step-action-approve-variance');
        skip();
        await screen.findByTestId('step-next');
        fireEvent.click(screen.getByRole('checkbox', { name: /add to do/i }));
        await waitFor(() => expect(button('Next')).toBeEnabled());
        next();
        await screen.findByTestId('step-next-add-todo');
        enter(screen.getByRole('textbox', { name: /^To Do/ }), 'Chase the budget reply');
        await pick(/search assigned to/i, 'dana', PERSON.name);
        fireEvent.change(screen.getByLabelText(/^Due/), { target: { value: '2026-10-20' } });
        next();
      },
      values: r => r.nextSteps?.[0].values,
      expected: { title: 'Chase the budget reply', assignee: PERSON.id, dueDate: '2026-10-20' },
    },
    {
      name: 'assign-work (a Person)',
      plan: PLANS.assignWork,
      fill: async () => {
        await screen.findByTestId('step-action-assign-work');
        await pick(/search assign to/i, 'dana', PERSON.name);
        enter(screen.getByRole('textbox', { name: /^What to do/ }), 'Review the engagement letter');
        next();
      },
      values: r => r.actions[0].values,
      expected: { assignee: PERSON.id, description: 'Review the engagement letter' },
    },
    {
      name: 'assign-task (a Person and a Boolean)',
      plan: PLANS.assignTask,
      fill: async () => {
        await screen.findByTestId('step-action-assign-task');
        await pick(/search assign to/i, 'dan', PERSON_2.name);
        enter(screen.getByRole('textbox', { name: /^What to do/ }), 'Send the file');
        next();
      },
      values: r => r.actions[0].values,
      expected: { assignee: PERSON_2.id, description: 'Send the file', notify: true },
    },
    {
      name: 'an inquiry to an outside firm (a Lookup on an organization)',
      plan: PLANS.inquiry,
      fill: async () => {
        await screen.findByTestId('step-action-send-budget-inquiry');
        enter(screen.getByRole('textbox', { name: /^To\b/ }), 'budgets@acme.example');
        enter(screen.getByRole('textbox', { name: /^Subject/ }), 'Budget question');
        enter(screen.getByRole('textbox', { name: /^Message/ }), 'Please confirm the budget.');
        await pick(/search outside firm/i, 'acme', FIRM.name);
        next();
      },
      values: r => r.actions[0].values,
      expected: { outsideFirm: FIRM.id, to: 'budgets@acme.example', subject: 'Budget question' },
    },
  ];

  it.each(cases)(
    '$name: records once, sends the because, and the row leaves the lane',
    async ({ plan, fill, values, expected }) => {
      const host = wizardHost(items(plan));
      mount(host);
      await openItem(1);
      next();
      await fill();

      const sent = await confirmAndRecord(host);

      expect(sent.signalId).toBe(guid('5', 1));
      expect(sent.review.confirmed).toBe(true);
      expect(sent.review.because).toBe('Because the matter settled.');
      expect(values(sent.review)).toMatchObject(expected as object);
      // Each lookup went through the type-ahead: the tool was asked about this Work Item, never about anything else.
      const searches = host.callsTo(LOOKUP_TOOL);
      expect(searches.length).toBeGreaterThan(0);
      expect(searches.every(s => s.args.signalId === guid('5', 1))).toBe(true);

      // The row leaves the lane and the count drops by one (the lane is read again, as the server answers it).
      await waitFor(() => expect(count()).toHaveTextContent('2'));
      expect(lineIds()).not.toContain(guid('5', 1));
      expect(lineIds()).toEqual([guid('5', 2), guid('5', 3)]);
    }
  );
});

describe('the type-ahead inside the wizard', () => {
  it('asks the tool nothing for one character, and once (not once per keystroke) for the text that was typed', async () => {
    const host = wizardHost(items(PLANS.reviseBudget, 0));
    mount(host);
    await openItem(1);
    next();
    await screen.findByTestId('step-action-revise-budget');

    const field = screen.getByPlaceholderText(/search budget to revise/i);
    fireEvent.focus(field);
    enter(field, 'f');
    // Longer than both debounces (the field's and the widget's): a call that was going to be made would have been.
    await new Promise(resolve => setTimeout(resolve, 900));
    expect(host.callsTo(LOOKUP_TOOL)).toHaveLength(0);
    // It says why nothing happened; "No results found" would read as "there are none".
    expect(screen.getByText('Type at least 2 letters')).toBeInTheDocument();
    expect(screen.queryByText('No results found')).toBeNull();

    enter(field, 'fy');
    enter(field, 'fy2');
    enter(field, 'fy26');
    fireEvent.click(await screen.findByText(BUDGET.name));
    const asked = host.callsTo(LOOKUP_TOOL);
    expect(asked).toHaveLength(1);
    expect(asked[0].args).toEqual({ signalId: guid('5', 1), parameterCode: 'budget', query: 'fy26' });
    expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(0);
  });

  it('a lookup the tool refuses says the search could not be completed (not "No results found") and the wizard stays usable', async () => {
    const base = wizardHost(items(PLANS.reviseBudget, 0));
    const host: WizardHost = {
      ...base,
      callTool: async (name, args) => {
        if (name === LOOKUP_TOOL) {
          base.calls.push({ name, args });
          return toolError('ontology.decision.rate_limited', 'Too many searches. Wait a moment.', 429);
        }
        return base.callTool(name, args);
      },
    };
    mount(host);
    await openItem(1);
    next();
    await screen.findByTestId('step-action-revise-budget');
    const field = screen.getByPlaceholderText(/search budget to revise/i);
    fireEvent.focus(field);
    enter(field, 'fy26');
    await waitFor(() => expect(host.callsTo(LOOKUP_TOOL)).toHaveLength(1), { timeout: 3000 });
    expect(await screen.findByText('Search could not be completed. Try again.')).toBeInTheDocument();
    expect(screen.queryByText('No results found')).toBeNull();
    expect(screen.queryByText(BUDGET.name)).toBeNull();
    // Still the wizard, still editable.
    enter(screen.getByRole('textbox', { name: /^Reason for revision/ }), 'Typed after the refusal');
    expect(screen.getByRole('textbox', { name: /^Reason for revision/ })).toHaveValue('Typed after the refusal');
  });
});

describe('every parameter kind renders and submits', () => {
  it('text, text area, choice, money, date, boolean, person and lookup', async () => {
    const host = wizardHost(items(PLANS.everyKind, 0));
    mount(host);
    await openItem(1);
    next();
    await screen.findByTestId('step-action-every-kind');

    enter(screen.getByRole('textbox', { name: /^Subject line/ }), 'Fee change');
    enter(screen.getByRole('textbox', { name: /^Details/ }), 'The firm raised its rate.');
    fireEvent.change(screen.getByRole('combobox', { name: /^Category/ }), { target: { value: '100000001' } });
    enter(screen.getByRole('textbox', { name: /^Amount/ }), '1250.50');
    fireEvent.change(screen.getByLabelText(/^Due on/), { target: { value: '2026-11-30' } });
    // A yes/no starts ticked (an unset one is on); unticking it is what the review must carry as false.
    expect(screen.getByRole('checkbox', { name: /tell them/i })).toBeChecked();
    fireEvent.click(screen.getByRole('checkbox', { name: /tell them/i }));
    expect(screen.getByRole('checkbox', { name: /tell them/i })).not.toBeChecked();
    await pick(/search contact/i, 'dana', PERSON.name);
    await pick(/search budget/i, 'fy26 lit', BUDGET.name);
    next();

    const { review } = await confirmAndRecord(host);
    expect(review.actions[0].values).toEqual({
      subject: 'Fee change',
      details: 'The firm raised its rate.',
      category: '100000001',
      amount: 1250.5,
      dueDate: '2026-11-30',
      notify: false,
      person: PERSON.id,
      budget: BUDGET.id,
    });
    expect(everyKind.parameters.map(p => p.kind)).toEqual([
      'Text',
      'TextArea',
      'Choice',
      'Money',
      'Date',
      'Boolean',
      'Person',
      'Lookup',
    ]);
  });
});

describe('what the server says is shown, and the Work Item stays open', () => {
  /** Walks Revise budget to Confirm and presses Record decision, once. */
  async function recordRevise(host: WizardHost) {
    mount(host);
    await openItem(1);
    next();
    await screen.findByTestId('step-action-revise-budget');
    await pick(/search budget to revise/i, 'fy26 lit', BUDGET.name);
    enter(screen.getByRole('textbox', { name: /^New budget/ }), '250000');
    enter(screen.getByRole('textbox', { name: /^Reason for revision/ }), 'Appeal added');
    next();
    fireEvent.click(await recordButtonReady(/^record decision$/i));
    return screen.findByTestId('commit-failure');
  }

  const refusals: Array<[string, string, string, number, string]> = [
    [
      'not_authorized',
      'ontology.decision.not_authorized',
      'You are not allowed to record this decision.',
      403,
      'You cannot record this decision',
    ],
    [
      'a stale plan',
      'ontology.decision.plan_stale',
      'The plan changed since you opened it. Close it and open the item again.',
      422,
      'The decision was refused',
    ],
  ];

  it.each(refusals)(
    "%s: the server's message is shown, nothing is removed and the item can still be recorded",
    async (_n, code, message, status, heading) => {
      const host = wizardHost(items(PLANS.reviseBudget), { complete: () => toolError(code, message, status) });
      const failure = await recordRevise(host);

      expect(failure).toHaveTextContent(heading);
      expect(failure).toHaveTextContent(message);
      expect(failure).toHaveTextContent('No Decision Record was written.');
      // Still open: the Confirm step is there with its button, and the lane has not changed.
      expect(button('Record decision')).toBeEnabled();
      expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(1);
      expect(count()).toHaveTextContent('3');
      expect(lineIds()).toContain(guid('5', 1));
    }
  );

  it('a billing item reaching the commit says Decide this in Invoice Review', async () => {
    const host = wizardHost(items(PLANS.reviseBudget), {
      complete: () =>
        toolError('ontology.decision.decided_in_invoice_review', 'This is decided in Invoice Review.', 409),
    });
    const failure = await recordRevise(host);
    expect(failure).toHaveTextContent('Decide this in Invoice Review');
    expect(failure).toHaveTextContent('Invoice items are decided in Invoice Review. Nothing was recorded.');
    expect(count()).toHaveTextContent('3');
  });

  it('a host that does not answer the commit says the decision may not have been recorded, and the lane is untouched', async () => {
    const base = wizardHost(items(PLANS.reviseBudget));
    const host: WizardHost = {
      ...base,
      callTool: async (name, args) => {
        if (name === COMPLETE_TOOL) {
          base.calls.push({ name, args });
          throw new Error('host closed');
        }
        return base.callTool(name, args);
      },
    };
    const failure = await recordRevise(host);
    expect(failure).toHaveTextContent('The decision may not have been recorded');
    expect(failure).toHaveTextContent('It is not known whether the decision was recorded.');
    expect(count()).toHaveTextContent('3');
  });

  it('a success with warnings words each one in the wizard', async () => {
    const options: WizardHostOptions = {
      complete: (_id, review) => ({
        structuredContent: {
          reviewId: review.reviewId,
          decisionRecordId: 'eeeeeeee-0000-0000-0000-000000000001',
          recordClass: 'Judgement',
          steps: [],
          followOns: [],
          signals: [],
          warnings: ['inquiry_email_not_archived'],
          replayed: false,
        },
      }),
    };
    const host = wizardHost(items(PLANS.inquiry), options);
    mount(host);
    await openItem(1);
    next();
    await screen.findByTestId('step-action-send-budget-inquiry');
    enter(screen.getByRole('textbox', { name: /^To\b/ }), 'budgets@acme.example');
    enter(screen.getByRole('textbox', { name: /^Subject/ }), 'Budget question');
    enter(screen.getByRole('textbox', { name: /^Message/ }), 'Please confirm the budget.');
    next();
    await confirmAndRecord(host);
    const warnings = await screen.findByTestId('record-warnings');
    expect(warnings).toHaveTextContent(
      'The inquiry email was sent, but a copy of it could not be saved to the matter.'
    );
  });
});

describe('nothing is written before Confirm', () => {
  it('only the lane page, the plan (once) and the lookup are called while a Work Item is opened, walked and closed', async () => {
    const host = wizardHost(items(PLANS.reviseBudget));
    mount(host);
    await openItem(1);
    next();
    await screen.findByTestId('step-action-revise-budget');
    await pick(/search budget to revise/i, 'fy26 lit', BUDGET.name);
    enter(screen.getByRole('textbox', { name: /^New budget/ }), '250000');
    enter(screen.getByRole('textbox', { name: /^Reason for revision/ }), 'Appeal added');
    next(); // to Confirm
    await screen.findByTestId('step-confirm');
    fireEvent.click(button('Back')); // a step change back
    await screen.findByTestId('step-action-revise-budget');

    // Close: the shell asks before discarding, then discards.
    fireEvent.click(button('Close'));
    fireEvent.click(await screen.findByRole('button', { name: 'Discard and close' }));
    await waitFor(() => expect(screen.queryByTestId('step-action-revise-budget')).toBeNull());

    expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(0);
    const names = new Set(host.calls.map(c => c.name));
    expect([...names].every(n => [PAGE_TOOL, PLAN_TOOL, LOOKUP_TOOL].includes(n))).toBe(true);
    // The plan, the suppression and the item share ONE plan call per open.
    expect(host.callsTo(PLAN_TOOL)).toHaveLength(1);
    expect(count()).toHaveTextContent('3');
    // The frame goes back to inline when the wizard closes.
    expect(host.requestDisplayMode).toHaveBeenLastCalledWith('inline');
  });

  it('opening a Work Item again reads its plan again, so the plan-read stamp is fresh', async () => {
    const host = wizardHost(items(PLANS.reviseBudget));
    mount(host);
    await openItem(1);
    fireEvent.click(button('Close'));
    await waitFor(() => expect(screen.queryByTestId('step-found')).toBeNull());
    await openItem(1);
    expect(host.callsTo(PLAN_TOOL)).toHaveLength(2);
  });
});

describe('the display chain (O-5)', () => {
  it('fullscreen granted: the wizard is a dialog in the frame, after asking for fullscreen', async () => {
    const host = wizardHost(items(PLANS.reviseBudget));
    mount(host);
    await openItem(1);
    expect(host.requestDisplayMode).toHaveBeenCalledWith('fullscreen');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByTestId('wizard-place')).toHaveAttribute('data-place', 'dialog');
    expect(screen.queryByTestId('wizard-detail')).toBeNull();
  });

  it('fullscreen refused: master-detail, the wizard beside the lane with no dialog, and it can still record', async () => {
    const host = wizardHost(items(PLANS.reviseBudget), { displayMode: 'inline' });
    mount(host);
    await openItem(1);
    expect(host.requestDisplayMode).toHaveBeenCalledWith('fullscreen');
    expect(screen.getByTestId('wizard-place')).toHaveAttribute('data-place', 'detail');
    expect(screen.queryByRole('dialog')).toBeNull();
    const detail = screen.getByTestId('wizard-detail');
    expect(within(detail).getByTestId('step-found')).toBeInTheDocument();
    // The lane is still there, as the master.
    expect(screen.getByTestId('decide-lane')).toBeInTheDocument();
    expect(lineIds()).toHaveLength(3);
  });

  it('the host cannot say yes (no fullscreen offered, or no answer): master-detail too, and fullscreen is not even asked when not offered', async () => {
    const host = wizardHost(items(PLANS.reviseBudget));
    mount(host, { hostContext: { theme: 'light', availableDisplayModes: ['inline'] } });
    await openItem(1);
    expect(host.requestDisplayMode).not.toHaveBeenCalledWith('fullscreen');
    expect(screen.getByTestId('wizard-place')).toHaveAttribute('data-place', 'detail');
  });

  it('a frame too narrow for master-detail: the Console is opened through the host (openLink) and the lane stays', async () => {
    const host = wizardHost(items(PLANS.reviseBudget), { displayMode: 'inline' });
    const width = window.innerWidth;
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    try {
      mount(host);
      await openItem1Link();
    } finally {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: width });
    }
    await waitFor(() =>
      expect(host.openLink).toHaveBeenCalledWith(
        `${CONSOLE_BASE}/main.aspx?pagetype=webresource&webresourceName=sprk_spaarkeai`
      )
    );
    expect(screen.getByTestId('wizard-link-fallback')).toBeInTheDocument();
    expect(screen.queryByTestId('step-found')).toBeNull();
    expect(host.callsTo(PLAN_TOOL)).toHaveLength(0);
    // The lane is untouched and the banner can be closed.
    expect(lineIds()).toHaveLength(3);
    fireEvent.click(screen.getByTestId('wizard-link-close'));
    expect(screen.queryByTestId('wizard-link-fallback')).toBeNull();
  });

  it('a second Work Item opened while one is open is refused, and the open one is not replaced', async () => {
    const host = wizardHost(items(PLANS.reviseBudget), { displayMode: 'inline' });
    mount(host);
    await openItem(1);
    fireEvent.click(lines().find(l => l.getAttribute('data-signal-id') === guid('5', 2))!);
    expect(await screen.findByTestId('decide-lane-notice')).toHaveTextContent('Finish or close the open decision');
    expect(within(screen.getByTestId('wizard-detail')).getByTestId('step-found')).toBeInTheDocument();
    expect(host.callsTo(PLAN_TOOL)).toHaveLength(1);
  });

  it('a review step taken while a decision is open is refused, and the review keeps naming the item in front of the user', async () => {
    const host = wizardHost(items(PLANS.reviseBudget), { displayMode: 'inline' });
    mount(host);
    await screen.findByTestId('decide-lane');
    fireEvent.click(screen.getByTestId('lane-start-Decide'));
    await screen.findByTestId('step-found');
    expect(screen.getByTestId('decide-review-position')).toHaveTextContent('1 of 3');

    fireEvent.click(screen.getByTestId('decide-review-next'));

    expect(await screen.findByTestId('decide-lane-notice')).toHaveTextContent('Finish or close the open decision');
    expect(screen.getByTestId('decide-review-position')).toHaveTextContent('1 of 3');
    expect(host.callsTo(PLAN_TOOL)).toHaveLength(1);
  });

  it('once the open decision is closed, the next one opens', async () => {
    const host = wizardHost(items(PLANS.reviseBudget), { displayMode: 'inline' });
    mount(host);
    await openItem(1);
    fireEvent.click(button('Close'));
    await waitFor(() => expect(screen.queryByTestId('step-found')).toBeNull());
    fireEvent.click(lines().find(l => l.getAttribute('data-signal-id') === guid('5', 2))!);
    await screen.findByTestId('step-found');
    expect(host.callsTo(PLAN_TOOL).map(c => c.args.signalId)).toEqual([guid('5', 1), guid('5', 2)]);
  });
});

/** The lane row is clickable in a narrow frame as in any other: it opens nothing but the link. */
async function openItem1Link() {
  await screen.findByTestId('decide-lane');
  fireEvent.click(lines().find(l => l.getAttribute('data-signal-id') === guid('5', 1))!);
}

describe('a Work Item the wizard cannot read', () => {
  it('a plan refusal opens nothing and says why, with Try again', async () => {
    const host = wizardHost(items(PLANS.reviseBudget), {
      plan: () => toolError('sdap.access.deny.record_unavailable', 'This item is not available to you.', 404),
    });
    mount(host);
    await screen.findByTestId('decide-lane');
    fireEvent.click(lines()[0]);
    expect(await screen.findByTestId('item-error')).toHaveTextContent('This item is not available to you.');
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(0);
  });

  it('a row the plan tool could not read opens nothing (it is not decided without its evidence)', async () => {
    const host = wizardHost(items(PLANS.reviseBudget), {
      plan: (signalId, item) => ({
        structuredContent: {
          plan: { ...item.plan, signalId },
          suppression: null,
          row: null,
          rowError: { reasonCode: 'x', message: 'could not read the row' },
        },
      }),
    });
    mount(host);
    await screen.findByTestId('decide-lane');
    fireEvent.click(lines()[0]);
    expect(await screen.findByTestId('item-error')).toHaveTextContent(
      'The details of this item could not be read. Try again.'
    );
  });

  it('a host that does not answer the plan says so and offers Try again', async () => {
    const base = wizardHost(items(PLANS.reviseBudget));
    const host: WizardHost = {
      ...base,
      callTool: async (name, args) => {
        if (name === PLAN_TOOL) throw new Error('host closed');
        return base.callTool(name, args);
      },
    };
    mount(host);
    await screen.findByTestId('decide-lane');
    fireEvent.click(lines()[0]);
    expect(await screen.findByTestId('item-error')).toHaveTextContent('The decision could not be read. Try again.');
  });
});

describe('the Because box', () => {
  it('is on Confirm, optional, and a blank one is not sent', async () => {
    const host = wizardHost(items(PLANS.reviseBudget));
    mount(host);
    await openItem(1);
    next();
    await screen.findByTestId('step-action-revise-budget');
    await pick(/search budget to revise/i, 'fy26 lit', BUDGET.name);
    enter(screen.getByRole('textbox', { name: /^New budget/ }), '1');
    enter(screen.getByRole('textbox', { name: /^Reason for revision/ }), 'r');
    next();
    await screen.findByTestId('step-confirm');
    expect(screen.getByTestId('because')).toBeInTheDocument();
    enter(screen.getByRole('textbox', { name: /^Because/ }), '   ');
    fireEvent.click(await recordButtonReady(/^record decision$/i));
    await waitFor(() => expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(1));
    expect((host.callsTo(COMPLETE_TOOL)[0].args.review as DecisionReview).because).toBeUndefined();
  });
});
