/**
 * ConversationPane.open-in-compose-active-doc.test.tsx — Wave 3 Parts 1 + 2 (UAT-R3 Test #1 + DEF-11).
 *
 * Part 1 (Test #1): "Open in Compose" must open the ACTIVE SOURCE DOCUMENT, not the chat message text.
 *   (a) With an active source document registered, `onOpenInCompose(content)` dispatches a
 *       `compose.upload` seed ({layoutName:"Compose", compose:{ upload:{ sessionId, sessionFileId } }})
 *       — the same shape SendWorkspaceArtifactHandler produces — NOT a `compose.draft.html` of the prose.
 *   (b) With NO active source document (a genuine AI-drafted / manual-fallback message), it still seeds
 *       the message text as an editable draft (`compose.draft.html`).
 *
 * Part 2 (DEF-11 TEXT-path close): registering the active document POSTs the tab's `documentSessionId`
 *   in the `/api/compose/active-document` request body (the field the server persists so
 *   BindingCapabilityTool routes a typed revise/draft into the document session).
 *
 * Drives the REAL ConversationPane over a real PaneEventBus + ComposeActionBridge; the network is
 * mocked at the wire boundary. Mirrors the ConversationPane.compose-edit-controls forcing-test harness.
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

// SprkChat stub — captures the onOpenInCompose prop the affordance invokes + the attachment lifecycle
// props so a chat upload can be driven end-to-end (onAttachmentReady → onAttachmentsChanged → promote).
const captured: {
  onOpenInCompose?: (content: string) => void;
  onAttachmentReady?: (a: unknown) => void;
  onAttachmentsChanged?: (chips: unknown[]) => void;
} = {};
jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      const p = props as ISprkChatProps & {
        onOpenInCompose?: (content: string) => void;
        onAttachmentReady?: (a: unknown) => void;
        onAttachmentsChanged?: (chips: unknown[]) => void;
      };
      captured.onOpenInCompose = p.onOpenInCompose;
      captured.onAttachmentReady = p.onAttachmentReady;
      captured.onAttachmentsChanged = p.onAttachmentsChanged;
      return <div data-testid="sprkchat-stub">{props.transcriptFooterSlot}</div>;
    },
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

function lastComposeOpen(): WorkspacePaneEvent | undefined {
  return [...workspaceEvents]
    .reverse()
    .find(
      (e) =>
        e.type === 'widget_load' &&
        (e.widgetData as { layoutName?: string } | null)?.layoutName === 'Compose'
    );
}

beforeEach(() => {
  workspaceEvents.length = 0;
  activeDocPostBodies.length = 0;
  authenticatedFetchMock.mockClear();
  captured.onOpenInCompose = undefined;
  captured.onAttachmentReady = undefined;
  captured.onAttachmentsChanged = undefined;
  bridgeRef.current = null;
});

const DOCX_MIME = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

/** Drives the chat attachment lifecycle so the auto-promote effect POSTs /documents (→ sessionFileId). */
async function driveChatUpload(fileName: string): Promise<void> {
  const bytes = new Uint8Array([10, 20, 30]);
  await act(async () => {
    captured.onAttachmentReady!({
      id: 'chip-1',
      filename: fileName,
      file: new File([bytes], fileName, { type: DOCX_MIME }),
      contentType: DOCX_MIME,
      textContent: '',
    });
    captured.onAttachmentsChanged!([{ id: 'chip-1', filename: fileName, status: 'ready' }]);
    for (let i = 0; i < 10; i++) await Promise.resolve();
  });
}

describe('Wave 3 Part 1: "Open in Compose" opens the active source document, not the message prose', () => {
  it('with NO active source document, seeds the message text as an editable draft (compose.draft.html)', async () => {
    renderPane();
    expect(captured.onOpenInCompose).toBeDefined();

    await act(async () => {
      captured.onOpenInCompose!('You could revise the indemnity clause as follows...');
      await Promise.resolve();
    });

    const open = lastComposeOpen();
    expect(open).toBeDefined();
    const compose = (open!.widgetData as { compose?: Record<string, unknown> }).compose ?? {};
    expect(compose.draft).toBeDefined();
    expect(compose.upload).toBeUndefined();
  });

  it('with an active source document registered, opens THAT document via a compose.upload seed', async () => {
    renderPane();
    // Register a browse/host-direct active document through the bridge (the ComposeWorkspace hand-off).
    await act(async () => {
      await bridgeRef.current!.registerActiveDocument({
        docxBytes: new Uint8Array([1, 2, 3]).buffer,
        fileName: 'NDA.docx',
        documentSessionId: DOC_SESSION,
      });
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    workspaceEvents.length = 0; // ignore any dispatch noise from registration

    await act(async () => {
      captured.onOpenInCompose!('Do you want to revise the whole document or a section?');
      await Promise.resolve();
    });

    const open = lastComposeOpen();
    expect(open).toBeDefined();
    const compose = (open!.widgetData as { compose?: { upload?: Record<string, unknown>; draft?: unknown } }).compose ?? {};
    // Part 1: opens the SOURCE document (compose.upload), NOT the disambiguation prose (compose.draft).
    expect(compose.draft).toBeUndefined();
    expect(compose.upload).toBeDefined();
    expect(compose.upload!.sessionFileId).toBe(SESSION_FILE_ID);
    expect(compose.upload!.sessionId).toBe(CHAT_SESSION);
  });

  it('after a CHAT upload never mounted in Compose (Test #1 real repro), opens THAT uploaded file', async () => {
    renderPane();
    expect(captured.onAttachmentReady).toBeDefined();

    // The file is uploaded to the ASSISTANT (chat) only — no Compose mount, no bridge registration.
    await driveChatUpload('revise-me.docx');

    // The chat upload promoted to a ChatSessionFile (the auto-promote /documents POST).
    expect(authenticatedFetchMock.mock.calls.some(([u]) => String(u).includes('/documents'))).toBe(true);

    workspaceEvents.length = 0;

    // The disambiguation message's "Open in Compose" must open the uploaded FILE, not the prose.
    await act(async () => {
      captured.onOpenInCompose!('Do you want to revise the whole document or a section?');
      await Promise.resolve();
    });

    const open = lastComposeOpen();
    expect(open).toBeDefined();
    const compose = (open!.widgetData as { compose?: { upload?: Record<string, unknown>; draft?: unknown } }).compose ?? {};
    expect(compose.draft).toBeUndefined();
    expect(compose.upload).toBeDefined();
    expect(compose.upload!.sessionFileId).toBe(SESSION_FILE_ID);
    expect(compose.upload!.sessionId).toBe(CHAT_SESSION);
  });
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
