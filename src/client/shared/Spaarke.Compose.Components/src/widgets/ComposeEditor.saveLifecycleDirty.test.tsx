/**
 * ComposeEditor.saveLifecycleDirty.test.tsx — spaarkeai-compose-r8 task 012 (FR-S03).
 *
 * THE CONTRACT: the editor's dirty flag survives a failed save.
 *
 * Why this file exists: `buildContentModel()` — the born-in-editor capture — used to clear the dirty
 * flag as a side effect of BUILDING the payload, before the POST was even issued. Every recovery
 * affordance in the workspace keys off that one flag (Save enablement, Ctrl+S, the `beforeunload`
 * guard, the flush-on-unmount, the toolbar label, the draft autosave tick), so a save that then failed
 * left the editor believing it was clean and the user one tab-close from losing their work. The
 * imported sibling (`buildImportedContentModel`) already got this right, which is exactly why nobody
 * noticed: whichever path the tester used decided whether they saw the bug.
 *
 * Every assertion here runs against the REAL `ComposeEditor` and the REAL handle — the flag lives in a
 * ref inside the component, so a stubbed editor could only re-assert the test's own fiction. Edits are
 * driven through the live TipTap instance (`editor.chain()...`), the same transactions a keystroke
 * produces, because the op-log high-water mark that `commitSaved()` reads is populated by the
 * ProseMirror plugin and nothing else.
 *
 * ADR-038: behavior at the editor's save seam — real state transitions, no assertions on reducer or
 * op-log internals as a proxy.
 */

import * as React from 'react';
import { render, screen, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type { Editor } from '@tiptap/react';

// `@spaarke/auth` is mocked WITHOUT `virtual: true` — as every `@spaarke/*` mock in this package now
// is (r8 task 018). A virtual registration here was observed corrupting the resolution of
// `@spaarke/auth` for LATER suites in a full-package run (`useComposeWordShuttle.test.tsx`, whose own
// ordinary mock stopped being applied, failed with "Auth not initialized"). The plain mock is enough —
// nothing in this file calls the transport. Same shape as ComposeEditor.aiToolbarTriggers.test.tsx.
jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
  authenticatedFetch: jest.fn(),
}));

// NO `virtual: true` on the sibling-lib mocks below — deliberately, and it is load-bearing. The flag
// registers the specifier in jest's RESOLVER, which is shared by every suite a worker runs, so one
// suite's virtual registration changes how a LATER suite resolves the same specifier. See the
// "Sibling `@spaarke/*` resolution" note in jest.config.js for the measurement and the contract.
// This suite exercises the REAL ComposeEditor, whose import graph reaches `@spaarke/ui-components`
// and `@spaarke/ai-widgets/events` through ComposeAiToolbar. FR-S03 is the contract that decides
// whether a user keeps their work; its guard must run everywhere, including `compose-client-gate`.
jest.mock('@spaarke/ui-components', () => ({
  createConsumerDispatcher: () => async () => ({ status: 'ok' }),
  FormModal: () => null,
  SprkModal: () => null,
  RichFilePreviewDialog: () => null,
  SendEmailDialog: () => null,
  createXrmNavigationService: () => ({ openLookup: jest.fn() }),
  createXrmDataService: () => ({ retrieveRecord: jest.fn() }),
}));
jest.mock('@spaarke/ai-widgets/events', () => ({
  useDispatchPaneEvent: () => jest.fn(),
  usePaneEvent: () => undefined,
}));

// `ComposeAiToolbar` is the ONE component in the editor's graph that calls `useAuth()`, which throws
// outside a real `initAuth()` bootstrap. Mocking the module (rather than relying on the
// `@spaarke/auth` mock above winning) is belt-and-braces against the same class of resolution leak:
// run on its own the suite's own mock is used, but a full-package run was observed loading the real
// `Spaarke.Auth/dist` instead and every test in this file failed with "Auth not initialized". The AI
// toolbar has no bearing on the dirty-flag contract under test, so removing it from the tree is both
// the smaller surface and the deterministic one. Runtime exports only — the types are erased.
jest.mock('./ComposeAiToolbar', () => ({
  ComposeAiToolbar: () => null,
  getComposeAiToolbarActions: () => [],
  getToolsForSurface: () => [],
  subscribeComposeAiToolbarActions: () => () => undefined,
}));

// Imported AFTER the mocks so the editor's module graph resolves through them.
// eslint-disable-next-line import/first
import { ComposeEditor, type ComposeEditorHandle } from './ComposeEditor';

/** A born-in-editor mount: no retained bytes — the shape whose save posts `buildContentModel()`. */
function renderBornInEditor(onDirtyChange: (dirty: boolean) => void, ref: React.Ref<ComposeEditorHandle>) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeEditor ref={ref} docxBytes={null} sessionId="session-fr-s03" onDirtyChange={onDirtyChange} />
    </FluentProvider>
  );
}

/** The live TipTap instance the PM view attaches to its contenteditable node (same accessor as the
 *  aiToolbarTriggers suite). Used to drive REAL edit transactions — jsdom cannot type into a
 *  contenteditable, and the op-log only observes genuine transactions. */
