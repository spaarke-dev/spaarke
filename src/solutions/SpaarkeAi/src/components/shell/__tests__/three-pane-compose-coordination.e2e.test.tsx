/**
 * three-pane-compose-coordination.e2e.test.tsx — the anti-recurrence FORCING
 * FUNCTION for the Compose three-pane coordination layer (spaarkeai-compose-r2
 * task 104, E2E-R5; supersedes/absorbs task 070).
 *
 * WHAT THIS PROVES (project E2E Definition-of-Done row 3 — "mounted + reachable"):
 * for EACH of the six Compose flows, we emit on the SOURCE channel over a REAL
 * PaneEventBus (NOT a mocked bus) and assert the RECEIVING pane's observable
 * effect. The three receiver surfaces are mounted TOGETHER under ONE real
 * PaneEventBusProvider — the exact cross-pane topology of the running shell
 * (App.tsx → ThreePaneShell co-mounts ComposeWorkspace, ContextPaneController,
 * and ConversationPane under a single bus):
 *
 *   Flow 1 compose_selection_changed  (Workspace→Context)  → ContextPaneController
 *   Flow 2 compose_selection_offer     (Workspace→Assistant)→ ComposeAssistantCoordination
 *   Flow 3 compose_context_insert      (Context→Workspace)  → useComposeWorkspaceReceivers
 *   Flow 4 compose_context_offer       (Context→Assistant)  → ComposeAssistantCoordination
 *   Flow 5 compose_assistant_insert    (Assistant→Workspace)→ useComposeWorkspaceReceivers
 *   Flow 6 compose_assistant_insight   (Assistant→Context)  → ContextPaneController
 *   Flow 7 compose_qa_highlight        (Assistant→Workspace)→ useComposeWorkspaceReceivers
 *          (task 072, FR-35 Doc Q&A stretch — added without disturbing Flows 1-6)
 *
 * The receivers exercised here are the SHIPPED ones (the same components/hook the
 * running host mounts) — no parallel test-only implementation. The workspace-leg
 * hook is mounted with spy handlers (the editor is a downstream dependency,
 * legitimately stubbed — the BUS is real, which is the DoD requirement).
 *
 * Every event literal below is type-checked against the bus channel unions — if a
 * compose discriminant or field were NOT typed on the shared-lib union (the gap
 * 5.2 `as any` regression), this test would fail to COMPILE. That compile-time
 * guarantee is part of the forcing function.
 *
 * @see notes/e2e-gap-register.md Cluster 5 (gaps 5.1–5.2)
 * @see ContextPaneController.test.tsx — the single-pane mounting harness modelled here
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';
import { useComposeWorkspaceReceivers } from '@spaarke/compose-components/widgets/useComposeWorkspaceReceivers';
import { ContextPaneController } from '../../context/ContextPaneController';
import { ComposeAssistantCoordination } from '../../conversation/ComposeAssistantCoordination';
import type { ShellStageContextValue } from '../ThreePaneShell';
import { ShellStageContext } from '../ThreePaneShell';

// ---------------------------------------------------------------------------
// Harness
// ---------------------------------------------------------------------------

function makeStageContext(overrides: Partial<ShellStageContextValue> = {}): ShellStageContextValue {
  return {
    currentStage: 'active-chat',
    toLoading: jest.fn(),
    toActiveChat: jest.fn(),
    toReview: jest.fn(),
    toActiveWork: jest.fn(),
    reset: jest.fn(),
    ...overrides,
  };
}

/**
 * Mounts the SHIPPED workspace-leg receiver hook (Flows 3 + 5) with spy
 * handlers. The hook is the exact one ComposeWorkspace consumes — proving the
 * workspace-channel reception is reachable over the real bus without standing up
 * a full TipTap editor.
 */
function WorkspaceReceiverHarness(props: {
  onContextInsert: (e: WorkspacePaneEvent) => void;
  onAssistantInsert: (e: WorkspacePaneEvent) => void;
  onQaHighlight: (e: WorkspacePaneEvent) => void;
}): React.JSX.Element {
  useComposeWorkspaceReceivers({
    onContextInsert: props.onContextInsert,
    onAssistantInsert: props.onAssistantInsert,
    onQaHighlight: props.onQaHighlight,
  });
  return <div data-testid="workspace-receiver-harness" />;
}

interface Harness {
  bus: PaneEventBus;
  onContextInsert: jest.Mock;
  onAssistantInsert: jest.Mock;
  onQaHighlight: jest.Mock;
}

