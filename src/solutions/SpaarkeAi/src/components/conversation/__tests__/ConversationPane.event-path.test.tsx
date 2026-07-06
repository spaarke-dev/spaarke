/**
 * ConversationPane — Event-path (document-uploaded) wiring tests.
 *
 * ai-architecture-redesign-r1 task 022b / FR-P1-03 / ADR-039.
 *
 * Covers the client leg of the Event entry path at the component boundary:
 *   1. Attach-gesture completion (count-complete batching — G-P1 UAT round-1
 *      Defect-2 fix, 2026-07-05: the batch fires the moment EVERY chip of the
 *      gesture has its /documents 202, however far apart the promotions land;
 *      30 s stuck-promotion fallback) triggers EXACTLY ONE
 *      runDocumentUploadedEvent call with the batch's documentIds in upload
 *      order.
 *   2. typedCommand pass-through: a message sent alongside the upload batch
 *      is forwarded VERBATIM (server decides supersede — no client pre-filter).
 *   3. Each stream event type renders: event_classification (classification
 *      line), event_output (the STORED ledger payload as the summary message,
 *      ADR-040), event_confirmation (message), event_notice (subtle notice).
 *   4. Chips delivered through the onChips seam render as <ConsumerChips> and
 *      dispatch through the ONE dispatchConsumer on click.
 *   5. Error paths: server `error` event renders its safe message; an HTTP /
 *      network rejection renders the stable local failure line.
 *
 * Test-surface strategy mirrors ConversationPane.consumer-chips.test.tsx:
 * mock ONLY the heavy children (SprkChat prop-capture stub — here extended to
 * simulate the injectLocalMessage/onLocalMessageInjected acknowledgement
 * lifecycle — useAiSession, ThreePaneShell) plus the DocumentUploadedEventStream
 * seam (its SSE consumption is covered by DocumentUploadedEventStream.test.ts);
 * render the real ConversationPane.
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { ISprkChatProps, IChatMessage, AttachmentChip } from '@spaarke/ui-components';
import type { RunDocumentUploadedEventOptions } from '../DocumentUploadedEventStream';

// ---------------------------------------------------------------------------
// Mock SprkChat (prop capture + injection-lifecycle simulation),
// createConsumerDispatcher (dispatch spy), runDocumentUploadedEvent (run spy).
// ---------------------------------------------------------------------------

const sprkChatPropsRef: { current: ISprkChatProps | null } = { current: null };
/** Messages the (stubbed) SprkChat appended via injectLocalMessage, in order. */
const injectedMessages: IChatMessage[] = [];

const dispatchConsumerSpy = jest.fn(
  () => Promise.resolve({ streamId: 'stream-test', status: 'complete' as const })
);
const createConsumerDispatcherSpy = jest.fn((..._args: unknown[]) => dispatchConsumerSpy);

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  const ReactActual = jest.requireActual('react') as typeof import('react');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      sprkChatPropsRef.current = props;
      // Simulate the real SprkChat one-shot injection contract: on identity
      // change of injectLocalMessage, append + acknowledge — this drives the
      // host's injection queue drain (task 022b enqueueAssistantMessage).
      const lastInjectedRef = ReactActual.useRef<IChatMessage | null>(null);
      ReactActual.useEffect(() => {
        const message = props.injectLocalMessage ?? null;
        if (!message) {
          lastInjectedRef.current = null;
          return;
        }
        if (lastInjectedRef.current === message) return;
        lastInjectedRef.current = message;
        injectedMessages.push(message);
        props.onLocalMessageInjected?.();
      });
      return <div data-testid="sprkchat-stub" />;
    },
    createConsumerDispatcher: (...args: unknown[]) =>
      (createConsumerDispatcherSpy as (...a: unknown[]) => unknown)(...args),
  };
});

const runEventSpy = jest.fn<Promise<void>, [RunDocumentUploadedEventOptions]>();

jest.mock('../DocumentUploadedEventStream', () => {
  const actual = jest.requireActual('../DocumentUploadedEventStream');
  return {
    ...actual,
    runDocumentUploadedEvent: (...args: [RunDocumentUploadedEventOptions]) =>
      runEventSpy(...args),
  };
});

