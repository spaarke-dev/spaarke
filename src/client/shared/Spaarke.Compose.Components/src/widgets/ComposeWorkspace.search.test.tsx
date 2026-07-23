/**
 * ComposeWorkspace.search.test.tsx — FR-02 (spaarkeai-compose-r2 task 011)
 *
 * Verifies the Search-for-Document seam: clicking "Search for Document" on the empty
 * state opens the standard Dataverse lookup (Xrm.Utility.lookupObjects) scoped to
 * `sprk_document`; selecting a record resolves its SPE pointer (Xrm.WebApi.retrieveRecord
 * — `sprk_graphitemid` / `sprk_graphdriveid` / `sprk_filename`, the SAME fields the 1c
 * ribbon launcher `DocumentComposeLaunch.ts` reads) and drives the EXISTING
 * `GET /api/compose/documents/{speId}` load path (`authenticatedFetch`) — producing a
 * REAL loaded document (with `documentRecordId`), identical to 1c. Mirrors the sibling
 * `ComposeWorkspace.browse.test.tsx` / `ComposeWorkspace.upload.test.tsx` mocking
 * strategy so all three entry-path seams are verified at the same fidelity.
 *
 * Heavy children are mocked to keep the test on the seam under test:
 *   - `./ComposeEditor`  — captured stub recording the `docxBytes` + `documentRef` props.
 *   - `./ComposeToolbar` — captured stub recording the `isDirty` prop it receives.
 *   - `./hooks`          — BroadcastChannel / checkout / heartbeat side-effects.
 *   - `@spaarke/ai-widgets/events` — PaneEventBus dispatch/subscribe.
 *   - `@spaarke/auth`    — `authenticatedFetch` is the fetch boundary (the 1c Load leg).
 *   - `@spaarke/ui-components` — `createXrmNavigationService` / `createXrmDataService`
 *     (the two Xrm primitives backing the Search lookup + SPE-pointer resolution).
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

// ── Fetch boundary (ADR-028) — the 1c Load leg Search reuses ────────────────
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

// ── Xrm adapters (ADR-028: no raw Bearer / no direct Xrm coupling in the component) ──
const openLookupMock = jest.fn();
const retrieveRecordMock = jest.fn();
jest.mock('@spaarke/ui-components', () => ({
  createXrmNavigationService: () => ({ openLookup: openLookupMock }),
  createXrmDataService: () => ({ retrieveRecord: retrieveRecordMock }),
}));

// ── PaneEventBus (no-op in this test) ───────────────────────────────────────
// NOT `virtual`: jest.config `moduleNameMapper` maps `@spaarke/ai-widgets/events` to the real
// source, so a resolved (non-virtual) mock binds to the mapped path and applies deterministically.
// A virtual mock is keyed to the raw specifier, not the resolved path, so it gets bypassed once a
// sibling suite loads the real module in a shared --runInBand registry → the real hook runs with
// no provider and throws "must be called inside a <PaneEventBusProvider>".
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

// ── ComposeEditor — capture the docxBytes + documentRef it is handed ───────
const editorProps: { current: { docxBytes?: ArrayBuffer | null; documentRef?: { sprkDocumentId?: string } } } = {
  current: {},
};
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef(
      (
        props: { docxBytes: ArrayBuffer | null; documentRef?: { sprkDocumentId?: string } },
        ref: React.Ref<unknown>
      ) => {
        editorProps.current = props;
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

/** A single Dataverse lookup result for `sprk_document`. */
const PICKED_DOCUMENT = { id: 'doc-guid-1', name: 'Contract.docx', entityType: 'sprk_document' };

/** The `sprk_document` record fields the lookup selection resolves to (1c parity). */
function mockRetrieveRecordResolves(overrides: Partial<Record<string, unknown>> = {}): void {
  retrieveRecordMock.mockResolvedValue({
    sprk_documentid: PICKED_DOCUMENT.id,
    sprk_graphitemid: 'spe-item-123',
    sprk_graphdriveid: 'drive-abc',
    sprk_filename: 'Contract.docx',
    ...overrides,
  });
}

