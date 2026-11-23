/**
 * Task 133: the open email's earlier invoice submission (ebilling 095a, contract
 * projects/ebilling-r1/notes/095-from-email-contract.md §11): the request, which answers mean "nothing to show", which
 * ones are a failure to decide, how the 200 body is read, and following a running submission to its final status.
 */
import type { IHostAdapter } from '@shared/adapters';
import {
  createPreviousSubmissionFollower,
  createPreviousSubmissionLoader,
  loadPreviousSubmission,
  SUBMISSIONS_BY_EMAIL_ROUTE,
} from '../invoiceSubmissionService';
import {
  describeSubmittedLine,
  previousSubmissionTitle,
  type InvoicePreviousSubmission,
} from '../invoiceSubmissionSeam';
import { HELD_FOR_REVIEW_MESSAGE } from '../../utils/invoiceSubmissionMessages';

const BASE = 'https://bff.example';
const config = { apiBaseUrl: BASE, getAccessToken: async () => 'token' };
const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

function res(status: number, body: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: () => null },
    json: async () => {
      if (body === undefined) throw new Error('no body');
      return body;
    },
  };
}

const MESSAGE_ID = '<CA+abc123@mail.firm.example>';
const SUBMISSION_ID = '5a1a526f-0000-4000-8000-000000000001';

/** A §11 200 body (the by-id body plus submittedOn / submittedBy / isCorrection). */
function body(over: Record<string, unknown> = {}) {
  return {
    submissionId: SUBMISSION_ID,
    trackingNumber: 'INV-792345.3',
    status: 'Accepted',
    invoiceId: '3d3009f7-0000-4000-8000-000000000002',
    submittedOn: '2026-11-03T14:04:38Z',
    submittedBy: { name: 'Ana Diaz' },
    isCorrection: false,
    ...over,
  };
}

beforeEach(() => mockFetch.mockReset());

