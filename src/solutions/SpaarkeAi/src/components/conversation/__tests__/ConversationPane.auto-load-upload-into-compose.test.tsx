/**
 * ConversationPane.auto-load-upload-into-compose.test.tsx — spaarkeai-compose-r2 FIX #7.
 *
 * Owner decision (settled): when the user uploads a file in the ASSISTANT (a chat attachment), it
 * should AUTO-load into the Compose tab — no need to say "revise this document" and no chip click.
 *
 * This drives the REAL ConversationPane over a real PaneEventBus + ComposeActionBridge (network mocked
 * at the wire boundary), completes a chat attachment upload, and asserts that a compose upload-seed
 * mount (`widget_load` with widgetType 'compose' + `compose.upload`) is dispatched automatically —
 * with NO revise message sent and no chip clicked. Also asserts the same file auto-loads exactly once
 * (dedup), so a single upload does not stack duplicate mounts.
 *
 * @see ./ConversationPane.revise-this-document.e2e.test.tsx (chat-upload lifecycle harness this mirrors)
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
import type { ISprkChatProps } from '@spaarke/ui-components';
import { ComposeActionBridgeProvider } from '@spaarke/compose-components/context/composeActionBridge';

const CHAT_SESSION = '00000000-0000-0000-0000-00000000c8a7';
const SESSION_FILE_ID = 'session-file-active-source-123';
const BFF = 'https://test-bff.example.com';
const DOCX_MIME = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

const authenticatedFetchMock = jest.fn(async (url: string) => {
  if (url.includes('/documents')) {
    return { ok: true, status: 200, json: async () => ({ documentId: SESSION_FILE_ID }) } as unknown as Response;
  }
  if (url.includes('/compose/active-document')) {
    return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

const captured: {
  onAttachmentReady?: (a: unknown) => void;
  onAttachmentsChanged?: (chips: unknown[]) => void;
} = {};

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      const p = props as ISprkChatProps & {
        onAttachmentReady?: (a: unknown) => void;
        onAttachmentsChanged?: (chips: unknown[]) => void;
      };
      captured.onAttachmentReady = p.onAttachmentReady;
      captured.onAttachmentsChanged = p.onAttachmentsChanged;
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
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

function composeUploadOpens(): WorkspacePaneEvent[] {
  return workspaceEvents.filter((e) => e.type === 'widget_load' && e.widgetType === 'compose');
}

/** Drives the chat attachment lifecycle so the auto-promote effect POSTs /documents (→ upload back-fill). */
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
  authenticatedFetchMock.mockClear();
  captured.onAttachmentReady = undefined;
  captured.onAttachmentsChanged = undefined;
});

describe('FIX #7: an Assistant file upload auto-loads into Compose', () => {
  it('auto-dispatches the compose upload-seed mount when a chat file finishes uploading (no revise, no chip)', async () => {
    renderPane();

    await driveChatUpload('brief.docx');

    const opens = composeUploadOpens();
    expect(opens).toHaveLength(1); // exactly one mount — deduped, no stacking
    const compose = (opens[0].widgetData as { compose?: { upload?: Record<string, unknown> } }).compose ?? {};
    expect(compose.upload).toBeDefined();
    expect(compose.upload!.sessionFileId).toBe(SESSION_FILE_ID);
    expect(compose.upload!.sessionId).toBe(CHAT_SESSION);
    expect(compose.upload!.fileName).toBe('brief.docx');
  });
});