// ---------------------------------------------------------------------------
// Mock @spaarke/ai-widgets useAiSession — non-null session id so the
// auto-promote effect (which feeds the Event batch) runs.
// ---------------------------------------------------------------------------

const TEST_SESSION_ID = '00000000-0000-0000-0000-000000000001';
const authenticatedFetchMock = jest.fn();

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: (...args: unknown[]) =>
        (authenticatedFetchMock as (...a: unknown[]) => unknown)(...args),
      getAccessToken: jest.fn(async () => 'token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: TEST_SESSION_ID,
      setChatSessionId: jest.fn(),
      playbookId: undefined,
      setPlaybookId: jest.fn(),
      entityContext: null,
      contextMapping: null,
      isLoadingContextMapping: false,
      streaming: { onPaneEvent: null },
      streamingState: { isStreaming: false, tokenCount: 0 },
      turnCount: 0,
      isLoading: false,
    }),
  };
});

jest.mock('../../shell/ThreePaneShell', () => ({
  useShellStage: () => ({
    stage: 'active-chat' as const,
    toLoading: jest.fn(),
    toActiveChat: jest.fn(),
    toReview: jest.fn(),
    reset: jest.fn(),
  }),
  useRestoreContext: () => null,
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => null,
}));

// Import AFTER the mocks.
import { ConversationPane } from '../ConversationPane';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderPane(): { bus: PaneEventBus } {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ConversationPane />
      </PaneEventBusProvider>
    </FluentProvider>
  );
  return { bus };
}

function makeReadyChip(id: string, filename: string): AttachmentChip {
  return {
    id,
    filename,
    sizeBytes: 11,
    mimeType: 'text/plain',
    status: 'ready',
    textContent: 'hello world',
  };
}

/**
 * Simulate an upload batch: per-file onAttachmentReady (held File capture) +
 * a ready-chip onAttachmentsChanged tick, then flush the async promote IIFEs
 * (fetch → 202 json → Event-batch queue).
 */
async function uploadBatch(files: Array<{ id: string; filename: string }>): Promise<void> {
  const props = sprkChatPropsRef.current;
  expect(props).not.toBeNull();
  act(() => {
    for (const f of files) {
      props?.onAttachmentReady?.({
        filename: f.filename,
        contentType: 'text/plain',
        textContent: 'hello world',
        file: new File(['hello world'], f.filename, { type: 'text/plain' }),
      });
    }
  });
  await act(async () => {
    props?.onAttachmentsChanged?.(files.map((f) => makeReadyChip(f.id, f.filename)));
  });
  // Flush promote-effect microtasks. Promotions run SEQUENTIALLY since the
  // G-P1 Defect-2 fix (manifest write-race mitigation), so a multi-file batch
  // needs several rounds (each promoteOne awaits fetch → json → queue).
  await act(async () => {
    for (let i = 0; i < 12; i++) {
      await Promise.resolve();
    }
  });
}

/** Advance past the 250 ms ready-confirmation timer (the Event batch itself is
 *  count-complete — it fires on the last 202, not on a timer). */
async function fireBatchTimers(): Promise<void> {
  await act(async () => {
    jest.advanceTimersByTime(250);
  });
}

/** The captured options of the (single expected) Event run. */
function capturedRun(): RunDocumentUploadedEventOptions {
  expect(runEventSpy).toHaveBeenCalledTimes(1);
  return runEventSpy.mock.calls[0][0];
}

beforeEach(() => {
  jest.useFakeTimers();
  sprkChatPropsRef.current = null;
  injectedMessages.length = 0;
  dispatchConsumerSpy.mockClear();
  createConsumerDispatcherSpy.mockClear();
  runEventSpy.mockReset();
  // Default: capture options; keep the stream "open" (never settles) so
  // handler-driven assertions are isolated from resolution timing.
  runEventSpy.mockImplementation(() => new Promise<void>(() => undefined));
  authenticatedFetchMock.mockReset();
  let docCounter = 0;
  authenticatedFetchMock.mockImplementation(async (url: unknown) => {
    if (typeof url === 'string' && url.includes('/documents')) {
      docCounter += 1;
      // Bind the id at CALL time — json() runs later, after further calls.
      const documentId = `doc-${docCounter}`;
      return {
        ok: true,
        status: 202,
        json: async () => ({ documentId, filename: documentId, status: 'ready' }),
      };
    }
    return { ok: true, status: 200, json: async () => ({}) };
  });
});

