/**
 * The route fields task 154 added (sentence, subject reference), the two inquiry routes the D-111 picker uses (task 154's read,
 * task 071's write), the four dispositions and the plain-words failures (task 155).
 */

import { createWorklistService, parseWorklistResponse } from '..';
import {
  REPLY,
  SERVICE_REQUEST_ID,
  body,
  group,
  inquiryForTodo,
  matterCore,
  routeItem,
} from '../__fixtures__/worklistRouteFixtures';
import { DISPOSITION_OPTIONS, dispositionFailureMessage, inquiryTodoIdOf } from '../inquiryDisposition';
import { InquiryRequestError } from '../worklistService';
import type { WorklistGroupData, WorklistRouteItem } from '../worklistService';
import { buildWizardItem } from '../worklistWizardItem';

const TODAY = new Date(2026, 9, 9, 10, 30);
const ok = (json: unknown) => ({ ok: true, status: 200, json: async () => json }) as unknown as Response;
const refused = (status: number, json: unknown) =>
  ({ ok: false, status, json: async () => json }) as unknown as Response;
const apiError = (status: number, problem?: Record<string, unknown>) =>
  Object.assign(new Error(`HTTP ${status}`), { name: 'ApiError', status, problemDetails: problem });

describe('the route fields task 154 added', () => {
  it('reads sentence, subjectRecordType and subjectRecordId; an empty or missing value is null', () => {
    const data = parseWorklistResponse(
      body(
        'lanes',
        [group(matterCore, [routeItem('d', 'Decide')])],
        [
          group(matterCore, [
            routeItem('a', 'Do', {
              sentence: 'A To Do is overdue.',
              subjectRecordType: 'sprk_todo',
              subjectRecordId: 'x1',
            }),
            routeItem('b', 'Do', { sentence: '', subjectRecordType: '', subjectRecordId: null }),
            routeItem('c', 'Do'),
          ]),
        ]
      ),
      'lanes'
    );
    const [a, b, c] = data.Do[0].items;
    expect(a).toMatchObject({ sentence: 'A To Do is overdue.', subjectRecordType: 'sprk_todo', subjectRecordId: 'x1' });
    expect(b).toMatchObject({ sentence: null, subjectRecordType: null, subjectRecordId: null });
    expect(c).toMatchObject({ sentence: null, subjectRecordType: null, subjectRecordId: null });
    expect(data.Decide[0].items[0].subjectRecordId).toBeNull();
  });

  describe('the wizard item', () => {
    const g: WorklistGroupData = {
      core: { recordType: 'sprk_matter', recordId: 'm1' },
      items: [{ signalId: 'a', lane: 'Decide' }],
    };

    it('passes the rule sentence through (D-23)', () => {
      const item: WorklistRouteItem = { ...g.items[0], sentence: 'Spend is over budget on this matter.' };
      expect(buildWizardItem({ item, group: g, set: 'lanes', today: TODAY }).sentence).toBe(
        'Spend is over budget on this matter.'
      );
    });

    it('passes null when the Signal stores none, so the wizard keeps its existing statement', () => {
      expect(buildWizardItem({ item: g.items[0], group: g, set: 'lanes', today: TODAY }).sentence).toBeNull();
      const explicit: WorklistRouteItem = { ...g.items[0], sentence: null };
      expect(buildWizardItem({ item: explicit, group: g, set: 'lanes', today: TODAY }).sentence).toBeNull();
    });
  });
});

describe('inquiryTodoIdOf (which rows can have a picker)', () => {
  const row = (extra: Partial<WorklistRouteItem>): WorklistRouteItem => ({ signalId: 's', lane: 'Do', ...extra });

  it('only a Do row whose subject is a To Do', () => {
    expect(inquiryTodoIdOf(row({ subjectRecordType: 'sprk_todo', subjectRecordId: 'todo-1' }))).toBe('todo-1');
    expect(inquiryTodoIdOf(row({ subjectRecordType: 'sprk_event', subjectRecordId: 'ev-1' }))).toBeNull();
    expect(inquiryTodoIdOf(row({ subjectRecordType: 'sprk_workassignment', subjectRecordId: 'w-1' }))).toBeNull();
    expect(inquiryTodoIdOf(row({}))).toBeNull();
    expect(inquiryTodoIdOf(row({ subjectRecordType: 'sprk_todo', subjectRecordId: null }))).toBeNull();
    // A Decide row never has one, whatever it carries.
    expect(
      inquiryTodoIdOf(row({ lane: 'Decide', subjectRecordType: 'sprk_todo', subjectRecordId: 'todo-1' }))
    ).toBeNull();
  });
});

