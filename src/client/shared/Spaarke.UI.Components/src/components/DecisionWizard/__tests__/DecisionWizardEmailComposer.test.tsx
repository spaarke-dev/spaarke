/**
 * DecisionWizard: the message step in the shared EmailComposer (task 160; D-179, owner 2026-10-23).
 *  - An action whose plan entry says `composer: "email"` renders the EmailComposer, compose-only: no Send, no From row, no
 *    External / Internal Only tags. Its To, Subject and Message flow into the step's values through `onStateChange` and are
 *    committed in the existing shape (`to` = the addresses joined with "; "). Other parameters (the outside firm) stay fields.
 *  - To starts with the plan's `prefillTo` (the outside firm's contact); without one it starts empty and Next is disabled until
 *    To, Subject and Message are all present.
 *  - The To lookup opens the host's side-pane lookup for contacts (task 158's `openLookup`) and adds the pick's email address;
 *    a pick with no address adds nothing.
 *  - Must-not-fire: an action without `composer` keeps one field per parameter.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import type {
  DecisionAction,
  DecisionOpenLookup,
  DecisionPlan,
  DecisionReview,
  DecisionWizardItem,
} from '../decisionWizardTypes';
import { DecisionWizard } from '../DecisionWizard';
import { PATH_B_ID, matterCore, pathBItem, pathBPlan, sendInquiry } from '../__fixtures__/decisionWizardFixtures';
import { makeLoader, makeService, renderWizard } from './decisionTestKit';

/** Task 192 (D-224): the props every EmailComposer mount received, so a test can pin what the step does NOT pass it. */
const mockComposerProps: Array<Record<string, unknown>> = [];
/** Task 197: the last composer state each mount reported, so a test can report an edited rich-text body the way the editor does. */
const mockComposerStates: Array<Record<string, unknown>> = [];
jest.mock('../../EmailComposer/EmailComposer', () => {
  // jest.mock factories cannot reference imports, so react is required here
  const react = require('react') as typeof import('react');
  const actual = jest.requireActual(
    '../../EmailComposer/EmailComposer'
  ) as typeof import('../../EmailComposer/EmailComposer');
  const Spy = react.forwardRef<unknown, Record<string, unknown>>((props, ref) => {
    mockComposerProps.push(props);
    const onStateChange = props.onStateChange as ((s: Record<string, unknown>) => void) | undefined;
    const spied = {
      ...props,
      onStateChange: (s: Record<string, unknown>) => {
        mockComposerStates.push(s);
        onStateChange?.(s);
      },
    };
    return react.createElement(actual.EmailComposer, { ...spied, ref } as never);
  });
  Spy.displayName = 'SpyEmailComposer';
  return { ...actual, EmailComposer: Spy };
});

const click = (element: Element) => fireEvent.click(element);
const button = (name: RegExp) => screen.getByRole('button', { name });

/** Task 197: the message opens in rich text, so its text is the editor's, not a textarea's. */
const richBody = (step: HTMLElement): HTMLElement => {
  const editor = step.querySelector('[contenteditable="true"]');
  if (!editor) throw new Error('the rich-text editor is not shown');
  return editor as HTMLElement;
};

/** Task 197: edits go through the plain-text box (the editor's own typing is not simulated in jsdom). */
const plainBody = (step: HTMLElement): HTMLTextAreaElement => {
  const existing = within(step).queryByRole('textbox', { name: 'Message body' });
  if (!existing) click(within(step).getByRole('button', { name: 'Switch to plain text' }));
  return within(step).getByRole('textbox', { name: 'Message body' }) as HTMLTextAreaElement;
};

const LEAD = { contactId: 'cccccccc-0000-0000-0000-0000000000aa', name: 'Lead Counsel', email: 'lead@firm.example' };

function planWith(action: DecisionAction): DecisionPlan {
  return { ...pathBPlan, actions: [action, ...pathBPlan.actions.slice(1)] };
}

const composerInquiry = (prefillTo: DecisionAction['prefillTo'] = null): DecisionAction => ({
  ...sendInquiry,
  composer: 'email',
  prefillTo,
});

/** Task 190 (D-222): the inquiry as the plan route returns it now: the catalog declares optional `cc` and `bcc`. */
const composerInquiryWithCopies: DecisionAction = {
  ...composerInquiry([LEAD]),
  parameters: [
    ...sendInquiry.parameters,
    { code: 'cc', label: 'Cc', kind: 'Text', required: false },
    { code: 'bcc', label: 'Bcc', kind: 'Text', required: false },
  ],
};

