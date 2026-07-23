/**
 * ConversationPane — DEF-09 two-session Compose EDIT-action routing forcing test.
 *
 * THE DEFECT (owner UAT): "Draft alternative" rendered its proposed clause as a
 * chat MESSAGE in the Assistant and the document was NOT redlined. Root cause: the
 * toolbar dispatch wrote its `compose`-disposition SessionOutput into the ASSISTANT
 * CHAT session, but `ComposeWorkspace` reads `compose-outputs` from its OWN
 * DOCUMENT-keyed session to materialize the inline redline — different session ids,
 * so the materialize read found nothing → no redline, no accept/reject.
 *
 * THE FIX (client-only session routing): a compose-disposition EDIT action carries
 * the editor's DOCUMENT session id; `ConversationPane.dispatchComposeAction` folds it
 * into `args.sessionIdOverride` so the `/dispatch` write targets the DOCUMENT session
 * (the WRITE and the redline-materialize READ now coincide), and the Assistant shows a
 * CONFIRMATION-only line instead of the proposed-text prose.
 *
 * WHY THIS IS A FORCING FUNCTION (per project E2E DoD + the task-115 process lesson —
 * "do NOT share one session id or mock the ledger away"):
 *   - TWO DISTINCT session ids are used: a CHAT session (ConversationPane's
 *     `chatSessionId`) and a DISTINCT DOCUMENT session (threaded via the edit action's
 *     `documentSessionId`, exactly as `ComposeAiToolbar` sends it for Draft alternative).
 *   - The REAL dispatch runs (`createConsumerDispatcher` is NOT mocked); the network is
 *     mocked at the true wire boundary (global `fetch` for the SSE `/dispatch`,
 *     `authenticatedFetch` for the `compose-outputs` read) and backed by ONE in-memory
 *     ledger KEYED BY SESSION. The dispatch stores its compose output under whichever
 *     session it POSTed to; the apply-leg reads the DOCUMENT session. If the dispatch
 *     regressed to the chat-session write, the DOCUMENT-session read returns EMPTY, no
 *     Flow-5 `compose_assistant_insert` reaches the (REAL) workspace receiver, and the
 *     assertions below fail. Assertion (1) also fails directly on the POST URL.
 *
 * The redline-CONTROLS render (accept/reject) from a document-session read is proven in
 * the sibling shared-lib test `ComposeWorkspace.redline-from-ledger.test.tsx` (TipTap
 * mounts only in the @spaarke/compose-components jest env; ConversationPane — the unit
 * that owns the write-routing fix — lives in this solution and cannot be imported there).
 * Together the two files close the write → read → render slice.
 *
 * @see ../ConversationPane.tsx (dispatchComposeAction — the routing fix + confirmation-only)
 * @see ../ConversationPane.compose-action-format.test.tsx (the mock-strategy this extends)
 * @see src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.redline-from-ledger.test.tsx
 */

import '@testing-library/jest-dom';
// jsdom (this env) ships no TextEncoder/TextDecoder — readSseStream + our SSE
// response builder need them. Polyfill from Node's `util` before any dispatch runs.
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
import { useComposeWorkspaceReceivers } from '@spaarke/compose-components/widgets/useComposeWorkspaceReceivers';

// ---------------------------------------------------------------------------
// Two DISTINCT session ids — the whole point of the forcing function.
// ---------------------------------------------------------------------------
const CHAT_SESSION = '00000000-0000-0000-0000-00000000c8a7';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
const BFF = 'https://test-bff.example.com';
const DRAFT_BINDING = 'binding-draft-alternative-guid';
const NEW_TEXT = 'The Supplier shall indemnify the Customer without any liability cap.';

// ---------------------------------------------------------------------------
// ONE in-memory ledger, KEYED BY SESSION. The dispatch stores under the session
// it POSTed to; the apply-leg reads a session. A session mismatch → empty read.
// ---------------------------------------------------------------------------
interface LedgerOutput {
  key: string;
  bindingId: string;
  turn: number;
  disposition: string;
  payload: Record<string, unknown>;
}
const ledger = new Map<string, LedgerOutput[]>();

/** Records every `/dispatch` POST so we can assert the ACTUAL session it targeted. */
const dispatchPostUrls: string[] = [];