/** A successful `GET /api/compose/documents/{speId}` response (the 1c Load leg). */
function mockLoadResponse(overrides: Partial<Record<string, unknown>> = {}): void {
  authenticatedFetchMock.mockResolvedValue({
    ok: true,
    json: async () => ({
      documentSpeId: 'spe-item-123',
      driveId: 'drive-abc',
      sessionId: 'session-1',
      documentRecordId: PICKED_DOCUMENT.id,
      content: btoa('fake-docx-bytes'),
      eTag: 'etag-1',
      fileName: 'Contract.docx',
      size: 100,
      ...overrides,
    }),
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockReset();
  openLookupMock.mockReset();
  retrieveRecordMock.mockReset();
  editorProps.current = {};
  toolbarProps.current = {};
});

describe('ComposeWorkspace — FR-02 Search-for-Document -> reuse 1c load path', () => {
  it('renders the empty state with the Search CTA when no document/upload ref is supplied', () => {
    renderWorkspace();
    expect(screen.getByTestId('compose-empty-state')).toBeInTheDocument();
    expect(screen.getByTestId('compose-empty-state-search')).toBeInTheDocument();
  });

  it('clicking "Search for Document" opens the Spaarke Document lookup scoped to sprk_document', () => {
    openLookupMock.mockResolvedValue([]);
    renderWorkspace();

    fireEvent.click(screen.getByTestId('compose-empty-state-search'));

    expect(openLookupMock).toHaveBeenCalledWith(expect.objectContaining({ entityType: 'sprk_document' }));
  });

  it('selecting a document loads it via GET /api/compose/documents/{speId} (1c leg) and mounts it', async () => {
    openLookupMock.mockResolvedValue([PICKED_DOCUMENT]);
    mockRetrieveRecordResolves();
    mockLoadResponse();
    renderWorkspace();

    fireEvent.click(screen.getByTestId('compose-empty-state-search'));

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    expect(screen.queryByTestId('compose-empty-state')).not.toBeInTheDocument();

    // Resolved the SPE pointer via the SAME two fields the 1c ribbon launcher reads.
    expect(retrieveRecordMock).toHaveBeenCalledWith(
      'sprk_document',
      PICKED_DOCUMENT.id,
      expect.stringContaining('sprk_graphitemid')
    );

    // The 1c Load leg: GET /api/compose/documents/{speId}?driveId&tenantId&documentRecordId&displayName
    // via authenticatedFetch (ADR-028) — never a raw Bearer header. (A second authenticatedFetch call
    // may follow — the existing FR-04 render-follows-store draft-materialize effect probes the
    // compose-outputs ledger on every 'loaded' transition; that is pre-existing behaviour, not part
    // of this seam.)
    const loadCall = authenticatedFetchMock.mock.calls.find(([callUrl]: [string]) =>
      callUrl.includes('/api/compose/documents/')
    );
    expect(loadCall).toBeDefined();
    const [url, init] = loadCall as [string, RequestInit];
    expect(url).toContain('/api/compose/documents/spe-item-123');
    expect(url).toContain('driveId=drive-abc');
    expect(url).toContain(`documentRecordId=${PICKED_DOCUMENT.id}`);
    expect(init).toMatchObject({ method: 'GET' });

    // Real loaded document — carries documentRecordId (loaded stage), identical to 1c;
    // NOT a transient mount (unlike 010's Browse / 012's upload paths).
    expect(editorProps.current.documentRef?.sprkDocumentId).toBe(PICKED_DOCUMENT.id);
    expect(editorProps.current.docxBytes).toBeInstanceOf(ArrayBuffer);
  });

  it('(negative) dismissing the lookup without a selection leaves the empty state unchanged', async () => {
    openLookupMock.mockResolvedValue([]); // Xrm.Utility.lookupObjects resolves [] on cancel
    renderWorkspace();

    fireEvent.click(screen.getByTestId('compose-empty-state-search'));

    await waitFor(() => expect(openLookupMock).toHaveBeenCalled());
    expect(screen.getByTestId('compose-empty-state')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-editor-stub')).not.toBeInTheDocument();
    expect(retrieveRecordMock).not.toHaveBeenCalled();
    expect(authenticatedFetchMock).not.toHaveBeenCalled();
  });

  it('surfaces an error (stays recoverable) when the selected document has no SPE drive pointer', async () => {
    openLookupMock.mockResolvedValue([PICKED_DOCUMENT]);
    retrieveRecordMock.mockResolvedValue({
      sprk_documentid: PICKED_DOCUMENT.id,
      sprk_graphitemid: null,
      sprk_graphdriveid: null,
      sprk_filename: 'Contract.docx',
    });
    renderWorkspace();

    fireEvent.click(screen.getByTestId('compose-empty-state-search'));

    await waitFor(() => expect(screen.getByTestId('compose-workspace-error-empty')).toBeInTheDocument());
    expect(authenticatedFetchMock).not.toHaveBeenCalled();
  });

  it('fails soft (empty state unchanged) when no Dataverse-hosted context is available', async () => {
    openLookupMock.mockRejectedValue(new Error('Xrm.Utility is not available.'));
    renderWorkspace();

    fireEvent.click(screen.getByTestId('compose-empty-state-search'));

    await waitFor(() => expect(openLookupMock).toHaveBeenCalled());
    expect(screen.getByTestId('compose-empty-state')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-editor-stub')).not.toBeInTheDocument();
  });

  it('renders the empty state + Search CTA correctly under the dark theme (ADR-021)', () => {
    renderWorkspace({}, webDarkTheme);
    expect(screen.getByTestId('compose-empty-state')).toBeInTheDocument();
    expect(screen.getByTestId('compose-empty-state-search')).toBeInTheDocument();
  });
});
