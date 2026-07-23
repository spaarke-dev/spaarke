/**
 * ConversationPane — DEF-11 whole-document revise two-session routing forcing test.
 *
 * Mirrors `ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx` (DEF-09)
 * EXACTLY — same harness, same two-session forcing rationale, same real dispatch / real bridge /
 * real workspace receiver topology — but for the DEF-11 whole-document revise action
 * (`compose-revise-document`), whose stored `compose`-disposition ledger output carries a
 * MULTI-change `edits[]` list rather than a single `{target_text,new_text}` payload.
 *
 * WHAT THIS PROVES (the two-session-routing half of the DEF-11 DoD — the multi-change
 * MATERIALIZE half is proven in the sibling shared-lib test
 * `ComposeWorkspace.redline-from-ledger.test.tsx`, TipTap-mount-only, per the same split
 * rationale DEF-09 uses):
 *   1. The whole-document revise dispatch (`documentSessionId` present, `revisionScope:
 *      'whole-document'`) targets the DOCUMENT session, never the chat session — the SAME
 *      `dispatchComposeAction` session-routing fix DEF-09 shipped, now exercised for a DIFFERENT
 *      action shape (a multi-change payload) to prove the routing is payload-shape-agnostic.
 *   2. The compose output written under the DOCUMENT session round-trips through the apply-leg
 *      READ, and a Flow-5 `compose_assistant_insert` reaches the REAL workspace receiver carrying
 *      the base `ledgerRef` (never a sub-key — the workspace resolves sub-keys internally via
 *      `usePendingRedline.materializeMany`).
 *   3. The Assistant confirmation carries the `composeEdit` controls (ledgerRef, bindingId) with
 *      the DEF-11 whole-document COPY VARIANT (`COMPOSE_WHOLE_DOCUMENT_EDIT_CONFIRMATION`), not the
 *      DEF-09 selection copy — proving `revisionScope` threads through correctly.
 *
 * @see ../ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx (the DEF-09
 *      original this mirrors — same mock strategy, same harness)
 * @see src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.redline-from-ledger.test.tsx
 *      (the multi-change MATERIALIZE + Accept-all half, TipTap-mount-only)
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
const REVISE_BINDING = 'binding-revise-document-guid';
const REVISE_LEDGER_REF = `${REVISE_BINDING}@t1`;
const EDIT_TEXTS = [
  'The Supplier shall indemnify the Customer without any liability cap.',
  'Either party may terminate this Agreement upon 30 days written notice.',
  'This Agreement shall be governed by the laws of the State of Delaware.',
];

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
/** Records every dispatch POST *body* so we can assert the revisionIntent slot reached the wire. */
const dispatchPostBodies: string[] = [];

/** Build an SSE `Response` whose single `complete` chunk carries the multi-change payload. */
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
  global.fetch = jest.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.includes('/dispatch')) {
      dispatchPostUrls.push(url);
      dispatchPostBodies.push(typeof init?.body === 'string' ? init.body : '');
      const session = sessionFromUrl(url);
      // Store the MULTI-CHANGE compose output UNDER THE SESSION THE DISPATCH TARGETED (the fix
      // routes this to DOC_SESSION; a regression would store it under CHAT_SESSION).
      const entry: LedgerOutput = {
        key: REVISE_LEDGER_REF,
        bindingId: REVISE_BINDING,
        turn: 1,
        disposition: 'compose',
        payload: {
          edits: [
            { target_text: 'liability clause', new_text: EDIT_TEXTS[0], match_mode: 'strict' },
            { target_text: 'termination clause', new_text: EDIT_TEXTS[1], match_mode: 'strict' },
            { target_text: 'governing law clause', new_text: EDIT_TEXTS[2], match_mode: 'strict' },
          ],
          rationale: 'Improved clarity across three clauses.',
          sources: [],
        },
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
// Mirrors the DEF-09 original. createConsumerDispatcher is DELIBERATELY NOT mocked —
// the REAL dispatch runs so the /dispatch URL is real.
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
import { ConversationPane, COMPOSE_WHOLE_DOCUMENT_EDIT_CONFIRMATION, COMPOSE_EDIT_CONFIRMATION } from '../ConversationPane';

// ---------------------------------------------------------------------------
// Harness — REAL bus + REAL bridge + REAL workspace receiver (the shipped Flow-5
// receiver, editor stubbed via spy) co-mounted with the REAL ConversationPane,
// the exact three-pane topology (minus TipTap). Identical to the DEF-09 harness.
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
  dispatchPostBodies.length = 0;
  ledger.clear();
  authenticatedFetchMock.mockClear();
  (global.fetch as jest.Mock).mockClear();
  bridgeRef.current = null;
});

