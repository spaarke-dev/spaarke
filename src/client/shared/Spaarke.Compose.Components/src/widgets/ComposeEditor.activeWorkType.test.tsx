/**
 * ComposeEditor.activeWorkType.test.tsx — ai-advanced-capabilities-analysis-hub-r1 task 041
 * (FR-13) render-level coverage for the `activeWorkType` host prop against the REAL
 * `ComposeEditor` (real TipTap editor, real right-click popup, real `ComposeAiToolbar`) —
 * mirrors `ComposeEditor.aiToolbarTriggers.test.tsx`'s mount pattern.
 *
 * This test proves the PLUMBING end-to-end at the ComposeEditor layer: `activeWorkType` is a
 * pass-through prop into the ALREADY-SHIPPED `getToolsForSurface(surface, activeWorkType)`
 * (`ComposeAiToolbar.tsx:490`, exercised via its own `getToolsForSurface` unit-test suite in
 * `ComposeAiToolbar.test.tsx` "workType narrowing"). No tool-filtering logic is reimplemented
 * here — this file registers a throwaway `workTypes: ['agreement-analysis']` tool (same pattern
 * as the existing `ComposeAiToolbar.test.tsx` narrowing test) and asserts the REAL right-click
 * popup shows/hides it per the SAME rule already proven at the unit layer.
 *
 * `ComposeWorkspace`'s forwarding of this prop to `<ComposeEditor>` is covered separately in
 * `ComposeWorkspace.activeWorkType.test.tsx` (mocked-editor unit test, mirroring the
 * `ComposeWorkspace.browse.test.tsx` mocking convention).
 */

import * as React from 'react';
import { render, screen, within, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor } from './ComposeEditor';
import { registerComposeAiToolbarAction, __resetComposeAiToolbarActionsForTests } from './ComposeAiToolbar';

// ComposeAiToolbar's `useAuth()` throws outside a real `initAuth()` bootstrap (MSAL) — same
// stub as the sibling `ComposeEditor.aiToolbarTriggers.test.tsx` mock (no action is dispatched
// in this file — no bindingId is ever wired).
jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

function renderComposeEditor(activeWorkType: string | undefined, theme: typeof webLightTheme = webLightTheme) {
  return render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider>
        <ComposeEditor docxBytes={null} sessionId="session-041" activeWorkType={activeWorkType} />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

/** Locates the actual ProseMirror contenteditable node TipTap mounts. */
function getEditorDom(container: HTMLElement): HTMLElement {
  const dom = container.querySelector('.ProseMirror') as HTMLElement | null;
  if (!dom) throw new Error('ProseMirror editor DOM not found — editor did not mount');
  return dom;
}

const AGREEMENT_ONLY_TOOL_ID = 'test-041-agreement-only';

function registerAgreementOnlyTool(): void {
  registerComposeAiToolbarAction({
    id: AGREEMENT_ONLY_TOOL_ID,
    label: 'Agreement-only test tool',
    tooltip: 'workTypes: [agreement-analysis] — task 041 fixture.',
    bindingId: 'b-041-agr',
    placement: 'primary',
    surfaces: ['selection'],
    workTypes: ['agreement-analysis'],
  });
}

afterEach(() => {
  __resetComposeAiToolbarActionsForTests();
});

describe('ComposeEditor — activeWorkType host prop (task 041, FR-13)', () => {
  it('Agreement Review scopes palette: activeWorkType="agreement-analysis" shows the agreement-only tool + shared primitives', async () => {
    registerAgreementOnlyTool();
    const { container } = renderComposeEditor('agreement-analysis');
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);

    fireEvent.contextMenu(editorDom, { clientX: 10, clientY: 10 });
    const popup = await screen.findByTestId('compose-ai-context-menu');
    const toolbar = within(popup).getByTestId('compose-ai-toolbar');

    // Work-type-scoped tool shows...
    expect(within(toolbar).getByTestId(`compose-ai-toolbar-${AGREEMENT_ONLY_TOOL_ID}`)).toBeInTheDocument();
    // ...alongside the shared '*' primitive (getToolsForSurface reused unchanged — FR-13 rule:
    // surfaces ∋ surface AND (workTypes ∋ '*' OR workTypes ∋ activeWorkType)).
    expect(within(toolbar).getByTestId('compose-ai-toolbar-compose-draft-alternative')).toBeInTheDocument();
  });

  it('Default is unscoped: omitting activeWorkType hides the agreement-only tool but keeps shared primitives (no regression)', async () => {
    registerAgreementOnlyTool();
    const { container } = renderComposeEditor(undefined);
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);

    fireEvent.contextMenu(editorDom, { clientX: 10, clientY: 10 });
    const popup = await screen.findByTestId('compose-ai-context-menu');
    const toolbar = within(popup).getByTestId('compose-ai-toolbar');

    expect(within(toolbar).queryByTestId(`compose-ai-toolbar-${AGREEMENT_ONLY_TOOL_ID}`)).not.toBeInTheDocument();
    expect(within(toolbar).getByTestId('compose-ai-toolbar-compose-draft-alternative')).toBeInTheDocument();
  });

  it("Negative: an unrecognized activeWorkType value scopes to shared '*' tools only (no throw, no partial match)", async () => {
    registerAgreementOnlyTool();
    const { container } = renderComposeEditor('some-unregistered-work-type');
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);

    fireEvent.contextMenu(editorDom, { clientX: 10, clientY: 10 });
    const popup = await screen.findByTestId('compose-ai-context-menu');
    const toolbar = within(popup).getByTestId('compose-ai-toolbar');

    expect(within(toolbar).queryByTestId(`compose-ai-toolbar-${AGREEMENT_ONLY_TOOL_ID}`)).not.toBeInTheDocument();
    expect(within(toolbar).getByTestId('compose-ai-toolbar-compose-draft-alternative')).toBeInTheDocument();
  });

  it('ADR-021 dark-mode: the agreement-analysis-scoped palette renders with no hardcoded hex color', async () => {
    registerAgreementOnlyTool();
    const { container } = renderComposeEditor('agreement-analysis', webDarkTheme);
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);

    fireEvent.contextMenu(editorDom, { clientX: 5, clientY: 5 });
    const popup = await screen.findByTestId('compose-ai-context-menu');
    expect(within(popup).getByTestId(`compose-ai-toolbar-${AGREEMENT_ONLY_TOOL_ID}`)).toBeInTheDocument();
    expect(popup.outerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});
