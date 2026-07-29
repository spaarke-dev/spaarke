/**
 * ComposeAiToolbar.test.tsx — RTL coverage for the inline AI toolbar
 * (task 030, FR-14). Co-located per package convention.
 *
 * NOTE (honest-scope disclosure — mirrors this project's Spike 0 discipline):
 * `Spaarke.Compose.Components` currently ships NO `jest.config.*` (verified
 * by directory listing at authoring time — this is the FIRST test file in
 * the package). This file is written to the same ts-jest + jsdom + RTL
 * convention used by sibling packages (`Spaarke.AI.Widgets`,
 * `Spaarke.UI.Components`) so it is ready to run the moment that config
 * exists, but it CANNOT execute until a `jest.config.ts` is added to this
 * package (minimally: `preset: 'ts-jest'`, `testEnvironment:
 * 'jest-environment-jsdom'`, plus a `moduleNameMapper` resolving
 * `@spaarke/ui-components` to its `src` entry for the pre-build workspace
 * link). Flagged as a follow-up in the task 030 completion report rather than
 * guessing at a config this session cannot verify by running.
 *
 * `@spaarke/auth`'s `useAuth()` is mocked because it throws outside a real
 * `initAuth()` bootstrap (MSAL) — every test either supplies
 * `dispatchConsumerOverride` (bypassing the real dispatcher entirely) or
 * relies on the mocked `getAccessToken`.
 */

import * as React from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import type { Editor } from '@tiptap/react';
import {
  ComposeAiToolbar,
  registerComposeAiToolbarAction,
  getToolsForSurface,
  __resetComposeAiToolbarActionsForTests,
  extractCleanDraftText,
  type ComposeAiToolbarAction,
} from './ComposeAiToolbar';
import type { TipTapNode } from '../utils/docxBridge';
import type { DispatchPaneEvent } from '@spaarke/ai-widgets/events';
import type { DispatchConsumer } from '@spaarke/ui-components';

jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

/** Minimal TipTap-`Editor`-shaped mock — only the members ComposeAiToolbar reads. */
function createMockEditor(params: { from: number; to: number; text: string }): Editor {
  const listeners = new Map<string, Set<() => void>>();
  const mock = {
    state: {
      selection: { from: params.from, to: params.to },
      doc: {
        textBetween: () => params.text,
      },
    },
    on: (event: string, handler: () => void) => {
      const set = listeners.get(event) ?? new Set<() => void>();
      set.add(handler);
      listeners.set(event, set);
      return mock;
    },
    off: (event: string, handler: () => void) => {
      listeners.get(event)?.delete(handler);
      return mock;
    },
  };
  return mock as unknown as Editor;
}

function noopDispatch(): DispatchPaneEvent {
  return jest.fn() as unknown as DispatchPaneEvent;
}

function renderToolbar(
  ui: {
    editor: Editor | null;
    actions?: ReadonlyArray<ComposeAiToolbarAction>;
    dispatchConsumerOverride?: DispatchConsumer;
    forceVisible?: boolean;
    onRequestInstruction?: (action: ComposeAiToolbarAction) => Promise<string | null>;
  },
  theme: typeof webLightTheme = webLightTheme
) {
  return render(
    <FluentProvider theme={theme}>
      <ComposeAiToolbar
        editor={ui.editor}
        documentRef={{ speDriveItemId: 'spe-123', sprkDocumentId: 'doc-456' }}
        sessionId="session-789"
        bffBaseUrl="https://bff.example.test"
        dispatch={noopDispatch()}
        actions={ui.actions}
        dispatchConsumerOverride={ui.dispatchConsumerOverride}
        forceVisible={ui.forceVisible}
        onRequestInstruction={ui.onRequestInstruction}
      />
    </FluentProvider>
  );
}

const WIRED_TEST_ACTIONS: ComposeAiToolbarAction[] = [
  {
    id: 'compose-explain-clause',
    label: 'Explain',
    tooltip: 'Explain this clause.',
    bindingId: 'test-binding-explain',
    placement: 'primary',
  },
  {
    id: 'compose-compare-to-playbook',
    label: 'Compare to playbook',
    tooltip: 'Compare this clause.',
    bindingId: 'test-binding-compare',
    placement: 'primary',
  },
  {
    id: 'compose-draft-alternative',
    label: 'Draft alternative',
    tooltip: 'Draft an alternative.',
    bindingId: 'test-binding-draft',
    placement: 'primary',
  },
];

