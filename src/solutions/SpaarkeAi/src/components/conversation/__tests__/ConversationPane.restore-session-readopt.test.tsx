/**
 * ConversationPane.restore-session-readopt.test.tsx — UAT round-6 item #15b.
 *
 * On cold-load restore of a persisted home-surface Compose tab (item #13), WorkspacePane dispatches
 * `widget_load{widgetType:'compose'}` threading the tab's `composeSessionId` — but NO `analysisId`
 * (the direct-Compose door is UNBOUND, unlike the task-033 wizard hand-off which always carries an
 * analysisId). Before this fix the Assistant pane cold-started ("back at the Review an NDA step")
 * because its own session (persisted via AiSessionProvider's sessionStorage) can be lost across the
 * code-page iframe teardown a same-tab MDA navigation performs.
 *
 * This pane's workspace-channel listener now RE-ADOPTS that restored session — the SAME
 * `handleSelectHistorySession` mechanism the History menu / hub-grid `session_switch` / task-033
 * wizard hand-off all use — so the transcript CONTINUES on return.
 *
 * GUARD (never clobber an active conversation the user started after returning): adopt AT MOST once
 * per mount, only when the pane is on a FRESH/DEFAULT session — the current session id differs from
 * the restored one AND the transcript is empty.
 *
 * Harness mirrors ./ConversationPane.consumer-chips.test.tsx (mock ONLY the heavy children —
 * SprkChat prop-capture stub, useAiSession, useShellStage — render the real ConversationPane). The
 * SprkChat stub additionally drives `onMessagesChange` with a configurable count so the guard's
 * empty-transcript check is exercisable.
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { render } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';
import type { ISprkChatProps } from '@spaarke/ui-components';

const RESTORE_SESSION = '00000000-0000-0000-0000-0000000re570';
const OTHER_SESSION = '00000000-0000-0000-0000-00000000e0e0';

// Mutable session + transcript-length knobs the mocks below read.
let mockChatSessionId: string | null = null;
let driveMessageCount = 0;
const setChatSessionIdMock = jest.fn((id: string) => {
  mockChatSessionId = id;
});

const dispatchConsumerSpy = jest.fn(() =>
  Promise.resolve({ streamId: 'stream-test', status: 'complete' as const })
);

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  const ReactActual = jest.requireActual('react') as typeof import('react');
  return {
    ...actual,
    createConsumerDispatcher: () => dispatchConsumerSpy,
    createXrmDataService: () => ({
      createRecord: jest.fn(),
      retrieveRecord: jest.fn(),
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    }),
    SprkChat: (props: ISprkChatProps) => {
      // Drive the transcript length once on mount so the #15b guard's empty-transcript check is
      // observable (default 0 = a fresh session; >0 = an active conversation the user is mid-way through).
      ReactActual.useEffect(() => {
        const msgs = Array.from({ length: driveMessageCount }, (_, i) => ({
          role: 'User' as const,
          content: `m${i}`,
          timestamp: new Date().toISOString(),
        })) as unknown as Parameters<NonNullable<ISprkChatProps['onMessagesChange']>>[0];
        props.onMessagesChange?.(msgs);
        // eslint-disable-next-line react-hooks/exhaustive-deps
      }, []);
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
      authenticatedFetch: jest.fn(),
      getAccessToken: jest.fn(),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: mockChatSessionId,
      setChatSessionId: setChatSessionIdMock,
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

// Import AFTER the mocks.
import { ConversationPane } from '../ConversationPane';

function renderPane(): { bus: PaneEventBus } {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ConversationPane />
      </PaneEventBusProvider>
    </FluentProvider>
  );
  return { bus };
}

/** WorkspacePane's item-#13 restore seed: a compose open threading `composeSessionId`, NO `analysisId`. */
function restoreSeedEvent(composeSessionId: string): WorkspacePaneEvent {
  return {
    type: 'widget_load',
    widgetType: 'compose',
    widgetData: {
      compose: {
        upload: { sessionFileId: 'file-restore-1', fileName: 'Restored.docx' },
        fileName: 'Restored.docx',
        composeSessionId,
      },
    },
    displayName: 'Restored.docx',
  } as unknown as WorkspacePaneEvent;
}

async function dispatchAndFlush(bus: PaneEventBus, event: WorkspacePaneEvent): Promise<void> {
  await act(async () => {
    bus.dispatch('workspace', event);
    for (let i = 0; i < 10; i++) await Promise.resolve();
  });
}

beforeEach(() => {
  mockChatSessionId = null;
  driveMessageCount = 0;
  setChatSessionIdMock.mockClear();
  dispatchConsumerSpy.mockClear();
});

describe('UAT round-6 item #15b — cold-load restore re-adopts the persisted Compose session', () => {
  it('adopts the restored session when the Assistant is on a fresh/default session (transcript continues)', async () => {
    mockChatSessionId = null; // cold Assistant (its own session was lost across the iframe teardown)
    driveMessageCount = 0; // fresh transcript
    const { bus } = renderPane();

    await dispatchAndFlush(bus, restoreSeedEvent(RESTORE_SESSION));

    // Adopted via the SAME session-switch mechanism the History menu uses.
    expect(setChatSessionIdMock).toHaveBeenCalledWith(RESTORE_SESSION);
  });

  it('does NOT clobber an active conversation the user already started after returning (non-empty transcript)', async () => {
    mockChatSessionId = OTHER_SESSION; // the user is mid-way through a DIFFERENT conversation
    driveMessageCount = 2; // …with messages
    const { bus } = renderPane();
    // Let the SprkChat stub's onMessagesChange land (chatMessageCount → 2).
    await act(async () => {
      for (let i = 0; i < 5; i++) await Promise.resolve();
    });

    await dispatchAndFlush(bus, restoreSeedEvent(RESTORE_SESSION));

    // Guard held: the restore did NOT hijack the active conversation.
    expect(setChatSessionIdMock).not.toHaveBeenCalledWith(RESTORE_SESSION);
  });

  it('is a no-op when the Assistant already resumed the SAME restored session (id equality)', async () => {
    mockChatSessionId = RESTORE_SESSION; // AiSessionProvider happened to resume the review session itself
    driveMessageCount = 0;
    const { bus } = renderPane();

    await dispatchAndFlush(bus, restoreSeedEvent(RESTORE_SESSION));

    // Already on it → no redundant adopt/remount.
    expect(setChatSessionIdMock).not.toHaveBeenCalledWith(RESTORE_SESSION);
  });

  it('adopts at most once per mount (a duplicate restore seed does not re-adopt)', async () => {
    mockChatSessionId = null;
    driveMessageCount = 0;
    const { bus } = renderPane();

    await dispatchAndFlush(bus, restoreSeedEvent(RESTORE_SESSION));
    await dispatchAndFlush(bus, restoreSeedEvent(RESTORE_SESSION));

    const adoptCalls = setChatSessionIdMock.mock.calls.filter((c) => c[0] === RESTORE_SESSION);
    expect(adoptCalls).toHaveLength(1);
  });

  it('NEGATIVE — a plain compose open (no composeSessionId, no analysisId) never adopts', async () => {
    mockChatSessionId = null;
    driveMessageCount = 0;
    const { bus } = renderPane();

    await dispatchAndFlush(bus, {
      type: 'widget_load',
      widgetType: 'compose',
      widgetData: { compose: { upload: { sessionFileId: 'file-plain', fileName: 'Plain.docx' }, fileName: 'Plain.docx' } },
      displayName: 'Plain.docx',
    } as unknown as WorkspacePaneEvent);

    expect(setChatSessionIdMock).not.toHaveBeenCalled();
  });
});
