/**
 * ConversationPane — task 031 (spec FR-16(c), DEF-09) agreement-review DOCUMENT-session routing
 * forcing test.
 *
 * THE DEFECT (HUB-R1-REVIEW §4 change 3, re-verified post-021): task 030 flipped the agreement-review
 * Binding's `sprk_disposition` Informational -> Compose, so its output is now a `compose`-disposition
 * SessionOutput that `ComposeWorkspace`'s FR-04 refresh-durability effect re-materializes from
 * `GET .../compose-outputs` on its OWN document session (`state.sessionId`). But the review dispatches
 * via `chips.dispatchBinding` (bound to the CHAT session) — the WRITE and the redline-materialize READ
 * must coincide, mirroring the shipped Compose-EDIT-toolbar DEF-09 precedent
 * (`ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx`).
 *
 * THE FIX (documentSessionWaiter.ts + useConsumerChips/useAgreementReviewGate threading): the gate's
 * `dispatchReview` awaits the mounted file's REAL document session — backfilled by
 * `registerComposeActiveDocument` (the SAME reactive conduit "revise this document" already uses) — and
 * threads it as `args.sessionIdOverride` on the review's `chips.dispatchBinding` call, so the
 * `/dispatch` POST targets the DOCUMENT session, not the chat session.
 *
 * TWO DISTINCT session ids are used (same forcing-function discipline as the draft-alternative test):
 * a CHAT session (`ConversationPane`'s `chatSessionId`) and a DISTINCT DOCUMENT session, simulated via a
 * `useComposeActiveDocumentRegistration()` stub that calls back into `ConversationPane` (exactly what a
 * REAL `ComposeWorkspace` mount would do) the moment the review's `widget_load{widgetType:'compose'}`
 * seed is observed on the PaneEventBus. The REAL dispatch runs (`createConsumerDispatcher` is NOT
 * mocked); the network is mocked at the wire boundary and backed by ONE in-memory ledger KEYED BY
 * SESSION — a session-routing regression shows up as the DOCUMENT session's ledger being empty.
 *
 * @see ../ConversationPane.tsx (dispatchComposeAction / useAgreementReviewGate wiring)
 * @see ../documentSessionWaiter.ts (the pure waiter/timeout seam this test exercises end-to-end)
 * @see ./ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx (the sibling EDIT-path forcing test)
 * @see ./ConversationPane.agreement-review-gate.e2e.test.tsx (the task 021 gate wiring this test extends)
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

// ---------------------------------------------------------------------------
// Two DISTINCT session ids — the whole point of the forcing function.
// ---------------------------------------------------------------------------
const CHAT_SESSION = '00000000-0000-0000-0000-00000000c8a7';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
const SESSION_FILE_ID = 'session-file-agreement-routing-1';
const CLASSIFY_BINDING = 'binding-agreement-classify-guid';
const REVIEW_BINDING = 'binding-nda-review-guid';
const BFF = 'https://test-bff.example.com';
const DOCX_MIME = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
const FILE_NAME = 'acme-nda.pdf';

// ---------------------------------------------------------------------------
// ONE in-memory ledger, KEYED BY SESSION — mirrors the draft-alternative forcing test.
// ---------------------------------------------------------------------------
interface LedgerOutput {
  key: string;
  bindingId: string;
  turn: number;
  disposition: string;
  payload: Record<string, unknown>;
}
const ledger = new Map<string, LedgerOutput[]>();

const dispatchPostUrls: string[] = [];
const dispatchPostBodies: Array<Record<string, unknown>> = [];

function sessionFromUrl(url: string): string {
  return decodeURIComponent(url.match(/\/sessions\/([^/]+)\//)?.[1] ?? '');
}

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
      const session = sessionFromUrl(url);
      const parsedBody = init?.body ? (JSON.parse(String(init.body)) as Record<string, unknown>) : {};
      dispatchPostBodies.push(parsedBody);
      const bindingId = parsedBody.bindingId as string | undefined;

      if (bindingId === CLASSIFY_BINDING) {
        return sseCompleteResponse({
          isAgreement: true,
          composite: false,
          candidates: [{ subDomainKey: 'nda', confidence: 0.95 }],
        });
      }
      // The review (task 030's flip — disposition:'compose' in the durable ledger).
      const payload = {
        overallRisk: 'medium',
        flaggedSections: [{ sectionRef: '4.2', quotedText: 'Broad indemnity clause', explanation: 'Uncapped liability.' }],
      };
      const entry: LedgerOutput = {
        key: `${REVIEW_BINDING}@t1`,
        bindingId: REVIEW_BINDING,
        turn: 1,
        disposition: 'compose',
        payload,
      };
      ledger.set(session, [...(ledger.get(session) ?? []), entry]);
      return sseCompleteResponse(payload);
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
    const session = sessionFromUrl(url);
    return { ok: true, status: 200, json: async () => ledger.get(session) ?? [] } as unknown as Response;
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
const registerActiveDocumentRef: { current: ComposeActiveDocumentRegistration | null } = { current: null };

/**
 * Stub standing in for a REAL `ComposeWorkspace` mount: reads the bridge's active-document
 * registration delegate so the test harness can simulate "the Compose tab finished loading and
 * registered its document session" — the SAME conduit `ComposeWorkspace.tsx` calls in production.
 */
