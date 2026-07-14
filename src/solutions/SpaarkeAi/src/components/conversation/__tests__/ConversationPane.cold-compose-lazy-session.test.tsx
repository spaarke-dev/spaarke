/**
 * ConversationPane.cold-compose-lazy-session.test.tsx — spaarkeai-compose-r2 cold Workspaces-menu
 * Compose bug.
 *
 * A file opened via Browse in a Compose tab that was opened COLD from the Workspaces menu (no prior
 * Assistant interaction) has NO chat session. `registerComposeActiveDocument` used to early-return in
 * that case (`if (!sessionId) return`), so the file's bytes never became a `ChatSessionFile` and the
 * Assistant could not summarize / answer about it.
 *
 * The fix LAZILY creates a chat session (POST /api/ai/chat/sessions) before the ChatSessionFile upload
 * + the /api/compose/active-document POST, and makes that created session the pane's ACTIVE session
 * (setChatSessionId). This suite drives the REAL ConversationPane over a real PaneEventBus +
 * ComposeActionBridge; the network is mocked at the wire boundary.
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

const NEW_SESSION = '00000000-0000-0000-0000-00000000cccc';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
const SESSION_FILE_ID = 'session-file-cold-source-123';
const BFF = 'https://test-bff.example.com';

// Records for assertions.
const activeDocPostBodies: Array<Record<string, unknown>> = [];
const setChatSessionIdMock = jest.fn();

// Exact create-session endpoint (no trailing id/segment) — distinguish from `.../{id}/documents`.
const isCreateSessionCall = (url: string, init?: RequestInit): boolean =>
  (init?.method ?? 'GET') === 'POST' && url.endsWith('/api/ai/chat/sessions');

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit) => {
  if (url.includes('/documents')) {
    // The chat upload endpoint mints the session file id (the sessionFileId).
    return { ok: true, status: 200, json: async () => ({ documentId: SESSION_FILE_ID }) } as unknown as Response;
  }
  if (url.includes('/compose/active-document')) {
    activeDocPostBodies.push(JSON.parse(String(init?.body ?? '{}')));
    return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
  }
  if (isCreateSessionCall(url, init)) {
    // The lazy session-create endpoint mints a fresh chat session id.
    return { ok: true, status: 200, json: async () => ({ sessionId: NEW_SESSION }) } as unknown as Response;
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
      // COLD Workspaces-menu Compose tab: no chat session yet.
      chatSessionId: null,
      setChatSessionId: setChatSessionIdMock,
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

const createSessionCalls = () =>
  authenticatedFetchMock.mock.calls.filter(([u, i]) => isCreateSessionCall(String(u), i as RequestInit));
const uploadCalls = () => authenticatedFetchMock.mock.calls.filter(([u]) => String(u).includes('/documents'));

beforeEach(() => {
  workspaceEvents.length = 0;
  activeDocPostBodies.length = 0;
  authenticatedFetchMock.mockClear();
  setChatSessionIdMock.mockClear();
  bridgeRef.current = null;
});

describe('cold Workspaces-menu Compose: registering an active document with NO session lazily creates one', () => {
  it('creates ONE session, then uploads the file bytes, then POSTs active-document with the created session', async () => {
    renderPane();

    await act(async () => {
      await bridgeRef.current!.registerActiveDocument({
        docxBytes: new Uint8Array([1, 2, 3, 4]).buffer,
        fileName: 'cold-brief.docx',
        documentSessionId: DOC_SESSION,
      });
      for (let i = 0; i < 8; i++) await Promise.resolve();
    });

    // Exactly one session was minted (POST /api/ai/chat/sessions).
    expect(createSessionCalls()).toHaveLength(1);
    // The created session becomes the pane's ACTIVE session — pushed into AiSessionProvider.
    expect(setChatSessionIdMock).toHaveBeenCalledWith(NEW_SESSION);

    // The file bytes uploaded as a ChatSessionFile against the created session.
    expect(uploadCalls()).toHaveLength(1);
    expect(String(uploadCalls()[0][0])).toContain(`/api/ai/chat/sessions/${NEW_SESSION}/documents`);

    // The active-document POST fired against the created session with the uploaded sessionFileId.
    expect(activeDocPostBodies).toHaveLength(1);
    expect(activeDocPostBodies[0].sessionId).toBe(NEW_SESSION);
    expect(activeDocPostBodies[0].sessionFileId).toBe(SESSION_FILE_ID);
    expect(activeDocPostBodies[0].documentSessionId).toBe(DOC_SESSION);
  });

  it('two near-simultaneous registrations create only ONE session (race guard)', async () => {
    renderPane();

    const bytes = new Uint8Array([5, 6, 7, 8]).buffer;
    await act(async () => {
      // Fire both without awaiting between them — the in-flight guard must collapse them to one create.
      const a = bridgeRef.current!.registerActiveDocument({ docxBytes: bytes, fileName: 'doc.docx', documentSessionId: DOC_SESSION });
      const b = bridgeRef.current!.registerActiveDocument({ docxBytes: bytes, fileName: 'doc.docx', documentSessionId: DOC_SESSION });
      await Promise.all([a, b]);
      for (let i = 0; i < 8; i++) await Promise.resolve();
    });

    // Only one session minted despite two registrations (the in-flight session-create guard).
    expect(createSessionCalls()).toHaveLength(1);
    // Both registrations resolve to the SAME created session (never a second/throwaway session).
    expect(setChatSessionIdMock).toHaveBeenCalledWith(NEW_SESSION);
    expect(activeDocPostBodies.length).toBeGreaterThanOrEqual(1);
    activeDocPostBodies.forEach((body) => expect(body.sessionId).toBe(NEW_SESSION));
  });

  it('a WITHDRAW (visible:false) with no session does NOT create one', async () => {
    renderPane();

    await act(async () => {
      await bridgeRef.current!.registerActiveDocument({
        docxBytes: new Uint8Array([9, 9, 9, 9]).buffer,
        fileName: 'doc.docx',
        documentSessionId: DOC_SESSION,
        visible: false,
      });
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    expect(createSessionCalls()).toHaveLength(0);
    expect(uploadCalls()).toHaveLength(0);
    expect(activeDocPostBodies).toHaveLength(0);
  });
});
