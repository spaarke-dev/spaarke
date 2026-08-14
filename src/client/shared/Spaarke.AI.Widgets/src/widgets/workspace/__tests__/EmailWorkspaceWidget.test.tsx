/**
 * EmailWorkspaceWidget — producer wiring unit tests (task 042b, FR-C1
 * "Path 1: persisted Email carrier").
 *
 * Verifies the ONE thing this widget wrapper adds over mounting `<EmailWorkspace />`:
 * when mounted AS a workspace TAB (tabId + onDataChange supplied by the host tab
 * manager), the selected email's compact shape surfaced by `EmailWorkspace`'s
 * `onVisibleEmailChange` callback is persisted into the tab's `widgetData` as an
 * `EmailTabWidgetData` via `WorkspaceWidgetProps.onDataChange` (the existing
 * widget self-update seam → WorkspaceTabManager.updateTab → PATCH /tabs).
 *
 * `@spaarke/communication-components` (heavy Xrm-coupled EmailWorkspace) and
 * `@spaarke/ui-components` (Xrm adapter factories) are mocked at the module
 * boundary — this test exercises the wrapper's persistence wiring only, not the
 * real email surface.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { act, render } from '@testing-library/react';
import type { EmailWorkspaceVisibleState } from '@spaarke/communication-components';
import type { EmailTabWidgetData } from '../../../types/WorkspaceTab';

// The compact shape the mocked EmailWorkspace will surface via onVisibleEmailChange.
// Mutated per-test before render.
let nextVisibleState: EmailWorkspaceVisibleState | null = null;

// Mock the shared EmailWorkspace composition root: on mount, invoke the
// onVisibleEmailChange callback with the per-test fixture (mirrors the real
// component's effect firing when a record resolves).
jest.mock('@spaarke/communication-components', () => ({
  __esModule: true,
  EmailWorkspace: (props: { onVisibleEmailChange?: (s: EmailWorkspaceVisibleState | null) => void }) => {
    React.useEffect(() => {
      props.onVisibleEmailChange?.(nextVisibleState);
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);
    return React.createElement('div', { 'data-testid': 'mock-email-workspace' });
  },
}));

// Mock the Xrm adapter factories the widget resolves at mount. `getXrm()?.WebApi`
// MUST be truthy or the widget fails closed (returns null before rendering).
jest.mock('@spaarke/ui-components', () => ({
  __esModule: true,
  XrmDataverseClient: class {},
  createXrmDataService: () => ({}),
  createXrmNavigationService: () => ({}),
  createXrmEmailComposeHandlers: () => ({}),
  resolveCurrentUserEmail: () => Promise.resolve('me@example.com'),
  searchUsersAndContacts: () => Promise.resolve([]),
  getXrm: () => ({ WebApi: {}, Utility: { getGlobalContext: () => ({ getClientUrl: () => '' }) } }),
}));

// The widget reads auth/bff from the AiSession provider context.
jest.mock('../../../providers/useAiSession', () => ({
  __esModule: true,
  useAiSession: () => ({ authenticatedFetch: jest.fn(), bffBaseUrl: 'https://bff.example' }),
}));

import { EmailWorkspaceWidget } from '../EmailWorkspaceWidget';

const SAMPLE: EmailWorkspaceVisibleState = {
  communicationId: 'comm-1',
  emlDocumentId: 'eml-doc-1',
  subject: 'Quarterly filing update',
  from: 'jane.doe@example.com',
  date: '2026-08-02T10:30:00Z',
  snippet: 'Please review the attached filing before Friday.',
};

describe('EmailWorkspaceWidget — persisted Email carrier producer (FR-C1)', () => {
  beforeEach(() => {
    nextVisibleState = null;
  });

  it('persists the compact shape as an EmailTabWidgetData (kind="Email") via onDataChange when mounted as a tab', async () => {
    nextVisibleState = SAMPLE;
    const onDataChange = jest.fn();

    await act(async () => {
      render(<EmailWorkspaceWidget data={null} widgetType="email" tabId="wstab-1-email" onDataChange={onDataChange} />);
    });

    expect(onDataChange).toHaveBeenCalledTimes(1);
    const persisted = onDataChange.mock.calls[0][0] as EmailTabWidgetData;
    expect(persisted.kind).toBe('Email');
    expect(persisted.subject).toBe('Quarterly filing update');
    expect(persisted.from).toBe('jane.doe@example.com');
    expect(persisted.date).toBe('2026-08-02T10:30:00Z');
    // emlDocumentId persisted as an on-demand eml-render fetch handle (FR-C4).
    expect(persisted.emlDocumentId).toBe('eml-doc-1');
    // threadId absent on the source shape → not added.
    expect(persisted.threadId).toBeUndefined();
  });

  // spaarkeai-assistant-enhancements-r3 task 012 (FR-05) — R2 FR-C1 walk-back: `snippet`
  // (the sole content-bearing field) is no longer forwarded into the Assistant-visible
  // persisted carrier, even though the source `EmailWorkspaceVisibleState` still carries one.
  it('OMITS snippet (content) from the persisted patch — ADR-015 id/label-only walk-back', async () => {
    nextVisibleState = SAMPLE;
    const onDataChange = jest.fn();

    await act(async () => {
      render(<EmailWorkspaceWidget data={null} widgetType="email" tabId="wstab-1-email" onDataChange={onDataChange} />);
    });

    const persisted = onDataChange.mock.calls[0][0] as Record<string, unknown>;
    expect(persisted.snippet).toBeUndefined();
    expect('snippet' in persisted).toBe(false);
  });

  // task 012 (FR-05) — the transient bridge field the SpaarkeAi host reads to publish the
  // active-item conduit id handle. Not part of EmailTabWidgetData; the host strips it.
  it('includes a transient communicationId field on the patch for the SpaarkeAi active-item bridge', async () => {
    nextVisibleState = SAMPLE;
    const onDataChange = jest.fn();

    await act(async () => {
      render(<EmailWorkspaceWidget data={null} widgetType="email" tabId="wstab-1-email" onDataChange={onDataChange} />);
    });

    const persisted = onDataChange.mock.calls[0][0] as Record<string, unknown>;
    expect(persisted.communicationId).toBe('comm-1');
  });

  // task 012 (FR-05) — deselect now signals a clear-only patch (`{ communicationId: null }`) so
  // the SpaarkeAi bridge can clear the active-item conduit, WITHOUT clobbering the persisted
  // Email carrier (no `kind`/subject/etc. on this patch — same "leave it intact" contract).
  it('signals a clear-only patch ({ communicationId: null }) when the surfaced state is null (nothing selected / loading)', async () => {
    nextVisibleState = null;
    const onDataChange = jest.fn();

    await act(async () => {
      render(<EmailWorkspaceWidget data={null} widgetType="email" tabId="wstab-1-email" onDataChange={onDataChange} />);
    });

    expect(onDataChange).toHaveBeenCalledTimes(1);
    expect(onDataChange).toHaveBeenCalledWith({ communicationId: null });
  });

  it('is a no-op when NOT mounted as a tab (no tabId) even with a valid shape', async () => {
    nextVisibleState = SAMPLE;
    const onDataChange = jest.fn();

    await act(async () => {
      render(<EmailWorkspaceWidget data={null} widgetType="email" onDataChange={onDataChange} />);
    });

    expect(onDataChange).not.toHaveBeenCalled();
  });
});