function renderThreePane(): Harness {
  const bus = new PaneEventBus();
  const onContextInsert = jest.fn();
  const onAssistantInsert = jest.fn();
  const onQaHighlight = jest.fn();

  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ShellStageContext.Provider value={makeStageContext()}>
          {/* Context pane — Flows 1 + 6 */}
          <ContextPaneController />
        </ShellStageContext.Provider>
        {/* Assistant pane — Flows 2 + 4 */}
        <ComposeAssistantCoordination />
        {/* Workspace pane — Flows 3 + 5 + 7 (shipped hook, spy handlers) */}
        <WorkspaceReceiverHarness
          onContextInsert={onContextInsert}
          onAssistantInsert={onAssistantInsert}
          onQaHighlight={onQaHighlight}
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );

  return { bus, onContextInsert, onAssistantInsert, onQaHighlight };
}

const DOC = { speDriveItemId: 'drive-item-1' } as const;
const NOW = '2026-07-10T00:00:00.000Z';

// ---------------------------------------------------------------------------
// Cross-pane forcing function — one assertion per flow
// ---------------------------------------------------------------------------

describe('three-pane Compose coordination — real PaneEventBus cross-pane (task 104)', () => {
  it('Flow 1 (Workspace→Context): compose_selection_changed surfaces the selection on the Context pane', async () => {
    const { bus } = renderThreePane();

    act(() => {
      bus.dispatch('context', {
        type: 'compose_selection_changed',
        documentRef: DOC,
        selection: { from: 0, to: 12, selectionText: 'Hello clause', contextLabel: 'Clause 3.4' },
        sessionId: 'sess-1',
        timestamp: NOW,
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('context-compose-selection')).toBeInTheDocument();
    });
    expect(screen.getByTestId('context-compose-coordination')).toHaveTextContent('Clause 3.4');
  });

  it('Flow 6 (Assistant→Context): compose_assistant_insight renders a read-only derived insight on the Context pane', async () => {
    const { bus } = renderThreePane();

    act(() => {
      bus.dispatch('context', {
        type: 'compose_assistant_insight',
        documentRef: DOC,
        insightKind: 'risk',
        insightText: 'Indemnity is uncapped.',
        sourceNodeId: 'node-7',
        sessionId: 'sess-1',
        timestamp: NOW,
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('context-compose-insight')).toBeInTheDocument();
    });
    const insight = screen.getByTestId('context-compose-insight');
    expect(insight).toHaveTextContent('Indemnity is uncapped.');
    expect(insight).toHaveAttribute('data-insight-kind', 'risk');
  });

  it('Flow 2 (Workspace→Assistant): compose_selection_offer surfaces an action affordance on the Assistant pane', async () => {
    const { bus } = renderThreePane();

    act(() => {
      bus.dispatch('conversation', {
        type: 'compose_selection_offer',
        documentRef: DOC,
        selection: { from: 0, to: 12, selectionText: 'Hello clause', contextLabel: 'Clause 3.4' },
        jpsScope: 'compose-selection',
        sessionId: 'sess-1',
        timestamp: NOW,
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('assistant-compose-selection-offer')).toBeInTheDocument();
    });
    expect(screen.getByTestId('assistant-compose-coordination')).toHaveTextContent('Clause 3.4');
  });

  it('Flow 4 (Context→Assistant): compose_context_offer stages a precedent on the Assistant pane', async () => {
    const { bus } = renderThreePane();

    act(() => {
      bus.dispatch('conversation', {
        type: 'compose_context_offer',
        documentRef: DOC,
        sourceClauseId: 'precedent-42',
        contentHtml: '<p>Standard indemnity</p>',
        jpsScope: 'compose-document',
        sessionId: 'sess-1',
        timestamp: NOW,
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('assistant-compose-staged-context')).toBeInTheDocument();
    });
    expect(screen.getByTestId('assistant-compose-staged-context')).toHaveTextContent('precedent-42');
  });

  it('Flow 3 (Context→Workspace): compose_context_insert reaches the Workspace editor receiver', () => {
    const { bus, onContextInsert, onAssistantInsert } = renderThreePane();

    act(() => {
      bus.dispatch('workspace', {
        type: 'compose_context_insert',
        documentRef: DOC,
        sourceClauseId: 'precedent-42',
        contentHtml: '<p>Standard indemnity</p>',
        format: 'html',
        sessionId: 'sess-1',
        timestamp: NOW,
      });
    });

    expect(onContextInsert).toHaveBeenCalledTimes(1);
    const received = onContextInsert.mock.calls[0][0] as WorkspacePaneEvent;
    expect(received.type).toBe('compose_context_insert');
    expect(received.contentHtml).toBe('<p>Standard indemnity</p>');
    expect(received.sourceClauseId).toBe('precedent-42');
    // Flow 3 must NOT be misrouted to the Flow 5 handler.
    expect(onAssistantInsert).not.toHaveBeenCalled();
  });

  it('Flow 5 (Assistant→Workspace): compose_assistant_insert reaches the Workspace receiver carrying the ledgerRef', () => {
    const { bus, onAssistantInsert, onContextInsert } = renderThreePane();

    act(() => {
      bus.dispatch('workspace', {
        type: 'compose_assistant_insert',
        documentRef: DOC,
        sourceNodeId: 'node-3',
        sourcePlaybookId: 'pb-1',
        contentHtml: '',
        format: 'html',
        insertMode: 'insert-at-cursor',
        requireUserConfirm: false,
        ledgerRef: 'binding-1@t1',
        sessionId: 'sess-1',
        timestamp: NOW,
      });
    });

    expect(onAssistantInsert).toHaveBeenCalledTimes(1);
    const received = onAssistantInsert.mock.calls[0][0] as WorkspacePaneEvent;
    expect(received.type).toBe('compose_assistant_insert');
    expect(received.ledgerRef).toBe('binding-1@t1');
    // Flow 5 must NOT be misrouted to the Flow 3 handler.
    expect(onContextInsert).not.toHaveBeenCalled();
  });

  // ── Flow 7 (task 072, FR-35 Doc Q&A stretch) ──────────────────────────────

  it('Flow 7 (Assistant→Workspace): compose_qa_highlight reaches the Workspace editor receiver', () => {
    const { bus, onQaHighlight, onContextInsert, onAssistantInsert } = renderThreePane();

    act(() => {
      bus.dispatch('workspace', {
        type: 'compose_qa_highlight',
        qaSourceText: 'the indemnification cap is $500,000',
        qaSectionLabel: 'Section 7.3',
        sessionId: 'sess-1',
        timestamp: NOW,
      });
    });

    expect(onQaHighlight).toHaveBeenCalledTimes(1);
    const received = onQaHighlight.mock.calls[0][0] as WorkspacePaneEvent;
    expect(received.type).toBe('compose_qa_highlight');
    expect(received.qaSourceText).toBe('the indemnification cap is $500,000');
    expect(received.qaSectionLabel).toBe('Section 7.3');
    // Must NOT be misrouted to the Flow 3/5 handlers.
    expect(onContextInsert).not.toHaveBeenCalled();
    expect(onAssistantInsert).not.toHaveBeenCalled();
  });

  it('Flow 6 with insightKind "citation" (task 072): a Doc Q&A citation surfaces audit-only on the Context pane', async () => {
    const { bus } = renderThreePane();

    act(() => {
      bus.dispatch('context', {
        type: 'compose_assistant_insight',
        insightKind: 'citation',
        insightText: 'Cited from Section 7.3',
        sessionId: 'sess-1',
        timestamp: NOW,
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('context-compose-insight')).toBeInTheDocument();
    });
    const insight = screen.getByTestId('context-compose-insight');
    expect(insight).toHaveTextContent('Cited from Section 7.3');
    expect(insight).toHaveAttribute('data-insight-kind', 'citation');
  });

  // ── Cross-pane isolation regression ───────────────────────────────────────

  it('a Context-channel compose flow does NOT trigger the Assistant or Workspace receivers', async () => {
    const { bus, onContextInsert, onAssistantInsert } = renderThreePane();

    act(() => {
      bus.dispatch('context', {
        type: 'compose_selection_changed',
        documentRef: DOC,
        selection: { from: 0, to: 5, selectionText: 'Hello', contextLabel: 'Heading 2' },
        sessionId: 'sess-1',
        timestamp: NOW,
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('context-compose-selection')).toBeInTheDocument();
    });
    // The Assistant coordination surface renders nothing until a conversation
    // flow fires; the workspace receivers were never invoked.
    expect(screen.queryByTestId('assistant-compose-coordination')).not.toBeInTheDocument();
    expect(onContextInsert).not.toHaveBeenCalled();
    expect(onAssistantInsert).not.toHaveBeenCalled();
  });
});