afterEach(() => {
  __resetComposeAiToolbarActionsForTests();
});

// ---------------------------------------------------------------------------
// 1. Renders on selection (+ NEGATIVE: collapsed/empty selection)
// ---------------------------------------------------------------------------

describe('ComposeAiToolbar — render on selection', () => {
  it('renders only the Draft-alternative button + overflow trigger on a non-collapsed selection (round-8 #6: Explain/Compare/Defined-terms/Email retired from the selection surface)', () => {
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    renderToolbar({ editor });

    expect(screen.getByTestId('compose-ai-toolbar')).toBeInTheDocument();
    // Contextual AI Tool Library: the default selection surface now carries ONLY the
    // reusable Draft-alternative edit primitive; the three retired tools carry `surfaces: []`.
    expect(screen.getByTestId('compose-ai-toolbar-compose-draft-alternative')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-ai-toolbar-compose-explain-clause')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-ai-toolbar-compose-compare-to-playbook')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-ai-toolbar-email')).not.toBeInTheDocument();
    expect(screen.getByTestId('compose-ai-toolbar-more')).toBeInTheDocument();
  });

  it('NEGATIVE: renders nothing when the selection is collapsed', () => {
    const editor = createMockEditor({ from: 5, to: 5, text: '' });
    const { container } = renderToolbar({ editor });
    expect(screen.queryByTestId('compose-ai-toolbar')).not.toBeInTheDocument();
    expect(container.querySelector('[data-testid^="compose-ai-toolbar"]')).toBeNull();
  });

  it('NEGATIVE: renders nothing when no editor is mounted', () => {
    renderToolbar({ editor: null });
    expect(screen.queryByTestId('compose-ai-toolbar')).not.toBeInTheDocument();
  });

  it('the default (Phase-4-stubbed) Draft-alternative button renders disabled — no Binding wired yet', () => {
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    renderToolbar({ editor });
    expect(screen.getByTestId('compose-ai-toolbar-compose-draft-alternative')).toBeDisabled();
  });
});

// ---------------------------------------------------------------------------
// 1b. Task 111 (UAT-R2 layout fix) — ONE Toolbar, single tightly-spaced row,
//     and `forceVisible` (right-click / point-insertion trigger).
// ---------------------------------------------------------------------------

