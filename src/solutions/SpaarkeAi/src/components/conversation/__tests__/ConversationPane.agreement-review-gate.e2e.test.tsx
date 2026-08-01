/**
 * ConversationPane.agreement-review-gate.e2e.test.tsx — task 021 (spec FR-07/FR-08/FR-09
 * interactive orientation + confirmation gate).
 *
 * Drives the REAL ConversationPane over a real PaneEventBus with the network mocked at the
 * wire boundary (createConsumerDispatcher is NOT mocked — the /dispatch URL is real), mirroring
 * ./ConversationPane.revise-this-document.e2e.test.tsx's harness. Proves the TEXT-path
 * interception + end-to-end wiring (the pure branch logic itself is covered in isolation by
 * agreementReviewRouting.test.ts + useAgreementReviewGate.test.ts):
 *   (a) An uploaded, unclassified document + "review this document" CANCELS the agent turn
 *       (onDecorateOutboundBody returns null), dispatches the classifier, then auto-proceeds
 *       into the review dispatch (near-certain confidence) — asserting the review dispatch's
 *       wire body carries the classified `subDomain` slot (pack-binding proof).
 *   (b) Negative criterion: "review this" with NO uploaded/active document does NOT cancel the
 *       agent turn (falls through unchanged) and posts NO dispatch at all.
 *
 * task 031 update (DEF-09 routing): the review dispatch now AWAITS the mounted file's REAL
 * document session (documentSessionWaiter.ts) before calling `chips.dispatchBinding`, so this
 * harness mounts a `ComposeRegistrationCapture` stub that simulates `ComposeWorkspace` calling back
 * into `registerComposeActiveDocument` the moment the review's `widget_load{widgetType:'compose'}`
 * seed is observed — exactly the production topology (WorkspacePane is always mounted alongside
 * ConversationPane). Without it, `awaitDocumentSessionId` would degrade to `null` only after its
 * (real, 8s) timeout — outside this test's microtask-flush window. See
 * ./ConversationPane.agreement-review-session-routing.e2e.test.tsx for the dedicated DEF-09
 * two-session forcing test this update is a lighter-weight sibling of.
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
  useComposeActiveDocumentRegistration,
  type ComposeActiveDocumentRegistration,
} from '@spaarke/compose-components/context/composeActionBridge';

const CHAT_SESSION = '00000000-0000-0000-0000-00000000c8a7';
// task 031: a DISTINCT document session the ComposeRegistrationCapture stub back-fills — not
// asserted here (see the dedicated DEF-09 routing test), just enough so awaitDocumentSessionId
// resolves promptly instead of degrading via its (real) timeout.
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
const SESSION_FILE_ID = 'session-file-agreement-123';
const CLASSIFY_BINDING = 'binding-agreement-classify-guid';
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
        return sseCompleteResponse({
          isAgreement: true,
          composite: false,
          candidates: [{ subDomainKey: 'nda', confidence: 0.95 }],
          reasoning: 'Mutual confidentiality obligations throughout.',
        });
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

const workspaceEvents: WorkspacePaneEvent[] = [];
let bus: PaneEventBus;

// task 031 (DEF-09 routing) — stub standing in for a REAL ComposeWorkspace mount: reads the
// bridge's active-document registration delegate so this harness can simulate "the Compose tab
// finished loading and registered its document session" the moment the review's compose widget_load
// seed is observed (mirrors ConversationPane.agreement-review-session-routing.e2e.test.tsx).
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
        fileName: 'acme-nda.pdf',
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
});

describe('task 021: "review this document" — untyped upload + interactive classify + auto-proceed gate', () => {
  it('cancels the agent turn, classifies, then auto-proceeds the review bound to the classified pack', async () => {
    renderPane();
    await driveChatUpload('acme-nda.pdf');

    let decorateResult: unknown;
    await act(async () => {
      decorateResult = await captured.onDecorateOutboundBody!({ message: 'review this document' });
      // Two REAL sequential SSE round-trips (classify, then the review) each need several
      // microtask hops (readSseStream -> parseSseEvent -> the runGate/dispatchReview await chain)
      // — loop generously rather than guess a tight bound.
      for (let i = 0; i < 60; i++) await Promise.resolve();
    });

    // The agent turn is CANCELLED (return null) — the gate orchestrates classify -> auto-proceed.
    expect(decorateResult).toBeNull();

    // Two /dispatch calls: the classifier, then the review — both real network POSTs.
    const bindingIdsDispatched = dispatchPostBodies.map((b) => b.bindingId);
    expect(bindingIdsDispatched).toContain(CLASSIFY_BINDING);
    expect(bindingIdsDispatched).toContain(REVIEW_BINDING);

    // Pack-binding proof: the review dispatch's wire body carries the classified subDomain slot.
    const reviewBody = dispatchPostBodies.find((b) => b.bindingId === REVIEW_BINDING)!;
    const args = reviewBody.args as Record<string, unknown> | undefined;
    expect(args?.subDomain).toBe('nda');
    expect(args?.fileIds).toEqual([SESSION_FILE_ID]);

    // Orientation proof (FR-07): the Compose mount seed carries activeWorkType='agreement-analysis'
    // — the SAME ComposeWidgetSeed field task 041 (hub) already wired end-to-end into
    // getToolsForSurface (ComposeWorkspace.activeWorkType.test.tsx / ComposeEditor.activeWorkType.
    // test.tsx cover that downstream half; this proves THIS gate correctly sets the seed).
    const composeOpen = [...workspaceEvents]
      .reverse()
      .find((e) => e.type === 'widget_load' && (e as WorkspacePaneEvent & { widgetType?: string }).widgetType === 'compose');
    expect(composeOpen).toBeDefined();
    const seed = (composeOpen as WorkspacePaneEvent & { widgetData?: { compose?: Record<string, unknown> } })
      .widgetData?.compose;
    expect(seed?.activeWorkType).toBe('agreement-analysis');
  });
});

describe('task 021 negative criterion: a bare "review" with NO attached/target doc never fires the gate', () => {
  it('falls through unchanged to the normal agent turn — no classify dispatch, no cancellation', async () => {
    renderPane();
    // No upload driven this time.

    let decorateResult: unknown;
    await act(async () => {
      decorateResult = await captured.onDecorateOutboundBody!({ message: 'can you review that for me' });
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    // Falls through to handleDecorateOutboundBody, which returns the body unchanged (non-null).
    expect(decorateResult).not.toBeNull();
    expect(dispatchPostBodies).toHaveLength(0);
  });
});