// ---------------------------------------------------------------------------
// The forcing function
// ---------------------------------------------------------------------------
describe('DEF-11: Compose whole-document revise routes to the DOCUMENT session (two-session forcing test)', () => {
  it('dispatches the multi-change edits[] output to the DOCUMENT session, materializes from that session, and confirms with the whole-document copy', async () => {
    const { onAssistantInsert } = renderThreePane();
    expect(bridgeRef.current?.hasDispatcher).toBe(true);

    await act(async () => {
      // Exactly what a whole-document revise trigger sends: a `revisionIntent` slot (the DEF-11
      // input schema), `documentSessionId` (the open editor's DOCUMENT session — an edit action per
      // DEF-09's `isEditAction` gate), and `revisionScope: 'whole-document'` (confirmation-copy pick
      // only — see ConversationPane.dispatchComposeAction).
      await bridgeRef.current!.enqueue({
        id: 'compose-revise-document#1',
        bindingId: REVISE_BINDING,
        args: { slots: { revisionIntent: 'improve-clarity', documentText: 'the whole loaded document text' } },
        documentSessionId: DOC_SESSION,
        revisionScope: 'whole-document',
      });
      for (let i = 0; i < 8; i++) await Promise.resolve();
    });

    // (1) The ACTUAL /dispatch POST targeted the DOCUMENT session — NOT the chat session. The
    // routing fix is payload-shape-agnostic: it works the same for a multi-change whole-doc output
    // as it does for DEF-09's single-edit output.
    expect(dispatchPostUrls).toHaveLength(1);
    expect(dispatchPostUrls[0]).toContain(`/sessions/${DOC_SESSION}/dispatch`);
    expect(dispatchPostUrls[0]).not.toContain(`/sessions/${CHAT_SESSION}/dispatch`);
    // The revisionIntent slot reached the wire body (sessionIdOverride folded in alongside it).
    expect(dispatchPostBodies[0]).toContain('improve-clarity');

    // (2) The compose output was written under the DOCUMENT session, and the apply-leg
    // READ hit that SAME document session → a Flow-5 compose_assistant_insert reached the
    // REAL workspace receiver carrying the BASE ledgerRef (never a sub-key — ComposeWorkspace
    // resolves `#{i}` sub-keys internally via usePendingRedline.materializeMany).
    expect(ledger.get(DOC_SESSION)).toHaveLength(1);
    expect(ledger.get(CHAT_SESSION) ?? []).toHaveLength(0);
    const composeOutputsReads = authenticatedFetchMock.mock.calls
      .map((c) => String(c[0]))
      .filter((u) => u.includes('/compose-outputs'));
    expect(composeOutputsReads.some((u) => u.includes(`/sessions/${DOC_SESSION}/compose-outputs`))).toBe(true);
    expect(onAssistantInsert).toHaveBeenCalledTimes(1);
    const flow5 = onAssistantInsert.mock.calls[0][0] as WorkspacePaneEvent;
    expect(flow5.type).toBe('compose_assistant_insert');
    expect(flow5.ledgerRef).toBe(REVISE_LEDGER_REF); // BASE key — no `#{i}` suffix
    expect(flow5.sessionId).toBe(DOC_SESSION);

    // (3) The Assistant got the DEF-11 WHOLE-DOCUMENT confirmation copy (not DEF-09's selection
    // copy), carrying the composeEdit controls (ledgerRef/bindingId) — never the proposed text.
    const contents = injectedMessages.map((m) => m.content);
    expect(contents).toContain(COMPOSE_WHOLE_DOCUMENT_EDIT_CONFIRMATION);
    expect(contents).not.toContain(COMPOSE_EDIT_CONFIRMATION);
    expect(contents.every((c) => !EDIT_TEXTS.some((t) => c.includes(t)))).toBe(true);
    expect(injectedMessages.every((m) => m.role === 'Assistant')).toBe(true);
    const controlsMessage = injectedMessages.find((m) => m.content === COMPOSE_WHOLE_DOCUMENT_EDIT_CONFIRMATION);
    expect(controlsMessage?.metadata?.composeEdit).toMatchObject({
      ledgerRef: REVISE_LEDGER_REF,
      bindingId: REVISE_BINDING,
    });

    // (4) Accept-all: the SAME bridge conduit DEF-12's per-message Accept control uses
    // (`acceptRedlineViaBridge(ledgerRef)` with the BASE key) reaches the editor's redline-accept
    // handler — proven end-to-end (with a REAL TipTap mount) in the sibling shared-lib test; here we
    // assert only that the bridge is registered and reachable with the base key (no editor is
    // mounted in this jsdom env — see file header split rationale).
    expect(bridgeRef.current?.hasRedlineAcceptHandler).toBe(false); // no ComposeWorkspace mounted here
    expect(() => bridgeRef.current!.acceptRedline(REVISE_LEDGER_REF)).not.toThrow();
  });
});
