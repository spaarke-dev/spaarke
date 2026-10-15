/**
 * The D-111 disposition picker on the inquiry-outcome To Do (task 155). The stated contract:
 *  - opening a Do row whose subject is a To Do asks the BFF for its inquiry; if there is one the picker lists the four dispositions and the
 *    replies (newest selected), and confirming posts `{ replyCommunicationId, disposition }` and reads the lanes again
 *  - a 403, a 404 and each 409 reason code say why in plain words and leave the To Do open
 *  - any other row (another To Do, a non-To Do subject, a Decide row, a closed copy) shows no picker and makes no lookup call
 *  - the widget reads no Dataverse and calls no Xrm (D-15)
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { WorklistWidget, createWorklistService } from '..';
import type { WorklistService } from '..';
import type { DecisionWizardLaunchRequest } from '../../DecisionWizard';
import {
  A1,
  D1,
  REPLY,
  ROUTE_BODIES,
  SERVICE_REQUEST_ID,
  T1,
  T2,
  T3,
  TODO_ID,
  fakeService,
  inquiryForTodo,
  inquiryLanesBody,
} from '../__fixtures__/worklistRouteFixtures';
import { InquiryRequestError } from '../worklistService';
import type { InquiryForTodo } from '../worklistService';

const TODAY = new Date(2026, 9, 9, 10, 30);

type Launch = jest.Mock<Promise<{ opened: boolean; reason?: 'no-host' | 'busy' }>, [DecisionWizardLaunchRequest]>;

/** A service whose inquiry lookup answers for the inquiry-outcome To Do only (every other To Do gets the route's 404: null). */
function inquiryService(inquiry: InquiryForTodo | null = inquiryForTodo) {
  const service = fakeService({ ...ROUTE_BODIES, lanes: inquiryLanesBody });
  service.findInquiryForTodo.mockImplementation(async todoId => (todoId === TODO_ID.inquiry ? inquiry : null));
  return service;
}

function renderWidget(service: WorklistService, dark = false) {
  const launchWizard: Launch = jest.fn(async () => ({ opened: true }));
  const utils = render(
    <FluentProvider theme={dark ? webDarkTheme : webLightTheme}>
      <WorklistWidget service={service} today={TODAY} launchWizard={launchWizard} />
    </FluentProvider>
  );
  return { ...utils, launchWizard };
}

const ready = () => screen.findByTestId('worklist-widget');
const line = (signalId: string) => {
  const el = document.querySelector(`[data-testid="issue-line"][data-signal-id="${signalId}"]`);
  if (!el) throw new Error(`no line for ${signalId}`);
  return el as HTMLElement;
};
const radio = (name: RegExp | string) => screen.getByRole('radio', { name });
const OLDER_REPLY = /^RE: Budget inquiry - received/;
const recordButton = () => screen.getByRole('button', { name: 'Record outcome' });

async function openPicker(service = inquiryService()) {
  const utils = renderWidget(service);
  await ready();
  await userEvent.click(line(T1));
  await screen.findByTestId('disposition-picker');
  return { ...utils, service };
}

