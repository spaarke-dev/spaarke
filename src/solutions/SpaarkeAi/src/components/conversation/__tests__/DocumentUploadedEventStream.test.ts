/**
 * DocumentUploadedEventStream — Event-path SSE consumption tests.
 *
 * ai-architecture-redesign-r1 task 022b / FR-P1-03 / ADR-039.
 *
 * Verifies the client leg of the task-022 Event SSE contract
 * (`notes/task-022-event-sse-contract.md`):
 *   1. The endpoint URL + `{fileIds, typedCommand}` body flow through the
 *      canonical `readSseStream` (the ONE SSE consumption path — no
 *      hand-rolled reader).
 *   2. Every contract event type dispatches to its typed handler.
 *   3. Chips are forwarded through the single `onChips` seam from ALL THREE
 *      carrier events (`chips`, `event_confirmation`, `event_notice`).
 *   4. error/done terminal handling matches chat-stream conventions
 *      (+ defensive done when the server omits a terminal event).
 *   5. ProblemDetails errorCode mapping (ADR-019 stable codes).
 *   6. Pure formatters (classification line, ledger-payload markdown, notice).
 *
 * `readSseStream` is mocked at the package seam (the module under test must
 * call it — asserting the canonical-path invariant); `parseSseEvent` is the
 * REAL canonical parser so `data:` line handling is exercised verbatim.
 */

// Mocked readSseStream capture — the module under test must route through it.
const readSseStreamMock = jest.fn<Promise<void>, [Record<string, unknown>]>();

jest.mock('@spaarke/ui-components', () => {
  const actualHooks = jest.requireActual('@spaarke/ui-components/hooks/useSseStream');
  return {
    parseSseEvent: actualHooks.parseSseEvent,
    readSseStream: (...args: unknown[]) =>
      (readSseStreamMock as unknown as (...a: unknown[]) => Promise<void>)(...args),
  };
});

import {
  buildDocumentUploadedEventUrl,
  mapDocumentUploadedEventHttpError,
  runDocumentUploadedEvent,
  formatClassificationMessage,
  formatEventOutputMarkdown,
  formatNoticeMessage,
  type DocumentUploadedEventHandlers,
} from '../DocumentUploadedEventStream';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const BASE = 'https://test-bff.example.com';
const SESSION = '00000000-0000-0000-0000-000000000001';

/** Encode contract events as `data:` SSE lines fed to the captured onLine. */
function sseLine(event: Record<string, unknown>): string {
  return `data: ${JSON.stringify(event)}`;
}

function makeHandlers(): jest.Mocked<Required<DocumentUploadedEventHandlers>> {
  return {
    onClassification: jest.fn(),
    onOutput: jest.fn(),
    onConfirmation: jest.fn(),
    onNotice: jest.fn(),
    onChips: jest.fn(),
    onError: jest.fn(),
    onDone: jest.fn(),
  };
}

/** Run the module with a scripted stream of SSE lines. */
async function runWithLines(
  lines: string[],
  handlers: DocumentUploadedEventHandlers,
  overrides?: Partial<Parameters<typeof runDocumentUploadedEvent>[0]>
): Promise<Record<string, unknown>> {
  let captured: Record<string, unknown> = {};
  readSseStreamMock.mockImplementationOnce(async (options: Record<string, unknown>) => {
    captured = options;
    const onLine = options.onLine as (line: string) => void;
    for (const line of lines) onLine(line);
  });

  await runDocumentUploadedEvent({
    bffBaseUrl: BASE,
    sessionId: SESSION,
    fileIds: ['doc-1'],
    typedCommand: null,
    getAccessToken: async () => 'token',
    handlers,
    ...overrides,
  });
  return captured;
}

beforeEach(() => {
  readSseStreamMock.mockReset();
});

// ---------------------------------------------------------------------------
// URL + request contract
// ---------------------------------------------------------------------------

describe('Event endpoint contract (task 022 §1)', () => {
  it('builds the document-uploaded endpoint URL with an encoded session id', () => {
    expect(buildDocumentUploadedEventUrl(BASE, 'a/b c')).toBe(
      `${BASE}/api/ai/chat/sessions/a%2Fb%20c/events/document-uploaded`
    );
    // Trailing slash on the base is normalized (no `//api`).
    expect(buildDocumentUploadedEventUrl(`${BASE}/`, SESSION)).toBe(
      `${BASE}/api/ai/chat/sessions/${SESSION}/events/document-uploaded`
    );
  });

  it('POSTs {fileIds, typedCommand} through the canonical readSseStream', async () => {
    const captured = await runWithLines([sseLine({ type: 'done' })], makeHandlers(), {
      fileIds: ['doc-1', 'doc-2'],
      typedCommand: 'translate to French',
    });

    expect(readSseStreamMock).toHaveBeenCalledTimes(1);
    expect(captured.url).toBe(
      `${BASE}/api/ai/chat/sessions/${SESSION}/events/document-uploaded`
    );
    expect(captured.body).toEqual({
      fileIds: ['doc-1', 'doc-2'],
      typedCommand: 'translate to French',
    });
    expect(typeof captured.mapHttpError).toBe('function');
  });

  it('maps ProblemDetails errorCode on non-OK responses (ADR-019 stable codes)', async () => {
    const problemResponse = {
      status: 503,
      json: async () => ({ errorCode: 'ai.event-rules.disabled' }),
    } as unknown as Response;
    const err = await mapDocumentUploadedEventHttpError(problemResponse);
    expect(err.message).toContain('status=503');
    expect(err.message).toContain('errorCode=ai.event-rules.disabled');

    const nonJsonResponse = {
      status: 500,
      json: async () => {
        throw new Error('not json');
      },
    } as unknown as Response;
    const fallback = await mapDocumentUploadedEventHttpError(nonJsonResponse);
    expect(fallback.message).toContain('errorCode=event-rules.failed');
  });
});

