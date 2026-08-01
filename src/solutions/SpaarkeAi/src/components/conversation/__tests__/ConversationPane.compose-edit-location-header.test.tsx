/**
 * ConversationPane.compose-edit-location-header.test.tsx — task 042 (FR-12) coverage.
 *
 * Proves the bold clause-location header + graceful no-location fallback added to the compose EDIT
 * confirmation (`dispatchComposeAction`'s `extractComposeEditLocationLabel` /
 * `withComposeEditLocationHeader` helpers in `ConversationPane.tsx`), WITHOUT touching
 * `ConversationPane.compose-edit-controls.test.tsx`'s locked exact-match assertion (ADR-041 "existing
 * tests pass untouched"). That DEF-12 forcing test's dispatch payload carries no location signal, so
 * it already IS the byte-identical no-header case — this suite adds the resolved-location, batch, and
 * graceful-fallback coverage that test does not exercise (see `extractComposeEditLocationLabel`'s doc
 * comment in `ConversationPane.tsx` for the current wiring-gap rationale: no shipped caller populates a
 * location field yet — this activates automatically once one does, without further changes here).
 *
 * Same harness pattern as ConversationPane.compose-edit-controls.test.tsx (real dispatch, wire-boundary
 * fetch mock, one per-session in-memory ledger) — see that file's header for the harness rationale.
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
  type ComposeActionBridgeValue,
} from '@spaarke/compose-components/context/composeActionBridge';

const CHAT_SESSION = '00000000-0000-0000-0000-00000000c8a7';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
const BFF = 'https://test-bff.example.com';
const DRAFT_BINDING = 'binding-draft-alternative-guid';

interface LedgerOutput {
  key: string;
  bindingId: string;
  turn: number;
  disposition: string;
  payload: Record<string, unknown>;
}
const ledger = new Map<string, LedgerOutput[]>();
let turnCounter = 0;
// Set BEFORE a dispatch to merge extra fields onto the mocked ledger payload — simulates a future
// result shape that carries a location signal (extractComposeEditLocationLabel's "result" fallback
// branch, exercised separately from the "request slots" branch below).
let nextResultExtra: Record<string, unknown> = {};

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
      const session = sessionFromUrl(url);
      turnCounter += 1;
      const entry: LedgerOutput = {
        key: `${DRAFT_BINDING}@t${turnCounter}`,
        bindingId: DRAFT_BINDING,
        turn: turnCounter,
        disposition: 'compose',
        payload: {
          target_text: 'the prior clause',
          new_text: 'The Supplier shall indemnify the Customer.',
          match_mode: 'strict',
          rationale: 'clearer indemnity language',
          ...nextResultExtra,
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

const authenticatedFetchMock = jest.fn(async (url: string) => {
  if (url.includes('/compose-outputs')) {
    const session = sessionFromUrl(url);
    return { ok: true, status: 200, json: async () => ledger.get(session) ?? [] } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

// SprkChat stub — captures injected local (Assistant confirmation) messages only; this suite does not
// exercise Accept/Reject/Try-another (covered by ConversationPane.compose-edit-controls.test.tsx).
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

const bridgeRef: { current: ComposeActionBridgeValue | null } = { current: null };
function BridgeCapture(): null {
  bridgeRef.current = useComposeActionBridge();
  return null;
}

function renderPane(): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={new PaneEventBus()}>
        <ComposeActionBridgeProvider>
          <ConversationPane />
          <BridgeCapture />
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

beforeEach(() => {
  injectedMessages.length = 0;
  ledger.clear();
  turnCounter = 0;
  nextResultExtra = {};
  authenticatedFetchMock.mockClear();
  (global.fetch as jest.Mock).mockClear();
  bridgeRef.current = null;
});

async function dispatchNoteEdit(id: string, locationLabel?: string): Promise<void> {
  await act(async () => {
    await bridgeRef.current!.enqueue({
      id,
      bindingId: DRAFT_BINDING,
      args: {
        slots: {
          selectionText: 'the prior clause',
          sessionId: DOC_SESSION,
          ...(locationLabel !== undefined ? { locationLabel } : {}),
        },
      },
      documentSessionId: DOC_SESSION,
    });
    for (let i = 0; i < 10; i++) await Promise.resolve();
  });
}

describe('task 042 (FR-12): bold clause-location header on compose EDIT confirmations', () => {
  it('prepends a bold ### header when a locationLabel is present on the request slots', async () => {
    renderPane();
    await dispatchNoteEdit('note-1', 'Pg 1 · Sec 3 · Para 1 · Confidentiality');

    const confirmation = injectedMessages.find(m => m.metadata?.composeEdit);
    expect(confirmation).toBeDefined();
    expect(confirmation!.content.startsWith('### Pg 1 · Sec 3 · Para 1 · Confidentiality\n\n')).toBe(true);
    expect(confirmation!.content).toContain(COMPOSE_EDIT_CONFIRMATION);
  });

  it('falls back gracefully (no header, never "undefined") when no location is resolvable', async () => {
    renderPane();
    await dispatchNoteEdit('note-2');

    const confirmation = injectedMessages.find(m => m.metadata?.composeEdit);
    expect(confirmation).toBeDefined();
    expect(confirmation!.content.startsWith('###')).toBe(false);
    expect(confirmation!.content).not.toContain('undefined');
    // Byte-identical to the pre-042 shape — the same shape the DEF-12 forcing test locks with an
    // exact `.toBe` match for an equivalent no-location payload.
    expect(confirmation!.content).toBe(
      `${COMPOSE_EDIT_CONFIRMATION}\n\n**What I changed:** clearer indemnity language`
    );
  });

  it('treats a whitespace-only locationLabel as unresolved (graceful, not a blank header)', async () => {
    renderPane();
    await dispatchNoteEdit('note-3', '   ');

    const confirmation = injectedMessages.find(m => m.metadata?.composeEdit);
    expect(confirmation).toBeDefined();
    expect(confirmation!.content.startsWith('###')).toBe(false);
    expect(confirmation!.content).not.toContain('undefined');
  });

  it('resolves a location from the result payload when the request slots carry none (forward-compat fallback)', async () => {
    nextResultExtra = { sectionRef: '4.2' };
    renderPane();
    await dispatchNoteEdit('note-4');

    const confirmation = injectedMessages.find(m => m.metadata?.composeEdit);
    expect(confirmation).toBeDefined();
    expect(confirmation!.content.startsWith('### 4.2\n\n')).toBe(true);
  });

  it('a 041-style batch of 3 sequential edits reads as 3 visually distinct, separately-headed entries', async () => {
    renderPane();
    await dispatchNoteEdit('note-5', 'Sec 2 · Definitions');
    await dispatchNoteEdit('note-6', 'Sec 4 · Confidentiality');
    await dispatchNoteEdit('note-7'); // no location for this one — graceful, distinct from the other two

    const confirmations = injectedMessages.filter(m => m.metadata?.composeEdit);
    expect(confirmations).toHaveLength(3);
    expect(confirmations[0].content.startsWith('### Sec 2 · Definitions\n\n')).toBe(true);
    expect(confirmations[1].content.startsWith('### Sec 4 · Confidentiality\n\n')).toBe(true);
    expect(confirmations[2].content.startsWith('###')).toBe(false);
    // Each is its OWN message (a separate SprkChat bubble) — the inter-entry whitespace the spec asks
    // for is inherited from SprkChat's existing per-message layout (unaffected by this task's file
    // boundary). Distinct ledgerRefs prove these are 3 independent confirmations, not a merged block.
    const ledgerRefs = confirmations.map(c => (c.metadata!.composeEdit as { ledgerRef: string }).ledgerRef);
    expect(new Set(ledgerRefs).size).toBe(3);
  });

  it('applies the same header treatment to the whole-document revision confirmation copy', async () => {
    renderPane();
    await act(async () => {
      await bridgeRef.current!.enqueue({
        id: 'whole-doc-1',
        bindingId: DRAFT_BINDING,
        args: { slots: { selectionText: 'whole document', sessionId: DOC_SESSION, locationLabel: 'Document-wide' } },
        documentSessionId: DOC_SESSION,
        revisionScope: 'whole-document',
      });
      for (let i = 0; i < 10; i++) await Promise.resolve();
    });

    const confirmation = injectedMessages.find(m => m.metadata?.composeEdit);
    expect(confirmation).toBeDefined();
    expect(confirmation!.content.startsWith('### Document-wide\n\n')).toBe(true);
  });
});
