/**
 * ComposeWorkspace.missing-drive.test.tsx — issue #572 honest-empty-state tests.
 *
 * Defect (belt-and-braces companion to the DocumentComposeLaunch ribbon
 * guard): when a `documentRef` IS present but the host supplies an empty
 * `driveId` (half-provisioned document — missing SPE drive pointer), the
 * workspace previously dispatched a hard `loadFailed` and rendered the
 * "Cannot load document" error MessageBar. The user got a dead end.
 *
 * Fix under test:
 *   1. documentRef + empty driveId → stays in the 'empty' state (Browse /
 *      Search picker) with an INFORMATIONAL MessageBar — a working
 *      affordance, not an error. No BFF load is attempted.
 *   2. The hard error is retained ONLY for the truly-misconfigured host
 *      (missing tenantId).
 *   3. Control: with a full documentRef + driveId + tenantId the load
 *      proceeds (BFF GET attempted).
 *
 * Test category per ADR-038: Component Tests. Mock boundary: `@spaarke/auth`
 * (authenticatedFetch — network boundary) via the jest.config moduleNameMapper.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets/events';
// `@spaarke/auth` is mapped to the AI.Widgets mock module by jest.config —
// authenticatedFetch is a jest.fn resolving 503 by default.
import { authenticatedFetch } from '@spaarke/auth';

import { ComposeWorkspace } from '@spaarke/compose-components/widgets/ComposeWorkspace';

const mockedFetch = authenticatedFetch as jest.MockedFunction<typeof authenticatedFetch>;

const BFF_URL = 'https://bff.example.com';
const TENANT_ID = 'tenant-guid-1';

function renderWorkspace(ui: React.ReactElement): void {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>{ui}</PaneEventBusProvider>
    </FluentProvider>,
  );
}

beforeAll(() => {
  // Fluent v9 MessageBar (useMessageBarReflow) requires ResizeObserver,
  // which jsdom does not provide. Minimal no-op polyfill.
  if (typeof globalThis.ResizeObserver === 'undefined') {
    class ResizeObserverStub {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    }
    (globalThis as { ResizeObserver?: unknown }).ResizeObserver = ResizeObserverStub;
  }
});

beforeEach(() => {
  mockedFetch.mockClear();
});

describe('ComposeWorkspace — documentRef without driveId (#572 honest empty state)', () => {
  it('renders the empty-state picker + informational banner (NOT the hard error) and attempts no load', async () => {
    renderWorkspace(
      <ComposeWorkspace
        initialDocumentRef={{
          speDriveItemId: '01DRIVEITEM',
          sprkDocumentId: 'doc-guid-1',
          fileName: 'Half Provisioned.docx',
        }}
        bffBaseUrl={BFF_URL}
        driveId=""
        tenantId={TENANT_ID}
      />,
    );

    // Empty-state picker is the working affordance…
    expect(await screen.findByTestId('compose-empty-state')).toBeInTheDocument();
    expect(screen.getByTestId('compose-empty-state-browse')).toBeInTheDocument();

    // …with the informational missing-drive-pointer banner…
    const banner = screen.getByTestId('compose-workspace-missing-drive-pointer');
    expect(banner).toBeInTheDocument();
    expect(banner).toHaveTextContent(/isn't fully provisioned in SharePoint Embedded/i);

    // …and NOT the hard "Cannot load document" error.
    expect(screen.queryByTestId('compose-workspace-error-empty')).not.toBeInTheDocument();
    expect(screen.getByTestId('compose-workspace')).toHaveAttribute(
      'data-compose-workspace-status',
      'empty',
    );

    // No BFF load attempted for a half-provisioned document.
    expect(mockedFetch).not.toHaveBeenCalled();
  });

  it('keeps the hard error for the truly-misconfigured host (missing tenantId)', async () => {
    renderWorkspace(
      <ComposeWorkspace
        initialDocumentRef={{
          speDriveItemId: '01DRIVEITEM',
          sprkDocumentId: 'doc-guid-1',
          fileName: 'Doc.docx',
        }}
        bffBaseUrl={BFF_URL}
        driveId="b!DRIVE"
        tenantId=""
      />,
    );

    const error = await screen.findByTestId('compose-workspace-error-empty');
    expect(error).toHaveTextContent(/tenant id is required/i);
    expect(mockedFetch).not.toHaveBeenCalled();
  });

  it('control: proceeds with the BFF load when documentRef + driveId + tenantId are all present', async () => {
    renderWorkspace(
      <ComposeWorkspace
        initialDocumentRef={{
          speDriveItemId: '01DRIVEITEM',
          sprkDocumentId: 'doc-guid-1',
          fileName: 'Doc.docx',
        }}
        bffBaseUrl={BFF_URL}
        driveId="b!DRIVE"
        tenantId={TENANT_ID}
      />,
    );

    await waitFor(() => {
      expect(mockedFetch).toHaveBeenCalled();
    });
    const [url] = mockedFetch.mock.calls[0] as [string, RequestInit?];
    expect(url).toContain('/api/compose/documents/01DRIVEITEM');
    expect(url).toContain('driveId=b%21DRIVE');
  });
});