describe('the picker (D-111)', () => {
  it('lists the four dispositions and the replies with the newest selected; nothing infers the outcome', async () => {
    const { service, launchWizard } = await openPicker();
    expect(service.findInquiryForTodo).toHaveBeenCalledTimes(1);
    expect(service.findInquiryForTodo).toHaveBeenCalledWith(TODO_ID.inquiry);
    expect(launchWizard).not.toHaveBeenCalled();

    expect(screen.getByRole('dialog')).toHaveAccessibleName('Record the outcome of the inquiry');
    for (const label of ['Write-off', 'Budget Revised', 'Scope Approved', 'No Action']) {
      expect(radio(label)).not.toBeChecked();
    }
    expect(radio(/revised estimate/)).toBeChecked();
    expect(radio(OLDER_REPLY)).not.toBeChecked();
    // The person has not chosen yet: recording is not offered.
    expect(recordButton()).toBeDisabled();
    await userEvent.click(radio('Write-off'));
    expect(recordButton()).toBeEnabled();
  });

  it('confirming posts { replyCommunicationId, disposition } for the chosen reply, then reads the lanes again', async () => {
    const { service } = await openPicker();
    await waitFor(() => expect(service.read).toHaveBeenCalledTimes(2));
    await userEvent.click(radio('Budget Revised'));
    await userEvent.click(recordButton());

    await waitFor(() => expect(service.recordDisposition).toHaveBeenCalledTimes(1));
    expect(service.recordDisposition).toHaveBeenCalledWith(SERVICE_REQUEST_ID, {
      replyCommunicationId: REPLY.newest,
      disposition: 100000001,
    });
    // The picker closes and the lanes (and Closed today) are read again.
    await waitFor(() => expect(screen.queryByTestId('disposition-picker')).not.toBeInTheDocument());
    await waitFor(() => expect(service.read).toHaveBeenCalledTimes(4));
    expect(
      (service.read as jest.Mock).mock.calls
        .slice(2)
        .map(c => c[0])
        .sort()
    ).toEqual(['closedToday', 'lanes']);
  });

  it.each([
    ['Write-off', 100000000],
    ['Budget Revised', 100000001],
    ['Scope Approved', 100000002],
    ['No Action', 100000003],
  ])('%s sends option value %i, against the reply the person chose', async (label, value) => {
    const { service } = await openPicker();
    await userEvent.click(radio(OLDER_REPLY));
    await userEvent.click(radio(label));
    await userEvent.click(recordButton());
    await waitFor(() => expect(service.recordDisposition).toHaveBeenCalledTimes(1));
    expect(service.recordDisposition).toHaveBeenCalledWith(SERVICE_REQUEST_ID, {
      replyCommunicationId: REPLY.older,
      disposition: value,
    });
  });

  it('cancelling records nothing, reads nothing again and leaves the To Do on the list', async () => {
    const { service, launchWizard } = await openPicker();
    await waitFor(() => expect(service.read).toHaveBeenCalledTimes(2));
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByTestId('disposition-picker')).not.toBeInTheDocument());
    expect(service.recordDisposition).not.toHaveBeenCalled();
    expect(service.read).toHaveBeenCalledTimes(2);
    expect(launchWizard).not.toHaveBeenCalled();
    expect(line(T1)).toBeInTheDocument();
  });

  it('"Open the decision wizard instead" opens the wizard on the same To Do, with no second lookup', async () => {
    const { service, launchWizard } = await openPicker();
    await userEvent.click(screen.getByRole('button', { name: 'Open the decision wizard instead' }));
    await waitFor(() => expect(launchWizard).toHaveBeenCalledTimes(1));
    expect(launchWizard.mock.calls[0][0]).toMatchObject({ itemId: T1 });
    expect(service.findInquiryForTodo).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId('disposition-picker')).not.toBeInTheDocument();
  });

  it('an inquiry with no reply filed yet cannot be recorded against nothing', async () => {
    await openPicker(inquiryService({ serviceRequestId: SERVICE_REQUEST_ID, replies: [] }));
    expect(screen.getByTestId('disposition-no-replies')).toHaveTextContent('No reply has been filed');
    await userEvent.click(radio('No Action'));
    expect(recordButton()).toBeDisabled();
  });

  it('renders in the dark theme', async () => {
    const service = inquiryService();
    renderWidget(service, true);
    await ready();
    await userEvent.click(line(T1));
    expect(await screen.findByTestId('disposition-picker')).toBeInTheDocument();
  });
});