const addRecipient = (step: HTMLElement, field: 'To' | 'Cc' | 'Bcc', address: string) => {
  // The composer hides the Bcc row until its small "Bcc" toggle is used.
  if (field === 'Bcc' && !within(step).queryByRole('textbox', { name: 'Bcc' })) {
    click(within(step).getByRole('button', { name: 'Bcc' }));
  }
  const input = within(step).getByRole('textbox', { name: field });
  fireEvent.change(input, { target: { value: address } });
  fireEvent.keyDown(input, { key: 'Enter' });
};

function setup(
  plan: DecisionPlan,
  overrides: Partial<React.ComponentProps<typeof DecisionWizard>> = {},
  item: DecisionWizardItem = pathBItem
) {
  const service = makeService({ plans: { [PATH_B_ID]: plan } });
  const view = renderWizard({
    service,
    loadItem: makeLoader([item]),
    itemIds: [PATH_B_ID],
    startItemId: PATH_B_ID,
    ...overrides,
  });
  return { service, ...view };
}

async function toInquiry() {
  await screen.findByTestId('step-found');
  click(button(/^next$/i));
  return screen.findByTestId('step-action-send-budget-inquiry');
}

async function recordAfterInquiry(service: ReturnType<typeof makeService>) {
  click(button(/^next$/i));
  for (let i = 0; i < 8 && !screen.queryByTestId('step-confirm'); i++) {
    const nextButton = screen.getByRole('button', { name: /^next$/i }) as HTMLButtonElement;
    click(nextButton.disabled ? button(/^skip$/i) : nextButton);
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  await screen.findByTestId('step-confirm');
  click(button(/^record decision$/i));
  await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
  return service.commit.mock.calls[0][1] as DecisionReview;
}

describe('message step in the EmailComposer (task 160)', () => {
  it('renders the composer compose-only: no Send, no From row, no tags; the outside firm stays a field', async () => {
    setup(planWith(composerInquiry([LEAD])));
    const step = await toInquiry();

    expect(within(step).getByRole('region', { name: 'Email composer' })).toBeInTheDocument();
    expect(within(step).queryByRole('button', { name: /^send$/i })).toBeNull();
    expect(within(step).queryByRole('group', { name: 'From' })).toBeNull();
    expect(within(step).queryByRole('radiogroup', { name: 'Who can see this message' })).toBeNull();
    // To / Subject / Message are the composer's, not three separate wizard fields.
    expect(within(step).queryByTestId('field-to')).toBeNull();
    expect(within(step).queryByTestId('field-subject')).toBeNull();
    expect(within(step).queryByTestId('field-body')).toBeNull();
    expect(within(step).getByTestId('field-outsideFirm')).toBeInTheDocument();
  });

  it('starts To with the pre-fill and the templated subject and message, so Next is enabled', async () => {
    setup(planWith(composerInquiry([LEAD])));
    const step = await toInquiry();

    expect(within(step).getByText(LEAD.email)).toBeInTheDocument();
    expect(within(step).getByRole('textbox', { name: 'Subject' })).not.toHaveValue('');
    await waitFor(() => expect(richBody(step).textContent).not.toBe(''));
    expect(button(/^next$/i)).toBeEnabled();
  });

  it('with no pre-fill To is empty and Next is disabled until an address is added', async () => {
    setup(planWith(composerInquiry(null)));
    const step = await toInquiry();

    expect(button(/^next$/i)).toBeDisabled();
    const to = within(step).getByRole('textbox', { name: 'To' });
    fireEvent.change(to, { target: { value: 'typed@firm.example' } });
    fireEvent.keyDown(to, { key: 'Enter' });

    await waitFor(() => expect(button(/^next$/i)).toBeEnabled());
  });

  it('Next is disabled when the subject or the message is cleared', async () => {
    setup(planWith(composerInquiry([LEAD])));
    const step = await toInquiry();

    fireEvent.change(within(step).getByRole('textbox', { name: 'Subject' }), { target: { value: '' } });
    await waitFor(() => expect(button(/^next$/i)).toBeDisabled());
    fireEvent.change(within(step).getByRole('textbox', { name: 'Subject' }), { target: { value: 'Budget question' } });
    await waitFor(() => expect(button(/^next$/i)).toBeEnabled());
    fireEvent.change(plainBody(step), { target: { value: '  ' } });
    await waitFor(() => expect(button(/^next$/i)).toBeDisabled());
  });

  it('commits the composer state in the existing shape: addresses joined with "; ", the edited subject and message', async () => {
    const { service } = setup(planWith(composerInquiry([LEAD])));
    const step = await toInquiry();

    const to = within(step).getByRole('textbox', { name: 'To' });
    fireEvent.change(to, { target: { value: 'second@firm.example' } });
    fireEvent.keyDown(to, { key: 'Enter' });
    fireEvent.change(within(step).getByRole('textbox', { name: 'Subject' }), { target: { value: 'Budget question' } });
    fireEvent.change(plainBody(step), { target: { value: 'Why the overage?' } });

    const review = await recordAfterInquiry(service);
    expect(review.actions.find(a => a.code === 'send-budget-inquiry')).toEqual({
      code: 'send-budget-inquiry',
      taken: true,
      values: { to: 'lead@firm.example; second@firm.example', subject: 'Budget question', body: 'Why the overage?' },
    });
  }, 30000);

  it('the To lookup opens the side pane for contacts and adds the pick by its email address', async () => {
    const openLookup = Object.assign(
      jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => ({
        id: 'cccccccc-0000-0000-0000-0000000000bb',
        name: 'Dana Whitfield',
        email: 'dana@firm.example',
      })),
      { supportedEntities: ['contact', 'sprk_organization', 'sprk_budget'] as readonly string[] }
    );
    setup(planWith(composerInquiry(null)), { openLookup });
    const step = await toInquiry();

    click(within(step).getByRole('button', { name: 'Look up To recipients' }));

    await waitFor(() =>
      expect(openLookup).toHaveBeenCalledWith({
        kind: 'Person',
        entity: 'contact',
        parameterCode: 'to',
        core: { recordType: matterCore.recordType, recordId: matterCore.recordId },
      })
    );
    expect(await within(step).findByText(/Dana Whitfield|dana@firm\.example/)).toBeInTheDocument();
    await waitFor(() => expect(button(/^next$/i)).toBeEnabled());
  });

  it('a side-pane pick with no email address adds nothing', async () => {
    const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => ({
      id: 'cccccccc-0000-0000-0000-0000000000cc',
      name: 'No Mail',
    }));
    setup(planWith(composerInquiry(null)), { openLookup });
    const step = await toInquiry();

    click(within(step).getByRole('button', { name: 'Look up To recipients' }));

    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    expect(within(step).queryByText('No Mail')).toBeNull();
    expect(button(/^next$/i)).toBeDisabled();
  });

  it('no side-pane lookup control when the host has no openLookup', async () => {
    setup(planWith(composerInquiry(null)));
    const step = await toInquiry();

    expect(within(step).queryByRole('button', { name: 'Look up To recipients' })).toBeNull();
  });

  it('a side-pane pick with no email address says so and tells the user to type one in To (F2)', async () => {
    const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => ({
      id: 'cccccccc-0000-0000-0000-0000000000cc',
      name: 'No Mail',
    }));
    setup(planWith(composerInquiry(null)), { openLookup });
    const step = await toInquiry();
    expect(within(step).queryByTestId('no-email')).toBeNull();

    click(within(step).getByRole('button', { name: 'Look up To recipients' }));

    const warning = await within(step).findByTestId('no-email');
    expect(warning).toHaveTextContent('No Mail has no email address');
    expect(warning).toHaveTextContent('type one in To');
  });

  it('a pick whose address is not a usable address is treated as having none (F2)', async () => {
    const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => ({
      id: 'cccccccc-0000-0000-0000-0000000000dd',
      name: 'Odd Mail',
      email: 'lead@firm',
    }));
    setup(planWith(composerInquiry(null)), { openLookup });
    const step = await toInquiry();

    click(within(step).getByRole('button', { name: 'Look up To recipients' }));

    expect(await within(step).findByTestId('no-email')).toHaveTextContent('Odd Mail has no email address');
    expect(button(/^next$/i)).toBeDisabled();
  });

  it('the no-address warning clears on a later pick with an address; a cancelled pane warns of nothing (F2 must-not-fire)', async () => {
    const answers: Array<{ id: string; name: string; email?: string } | null> = [
      null,
      { id: 'cccccccc-0000-0000-0000-0000000000cc', name: 'No Mail' },
      { id: 'cccccccc-0000-0000-0000-0000000000bb', name: 'Dana Whitfield', email: 'dana@firm.example' },
    ];
    const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(
      async () => answers.shift() ?? null
    );
    setup(planWith(composerInquiry(null)), { openLookup });
    const step = await toInquiry();
    const lookup = () => click(within(step).getByRole('button', { name: 'Look up To recipients' }));

    lookup();
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    expect(within(step).queryByTestId('no-email')).toBeNull();

    lookup();
    expect(await within(step).findByTestId('no-email')).toBeInTheDocument();

    lookup();
    await waitFor(() => expect(within(step).queryByTestId('no-email')).toBeNull());
    await waitFor(() => expect(button(/^next$/i)).toBeEnabled());
  });

  it('a side pane that throws shows the lookup-failed warning and adds nothing (K-2)', async () => {
    const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => {
      throw new Error('side pane unavailable');
    });
    setup(planWith(composerInquiry(null)), { openLookup });
    const step = await toInquiry();
    expect(within(step).queryByTestId('lookup-failed')).toBeNull();

    click(within(step).getByRole('button', { name: 'Look up To recipients' }));

    expect(await within(step).findByTestId('lookup-failed')).toHaveTextContent(
      'The contact lookup could not be opened; type the address in To.'
    );
    expect(within(step).queryByTestId('no-email')).toBeNull();
    expect(button(/^next$/i)).toBeDisabled();
  });

  it('the lookup-failed warning clears when the next lookup starts (K-2)', async () => {
    const openLookup = jest
      .fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>()
      .mockRejectedValueOnce(new Error('side pane unavailable'))
      .mockResolvedValueOnce(null);
    setup(planWith(composerInquiry(null)), { openLookup });
    const step = await toInquiry();
    const lookup = () => click(within(step).getByRole('button', { name: 'Look up To recipients' }));

    lookup();
    expect(await within(step).findByTestId('lookup-failed')).toBeInTheDocument();
    lookup();
    await waitFor(() => expect(within(step).queryByTestId('lookup-failed')).toBeNull());
  });

  it('typing an address in To clears the no-email and lookup-failed warnings (K-3)', async () => {
    const openLookup = jest
      .fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>()
      .mockResolvedValueOnce({ id: 'cccccccc-0000-0000-0000-0000000000cc', name: 'No Mail' })
      .mockRejectedValueOnce(new Error('side pane unavailable'));
    setup(planWith(composerInquiry(null)), { openLookup });
    const step = await toInquiry();
    const lookup = () => click(within(step).getByRole('button', { name: 'Look up To recipients' }));
    const typeInTo = (address: string) => {
      const to = within(step).getByRole('textbox', { name: 'To' });
      fireEvent.change(to, { target: { value: address } });
      fireEvent.keyDown(to, { key: 'Enter' });
    };

    lookup();
    expect(await within(step).findByTestId('no-email')).toBeInTheDocument();
    typeInTo('typed@firm.example');
    await waitFor(() => expect(within(step).queryByTestId('no-email')).toBeNull());
    await waitFor(() => expect(button(/^next$/i)).toBeEnabled());

    lookup();
    expect(await within(step).findByTestId('lookup-failed')).toBeInTheDocument();
    typeInTo('second@firm.example');
    await waitFor(() => expect(within(step).queryByTestId('lookup-failed')).toBeNull());
  });

  it('typing in Subject leaves the no-email warning on screen (K-3 must-not-fire)', async () => {
    const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => ({
      id: 'cccccccc-0000-0000-0000-0000000000cc',
      name: 'No Mail',
    }));
    setup(planWith(composerInquiry(null)), { openLookup });
    const step = await toInquiry();

    click(within(step).getByRole('button', { name: 'Look up To recipients' }));
    expect(await within(step).findByTestId('no-email')).toBeInTheDocument();
    fireEvent.change(within(step).getByRole('textbox', { name: 'Subject' }), { target: { value: 'Budget question' } });
    await new Promise(resolve => setTimeout(resolve, 50));

    expect(within(step).getByTestId('no-email')).toBeInTheDocument();
  });

  it('Cc and Bcc are not sent: adding a Cc shows the warning (F3b)', async () => {
    setup(planWith(composerInquiry([LEAD])));
    const step = await toInquiry();
    expect(within(step).queryByTestId('cc-not-sent')).toBeNull();

    const cc = within(step).getByRole('textbox', { name: 'Cc' });
    fireEvent.change(cc, { target: { value: 'copy@firm.example' } });
    fireEvent.keyDown(cc, { key: 'Enter' });

    expect(await within(step).findByTestId('cc-not-sent')).toHaveTextContent('Cc and Bcc are not sent');
  });

  describe('Cc and Bcc on an action that declares them (task 190, D-222)', () => {
    it('commits the Cc to the cc step value and shows no cc-not-sent warning', async () => {
      const { service } = setup(planWith(composerInquiryWithCopies));
      const step = await toInquiry();

      addRecipient(step, 'Cc', 'copy@firm.example');
      await waitFor(() => expect(within(step).getByText('copy@firm.example')).toBeInTheDocument());
      await new Promise(resolve => setTimeout(resolve, 50));
      expect(within(step).queryByTestId('cc-not-sent')).toBeNull();
      expect(button(/^next$/i)).toBeEnabled();

      const review = await recordAfterInquiry(service);
      expect(review.actions.find(a => a.code === 'send-budget-inquiry')?.values).toMatchObject({
        to: 'lead@firm.example',
        cc: 'copy@firm.example',
      });
    }, 30000);

    it('commits Cc and Bcc joined with "; "', async () => {
      const { service } = setup(planWith(composerInquiryWithCopies));
      const step = await toInquiry();

      addRecipient(step, 'Cc', 'c1@firm.example');
      addRecipient(step, 'Cc', 'c2@firm.example');
      addRecipient(step, 'Bcc', 'hidden@firm.example');

      const review = await recordAfterInquiry(service);
      const values = review.actions.find(a => a.code === 'send-budget-inquiry')?.values;
      expect(values).toMatchObject({ cc: 'c1@firm.example; c2@firm.example', bcc: 'hidden@firm.example' });
      expect(values?.to).toBe('lead@firm.example');
    }, 30000);

    it('sends no cc or bcc value when none was added', async () => {
      const { service } = setup(planWith(composerInquiryWithCopies));
      await toInquiry();

      const review = await recordAfterInquiry(service);
      const values = review.actions.find(a => a.code === 'send-budget-inquiry')?.values ?? {};
      expect(Object.keys(values)).not.toContain('cc');
      expect(Object.keys(values)).not.toContain('bcc');
    }, 30000);

    it('does not render cc and bcc as separate fields beside the composer', async () => {
      setup(planWith(composerInquiryWithCopies));
      const step = await toInquiry();

      expect(within(step).queryByTestId('field-cc')).toBeNull();
      expect(within(step).queryByTestId('field-bcc')).toBeNull();
      expect(within(step).getByTestId('field-outsideFirm')).toBeInTheDocument();
    });

    it('starts Cc and Bcc from the step values: they are still there after Back', async () => {
      setup(planWith(composerInquiryWithCopies));
      const step = await toInquiry();
      addRecipient(step, 'Cc', 'copy@firm.example');
      addRecipient(step, 'Bcc', 'hidden@firm.example');
      await waitFor(() => expect(within(step).getByText('hidden@firm.example')).toBeInTheDocument());

      click(button(/^next$/i));
      await screen.findByTestId('step-action-revise-budget');
      click(button(/^back$/i));
      const again = await screen.findByTestId('step-action-send-budget-inquiry');

      expect(within(again).getByText('copy@firm.example')).toBeInTheDocument();
      expect(within(again).getByText('hidden@firm.example')).toBeInTheDocument();
      expect(within(again).queryByTestId('cc-not-sent')).toBeNull();
    }, 30000);

    it.each(['Cc', 'Bcc'] as const)('Next stays disabled while a %s address is not one, as for To', async field => {
      setup(planWith(composerInquiryWithCopies));
      const step = await toInquiry();
      expect(button(/^next$/i)).toBeEnabled();

      addRecipient(step, field, 'copy@firm');

      await waitFor(() => expect(button(/^next$/i)).toBeDisabled());
    });

    it('must-not-fire: an action that declares no cc/bcc (send-reminder) still shows the warning', async () => {
      // send-reminder's message step is the same composer over to/subject/body only (the catalog's MessageParameters).
      setup(planWith(composerInquiry([LEAD])));
      const step = await toInquiry();
      expect(sendInquiry.parameters.some(p => p.code === 'cc' || p.code === 'bcc')).toBe(false);

      addRecipient(step, 'Cc', 'copy@firm.example');

      expect(await within(step).findByTestId('cc-not-sent')).toBeInTheDocument();
    });
  });

  it('mounting the composer is not a choice: opening and leaving the step asks nothing (F3b)', async () => {
    const { onClose } = setup(planWith(composerInquiry([LEAD])));
    await toInquiry();

    click(button(/^close$/i));

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(screen.queryByText('Discard unrecorded choices?')).toBeNull();
  });

  it('typing in the composer is a choice: leaving the step asks (F3b must-not-fire)', async () => {
    const { onClose } = setup(planWith(composerInquiry([LEAD])));
    const step = await toInquiry();

    fireEvent.change(within(step).getByRole('textbox', { name: 'Subject' }), { target: { value: 'Budget question' } });
    click(button(/^close$/i));

    expect(await screen.findByText('Discard unrecorded choices?')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('a pre-fill that is not a usable address blocks Next (the server refuses it too)', async () => {
    setup(planWith(composerInquiry([{ ...LEAD, email: 'lead@firm' }])));
    const step = await toInquiry();

    expect(within(step).getByText('lead@firm')).toBeInTheDocument();
    expect(button(/^next$/i)).toBeDisabled();
  });

  it('a matter name that is markup never runs, and shows literally, in the rich-text body it opens in (F1, stored XSS)', async () => {
    const payload = 'Acme <img src=x onerror="window.__xss=1">';
    const writes: string[] = [];
    const descriptor = Object.getOwnPropertyDescriptor(Element.prototype, 'innerHTML')!;
    Object.defineProperty(Element.prototype, 'innerHTML', {
      ...descriptor,
      set(this: Element, value: string) {
        writes.push(String(value));
        descriptor.set!.call(this, value);
      },
    });
    try {
      setup(planWith(composerInquiry([LEAD])), {}, { ...pathBItem, core: { ...matterCore, name: payload } });
      const step = await toInquiry();
      // Task 197: the message opens in rich text; the markup in the name is escaped by the plain-text conversion, so it is
      // shown as the characters it is, never parsed.
      await waitFor(() => expect(richBody(step).textContent).toContain('onerror'));
      expect(richBody(step).querySelector('img')).toBeNull();
      await new Promise(resolve => setTimeout(resolve, 50));

      expect((window as unknown as { __xss?: number }).__xss).toBeUndefined();
      // jsdom never loads an <img>, so a live <img onerror> cannot fire here; the sink itself is what is pinned:
      // nothing the wizard does hands a draft to `innerHTML`.
      expect(writes.filter(w => /onerror/i.test(w))).toEqual([]);
    } finally {
      Object.defineProperty(Element.prototype, 'innerHTML', descriptor);
    }
  });

  it('must-not-fire: an action without a composer keeps one field per parameter', async () => {
    setup(planWith(sendInquiry));
    const step = await toInquiry();

    expect(within(step).queryByRole('region', { name: 'Email composer' })).toBeNull();
    expect(within(step).getByTestId('field-to')).toBeInTheDocument();
    expect(within(step).getByTestId('field-subject')).toBeInTheDocument();
    expect(within(step).getByTestId('field-body')).toBeInTheDocument();
  });
});

/** Task 192 (D-224): the owner-approved text the plan route sends as the budget inquiry's starting Subject and Message. */
const DEFAULT_SUBJECT = 'Budget inquiry: MTR-00042 Acme v. Beta';
const DEFAULT_BODY = [
  'Hello,',
  '',
  "We received notice of a fee or scope change on Acme v. Beta (MTR-00042). The matter's budget has not been revised to reflect it.",
  '',
  'Please send us a revised budget estimate, or confirm that the change does not affect the current budget.',
  '',
  'Thank you,',
  'Dana Whitfield',
].join('\n');

/** The inquiry as the plan route returns it now: Subject and Message carry the server's default (`defaultValue`). */
const inquiryWithDefaults = (): DecisionAction => {
  const base = composerInquiry([LEAD]);
  return {
    ...base,
    parameters: base.parameters.map(p =>
      p.code === 'subject'
        ? { ...p, defaultValue: DEFAULT_SUBJECT }
        : p.code === 'body'
          ? { ...p, defaultValue: DEFAULT_BODY }
          : p
    ),
  };
};

describe('message step starts from the server default text (task 192, D-224)', () => {
  const subjectBox = (step: HTMLElement) => within(step).getByRole('textbox', { name: 'Subject' }) as HTMLInputElement;
  // Task 197: the message opens in rich text. `bodyBox` is the plain-text box (reached through the format toggle) for edits.
  const bodyBox = plainBody;

  it('opens with the default Subject and Message, line breaks kept, instead of the generic template', async () => {
    setup(planWith(inquiryWithDefaults()));
    const step = await toInquiry();

    expect(subjectBox(step).value).toBe(DEFAULT_SUBJECT);
    // Task 197: opens in rich text, one paragraph per blank-line-separated block, the single line break kept inside a block.
    await waitFor(() => expect(richBody(step).textContent).toContain('Hello,'));
    const paragraphs = Array.from(richBody(step).querySelectorAll('p'));
    expect(paragraphs.map(p => p.textContent)).toEqual([
      'Hello,',
      "We received notice of a fee or scope change on Acme v. Beta (MTR-00042). The matter's budget has not been revised to reflect it.",
      'Please send us a revised budget estimate, or confirm that the change does not affect the current budget.',
      'Thank you,Dana Whitfield',
    ]);
    expect(richBody(step).querySelectorAll('br').length).toBeGreaterThanOrEqual(1);
    expect(button(/^next$/i)).toBeEnabled();
  });

  it('an untouched default is committed as plain text with its line breaks', async () => {
    const { service } = setup(planWith(inquiryWithDefaults()));
    await toInquiry();

    const review = await recordAfterInquiry(service);

    expect(review.actions.find(a => a.code === 'send-budget-inquiry')?.values).toEqual({
      to: LEAD.email,
      subject: DEFAULT_SUBJECT,
      body: DEFAULT_BODY,
    });
  }, 30000);

  it("the user's edit is committed instead of the default", async () => {
    const { service } = setup(planWith(inquiryWithDefaults()));
    const step = await toInquiry();

    fireEvent.change(subjectBox(step), { target: { value: 'Budget question' } });
    fireEvent.change(bodyBox(step), { target: { value: `${DEFAULT_BODY}\n\nP.S. Please reply by Friday.` } });

    const review = await recordAfterInquiry(service);
    expect(review.actions.find(a => a.code === 'send-budget-inquiry')?.values).toEqual({
      to: LEAD.email,
      subject: 'Budget question',
      body: `${DEFAULT_BODY}\n\nP.S. Please reply by Friday.`,
    });
  }, 30000);

  it('a default is a starting value, not a choice: opening and leaving the step asks nothing', async () => {
    const { onClose } = setup(planWith(inquiryWithDefaults()));
    await toInquiry();

    click(button(/^close$/i));

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(screen.queryByText('Discard unrecorded choices?')).toBeNull();
  });

  it('must-not-fire: a Subject and Message with no server default keep the generic templated start', async () => {
    setup(planWith(composerInquiry([LEAD])));
    const step = await toInquiry();

    expect(subjectBox(step).value).not.toBe(DEFAULT_SUBJECT);
    expect(subjectBox(step).value).not.toBe('');
    await waitFor(() => expect(richBody(step).textContent).not.toBe(''));
    expect(richBody(step).textContent).not.toContain('Dana Whitfield');
  });

  it('stays compose-only: the composer gets no template picker, no AI draft and a fetch that refuses (D-224)', async () => {
    mockComposerProps.length = 0;
    setup(planWith(inquiryWithDefaults()));
    const step = await toInquiry();

    expect(within(step).queryByRole('button', { name: /template/i })).toBeNull();
    expect(within(step).queryByRole('button', { name: /draft with ai/i })).toBeNull();
    expect(mockComposerProps.length).toBeGreaterThan(0);
    for (const props of mockComposerProps) {
      expect(props.onListEmailTemplates).toBeUndefined();
      expect(props.onRenderEmailTemplate).toBeUndefined();
      expect(props.onDraftWithAi).toBeUndefined();
      expect(props.composeOnly).toBe(true);
    }
    await expect((mockComposerProps[0].authenticatedFetch as () => Promise<unknown>)()).rejects.toThrow(
      'never sends from the composer'
    );
  });
});

/**
 * Task 197 (owner 2026-11-18): the message opens in rich text, in this step too. The commit route sends the body as plain text
 * (`BodyFormat.PlainText` in the inquiry and reminder executors), so what is committed is always plain text: never HTML.
 */
describe('message step opens in rich text and commits plain text (task 197)', () => {
  /** Report an edited rich-text body the way the editor does (its typing is not simulated in jsdom). */
  const editRichBody = (html: string) => {
    const props = mockComposerProps[mockComposerProps.length - 1];
    const last = mockComposerStates[mockComposerStates.length - 1];
    act(() => {
      (props.onStateChange as (s: Record<string, unknown>) => void)({ ...last, body: html, bodyFormat: 'HTML' });
    });
  };

  it('opens the message in the rich-text editor: the toggle offers plain text and no plain-text box is shown', async () => {
    setup(planWith(inquiryWithDefaults()));
    const step = await toInquiry();

    expect(richBody(step)).toBeInTheDocument();
    expect(within(step).getByRole('button', { name: 'Switch to plain text' })).toBeInTheDocument();
    expect(within(step).queryByRole('textbox', { name: 'Message body' })).toBeNull();
  });

  it('an edited rich-text body is committed as plain text: markup dropped, paragraphs kept as blank-line-separated blocks', async () => {
    mockComposerProps.length = 0;
    mockComposerStates.length = 0;
    const { service } = setup(planWith(inquiryWithDefaults()));
    await toInquiry();

    editRichBody(
      '<p>Why the <strong>overage</strong>?</p><p>Reply by <a href="https://x.example">Friday</a> &amp; oblige</p>'
    );

    const review = await recordAfterInquiry(service);
    const body = review.actions.find(a => a.code === 'send-budget-inquiry')?.values?.body as string;
    expect(body).toBe('Why the overage?\n\nReply by Friday & oblige');
    expect(body).not.toMatch(/<|&amp;|&lt;/);
  }, 30000);

  it('text that looks like markup is committed as the characters typed, never as markup', async () => {
    mockComposerProps.length = 0;
    mockComposerStates.length = 0;
    const { service } = setup(planWith(inquiryWithDefaults()));
    await toInquiry();

    editRichBody('<p>Is 2 &lt; 3 &amp;&amp; 3 &gt; 2?</p>');

    const review = await recordAfterInquiry(service);
    expect(review.actions.find(a => a.code === 'send-budget-inquiry')?.values?.body).toBe('Is 2 < 3 && 3 > 2?');
  }, 30000);

  it('the editor re-serialising the untouched default is not an edit: the default is committed as it was', async () => {
    mockComposerProps.length = 0;
    mockComposerStates.length = 0;
    const { service } = setup(planWith(inquiryWithDefaults()));
    await toInquiry();

    // The editor's own re-serialisation of the opened paragraphs: spans, classes and runs of spaces.
    editRichBody(
      '<p class="p" dir="ltr"><span style="white-space: pre-wrap;">Hello,</span></p>' +
        '<p><span style="white-space: pre-wrap;">We received notice of a fee or scope change on Acme v. Beta (MTR-00042). The matter\'s budget has  not been revised to reflect it.</span></p>' +
        '<p><span style="white-space: pre-wrap;">Please send us a revised budget estimate, or confirm that the change does not affect the current budget.</span></p>' +
        '<p><span style="white-space: pre-wrap;">Thank you,</span><br><span style="white-space: pre-wrap;">Dana Whitfield</span></p>'
    );

    const review = await recordAfterInquiry(service);
    expect(review.actions.find(a => a.code === 'send-budget-inquiry')?.values?.body).toBe(DEFAULT_BODY);
  }, 30000);

  it('must-not-fire: the same words with formatting added are an edit, committed as plain text', async () => {
    mockComposerProps.length = 0;
    mockComposerStates.length = 0;
    const { service } = setup(planWith(inquiryWithDefaults()));
    await toInquiry();

    editRichBody('<p><strong>Hello,</strong></p><p>Please send us a revised budget.</p>');

    const review = await recordAfterInquiry(service);
    expect(review.actions.find(a => a.code === 'send-budget-inquiry')?.values?.body).toBe(
      'Hello,\n\nPlease send us a revised budget.'
    );
  }, 30000);
});
