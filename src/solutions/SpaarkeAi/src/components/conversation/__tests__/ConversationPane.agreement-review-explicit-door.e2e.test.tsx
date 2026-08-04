/**
 * ConversationPane.agreement-review-explicit-door.e2e.test.tsx — task 023 (spec FR-09 explicit
 * door; design Lens 3d "hint authoritative").
 *
 * Sibling of ./ConversationPane.agreement-review-gate.e2e.test.tsx (task 021's classifier-door
 * e2e test) — same harness, but with `useComposeLaunch()` mocked to return a non-null
 * `subDomain` (the wizard / deep-link / open-existing envelope, task 022). Drives the REAL
 * ConversationPane over a real PaneEventBus with the network mocked at the wire boundary. Proves:
 *   (a) "explicit-wins": an uploaded document + "review this document" on an ALREADY-oriented
 *       session CANCELS the agent turn, dispatches the review DIRECTLY bound to the explicit
 *       subDomain, and issues NO classifier /dispatch call synchronously before that review
 *       dispatch — no gate, no confirm chips, no re-route.
 *   (b) "mismatch-warns": when the (still non-blocking, background) classifier sanity check
 *       disagrees with the explicit choice at high confidence, an informational notice message is
 *       injected — the review itself is still bound to the EXPLICIT choice, never re-routed.
 */
import '@testing-library/jest-dom';
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextEncoder === 'undefined') (global as any).TextEncoder = NodeTextEncoder;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextDecoder === 'undefined') (global as any).TextDecoder = NodeTextDecoder;
import React, { act } from 'react';
import { render, waitFor, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';
import type { ISprkChatProps, IChatMessage } from '@spaarke/ui-components';
import {
  ComposeActionBridgeProvider,
  useComposeActiveDocumentRegistration,
  type ComposeActiveDocumentRegistration,
} from '@spaarke/compose-components/context/composeActionBridge';

const CHAT_SESSION = '00000000-0000-0000-0000-00000000e8a7';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0e07';
const SESSION_FILE_ID = 'session-file-explicit-123';
const CLASSIFY_BINDING = 'binding-agreement-classify-guid';
const REVIEW_BINDING = 'binding-nda-review-guid';
const BFF = 'https://test-bff.example.com';
const DOCX_MIME = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

const dispatchPostUrls: string[] = [];
const dispatchPostBodies: Array<Record<string, unknown>> = [];

// Toggled per-test — feeds the classifier's SSE response. Default: agrees with the explicit
// choice (employment) so the baseline "explicit-wins" test never trips the mismatch notice.
let classifyResponse: Record<string, unknown> = {
  isAgreement: true,
  composite: false,
  candidates: [{ subDomainKey: 'employment', confidence: 0.95 }],
};

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
      const parsedBody = init?.body ? (JSON.parse(String(init.body)) as Record<string, unknown>) : {};
      dispatchPostBodies.push(parsedBody);
      const bindingId = parsedBody.bindingId as string | undefined;
      if (bindingId === CLASSIFY_BINDING) {
        return sseCompleteResponse(classifyResponse);
      }
      return sseCompleteResponse({ overallRisk: 'low', flaggedSections: [] });
    }
    return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
  }) as unknown as typeof fetch;
});
afterAll(() => {
  global.fetch = originalFetch;
});