function getEditorInstance(container: HTMLElement): Editor {
  const dom = container.querySelector('.ProseMirror') as unknown as { editor?: Editor } | null;
  if (!dom?.editor) throw new Error('TipTap editor instance not found — editor did not mount');
  return dom.editor;
}

/** Type `text` at the end of the document — one ordinary edit transaction. */
function typeSomething(editor: Editor, text: string): void {
  act(() => {
    editor.chain().focus('end').insertContent(text).run();
  });
}

describe('ComposeEditor — FR-S03: the dirty flag is cleared only by a confirmed save', () => {
  it('buildContentModel() does NOT clear the dirty flag — a save that fails leaves the document dirty', async () => {
    const onDirtyChange = jest.fn();
    const ref = React.createRef<ComposeEditorHandle>();
    const { container } = renderBornInEditor(onDirtyChange, ref);
    await screen.findByRole('textbox');
    const editor = getEditorInstance(container);
    onDirtyChange.mockClear();

    typeSomething(editor, 'first draft sentence');
    expect(ref.current?.isDirty()).toBe(true);

    // The save payload is captured...
    const model = ref.current?.buildContentModel();
    expect(model).toBeTruthy();

    // ...and then the POST fails, so `commitSaved()` is never called. THE assertion: the editor is
    // still dirty, and it never told the workspace otherwise. Before this fix both were false here,
    // which disabled Save, disarmed `beforeunload` and disarmed the unmount flush in one stroke.
    expect(ref.current?.isDirty()).toBe(true);
    expect(onDirtyChange).not.toHaveBeenCalledWith(false);
  });

  it('a retry after a failed save re-captures the SAME work — repeated builds never clear the flag', async () => {
    const onDirtyChange = jest.fn();
    const ref = React.createRef<ComposeEditorHandle>();
    const { container } = renderBornInEditor(onDirtyChange, ref);
    await screen.findByRole('textbox');
    const editor = getEditorInstance(container);

    typeSomething(editor, 'work that must survive two failures');
    ref.current?.buildContentModel(); // attempt 1 — fails
    ref.current?.buildContentModel(); // attempt 2 — fails
    ref.current?.buildContentModel(); // attempt 3 — fails

    expect(ref.current?.isDirty()).toBe(true);
    // The third capture still contains the text: the payload is derived from the live document, so a
    // retry cannot post an empty model (the op-log's pre-038 failure mode, pointed at the model path).
    expect(JSON.stringify(ref.current?.buildContentModel())).toContain('work that must survive');
  });

  it('NEGATIVE: a CONFIRMED save clears the flag — commitSaved() reports clean exactly once', async () => {
    const onDirtyChange = jest.fn();
    const ref = React.createRef<ComposeEditorHandle>();
    const { container } = renderBornInEditor(onDirtyChange, ref);
    await screen.findByRole('textbox');
    const editor = getEditorInstance(container);

    typeSomething(editor, 'saved successfully');
    ref.current?.buildContentModel();
    onDirtyChange.mockClear();

    act(() => {
      ref.current?.commitSaved();
    });

    expect(ref.current?.isDirty()).toBe(false);
    expect(onDirtyChange).toHaveBeenCalledTimes(1);
    expect(onDirtyChange).toHaveBeenCalledWith(false);
  });

  it('an edit typed DURING the in-flight save keeps the document dirty after the save confirms', async () => {
    const onDirtyChange = jest.fn();
    const ref = React.createRef<ComposeEditorHandle>();
    const { container } = renderBornInEditor(onDirtyChange, ref);
    await screen.findByRole('textbox');
    const editor = getEditorInstance(container);

    typeSomething(editor, 'the sentence this save carries');
    ref.current?.buildContentModel(); // payload captured — the POST is now in flight
    typeSomething(editor, ' and the one it does not'); // the user keeps typing
    onDirtyChange.mockClear();

    act(() => {
      ref.current?.commitSaved(); // the POST confirms
    });

    // The confirmed save did NOT carry the mid-flight edit, so the document is still dirty and Save
    // must stay live. Reporting clean here would acknowledge work that was never sent.
    expect(ref.current?.isDirty()).toBe(true);
    expect(onDirtyChange).toHaveBeenLastCalledWith(true);
  });

  it('a save with nothing outstanding leaves the document clean (no false-dirty regression)', async () => {
    const onDirtyChange = jest.fn();
    const ref = React.createRef<ComposeEditorHandle>();
    const { container } = renderBornInEditor(onDirtyChange, ref);
    await screen.findByRole('textbox');
    const editor = getEditorInstance(container);

    typeSomething(editor, 'one edit');
    ref.current?.buildContentModel();
    act(() => {
      ref.current?.commitSaved();
    });
    expect(ref.current?.isDirty()).toBe(false);

    // A SECOND save with no intervening edit (a zero-edit Ctrl+S) must not resurrect the flag — the
    // capture watermark is consumed by each commit, so a stale one cannot report dirty forever.
    ref.current?.buildContentModel();
    act(() => {
      ref.current?.commitSaved();
    });
    expect(ref.current?.isDirty()).toBe(false);
  });
});