function ComposeRegistrationCapture(): null {
  registerActiveDocumentRef.current = useComposeActiveDocumentRegistration();
  return null;
}

function renderPane(): void {
  const bus = new PaneEventBus();
  bus.subscribe('workspace', (e) => {
    workspaceEvents.push(e as WorkspacePaneEvent);
    const evt = e as WorkspacePaneEvent & { widgetType?: string };
    // Simulate ComposeWorkspace mounting THIS file with the DISTINCT document session — exactly
    // what registerComposeActiveDocument's caller (ComposeWorkspace) does after loading bytes.
    if (evt.type === 'widget_load' && evt.widgetType === 'compose') {
      void registerActiveDocumentRef.current?.({
        docxBytes: new ArrayBuffer(0),
        fileName: FILE_NAME,
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
  ledger.clear();
  authenticatedFetchMock.mockClear();
  (global.fetch as jest.Mock).mockClear();
  captured.onDecorateOutboundBody = undefined;
  captured.onAttachmentReady = undefined;
  captured.onAttachmentsChanged = undefined;
  registerActiveDocumentRef.current = null;
});

describe('task 031 DEF-09: agreement-review dispatches to the DOCUMENT session (two-session forcing test)', () => {
  it('routes the review /dispatch to the DOCUMENT session — write and redline-materialize read coincide', async () => {
    renderPane();
    await driveChatUpload(FILE_NAME);

    let decorateResult: unknown;
    await act(async () => {
      decorateResult = await captured.onDecorateOutboundBody!({ message: 'review this document' });
      for (let i = 0; i < 60; i++) await Promise.resolve();
    });

    expect(decorateResult).toBeNull();

    // The classifier still targets the CHAT session (unaffected — it is not the compose-disposition
    // action; DEF-09 routing applies only to the review's own dispatch).
    const classifyUrl = dispatchPostUrls.find((u) => dispatchPostBodies[dispatchPostUrls.indexOf(u)]?.bindingId === CLASSIFY_BINDING);
    expect(classifyUrl).toContain(`/sessions/${CHAT_SESSION}/dispatch`);

    // (1) THE FIX: the review's /dispatch POST targeted the DOCUMENT session, NOT the chat session.
    const reviewDispatchIndex = dispatchPostBodies.findIndex((b) => b.bindingId === REVIEW_BINDING);
    expect(reviewDispatchIndex).toBeGreaterThanOrEqual(0);
    const reviewUrl = dispatchPostUrls[reviewDispatchIndex];
    expect(reviewUrl).toContain(`/sessions/${DOC_SESSION}/dispatch`);
    expect(reviewUrl).not.toContain(`/sessions/${CHAT_SESSION}/dispatch`);

    // Pack-binding proof still holds alongside the routing fix.
    const reviewArgs = dispatchPostBodies[reviewDispatchIndex].args as Record<string, unknown> | undefined;
    expect(reviewArgs?.subDomain).toBe('nda');
    expect(reviewArgs?.fileIds).toEqual([SESSION_FILE_ID]);

    // (2) Acceptance criterion 1 (spec FR-16(c), literal): GET compose-outputs on the DOCUMENT
    // session contains the findings output; the CHAT session's ledger has NONE.
    expect(ledger.get(DOC_SESSION)).toHaveLength(1);
    expect(ledger.get(DOC_SESSION)![0].disposition).toBe('compose');
    expect(ledger.get(CHAT_SESSION) ?? []).toHaveLength(0);
  });
});
