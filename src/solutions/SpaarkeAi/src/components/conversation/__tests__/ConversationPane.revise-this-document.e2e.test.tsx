/**
 * ConversationPane.revise-this-document.e2e.test.tsx — Wave 4 (end-to-end revise).
 *
 * Proves the natural-language "revise this document" flow for a CHAT-UPLOADED source document,
 * driving the REAL ConversationPane over a real PaneEventBus + ComposeActionBridge with the network
 * mocked at the wire boundary (createConsumerDispatcher is NOT mocked — the /dispatch URL is real, so
 * we can assert which SESSION the revise dispatch targeted).
 *
 * WHAT THIS PROVES:
 *   (a) "revise this document" (no named intent) with an active chat-uploaded source doc →
 *       CANCELS the agent turn (onDecorateOutboundBody returns null), AUTO-MOUNTS the file into
 *       Compose (a compose.upload seed), injects the mount-then-ask message, and renders the four
 *       intent chips — and does NOT POST a server revise dispatch (no narrated prose).
 *   (b) Clicking an intent chip (after the mount registers the document session) dispatches
 *       compose-revise-document into the DOCUMENT session (revisionScope whole-document) — asserted
 *       via the real /dispatch URL segment + the revisionIntent in the wire body.
 *   (c) A NAMED intent in the original message ("flag risks in this document") → mount + apply that
 *       intent directly once the document session registers, with NO chips shown.
 *
 * @see ./ConversationPane.open-in-compose-active-doc.test.tsx (chat-upload lifecycle harness this reuses)
 * @see ./ConversationPane.compose-revise-document-session-routing.e2e.test.tsx (real-dispatch harness this mirrors)
 */
import '@testing-library/jest-dom';
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextEncoder === 'undefined') (global as any).TextEncoder = NodeTextEncoder;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextDecoder === 'undefined') (global as any).TextDecoder = NodeTextDecoder;
import React, { act } from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
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
const SUMMARIZE_BINDING = 'binding-compose-summarize-guid';
const BFF = 'https://test-bff.example.com';
const DOCX_MIME = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

// Records for assertions.
const dispatchPostUrls: string[] = [];
const dispatchPostBodies: string[] = [];

/** Build an SSE `Response` whose single `complete` chunk carries a compose-disposition edit output. */
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

// global.fetch — the /dispatch SSE wire (dispatchConsumer → readSseStream, token mode).
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

// authenticatedFetch (useAiSession) — capability discovery + chat upload + active-document + ledger read.
const activeDocPostBodies: Array<Record<string, unknown>> = [];
const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit) => {
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
          {
            bindingId: SUMMARIZE_BINDING,
            consumerType: 'compose-summarize',
            consumerCode: 'default',
            displayLabel: 'Summarize the document.',
            surfaces: ['assistant', 'compose'],
            launchArgsSchemaJson: '{"type":"object"}',
          },
        ],
      }),
    } as unknown as Response;
  }
  if (url.includes('/documents')) {
    return { ok: true, status: 200, json: async () => ({ documentId: SESSION_FILE_ID }) } as unknown as Response;
  }
  if (url.includes('/compose/active-document')) {
    activeDocPostBodies.push(JSON.parse(String(init?.body ?? '{}')));
    return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
  }
  if (url.includes('/compose-outputs')) {
    return { ok: true, status: 200, json: async () => [] } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

// SprkChat stub — captures the send-time hooks + the attachment lifecycle + drives injection.
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
import { ConversationPane, REVISE_MOUNT_ASK_MESSAGE } from '../ConversationPane';

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
  // spaarkeai-compose-r2: Compose mounts through the first-class DIRECT 'compose'
  // widget (ComposeDirectWidget), so the mount dispatch carries widgetType
  // 'compose' (not the LAYOUT door widgetType 'workspace' + layoutName 'Compose').
  return [...workspaceEvents]
    .reverse()
    .find((e) => e.type === 'widget_load' && e.widgetType === 'compose');
}

/** Drives the chat attachment lifecycle so the auto-promote effect POSTs /documents (→ active source doc). */
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
    for (let i = 0; i < 12; i++) await Promise.resolve();
  });
}

