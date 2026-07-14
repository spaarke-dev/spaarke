/**
 * ConversationPane.compose-browse-ingest-ceremony.test.tsx — spaarkeai-compose-r2.
 *
 * A file opened MANUALLY via Browse in a Compose tab is context-uploaded through
 * `registerComposeActiveDocument` (POST /documents → POST /api/compose/active-document).
 * Historically that path was SILENT — no Assistant-visible acknowledgement. This suite pins the
 * new "ingest ceremony" that brings the Browse-opened file to parity with an Assistant-uploaded
 * file:
 *   1. an "I have your file: X" loaded message (the same `buildFileConfirmationMessage` the chip
 *      strip emits on a chip reaching 'ready'), and
 *   2. a "Classified 'X' as <type> (N% confidence)" message via the Event path — the file already
 *      lives in session.UploadedFiles (the /documents call added it), so the server's classify
 *      rule resolves it and streams `event_classification`.
 *
 * Idempotency is the load-bearing invariant: `registerComposeActiveDocument` runs on load AND on
 * tab_change AND on visibility toggles, so the ceremony MUST run ONCE per newly-loaded file.
 *
 * Surface strategy mirrors ConversationPane.event-path.test.tsx (SprkChat prop-capture stub with
 * the injectLocalMessage/onLocalMessageInjected drain lifecycle + a runDocumentUploadedEvent spy)
 * and ConversationPane.cold-compose-lazy-session.test.tsx (drive the REAL ConversationPane over a
 * real PaneEventBus + ComposeActionBridge; network mocked at the wire boundary).
 */
import '@testing-library/jest-dom';
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextEncoder === 'undefined') (global as any).TextEncoder = NodeTextEncoder;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextDecoder === 'undefined') (global as any).TextDecoder = NodeTextDecoder;
import React, { act } from 'react';
import { render } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { ISprkChatProps, IChatMessage } from '@spaarke/ui-components';
import type { RunDocumentUploadedEventOptions } from '../DocumentUploadedEventStream';
import {
  ComposeActionBridgeProvider,
  useComposeActionBridge,
  type ComposeActionBridgeValue,
} from '@spaarke/compose-components/context/composeActionBridge';

const SESSION_ID = '00000000-0000-0000-0000-0000000000ab';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
const SESSION_FILE_ID = 'session-file-browse-source-123';
const BFF = 'https://test-bff.example.com';

/** Messages the (stubbed) SprkChat appended via injectLocalMessage, in order. */
const injectedMessages: IChatMessage[] = [];

const authenticatedFetchMock = jest.fn(async (url: string) => {
  if (url.includes('/documents')) {
    return { ok: true, status: 200, json: async () => ({ documentId: SESSION_FILE_ID }) } as unknown as Response;
  }
  if (url.includes('/compose/active-document')) {
    return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  const ReactActual = jest.requireActual('react') as typeof import('react');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      // Simulate the real SprkChat one-shot injection contract: on identity change of
      // injectLocalMessage, append + acknowledge — this drives the host injection-queue drain.
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
      return <div data-testid="sprkchat-stub">{props.transcriptFooterSlot}</div>;
    },
  };
});

const runEventSpy = jest.fn<Promise<void>, [RunDocumentUploadedEventOptions]>();

jest.mock('../DocumentUploadedEventStream', () => {
  const actual = jest.requireActual('../DocumentUploadedEventStream');
  return {
    ...actual,
    runDocumentUploadedEvent: (...args: [RunDocumentUploadedEventOptions]) => runEventSpy(...args),
  };
});

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn(async () => 'token'),
      bffBaseUrl: BFF,
      tenantId: 'test-tenant',
      chatSessionId: SESSION_ID,
      setChatSessionId: jest.fn(),
      clearChatSession: jest.fn(),
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

// Import AFTER mocks.
import { ConversationPane } from '../ConversationPane';

const bridgeRef: { current: ComposeActionBridgeValue | null } = { current: null };
function BridgeCapture(): null {
  bridgeRef.current = useComposeActionBridge();
  return null;
}

