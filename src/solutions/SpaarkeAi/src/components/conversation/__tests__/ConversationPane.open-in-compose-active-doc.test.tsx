/**
 * ConversationPane.open-in-compose-active-doc.test.tsx — Wave 3 Part 2 (DEF-11 active-document registration).
 *
 * NOTE (spaarkeai-compose-r2 FIX #10a): the generic per-message "Open in Compose" affordance was
 * REMOVED. The former Part 1 tests (which drove `onOpenInCompose`) are gone with the feature. What
 * remains here is the STILL-VALID Part 2 coverage: registering the active document POSTs the tab's
 * `documentSessionId` in the `/api/compose/active-document` request body (the field the server
 * persists so BindingCapabilityTool routes a typed revise/draft into the document session), and the
 * upload dedups across re-registrations.
 *
 * Drives the REAL ConversationPane over a real PaneEventBus + ComposeActionBridge; the network is
 * mocked at the wire boundary.
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
import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';
import type { ISprkChatProps } from '@spaarke/ui-components';
import {
  ComposeActionBridgeProvider,
  useComposeActionBridge,
  type ComposeActionBridgeValue,
} from '@spaarke/compose-components/context/composeActionBridge';

const CHAT_SESSION = '00000000-0000-0000-0000-00000000c8a7';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
const SESSION_FILE_ID = 'session-file-active-source-123';
const BFF = 'https://test-bff.example.com';

// Records for assertions.
const activeDocPostBodies: Array<Record<string, unknown>> = [];

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit) => {
  if (url.includes('/documents')) {
    // The chat upload endpoint mints the session file id (the sessionFileId).
    return { ok: true, status: 200, json: async () => ({ documentId: SESSION_FILE_ID }) } as unknown as Response;
  }
  if (url.includes('/compose/active-document')) {
    activeDocPostBodies.push(JSON.parse(String(init?.body ?? '{}')));
    return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => <div data-testid="sprkchat-stub">{props.transcriptFooterSlot}</div>,
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
      chatSessionId: CHAT_SESSION,
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

const workspaceEvents: WorkspacePaneEvent[] = [];
let bus: PaneEventBus;

function renderPane() {
  bus = new PaneEventBus();
  bus.subscribe('workspace', (e) => workspaceEvents.push(e as WorkspacePaneEvent));
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

beforeEach(() => {
  workspaceEvents.length = 0;
  activeDocPostBodies.length = 0;
  authenticatedFetchMock.mockClear();
  bridgeRef.current = null;
});

describe('Wave 3 Part 2: registering the active document POSTs the tab document session id (DEF-11)', () => {
  it('threads documentSessionId into the /api/compose/active-document request body', async () => {
    renderPane();

    await act(async () => {
      await bridgeRef.current!.registerActiveDocument({
        docxBytes: new Uint8Array([9, 9, 9, 9]).buffer,
        fileName: 'brief.docx',
        documentSessionId: DOC_SESSION,
      });
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    expect(activeDocPostBodies).toHaveLength(1);
    expect(activeDocPostBodies[0].sessionId).toBe(CHAT_SESSION);
    expect(activeDocPostBodies[0].sessionFileId).toBe(SESSION_FILE_ID);
    expect(activeDocPostBodies[0].documentSessionId).toBe(DOC_SESSION);
  });

  it('dedups the upload across re-registrations (tab_change) — one upload, pointer re-asserted', async () => {
    renderPane();

    const bytes = new Uint8Array([4, 5, 6, 7]).buffer;
    await act(async () => {
      await bridgeRef.current!.registerActiveDocument({ docxBytes: bytes, fileName: 'doc.docx', documentSessionId: DOC_SESSION });
      await bridgeRef.current!.registerActiveDocument({ docxBytes: bytes, fileName: 'doc.docx', documentSessionId: DOC_SESSION });
      for (let i = 0; i < 8; i++) await Promise.resolve();
    });

    const uploadCalls = authenticatedFetchMock.mock.calls.filter(([u]) => String(u).includes('/documents'));
    const activeDocCalls = authenticatedFetchMock.mock.calls.filter(([u]) => String(u).includes('/compose/active-document'));
    expect(uploadCalls).toHaveLength(1); // deduped — no duplicate ChatSessionFile
    expect(activeDocCalls).toHaveLength(2); // pointer re-asserted each time (most-recent-active-wins)
  });
});
