/**
 * ComposeWorkspace.browse.test.tsx — FR-01 (spaarkeai-compose-r2 task 010)
 *
 * Verifies the Browse transient-mount seam: clicking "Browse / open file" on the empty
 * state opens a local file picker; selecting a `.docx` reads it into an ArrayBuffer and
 * routes it into the editor's `docxBytes` seam via the SAME `mountTransient` reducer
 * action FR-03/task 012 uses for Assistant-uploaded files — no `sprk_document` create,
 * no BFF round-trip (ADR-040). Mirrors the sibling `ComposeWorkspace.upload.test.tsx`
 * mocking strategy so both transient-mount paths are verified at the same fidelity.
 *
 * Heavy children are mocked to keep the test on the seam under test:
 *   - `./ComposeEditor`  — captured stub recording the `docxBytes` prop it receives.
 *   - `./ComposeToolbar` — captured stub recording the `isDirty` prop it receives.
 *   - `./hooks`          — BroadcastChannel / checkout / heartbeat side-effects.
 *   - `@spaarke/ai-widgets/events` — PaneEventBus dispatch/subscribe.
 *   - `@spaarke/auth`    — `authenticatedFetch` is the fetch boundary (must NOT be
 *     called on a Browse mount — transient mounts never hit the BFF).
 */

import * as React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

// Fluent's MessageBar (error/warning banners) uses ResizeObserver, which jsdom lacks.
// Minimal no-op polyfill so the error-state render doesn't throw.
if (typeof (globalThis as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
  (globalThis as { ResizeObserver?: unknown }).ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  };
}

// ── Fetch boundary (ADR-028) — must NOT be called for a Browse transient mount ──
const authenticatedFetchMock = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...args),
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-token',
    authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...args),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// ── PaneEventBus (no-op in this test) ───────────────────────────────────────
// NOT `virtual`: jest.config `moduleNameMapper` maps `@spaarke/ai-widgets/events` to the real
// source, so a resolved (non-virtual) mock binds to the mapped path and applies deterministically
// (a virtual mock is keyed to the raw specifier and gets bypassed in a shared --runInBand registry).
jest.mock('@spaarke/ai-widgets/events', () => ({
  useDispatchPaneEvent: () => jest.fn(),
  usePaneEvent: () => undefined,
}));

// ── Heavy workspace hooks — inert doubles ───────────────────────────────────
jest.mock('./hooks', () => ({
  useComposeBroadcastChannel: () => ({ postFocusMe: jest.fn(), postForceClosed: jest.fn() }),
  useComposeCheckoutLifecycle: () => ({ forceCloseAndAcquire: jest.fn(), discardAndCancel: jest.fn() }),
  useComposeHeartbeatGate: () => undefined,
}));

// ── ComposeToolbar — capture the `isDirty` prop it is handed ────────────────
const toolbarProps: { current: { isDirty?: boolean } } = { current: {} };
jest.mock('./ComposeToolbar', () => ({
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  ComposeToolbar: (props: any) => {
    toolbarProps.current = props;
    return <div data-testid="compose-toolbar-stub" />;
  },
}));

// ── ComposeEditor — capture the docxBytes + sessionId it is handed ──────────
// `sessionId` is the DOCUMENT session id threaded onward to ComposeAiToolbar as
// `documentSessionId` — the non-empty value is what classifies a compose EDIT
// (redline) vs a misrouted INFORMATIONAL prose card (Wave 2 / UAT-R3 Test #3).
const editorDocxBytes: { current: ArrayBuffer | null | undefined } = { current: undefined };
const editorSessionId: { current: string | undefined } = { current: undefined };
// FIX #5 (UAT): the standalone ComposeToolbar row was folded into the consolidated
// ComposeFormatToolbar inside ComposeEditor; the workspace now threads Save state
// (`canSave`) to ComposeEditor. Capture it here to assert the transient-draft
// "first Save is reachable" signal that previously rode `ComposeToolbar.isDirty`.
const editorCanSave: { current: boolean | undefined } = { current: undefined };
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef(
      (props: { docxBytes: ArrayBuffer | null; sessionId?: string; canSave?: boolean }, ref: React.Ref<unknown>) => {
        editorDocxBytes.current = props.docxBytes;
        editorSessionId.current = props.sessionId;
        editorCanSave.current = props.canSave;
        ReactLib.useImperativeHandle(ref, () => ({
          serialize: async () => new ArrayBuffer(0),
          getCounts: () => ({ characters: 0, words: 0 }),
          isDirty: () => true,
          materializeComposeDraft: () => undefined,
          materializePendingRedline: () => 'applied',
        }));
        return <div data-testid="compose-editor-stub" />;
      }
    ),
  };
});

// Import AFTER mocks are registered.
// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderWorkspace(
  props: Partial<React.ComponentProps<typeof ComposeWorkspace>> = {},
  theme: typeof webLightTheme = webLightTheme
) {
  return render(
    <FluentProvider theme={theme}>
      <ComposeWorkspace bffBaseUrl="https://bff.example.test" driveId="" tenantId="tenant-1" {...props} />
    </FluentProvider>
  );
}

