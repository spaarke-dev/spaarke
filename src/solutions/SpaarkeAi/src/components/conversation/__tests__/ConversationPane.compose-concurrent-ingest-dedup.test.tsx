/**
 * ConversationPane.compose-concurrent-ingest-dedup.test.tsx — spaarkeai-compose-r2 quality pass.
 *
 * Regression guard for the CONCURRENT same-file ingest dedup gap. `registerComposeActiveDocument`
 * runs on load AND tab_change AND visibility toggles, so two registrations for the SAME file can
 * fire in the SAME tick before either `/documents` upload resolves. The dedup cache
 * (`activeDocUploadCacheRef`) is written only AFTER the upload completes, so both truly-concurrent
 * registrations used to miss the cache → two `POST /documents` → two DISTINCT sessionFileIds → the
 * once-per-file ceremony gate (keyed by sessionFileId) passed for EACH → duplicate "I have your
 * file" + duplicate classify.
 *
 * The fix adds an in-flight promise map (keyed by the same `cacheKey`) so concurrent same-file
 * registrations collapse into ONE upload and resolve to the SAME sessionFileId — one upload, one
 * ceremony. This suite mints a DISTINCT documentId per `/documents` POST (faithful to a real server)
 * so a regression to double-upload is observable via BOTH the upload count AND the ceremony count.
 *
 * Surface strategy mirrors ConversationPane.compose-browse-ingest-ceremony.test.tsx.
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
const BFF = 'https://test-bff.example.com';

const injectedMessages: IChatMessage[] = [];

// Mint a DISTINCT documentId per /documents POST — faithful to the real server, so a regression to
// two uploads would produce two distinct sessionFileIds and thus a DOUBLE ceremony (observable).
let uploadSeq = 0;

const authenticatedFetchMock = jest.fn(async (url: string) => {
  if (url.includes('/documents')) {
    const id = `session-file-${++uploadSeq}`;
    return { ok: true, status: 200, json: async () => ({ documentId: id }) } as unknown as Response;
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
  uploadSeq = 0;
  runEventSpy.mockReset();
  runEventSpy.mockImplementation(() => new Promise<void>(() => undefined));
  bridgeRef.current = null;
});

describe('compose concurrent ingest dedup — two same-tick registers collapse into ONE upload/ceremony', () => {
  it('fires exactly one POST /documents and one ceremony for two concurrent same-file registrations', async () => {
    renderPane();

    const register = () =>
      bridgeRef.current!.registerActiveDocument({
        docxBytes: new Uint8Array([1, 2, 3, 4]).buffer,
        fileName: 'concurrent-brief.docx',
        documentSessionId: DOC_SESSION,
      });

    // Fire BOTH registrations in the SAME tick WITHOUT awaiting between them —
    // both reach the (empty) dedup cache before either /documents upload resolves.
    await act(async () => {
      const p1 = register();
      const p2 = register();
      await Promise.all([p1, p2]);
      for (let i = 0; i < 12; i++) await Promise.resolve();
    });
    await act(async () => {
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    // Exactly ONE upload (the in-flight guard collapsed the concurrent pair).
    expect(uploadCalls()).toHaveLength(1);

    // Exactly ONE "I have your file" ceremony message.
    expect(
      contents().filter((c) => c === 'I have your file: concurrent-brief.docx')
    ).toHaveLength(1);

    // Exactly ONE classify Event-path fire, against the active session + the single file id.
    expect(runEventSpy).toHaveBeenCalledTimes(1);
    const run = runEventSpy.mock.calls[0][0];
    expect(run.sessionId).toBe(SESSION_ID);
    expect(run.fileIds).toEqual(['session-file-1']);
  });
});
