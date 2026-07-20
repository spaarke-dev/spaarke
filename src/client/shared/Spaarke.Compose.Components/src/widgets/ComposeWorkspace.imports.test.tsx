/**
 * ComposeWorkspace.imports.test.tsx — task 052 fast-follow (FR-08/FR-24/FR-25 wire gap).
 *
 * The through-the-wire seam test added by task 052 caught that `ComposeEndpoints.Load`
 * projects `paraIdMap` / `importedRevisions` / `importedComments` onto the
 * `GET /api/compose/documents/{id}` JSON response, but `ComposeWorkspace` never read
 * those three fields off the Load response or threaded them to `ComposeEditor` — so in
 * the running app `ComposeEditor`'s paraId substrate (task 010) and imported-Word-
 * revisions/comments rendering (tasks 050/051) were dead code (props always `undefined`).
 *
 * This suite proves the gap is closed:
 *   1. A stored-document Load response carrying all three fields reaches `ComposeEditor`
 *      as props, set ATOMICALLY alongside `docxBytes` (the documented mount contract —
 *      see `ComposeEditor.tsx` JSDoc on `paraIdMap`/`importedRevisions`/`importedComments`).
 *   2. An older BFF response that OMITS the three fields degrades safely to `[]` — no
 *      crash, no `undefined` prop reaching a required-array consumer.
 *   3. A transient (Browse) mount — a door with NO server pre-parse to source imports
 *      from — deliberately reaches `ComposeEditor` with EMPTY import props, proving the
 *      workspace does not fabricate imports where there is no source.
 *
 * Heavy children are mocked to keep the test on the seam under test, mirroring the
 * sibling `ComposeWorkspace.search.test.tsx` / `ComposeWorkspace.browse.test.tsx`
 * mocking strategy:
 *   - `./ComposeEditor`  — captured stub recording every prop it receives.
 *   - `./hooks`          — BroadcastChannel / checkout / heartbeat side-effects.
 *   - `@spaarke/ai-widgets/events` — PaneEventBus dispatch/subscribe.
 *   - `@spaarke/auth`    — `authenticatedFetch` is the fetch boundary (the Load leg).
 *   - `@spaarke/ui-components` — Xrm adapters (statically imported by ComposeWorkspace
 *     for the FR-02 Search seam; unused by these tests but must resolve).
 */

import * as React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type { ParaIdMapEntry, ImportedRevision, ImportedComment } from '../types/compose-contracts';

// Fluent's MessageBar (error/warning banners) uses ResizeObserver, which jsdom lacks.
if (typeof (globalThis as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
  (globalThis as { ResizeObserver?: unknown }).ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  };
}

// ── Fetch boundary (ADR-028) — the Load leg under test ──────────────────────
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

// ── Xrm adapters (statically imported by ComposeWorkspace for FR-02 Search) ──
jest.mock('@spaarke/ui-components', () => ({
  createXrmNavigationService: () => ({ openLookup: jest.fn() }),
  createXrmDataService: () => ({ retrieveRecord: jest.fn() }),
}));

// ── PaneEventBus (no-op in this test) ───────────────────────────────────────
// NOT `virtual`: jest.config `moduleNameMapper` maps `@spaarke/ai-widgets/events` to the real
// source, so a resolved (non-virtual) mock binds to the mapped path and applies deterministically.
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

// ── ComposeEditor — capture EVERY prop, including the three under test ──────
type CapturedEditorProps = {
  docxBytes?: ArrayBuffer | null;
  paraIdMap?: readonly ParaIdMapEntry[];
  importedRevisions?: readonly ImportedRevision[];
  importedComments?: readonly ImportedComment[];
};
const editorProps: { current: CapturedEditorProps } = { current: {} };
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef((props: CapturedEditorProps, ref: React.Ref<unknown>) => {
      editorProps.current = props;
      ReactLib.useImperativeHandle(ref, () => ({
        serialize: async () => new ArrayBuffer(0),
        getCounts: () => ({ characters: 0, words: 0 }),
        isDirty: () => true,
        materializeComposeDraft: () => undefined,
        materializePendingRedline: () => 'applied',
      }));
      return <div data-testid="compose-editor-stub" />;
    }),
  };
});

// Import AFTER mocks are registered.
// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderWorkspace(props: Partial<React.ComponentProps<typeof ComposeWorkspace>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace bffBaseUrl="https://bff.example.test" driveId="" tenantId="tenant-1" {...props} />
    </FluentProvider>
  );
}

const PARA_ID_MAP: ParaIdMapEntry[] = [
  { index: 0, paraId: 'AAAAAAA1', isMinted: false },
  { index: 1, paraId: 'AAAAAAA2', isMinted: true },
];