const authenticatedFetchMock = jest.fn(async (url: string) => {
  if (url.includes('/api/ai/capabilities')) {
    return {
      ok: true,
      status: 200,
      json: async () => ({
        capabilities: [
          {
            bindingId: CLASSIFY_BINDING,
            consumerType: 'agreement-classify',
            consumerCode: 'default',
            displayLabel: 'Classifies an uploaded document into an agreement sub-domain.',
            surfaces: ['assistant'],
            launchArgsSchemaJson: '{"type":"object"}',
          },
          {
            bindingId: REVIEW_BINDING,
            consumerType: 'nda-review',
            consumerCode: 'default',
            displayLabel: 'Review an uploaded agreement.',
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
}));

// task 023 — the explicit door's entire premise: `useComposeLaunch()` returns a non-null
// `subDomain` (the wizard / deep-link / open-existing envelope). Mocked at the deep-import path
// ConversationPane itself uses.
jest.mock('@spaarke/compose-components/context/composeLaunchContext', () => ({
  useComposeLaunch: () => ({ subDomain: 'employment' }),
}));

// task 021 — no Xrm.WebApi host context in this jsdom test; the registry read degrades to []
// (the gate's own graceful-degrade path: global 0.85 threshold + key-derived display names).
jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  const ReactActual = jest.requireActual('react') as typeof import('react');
  return {
    ...actual,
    createXrmDataService: () => ({
      createRecord: jest.fn(),
      retrieveRecord: jest.fn(),
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    }),
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

// Import AFTER mocks.
import { ConversationPane } from '../ConversationPane';
// task 070 (UAT2 review-depth selector): the TEXT-door explicit bind (no reviewDepth supplied by
// the caller) now inserts ONE depth-choice turn before dispatching — this harness clicks the real
// `<ConsumerChips>` button (rendered inside the stubbed SprkChat's `transcriptFooterSlot`).
import { LOCAL_CHIP } from '../localActionChips';

const workspaceEvents: WorkspacePaneEvent[] = [];
let bus: PaneEventBus;

const registerActiveDocumentRef: { current: ComposeActiveDocumentRegistration | null } = { current: null };
function ComposeRegistrationCapture(): null {
  registerActiveDocumentRef.current = useComposeActiveDocumentRegistration();
  return null;
}

function renderPane() {
  bus = new PaneEventBus();
  bus.subscribe('workspace', (e) => {
    workspaceEvents.push(e as WorkspacePaneEvent);
    const evt = e as WorkspacePaneEvent & { widgetType?: string };
    if (evt.type === 'widget_load' && evt.widgetType === 'compose') {
      void registerActiveDocumentRef.current?.({
        docxBytes: new ArrayBuffer(0),
        fileName: 'acme.pdf',
        documentSessionId: DOC_SESSION,
      });
    }
  });
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ComposeActionBridgeProvider>
          <ConversationPane />
          <ComposeRegistrationCapture />
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

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
  registerActiveDocumentRef.current = null;
  classifyResponse = {
    isAgreement: true,
    composite: false,
    candidates: [{ subDomainKey: 'employment', confidence: 0.95 }],
  };
});

describe('task 023: explicit-wins — an already-oriented session binds the pack deterministically', () => {
  it('cancels the agent turn, shows ONE depth-choice turn (task 070 — no double-ask, no type gate/chips), then dispatches the review DIRECTLY bound to the explicit subDomain + picked depth', async () => {
    renderPane();
    await driveChatUpload('acme.pdf');

    let decorateResult: unknown;
    await act(async () => {
      decorateResult = await captured.onDecorateOutboundBody!({ message: 'review this document' });
      for (let i = 0; i < 60; i++) await Promise.resolve();
    });

    // The agent turn is CANCELLED (return null) — the explicit door orchestrates the dispatch.
    expect(decorateResult).toBeNull();

    // No review dispatch yet — task 070 inserts ONE depth-choice turn (Quick/Thorough) before the
    // TEXT-door's explicit bind fires (no classifier gate, no type question — depth only).
    expect(dispatchPostBodies.some((b) => b.bindingId === REVIEW_BINDING)).toBe(false);
    const depthChip = screen.getByTestId(`consumer-chip-${LOCAL_CHIP.agreementReviewDepthThorough}`);
    expect(depthChip).toBeInTheDocument();

    await act(async () => {
      fireEvent.click(depthChip);
      for (let i = 0; i < 30; i++) await Promise.resolve();
    });

    // The review dispatch's wire body carries the EXPLICIT subDomain (employment), not a
    // classifier guess — plus the picked reviewDepth (task 070).
    const reviewBody = dispatchPostBodies.find((b) => b.bindingId === REVIEW_BINDING);
    expect(reviewBody).toBeDefined();
    const args = reviewBody!.args as Record<string, unknown> | undefined;
    expect(args?.subDomain).toBe('employment');
    expect(args?.reviewDepth).toBe('thorough');
    expect(args?.fileIds).toEqual([SESSION_FILE_ID]);

    // Orientation proof (mirrors task 021's e2e proof): the Compose mount seed still carries
    // activeWorkType='agreement-analysis'.
    const composeOpen = [...workspaceEvents]
      .reverse()
      .find((e) => e.type === 'widget_load' && (e as WorkspacePaneEvent & { widgetType?: string }).widgetType === 'compose');
    expect(composeOpen).toBeDefined();
    const seed = (composeOpen as WorkspacePaneEvent & { widgetData?: { compose?: Record<string, unknown> } })
      .widgetData?.compose;
    expect(seed?.activeWorkType).toBe('agreement-analysis');
  });
});

describe('task 023: mismatch-warns — a high-confidence classifier disagreement never re-routes, only notices', () => {
  it('dispatches the review under the EXPLICIT key (after the depth-choice turn), then injects an informational mismatch notice', async () => {
    // The background sanity-check classifier disagrees at high confidence.
    classifyResponse = {
      isAgreement: true,
      composite: false,
      candidates: [{ subDomainKey: 'nda', confidence: 0.93 }],
    };
    renderPane();
    await driveChatUpload('acme.pdf');

    await act(async () => {
      await captured.onDecorateOutboundBody!({ message: 'review this document' });
      for (let i = 0; i < 60; i++) await Promise.resolve();
    });

    // task 070: the depth-choice turn is showing; the sanity-check classifier already fired in
    // parallel (non-blocking) — answer the depth choice to actually dispatch the review.
    const depthChip = screen.getByTestId(`consumer-chip-${LOCAL_CHIP.agreementReviewDepthThorough}`);
    await act(async () => {
      fireEvent.click(depthChip);
      for (let i = 0; i < 30; i++) await Promise.resolve();
    });

    // The review is STILL bound to the explicit choice — never re-routed to the classifier's "nda".
    const reviewBody = dispatchPostBodies.find((b) => b.bindingId === REVIEW_BINDING);
    const args = reviewBody!.args as Record<string, unknown> | undefined;
    expect(args?.subDomain).toBe('employment');

    // The background sanity-check classifier DID run (non-blocking, alongside the review). It is a
    // SEPARATE chain from the review dispatch, so poll (rather than guess a fixed tick count) for
    // its own SSE round-trip + the resulting enqueueAssistantMessage to settle.
    await waitFor(() => expect(dispatchPostBodies.some((b) => b.bindingId === CLASSIFY_BINDING)).toBe(true), {
      timeout: 5000,
    });

    // An informational mismatch notice was injected (never a chip/gate — plain assistant message).
    // This jsdom harness has no Xrm.WebApi host context, so the registry read degrades to []
    // (mirrors the classifier-door e2e test's own documented convention) — displayNameFor's
    // key-derived fallback renders "Nda" (title case), not the registry's real "NDA" — hence the
    // case-insensitive match here.
    await waitFor(
      () => expect(injectedMessages.some((m) => typeof m.content === 'string' && /\bnda\b/i.test(m.content))).toBe(true),
      { timeout: 5000 }
    );
  });
});