// ---------------------------------------------------------------------------
// Event dispatch
// ---------------------------------------------------------------------------

describe('SSE event dispatch (task 022 §2)', () => {
  it('dispatches event_classification to onClassification with the contract data', async () => {
    const handlers = makeHandlers();
    const data = {
      fileId: 'doc-1',
      fileName: 'engagement-letter.pdf',
      docType: 'Engagement Letter',
      confidence: 0.92,
      bindingId: 'b-classify',
      ucid: 'UC-A-7',
      ledgerKey: 'lk-1',
    };
    await runWithLines(
      [sseLine({ type: 'event_classification', data }), sseLine({ type: 'done' })],
      handlers
    );
    expect(handlers.onClassification).toHaveBeenCalledWith(data);
  });

  it('dispatches event_output to onOutput carrying the STORED ledger payload (ADR-040)', async () => {
    const handlers = makeHandlers();
    const data = {
      bindingId: 'b-summarize',
      ucid: 'UC-A-1',
      ledgerKey: 'lk-2',
      disposition: 'informational',
      payload: { tldr: 'Short version.', summary: 'Long version.' },
    };
    await runWithLines(
      [sseLine({ type: 'event_output', data }), sseLine({ type: 'done' })],
      handlers
    );
    expect(handlers.onOutput).toHaveBeenCalledWith(data);
  });

  it('forwards chips from ALL THREE carrier events through the single onChips seam', async () => {
    const handlers = makeHandlers();
    const confirmChips = [
      { targetBindingId: 'b-confirm', label: 'Yes, engagement letter', args: { fileIds: ['doc-1'], confirmedDocType: 'Engagement Letter' } },
    ];
    const noticeChips = [
      { targetBindingId: 'b-manual', label: 'Run it now', args: { fileIds: ['doc-1'] } },
    ];
    const nextStepChips = [
      { targetBindingId: 'b-all', label: 'Summarize all 3 files?', args: { fileIds: ['doc-1', 'doc-2', 'doc-3'] } },
    ];

    await runWithLines(
      [
        sseLine({
          type: 'event_confirmation',
          data: { fileId: 'doc-1', docType: 'Letter', confidence: 0.4, threshold: 0.85, message: 'Is this an Engagement Letter?', chips: confirmChips },
        }),
        sseLine({ type: 'event_notice', data: { reason: 'daily-cap', message: 'Daily limit reached.', chips: noticeChips } }),
        sseLine({ type: 'chips', data: { sourceBindingId: 'b-summarize', chips: nextStepChips } }),
        sseLine({ type: 'done' }),
      ],
      handlers
    );

    expect(handlers.onConfirmation).toHaveBeenCalledTimes(1);
    expect(handlers.onNotice).toHaveBeenCalledTimes(1);
    expect(handlers.onChips).toHaveBeenCalledTimes(3);
    expect(handlers.onChips).toHaveBeenNthCalledWith(1, confirmChips);
    expect(handlers.onChips).toHaveBeenNthCalledWith(2, noticeChips);
    expect(handlers.onChips).toHaveBeenNthCalledWith(3, nextStepChips);
  });

  it('does NOT forward empty or absent chip arrays', async () => {
    const handlers = makeHandlers();
    await runWithLines(
      [
        sseLine({ type: 'event_notice', data: { reason: 'superseded', message: 'Following your instructions instead.' } }),
        sseLine({ type: 'chips', data: { sourceBindingId: 'b-x', chips: [] } }),
        sseLine({ type: 'done' }),
      ],
      handlers
    );
    expect(handlers.onNotice).toHaveBeenCalledTimes(1);
    expect(handlers.onChips).not.toHaveBeenCalled();
  });

  it('error event delivers the safe content; missing content falls back (chat-stream shape)', async () => {
    const handlers = makeHandlers();
    await runWithLines(
      [sseLine({ type: 'error', content: 'Something went wrong processing your files.' })],
      handlers
    );
    expect(handlers.onError).toHaveBeenCalledWith('Something went wrong processing your files.');
    expect(handlers.onDone).not.toHaveBeenCalled(); // error was the terminal — no defensive done

    const handlers2 = makeHandlers();
    await runWithLines([sseLine({ type: 'error' })], handlers2);
    expect(handlers2.onError).toHaveBeenCalledWith('Stream error');
  });

  it('done is terminal; a stream ending WITHOUT a terminal event invokes onDone defensively', async () => {
    const handlers = makeHandlers();
    await runWithLines([sseLine({ type: 'done' })], handlers);
    expect(handlers.onDone).toHaveBeenCalledTimes(1);

    const handlers2 = makeHandlers();
    await runWithLines(
      [sseLine({ type: 'event_classification', data: { fileId: 'doc-1' } })],
      handlers2
    );
    expect(handlers2.onDone).toHaveBeenCalledTimes(1);
  });

  it('ignores unknown event types and malformed lines (tolerant, never throws)', async () => {
    const handlers = makeHandlers();
    await runWithLines(
      [
        'event: keepalive',
        'data: {not-json',
        sseLine({ type: 'some_future_event', data: { x: 1 } }),
        sseLine({ type: 'done' }),
      ],
      handlers
    );
    expect(handlers.onClassification).not.toHaveBeenCalled();
    expect(handlers.onError).not.toHaveBeenCalled();
    expect(handlers.onDone).toHaveBeenCalledTimes(1);
  });

  it('rejects when readSseStream rejects (HTTP/network failure propagates to the caller)', async () => {
    readSseStreamMock.mockRejectedValueOnce(new Error('documentUploadedEvent: request failed (status=503, errorCode=ai.event-rules.disabled)'));
    await expect(
      runDocumentUploadedEvent({
        bffBaseUrl: BASE,
        sessionId: SESSION,
        fileIds: ['doc-1'],
        typedCommand: null,
        getAccessToken: async () => 'token',
        handlers: makeHandlers(),
      })
    ).rejects.toThrow('errorCode=ai.event-rules.disabled');
  });
});