describe('the inquiry routes (task 154 read, task 071 write)', () => {
  const forTodoBody = {
    serviceRequestId: SERVICE_REQUEST_ID,
    replies: [
      {
        communicationId: REPLY.newest,
        subject: 'RE: Budget inquiry - revised estimate',
        receivedOn: '2026-10-08T15:00:00Z',
      },
      { communicationId: REPLY.older, subject: null, receivedOn: null },
      { subject: 'no id: skipped' },
    ],
  };

  it('looks the inquiry up by the To Do id: the replies in the order the route gave them', async () => {
    const calls: Array<[string, RequestInit | undefined]> = [];
    const service = createWorklistService({
      fetch: async (url, init) => {
        calls.push([url, init]);
        return ok(forTodoBody);
      },
      baseUrl: 'https://bff.test/',
    });
    await expect(service.findInquiryForTodo('todo-1')).resolves.toEqual({
      serviceRequestId: SERVICE_REQUEST_ID,
      replies: [
        {
          communicationId: REPLY.newest,
          subject: 'RE: Budget inquiry - revised estimate',
          receivedOn: '2026-10-08T15:00:00Z',
        },
        { communicationId: REPLY.older, subject: null, receivedOn: null },
      ],
    });
    expect(calls).toHaveLength(1);
    expect(calls[0][0]).toBe('https://bff.test/api/v1/inquiries/for-todo/todo-1');
    expect(calls[0][1]).toMatchObject({ method: 'GET' });
  });

  it('a 404 is "no inquiry" (null), whether it is returned or thrown', async () => {
    const returned = createWorklistService({
      fetch: async () => refused(404, { title: 'Not found' }),
      baseUrl: 'https://bff.test',
    });
    await expect(returned.findInquiryForTodo('t')).resolves.toBeNull();
    const thrown = createWorklistService({
      fetch: async () => {
        throw apiError(404);
      },
      baseUrl: 'https://bff.test',
    });
    await expect(thrown.findInquiryForTodo('t')).resolves.toBeNull();
  });

  it('a 503 inquiry.unavailable, a network error and an unreadable 200 are errors, never "no inquiry"', async () => {
    const unavailable = createWorklistService({
      fetch: async () =>
        refused(503, { detail: 'The inquiry could not be read. Try again.', reasonCode: 'inquiry.unavailable' }),
      baseUrl: 'https://bff.test',
    });
    await expect(unavailable.findInquiryForTodo('t')).rejects.toMatchObject({
      name: 'InquiryRequestError',
      httpStatus: 503,
      reasonCode: 'inquiry.unavailable',
      message: 'The inquiry could not be read. Try again.',
    });
    const offline = createWorklistService({
      fetch: async () => {
        throw new TypeError('Failed to fetch');
      },
      baseUrl: 'https://bff.test',
    });
    await expect(offline.findInquiryForTodo('t')).rejects.toMatchObject({ httpStatus: null });
    const unreadable = createWorklistService({ fetch: async () => ok({ replies: [] }), baseUrl: 'https://bff.test' });
    await expect(unreadable.findInquiryForTodo('t')).rejects.toBeInstanceOf(InquiryRequestError);
  });

  it('records the disposition: POST /api/v1/inquiries/{id}/disposition with { replyCommunicationId, disposition }', async () => {
    const calls: Array<[string, RequestInit | undefined]> = [];
    const service = createWorklistService({
      fetch: async (url, init) => {
        calls.push([url, init]);
        return ok({ serviceRequestId: SERVICE_REQUEST_ID, todoCompleted: true });
      },
      baseUrl: 'https://bff.test',
    });
    await expect(
      service.recordDisposition(SERVICE_REQUEST_ID, { replyCommunicationId: REPLY.newest, disposition: 100000001 })
    ).resolves.toEqual({ todoCompleted: true });
    expect(calls[0][0]).toBe(`https://bff.test/api/v1/inquiries/${SERVICE_REQUEST_ID}/disposition`);
    expect(calls[0][1]).toMatchObject({ method: 'POST' });
    expect(JSON.parse(String(calls[0][1]?.body))).toEqual({
      replyCommunicationId: REPLY.newest,
      disposition: 100000001,
    });
    expect(calls[0][1]?.headers).toMatchObject({ 'Content-Type': 'application/json' });
  });

  it('reads todoCompleted from the 200 body; false, missing or an unreadable body is "not completed"', async () => {
    const send = { replyCommunicationId: 'r', disposition: 100000000 };
    const answer = (readBody: () => Promise<unknown>) =>
      createWorklistService({
        fetch: async () => ({ ok: true, status: 200, json: readBody }) as unknown as Response,
        baseUrl: 'https://bff.test',
      }).recordDisposition('s', send);
    await expect(answer(async () => ({ todoCompleted: true }))).resolves.toEqual({ todoCompleted: true });
    await expect(answer(async () => ({ todoCompleted: false }))).resolves.toEqual({ todoCompleted: false });
    await expect(answer(async () => ({}))).resolves.toEqual({ todoCompleted: false });
    await expect(
      answer(async () => {
        throw new SyntaxError('no body');
      })
    ).resolves.toEqual({ todoCompleted: false });
  });

  it('a refused disposition carries the status and the reason code, returned or thrown', async () => {
    const send = { replyCommunicationId: 'r', disposition: 100000000 };
    const returned = createWorklistService({
      fetch: async () => refused(409, { detail: 'Already resolved.', reasonCode: 'inquiry.already_resolved' }),
      baseUrl: 'https://bff.test',
    });
    await expect(returned.recordDisposition('s', send)).rejects.toMatchObject({
      httpStatus: 409,
      reasonCode: 'inquiry.already_resolved',
    });
    const thrown = createWorklistService({
      fetch: async () => {
        throw apiError(409, { detail: 'Mismatch.', reasonCode: 'inquiry.reply_mismatch' });
      },
      baseUrl: 'https://bff.test',
    });
    await expect(thrown.recordDisposition('s', send)).rejects.toMatchObject({
      httpStatus: 409,
      reasonCode: 'inquiry.reply_mismatch',
    });
  });

  it('the widget fixture is a shape the reader accepts unchanged', async () => {
    const service = createWorklistService({ fetch: async () => ok(inquiryForTodo), baseUrl: 'https://bff.test' });
    await expect(service.findInquiryForTodo('t')).resolves.toEqual(inquiryForTodo);
  });
});