afterEach(() => {
  jest.useRealTimers();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('Event path — attach gesture fires the document-uploaded event ONCE (FR-P1-03, count-complete)', () => {
  it('a multi-file batch triggers exactly one event run with documentIds in upload order and no typedCommand', async () => {
    renderPane();
    await uploadBatch([
      { id: 'chip-a', filename: 'a.txt' },
      { id: 'chip-b', filename: 'b.txt' },
    ]);

    // Both files promoted through /documents…
    const documentCalls = authenticatedFetchMock.mock.calls.filter(
      (c) => typeof c[0] === 'string' && (c[0] as string).includes('/documents')
    );
    expect(documentCalls).toHaveLength(2);

    // …and the batch fired the instant the LAST 202 settled (count-complete —
    // no coalescing timer), exactly once.
    const run = capturedRun();
    expect(run.sessionId).toBe(TEST_SESSION_ID);
    expect(run.bffBaseUrl).toBe('https://test-bff.example.com');
    expect(run.fileIds).toEqual(['doc-1', 'doc-2']);
    expect(run.typedCommand).toBeNull();
  });

  it('G-P1 Defect 2 repro: promotions landing SECONDS apart still produce ONE event per gesture', async () => {
    renderPane();
    const props = sprkChatPropsRef.current;

    const resolvers: Array<(r: unknown) => void> = [];
    authenticatedFetchMock.mockImplementation(
      () => new Promise((resolve) => resolvers.push(resolve))
    );

    act(() => {
      for (const f of ['a.pdf', 'b.pdf', 'c.pdf']) {
        props?.onAttachmentReady?.({
          filename: f, contentType: 'application/pdf', textContent: 'x',
          file: new File(['x'], f, { type: 'application/pdf' }),
        });
      }
    });
    await act(async () => {
      props?.onAttachmentsChanged?.([
        makeReadyChip('chip-a', 'a.pdf'),
        makeReadyChip('chip-b', 'b.pdf'),
        makeReadyChip('chip-c', 'c.pdf'),
      ]);
    });

    const respond = (documentId: string) => ({
      ok: true, status: 202, json: async () => ({ documentId }),
    });

    // Promotions run sequentially — resolve them 5 s apart (the real-world
    // timing that broke the old 250 ms debounce: it fired one POST per file).
    for (const [index, docId] of (['doc-a', 'doc-b', 'doc-c'] as const).entries()) {
      expect(runEventSpy).not.toHaveBeenCalled();
      await act(async () => {
        jest.advanceTimersByTime(5_000);
        resolvers[index](respond(docId));
        for (let i = 0; i < 8; i++) await Promise.resolve();
      });
    }

    // ONE event, the full gesture, upload order.
    expect(runEventSpy).toHaveBeenCalledTimes(1);
    expect(runEventSpy.mock.calls[0][0].fileIds).toEqual(['doc-a', 'doc-b', 'doc-c']);
  });

  it('a stuck promotion is bounded by the 30 s fallback — the batch fires with whatever settled', async () => {
    renderPane();
    const props = sprkChatPropsRef.current;

    const resolvers: Array<(r: unknown) => void> = [];
    authenticatedFetchMock.mockImplementation(
      () => new Promise((resolve) => resolvers.push(resolve))
    );

    act(() => {
      for (const f of ['a.pdf', 'b.pdf']) {
        props?.onAttachmentReady?.({
          filename: f, contentType: 'application/pdf', textContent: 'x',
          file: new File(['x'], f, { type: 'application/pdf' }),
        });
      }
    });
    await act(async () => {
      props?.onAttachmentsChanged?.([
        makeReadyChip('chip-a', 'a.pdf'),
        makeReadyChip('chip-b', 'b.pdf'),
      ]);
    });

    // Only chip-a's promotion ever lands; chip-b's hangs.
    await act(async () => {
      resolvers[0]({ ok: true, status: 202, json: async () => ({ documentId: 'doc-a' }) });
      for (let i = 0; i < 8; i++) await Promise.resolve();
    });
    expect(runEventSpy).not.toHaveBeenCalled();

    await act(async () => {
      jest.advanceTimersByTime(30_000);
    });

    expect(runEventSpy).toHaveBeenCalledTimes(1);
    expect(runEventSpy.mock.calls[0][0].fileIds).toEqual(['doc-a']);
  });

  it('orders fileIds by the chip strip (upload order) even when promotions complete out of order', async () => {
    renderPane();
    const props = sprkChatPropsRef.current;

    // Manual promise control. NOTE: promotions are dispatched sequentially
    // since the Defect-2 fix, so "out of order" is exercised at the queue
    // seam by scripting the promote responses' documentIds.
    const resolvers: Array<(r: unknown) => void> = [];
    authenticatedFetchMock.mockImplementation(
      () => new Promise((resolve) => resolvers.push(resolve))
    );

    act(() => {
      props?.onAttachmentReady?.({
        filename: 'a.txt', contentType: 'text/plain', textContent: 'x',
        file: new File(['x'], 'a.txt', { type: 'text/plain' }),
      });
      props?.onAttachmentReady?.({
        filename: 'b.txt', contentType: 'text/plain', textContent: 'x',
        file: new File(['x'], 'b.txt', { type: 'text/plain' }),
      });
    });
    await act(async () => {
      props?.onAttachmentsChanged?.([makeReadyChip('chip-a', 'a.txt'), makeReadyChip('chip-b', 'b.txt')]);
    });

    const respond = (documentId: string) => ({
      ok: true,
      status: 202,
      json: async () => ({ documentId }),
    });
    await act(async () => {
      resolvers[0](respond('doc-a'));
      for (let i = 0; i < 8; i++) await Promise.resolve();
      resolvers[1](respond('doc-b'));
      for (let i = 0; i < 8; i++) await Promise.resolve();
    });

    // Upload order wins: chip-a's document stays index 0.
    expect(capturedRun().fileIds).toEqual(['doc-a', 'doc-b']);
  });

  it('passes a command typed alongside the upload VERBATIM as typedCommand (server decides — ADR-039)', async () => {
    renderPane();
    const props = sprkChatPropsRef.current;

    // Hold the promotion open so the batch is still gathering when the user types.
    const resolvers: Array<(r: unknown) => void> = [];
    authenticatedFetchMock.mockImplementation(
      () => new Promise((resolve) => resolvers.push(resolve))
    );

    act(() => {
      props?.onAttachmentReady?.({
        filename: 'a.txt', contentType: 'text/plain', textContent: 'x',
        file: new File(['x'], 'a.txt', { type: 'text/plain' }),
      });
    });
    await act(async () => {
      props?.onAttachmentsChanged?.([makeReadyChip('chip-a', 'a.txt')]);
    });

    // The user sends a command while the batch is open (before its last 202).
    act(() => {
      sprkChatPropsRef.current?.onBeforeSendMessage?.('translate this to French');
    });

    await act(async () => {
      resolvers[0]({ ok: true, status: 202, json: async () => ({ documentId: 'doc-1' }) });
      for (let i = 0; i < 8; i++) await Promise.resolve();
    });

    expect(capturedRun().typedCommand).toBe('translate this to French');
  });
});

describe('Event path — stream events render in the conversation surface', () => {
  async function startedRun(): Promise<RunDocumentUploadedEventOptions> {
    renderPane();
    await uploadBatch([{ id: 'chip-a', filename: 'engagement-letter.pdf' }]);
    await fireBatchTimers();
    return capturedRun();
  }

  it('event_classification renders the classification line as an assistant message', async () => {
    const run = await startedRun();
    await act(async () => {
      run.handlers.onClassification?.({
        fileId: 'doc-1',
        fileName: 'engagement-letter.pdf',
        docType: 'Engagement Letter',
        confidence: 0.92,
        bindingId: 'b-classify',
        ledgerKey: 'lk-1',
      });
    });

    const contents = injectedMessages.map((m) => m.content);
    expect(contents).toContain(
      'Classified "engagement-letter.pdf" as **Engagement Letter** (92% confidence).'
    );
    expect(injectedMessages.every((m) => m.role === 'Assistant')).toBe(true);
  });

  it('event_output renders the STORED ledger payload as the summary message (ADR-040 render-follows-store)', async () => {
    const run = await startedRun();
    await act(async () => {
      run.handlers.onOutput?.({
        bindingId: 'b-summarize',
        ledgerKey: 'lk-2',
        disposition: 'informational',
        payload: { tldr: 'Two-line engagement letter.', summary: 'Full summary text.', keywords: ['engagement'] },
      });
    });

    const summaryMessage = injectedMessages.find((m) => m.content.includes('**TL;DR:**'));
    expect(summaryMessage).toBeDefined();
    expect(summaryMessage?.content).toContain('Two-line engagement letter.');
    expect(summaryMessage?.content).toContain('Full summary text.');
    expect(summaryMessage?.content).toContain('**Keywords:** engagement');
  });

  it('rapid successive events all render (injection queue — none clobbered)', async () => {
    const run = await startedRun();
    await act(async () => {
      run.handlers.onClassification?.({ fileName: 'a.txt', docType: 'Letter', confidence: 0.9 });
      run.handlers.onOutput?.({ payload: { tldr: 'T.', summary: 'S.' } });
      run.handlers.onNotice?.({ reason: 'daily-cap', message: 'Daily limit reached.' });
    });

    const contents = injectedMessages.map((m) => m.content);
    expect(contents).toContainEqual(expect.stringContaining('Classified "a.txt"'));
    expect(contents).toContainEqual(expect.stringContaining('**TL;DR:** T.'));
    expect(contents).toContain('_Daily limit reached._');
  });

  it('event_confirmation renders its message; its chips arrive via onChips and render as ConsumerChips', async () => {
    const run = await startedRun();
    const chips = [
      {
        targetBindingId: 'b-confirm-resume',
        label: 'Yes — summarize it',
        args: { fileIds: ['doc-1'], confirmedDocType: 'Engagement Letter' },
      },
    ];
    await act(async () => {
      run.handlers.onConfirmation?.({
        fileId: 'doc-1',
        docType: 'Engagement Letter',
        confidence: 0.4,
        threshold: 0.85,
        message: 'I think this is an Engagement Letter, but I am not sure. Summarize it as one?',
        chips,
      });
      // The module forwards carrier chips through the single onChips seam
      // (covered in DocumentUploadedEventStream.test.ts) — replay here.
      run.handlers.onChips?.(chips);
    });

    expect(injectedMessages.map((m) => m.content)).toContain(
      'I think this is an Engagement Letter, but I am not sure. Summarize it as one?'
    );
    const chip = screen.getByTestId('consumer-chip-b-confirm-resume');
    expect(chip).toHaveTextContent('Yes — summarize it');
  });

  it('event_notice renders the subtle notice; notice chips render when present', async () => {
    const run = await startedRun();
    const chips = [{ targetBindingId: 'b-manual-run', label: 'Run it now', args: { fileIds: ['doc-1'] } }];
    await act(async () => {
      run.handlers.onNotice?.({ reason: 'opted-out', message: 'Automatic document processing is turned off.', chips });
      run.handlers.onChips?.(chips);
    });

    expect(injectedMessages.map((m) => m.content)).toContain(
      '_Automatic document processing is turned off._'
    );
    expect(screen.getByTestId('consumer-chip-b-manual-run')).toBeInTheDocument();
  });

  it('chips delivered by the event stream dispatch through the ONE dispatchConsumer on click (EventChip args → slots)', async () => {
    const run = await startedRun();
    const chips = [
      { targetBindingId: 'b-summarize-all', label: 'Summarize all 2 files?', args: { fileIds: ['doc-1', 'doc-2'] } },
    ];
    await act(async () => {
      run.handlers.onChips?.(chips);
    });

    await act(async () => {
      fireEvent.click(screen.getByTestId('consumer-chip-b-summarize-all'));
    });

    expect(dispatchConsumerSpy).toHaveBeenCalledTimes(1);
    const [bindingId, args] = dispatchConsumerSpy.mock.calls[0] as unknown as [
      string,
      { slots?: Record<string, unknown> }
    ];
    expect(bindingId).toBe('b-summarize-all');
    expect(args.slots).toEqual({ fileIds: ['doc-1', 'doc-2'] });
  });
});

describe('Event path — chip persistence (G-P1 Defect 1)', () => {
  async function startedRun(): Promise<RunDocumentUploadedEventOptions> {
    renderPane();
    await uploadBatch([{ id: 'chip-a', filename: 'a.pdf' }]);
    return capturedRun();
  }

  it('a later chip-less event (e.g. a notice) does NOT clear previously-rendered chips', async () => {
    const run = await startedRun();
    await act(async () => {
      run.handlers.onChips?.([{ targetBindingId: 'b-sum', label: 'Summarize' }]);
    });
    expect(screen.getByTestId('consumer-chip-b-sum')).toBeInTheDocument();

    await act(async () => {
      run.handlers.onNotice?.({ reason: 'no-attachments', message: 'Nothing ran.' });
      run.handlers.onDone?.();
    });

    expect(screen.getByTestId('consumer-chip-b-sum')).toBeInTheDocument();
  });

  it('an empty or malformed chips payload never blanks the strip (tolerant replace-only-when-non-empty)', async () => {
    const run = await startedRun();
    await act(async () => {
      run.handlers.onChips?.([{ targetBindingId: 'b-sum', label: 'Summarize' }]);
    });

    await act(async () => {
      run.handlers.onChips?.([]);
      run.handlers.onChips?.([{ bogus: true }]);
    });

    expect(screen.getByTestId('consumer-chip-b-sum')).toBeInTheDocument();
  });

  it('a NEW non-empty chip set replaces the previous one (chips track the latest output)', async () => {
    const run = await startedRun();
    await act(async () => {
      run.handlers.onChips?.([{ targetBindingId: 'b-old', label: 'Old step' }]);
    });
    await act(async () => {
      run.handlers.onChips?.([{ targetBindingId: 'b-new', label: 'New step' }]);
    });

    expect(screen.queryByTestId('consumer-chip-b-old')).not.toBeInTheDocument();
    expect(screen.getByTestId('consumer-chip-b-new')).toBeInTheDocument();
  });

  it('a chip click renders the dispatched STORED output as an assistant message and re-arms the strip from the dispatch chips', async () => {
    dispatchConsumerSpy.mockResolvedValueOnce({
      streamId: 'stream-test',
      status: 'complete' as const,
      result: { tldr: 'Dispatched TL;DR.', summary: 'Dispatched summary.', keywords: ['dispatch'] },
      chips: [{ bindingId: 'b-again', label: 'Summarize again' }],
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any);

    const run = await startedRun();
    await act(async () => {
      run.handlers.onChips?.([
        { targetBindingId: 'b-sum', label: 'Summarize', requiresAttachments: true },
      ]);
    });

    await act(async () => {
      fireEvent.click(screen.getByTestId('consumer-chip-b-sum'));
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    // The dispatched output rendered in the CONVERSATION (ADR-040 render-follows-store)…
    const contents = injectedMessages.map((m) => m.content);
    expect(contents).toContainEqual(expect.stringContaining('**TL;DR:** Dispatched TL;DR.'));
    expect(contents).toContainEqual(expect.stringContaining('Dispatched summary.'));
    // …and the NEXT chip set arrived from the dispatch stream (never a dead strip).
    expect(screen.getByTestId('consumer-chip-b-again')).toHaveTextContent('Summarize again');
  });
});

describe('Event path — terminal error handling', () => {
  it("a server `error` event renders its safe message as an assistant line", async () => {
    renderPane();
    await uploadBatch([{ id: 'chip-a', filename: 'a.txt' }]);
    await fireBatchTimers();
    const run = capturedRun();

    await act(async () => {
      run.handlers.onError?.('Something went wrong processing your files.');
    });

    expect(injectedMessages.map((m) => m.content)).toContain(
      'Something went wrong processing your files.'
    );
  });

  it('an HTTP/network rejection renders the stable local failure line (ADR-019 — no raw detail)', async () => {
    runEventSpy.mockImplementation(() =>
      Promise.reject(new Error('documentUploadedEvent: request failed (status=503, errorCode=ai.event-rules.disabled)'))
    );

    renderPane();
    await uploadBatch([{ id: 'chip-a', filename: 'a.txt' }]);
    await fireBatchTimers();
    await act(async () => {
      await Promise.resolve();
    });

    const contents = injectedMessages.map((m) => m.content);
    expect(contents).toContain(
      "Sorry — I couldn't process those files automatically. You can still ask me about them."
    );
    // The raw errorCode detail never reaches the conversation surface.
    expect(contents.join('\n')).not.toContain('errorCode');
  });
});