/** Simulates ComposeWorkspace registering the mounted upload's document session (post-mount back-fill). */
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
  activeDocPostBodies.length = 0;
  injectedMessages.length = 0;
  authenticatedFetchMock.mockClear();
  (global.fetch as jest.Mock).mockClear();
  captured.onDecorateOutboundBody = undefined;
  captured.onAttachmentReady = undefined;
  captured.onAttachmentsChanged = undefined;
  bridgeRef.current = null;
});

describe('FIX #1: "revise this document" (no named intent) → mount + editor-centric message + doc-action chips', () => {
  it('cancels the agent turn, auto-mounts the file, injects the mount message, renders the three doc-action chips', async () => {
    renderPane();
    await driveChatUpload('revise-me.docx');
    workspaceEvents.length = 0;

    let decorateResult: unknown;
    await act(async () => {
      decorateResult = await captured.onDecorateOutboundBody!({ message: 'revise this document' });
      for (let i = 0; i < 4; i++) await Promise.resolve();
    });

    // The agent turn is CANCELLED (return null) — the client orchestrates the flow instead.
    expect(decorateResult).toBeNull();

    // The active source document was auto-mounted into Compose via a compose.upload seed.
    const open = lastComposeUploadOpen();
    expect(open).toBeDefined();
    const compose = (open!.widgetData as { compose?: { upload?: Record<string, unknown> } }).compose ?? {};
    expect(compose.upload).toBeDefined();
    expect(compose.upload!.sessionFileId).toBe(SESSION_FILE_ID);

    // The editor-centric mount message is injected + the three DOCUMENT-LEVEL action chips render.
    expect(injectedMessages.map((m) => m.content)).toContain(REVISE_MOUNT_ASK_MESSAGE);
    expect(screen.getByTestId('compose-doc-action-chips')).toBeInTheDocument();
    expect(screen.getByTestId('compose-doc-action-chip-summarize')).toBeInTheDocument();
    expect(screen.getByTestId('compose-doc-action-chip-add-to-dms')).toBeInTheDocument();
    expect(screen.getByTestId('compose-doc-action-chip-draft-email')).toBeInTheDocument();

    // FIX #1a: the chips render INSIDE SprkChat's transcript footer slot (beneath the ask message),
    // NOT above the chat — the stub renders only `transcriptFooterSlot`, so finding the chips within
    // it proves the below-the-message placement.
    const stub = screen.getByTestId('sprkchat-stub');
    expect(within(stub).getByTestId('compose-doc-action-chips')).toBeInTheDocument();

    // No SERVER revise dispatch was made — the chips are document actions, not a whole-document revise.
    expect(dispatchPostUrls).toHaveLength(0);
  });
});