describe('a refused disposition keeps the To Do open and says why in plain words', () => {
  it.each([
    [403, 'inquiry.denied', /don't have permission/],
    [404, null, /couldn't be found/],
    [409, 'inquiry.already_resolved', /already been recorded/],
    [409, 'inquiry.reply_mismatch', /isn't filed against this inquiry/],
    [409, 'inquiry.not_a_reply', /isn't a reply/],
    [409, 'inquiry.not_an_inquiry', /isn't an open inquiry/],
  ])('%i %s', async (status, reasonCode, expected) => {
    const { service } = await openPicker();
    await waitFor(() => expect(service.read).toHaveBeenCalledTimes(2));
    service.recordDisposition.mockRejectedValueOnce(new InquiryRequestError('route text', status, reasonCode));
    await userEvent.click(radio('Scope Approved'));
    await userEvent.click(recordButton());

    const error = await screen.findByTestId('disposition-error');
    expect(error).toHaveTextContent(expected);
    expect(error.textContent).not.toMatch(/route text|\d{3}|inquiry\.[a-z_]/);
    // Still open: the picker is up, the lanes were not read again, and the person can try again.
    expect(screen.getByTestId('disposition-picker')).toBeInTheDocument();
    expect(service.read).toHaveBeenCalledTimes(2);
    expect(recordButton()).toBeEnabled();
  });

  it('after a refusal the person can choose again and record', async () => {
    const { service } = await openPicker();
    service.recordDisposition.mockRejectedValueOnce(new InquiryRequestError('x', 409, 'inquiry.reply_mismatch'));
    await userEvent.click(radio('Write-off'));
    await userEvent.click(recordButton());
    await screen.findByTestId('disposition-error');
    await userEvent.click(radio(OLDER_REPLY));
    await userEvent.click(recordButton());
    await waitFor(() => expect(screen.queryByTestId('disposition-picker')).not.toBeInTheDocument());
    expect(service.recordDisposition).toHaveBeenCalledTimes(2);
    expect(service.recordDisposition).toHaveBeenLastCalledWith(SERVICE_REQUEST_ID, {
      replyCommunicationId: REPLY.older,
      disposition: 100000000,
    });
  });

  it('a network failure or an unexpected status is a plain try-again, not route text', async () => {
    const { service } = await openPicker();
    service.recordDisposition.mockRejectedValueOnce(
      new InquiryRequestError('Bad gateway upstream', 502, 'inquiry.failed')
    );
    await userEvent.click(radio('No Action'));
    await userEvent.click(recordButton());
    expect(await screen.findByTestId('disposition-error')).toHaveTextContent(
      "The outcome couldn't be recorded. Try again."
    );
  });
});

describe('no picker, and no lookup call, for any other row', () => {
  it('another To Do (the route finds no inquiry) opens the wizard as for any row', async () => {
    const service = inquiryService();
    const { launchWizard } = renderWidget(service);
    await ready();
    await userEvent.click(line(T2));
    await waitFor(() => expect(launchWizard).toHaveBeenCalledTimes(1));
    expect(launchWizard.mock.calls[0][0]).toMatchObject({ itemId: T2 });
    expect(service.findInquiryForTodo).toHaveBeenCalledTimes(1);
    expect(service.findInquiryForTodo).toHaveBeenCalledWith(TODO_ID.other);
    expect(screen.queryByTestId('disposition-picker')).not.toBeInTheDocument();
  });

  it('a Do row whose subject is not a To Do makes no lookup call', async () => {
    const service = inquiryService();
    const { launchWizard } = renderWidget(service);
    await ready();
    await userEvent.click(line(T3));
    await waitFor(() => expect(launchWizard).toHaveBeenCalledTimes(1));
    expect(launchWizard.mock.calls[0][0]).toMatchObject({ itemId: T3 });
    expect(service.findInquiryForTodo).not.toHaveBeenCalled();
    expect(screen.queryByTestId('disposition-picker')).not.toBeInTheDocument();
  });

  it('a Decide row makes no lookup call', async () => {
    const service = inquiryService();
    const { launchWizard } = renderWidget(service);
    await ready();
    await userEvent.click(line(A1));
    await waitFor(() => expect(launchWizard).toHaveBeenCalledTimes(1));
    expect(service.findInquiryForTodo).not.toHaveBeenCalled();
  });

  it('rows from a route that carries no subject reference make no lookup call', async () => {
    const service = fakeService();
    const { launchWizard } = renderWidget(service);
    await ready();
    await userEvent.click(line(D1));
    await waitFor(() => expect(launchWizard).toHaveBeenCalledTimes(1));
    expect(service.findInquiryForTodo).not.toHaveBeenCalled();
  });

  it('a closed copy of the inquiry To Do opens read-only with no lookup', async () => {
    const closedToday = {
      ...ROUTE_BODIES.closedToday,
      lanes: [
        { lane: 'Decide', groups: [] },
        {
          lane: 'Do',
          groups: [
            {
              core: {
                recordType: 'sprk_matter',
                recordId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                name: 'Acme v. Beta',
              },
              notFiled: false,
              items: [
                {
                  signalId: '30000000-0000-0000-0000-0000000000aa',
                  lane: 'Do',
                  status: 'Resolved',
                  subjectName: 'Record the outcome of the budget inquiry',
                  resolutionType: 100000000,
                  resolvedOn: '2026-10-09T09:00:00Z',
                  subjectRecordType: 'sprk_todo',
                  subjectRecordId: TODO_ID.inquiry,
                },
              ],
            },
          ],
        },
      ],
    };
    const service = fakeService({ ...ROUTE_BODIES, lanes: inquiryLanesBody, closedToday });
    service.findInquiryForTodo.mockResolvedValue(inquiryForTodo);
    const { launchWizard } = renderWidget(service);
    await ready();
    const disclosure = await screen.findByTestId('disclosure-closedToday');
    await userEvent.click(within(disclosure).getByRole('button'));
    await userEvent.click(await within(disclosure).findByTestId('issue-line'));
    await waitFor(() => expect(launchWizard).toHaveBeenCalledTimes(1));
    expect(service.findInquiryForTodo).not.toHaveBeenCalled();
  });
});

describe('a lookup that could not be answered', () => {
  it("says so and opens nothing: the To Do might be an inquiry's, and the wizard cannot complete one", async () => {
    const service = inquiryService();
    service.findInquiryForTodo.mockRejectedValue(new InquiryRequestError('x', 503, 'inquiry.unavailable'));
    const { launchWizard } = renderWidget(service);
    await ready();
    await userEvent.click(line(T1));
    expect(await screen.findByTestId('worklist-notice')).toHaveTextContent(
      'This To Do could not be opened because its inquiry could not be read. Try again.'
    );
    expect(launchWizard).not.toHaveBeenCalled();
    expect(screen.queryByTestId('disposition-picker')).not.toBeInTheDocument();
    // And the person can try again.
    service.findInquiryForTodo.mockResolvedValue(inquiryForTodo);
    await userEvent.click(line(T1));
    expect(await screen.findByTestId('disposition-picker')).toBeInTheDocument();
  });
});

describe('while a lookup is in flight', () => {
  it('a second click on a To Do row does not start a second lookup or a second picker', async () => {
    const service = inquiryService();
    const gate = { release: (_: InquiryForTodo | null) => {} };
    service.findInquiryForTodo.mockImplementation(
      () =>
        new Promise<InquiryForTodo | null>(resolve => {
          gate.release = resolve;
        })
    );
    renderWidget(service);
    await ready();
    await userEvent.click(line(T1));
    await userEvent.click(line(T2));
    expect(service.findInquiryForTodo).toHaveBeenCalledTimes(1);
    gate.release(inquiryForTodo);
    expect(await screen.findByTestId('disposition-picker')).toBeInTheDocument();
    expect(screen.getAllByTestId('disposition-picker')).toHaveLength(1);
  });
});

describe('end to end over the route client (the BFF is the only thing the widget calls)', () => {
  it('looks up, records with the chosen reply and outcome, and reads the lanes again', async () => {
    const calls: Array<{ url: string; method: string; body?: unknown }> = [];
    let lanesReads = 0;
    const respond = (json: unknown, status = 200) =>
      ({ ok: status < 300, status, json: async () => json }) as unknown as Response;
    const service = createWorklistService({
      baseUrl: 'https://bff.test',
      fetch: async (url, init) => {
        const method = init?.method ?? 'GET';
        calls.push({ url, method, body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined });
        if (url.includes('/signals/worklist')) {
          const set = new URL(url).searchParams.get('set') as keyof typeof ROUTE_BODIES;
          if (set === 'lanes') lanesReads += 1;
          return respond(set === 'lanes' ? inquiryLanesBody : ROUTE_BODIES[set]);
        }
        if (url.endsWith(`/api/v1/inquiries/for-todo/${TODO_ID.inquiry}`)) return respond(inquiryForTodo);
        if (url.includes('/api/v1/inquiries/for-todo/')) return respond({ title: 'Not found' }, 404);
        if (url.endsWith(`/api/v1/inquiries/${SERVICE_REQUEST_ID}/disposition`)) {
          return respond({ serviceRequestId: SERVICE_REQUEST_ID, todoCompleted: true });
        }
        return respond({}, 500);
      },
    });
    renderWidget(service);
    await ready();
    await userEvent.click(line(T1));
    await screen.findByTestId('disposition-picker');
    await userEvent.click(radio('No Action'));
    await userEvent.click(recordButton());
    await waitFor(() => expect(lanesReads).toBe(2));

    const post = calls.find(c => c.method === 'POST');
    expect(post).toMatchObject({
      url: `https://bff.test/api/v1/inquiries/${SERVICE_REQUEST_ID}/disposition`,
      body: { replyCommunicationId: REPLY.newest, disposition: 100000003 },
    });
    // Every call went to the BFF.
    expect(calls.every(c => c.url.startsWith('https://bff.test/api/v1/'))).toBe(true);
  });

  it('shows the 409 reason from a returned problem and keeps the To Do open', async () => {
    const respond = (json: unknown, status = 200) =>
      ({ ok: status < 300, status, json: async () => json }) as unknown as Response;
    const service = createWorklistService({
      baseUrl: 'https://bff.test',
      fetch: async (url, init) => {
        if (url.includes('/signals/worklist')) {
          const set = new URL(url).searchParams.get('set') as keyof typeof ROUTE_BODIES;
          return respond(set === 'lanes' ? inquiryLanesBody : ROUTE_BODIES[set]);
        }
        if (init?.method === 'POST') {
          return respond({ detail: 'raw route text', reasonCode: 'inquiry.already_resolved' }, 409);
        }
        return respond(inquiryForTodo);
      },
    });
    renderWidget(service);
    await ready();
    await userEvent.click(line(T1));
    await screen.findByTestId('disposition-picker');
    await userEvent.click(radio('Write-off'));
    await userEvent.click(recordButton());
    expect(await screen.findByTestId('disposition-error')).toHaveTextContent(
      'An outcome has already been recorded for this inquiry.'
    );
    expect(screen.getByTestId('disposition-picker')).toBeInTheDocument();
  });
});

describe('the widget reads no Dataverse (D-15) and is themed by tokens only (ADR-021)', () => {
  const dir = path.join(__dirname, '..');
  const code = (f: string) =>
    fs
      .readFileSync(path.join(dir, f), 'utf8')
      .split('\n')
      .filter(l => !/^\s*(\*|\/\/|\/\*)/.test(l))
      .join('\n');

  it('the picker, its helpers and the service name no table but the To Do and call no Xrm, WebApi or browser storage', () => {
    for (const f of [
      'InquiryDispositionPicker.tsx',
      'inquiryDisposition.ts',
      'worklistService.ts',
      'WorklistWidget.tsx',
    ]) {
      const source = code(f);
      expect({ f, hit: /Xrm|WebApi|retrieveMultiple|localStorage|sessionStorage|indexedDB/.test(source) }).toEqual({
        f,
        hit: false,
      });
      expect({ f, hit: /sprk_(?!todo\b)\w+/.test(source) }).toEqual({ f, hit: false });
    }
    // The To Do table is named in one place only.
    for (const f of ['InquiryDispositionPicker.tsx', 'worklistService.ts', 'WorklistWidget.tsx']) {
      expect({ f, hit: /sprk_todo/.test(code(f)) }).toEqual({ f, hit: false });
    }
  });

  it('the picker has no colour literal', () => {
    expect(/#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?)\(/.test(code('InquiryDispositionPicker.tsx'))).toBe(false);
  });

  it('the only inquiry calls are the two BFF routes', () => {
    const urls = code('worklistService.ts').match(/\/api\/v1\/[\w/-]+/g) ?? [];
    expect([...new Set(urls)].sort()).toEqual(['/api/v1/inquiries', '/api/v1/signals/worklist']);
  });
});
