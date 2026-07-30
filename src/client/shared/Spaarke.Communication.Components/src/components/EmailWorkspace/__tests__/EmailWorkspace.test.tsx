/**
 * EmailWorkspace.test.tsx
 *
 * Smoke + dual-mount-parity tests for `<EmailWorkspace />` (email-
 * communication-solution-r5 task 040, spec FR-01 / NFR-06 / Success
 * Criterion 1). Verifies the composition renders the two-pane surface,
 * selection wires the correct id through the Phase-3 sub-views, dark mode
 * renders cleanly (ADR-021), and the closed negative set holds: no mount-
 * specific branching, no self-mounted `FluentProvider`, no `as
 * React.ComponentType` cast, no `ComponentFramework` import (NFR-05).
 */
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

// `EmailBodyView` (task 033, mounted by this component's `renderBody` slot)
// imports the free-function `authenticatedFetch` from `@spaarke/auth` as its
// DEFAULT (this workspace supplies its own via props, but the module-level
// import still executes at load time). This package never calls `initAuth()`,
// so the real function throws — mock it, mirroring `EmailBodyView.test.tsx`.
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: jest.fn(),
}));

import { EmailWorkspace } from '../EmailWorkspace';
import type { EmailWorkspaceProps } from '../EmailWorkspace.types';
import type {
  IDataverseClient,
  IDataService,
  INavigationService,
  SavedQuerySummary,
  SavedQueryResult,
} from '@spaarke/ui-components';

// jsdom has no `ResizeObserver` implementation; Fluent's `MessageBar` reflow
// hook (used by `EmailViewSelector`'s error state and `EmailConnectionsReview`)
// needs one — mirrors the stub already established in the task-035 test suite.
beforeAll(() => {
  if (typeof (global as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
    class ResizeObserverStub {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    }
    (global as unknown as { ResizeObserver: unknown }).ResizeObserver = ResizeObserverStub;
  }
});

beforeEach(() => {
  window.localStorage.clear();
});

const COMMUNICATION_ID = '11111111-1111-1111-1111-111111111111';

const VIEW_SUMMARY: SavedQuerySummary = {
  id: 'view-1',
  name: 'Email — Inbox',
  isDefault: true,
  queryType: 0,
};

const VIEW_RESULT: SavedQueryResult = {
  entityName: 'sprk_communication',
  fetchXml: '<fetch><entity name="sprk_communication"></entity></fetch>',
  layoutXml: '<grid/>',
  name: 'Email — Inbox',
};

const VIEW_ROW = {
  sprk_communicationid: COMMUNICATION_ID,
  sprk_from: 'jane.doe@example.com',
  sprk_subject: 'Quarterly filing update',
  sprk_body: '<p>Please find attached the <b>latest draft</b> for review.</p>',
  sprk_receiveddate: '2026-07-20T10:00:00Z',
  sprk_communicationtype: 100000000,
};

const FULL_RECORD: Record<string, unknown> = {
  sprk_communicationid: COMMUNICATION_ID,
  sprk_subject: 'Quarterly filing update',
  sprk_from: 'jane.doe@example.com',
  sprk_to: 'legal-team@example.com',
  sprk_body: '<p>Please find attached the <b>latest draft</b> for review.</p>',
  sprk_regardingrecordname: 'Acme v Beta',
  sprk_associationstatus: 100000001,
  sprk_associationprovenance: null,
  sprk_ismonitored: true,
  sprk_ishighpriority: false,
  sprk_accesspermission: 100000000,
};

function makeDataverseClient(): IDataverseClient {
  return {
    retrieveSavedQuery: jest.fn().mockResolvedValue(VIEW_RESULT),
    retrieveSavedQueriesForEntity: jest.fn().mockResolvedValue([VIEW_SUMMARY]),
    retrieveEntityMetadata: jest.fn().mockResolvedValue({
      primaryIdAttribute: 'sprk_communicationid',
      primaryNameAttribute: 'sprk_subject',
      attributes: {},
    }),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [VIEW_ROW], moreRecords: false }),
    retrieveRecord: jest.fn().mockResolvedValue(VIEW_ROW),
  };
}