let bus: PaneEventBus;
function renderPane() {
  bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ComposeActionBridgeProvider>
          <ConversationPane />
          <BridgeCapture />
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

const contents = () => injectedMessages.map((m) => m.content);
const uploadCalls = () => authenticatedFetchMock.mock.calls.filter(([u]) => String(u).includes('/documents'));

beforeEach(() => {
  injectedMessages.length = 0;
  authenticatedFetchMock.mockClear();
  runEventSpy.mockReset();
  // Keep the stream "open" (never settles) so handler-driven assertions are timing-isolated.
  runEventSpy.mockImplementation(() => new Promise<void>(() => undefined));
  bridgeRef.current = null;
});

describe('manual Browse ingest ceremony — first register brings the file to Assistant parity', () => {
  it('emits "I have your file" AND fires the classify Event path for the promoted file', async () => {
    renderPane();

    await act(async () => {
      await bridgeRef.current!.registerActiveDocument({
        docxBytes: new Uint8Array([1, 2, 3, 4]).buffer,
        fileName: 'browse-brief.docx',
        documentSessionId: DOC_SESSION,
      });
      for (let i = 0; i < 10; i++) await Promise.resolve();
    });
    // Extra flush: the ceremony's injection.enqueue runs inside the async
    // (imperative) register call — drain the SprkChat injection lifecycle.
    await act(async () => {
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    // The bytes were uploaded as a ChatSessionFile (context-only upload).
    expect(uploadCalls()).toHaveLength(1);

    // (1) The "I have your file" loaded message was injected.
    expect(contents()).toContain('I have your file: browse-brief.docx');

    // (2) The Event path fired ONCE for the promoted session file, against the active session.
    expect(runEventSpy).toHaveBeenCalledTimes(1);
    const run = runEventSpy.mock.calls[0][0];
    expect(run.sessionId).toBe(SESSION_ID);
    expect(run.fileIds).toEqual([SESSION_FILE_ID]);
    expect(run.typedCommand).toBeNull();

    // The server's event_classification frame renders the "Classified …" message.
    await act(async () => {
      run.handlers.onClassification?.({
        fileId: SESSION_FILE_ID,
        fileName: 'browse-brief.docx',
        docType: 'Legal Brief',
        confidence: 0.91,
        bindingId: 'b-classify',
        ledgerKey: 'lk-1',
      });
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    expect(contents()).toContain('Classified "browse-brief.docx" as **Legal Brief** (91% confidence).');
  });
});

describe('manual Browse ingest ceremony — idempotency (once per newly-loaded file)', () => {
  it('a SECOND register of the same file (tab switch / re-register) does NOT re-emit or re-classify', async () => {
    renderPane();

    const register = () =>
      bridgeRef.current!.registerActiveDocument({
        docxBytes: new Uint8Array([1, 2, 3, 4]).buffer,
        fileName: 'browse-brief.docx',
        documentSessionId: DOC_SESSION,
      });

    await act(async () => {
      await register();
      for (let i = 0; i < 10; i++) await Promise.resolve();
    });
    await act(async () => {
      await register();
      for (let i = 0; i < 10; i++) await Promise.resolve();
    });

    // The upload deduped (one ChatSessionFile), and so did the ceremony.
    expect(uploadCalls()).toHaveLength(1);
    expect(contents().filter((c) => c === 'I have your file: browse-brief.docx')).toHaveLength(1);
    expect(runEventSpy).toHaveBeenCalledTimes(1);
  });

  it('a WITHDRAW (visible:false) never runs the ceremony', async () => {
    renderPane();

    await act(async () => {
      await bridgeRef.current!.registerActiveDocument({
        docxBytes: new Uint8Array([9, 9, 9, 9]).buffer,
        fileName: 'withdrawn.docx',
        documentSessionId: DOC_SESSION,
        visible: false,
      });
      for (let i = 0; i < 8; i++) await Promise.resolve();
    });

    expect(contents()).not.toContain('I have your file: withdrawn.docx');
    expect(runEventSpy).not.toHaveBeenCalled();
  });
});
