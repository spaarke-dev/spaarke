/**
 * ConversationPane.review-chip-depth-ask.e2e.test.tsx — UAT round-3 (2026-08-04) item #7.
 *
 * Task 070 (UAT round-2) wired the Quick/Thorough depth choice into the interactive gate's
 * three branches (confirm / auto-proceed / explicit) + composite + the wizard door, but
 * deliberately left the DIRECT consumer-chip/card click path
 * (`ConversationPane.handleReviewNda`, the "Review an NDA" chip that renders after the CLS-CHAT
 * docType classifier flags an upload) untouched — see `notes/070-execution-notes.md`'s "Scoping
 * decision" section. UAT round-3 owner feedback: clicking that chip dispatched the review
 * IMMEDIATELY with the default depth, with no ask.
 *
 * This test drives the REAL ConversationPane over a real PaneEventBus with the network mocked
 * at the wire boundary (mirrors ./ConversationPane.agreement-review-gate.e2e.test.tsx's
 * harness), proving END TO END:
 *   1. An uploaded file that the CLS-CHAT docType classifier flags as an NDA (the
 *      `event_classification` SSE event on `/events/document-uploaded`) renders the "Review an
 *      NDA" chip.
 *   2. Clicking that chip does NOT dispatch the review immediately — it inserts the SAME
 *      one-turn Quick/Thorough depth-choice machinery task 070 built for the gate's other
 *      branches (item #7's "reuse, don't rebuild" instruction).
 *   3. Answering the depth chip dispatches the review with `reviewDepth` in the wire body, and
 *      — the item #7-specific negative proof — NO `subDomain` slot (the classic chip/card path
 *      never classified a type; the chip click IS the type commitment, unchanged pre-070 shape).
 */
import '@testing-library/jest-dom';
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextEncoder === 'undefined') (global as any).TextEncoder = NodeTextEncoder;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextDecoder === 'undefined') (global as any).TextDecoder = NodeTextDecoder;
import React, { act } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';
import type { ISprkChatProps, IChatMessage } from '@spaarke/ui-components';
import {
  ComposeActionBridgeProvider,
  useComposeActiveDocumentRegistration,
  type ComposeActiveDocumentRegistration,
} from '@spaarke/compose-components/context/composeActionBridge';

const CHAT_SESSION = '00000000-0000-0000-0000-00000000c7b1';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c71';
const SESSION_FILE_ID = 'session-file-nda-chip-456';
const REVIEW_BINDING = 'binding-nda-review-guid';
const BFF = 'https://test-bff.example.com';
const DOCX_MIME = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

const dispatchPostUrls: string[] = [];
const dispatchPostBodies: Array<Record<string, unknown>> = [];

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

/** The `/events/document-uploaded` SSE shape (DocumentUploadedEventStream.ts) — a DIFFERENT
 * envelope grammar ({type, data}) than the /dispatch endpoint's ({type, done, result}). */
function eventSseResponse(events: Array<{ type: string; data?: Record<string, unknown> }>): Response {
  const lines = events.map((e) => `data: ${JSON.stringify(e)}\n\n`).join('') + `data: ${JSON.stringify({ type: 'done' })}\n\n`;
  const chunk = new TextEncoder().encode(lines);
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
    if (url.includes('/events/document-uploaded')) {
      // The CLS-CHAT docType classifier flags this upload as an NDA — the SAME event
      // `handleNdaClassified` (ConversationPane.tsx) consumes to populate `ndaReviewFile`. The
      // client-only "Review an NDA" local chip is APPENDED (useConsumerChips.acceptChips,
      // R5-1) only when a non-empty SERVER chip array arrives via one of the three carrier
      // events — mirror production's follow-on `chips` event (e.g. a generic "Ask a
      // follow-up" next-step) so the merge actually happens, exactly like the real
      // EventRuleSseContract.cs flow.
      return eventSseResponse([
        {
          type: 'event_classification',
          data: { fileId: SESSION_FILE_ID, fileName: 'AppligentNDA_Signed.docx', docType: 'nda', confidence: 0.94 },
        },
        {
          type: 'chips',
          data: {
            chips: [{ targetBindingId: 'binding-ask-followup', chipLabel: 'Ask a follow-up', requiresAttachments: false }],
          },
        },
      ]);
    }
    if (url.includes('/dispatch')) {
      dispatchPostUrls.push(url);
      const parsedBody = init?.body ? (JSON.parse(String(init.body)) as Record<string, unknown>) : {};
      dispatchPostBodies.push(parsedBody);
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
  onAttachmentReady?: (a: unknown) => void;
  onAttachmentsChanged?: (chips: unknown[]) => void;
} = {};

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
        onAttachmentReady?: (a: unknown) => void;
        onAttachmentsChanged?: (chips: unknown[]) => void;
      };
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
        props.onLocalMessageInjected?.();
      });
      return <div data-testid="sprkchat-stub">{props.transcriptFooterSlot}</div>;
    },
  };
});

