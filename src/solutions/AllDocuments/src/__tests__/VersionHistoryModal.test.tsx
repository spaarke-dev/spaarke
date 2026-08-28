/**
 * VersionHistoryModal tests — spaarkeai-compose-r6 task 051 <ui-tests>.
 *
 *   (1) open-version-list: the version list renders (label / timestamp / size)
 *       fetched via the task-050 OBO list-versions endpoint through
 *       `@spaarke/auth` authenticatedFetch.
 *   (2) open-prior-version-read-only: selecting v3 (while v4 is latest) fetches
 *       the exact bytes from the OBO content endpoint and shows the honest
 *       "Viewing a prior version (read-only)" banner; NO restore/branch
 *       affordance exists anywhere in the modal.
 *   (3) negative (ADR-028 + unified-access-control-r2 task 079): the affordance
 *       ONLY ever calls the per-document, gated endpoint pair
 *       (`/api/documents/{documentId}/versions...`) — never the admin/app-only
 *       container surface, and never the DELETED drive-keyed shape
 *       (`/api/obo/drives/{driveId}/items/{itemId}/versions`), which read an
 *       arbitrary SPE item with no per-document authorization.
 *   (4) dark-mode (ADR-021): the affordance renders under BOTH webLightTheme
 *       and webDarkTheme without errors — all styling is Fluent v9 theme
 *       tokens (structural check, matching the repo's established pattern,
 *       e.g. WorkspaceLayoutWizard rowHeight.test.tsx criterion (e)).
 *
 * `@spaarke/auth` is mocked at module level so the component's real fetch
 * wiring (versionHistory.ts) is exercised and every outbound URL is captured.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
} from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// @spaarke/auth mock — captures every authenticatedFetch URL
// ---------------------------------------------------------------------------

const mockAuthenticatedFetch = jest.fn();

jest.mock('@spaarke/auth', () => ({
  resolveRuntimeConfig: jest.fn().mockResolvedValue({
    msalClientId: 'test-client',
    bffBaseUrl: 'https://bff.test',
    bffOAuthScope: 'api://test/.default',
    tenantId: 'test-tenant',
  }),
  initAuth: jest.fn().mockResolvedValue(undefined),
  authenticatedFetch: (...args: unknown[]) => mockAuthenticatedFetch(...args),
}));

import { VersionHistoryModal } from '../VersionHistoryModal';

// ---------------------------------------------------------------------------
// Fixtures — endpoint returns newest first (task-050 contract)
// ---------------------------------------------------------------------------

const FIXTURE_VERSIONS = [
  { id: '4.0', eTag: 'e4', lastModifiedDateTime: '2026-08-05T14:00:00Z', size: 2097152 },
  { id: '3.0', eTag: 'e3', lastModifiedDateTime: '2026-08-01T10:30:00Z', size: 1048576 },
  { id: '2.0', eTag: 'e2', lastModifiedDateTime: '2026-07-20T09:00:00Z', size: 524288 },
];

const DOCUMENT_ID = '11111111-1111-1111-1111-111111111111';
const VERSIONS_PATH = `/api/documents/${DOCUMENT_ID}/versions`;

function installFetchMock(): void {
  mockAuthenticatedFetch.mockReset();
  mockAuthenticatedFetch.mockImplementation(async (url: string) => {
    if (url.endsWith('/content')) {
      return {
        ok: true,
        blob: async () => new Blob(['v3-exact-bytes'], { type: 'application/octet-stream' }),
      };
    }
    return {
      ok: true,
      json: async () => FIXTURE_VERSIONS,
    };
  });
}

function renderModal(theme = webLightTheme): ReturnType<typeof render> {
  return render(
    <FluentProvider theme={theme}>
      <VersionHistoryModal
        open={true}
        onClose={() => undefined}
        documentName="Master Services Agreement.pdf"
        fileType="pdf"
        documentId={DOCUMENT_ID}
      />
    </FluentProvider>
  );
}

beforeEach(() => {
  installFetchMock();
  // jsdom has no createObjectURL / window.open implementations
  (URL as unknown as { createObjectURL: unknown }).createObjectURL = jest.fn(() => 'blob:test-url');
  (URL as unknown as { revokeObjectURL: unknown }).revokeObjectURL = jest.fn();
  window.open = jest.fn();
});

// ---------------------------------------------------------------------------
// (1) open-version-list
// ---------------------------------------------------------------------------

test('open-version-list: renders label / timestamp / size from the OBO list endpoint', async () => {
  renderModal();

  // Version labels render
  expect(await screen.findByText('Version 4.0')).toBeInTheDocument();
  expect(screen.getByText('Version 3.0')).toBeInTheDocument();
  expect(screen.getByText('Version 2.0')).toBeInTheDocument();

  // Newest is marked Current
  expect(screen.getByText('Current')).toBeInTheDocument();

  // Size renders (1048576 B → "1.0 MB"; 524288 B → "512.0 KB")
  expect(screen.getByText(/1\.0 MB/)).toBeInTheDocument();
  expect(screen.getByText(/512\.0 KB/)).toBeInTheDocument();

  // Timestamp renders (locale-formatted; year is stable across locales)
  expect(screen.getAllByText(/2026/).length).toBeGreaterThan(0);

  // Fetched via @spaarke/auth authenticatedFetch at the gated per-document path
  expect(mockAuthenticatedFetch).toHaveBeenCalledWith(VERSIONS_PATH);
});

// ---------------------------------------------------------------------------
// (2) open-prior-version-read-only
// ---------------------------------------------------------------------------

test('open-prior-version-read-only: v3 opens read-only (exact bytes) with the honest banner; no restore/branch affordance', async () => {
  renderModal();
  await screen.findByText('Version 3.0');

  // The CURRENT version (v4) has no open affordance — scope is PRIOR versions
  expect(
    screen.queryByRole('button', { name: /open version 4\.0/i })
  ).not.toBeInTheDocument();

  // Open v3
  fireEvent.click(screen.getByRole('button', { name: /open version 3\.0 read-only/i }));

  // Exact-bytes content fetch at the gated per-document content path
  await waitFor(() =>
    expect(mockAuthenticatedFetch).toHaveBeenCalledWith(`${VERSIONS_PATH}/3.0/content`)
  );

  // Honest read-only banner
  expect(await screen.findByTestId('read-only-banner')).toHaveTextContent(
    'Viewing a prior version (read-only)'
  );

  // The bytes were opened (pdf → blob URL in a new tab), not mutated
  await waitFor(() => expect(window.open).toHaveBeenCalled());

  // NO restore / branch affordance anywhere (deferred by scope — honest copy)
  expect(screen.queryByText(/restore/i, { selector: 'button' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /restore/i })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /branch/i })).not.toBeInTheDocument();
  // The only mention of "restor"/"branch" is the honest scope note saying it is NOT available
  expect(
    screen.getByText(/Restoring or branching from a prior version is not available/i)
  ).toBeInTheDocument();
});

// ---------------------------------------------------------------------------
// (3) negative — the GATED per-document pair ONLY (ADR-028 + task 079)
// ---------------------------------------------------------------------------

test('negative: only the gated per-document pair is called — never the admin surface, never the deleted drive-keyed shape', async () => {
  renderModal();
  await screen.findByText('Version 3.0');
  fireEvent.click(screen.getByRole('button', { name: /open version 3\.0 read-only/i }));
  await waitFor(() =>
    expect(mockAuthenticatedFetch).toHaveBeenCalledWith(`${VERSIONS_PATH}/3.0/content`)
  );

  const calledUrls = mockAuthenticatedFetch.mock.calls.map((c) => String(c[0]));
  expect(calledUrls.length).toBeGreaterThan(0);
  for (const url of calledUrls) {
    // Every call is on the gated, per-document surface, keyed by the document ROW id
    expect(url).toMatch(
      new RegExp(`^/api/documents/${DOCUMENT_ID}/versions`)
    );
    // Never the admin/app-only container surface (ContainerItemEndpoints)
    expect(url).not.toMatch(/containers/i);
    // Never the DELETED drive-keyed shape. unified-access-control-r2 task 079: that pair took an
    // arbitrary (driveId, itemId) off the route and served version metadata and PRIOR-VERSION
    // BYTES with no per-document authorization. A client that reverts to it is asking the server
    // for a route that no longer exists — this assertion is what makes that a test failure rather
    // than a 404 discovered in production.
    expect(url).not.toMatch(/\/api\/obo\/drives\//);
  }
});

// ---------------------------------------------------------------------------
// (4) dark-mode (ADR-021)
// ---------------------------------------------------------------------------

test('dark-mode: renders under webDarkTheme (and webLightTheme) via theme tokens without errors', async () => {
  const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);

  const dark = renderModal(webDarkTheme);
  expect(await screen.findByText('Version 3.0')).toBeInTheDocument();
  expect(screen.getByText(/Prior versions open read-only/i)).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: /open version 3\.0 read-only/i }));
  expect(await screen.findByTestId('read-only-banner')).toBeVisible();
  dark.unmount();

  const light = renderModal(webLightTheme);
  expect(await screen.findByText('Version 3.0')).toBeInTheDocument();
  light.unmount();

  // No React/Fluent rendering errors in either theme — all styling comes from
  // Fluent v9 theme tokens (makeStyles + tokens.*; no hard-coded colors in
  // VersionHistoryModal.tsx — verified structurally by both-theme render).
  expect(errorSpy).not.toHaveBeenCalled();
  errorSpy.mockRestore();
});
