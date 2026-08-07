/**
 * ConversationPane.restore-attachment-chip.test.tsx — FR-D5 (task 036).
 *
 * On cold-load restore of a session that had a file attached, the client rehydrates the attachment
 * chip from the server `UploadedFiles` manifest (surfaced via `useRestoreContext().uploadedFiles`;
 * ADR-040 — the EXISTING manifest, no new store). The chip renders through the SAME host-owned
 * `FilesAttachedIndicator` the live upload path uses — NOT by seeding SprkChat's internal attachment
 * state (which would re-fire the ready-transition side-effects on a restored session).
 *
 * Gating verified:
 *   - shows only while the pane is on the restored session (chatSessionId === restoreCtx.sessionId);
 *   - hidden when the restored manifest is empty;
 *   - hidden when the pane is on a DIFFERENT session (a New Session / History reopen).
 *
 * ADR-021: the indicator renders under BOTH light and dark Fluent themes (dark-mode regression check).
 *
 * Harness mirrors ./ConversationPane.restore-session-readopt.test.tsx — mock ONLY the heavy children
 * (SprkChat prop-capture stub, useAiSession, useShellStage), render the real ConversationPane. The
 * restore context is a mutable knob so each test drives a different manifest / session.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme, type Theme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { ISprkChatProps } from '@spaarke/ui-components';

const RESTORE_SESSION = '00000000-0000-0000-0000-0000000re570';
const OTHER_SESSION = '00000000-0000-0000-0000-00000000e0e0';

interface RestoreUploadedFile {
  fileId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}
interface MockRestoreCtx {
  sessionId: string;
  conversationSummary: string | null;
  recentMessages: unknown[];
  hasStaleEntities: boolean;
  uploadedFiles: RestoreUploadedFile[];
}

// Mutable knobs the mocks below read.
let mockChatSessionId: string | null = null;
let mockRestoreCtx: MockRestoreCtx | null = null;

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    createConsumerDispatcher: () =>
      jest.fn(() => Promise.resolve({ streamId: 'stream-test', status: 'complete' as const })),
    createXrmDataService: () => ({
      createRecord: jest.fn(),
      retrieveRecord: jest.fn(),
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    }),
    SprkChat: (props: ISprkChatProps) => (
      <div data-testid="sprkchat-stub">{props.transcriptFooterSlot}</div>
    ),
  };
});

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: jest.fn(),
      getAccessToken: jest.fn(),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: mockChatSessionId,
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
  useRestoreContext: () => mockRestoreCtx,
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => null,
}));

// Import AFTER the mocks.
import { ConversationPane } from '../ConversationPane';

function renderPane(theme: Theme = webLightTheme): void {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider bus={bus}>
        <ConversationPane />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

function restoreCtxWith(files: RestoreUploadedFile[], sessionId = RESTORE_SESSION): MockRestoreCtx {
  return {
    sessionId,
    conversationSummary: null,
    recentMessages: [],
    hasStaleEntities: false,
    uploadedFiles: files,
  };
}

const ONE_FILE: RestoreUploadedFile[] = [
  { fileId: 'file-1', fileName: 'NDA.pdf', contentType: 'application/pdf', sizeBytes: 12345 },
];

beforeEach(() => {
  mockChatSessionId = null;
  mockRestoreCtx = null;
});

describe('FR-D5 — attachment chip rehydration on restore', () => {
  it('shows the attachment chip when a restored session (on this pane) has an uploaded file', () => {
    mockChatSessionId = RESTORE_SESSION;
    mockRestoreCtx = restoreCtxWith(ONE_FILE);

    renderPane();

    expect(screen.getByTestId('files-attached-indicator')).toBeInTheDocument();
    expect(screen.getByText('1 file attached')).toBeInTheDocument();
    expect(screen.getByText('NDA.pdf')).toBeInTheDocument();
  });

  it('does NOT show the chip when the restored manifest is empty', () => {
    mockChatSessionId = RESTORE_SESSION;
    mockRestoreCtx = restoreCtxWith([]);

    renderPane();

    expect(screen.queryByTestId('files-attached-indicator')).not.toBeInTheDocument();
  });

  it('does NOT show restored files once the pane is on a DIFFERENT session', () => {
    // The user reopened / started another session — restore payload is stale for THIS session.
    mockChatSessionId = OTHER_SESSION;
    mockRestoreCtx = restoreCtxWith(ONE_FILE, RESTORE_SESSION);

    renderPane();

    expect(screen.queryByTestId('files-attached-indicator')).not.toBeInTheDocument();
  });

  it('renders the rehydrated chip under the dark theme without regression (ADR-021)', () => {
    mockChatSessionId = RESTORE_SESSION;
    mockRestoreCtx = restoreCtxWith(ONE_FILE);

    renderPane(webDarkTheme);

    expect(screen.getByTestId('files-attached-indicator')).toBeInTheDocument();
    expect(screen.getByText('NDA.pdf')).toBeInTheDocument();
  });
});