/** Build an SSE `Response` whose single `complete` chunk carries the draft payload. */
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

/** Extract the `{sessionId}` segment from a `/sessions/{id}/…` URL. */
function sessionFromUrl(url: string): string {
  return decodeURIComponent(url.match(/\/sessions\/([^/]+)\//)?.[1] ?? '');
}

// global.fetch — the /dispatch SSE wire (dispatchConsumer → readSseStream, token mode).
const originalFetch = global.fetch;
beforeAll(() => {
  global.fetch = jest.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    if (url.includes('/dispatch')) {
      dispatchPostUrls.push(url);
      const session = sessionFromUrl(url);
      // Store the compose output UNDER THE SESSION THE DISPATCH TARGETED (the fix routes
      // this to DOC_SESSION; a regression would store it under CHAT_SESSION).
      const entry: LedgerOutput = {
        key: `${DRAFT_BINDING}@t1`,
        bindingId: DRAFT_BINDING,
        turn: 1,
        disposition: 'compose',
        payload: { target_text: 'the prior clause', new_text: NEW_TEXT, match_mode: 'strict', rationale: 'clearer' },
      };
      ledger.set(session, [...(ledger.get(session) ?? []), entry]);
      return sseCompleteResponse(entry.payload);
    }
    return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
  }) as unknown as typeof fetch;
});
afterAll(() => {
  global.fetch = originalFetch;
});

