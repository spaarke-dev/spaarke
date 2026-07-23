/**
 * ConversationPane.revise-race.e2e.test.tsx — spaarkeai-compose-r2 R2 (race-safe revise).
 *
 * Proves the RACE ordering: the user sends "…revise this document" BEFORE the chat upload's
 * registration has back-filled the active source doc id. The BUG was that
 * `mountActiveSourceDocInCompose` returned false (no sessionFileId yet), so the decorate hook fell
 * through to a normal agent turn dispatched to the CHAT session — narrated as prose (no mount, no
 * redline). The FIX buffers the revise intent and fires the mount + revise once
 * `onSessionFileUploaded` back-fills, so the flow ALWAYS ends in a mounted doc + doc-session routing.
 *
 * Harness mirrors ConversationPane.revise-this-document.e2e.test.tsx but GATES the `/documents`
 * upload response so we can call the decorate hook while the registration is still in flight.
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
import type { ISprkChatProps, IChatMessage } from '@spaarke/ui-components';
import {
  ComposeActionBridgeProvider,
  useComposeActionBridge,
  type ComposeActionBridgeValue,
} from '@spaarke/compose-components/context/composeActionBridge';

const CHAT_SESSION = '00000000-0000-0000-0000-00000000c8a7';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
const SESSION_FILE_ID = 'session-file-active-source-123';
const REVISE_BINDING = 'binding-revise-document-guid';
const BFF = 'https://test-bff.example.com';
const DOCX_MIME = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

const dispatchPostUrls: string[] = [];
const dispatchPostBodies: string[] = [];

function sseCompleteResponse(result: Record<string, unknown>): Response {
  const line = `data: ${JSON.stringify({ type: 'complete', done: true, result })}\n\n`;
  const chunk = new TextEncoder().encode(line);
  let sent = false;
  const body = {
    getReader() {
      return {
        read: async () => (sent ? { done: true, value: undefined } : ((sent = true), { done: false, value: chunk })),
        releaseLock() {},
      };
    },
  };
  return { ok: true, status: 200, body, json: async () => ({}), text: async () => '' } as unknown as Response;
}

const originalFetch = global.fetch;
beforeAll(() => {
  global.fetch = jest.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.includes('/dispatch')) {
      dispatchPostUrls.push(url);
      dispatchPostBodies.push(typeof init?.body === 'string' ? init.body : '');
      return sseCompleteResponse({ edits: [], rationale: 'revised', sources: [] });
    }
    return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
  }) as unknown as typeof fetch;
});
afterAll(() => {
  global.fetch = originalFetch;
});

// A gate on the `/documents` upload response so we can hold the registration IN FLIGHT.
let releaseDocuments: (() => void) | null = null;
let documentsGate: Promise<void> = Promise.resolve();

const authenticatedFetchMock = jest.fn(async (url: string) => {
  if (url.includes('/api/ai/capabilities')) {
    return {
      ok: true,
      status: 200,
      json: async () => ({
        capabilities: [
          {
            bindingId: REVISE_BINDING,
            consumerType: 'compose-revise-document',
            consumerCode: 'default',
            displayLabel: 'Revise the whole document.',
            surfaces: ['assistant', 'compose'],
            launchArgsSchemaJson: '{"type":"object","required":["revisionIntent"]}',
          },
        ],
      }),
    } as unknown as Response;
  }
  if (url.includes('/documents')) {
    // Race window: hold the upload response so onSessionFileUploaded hasn't back-filled yet.
    await documentsGate;
    return { ok: true, status: 200, json: async () => ({ documentId: SESSION_FILE_ID }) } as unknown as Response;
  }
  if (url.includes('/compose/active-document')) {
    return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
  }
  if (url.includes('/compose-outputs')) {
    return { ok: true, status: 200, json: async () => [] } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

const captured: {
  onDecorateOutboundBody?: (body: Record<string, unknown>) => Promise<Record<string, unknown> | null>;
  onAttachmentReady?: (a: unknown) => void;
  onAttachmentsChanged?: (chips: unknown[]) => void;
} = {};
const injectedMessages: IChatMessage[] = [];

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  const ReactActual = jest.requireActual('react') as typeof import('react');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      const p = props as ISprkChatProps & {
        onDecorateOutboundBody?: (body: Record<string, unknown>) => Promise<Record<string, unknown> | null>;
        onAttachmentReady?: (a: unknown) => void;
        onAttachmentsChanged?: (chips: unknown[]) => void;
      };
      captured.onDecorateOutboundBody = p.onDecorateOutboundBody;
      captured.onAttachmentReady = p.onAttachmentReady;
      captured.onAttachmentsChanged = p.onAttachmentsChanged;
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

function lastComposeUploadOpen(): WorkspacePaneEvent | undefined {
  return [...workspaceEvents].reverse().find((e) => e.type === 'widget_load' && e.widgetType === 'compose');
}

/** Attach a chat file WITHOUT letting the /documents upload resolve (registration stays in flight). */
async function attachFileInFlight(fileName: string): Promise<void> {
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
    // Let the promote effect START the /documents POST (which now hangs on documentsGate).
    for (let i = 0; i < 8; i++) await Promise.resolve();
  });
}