describe('the four dispositions and the plain-words failures (D-111)', () => {
  it('offers exactly Write-off, Budget Revised, Scope Approved and No Action with their option values', () => {
    expect(DISPOSITION_OPTIONS.map(o => [o.label, o.value])).toEqual([
      ['Write-off', 100000000],
      ['Budget Revised', 100000001],
      ['Scope Approved', 100000002],
      ['No Action', 100000003],
    ]);
  });

  const failure = (status: number | null, reasonCode: string | null) =>
    new InquiryRequestError('raw', status, reasonCode);

  it.each([
    [403, 'inquiry.denied', /permission/],
    [404, null, /couldn't be found/],
    [409, 'inquiry.already_resolved', /already been recorded/],
    [409, 'inquiry.reply_mismatch', /isn't filed against this inquiry/],
    [409, 'inquiry.not_a_reply', /isn't a reply/],
    [409, 'inquiry.not_an_inquiry', /isn't an open inquiry/],
    [409, 'inquiry.something_new', /inquiry has changed/],
    [400, 'inquiry.invalid', /isn't valid/],
    [502, 'inquiry.failed', /couldn't be recorded/],
    [null, null, /couldn't be recorded/],
  ])('%s %s reads in plain words', (status, code, expected) => {
    const message = dispositionFailureMessage(failure(status, code));
    expect(message).toMatch(expected);
    // Plain words: no status number, no reason code, no route text.
    expect(message).not.toMatch(/\d{3}|inquiry\.[a-z_]|raw/);
  });

  it('an error that is not an InquiryRequestError is the generic try-again', () => {
    expect(dispositionFailureMessage(new Error('boom'))).toBe("The outcome couldn't be recorded. Try again.");
  });
});