/** Build a `File` whose `.text()`/FileReader read resolves to known bytes. */
function makeDocxFile(name = 'contract.docx', content = 'fake-docx-bytes'): File {
  return new File([content], name, {
    type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockReset();
  // Wave 2: once a Browse mount establishes a real document session id, the FR-04
  // refresh-durability effect issues a READ-ONLY `GET .../compose-outputs` probe against
  // that session (harmless — nothing is in the ledger yet). Default the fetch boundary to a
  // 404 so the probe early-returns without throwing. Persistence routes (create-on-save /
  // save / upload / persist) must STILL never be called on a transient mount — asserted below.
  authenticatedFetchMock.mockResolvedValue({
    ok: false,
    status: 404,
    json: async () => [],
    text: async () => '',
  });
  editorDocxBytes.current = undefined;
  editorSessionId.current = undefined;
  editorCanSave.current = undefined;
  toolbarProps.current = {};
});

/** URL fragments for the persistence routes a TRANSIENT mount must never call. */
const PERSISTENCE_ROUTE_FRAGMENTS = ['create-on-save', '/save', '/upload', '/persist', '/documents/'];

/** Assert no fetch touched a persistence route (a read-only compose-outputs probe is allowed). */
function expectNoPersistenceCalls(): void {
  const persistenceCalls = authenticatedFetchMock.mock.calls.filter(
    ([url]) => typeof url === 'string' && PERSISTENCE_ROUTE_FRAGMENTS.some(frag => (url as string).includes(frag))
  );
  expect(persistenceCalls).toHaveLength(0);
}

describe('ComposeWorkspace — FR-01 Browse transient mount', () => {
  it('renders the empty state with the Browse CTA when no document/upload ref is supplied', () => {
    renderWorkspace();
    expect(screen.getByTestId('compose-empty-state')).toBeInTheDocument();
    expect(screen.getByTestId('compose-empty-state-browse')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-editor-stub')).not.toBeInTheDocument();
  });

  it('clicking Browse and picking a .docx mounts its bytes into the editor (empty state gone)', async () => {
    renderWorkspace();

    const browseButton = screen.getByTestId('compose-empty-state-browse');
    fireEvent.click(browseButton);

    const fileInput = screen.getByTestId('compose-workspace-browse-file-input') as HTMLInputElement;
    const file = makeDocxFile();
    fireEvent.change(fileInput, { target: { files: [file] } });

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    expect(screen.queryByTestId('compose-empty-state')).not.toBeInTheDocument();

    expect(editorDocxBytes.current).toBeInstanceOf(ArrayBuffer);
    expect((editorDocxBytes.current as ArrayBuffer).byteLength).toBe(file.size);
  });

  it('issues no BFF calls and creates no sprk_document on a Browse mount (transient only)', async () => {
    renderWorkspace();

    fireEvent.click(screen.getByTestId('compose-empty-state-browse'));
    const fileInput = screen.getByTestId('compose-workspace-browse-file-input') as HTMLInputElement;
    fireEvent.change(fileInput, { target: { files: [makeDocxFile()] } });

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());

    // No Load/Save/create/promote/upload calls — a Browse mount never PERSISTS (transient).
    // (A read-only compose-outputs durability probe against the tab's document session is allowed.)
    expectNoPersistenceCalls();
  });

  it('marks the editor dirty after a Browse mount so first Save triggers create-on-save (FR-05)', async () => {
    renderWorkspace();

    fireEvent.click(screen.getByTestId('compose-empty-state-browse'));
    const fileInput = screen.getByTestId('compose-workspace-browse-file-input') as HTMLInputElement;
    fireEvent.change(fileInput, { target: { files: [makeDocxFile()] } });

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    // FIX #5: the transient Browse draft makes Save reachable (create-on-save first
    // Save) — surfaced now via the `canSave` prop threaded to ComposeEditor's
    // consolidated toolbar, replacing the old `ComposeToolbar.isDirty` signal.
    expect(editorCanSave.current).toBe(true);
  });

  it('establishes a NON-EMPTY document session id on a Browse mount (Wave 2 / UAT-R3 Test #3)', async () => {
    renderWorkspace();

    fireEvent.click(screen.getByTestId('compose-empty-state-browse'));
    const fileInput = screen.getByTestId('compose-workspace-browse-file-input') as HTMLInputElement;
    fireEvent.change(fileInput, { target: { files: [makeDocxFile()] } });

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());

    // The minted document session id is threaded to ComposeEditor (→ ComposeAiToolbar's
    // `documentSessionId`). A non-empty value is what routes a compose EDIT to the DOCUMENT
    // session (redline + DEF-12 confirmation) instead of being reclassified INFORMATIONAL.
    // Pre-fix this stayed '' (INITIAL_STATE.sessionId) — the Test #3 defect.
    expect(typeof editorSessionId.current).toBe('string');
    expect(editorSessionId.current).not.toBe('');
    expect((editorSessionId.current as string).length).toBeGreaterThan(0);

    // Still transient: the id was minted CLIENT-side (Browse bytes are local) — no persistence
    // round-trip. (A read-only compose-outputs durability probe against the session is allowed.)
    expectNoPersistenceCalls();
  });

  it('(negative) cancelling the picker leaves the empty state unchanged and mounts nothing', async () => {
    renderWorkspace();

    fireEvent.click(screen.getByTestId('compose-empty-state-browse'));
    const fileInput = screen.getByTestId('compose-workspace-browse-file-input') as HTMLInputElement;
    // Cancelling the native picker fires `change` with an empty FileList.
    fireEvent.change(fileInput, { target: { files: [] } });

    expect(screen.getByTestId('compose-empty-state')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-editor-stub')).not.toBeInTheDocument();
    expect(authenticatedFetchMock).not.toHaveBeenCalled();
  });

  it('renders the empty state + Browse CTA correctly under the dark theme (ADR-021)', () => {
    renderWorkspace({}, webDarkTheme);
    expect(screen.getByTestId('compose-empty-state')).toBeInTheDocument();
    expect(screen.getByTestId('compose-empty-state-browse')).toBeInTheDocument();
  });
});