function makeDataService(): IDataService {
  return {
    createRecord: jest.fn().mockResolvedValue(COMMUNICATION_ID),
    retrieveRecord: jest.fn().mockResolvedValue(FULL_RECORD),
    // Backs BOTH the `.eml` archive-document lookup (no archive → degrade to
    // `sprk_body`, task 033) and `EmailReadingAttachments`' attachment list
    // (empty → empty state) — an empty result is a safe default for either.
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
}

function makeNavigationService(): INavigationService {
  return {
    openRecord: jest.fn().mockResolvedValue(undefined),
    openRecordModal: jest.fn().mockResolvedValue(undefined),
    openDialog: jest.fn().mockResolvedValue({ confirmed: true }),
    closeDialog: jest.fn(),
    openLookup: jest.fn().mockResolvedValue([]),
  };
}

function makeWebApi(): EmailWorkspaceProps['webApi'] {
  return {
    updateRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
  } as unknown as EmailWorkspaceProps['webApi'];
}

function baseProps(overrides: Partial<EmailWorkspaceProps> = {}): EmailWorkspaceProps {
  return {
    dataverseClient: makeDataverseClient(),
    dataService: makeDataService(),
    navigationService: makeNavigationService(),
    webApi: makeWebApi(),
    authenticatedFetch: jest
      .fn()
      .mockResolvedValue({ ok: true, status: 200, text: async () => '<p>rendered</p>' } as unknown as Response),
    bffBaseUrl: 'https://bff.example.com',
    ...overrides,
  };
}

function renderWorkspace(overrides: Partial<EmailWorkspaceProps> = {}, theme = webLightTheme) {
  return render(
    <FluentProvider theme={theme}>
      <EmailWorkspace {...baseProps(overrides)} />
    </FluentProvider>
  );
}

describe('EmailWorkspace', () => {
  it('composes the two-pane surface: view selector + card list + reading-pane shell placeholder', async () => {
    renderWorkspace();

    expect(await screen.findByText('Quarterly filing update')).toBeInTheDocument();
    expect(screen.getByTestId('email-workspace')).toBeInTheDocument();
    expect(screen.getByTestId('email-list-pane')).toBeInTheDocument();
    expect(screen.getByTestId('email-reading-pane')).toBeInTheDocument();
    expect(screen.getByText('Select an email')).toBeInTheDocument();
  });

  it('selecting a card paints the header from the record and swaps the body into the reading pane', async () => {
    renderWorkspace();

    fireEvent.click(await screen.findByText('Quarterly filing update'));

    // Title bar paints from the record (EmailReadingHeader) — the subject appears
    // on its own light-gray row (no tracking trio, no from/to/cc/bcc here).
    await waitFor(() => expect(screen.getByTestId('email-reading-header')).toBeInTheDocument());
    // The tracking trio was REMOVED from the reading pane entirely (redesign).
    expect(screen.queryByTestId('email-tracking-panel')).not.toBeInTheDocument();
    // Recipients block reflects the record's From/To — Cc/Bcc rows are absent
    // since FULL_RECORD carries neither (the mapping reads them defensively from
    // the same no-`$select` read, never throwing).
    const recipients = await screen.findByTestId('email-recipients');
    expect(within(recipients).getByText('jane.doe@example.com')).toBeInTheDocument();
    expect(within(recipients).getByText('legal-team@example.com')).toBeInTheDocument();
    expect(within(recipients).queryByText('Cc')).not.toBeInTheDocument();
    expect(within(recipients).queryByText('Bcc')).not.toBeInTheDocument();
    // Body degrades to sanitized `sprk_body` (no `.eml` archive in this fixture — a normal
    // state) — scope to the reading pane since the left card list's preview text also
    // contains a snippet of the same sentence.
    const readingPane = screen.getByTestId('email-reading-pane');
    await waitFor(() => expect(within(readingPane).getByText(/latest draft/i)).toBeInTheDocument());
    // "Open full form" was removed from the toolbar per owner UAT (2026-07-29).
    expect(screen.queryByRole('button', { name: 'Open full form' })).not.toBeInTheDocument();
    // The collapsible sections render with their headers. "Related to" is the
    // merged association section (open by default) that BOTH shows the primary
    // association state and resolves it — the old separate "Association" section
    // is gone (single-primary redesign 2026-07-29).
    expect(screen.getByText('Attachments')).toBeInTheDocument();
    expect(screen.getByText('Related to')).toBeInTheDocument();
    expect(screen.queryByText('Association')).not.toBeInTheDocument();
    // "Related to" is open by default, so the redesigned resolver is already
    // mounted; with no engine provenance in this fixture it shows the candidate
    // slots + the "Link another record" affordance.
    expect(await screen.findByTestId('email-connections-review')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /link another record/i })).toBeInTheDocument();
  });

  it('given a selected card WITH a `.eml` archive, resolves the archive document id and renders the server-rendered body (loading→loaded transition)', async () => {
    const EML_DOCUMENT_ID = '99999999-9999-9999-9999-999999999999';
    const authenticatedFetch = jest.fn().mockResolvedValue({
      ok: true,
      status: 200,
      text: async () => '<p>Server-rendered .eml content</p>',
    } as unknown as Response);

    const dataService = makeDataService();
    // Resolve the `.eml` archive lookup (`sprk_document`, `sprk_isemailarchive = true`) to a real id
    // instead of the default empty-result stub — this is the acceptance-criterion-3 "WITH a `.eml`
    // archive" path (the base fixture in the prior test exercises the archive-LESS degrade path).
    (dataService.retrieveMultipleRecords as jest.Mock).mockResolvedValue({
      entities: [{ sprk_documentid: EML_DOCUMENT_ID }],
    });

    renderWorkspace({ dataService, authenticatedFetch });

    fireEvent.click(await screen.findByText('Quarterly filing update'));

    // Loading: a skeleton renders while the eml-render fetch is in flight (EmailBodyView, task 033).
    // Loaded: the resolved archive id flows through to `EmailBodyView`, which calls `authenticatedFetch`
    // against the `eml-render` endpoint for THAT document id — proving the wiring (not a re-test of
    // EmailBodyView's own render-branch logic, already covered by its task-033 suite).
    await waitFor(() => expect(authenticatedFetch).toHaveBeenCalledWith(`/documents/${EML_DOCUMENT_ID}/eml-render`));
    // Server HTML lands in the sandboxed iframe's `srcdoc` (EmailBodyView, task 033 —
    // content is NOT queryable via RTL text matchers since it's inside a real iframe).
    const iframe = await screen.findByTestId('email-body-iframe');
    expect(iframe.getAttribute('srcdoc')).toContain('Server-rendered .eml content');
    expect(iframe).toHaveAttribute('sandbox', '');
  });

  it('dual-mount parity: two independent host wrappers around the SAME component render an identical surface', async () => {
    const props = baseProps();
    const first = render(
      <FluentProvider theme={webLightTheme}>
        <div data-testid="widget-host-wrapper">
          <EmailWorkspace {...props} />
        </div>
      </FluentProvider>
    );
    expect(await screen.findByText('Quarterly filing update')).toBeInTheDocument();
    const widgetHtml = first.container.querySelector('[data-testid="email-workspace"]')?.outerHTML;
    first.unmount();

    const second = render(
      <FluentProvider theme={webLightTheme}>
        <main data-testid="code-page-host-wrapper">
          <EmailWorkspace {...baseProps()} />
        </main>
      </FluentProvider>
    );
    expect(await screen.findByText('Quarterly filing update')).toBeInTheDocument();
    const codePageHtml = second.container.querySelector('[data-testid="email-workspace"]')?.outerHTML;

    expect(widgetHtml).toBeTruthy();
    expect(widgetHtml).toEqual(codePageHtml);
  });

  it('renders correctly under a dark FluentProvider theme (ADR-021) with no console errors', async () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderWorkspace({}, webDarkTheme);

    expect(await screen.findByText('Quarterly filing update')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Quarterly filing update'));
    await waitFor(() => expect(screen.getByTestId('email-reading-header')).toBeInTheDocument());

    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});

describe('Negative: dual-mount parity + NFR-05 + no self-mounted FluentProvider', () => {
  it('contains no mount-specific branch and mounts no FluentProvider of its own', () => {
    const src = fs.readFileSync(path.join(__dirname, '../EmailWorkspace.tsx'), 'utf8');

    expect(src).not.toMatch(/isWidget|isCodePage/);
    expect(src).not.toMatch(/from ['"]@fluentui\/react-components['"][^;]*FluentProvider/);
    expect(src).not.toMatch(/<FluentProvider/);
  });

  it('imports no PCF ComponentFramework type and introduces no `as React.ComponentType` cast (NFR-05)', () => {
    const files = ['../EmailWorkspace.tsx', '../useEmailWorkspaceRecord.ts', '../EmailWorkspace.mapping.ts'];
    for (const rel of files) {
      const src = fs.readFileSync(path.join(__dirname, rel), 'utf8');
      expect(src).not.toMatch(/ComponentFramework/);
      expect(src).not.toMatch(/as React\.ComponentType</);
      expect(src).not.toMatch(/from ['"]Xrm['"]|window\.Xrm/);
    }
  });
});
