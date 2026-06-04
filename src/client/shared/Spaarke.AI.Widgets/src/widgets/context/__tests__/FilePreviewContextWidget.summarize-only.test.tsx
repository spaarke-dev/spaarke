/**
 * FilePreviewContextWidget — per-file "Summarize this only" affordance tests
 *
 * Covers acceptance criteria for R5 task 021 (D2-12):
 *
 *   (1) Render        — button renders on EVERY file card (multi-file mode)
 *                       AND on the single-file inline action bar.
 *   (2) Dispatch      — click dispatches BOTH `workspace.widget_load` and
 *                       `workspace.streaming_started` on the `workspace`
 *                       channel; `widget_load` carries `widgetData.fileIds
 *                       === [singleFileId]` and `widgetData.sessionId`; the
 *                       paired `streaming_started.streamId` equals
 *                       `widget_load.widgetData.correlationId`.
 *   (3) Additivity    — a second click on a DIFFERENT file emits ANOTHER
 *                       pair with a DIFFERENT correlationId; the prior
 *                       dispatch is NOT cancelled or replaced (FR-06).
 *   (4) Menu parity   — the `DocumentRowMenu.aiSummary` action emits the
 *                       SAME `widget_load + streaming_started` pair as the
 *                       button (FR-05 dual-surface mandate).
 *   (5) Accessibility — button is keyboard-activatable (Enter key triggers
 *                       click); `aria-label` includes the file name.
 *   (6) In-flight     — after click, button renders Spinner + is disabled;
 *                       on receipt of `workspace.streaming_complete` with
 *                       matching `streamId`, button returns to interactive
 *                       state.
 *   (7) Dark mode     — widget renders cleanly under `webDarkTheme` with no
 *                       runtime errors and no hex/rgb literals (semantic
 *                       tokens drive the colour palette).
 *
 * Task: R5-021 (D2-12). Sibling tests: PlaybookGalleryWidget.test.tsx.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { PaneEventBus } from '../../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';
import FilePreviewContextWidget, {
  dispatchSummarizeOnly,
  type FilePreviewContextData,
} from '../FilePreviewContextWidget';
import type { WorkspacePaneEvent } from '../../../events/PaneEventTypes';
import type { DispatchPaneEvent } from '../../../events/useDispatchPaneEvent';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const SESSION_ID = 'sess-123';

const FILE_A = {
  fileId: 'file-a',
  fileName: 'Contract.pdf',
  documentType: 'Contract',
  contentType: 'application/pdf',
  sizeBytes: 12345,
};
const FILE_B = {
  fileId: 'file-b',
  fileName: 'NDA.pdf',
  documentType: 'NDA',
  contentType: 'application/pdf',
  sizeBytes: 6789,
};
const FILE_C = {
  fileId: 'file-c',
  fileName: 'Notes.txt',
  documentType: 'Notes',
  contentType: 'text/plain',
  sizeBytes: 512,
};

const MULTI_FILE_DATA: FilePreviewContextData = {
  files: [FILE_A, FILE_B, FILE_C],
  sessionId: SESSION_ID,
};

const SINGLE_FILE_DATA: FilePreviewContextData = {
  files: [FILE_A],
  sessionId: SESSION_ID,
};

function renderWidget(
  data: FilePreviewContextData,
  options: {
    bus?: PaneEventBus;
    theme?: typeof webLightTheme;
    onFetchPreviewUrl?: (fileId: string) => Promise<string | null>;
    onFileAction?: jest.Mock;
  } = {}
) {
  const bus = options.bus ?? new PaneEventBus();
  const theme = options.theme ?? webLightTheme;
  const onFetchPreviewUrl =
    options.onFetchPreviewUrl ?? (() => Promise.resolve('https://example.com/preview'));

  const result = render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider bus={bus}>
        <FilePreviewContextWidget
          data={data}
          widgetType="file-preview"
          onFetchPreviewUrl={onFetchPreviewUrl}
          onFileAction={options.onFileAction}
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );

  return { bus, ...result };
}

function captureWorkspaceEvents(bus: PaneEventBus): WorkspacePaneEvent[] {
  const events: WorkspacePaneEvent[] = [];
  bus.subscribe('workspace', e => events.push(e));
  return events;
}

// ---------------------------------------------------------------------------
// (1) Render — button renders per file card + on single-file action bar
// ---------------------------------------------------------------------------

describe('FilePreviewContextWidget — Summarize-this-only button rendering', () => {
  it('renders the compact Summarize button on EVERY file card in multi-file mode', () => {
    renderWidget(MULTI_FILE_DATA);

    const compactButtons = screen.getAllByTestId('summarize-only-button-compact');
    expect(compactButtons).toHaveLength(3);

    const fileIds = compactButtons.map(b => b.getAttribute('data-file-id'));
    expect(fileIds).toEqual(['file-a', 'file-b', 'file-c']);
  });

  it('renders the prominent Summarize button on the single-file action bar', () => {
    renderWidget(SINGLE_FILE_DATA);

    const actionBar = screen.getByTestId('single-file-action-bar');
    expect(actionBar).toBeInTheDocument();

    const prominentButton = screen.getByTestId('summarize-only-button');
    expect(prominentButton).toBeInTheDocument();
    expect(prominentButton).toHaveAttribute('data-file-id', 'file-a');
    expect(prominentButton).toHaveTextContent(/summarize this only/i);
  });

  it('renders the prominent Summarize button bound to the ACTIVE file in multi-file mode', () => {
    renderWidget(MULTI_FILE_DATA);

    const prominentButton = screen.getByTestId('summarize-only-button');
    // First file is the default-active selection.
    expect(prominentButton).toHaveAttribute('data-file-id', 'file-a');
  });

  it('hides the Summarize button when no sessionId is supplied (no dispatch target)', () => {
    renderWidget({ files: [FILE_A], sessionId: undefined });

    expect(screen.queryByTestId('summarize-only-button-compact')).not.toBeInTheDocument();
    expect(screen.queryByTestId('summarize-only-button')).not.toBeInTheDocument();
    expect(screen.queryByTestId('single-file-action-bar')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (2) Dispatch — workspace.widget_load + workspace.streaming_started pair
// ---------------------------------------------------------------------------

describe('FilePreviewContextWidget — Summarize-this-only dispatch', () => {
  it('dispatches BOTH widget_load AND streaming_started on the workspace channel', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const workspaceEvents = captureWorkspaceEvents(bus);

    renderWidget(SINGLE_FILE_DATA, { bus });

    const button = screen.getByTestId('summarize-only-button');
    await user.click(button);

    expect(workspaceEvents).toHaveLength(2);

    // First event = widget_load (mount the new tab).
    expect(workspaceEvents[0].type).toBe('widget_load');
    expect(workspaceEvents[0].widgetType).toBe('structured-output-stream');

    // Second event = streaming_started (flip the reducer phase).
    expect(workspaceEvents[1].type).toBe('streaming_started');

    // Correlation id MUST be identical between the pair.
    const widgetData = workspaceEvents[0].widgetData as {
      correlationId: string;
      fileIds: string[];
      sessionId: string;
      mode: string;
    };
    expect(typeof widgetData.correlationId).toBe('string');
    expect(widgetData.correlationId.length).toBeGreaterThan(0);
    expect(workspaceEvents[1].streamId).toBe(widgetData.correlationId);
  });

  it('widget_load payload carries fileIds = [singleFileId], sessionId, schema, and streaming mode', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const workspaceEvents = captureWorkspaceEvents(bus);

    renderWidget(SINGLE_FILE_DATA, { bus });
    await user.click(screen.getByTestId('summarize-only-button'));

    const widgetData = workspaceEvents[0].widgetData as {
      mode: string;
      schema: { fields: Array<{ path: string }> };
      sessionId: string;
      fileIds: string[];
      correlationId: string;
      title: string;
    };

    expect(widgetData.mode).toBe('streaming');
    expect(widgetData.sessionId).toBe(SESSION_ID);
    expect(widgetData.fileIds).toEqual(['file-a']);
    expect(widgetData.title).toBe('Summary: Contract.pdf');
    expect(workspaceEvents[0].displayName).toBe('Summary: Contract.pdf');

    // SUMMARIZE_SCHEMA carries `tldr` first.
    expect(widgetData.schema.fields[0].path).toBe('tldr');
  });

  it('per-card click in multi-file mode binds fileIds to the CARD file, not the active file', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const workspaceEvents = captureWorkspaceEvents(bus);

    renderWidget(MULTI_FILE_DATA, { bus });

    // Click the compact button on file-b's card (NOT the active file-a).
    const compactButtons = screen.getAllByTestId('summarize-only-button-compact');
    const fileBButton = compactButtons.find(b => b.getAttribute('data-file-id') === 'file-b')!;
    expect(fileBButton).toBeDefined();
    await user.click(fileBButton);

    expect(workspaceEvents).toHaveLength(2);
    const widgetData = workspaceEvents[0].widgetData as { fileIds: string[]; sessionId: string };
    expect(widgetData.fileIds).toEqual(['file-b']);
    expect(widgetData.sessionId).toBe(SESSION_ID);
  });

  it('does NOT dispatch to any non-workspace channel (no channel drift)', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const workspaceEvents: unknown[] = [];
    const contextEvents: unknown[] = [];
    const conversationEvents: unknown[] = [];
    const safetyEvents: unknown[] = [];

    bus.subscribe('workspace', e => workspaceEvents.push(e));
    bus.subscribe('context', e => contextEvents.push(e));
    bus.subscribe('conversation', e => conversationEvents.push(e));
    bus.subscribe('safety', e => safetyEvents.push(e));

    renderWidget(SINGLE_FILE_DATA, { bus });
    await user.click(screen.getByTestId('summarize-only-button'));

    expect(workspaceEvents.length).toBeGreaterThan(0);
    expect(contextEvents).toHaveLength(0);
    expect(conversationEvents).toHaveLength(0);
    expect(safetyEvents).toHaveLength(0);
  });

  it('clicking the compact card button does NOT also fire the card-select handler', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const contextEvents: unknown[] = [];
    bus.subscribe('context', e => contextEvents.push(e));

    renderWidget(MULTI_FILE_DATA, { bus });

    // Click the compact button on file-b's card.
    const compactButtons = screen.getAllByTestId('summarize-only-button-compact');
    const fileBButton = compactButtons.find(b => b.getAttribute('data-file-id') === 'file-b')!;
    await user.click(fileBButton);

    // No context.file_selected event should fire from the button click (the
    // SummarizeOnlyButton's onClick calls e.stopPropagation()).
    expect(contextEvents).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// (3) Additivity — second click produces ANOTHER pair with a new correlationId
// ---------------------------------------------------------------------------

describe('FilePreviewContextWidget — Summarize-this-only tab additivity (FR-06)', () => {
  it('second click on a different file dispatches ANOTHER pair with a UNIQUE correlationId', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const workspaceEvents = captureWorkspaceEvents(bus);

    renderWidget(MULTI_FILE_DATA, { bus });

    const compactButtons = screen.getAllByTestId('summarize-only-button-compact');
    const fileAButton = compactButtons.find(b => b.getAttribute('data-file-id') === 'file-a')!;
    const fileBButton = compactButtons.find(b => b.getAttribute('data-file-id') === 'file-b')!;

    await user.click(fileAButton);
    await user.click(fileBButton);

    // 2 pairs = 4 events.
    expect(workspaceEvents).toHaveLength(4);

    // Pair 1: file-a
    const pair1Load = workspaceEvents[0];
    const pair1Started = workspaceEvents[1];
    const pair1Data = pair1Load.widgetData as { correlationId: string; fileIds: string[] };

    // Pair 2: file-b
    const pair2Load = workspaceEvents[2];
    const pair2Started = workspaceEvents[3];
    const pair2Data = pair2Load.widgetData as { correlationId: string; fileIds: string[] };

    expect(pair1Load.type).toBe('widget_load');
    expect(pair1Started.type).toBe('streaming_started');
    expect(pair2Load.type).toBe('widget_load');
    expect(pair2Started.type).toBe('streaming_started');

    expect(pair1Data.fileIds).toEqual(['file-a']);
    expect(pair2Data.fileIds).toEqual(['file-b']);

    // CRITICAL: correlationIds MUST be unique across invocations.
    expect(pair1Data.correlationId).not.toBe(pair2Data.correlationId);
    expect(pair1Started.streamId).toBe(pair1Data.correlationId);
    expect(pair2Started.streamId).toBe(pair2Data.correlationId);
  });

  it('repeated clicks on the SAME file still produce unique correlationIds', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const workspaceEvents = captureWorkspaceEvents(bus);

    renderWidget(SINGLE_FILE_DATA, { bus });

    const button = screen.getByTestId('summarize-only-button');
    await user.click(button);
    // Button becomes disabled after click (in-flight); simulate completion
    // by dispatching streaming_complete for the first request so the next
    // click is accepted.
    const firstCorrelationId = (workspaceEvents[0].widgetData as { correlationId: string }).correlationId;
    act(() => {
      bus.dispatch('workspace', { type: 'streaming_complete', streamId: firstCorrelationId });
    });

    await user.click(button);

    const loadEvents = workspaceEvents.filter(e => e.type === 'widget_load');
    expect(loadEvents).toHaveLength(2);
    const cid1 = (loadEvents[0].widgetData as { correlationId: string }).correlationId;
    const cid2 = (loadEvents[1].widgetData as { correlationId: string }).correlationId;
    expect(cid1).not.toBe(cid2);
  });
});

// ---------------------------------------------------------------------------
// (4) Menu parity — DocumentRowMenu.aiSummary routes through the SAME shape
// ---------------------------------------------------------------------------

describe('FilePreviewContextWidget — DocumentRowMenu aiSummary parity (FR-05)', () => {
  it('clicking the aiSummary menu action dispatches the same widget_load + streaming_started pair as the button', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const workspaceEvents = captureWorkspaceEvents(bus);

    renderWidget(MULTI_FILE_DATA, { bus });

    // Open the DocumentRowMenu on file-a's card. The 3-dot trigger has
    // aria-label "Actions for {fileName}". Use the first one (file-a card).
    const menuTriggers = screen.getAllByRole('button', { name: /more actions for/i });
    expect(menuTriggers.length).toBeGreaterThan(0);
    await user.click(menuTriggers[0]);

    // Click the AI summary action — its label is "AI summary" per
    // DocumentRowMenu's FR-DOC-01 canonical ordering.
    const aiSummaryItem = await screen.findByRole('menuitem', { name: /ai summary/i });
    await user.click(aiSummaryItem);

    expect(workspaceEvents).toHaveLength(2);
    expect(workspaceEvents[0].type).toBe('widget_load');
    expect(workspaceEvents[1].type).toBe('streaming_started');

    const widgetData = workspaceEvents[0].widgetData as {
      fileIds: string[];
      sessionId: string;
      correlationId: string;
      mode: string;
    };
    expect(widgetData.fileIds).toEqual(['file-a']);
    expect(widgetData.sessionId).toBe(SESSION_ID);
    expect(widgetData.mode).toBe('streaming');
    expect(workspaceEvents[1].streamId).toBe(widgetData.correlationId);
  });

  it('aiSummary menu action ALSO bubbles to host onFileAction for analytics / side-effects', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const onFileAction = jest.fn();

    renderWidget(MULTI_FILE_DATA, { bus, onFileAction });

    const menuTriggers = screen.getAllByRole('button', { name: /more actions for/i });
    await user.click(menuTriggers[0]);
    const aiSummaryItem = await screen.findByRole('menuitem', { name: /ai summary/i });
    await user.click(aiSummaryItem);

    expect(onFileAction).toHaveBeenCalledWith('aiSummary', 'file-a');
  });
});

// ---------------------------------------------------------------------------
// (5) Accessibility — keyboard + aria-label
// ---------------------------------------------------------------------------

describe('FilePreviewContextWidget — Summarize button accessibility', () => {
  it('exposes a per-file aria-label that includes the file name', () => {
    renderWidget(MULTI_FILE_DATA);

    const compactButtons = screen.getAllByTestId('summarize-only-button-compact');
    const labels = compactButtons.map(b => b.getAttribute('aria-label'));
    expect(labels).toEqual([
      'Summarize Contract.pdf only',
      'Summarize NDA.pdf only',
      'Summarize Notes.txt only',
    ]);
  });

  it('prominent button has a file-name-bearing aria-label', () => {
    renderWidget(SINGLE_FILE_DATA);

    const button = screen.getByTestId('summarize-only-button');
    expect(button).toHaveAttribute('aria-label', 'Summarize Contract.pdf only');
  });

  it('is keyboard-activatable via Enter key', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const workspaceEvents = captureWorkspaceEvents(bus);

    renderWidget(SINGLE_FILE_DATA, { bus });

    const button = screen.getByTestId('summarize-only-button');
    button.focus();
    await user.keyboard('{Enter}');

    expect(workspaceEvents.length).toBeGreaterThan(0);
    expect(workspaceEvents[0].type).toBe('widget_load');
  });

  it('is keyboard-activatable via Space key', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const workspaceEvents = captureWorkspaceEvents(bus);

    renderWidget(SINGLE_FILE_DATA, { bus });

    const button = screen.getByTestId('summarize-only-button');
    button.focus();
    // Use the named-key form `[Space]` for clarity + compat with user-event v14.
    await user.keyboard('[Space]');

    expect(workspaceEvents.length).toBeGreaterThan(0);
    expect(workspaceEvents[0].type).toBe('widget_load');
  });
});

// ---------------------------------------------------------------------------
// (6) In-flight state — Spinner + disabled while streaming
// ---------------------------------------------------------------------------

describe('FilePreviewContextWidget — Summarize in-flight indicator', () => {
  it('disables the button and flips data-in-flight after click', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();

    renderWidget(SINGLE_FILE_DATA, { bus });
    const button = screen.getByTestId('summarize-only-button');

    expect(button).toHaveAttribute('data-in-flight', 'false');

    await user.click(button);

    expect(button).toHaveAttribute('data-in-flight', 'true');
    expect(button).toBeDisabled();
  });

  it('resets in-flight state when matching streaming_complete arrives', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const workspaceEvents = captureWorkspaceEvents(bus);

    renderWidget(SINGLE_FILE_DATA, { bus });
    const button = screen.getByTestId('summarize-only-button');

    await user.click(button);
    expect(button).toHaveAttribute('data-in-flight', 'true');

    const correlationId = (workspaceEvents[0].widgetData as { correlationId: string }).correlationId;

    act(() => {
      bus.dispatch('workspace', { type: 'streaming_complete', streamId: correlationId });
    });

    expect(button).toHaveAttribute('data-in-flight', 'false');
    expect(button).not.toBeDisabled();
  });

  it('does NOT reset in-flight state for a streaming_complete with a different streamId', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();

    renderWidget(SINGLE_FILE_DATA, { bus });
    const button = screen.getByTestId('summarize-only-button');

    await user.click(button);
    expect(button).toHaveAttribute('data-in-flight', 'true');

    act(() => {
      bus.dispatch('workspace', { type: 'streaming_complete', streamId: 'unrelated-id' });
    });

    // Still in-flight — different streamId is ignored.
    expect(button).toHaveAttribute('data-in-flight', 'true');
  });
});

// ---------------------------------------------------------------------------
// (7) Dark mode — semantic tokens render cleanly under webDarkTheme
// ---------------------------------------------------------------------------

describe('FilePreviewContextWidget — dark mode parity (ADR-021)', () => {
  it('renders cleanly under webDarkTheme without runtime errors', () => {
    // jsdom does not resolve final computed colours; the binding contract is
    // that no error is thrown during render and the expected DOM structure
    // is present. Hex-literal absence is enforced statically by ADR-021
    // (see `notes/task-021-per-file-affordance-evidence.md` ADR-021 check).
    const { container } = renderWidget(MULTI_FILE_DATA, { theme: webDarkTheme });

    expect(container).toBeTruthy();
    expect(screen.getAllByTestId('summarize-only-button-compact')).toHaveLength(3);
    expect(screen.getByTestId('summarize-only-button')).toBeInTheDocument();
    expect(screen.getByTestId('single-file-action-bar')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (8) Pure dispatch helper — exposes the same shape independent of React
// ---------------------------------------------------------------------------

describe('dispatchSummarizeOnly — pure dispatch helper', () => {
  it('emits widget_load + streaming_started with matching correlationId / streamId', () => {
    const bus = new PaneEventBus();
    const workspaceEvents = captureWorkspaceEvents(bus);

    // Bind to PaneEventBus.dispatch — it already satisfies DispatchPaneEvent.
    const dispatch: DispatchPaneEvent = (channel, event) => bus.dispatch(channel, event);

    const result = dispatchSummarizeOnly('file-a', SESSION_ID, 'Contract.pdf', dispatch);

    expect(typeof result.correlationId).toBe('string');
    expect(workspaceEvents).toHaveLength(2);
    expect(workspaceEvents[0].type).toBe('widget_load');
    expect(workspaceEvents[1].type).toBe('streaming_started');
    expect((workspaceEvents[0].widgetData as { correlationId: string }).correlationId).toBe(
      result.correlationId,
    );
    expect(workspaceEvents[1].streamId).toBe(result.correlationId);
  });
});