describe('loadPreviousSubmission (§11)', () => {
  it('asks for the Message-ID as Outlook gives it, encoded, with the bearer token', async () => {
    mockFetch.mockResolvedValueOnce(res(200, body()));
    await loadPreviousSubmission(MESSAGE_ID, config);
    const [url, init] = mockFetch.mock.calls[0]!;
    expect(url).toBe(`${BASE}${SUBMISSIONS_BY_EMAIL_ROUTE}?internetMessageId=${encodeURIComponent(MESSAGE_ID)}`);
    expect(String(url)).not.toContain('<');
    expect((init as RequestInit).method).toBe('GET');
  });

  it('reads the 200 body: who, when, tracking number, status, invoice and the status URL to follow', async () => {
    mockFetch.mockResolvedValueOnce(res(200, body()));
    const found = await loadPreviousSubmission(MESSAGE_ID, config);
    expect(found).toEqual({
      submissionId: SUBMISSION_ID,
      statusUrl: `${SUBMISSIONS_BY_EMAIL_ROUTE}/${SUBMISSION_ID}`,
      trackingNumber: 'INV-792345.3',
      status: 'Accepted',
      message: null,
      invoiceId: '3d3009f7-0000-4000-8000-000000000002',
      submittedOn: '2026-11-03T14:04:38Z',
      submittedBy: 'Ana Diaz',
      isCorrection: false,
    });
  });

  it('a null or nameless submittedBy, and a correction, are read as such', async () => {
    mockFetch.mockResolvedValueOnce(res(200, body({ submittedBy: null, isCorrection: true })));
    expect(await loadPreviousSubmission(MESSAGE_ID, config)).toMatchObject({ submittedBy: null, isCorrection: true });
    mockFetch.mockResolvedValueOnce(res(200, body({ submittedBy: { name: '  ' } })));
    expect(await loadPreviousSubmission(MESSAGE_ID, config)).toMatchObject({ submittedBy: null });
  });

  it('words a final status from its outcome code; the view never gets the code', async () => {
    mockFetch.mockResolvedValueOnce(res(200, body({ outcomeReasonCode: 'ebilling.held.duplicate_suspected' })));
    const held = await loadPreviousSubmission(MESSAGE_ID, config);
    expect(held?.message).toBe(HELD_FOR_REVIEW_MESSAGE);
    expect(held).not.toHaveProperty('outcomeReasonCode');

    mockFetch.mockResolvedValueOnce(
      res(200, body({ status: 'Failed', outcomeReasonCode: 'ai.invoice.read.not_configured', invoiceId: undefined }))
    );
    const failed = await loadPreviousSubmission(MESSAGE_ID, config);
    expect(failed?.status).toBe('Failed');
    expect(failed?.message).toEqual(expect.any(String));
    expect(failed?.message).not.toContain('ai.invoice');
    expect(failed?.invoiceId).toBeNull();

    mockFetch.mockResolvedValueOnce(res(200, body({ status: 'Processing', invoiceId: undefined })));
    expect((await loadPreviousSubmission(MESSAGE_ID, config))?.message).toBeNull();
  });

  it('EB-D-185: an Accepted submission made without a firm says so (the banner, via the same wording)', async () => {
    mockFetch.mockResolvedValueOnce(res(200, body({ outcomeReasonCode: 'ebilling.submission.firm_missing' })));
    const previous = await loadPreviousSubmission(MESSAGE_ID, config);
    expect(previous?.status).toBe('Accepted');
    expect(previous?.message).toBe('Submitted without a firm. A reviewer will add the firm before approval.');
    expect(previous).not.toHaveProperty('outcomeReasonCode');
  });

  it('names the correcting submission of an invoice replaced by a correction (097f)', async () => {
    mockFetch.mockResolvedValueOnce(
      res(
        200,
        body({
          outcomeReasonCode: 'ebilling.submission.replaced_by_correction',
          replacedBy: { trackingNumber: 'INV-792345.4' },
        })
      )
    );
    expect((await loadPreviousSubmission(MESSAGE_ID, config))?.message).toContain('INV-792345.4');
  });

  it('the uniform 404 and 503 ebilling.disabled mean nothing to show', async () => {
    mockFetch.mockResolvedValueOnce(res(404, { reasonCode: 'sdap.access.deny.record_unavailable' }));
    expect(await loadPreviousSubmission(MESSAGE_ID, config)).toBeNull();
    mockFetch.mockResolvedValueOnce(res(503, { errorCode: 'ebilling.disabled' }));
    expect(await loadPreviousSubmission(MESSAGE_ID, config)).toBeNull();
  });

  it.each([
    ['a read fault (503 record_unavailable)', res(503, { errorCode: 'ebilling.submission.record_unavailable' })],
    ['a 503 with no body', res(503, undefined)],
    ['a 500', res(500, {})],
    ['a 403', res(403, {})],
  ])('%s THROWS: never "not submitted" when Spaarke could not decide', async (_label, answer) => {
    mockFetch.mockResolvedValueOnce(answer);
    await expect(loadPreviousSubmission(MESSAGE_ID, config)).rejects.toThrow(/already submitted/);
  });

  it('no answer at all THROWS', async () => {
    mockFetch.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    await expect(loadPreviousSubmission(MESSAGE_ID, config)).rejects.toThrow(/Couldn't reach Spaarke/);
  });

  it.each([
    ['no submission id', body({ submissionId: undefined })],
    ['a status this build does not know', body({ status: 'Archived' })],
    ['no JSON object', 'text'],
  ])('a 200 with %s THROWS (unreadable)', async (_label, answer) => {
    mockFetch.mockResolvedValueOnce(res(200, answer));
    await expect(loadPreviousSubmission(MESSAGE_ID, config)).rejects.toThrow(/couldn't be read/);
  });
});

function adapter(over: { canResolveEmailIdentity?: boolean; keys?: () => Promise<unknown> } = {}): IHostAdapter {
  return {
    getCapabilities: () => ({ canResolveEmailIdentity: over.canResolveEmailIdentity ?? true }),
    getEmailIdentityKeys: over.keys ?? (async () => ({ internetMessageId: MESSAGE_ID, itemId: 'AAMk...' })),
  } as unknown as IHostAdapter;
}

describe('createPreviousSubmissionLoader — the open email', () => {
  it('looks up the open email by its RFC Message-ID', async () => {
    mockFetch.mockResolvedValueOnce(res(200, body()));
    const found = await createPreviousSubmissionLoader({ config, adapter: adapter() })();
    expect(found?.trackingNumber).toBe('INV-792345.3');
    expect(String(mockFetch.mock.calls[0]![0])).toContain(encodeURIComponent(MESSAGE_ID));
  });

  it.each([
    ['the host cannot resolve the email identity', adapter({ canResolveEmailIdentity: false })],
    ['the email has no Message-ID (a draft)', adapter({ keys: async () => ({ internetMessageId: '' }) })],
    [
      'the Message-ID cannot be read',
      adapter({
        keys: async () => {
          throw new Error('Office error');
        },
      }),
    ],
  ])('nothing is asked when %s', async (_label, host) => {
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    expect(await createPreviousSubmissionLoader({ config, adapter: host })()).toBeNull();
    expect(mockFetch).not.toHaveBeenCalled();
  });
});

const RUNNING: InvoicePreviousSubmission = {
  submissionId: SUBMISSION_ID,
  statusUrl: `${SUBMISSIONS_BY_EMAIL_ROUTE}/${SUBMISSION_ID}`,
  trackingNumber: 'INV-012.2',
  status: 'Processing',
  message: null,
  invoiceId: null,
  submittedOn: '2026-11-03T14:04:38Z',
  submittedBy: 'Ana Diaz',
  isCorrection: false,
};

describe('createPreviousSubmissionFollower — a running submission to its final status', () => {
  const poll = { initialDelayMs: 0, maxDelayMs: 0, maxAttempts: 3, sleep: async () => undefined };

  it('follows the status URL and updates the status and its sentence', async () => {
    mockFetch
      .mockResolvedValueOnce(res(200, { status: 'Processing' }))
      .mockResolvedValueOnce(res(200, { status: 'Failed', outcomeReasonCode: 'ai.invoice.read.not_configured' }));
    const followed = await createPreviousSubmissionFollower({ config, poll })(RUNNING);
    expect(String(mockFetch.mock.calls[0]![0])).toBe(`${BASE}${RUNNING.statusUrl}`);
    expect(followed).toMatchObject({ status: 'Failed', trackingNumber: 'INV-012.2', submittedBy: 'Ana Diaz' });
    expect(followed?.message).toEqual(expect.any(String));
  });

  it('leaves it as it was when the answer is not known in time', async () => {
    mockFetch.mockResolvedValue(res(200, { status: 'Processing' }));
    expect(await createPreviousSubmissionFollower({ config, poll })(RUNNING)).toEqual(RUNNING);
  });

  it('resolves null once the view went away', async () => {
    const controller = new AbortController();
    controller.abort();
    expect(await createPreviousSubmissionFollower({ config, poll })(RUNNING, controller.signal)).toBeNull();
    expect(mockFetch).not.toHaveBeenCalled();
  });
});

describe('the banner words', () => {
  it('says when, as what and by whom — each part only when known', () => {
    const when = new Date('2026-11-03T14:04:38Z').toLocaleString('en-US', { dateStyle: 'medium', timeStyle: 'short' });
    expect(describeSubmittedLine(RUNNING, 'en-US')).toBe(`Submitted on ${when} as INV-012.2 by Ana Diaz.`);
    expect(
      describeSubmittedLine(
        { ...RUNNING, submittedOn: null, submittedBy: null, trackingNumber: null, isCorrection: true },
        'en-US'
      )
    ).toBe('A correction was submitted.');
    expect(describeSubmittedLine({ ...RUNNING, submittedOn: 'not a date' }, 'en-US')).toBe(
      'Submitted as INV-012.2 by Ana Diaz.'
    );
  });

  it('titles each status', () => {
    expect(previousSubmissionTitle({ ...RUNNING, status: 'Accepted' })).toBe(
      "This email's invoice was already submitted"
    );
    expect(previousSubmissionTitle({ ...RUNNING, status: 'Received' })).toBe("This email's invoice is being processed");
    expect(previousSubmissionTitle({ ...RUNNING, status: 'Failed' })).toBe(
      "This email's invoice could not be processed"
    );
    expect(previousSubmissionTitle({ ...RUNNING, status: 'Rejected' })).toBe("This email's invoice was rejected");
  });
});