const IMPORTED_REVISIONS: ImportedRevision[] = [
  {
    kind: 'insertion',
    id: 'rev-1',
    author: 'Jane Author',
    date: '2026-01-01T00:00:00Z',
    text: 'inserted clause text',
    anchorText: 'surrounding paragraph context',
    paragraphHint: 0,
    paraId: 'AAAAAAA1',
  },
];

const IMPORTED_COMMENTS: ImportedComment[] = [
  {
    id: 'cmt-1',
    author: 'Jane Author',
    date: '2026-01-01T00:00:00Z',
    commentText: 'please review this clause',
    anchorText: 'surrounding paragraph context',
    paragraphHint: 0,
    paraId: 'AAAAAAA1',
  },
];

/** A successful `GET /api/compose/documents/{speId}` response (the stored-document Load leg). */
function mockLoadResponse(overrides: Partial<Record<string, unknown>> = {}): void {
  authenticatedFetchMock.mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      documentSpeId: 'spe-item-123',
      driveId: 'drive-abc',
      sessionId: 'session-1',
      documentRecordId: 'doc-guid-1',
      content: btoa('fake-docx-bytes'),
      eTag: 'etag-1',
      fileName: 'Contract.docx',
      size: 100,
      ...overrides,
    }),
  });
}

/** Build a `File` whose FileReader read resolves to known bytes (Browse mount). */
function makeDocxFile(name = 'local.docx', content = 'fake-local-docx-bytes'): File {
  return new File([content], name, {
    type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockReset();
  // Benign default for any secondary call (e.g. the FR-04 refresh-durability `compose-outputs`
  // probe fired once the editor reaches 'loaded') — mirrors ComposeWorkspace.browse.test.tsx's
  // beforeEach. Tests that drive a real Load response override this via `mockLoadResponse`.
  authenticatedFetchMock.mockResolvedValue({ ok: false, status: 404, json: async () => [], text: async () => '' });
  editorProps.current = {};
});

describe('ComposeWorkspace — task 052 fast-follow: paraIdMap/importedRevisions/importedComments wiring', () => {
  it('threads a stored-document Load response`s paraIdMap/importedRevisions/importedComments to ComposeEditor, atomically with docxBytes', async () => {
    mockLoadResponse({
      paraIdMap: PARA_ID_MAP,
      importedRevisions: IMPORTED_REVISIONS,
      importedComments: IMPORTED_COMMENTS,
    });

    renderWorkspace({
      initialDocumentRef: { speDriveItemId: 'spe-item-123' },
      driveId: 'drive-abc',
    });

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());

    // Atomic mount contract: docxBytes AND the three import props all present together on
    // the SAME captured render — never a two-phase mount where docxBytes lands first.
    expect(editorProps.current.docxBytes).toBeInstanceOf(ArrayBuffer);
    expect(editorProps.current.paraIdMap).toEqual(PARA_ID_MAP);
    expect(editorProps.current.importedRevisions).toEqual(IMPORTED_REVISIONS);
    expect(editorProps.current.importedComments).toEqual(IMPORTED_COMMENTS);
  });

  it('degrades safely to empty arrays when an older BFF Load response omits the three fields', async () => {
    mockLoadResponse(); // no paraIdMap/importedRevisions/importedComments keys at all

    renderWorkspace({
      initialDocumentRef: { speDriveItemId: 'spe-item-123' },
      driveId: 'drive-abc',
    });

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());

    expect(editorProps.current.docxBytes).toBeInstanceOf(ArrayBuffer);
    expect(editorProps.current.paraIdMap).toEqual([]);
    expect(editorProps.current.importedRevisions).toEqual([]);
    expect(editorProps.current.importedComments).toEqual([]);
  });

  it('leaves paraIdMap/importedRevisions/importedComments empty on a transient Browse mount (no server pre-parse to source imports from)', async () => {
    renderWorkspace();

    fireEvent.click(screen.getByTestId('compose-empty-state-browse'));
    const fileInput = screen.getByTestId('compose-workspace-browse-file-input') as HTMLInputElement;
    fireEvent.change(fileInput, { target: { files: [makeDocxFile()] } });

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());

    expect(editorProps.current.docxBytes).toBeInstanceOf(ArrayBuffer);
    expect(editorProps.current.paraIdMap).toEqual([]);
    expect(editorProps.current.importedRevisions).toEqual([]);
    expect(editorProps.current.importedComments).toEqual([]);
    // No stored-document Load call — a Browse mount never hits the BFF.
    expect(authenticatedFetchMock).not.toHaveBeenCalledWith(
      expect.stringContaining('/api/compose/documents/'),
      expect.anything()
    );
  });
});