describe('FIX #1: the document-level action chips reuse existing mechanisms', () => {
  it('"Draft a reporting email" dispatches the Email workspace widget_load (exact interop contract), no revise dispatch', async () => {
    renderPane();
    await driveChatUpload('revise-me.docx');

    await act(async () => {
      await captured.onDecorateOutboundBody!({ message: 'revise this document' });
      for (let i = 0; i < 4; i++) await Promise.resolve();
    });
    workspaceEvents.length = 0;

    await act(async () => {
      fireEvent.click(screen.getByTestId('compose-doc-action-chip-draft-email'));
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    const emailEvent = workspaceEvents.find(
      (e) => e.type === 'widget_load' && e.widgetType === 'email'
    ) as (WorkspacePaneEvent & { layoutName?: string; widgetData?: { source?: string } }) | undefined;
    expect(emailEvent).toBeDefined();
    expect(emailEvent!.layoutName).toBe('Email');
    expect(emailEvent!.widgetData?.source).toBe('compose-reporting-email');

    // The email chip is NOT a revise — no /dispatch was POSTed.
    expect(dispatchPostUrls).toHaveLength(0);
  });

  it('"Summarize the document" dispatches compose-summarize on the CHAT session (informational)', async () => {
    renderPane();
    await driveChatUpload('revise-me.docx');

    await act(async () => {
      await captured.onDecorateOutboundBody!({ message: 'revise this document' });
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    await act(async () => {
      fireEvent.click(screen.getByTestId('compose-doc-action-chip-summarize'));
      for (let i = 0; i < 10; i++) await Promise.resolve();
    });

    // Summarize dispatches on the CHAT session (not the document session) — informational.
    expect(dispatchPostUrls).toHaveLength(1);
    expect(dispatchPostUrls[0]).toContain(`/sessions/${CHAT_SESSION}/dispatch`);
    expect(dispatchPostUrls[0]).not.toContain(`/sessions/${DOC_SESSION}/dispatch`);
  });
});

describe('Wave 4: a NAMED intent in the original message mounts + applies directly (no chips)', () => {
  it('flag risks in this document → mount then auto-dispatch flag-risks once the doc session registers', async () => {
    renderPane();
    await driveChatUpload('revise-me.docx');
    workspaceEvents.length = 0;

    let decorateResult: unknown;
    await act(async () => {
      decorateResult = await captured.onDecorateOutboundBody!({ message: 'flag risks in this document' });
      for (let i = 0; i < 4; i++) await Promise.resolve();
    });

    // Agent turn cancelled + file auto-mounted, but NO chips + NO ask message (intent already named).
    expect(decorateResult).toBeNull();
    expect(lastComposeUploadOpen()).toBeDefined();
    expect(screen.queryByTestId('revise-intent-chips')).not.toBeInTheDocument();
    expect(injectedMessages.map((m) => m.content)).not.toContain(REVISE_MOUNT_ASK_MESSAGE);
    expect(dispatchPostUrls).toHaveLength(0); // nothing dispatched until the doc session registers

    // The mount registers the document session → the pending named revise fires automatically.
    await simulateComposeMountRegistration();
    await act(async () => {
      for (let i = 0; i < 10; i++) await Promise.resolve();
    });

    expect(dispatchPostUrls).toHaveLength(1);
    expect(dispatchPostUrls[0]).toContain(`/sessions/${DOC_SESSION}/dispatch`);
    expect(dispatchPostBodies[0]).toContain('flag-risks');
  });
});

describe('FIX #1b/#7a: "Add the document to the DMS" chip → editor Save → persistent chat confirmation', () => {
  it('clicking Add-to-DMS drives the editor Save conduit; the save-completed conduit posts a "Saved to the DMS" message with Open preview metadata', async () => {
    renderPane();
    await driveChatUpload('revise-me.docx');

    // Drive the revise flow so the three doc-action chips render (in the transcript footer).
    await act(async () => {
      await captured.onDecorateOutboundBody!({ message: 'revise this document' });
      for (let i = 0; i < 4; i++) await Promise.resolve();
    });
    expect(screen.getByTestId('compose-doc-action-chip-add-to-dms')).toBeInTheDocument();

    // Simulate ComposeWorkspace publishing its Save handler onto the bridge (a live Compose tab).
    const fakeSave = jest.fn();
    await act(async () => {
      bridgeRef.current!.setComposeSaveHandler(fakeSave);
      for (let i = 0; i < 4; i++) await Promise.resolve();
    });

    // Clicking "Add the document to the DMS" triggers the ACTUAL editor Save via the bridge conduit
    // (FIX #1b) — no eager transient message, no workspace re-activation hand-off.
    await act(async () => {
      fireEvent.click(screen.getByTestId('compose-doc-action-chip-add-to-dms'));
      for (let i = 0; i < 4; i++) await Promise.resolve();
    });
    expect(fakeSave).toHaveBeenCalledTimes(1);

    // On Save success ComposeWorkspace fires the save-completed conduit → ConversationPane injects a
    // PERSISTENT "Saved '…' to the DMS." chat message carrying savedPreview metadata (FIX #7a).
    injectedMessages.length = 0;
    await act(async () => {
      bridgeRef.current!.notifyComposeSaveCompleted({ documentRecordId: 'doc-xyz', fileName: 'revise-me.docx' });
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    const savedMsg = injectedMessages.find((m) => m.content.includes('to the DMS'));
    expect(savedMsg).toBeDefined();
    expect(savedMsg!.content).toBe("Saved 'revise-me.docx' to the DMS.");
    expect(savedMsg!.metadata?.savedPreview).toEqual({ documentId: 'doc-xyz', fileName: 'revise-me.docx' });
  });
});