// ---------------------------------------------------------------------------
// Formatters
// ---------------------------------------------------------------------------

describe('formatters (pure)', () => {
  it('formatClassificationMessage renders name, docType and rounded confidence', () => {
    expect(
      formatClassificationMessage({ fileName: 'engagement-letter.pdf', docType: 'Engagement Letter', confidence: 0.923 })
    ).toBe('Classified "engagement-letter.pdf" as **Engagement Letter** (92% confidence).');
  });

  it('formatClassificationMessage tolerates missing fields', () => {
    expect(formatClassificationMessage({})).toBe('Classified "your document" as **a document**.');
  });

  it('formatEventOutputMarkdown renders SUM-CHAT@v1 tldr/summary/keywords', () => {
    const md = formatEventOutputMarkdown({
      tldr: 'Short version.',
      summary: 'Long version.',
      keywords: ['contract', 'engagement'],
    });
    expect(md).toContain('**TL;DR:** Short version.');
    expect(md).toContain('Long version.');
    expect(md).toContain('**Keywords:** contract, engagement');
  });

  it('formatEventOutputMarkdown renders string payloads verbatim and degrades unknown shapes to fenced JSON', () => {
    expect(formatEventOutputMarkdown('Plain stored text.')).toBe('Plain stored text.');
    const md = formatEventOutputMarkdown({ classification: 'invoice', amount: 12 });
    expect(md).toContain('```json');
    expect(md).toContain('"classification": "invoice"');
    expect(formatEventOutputMarkdown(null)).toBe('Analysis complete.');
  });

  // Draft-a-response (draft-correspondence) rendering fix — UAT 2026-07-19.
  it('formatEventOutputMarkdown renders a draft-correspondence payload readably (not raw JSON)', () => {
    const md = formatEventOutputMarkdown({
      subject: 'Re: NDA revisions',
      body: 'Thanks for sending the draft.\n\nWe accept clauses 1–3.',
      recipients_suggestion: ['counsel@acme.com', 'Jane Roe'],
      cited_refs: [{ title: 'NDA v3' }, 'Engagement letter'],
    });
    expect(md).not.toContain('```json');
    expect(md).toContain('**Draft response**');
    expect(md).toContain('**Subject:** Re: NDA revisions');
    expect(md).toContain('We accept clauses 1–3.');
    expect(md).toContain('**Suggested recipients:** counsel@acme.com, Jane Roe');
    expect(md).toContain('**Sources:** NDA v3, Engagement letter');
  });

  it('formatEventOutputMarkdown treats a body-only correspondence payload as a draft, and does not hijack summary shapes', () => {
    // body + subject → draft
    expect(formatEventOutputMarkdown({ subject: 'Hi', body: 'A short note.' })).toContain('**Draft response**');
    // tldr/summary payloads still render as a summary, never as a draft
    const summary = formatEventOutputMarkdown({ summary: 'A summary.', body: 'ignored' });
    // `body` with no subject/recipients/cited_refs is NOT a correspondence draft → summary branch wins
    expect(summary).toContain('A summary.');
    expect(summary).not.toContain('**Draft response**');
  });

  it('formatNoticeMessage renders the server message subtly (italic) with a defensive fallback', () => {
    expect(formatNoticeMessage({ reason: 'daily-cap', message: 'Daily limit reached.' })).toBe('_Daily limit reached._');
    expect(formatNoticeMessage({ reason: 'no-rule' })).toBe('_The automatic document workflow did not run._');
  });
});