async function simulateComposeMountRegistration(): Promise<void> {
  await act(async () => {
    await bridgeRef.current!.registerActiveDocument({
      docxBytes: new Uint8Array([1, 2, 3]).buffer,
      fileName: 'revise-me.docx',
      documentSessionId: DOC_SESSION,
    });
    for (let i = 0; i < 8; i++) await Promise.resolve();
  });
}

beforeEach(() => {
  workspaceEvents.length = 0;
  dispatchPostUrls.length = 0;
  dispatchPostBodies.length = 0;
  injectedMessages.length = 0;
  authenticatedFetchMock.mockClear();
  (global.fetch as jest.Mock).mockClear();
  captured.onDecorateOutboundBody = undefined;
  captured.onAttachmentReady = undefined;
  captured.onAttachmentsChanged = undefined;
  bridgeRef.current = null;
  documentsGate = new Promise<void>((resolve) => {
    releaseDocuments = resolve;
  });
});

describe('R2: "revise this document" sent BEFORE the upload registration completes', () => {
  it('buffers the intent, suppresses the agent turn, then mounts + routes the revise to the DOCUMENT session once the upload back-fills', async () => {
    renderPane();

    // (1) File attached but its /documents registration is still in flight (gate held).
    await attachFileInFlight('revise-me.docx');
    workspaceEvents.length = 0;

    // (2) The user sends the revise NOW — before the source-doc id has back-filled.
    let decorateResult: unknown;
    await act(async () => {
      decorateResult = await captured.onDecorateOutboundBody!({ message: 'flag risks in this document' });
      for (let i = 0; i < 4; i++) await Promise.resolve();
    });

    // The agent turn is CANCELLED (buffered) — NOT dispatched to the chat session as prose.
    expect(decorateResult).toBeNull();
    // Nothing has mounted yet, and no server dispatch happened.
    expect(lastComposeUploadOpen()).toBeUndefined();
    expect(dispatchPostUrls).toHaveLength(0);

    // (3) The upload registration completes → onSessionFileUploaded back-fills the source doc id.
    await act(async () => {
      releaseDocuments!();
      for (let i = 0; i < 12; i++) await Promise.resolve();
    });

    // The buffered revise now auto-mounts the file into Compose (a compose.upload seed).
    const open = lastComposeUploadOpen();
    expect(open).toBeDefined();
    const compose = (open!.widgetData as { compose?: { upload?: Record<string, unknown> } }).compose ?? {};
    expect(compose.upload).toBeDefined();
    expect(compose.upload!.sessionFileId).toBe(SESSION_FILE_ID);
    // The named intent has NOT dispatched yet — it waits for the document session to register.
    expect(dispatchPostUrls).toHaveLength(0);

    // (4) The mount registers the document session → the named revise fires into the DOC session.
    await simulateComposeMountRegistration();
    await act(async () => {
      for (let i = 0; i < 10; i++) await Promise.resolve();
    });

    expect(dispatchPostUrls).toHaveLength(1);
    expect(dispatchPostUrls[0]).toContain(`/sessions/${DOC_SESSION}/dispatch`);
    expect(dispatchPostUrls[0]).not.toContain(`/sessions/${CHAT_SESSION}/dispatch`);
    expect(dispatchPostBodies[0]).toContain('flag-risks');
  });
});