// ---------------------------------------------------------------------------
// authenticatedFetch (useAiSession) — the compose-outputs READ the apply-leg does.
// Reads the ledger for the REQUESTED session (returns [] for a session with no writes).
// ---------------------------------------------------------------------------
const authenticatedFetchMock = jest.fn(async (url: string, _init?: RequestInit) => {
  if (url.includes('/compose-outputs')) {
    const session = sessionFromUrl(url);
    return { ok: true, status: 200, json: async () => ledger.get(session) ?? [] } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

// ---------------------------------------------------------------------------
// SprkChat stub (prop capture + injection-lifecycle) + useAiSession (CHAT session).
// Mirrors ConversationPane.compose-action-format.test.tsx. createConsumerDispatcher
// is DELIBERATELY NOT mocked — the REAL dispatch runs so the /dispatch URL is real.
// ---------------------------------------------------------------------------
const injectedMessages: IChatMessage[] = [];

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
import { ConversationPane, COMPOSE_EDIT_CONFIRMATION } from '../ConversationPane';

// ---------------------------------------------------------------------------
// Harness — REAL bus + REAL bridge + REAL workspace receiver (the shipped Flow-5
// receiver, editor stubbed via spy) co-mounted with the REAL ConversationPane,
// the exact three-pane topology (minus TipTap).
// ---------------------------------------------------------------------------
const bridgeRef: { current: ComposeActionBridgeValue | null } = { current: null };
function BridgeCapture(): null {
  bridgeRef.current = useComposeActionBridge();
  return null;
}

function WorkspaceReceiverHarness(props: { onAssistantInsert: (e: WorkspacePaneEvent) => void }): React.JSX.Element {
  useComposeWorkspaceReceivers({
    onContextInsert: () => undefined,
    onAssistantInsert: props.onAssistantInsert,
    onQaHighlight: () => undefined,
  });
  return <div data-testid="workspace-receiver-harness" />;
}

function renderThreePane(): { onAssistantInsert: jest.Mock } {
  const onAssistantInsert = jest.fn();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={new PaneEventBus()}>
        <ComposeActionBridgeProvider>
          <ConversationPane />
          <BridgeCapture />
          <WorkspaceReceiverHarness onAssistantInsert={onAssistantInsert} />
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
  return { onAssistantInsert };
}

beforeEach(() => {
  injectedMessages.length = 0;
  dispatchPostUrls.length = 0;
  ledger.clear();
  authenticatedFetchMock.mockClear();
  (global.fetch as jest.Mock).mockClear();
  bridgeRef.current = null;
});

// ---------------------------------------------------------------------------
// The forcing function
// ---------------------------------------------------------------------------
describe('DEF-09: Compose "Draft alternative" routes to the DOCUMENT session (two-session forcing test)', () => {
  it('dispatches to the DOCUMENT session, materializes from that same session, and confirms-only in the Assistant', async () => {
    const { onAssistantInsert } = renderThreePane();
    expect(bridgeRef.current?.hasDispatcher).toBe(true);

    await act(async () => {
      // Exactly what ComposeAiToolbar.handleActionClick sends for Draft alternative
      // (materializesInEditor ⇒ documentSessionId = the editor's DOCUMENT session).
      await bridgeRef.current!.enqueue({
        id: 'compose-draft-alternative#1',
        bindingId: DRAFT_BINDING,
        args: { slots: { selectionText: 'the prior clause', sessionId: DOC_SESSION } },
        documentSessionId: DOC_SESSION,
      });
      for (let i = 0; i < 8; i++) await Promise.resolve();
    });

    // (1) The ACTUAL /dispatch POST targeted the DOCUMENT session — NOT the chat session.
    expect(dispatchPostUrls).toHaveLength(1);
    expect(dispatchPostUrls[0]).toContain(`/sessions/${DOC_SESSION}/dispatch`);
    expect(dispatchPostUrls[0]).not.toContain(`/sessions/${CHAT_SESSION}/dispatch`);

    // (2) The compose output was written under the DOCUMENT session, and the apply-leg
    // READ hit that SAME document session → a Flow-5 compose_assistant_insert reached the
    // REAL workspace receiver carrying the ledgerRef resolved from the DOCUMENT session.
    // (A chat-session write would leave the document session empty → no such event.)
    expect(ledger.get(DOC_SESSION)).toHaveLength(1);
    expect(ledger.get(CHAT_SESSION) ?? []).toHaveLength(0);
    const composeOutputsReads = authenticatedFetchMock.mock.calls
      .map((c) => String(c[0]))
      .filter((u) => u.includes('/compose-outputs'));
    expect(composeOutputsReads.some((u) => u.includes(`/sessions/${DOC_SESSION}/compose-outputs`))).toBe(true);
    expect(onAssistantInsert).toHaveBeenCalledTimes(1);
    const flow5 = onAssistantInsert.mock.calls[0][0] as WorkspacePaneEvent;
    expect(flow5.type).toBe('compose_assistant_insert');
    expect(flow5.ledgerRef).toBe(`${DRAFT_BINDING}@t1`);
    expect(flow5.sessionId).toBe(DOC_SESSION);

    // (3) The Assistant got a CONFIRMATION-only line — NOT the proposed-text prose.
    const contents = injectedMessages.map((m) => m.content);
    expect(contents).toContain(COMPOSE_EDIT_CONFIRMATION);
    expect(contents.every((c) => !c.includes(NEW_TEXT))).toBe(true);
    expect(contents.every((c) => !c.includes('**Proposed text:**'))).toBe(true);
    expect(injectedMessages.every((m) => m.role === 'Assistant')).toBe(true);
  });

  it('(regression guard) an INFORMATIONAL compose action (no documentSessionId) still dispatches to the CHAT session and renders prose', async () => {
    const { onAssistantInsert } = renderThreePane();

    // Explain-clause result shape (informational) — served via the same /dispatch wire.
    (global.fetch as jest.Mock).mockImplementationOnce(async (input: RequestInfo | URL) => {
      dispatchPostUrls.push(String(input));
      return sseCompleteResponse({
        explanation: 'This clause caps liability at the contract value.',
        keyConcepts: ['liability cap'],
      });
    });

    await act(async () => {
      await bridgeRef.current!.enqueue({
        id: 'compose-explain-clause#1',
        bindingId: 'binding-explain-guid',
        args: { slots: { selectionText: 'the clause', sessionId: DOC_SESSION } },
        // NO documentSessionId — informational path.
      });
      for (let i = 0; i < 8; i++) await Promise.resolve();
    });

    // Informational actions still target the CHAT session (unchanged behavior).
    expect(dispatchPostUrls[0]).toContain(`/sessions/${CHAT_SESSION}/dispatch`);
    // Full grounded prose, NOT the edit confirmation, and no Flow-5 materialize.
    const contents = injectedMessages.map((m) => m.content);
    expect(contents.some((c) => c.includes('**Explanation:**'))).toBe(true);
    expect(contents).not.toContain(COMPOSE_EDIT_CONFIRMATION);
    expect(onAssistantInsert).not.toHaveBeenCalled();
  });
});