describe('ComposeAiToolbar — task 111 layout fix (single Toolbar + tightly-spaced row + no overflow)', () => {
  it('renders exactly ONE Toolbar (no second toolbar, no orphaned sibling) with no large-gap dividers', () => {
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    renderToolbar({ editor });

    const toolbars = screen.getAllByRole('toolbar');
    expect(toolbars).toHaveLength(1);
    const toolbar = toolbars[0];
    expect(toolbar).toBe(screen.getByTestId('compose-ai-toolbar'));

    // GAP FIX (UAT 2026-07-15): the ToolbarDividers were removed so every action
    // sits in ONE tightly-spaced row at the single columnGap token — no large gap
    // between the primary AI icons and the Email / overflow affordances.
    expect(toolbar.querySelector('[role="separator"]')).toBeNull();
  });

  it('DEF-17: the Toolbar renders on a SINGLE line (flex-wrap: nowrap) — overflow goes to the ⋯ menu, not a second row', () => {
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    renderToolbar({ editor });
    const toolbar = screen.getByTestId('compose-ai-toolbar');
    // Real Griffel-injected CSS (not a class-name guess): the AI bubble must be
    // one row. Actions that do not fit belong in the overflow menu.
    expect(getComputedStyle(toolbar).flexWrap).toBe('nowrap');
    // The single-row container also holds the ⋯ overflow trigger — the
    // structural seam for spilling extra (incl. future DEF-18) actions off-row.
    expect(within(toolbar).getByTestId('compose-ai-toolbar-more')).toBeInTheDocument();
  });

  it('forceVisible renders the toolbar even on a COLLAPSED selection (task 111 right-click / point-insertion trigger)', () => {
    const editor = createMockEditor({ from: 5, to: 5, text: '' });
    renderToolbar({ editor, forceVisible: true });

    expect(screen.getByTestId('compose-ai-toolbar')).toBeInTheDocument();
    expect(screen.getByTestId('compose-ai-toolbar-compose-draft-alternative')).toBeInTheDocument();
    expect(screen.getByTestId('compose-ai-toolbar-more')).toBeInTheDocument();
  });

  it('forceVisible does NOT change the dispatch guard — clicking an action on a collapsed selection still no-ops', async () => {
    const user = userEvent.setup();
    const editor = createMockEditor({ from: 5, to: 5, text: '' });
    const dispatchConsumerOverride = jest.fn();
    renderToolbar({ editor, actions: WIRED_TEST_ACTIONS, forceVisible: true, dispatchConsumerOverride });

    await user.click(screen.getByTestId('compose-ai-toolbar-compose-explain-clause'));

    expect(dispatchConsumerOverride).not.toHaveBeenCalled();
  });

  it('without forceVisible (the selection-triggered BubbleMenu mount path), a collapsed selection still renders nothing — unchanged default behavior', () => {
    const editor = createMockEditor({ from: 5, to: 5, text: '' });
    renderToolbar({ editor }); // forceVisible omitted (defaults to false/undefined)
    expect(screen.queryByTestId('compose-ai-toolbar')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// 1c. FIX #9 (UAT) — bubble restyle: icons-only, elevated grey surface,
//     vertical overflow, tooltip names.
// ---------------------------------------------------------------------------

describe('ComposeAiToolbar — FIX #9 bubble restyle', () => {
  it('primary action button + overflow are ICON-ONLY (no tool WORDS on the bubble)', () => {
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    renderToolbar({ editor });
    const toolbar = screen.getByTestId('compose-ai-toolbar');

    // No visible label text on the primary button (icon-only). The name lives in
    // the hover Tooltip + aria-label, not as button text.
    expect(within(toolbar).queryByText('Draft alternative')).not.toBeInTheDocument();

    // The Draft-alternative button renders its icon (an svg) and keeps its accessible name.
    const draft = screen.getByTestId('compose-ai-toolbar-compose-draft-alternative');
    expect(draft.querySelector('svg')).not.toBeNull();
    expect(draft.textContent).toBe('');
    expect(screen.getByLabelText('Draft alternative')).toBeInTheDocument();
    // Overflow trigger renders an icon (vertical three-dots) with no text.
    const more = screen.getByTestId('compose-ai-toolbar-more');
    expect(more.querySelector('svg')).not.toBeNull();
    expect(more.textContent).toBe('');
  });

  it('the bubble Toolbar carries a full-width elevated surface (background + shadow) so it spans all items', () => {
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    renderToolbar({ editor });
    const toolbar = screen.getByTestId('compose-ai-toolbar');
    const style = getComputedStyle(toolbar);
    // Griffel-injected (real CSS, not a class-name guess): the elevated surface now
    // lives on the Toolbar itself (FIX #9) — a background + a shadow are present.
    expect(style.backgroundColor).not.toBe('');
    expect(style.boxShadow).not.toBe('');
  });

  it('hovering a primary button surfaces a Tooltip naming the tool', async () => {
    const user = userEvent.setup();
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    renderToolbar({ editor });

    await user.hover(screen.getByTestId('compose-ai-toolbar-compose-draft-alternative'));
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Draft alternative');
  });
});

// ---------------------------------------------------------------------------
// 2. Dark mode (ADR-021) — semantic tokens, no hardcoded hex
// ---------------------------------------------------------------------------

describe('ComposeAiToolbar — dark mode (ADR-021)', () => {
  it('renders under a dark FluentProvider theme with no hardcoded hex color in the DOM', () => {
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const { container } = renderToolbar({ editor }, webDarkTheme);

    expect(screen.getByTestId('compose-ai-toolbar')).toBeInTheDocument();
    // ADR-021: no `#rrggbb`/`#rgb` literal anywhere in the rendered markup —
    // all color must flow through Griffel-generated classes referencing
    // semantic `tokens.*`, never an inline hex value.
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });

  it('renders under a light FluentProvider theme with no hardcoded hex color in the DOM', () => {
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const { container } = renderToolbar({ editor }, webLightTheme);

    expect(screen.getByTestId('compose-ai-toolbar')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});

// ---------------------------------------------------------------------------
// 3. Selection interaction + dispatch (direct dispatchConsumer — NOT a
//    PaneEventBus event; Spike 0 correction) + registry extensibility
// ---------------------------------------------------------------------------

describe('ComposeAiToolbar — dispatch + extensibility', () => {
  it('clicking a wired primary action calls dispatchConsumer(bindingId, { slots }) directly — no compose_action_request event', async () => {
    const user = userEvent.setup();
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const dispatchConsumerOverride = jest.fn().mockResolvedValue({ streamId: 's1', status: 'complete' });

    renderToolbar({ editor, actions: WIRED_TEST_ACTIONS, dispatchConsumerOverride });

    await user.click(screen.getByTestId('compose-ai-toolbar-compose-explain-clause'));

    expect(dispatchConsumerOverride).toHaveBeenCalledTimes(1);
    expect(dispatchConsumerOverride).toHaveBeenCalledWith('test-binding-explain', {
      slots: {
        selectionText: 'Hello world',
        selectionAnchorStart: 0,
        selectionAnchorEnd: 11,
        documentSpeId: 'spe-123',
        documentRecordId: 'doc-456',
        sessionId: 'session-789',
      },
    });
  });

  it('a disabled (stub) primary action does not call the dispatcher on click', async () => {
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const dispatchConsumerOverride = jest.fn();
    renderToolbar({ editor, dispatchConsumerOverride }); // default actions — all bindingId: ''

    const button = screen.getByTestId('compose-ai-toolbar-compose-draft-alternative');
    expect(button).toBeDisabled();
    expect(dispatchConsumerOverride).not.toHaveBeenCalled();
  });

  it('the overflow menu lists a registered test action and dispatches it on click — proves registry extensibility without editing ComposeAiToolbar', async () => {
    const user = userEvent.setup();
    registerComposeAiToolbarAction({
      id: 'test-clause-insert',
      label: 'Insert test clause',
      tooltip: 'A follow-on registered action (extensibility proof).',
      bindingId: 'test-binding-clause-insert',
      placement: 'overflow',
    });

    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const dispatchConsumerOverride = jest.fn().mockResolvedValue({ streamId: 's1', status: 'complete' });

    // No `actions` prop passed — the component reads the REAL module registry
    // (DEFAULT_ACTIONS + the registration above), proving the extension point
    // works without any component-source edit.
    renderToolbar({ editor, dispatchConsumerOverride });

    await user.click(screen.getByTestId('compose-ai-toolbar-more'));
    const overflowItem = await screen.findByTestId('compose-ai-toolbar-overflow-test-clause-insert');
    expect(overflowItem).toBeInTheDocument();
    expect(within(overflowItem).getByText('Insert test clause')).toBeInTheDocument();

    await user.click(overflowItem);

    expect(dispatchConsumerOverride).toHaveBeenCalledWith(
      'test-binding-clause-insert',
      expect.objectContaining({ slots: expect.objectContaining({ selectionText: 'Hello world' }) })
    );
  });

  it('round-8 #6: the defined-terms action is RETIRED from the selection surface — the default overflow is empty', async () => {
    const user = userEvent.setup();
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });

    // No `actions` prop → reads the REAL module DEFAULT_ACTIONS. Contextual AI Tool
    // Library (round-8 #6): `compose-defined-terms` now carries `surfaces: []`, so it
    // no longer appears on the selection toolbar's overflow (it belongs to a future
    // `whole-document` surface). With Explain/Compare/Defined-terms all retired, the
    // default selection overflow is empty.
    renderToolbar({ editor });

    await user.click(screen.getByTestId('compose-ai-toolbar-more'));

    expect(screen.queryByTestId('compose-ai-toolbar-overflow-compose-defined-terms')).not.toBeInTheDocument();
    expect(await screen.findByTestId('compose-ai-toolbar-more-empty')).toBeInTheDocument();
  });

  it('the overflow menu shows a placeholder when no overflow actions are registered', async () => {
    const user = userEvent.setup();
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    renderToolbar({ editor, actions: WIRED_TEST_ACTIONS }); // all primary — no overflow entries

    await user.click(screen.getByTestId('compose-ai-toolbar-more'));
    expect(await screen.findByTestId('compose-ai-toolbar-more-empty')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Contextual AI Tool Library (phase 3) — free-text instruction tools
// (compose-rewrite-instruction / "Describe a change…") collect the user's
// instruction via onRequestInstruction BEFORE dispatch and pass it as a slot.
// ---------------------------------------------------------------------------

describe('ComposeAiToolbar — free-text instruction tools (phase 3)', () => {
  const INSTRUCTION_ACTION: ComposeAiToolbarAction = {
    id: 'compose-rewrite-instruction',
    label: 'Describe a change',
    tooltip: 'Describe a change.',
    bindingId: 'test-binding-rewrite',
    placement: 'primary',
    materializesInEditor: true,
    surfaces: ['selection', 'review-note'],
    inputPrompt: 'Describe the change…',
  };

  it('collects the instruction via onRequestInstruction and dispatches it as a slot', async () => {
    const user = userEvent.setup();
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const dispatchConsumerOverride = jest.fn().mockResolvedValue({ streamId: 's1', status: 'complete' });
    const onRequestInstruction = jest.fn().mockResolvedValue('make it mutual');

    renderToolbar({ editor, actions: [INSTRUCTION_ACTION], dispatchConsumerOverride, onRequestInstruction });

    await user.click(screen.getByTestId('compose-ai-toolbar-compose-rewrite-instruction'));

    expect(onRequestInstruction).toHaveBeenCalledWith(expect.objectContaining({ id: 'compose-rewrite-instruction' }));
    expect(dispatchConsumerOverride).toHaveBeenCalledWith(
      'test-binding-rewrite',
      expect.objectContaining({
        slots: expect.objectContaining({ selectionText: 'Hello world', instruction: 'make it mutual' }),
      })
    );
  });

  it('does NOT dispatch when the user cancels the instruction dialog (resolves null)', async () => {
    const user = userEvent.setup();
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const dispatchConsumerOverride = jest.fn();
    const onRequestInstruction = jest.fn().mockResolvedValue(null);

    renderToolbar({ editor, actions: [INSTRUCTION_ACTION], dispatchConsumerOverride, onRequestInstruction });

    await user.click(screen.getByTestId('compose-ai-toolbar-compose-rewrite-instruction'));

    expect(onRequestInstruction).toHaveBeenCalledTimes(1);
    expect(dispatchConsumerOverride).not.toHaveBeenCalled();
  });

  it('a non-instruction tool (no inputPrompt) dispatches WITHOUT calling onRequestInstruction or adding an instruction slot', async () => {
    const user = userEvent.setup();
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const dispatchConsumerOverride = jest.fn().mockResolvedValue({ streamId: 's1', status: 'complete' });
    const onRequestInstruction = jest.fn().mockResolvedValue('should not be called');
    const concise: ComposeAiToolbarAction = {
      id: 'compose-make-concise',
      label: 'Make more concise',
      tooltip: 'Condense.',
      bindingId: 'test-binding-concise',
      placement: 'primary',
      materializesInEditor: true,
      surfaces: ['selection', 'review-note'],
    };

    renderToolbar({ editor, actions: [concise], dispatchConsumerOverride, onRequestInstruction });

    await user.click(screen.getByTestId('compose-ai-toolbar-compose-make-concise'));

    expect(onRequestInstruction).not.toHaveBeenCalled();
    expect(dispatchConsumerOverride).toHaveBeenCalledWith(
      'test-binding-concise',
      expect.objectContaining({ slots: expect.not.objectContaining({ instruction: expect.anything() }) })
    );
  });
});

describe('extractCleanDraftText — clean email body (FIX #10b)', () => {
  it('drops struck (deletion-marked) originals and keeps AI insertions as accepted text', () => {
    // A strike+replace pending redline: "old clause" struck, "new clause" inserted.
    const doc: TipTapNode = {
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [
            { type: 'text', text: 'Keep this. ' },
            { type: 'text', text: 'old clause', marks: [{ type: 'deletion' }] },
            { type: 'text', text: 'new clause', marks: [{ type: 'insertion' }] },
            { type: 'text', text: '.' },
          ],
        },
      ],
    };
    // Garbled (what textBetween produced): "Keep this. old clausenew clause."
    // Clean accepted view:
    expect(extractCleanDraftText(doc)).toBe('Keep this. new clause.');
  });

  it('joins block paragraphs with newlines and trims', () => {
    const doc: TipTapNode = {
      type: 'doc',
      content: [
        { type: 'paragraph', content: [{ type: 'text', text: 'First paragraph.' }] },
        { type: 'heading', attrs: { level: 2 }, content: [{ type: 'text', text: 'A Heading' }] },
        { type: 'paragraph', content: [{ type: 'text', text: 'Second paragraph.' }] },
      ],
    };
    expect(extractCleanDraftText(doc)).toBe('First paragraph.\nA Heading\nSecond paragraph.');
  });

  it('renders a fully chat-drafted (all-insertion) body as its accepted text (not empty)', () => {
    // Every span is a pending insertion — the reject baseline would strip this to empty; the
    // accept view (what an email of the draft must carry) keeps it.
    const doc: TipTapNode = {
      type: 'doc',
      content: [
        {
          type: 'paragraph',
          content: [{ type: 'text', text: 'A drafted sentence.', marks: [{ type: 'insertion' }] }],
        },
      ],
    };
    expect(extractCleanDraftText(doc)).toBe('A drafted sentence.');
  });
});

// ---------------------------------------------------------------------------
// Contextual AI Tool Library — getToolsForSurface: the two-dimension
// (surface × workType) selector + the surface/workType defaults.
// ---------------------------------------------------------------------------

describe('getToolsForSurface — Contextual AI Tool Library selector', () => {
  it('default DEFAULT_ACTIONS: only Draft-alternative is on the selection surface (Explain/Compare/Defined-terms retired, #6)', () => {
    const ids = getToolsForSurface('selection', '*').map(a => a.id);
    // The three edit primitives (round-8 #6 retired Explain/Compare/Defined-terms; phase-3 added
    // make-concise + rewrite-instruction). Order = DEFAULT_ACTIONS order.
    expect(ids).toEqual(['compose-draft-alternative', 'compose-make-concise', 'compose-rewrite-instruction']);
  });

  it('the edit primitives are shown on both the selection AND review-note surfaces from one definition each', () => {
    const EDIT_TOOLS = ['compose-draft-alternative', 'compose-make-concise', 'compose-rewrite-instruction'];
    expect(getToolsForSurface('selection', '*').map(a => a.id)).toEqual(EDIT_TOOLS);
    expect(getToolsForSurface('review-note', '*').map(a => a.id)).toEqual(EDIT_TOOLS);
  });

  it('retired tools (surfaces: []) appear on NO surface even though they remain in the registry', () => {
    for (const surface of ['selection', 'review-note', 'whole-document', 'assistant-chip'] as const) {
      const ids = getToolsForSurface(surface, '*').map(a => a.id);
      expect(ids).not.toContain('compose-explain-clause');
      expect(ids).not.toContain('compose-compare-to-playbook');
      expect(ids).not.toContain('compose-defined-terms');
    }
  });

  it('workType narrowing: a workTypes:[agreement-analysis] tool shows ONLY for that work type, while a workTypes:[*] primitive shows for any', () => {
    registerComposeAiToolbarAction({
      id: 'test-agreement-only',
      label: 'Compare to firm playbook',
      tooltip: 'Agreement-analysis-only.',
      bindingId: 'b-agr',
      placement: 'primary',
      surfaces: ['review-note'],
      workTypes: ['agreement-analysis'],
    });

    // Shared work type '*' → the agreement-only tool is hidden; the '*' edit primitives still show.
    expect(getToolsForSurface('review-note', '*').map(a => a.id)).not.toContain('test-agreement-only');
    expect(getToolsForSurface('review-note', '*').map(a => a.id)).toContain('compose-draft-alternative');
    // Active work type 'agreement-analysis' → BOTH the '*' primitives and the agreement-only tool.
    const agrIds = getToolsForSurface('review-note', 'agreement-analysis').map(a => a.id);
    expect(agrIds).toContain('compose-draft-alternative');
    expect(agrIds).toContain('test-agreement-only');
    // A DIFFERENT work type ('legal-research') → the agreement-only tool is hidden; primitives remain.
    const researchIds = getToolsForSurface('review-note', 'legal-research').map(a => a.id);
    expect(researchIds).not.toContain('test-agreement-only');
    expect(researchIds).toContain('compose-draft-alternative');
  });

  it('a registered action with NO surfaces defaults to the selection surface (backward-compatible)', () => {
    registerComposeAiToolbarAction({
      id: 'test-legacy-shape',
      label: 'Legacy',
      tooltip: 'No surfaces field.',
      bindingId: 'b-legacy',
      placement: 'overflow',
    });
    expect(getToolsForSurface('selection', '*').map(a => a.id)).toContain('test-legacy-shape');
    expect(getToolsForSurface('review-note', '*').map(a => a.id)).not.toContain('test-legacy-shape');
  });

  it('appliesTo predicate can hide a tool for a given context', () => {
    registerComposeAiToolbarAction({
      id: 'test-gated',
      label: 'Gated',
      tooltip: 'Predicate-gated.',
      bindingId: 'b-gated',
      placement: 'primary',
      surfaces: ['selection'],
      appliesTo: ctx => (ctx.selectionText?.length ?? 0) > 5,
    });
    expect(getToolsForSurface('selection', '*', { selectionText: 'short' }).map(a => a.id)).not.toContain('test-gated');
    expect(getToolsForSurface('selection', '*', { selectionText: 'a longer selection' }).map(a => a.id)).toContain(
      'test-gated'
    );
  });
});