// Import AFTER mocks.
import { ConversationPane } from '../ConversationPane';
import { LOCAL_CHIP } from '../localActionChips';

const workspaceEvents: WorkspacePaneEvent[] = [];
let bus: PaneEventBus;

// task 031 (DEF-09 routing) stub — same pattern as the sibling gate e2e test.
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
        fileName: 'AppligentNDA_Signed.docx',
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
    // The upload -> /documents promote -> queueDocumentUploadedEvent -> fire() ->
    // /events/document-uploaded SSE round-trip needs several microtask hops.
    for (let i = 0; i < 40; i++) await Promise.resolve();
  });
}

beforeEach(() => {
  workspaceEvents.length = 0;
  dispatchPostUrls.length = 0;
  dispatchPostBodies.length = 0;
  authenticatedFetchMock.mockClear();
  (global.fetch as jest.Mock).mockClear();
  captured.onAttachmentReady = undefined;
  captured.onAttachmentsChanged = undefined;
  registerActiveDocumentRef.current = null;
});

describe('UAT round-3 item #7: "Review an NDA" chip click now asks Quick/Thorough before dispatching', () => {
  it('clicking the chip shows the depth-choice turn (no dispatch yet); answering it dispatches with reviewDepth and NO subDomain slot', async () => {
    renderPane();
    await driveChatUpload('AppligentNDA_Signed.docx');

    // The docType classifier flagged this upload as an NDA — the "Review an NDA" chip renders.
    const ndaChip = screen.getByTestId(`consumer-chip-${LOCAL_CHIP.ndaReview}`);
    expect(ndaChip).toBeInTheDocument();

    await act(async () => {
      fireEvent.click(ndaChip);
      for (let i = 0; i < 20; i++) await Promise.resolve();
    });

    // ITEM #7: no immediate dispatch — the depth-choice turn renders instead (the SAME chip ids
    // task 070 established for every other branch — reused, not rebuilt).
    expect(dispatchPostBodies.filter((b) => b.bindingId === REVIEW_BINDING)).toHaveLength(0);
    const depthChip = screen.getByTestId(`consumer-chip-${LOCAL_CHIP.agreementReviewDepthThorough}`);
    expect(depthChip).toBeInTheDocument();

    await act(async () => {
      fireEvent.click(depthChip);
      for (let i = 0; i < 30; i++) await Promise.resolve();
    });

    const reviewBody = dispatchPostBodies.find((b) => b.bindingId === REVIEW_BINDING);
    expect(reviewBody).toBeDefined();
    // Wire shape: POST body is `{ bindingId, args }` where `args` IS the slots object directly
    // (dispatchConsumer.ts: `body: { bindingId, args: args?.slots ?? {} }`).
    const args = reviewBody!.args as Record<string, unknown> | undefined;
    expect(args).toMatchObject({ fileIds: [SESSION_FILE_ID], reviewDepth: 'thorough' });
    // Item #7 negative proof: the classic chip/card path never classified a type — the chip click
    // IS the type commitment, unchanged pre-070 wire shape. No subDomain slot, ever.
    expect(args?.subDomain).toBeUndefined();
  });

  it('picking Quick dispatches with reviewDepth:"quick"', async () => {
    renderPane();
    await driveChatUpload('AppligentNDA_Signed.docx');

    const ndaChip = screen.getByTestId(`consumer-chip-${LOCAL_CHIP.ndaReview}`);
    await act(async () => {
      fireEvent.click(ndaChip);
      for (let i = 0; i < 20; i++) await Promise.resolve();
    });

    const depthChip = screen.getByTestId(`consumer-chip-${LOCAL_CHIP.agreementReviewDepthQuick}`);
    await act(async () => {
      fireEvent.click(depthChip);
      for (let i = 0; i < 30; i++) await Promise.resolve();
    });

    const reviewBody = dispatchPostBodies.find((b) => b.bindingId === REVIEW_BINDING);
    const args = reviewBody!.args as Record<string, unknown> | undefined;
    expect(args).toMatchObject({ fileIds: [SESSION_FILE_ID], reviewDepth: 'quick' });
  });

  it('the file still opens in Compose immediately on the chip click, before the depth question is answered', async () => {
    renderPane();
    await driveChatUpload('AppligentNDA_Signed.docx');

    const ndaChip = screen.getByTestId(`consumer-chip-${LOCAL_CHIP.ndaReview}`);
    await act(async () => {
      fireEvent.click(ndaChip);
      for (let i = 0; i < 20; i++) await Promise.resolve();
    });

    // mountFileInCompose fired at click time (a workspace widget_load{widgetType:'compose'}), well
    // before any depth chip is clicked — matches handleReviewNda's pre-existing "open immediately"
    // behavior, which item #7 does not change.
    const composeOpen = workspaceEvents.find(
      (e) => e.type === 'widget_load' && (e as WorkspacePaneEvent & { widgetType?: string }).widgetType === 'compose'
    );
    expect(composeOpen).toBeDefined();
  });
});
