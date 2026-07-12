/**
 * ConversationPane.compose-edit-controls.test.tsx — DEF-12 wiring forcing test.
 *
 * DEF-12 moved the Accept / Reject / Try-another controls off the cramped in-editor bar and onto
 * the Assistant CONFIRMATION message (the AI↔user interaction surface). This proves the ConversationPane
 * side of that move WITHOUT re-implementing accept/reject logic:
 *
 *   (1) After a compose EDIT dispatch, the injected confirmation message carries `composeEdit`
 *       metadata whose `ledgerRef` addresses the applied compose output — the hook SprkChat uses to
 *       render the per-message controls — and stays SUMMARY-ONLY (no proposed text, no reasoning).
 *   (2) `activeComposeEditLedgerRef` threaded to SprkChat equals that ledgerRef (so only the live
 *       edit's confirmation shows controls).
 *   (3) Clicking Accept routes to the EXISTING editor accept via the compose bridge
 *       (`useComposeRedlineAccept`), then clears the live edit (controls disappear).
 *   (4) Clicking Reject routes to the EXISTING `useEditSupersession.undo` — a durable ledger
 *       supersession POST (not a DOM undo).
 *
 * The real dispatch runs (createConsumerDispatcher NOT mocked); the network is mocked at the wire
 * boundary and backed by one per-session in-memory ledger — mirroring the DEF-09 forcing test.
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
import {
  ComposeActionBridgeProvider,
  useComposeActionBridge,
  useRegisterComposeRedlineAcceptHandler,
  type ComposeActionBridgeValue,
} from '@spaarke/compose-components/context/composeActionBridge';

const CHAT_SESSION = '00000000-0000-0000-0000-00000000c8a7';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
const BFF = 'https://test-bff.example.com';
const DRAFT_BINDING = 'binding-draft-alternative-guid';
const LEDGER_REF = `${DRAFT_BINDING}@t1`;
const NEW_TEXT = 'The Supplier shall indemnify the Customer without any liability cap.';
const RATIONALE = 'clearer indemnity language';

interface LedgerOutput {
  key: string;
  bindingId: string;
  turn: number;
  disposition: string;
  payload: Record<string, unknown>;
}
const ledger = new Map<string, LedgerOutput[]>();
const dispatchPostUrls: string[] = [];
const supersedePostUrls: string[] = [];

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

function sessionFromUrl(url: string): string {
  return decodeURIComponent(url.match(/\/sessions\/([^/]+)\//)?.[1] ?? '');
}

const originalFetch = global.fetch;
beforeAll(() => {
  global.fetch = jest.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    if (url.includes('/dispatch')) {
      dispatchPostUrls.push(url);
      const session = sessionFromUrl(url);
      const entry: LedgerOutput = {
        key: LEDGER_REF,
        bindingId: DRAFT_BINDING,
        turn: 1,
        disposition: 'compose',
        payload: { target_text: 'the prior clause', new_text: NEW_TEXT, match_mode: 'strict', rationale: RATIONALE },
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

const authenticatedFetchMock = jest.fn(async (url: string, _init?: RequestInit) => {
  if (url.includes('/compose-outputs/supersede')) {
    supersedePostUrls.push(url);
    return { ok: true, status: 200, json: async () => ({ key: `${DRAFT_BINDING}@t2`, outcome: 'superseded' }) } as unknown as Response;
  }
  if (url.includes('/compose-outputs')) {
    const session = sessionFromUrl(url);
    return { ok: true, status: 200, json: async () => ledger.get(session) ?? [] } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

// SprkChat stub — captures the DEF-12 props + injection lifecycle.
const injectedMessages: IChatMessage[] = [];
interface CapturedChatProps {
  onComposeEditAccept?: (ledgerRef: string, bindingId: string) => void;
  onComposeEditReject?: (ledgerRef: string, bindingId: string) => void;
  onComposeEditTryAnother?: (ledgerRef: string, bindingId: string) => void;
  activeComposeEditLedgerRef?: string | null;
}
const captured: CapturedChatProps = {};

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  const ReactActual = jest.requireActual('react') as typeof import('react');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      captured.onComposeEditAccept = props.onComposeEditAccept;
      captured.onComposeEditReject = props.onComposeEditReject;
      captured.onComposeEditTryAnother = props.onComposeEditTryAnother;
      captured.activeComposeEditLedgerRef = props.activeComposeEditLedgerRef;
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

const bridgeRef: { current: ComposeActionBridgeValue | null } = { current: null };
function BridgeCapture(): null {
  bridgeRef.current = useComposeActionBridge();
  return null;
}

// Stand-in for ComposeWorkspace: registers a spy redline-accept handler on the bridge, exactly as
// the real workspace does (useRegisterComposeRedlineAcceptHandler → editor.acceptPendingRedline).
const acceptSpy = jest.fn();
function AcceptHandlerHarness(): null {
  useRegisterComposeRedlineAcceptHandler(acceptSpy);
  return null;
}

function renderPane() {
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={new PaneEventBus()}>
        <ComposeActionBridgeProvider>
          <ConversationPane />
          <BridgeCapture />
          <AcceptHandlerHarness />
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

beforeEach(() => {
  injectedMessages.length = 0;
  dispatchPostUrls.length = 0;
  supersedePostUrls.length = 0;
  ledger.clear();
  acceptSpy.mockClear();
  authenticatedFetchMock.mockClear();
  (global.fetch as jest.Mock).mockClear();
  bridgeRef.current = null;
  captured.onComposeEditAccept = undefined;
  captured.onComposeEditReject = undefined;
  captured.onComposeEditTryAnother = undefined;
  captured.activeComposeEditLedgerRef = null;
});

async function driveEditDispatch(): Promise<void> {
  await act(async () => {
    await bridgeRef.current!.enqueue({
      id: 'compose-draft-alternative#1',
      bindingId: DRAFT_BINDING,
      args: { slots: { selectionText: 'the prior clause', sessionId: DOC_SESSION } },
      documentSessionId: DOC_SESSION,
    });
    for (let i = 0; i < 10; i++) await Promise.resolve();
  });
}

describe('DEF-12: Accept/Reject/Try-another move onto the Assistant confirmation message', () => {
  it('injects a summary-only confirmation carrying composeEdit.ledgerRef and threads it as the active edit', async () => {
    renderPane();
    expect(bridgeRef.current?.hasDispatcher).toBe(true);
    expect(bridgeRef.current?.hasRedlineAcceptHandler).toBe(true);

    await driveEditDispatch();

    const confirmation = injectedMessages.find(m => m.metadata?.composeEdit);
    expect(confirmation).toBeDefined();
    expect(confirmation!.content).toBe(COMPOSE_EDIT_CONFIRMATION);
    expect(confirmation!.metadata!.composeEdit).toEqual({ ledgerRef: LEDGER_REF, bindingId: DRAFT_BINDING });
    // Summary-only — the proposed text and the reasoning are NOT dumped into the message.
    expect(confirmation!.content).not.toContain(NEW_TEXT);
    expect(confirmation!.content).not.toContain(RATIONALE);

    // The live edit is threaded to SprkChat so only its confirmation shows the controls.
    expect(captured.activeComposeEditLedgerRef).toBe(LEDGER_REF);
  });

  it('Accept routes to the existing editor accept via the compose bridge, then clears the live edit', async () => {
    renderPane();
    await driveEditDispatch();
    expect(captured.activeComposeEditLedgerRef).toBe(LEDGER_REF);

    await act(async () => {
      captured.onComposeEditAccept!(LEDGER_REF, DRAFT_BINDING);
      for (let i = 0; i < 4; i++) await Promise.resolve();
    });

    // Reused editor accept invoked with the message's ledgerRef (no parallel accept logic).
    expect(acceptSpy).toHaveBeenCalledTimes(1);
    expect(acceptSpy).toHaveBeenCalledWith(LEDGER_REF);
    // The live edit is cleared → the controls disappear (no lingering dead buttons).
    expect(captured.activeComposeEditLedgerRef).toBeNull();
    // Accept is NOT a supersession — no ledger retraction POST fired.
    expect(supersedePostUrls).toHaveLength(0);
  });

  it('Reject routes to the existing useEditSupersession.undo — a durable ledger supersession POST', async () => {
    renderPane();
    await driveEditDispatch();

    await act(async () => {
      captured.onComposeEditReject!(LEDGER_REF, DRAFT_BINDING);
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    // The retraction hit the supersede seam on the DOCUMENT session (where the edit lives).
    expect(supersedePostUrls.length).toBeGreaterThan(0);
    expect(supersedePostUrls.some(u => u.includes(`/sessions/${DOC_SESSION}/compose-outputs/supersede`))).toBe(true);
    // Reject is a ledger write, NOT the editor's client accept.
    expect(acceptSpy).not.toHaveBeenCalled();
  });
});
